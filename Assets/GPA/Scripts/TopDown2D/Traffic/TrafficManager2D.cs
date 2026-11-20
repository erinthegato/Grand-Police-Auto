using System;
using System.Collections.Generic;
using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Keeps civilian traffic around the player: spawns cars on lane segments out of view, hands
    /// them a <see cref="TrafficDriver2D"/>, and removes them when they're far away. Density comes
    /// from the gameplay.trafficDensity setting (0–1). Also tracks the player's emergency lights so
    /// drivers can yield.
    /// </summary>
    public sealed class TrafficManager2D : MonoBehaviour
    {
        [SerializeField] private int _minCars = 6;
        [SerializeField] private int _maxCars = 28;
        [SerializeField, Range(0f, 1f)] private float _defaultDensity = 0.6f;
        [SerializeField] private float _spawnMin = 55f, _spawnMax = 120f, _despawn = 150f;
        [SerializeField] private float _spawnInterval = 0.35f;

        private readonly List<Car2D> _cars = new List<Car2D>();
        private readonly List<int> _laneEdges = new List<int>();
        private IDisposable _signalSub;
        private IPlayerAvatar _player;
        private ISettings _settings;
        private float _timer;

        /// <summary>The player's car while its emergency lights are on; null otherwise.</summary>
        public static Transform ActiveEmergencyVehicle { get; private set; }
        public int Count => _cars.Count;

        private void OnEnable() => _signalSub = GameEvents.Subscribe<EmergencySignalChangedEvent>(OnSignal);
        private void OnDisable() { _signalSub?.Dispose(); ActiveEmergencyVehicle = null; }

        private static void OnSignal(EmergencySignalChangedEvent e)
        {
            bool on = e.Lights != EmergencyLightMode.Off || e.Siren;
            ActiveEmergencyVehicle = on && e.Vehicle != null ? e.Vehicle.Transform : null;
        }

        private void Start()
        {
            Services.TryGet(out _settings);
            if (RoadGraph2D.Instance != null) _laneEdges.AddRange(RoadGraph2D.Instance.LaneEdges());
        }

        private int Target
        {
            get
            {
                float d = _settings != null ? _settings.GetFloat(SettingKeys.TrafficDensity, _defaultDensity) : _defaultDensity;
                return Mathf.RoundToInt(Mathf.Lerp(_minCars, _maxCars, Mathf.Clamp01(d)));
            }
        }

        private void Update()
        {
            if (_player == null && !Services.TryGet(out _player)) return;
            RoadGraph2D graph = RoadGraph2D.Instance;
            if (graph == null || WorldSprites2D.Instance == null || _laneEdges.Count == 0) return;
            Vector2 p = _player.Transform.position;

            for (int i = _cars.Count - 1; i >= 0; i--)
            {
                Car2D c = _cars[i];
                if (c == null) { _cars.RemoveAt(i); continue; }
                bool stuck = c.TryGetComponent(out TrafficDriver2D driver) && driver.Stuck;
                if ((Vector2.Distance(c.transform.position, p) > _despawn || stuck) && !Visible(c.transform.position))
                {
                    Destroy(c.gameObject);
                    _cars.RemoveAt(i);
                }
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f || _cars.Count >= Target) return;
            _timer = _spawnInterval;
            TrySpawn(graph, p);
        }

        private void TrySpawn(RoadGraph2D graph, Vector2 player)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int edge = _laneEdges[UnityEngine.Random.Range(0, _laneEdges.Count)];
                RoadGraph2D.Node from = graph.Nodes[graph.Edges[edge].From];
                float d = Vector2.Distance(from.Position, player);
                if (d < _spawnMin || d > _spawnMax || Visible(from.Position)) continue;
                if (Overpass2D.Covers(from.Position)) continue;   // never spawn on (or under) a bridge
                if (Physics2D.OverlapCircle(from.Position, 2.6f) != null) continue;   // something already there

                // Draw a registered vehicle from the citizen database when it's available.
                Sprite sprite = null;
                TrafficVehicleInfo info = default;
                bool registered = Services.TryGet(out IRecordsDirectory records) && records.Ready && records.TryPickTrafficVehicle(out info);
                VehicleSpec spec = null;
                if (registered)
                {
                    sprite = FindCarSprite(info.SpriteName);
                    if (sprite == null && VehicleCatalog.Instance != null) spec = VehicleCatalog.Instance.BySprite(info.SpriteName, out sprite);
                }
                // Big vehicles need a longer clear gap to spawn into.
                if (spec != null && spec.Length > 6f && Physics2D.OverlapCircle(from.Position, spec.Length * 0.6f) != null) continue;
                Car2D car = WorldSprites2D.Instance.SpawnCar(from.Position, from.Heading, sprite, "Traffic");
                if (spec != null) car.ApplySpec(spec, sprite);
                if (registered) car.AssignRecord(info.VehicleId, info.Plate, info.Description, info.DriverCitizenId);
                car.gameObject.AddComponent<TrafficDriver2D>().Begin(car, graph, edge);
                _cars.Add(car);
                return;
            }
        }

        private static Sprite FindCarSprite(string name)
        {
            if (string.IsNullOrEmpty(name) || WorldSprites2D.Instance == null) return null;
            foreach (Sprite s in WorldSprites2D.Instance.CivilianCars) if (s != null && s.name == name) return s;
            return null;
        }

        private static bool Visible(Vector2 point)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(point);
            return v.x > -0.15f && v.x < 1.15f && v.y > -0.15f && v.y < 1.15f;
        }
    }
}
