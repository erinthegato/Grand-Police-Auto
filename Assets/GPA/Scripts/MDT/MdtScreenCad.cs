using System;
using System.Collections.Generic;
using GPA.Core;
using GPA.Records;
using UnityEngine;

namespace GPA.Mdt
{
    /// <summary>
    /// The CAD side of the MDT (spec §8): officer dashboard and status, the active call, BOLOs,
    /// citations and dispatch history. Calls come through <see cref="ICadCallSource"/> and the
    /// radio log from <see cref="DispatchLineEvent"/>, so none of this references callout classes.
    /// </summary>
    internal sealed partial class MdtScreen
    {
        private static readonly string[] BoloReasons =
        {
            "Wanted for questioning", "Fled from police", "Stolen vehicle", "Armed and dangerous",
            "Suspect in robbery", "Hit and run", "Reckless driving", "Missing person"
        };

        private string _boloFilter = "", _citationFilter = "";
        private bool _boloShowAll;
        private Bolo _boloDraft; // BOLO being composed (not yet issued)

        private static ICadCallSource Cad => Services.TryGet(out ICadCallSource cad) ? cad : null;

        private static bool HasQuery(Tab tab) =>
            tab == Tab.Persons || tab == Tab.Vehicles || tab == Tab.Warrants || tab == Tab.Bolos || tab == Tab.Citations;

        private int ActiveBoloCount()
        {
            int n = 0;
            foreach (var b in _db.Data.Bolos) if (b.Active) n++;
            return n;
        }

        private OfficerStatus CurrentStatus => _officerStatus != null ? _officerStatus.Status : OfficerStatus.Available;

        private static string StatusColour(OfficerStatus s)
        {
            switch (s)
            {
                case OfficerStatus.Available: return "#5fd16b";
                case OfficerStatus.Busy:
                case OfficerStatus.TrafficStop: return "#ffb347";
                case OfficerStatus.EnRoute:
                case OfficerStatus.OnScene: return "#7fb2ff";
                default: return "#9aa3ad";
            }
        }

        private string StatusText()
        {
            var s = CurrentStatus;
            return $"<color={StatusColour(s)}>● {OfficerStatusCodes.Code(s)} {OfficerStatusCodes.Label(s).ToUpperInvariant()}</color>";
        }

        // ------------------------------------------------------------------ dashboard

        private void DrawDashboardTab()
        {
            var r = _detailRect;
            float w = r.width - 22;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);

            float colW = (w - 24) * 0.42f, rightX = colW + 24, rightW = w - rightX;
            float left = DrawOfficerCard(0, colW);
            float right = DrawActiveCall(rightX, rightW);
            float y = Mathf.Max(left, right) + 10;

            y = DrawRecentRadio(y, w, 6);
            y = DrawCallTypes(y, w);

            _detailHeight = y + 20;
            _nav.PopGroup();
            GUI.EndScrollView();
        }

