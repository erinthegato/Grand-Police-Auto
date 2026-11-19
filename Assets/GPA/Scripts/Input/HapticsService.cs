using System.Collections.Generic;
using GPA.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Input
{
    /// <summary>
    /// <see cref="IHapticsService"/> for gamepads (dual-motor rumble on Xbox, PlayStation, Switch Pro
    /// and MFi pads via the Input System) with a phone-vibration fallback for strong pulses on
    /// Android/iOS when no pad is connected. Motors are always released on pause, focus loss and
    /// destroy so a pad never keeps buzzing.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    public sealed class HapticsService : MonoBehaviour, IHapticsService
    {
        [Tooltip("Pulses at least this strong also vibrate a phone when no gamepad is connected.")]
        [SerializeField, Range(0f, 1f)] private float _handheldThreshold = 0.6f;
        [Tooltip("Minimum seconds between phone vibrations (the OS call is a fixed ~0.4 s buzz).")]
        [SerializeField] private float _handheldCooldown = 0.5f;

        private struct PulseState { public float Low, High, Remaining, Duration; }

        private readonly Dictionary<object, Vector2> _continuous = new Dictionary<object, Vector2>();
        private readonly List<PulseState> _pulses = new List<PulseState>();
        private ISettings _settings;
        private bool _paused;
        private float _nextHandheld;

        public bool Enabled { get; set; } = true;
        public float Intensity { get; set; } = 1f;

        /// <summary>Safety net for builds: if a scene has no Haptics object, make one that survives scene loads.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureExists()
        {
            if (Services.Has<IHapticsService>() || FindAnyObjectByType<HapticsService>() != null) return;
            var go = new GameObject("Haptics (auto)");
            DontDestroyOnLoad(go);
            go.AddComponent<HapticsService>();
        }

        private void Awake()
        {
            Services.Register<IHapticsService>(this);
            InputSystem.onDeviceChange += OnDeviceChange;
        }

        /// <summary>
        /// A pad connected or reconnected: send it a zero command straight away. On iOS the first
        /// SetMotorSpeeds call allocates the rumble engine (slow), so this primes it before the first
        /// real pulse; on Windows it clears anything left over from a previous session.
        /// </summary>
        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!(device is Gamepad pad)) return;
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled)
            {
                try { pad.SetMotorSpeeds(0f, 0f); } catch (System.Exception) { /* pads without motors */ }
                _sentPads = -1;   // force a resend next frame
            }
        }

        private void Start()
        {
            if (Services.TryGet(out _settings))
            {
                Enabled = _settings.GetBool(HapticsSettingKeys.Enabled, true);
                Intensity = Mathf.Clamp01(_settings.GetFloat(HapticsSettingKeys.Intensity, 1f));
                _settings.Changed += OnSettingChanged;
            }
        }

        private void OnSettingChanged(string key)
        {
            if (key == HapticsSettingKeys.Enabled) Enabled = _settings.GetBool(key, true);
            else if (key == HapticsSettingKeys.Intensity) Intensity = Mathf.Clamp01(_settings.GetFloat(key, 1f));
        }

        public void Pulse(float low, float high, float duration)
        {
            if (!Enabled || duration <= 0f) return;
            _pulses.Add(new PulseState { Low = Mathf.Clamp01(low), High = Mathf.Clamp01(high), Remaining = duration, Duration = duration });

            if (Application.isMobilePlatform && Gamepad.current == null && Mathf.Max(low, high) * Intensity >= _handheldThreshold
                && Time.unscaledTime >= _nextHandheld)
            {
                _nextHandheld = Time.unscaledTime + _handheldCooldown;
#if UNITY_ANDROID || UNITY_IOS
                Handheld.Vibrate();
#endif
            }
        }

        public void SetContinuous(object owner, float low, float high)
        {
            if (owner == null) return;
            _continuous[owner] = new Vector2(Mathf.Clamp01(low), Mathf.Clamp01(high));
        }

        public void ClearContinuous(object owner)
        {
            if (owner != null) _continuous.Remove(owner);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float low = 0f, high = 0f;

            foreach (var v in _continuous.Values) { low = Mathf.Max(low, v.x); high = Mathf.Max(high, v.y); }

            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                PulseState p = _pulses[i];
                p.Remaining -= dt;
                if (p.Remaining <= 0f) { _pulses.RemoveAt(i); continue; }
                // Short ease-out so impacts feel like a thump rather than a flat buzz.
                float k = Mathf.Sqrt(p.Remaining / p.Duration);
                low = Mathf.Max(low, p.Low * k);
                high = Mathf.Max(high, p.High * k);
                _pulses[i] = p;
            }

            // Focus is read live every frame (a missed focus-regained event used to leave the pads muted).
            bool silent = !Enabled || _paused || !Application.isFocused || Time.timeScale <= 0f;
            float scale = silent ? 0f : Intensity;
            low *= scale; high *= scale;

            // Drive every connected pad, not just Gamepad.current: Steam Input / DS4Windows style virtual
            // pads and wireless reconnects can make 'current' a device that has no motors.
            var pads = Gamepad.all;
            _resend -= dt;
            bool changed = !Mathf.Approximately(low, _sentLow) || !Mathf.Approximately(high, _sentHigh) || pads.Count != _sentPads;
            if (!changed && _resend > 0f) return;
            _resend = 0.25f;                                    // refresh periodically in case a pad was reset
            _sentLow = low; _sentHigh = high; _sentPads = pads.Count;
            for (int i = 0; i < pads.Count; i++)
                if (pads[i] != null && pads[i].added && pads[i].enabled) pads[i].SetMotorSpeeds(low, high);
        }

        private float _sentLow = -1f, _sentHigh = -1f, _resend;
        private int _sentPads = -1;

        private void OnApplicationPause(bool paused) { _paused = paused; if (paused) Silence(); }
        private void OnApplicationFocus(bool focus) { if (!focus) Silence(); else _paused = false; }

        private void Silence()
        {
            _pulses.Clear();
            _sentLow = _sentHigh = -1f;
            InputSystem.ResetHaptics();
        }

        private void OnDisable() => Silence();
        private void OnApplicationQuit() => Silence();   // never leave a pad buzzing after the game closes

        private void OnDestroy()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            if (_settings != null) _settings.Changed -= OnSettingChanged;
            Services.Unregister<IHapticsService>(this);
        }
    }
}
