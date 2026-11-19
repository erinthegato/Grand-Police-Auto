using System.Collections.Generic;
using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Keeps the pavements populated around the player: spawns ambient pedestrians on sidewalk
    /// cells out of view, gives each a personality, and removes them when far away. Density comes
    /// from the gameplay.pedDensity setting (0–1).
    /// </summary>
    public sealed class PedestrianManager2D : MonoBehaviour
    {
        [SerializeField] private int _minPeds = 8;
        [SerializeField] private int _maxPeds = 40;
        [SerializeField, Range(0f, 1f)] private float _defaultDensity = 0.6f;
        [SerializeField] private float _spawnMin = 30f, _spawnMax = 85f, _despawn = 110f;
        [SerializeField] private float _spawnInterval = 0.25f;

        private readonly List<Ped2D> _peds = new List<Ped2D>();
        private IPlayerAvatar _player;
        private ISettings _settings;
        private float _timer;

        private static readonly (Ped2D.Personality p, float w)[] Profiles =
        {
            (Ped2D.Personality.Cooperative, 55f), (Ped2D.Personality.Nervous, 12f), (Ped2D.Personality.Confused, 8f),
            (Ped2D.Personality.Uncooperative, 10f), (Ped2D.Personality.Aggressive, 5f), (Ped2D.Personality.Suspicious, 7f),
            (Ped2D.Personality.Fleeing, 3f),
        };

        public int Count => _peds.Count;

        private void Start() => Services.TryGet(out _settings);

        private int Target
        {
            get
            {
                float d = _settings != null ? _settings.GetFloat(SettingKeys.PedDensity, _defaultDensity) : _defaultDensity;
                return Mathf.RoundToInt(Mathf.Lerp(_minPeds, _maxPeds, Mathf.Clamp01(d)));
            }
        }

        private void Update()
        {
            if (_player == null && !Services.TryGet(out _player)) return;
            WalkMap2D map = WalkMap2D.Instance;
            WorldSprites2D sprites = WorldSprites2D.Instance;
            if (map == null || sprites == null) return;
            Vector2 p = _player.Transform.position;

            for (int i = _peds.Count - 1; i >= 0; i--)
            {
                Ped2D ped = _peds[i];
                if (ped == null) { _peds.RemoveAt(i); continue; }
                if (Vector2.Distance(ped.transform.position, p) > _despawn && !Visible(ped.transform.position))
                {
                    Destroy(ped.gameObject);
                    _peds.RemoveAt(i);
                }
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f || _peds.Count >= Target) return;
            _timer = _spawnInterval;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                Vector2 candidate = p + Random.insideUnitCircle.normalized * Random.Range(_spawnMin, _spawnMax);
                Vector2Int cell = map.CellOf(candidate);
                WalkMap2D.Kind kind = map.At(cell);
                if (kind != WalkMap2D.Kind.Sidewalk && kind != WalkMap2D.Kind.Park) continue;
                Vector2 at = map.Centre(cell);
                if (Visible(at) || Physics2D.OverlapCircle(at, 0.6f) != null) continue;

                int look = Random.Range(0, sprites.Civilians.Length);
                Ped2D ped = sprites.SpawnPed(at, Random.Range(0f, 360f), sprites.Civilians.Length > 0 ? sprites.Civilians[look] : null, look);
                ped.name = "Pedestrian";
                ped.Ambient = true;
                if (Services.TryGet(out IRecordsDirectory records) && records.Ready) ped.CitizenId = records.PickCitizenForPedestrian(look);
                ped.Profile = RollProfile();
                ped.Wander();
                _peds.Add(ped);
                return;
            }
        }

        private static Ped2D.Personality RollProfile()
        {
            float total = 0f;
            foreach (var p in Profiles) total += p.w;
            float roll = Random.value * total;
            foreach (var p in Profiles) { roll -= p.w; if (roll <= 0f) return p.p; }
            return Ped2D.Personality.Cooperative;
        }

        private static bool Visible(Vector2 point)
        {
            Camera cam = Camera.main;
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(point);
            return v.x > -0.1f && v.x < 1.1f && v.y > -0.1f && v.y < 1.1f;
        }
    }
}
