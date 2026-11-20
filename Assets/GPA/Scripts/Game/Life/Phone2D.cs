using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GPA.Callouts;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Game
{
    /// <summary>
    /// The officer's personal phone (off duty only — it stays in the locker on shift).
    /// Up arrow / Right Bumper (RB) opens it. Apps:
    /// - Cab: book a taxi to one of your homes or a city spot; it pulls up next to you, get in
    ///   (Interact) and the ride skips ahead with a fade, the fare charged and the clock advanced.
    /// - Mechanic: have one of your owned cars delivered to the street beside you.
    /// - Bank: balance, earnings, daily property income.
    /// - Save: save the game.
    /// - Dispatch: call in and start a shift.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class Phone2D : MonoBehaviour
    {
        public static Phone2D Instance { get; private set; }
        public static bool IsOpen => Instance != null && Instance._open;

        private sealed class Row
        {
            public string Label, Value; public Action Use; public bool Enabled;
            public Row(string l, string v, Action u, bool e = true) { Label = l; Value = v; Use = u; Enabled = e; }
        }

        private sealed class App { public string Name, Glyph; public Color Colour; public Func<List<Row>> Page; public string Blurb; }

        private readonly List<App> _apps = new List<App>();
        private App _app;                 // null = home screen
        private bool _open;
        private float _slide;             // 0 hidden .. 1 up
        private int _sel;
        private float _navRepeat;
        private int _openedFrame;
        private string _notice;
        private float _noticeUntil;
        private Officer2D _officer;
        private List<Row> _rows = new List<Row>();
        private GUIStyle _title, _small, _row, _rowSel, _value, _off, _glyph, _appLabel, _clock, _help;
        private string _lastSave;

        // Taxi
        private enum Cab { None, Coming, Waiting, Riding }
        private Cab _cab;
        private string _cabDest;
        private Vector2 _cabTo;
        private int _cabFare;
        private float _cabEta, _cabExpires;
        private Car2D _taxi;
        private float _fade;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<Phone2D>() != null || FindAnyObjectByType<Officer2D>() == null) return;
            new GameObject("Phone").AddComponent<Phone2D>();
        }

        private void Awake()
        {
            Instance = this;
            _apps.Add(new App { Name = "Cab", Glyph = "TAXI", Colour = new Color(0.98f, 0.78f, 0.15f), Page = CabPage, Blurb = "Downtown Cab Co." });
            _apps.Add(new App { Name = "Mechanic", Glyph = "CAR", Colour = new Color(0.95f, 0.5f, 0.2f), Page = MechanicPage, Blurb = "Your cars, delivered" });
            _apps.Add(new App { Name = "Bank", Glyph = "$", Colour = new Color(0.25f, 0.75f, 0.4f), Page = BankPage, Blurb = "Halden Savings" });
            _apps.Add(new App { Name = "Save", Glyph = "SAVE", Colour = new Color(0.3f, 0.55f, 0.95f), Page = SavePage, Blurb = "Save your game" });
            _apps.Add(new App { Name = "Dispatch", Glyph = "911", Colour = new Color(0.85f, 0.25f, 0.3f), Page = DispatchPage, Blurb = "Call in for a shift" });
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private static PlayerLife Life => PlayerLife.Instance;
        private static bool OnDuty => CalloutManager.Instance != null && CalloutManager.Instance.OnDuty;

        private Officer2D Officer => _officer != null ? _officer : (_officer = FindAnyObjectByType<Officer2D>());

        private static bool Playing() =>
            !Services.TryGet(out IGameStateService s) || s.Current == GameState.OnFoot || s.Current == GameState.InVehicle;

        // ------------------------------------------------------------------ open / close

        public void Open()
        {
            if (_open) return;
            if (OnDuty) { Life?.Toast("Your phone stays in the locker on shift", Color.white); return; }
            _open = true; _app = null; _sel = 0; _openedFrame = Time.frameCount;
            if (Officer != null && Officer.CurrentVehicle == null) Officer.SetControlLocked(this, true);
            if (Services.TryGet(out IGameStateService st)) st.Push(GameState.Menu);
            GameAudio.UiClick();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            if (Officer != null) Officer.SetControlLocked(this, false);
            if (Services.TryGet(out IGameStateService st)) st.Pop(GameState.Menu);
            GameplayHotkeys.SuppressThisFrame();
        }

        private void Notice(string text) { _notice = text; _noticeUntil = Time.unscaledTime + 3f; }

        // ------------------------------------------------------------------ input (LateUpdate: after the pause menu has looked at Esc)

        private void LateUpdate()
        {
            UpdateCab();
            _slide = Mathf.MoveTowards(_slide, _open ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            var kb = Keyboard.current; var pad = Gamepad.current;

            if (!_open)
            {
                // Keyboard: Up arrow (not bound in any gameplay map). Controller: Right Bumper — the one button
                // free on foot (D-pad Up cycles interaction options); in a car RB is the siren, so it only
                // opens the phone in cars without a lightbar (every off-duty car).
                bool padPress = pad != null && pad.rightShoulder.wasPressedThisFrame
                                && !(Officer != null && Officer.CurrentVehicle is Car2D car && car.GetComponent<Lightbar2D>() != null);
                bool press = (kb != null && kb.upArrowKey.wasPressedThisFrame) || padPress;
                if (press && Playing() && !GameMenu2D.TitleShowing && !LifeMenu.AnyOpen && _cab != Cab.Riding && !GameplayHotkeys.Blocked) Open();
                return;
            }
            if (OnDuty) { Close(); return; }

            _rows = _app != null ? _app.Page() : null;
            int count = _app != null ? _rows.Count : _apps.Count;
            if (Time.frameCount <= _openedFrame) return;

            bool driving = Officer != null && Officer.CurrentVehicle != null;
            int dir = 0;
            if (kb != null)
            {
                if (kb.upArrowKey.wasPressedThisFrame || (!driving && kb.wKey.wasPressedThisFrame)) dir = _app == null ? -2 : -1;
                if (kb.downArrowKey.wasPressedThisFrame || (!driving && kb.sKey.wasPressedThisFrame)) dir = _app == null ? 2 : 1;
                if (_app == null && (kb.leftArrowKey.wasPressedThisFrame || (!driving && kb.aKey.wasPressedThisFrame))) dir = -1;
                if (_app == null && (kb.rightArrowKey.wasPressedThisFrame || (!driving && kb.dKey.wasPressedThisFrame))) dir = 1;
            }
            if (pad != null)
            {
                Vector2 v = pad.dpad.ReadValue() + (driving ? Vector2.zero : pad.leftStick.ReadValue());
                if (v.magnitude < 0.5f) _navRepeat = 0f;
                else if (_navRepeat <= 0f)
                {
                    _navRepeat = 0.22f;
                    if (Mathf.Abs(v.y) >= Mathf.Abs(v.x)) dir = v.y > 0 ? (_app == null ? -2 : -1) : (_app == null ? 2 : 1);
                    else if (_app == null) dir = v.x > 0 ? 1 : -1;
                }
                _navRepeat -= Time.unscaledDeltaTime;
            }
            if (dir != 0 && count > 0) { _sel = Mathf.Clamp(_sel + dir, 0, count - 1); }
            _sel = Mathf.Clamp(_sel, 0, Mathf.Max(0, count - 1));

            bool confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || (!driving && kb.eKey.wasPressedThisFrame))) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonEast.wasPressedThisFrame)
                        || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);
            if (pad != null && pad.rightShoulder.wasPressedThisFrame) { Close(); return; }
            if (confirm) Activate(_sel);
            else if (back) Back();
        }

        private void Activate(int i)
        {
            GameAudio.UiClick();
            if (_app == null) { if (i >= 0 && i < _apps.Count) { _app = _apps[i]; _sel = 0; } return; }
            if (_rows == null || i < 0 || i >= _rows.Count || !_rows[i].Enabled) return;
            _rows[i].Use?.Invoke();
        }

        private void Back()
        {
            GameAudio.UiClick();
            if (_app != null) { _sel = _apps.IndexOf(_app); _app = null; }
            else Close();
        }

        // ------------------------------------------------------------------ apps

        private List<Row> CabPage()
        {
            var rows = new List<Row>();
            switch (_cab)
            {
                case Cab.Coming:
                    rows.Add(new Row($"To {_cabDest}", $"${_cabFare:N0}", null, false));
                    rows.Add(new Row("Arriving in", $"{Mathf.CeilToInt(Mathf.Max(0f, _cabEta - Time.time))} s", null, false));
                    rows.Add(new Row("Cancel the cab", "", CancelCab));
                    return rows;
                case Cab.Waiting:
                    rows.Add(new Row("Your cab is outside", "get in (Interact)", null, false));
                    rows.Add(new Row($"To {_cabDest}", $"${_cabFare:N0}", null, false));
                    rows.Add(new Row("Cancel the cab", "", CancelCab));
                    return rows;
            }
            var interiors = FindAnyObjectByType<InteriorManager2D>();
            if (interiors != null && interiors.Inside) { rows.Add(new Row("Step outside to book a cab", "", null, false)); return rows; }
            foreach (var (name, at) in Destinations())
            {
                int fare = FareTo(at);
                rows.Add(new Row(name, $"${fare:N0}", () => BookCab(name, at, fare), Life != null && Life.CanAfford(fare)));
            }
            return rows;
        }

        private IEnumerable<(string, Vector2)> Destinations()
        {
            var world = OffDutyWorld.Instance;
            if (world != null && Life != null)
                foreach (var p in world.Properties.Where(p => Life.OwnsProperty(p.Building.Id)))
                    yield return ((Life.Data.home == p.Building.Id ? "Home: " : "") + p.Title, p.Marker);
            if (FleetGarage2D.Instance != null) yield return ("Precinct", FleetGarage2D.Instance.PrecinctPoint);
            if (PlayerNeeds.Instance != null && PlayerNeeds.Instance.HospitalPoint.HasValue) yield return ("Hospital", PlayerNeeds.Instance.HospitalPoint.Value);
            if (world != null)
            {
                if (world.DealershipPoint != Vector2.zero) yield return ("Halden Motors", world.DealershipPoint);
                if (world.ClothingPoint != Vector2.zero) yield return ("Halden Threads", world.ClothingPoint);
                if (world.CasinoPoint != Vector2.zero) yield return ("Harbor Lights Casino", world.CasinoPoint);
            }
        }

        private int FareTo(Vector2 to)
        {
            float d = Officer != null ? Vector2.Distance(Officer.transform.position, to) : 0f;
            return Mathf.Max(15, Mathf.RoundToInt((15f + d * 0.12f) / 5f) * 5);
        }

        private List<Row> MechanicPage()
        {
            var rows = new List<Row>();
            var world = OffDutyWorld.Instance;
            if (Life == null || world == null || Life.Data.cars.Count == 0) { rows.Add(new Row("No cars yet", "visit Halden Motors", null, false)); return rows; }
            var interiors = FindAnyObjectByType<InteriorManager2D>();
            bool inside = interiors != null && interiors.Inside;
            foreach (string id in Life.Data.cars)
            {
                var spec = VehicleCatalog.Instance != null ? VehicleCatalog.Instance.Get(id) : null;
                if (spec == null) continue;
                bool isOut = world.CarIsOut(id);
                rows.Add(new Row(spec.DisplayName, inside ? "step outside first" : isOut ? "out · bring it here" : "deliver to me", () =>
                {
                    if (Officer == null) return;
                    if (world.DeliverCar(id, Officer.transform.position)) Notice($"{spec.DisplayName} delivered — it's on the street.");
                }, !inside));
            }
            return rows;
        }

        private List<Row> BankPage()
        {
            var rows = new List<Row>();
            if (Life == null) return rows;
            var d = Life.Data;
            var world = OffDutyWorld.Instance;
            rows.Add(new Row("Balance", $"${d.money:N0}", null, false));
            rows.Add(new Row("Property income", world != null ? $"${world.TotalDailyIncome():N0}/day" : "-", null, false));
            rows.Add(new Row("Homes owned", d.properties.Count.ToString(), null, false));
            rows.Add(new Row("Cars owned", world != null ? $"{d.cars.Count} / {world.GarageSpaces} spaces" : d.cars.Count.ToString(), null, false));
            rows.Add(new Row("Total earned", $"${d.earned:N0}", null, false));
            rows.Add(new Row("Total spent", $"${d.spent:N0}", null, false));
            long casino = d.casinoWon - d.casinoLost;
            rows.Add(new Row("Casino", (casino >= 0 ? "+" : "-") + $"${Math.Abs(casino):N0}", null, false));
            return rows;
        }

        private List<Row> SavePage()
        {
            var peek = SaveGame.Exists ? SaveGame.Peek() : null;
            return new List<Row>
            {
                new Row("Save game", _lastSave != null ? "saved " + _lastSave : peek != null ? "last: " + peek.savedAt : "", () =>
                {
                    bool ok = SaveGame.Save();
                    Life?.Save();
                    _lastSave = DateTime.Now.ToString("HH:mm");
                    Notice(ok ? "Game saved." : "Couldn't save right now.");
                }),
            };
        }

        private List<Row> DispatchPage() => new List<Row>
        {
            new Row("Call in — start a shift", "10-8", () =>
            {
                Close();
                CalloutManager.Instance?.SetDuty(true);
            }, CalloutManager.Instance != null),
        };

        // ------------------------------------------------------------------ taxi

        private void BookCab(string name, Vector2 to, int fare)
        {
            _cab = Cab.Coming; _cabDest = name; _cabTo = to; _cabFare = fare;
            _cabEta = Time.time + UnityEngine.Random.Range(8f, 14f);
            Notice("Cab booked — wait by the road.");
        }

        private void CancelCab()
        {
            if (_taxi != null) Destroy(_taxi.gameObject);
            _taxi = null; _cab = Cab.None;
            Notice("Cab cancelled.");
        }

        private void UpdateCab()
        {
            if (_cab == Cab.Coming && Time.time >= _cabEta && Officer != null)
            {
                var interiors = FindAnyObjectByType<InteriorManager2D>();
                if (interiors != null && interiors.Inside) { _cabEta = Time.time + 2f; return; }   // wait until you're outside
                var spec = VehicleCatalog.Instance != null ? VehicleCatalog.Instance.Get("taxi") : null;
                var garage = FleetGarage2D.Instance;
                if (spec == null || garage == null) { _cab = Cab.None; return; }
                _taxi = garage.SpawnPersonal(spec, Officer.transform.position);
                if (_taxi == null) { _cab = Cab.None; return; }
                _taxi.name = "Taxi (your cab)";
                var use = _taxi.gameObject.AddComponent<Interactable2D>();
                use.Range = 3.6f;
                use.Prompt = $"Ride to {_cabDest} — ${_cabFare:N0}";
                use.Used += _ => StartCoroutine(Ride());
                _cab = Cab.Waiting;
                _cabExpires = Time.time + 120f;
                Life?.Toast("Your cab is here", new Color(0.98f, 0.8f, 0.2f));
                GameAudio.UiClick();
            }
            else if (_cab == Cab.Waiting)
            {
                if (_taxi == null) { _cab = Cab.None; return; }
                if (Officer != null && (Officer.CurrentVehicle as Car2D) == _taxi) { StartCoroutine(Ride()); return; }   // got in the driver's seat: just take the ride
                if (Time.time > _cabExpires) { Destroy(_taxi.gameObject); _taxi = null; _cab = Cab.None; Life?.Toast("Your cab gave up and left", Color.white); }
            }
        }

        private IEnumerator Ride()
        {
            if (_cab != Cab.Waiting) yield break;
            _cab = Cab.Riding;
            var officer = Officer;
            if (officer == null) { _cab = Cab.None; yield break; }
            if (Life != null && !Life.Spend(_cabFare, "Cab fare")) { Life.Toast("Not enough cash for the fare", Color.white); _cab = Cab.Waiting; yield break; }
            officer.SetControlLocked(this, true);
            for (_fade = 0f; _fade < 1f; _fade += Time.unscaledDeltaTime * 2.5f) yield return null;
            _fade = 1f;

            if (officer.CurrentVehicle != null) officer.ExitVehicle(_taxi.transform.position, Quaternion.identity);
            float dist = Vector2.Distance(officer.transform.position, _cabTo);
            officer.transform.position = _cabTo;
            if (officer.TryGetComponent(out Rigidbody2D rb)) { rb.position = _cabTo; rb.linearVelocity = Vector2.zero; }
            if (_taxi != null) Destroy(_taxi.gameObject);
            _taxi = null;
            Camera2DFollow.SnapAll();
            if (Services.TryGet(out ITimeOfDay clock)) clock.SetTime(clock.Hour + Mathf.Max(5f, dist / 12f) / 60f);   // ~12 m per in-game minute through traffic
            Life?.Toast($"Arrived: {_cabDest}", Color.white);

            float hold = Time.unscaledTime + 0.6f;
            while (Time.unscaledTime < hold) yield return null;
            for (; _fade > 0f; _fade -= Time.unscaledDeltaTime * 2f) yield return null;
            _fade = 0f;
            officer.SetControlLocked(this, false);
            _cab = Cab.None;
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (_fade > 0f)
            {
                GUI.depth = -900;
                GUI.color = new Color(0, 0, 0, _fade);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            if (_slide <= 0.001f) return;
            Styles();
            GUI.depth = -120;
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = 320f, h = 600f;
            float ease = 1f - Mathf.Pow(1f - _slide, 3f);
            var body = new Rect(canvas.x - w - 60f, canvas.y - h * ease + 40f * (1f - ease) - 30f * ease, w, h);

            // Case, screen
            GUI.DrawTexture(body, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.07f, 0.07f, 0.09f, 0.98f), 0, 34f);
            var screen = new Rect(body.x + 12, body.y + 14, w - 24, h - 28);
            GUI.DrawTexture(screen, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, _app != null ? new Color(0.1f, 0.11f, 0.15f) : new Color(0.13f, 0.17f, 0.3f), 0, 26f);
            if (_app == null)   // wallpaper glow: a soft gradient into the bottom of the screen (no hard edge)
            {
                const int bands = 16;
                float top = screen.y + screen.height * 0.4f, bandH = screen.height * 0.6f / bands;
                for (int i = 0; i < bands; i++)
                {
                    float a = 0.38f * Mathf.Pow((i + 1f) / bands, 1.6f);
                    bool last = i == bands - 1;
                    var band = new Rect(screen.x, top + i * bandH, screen.width, last ? screen.yMax - (top + i * bandH) : bandH + 0.5f);
                    GUI.DrawTexture(band, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.35f, 0.2f, 0.45f, a),
                        Vector4.zero, last ? new Vector4(0f, 0f, 26f, 26f) : Vector4.zero);
                }
            }
            GUI.DrawTexture(new Rect(screen.center.x - 40, screen.y + 8, 80, 20), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Color.black, 0, 10f);   // notch

            // Status bar
            string time = Services.TryGet(out ITimeOfDay clock) ? $"{Mathf.FloorToInt(clock.Hour):00}:{Mathf.FloorToInt(clock.Hour % 1f * 60f):00}" : "";
            GUI.Label(new Rect(screen.x + 22, screen.y + 6, 80, 24), time, _small);
            GUI.Label(new Rect(screen.xMax - 122, screen.y + 6, 100, 24), Life != null ? $"${Life.Money:N0}" : "", _value);

            if (_app == null) DrawHome(screen); else DrawApp(screen);

            string help = _notice != null && Time.unscaledTime < _noticeUntil ? _notice
                : Gamepad.current != null ? "A open   B back   RB close" : "Arrows choose · Enter open · Backspace back";
            GUI.Label(new Rect(screen.x + 10, screen.yMax - 40, screen.width - 20, 32), help, _help);
            GUI.matrix = old;
        }

        private void DrawHome(Rect screen)
        {
            GUI.Label(new Rect(screen.x, screen.y + 50, screen.width, 60), Services.TryGet(out ITimeOfDay clock) ? $"{Mathf.FloorToInt(clock.Hour):00}:{Mathf.FloorToInt(clock.Hour % 1f * 60f):00}" : "", _clock);
            GUI.Label(new Rect(screen.x, screen.y + 108, screen.width, 24), _cab == Cab.Coming ? "Cab on the way" : _cab == Cab.Waiting ? "Your cab is outside" : "Off duty", _small);
            const int cols = 2;
            float tile = 86f, gapX = (screen.width - cols * tile) / (cols + 1), top = screen.y + 150f;
            for (int i = 0; i < _apps.Count; i++)
            {
                int cx = i % cols, cy = i / cols;
                var r = new Rect(screen.x + gapX + cx * (tile + gapX), top + cy * (tile + 36f), tile, tile);   // leaves room for the help line
                bool sel = i == _sel;
                if (sel) GUI.DrawTexture(new Rect(r.x - 5, r.y - 5, r.width + 10, r.height + 10), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(1, 1, 1, 0.85f), 0, 26f);
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, _apps[i].Colour, 0, 22f);
                GUI.Label(r, _apps[i].Glyph, _glyph);
                GUI.Label(new Rect(r.x - 20, r.yMax + 4, r.width + 40, 24), _apps[i].Name, _appLabel);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _sel = i; Activate(i); }
            }
        }

        private void DrawApp(Rect screen)
        {
            var head = new Rect(screen.x, screen.y + 36, screen.width, 64);
            GUI.DrawTexture(head, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, _app.Colour * new Color(1, 1, 1, 0.9f), 0, 0f);
            GUI.Label(new Rect(head.x + 18, head.y + 4, head.width - 36, 36), _app.Name, _title);
            GUI.Label(new Rect(head.x + 18, head.y + 36, head.width - 36, 22), _app.Blurb, _small);
            if (GUI.Button(new Rect(head.xMax - 56, head.y + 10, 44, 44), "‹", _title)) Back();

            float y = head.yMax + 10f, rowH = 54f;
            for (int i = 0; _rows != null && i < _rows.Count; i++)
            {
                var r = new Rect(screen.x + 10, y + i * rowH, screen.width - 20, rowH - 6);
                if (r.yMax > screen.yMax - 50) break;
                bool sel = i == _sel && _rows[i].Enabled;
                GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, sel ? new Color(1, 1, 1, 0.18f) : new Color(1, 1, 1, 0.05f), 0, 12f);
                if (i == _sel) GUI.DrawTexture(new Rect(r.x, r.y + 8, 4, r.height - 16), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, _app.Colour, 0, 2f);
                var st = !_rows[i].Enabled && _rows[i].Use != null ? _off : i == _sel ? _rowSel : _row;
                GUI.Label(new Rect(r.x + 14, r.y, r.width * 0.62f, r.height), _rows[i].Label, st);
                GUI.Label(new Rect(r.x + r.width * 0.38f, r.y, r.width * 0.62f - 12, r.height), _rows[i].Value, _value);
                if (_rows[i].Use != null && GUI.Button(r, GUIContent.none, GUIStyle.none)) { _sel = i; Activate(i); }
            }
        }

        private void Styles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(0.85f, 0.88f, 0.94f) } };
            _row = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleLeft, wordWrap = true, normal = { textColor = new Color(0.88f, 0.9f, 0.94f) } };
            _rowSel = new GUIStyle(_row) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _off = new GUIStyle(_row) { normal = { textColor = new Color(0.5f, 0.52f, 0.56f) } };
            _value = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(0.55f, 0.95f, 0.6f) } };
            _glyph = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.08f, 0.08f, 0.1f) } };
            _appLabel = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _clock = new GUIStyle(GUI.skin.label) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _help = new GUIStyle(_small) { fontSize = 12, normal = { textColor = new Color(0.7f, 0.74f, 0.82f) } };
        }
    }
}
