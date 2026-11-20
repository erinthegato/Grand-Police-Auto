using System;
using System.Collections.Generic;
using GPA.Core;
using GPA.Records;
using UnityEngine;

namespace GPA.Mdt
{
    /// <summary>
    /// What the MDT shows (spec §8): person, vehicle and warrant lookups against the persistent
    /// records, and incident reports with an autofilled narrative. Built to work without a system
    /// keyboard: every control is reachable with a controller (see <see cref="FocusNav"/>), text goes
    /// through the MDT's own <see cref="MdtKeyboard"/>, and recent / nearby people and vehicles are
    /// offered as one-tap picks so most lookups need no typing at all.
    /// </summary>
    internal sealed partial class MdtScreen
    {
        private enum Tab { Dashboard, Persons, Vehicles, Warrants, Bolos, Citations, Arrests, Reports, Dispatch, Booking }
        private const int TabCount = 10;

        private struct Pick<T>
        {
            public T Item;
            public string Tag;
            public Pick(T item, string tag) { Item = item; Tag = tag; }
        }

        private static readonly string[] ReportTypes =
        {
            "General", "Traffic Stop", "Traffic Collision", "Arrest", "Theft", "Burglary", "Assault",
            "Disturbance", "Suspicious Activity", "Welfare Check", "Stolen Vehicle", "Other"
        };

        private const float RowHeight = 48f, ReportRowHeight = 60f, LineHeight = 28f;
        private const int ListGroup = 1, DetailGroup = 2;

        private readonly RecordsDatabase _db;
        private readonly ILocationService _locations;
        private readonly MdtActivity _activity;
        private readonly IOfficerStatusService _officerStatus;
        private readonly string _unit;
        private readonly MdtKeyboard _kb = new MdtKeyboard();
        private MdtStyles _s;
        private FocusNav _nav;

        private Tab _tab;
        private readonly int[] _sel = new int[TabCount];
        private readonly Vector2[] _listScroll = new Vector2[TabCount];
        private Vector2 _detailScroll;
        private float _listContentHeight, _detailHeight;
        private Rect _listRect, _detailRect, _kbRect;

        // Touch / mouse drag scrolling.
        private int _dragTarget;
        private bool _dragging;
        private Vector2 _dragStart;

        private string _personQuery = "", _vehicleQuery = "", _warrantFilter = "";
        private List<Pick<Citizen>> _people = new List<Pick<Citizen>>();
        private List<Pick<VehicleRecord>> _cars = new List<Pick<VehicleRecord>>();
        private List<Pick<Citizen>> _contactPeople = new List<Pick<Citizen>>();
        private List<Pick<VehicleRecord>> _contactCars = new List<Pick<VehicleRecord>>();
        private List<Citizen> _wanted = new List<Citizen>();
        private Citizen _lastPerson;
        private VehicleRecord _lastVehicle;

        // Report editor: _draft is edited, _source is the saved record (null for a new report).
        private IncidentReport _draft, _source, _pendingLoad;
        private bool _dirty, _pendingNew;
        private string _undoTitle, _undoNarrative, _autoTitle;
        private bool _canUndo;
        private int _phraseCategory;
        private string _addPerson = "", _addVehicle = "";

        private string _officerLocation = "";
        private Vector2 _officerPos;
        private string _status;
        private bool _statusWarn;
        private float _statusUntil;
        private bool _gamepad, _focusOnOpen, _focusKeyboard, _kbWasOpen;
        private string _returnFocus;

        public MdtScreen(RecordsDatabase db, ILocationService locations, MdtActivity activity, IOfficerStatusService status, string unit)
        {
            _db = db;
            _locations = locations;
            _activity = activity;
            _officerStatus = status;
            _unit = unit;
        }

        public bool KeyboardOpen => _kb.Open;

        public void OnOpened(Vector3 officerPosition, bool gamepad)
        {
            _officerPos = officerPosition;
            _officerLocation = _locations != null ? _locations.Describe(officerPosition) : "";
            _wanted = _db.WantedCitizens();
            BuildContacts();
            if (string.IsNullOrEmpty(_personQuery)) ShowContacts(Tab.Persons);
            if (string.IsNullOrEmpty(_vehicleQuery)) ShowContacts(Tab.Vehicles);
            _focusOnOpen = gamepad;
        }

        public void CloseKeyboard() => _kb.Close();
        public void Backspace() { if (_kb.Open) _kb.Backspace(); }
        public void Space() { if (_kb.Open) _kb.Space(); }
        public void Activate() { GPA.TopDown2D.GameAudio.UiClick(); _nav?.Activate(); }
        public void CycleTab(int dir) { GPA.TopDown2D.GameAudio.UiClick(); if (!_kb.Open) SelectTab((Tab)(((int)_tab + dir + TabCount) % TabCount)); }
        public void ScrollDetail(float delta) => ScrollGroup(DetailGroup, delta);

        public void Navigate(Vector2Int dir)
        {
            if (_nav == null || !_nav.Move(dir)) return;
            if (!_nav.TryGet(_nav.Focused, out var item)) return;
            EnsureVisible(item);
            // In the lookup lists the record on the right follows the highlighted row.
            string prefix = $"row:{(int)_tab}:";
            if (_tab != Tab.Reports && item.Id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(item.Id.Substring(prefix.Length), out int row))
            {
                if (_sel[(int)_tab] != row) _detailScroll = Vector2.zero;
                _sel[(int)_tab] = row;
            }
        }

        // ------------------------------------------------------------------ records navigation

        private void SelectTab(Tab tab)
        {
            if (_tab == tab) return;
            _kb.Close();
            _tab = tab;
            _detailScroll = Vector2.zero;
            if (tab == Tab.Warrants) _wanted = _db.WantedCitizens();
        }

        private void BuildContacts()
        {
            _contactPeople.Clear();
            _contactCars.Clear();
            var seenPeople = new HashSet<string>();
            var seenCars = new HashSet<string>();
            void Person(string id, string tag) { var c = _db.GetCitizen(id); if (c != null && seenPeople.Add(id)) _contactPeople.Add(new Pick<Citizen>(c, tag)); }
            void Car(string id, string tag) { var v = _db.GetVehicle(id); if (v != null && seenCars.Add(id)) _contactCars.Add(new Pick<VehicleRecord>(v, tag)); }

            foreach (var stop in _activity.Stops)
            {
                string tag = $"stop {stop.Time:HH:mm} · {stop.Outcome.ToString().ToLowerInvariant()}";
                if (stop.CitizenId != null) Person(stop.CitizenId, tag);
                if (stop.VehicleId != null) Car(stop.VehicleId, tag);
            }
            string today = GameClock.Today;
            for (int i = _db.Data.Arrests.Count - 1; i >= 0; i--)
            {
                var a = _db.Data.Arrests[i];
                if (a.Date != null && a.Date.StartsWith(today)) Person(a.CitizenId, $"arrested {TimeOf(a.Date)}");
            }
            for (int i = _db.Data.Citations.Count - 1; i >= 0; i--)
            {
                var c = _db.Data.Citations[i];
                if (c.Date == null || !c.Date.StartsWith(today)) continue;
                Person(c.CitizenId, $"cited {TimeOf(c.Date)}");
                if (c.VehicleId != null) Car(c.VehicleId, $"cited {TimeOf(c.Date)}");
            }
            foreach (var (id, d) in MdtActivity.NearbyPeople(_officerPos, 30f, 8)) Person(id, $"nearby · {d:0} m");
            foreach (var (id, d) in MdtActivity.NearbyVehicles(_officerPos, 45f, 8)) Car(id, $"nearby · {d:0} m");
        }

