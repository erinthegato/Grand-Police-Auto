using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Game
{
    /// <summary>
    /// Boot-up sequence before the title screen, in the spirit of late-'90s/early-2000s console
    /// intros: black, then the CODE 4 STUDIOS logo over a strobing red/blue lightbar background
    /// with VHS scanlines, then "presents", then a fade into the title. Police sirens wail in the
    /// background (muffled, drifting across the stereo field). Any key, click or button skips it.
    /// Runs on unscaled time with its own audio sources (the title screen pauses the game and the
    /// listener). Shown once per launch, only when the title screen is shown.
    /// </summary>
    [DefaultExecutionOrder(32000)]   // OnGUI runs last, so the intro draws over the title menu and HUD
    public sealed class BootSplash2D : MonoBehaviour
    {
        /// <summary>True while the intro covers the screen; the title menu ignores input meanwhile.</summary>
        public static bool Showing { get; private set; }
        private static bool _shownThisLaunch;

        private const float Black = 0.7f, LogoIn = 0.7f, LogoHold = 3.6f, PresentsAt = Black + LogoIn + LogoHold, PresentsHold = 2.4f, FadeOut = 0.9f;
        private const float Total = PresentsAt + PresentsHold + FadeOut;

        private float _t;
        private bool _skipping;
        private float _skipFade;
        private AudioSource _near, _far;
        private GUIStyle _logo, _studios, _presents, _tiny;
        private Texture2D _scan, _vignette;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Showing = false; _shownThisLaunch = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_shownThisLaunch || FindAnyObjectByType<BootSplash2D>() != null || FindAnyObjectByType<Officer2D>() == null) return;
            new GameObject("Boot Splash").AddComponent<BootSplash2D>();
        }

        private void Awake()
        {
            _shownThisLaunch = true;
            Showing = true;   // cover the very first frame so the title never flashes underneath
        }

        private bool _init;

        /// <summary>Runs on the first Update, after the title menu has decided whether it shows.</summary>
        private void Init()
        {
            _init = true;
            // Editor play-testing that skips the title screen skips the intro too.
            if (GameMenu2D.HasStarted && !GameMenu2D.TitleShowing) { Finish(); return; }
            _scan = MakeScanlines();
            _vignette = MakeVignette();
            var lib = GameAudio.Library;
            if (lib != null)
            {
                _near = MakeSource(lib.SirenWail, 0.42f, 2600f);
                _far = MakeSource(lib.SirenYelp != null ? lib.SirenYelp : lib.SirenPriority, 0.0f, 1400f);
                if (_far != null) _far.pitch = 0.97f;
            }
        }

        private AudioSource MakeSource(AudioClip clip, float volume, float cutoff)
        {
            if (clip == null) return null;
            var go = new GameObject("Splash siren");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip; src.loop = true; src.volume = volume; src.spatialBlend = 0f;
            src.ignoreListenerPause = true;   // the title screen pauses the listener
            src.playOnAwake = false;
            var lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = cutoff;      // muffled: sirens a few blocks away
            var echo = go.AddComponent<AudioEchoFilter>();
            echo.delay = 140f; echo.decayRatio = 0.35f; echo.wetMix = 0.35f; echo.dryMix = 1f;   // bouncing off buildings
            src.Play();
            return src;
        }

        private void Finish()
        {
            Showing = false;
            Destroy(gameObject);
        }

        private void Update()
        {
            useGUILayout = true;   // PerfTuning turns layout off for HUDs; GUI.depth ordering needs it here
            if (!_init) { Init(); if (!Showing) return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _t += dt;

            // Skip with any key / click / button (after a moment, so a held key from launch doesn't).
            if (!_skipping && _t > 0.5f && AnyPressed()) { _skipping = true; _skipFade = 0f; }
            if (_skipping) _skipFade += dt / 0.35f;

            // Sirens: the near one drifts from left to right as if passing a block over; a second,
            // fainter yelp answers from the other side and fades in.
            float fadeOut = _skipping ? 1f - Mathf.Clamp01(_skipFade) : 1f - Mathf.Clamp01((_t - (Total - FadeOut - 0.4f)) / (FadeOut + 0.4f));
            float fadeIn = Mathf.Clamp01(_t / 1.2f);
            if (_near != null)
            {
                _near.panStereo = Mathf.Lerp(-0.8f, 0.7f, Mathf.SmoothStep(0f, 1f, _t / Total));
                _near.volume = 0.42f * fadeIn * fadeOut;
            }
            if (_far != null)
            {
                _far.panStereo = Mathf.Lerp(0.6f, -0.3f, _t / Total);
                _far.volume = 0.16f * Mathf.Clamp01((_t - 2.2f) / 1.5f) * fadeOut;
            }

            if ((_skipping && _skipFade >= 1f) || _t >= Total) Finish();
        }

        private static bool AnyPressed()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            if (Mouse.current != null && (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)) return true;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
            var pad = Gamepad.current;
            return pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame
                                   || pad.buttonNorth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame);
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (!Showing) return;
            GUI.depth = -3000;
            if (!_init || _scan == null)   // first frame: just cover the screen
            {
                GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture); GUI.color = Color.white;
                return;
            }
            Styles();
            float W = Screen.width, H = Screen.height, s = H / 1080f;
            var full = new Rect(0, 0, W, H);

            GUI.color = Color.black;
            GUI.DrawTexture(full, Texture2D.whiteTexture);

            float t = _t;
            float logoA = Mathf.Clamp01((t - Black) / LogoIn) * (1f - Mathf.Clamp01((t - PresentsAt + 0.5f) / 0.5f));
            float presA = Mathf.Clamp01((t - PresentsAt) / 0.5f) * (1f - Mathf.Clamp01((t - PresentsAt - PresentsHold + 0.4f) / 0.4f));
            float strobeA = Mathf.Clamp01((t - Black) / 0.4f) * (1f - Mathf.Clamp01((t - (Total - FadeOut)) / FadeOut));

            // Lightbar strobe: red sweeps the left, blue the right, in the classic double-flash
            // pattern; the colours swap sides every cycle and wash softly into the centre.
            if (strobeA > 0f)
            {
                const float cycle = 0.9f;
                float c = t % cycle;
                bool swap = Mathf.FloorToInt(t / cycle) % 2 == 1;
                float a1 = Flash(c, 0f), a2 = Flash(c, cycle * 0.5f);
                Color red = new Color(0.95f, 0.06f, 0.1f), blue = new Color(0.08f, 0.22f, 1f);
                Color left = swap ? blue : red, right = swap ? red : blue;
                DrawWash(new Rect(0, 0, W * 0.62f, H), left, a1 * 0.85f * strobeA, true);
                DrawWash(new Rect(W * 0.38f, 0, W * 0.62f, H), right, a2 * 0.85f * strobeA, false);
                // A low ambient glow so it never drops to pure black between flashes.
                GUI.color = new Color(0.12f, 0.02f, 0.2f, 0.5f * strobeA);
                GUI.DrawTexture(full, Texture2D.whiteTexture);
            }

            // CODE 4 STUDIOS logo with a chromatic (red/blue) split that settles as it fades in.
            if (logoA > 0f)
            {
                float settle = 1f - Mathf.Clamp01((t - Black) / 1.6f);
                float pulse = 1f + 0.015f * Mathf.Sin(t * 6f);
                var logoRect = new Rect(0, H * 0.5f - 170f * s * pulse, W, 220f * s * pulse);
                _logo.fontSize = Mathf.RoundToInt(190 * s * pulse);
                float split = (4f + 26f * settle) * s;
                Text(logoRect, "CODE 4", _logo, new Color(1f, 0.15f, 0.2f, 0.7f * logoA), -split, 0);
                Text(logoRect, "CODE 4", _logo, new Color(0.2f, 0.45f, 1f, 0.7f * logoA), split, 0);
                Text(logoRect, "CODE 4", _logo, new Color(0f, 0f, 0f, 0.6f * logoA), 6f * s, 6f * s);
                Text(logoRect, "CODE 4", _logo, new Color(1f, 1f, 1f, logoA), 0, 0);

                // Rule and letter-spaced STUDIOS underneath.
                float barW = 640f * s * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - Black - 0.3f) / 0.8f));
                GUI.color = new Color(1f, 1f, 1f, 0.9f * logoA);
                GUI.DrawTexture(new Rect(W * 0.5f - barW * 0.5f, H * 0.5f + 62f * s, barW, 4f * s), Texture2D.whiteTexture);
                _studios.fontSize = Mathf.RoundToInt(54 * s);
                Text(new Rect(0, H * 0.5f + 78f * s, W, 80f * s), "S  T  U  D  I  O  S", _studios, new Color(1f, 1f, 1f, logoA), 0, 0);
                _tiny.fontSize = Mathf.RoundToInt(20 * s);
                Text(new Rect(0, H * 0.5f + 160f * s, W, 40f * s), "ALL UNITS  ·  CODE 4  ·  NO FURTHER ASSISTANCE REQUIRED", _tiny, new Color(1f, 1f, 1f, 0.55f * logoA), 0, 0);
            }

            if (presA > 0f)
            {
                _presents.fontSize = Mathf.RoundToInt(64 * s);
                var r = new Rect(0, H * 0.5f - 50f * s, W, 100f * s);
                Text(r, "presents", _presents, new Color(1f, 0.2f, 0.25f, 0.6f * presA), -3f * s, 0);
                Text(r, "presents", _presents, new Color(0.25f, 0.5f, 1f, 0.6f * presA), 3f * s, 0);
                Text(r, "presents", _presents, new Color(1f, 1f, 1f, presA), 0, 0);
            }

            // VHS scanlines and a dark vignette over everything.
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTextureWithTexCoords(full, _scan, new Rect(0, (t * 0.4f) % 1f, 1f, H / 4f));
            GUI.color = Color.white;
            GUI.DrawTexture(full, _vignette, ScaleMode.StretchToFill, true);

            // Final fade to black (natural end or skip), then the title takes over.
            float end = _skipping ? Mathf.Clamp01(_skipFade) : Mathf.Clamp01((t - (Total - FadeOut)) / FadeOut);
            if (end > 0f) { GUI.color = new Color(0, 0, 0, end); GUI.DrawTexture(full, Texture2D.whiteTexture); }

            if (t > 1.5f && !_skipping)
            {
                _tiny.fontSize = Mathf.RoundToInt(18 * s);
                Text(new Rect(0, H - 60f * s, W - 40f * s, 40f * s), "Press any button to skip", _tiny, new Color(1f, 1f, 1f, 0.4f * (0.6f + 0.4f * Mathf.Sin(t * 3f))), 0, 0, TextAnchor.MiddleRight);
            }
            GUI.color = Color.white;
        }

        /// <summary>Double flash: two quick bursts at the start of each half-cycle.</summary>
        private static float Flash(float c, float start)
        {
            float x = c - start;
            if (x < 0f) return 0f;
            float a = Burst(x, 0f) + Burst(x, 0.13f);
            return Mathf.Clamp01(a);
        }

        private static float Burst(float x, float at)
        {
            float d = x - at;
            if (d < 0f || d > 0.16f) return 0f;
            return d < 0.03f ? d / 0.03f : 1f - (d - 0.03f) / 0.13f;
        }

        private void DrawWash(Rect r, Color c, float a, bool fromLeft)
        {
            if (a <= 0.001f) return;
            // A few overlapping bands, brightest at the screen edge, fading toward the middle.
            const int bands = 8;
            for (int i = 0; i < bands; i++)
            {
                float k = (i + 1) / (float)bands;
                float w = r.width * k;
                var band = fromLeft ? new Rect(r.x, r.y, w, r.height) : new Rect(r.xMax - w, r.y, w, r.height);
                GUI.color = new Color(c.r, c.g, c.b, a / bands * 1.6f);
                GUI.DrawTexture(band, Texture2D.whiteTexture);
            }
        }

        private static void Text(Rect r, string text, GUIStyle style, Color colour, float dx, float dy, TextAnchor? anchor = null)
        {
            var old = style.alignment;
            if (anchor.HasValue) style.alignment = anchor.Value;
            style.normal.textColor = colour;
            GUI.color = Color.white;
            GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), text, style);
            style.alignment = old;
        }

        private void Styles()
        {
            if (_logo != null) return;
            _logo = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            _studios = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            _presents = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
            _tiny = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Overflow };
        }

        private static Texture2D MakeScanlines()
        {
            var t = new Texture2D(1, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            t.SetPixels32(new[] { new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 120), new Color32(0, 0, 0, 60) });
            t.Apply();
            return t;
        }

        private static Texture2D MakeVignette()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Clamp01((Mathf.Sqrt(u * u + v * v) - 0.55f) / 0.8f);
                    px[y * n + x] = new Color32(0, 0, 0, (byte)(d * d * 230));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        private void OnDestroy()
        {
            if (Showing) Showing = false;
            if (_scan != null) Destroy(_scan);
            if (_vignette != null) Destroy(_vignette);
        }
    }
}
