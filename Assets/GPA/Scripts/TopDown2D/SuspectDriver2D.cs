using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Fleeing-driver AI for pursuits. Drives fast, steers away from the pursuer and around
    /// buildings using three short "whisker" raycasts, and backs up when it gets stuck.
    /// Plug into <see cref="Car2D.Input"/>.
    /// </summary>
    public sealed class SuspectDriver2D : MonoBehaviour, IVehicleControlInput
    {
        [SerializeField] private float _aggression = 0.9f;
        [SerializeField] private float _whiskerLength = 9f;

        private Car2D _car;
        private Transform _pursuer;
        private float _stuckTimer, _reverseTimer;
        private float _throttle, _brake, _steer;

        public float Throttle => _throttle;
        public float Brake => _brake;
        public float Steer => _steer;
        public bool Handbrake => false;
        public bool Fleeing { get; private set; }

        public void Begin(Car2D car, Transform pursuer)
        {
            _car = car;
            _pursuer = pursuer;
            Fleeing = true;
            car.Input = this;
        }

        public void Stop()
        {
            Fleeing = false;
            _throttle = _steer = 0f;
            _brake = 1f;
            if (_car != null) _car.Input = IdleVehicleControl.Instance;
        }

        private void FixedUpdate()
        {
            if (!Fleeing || _car == null) return;
            Transform t = _car.transform;
            Vector2 pos = t.position;
            Vector2 fwd = t.up;

            if (_reverseTimer > 0f)
            {
                _reverseTimer -= Time.fixedDeltaTime;
                _throttle = 0f; _brake = 1f; _steer = 0.8f;
                return;
            }

            // Desired direction: away from the pursuer, biased to keep going forward.
            Vector2 away = _pursuer != null ? (pos - (Vector2)_pursuer.position).normalized : fwd;
            Vector2 desired = (away * 0.6f + fwd * 0.4f).normalized;

            // Whiskers: steer away from whatever is close ahead-left / ahead-right.
            float left = Probe(pos, Rotate(fwd, 30f));
            float right = Probe(pos, Rotate(fwd, -30f));
            float centre = Probe(pos, fwd);
            float avoid = (right - left) * 1.6f; // positive = turn right
            float signedAngle = Vector2.SignedAngle(fwd, desired); // positive = desired is to the left
            _steer = Mathf.Clamp(-signedAngle / 45f + avoid, -1f, 1f);
            if (centre < 0.35f) _steer = left > right ? -1f : 1f;

            _throttle = centre < 0.25f ? 0.4f : _aggression;
            _brake = 0f;

            // Stuck against something: back up briefly.
            if (_car.SpeedKmh < 4f) _stuckTimer += Time.fixedDeltaTime; else _stuckTimer = 0f;
            if (_stuckTimer > 1.2f) { _reverseTimer = 1.1f; _stuckTimer = 0f; }
        }

        /// <summary>0 = blocked right in front, 1 = clear for the whole whisker.</summary>
        private float Probe(Vector2 from, Vector2 dir)
        {
            RaycastHit2D[] hits = Physics2D.RaycastAll(from + dir * 2.6f, dir, _whiskerLength);
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.transform == _car.transform) continue;
                if (hit.collider.isTrigger) continue;
                return Mathf.Clamp01(hit.distance / _whiskerLength);
            }
            return 1f;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return new Vector2(v.x * Mathf.Cos(r) - v.y * Mathf.Sin(r), v.x * Mathf.Sin(r) + v.y * Mathf.Cos(r));
        }
    }
}
