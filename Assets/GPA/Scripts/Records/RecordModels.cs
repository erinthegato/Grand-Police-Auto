using System;
using System.Collections.Generic;

namespace GPA.Records
{
    // Plain serialisable records (JsonUtility). Dates are ISO strings (yyyy-MM-dd) so the file stays readable.

    public enum LicenseStatus { Valid, Suspended, Revoked, Expired, None }
    public enum RegistrationStatus { Valid, Expired, Suspended }

    [Serializable]
    public sealed class DriverLicense
    {
        public string Number;
        public string Class = "D";
        public LicenseStatus Status;
        public string Expires;
    }

    [Serializable]
    public sealed class Warrant
    {
        public string Id;
        public string Offense;
        public string Issued;
        public bool Felony;
    }

    [Serializable]
    public sealed class HistoryEntry
    {
        public string Date;
        public string Offense;
        public string Disposition;   // Convicted, Dismissed, Pending...
    }

    [Serializable]
    public sealed class Citizen
    {
        public string Id;            // C-000123
        public string FirstName;
        public string LastName;
        public string Sex;           // M / F
        public string DateOfBirth;
        public string Address;
        public string BuildingId;
        public int HeightCm;
        public int WeightKg;
        public string Hair;
        public string Eyes;
        public int LookIndex;        // which pedestrian outfit sprite set
        public DriverLicense License = new DriverLicense();
        public List<string> VehicleIds = new List<string>();
        public List<Warrant> Warrants = new List<Warrant>();
        public List<HistoryEntry> CriminalHistory = new List<HistoryEntry>();
        public List<string> CitationIds = new List<string>();
        public List<string> ArrestIds = new List<string>();
        public List<string> IncidentIds = new List<string>();

        public string FullName => $"{FirstName} {LastName}";
        public bool Wanted => Warrants.Count > 0;
    }

    [Serializable]
    public sealed class VehicleRecord
    {
        public string Id;            // V-000123
        public string Make;
        public string Model;
        public string Color;
        public int Year;
        public string Plate;
        public string Vin;
        public string OwnerId;
        public RegistrationStatus Registration;
        public string RegistrationExpires;
        public bool Insured = true;
        public bool Stolen;
        public string SpriteName;
        public List<string> IncidentIds = new List<string>();

        public string Description => $"{Color} {Year} {Make} {Model}";
    }

    [Serializable]
    public sealed class Bolo
    {
        public string Id;
        public string Created;
        public string Subject;       // "Red Bayline Meridian, plate ABC-1234" / person description
        public string Reason;
        public string VehicleId;
        public string CitizenId;
        public bool Active = true;
    }

    [Serializable]
    public sealed class Citation
    {
        public string Id;
        public string Date;
        public string CitizenId;
        public string VehicleId;
        public string Offense;
        public int Fine;
        public string Location;
        public string Officer;
    }

    [Serializable]
    public sealed class Arrest
    {
        public string Id;
        public string Date;
        public string CitizenId;
        public List<string> Charges = new List<string>();
        public string Location;
        public string Officer;
    }

    [Serializable]
    public sealed class IncidentReport
    {
        public string Id;
        public string Created;
        public string Updated;
        public string Title;
        public string Type;
        public string Location;
        public string Officer;
        public string Narrative;
        public List<string> CitizenIds = new List<string>();
        public List<string> VehicleIds = new List<string>();
    }

    /// <summary>Everything persisted to disk.</summary>
    [Serializable]
    public sealed class RecordsData
    {
        public int Version = 1;
        public int Seed;
        public string Generated;
        public string GameDate;
        public List<Citizen> Citizens = new List<Citizen>();
        public List<VehicleRecord> Vehicles = new List<VehicleRecord>();
        public List<Bolo> Bolos = new List<Bolo>();
        public List<Citation> Citations = new List<Citation>();
        public List<Arrest> Arrests = new List<Arrest>();
        public List<IncidentReport> Reports = new List<IncidentReport>();
        public int NextId = 1;
    }
}
