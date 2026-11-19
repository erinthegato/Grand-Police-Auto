using GPA.Core;
using GPA.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Emergency lights for a 2D patrol car: alternating red/blue double-flash glows over the roof
    /// lightbar, toggled with the existing EmergencyLights action while the player drives.
    /// Publishes <see cref="EmergencySignalChangedEvent"/> like the 3D cruiser.
    /// </summary>
    [RequireComponent(typeof(Car2D))]
    public sealed class Lightbar2D : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _red;
        [SerializeField] private SpriteRenderer _blue;
        [SerializeField] private float _cycleSeconds = 0.75f;

        private Car2D _car;
        private InputAction _toggle;
        private bool _on;
        private float _t;

        public bool On => _on;

        /// <summary>For AI police cars (backup units): switch the lights without the player's key.</summary>
        public void SetOn(bool on)
        {
            if (_on == on) return;
            _on = on; _t = 0f;
            if (!on) Apply(0f, 0f);
        }

        private void Awake()
        {
            _car = GetComponent<Car2D>();
            Apply(0f, 0f);
        }

        private void Start()
        {
            if (Services.TryGet(out IInputService input)) _toggle = input.Get(ActionMaps.Vehicle, "EmergencyLights");
        }

        private void Update()
        {
            if (_car.IsPlayerDriven && _toggle != null && _toggle.WasPerformedThisFrame())
            {
                _on = !_on;
                _t = 0f;
                GameEvents.Publish(new EmergencySignalChangedEvent(_car, _on ? EmergencyLightMode.Stage3 : EmergencyLightMode.Off, false));
            }
            if (!_on) { Apply(0f, 0f); return; }

            _t = (_t + Time.deltaTime) % _cycleSeconds;
            float phase = _t / _cycleSeconds;
            // Double flash: red twice in the first half, blue twice in the second.
            float red = phase < 0.5f && Mathf.Repeat(phase * 8f, 2f) < 1f ? 1f : 0f;
            float blue = phase >= 0.5f && Mathf.Repeat(phase * 8f, 2f) < 1f ? 1f : 0f;
            Apply(red, blue);
        }

        private void Apply(float red, float blue)
        {
            if (_red != null) _red.enabled = red > 0f;
            if (_blue != null) _blue.enabled = blue > 0f;
        }
    }
}
