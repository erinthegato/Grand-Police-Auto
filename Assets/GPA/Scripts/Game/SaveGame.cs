using System;
using System.IO;
using GPA.Callouts;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>
    /// One save slot: where the officer is (and their cruiser), the time of day, duty status and the
    /// shift tally. Records (citizens, citations, arrests) already save themselves; radio settings too.
    /// Stored as JSON in {persistentDataPath}/GPA/save.json — works the same on PC and iPhone.
    /// </summary>
    public static class SaveGame
    {
        [Serializable]
        public sealed class Data
        {
            public int version = 1;
            public string savedAt;
            public float playSeconds;
            public float x, y, heading;
            public bool inVehicle;
            public bool hasCar;
            public float carX, carY, carHeading;
            public float hour = 8f;
            public bool onDuty = true;
            public int completed, failed, arrests;
            public string place;
            public string vehicleId = "patrol_sedan";
            public System.Collections.Generic.List<SavedPrisoner> prisoners = new System.Collections.Generic.List<SavedPrisoner>();
        }

        public static string Path => System.IO.Path.Combine(Application.persistentDataPath, "GPA", "save.json");
        public static bool Exists => File.Exists(Path) || File.Exists(Path + ".bak");
        public static float PlaySeconds;

        // Domain reload is off in the editor, so statics survive between Play sessions: start each at zero.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => PlaySeconds = 0f;

        /// <summary>Reads the save, falling back to the backup copy if the main file is damaged.</summary>
        public static Data Peek() => SafeFile.Read<Data>(Path, "Save");

        public static bool Save()
        {
            var officer = UnityEngine.Object.FindAnyObjectByType<Officer2D>();
            if (officer == null) return false;
            var d = new Data { savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), playSeconds = PlaySeconds };

            Vector2 pos = officer.transform.position;
            // Indoors: save the spot just outside the front door (interiors are rebuilt on entry).
            var interiors = UnityEngine.Object.FindAnyObjectByType<InteriorManager2D>();
            if (interiors != null && interiors.Inside && interiors.Current != null)
            {
                Rect f = interiors.Current.Footprint;
                Vector2? door = null;
                var db = UnityEngine.Object.FindAnyObjectByType<CityLocations>()?.Database;
                if (db != null) foreach (var b in db.Buildings) if (b.Id == interiors.Current.BuildingId) door = InteriorManager2D.DoorPoint(b);
                if (door.HasValue)   // without a door we keep the officer's spot instead of saving them at (0,0)
                {
                    Vector2 outward = (door.Value - f.center).normalized;
                    pos = door.Value + outward * 1.5f;
                }
            }
            d.x = pos.x; d.y = pos.y; d.heading = officer.transform.eulerAngles.z;

            var garage = FleetGarage2D.Instance;
            if (garage != null) d.vehicleId = garage.CurrentId;
            Car2D car = officer.CurrentCar != null ? officer.CurrentCar : garage != null && garage.Current != null ? garage.Current : FindCruiser(officer.transform.position);
            d.inVehicle = officer.CurrentCar != null;
            if (car != null)
            {
                d.hasCar = true;
                d.carX = car.transform.position.x; d.carY = car.transform.position.y; d.carHeading = car.transform.eulerAngles.z;
            }
            if (Services.TryGet(out ITimeOfDay clock)) d.hour = clock.Hour;
            var calls = UnityEngine.Object.FindAnyObjectByType<CalloutManager>();
            if (calls != null) { d.onDuty = calls.OnDuty; d.completed = calls.Completed; d.failed = calls.Failed; d.arrests = calls.Arrests; }
            if (Services.TryGet(out ILocationService loc)) d.place = loc.Describe(pos);
            if (Services.TryGet(out ICustodySave custody)) d.prisoners = custody.Snapshot();

            try
            {
                // Crash-safe: keeps the previous save as save.json.bak instead of deleting it first.
                SafeFile.Write(Path, JsonUtility.ToJson(d, true));
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[Save] Could not write save: " + e.Message); return false; }
        }

        public static bool Load()
        {
            Data d = Peek();
            var officer = UnityEngine.Object.FindAnyObjectByType<Officer2D>();
            if (d == null || officer == null) return false;

            var garage = FleetGarage2D.Instance;
            if (garage != null) garage.Restore(d.vehicleId);
            Car2D car = officer.CurrentCar != null ? officer.CurrentCar : garage != null && garage.Current != null ? garage.Current : FindCruiser(officer.transform.position);
            if (officer.CurrentCar != null) officer.ExitVehicle(new Vector3(d.x, d.y), Quaternion.Euler(0, 0, d.heading));
            if (car != null && d.hasCar) Place(car.transform, car.GetComponent<Rigidbody2D>(), new Vector2(d.carX, d.carY), d.carHeading);
            Place(officer.transform, officer.GetComponent<Rigidbody2D>(), new Vector2(d.x, d.y), d.heading);
            if (d.inVehicle && car != null) officer.EnterVehicle(car, null);
            // Prisoners come back cuffed where they were, or in the back of the patrol car.
            if (Services.TryGet(out ICustodySave custody))
                custody.Restore(d.prisoners, car != null ? (Vector2)car.transform.position : new Vector2(d.carX, d.carY));

            if (Services.TryGet(out ITimeOfDay clock)) clock.SetTime(d.hour);
            var calls = UnityEngine.Object.FindAnyObjectByType<CalloutManager>();
            if (calls != null) { calls.SetDuty(false, false); calls.RestoreStats(d.completed, d.failed, d.arrests); }   // you always start off duty
            PlaySeconds = d.playSeconds;
            return true;
        }

        public static void Delete()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch (Exception) { }
            try { if (File.Exists(Path + ".bak")) File.Delete(Path + ".bak"); } catch (Exception) { }
        }

        private static void Place(Transform t, Rigidbody2D rb, Vector2 pos, float heading)
        {
            t.SetPositionAndRotation(pos, Quaternion.Euler(0, 0, heading));
            if (rb != null) { rb.position = pos; rb.rotation = heading; rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0f; }
        }

        /// <summary>The officer's patrol car: the nearest car with "police" or "cruiser" in its name.</summary>
        private static Car2D FindCruiser(Vector2 near)
        {
            Car2D best = null; float bestD = float.MaxValue;
            foreach (var c in UnityEngine.Object.FindObjectsByType<Car2D>(FindObjectsInactive.Exclude))
            {
                string n = c.name.ToLowerInvariant();
                if (!n.Contains("police") && !n.Contains("cruiser") && !n.Contains("patrol")) continue;
                float dd = Vector2.Distance(c.transform.position, near);
                if (dd < bestD) { bestD = dd; best = c; }
            }
            return best;
        }
    }
}
