using System.Collections.Generic;
using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Blood effects for the top-down world: spray droplets that fly out from a hit and land as
    /// stains, splats on the ground, and a pool that slowly spreads under someone who's down.
    /// Sprites are drawn in code, everything is pooled (cheap on phones), and old stains fade away.
    /// Players can turn it off in Settings ("Blood").
    /// </summary>
    public sealed class BloodFx2D : MonoBehaviour
    {
        public const string SettingKey = "gameplay.blood";
        private const int MaxStains = 220, MaxDrops = 160;
        private const float StainLife = 150f, FadeTime = 12f, PoolLife = 300f;

        private static BloodFx2D _instance;
        private static Sprite[] _splats;
        private static Sprite _drop, _pool;
        private static bool? _enabled;

        /// <summary>Whether blood is shown (saved setting, on by default).</summary>
        public static bool Enabled
        {
            get
            {
                if (_enabled == null) _enabled = !Services.TryGet(out ISettings s) || s.GetBool(SettingKey, true);
                return _enabled.Value;
            }
            set
            {
                _enabled = value;
                if (Services.TryGet(out ISettings s)) { s.Set(SettingKey, value); s.Save(); }
                if (!value && _instance != null) _instance.ClearAll();
            }
        }

        private static readonly Color Fresh = new Color(0.62f, 0.03f, 0.04f, 0.95f);
        private static readonly Color Dark = new Color(0.36f, 0.02f, 0.03f, 0.92f);

        private struct Drop { public Transform T; public SpriteRenderer R; public Vector2 Pos, Vel; public float Height, VelUp; public bool On; }
        private sealed class Stain { public SpriteRenderer R; public float Age, Life, Grow, Size; public Color Color; public bool On; }

        private readonly Drop[] _drops = new Drop[MaxDrops];
        private readonly List<Stain> _stains = new List<Stain>();
        private int _nextStain, _nextDrop;

        // ------------------------------------------------------------------ public API

        /// <summary>A hit: a splat where it landed and droplets thrown out along <paramref name="dir"/>.</summary>
        public static void Spray(Vector2 point, Vector2 dir, int drops = 10, float force = 1f)
        {
            if (!Enabled) return;
            var fx = Get();
            if (dir.sqrMagnitude < 0.0001f) dir = Random.insideUnitCircle.normalized;
            dir.Normalize();
            fx.AddStain(point + dir * 0.15f, Random.Range(0.35f, 0.6f) * Mathf.Sqrt(force), StainLife, 0.12f);
            for (int i = 0; i < drops; i++)
            {
                float spread = Random.Range(-35f, 35f) * (i % 3 == 0 ? 1.6f : 1f);
                Vector2 d = Quaternion.Euler(0, 0, spread) * dir;
                fx.AddDrop(point, d * Random.Range(2.5f, 7.5f) * force, Random.Range(0.6f, 2.2f));
            }
        }

        /// <summary>One stain on the ground (e.g. a drip).</summary>
        public static void Splat(Vector2 point, float size)
        {
            if (!Enabled) return;
            Get().AddStain(point, size, StainLife, 0f);
        }

        /// <summary>A pool that spreads out under someone who's down.</summary>
        public static void Pool(Vector2 point, float size = 1.4f)
        {
            if (!Enabled) return;
            Get().AddStain(point, size, PoolLife, 9f, pool: true);
        }

        // ------------------------------------------------------------------ internals

        private static BloodFx2D Get()
        {
            if (_instance != null) return _instance;
            BuildSprites();
            _instance = new GameObject("BloodFx").AddComponent<BloodFx2D>();
            return _instance;
        }

        private void AddDrop(Vector2 from, Vector2 vel, float up)
        {
            ref Drop d = ref _drops[_nextDrop];
            _nextDrop = (_nextDrop + 1) % MaxDrops;
            if (d.T == null)
            {
                var go = new GameObject("drop");
                go.transform.SetParent(transform, false);
                d.T = go.transform;
                d.R = go.AddComponent<SpriteRenderer>();
                d.R.sprite = _drop;
                Sorting2D.Apply(d.R, Sorting2D.Effects, -100);
            }
            d.On = true; d.Pos = from; d.Vel = vel; d.Height = 0.4f; d.VelUp = up;
            d.R.color = Fresh;
            d.T.gameObject.SetActive(true);
            d.T.position = from;
            d.T.localScale = Vector3.one * Random.Range(0.07f, 0.14f);
        }

        private void AddStain(Vector2 at, float size, float life, float grow, bool pool = false)
        {
            Stain s;
            if (_stains.Count < MaxStains) { s = new Stain(); _stains.Add(s); }
            else { s = _stains[_nextStain]; _nextStain = (_nextStain + 1) % MaxStains; }
            if (s.R == null)
            {
                var go = new GameObject("stain");
                go.transform.SetParent(transform, false);
                s.R = go.AddComponent<SpriteRenderer>();
            }
            s.R.sprite = pool ? _pool : _splats[Random.Range(0, _splats.Length)];
            // Ground decals: above roads and pavement, under props' shadows, cars and people.
            Sorting2D.Apply(s.R, Sorting2D.Props, pool ? -480 : -470);
            s.On = true; s.Age = 0f; s.Life = life; s.Grow = grow; s.Size = size;
            s.Color = pool ? Dark : Color.Lerp(Fresh, Dark, Random.value * 0.5f);
            s.R.color = s.Color;
            var t = s.R.transform;
            t.gameObject.SetActive(true);
            t.position = at;
            t.rotation = Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            t.localScale = Vector3.one * (grow > 0f ? size * 0.15f : size);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // Flying droplets: a fake height arc, then land as a small stain.
            for (int i = 0; i < MaxDrops; i++)
            {
                ref Drop d = ref _drops[i];
                if (!d.On) continue;
                d.Pos += d.Vel * dt;
                d.Vel *= 1f - Mathf.Min(1f, 2.5f * dt);
                d.VelUp -= 9f * dt;
                d.Height += d.VelUp * dt;
                if (d.Height <= 0f)
                {
                    d.On = false;
                    d.T.gameObject.SetActive(false);
                    AddStain(d.Pos, Random.Range(0.08f, 0.2f), StainLife, 0f);
                    continue;
                }
                d.T.position = d.Pos + new Vector2(0f, d.Height * 0.25f);
            }
            // Stains: pools spread out (fast then slowing), everything fades at the end of its life.
            for (int i = 0; i < _stains.Count; i++)
            {
                var s = _stains[i];
                if (!s.On) continue;
                s.Age += dt;
                if (s.Grow > 0f && s.Age < s.Grow)
                {
                    float k = 1f - Mathf.Pow(1f - s.Age / s.Grow, 3f);
                    s.R.transform.localScale = Vector3.one * s.Size * Mathf.Lerp(0.15f, 1f, k);
                }
                float left = s.Life - s.Age;
                if (left < FadeTime)
                {
                    if (left <= 0f) { s.On = false; s.R.gameObject.SetActive(false); continue; }
                    var c = s.Color; c.a *= left / FadeTime; s.R.color = c;
                }
            }
        }

        private void ClearAll()
        {
            for (int i = 0; i < MaxDrops; i++) if (_drops[i].T != null) { _drops[i].On = false; _drops[i].T.gameObject.SetActive(false); }
            foreach (var s in _stains) { s.On = false; if (s.R != null) s.R.gameObject.SetActive(false); }
        }

        // ------------------------------------------------------------------ procedural sprites

        private static void BuildSprites()
        {
            if (_splats != null) return;
            _splats = new Sprite[4];
            for (int i = 0; i < _splats.Length; i++) _splats[i] = Blob(64, 1000 + i * 77, 0.42f, 0.32f, 6);
            _drop = Blob(16, 5, 0.5f, 0.05f, 0);
            _pool = Blob(128, 31, 0.46f, 0.14f, 3);
        }

        /// <summary>A white blob with a wobbly edge and a few satellite droplets (tinted by the renderer).</summary>
        private static Sprite Blob(int size, int seed, float radius, float wobble, int satellites)
        {
            var rng = new System.Random(seed);
            float a1 = (float)rng.NextDouble() * 6.28f, a2 = (float)rng.NextDouble() * 6.28f, a3 = (float)rng.NextDouble() * 6.28f;
            int f1 = 3 + rng.Next(3), f2 = 7 + rng.Next(4);
            var sats = new Vector3[satellites];
            for (int i = 0; i < satellites; i++)
            {
                float ang = (float)rng.NextDouble() * 6.28f, dist = radius * (0.95f + (float)rng.NextDouble() * 0.35f);
                sats[i] = new Vector3(0.5f + Mathf.Cos(ang) * dist, 0.5f + Mathf.Sin(ang) * dist, 0.02f + (float)rng.NextDouble() * 0.045f);
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "Blood" };
            var px = new Color32[size * size];
            float edge = 1.5f / size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size - 0.5f;
                    float r = Mathf.Sqrt(u * u + v * v), th = Mathf.Atan2(v, u);
                    float edgeR = radius * (1f + wobble * (0.55f * Mathf.Sin(f1 * th + a1) + 0.3f * Mathf.Sin(f2 * th + a2) + 0.15f * Mathf.Sin(13 * th + a3))) * (satellites > 3 ? 0.78f : 1f);
                    float a = Mathf.Clamp01((edgeR - r) / edge);
                    for (int i = 0; i < sats.Length; i++)
                    {
                        float du = (x + 0.5f) / size - sats[i].x, dv = (y + 0.5f) / size - sats[i].y;
                        a = Mathf.Max(a, Mathf.Clamp01((sats[i].z - Mathf.Sqrt(du * du + dv * dv)) / edge));
                    }
                    // Slightly darker, thicker middle.
                    byte shade = (byte)(255 - 40 * Mathf.Clamp01(1f - r / Mathf.Max(0.01f, edgeR)));
                    px[y * size + x] = new Color32(shade, shade, shade, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private void OnDestroy() { if (_instance == this) _instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instance = null; _enabled = null; }
    }
}
