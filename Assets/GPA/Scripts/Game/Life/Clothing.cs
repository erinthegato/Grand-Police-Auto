using System;
using System.Collections.Generic;
using System.Linq;
using GPA.Callouts;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>Off-duty clothes: hats and shirts sold at Halden Threads and kept in your wardrobe.</summary>
    public static class Clothing
    {
        public enum Kind { Hat, Shirt }
        public enum HatStyle { Cap, BackwardsCap, Beanie, Bucket, Fedora, Cowboy }
        public enum Pattern { Solid, Stripes, Plaid, Floral, TwoTone }

        public sealed class Item
        {
            public string Id, Name;
            public Kind Kind;
            public int Price;
            public Color32 Main, Second;
            public HatStyle Hat;
            public Pattern Pattern;
        }

        private static Color32 C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);
        private static Item H(string id, string name, int price, HatStyle s, Color32 a, Color32 b) => new Item { Id = id, Name = name, Kind = Kind.Hat, Price = price, Hat = s, Main = a, Second = b };
        private static Item S(string id, string name, int price, Pattern p, Color32 a, Color32 b) => new Item { Id = id, Name = name, Kind = Kind.Shirt, Price = price, Pattern = p, Main = a, Second = b };

        public const string DefaultShirt = "tee_grey";

        public static readonly List<Item> All = new List<Item>
        {
            H("cap_black", "Black ball cap", 120, HatStyle.Cap, C(30, 30, 34), C(30, 30, 34)),
            H("cap_red", "Red ball cap", 120, HatStyle.Cap, C(175, 38, 38), C(175, 38, 38)),
            H("cap_hawks", "Halden Hawks cap", 180, HatStyle.Cap, C(28, 40, 80), C(232, 112, 30)),
            H("cap_back", "Backwards cap", 140, HatStyle.BackwardsCap, C(45, 95, 170), C(45, 95, 170)),
            H("beanie_grey", "Grey beanie", 90, HatStyle.Beanie, C(120, 122, 128), C(95, 97, 102)),
            H("beanie_orange", "Hunter-orange beanie", 90, HatStyle.Beanie, C(232, 112, 30), C(190, 85, 20)),
            H("bucket_tan", "Tan bucket hat", 150, HatStyle.Bucket, C(190, 170, 122), C(160, 140, 98)),
            H("fedora", "Charcoal fedora", 320, HatStyle.Fedora, C(62, 62, 68), C(22, 22, 24)),
            H("cowboy", "Brown cowboy hat", 450, HatStyle.Cowboy, C(128, 84, 46), C(70, 44, 24)),

            S(DefaultShirt, "Grey tee", 0, Pattern.Solid, C(128, 130, 136), C(128, 130, 136)),
            S("tee_white", "White tee", 60, Pattern.Solid, C(228, 228, 232), C(228, 228, 232)),
            S("tee_black", "Black tee", 60, Pattern.Solid, C(42, 42, 46), C(42, 42, 46)),
            S("polo_green", "Green polo", 110, Pattern.Solid, C(42, 124, 72), C(42, 124, 72)),
            S("flannel_red", "Red flannel", 180, Pattern.Plaid, C(165, 38, 38), C(35, 30, 30)),
            S("stripes_navy", "Breton stripes", 160, Pattern.Stripes, C(232, 232, 236), C(30, 45, 95)),
            S("hawaiian", "Hawaiian shirt", 220, Pattern.Floral, C(36, 150, 165), C(250, 205, 80)),
            S("hoodie_grey", "Grey hoodie", 240, Pattern.TwoTone, C(98, 100, 108), C(80, 82, 90)),
            S("jersey_hawks", "Hawks jersey", 300, Pattern.TwoTone, C(232, 112, 30), C(28, 40, 80)),
            S("leather", "Leather jacket", 650, Pattern.TwoTone, C(46, 34, 28), C(36, 26, 22)),
            S("suit", "Charcoal suit jacket", 900, Pattern.TwoTone, C(55, 57, 64), C(48, 50, 56)),
        };

        public static Item Get(string id) => string.IsNullOrEmpty(id) ? null : All.FirstOrDefault(i => i.Id == id);

        // ------------------------------------------------------------------ store / wardrobe menus

        private static PlayerLife Life => PlayerLife.Instance;

        public static bool Owns(string id)
        {
            if (Life == null) return false;
            if (id == DefaultShirt) return true;
            return Life.Data.clothes != null && Life.Data.clothes.Contains(id);
        }

        /// <summary>The clothing store (buy + wear) or the home wardrobe (owned items only).</summary>
        public static void Open(bool store)
        {
            if (Life == null) return;
            var menu = LifeMenu.Get();
            string title = store ? "Halden Threads" : "Wardrobe";
            menu.Show(title, store ? "Hats and shirts. You wear them off duty — on shift it's the uniform." : "Change what you wear off duty.", () => new List<LifeMenu.Row>
            {
                new LifeMenu.Row("Hats", Get(Life.Data.hat)?.Name ?? "no hat", () => OpenKind(Kind.Hat, store)),
                new LifeMenu.Row("Shirts", (Get(Life.Data.shirt) ?? Get(DefaultShirt)).Name, () => OpenKind(Kind.Shirt, store)),
                new LifeMenu.Row(store ? "Leave" : "Close", "", menu.Close),
            });
        }

        private static void OpenKind(Kind kind, bool store)
        {
            var menu = LifeMenu.Get();
            menu.Show(kind == Kind.Hat ? "Hats" : "Shirts", store ? "Buying an item puts it on straight away." : "Your clothes.", () =>
            {
                var rows = new List<LifeMenu.Row>();
                if (kind == Kind.Hat)
                    rows.Add(new LifeMenu.Row("No hat", string.IsNullOrEmpty(Life.Data.hat) ? "wearing" : "", () => Wear(Kind.Hat, ""), !string.IsNullOrEmpty(Life.Data.hat)));
                foreach (var item in All.Where(i => i.Kind == kind))
                {
                    bool owned = Owns(item.Id);
                    if (!owned && !store) continue;
                    string worn = kind == Kind.Hat ? Life.Data.hat : (string.IsNullOrEmpty(Life.Data.shirt) ? DefaultShirt : Life.Data.shirt);
                    if (owned)
                        rows.Add(new LifeMenu.Row(item.Name, worn == item.Id ? "wearing" : "owned · wear", () => Wear(kind, item.Id), worn != item.Id));
                    else
                        rows.Add(new LifeMenu.Row(item.Name, $"${item.Price:N0}", () =>
                        {
                            if (!Life.Spend(item.Price, item.Name)) { menu.Message("Not enough money."); return; }
                            if (!Life.Data.clothes.Contains(item.Id)) Life.Data.clothes.Add(item.Id);
                            Wear(kind, item.Id);
                        }, Life.CanAfford(item.Price)));
                }
                rows.Add(new LifeMenu.Row("Back", "", () => Open(store)));
                return rows;
            });
        }

        private static void Wear(Kind kind, string id)
        {
            if (kind == Kind.Hat) Life.Data.hat = id; else Life.Data.shirt = id;
            Life.Save();
            OfficerOutfit2D.Refresh();
            bool duty = CalloutManager.Instance != null && CalloutManager.Instance.OnDuty;
            if (duty) LifeMenu.Get().Message("Saved — you'll wear it when you go off duty.");
        }
    }

    /// <summary>
    /// Draws the off-duty outfit on the officer: recolours the uniform shirt in each body frame and
    /// swaps the police cap for hair, then puts a hat sprite on the head. On duty the plain uniform
    /// is shown. Runs after the walk animator so the swapped frame is what gets drawn.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class OfficerOutfit2D : MonoBehaviour
    {
        private static OfficerOutfit2D _instance;
        private SpriteRenderer _body, _hat;
        private readonly Dictionary<Sprite, Frame> _frames = new Dictionary<Sprite, Frame>();
        private readonly HashSet<Sprite> _made = new HashSet<Sprite>();
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private string _key;

        private sealed class Frame { public Sprite Sprite; public Vector2 Head; public bool HasHead; }

        private static readonly Color32 Hair = new Color32(96, 66, 42, 255);
        private static readonly Color32 Outline = new Color32(22, 22, 22, 255);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var officer = FindAnyObjectByType<Officer2D>();
            if (officer == null || officer.GetComponent<OfficerOutfit2D>() != null) return;
            officer.gameObject.AddComponent<OfficerOutfit2D>();
        }

        public static void Refresh() { if (_instance != null) _instance._key = null; }

        private void Awake()
        {
            _instance = this;
            var anim = GetComponentInChildren<SpriteAnimator2D>(true);
            _body = anim != null ? anim.GetComponent<SpriteRenderer>() : null;
            if (_body == null) { enabled = false; return; }
            var go = new GameObject("Outfit hat");
            go.transform.SetParent(_body.transform, false);
            _hat = go.AddComponent<SpriteRenderer>();
            _hat.sortingLayerID = _body.sortingLayerID;
            _hat.sortingOrder = _body.sortingOrder + 3;
            _hat.enabled = false;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            Clear();
        }

        private void Clear()
        {
            foreach (var o in _owned) if (o != null) Destroy(o);
            _owned.Clear(); _frames.Clear(); _made.Clear();
        }

        private void LateUpdate()
        {
            var life = PlayerLife.Instance;
            bool onDuty = CalloutManager.Instance != null && CalloutManager.Instance.OnDuty;
            if (life == null || onDuty || _body.sprite == null)
            {
                _hat.enabled = false;
                // Put the original frame back if we swapped it last frame and the animator is frozen.
                if (_body.sprite != null && _made.Contains(_body.sprite))
                    foreach (var kv in _frames) if (kv.Value.Sprite == _body.sprite) { _body.sprite = kv.Key; break; }
                return;
            }

            var shirt = Clothing.Get(life.Data.shirt) ?? Clothing.Get(Clothing.DefaultShirt);
            var hat = Clothing.Get(life.Data.hat);
            string key = shirt.Id + "|" + (hat != null ? hat.Id : "");
            if (key != _key)
            {
                // Swap back to originals before throwing the old variants away.
                if (_made.Contains(_body.sprite))
                    foreach (var kv in _frames) if (kv.Value.Sprite == _body.sprite) { _body.sprite = kv.Key; break; }
                Clear();
                _key = key;
                if (hat != null) { _hat.sprite = HatSprite(hat); _owned.Add(_hat.sprite.texture); _owned.Add(_hat.sprite); }
                else _hat.sprite = null;
            }

            Sprite src = _body.sprite;
            if (_made.Contains(src)) src = null;   // already swapped this frame (animator frozen)
            Frame f = null;
            if (src != null)
            {
                if (!_frames.TryGetValue(src, out f)) { f = MakeFrame(src, shirt); _frames[src] = f; if (f.Sprite != null) _made.Add(f.Sprite); }
                if (f.Sprite != null) _body.sprite = f.Sprite;
            }
            else foreach (var kv in _frames) if (kv.Value.Sprite == _body.sprite) { f = kv.Value; break; }

            bool showHat = hat != null && _body.enabled && f != null && f.HasHead && _hat.sprite != null;
            _hat.enabled = showHat;
            if (showHat)
            {
                _hat.transform.localPosition = new Vector3(f.Head.x, f.Head.y, 0f);
                _hat.color = _body.color;
            }
        }

        // ------------------------------------------------------------------ body recolour

        private static float Lum(Color32 c) => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;

        private Frame MakeFrame(Sprite src, Clothing.Item shirt)
        {
            var frame = new Frame();
            // Sprites may live in a (trimmed, possibly scaled) atlas, so map pixels through the
            // sprite mesh: its UV extremes give the pixel block, its vertex extremes the local size.
            Vector2[] uv = src.uv, vt = src.vertices;
            Vector2 uMin = uv[0], uMax = uv[0], lMin = vt[0], lMax = vt[0];
            for (int i = 1; i < uv.Length; i++) { uMin = Vector2.Min(uMin, uv[i]); uMax = Vector2.Max(uMax, uv[i]); }
            for (int i = 1; i < vt.Length; i++) { lMin = Vector2.Min(lMin, vt[i]); lMax = Vector2.Max(lMax, vt[i]); }
            Texture2D t = src.texture;
            int x0 = Mathf.FloorToInt(uMin.x * t.width), y0 = Mathf.FloorToInt(uMin.y * t.height);
            int w = Mathf.CeilToInt(uMax.x * t.width) - x0, h = Mathf.CeilToInt(uMax.y * t.height) - y0;
            if (w <= 0 || h <= 0) return frame;
            float unit = (lMax.x - lMin.x) / Mathf.Max(1e-5f, (uMax.x - uMin.x) * t.width);   // metres per texture pixel
            float k = 1f / (unit * 64f);                                                        // texture px per art px
            Vector2 origin = lMin + new Vector2(((float)x0 / t.width - uMin.x) * t.width, ((float)y0 / t.height - uMin.y) * t.height) * unit;
            Color32[] px = ReadPixels(t, x0, y0, w, h);
            if (px == null) return frame;

            // Head centre = middle of the gold cap badge.
            float hx = 0, hy = 0; int n = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color32 c = px[y * w + x];
                    if (c.a > 200 && Mathf.Abs(c.r - 143) + Mathf.Abs(c.g - 122) + Mathf.Abs(c.b - 58) < 30) { hx += x; hy += y; n++; }
                }
            bool hasHead = n >= Mathf.Max(3, Mathf.RoundToInt(6 * k * k));
            if (hasHead) { hx /= n; hy /= n; }
            float headR = 14f * k;
            float shirtRef = Lum(new Color32(36, 52, 92, 255)), capRef = Lum(new Color32(26, 31, 43, 255));

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    Color32 c = px[i];
                    if (c.a == 0) continue;
                    bool grey = Mathf.Abs(c.r - c.b) < 7 && Mathf.Abs(c.r - c.g) < 7 && c.r < 40;   // outline
                    if (grey) continue;
                    float d = hasHead ? Vector2.Distance(new Vector2(x, y), new Vector2(hx, hy)) : 999f;
                    if (d <= headR)
                    {
                        bool badge = Mathf.Abs(c.r - 143) + Mathf.Abs(c.g - 122) + Mathf.Abs(c.b - 58) < 60;
                        float s = badge ? 1f : Mathf.Clamp(Lum(c) / capRef, 0.55f, 1.35f);
                        float back = Mathf.Clamp01((hy - y) / headR) * 0.15f;   // lighter toward the back of the head
                        px[i] = Tint(Hair, s + back, c.a);
                        continue;
                    }
                    bool blue = c.b - c.r >= 12 && c.b > c.g;   // uniform fabric (skin and gear are not blue)
                    if (!blue) continue;
                    float shade = Mathf.Clamp(Lum(c) / shirtRef, 0.35f, 1.3f);
                    px[i] = Tint(ShirtColour(shirt, Mathf.RoundToInt(x / k), Mathf.RoundToInt(y / k), d / k), shade, c.a);
                }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = t.filterMode, wrapMode = TextureWrapMode.Clamp, name = src.name + "_outfit" };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            Vector2 pivot = new Vector2(-origin.x / (w * unit), -origin.y / (h * unit));
            frame.Sprite = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, 1f / unit, 0, SpriteMeshType.FullRect);
            frame.Sprite.name = src.name + "_outfit";
            _owned.Add(tex); _owned.Add(frame.Sprite);
            frame.HasHead = hasHead;
            frame.Head = origin + new Vector2(hx + 0.5f, hy + 0.5f) * unit;
            return frame;
        }

        private static Color32 ShirtColour(Clothing.Item s, int x, int y, float headDist)
        {
            switch (s.Pattern)
            {
                case Clothing.Pattern.Stripes: return (y / 3) % 2 == 0 ? s.Main : s.Second;
                case Clothing.Pattern.Plaid:
                {
                    bool a = (x / 4) % 2 == 0, b = (y / 4) % 2 == 0;
                    if (a && b) return Color32.Lerp(s.Main, s.Second, 0.65f);
                    if (a || b) return Color32.Lerp(s.Main, s.Second, 0.25f);
                    return s.Main;
                }
                case Clothing.Pattern.Floral:
                {
                    int h = (x * 73856093) ^ (y * 19349663);
                    bool dot = ((x + 2 * (y / 5)) % 6 == 0 && y % 5 < 2) || ((h >> 5) & 31) == 0;
                    return dot ? s.Second : s.Main;
                }
                case Clothing.Pattern.TwoTone: return headDist > 24f ? s.Second : s.Main;   // sleeves / arms
                default: return s.Main;
            }
        }

        private static Color32 Tint(Color32 c, float k, byte a) =>
            new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), a);

        /// <summary>A block of a texture's pixels, even when it isn't CPU-readable (via a GPU copy).</summary>
        private static Color32[] ReadPixels(Texture2D t, int x0, int y0, int w, int h)
        {
            try
            {
                if (t.isReadable)
                {
                    var all = t.GetPixels32();
                    var outPx = new Color32[w * h];
                    for (int y = 0; y < h; y++) Array.Copy(all, (y0 + y) * t.width + x0, outPx, y * w, w);
                    return outPx;
                }
                var rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(t, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tmp.ReadPixels(new Rect(x0, y0, w, h), 0, 0);
                tmp.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                var px = tmp.GetPixels32();
                Destroy(tmp);
                return px;
            }
            catch (Exception e) { Debug.LogWarning("[Outfit] " + e.Message); return null; }
        }

        // ------------------------------------------------------------------ hats (drawn at 2x, 128 px per metre)

        private static Sprite HatSprite(Clothing.Item hat)
        {
            const int N = 128;            // texture size; 2 px per body pixel
            const float S = 2f;           // body pixel → hat pixel
            var px = new Color32[N * N];
            var region = new int[N * N];  // 0 empty, 1 brim, 2 crown, 3 band / detail
            float cx = N * 0.5f, cy = N * 0.5f;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f - cx) / S, v = (y + 0.5f - cy) / S;   // body pixels, +v = forward
                    region[y * N + x] = HatRegion(hat.Hat, u, v);
                }
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int i = y * N + x, reg = region[i];
                    if (reg == 0)
                    {
                        // Outline ring around the shape.
                        bool edge = false;
                        for (int dy = -3; dy <= 3 && !edge; dy++)
                            for (int dx = -3; dx <= 3 && !edge; dx++)
                            {
                                int xx = x + dx, yy = y + dy;
                                if (xx < 0 || yy < 0 || xx >= N || yy >= N || dx * dx + dy * dy > 9) continue;
                                if (region[yy * N + xx] != 0) edge = true;
                            }
                        if (edge) px[i] = Outline;
                        continue;
                    }
                    // Inner seams between brim and crown.
                    bool seam = false;
                    for (int k = 0; k < 4 && !seam; k++)
                    {
                        int xx = x + (k == 0 ? 2 : k == 1 ? -2 : 0), yy = y + (k == 2 ? 2 : k == 3 ? -2 : 0);
                        if (xx >= 0 && yy >= 0 && xx < N && yy < N && region[yy * N + xx] != reg && region[yy * N + xx] != 0 && reg != 3 && region[yy * N + xx] != 3) seam = true;
                    }
                    Color32 baseCol = reg == 3 ? hat.Second : reg == 1 && hat.Hat == Clothing.HatStyle.Cap ? hat.Second : hat.Main;
                    float u = (x + 0.5f - cx) / S, v = (y + 0.5f - cy) / S;
                    float light = 1f + Mathf.Clamp((v - u) / 60f, -0.12f, 0.12f) + (reg == 2 ? 0.06f : reg == 1 ? -0.05f : 0f);
                    if (hat.Hat == Clothing.HatStyle.Beanie && reg == 2 && ((int)(Mathf.Sqrt(u * u + v * v) * 1.2f)) % 3 == 0) light -= 0.08f;   // knit rings
                    if (seam) light *= 0.6f;
                    px[i] = Tint(baseCol, light, 255);
                }
            // Button on top of caps.
            if (hat.Hat == Clothing.HatStyle.Cap || hat.Hat == Clothing.HatStyle.BackwardsCap)
                for (int y = -2; y <= 2; y++) for (int x = -2; x <= 2; x++) if (x * x + y * y <= 4) px[(int)(cy + y) * N + (int)(cx + x)] = Tint(hat.Second, 0.8f, 255);

            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "hat_" + hat.Id };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            var sp = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 64f * S, 0, SpriteMeshType.FullRect);
            sp.name = "hat_" + hat.Id;
            return sp;
        }

        /// <summary>Which part of the hat covers this point (u,v in body pixels from the head centre, +v forward).</summary>
        private static int HatRegion(Clothing.HatStyle style, float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            bool E(float cx, float cy, float rx, float ry) => (u - cx) * (u - cx) / (rx * rx) + (v - cy) * (v - cy) / (ry * ry) <= 1f;
            switch (style)
            {
                case Clothing.HatStyle.Cap:
                    if (r <= 14.5f) return 2;
                    return E(0, 13, 11, 9) && v > 6 ? 1 : 0;
                case Clothing.HatStyle.BackwardsCap:
                    if (r <= 14.5f) return 2;
                    return E(0, -13, 11, 8) && v < -6 ? 1 : 0;
                case Clothing.HatStyle.Beanie:
                    if (r <= 13f) return 2;
                    return r <= 15f ? 3 : 0;   // folded cuff
                case Clothing.HatStyle.Bucket:
                    if (r <= 12f) return 2;
                    return r <= 19f ? 1 : 0;
                case Clothing.HatStyle.Fedora:
                    if (E(0, 0.5f, 11.5f, 13f)) return E(0, 0.5f, 10f, 11.5f) ? 2 : 3;
                    return E(0, 0, 18f, 19.5f) ? 1 : 0;
                case Clothing.HatStyle.Cowboy:
                    if (E(0, 0, 10.5f, 12.5f)) return Mathf.Abs(u) < 1.2f && Mathf.Abs(v) < 8f ? 3 : 2;   // crown with crease
                    return E(0, 0, 21f, 18f) && !E(0, 18.5f, 7f, 4f) && !E(0, -18.5f, 7f, 4f) ? 1 : 0;   // curled brim
            }
            return 0;
        }
    }
}
