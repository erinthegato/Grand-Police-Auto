using System.Collections.Generic;
using GPA.Core;
using UnityEngine;

namespace GPA.Mdt
{
    /// <summary>
    /// Booking tab: prisoners in the back of a car parked at the precinct, with their charges as
    /// toggles, then Book or Release. Talks to the arrest pipeline only through <see cref="IBookingDesk"/>.
    /// Pressing E / A "Book prisoner" at the precinct opens the computer straight on this tab.
    /// </summary>
    internal sealed partial class MdtScreen
    {
        private static IBookingDesk Desk => Services.TryGet(out IBookingDesk desk) ? desk : null;

        public void ShowPage(string page)
        {
            if (page == MdtPages.Booking) SelectTab(Tab.Booking);
        }

        private string BookingTabLabel()
        {
            int n = Desk != null ? Desk.Pending.Count : 0;
            return n > 0 ? $"<color=#ffcc44>BOOKING ({n})</color>" : "BOOKING";
        }

        private void DrawBookingTab()
        {
            var list = _listRect;
            IBookingDesk desk = Desk;
            IReadOnlyList<BookingCase> pending = desk != null ? desk.Pending : new List<BookingCase>();
            GUI.Label(new Rect(list.x, list.y + 4, list.width, 24), $"AWAITING BOOKING ({pending.Count})", _s.H2);
            var rows = new Rect(list.x, list.y + 34, list.width, list.height - 34);
            if (pending.Count == 0)
            {
                Empty(rows, "No prisoners waiting. Put a suspect in your back seat and park outside the precinct.");
                Empty(_detailRect, "Nobody to book.");
                return;
            }
            var cases = new List<BookingCase>(pending);
            _sel[(int)Tab.Booking] = Mathf.Clamp(_sel[(int)Tab.Booking], 0, cases.Count - 1);
            DrawList(rows, cases, ReportRowHeight, b =>
                $"<b>{Escape(b.Name)}</b>   <color=#ffcc44>{b.Selected.Count} charge(s)</color>\n<size=15><color=#9aa3ad>Arrested at {Escape(b.ArrestLocation)}</color></size>");
            DrawBooking(desk, cases[_sel[(int)Tab.Booking]]);
        }

        private void DrawBooking(IBookingDesk desk, BookingCase b)
        {
            var r = _detailRect;
            float w = r.width - 22;
            _detailScroll = GUI.BeginScrollView(r, _detailScroll, new Rect(0, 0, w, Mathf.Max(_detailHeight, r.height)));
            _nav.PushGroup(DetailGroup, r, _detailScroll);
            float y = 0;
            GUI.Label(new Rect(0, y, w, 36), $"BOOKING — {Escape(b.Name).ToUpperInvariant()}", _s.H1);
            GUI.Label(new Rect(0, y + 34, w, 24), $"Arrested at {Escape(b.ArrestLocation)}", _s.Small);
            y += 66;

            y = Section(y, w, "SUBJECT");
            foreach (string line in (b.Notes ?? "").Split('\n')) y = Line(y, w, line);
            var citizen = !string.IsNullOrEmpty(b.CitizenId) ? _db.GetCitizen(b.CitizenId) : null;
            if (citizen != null && LinkButton(ref y, w, $"<b>{citizen.LastName.ToUpperInvariant()}, {citizen.FirstName}</b>   {citizen.Id} · open full record", "book:person"))
            {
                _nav.PopGroup();
                GUI.EndScrollView();
                ShowPerson(citizen);
                return;
            }

            y = Section(y, w, $"CHARGES ({b.Selected.Count} selected)");
            for (int i = 0; i < b.Charges.Count; i++)
            {
                string charge = b.Charges[i];
                bool on = b.Selected.Contains(charge);
                var row = new Rect(0, y, w, 44);
                GUI.DrawTexture(row, on ? _s.TabOn : _s.TabOff);
                string box = on ? "<color=#5fd16b><b>[X]</b></color>" : "<color=#7d8794>[  ]</color>";
                if (Btn(row, $"{box}   {Escape(charge)}", _s.Row, $"book:ch:{i}")) desk.ToggleCharge(b, charge);
                y += 50;
            }

            y += 16;
            bool canBook = b.Selected.Count > 0;
            GUI.enabled = canBook;
            if (Btn(new Rect(0, y, 360, 52), $"Book with {b.Selected.Count} charge(s)", _s.Primary, "book:book") && canBook)
            {
                string name = b.Name;
                if (desk.Book(b)) Status($"{name} booked.", false);
            }
            GUI.enabled = true;
            if (Btn(new Rect(380, y, 320, 52), "Release without charges", _s.ButtonStyle, "book:release"))
            {
                string name = b.Name;
                desk.Release(b);
                Status($"{name} released at the precinct.", true);
            }
            y += 70;

            _detailHeight = y;
            _nav.PopGroup();
            GUI.EndScrollView();
        }
    }
}