        private float DrawOfficerCard(float y, float w)
        {
            var status = CurrentStatus;
            GUI.Label(new Rect(0, y, w, 24), "OFFICER", _s.H2);
            y += 30;
            var card = new Rect(0, y, w, 128);
            GUI.DrawTexture(card, _s.TabOff);
            GUI.DrawTexture(new Rect(0, y, 8, card.height), StatusTexture(status));
            GUI.Label(new Rect(24, y + 12, w - 40, 34), $"UNIT {_unit}", _s.H1);
            GUI.Label(new Rect(24, y + 50, w - 40, 34), $"<size=24><b>{StatusText()}</b></size>", _s.Body);
            GUI.Label(new Rect(24, y + 90, w - 40, 26), Escape(_officerLocation), _s.Small);
            y += card.height + 12;

            // The officer's own status choices; activity statuses (en route, stop) follow the game.
            var choices = new[] { OfficerStatus.Available, OfficerStatus.Busy, OfficerStatus.OutOfService };
            float bw = (w - 20) / 3f;
            for (int i = 0; i < choices.Length; i++)
            {
                var c = choices[i];
                bool on = status == c;
                var br = new Rect(i * (bw + 10), y, bw, 52);
                if (Btn(br, $"{OfficerStatusCodes.Code(c)}  {OfficerStatusCodes.Label(c)}", on ? _s.Primary : _s.ButtonStyle, $"dash:st:{i}"))
                {
                    _officerStatus?.Request(c);
                    Status($"Status set to {OfficerStatusCodes.Code(c)} {OfficerStatusCodes.Label(c).ToLowerInvariant()}.", false);
                }
            }
            y += 60;
            string note = status == OfficerStatus.Busy ? "Dispatch holds new calls while you are 10-6."
                : status == OfficerStatus.TrafficStop ? "On a traffic stop: new calls are held until it ends."
                : status == OfficerStatus.OutOfService ? "Off duty. Go 10-8 to start receiving calls."
                : status == OfficerStatus.Available ? "Available: dispatch will offer you calls." : "Status follows your active call.";
            GUI.Label(new Rect(0, y, w, 24), $"<color=#9aa3ad>{note}</color>", _s.Small);
            y += 34;

            // Shift numbers.
            GUI.Label(new Rect(0, y, w, 24), "THIS SHIFT", _s.H2);
            y += 30;
            var cad = Cad;
            int completed = 0, failed = 0;
            if (cad != null) foreach (var h in cad.History) { if (h.Phase == CalloutPhase.Completed) completed++; else failed++; }
            string today = DateTime.Now.ToString("yyyy-MM-dd");
            int cites = 0, arrests = 0, reports = 0;
            foreach (var c in _db.Data.Citations) if (c.Date != null && c.Date.StartsWith(today)) cites++;
            foreach (var a in _db.Data.Arrests) if (a.Date != null && a.Date.StartsWith(today)) arrests++;
            foreach (var rp in _db.Data.Reports) if (rp.Created != null && rp.Created.StartsWith(today)) reports++;
            var stats = new[] { ("Calls cleared", completed.ToString()), ("Calls failed", failed.ToString()), ("Traffic stops", _activity.Stops.Count.ToString()),
                                ("Citations", cites.ToString()), ("Arrests", arrests.ToString()), ("Reports", reports.ToString()) };
            float sw = (w - 20) / 3f;
            for (int i = 0; i < stats.Length; i++)
            {
                var sr = new Rect((i % 3) * (sw + 10), y + (i / 3) * 78, sw, 70);
                GUI.DrawTexture(sr, _s.TabOff);
                GUI.Label(new Rect(sr.x + 14, sr.y + 6, sr.width - 20, 34), $"<size=28><b>{stats[i].Item2}</b></size>", _s.Body);
                GUI.Label(new Rect(sr.x + 14, sr.y + 42, sr.width - 20, 22), stats[i].Item1, _s.Small);
            }
            return y + 2 * 78;
        }

        private Texture2D StatusTexture(OfficerStatus s)
        {
            switch (s)
            {
                case OfficerStatus.Available: return _s.Green;
                case OfficerStatus.Busy:
                case OfficerStatus.TrafficStop: return _s.Amber;
                case OfficerStatus.EnRoute:
                case OfficerStatus.OnScene: return _s.Accent;
                default: return _s.Grey;
            }
        }