        private void ShowContacts(Tab tab)
        {
            if (tab == Tab.Persons) _people = new List<Pick<Citizen>>(_contactPeople);
            else _cars = new List<Pick<VehicleRecord>>(_contactCars);
            _sel[(int)tab] = 0;
            _listScroll[(int)tab] = Vector2.zero;
        }

        private void SearchPersons()
        {
            if (_personQuery.Trim().Length == 0) { ShowContacts(Tab.Persons); return; }
            _people = _db.SearchCitizens(_personQuery).ConvertAll(c => new Pick<Citizen>(c, c.DateOfBirth));
            _sel[(int)Tab.Persons] = 0;
            _listScroll[(int)Tab.Persons] = Vector2.zero;
            _detailScroll = Vector2.zero;
        }

        private void SearchVehicles()
        {
            if (_vehicleQuery.Trim().Length == 0) { ShowContacts(Tab.Vehicles); return; }
            _cars = _db.SearchVehicles(_vehicleQuery).ConvertAll(v => new Pick<VehicleRecord>(v, ""));
            _sel[(int)Tab.Vehicles] = 0;
            _listScroll[(int)Tab.Vehicles] = Vector2.zero;
            _detailScroll = Vector2.zero;
        }

        private void ShowPerson(Citizen c)
        {
            if (c == null) return;
            SelectTab(Tab.Persons);
            _personQuery = c.Id;
            _people = new List<Pick<Citizen>> { new Pick<Citizen>(c, c.DateOfBirth) };
            _sel[(int)Tab.Persons] = 0;
            _detailScroll = Vector2.zero;
        }

        private void ShowVehicle(VehicleRecord v)
        {
            if (v == null) return;
            SelectTab(Tab.Vehicles);
            _vehicleQuery = v.Plate;
            _cars = new List<Pick<VehicleRecord>> { new Pick<VehicleRecord>(v, "") };
            _sel[(int)Tab.Vehicles] = 0;
            _detailScroll = Vector2.zero;
        }

