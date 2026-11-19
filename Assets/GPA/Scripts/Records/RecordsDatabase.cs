using System;
using System.Collections.Generic;
using System.IO;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Records
{
    /// <summary>
    /// The persistent citizen and vehicle database (spec §7). Saved as structured JSON in
    /// {persistentDataPath}/Records/records.json. The population is generated once on first launch
    /// and loaded every time after; police records (citations, arrests, reports, BOLOs) are added
    /// to it during play and survive across sessions. A missing or corrupt file never stops the
    /// game: a corrupt file is set aside and a fresh population is generated.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class RecordsDatabase : MonoBehaviour, IRecordsDirectory
    {
        [SerializeField] private int _seed = 1997;
        [SerializeField] private int _population = 2500;
        [SerializeField] private float _saveDelay = 4f;

        private readonly Dictionary<string, Citizen> _citizens = new Dictionary<string, Citizen>();
        private readonly Dictionary<string, VehicleRecord> _vehicles = new Dictionary<string, VehicleRecord>();
        private readonly Dictionary<string, VehicleRecord> _byPlate = new Dictionary<string, VehicleRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, List<Citizen>> _byLook = new Dictionary<int, List<Citizen>>();
        private readonly List<Citizen> _licensedDrivers = new List<Citizen>();
        private RecordsData _data;
        private float _saveTimer = -1f;

        public static RecordsDatabase Instance { get; private set; }
        public bool Ready => _data != null;
        public RecordsData Data => _data;
        public static string Folder => Path.Combine(Application.persistentDataPath, "Records");
        public static string FilePath => Path.Combine(Folder, "records.json");
        public event Action Changed;

        private void Awake()
        {
            Instance = this;
            Services.Register<IRecordsDirectory>(this);
        }

        private void Start()
        {
            Load();
        }

        private void OnDestroy()
        {
            if (_saveTimer >= 0f) Save();
            Services.Unregister<IRecordsDirectory>(this);
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit() { if (_saveTimer >= 0f) Save(); }
        private void OnApplicationPause(bool paused) { if (paused && _saveTimer >= 0f) Save(); }

        private void Update()
        {
            if (_saveTimer < 0f) return;
            _saveTimer -= Time.unscaledDeltaTime;
            if (_saveTimer < 0f) Save();
        }

        // ------------------------------------------------------------------ load / save

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    _data = JsonUtility.FromJson<RecordsData>(File.ReadAllText(FilePath));
                    if (_data == null || _data.Citizens == null || _data.Citizens.Count == 0) throw new InvalidDataException("empty records");
                    Debug.Log($"[Records] Loaded {_data.Citizens.Count} citizens, {_data.Vehicles.Count} vehicles from {FilePath}");
                }
            }
            catch (Exception e)
            {
                string aside = FilePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                try { File.Move(FilePath, aside); } catch (Exception) { /* nothing to move */ }
                Debug.LogWarning($"[Records] Could not read records ({e.Message}). Set aside as {aside}; generating a new population.");
                _data = null;
            }

            if (_data == null) Regenerate(save: true);
            bool mixed = VehicleMix.Apply(_data);
            Index();
            if (mixed) { Save(); Debug.Log("[Records] Vehicle records updated with SUVs, compacts, pickups, vans and box trucks."); }
        }

        /// <summary>Discards everything and builds a new population (dev tool / corrupt file).</summary>
        public void Regenerate(bool save)
        {
            var locations = FindAnyObjectByType<CityLocations>();
            int looks = WorldSprites2D.Instance != null && WorldSprites2D.Instance.Civilians.Length > 0 ? WorldSprites2D.Instance.Civilians.Length : 8;
            _data = PopulationGenerator.Generate(_seed, _population, looks, locations != null ? locations.Database : null, DateTime.Today);
            Debug.Log($"[Records] Generated {_data.Citizens.Count} citizens and {_data.Vehicles.Count} vehicles (seed {_seed}).");
            Index();
            if (save) Save();
        }

        public void Save()
        {
            _saveTimer = -1f;
            if (_data == null) return;
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(_data));
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
                else File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Records] Save failed: {e.Message}");
            }
        }

        /// <summary>Marks the data as changed; it is written to disk shortly after.</summary>
        public void MarkDirty()
        {
            if (_saveTimer < 0f) _saveTimer = _saveDelay;
            Changed?.Invoke();
        }

        private void Index()
        {
            _citizens.Clear(); _vehicles.Clear(); _byPlate.Clear(); _byLook.Clear(); _licensedDrivers.Clear();
            foreach (var c in _data.Citizens)
            {
                _citizens[c.Id] = c;
                if (!_byLook.TryGetValue(c.LookIndex, out var list)) _byLook[c.LookIndex] = list = new List<Citizen>();
                list.Add(c);
                if (c.License.Status == LicenseStatus.Valid) _licensedDrivers.Add(c);
            }
            foreach (var v in _data.Vehicles) { _vehicles[v.Id] = v; if (!string.IsNullOrEmpty(v.Plate)) _byPlate[v.Plate] = v; }
        }

        // ------------------------------------------------------------------ queries (MDT, traffic stops)

        public Citizen GetCitizen(string id) => id != null && _citizens.TryGetValue(id, out var c) ? c : null;
        public VehicleRecord GetVehicle(string id) => id != null && _vehicles.TryGetValue(id, out var v) ? v : null;
        public VehicleRecord FindByPlate(string plate) => plate != null && _byPlate.TryGetValue(plate.Trim(), out var v) ? v : null;

        public List<Citizen> SearchCitizens(string query, int max = 25)
        {
            var results = new List<Citizen>();
            if (_data == null || string.IsNullOrWhiteSpace(query)) return results;
            string q = query.Trim();
            foreach (var c in _data.Citizens)
            {
                if (c.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(c.License.Number) && c.License.Number.Replace(" ", "").Contains(q.Replace(" ", ""))) ||
                    c.Id.Equals(q, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(c);
                    if (results.Count >= max) break;
                }
            }
            return results;
        }

        public IEnumerable<VehicleRecord> VehiclesOf(string citizenId)
        {
            var c = GetCitizen(citizenId);
            if (c == null) yield break;
            foreach (string id in c.VehicleIds) { var v = GetVehicle(id); if (v != null) yield return v; }
        }

        /// <summary>Plate (partial, spaces and dashes ignored), VIN or vehicle id.</summary>
        public List<VehicleRecord> SearchVehicles(string query, int max = 25)
        {
            var results = new List<VehicleRecord>();
            if (_data == null || string.IsNullOrWhiteSpace(query)) return results;
            var exact = FindByPlate(query);
            if (exact != null) results.Add(exact);
            string q = Compact(query);
            foreach (var v in _data.Vehicles)
            {
                if (results.Count >= max) break;
                if (v == exact) continue;
                if (Compact(v.Plate).Contains(q) || (v.Vin != null && v.Vin.Equals(q, StringComparison.OrdinalIgnoreCase)) || v.Id.Equals(q, StringComparison.OrdinalIgnoreCase))
                    results.Add(v);
            }
            return results;
        }

        private static string Compact(string s) => s == null ? "" : s.Replace(" ", "").Replace("-", "").ToUpperInvariant();

        public List<Citizen> WantedCitizens()
        {
            var results = new List<Citizen>();
            if (_data != null) foreach (var c in _data.Citizens) if (c.Wanted) results.Add(c);
            return results;
        }

        public List<Citation> CitationsOf(string citizenId) => _data == null ? new List<Citation>() : _data.Citations.FindAll(c => c.CitizenId == citizenId);
        public List<Arrest> ArrestsOf(string citizenId) => _data == null ? new List<Arrest>() : _data.Arrests.FindAll(a => a.CitizenId == citizenId);

        public List<Bolo> ActiveBolosFor(string citizenId, string vehicleId) => _data == null ? new List<Bolo>() :
            _data.Bolos.FindAll(b => b.Active && ((citizenId != null && b.CitizenId == citizenId) || (vehicleId != null && b.VehicleId == vehicleId)));

        /// <summary>Reports that name this citizen or vehicle id, newest first.</summary>
        public List<IncidentReport> ReportsFor(string recordId)
        {
            var results = _data == null ? new List<IncidentReport>() : _data.Reports.FindAll(r => r.CitizenIds.Contains(recordId) || r.VehicleIds.Contains(recordId));
            results.Reverse();
            return results;
        }

        // ------------------------------------------------------------------ police records

        private string NextId(string prefix) => $"{prefix}-{_data.NextId++:000000}";

        public Citation AddCitation(string citizenId, string vehicleId, string offense, int fine, string location, string officer)
        {
            var c = new Citation { Id = NextId("CIT"), Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), CitizenId = citizenId, VehicleId = vehicleId, Offense = offense, Fine = fine, Location = location, Officer = officer };
            _data.Citations.Add(c);
            GetCitizen(citizenId)?.CitationIds.Add(c.Id);
            MarkDirty();
            return c;
        }

        public Arrest AddArrest(string citizenId, IEnumerable<string> charges, string location, string officer)
        {
            var a = new Arrest { Id = NextId("ARR"), Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), CitizenId = citizenId, Location = location, Officer = officer };
            a.Charges.AddRange(charges);
            _data.Arrests.Add(a);
            var citizen = GetCitizen(citizenId);
            if (citizen != null)
            {
                citizen.ArrestIds.Add(a.Id);
                foreach (string charge in a.Charges) citizen.CriminalHistory.Add(new HistoryEntry { Date = DateTime.Now.ToString("yyyy-MM-dd"), Offense = charge, Disposition = "Pending" });
            }
            MarkDirty();
            return a;
        }

        public IncidentReport SaveReport(IncidentReport report)
        {
            if (string.IsNullOrEmpty(report.Id)) { report.Id = NextId("RPT"); report.Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm"); _data.Reports.Add(report); }
            report.Updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            // Keep each record's IncidentIds in step with who the report names, including people removed on edit.
            foreach (var c in _data.Citizens) if (!report.CitizenIds.Contains(c.Id)) c.IncidentIds.Remove(report.Id);
            foreach (var v in _data.Vehicles) if (!report.VehicleIds.Contains(v.Id)) v.IncidentIds.Remove(report.Id);
            foreach (string id in report.CitizenIds) { var c = GetCitizen(id); if (c != null && !c.IncidentIds.Contains(report.Id)) c.IncidentIds.Add(report.Id); }
            foreach (string id in report.VehicleIds) { var v = GetVehicle(id); if (v != null && !v.IncidentIds.Contains(report.Id)) v.IncidentIds.Add(report.Id); }
            MarkDirty();
            return report;
        }

        public Bolo AddBolo(string subject, string reason, string vehicleId = null, string citizenId = null)
        {
            var b = new Bolo { Id = NextId("BOLO"), Created = DateTime.Now.ToString("yyyy-MM-dd"), Subject = subject, Reason = reason, VehicleId = vehicleId, CitizenId = citizenId };
            _data.Bolos.Add(b);
            MarkDirty();
            return b;
        }

        /// <summary>Cancels (or reinstates) a BOLO. Cancelled BOLOs stay on file for the record.</summary>
        public void SetBoloActive(Bolo bolo, bool active)
        {
            if (bolo == null || bolo.Active == active) return;
            bolo.Active = active;
            MarkDirty();
        }

        // ------------------------------------------------------------------ IRecordsDirectory (world systems)

        private Dictionary<string, List<Citizen>> _residents;
        private int _residentsBuiltFor = -1;

        public int ResidentsOf(string buildingId, List<string> citizenIds, List<int> looks)
        {
            if (_data == null || string.IsNullOrEmpty(buildingId)) return 0;
            if (_residents == null || _residentsBuiltFor != _data.Citizens.Count)
            {
                _residents = new Dictionary<string, List<Citizen>>();
                foreach (var c in _data.Citizens)
                {
                    if (string.IsNullOrEmpty(c.BuildingId)) continue;
                    if (!_residents.TryGetValue(c.BuildingId, out var list)) _residents[c.BuildingId] = list = new List<Citizen>();
                    list.Add(c);
                }
                _residentsBuiltFor = _data.Citizens.Count;
            }
            if (!_residents.TryGetValue(buildingId, out var found)) return 0;
            foreach (var c in found) { citizenIds.Add(c.Id); looks.Add(c.LookIndex); }
            return found.Count;
        }

        public string PickCitizenForPedestrian(int lookIndex)
        {
            if (_data == null) return null;
            if (_byLook.TryGetValue(lookIndex, out var list) && list.Count > 0) return list[UnityEngine.Random.Range(0, list.Count)].Id;
            return _data.Citizens[UnityEngine.Random.Range(0, _data.Citizens.Count)].Id;
        }

        public bool TryPickTrafficVehicle(out TrafficVehicleInfo info)
        {
            info = default;
            if (_data == null || _data.Vehicles.Count == 0) return false;
            var v = _data.Vehicles[UnityEngine.Random.Range(0, _data.Vehicles.Count)];
            // Usually the owner drives; sometimes a family member or friend (who may not be licensed).
            string driver = UnityEngine.Random.value < 0.8f || _licensedDrivers.Count == 0
                ? v.OwnerId
                : _data.Citizens[UnityEngine.Random.Range(0, _data.Citizens.Count)].Id;
            info = new TrafficVehicleInfo { VehicleId = v.Id, Plate = v.Plate, Description = v.Description, SpriteName = v.SpriteName, DriverCitizenId = driver };
            return true;
        }

        public bool TryReportStolen(string spriteName, out TrafficVehicleInfo info)
        {
            info = default;
            if (_data == null) return false;
            var candidates = _data.Vehicles.FindAll(v => !v.Stolen && (spriteName == null || v.SpriteName == spriteName));
            if (candidates.Count == 0) return false;
            var vehicle = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            vehicle.Stolen = true;
            AddBolo($"{vehicle.Description}, plate {vehicle.Plate}", "Reported stolen", vehicle.Id);
            // The thief is someone else, often with a record.
            var thief = _data.Citizens.Find(c => c.CriminalHistory.Count > 0 && c.Id != vehicle.OwnerId) ?? _data.Citizens[0];
            info = new TrafficVehicleInfo { VehicleId = vehicle.Id, Plate = vehicle.Plate, Description = vehicle.Description, SpriteName = vehicle.SpriteName, DriverCitizenId = thief.Id };
            return true;
        }

        public string DescribeCitizen(string citizenId)
        {
            var c = GetCitizen(citizenId);
            if (c == null) return "Unknown person";
            int age = DateTime.Today.Year - DateTime.Parse(c.DateOfBirth).Year;
            return $"{c.FullName}, {c.Sex}, {age}, {c.Address}";
        }
    }
}