        private float DrawActiveCall(float x, float w)
        {
            float y = 0;
            GUI.Label(new Rect(x, y, w, 24), "ACTIVE CALL", _s.H2);
            y += 30;
            var cad = Cad;
            if (cad == null) { GUI.Label(new Rect(x, y, w, 60), "<color=#9aa3ad>Dispatch is not available on this map.</color>", _s.Wrapped); return y + 60; }

            if (!cad.TryGetCurrentCall(out var call))
            {
                var box = new Rect(x, y, w, 128);
                GUI.DrawTexture(box, _s.TabOff);
                GUI.Label(new Rect(x + 24, y + 18, w - 40, 34), "No active call", _s.H1);
                string why = !cad.OnDuty ? "You are off duty." : CurrentStatus == OfficerStatus.Busy ? "Calls are held while you are 10-6 busy." : "Dispatch will send the next call to this terminal.";
                GUI.Label(new Rect(x + 24, y + 62, w - 40, 50), $"<color=#9aa3ad>{why}</color>", _s.Wrapped);
                return y + 140;
            }

            bool offered = call.Phase == CalloutPhase.Offered;
            float detailsH = _s.Wrapped.CalcHeight(new GUIContent(Escape(call.Details)), w - 48);
            var card = new Rect(x, y, w, 174 + detailsH);
            GUI.DrawTexture(card, _s.TabOff);
            GUI.DrawTexture(new Rect(x, y, 8, card.height), offered ? _s.Amber : _s.Accent);
            string phase = offered ? $"<color=#ffb347><b>INCOMING · {Mathf.CeilToInt(call.OfferSecondsLeft)}s</b></color>"
                : call.Phase == CalloutPhase.EnRoute ? "<color=#7fb2ff><b>EN ROUTE</b></color>" : "<color=#7fb2ff><b>ON SCENE</b></color>";
            GUI.Label(new Rect(x + 24, y + 12, w - 40, 26), $"{phase}   <color=#ffcc44>{call.Code}</color>   {call.ResponseCode}   <color=#9aa3ad>dispatched {call.Time}</color>", _s.Body);
            GUI.Label(new Rect(x + 24, y + 42, w - 40, 36), Escape(call.Title), _s.H1);
            GUI.Label(new Rect(x + 24, y + 82, w - 40, 26), Escape(call.Location), _s.Body);
            GUI.Label(new Rect(x + 24, y + 116, w - 48, detailsH), Escape(call.Details), _s.Wrapped);
            y += card.height + 12;

            float bw = (w - 10) / 2f;
            if (offered)
            {
                if (Btn(new Rect(x, y, bw, 54), "Accept call", _s.Primary, "dash:accept") && cad.AcceptOffer()) Status("Call accepted. You're en route.", false);
                if (Btn(new Rect(x + bw + 10, y, bw, 54), "Decline", _s.ButtonStyle, "dash:decline") && cad.DeclineOffer()) Status("Call declined.", false);
            }
            else
            {
                if (Btn(new Rect(x, y, bw, 54), "Write report on this call", _s.Primary, "dash:report"))
                {
                    if (NewReport(type: ReportTypeFor(call.Title), location: call.Location)) _draft.Title = call.Title;
                }
                if (Btn(new Rect(x + bw + 10, y, bw, 54), "Clear call", _s.ButtonStyle, "dash:clear") && cad.ClearCall()) Status("Call cleared.", false);
            }
            return y + 62;
        }

