using System.Collections.Generic;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// One whole roof per building: a single slab with a parapet, a drop shadow and a few rooftop
    /// fittings (AC units, vents, hatch, helipad), instead of a grid of 4 m roof tiles. The pieces
    /// are rebuilt from these settings whenever the object is enabled, so nothing has to be saved
    /// as assets. All pieces are baked into two small meshes (the street shadow and the roof
    /// itself) instead of one sprite object per piece: a gravel roof used to be 60+ objects.
    /// </summary>
    [ExecuteAlways]
    public sealed class Roof2D : MonoBehaviour
    {
        public enum Style { Tar, Gravel, Light, Brownstone, Police, Tower, Mansion, Cabin }   // append only: scenes store the number

        public Vector2 Size = new Vector2(8, 8);
        public Style Kind = Style.Tar;
        public Color Tint = Color.white;
        public int Seed;

        private Transform _parts;
        private readonly List<(Rect r, Color c, int order)> _shadow = new List<(Rect, Color, int)>(), _roof = new List<(Rect, Color, int)>();
        private static Material _material;
        private static bool _materialInPlay;

        private void OnEnable() => Rebuild();
        private void OnDisable() => Clear();

        private void Clear()
        {
            if (_parts == null) return;
            foreach (var mf in _parts.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh != null) DestroyImmediate(mf.sharedMesh);
            DestroyImmediate(_parts.gameObject);
            _parts = null;
        }

        public void Rebuild()
        {
            Clear();
            var go = new GameObject("RoofParts") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            _parts = go.transform;
            _parts.SetParent(transform, false);
            _shadow.Clear(); _roof.Clear();
            Design();
            Bake("Shadow", _shadow, Sorting2D.Shadows);
            Bake("Roof", _roof, Sorting2D.Roofs);
        }

        private void Design()
        {
            var rng = new System.Random(Seed);

            Color baseC = BaseColour() * Tint; baseC.a = 1f;
            Color edge = baseC * 0.72f; edge.a = 1f;
            Color dark = baseC * 0.5f; dark.a = 1f;
            float w = Size.x, h = Size.y;

            // Drop shadow on the street, slab, parapet.
            Quad(new Rect(-w / 2 + 0.6f, -h / 2 - 0.6f, w, h), new Color(0, 0, 0, 0.28f), Sorting2D.Shadows, -5);
            if (Kind == Style.Mansion || Kind == Style.Cabin) { Pitched(rng, baseC, edge); return; }
            Quad(new Rect(-w / 2, -h / 2, w, h), baseC, Sorting2D.Roofs, 0);
            float p = Kind == Style.Brownstone ? 0.5f : 0.35f;
            Quad(new Rect(-w / 2, -h / 2, w, p), edge, Sorting2D.Roofs, 2);
            Quad(new Rect(-w / 2, h / 2 - p, w, p), edge, Sorting2D.Roofs, 2);
            Quad(new Rect(-w / 2, -h / 2, p, h), edge, Sorting2D.Roofs, 2);
            Quad(new Rect(w / 2 - p, -h / 2, p, h), edge, Sorting2D.Roofs, 2);
            // Inner shade under the north/west parapet gives the slab some depth.
            Quad(new Rect(-w / 2 + p, h / 2 - p - 0.3f, w - 2 * p, 0.3f), new Color(0, 0, 0, 0.15f), Sorting2D.Roofs, 1);
            Quad(new Rect(-w / 2 + p, -h / 2 + p, 0.3f, h - 2 * p), new Color(0, 0, 0, 0.12f), Sorting2D.Roofs, 1);

            if (Kind == Style.Gravel)   // speckle
                for (int i = 0; i < (int)(w * h * 0.25f); i++)
                {
                    float x = R(rng, -w / 2 + p, w / 2 - p - 0.2f), y = R(rng, -h / 2 + p, h / 2 - p - 0.2f);
                    Quad(new Rect(x, y, 0.15f, 0.15f), baseC * (rng.NextDouble() < 0.5 ? 0.85f : 1.12f), Sorting2D.Roofs, 1);
                }

            if (Kind == Style.Police) { Helipad(); Unit(new Vector2(-w / 2 + 2.2f, h / 2 - 2.2f), 1.8f, 1.4f, dark); Unit(new Vector2(w / 2 - 2.2f, h / 2 - 2.2f), 1.8f, 1.4f, dark); return; }

            if (Kind == Style.Tower && w >= 10 && h >= 10)   // mechanical penthouse
            {
                Vector2 pc = new Vector2(R(rng, -w * 0.15f, w * 0.15f), R(rng, -h * 0.15f, h * 0.15f));
                Quad(new Rect(pc.x - w * 0.2f + 0.4f, pc.y - h * 0.2f - 0.4f, w * 0.4f, h * 0.4f), new Color(0, 0, 0, 0.3f), Sorting2D.Roofs, 3);
                Quad(new Rect(pc.x - w * 0.2f, pc.y - h * 0.2f, w * 0.4f, h * 0.4f), baseC * 1.08f, Sorting2D.Roofs, 4);
                Quad(new Rect(pc.x - w * 0.2f, pc.y - h * 0.2f, w * 0.4f, 0.25f), edge, Sorting2D.Roofs, 5);
            }

            // Hatch, AC units and vents, kept off the parapet.
            Unit(new Vector2(R(rng, -w / 4, w / 4), R(rng, -h / 4, h / 4)), 1.2f, 1.2f, edge);
            int units = Mathf.Clamp((int)(w * h / 60f), 1, 6);
            for (int i = 0; i < units; i++)
                Unit(new Vector2(R(rng, -w / 2 + 1.5f, w / 2 - 1.5f), R(rng, -h / 2 + 1.5f, h / 2 - 1.5f)), 1.4f, 1f, new Color(0.78f, 0.79f, 0.8f));
            for (int i = 0; i < units + 1; i++)
                Quad(new Rect(R(rng, -w / 2 + 1f, w / 2 - 1.4f), R(rng, -h / 2 + 1f, h / 2 - 1.4f), 0.4f, 0.4f), dark, Sorting2D.Roofs, 4);
        }

        private Color BaseColour()
        {
            switch (Kind)
            {
                case Style.Tar: return new Color(0.3f, 0.31f, 0.33f);
                case Style.Gravel: return new Color(0.52f, 0.52f, 0.5f);
                case Style.Light: case Style.Tower: return new Color(0.74f, 0.75f, 0.76f);
                case Style.Brownstone: return new Color(0.55f, 0.36f, 0.3f);
                case Style.Police: return new Color(0.5f, 0.57f, 0.7f);
                case Style.Mansion: return Seed % 2 == 0 ? new Color(0.42f, 0.45f, 0.52f) : new Color(0.66f, 0.38f, 0.3f);   // slate or terracotta
                case Style.Cabin: return new Color(0.48f, 0.34f, 0.22f);   // cedar shingles
            }
            return Color.gray;
        }

        private static Color K(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        /// <summary>
        /// Pitched house roof (mansions, cabins): two slopes either side of a ridge, the sunny one
        /// lighter, with shingle courses and stone chimneys. Mansions add a cross-gable wing.
        /// </summary>
        private void Pitched(System.Random rng, Color baseC, Color edge)
        {
            float w = Size.x, h = Size.y, e = 0.3f;
            Quad(new Rect(-w / 2, -h / 2, w, h), edge, Sorting2D.Roofs, 0);   // eaves
            Gable(new Rect(-w / 2 + e, -h / 2 + e, w - 2 * e, h - 2 * e), w >= h, baseC, 1);
            if (Kind == Style.Mansion)
            {
                // Cross-gable wing over the middle, ridge running the other way.
                float cw = (w >= h ? w : h) * 0.3f;
                Rect wing = w >= h ? new Rect(-cw / 2, -h / 2 - 0.2f, cw, h + 0.4f) : new Rect(-w / 2 - 0.2f, -cw / 2, w + 0.4f, cw);
                Quad(new Rect(wing.x + 0.3f, wing.y - 0.3f, wing.width, wing.height), new Color(0, 0, 0, 0.25f), Sorting2D.Roofs, 3);
                Quad(wing, edge, Sorting2D.Roofs, 4);
                Gable(new Rect(wing.x + e, wing.y + e, wing.width - 2 * e, wing.height - 2 * e), !(w >= h), baseC, 5);
                Chimney(w >= h ? new Vector2(-w / 2 + 2f, h * 0.18f) : new Vector2(w * 0.18f, -h / 2 + 2f));
                Chimney(w >= h ? new Vector2(w / 2 - 2f, -h * 0.18f) : new Vector2(-w * 0.18f, h / 2 - 2f));
            }
            else
                Chimney(w >= h ? new Vector2(w / 2 - 1.6f, R(rng, -h * 0.2f, h * 0.2f)) : new Vector2(R(rng, -w * 0.2f, w * 0.2f), h / 2 - 1.6f));
        }

        private void Gable(Rect r, bool ridgeAlongX, Color baseC, int order)
        {
            Color lit = K(baseC, 1.12f), shade = K(baseC, 0.78f);
            if (ridgeAlongX)
            {
                float mid = r.center.y;
                Quad(new Rect(r.x, mid, r.width, r.yMax - mid), lit, Sorting2D.Roofs, order);
                Quad(new Rect(r.x, r.y, r.width, mid - r.y), shade, Sorting2D.Roofs, order);
                for (float d = 0.7f; d < r.height / 2; d += 0.7f)
                {
                    Quad(new Rect(r.x, mid + d, r.width, 0.08f), K(lit, 0.9f), Sorting2D.Roofs, order + 1);
                    Quad(new Rect(r.x, mid - d, r.width, 0.08f), K(shade, 0.86f), Sorting2D.Roofs, order + 1);
                }
                Quad(new Rect(r.x, mid - 0.14f, r.width, 0.28f), K(baseC, 0.55f), Sorting2D.Roofs, order + 2);   // ridge
            }
            else
            {
                float mid = r.center.x;
                Quad(new Rect(r.x, r.y, mid - r.x, r.height), lit, Sorting2D.Roofs, order);
                Quad(new Rect(mid, r.y, r.xMax - mid, r.height), shade, Sorting2D.Roofs, order);
                for (float d = 0.7f; d < r.width / 2; d += 0.7f)
                {
                    Quad(new Rect(mid - d, r.y, 0.08f, r.height), K(lit, 0.9f), Sorting2D.Roofs, order + 1);
                    Quad(new Rect(mid + d, r.y, 0.08f, r.height), K(shade, 0.86f), Sorting2D.Roofs, order + 1);
                }
                Quad(new Rect(mid - 0.14f, r.y, 0.28f, r.height), K(baseC, 0.55f), Sorting2D.Roofs, order + 2);
            }
        }

        private void Chimney(Vector2 c)
        {
            Quad(new Rect(c.x - 0.5f + 0.35f, c.y - 0.5f - 0.35f, 1f, 1f), new Color(0, 0, 0, 0.35f), Sorting2D.Roofs, 8);
            Quad(new Rect(c.x - 0.5f, c.y - 0.5f, 1f, 1f), new Color(0.56f, 0.53f, 0.5f), Sorting2D.Roofs, 9);
            Quad(new Rect(c.x - 0.3f, c.y - 0.3f, 0.6f, 0.6f), new Color(0.16f, 0.15f, 0.15f), Sorting2D.Roofs, 10);
        }

        private void Unit(Vector2 c, float w, float h, Color colour)
        {
            Quad(new Rect(c.x - w / 2 + 0.25f, c.y - h / 2 - 0.25f, w, h), new Color(0, 0, 0, 0.3f), Sorting2D.Roofs, 5);
            Quad(new Rect(c.x - w / 2, c.y - h / 2, w, h), colour, Sorting2D.Roofs, 6);
            Quad(new Rect(c.x - w * 0.3f, c.y - h * 0.3f, w * 0.6f, h * 0.6f), colour * 0.8f + new Color(0, 0, 0, 0.2f), Sorting2D.Roofs, 7);
        }

        private void Helipad()
        {
            float s = Mathf.Min(Mathf.Min(Size.x, Size.y) - 3f, 11f);
            Quad(new Rect(-s / 2, -s / 2, s, s), new Color(0.95f, 0.8f, 0.2f), Sorting2D.Roofs, 3);
            Quad(new Rect(-s / 2 + 0.4f, -s / 2 + 0.4f, s - 0.8f, s - 0.8f), new Color(0.22f, 0.24f, 0.27f), Sorting2D.Roofs, 4);
            float hw = s * 0.35f, hh = s * 0.45f, t = s * 0.1f;
            Quad(new Rect(-hw / 2, -hh / 2, t, hh), Color.white, Sorting2D.Roofs, 5);
            Quad(new Rect(hw / 2 - t, -hh / 2, t, hh), Color.white, Sorting2D.Roofs, 5);
            Quad(new Rect(-hw / 2, -t / 2, hw, t), Color.white, Sorting2D.Roofs, 5);
        }

        private void Quad(Rect r, Color c, string layer, int order) =>
            (layer == Sorting2D.Shadows ? _shadow : _roof).Add((r, c, order));

        /// <summary>One mesh per sorting layer; pieces are drawn in their old sorting order (triangle order = draw order).</summary>
        private void Bake(string name, List<(Rect r, Color c, int order)> pieces, string layer)
        {
            if (pieces.Count == 0) return;
            pieces.Sort((a, b) => a.order.CompareTo(b.order));   // List.Sort isn't stable, but equal orders never overlap in a way that matters
            var verts = new Vector3[pieces.Count * 4];
            var cols = new Color[pieces.Count * 4];
            var uvs = new Vector2[pieces.Count * 4];
            var tris = new int[pieces.Count * 6];
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Sprite px = BuildingInterior2D.Pixel;
            Vector2 uv = px != null && px.uv.Length > 0 ? px.uv[0] : Vector2.zero;
            if (px != null && px.uv.Length >= 4) uv = (px.uv[0] + px.uv[1] + px.uv[2] + px.uv[3]) * 0.25f;
            for (int i = 0; i < pieces.Count; i++)
            {
                Rect r = pieces[i].r; int v = i * 4;
                verts[v] = new Vector3(r.xMin, r.yMin); verts[v + 1] = new Vector3(r.xMin, r.yMax);
                verts[v + 2] = new Vector3(r.xMax, r.yMax); verts[v + 3] = new Vector3(r.xMax, r.yMin);
                Color c = linear ? pieces[i].c.linear : pieces[i].c;   // sprite colours are converted to linear for us; vertex colours are not
                for (int k = 0; k < 4; k++) { cols[v + k] = c; uvs[v + k] = uv; }
                int t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = "Roof " + name, hideFlags = HideFlags.DontSave, vertices = verts, colors = cols, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            go.transform.SetParent(_parts, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SharedMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Sorting2D.Apply(mr, layer, 0);
        }

        /// <summary>An unlit vertex-colour material (no texture: the shader's default white), shared by every roof.</summary>
        private static Material SharedMaterial()
        {
            // Edit-mode and play-mode materials are kept apart: one carried over from edit mode
            // into play mode stops drawing.
            if (_material != null && _materialInPlay == Application.isPlaying) return _material;
            _materialInPlay = Application.isPlaying;
            // Sprites/Default (unlit, vertex colour) — the same shader the city's road and pond meshes use.
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                var probe = new GameObject("probe") { hideFlags = HideFlags.HideAndDontSave };
                shader = probe.AddComponent<SpriteRenderer>().sharedMaterial.shader;
                DestroyImmediate(probe);
            }
            _material = new Material(shader) { name = "Roof (sprite material)", hideFlags = HideFlags.DontSave };
            return _material;
        }

        private static float R(System.Random rng, float a, float b) => b <= a ? a : a + (float)rng.NextDouble() * (b - a);
    }
}
