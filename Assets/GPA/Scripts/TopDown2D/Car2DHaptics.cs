using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Controller rumble while the player drives a <see cref="Car2D"/>. One-shot pulses only, no
    /// continuous rumble: a short engine kick when you get in and start the car, a bump each time
    /// you leave the road (kerb, pavement, park), and a thump scaled to how hard you hit something.
    /// </summary>
    [RequireComponent(typeof(Car2D))]
    public sealed class Car2DHaptics : MonoBehaviour
    {
        [Tooltip("Impact speed (m/s) for a full-strength thump.")]
        [SerializeField] private float _fullImpactSpeed = 14f;
        [SerializeField] private float _minImpactSpeed = 2.5f;
        [Tooltip("Minimum seconds between crash pulses (one per impact, not per scrape).")]
        [SerializeField] private float _crashCooldown = 0.6f;
        [Tooltip("Must be moving at least this fast (m/s) for leaving the road to register.")]
        [SerializeField] private float _offroadMinSpeed = 2f;

        private Car2D _car;
        private IHapticsService _haptics;
        private bool _wasDriven, _offroad;
        private float _lastCrash = -10f, _offroadSettle;

        private void Awake() => _car = GetComponent<Car2D>();

        private void Update()
        {
            if (_haptics == null && !Services.TryGet(out _haptics)) return;
            _haptics.ClearContinuous(this);   // clear anything left over from the old continuous rumble

            bool driven = _car.IsPlayerDriven;
            if (driven && !_wasDriven)
            {
                _haptics.Pulse(0.45f, 0.2f, 0.25f);          // engine turns over
                _offroad = IsOffroad();                      // don't fire if you start parked on the kerb
            }
            _wasDriven = driven;
            if (!driven) return;

            // Leaving the road: one bump on the transition, after it holds for a moment (no flicker at the kerb line).
            bool off = IsOffroad();
            if (off != _offroad)
            {
                _offroadSettle += Time.deltaTime;
                if (_offroadSettle > 0.12f)
                {
                    _offroad = off;
                    _offroadSettle = 0f;
                    if (off && Mathf.Abs(_car.ForwardSpeed) > _offroadMinSpeed)
                        _haptics.Pulse(0.3f, 0.5f, 0.18f);
                }
            }
            else _offroadSettle = 0f;
        }

        private bool IsOffroad()
        {
            var map = WalkMap2D.Instance;
            if (map == null) return false;
            var kind = map.At(map.CellOf(transform.position));
            return kind == WalkMap2D.Kind.Sidewalk || kind == WalkMap2D.Kind.Park;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_haptics == null || !_car.IsPlayerDriven) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < _minImpactSpeed || Time.time - _lastCrash < _crashCooldown) return;
            _lastCrash = Time.time;
            float k = Mathf.Clamp01(speed / _fullImpactSpeed);
            _haptics.Pulse(0.35f + 0.65f * k, 0.25f + 0.55f * k, 0.12f + 0.3f * k);
        }

        private void OnDisable() { if (_haptics != null) _haptics.ClearContinuous(this); }
    }
}