        private List<Citizen> FilteredWanted()
        {
            if (string.IsNullOrWhiteSpace(_warrantFilter)) return _wanted;
            string q = _warrantFilter.Trim();
            return _wanted.FindAll(c => c.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        c.Warrants.Exists(w => w.Offense.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private List<IncidentReport> Reports()
        {
            var list = new List<IncidentReport>(_db.Data.Reports);
            list.Reverse();
            return list;
        }

        // ------------------------------------------------------------------ keyboard

        private void OpenKeyboard(string fieldId, string label, Func<string> get, Action<string> set, int maxLength, bool multiline,
                                  bool caps = false, bool autoCap = false, Func<string, List<MdtKeyboard.Suggestion>> suggest = null, Action onClose = null)
        {
            _returnFocus = fieldId;
            _kb.Begin(fieldId, label, get, set, maxLength, multiline, caps, autoCap, suggest, onClose);
            _nav.ActiveLayer = 1;
            _focusKeyboard = true;
        }

        private void OpenQueryKeyboard()
        {
            switch (_tab)
            {
                case Tab.Persons:
                    OpenKeyboard("field:pq", "Search persons — name, license or citizen ID", () => _personQuery, v => { _personQuery = v; SearchPersons(); }, 64, false, caps: true);
                    break;
                case Tab.Vehicles:
                    OpenKeyboard("field:vq", "Run a plate, VIN or vehicle ID", () => _vehicleQuery, v => { _vehicleQuery = v; SearchVehicles(); }, 32, false, caps: true);
                    break;
                case Tab.Warrants:
                    OpenKeyboard("field:wf", "Filter warrants by name or offense", () => _warrantFilter, v => { _warrantFilter = v; _sel[(int)Tab.Warrants] = 0; }, 64, false, caps: true);
                    break;
                case Tab.Bolos:
                    OpenKeyboard("field:bf", "Filter BOLOs by subject, plate or reason", () => _boloFilter, v => { _boloFilter = v; _sel[(int)Tab.Bolos] = 0; }, 64, false, caps: true);
                    break;
                case Tab.Citations:
                    OpenKeyboard("field:cf", "Filter citations by name, plate, offense or number", () => _citationFilter, v => { _citationFilter = v; _sel[(int)Tab.Citations] = 0; }, 64, false, caps: true);
                    break;
                case Tab.Arrests:
                    OpenKeyboard("field:af", "Filter arrests by name, booking number or charge", () => _arrestFilter, v => { _arrestFilter = v; _sel[(int)Tab.Arrests] = 0; }, 64, false, caps: true);
                    break;
            }
        }

        // ------------------------------------------------------------------ report editing

        /// <returns>False when an unsaved draft was protected instead (press again to discard it).</returns>
        private bool NewReport(string citizenId = null, string vehicleId = null, string type = null, string location = null)
        {
            if (_dirty && !_pendingNew) { _pendingNew = true; _pendingLoad = null; Status("Unsaved changes on the open report. Press again to discard them.", true); SelectTab(Tab.Reports); return false; }
            _pendingNew = false;
            _kb.Close();
            _source = null;
            _draft = new IncidentReport { Type = type ?? ReportTypes[0], Location = string.IsNullOrEmpty(location) ? _officerLocation : location, Officer = $"Unit {_unit}" };
            if (citizenId != null) _draft.CitizenIds.Add(citizenId);
            if (vehicleId != null) _draft.VehicleIds.Add(vehicleId);
            _dirty = false;
            _canUndo = false;
            _autoTitle = null;
            SelectTab(Tab.Reports);
            _detailScroll = Vector2.zero;
            return true;
        }

        private void NewReportFromStop(StopRecord stop)
        {
            if (NewReport(stop.CitizenId, stop.VehicleId, "Traffic Stop", stop.Location)) Autofill();
        }

        private void LoadReport(IncidentReport report)
        {
            if (report == _source) return;
            if (_dirty && _pendingLoad != report) { _pendingLoad = report; _pendingNew = false; Status("Unsaved changes on the open report. Select it again to discard them.", true); return; }
            _pendingLoad = null;
            _kb.Close();
            _source = report;
            _draft = Clone(report);
            _dirty = false;
            _canUndo = false;
            _autoTitle = null;
            _detailScroll = Vector2.zero;
        }

        private void Autofill()
        {
            _undoTitle = _draft.Title;
            _undoNarrative = _draft.Narrative;
            _canUndo = true;
            var stop = _activity.StopFor(_draft.VehicleIds, _draft.CitizenIds);
            if (string.IsNullOrWhiteSpace(_draft.Location) && stop != null) _draft.Location = stop.Location;
            _draft.Narrative = NarrativeBuilder.Build(_draft, _db, stop);
            if (string.IsNullOrWhiteSpace(_draft.Title) || _draft.Title == _autoTitle)
                _draft.Title = _autoTitle = NarrativeBuilder.BuildTitle(_draft, _db);
            MarkDirty();
            Status(_draft.CitizenIds.Count + _draft.VehicleIds.Count == 0
                ? "Narrative started. Add the people and vehicles involved, then autofill again for the full write-up."
                : "Narrative written from the records. Review it, add phrases if needed, then save.", false);
        }

        private void UndoAutofill()
        {
            if (!_canUndo) return;
            _draft.Title = _undoTitle;
            _draft.Narrative = _undoNarrative;
            _canUndo = false;
            MarkDirty();
            Status("Autofill undone.", false);
        }

        private void MarkDirty()
        {
            _dirty = true;
            _pendingLoad = null;
            _pendingNew = false;
        }

        private void SaveReport()
        {
            _kb.Close();
            if (string.IsNullOrWhiteSpace(_draft.Title)) { Status("A report needs a title before it can be saved. Autofill can write one.", true); return; }
            if (_source == null) _source = _db.SaveReport(Clone(_draft));
            else { CopyInto(_draft, _source); _db.SaveReport(_source); }
            _draft = Clone(_source);
            _dirty = false;
            Status($"Saved {_source.Id}.", false);
        }

        private void DiscardChanges()
        {
            _kb.Close();
            _dirty = false;
            _pendingLoad = null; _pendingNew = false;
            _canUndo = false;
            if (_source != null) { _draft = Clone(_source); Status("Changes discarded.", false); }
            else { _draft = null; Status("Draft discarded.", false); }
        }

        private static IncidentReport Clone(IncidentReport r)
        {
            var copy = new IncidentReport();
            CopyInto(r, copy);
            copy.Id = r.Id; copy.Created = r.Created; copy.Updated = r.Updated;
            return copy;
        }

        private static void CopyInto(IncidentReport from, IncidentReport to)
        {
            to.Title = from.Title; to.Type = from.Type; to.Location = from.Location;
            to.Officer = from.Officer; to.Narrative = from.Narrative;
            to.CitizenIds = new List<string>(from.CitizenIds);
            to.VehicleIds = new List<string>(from.VehicleIds);
        }

        private void AttachPerson(Citizen c)
        {
            if (c == null || _draft.CitizenIds.Contains(c.Id)) return;
            _draft.CitizenIds.Add(c.Id);
            MarkDirty();
        }

        private void AttachVehicle(VehicleRecord v)
        {
            if (v == null || _draft.VehicleIds.Contains(v.Id)) return;
            _draft.VehicleIds.Add(v.Id);
            MarkDirty();
        }

        /// <summary>People worth one-tap attaching: the last stop's driver, owners of attached cars, the person last viewed, recent and nearby contacts.</summary>
        private List<Citizen> PersonSuggestions()
        {
            var list = new List<Citizen>();
            void Add(Citizen c) { if (c != null && !_draft.CitizenIds.Contains(c.Id) && !list.Exists(x => x.Id == c.Id)) list.Add(c); }
            var stop = _activity.StopFor(_draft.VehicleIds, _draft.CitizenIds);
            if (stop != null) Add(_db.GetCitizen(stop.CitizenId));
            foreach (string id in _draft.VehicleIds) { var v = _db.GetVehicle(id); if (v != null) Add(_db.GetCitizen(v.OwnerId)); }
            Add(_lastPerson);
            foreach (var p in _contactPeople) Add(p.Item);
            return list.Count > 6 ? list.GetRange(0, 6) : list;
        }

        private List<VehicleRecord> VehicleSuggestions()
        {
            var list = new List<VehicleRecord>();
            void Add(VehicleRecord v) { if (v != null && !_draft.VehicleIds.Contains(v.Id) && !list.Exists(x => x.Id == v.Id)) list.Add(v); }
            var stop = _activity.StopFor(_draft.VehicleIds, _draft.CitizenIds);
            if (stop != null) Add(_db.GetVehicle(stop.VehicleId));
            foreach (string id in _draft.CitizenIds) foreach (var v in _db.VehiclesOf(id)) Add(v);
            Add(_lastVehicle);
            foreach (var p in _contactCars) Add(p.Item);
            return list.Count > 6 ? list.GetRange(0, 6) : list;
        }

        private void Status(string text, bool warn)
        {
            _status = text;
            _statusWarn = warn;
            _statusUntil = Time.unscaledTime + 6f;
        }

        // ------------------------------------------------------------------ drawing

        public void Draw(Rect area, bool gamepad)
        {
            if (_s == null) { _s = new MdtStyles(); _nav = new FocusNav(_s.Outline); }
            _gamepad = gamepad;
            var e = Event.current;
            var panel = new Rect(40, 30, area.width - 80, area.height - 60);
            _kbRect = new Rect(panel.x, panel.yMax - _kb.Height, panel.width, _kb.Height);

            HandleDrag(e);
            HandleTyping(e);
            _nav.ActiveLayer = _kb.Open ? 1 : 0;
            _nav.BeginFrame();

            // Taps on the keyboard must not fall through to the screen underneath it.
            bool overKeyboard = _kb.Open && e.isMouse && _kbRect.Contains(e.mousePosition);
            GUI.enabled = !overKeyboard;

            GUI.DrawTexture(area, _s.Dim);
            GUI.DrawTexture(panel, _s.Panel);
            DrawHeader(panel);

            float top = panel.y + 64 + 72, bottom = panel.yMax - 46;
            if (_tab == Tab.Dashboard)
            {
                _listRect = new Rect(panel.x + 20, top, 0f, bottom - top);
                _detailRect = new Rect(panel.x + 20, top, panel.width - 40, bottom - top);
            }
            else
            {
                _listRect = new Rect(panel.x + 20, top, Mathf.Min(520f, panel.width * 0.36f), bottom - top);
                _detailRect = new Rect(_listRect.xMax + 24, top, panel.xMax - _listRect.xMax - 44, bottom - top);
                GUI.DrawTexture(new Rect(_listRect.xMax + 11, top, 1, bottom - top), _s.Divider);
            }

            switch (_tab)
            {
                case Tab.Persons: DrawPersonsTab(); break;
                case Tab.Vehicles: DrawVehiclesTab(); break;
                case Tab.Warrants: DrawWarrantsTab(); break;
                case Tab.Reports: DrawReportsTab(); break;
                case Tab.Dashboard: DrawDashboardTab(); break;
                case Tab.Bolos: DrawBolosTab(); break;
                case Tab.Citations: DrawCitationsTab(); break;
                case Tab.Arrests: DrawArrestsTab(); break;
                case Tab.Dispatch: DrawDispatchTab(); break;
                case Tab.Booking: DrawBookingTab(); break;
            }
            DrawFooter(new Rect(panel.x, panel.yMax - 42, panel.width, 42));
            GUI.enabled = true;

            if (_kb.Open)
            {
                _nav.Layer = 1;
                _kb.Draw(_kbRect, _s, Btn, _gamepad);
                _nav.Layer = 0;
            }
            _nav.EndFrame();

            if (e.type == EventType.Repaint) AfterRepaint();
        }

        /// <summary>Focus bookkeeping that needs the widget list the repaint just built.</summary>
        private void AfterRepaint()
        {
            if (_kbWasOpen && !_kb.Open)
            {
                _nav.ActiveLayer = 0;
                if (_returnFocus != null) _nav.Focused = _returnFocus;
            }
            _kbWasOpen = _kb.Open;
            if (_focusKeyboard && _kb.Open)
            {
                _nav.ActiveLayer = 1;
                _nav.FocusFirstWithPrefix("osk:r1:");
                _focusKeyboard = false;
            }
            if (_focusOnOpen)
            {
                _nav.ShowHighlight = true;
                _nav.Focused = null;
                _nav.FocusFirstWithPrefix($"row:{(int)_tab}:");
                if (_nav.Focused == null) _nav.Focused = $"tab:{(int)_tab}";
                _focusOnOpen = false;
            }
        }

        private void HandleDrag(Event e)
        {
            switch (e.type)
            {
                case EventType.MouseDown:
                    _nav.ShowHighlight = false;
                    _dragStart = e.mousePosition;
                    _dragging = false;
                    _dragTarget = _kb.Open && _kbRect.Contains(e.mousePosition) ? 0
                        : _listRect.Contains(e.mousePosition) ? ListGroup
                        : _detailRect.Contains(e.mousePosition) ? DetailGroup : 0;
                    break;
                case EventType.MouseDrag when _dragTarget != 0:
                    if (!_dragging && (e.mousePosition - _dragStart).sqrMagnitude > 14f * 14f) _dragging = true;
                    if (_dragging) { ScrollGroup(_dragTarget, -e.delta.y); e.Use(); }
                    break;
                case EventType.MouseUp:
                    // A drag is a scroll, not a tap on whatever row it ended over.
                    if (_dragging) { e.Use(); GUIUtility.hotControl = 0; }
                    _dragTarget = 0;
                    _dragging = false;
                    break;
            }
        }

        private void HandleTyping(Event e)
        {
            if (e.type != EventType.KeyDown) return;
            if (_kb.Open)
            {
                if (e.keyCode == KeyCode.Backspace) { _kb.Backspace(); e.Use(); }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { _kb.Newline(); e.Use(); }
                else if (e.character != 0 && !char.IsControl(e.character)) { _kb.TypeRaw(e.character); e.Use(); }
            }
            else if (HasQuery(_tab) && e.character != 0 && !char.IsControl(e.character) && !char.IsWhiteSpace(e.character) && !e.control && !e.command && !e.alt)
            {
                // On a desktop, just start typing to search.
                OpenQueryKeyboard();
                _kb.TypeRaw(e.character);
                e.Use();
            }
        }

        private void ScrollGroup(int group, float delta)
        {
            if (group == ListGroup)
            {
                ref Vector2 s = ref _listScroll[(int)_tab];
                s.y = Mathf.Clamp(s.y + delta, 0f, Mathf.Max(0f, _listContentHeight - _listRect.height));
            }
            else if (group == DetailGroup)
                _detailScroll.y = Mathf.Clamp(_detailScroll.y + delta, 0f, Mathf.Max(0f, _detailHeight - _detailRect.height));
        }

        private void EnsureVisible(FocusNav.Item item)
        {
            if (item.Group == 0) return;
            float margin = 10f, delta = 0f;
            if (item.Rect.yMin < item.Clip.yMin) delta = item.Rect.yMin - item.Clip.yMin - margin;
            else if (item.Rect.yMax > item.Clip.yMax) delta = item.Rect.yMax - item.Clip.yMax + margin;
            if (delta != 0f) ScrollGroup(item.Group, delta);
        }

        /// <summary>Every clickable thing in the MDT: pointer click, or Submit while focused.</summary>
        private bool Btn(Rect r, string text, GUIStyle style, string id)
        {
            bool clicked = GUI.Button(r, text, style);
            if (clicked)
            {
                _nav.Focused = id;
                _nav.ShowHighlight = false;
                // Tapping the screen behind the keyboard finishes typing.
                if (_kb.Open && _nav.Layer == 0 && id != _kb.FieldId) _kb.Close();
            }
            if (_nav.TakeActivation(id)) clicked = true;
            _nav.Register(id, r);
            return clicked;
        }

        /// <summary>A text box that opens the MDT keyboard instead of the system one.</summary>
        private bool TextBox(Rect r, string id, string value, string placeholder, bool multiline = false)
        {
            bool editing = _kb.Open && _kb.FieldId == id;
            GUI.DrawTexture(r, editing ? _s.FieldActive : _s.FieldBg);
            if (editing) GUI.DrawTexture(new Rect(r.x, r.yMax - 3, r.width, 3), _s.Outline);
            string shown = string.IsNullOrEmpty(value) ? $"<color=#6c7480>{placeholder}</color>" : Escape(value);
            if (!multiline && !string.IsNullOrEmpty(value))
            {
                // Long single-line values show their end, where the typing is.
                int fit = Mathf.Max(8, (int)((r.width - 30) / 10.5f));
                if (value.Length > fit) shown = "…" + Escape(value.Substring(value.Length - fit + 1));
            }
            if (editing && Mathf.Repeat(Time.unscaledTime, 1f) < 0.55f) shown += "<color=#ffd040>▌</color>";
            var inner = new Rect(r.x + 12, r.y + (multiline ? 8 : 0), r.width - 24, r.height - (multiline ? 16 : 0));
            GUI.Label(inner, shown, multiline ? _s.Wrapped : GuiStyleCache.With(_s.Body, TextAnchor.MiddleLeft));
            return Btn(r, "", GUIStyle.none, id) && !editing;
        }

        private void DrawHeader(Rect panel)
        {
            var header = new Rect(panel.x, panel.y, panel.width, 64);
            GUI.DrawTexture(header, _s.HeaderBg);
            GUI.DrawTexture(new Rect(header.x, header.yMax - 3, header.width, 3), _s.Accent);
            string city = _locations != null ? _locations.CityName.ToUpperInvariant() : "CITY";
            GUI.Label(new Rect(header.x + 24, header.y + 8, header.width * 0.5f, 30), $"{city} POLICE DEPARTMENT", _s.H1);
            GUI.Label(new Rect(header.x + 24, header.y + 36, header.width * 0.5f, 24), "MOBILE DATA TERMINAL  ·  RECORDS", _s.Small);
            var right = GuiStyleCache.With(_s.Small, TextAnchor.UpperRight);
            GUI.Label(new Rect(header.xMax - 824, header.y + 10, 800, 24), $"<b>UNIT {_unit}</b>   {StatusText()}", right);
            GUI.Label(new Rect(header.xMax - 824, header.y + 36, 800, 24), $"{Escape(_officerLocation)}   ·   {GameClock.Now:ddd dd MMM yyyy  HH:mm}", right);

            string[] names = { "DASHBOARD", "PERSONS", "VEHICLES", $"WARRANTS ({_wanted.Count})", $"BOLOS ({ActiveBoloCount()})", "CITATIONS", $"ARRESTS ({_db.Data.Arrests.Count})", $"REPORTS ({_db.Data.Reports.Count})", "DISPATCH", BookingTabLabel() };
            float tabW = Mathf.Min(232f, (panel.width - 40) / TabCount);
            for (int i = 0; i < TabCount; i++)
            {
                var r = new Rect(panel.x + 20 + i * tabW, header.yMax + 12, tabW - 10, 48);
                GUI.DrawTexture(r, (int)_tab == i ? _s.TabOn : _s.TabOff);
                // Ten tabs: a long label ("WARRANTS (111)") drops to a smaller size rather than wrapping.
                GUIStyle style = _s.Tab;
                if (style.CalcSize(new GUIContent(names[i])).x > r.width - 12f)
                {
                    _tabSmall ??= new GUIStyle(_s.Tab) { wordWrap = false };
                    _tabSmall.fontSize = Mathf.Max(11, _s.Tab.fontSize - 3);
                    style = _tabSmall;
                }
                if (Btn(r, names[i], style, $"tab:{i}")) SelectTab((Tab)i);
            }
        }

        private GUIStyle _tabSmall;

        private void DrawFooter(Rect r)
        {
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1), _s.Divider);
            string hints = _gamepad
                ? "D-pad  move     A  select     B  back / close     LB / RB  tabs     R-stick  scroll"
                : "Tap or click     Type to search     Arrows + Enter  select     PgUp / PgDn  tabs     Esc / F1  close";
            GUI.Label(new Rect(r.x + 24, r.y + 10, r.width * 0.5f - 24, 26), hints, _s.Small);
            if (_status != null && Time.unscaledTime < _statusUntil)
                GUI.Label(new Rect(r.x + r.width * 0.5f, r.y + 10, r.width * 0.5f - 24, 26),
                    _statusWarn ? $"<color=#ffb347>{_status}</color>" : $"<color=#5fd16b>{_status}</color>",
                    GuiStyleCache.With(_s.Small, TextAnchor.UpperRight));
        }

        private void DrawList<T>(Rect r, IList<T> items, float rowH, Func<T, string> label, Action<int> onActivate = null)
        {
            int t = (int)_tab;
            _listContentHeight = items.Count * rowH;
            var view = new Rect(0, 0, r.width - 18, _listContentHeight);
            _listScroll[t] = GUI.BeginScrollView(r, _listScroll[t], view);
            _nav.PushGroup(ListGroup, r, _listScroll[t]);
            int first = Mathf.Max(0, (int)(_listScroll[t].y / rowH) - 1), last = Mathf.Min(items.Count, first + (int)(r.height / rowH) + 3);
            for (int i = 0; i < items.Count; i++)
            {
                var row = new Rect(0, i * rowH, view.width, rowH - 5);
                string id = $"row:{t}:{i}";
                if (i < first || i >= last) { _nav.Register(id, row); continue; } // focusable even while scrolled away
                GUI.DrawTexture(row, i == _sel[t] ? _s.RowSel : _s.TabOff);
                if (Btn(row, label(items[i]), _s.Row, id))
                {
                    if (_sel[t] != i) _detailScroll = Vector2.zero;
                    _sel[t] = i;
                    onActivate?.Invoke(i);
                }
            }
            _nav.PopGroup();
            GUI.EndScrollView();
        }

        private void Empty(Rect r, string text) => GUI.Label(new Rect(r.x + 4, r.y + 10, r.width - 8, 90), $"<color=#7d8794>{text}</color>", _s.Wrapped);

        // ---- persons

        private void DrawPersonsTab()
        {
            var list = _listRect;
            if (TextBox(new Rect(list.x, list.y, list.width, 48), "field:pq", _personQuery, "Search name, license or ID…")) OpenQueryKeyboard();
            bool contacts = _personQuery.Trim().Length == 0;
            var rows = new Rect(list.x, list.y + 82, list.width, list.height - 82);
            GUI.Label(new Rect(list.x, list.y + 54, list.width, 24), contacts ? "RECENT CONTACTS & NEARBY" : $"{_people.Count} RESULT{(_people.Count == 1 ? "" : "S")}", _s.H2);
            if (_people.Count == 0)
            {
                Empty(rows, contacts ? "No recent contacts and nobody nearby. Tap the search box to look someone up." : "No matching persons on file.");
                Empty(_detailRect, "No person selected.");
                return;
            }
            _sel[(int)Tab.Persons] = Mathf.Clamp(_sel[(int)Tab.Persons], 0, _people.Count - 1);
            DrawList(rows, _people, RowHeight, p =>
                $"<b>{p.Item.LastName.ToUpperInvariant()}, {p.Item.FirstName}</b>   <color=#9aa3ad>{p.Tag}</color>{(p.Item.Wanted ? "   <color=#ff6b6b><b>WANTED</b></color>" : "")}");
            DrawPerson(_people[_sel[(int)Tab.Persons]].Item);
        }

        private void DrawPerson(Citizen c)
        {
            _lastPerson = c;
            var r = _detailRect;
            float w = r.width - 22;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);
            float y = 0;
            GUI.Label(new Rect(0, y, w, 36), $"{c.LastName.ToUpperInvariant()}, {c.FirstName}", _s.H1);
            GUI.Label(new Rect(0, y + 34, w, 24), $"Citizen {c.Id}", _s.Small);
            if (Btn(new Rect(w - 300, y + 4, 300, 46), "New report on this person", _s.ButtonStyle, "det:newrpt")) NewReport(citizenId: c.Id);
            if (Btn(new Rect(w - 500, y + 4, 190, 46), "Issue BOLO", _s.ButtonStyle, "det:bolo")) StartBolo(c, null);
            y += 66;
            if (c.Wanted) y = Banner(y, w, _s.Red, $"ACTIVE WARRANT{(c.Warrants.Count > 1 ? "S" : "")} ({c.Warrants.Count})  —  use caution, confirm identity");
            foreach (var b in _db.ActiveBolosFor(c.Id, null)) y = Banner(y, w, _s.Amber, $"BOLO {b.Id}: {b.Reason}");

            y = Section(y, w, "IDENTITY");
            y = Field(y, w, "Sex", c.Sex == "F" ? "Female" : "Male");
            y = Field(y, w, "Date of birth", $"{c.DateOfBirth}   (age {Age(c.DateOfBirth)})");
            y = Field(y, w, "Address", c.Address);
            y = Field(y, w, "Height / weight", $"{c.HeightCm} cm  /  {c.WeightKg} kg");
            y = Field(y, w, "Hair / eyes", $"{c.Hair}  /  {c.Eyes}");

            y = Section(y, w, "DRIVER LICENSE");
            if (c.License == null || c.License.Status == LicenseStatus.None) y = Field(y, w, "Status", "<color=#9aa3ad>No license on file</color>");
            else
            {
                y = Field(y, w, "Number", $"{c.License.Number}   (class {c.License.Class})");
                y = Field(y, w, "Status", LicenseColor(c.License.Status));
                y = Field(y, w, "Expires", c.License.Expires);
            }

            y = Section(y, w, $"WARRANTS ({c.Warrants.Count})");
            if (c.Warrants.Count == 0) y = Line(y, w, "<color=#9aa3ad>None</color>");
            foreach (var wr in c.Warrants)
                y = Line(y, w, $"<color=#ff6b6b><b>{(wr.Felony ? "FELONY" : "MISDEMEANOR")}</b></color>   {wr.Offense}   <color=#9aa3ad>{wr.Id} · issued {wr.Issued}</color>");

            y = Section(y, w, $"CRIMINAL HISTORY ({c.CriminalHistory.Count})");
            if (c.CriminalHistory.Count == 0) y = Line(y, w, "<color=#9aa3ad>No record</color>");
            foreach (var h in c.CriminalHistory) y = Line(y, w, $"<color=#9aa3ad>{h.Date}</color>   {h.Offense}  —  {h.Disposition}");

            y = Section(y, w, "REGISTERED VEHICLES");
            int n = 0;
            foreach (var v in _db.VehiclesOf(c.Id))
                if (LinkButton(ref y, w, $"<b>{v.Plate}</b>   {v.Description}{(v.Stolen ? "   <color=#ff6b6b><b>STOLEN</b></color>" : "")}", $"det:veh:{n++}")) ShowVehicle(v);
            if (n == 0) y = Line(y, w, "<color=#9aa3ad>None</color>");

            var citations = _db.CitationsOf(c.Id);
            y = Section(y, w, $"CITATIONS ({citations.Count})");
            if (citations.Count == 0) y = Line(y, w, "<color=#9aa3ad>None</color>");
            foreach (var ct in citations) y = Line(y, w, $"<color=#9aa3ad>{ct.Date}</color>   {ct.Offense}   ${ct.Fine}   <color=#9aa3ad>{ct.Location}</color>");

            var arrests = _db.ArrestsOf(c.Id);
            y = Section(y, w, $"ARRESTS ({arrests.Count})");
            if (arrests.Count == 0) y = Line(y, w, "<color=#9aa3ad>None</color>");
            for (int i = arrests.Count - 1; i >= 0; i--)   // newest first; each opens the full arrest record
            {
                var a = arrests[i];
                if (LinkButton(ref y, w, $"<b>{BookingNo(a)}</b>   {Escape(string.Join(", ", a.Charges))}   <color=#9aa3ad>{a.Date}</color>", $"det:arr:{i}")) { ShowArrest(a); break; }
            }

            y = ReportLinks(y, w, c.Id);
            _detailHeight = y + 20;
            _nav.PopGroup();
            GUI.EndScrollView();
        }

        // ---- vehicles

        private void DrawVehiclesTab()
        {
            var list = _listRect;
            if (TextBox(new Rect(list.x, list.y, list.width, 48), "field:vq", _vehicleQuery, "Run a plate, VIN or vehicle ID…")) OpenQueryKeyboard();
            bool contacts = _vehicleQuery.Trim().Length == 0;
            var rows = new Rect(list.x, list.y + 82, list.width, list.height - 82);
            GUI.Label(new Rect(list.x, list.y + 54, list.width, 24), contacts ? "RECENT STOPS & NEARBY" : $"{_cars.Count} RESULT{(_cars.Count == 1 ? "" : "S")}", _s.H2);
            if (_cars.Count == 0)
            {
                Empty(rows, contacts ? "No recent stops and no vehicles nearby. Tap the search box to run a plate." : "No matching vehicles on file.");
                Empty(_detailRect, "No vehicle selected.");
                return;
            }
            _sel[(int)Tab.Vehicles] = Mathf.Clamp(_sel[(int)Tab.Vehicles], 0, _cars.Count - 1);
            DrawList(rows, _cars, RowHeight, p =>
                $"<b>{p.Item.Plate}</b>   {p.Item.Color} {p.Item.Make} {p.Item.Model}   <color=#9aa3ad>{p.Tag}</color>{(p.Item.Stolen ? "   <color=#ff6b6b><b>STOLEN</b></color>" : "")}");
            DrawVehicle(_cars[_sel[(int)Tab.Vehicles]].Item);
        }

        private void DrawVehicle(VehicleRecord v)
        {
            _lastVehicle = v;
            var r = _detailRect;
            float w = r.width - 22;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);
            float y = 0;
            GUI.Label(new Rect(0, y, w, 36), v.Plate, _s.H1);
            GUI.Label(new Rect(0, y + 34, w - 520, 24), $"{v.Description}   ·   Vehicle {v.Id}", _s.Small);
            if (Btn(new Rect(w - 300, y + 4, 300, 46), "New report on this vehicle", _s.ButtonStyle, "det:newrpt")) NewReport(vehicleId: v.Id);
            if (Btn(new Rect(w - 500, y + 4, 190, 46), "Issue BOLO", _s.ButtonStyle, "det:bolo")) StartBolo(null, v);
            y += 66;
            if (v.Stolen) y = Banner(y, w, _s.Red, "REPORTED STOLEN  —  do not release, confirm with dispatch");
            foreach (var b in _db.ActiveBolosFor(null, v.Id)) y = Banner(y, w, _s.Amber, $"BOLO {b.Id}: {b.Reason}");

            y = Section(y, w, "VEHICLE");
            y = Field(y, w, "Make / model", $"{v.Make} {v.Model}");
            y = Field(y, w, "Year / color", $"{v.Year}  /  {v.Color}");
            y = Field(y, w, "VIN", v.Vin);

            y = Section(y, w, "REGISTRATION");
            y = Field(y, w, "Status", RegistrationColor(v.Registration));
            y = Field(y, w, "Expires", v.RegistrationExpires);
            y = Field(y, w, "Insurance", v.Insured ? "<color=#5fd16b>Insured</color>" : "<color=#ff6b6b><b>NO INSURANCE ON FILE</b></color>");

            y = Section(y, w, "REGISTERED OWNER");
            var owner = _db.GetCitizen(v.OwnerId);
            if (owner == null) y = Line(y, w, "<color=#9aa3ad>Unknown</color>");
            else
            {
                if (LinkButton(ref y, w, $"<b>{owner.LastName.ToUpperInvariant()}, {owner.FirstName}</b>   {owner.Id}{(owner.Wanted ? "   <color=#ff6b6b><b>WANTED</b></color>" : "")}", "det:owner")) ShowPerson(owner);
                y = Line(y, w, $"<color=#9aa3ad>{owner.Address}   ·   license {LicenseColor(owner.License.Status)}</color>");
            }

            y = ReportLinks(y, w, v.Id);
            _detailHeight = y + 20;
            _nav.PopGroup();
            GUI.EndScrollView();
        }

        // ---- warrants

        private void DrawWarrantsTab()
        {
            var list = _listRect;
            if (TextBox(new Rect(list.x, list.y, list.width, 48), "field:wf", _warrantFilter, "Filter by name or offense…")) OpenQueryKeyboard();
            var filtered = FilteredWanted();
            var rows = new Rect(list.x, list.y + 82, list.width, list.height - 82);
            GUI.Label(new Rect(list.x, list.y + 54, list.width, 24), $"{filtered.Count} OF {_wanted.Count} WITH ACTIVE WARRANTS", _s.H2);
            if (filtered.Count == 0) { Empty(rows, "No active warrants match."); Empty(_detailRect, "No person selected."); return; }
            _sel[(int)Tab.Warrants] = Mathf.Clamp(_sel[(int)Tab.Warrants], 0, filtered.Count - 1);
            DrawList(rows, filtered, RowHeight, c =>
            {
                var top = c.Warrants[0];
                return $"<b>{c.LastName.ToUpperInvariant()}, {c.FirstName}</b>   {(top.Felony ? "<color=#ff6b6b>F</color>" : "<color=#ffb347>M</color>")} {top.Offense}{(c.Warrants.Count > 1 ? $" <color=#9aa3ad>+{c.Warrants.Count - 1}</color>" : "")}";
            });
            DrawPerson(filtered[_sel[(int)Tab.Warrants]]);
        }

        // ---- reports

        private void DrawReportsTab()
        {
            var list = _listRect;
            float y = list.y;
            if (Btn(new Rect(list.x, y, list.width, 48), "+  New incident report", _s.Primary, "rpt:new")) NewReport();
            y += 56;
            var stop = _activity.LastStop;
            if (stop != null)
            {
                var car = _db.GetVehicle(stop.VehicleId);
                string what = car != null ? car.Plate : _db.GetCitizen(stop.CitizenId)?.FullName ?? "last stop";
                if (Btn(new Rect(list.x, y, list.width, 48), $"✦  Report from last stop · {what} · {stop.Time:HH:mm}", _s.ButtonStyle, "rpt:fromstop")) NewReportFromStop(stop);
                y += 56;
            }
            var reports = Reports();
            var rows = new Rect(list.x, y + 28, list.width, list.yMax - y - 28);
            GUI.Label(new Rect(list.x, y + 2, list.width, 24), $"FILED REPORTS ({reports.Count})", _s.H2);
            if (reports.Count == 0) Empty(rows, "No reports filed yet.");
            else
            {
                _sel[(int)Tab.Reports] = Mathf.Clamp(_sel[(int)Tab.Reports], 0, reports.Count - 1);
                DrawList(rows, reports, ReportRowHeight, rp =>
                    $"<b>{Escape(string.IsNullOrEmpty(rp.Title) ? "(untitled)" : rp.Title)}</b>{(rp == _source ? "  <color=#7fb2ff>● open</color>" : "")}\n<size=15><color=#9aa3ad>{rp.Id} · {rp.Type} · {rp.Updated}</color></size>",
                    i => LoadReport(reports[i]));
            }

            if (_draft == null) { Empty(_detailRect, "Start a new report, or pick a filed report to view and edit it.\n\nAfter a traffic stop, \"Report from last stop\" writes the whole report for you."); return; }
            DrawEditor();
        }

        private void DrawEditor()
        {
            var r = _detailRect;
            float w = r.width - 22;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);
            float y = 0, lx = 150f, fw = w - lx;

            GUI.Label(new Rect(0, y, w, 36), _source == null ? "NEW INCIDENT REPORT" : $"INCIDENT REPORT {_source.Id}", _s.H1);
            string meta = _source == null ? $"Draft  ·  {_draft.Officer}" : $"Created {_source.Created}  ·  updated {_source.Updated}  ·  {_source.Officer}";
            GUI.Label(new Rect(0, y + 34, w, 24), meta + (_dirty ? "   <color=#ffb347>● unsaved changes</color>" : ""), _s.Small);
            y += 70;

            // Autofill: the one-press way to write the report.
            var bar = new Rect(0, y, w, 64);
            GUI.DrawTexture(bar, _s.Chip);
            if (Btn(new Rect(10, y + 8, 300, 48), "✦  Autofill report", _s.Primary, "rpt:autofill")) Autofill();
            float bx = 320;
            if (_canUndo) { if (Btn(new Rect(bx, y + 8, 170, 48), "Undo autofill", _s.ButtonStyle, "rpt:undo")) UndoAutofill(); bx += 180; }
            GUI.Label(new Rect(bx + 6, y + 8, w - bx - 16, 48), "<color=#b9c6dc>Writes the title and narrative from the people, vehicles, citations, arrests and stop on this report.</color>", GuiStyleCache.With(_s.Small, TextAnchor.MiddleLeft, true));
            y += 78;

            GUI.Label(new Rect(0, y + 13, lx, 28), "Title", _s.Label);
            if (TextBox(new Rect(lx, y, fw, 48), "rpt:title", _draft.Title, "Tap to type, or use Autofill"))
                OpenKeyboard("rpt:title", "Report title", () => _draft.Title, v => { _draft.Title = v; MarkDirty(); }, 120, false, autoCap: true);
            y += 58;

            GUI.Label(new Rect(0, y + 13, lx, 28), "Type", _s.Label);
            int type = Mathf.Max(0, Array.IndexOf(ReportTypes, _draft.Type));
            if (Btn(new Rect(lx, y, 56, 48), "◀", _s.ButtonStyle, "rpt:type-")) { _draft.Type = ReportTypes[(type + ReportTypes.Length - 1) % ReportTypes.Length]; MarkDirty(); }
            GUI.Label(new Rect(lx + 70, y + 11, 300, 28), _draft.Type ?? ReportTypes[0], _s.Body);
            if (Btn(new Rect(lx + 380, y, 56, 48), "▶", _s.ButtonStyle, "rpt:type+")) { _draft.Type = ReportTypes[(type + 1) % ReportTypes.Length]; MarkDirty(); }
            y += 58;

            GUI.Label(new Rect(0, y + 13, lx, 28), "Location", _s.Label);
            if (TextBox(new Rect(lx, y, fw - 210, 48), "rpt:location", _draft.Location, "Where it happened"))
                OpenKeyboard("rpt:location", "Location", () => _draft.Location, v => { _draft.Location = v; MarkDirty(); }, 120, false, autoCap: true);
            if (Btn(new Rect(w - 200, y, 200, 48), "Use my location", _s.ButtonStyle, "rpt:here")) { _draft.Location = _officerLocation; MarkDirty(); }
            y += 58;

            GUI.Label(new Rect(0, y + 13, lx, 28), "Narrative", _s.Label);
            string narrative = _draft.Narrative ?? "";
            float areaH = Mathf.Max(200f, _s.Wrapped.CalcHeight(new GUIContent(Escape(narrative) + "▌"), fw - 24) + 24f);
            if (TextBox(new Rect(lx, y, fw, areaH), "rpt:narrative", narrative, "Tap Autofill to write it for you, add phrases below, or tap here to type.", multiline: true))
                OpenKeyboard("rpt:narrative", "Narrative", () => _draft.Narrative, v => { _draft.Narrative = v; MarkDirty(); }, 4000, true, autoCap: true);
            y += areaH + 12;

            // Phrase chips: tap to add a ready-made sentence.
            GUI.Label(new Rect(lx, y + 4, 200, 24), "ADD A PHRASE", _s.H2);
            float cx = lx + 160;
            for (int i = 0; i < NarrativeBuilder.Phrases.Length; i++)
            {
                var cat = NarrativeBuilder.Phrases[i];
                var cr = new Rect(cx, y, 140, 40);
                if (i == _phraseCategory) GUI.DrawTexture(new Rect(cr.x, cr.yMax - 3, cr.width, 3), _s.Outline);
                if (Btn(cr, cat.Name, i == _phraseCategory ? _s.Primary : _s.ButtonStyle, $"rpt:cat:{i}")) _phraseCategory = i;
                cx += 148;
            }
            y += 50;
            float chipW = (fw - 10) / 2f;
            var phrases = NarrativeBuilder.Phrases[_phraseCategory].Phrases;
            for (int i = 0; i < phrases.Length; i++)
            {
                string text = NarrativeBuilder.FillPhrase(phrases[i], _draft, _db);
                var pr = new Rect(lx + (i % 2) * (chipW + 10), y + (i / 2) * 62, chipW, 56);
                if (Btn(pr, "+  " + Escape(text), _s.ChipStyle, $"rpt:phrase:{i}"))
                {
                    _draft.Narrative = NarrativeBuilder.Append(_draft.Narrative, text);
                    MarkDirty();
                    Status("Phrase added.", false);
                }
            }
            y += ((phrases.Length + 1) / 2) * 62 + 8;

            y = DrawInvolvedPeople(y, w);
            y = DrawInvolvedVehicles(y, w);

            y += 12;
            if (Btn(new Rect(0, y, 220, 52), "Save report", _s.Primary, "rpt:save")) SaveReport();
            if (Btn(new Rect(232, y, 240, 52), _source == null ? "Discard draft" : "Discard changes", _s.ButtonStyle, "rpt:discard")) DiscardChanges();
            y += 76;

            _detailHeight = y;
            _nav.PopGroup();
            GUI.EndScrollView();
        }

        private float DrawInvolvedPeople(float y, float w)
        {
            y = Section(y, w, $"PERSONS INVOLVED ({_draft.CitizenIds.Count})");
            for (int i = 0; i < _draft.CitizenIds.Count; i++)
            {
                var c = _db.GetCitizen(_draft.CitizenIds[i]);
                string text = c == null ? _draft.CitizenIds[i] : $"<b>{c.LastName.ToUpperInvariant()}, {c.FirstName}</b>   <color=#9aa3ad>{c.Id} · DOB {c.DateOfBirth}</color>{(c.Wanted ? "   <color=#ff6b6b>WANTED</color>" : "")}";
                GUI.Label(new Rect(0, y + 10, w - 250, 28), text, _s.Body);
                if (c != null && Btn(new Rect(w - 240, y, 110, 46), "Open", _s.ButtonStyle, $"rpt:popen:{i}")) ShowPerson(c);
                if (Btn(new Rect(w - 120, y, 120, 46), "Remove", _s.ButtonStyle, $"rpt:prem:{i}")) { _draft.CitizenIds.RemoveAt(i); MarkDirty(); break; }
                y += 52;
            }
            y = SuggestionChips(y, w, PersonSuggestions(), c => $"+  {c.LastName.ToUpperInvariant()}, {c.FirstName}", AttachPerson, "rpt:sugp");
            if (Btn(new Rect(0, y, 240, 46), "Find a person…", _s.ButtonStyle, "rpt:findp"))
                OpenKeyboard("rpt:findp", "Find a person to add — name, license or ID", () => _addPerson, v => _addPerson = v, 64, false, caps: true,
                    suggest: q =>
                    {
                        var list = new List<MdtKeyboard.Suggestion>();
                        if (q.Trim().Length < 2) return list;
                        var exact = _db.GetCitizen(q.Trim().ToUpperInvariant());
                        var hits = exact != null ? new List<Citizen> { exact } : _db.SearchCitizens(q, 4);
                        foreach (var c in hits) { var pick = c; list.Add(new MdtKeyboard.Suggestion($"+  {c.LastName.ToUpperInvariant()}, {c.FirstName} · {c.DateOfBirth}", () => { AttachPerson(pick); _addPerson = ""; })); }
                        return list;
                    });
            return y + 60;
        }

        private float DrawInvolvedVehicles(float y, float w)
        {
            y = Section(y, w, $"VEHICLES INVOLVED ({_draft.VehicleIds.Count})");
            for (int i = 0; i < _draft.VehicleIds.Count; i++)
            {
                var v = _db.GetVehicle(_draft.VehicleIds[i]);
                string text = v == null ? _draft.VehicleIds[i] : $"<b>{v.Plate}</b>   {v.Description}{(v.Stolen ? "   <color=#ff6b6b>STOLEN</color>" : "")}";
                GUI.Label(new Rect(0, y + 10, w - 250, 28), text, _s.Body);
                if (v != null && Btn(new Rect(w - 240, y, 110, 46), "Open", _s.ButtonStyle, $"rpt:vopen:{i}")) ShowVehicle(v);
                if (Btn(new Rect(w - 120, y, 120, 46), "Remove", _s.ButtonStyle, $"rpt:vrem:{i}")) { _draft.VehicleIds.RemoveAt(i); MarkDirty(); break; }
                y += 52;
            }
            y = SuggestionChips(y, w, VehicleSuggestions(), v => $"+  {v.Plate} · {v.Color} {v.Make} {v.Model}", AttachVehicle, "rpt:sugv");
            if (Btn(new Rect(0, y, 240, 46), "Find a vehicle…", _s.ButtonStyle, "rpt:findv"))
                OpenKeyboard("rpt:findv", "Find a vehicle to add — plate, VIN or ID", () => _addVehicle, v => _addVehicle = v, 32, false, caps: true,
                    suggest: q =>
                    {
                        var list = new List<MdtKeyboard.Suggestion>();
                        if (q.Trim().Length < 2) return list;
                        foreach (var v in _db.SearchVehicles(q, 4)) { var pick = v; list.Add(new MdtKeyboard.Suggestion($"+  {v.Plate} · {v.Color} {v.Make} {v.Model}", () => { AttachVehicle(pick); _addVehicle = ""; })); }
                        return list;
                    });
            return y + 60;
        }

        private float SuggestionChips<T>(float y, float w, List<T> items, Func<T, string> label, Action<T> pick, string idPrefix)
        {
            if (items.Count == 0) return y;
            GUI.Label(new Rect(0, y + 2, w, 22), "<color=#9aa3ad>Suggested — tap to add</color>", _s.Small);
            y += 26;
            float chipW = (w - 20) / 3f;
            for (int i = 0; i < items.Count; i++)
            {
                var r = new Rect((i % 3) * (chipW + 10), y + (i / 3) * 52, chipW, 46);
                if (Btn(r, label(items[i]), _s.ChipStyle, $"{idPrefix}:{i}")) { pick(items[i]); Status("Added to the report.", false); }
            }
            return y + ((items.Count + 2) / 3) * 52 + 6;
        }

        // ------------------------------------------------------------------ detail building blocks

        private float Section(float y, float w, string title)
        {
            y += 14;
            GUI.Label(new Rect(0, y, w, 24), title, _s.H2);
            GUI.DrawTexture(new Rect(0, y + 26, w, 1), _s.Divider);
            return y + 34;
        }

        private float Field(float y, float w, string label, string value)
        {
            GUI.Label(new Rect(0, y, 210, LineHeight), label, _s.Label);
            GUI.Label(new Rect(220, y, w - 220, LineHeight), value ?? "", _s.Body);
            return y + LineHeight;
        }

        private float Line(float y, float w, string text)
        {
            GUI.Label(new Rect(0, y, w, LineHeight), text, _s.Body);
            return y + LineHeight;
        }

        private float Banner(float y, float w, Texture2D colour, string text)
        {
            GUI.DrawTexture(new Rect(0, y, w, 38), colour);
            GUI.Label(new Rect(14, y + 6, w - 20, 28), text, _s.BannerText);
            return y + 46;
        }

        private bool LinkButton(ref float y, float w, string text, string id)
        {
            var r = new Rect(0, y, w, 44);
            GUI.DrawTexture(r, _s.TabOff);
            bool clicked = Btn(r, text, _s.Row, id);
            y += 50;
            return clicked;
        }

        private float ReportLinks(float y, float w, string recordId)
        {
            var reports = _db.ReportsFor(recordId);
            y = Section(y, w, $"INCIDENT REPORTS ({reports.Count})");
            if (reports.Count == 0) return Line(y, w, "<color=#9aa3ad>None</color>");
            for (int i = 0; i < reports.Count; i++)
            {
                var rp = reports[i];
                if (LinkButton(ref y, w, $"<b>{rp.Id}</b>   {Escape(rp.Title)}   <color=#9aa3ad>{rp.Type} · {rp.Updated}</color>", $"det:rpt:{i}")) { SelectTab(Tab.Reports); LoadReport(rp); }
            }
            return y;
        }

        /// <summary>"HH:mm" from a records timestamp ("yyyy-MM-dd HH:mm"), or "today".</summary>
        private static string TimeOf(string date) => date != null && date.Length >= 16 ? date.Substring(11, 5) : "today";

        private static string Escape(string text) => string.IsNullOrEmpty(text) ? "" : text.Replace("<", "‹").Replace(">", "›");

        private static int Age(string dob)
        {
            if (!DateTime.TryParse(dob, out var d)) return 0;
            var today = GameClock.Now.Date;   // ages in the game's year
            int age = today.Year - d.Year;
            if (today < d.AddYears(age)) age--;
            return age;
        }

        private static string LicenseColor(LicenseStatus s)
        {
            switch (s)
            {
                case LicenseStatus.Valid: return "<color=#5fd16b>VALID</color>";
                case LicenseStatus.Expired: return "<color=#ffb347><b>EXPIRED</b></color>";
                case LicenseStatus.None: return "<color=#9aa3ad>NONE</color>";
                default: return $"<color=#ff6b6b><b>{s.ToString().ToUpperInvariant()}</b></color>";
            }
        }

        private static string RegistrationColor(RegistrationStatus s)
        {
            switch (s)
            {
                case RegistrationStatus.Valid: return "<color=#5fd16b>VALID</color>";
                case RegistrationStatus.Expired: return "<color=#ffb347><b>EXPIRED</b></color>";
                default: return $"<color=#ff6b6b><b>{s.ToString().ToUpperInvariant()}</b></color>";
            }
        }
    }
}
