using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Chunked culling for the static city (buildings and roofs, street props, parks, the forest).
    /// The city has well over ten thousand sprite renderers, but only a few hundred are ever on
    /// screen. Renderers are grouped into 48 m chunks once at start; each frame only the chunks
    /// around the camera view stay enabled, so Unity's per-renderer culling and sorting skip the
    /// rest entirely. Colliders are untouched (physics still works everywhere).
    /// </summary>
    public sealed class WorldCulling2D : MonoBehaviour
    {
        private const float ChunkSize = 48f;
        private const float Margin = 20f;    // keep a border loaded so nothing pops in at the screen edge
        private static readonly string[] Roots = { "Buildings", "Props", "Parks", "Forest" };

        private readonly Dictionary<Vector2Int, List<Renderer>> _chunks = new Dictionary<Vector2Int, List<Renderer>>();
        private readonly HashSet<Vector2Int> _visible = new HashSet<Vector2Int>();
        private readonly List<Vector2Int> _scratch = new List<Vector2Int>();
        private RectInt _lastRange = new RectInt(int.MinValue, int.MinValue, 0, 0);
        private Camera _cam;
        private bool _ready;

        public int RendererCount { get; private set; }
        public int VisibleChunks => _visible.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<WorldCulling2D>() != null || FindAnyObjectByType<Camera2DFollow>() == null) return;
            new GameObject("World Culling").AddComponent<WorldCulling2D>();
        }

        private IEnumerator Start()
        {
            // Let roofs, streetlight glows and other start-up parts get built first.
            yield return null;
            yield return null;
            foreach (string name in Roots)
            {
                var root = GameObject.Find(name);
                if (root == null) continue;
                foreach (var r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled) continue;
                    Vector2Int key = ChunkOf(r.bounds.center);
                    if (!_chunks.TryGetValue(key, out var list)) _chunks[key] = list = new List<Renderer>();
                    list.Add(r);
                    r.enabled = false;
                    RendererCount++;
                }
            }
            _ready = true;
        }

        private static Vector2Int ChunkOf(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x / ChunkSize), Mathf.FloorToInt(p.y / ChunkSize));

        private void LateUpdate()
        {
            if (!_ready) return;
            if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
            float h = _cam.orthographicSize + Margin, w = _cam.orthographicSize * _cam.aspect + Margin;
            Vector2 c = _cam.transform.position;
            Vector2Int a = ChunkOf(new Vector2(c.x - w, c.y - h)), b = ChunkOf(new Vector2(c.x + w, c.y + h));
            var range = new RectInt(a.x, a.y, b.x - a.x + 1, b.y - a.y + 1);
            if (range.Equals(_lastRange)) return;
            _lastRange = range;

            // Hide chunks that left the range.
            _scratch.Clear();
            foreach (var k in _visible) if (!range.Contains(k)) _scratch.Add(k);
            foreach (var k in _scratch) { Set(k, false); _visible.Remove(k); }
            // Show chunks that entered it.
            for (int y = range.yMin; y < range.yMax; y++)
            for (int x = range.xMin; x < range.xMax; x++)
            {
                var k = new Vector2Int(x, y);
                if (_visible.Add(k)) Set(k, true);
            }
        }

        private void Set(Vector2Int key, bool on)
        {
            if (!_chunks.TryGetValue(key, out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var r = list[i];
                if (r == null) { list.RemoveAt(i); continue; }   // e.g. roof parts rebuilt after an interior visit
                r.enabled = on;
            }
        }
    }
}
