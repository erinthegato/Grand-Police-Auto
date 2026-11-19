using System.Collections.Generic;
using GPA.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Makes every building enterable (spec §4A). Puts a door on each facade, offers "Enter" when
    /// the officer is at one, then lifts the roof, drops the building's solid collider and shows
    /// the procedural interior. Closes it again once the officer walks back out.
    /// </summary>
    public sealed class InteriorManager2D : MonoBehaviour, IInteriorService
    {
        public static Vector2 PlayerPosition { get; private set; }

        [SerializeField] private float _doorRange = 3f;
        [SerializeField] private int _cacheSize = 4;

        private CityLocationDatabase _db;
        private Tilemap _roofs;
        private Officer2D _officer;
        private readonly Dictionary<string, Building2D> _solids = new Dictionary<string, Building2D>();
        private readonly List<CityLocationDatabase.Building> _enterable = new List<CityLocationDatabase.Building>();
        private readonly List<Vector2> _doorPoints = new List<Vector2>();
        private readonly Dictionary<string, BuildingInterior2D> _cache = new Dictionary<string, BuildingInterior2D>();
        private readonly List<string> _recent = new List<string>();
        private readonly Dictionary<Vector3Int, Color> _hiddenRoof = new Dictionary<Vector3Int, Color>();
        private BuildingInterior2D _current;
        private Roof2D _hiddenRoofPart;
        private Interactable2D _frontDoor;
        private CityLocationDatabase.Building _frontDoorTarget;
        private GUIStyle _style;

        // ---- IInteriorService ----------------------------------------------------------------
        /// <summary>The building whose interior is showing, if any.</summary>
        public BuildingInterior2D Current => _current;

        public bool Inside => _current != null && _current.Floor >= 0 && _current.Contains(PlayerPosition);
        public string BuildingId => _current != null ? _current.BuildingId : "";
        public string BuildingName => _current != null ? _current.BuildingName : "";
        public string Address => _current != null ? _current.Address : "";
        public int Floor => _current != null ? Mathf.Max(0, _current.Floor) : 0;
        public int FloorCount => _current != null ? _current.FloorCount : 0;
        public string FloorName(int floor) => InteriorPlan.FloorName(floor);
        public bool Contains(Vector2 point) => _current != null && _current.Floor >= 0 && _current.Contains(point);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<InteriorManager2D>() != null) return;
            if (FindAnyObjectByType<CityLocations>() == null || FindAnyObjectByType<Building2D>() == null) return;
            new GameObject("Interiors").AddComponent<InteriorManager2D>();
        }

        private void Awake() => Services.Register<IInteriorService>(this);
        private void OnDestroy() { RestoreRoof(); Services.Unregister<IInteriorService>(this); }

        private void Start()
        {
            var locations = FindAnyObjectByType<CityLocations>();
            _db = locations != null ? locations.Database : null;
            foreach (var tm in FindObjectsByType<Tilemap>(FindObjectsInactive.Exclude))
                if (tm.name == "Roofs") _roofs = tm;
            foreach (var b in FindObjectsByType<Building2D>(FindObjectsInactive.Exclude))
                if (!string.IsNullOrEmpty(b.BuildingId)) _solids[b.BuildingId] = b;
            if (_db != null)
                foreach (var b in _db.Buildings)
                    if (_solids.ContainsKey(b.Id) && b.Footprint.width >= 6f && b.Footprint.height >= 6f) { _enterable.Add(b); _doorPoints.Add(DoorPoint(b)); MarkDoor(b); }

            var go = new GameObject("FrontDoor");
            go.transform.SetParent(transform, false);
            _frontDoor = go.AddComponent<Interactable2D>();
            _frontDoor.Range = 2.2f;
            _frontDoor.Used += _ => { if (_frontDoorTarget != null) Open(_frontDoorTarget); };
            go.SetActive(false);
        }

        /// <summary>Door point on the facade: the footprint edge nearest the sidewalk entrance.</summary>
        public static Vector2 DoorPoint(CityLocationDatabase.Building b)
        {
            Rect f = b.Footprint;
            var plan = InteriorPlan.Build(f, b.Entrance, b.Type, 0, 1, 0);
            return plan.ToWorld(new Vector2(plan.Hall.center.x, 0f));
        }

        private void MarkDoor(CityLocationDatabase.Building b)
        {
            var plan = InteriorPlan.Build(b.Footprint, b.Entrance, b.Type, 0, 1, 0);
            var go = new GameObject("Door " + b.Id);
            go.transform.SetParent(transform, false);
            go.transform.position = plan.ToWorld(new Vector2(plan.Hall.center.x, 0.25f));
            go.transform.rotation = Quaternion.Euler(0, 0, plan.AngleU);
            go.transform.localScale = new Vector3(1.8f, 0.5f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuildingInterior2D.Pixel;
            sr.color = b.Type == "police" ? new Color(0.2f, 0.3f, 0.6f) : new Color(0.35f, 0.22f, 0.12f);
            Sorting2D.Apply(sr, Sorting2D.Roofs, 4);
        }

        private void Update()
        {
            if (_officer == null) _officer = FindAnyObjectByType<Officer2D>();
            if (_officer == null || _db == null) return;
            PlayerPosition = _officer.transform.position;
            bool onFoot = _officer.CurrentCar == null;

            // Leave: the officer walked out (or drove off).
            if (_current != null && (!onFoot || !_current.Contains(PlayerPosition, 1.2f))) Close();

            // Offer the nearest front door.
            CityLocationDatabase.Building best = null;
            float bestD = _doorRange;
            Vector2 bestPoint = default;
            if (onFoot && _current == null)
                for (int i = 0; i < _enterable.Count; i++)
                {
                    float d = Vector2.Distance(_doorPoints[i], PlayerPosition);
                    if (d < bestD) { bestD = d; best = _enterable[i]; bestPoint = _doorPoints[i]; }
                }
            _frontDoorTarget = best;
            _frontDoor.gameObject.SetActive(best != null);
            if (best != null)
            {
                _frontDoor.transform.position = bestPoint;
                _frontDoor.Prompt = $"Enter {best.Name} ({best.Address})";
            }
        }

        public void Open(CityLocationDatabase.Building b)
        {
            Close();
            if (!_cache.TryGetValue(b.Id, out var interior))
            {
                var go = new GameObject("Interior " + b.Id);
                go.transform.SetParent(transform, false);
                interior = go.AddComponent<BuildingInterior2D>();
                interior.Configure(b.Id, b.Name, b.Address, b.Type, b.Footprint, b.Entrance, b.Floors);
                _cache[b.Id] = interior;
            }
            _recent.Remove(b.Id); _recent.Add(b.Id);
            while (_recent.Count > _cacheSize)
            {
                string old = _recent[0]; _recent.RemoveAt(0);
                if (_cache.TryGetValue(old, out var dead)) { Destroy(dead.gameObject); _cache.Remove(old); }
            }

            _current = interior;
            HideRoof(b.Footprint);
            if (_solids.TryGetValue(b.Id, out var solid))
            {
                solid.GetComponent<BoxCollider2D>().enabled = false;
                _hiddenRoofPart = solid.GetComponentInChildren<Roof2D>();
                if (_hiddenRoofPart != null) _hiddenRoofPart.gameObject.SetActive(false);
            }
            interior.ShowFloor(0);
            GameAudio.UiClick();
        }

        public void Close()
        {
            if (_current == null) return;
            _current.Hide();
            if (_solids.TryGetValue(_current.BuildingId, out var solid)) solid.GetComponent<BoxCollider2D>().enabled = true;
            RestoreRoof();
            _current = null;
        }

        private void HideRoof(Rect f)
        {
            if (_roofs == null) return;
            Vector3Int a = _roofs.WorldToCell(new Vector3(f.xMin + 0.1f, f.yMin + 0.1f));
            Vector3Int c = _roofs.WorldToCell(new Vector3(f.xMax - 0.1f, f.yMax - 0.1f));
            for (int y = a.y; y <= c.y; y++)
            for (int x = a.x; x <= c.x; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                if (!_roofs.HasTile(cell)) continue;
                _hiddenRoof[cell] = _roofs.GetColor(cell);
                _roofs.SetTileFlags(cell, TileFlags.None);
                _roofs.SetColor(cell, Color.clear);
            }
        }

        private void RestoreRoof()
        {
            if (_hiddenRoofPart != null) { _hiddenRoofPart.gameObject.SetActive(true); _hiddenRoofPart = null; }
            if (_roofs != null) foreach (var kv in _hiddenRoof) _roofs.SetColor(kv.Key, kv.Value);
            _hiddenRoof.Clear();
        }

        // Small "where am I" tag while indoors.
        private void OnGUI()
        {
            if (!Inside) return;
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float s = 1f;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _style.fontSize = Mathf.RoundToInt(22 * s);
            var r = new Rect(canvas.x * 0.5f - 450 * s, 12 * s, 900 * s, 40 * s);
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(r, $"{BuildingName} · {Address} · {FloorName(Floor)} ({Floor + 1}/{FloorCount})", _style);
            GUI.matrix = old;
        }
    }
}
