using System;
using System.Collections.Generic;
using GPA.Core;
using GPA.Records;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Minimap
{
    /// <summary>
    /// HUD minimap (spec §16), top-right. Draws the city from the location database, the player
    /// arrow, markers from every <see cref="IMapMarkerProvider"/> (callouts, traffic stops, pursuits)
    /// plus parked or moving cars with an active BOLO, a GPS line along the streets to the active
    /// call, and the officer's radio status (10-8, 10-6...) underneath. M switches north-up and
    /// rotating. Rendered into a RenderTexture with GL and shown through IMGUI like the rest of the HUD;
    /// hidden while an overlay such as the MDT owns the screen.
    /// </summary>
    public sealed class Minimap2D : MonoBehaviour
    {
        [Tooltip("On-screen size at 1080p.")]
        [SerializeField] private float _size = 280f;
        [SerializeField] private int _textureSize = 512;
        [Tooltip("Metres from the centre to the edge of the map.")]
        [SerializeField] private float _onFootRadius = 55f;
        [SerializeField] private float _vehicleRadius = 85f;
        [SerializeField] private float _radiusPerKmh = 0.7f;
        [SerializeField] private float _maxRadius = 180f;
        [SerializeField] private float _cityPixelsPerMetre = 3f;

        private const string RotatePref = "GPA.Minimap.Rotating";

        private static readonly Color Water = new Color(0.10f, 0.17f, 0.23f, 1f);
        private static readonly Color32 Land = new Color32(30, 36, 44, 255);
        private static readonly Color32 Road = new Color32(84, 92, 104, 255);
        private static readonly Color32 BuildingColor = new Color32(50, 60, 74, 255);
        private static readonly Color32 ParkColor = new Color32(42, 84, 52, 255);
        private static readonly Color32 PoliceColor = new Color32(44, 74, 150, 255);
        private static readonly Color32 ShopColor = new Color32(68, 58, 84, 255);
        private static readonly Color RouteColor = new Color(1f, 0.82f, 0.25f, 0.95f);
        private static readonly Color Outline = new Color(0f, 0f, 0f, 0.85f);

        public static Minimap2D Instance { get; private set; }
        public bool Rotating { get; private set; }
        public bool Visible { get; private set; }
        public IReadOnlyList<MapMarker> Markers => _markers;
        public IReadOnlyList<Vector2> Route => _route;
        public OfficerStatus? Status => _status;

        private CityLocationDatabase _db;
        private RoadNetwork _roads;
        private Texture2D _city, _panel;
        private Rect _cityBounds;
        private RenderTexture _rt;
        private Material _cityMat, _flatMat;
        private InputAction _toggle;
        private IPlayerAvatar _player;
        private IDisposable _statusSub;
        private OfficerStatus? _status;
        private GUIStyle _small, _badge, _hint, _north;

        private readonly List<MapMarker> _markers = new List<MapMarker>();
        private readonly List<Vector2> _route = new List<Vector2>();
        private readonly List<Car2D> _boloCars = new List<Car2D>();
        private readonly HashSet<string> _boloVehicleIds = new HashSet<string>();
        private Vector2 _centre, _forward = Vector2.up;
        private float _angle, _radius, _ppm, _routeTimer, _boloTimer, _placeTimer;
        private Vector2? _routeTarget;
        private string _place = "";

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            Instance = this;
            Rotating = PlayerPrefs.GetInt(RotatePref, 0) == 1;
            _radius = _onFootRadius;
            _toggle = new InputAction("ToggleMinimapMode", InputActionType.Button);
            _toggle.AddBinding("<Keyboard>/m");
        }

        private void OnEnable()
        {
            _toggle.Enable();
            _statusSub = GameEvents.Subscribe<OfficerStatusChangedEvent>(e => _status = e.Current);
        }

        private void OnDisable()
        {
            _toggle.Disable();
            _statusSub?.Dispose();
            _statusSub = null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _toggle?.Dispose();
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_city != null) Destroy(_city);
            if (_panel != null) Destroy(_panel);
            if (_cityMat != null) Destroy(_cityMat);
            if (_flatMat != null) Destroy(_flatMat);
        }

        private void Start()
        {
            var locations = FindAnyObjectByType<CityLocations>();
            _db = locations != null ? locations.Database : null;
            if (_db == null)
            {
                Debug.LogWarning("[Minimap] No city location database in the scene; minimap disabled.");
                enabled = false;
                return;
            }
            _roads = new RoadNetwork(_db);
            BuildCityTexture();
            _rt = new RenderTexture(_textureSize, _textureSize, 0, RenderTextureFormat.ARGB32)
            {
                name = "Minimap", hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear,
            };
            _rt.Create();
            Shader shader = Shader.Find("Sprites/Default");
            _cityMat = new Material(shader) { mainTexture = _city, hideFlags = HideFlags.HideAndDontSave };
            _flatMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _panel = Solid(new Color(0.06f, 0.07f, 0.09f, 0.85f));
        }

        // ------------------------------------------------------------------ frame loop

        private void Update()
        {
            if (_player == null && !Services.TryGet(out _player)) return;
            if (!_status.HasValue && Services.TryGet(out IOfficerStatusService officerStatus)) _status = officerStatus.Status;

            if (!GameplayHotkeys.Blocked && _toggle.WasPerformedThisFrame()) SetRotating(!Rotating);

            float dt = Time.unscaledDeltaTime;
            IVehicle vehicle = _player.CurrentVehicle;
            Transform t = vehicle != null ? vehicle.Transform : _player.Transform;
            if (t == null) return;
            _centre = t.position;
            Vector2 up = t.up;
            if (up.sqrMagnitude > 0.01f) _forward = up.normalized;

            float kmh = vehicle != null ? vehicle.Velocity.magnitude * 3.6f : 0f;
            float targetRadius = vehicle != null ? Mathf.Min(_vehicleRadius + kmh * _radiusPerKmh, _maxRadius) : _onFootRadius;
            _radius = Mathf.Lerp(_radius, targetRadius, 1f - Mathf.Exp(-1.5f * dt));
            float targetAngle = Rotating ? Vector2.SignedAngle(Vector2.up, _forward) : 0f;
            _angle = Mathf.LerpAngle(_angle, targetAngle, 1f - Mathf.Exp(-8f * dt));

            MapMarkers.Collect(_markers);
            CollectBolos(dt);
            UpdateRoute(dt);

            if ((_placeTimer -= dt) <= 0f)
            {
                _placeTimer = 0.5f;
                _place = Services.TryGet(out ILocationService locations) ? locations.Describe(_centre) : "";
            }
        }

        private void LateUpdate()
        {
            Visible = _rt != null && _player != null && !GameplayHotkeys.Blocked;
            if (Visible) RenderMap();
        }

        public void SetRotating(bool rotating)
        {
            Rotating = rotating;
            PlayerPrefs.SetInt(RotatePref, rotating ? 1 : 0);
        }

        /// <summary>Cars in the world whose record has an active BOLO (refreshed once a second).</summary>
        private void CollectBolos(float dt)
        {
            if ((_boloTimer -= dt) <= 0f)
            {
                _boloTimer = 1f;
                _boloVehicleIds.Clear();
                _boloCars.Clear();
                RecordsData data = RecordsDatabase.Instance != null ? RecordsDatabase.Instance.Data : null;
                if (data != null)
                    foreach (Bolo b in data.Bolos)
                        if (b.Active && !string.IsNullOrEmpty(b.VehicleId)) _boloVehicleIds.Add(b.VehicleId);
                if (_boloVehicleIds.Count > 0)
                    foreach (Car2D car in FindObjectsByType<Car2D>(FindObjectsInactive.Exclude))
                        if (!car.IsPlayerDriven && car.VehicleRecordId != null && _boloVehicleIds.Contains(car.VehicleRecordId))
                            _boloCars.Add(car);
            }
            foreach (Car2D car in _boloCars)
                if (car != null) _markers.Add(new MapMarker(car.transform.position, MapMarkerKind.Bolo, car.Plate));
        }

        private void UpdateRoute(float dt)
        {
            Vector2? target = null;
            foreach (MapMarker m in _markers)
                if (m.Route) { target = m.Position; break; }
            if (!target.HasValue) { _route.Clear(); _routeTarget = null; return; }

            bool targetMoved = !_routeTarget.HasValue || (_routeTarget.Value - target.Value).sqrMagnitude > 4f;
            if (!targetMoved && (_routeTimer -= dt) > 0f) return;
            _routeTimer = 0.5f;
            _routeTarget = target;
            if (!_roads.TryRoute(_centre, target.Value, _route))
            {
                _route.Clear();
                _route.Add(_centre);
                _route.Add(target.Value);
            }
        }

        // ------------------------------------------------------------------ city texture

        private void BuildCityTexture()
        {
            bool any = false;
            Rect b = default;
            void Grow(Rect r)
            {
                if (!any) { b = r; any = true; return; }
                b = Rect.MinMaxRect(Mathf.Min(b.xMin, r.xMin), Mathf.Min(b.yMin, r.yMin), Mathf.Max(b.xMax, r.xMax), Mathf.Max(b.yMax, r.yMax));
            }
            foreach (var d in _db.Districts) Grow(d.Bounds);
            foreach (var s in _db.Streets) Grow(s.Bounds);
            foreach (var bl in _db.Buildings) Grow(bl.Footprint);
            b = Rect.MinMaxRect(b.xMin - 30f, b.yMin - 30f, b.xMax + 30f, b.yMax + 30f);

            float ppm = Mathf.Min(_cityPixelsPerMetre, 2048f / Mathf.Max(b.width, b.height));
            int tw = Mathf.CeilToInt(b.width * ppm), th = Mathf.CeilToInt(b.height * ppm);
            _cityBounds = new Rect(b.x, b.y, tw / ppm, th / ppm);

            var px = new Color32[tw * th];
            Color32 water = Water;
            for (int i = 0; i < px.Length; i++) px[i] = water;
            foreach (var d in _db.Districts) Fill(px, tw, th, ppm, d.Bounds, Land);
            foreach (var s in _db.Streets) Fill(px, tw, th, ppm, s.Bounds, Road);
            foreach (var bl in _db.Buildings)
            {
                Rect f = bl.Footprint;
                Fill(px, tw, th, ppm, Rect.MinMaxRect(f.xMin + 0.6f, f.yMin + 0.6f, f.xMax - 0.6f, f.yMax - 0.6f), ColorFor(bl.Type));
            }

            _city = new Texture2D(tw, th, TextureFormat.RGBA32, false)
            {
                name = "Minimap City", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave,
            };
            _city.SetPixels32(px);
            _city.Apply(false, true);
        }

        private void Fill(Color32[] px, int tw, int th, float ppm, Rect r, Color32 c)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt((r.xMin - _cityBounds.x) * ppm), 0, tw);
            int x1 = Mathf.Clamp(Mathf.CeilToInt((r.xMax - _cityBounds.x) * ppm), 0, tw);
            int y0 = Mathf.Clamp(Mathf.FloorToInt((r.yMin - _cityBounds.y) * ppm), 0, th);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((r.yMax - _cityBounds.y) * ppm), 0, th);
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                    px[y * tw + x] = c;
        }

        private static Color32 ColorFor(string type)
        {
            switch (type)
            {
                case "park": return ParkColor;
                case "plaza": return Land;
                case "police": return PoliceColor;
                case "shop":
                case "restaurant": return ShopColor;
                default: return BuildingColor;
            }
        }

        // ------------------------------------------------------------------ drawing (GL into the render texture)

        private void RenderMap()
        {
            _ppm = _textureSize * 0.5f / Mathf.Max(1f, _radius);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, _textureSize, 0, _textureSize);
            GL.Clear(true, true, Water);

            _cityMat.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(Color.white);
            CityVertex(_cityBounds.xMin, _cityBounds.yMin, 0f, 0f);
            CityVertex(_cityBounds.xMin, _cityBounds.yMax, 0f, 1f);
            CityVertex(_cityBounds.xMax, _cityBounds.yMax, 1f, 1f);
            CityVertex(_cityBounds.xMax, _cityBounds.yMin, 1f, 0f);
            GL.End();

            _flatMat.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            for (int i = 0; i + 1 < _route.Count; i++)
                Line(ToMap(i == 0 ? _centre : _route[i]), ToMap(_route[i + 1]), 7f, RouteColor);
            for (int i = 1; i + 1 < _route.Count; i++) Disc(ToMap(_route[i]), 3.5f, RouteColor, 10);
            foreach (MapMarker m in _markers) DrawMarker(m);
            DrawPlayer();
            GL.End();

            GL.PopMatrix();
            RenderTexture.active = previous;
        }

        private void CityVertex(float x, float y, float u, float v)
        {
            Vector2 p = ToMap(new Vector2(x, y));
            GL.TexCoord2(u, v);
            GL.Vertex3(p.x, p.y, 0f);
        }

        private Vector2 ToMap(Vector2 world)
        {
            float half = _textureSize * 0.5f;
            return Rotate(world - _centre, -_angle) * _ppm + new Vector2(half, half);
        }

        private void DrawMarker(MapMarker m)
        {
            float half = _textureSize * 0.5f, limit = half - 60f;   // inside the north marker's ring
            Vector2 c = new Vector2(half, half);
            Vector2 p = ToMap(m.Position);
            Vector2 d = p - c;
            float over = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
            bool atEdge = over > limit;
            if (atEdge) p = c + d * (limit / over);   // off the map: pin to the edge in its direction
            float r = atEdge ? 10f : 13f;
            float pulse = 0.45f + 0.55f * Mathf.PingPong(Time.unscaledTime * 3f, 1f);
            if (atEdge && m.Kind >= MapMarkerKind.Home) return;   // places only show when they're on the map

            switch (m.Kind)
            {
                case MapMarkerKind.Callout:
                    Diamond(p, r + 4f, Outline); Diamond(p, r, new Color(1f, 0.8f, 0.2f)); break;
                case MapMarkerKind.CalloutOffer:
                    Diamond(p, r + 4f, new Color(0f, 0f, 0f, 0.85f * pulse)); Diamond(p, r, new Color(1f, 0.8f, 0.2f, pulse)); break;
                case MapMarkerKind.TrafficStop:
                    Square(p, r + 3f, Outline); Square(p, r - 1f, new Color(1f, 0.55f, 0.15f)); break;
                case MapMarkerKind.Pursuit:
                    Disc(p, r + 4f, Outline, 18); Disc(p, r, new Color(0.95f, 0.15f, 0.15f, pulse), 18); break;
                case MapMarkerKind.Bolo:
                    Disc(p, r + 2f, Outline, 18); Disc(p, r - 2f, new Color(0.82f, 0.32f, 0.95f), 18); break;
                case MapMarkerKind.Home:
                    Square(p, r, Outline); Square(p, r - 4f, new Color(0.3f, 0.6f, 1f)); break;
                case MapMarkerKind.ForSale:
                    Square(p, r - 3f, Outline); Square(p, r - 6f, new Color(0.3f, 0.85f, 0.45f)); break;
                case MapMarkerKind.Shop:
                    Disc(p, r, Outline, 14); Disc(p, r - 4f, new Color(0.95f, 0.75f, 0.2f), 14); break;
                case MapMarkerKind.Hospital:
                    Square(p, r, Outline); Square(p, r - 3f, Color.white); Square(p, r - 7f, new Color(0.9f, 0.15f, 0.15f)); break;
                case MapMarkerKind.Casino:
                    Diamond(p, r + 1f, Outline); Diamond(p, r - 3f, new Color(0.75f, 0.35f, 0.95f)); break;
                default:
                    Disc(p, r + 3f, Outline, 14); Disc(p, r - 1f, Color.white, 14); break;
            }
        }

        private void DrawPlayer()
        {
            Vector2 c = new Vector2(_textureSize * 0.5f, _textureSize * 0.5f);
            Vector2 f = Rotate(_forward, -_angle);
            Vector2 s = new Vector2(f.y, -f.x);
            Arrow(c, f, s, 2.1f, Outline);
            Arrow(c, f, s, 1.5f, Color.white);
        }

        private static void Arrow(Vector2 c, Vector2 f, Vector2 s, float k, Color col)
        {
            Vector2 tip = c + f * 17f * k, left = c - f * 11f * k + s * 12f * k, right = c - f * 11f * k - s * 12f * k, notch = c - f * 4f * k;
            Tri(tip, left, notch, col);
            Tri(tip, notch, right, col);
        }

        private static void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            GL.Color(col);
            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(b.x, b.y, 0f);
            GL.Vertex3(c.x, c.y, 0f);
        }

        private static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col) { Tri(a, b, c, col); Tri(a, c, d, col); }

        private static void Line(Vector2 a, Vector2 b, float width, Color col)
        {
            Vector2 dir = b - a;
            if (dir.sqrMagnitude < 0.0001f) return;
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            Quad(a - n, a + n, b + n, b - n, col);
        }

        private static void Diamond(Vector2 p, float r, Color col) =>
            Quad(p + new Vector2(0f, r), p + new Vector2(r, 0f), p - new Vector2(0f, r), p - new Vector2(r, 0f), col);

        private static void Square(Vector2 p, float r, Color col) =>
            Quad(p + new Vector2(-r, -r), p + new Vector2(-r, r), p + new Vector2(r, r), p + new Vector2(r, -r), col);

        private static void Disc(Vector2 p, float r, Color col, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
                Tri(p, p + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, p + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, col);
            }
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ------------------------------------------------------------------ HUD (IMGUI)

        private void OnGUI()
        {
            if (!Visible || Event.current.type != EventType.Repaint) return;
            EnsureStyles();
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            float w = canvas.x;
            if (UiScale.Handheld)   // smaller map on a phone so it doesn't cover the road ahead
            {
                const float k = 0.72f;
                GUI.matrix = GUI.matrix * Matrix4x4.Scale(new Vector3(k, k, 1f));
                w /= k;
            }

            var frame = new Rect(w - _size - 28f, 20f, _size + 8f, _size + 66f);
            GUI.DrawTexture(frame, _panel);
            var map = new Rect(frame.x + 4f, frame.y + 4f, _size, _size);
            GUI.DrawTexture(map, _rt, ScaleMode.StretchToFill, false);

            // North marker on the rim, pointing at world north.
            Vector2 n = Rotate(Vector2.up, -_angle);
            float rim = _size * 0.5f - 14f;
            var nr = new Rect(map.center.x + n.x * rim - 11f, map.center.y - n.y * rim - 11f, 22f, 22f);
            GUI.DrawTexture(nr, _panel);
            GUI.Label(nr, "N", _north);

            var row = new Rect(map.x, map.yMax + 6f, _size, 28f);
            if (_status.HasValue)
            {
                OfficerStatus st = _status.Value;
                Color oldColor = GUI.color;
                GUI.color = StatusColor(st);
                GUI.DrawTexture(new Rect(row.x, row.y + 4f, 6f, row.height - 8f), Texture2D.whiteTexture);
                GUI.color = oldColor;
                GUI.Label(new Rect(row.x + 14f, row.y, _size - 14f, row.height), $"<b>{OfficerStatusCodes.Code(st)}</b>  {OfficerStatusCodes.Label(st)}", _badge);
            }
            GUI.Label(new Rect(row.x, row.y, _size - 4f, row.height), Rotating ? "[M] rotating" : "[M] north up", _hint);
            GUI.Label(new Rect(map.x, row.yMax, _size, 26f), _place, _small);

            GUI.matrix = old;
        }

        private static Color StatusColor(OfficerStatus s)
        {
            switch (s)
            {
                case OfficerStatus.Available: return new Color(0.37f, 0.82f, 0.42f);
                case OfficerStatus.Busy: return new Color(0.95f, 0.7f, 0.2f);
                case OfficerStatus.EnRoute: return new Color(0.3f, 0.6f, 1f);
                case OfficerStatus.OnScene: return new Color(0.7f, 0.45f, 1f);
                case OfficerStatus.TrafficStop: return new Color(1f, 0.55f, 0.15f);
                default: return new Color(0.55f, 0.58f, 0.62f);
            }
        }

        private void EnsureStyles()
        {
            if (_small != null) return;
            _small = new GUIStyle(GUI.skin.label) { fontSize = 16, clipping = TextClipping.Clip, wordWrap = false, normal = { textColor = new Color(0.75f, 0.79f, 0.84f) } };
            _badge = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            _hint = new GUIStyle(_small) { fontSize = 14, alignment = TextAnchor.MiddleRight };
            _north = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
