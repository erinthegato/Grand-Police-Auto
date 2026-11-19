using System.Collections.Generic;
using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Day/night cycle for the top-down city, driven by the game clock (<see cref="ITimeOfDay"/>).
    /// - A tinted overlay follows the camera: clear by day, orange at dawn and dusk, dark blue at night.
    /// - Streetlights get a warm pool of light that fades in at dusk.
    /// - Cars get headlight beams and tail lights after dark.
    /// - A small clock is drawn at the top of the screen.
    /// Created automatically in any scene that has a <see cref="Camera2DFollow"/>.
    /// </summary>
    public sealed class DayNight2D : MonoBehaviour
    {
        // Overlay colour by hour (alpha = how dark). Interpolated between keys; wraps at 24.
        private static readonly (float hour, Color c)[] Keys =
        {
            (0f,  new Color(0.03f, 0.05f, 0.16f, 0.64f)),
            (4.5f,new Color(0.03f, 0.05f, 0.16f, 0.62f)),
            (6f,  new Color(0.55f, 0.28f, 0.18f, 0.30f)),   // dawn
            (7.5f,new Color(0.60f, 0.45f, 0.30f, 0.06f)),
            (9f,  new Color(1f, 1f, 1f, 0f)),               // day
            (17f, new Color(1f, 1f, 1f, 0f)),
            (18.5f,new Color(0.62f, 0.32f, 0.18f, 0.20f)),  // golden hour
            (19.7f,new Color(0.40f, 0.18f, 0.30f, 0.38f)),  // dusk
            (21f, new Color(0.03f, 0.05f, 0.16f, 0.60f)),   // night
            (24f, new Color(0.03f, 0.05f, 0.16f, 0.64f)),
        };

        public static DayNight2D Instance { get; private set; }
        /// <summary>Set while the player is somewhere with its own lighting (e.g. an underground garage).</summary>
        public static bool Suppressed;
        /// <summary>0 = full daylight, 1 = darkest night.</summary>
        public float Darkness { get; private set; }

        private Camera _cam;
        private SpriteRenderer _overlay;
        private readonly List<SpriteRenderer> _streetGlows = new List<SpriteRenderer>();
        private readonly Dictionary<Car2D, CarLights> _cars = new Dictionary<Car2D, CarLights>();
        private float _appliedDark = -1f, _scanTimer;
        private Sprite _white, _glow, _beam;
        private GUIStyle _clockStyle;

        private sealed class CarLights { public SpriteRenderer[] Beams, Tails; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null || Object.FindAnyObjectByType<Camera2DFollow>() == null) return;
            new GameObject("Day Night Cycle").AddComponent<DayNight2D>();
        }

        private void Awake()
        {
            Instance = this;
            _white = MakeSprite(4, (u, v) => 1f);
            _glow = MakeSprite(64, (u, v) => { float d = Mathf.Clamp01(1f - new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f); return d * d; });
            // Headlight beam: narrow at the bottom (car), wide and faint at the top.
            _beam = MakeSprite(64, (u, v) =>
            {
                float half = Mathf.Lerp(0.08f, 0.5f, v);
                float x = Mathf.Abs(u - 0.5f) / half;
                return x > 1f ? 0f : (1f - x * x) * (1f - v) * Mathf.Clamp01(v * 6f);
            }, pivotY: 0f);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            _cam = Camera.main;
            var o = new GameObject("Night Overlay");
            o.transform.SetParent(transform, false);
            _overlay = o.AddComponent<SpriteRenderer>();
            _overlay.sprite = _white;
            Sorting2D.Apply(_overlay, Sorting2D.Effects, 900);

            // Pools of light under every streetlight.
            foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Exclude))
            {
                if (sr.sprite == null || !sr.sprite.name.StartsWith("prop_streetlight")) continue;
                var g = NewGlow(sr.transform, Vector2.zero, 7.5f, new Color(1f, 0.82f, 0.5f, 0f));
                g.transform.rotation = Quaternion.identity;
                g.enabled = true;   // left on (alpha 0 by day) so world culling alone decides visibility
                _streetGlows.Add(g);
            }
        }

        private float CurrentHour => Services.TryGet(out ITimeOfDay clock) ? clock.Hour : 12f;

        private void LateUpdate()
        {
            if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
            Color c = Suppressed ? Color.clear : Sample(CurrentHour);
            Darkness = Mathf.Clamp01(c.a / 0.6f);

            // Overlay covers the whole view (with margin for camera rotation).
            float h = _cam.orthographicSize * 2f, w = h * _cam.aspect;
            float size = Mathf.Max(w, h) * 1.5f;
            _overlay.transform.position = new Vector3(_cam.transform.position.x, _cam.transform.position.y, 0f);
            _overlay.transform.localScale = new Vector3(size / _white.bounds.size.x, size / _white.bounds.size.y, 1f);
            _overlay.color = c;
            _overlay.enabled = c.a > 0.005f;

            // Lights come on as it gets dark (updated only when the level changes noticeably).
            float lights = Mathf.Clamp01((Darkness - 0.25f) / 0.5f);
            if (Mathf.Abs(lights - _appliedDark) > 0.02f || (lights == 0f) != (_appliedDark == 0f))
            {
                _appliedDark = lights;
                foreach (var g in _streetGlows)
                {
                    if (g == null) continue;
                    var col = g.color; col.a = 0.5f * lights; g.color = col;
                }
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f) { _scanTimer = 1.5f; ScanCars(); }
            UpdateCars(lights);
        }

        private void ScanCars()
        {
            foreach (var car in Object.FindObjectsByType<Car2D>(FindObjectsInactive.Exclude))
            {
                if (_cars.ContainsKey(car) || car.Hover) continue;
                var box = car.GetComponent<BoxCollider2D>();
                Vector2 sz = box != null ? box.size : new Vector2(1.9f, 4.4f);
                float front = sz.y * 0.5f - 0.1f, side = sz.x * 0.3f;
                var lights = new CarLights
                {
                    Beams = new[] { NewBeam(car.transform, new Vector2(-side, front)), NewBeam(car.transform, new Vector2(side, front)) },
                    Tails = new[]
                    {
                        NewGlow(car.transform, new Vector2(-side, -front), 0.9f, new Color(1f, 0.1f, 0.08f, 0f)),
                        NewGlow(car.transform, new Vector2(side, -front), 0.9f, new Color(1f, 0.1f, 0.08f, 0f)),
                    },
                };
                _cars[car] = lights;
            }
            // Forget despawned cars.
            var dead = new List<Car2D>();
            foreach (var kv in _cars) if (kv.Key == null) dead.Add(kv.Key);
            foreach (var d in dead) _cars.Remove(d);
        }

        private void UpdateCars(float lights)
        {
            foreach (var kv in _cars)
            {
                if (kv.Key == null) continue;
                foreach (var b in kv.Value.Beams) { b.enabled = lights > 0f; var c = b.color; c.a = 0.42f * lights; b.color = c; }
                foreach (var t in kv.Value.Tails) { t.enabled = lights > 0f; var c = t.color; c.a = 0.85f * lights; t.color = c; }
            }
        }

        private SpriteRenderer NewBeam(Transform parent, Vector2 local)
        {
            var go = new GameObject("Headlight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = new Vector3(5f / _beam.bounds.size.x, 14f / _beam.bounds.size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _beam;
            sr.color = new Color(1f, 0.95f, 0.78f, 0f);
            sr.enabled = false;
            Sorting2D.Apply(sr, Sorting2D.Effects, 950);
            return sr;
        }

        private SpriteRenderer NewGlow(Transform parent, Vector2 local, float diameter, Color colour)
        {
            var go = new GameObject("Glow");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            Vector3 ls = parent.lossyScale;
            go.transform.localScale = new Vector3(diameter / _glow.bounds.size.x / Mathf.Max(0.01f, ls.x), diameter / _glow.bounds.size.y / Mathf.Max(0.01f, ls.y), 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _glow;
            sr.color = colour;
            sr.enabled = false;
            Sorting2D.Apply(sr, Sorting2D.Effects, 940);
            return sr;
        }

        private static Color Sample(float hour)
        {
            for (int i = 0; i + 1 < Keys.Length; i++)
            {
                if (hour < Keys[i].hour || hour > Keys[i + 1].hour) continue;
                float t = Mathf.InverseLerp(Keys[i].hour, Keys[i + 1].hour, hour);
                t = t * t * (3f - 2f * t);
                return Color.Lerp(Keys[i].c, Keys[i + 1].c, t);
            }
            return Keys[0].c;
        }

        private static Sprite MakeSprite(int size, System.Func<float, float, float> alpha, float pivotY = 0.5f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = new Color(1f, 1f, 1f, alpha((x + 0.5f) / size, (y + 0.5f) / size));
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, pivotY), size);
        }

        private void OnGUI()
        {
            if (GPA.Core.GameplayHotkeys.Blocked) return;   // hidden on the title screen and in menus
            if (!Services.TryGet(out ITimeOfDay clock)) return;
            if (_clockStyle == null)
                _clockStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 18, fontStyle = FontStyle.Bold, richText = true };
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            var t = clock.GameDateTime;
            string icon = Darkness > 0.5f ? "☾" : "☀";
            var r = new Rect(canvas.x * 0.5f - 90f, 6f, 180f, 26f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), $"<color=#000000aa>{icon} {t:HH:mm}  Day {clock.Day + 1}</color>", _clockStyle);
            GUI.Label(r, $"{icon} {t:HH:mm}  Day {clock.Day + 1}", _clockStyle);
            GUI.matrix = old;
        }
    }
}
