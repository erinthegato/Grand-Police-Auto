using System.Collections.Generic;
using UnityEngine;

namespace GPA.Mdt
{
    /// <summary>
    /// Controller / arrow-key navigation for the immediate-mode MDT. Every widget registers its
    /// rect while it is drawn; a d-pad press moves focus to the nearest widget in that direction,
    /// and Submit "clicks" the focused one the next time it is drawn. Widgets inside a scroll view
    /// belong to a group so focus can walk into rows that are scrolled out of sight.
    /// </summary>
    internal sealed class FocusNav
    {
        public struct Item
        {
            public string Id;
            public Rect Rect;   // panel (reference-resolution) coordinates
            public int Layer;
            public int Group;
            public Rect Clip;
        }

        private static readonly Rect NoClip = new Rect(-100000, -100000, 200000, 200000);

        private List<Item> _building = new List<Item>();
        private List<Item> _items = new List<Item>();
        private readonly Texture2D _outline;
        private string _activate;
        private bool _repaint;
        private Vector2 _offset;
        private Rect _clip = NoClip;
        private int _group;

        /// <summary>Focused widget id (null when nothing is focused).</summary>
        public string Focused;
        /// <summary>Draw the focus outline (keyboard/controller use). Pointer clicks turn it off.</summary>
        public bool ShowHighlight;
        /// <summary>Layer that widgets drawn from now on belong to (0 = screen, 1 = on-screen keyboard).</summary>
        public int Layer;
        /// <summary>Only this layer can take focus (the on-screen keyboard is modal for the d-pad).</summary>
        public int ActiveLayer;

        public FocusNav(Texture2D outline) => _outline = outline;

        public IReadOnlyList<Item> Items => _items;

        public void BeginFrame()
        {
            _repaint = Event.current.type == EventType.Repaint;
            if (_repaint) _building.Clear();
            _offset = Vector2.zero;
            _clip = NoClip;
            _group = 0;
            Layer = 0;
        }

        public void EndFrame()
        {
            if (!_repaint) return;
            (_items, _building) = (_building, _items);
            _activate = null; // a widget that was not drawn this frame can't be clicked later
        }

        public void PushGroup(int group, Rect view, Vector2 scroll)
        {
            _group = group;
            _offset = view.position - scroll;
            _clip = view;
        }

        public void PopGroup()
        {
            _group = 0;
            _offset = Vector2.zero;
            _clip = NoClip;
        }

        /// <summary>Records a focusable widget drawn at <paramref name="local"/> (current GUI space).</summary>
        public void Register(string id, Rect local)
        {
            if (!_repaint) return;
            _building.Add(new Item { Id = id, Rect = new Rect(local.position + _offset, local.size), Layer = Layer, Group = _group, Clip = _clip });
            if (ShowHighlight && id == Focused)
            {
                const float t = 3f;
                GUI.DrawTexture(new Rect(local.x - t, local.y - t, local.width + 2 * t, t), _outline);
                GUI.DrawTexture(new Rect(local.x - t, local.yMax, local.width + 2 * t, t), _outline);
                GUI.DrawTexture(new Rect(local.x - t, local.y, t, local.height), _outline);
                GUI.DrawTexture(new Rect(local.xMax, local.y, t, local.height), _outline);
            }
        }

        /// <summary>True once if Submit was pressed on this widget.</summary>
        public bool TakeActivation(string id)
        {
            if (_activate == null || _activate != id) return false;
            _activate = null;
            return true;
        }

        public void Activate()
        {
            ShowHighlight = true;
            if (!IsFocusable(Focused)) { FocusFirst(); return; }
            _activate = Focused;
        }

        public bool TryGet(string id, out Item item)
        {
            foreach (var it in _items)
                if (it.Id == id) { item = it; return true; }
            item = default;
            return false;
        }

        private bool IsFocusable(string id) => id != null && TryGet(id, out var it) && it.Layer == ActiveLayer;

        /// <summary>Focuses the top-left widget of the active layer.</summary>
        public void FocusFirst()
        {
            Item? best = null;
            foreach (var it in _items)
            {
                if (it.Layer != ActiveLayer || !Visible(it)) continue;
                if (best == null || it.Rect.y < best.Value.Rect.y - 4f || (Mathf.Abs(it.Rect.y - best.Value.Rect.y) <= 4f && it.Rect.x < best.Value.Rect.x)) best = it;
            }
            Focused = best?.Id;
        }

        public void FocusFirstWithPrefix(string prefix)
        {
            foreach (var it in _items)
                if (it.Layer == ActiveLayer && it.Id.StartsWith(prefix, System.StringComparison.Ordinal)) { Focused = it.Id; return; }
        }

        private static bool Visible(Item it) => it.Clip.Contains(it.Rect.center);

        /// <summary>Moves focus one step. <paramref name="dir"/> is in GUI space (y down).</summary>
        public bool Move(Vector2Int dir)
        {
            ShowHighlight = true;
            if (!TryGet(Focused, out var cur) || cur.Layer != ActiveLayer) { FocusFirst(); return Focused != null; }

            Vector2 from = cur.Rect.center;
            string best = null;
            float bestScore = float.MaxValue;
            foreach (var it in _items)
            {
                if (it.Layer != ActiveLayer || it.Id == cur.Id) continue;
                // Off-screen widgets are only reachable inside the scroll view we are already in.
                if (!Visible(it) && it.Group != cur.Group) continue;
                Vector2 d = it.Rect.center - from;
                float along = dir.x != 0 ? d.x * dir.x : d.y * dir.y;
                if (along <= 2f) continue;
                // Gap between the two rects across the direction of travel (0 when they line up).
                float gap = dir.x != 0
                    ? Mathf.Max(0f, Mathf.Max(it.Rect.yMin - cur.Rect.yMax, cur.Rect.yMin - it.Rect.yMax))
                    : Mathf.Max(0f, Mathf.Max(it.Rect.xMin - cur.Rect.xMax, cur.Rect.xMin - it.Rect.xMax));
                float centreOffset = dir.x != 0 ? Mathf.Abs(d.y) : Mathf.Abs(d.x);
                // Widgets that line up with this one (same row / column) always win over diagonal
                // ones, so focus walks along a keyboard row instead of zigzagging into the next.
                float score = gap > 0f ? 100000f + along + gap * 4f : along + centreOffset * 0.3f;
                if (score < bestScore) { bestScore = score; best = it.Id; }
            }
            if (best == null) return false;
            Focused = best;
            return true;
        }
    }
}
