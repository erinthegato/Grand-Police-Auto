using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Shared scaling for the IMGUI HUDs and menus. On a PC/console the UI is laid out on a 1080-high
    /// virtual canvas. Phones have small screens (an iPhone SE is 4.7", 1334×750), where that would
    /// make text tiny, so handhelds use a shorter virtual canvas (everything draws ~40% bigger) and
    /// keep clear of notches / home indicators via <see cref="Screen.safeArea"/>.
    /// </summary>
    public static class UiScale
    {
        public const float Reference = 1080f;
        /// <summary>Virtual canvas height on phones for HUDs and small menus (SE: ~1:1 with real pixels).</summary>
        public const float HandheldHeight = 760f;

        /// <summary>Lets the editor preview the phone layout (Tools ▸ GPA ▸ Preview phone UI).</summary>
        public static bool ForceHandheld;

        public static bool Handheld => ForceHandheld || Application.isMobilePlatform;

        /// <summary>Safe area in IMGUI coordinates (origin top-left).</summary>
        public static Rect Safe
        {
            get
            {
                Rect s = Screen.safeArea;
                if (s.width <= 0 || s.height <= 0) return new Rect(0, 0, Screen.width, Screen.height);
                return new Rect(s.x, Screen.height - s.yMax, s.width, s.height);
            }
        }

        /// <summary>
        /// Virtual canvas height. <paramref name="handheldHeight"/> lets a big screen (the MDT) ask for
        /// more room than a HUD; it's ignored on PC.
        /// </summary>
        public static float VirtualHeight(float handheldHeight = HandheldHeight) => Handheld ? handheldHeight : Reference;

        /// <summary>
        /// Sets GUI.matrix for the canvas and returns the old one; <paramref name="size"/> is the
        /// virtual canvas (lay out with size.x / size.y instead of Screen.width / 1080).
        /// </summary>
        public static Matrix4x4 Begin(out Vector2 size, float handheldHeight = HandheldHeight)
        {
            Rect safe = Handheld ? Safe : new Rect(0, 0, Screen.width, Screen.height);
            float vh = VirtualHeight(handheldHeight);
            float scale = safe.height / vh;
            size = new Vector2(safe.width / scale, vh);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3(safe.x, safe.y, 0f), Quaternion.identity, new Vector3(scale, scale, 1f));
            return old;
        }

        /// <summary>Screen point (pixels, origin bottom-left, e.g. WorldToScreenPoint) → canvas point.</summary>
        public static Vector2 ScreenToCanvas(Vector3 screen, float handheldHeight = HandheldHeight)
        {
            Rect safe = Handheld ? Safe : new Rect(0, 0, Screen.width, Screen.height);
            float f = safe.height / VirtualHeight(handheldHeight);
            return new Vector2((screen.x - safe.x) / f, (Screen.height - screen.y - safe.y) / f);
        }

        /// <summary>Canvas point → GUI pixel point (for GUIUtility.RotateAroundPivot and the like).</summary>
        public static Vector2 CanvasToGui(Vector2 canvas, float handheldHeight = HandheldHeight)
        {
            Rect safe = Handheld ? Safe : new Rect(0, 0, Screen.width, Screen.height);
            float f = safe.height / VirtualHeight(handheldHeight);
            return new Vector2(safe.x + canvas.x * f, safe.y + canvas.y * f);
        }

        /// <summary>Real pixels per virtual pixel (for code that draws outside the matrix).</summary>
        public static float Factor(float handheldHeight = HandheldHeight) =>
            (Handheld ? Safe.height : Screen.height) / VirtualHeight(handheldHeight);
    }
}
