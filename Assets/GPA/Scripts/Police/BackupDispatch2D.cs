using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Police
{
    /// <summary>
    /// Backup requests and officer emergencies (spec §15). The officer asks over the radio (B key, or
    /// the radio menu on a controller); dispatch assigns AI units that drive in on the road network
    /// with lights on, park near the officer and stand by until the officer clears them (Code 4).
    /// An emergency (10-33) sends two units, priority traffic.
    /// </summary>
    public sealed class BackupDispatch2D : MonoBehaviour, IBackupService
    {
        private sealed class Unit
        {
            public string Callsign;
            public Car2D Car;
            public BackupDriver2D Driver;
            public Ped2D Officer;
            public bool Arrived, Leaving;
        }

        private readonly List<Unit> _units = new List<Unit>();
        private IPlayerAvatar _player;
        private float _holdB;
        private int _nextCallsign;

        public int UnitsAssigned { get { int n = 0; foreach (var u in _units) if (!u.Leaving) n++; return n; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<BackupDispatch2D>() != null || RoadGraph2D.Instance == null) return;
            new GameObject("Backup Dispatch").AddComponent<BackupDispatch2D>();
        }

        private void Awake() { Services.Register<IBackupService>(this); _nextCallsign = Random.Range(10, 60); }
        private void OnDestroy() => Services.Unregister<IBackupService>(this);

        private IDispatchRadio Radio => Services.TryGet(out IDispatchRadio r) ? r : null;
        private string Me => Radio != null ? Radio.Unit : "4127";
        private string Where() =>
            _player != null && Services.TryGet(out ILocationService loc) ? loc.Describe(_player.Transform.position) : "my location";

        private void Update()
        {
            if (_player == null && !Services.TryGet(out _player)) return;

            // B: tap = request backup / Code 4 if units are already assigned; hold 1.5 s = officer needs assistance.
            var kb = Keyboard.current;
            if (kb != null && !GameplayHotkeys.Blocked)
            {
                if (kb.bKey.isPressed) _holdB += Time.unscaledDeltaTime;
                if (_holdB >= 1.5f) { _holdB = -999f; RequestBackup(true); }
                if (kb.bKey.wasReleasedThisFrame)
                {
                    if (_holdB > 0f) { if (UnitsAssigned > 0) CancelBackup(); else RequestBackup(false); }
                    _holdB = 0f;
                }
            }

            for (int i = _units.Count - 1; i >= 0; i--)
            {
                Unit u = _units[i];
                if (u.Car == null) { if (u.Officer != null) Destroy(u.Officer.gameObject); _units.RemoveAt(i); continue; }
                if (!u.Arrived && !u.Leaving && u.Driver.Arrived)
                {
                    u.Arrived = true;
                    Vector2 door = u.Car.DriverExitPoint;
                    CarDoors2D.Swing(u.Car, CarDoors2D.Door.DriverFront, 0.5f);
                    if (WorldSprites2D.Instance != null)
                    {
                        u.Officer = WorldSprites2D.Instance.SpawnPed(door, u.Car.transform.eulerAngles.z, OfficerSprite());
                        u.Officer.name = "Backup Officer " + u.Callsign;
                        u.Officer.Idle();
                        u.Officer.enabled = false;   // scripted, not a civilian
                    }
                    Radio?.Transmit(TransmissionPriority.Routine, $"{u.Callsign}, {RadioCodes.Get("on_scene", "10-97")}.");
                }
                // Keep the backup officer close to the player.
                if (u.Arrived && u.Officer != null && _player != null)
                {
                    Vector2 to = (Vector2)_player.Transform.position - (Vector2)u.Officer.transform.position;
                    if (to.magnitude > 9f) u.Officer.transform.position = (Vector2)u.Officer.transform.position + to.normalized * Mathf.Min(3.2f * Time.deltaTime, to.magnitude - 3f);
                    u.Officer.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(-to.x, to.y) * Mathf.Rad2Deg);
                }
                // Units going back into service disappear once they're off screen.
                if (u.Leaving && !Visible(u.Car.transform.position))
                {
                    if (u.Officer != null) Destroy(u.Officer.gameObject);
                    Destroy(u.Car.gameObject);
                    _units.RemoveAt(i);
                }
            }
        }

        public void RequestBackup(bool emergency)
        {
            if (_player == null && !Services.TryGet(out _player)) return;
            string where = Where();
            int count = emergency ? 2 : 1;
            var callsigns = new List<string>();
            for (int i = 0; i < count; i++)
            {
                Unit u = Dispatch(emergency);
                if (u != null) callsigns.Add(u.Callsign);
            }

            if (emergency)
            {
                Radio?.Transmit(TransmissionPriority.Emergency,
                    $"{Me}, {RadioCodes.Get("emergency", "10-33")}! Officer needs assistance, {where}!",
                    $"All units, {RadioCodes.Get("emergency", "10-33")}, officer needs assistance at {where}. Emergency traffic only.",
                    callsigns.Count > 0 ? $"{string.Join(" and ", callsigns)} responding Code 3." : "Units are being sent, hold on.");
            }
            else
            {
                Radio?.Transmit(TransmissionPriority.Critical,
                    $"{Me}, requesting a {RadioCodes.Get("backup", "10-78")} at {where}.",
                    callsigns.Count > 0 ? $"{RadioCodes.Get("copy", "10-4")} {Me}. {callsigns[0]}, respond Code 3 to {where}." : $"{Me}, no units available right now.");
            }
        }

        public void CancelBackup()
        {
            bool any = false;
            foreach (var u in _units)
            {
                if (u.Leaving) continue;
                any = true;
                u.Leaving = true;
                if (u.Officer != null) { Destroy(u.Officer.gameObject); u.Officer = null; }
                u.Driver.Release();
            }
            if (any) Radio?.Transmit(TransmissionPriority.Routine, $"{Me}, {RadioCodes.Get("code4", "Code 4")}, backup can clear.", $"{RadioCodes.Get("copy", "10-4")}, all units back in service.");
        }

        // ---- spawning --------------------------------------------------------------------
        private Unit Dispatch(bool emergency)
        {
            Car2D template = PlayerCruiser();
            RoadGraph2D graph = RoadGraph2D.Instance;
            if (template == null || graph == null || graph.Nodes.Length == 0) return null;

            Vector2 target = _player.Transform.position;
            if (!BackupRoute.Plan(graph, target, 70f, 160f, out List<Vector2> path, out Vector2 start, out float heading)) return null;

            GameObject go = Instantiate(template.gameObject, start, Quaternion.Euler(0, 0, heading));
            go.name = "Backup Unit";
            var car = go.GetComponent<Car2D>();
            car.SetPlayerDriven(false, null);
            var driver = go.AddComponent<BackupDriver2D>();
            driver.Begin(car, path, target, emergency);
            var lights = go.GetComponent<Lightbar2D>();
            if (lights != null) lights.SetOn(true);

            var unit = new Unit { Callsign = "4" + (_nextCallsign++ % 90 + 110), Car = car, Driver = driver };
            _units.Add(unit);
            return unit;
        }

        private static Car2D PlayerCruiser()
        {
            Services.TryGet(out IPlayerAvatar player);
            if (player?.CurrentVehicle is Car2D driving) return driving;
            Car2D best = null; float bestD = float.MaxValue;
            foreach (var c in FindObjectsByType<Car2D>(FindObjectsInactive.Exclude))
            {
                if (c.name.StartsWith("Backup")) continue;
                string n = c.name.ToLowerInvariant();
                if (!c.IsEmergencyVehicle && !n.Contains("police") && !n.Contains("cruiser")) continue;
                float d = player != null ? Vector2.Distance(c.transform.position, player.Transform.position) : 0f;
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        /// <summary>The officer's own body sprite (armed if available), so teammates look like police.</summary>
        private Sprite OfficerSprite()
        {
            if (_player == null) return null;
            Sprite any = null;
            foreach (var sr in _player.Transform.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.sprite == null || !sr.sprite.name.StartsWith("officer")) continue;
                if (sr.sprite.name == "officer_armed") return sr.sprite;
                if (any == null || !sr.sprite.name.Contains("walk")) any = sr.sprite;
            }
            return any;
        }

        private static bool Visible(Vector2 p)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(p);
            return v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }
    }
}
