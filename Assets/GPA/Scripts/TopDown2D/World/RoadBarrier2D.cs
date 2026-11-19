using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// A "bridge closed" roadblock where a road meets the edge of the island: a row of red-and-white
    /// jersey barriers with cones and amber blinkers in front. Built from plain quads when enabled
    /// (like <see cref="Roof2D"/>), so it needs no art assets. The shoreline collider does the blocking.
    /// </summary>
    [ExecuteAlways]
    public sealed class RoadBarrier2D : MonoBehaviour
    {
        /// <summary>Length across the road (m). The barrier runs along local X; local +Y faces the city.</summary>
        public float Width = 20f;

        private Transform _parts;
        private SpriteRenderer[] _blinkers;

        private void OnEnable() => Rebuild();
        private void OnDisable() { if (_parts != null) DestroyImmediate(_parts.gameObject); _parts = null; }

        public void Rebuild()
        {
            if (_parts != null) DestroyImmediate(_parts.gameObject);
            _parts = new GameObject("BarrierParts") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable }.transform;
            _parts.SetParent(transform, false);

            Color red = new Color(0.85f, 0.15f, 0.12f), white = new Color(0.95f, 0.95f, 0.95f), cone = new Color(1f, 0.5f, 0.1f);
            float half = Width * 0.5f;
            // Shadow, then alternating red / white barrier blocks.
            Quad(new Vector2(0.15f, -0.15f), new Vector2(Width, 0.9f), new Color(0, 0, 0, 0.3f), Sorting2D.Shadows, 0);
            int blocks = Mathf.Max(2, Mathf.RoundToInt(Width / 1.6f));
            float bw = Width / blocks;
            for (int i = 0; i < blocks; i++)
                Quad(new Vector2(-half + bw * (i + 0.5f), 0), new Vector2(bw - 0.08f, 0.8f), i % 2 == 0 ? red : white, Sorting2D.Props, 10);

            // Cones a couple of metres in front, and blinkers on top of the barrier.
            for (float x = -half + 1.5f; x < half - 1f; x += 3f)
            {
                Quad(new Vector2(x, 2.2f), new Vector2(0.55f, 0.55f), cone, Sorting2D.Props, 11);
                Quad(new Vector2(x, 2.2f), new Vector2(0.45f, 0.12f), white, Sorting2D.Props, 12);
            }
            _blinkers = new SpriteRenderer[3];
            for (int i = 0; i < 3; i++)
                _blinkers[i] = Quad(new Vector2(-half * 0.6f + i * half * 0.6f, 0), new Vector2(0.35f, 0.35f), new Color(1f, 0.75f, 0.1f), Sorting2D.Props, 13);
        }

        private void Update()
        {
            if (_blinkers == null) return;
            bool on = Application.isPlaying ? (Time.time * 1.5f) % 1f < 0.5f : true;
            foreach (var b in _blinkers) if (b != null) b.enabled = on;
        }

        private SpriteRenderer Quad(Vector2 pos, Vector2 size, Color c, string layer, int order)
        {
            var go = new GameObject("q") { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            go.transform.SetParent(_parts, false);
            go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuildingInterior2D.Pixel; sr.color = c;
            Sorting2D.Apply(sr, layer, order);
            return sr;
        }
    }
}
