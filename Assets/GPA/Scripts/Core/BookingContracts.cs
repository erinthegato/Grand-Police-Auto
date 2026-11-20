using System.Collections.Generic;

namespace GPA.Core
{
    /// <summary>One prisoner waiting to be booked, as the police computer's Booking tab shows it.</summary>
    public sealed class BookingCase
    {
        public string Id;
        public string Name;
        public string CitizenId;
        public string ArrestLocation;
        /// <summary>DOB, address and warrant summary (rich text).</summary>
        public string Notes;
        /// <summary>Every charge that can be chosen: the established ones first, then common extras.</summary>
        public readonly List<string> Charges = new List<string>();
        public readonly HashSet<string> Selected = new HashSet<string>();
    }

    /// <summary>
    /// The precinct booking desk (spec §9/§10 arrest loop). Implemented by the arrest pipeline;
    /// the MDT shows <see cref="Pending"/> on its Booking tab, so neither side references the other.
    /// </summary>
    public interface IBookingDesk
    {
        /// <summary>Prisoners in the back of a car parked at the precinct.</summary>
        IReadOnlyList<BookingCase> Pending { get; }
        void ToggleCharge(BookingCase booking, string charge);
        /// <summary>Records the arrest with the selected charges. False when nothing is selected.</summary>
        bool Book(BookingCase booking);
        void Release(BookingCase booking);
    }

    /// <summary>Lets gameplay open the police computer on a given page (e.g. booking at the precinct).</summary>
    public interface IPoliceComputerService
    {
        bool IsOpen { get; }
        void OpenTo(string page);
    }

    /// <summary>One prisoner as stored in the save game (cuffed, escorted or in the back seat).</summary>
    [System.Serializable]
    public sealed class SavedPrisoner
    {
        public string citizenId;
        public string location;          // where they were arrested
        public float x, y;               // where they are now
        public bool inCar;               // seated in the back of the patrol car
        public string callId, callCode, callTitle;   // the call they were arrested on, if any
        public List<string> charges = new List<string>();
        public List<string> selected = new List<string>();
    }

    /// <summary>
    /// Lets the save game store and restore prisoners in custody without referencing the police code.
    /// Implemented by the arrest pipeline.
    /// </summary>
    public interface ICustodySave
    {
        List<SavedPrisoner> Snapshot();
        /// <summary>Replaces current custody with <paramref name="prisoners"/>; seated ones go back in the patrol car nearest <paramref name="car"/>.</summary>
        void Restore(List<SavedPrisoner> prisoners, UnityEngine.Vector2 car);
    }

    public static class MdtPages
    {
        public const string Booking = "booking";
        public const string Arrests = "arrests";
    }
}
