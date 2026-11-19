using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>
    /// Title-screen background in the style of GTA V's main menu: full-screen stills of police
    /// work in New Halden (Resources/MenuSlides, captured in-game by the MenuSlideCapture editor
    /// tool), each slowly zooming and drifting (a Ken Burns move) before a soft crossfade to the
    /// next. Shuffled once per launch; works on unscaled time because the title pauses the game.
    /// </summary>
    public sealed class MenuSlideshow
    {
        public const float Hold = 7.5f, Fade = 1.6f;

        private readonly List<Texture2D> _slides;
        private readonly int[] _order;
        private float _start = -1f;

        public bool HasSlides => _slides.Count > 0;

        public MenuSlideshow()
        {
            _slides = Resources.LoadAll<Texture2D>("MenuSlides").OrderBy(t => t.name).ToList();
            _order = Enumerable.Range(0, _slides.Count).ToArray();
            var rng = new System.Random(System.Environment.TickCount);
            for (int i = _order.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (_order[i], _order[j]) = (_order[j], _order[i]); }
        }

        public void Restart() => _start = -1f;

        /// <summary>Draws the slideshow filling <paramref name="r"/> (call from OnGUI).</summary>
        public void Draw(Rect r)
        {
            if (_slides.Count == 0) { GUI.color = new Color(0.04f, 0.05f, 0.08f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white; return; }
            if (_start < 0f) _start = Time.unscaledTime;
            float t = Time.unscaledTime - _start;
            float step = Hold;
            int n = Mathf.FloorToInt(t / step);
            float local = t - n * step;                   // time into the current slide

            DrawSlide(r, n, local, 1f);
            if (local > step - Fade)                      // crossfade into the next slide
                DrawSlide(r, n + 1, local - step, Mathf.SmoothStep(0f, 1f, (local - (step - Fade)) / Fade));
            GUI.color = Color.white;
        }

        private void DrawSlide(Rect r, int index, float time, float alpha)
        {
            var tex = _slides[_order[((index % _order.Length) + _order.Length) % _order.Length]];
            // Each slide gets its own move: zoom in or out, drifting in one of four directions.
            int kind = ((index % 4) + 4) % 4;
            float life = Hold + Fade;
            float k = Mathf.Clamp01((time + Fade) / life);  // 0..1 across the slide's whole time on screen
            float zoom = kind % 2 == 0 ? Mathf.Lerp(1.04f, 1.16f, k) : Mathf.Lerp(1.16f, 1.04f, k);
            Vector2 drift = kind switch { 0 => new Vector2(1, 0.4f), 1 => new Vector2(-1, 0.2f), 2 => new Vector2(0.3f, -1), _ => new Vector2(-0.5f, 1) };
            drift *= Mathf.Lerp(-0.5f, 0.5f, k);

            // Cover the rect (crop to fill), then zoom about the centre plus the drift.
            float texAspect = (float)tex.width / tex.height, rectAspect = r.width / r.height;
            Vector2 size = texAspect > rectAspect ? new Vector2(rectAspect / texAspect, 1f) : new Vector2(1f, texAspect / rectAspect);
            size /= zoom;
            Vector2 slack = Vector2.one - size;
            Vector2 centre = new Vector2(0.5f, 0.5f) + Vector2.Scale(drift, slack * 0.5f);
            var uv = new Rect(centre.x - size.x * 0.5f, centre.y - size.y * 0.5f, size.x, size.y);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTextureWithTexCoords(r, tex, uv);
        }
    }
}
