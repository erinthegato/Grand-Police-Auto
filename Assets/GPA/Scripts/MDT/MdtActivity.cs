using System;
using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Mdt
{
    /// <summary>A traffic stop that ended this shift, as the MDT remembers it.</summary>
    internal sealed class StopRecord
    {
        public DateTime Time;
        public StopOutcome Outcome;
        public string VehicleId, CitizenId, Summary, Location;
    }

    /// <summary>One line the dispatcher read out, for the MDT's dispatch history.</summary>
    internal readonly struct RadioLine
    {
        public readonly DateTime Time;
        public readonly TransmissionPriority Priority;
        public readonly string Text;
        public RadioLine(DateTime time, TransmissionPriority priority, string text) { Time = time; Priority = priority; Text = text; }
    }

    /// <summary>
    /// What the officer has been doing and who is around them, so the MDT can offer one-tap picks
    /// instead of typed searches: traffic stops this shift (from <see cref="StopEndedEvent"/>) and the
    /// pedestrians and cars near the officer that carry a records identity. Also keeps the shift's
    /// dispatch history from <see cref="DispatchLineEvent"/>.
    /// </summary>
    internal sealed class MdtActivity : IDisposable
    {
        private const int MaxStops = 20, MaxRadioLines = 300;
        private readonly List<StopRecord> _stops = new List<StopRecord>();
        private readonly List<RadioLine> _radio = new List<RadioLine>();
        private readonly IDisposable _stopSubscription, _radioSubscription;

        public MdtActivity()
        {
            _stopSubscription = GameEvents.Subscribe<StopEndedEvent>(OnStopEnded);
            _radioSubscription = GameEvents.Subscribe<DispatchLineEvent>(e =>
            {
                _radio.Add(new RadioLine(DateTime.Now, e.Priority, e.Text));
                if (_radio.Count > MaxRadioLines) _radio.RemoveAt(0);
            });
        }

        public void Dispose()
        {
            _stopSubscription?.Dispose();
            _radioSubscription?.Dispose();
        }

        /// <summary>Oldest first.</summary>
        public IReadOnlyList<RadioLine> Radio => _radio;

        /// <summary>Newest first.</summary>
        public IReadOnlyList<StopRecord> Stops => _stops;
        public StopRecord LastStop => _stops.Count > 0 ? _stops[0] : null;

        private void OnStopEnded(StopEndedEvent e)
        {
            if (string.IsNullOrEmpty(e.VehicleId) && string.IsNullOrEmpty(e.CitizenId)) return;
            string location = "";
            if (Services.TryGet(out ILocationService locations) && Services.TryGet(out IPlayerAvatar player))
                location = locations.Describe(player.Transform.position);
            _stops.Insert(0, new StopRecord { Time = DateTime.Now, Outcome = e.Outcome, VehicleId = e.VehicleId, CitizenId = e.CitizenId, Summary = e.Summary, Location = location });
            if (_stops.Count > MaxStops) _stops.RemoveAt(_stops.Count - 1);
        }

        /// <summary>The most recent stop involving this vehicle or person, if any.</summary>
        public StopRecord StopFor(IReadOnlyCollection<string> vehicleIds, IReadOnlyCollection<string> citizenIds)
        {
            foreach (var s in _stops)
                if ((s.VehicleId != null && Contains(vehicleIds, s.VehicleId)) || (s.CitizenId != null && Contains(citizenIds, s.CitizenId))) return s;
            return null;
        }

        private static bool Contains(IReadOnlyCollection<string> ids, string id)
        {
            foreach (string x in ids) if (x == id) return true;
            return false;
        }

        public static List<(string id, float distance)> NearbyPeople(Vector2 from, float radius, int max)
        {
            var found = new List<(string, float)>();
            foreach (var ped in UnityEngine.Object.FindObjectsByType<Ped2D>())
            {
                if (string.IsNullOrEmpty(ped.CitizenId)) continue;
                float d = Vector2.Distance(from, ped.transform.position);
                if (d <= radius) found.Add((ped.CitizenId, d));
            }
            return Closest(found, max);
        }

        public static List<(string id, float distance)> NearbyVehicles(Vector2 from, float radius, int max)
        {
            var found = new List<(string, float)>();
            foreach (var car in UnityEngine.Object.FindObjectsByType<Car2D>())
            {
                if (string.IsNullOrEmpty(car.VehicleRecordId)) continue;
                float d = Vector2.Distance(from, car.transform.position);
                if (d <= radius) found.Add((car.VehicleRecordId, d));
            }
            return Closest(found, max);
        }

        private static List<(string, float)> Closest(List<(string id, float d)> found, int max)
        {
            found.Sort((a, b) => a.d.CompareTo(b.d));
            var unique = new List<(string, float)>();
            var seen = new HashSet<string>();
            foreach (var f in found)
            {
                if (!seen.Add(f.id)) continue;
                unique.Add(f);
                if (unique.Count >= max) break;
            }
            return unique;
        }
    }
}
