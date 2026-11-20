using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Cast shadows for every building, sized by its height (floors in the location database) and
    /// pointing away from the sun: long in the morning and evening, short at midday, only a faint
    /// moonlight shadow at night. Gives the top-down city a skyline: towers loom, houses sit low.
    ///
    /// One mesh for the whole city on the Shadows layer (under roofs, cars and people), updated as the
    /// clock moves. Created automatically in any scene that has a city. The small fixed contact
    /// shadow each roof draws (Roof2D) stays underneath.
    /// </summary>
    public sealed class BuildingShadows2D : MonoBehaviour
    {
        private const float StoreyHeight = 3.2f;     // metres per floor
        private const float Scale = 0.35f;           // top-down cheat: true shadows of 30-storey towers would swallow whole blocks
        private const float MaxLength = 42f;         // metres
        private const float SunStrength = 0.45f;     // shadow darkness in full sun (alpha at the wall)
        private const float FarFade = 0.6f;           // the far end of a shadow is this much as dark (soft edge)
        private const float MoonStrength = 0.07f, MoonLength = 0.15f;
        private static readonly Vector2 MoonDir = new Vector2(0.3f, 0.95f).normalized;

        public static BuildingShadows2D Instance { get; private set; }

        private Rect[] _foot;
        private float[] _height;
        private Mesh _mesh;
        private Vector3[] _v;
        private Color[] _c;
        private float _lastHour = -99f;
        private ITimeOfDay _clock;
        private static Material _material;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;
            var city = Object.FindAnyObjectByType<CityLocations>();
            if (city == null || city.Database == null || city.Database.Buildings.Count == 0) return;
            new GameObject("Building Shadows").AddComponent<BuildingShadows2D>();
        }

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; if (_mesh != null) Destroy(_mesh); }

        private void Start()
        {
            var db = FindAnyObjectByType<CityLocations>()?.Database;
            if (db == null) { enabled = false; return; }
            int n = db.Buildings.Count;
            _foot = new Rect[n];
            _height = new float[n];
            for (int i = 0; i < n; i++)
            {
                var b = db.Buildings[i];
                _foot[i] = b.Footprint;
                _height[i] = Mathf.Max(1, b.Floors) * StoreyHeight + 1.5f;   // + parapet / roof structure
            }

            // Six points per building: the footprint's sun-side corners plus the far corners pushed along the shadow.
            _v = new Vector3[n * 6];
            _c = new Color[n * 6];
            var tris = new int[n * 12];
            for (int i = 0; i < n; i++)
            {
                int v = i * 6, t = i * 12;
                for (int k = 0; k < 4; k++) { tris[t + k * 3] = v; tris[t + k * 3 + 1] = v + k + 1; tris[t + k * 3 + 2] = v + k + 2; }   // fan
            }
            _mesh = new Mesh { name = "Building Shadows", hideFlags = HideFlags.DontSave };
            if (_v.Length > 65000) _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.MarkDynamic();
            _mesh.vertices = _v;
            _mesh.colors = _c;
            _mesh.uv = new Vector2[_v.Length];
            _mesh.triangles = tris;

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SharedMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Sorting2D.Apply(mr, Sorting2D.Shadows, -10);   // under the roofs' own contact shadows and cars' shadows
            Refresh(true);
        }

        private void Update() => Refresh(false);

        private void Refresh(bool force)
        {
            if (_mesh == null) return;
            if (_clock == null) Services.TryGet(out _clock);
            float hour = _clock != null ? _clock.Hour : 13f;
            if (!force && Mathf.Abs(Mathf.DeltaAngle(hour * 15f, _lastHour * 15f)) < 0.75f) return;   // every ~3 in-game minutes
            _lastHour = hour;

            Sun(hour, out Vector2 dir, out float perMetre, out float alpha);
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color near = new Color(0f, 0f, 0f, alpha), far = new Color(0f, 0f, 0f, alpha * FarFade);
            if (linear) { near = near.linear; far = far.linear; }

            // Work with the shadow pointing up-right, then mirror back: the six-point outline is always
            // footprint corners (xMin,yMin) (xMax,yMin) (xMax,yMax)+s ... in that frame.
            float sx = Mathf.Sign(dir.x == 0f ? 1f : dir.x), sy = Mathf.Sign(dir.y == 0f ? 1f : dir.y);
            for (int i = 0; i < _foot.Length; i++)
            {
                Rect r = _foot[i];
                float len = Mathf.Min(_height[i] * perMetre, MaxLength);
                Vector2 s = new Vector2(Mathf.Abs(dir.x), Mathf.Abs(dir.y)) * len;
                Vector2 c = r.center, h = r.size * 0.5f;
                // Mirrored frame: corners relative to the centre.
                Vector2 a = new Vector2(-h.x, -h.y), b = new Vector2(h.x, -h.y), d = new Vector2(-h.x, h.y), e = new Vector2(h.x, h.y);
                Vector2 Back(Vector2 p) => c + new Vector2(p.x * sx, p.y * sy);
                int v = i * 6;
                _v[v]     = Back(a);
                _v[v + 1] = Back(b);
                _v[v + 2] = Back(b + s);
                _v[v + 3] = Back(e + s);
                _v[v + 4] = Back(d + s);
                _v[v + 5] = Back(d);
                _c[v] = _c[v + 1] = _c[v + 5] = near;
                _c[v + 2] = _c[v + 3] = _c[v + 4] = far;
            }
            _mesh.vertices = _v;
            _mesh.colors = _c;
            _mesh.RecalculateBounds();
        }

        /// <summary>
        /// Where the shadows point and how long they are per metre of height. The sun rises in the east at 6:00,
        /// stands in the south at noon (about 62° up) and sets in the west at 18:00; at night a faint moon shadow.
        /// </summary>
        private static void Sun(float hour, out Vector2 dir, out float perMetre, out float alpha)
        {
            float day = Mathf.InverseLerp(6f, 18f, hour);
            float elevation = hour > 6f && hour < 18f ? 62f * Mathf.Sin(Mathf.PI * day) : 0f;
            float azimuth = Mathf.Lerp(90f, 270f, day) * Mathf.Deg2Rad;                    // compass bearing of the sun
            Vector2 sunDir = -new Vector2(Mathf.Sin(azimuth), Mathf.Cos(azimuth));        // shadows fall away from it
            float sunPerMetre = Scale / Mathf.Tan(Mathf.Max(elevation, 9f) * Mathf.Deg2Rad);
            float sun = Mathf.Clamp01((elevation - 2f) / 10f);                              // fades in after sunrise, out before sunset
            dir = Vector2.Lerp(MoonDir, sunDir, sun).normalized;
            perMetre = Mathf.Lerp(MoonLength, sunPerMetre, sun);
            alpha = Mathf.Lerp(MoonStrength, SunStrength, sun);
        }

        /// <summary>
        /// Resources/BuildingShadow: unlit vertex colour with a stencil test so overlapping shadows don't
        /// stack. Falls back to Sprites/Default (overlaps a little darker) if the material is missing.
        /// </summary>
        private static Material SharedMaterial()
        {
            if (_material != null) return _material;
            var asset = Resources.Load<Material>("BuildingShadow");
            if (asset != null && asset.shader != null && asset.shader.isSupported) return _material = asset;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                var probe = new GameObject("probe") { hideFlags = HideFlags.HideAndDontSave };
                shader = probe.AddComponent<SpriteRenderer>().sharedMaterial.shader;
                Destroy(probe);
            }
            _material = new Material(shader) { name = "Building shadows", hideFlags = HideFlags.DontSave };
            return _material;
        }
    }
}
