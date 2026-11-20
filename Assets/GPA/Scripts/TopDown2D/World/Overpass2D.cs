using System.Collections.Generic;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// A road bridge over another road, on a flat 2D map. Anything that drives or walks onto the deck
    /// from either end is lifted to the upper level for as long as it stays over the bridge:
    /// - its colliders move to the upper physics layer, so it only touches other things on the deck and
    ///   the parapets (which nothing below can hit), and
    /// - its renderers draw above the deck, while traffic underneath disappears below it.
    /// Things that come in from the sides (the road underneath) stay on the ground level.
    /// AI and weapons use <see cref="SameLevel(Collider2D, Component)"/> to ignore what's on the other level.
    /// The transform's local Y runs along the bridge; the deck is HalfWidth either side of it.
    /// </summary>
    public sealed class Overpass2D : MonoBehaviour
    {
        public const string RailLayerName = "Overpass";        // the deck's parapets
        public const string UpperLayerName = "OverpassUpper";  // bodies on a deck

        public float HalfLength = 17f;
        public float HalfWidth = 6.6f;
        /// <summary>How close to an end something must be when it first reaches the deck to count as coming onto it.</summary>
        public float EndZone = 2f;

        private static readonly List<Overpass2D> _all = new List<Overpass2D>();
        private static readonly HashSet<Rigidbody2D> _upper = new HashSet<Rigidbody2D>();
        private static int _railLayer = -2, _upperLayer = -2;

        private sealed class Lifted
        {
            public readonly List<(Collider2D col, int layer, LayerMask exclude)> Colliders = new List<(Collider2D, int, LayerMask)>();
            public readonly Dictionary<Renderer, (int layerId, int order, int appliedId, int appliedOrder)> Renderers = new Dictionary<Renderer, (int, int, int, int)>();
            public float Rescan;
        }

        private readonly Dictionary<Rigidbody2D, Lifted> _on = new Dictionary<Rigidbody2D, Lifted>();   // bodies over this deck (null = ground level)
        private readonly HashSet<Rigidbody2D> _seen = new HashSet<Rigidbody2D>();
        private readonly List<Rigidbody2D> _gone = new List<Rigidbody2D>();
        private readonly List<Collider2D> _attached = new List<Collider2D>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Collider2D> _hits = new List<Collider2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _all.Clear(); _upper.Clear(); _railLayer = _upperLayer = -2; }

        private static int RailLayer => _railLayer == -2 ? _railLayer = LayerMask.NameToLayer(RailLayerName) : _railLayer;
        private static int UpperLayer => _upperLayer == -2 ? _upperLayer = LayerMask.NameToLayer(UpperLayerName) : _upperLayer;
        private static bool Ready => RailLayer >= 0 && UpperLayer >= 0;

        private void OnEnable()
        {
            _all.Add(this);
            if (!Ready) { Debug.LogWarning($"[Overpass] Physics layers '{RailLayerName}'/'{UpperLayerName}' are missing: bridges stay flat."); return; }
            // Parapets only stop things on the deck.
            foreach (var col in GetComponentsInChildren<Collider2D>(true))
                if (col.gameObject.layer == RailLayer) col.excludeLayers = ~(1 << UpperLayer);
        }

        private void OnDisable()
        {
            _all.Remove(this);
            foreach (var kv in _on) if (kv.Value != null) Lower(kv.Key, kv.Value);
            _on.Clear();
        }

        // ------------------------------------------------------------------ queries for AI and weapons

        /// <summary>True when the point is over (or right at the ends of) any bridge deck.</summary>
        public static bool Covers(Vector2 p, float margin = 2f)
        {
            foreach (var o in _all)
            {
                Vector2 l = o.transform.InverseTransformPoint(p);
                if (Mathf.Abs(l.x) < o.HalfWidth + margin && Mathf.Abs(l.y) < o.HalfLength + margin) return true;
            }
            return false;
        }

        /// <summary>True when the body is up on a bridge deck.</summary>
        public static bool IsUpper(Rigidbody2D body) => body != null && _upper.Contains(body);

        private static bool LevelOf(Collider2D col)
        {
            if (col == null) return false;
            if (col.gameObject.layer == RailLayer && RailLayer >= 0) return true;
            return IsUpper(col.attachedRigidbody);
        }

        private static bool LevelOf(Component self)
        {
            if (self == null) return false;
            var rb = self as Rigidbody2D ?? self.GetComponentInParent<Rigidbody2D>();
            return IsUpper(rb);
        }

        /// <summary>
        /// True when the collider is on the same level as <paramref name="self"/> (both on a deck, or both
        /// on the ground). Everything counts as the same level when there are no bridges.
        /// </summary>
        public static bool SameLevel(Collider2D hit, Component self)
        {
            if (_all.Count == 0) return true;
            return LevelOf(hit) == LevelOf(self);
        }

        // ------------------------------------------------------------------ lifting and lowering

        private void FixedUpdate()
        {
            if (!Ready) return;
            var filter = ContactFilter2D.noFilter;
            Physics2D.OverlapBox(transform.position, new Vector2(HalfWidth * 2f, HalfLength * 2f), transform.eulerAngles.z, filter, _hits);
            _seen.Clear();
            foreach (var col in _hits)
            {
                Rigidbody2D rb = col.attachedRigidbody;
                if (rb == null || rb.bodyType == RigidbodyType2D.Static || !_seen.Add(rb)) continue;
                if (rb.TryGetComponent(out Car2D car) && car.Hover) continue;   // helicopters fly over everything
                if (_on.ContainsKey(rb)) continue;
                _on[rb] = ComesOnto(rb) ? Lift(rb) : null;
            }
            // Bodies no longer over the deck (or switched off, e.g. a driver getting into a car) come down.
            _gone.Clear();
            foreach (var kv in _on)
                if (kv.Key == null || !_seen.Contains(kv.Key) || !kv.Key.simulated) _gone.Add(kv.Key);
            foreach (var rb in _gone)
            {
                if (_on.TryGetValue(rb, out var lifted) && lifted != null) Lower(rb, lifted);
                _on.Remove(rb);
            }
            // Renderers added while up (doors swinging open, a gun drawn) are lifted too.
            float now = Time.time;
            foreach (var kv in _on)
            {
                if (kv.Value == null || now < kv.Value.Rescan) continue;
                kv.Value.Rescan = now + 0.25f;
                LiftRenderers(kv.Key, kv.Value);
            }
        }

        /// <summary>Came on at an end (driving or walking onto the bridge), or appeared next to something already on it (getting out of a car).</summary>
        private bool ComesOnto(Rigidbody2D rb)
        {
            Vector2 l = transform.InverseTransformPoint(rb.position);
            if (Mathf.Abs(l.y) > HalfLength - EndZone && Mathf.Abs(l.x) < HalfWidth + 1f) return true;
            Rigidbody2D nearest = null; float best = 4f;
            foreach (var kv in _on)
            {
                if (kv.Key == null || kv.Key == rb) continue;
                float d = Vector2.Distance(kv.Key.position, rb.position);
                if (d < best) { best = d; nearest = kv.Key; }
            }
            return nearest != null && _on[nearest] != null;
        }

        private Lifted Lift(Rigidbody2D rb)
        {
            var lifted = new Lifted();
            rb.GetAttachedColliders(_attached);
            int keep = (1 << UpperLayer) | (1 << RailLayer);
            foreach (var col in _attached)
            {
                lifted.Colliders.Add((col, col.gameObject.layer, col.excludeLayers));
                col.gameObject.layer = UpperLayer;
                col.excludeLayers = ~keep;
            }
            LiftRenderers(rb, lifted);
            _upper.Add(rb);
            return lifted;
        }

        private void LiftRenderers(Rigidbody2D rb, Lifted lifted)
        {
            rb.GetComponentsInChildren(true, _renderers);
            foreach (var r in _renderers)
            {
                if (r == null) continue;
                if (lifted.Renderers.TryGetValue(r, out var st) && r.sortingLayerID == st.appliedId && r.sortingOrder == st.appliedOrder) continue;
                // New, or put back on its usual layer by its own script: remember that, and lift it again.
                string group = GroupOf(r);
                if (group == null) continue;   // effects stay where they are (already above the deck)
                int layerId = r.sortingLayerID, order = r.sortingOrder;
                int within = Sorting2D.LayersAvailable ? order : order - (System.Array.IndexOf(Sorting2D.All, group) - 1) * 1000;
                int baseOrder = group == Sorting2D.People ? -150 : group == Sorting2D.Vehicles ? -250 : group == Sorting2D.Shadows ? -330 : -420;
                Sorting2D.Apply(r, Sorting2D.Roofs, baseOrder + Mathf.Clamp(within, -40, 45));
                lifted.Renderers[r] = (layerId, order, r.sortingLayerID, r.sortingOrder);
            }
        }

        /// <summary>The draw group a renderer is in, or null for groups that already draw above the deck.</summary>
        private static string GroupOf(Renderer r)
        {
            string g;
            if (Sorting2D.LayersAvailable) g = r.sortingLayerName;
            else
            {
                int index = Mathf.FloorToInt((r.sortingOrder + 500f) / 1000f) + 1;
                g = index >= 0 && index < Sorting2D.All.Length ? Sorting2D.All[index] : null;
            }
            return g == Sorting2D.Ground || g == Sorting2D.Props || g == Sorting2D.Shadows || g == Sorting2D.Vehicles || g == Sorting2D.People ? g : null;
        }

        private static void Lower(Rigidbody2D rb, Lifted lifted)
        {
            foreach (var (col, layer, exclude) in lifted.Colliders)
            {
                if (col == null) continue;
                col.gameObject.layer = layer;
                col.excludeLayers = exclude;
            }
            foreach (var kv in lifted.Renderers)
            {
                if (kv.Key == null) continue;
                if (kv.Key.sortingLayerID != kv.Value.appliedId || kv.Key.sortingOrder != kv.Value.appliedOrder) continue;   // its own script has moved it since
                kv.Key.sortingLayerID = kv.Value.layerId;
                kv.Key.sortingOrder = kv.Value.order;
            }
            if (rb != null) _upper.Remove(rb);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(HalfWidth * 2f, HalfLength * 2f, 0f));
        }
    }
}
