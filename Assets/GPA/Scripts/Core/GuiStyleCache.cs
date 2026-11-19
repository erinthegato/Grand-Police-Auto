using System.Collections.Generic;
using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Derived IMGUI styles (same look, different alignment / word wrap), created once and reused.
    /// Building a GUIStyle inside OnGUI allocates every frame; HUDs ask this cache instead.
    /// </summary>
    public static class GuiStyleCache
    {
        private static readonly Dictionary<(GUIStyle, TextAnchor, bool), GUIStyle> Cache = new Dictionary<(GUIStyle, TextAnchor, bool), GUIStyle>();

        /// <summary><paramref name="source"/> with this alignment (and word wrap switched on if asked).</summary>
        public static GUIStyle With(GUIStyle source, TextAnchor alignment, bool wordWrap = false)
        {
            var key = (source, alignment, wordWrap);
            if (!Cache.TryGetValue(key, out var style))
            {
                style = new GUIStyle(source) { alignment = alignment };
                if (wordWrap) style.wordWrap = true;
                Cache[key] = style;
            }
            return style;
        }
    }
}
