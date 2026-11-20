using System;
using System.Collections.Generic;
using GPA.Core;
using GPA.Records;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Mdt
{
    /// <summary>
    /// Arrests tab: every arrest on file (newest first, filterable), and the arrest record itself —
    /// booking photo, booking number, case status, each count with its class and bail, where and by
    /// whom, the call it came from, and the arrest report written at booking.
    /// </summary>
    internal sealed partial class MdtScreen
    {
        private string _arrestFilter = "";

        private static string BookingNo(Arrest a) => !string.IsNullOrEmpty(a.BookingNumber) ? a.BookingNumber : RecordsDatabase.BookingNumberFor(a);

        /// <summary>The counts on an arrest; older records (before counts were stored) are classed on the fly.</summary>
        private List<ArrestCount> CountsOf(Arrest a, out int totalBail) => ArrestReports.CountsOf(_db, a, out totalBail);

        private enum CaseStage { InCustody, Arraigned, Held, Released }

        /// <summary>Where the case is now, from the in-game time since booking.</summary>
        private CaseStage StageOf(Arrest a, int totalBail, bool felony)
        {
            if (!DateTime.TryParse(a.Date, out var booked)) return CaseStage.InCustody;
            double hours = (GameClock.Now - booked).TotalHours;
            if (hours < 0) return CaseStage.Released;   // stamped with the real clock before records used game time: long since dealt with
            if (hours < 24) return CaseStage.InCustody;
            if (felony && totalBail >= 100000) return CaseStage.Held;   // high bail: stays in jail until trial
            return hours < 72 ? CaseStage.Arraigned : CaseStage.Released;
        }

        private string StageText(CaseStage stage, int bail)
        {
            switch (stage)
            {
                case CaseStage.InCustody: return "IN CUSTODY — awaiting arraignment";
                case CaseStage.Arraigned: return $"ARRAIGNED — bail set at ${bail:N0}";
                case CaseStage.Held: return $"HELD — bail ${bail:N0} not posted, awaiting trial";
                default: return "RELEASED ON BAIL — awaiting trial";
            }
        }

        private static string StageShort(CaseStage stage)
        {
            switch (stage)
            {
                case CaseStage.InCustody: return "<color=#ffb347>in custody</color>";
                case CaseStage.Arraigned: return "<color=#ffcc44>arraigned</color>";
                case CaseStage.Held: return "<color=#ff6b6b>held</color>";
                default: return "<color=#5fd16b>released on bail</color>";
            }
        }

        private Texture2D StageColour(CaseStage stage) =>
            stage == CaseStage.Released ? _s.Green : stage == CaseStage.Held ? _s.Red : _s.Amber;

        private static bool AnyFelony(List<ArrestCount> counts) { foreach (var c in counts) if (c.Felony) return true; return false; }

        private List<Arrest> FilteredArrests()
        {
            var list = new List<Arrest>();
            string q = _arrestFilter.Trim();
            for (int i = _db.Data.Arrests.Count - 1; i >= 0; i--)
            {
                var a = _db.Data.Arrests[i];
                if (q.Length > 0)
                {
                    var c = _db.GetCitizen(a.CitizenId);
                    bool hit = (a.Id ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               BookingNo(a).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               (c != null && c.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                               (a.Location ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                               a.Charges.Exists(ch => ch.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!hit) continue;
                }
                list.Add(a);
            }
            return list;
        }

        /// <summary>Opens the Arrests tab on this arrest.</summary>
        private void ShowArrest(Arrest a)
        {
            _arrestFilter = "";
            SelectTab(Tab.Arrests);
            var list = FilteredArrests();
            _sel[(int)Tab.Arrests] = Mathf.Max(0, list.IndexOf(a));
            _listScroll[(int)Tab.Arrests] = new Vector2(0f, Mathf.Max(0, _sel[(int)Tab.Arrests] - 3) * ReportRowHeight);
            _detailScroll = Vector2.zero;
        }

        private void DrawArrestsTab()
        {
            var list = _listRect;
            if (TextBox(new Rect(list.x, list.y, list.width, 48), "field:af", _arrestFilter, "Filter by name, booking no., charge…")) OpenQueryKeyboard();
            var arrests = FilteredArrests();
            GUI.Label(new Rect(list.x, list.y + 54, list.width, 24), $"{arrests.Count} OF {_db.Data.Arrests.Count} ARRESTS", _s.H2);
            var rows = new Rect(list.x, list.y + 82, list.width, list.height - 82);
            if (arrests.Count == 0)
            {
                Empty(rows, _db.Data.Arrests.Count == 0 ? "No arrests booked yet. Cuff a suspect, put them in your back seat and book them at the precinct." : "No arrests match.");
                Empty(_detailRect, "No arrest selected.");
                return;
            }
            _sel[(int)Tab.Arrests] = Mathf.Clamp(_sel[(int)Tab.Arrests], 0, arrests.Count - 1);
            DrawList(rows, arrests, ReportRowHeight, a =>
            {
                var c = _db.GetCitizen(a.CitizenId);
                string who = c != null ? $"{c.LastName.ToUpperInvariant()}, {c.FirstName}" : "UNIDENTIFIED";
                var counts = CountsOf(a, out int bail);
                bool felony = AnyFelony(counts);
                string cls = felony ? "<color=#ff6b6b>felony</color>" : "<color=#9aa3ad>misd.</color>";
                return $"<b>{Escape(who)}</b>   {counts.Count} count{(counts.Count == 1 ? "" : "s")} · {cls}\n" +
                       $"<size=15><color=#9aa3ad>{BookingNo(a)} · {a.Date} · </color>{StageShort(StageOf(a, bail, felony))}</size>";
            });
            DrawArrest(arrests[_sel[(int)Tab.Arrests]]);
        }

        private void DrawArrest(Arrest a)
        {
            var r = _detailRect;
            float w = r.width - 22;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);
            var c = _db.GetCitizen(a.CitizenId);
            var counts = CountsOf(a, out int bail);
            bool felony = AnyFelony(counts);
            CaseStage stage = StageOf(a, bail, felony);

            // Header beside the booking photo.
            const float photoW = 190f, photoH = 236f;
            float textW = w - photoW - 20f;
            float y = 0;
            GUI.Label(new Rect(0, y, textW, 36), $"ARREST {BookingNo(a)}", _s.H1);
            GUI.Label(new Rect(0, y + 34, textW, 24), $"Booked {a.Date}{(string.IsNullOrEmpty(a.BookedAt) ? "" : " at " + Escape(a.BookedAt))}", _s.Small);
            DrawMugshot(new Rect(w - photoW, 0, photoW, photoH), c, BookingNo(a), a.Date);
            y += 66;
            y = BannerW(y, textW, StageColour(stage), StageText(stage, bail));
            y = FieldW(y, textW, "Name", c != null ? $"<b>{Escape(c.LastName.ToUpperInvariant())}, {Escape(c.FirstName)}</b>" : "Unidentified");
            if (c != null)
            {
                y = FieldW(y, textW, "DOB / age", $"{c.DateOfBirth} · {Age(c.DateOfBirth)}");
                y = FieldW(y, textW, "Sex / height", $"{c.Sex} · {c.HeightCm} cm · {c.WeightKg} kg");
                y = FieldW(y, textW, "Hair / eyes", $"{Escape(c.Hair)} · {Escape(c.Eyes)}");
            }
            y = Mathf.Max(y, photoH + 8f);

            if (c != null && LinkButton(ref y, w, $"<b>{c.LastName.ToUpperInvariant()}, {c.FirstName}</b>   {c.Id} · {Escape(c.Address)}{(c.Wanted ? "   <color=#ff6b6b><b>WANTED</b></color>" : "")}   · open full record", "arr:person"))
            {
                _nav.PopGroup();
                GUI.EndScrollView();
                ShowPerson(c);
                return;
            }

            // Counts.
            y = Section(y, w, $"CHARGES ({counts.Count} count{(counts.Count == 1 ? "" : "s")})");
            for (int i = 0; i < counts.Count; i++)
            {
                var n = counts[i];
                string cls = n.Felony ? "<color=#ff6b6b><b>FELONY</b></color>" : "<color=#ffcc44>MISDEMEANOR</color>";
                GUI.Label(new Rect(0, y, 40, LineHeight), $"{i + 1}.", _s.Label);
                GUI.Label(new Rect(40, y, w - 360, LineHeight), Escape(n.Charge), _s.Body);
                GUI.Label(new Rect(w - 320, y, 170, LineHeight), cls, _s.Body);
                GUI.Label(new Rect(w - 150, y, 150, LineHeight), $"${n.Bail:N0}", GuiStyleCache.With(_s.Body, TextAnchor.UpperRight));
                y += LineHeight;
            }
            GUI.DrawTexture(new Rect(w - 320, y + 2, 320, 1), _s.Divider);
            GUI.Label(new Rect(w - 320, y + 6, 170, LineHeight), "<b>Total bail</b>", _s.Body);
            GUI.Label(new Rect(w - 150, y + 6, 150, LineHeight), $"<b>${bail:N0}</b>", GuiStyleCache.With(_s.Body, TextAnchor.UpperRight));
            y += LineHeight + 10;

            // Arrest details.
            y = Section(y, w, "ARREST");
            y = Field(y, w, "Arrested at", Escape(a.Location));
            y = Field(y, w, "Booked", $"{a.Date}{(string.IsNullOrEmpty(a.BookedAt) ? "" : " · " + Escape(a.BookedAt))}");
            y = Field(y, w, "Arresting officer", $"{Escape(a.Officer)}{(string.IsNullOrEmpty(a.Unit) ? "" : $" · Unit {a.Unit}")}");
            y = Field(y, w, "Call", string.IsNullOrEmpty(a.CallTitle) ? "<color=#9aa3ad>Officer-initiated (no call)</color>" : $"<color=#ffcc44>{Escape(a.CallCode)}</color>  {Escape(a.CallTitle)}");
            y = Field(y, w, "Arrest no.", a.Id);

            // Report.
            y = Section(y, w, "ARREST REPORT");
            var report = FindReport(a.ReportId);
            if (report != null)
            {
                if (LinkButton(ref y, w, $"<b>{report.Id}</b>   {Escape(report.Title)}   <color=#9aa3ad>{report.Type} · {report.Updated}</color>", "arr:report"))
                {
                    _nav.PopGroup();
                    GUI.EndScrollView();
                    SelectTab(Tab.Reports);
                    LoadReport(report);
                    return;
                }
            }
            else
            {
                y = Line(y, w, "<color=#9aa3ad>No report on file for this arrest.</color>");
                if (Btn(new Rect(0, y + 6, 320, 52), "Write arrest report", _s.Primary, "arr:write"))
                {
                    var rpt = WriteArrestReport(a);
                    if (rpt != null) Status($"Report {rpt.Id} filed for {BookingNo(a)}.", false);
                }
                y += 70;
            }

            _detailHeight = y + 20;
            _nav.PopGroup();
            GUI.EndScrollView();
        }

        private IncidentReport FindReport(string id) => string.IsNullOrEmpty(id) ? null : _db.Data.Reports.Find(r => r.Id == id);

        // Narrower versions of Field / Banner for the rows beside the booking photo.
        private float FieldW(float y, float w, string label, string value)
        {
            GUI.Label(new Rect(0, y, 170, LineHeight), label, _s.Label);
            GUI.Label(new Rect(176, y, w - 176, LineHeight), value ?? "", _s.Body);
            return y + LineHeight;
        }

        private float BannerW(float y, float w, Texture2D colour, string text)
        {
            GUI.DrawTexture(new Rect(0, y, w, 38), colour);
            GUI.Label(new Rect(14, y + 6, w - 20, 28), text, _s.BannerText);
            return y + 48;
        }

        private static Texture2D _photoBg, _photoLine, _plate;

        /// <summary>
        /// The booking photo: the person's own sprite against a height chart, with the booking
        /// placard underneath. (The game is top-down, so the "photo" is how they look on the street.)
        /// </summary>
        private void DrawMugshot(Rect r, Citizen c, string bookingNo, string date)
        {
            if (_photoBg == null)
            {
                Texture2D Solid(Color col) { var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; t.SetPixel(0, 0, col); t.Apply(); return t; }
                _photoBg = Solid(new Color(0.78f, 0.8f, 0.82f));
                _photoLine = Solid(new Color(0.55f, 0.58f, 0.62f));
                _plate = Solid(new Color(0.08f, 0.08f, 0.1f));
            }
            var photo = new Rect(r.x, r.y, r.width, r.height - 54f);
            GUI.DrawTexture(new Rect(r.x - 3, r.y - 3, r.width + 6, r.height + 6), _s.Divider);
            GUI.DrawTexture(photo, _photoBg);
            for (int i = 1; i < 8; i++)   // height chart
            {
                float ly = photo.y + photo.height * i / 8f;
                GUI.DrawTexture(new Rect(photo.x, ly, i % 2 == 0 ? photo.width : 18f, 1f), _photoLine);
            }

            Sprite sprite = null;
            var sprites = WorldSprites2D.Instance;
            if (c != null && sprites != null && c.LookIndex >= 0 && c.LookIndex < sprites.Civilians.Length) sprite = sprites.Civilians[c.LookIndex];
            if (sprite != null)
            {
                Texture tex = sprite.texture;
                Rect tr = sprite.textureRect;   // packed in an atlas: draw just this sprite
                var uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
                float k = Mathf.Min((photo.width - 40f) / tr.width, (photo.height - 30f) / tr.height);
                float iw = tr.width * k, ih = tr.height * k;
                GUI.DrawTextureWithTexCoords(new Rect(photo.center.x - iw * 0.5f, photo.center.y - ih * 0.5f, iw, ih), tex, uv);
            }
            else GUI.Label(photo, "<color=#555b63><b>NO PHOTO</b></color>", GuiStyleCache.With(_s.Body, TextAnchor.MiddleCenter));

            var plate = new Rect(r.x, photo.yMax, r.width, 54f);
            GUI.DrawTexture(plate, _plate);
            string city = _locations != null ? _locations.CityName.ToUpperInvariant() : "CITY";
            GUI.Label(new Rect(plate.x + 6, plate.y + 2, plate.width - 12, 22), $"<b>{city} PD</b>", GuiStyleCache.With(_s.Small, TextAnchor.MiddleCenter));   // the date is in the header
            GUI.Label(new Rect(plate.x + 6, plate.y + 24, plate.width - 12, 26), $"<b>{bookingNo}</b>", GuiStyleCache.With(_s.Body, TextAnchor.MiddleCenter));
        }

        private IncidentReport WriteArrestReport(Arrest a) => ArrestReports.Write(_db, a);

        /// <summary>After booking on the Booking tab: the newest arrest for this person gets its report.</summary>
        private Arrest WriteReportForLatestArrest(string citizenId, out IncidentReport report)
        {
            report = null;
            if (string.IsNullOrEmpty(citizenId)) return null;
            for (int i = _db.Data.Arrests.Count - 1; i >= 0; i--)
            {
                var a = _db.Data.Arrests[i];
                if (a.CitizenId != citizenId) continue;
                report = FindReport(a.ReportId) ?? WriteArrestReport(a);   // normally already filed when the arrest was recorded
                return a;
            }
            return null;
        }
    }

    /// <summary>Writes arrest reports. Static so the police computer can file one the moment an arrest is recorded.</summary>
    internal static class ArrestReports
    {
        public static List<ArrestCount> CountsOf(RecordsDatabase db, Arrest a, out int totalBail)
        {
            if (a.Counts != null && a.Counts.Count > 0) { totalBail = a.TotalBail; return a.Counts; }
            return ChargeBook.ClassifyAll(a.Charges, db.GetCitizen(a.CitizenId), out totalBail);
        }

        /// <summary>
        /// The arrest report: the standard narrative (who, where, warrants, history, the arrest itself)
        /// plus the booking details, filed and linked both ways. Returns the existing one if already filed.
        /// </summary>
        public static IncidentReport Write(RecordsDatabase db, Arrest a)
        {
            if (db == null || a == null) return null;
            var existing = string.IsNullOrEmpty(a.ReportId) ? null : db.Data.Reports.Find(r => r.Id == a.ReportId);
            if (existing != null) return existing;
            var report = new IncidentReport { Type = "Arrest", Location = a.Location, Officer = a.Officer, ArrestId = a.Id };
            if (!string.IsNullOrEmpty(a.CitizenId)) report.CitizenIds.Add(a.CitizenId);
            report.Title = NarrativeBuilder.BuildTitle(report, db);
            string narrative = NarrativeBuilder.Build(report, db, null);

            var c = db.GetCitizen(a.CitizenId);
            var counts = CountsOf(db, a, out int bail);
            int felonies = 0; foreach (var n in counts) if (n.Felony) felonies++;
            string bookingNo = !string.IsNullOrEmpty(a.BookingNumber) ? a.BookingNumber : RecordsDatabase.BookingNumberFor(a);
            string who = c != null ? c.FullName : "The arrestee";
            string booking = $"{who} was booked{(string.IsNullOrEmpty(a.BookedAt) ? "" : $" at {a.BookedAt}")} under booking number {bookingNo} " +
                             $"on {counts.Count} count{(counts.Count == 1 ? "" : "s")}" +
                             (felonies > 0 ? $" ({felonies} felon{(felonies == 1 ? "y" : "ies")})" : "") +
                             $". Bail was set at ${bail:N0}.";
            if (!string.IsNullOrEmpty(a.CallTitle)) booking = $"This arrest was made while handling call {a.CallCode} ({a.CallTitle}). " + booking;
            report.Narrative = NarrativeBuilder.Append(narrative, booking);

            db.SaveReport(report);
            db.LinkArrestReport(a, report);
            return report;
        }
    }
}