        /// <summary>Best report type for a call title ("Suspicious Person" → "Suspicious Activity").</summary>
        private static string ReportTypeFor(string title)
        {
            if (string.IsNullOrEmpty(title)) return ReportTypes[0];
            foreach (string t in ReportTypes)
                if (t != "General" && t != "Other" && title.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return t;
            foreach (string t in ReportTypes)
            {
                string first = t.Split(' ')[0];
                if (t != "General" && t != "Other" && title.IndexOf(first, StringComparison.OrdinalIgnoreCase) >= 0) return t;
            }
            return ReportTypes[0];
        }

        private float DrawRecentRadio(float y, float w, int count)
        {
            y = Section(y, w, "RECENT RADIO TRAFFIC");
            var radio = _activity.Radio;
            if (radio.Count == 0) return Line(y, w, "<color=#9aa3ad>Nothing on the radio yet.</color>");
            for (int i = radio.Count - 1; i >= 0 && i >= radio.Count - count; i--) y = RadioLineRow(y, w, radio[i]);
            return y;
        }

        private float RadioLineRow(float y, float w, RadioLine line)
        {
            string text = $"<color=#9aa3ad>{line.Time:HH:mm:ss}</color>   {PriorityTag(line.Priority)}   {Escape(line.Text)}";
            float h = Mathf.Max(LineHeight, _s.Wrapped.CalcHeight(new GUIContent(text), w));
            GUI.Label(new Rect(0, y, w, h), text, _s.Wrapped);
            return y + h + 4;
        }

        private static string PriorityTag(TransmissionPriority p)
        {
            switch (p)
            {
                case TransmissionPriority.Emergency: return "<color=#ff6b6b><b>EMERGENCY</b></color>";
                case TransmissionPriority.Critical: return "<color=#ffb347><b>CRITICAL</b></color>";
                case TransmissionPriority.Callout: return "<color=#7fb2ff><b>CALL</b></color>";
                case TransmissionPriority.Background: return "<color=#6c7480>BKG</color>";
                default: return "<color=#9aa3ad>ROUTINE</color>";
            }
        }

        private float DrawCallTypes(float y, float w)
        {
            var cad = Cad;
            if (cad == null) return y;
            var types = cad.CallTypes;
            y = Section(y, w, $"CALL TYPES DISPATCH CAN ASSIGN ({types.Count})");
            foreach (var t in types)
                y = Line(y, w, $"<color=#ffcc44>{t.Code}</color>   <b>{Escape(t.Title)}</b>   <color=#9aa3ad>{t.ResponseCode} · {Escape(t.Description)}</color>");
            return y;
        }

        // ------------------------------------------------------------------ BOLOs

        private List<Bolo> FilteredBolos()
        {
            var list = new List<Bolo>();
            string q = _boloFilter.Trim();
            for (int i = _db.Data.Bolos.Count - 1; i >= 0; i--)
            {
                var b = _db.Data.Bolos[i];
                if (!_boloShowAll && !b.Active) continue;
                if (q.Length > 0 && (b.Subject ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (b.Reason ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && !b.Id.Equals(q, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(b);
            }
            return list;
        }

        private void StartBolo(Citizen person, VehicleRecord vehicle)
        {
            _kb.Close();
            string subject = vehicle != null
                ? $"{vehicle.Description}, plate {vehicle.Plate}"
                : person != null ? $"{person.FullName}, {(person.Sex == "F" ? "female" : "male")}, DOB {person.DateOfBirth}, {person.HeightCm} cm, {person.Hair.ToLowerInvariant()} hair" : "";
            _boloDraft = new Bolo { Subject = subject, Reason = "", VehicleId = vehicle?.Id, CitizenId = person?.Id };
            SelectTab(Tab.Bolos);
            _detailScroll = Vector2.zero;
        }

        private void IssueBolo()
        {
            _kb.Close();
            if (string.IsNullOrWhiteSpace(_boloDraft.Subject) || string.IsNullOrWhiteSpace(_boloDraft.Reason))
            {
                Status("A BOLO needs a subject and a reason.", true);
                return;
            }
            var bolo = _db.AddBolo(_boloDraft.Subject.Trim(), _boloDraft.Reason.Trim(), _boloDraft.VehicleId, _boloDraft.CitizenId);
            if (Services.TryGet(out IDispatchRadio radio))
                radio.Transmit(TransmissionPriority.Callout, $"Attention all units, BOLO: {bolo.Subject}. {bolo.Reason}.");
            _boloDraft = null;
            _boloShowAll = false;
            _boloFilter = "";
            _sel[(int)Tab.Bolos] = 0;
            Status($"{bolo.Id} issued and broadcast.", false);
        }

        private void DrawBolosTab()
        {
            var list = _listRect;
            if (TextBox(new Rect(list.x, list.y, list.width - 130, 48), "field:bf", _boloFilter, "Filter BOLOs…")) OpenQueryKeyboard();
            if (Btn(new Rect(list.xMax - 120, list.y, 120, 48), _boloShowAll ? "All" : "Active", _s.ButtonStyle, "bolo:showall")) { _boloShowAll = !_boloShowAll; _sel[(int)Tab.Bolos] = 0; }
            if (Btn(new Rect(list.x, list.y + 56, list.width, 48), "+  New BOLO", _s.Primary, "bolo:new")) StartBolo(null, null);
            var bolos = FilteredBolos();
            GUI.Label(new Rect(list.x, list.y + 112, list.width, 24), $"{bolos.Count} {(_boloShowAll ? "BOLOS ON FILE" : "ACTIVE BOLOS")}", _s.H2);
            var rows = new Rect(list.x, list.y + 140, list.width, list.height - 140);
            if (bolos.Count == 0) Empty(rows, _boloShowAll ? "No BOLOs on file." : "No active BOLOs. Issue one from a person or vehicle record, or with New BOLO.");
            else
            {
                _sel[(int)Tab.Bolos] = Mathf.Clamp(_sel[(int)Tab.Bolos], 0, bolos.Count - 1);
                DrawList(rows, bolos, ReportRowHeight, b =>
                    $"<b>{Escape(b.Subject)}</b>\n<size=15><color=#9aa3ad>{b.Id} · {Escape(b.Reason)} · {b.Created}</color>{(b.Active ? "" : "  <color=#ff6b6b>cancelled</color>")}</size>",
                    i => _boloDraft = null);
            }

            if (_boloDraft != null) { DrawBoloComposer(); return; }
            if (bolos.Count == 0) { Empty(_detailRect, "No BOLO selected."); return; }
            DrawBolo(bolos[_sel[(int)Tab.Bolos]]);
        }

        private void DrawBolo(Bolo b)
        {
            var r = _detailRect;
            float w = r.width - 22;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);
            float y = 0;
            GUI.Label(new Rect(0, y, w, 36), $"BOLO {b.Id}", _s.H1);
            GUI.Label(new Rect(0, y + 34, w, 24), $"Issued {b.Created}", _s.Small);
            y += 66;
            y = Banner(y, w, b.Active ? _s.Amber : _s.Grey, b.Active ? "ACTIVE  —  be on the lookout" : "CANCELLED");

            y = Section(y, w, "SUBJECT");
            float sh = _s.Wrapped.CalcHeight(new GUIContent(Escape(b.Subject)), w);
            GUI.Label(new Rect(0, y, w, sh), Escape(b.Subject), _s.Wrapped);
            y += sh + 6;
            y = Section(y, w, "REASON");
            y = Line(y, w, Escape(b.Reason));

            var v = _db.GetVehicle(b.VehicleId);
            var c = _db.GetCitizen(b.CitizenId);
            if (v != null || c != null)
            {
                y = Section(y, w, "LINKED RECORDS");
                if (v != null && LinkButton(ref y, w, $"<b>{v.Plate}</b>   {v.Description}{(v.Stolen ? "   <color=#ff6b6b><b>STOLEN</b></color>" : "")}", "bolo:veh")) ShowVehicle(v);
                if (c != null && LinkButton(ref y, w, $"<b>{c.LastName.ToUpperInvariant()}, {c.FirstName}</b>   {c.Id}{(c.Wanted ? "   <color=#ff6b6b><b>WANTED</b></color>" : "")}", "bolo:person")) ShowPerson(c);
            }

            y += 16;
            if (Btn(new Rect(0, y, 240, 52), b.Active ? "Cancel BOLO" : "Reinstate BOLO", b.Active ? _s.ButtonStyle : _s.Primary, "bolo:toggle"))
            {
                _db.SetBoloActive(b, !b.Active);
                if (Services.TryGet(out IDispatchRadio radio))
                    radio.Transmit(TransmissionPriority.Routine, b.Active ? $"All units, {b.Id} is back in effect." : $"All units, cancel {b.Id}.");
                Status(b.Active ? $"{b.Id} reinstated." : $"{b.Id} cancelled.", false);
            }
            if (Btn(new Rect(252, y, 300, 52), "New report on this BOLO", _s.ButtonStyle, "bolo:report"))
            {
                if (NewReport(b.CitizenId, b.VehicleId, b.VehicleId != null && _db.GetVehicle(b.VehicleId)?.Stolen == true ? "Stolen Vehicle" : null))
                    _draft.Title = $"BOLO {b.Id} - {b.Reason}";
            }
            y += 70;

            _detailHeight = y;
            _nav.PopGroup();
            GUI.EndScrollView();
        }

        private void DrawBoloComposer()
        {
            var r = _detailRect;
            float w = r.width - 22, lx = 150f, fw = w - lx;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);
            float y = 0;
            GUI.Label(new Rect(0, y, w, 36), "NEW BOLO", _s.H1);
            GUI.Label(new Rect(0, y + 34, w, 24), "Issuing broadcasts it to all units on the dispatch channel.", _s.Small);
            y += 70;

            GUI.Label(new Rect(0, y + 13, lx, 28), "Subject", _s.Label);
            float sh = Mathf.Max(56f, _s.Wrapped.CalcHeight(new GUIContent(Escape(_boloDraft.Subject) + "▌"), fw - 24) + 20f);
            if (TextBox(new Rect(lx, y, fw, sh), "bolo:subject", _boloDraft.Subject, "Who or what to look for", multiline: true))
                OpenKeyboard("bolo:subject", "BOLO subject", () => _boloDraft.Subject, v => _boloDraft.Subject = v, 200, false, autoCap: true);
            y += sh + 10;

            GUI.Label(new Rect(0, y + 13, lx, 28), "Reason", _s.Label);
            if (TextBox(new Rect(lx, y, fw, 48), "bolo:reason", _boloDraft.Reason, "Tap a reason below, or type one"))
                OpenKeyboard("bolo:reason", "BOLO reason", () => _boloDraft.Reason, v => _boloDraft.Reason = v, 120, false, autoCap: true);
            y += 58;
            float cw = (fw - 30) / 4f;
            for (int i = 0; i < BoloReasons.Length; i++)
            {
                var cr = new Rect(lx + (i % 4) * (cw + 10), y + (i / 4) * 52, cw, 46);
                if (Btn(cr, BoloReasons[i], _boloDraft.Reason == BoloReasons[i] ? _s.Primary : _s.ChipStyle, $"bolo:reason:{i}")) _boloDraft.Reason = BoloReasons[i];
            }
            y += ((BoloReasons.Length + 3) / 4) * 52 + 10;

            var v = _db.GetVehicle(_boloDraft.VehicleId);
            var c = _db.GetCitizen(_boloDraft.CitizenId);
            y = Section(y, w, "LINKED RECORD");
            if (v != null) y = Line(y, w, $"<b>{v.Plate}</b>   {v.Description}");
            else if (c != null) y = Line(y, w, $"<b>{c.LastName.ToUpperInvariant()}, {c.FirstName}</b>   {c.Id}");
            else y = Line(y, w, "<color=#9aa3ad>None — issue a BOLO from a person or vehicle record to link it.</color>");

            y += 16;
            if (Btn(new Rect(0, y, 240, 52), "Issue BOLO", _s.Primary, "bolo:issue")) IssueBolo();
            if (Btn(new Rect(252, y, 200, 52), "Discard", _s.ButtonStyle, "bolo:discard")) { _kb.Close(); _boloDraft = null; Status("BOLO discarded.", false); }
            y += 70;

            _detailHeight = y;
            _nav.PopGroup();
            GUI.EndScrollView();
        }

        // ------------------------------------------------------------------ citations

        private List<Citation> FilteredCitations()
        {
            var list = new List<Citation>();
            string q = _citationFilter.Trim();
            for (int i = _db.Data.Citations.Count - 1; i >= 0; i--)
            {
                var ct = _db.Data.Citations[i];
                if (q.Length > 0)
                {
                    var c = _db.GetCitizen(ct.CitizenId);
                    var v = _db.GetVehicle(ct.VehicleId);
                    bool hit = ct.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (ct.Offense ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (c != null && c.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (v != null && v.Plate.Replace("-", "").IndexOf(q.Replace("-", ""), StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!hit) continue;
                }
                list.Add(ct);
            }
            return list;
        }

        private void DrawCitationsTab()
        {
            var list = _listRect;
            if (TextBox(new Rect(list.x, list.y, list.width, 48), "field:cf", _citationFilter, "Filter by name, plate, offense…")) OpenQueryKeyboard();
            var citations = FilteredCitations();
            GUI.Label(new Rect(list.x, list.y + 54, list.width, 24), $"{citations.Count} OF {_db.Data.Citations.Count} CITATIONS", _s.H2);
            var rows = new Rect(list.x, list.y + 82, list.width, list.height - 82);
            if (citations.Count == 0) { Empty(rows, _db.Data.Citations.Count == 0 ? "No citations written yet." : "No citations match."); Empty(_detailRect, "No citation selected."); return; }
            _sel[(int)Tab.Citations] = Mathf.Clamp(_sel[(int)Tab.Citations], 0, citations.Count - 1);
            DrawList(rows, citations, ReportRowHeight, ct =>
            {
                var c = _db.GetCitizen(ct.CitizenId);
                string who = c != null ? $"{c.LastName.ToUpperInvariant()}, {c.FirstName}" : ct.CitizenId;
                return $"<b>{Escape(ct.Offense)}</b>   ${ct.Fine}\n<size=15><color=#9aa3ad>{ct.Id} · {who} · {ct.Date}</color></size>";
            });
            DrawCitation(citations[_sel[(int)Tab.Citations]]);
        }

        private void DrawCitation(Citation ct)
        {
            var r = _detailRect;
            float w = r.width - 22;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);
            float y = 0;
            GUI.Label(new Rect(0, y, w, 36), $"CITATION {ct.Id}", _s.H1);
            GUI.Label(new Rect(0, y + 34, w, 24), $"Written {ct.Date} by {ct.Officer}", _s.Small);
            y += 66;

            y = Section(y, w, "VIOLATION");
            y = Field(y, w, "Offense", Escape(ct.Offense));
            y = Field(y, w, "Fine", $"${ct.Fine}");
            y = Field(y, w, "Location", Escape(ct.Location));
            y = Field(y, w, "Date / time", ct.Date);
            y = Field(y, w, "Officer", ct.Officer);

            var c = _db.GetCitizen(ct.CitizenId);
            var v = _db.GetVehicle(ct.VehicleId);
            y = Section(y, w, "CITED PERSON");
            if (c == null) y = Line(y, w, "<color=#9aa3ad>Unknown</color>");
            else if (LinkButton(ref y, w, $"<b>{c.LastName.ToUpperInvariant()}, {c.FirstName}</b>   {c.Id} · DOB {c.DateOfBirth}{(c.Wanted ? "   <color=#ff6b6b><b>WANTED</b></color>" : "")}", "cit:person")) ShowPerson(c);
            y = Section(y, w, "VEHICLE");
            if (v == null) y = Line(y, w, "<color=#9aa3ad>None</color>");
            else if (LinkButton(ref y, w, $"<b>{v.Plate}</b>   {v.Description}", "cit:veh")) ShowVehicle(v);

            y += 16;
            if (Btn(new Rect(0, y, 320, 52), "New report on this citation", _s.Primary, "cit:report"))
            {
                if (NewReport(ct.CitizenId, ct.VehicleId, ct.VehicleId != null ? "Traffic Stop" : null, ct.Location)) Autofill();
            }
            y += 70;

            _detailHeight = y;
            _nav.PopGroup();
            GUI.EndScrollView();
        }

        // ------------------------------------------------------------------ dispatch history

        private List<CadCall> CallHistory()
        {
            var list = new List<CadCall>();
            var cad = Cad;
            if (cad != null) for (int i = cad.History.Count - 1; i >= 0; i--) list.Add(cad.History[i]);
            return list;
        }

        private void DrawDispatchTab()
        {
            var list = _listRect;
            var calls = CallHistory();
            GUI.Label(new Rect(list.x, list.y + 4, list.width, 24), $"CALLS THIS SHIFT ({calls.Count})", _s.H2);
            var rows = new Rect(list.x, list.y + 34, list.width, list.height - 34);
            if (calls.Count == 0) Empty(rows, "No calls closed yet this shift.");
            else
            {
                _sel[(int)Tab.Dispatch] = Mathf.Clamp(_sel[(int)Tab.Dispatch], 0, calls.Count - 1);
                DrawList(rows, calls, ReportRowHeight, c =>
                    $"<color=#ffcc44>{c.Code}</color>  <b>{Escape(c.Title)}</b>\n<size=15><color=#9aa3ad>{c.Time} · </color>{(c.Phase == CalloutPhase.Completed ? "<color=#5fd16b>Code 4</color>" : "<color=#ffb347>cleared</color>")}<color=#9aa3ad> · {Escape(c.Location)}</color></size>");
            }

            var r = _detailRect;
            float w = r.width - 22;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);
            float y = 0;
            if (calls.Count > 0)
            {
                var c = calls[_sel[(int)Tab.Dispatch]];
                GUI.Label(new Rect(0, y, w, 36), Escape(c.Title), _s.H1);
                GUI.Label(new Rect(0, y + 34, w, 24), $"{c.Code} · {c.ResponseCode} · closed {c.Time}", _s.Small);
                y += 66;
                y = Field(y, w, "Location", Escape(c.Location));
                y = Field(y, w, "Disposition", c.Phase == CalloutPhase.Completed ? "<color=#5fd16b>Completed (Code 4)</color>" : "<color=#ffb347>Cleared without completion</color>");
                float dh = _s.Wrapped.CalcHeight(new GUIContent(Escape(c.Details)), w - 220);
                GUI.Label(new Rect(0, y, 210, LineHeight), "Result", _s.Label);
                GUI.Label(new Rect(220, y, w - 220, dh), Escape(c.Details), _s.Wrapped);
                y += Mathf.Max(LineHeight, dh) + 4;
                if (Btn(new Rect(0, y + 8, 300, 48), "Write report on this call", _s.ButtonStyle, "disp:report"))
                {
                    if (NewReport(type: ReportTypeFor(c.Title), location: c.Location)) _draft.Title = c.Title;
                }
                y += 64;
            }

            var radio = _activity.Radio;
            y = Section(y, w, $"RADIO LOG ({radio.Count})");
            if (radio.Count == 0) y = Line(y, w, "<color=#9aa3ad>Nothing on the radio yet.</color>");
            for (int i = radio.Count - 1; i >= 0; i--) y = RadioLineRow(y, w, radio[i]);

            _detailHeight = y + 20;
            _nav.PopGroup();
            GUI.EndScrollView();
        }
    }
}
