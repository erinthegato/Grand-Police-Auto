using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Police
{
    /// <summary>
    /// Takes over a civilian car that's been signalled: indicates right, eases across to the kerb
    /// along its own road, never stops inside a junction, and comes to a smooth stop, then holds there
    /// until released. Fail-safe: it stops where it is if anything is in the way, if it bumps into
    /// something, if the spot turns out not to be road, or if it's taking too long.
    /// </summary>
    public sealed class PullOverDriver2D : MonoBehaviour, IVehicleControlInput, ITurnSignalSource
    {
        private const float MaxSpeed = 7f;          // ~25 km/h while pulling over
        private const float MaxTime = 9f;

        private Car2D _car;
        private Vector2 _axis;                      // road direction (snapped to N/S/E/W)
        private Vector2 _target;                    // stop point at the kerb
        private float _throttle, _brake, _steer, _timer;
        private bool _handbrake, _stopNow;

        public float Throttle => _throttle;
        public float Brake => _brake;
        public float Steer => _steer;
        public bool Handbrake => _handbrake;
        public bool Stopped { get; private set; }
        public int TurnSignal => Stopped ? 0 : 1;

        public void Begin(Car2D car)
        {
            _car = car;
            Vector2 pos = car.transform.position;
            Vector2 up = car.transform.up;
            _axis = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? new Vector2(Mathf.Sign(up.x), 0f) : new Vector2(0f, Mathf.Sign(up.y));
            Vector2 right = new Vector2(_axis.y, -_axis.x);

            // Stop distance: ~20 m on, pushed past a junction ahead if needed, but never off our road.
            float along = 20f;
            for (int guard = 0; guard < 6 && InJunction(pos + _axis * along); guard++) along += 8f;
            while (along > 6f && !IsRoad(pos + _axis * along)) along -= 2f;
            if (InJunction(pos + _axis * along)) along = 6f;   // nowhere good ahead: stop just in front

            // Kerb offset: walk right until the pavement starts, stay ~1.7 m inside it.
            float toKerb = 0f;
            for (float d = 0.5f; d <= 14f; d += 0.5f)
            {
                if (!IsRoad(pos + _axis * along + right * d)) { toKerb = Mathf.Max(0f, d - 2.6f); break; }
                toKerb = d;
            }
            _target = pos + _axis * along + right * toKerb;
            car.Input = this;
        }

        private void FixedUpdate()
        {
            if (_car == null) return;
            Vector2 pos = _car.transform.position, fwd = _car.transform.up;
            float speed = _car.ForwardSpeed;
            float ahead = Vector2.Dot(_target - pos, _axis);
            _timer += Time.fixedDeltaTime;

            if (!Stopped && (_stopNow || ahead < 1f || _timer > MaxTime || ObstacleAhead(pos, fwd, Mathf.Max(3.5f, speed * 1.2f))))
                _stopNow = true;

            if (_stopNow || Stopped)
            {
                _throttle = 0f;
                _steer = speed > 0.5f ? Mathf.Clamp(-Vector2.SignedAngle(fwd, _axis) / 20f, -0.6f, 0.6f) : 0f;
                _brake = speed > 0.3f ? 1f : 0f;
                _handbrake = speed <= 0.3f;
                if (speed <= 0.3f) Stopped = true;
                return;
            }

            // Follow the road line, sliding across toward the kerb as we go (pure pursuit on that line).
            Vector2 right = new Vector2(_axis.y, -_axis.x);
            float lateralNow = Vector2.Dot(pos - _target, right);
            float lookAhead = Mathf.Clamp(ahead, 5f, 12f);
            float blend = Mathf.Clamp01(lookAhead / Mathf.Max(ahead, 0.01f));
            Vector2 aim = _target - _axis * (ahead - lookAhead) + right * (lateralNow * (1f - blend));
            Vector2 want = (aim - pos).normalized;
            float off = Vector2.SignedAngle(_axis, want);
            want = Quaternion.Euler(0, 0, Mathf.Clamp(off, -18f, 18f)) * _axis;   // never point at the kerb
            float angle = Vector2.SignedAngle(fwd, want);
            _steer = Mathf.Clamp(-angle / 35f, -0.6f, 0.6f);

            float targetSpeed = Mathf.Min(MaxSpeed, Mathf.Sqrt(2f * 3.5f * Mathf.Max(0f, ahead - 1f)));
            float error = targetSpeed - speed;
            _throttle = Mathf.Clamp(error * 0.3f, 0f, 0.5f);
            _brake = error < -0.3f && speed > 0.4f ? Mathf.Clamp01(-error * 0.5f) : 0f;
            _handbrake = false;
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (Stopped) return;
            if (c.rigidbody == null)
            {
                // Static scenery on the kerb: move the stop line away from it and keep going.
                Vector2 right = new Vector2(_axis.y, -_axis.x);
                _target -= right * 1.0f;
                return;
            }
            _stopNow = true;                  // another car or a person: stop right here
        }

        private bool ObstacleAhead(Vector2 pos, Vector2 fwd, float range)
        {
            foreach (var h in Physics2D.CircleCastAll(pos + fwd * 2.5f, 1.0f, fwd, range))
                if (h.collider != null && !h.collider.isTrigger && h.transform != _car.transform && GPA.TopDown2D.Overpass2D.SameLevel(h.collider, _car)) return true;
            return false;
        }

        private static bool IsRoad(Vector2 p)
        {
            WalkMap2D map = WalkMap2D.Instance;
            if (map == null) return true;
            var c = map.CellOf(p);
            // Blocked cells are road or buildings/water; rule out building and water footprints by collider.
            if (map.At(c) != WalkMap2D.Kind.Blocked) return false;
            foreach (var col in Physics2D.OverlapPointAll(p))
                if (col.attachedRigidbody == null && !col.isTrigger) return false;   // static: building
            return p.x > map.Origin.x && p.y > map.Origin.y &&
                   p.x < map.Origin.x + map.Width * map.CellSize && p.y < map.Origin.y + map.Height * map.CellSize;
        }

        private static bool InJunction(Vector2 p)
        {
            RoadGraph2D graph = RoadGraph2D.Instance;
            if (graph == null) return false;
            foreach (var j in graph.Junctions)
                if (Mathf.Abs(j.Centre.x - p.x) < 17f && Mathf.Abs(j.Centre.y - p.y) < 17f) return true;
            return false;
        }
    }
}
