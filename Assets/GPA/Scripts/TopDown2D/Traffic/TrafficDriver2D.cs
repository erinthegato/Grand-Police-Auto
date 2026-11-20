using System.Collections.Generic;
using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Civilian driver that follows the road graph: keeps to its lane, stops at red and yellow
    /// lights, keeps a following distance, brakes for people and cars ahead, and slows and moves
    /// right when a police car with its lights on comes up behind. Plugs into <see cref="Car2D.Input"/>.
    /// </summary>
    public sealed class TrafficDriver2D : MonoBehaviour, IVehicleControlInput, ITurnSignalSource
    {
        [SerializeField] private float _lookAhead = 5f;
        [SerializeField] private float _stopLineDistance = 2.5f;
        [SerializeField] private float _scanLength = 14f;
        [SerializeField] private float _speedVariation = 0.15f;

        private readonly List<Vector2> _path = new List<Vector2>();
        private Car2D _car;
        private RoadGraph2D _graph;
        private int _edge = -1;
        private int _pendingEdge = -1;       // junction edge waiting on the light
        private float _speedFactor = 1f;
        private float _throttle, _brake, _steer;
        private bool _handbrake;
        private float _stuckTimer;
        private float _crashTimer;
        // Passing a stopped vehicle (broken down, pulled over, queued behind a traffic stop).
        private Rigidbody2D _blocker;
        private float _blockedTimer, _passSide, _passBlend;
        private const float LaneWidth = 4f;          // one road tile
        private float _reverseTimer, _blockedTotal;
        private bool _heldByLight;                  // stopping for a red/yellow this step (not for an obstacle)

        /// <summary>-1 left, 1 right: next junction turn, or pulling out to pass.</summary>
        public int TurnSignal { get; private set; }
        private const float MaxCruise = 8.5f;     // ~30 km/h city driving
        private const float MaxTurn = 4.2f;
        private const float ComfortDecel = 5f;    // m/s² the driver plans to brake with

        /// <summary>True when the car has made no progress for a while (blocked, crashed) — the manager may recycle it.</summary>
        public bool Stuck => _stuckTimer > 8f || _blockedTotal > 20f;

        public float Throttle => _throttle;
        public float Brake => _brake;
        public float Steer => _steer;
        public bool Handbrake => _handbrake;
        /// <summary>Set by the traffic-stop system later to take over this driver.</summary>
        public bool Suspended { get; set; }

        public void Begin(Car2D car, RoadGraph2D graph, int laneEdge)
        {
            _car = car;
            _graph = graph;
            _speedFactor = 1f + Random.Range(-_speedVariation, _speedVariation);
            _path.Clear();
            Enter(laneEdge);
            car.Input = this;
        }

        private void Enter(int edge)
        {
            _edge = edge;
            _graph.Sample(edge, _path);
        }

        private void FixedUpdate()
        {
            if (_car == null || _graph == null || Suspended) { Hold(); return; }

            Vector2 pos = _car.transform.position;
            Vector2 fwd = _car.transform.up;

            // Consume reached points; when the path runs low, queue the next edge.
            while (_path.Count > 0 && Vector2.Distance(pos, _path[0]) < _lookAhead) _path.RemoveAt(0);
            if (_path.Count < 3 && _pendingEdge < 0 && _edge >= 0)
            {
                int next = _graph.PickNext(_edge);
                if (next >= 0)
                {
                    if (_graph.Edges[next].ThroughJunction) _pendingEdge = next;
                    else Enter(next);
                }
            }

            float edgeLimit = _edge >= 0 ? _graph.Edges[_edge].SpeedLimit : 0f;   // e.g. the beltway
            float targetSpeed = (edgeLimit > 0f ? edgeLimit : Mathf.Min(_graph.LaneSpeed, MaxCruise)) * _speedFactor;
            float turnSpeed = Mathf.Min(_graph.TurnSpeed, MaxTurn);
            if (_edge >= 0 && _graph.Edges[_edge].ThroughJunction) targetSpeed = turnSpeed;
            // Out of road ahead: stop rather than ploughing on in a straight line.
            if (_path.Count == 0 && _pendingEdge < 0) targetSpeed = 0f;
            if (_crashTimer > 0f) { _crashTimer -= Time.fixedDeltaTime; targetSpeed = Mathf.Min(targetSpeed, 2.5f); }

            _heldByLight = false;
            // Waiting at the stop line for the light.
            if (_pendingEdge >= 0)
            {
                RoadGraph2D.Node stop = _graph.Nodes[_graph.Edges[_pendingEdge].From];
                float toStop = Vector2.Dot(stop.Position - pos, fwd);
                var light = TrafficSignals2D.Instance != null ? TrafficSignals2D.Instance.State(stop.Junction, stop.Vertical) : TrafficSignals2D.Light.Green;
                bool mustStop = light == TrafficSignals2D.Light.Red || (light == TrafficSignals2D.Light.Yellow && toStop > 6f);
                if (mustStop && toStop > -0.5f) _heldByLight = toStop - _stopLineDistance < 12f;
                if (mustStop && toStop > -0.5f)
                    targetSpeed = Mathf.Min(targetSpeed, BrakeFor(toStop - _stopLineDistance));
                else
                {
                    targetSpeed = Mathf.Min(targetSpeed, Mathf.Max(turnSpeed, BrakeFor(toStop) + turnSpeed)); // ease into the junction
                }
                if (!(mustStop && toStop > -0.5f) && toStop < 4f)
                {
                    int e = _pendingEdge;
                    _pendingEdge = -1;
                    Enter(e);
                }
                if (_path.Count == 0) _path.Add(stop.Position + fwd * 0.1f);
            }

            // Anything moving (car, person, player) in the lane ahead.
            Vector2 passOffset = (Vector2)_car.transform.right * (_passSide * LaneWidth * _passBlend);
            float clear = ScanAhead(pos + passOffset * 0.6f, fwd, Mathf.Max(_scanLength, _car.ForwardSpeed * 2.2f), _passBlend > 0.5f ? _blocker : null);
            if (clear < float.MaxValue) targetSpeed = Mathf.Min(targetSpeed, BrakeFor(clear - 3f));
            UpdatePassing(pos, fwd, clear, targetSpeed);
            if (_passBlend > 0.01f) targetSpeed = Mathf.Min(targetSpeed, 6f);

            // Yield to an emergency vehicle behind: slow and drift right.
            Vector2 aimOffset = (Vector2)_car.transform.right * (_passSide * LaneWidth * _passBlend);
            Transform siren = TrafficManager2D.ActiveEmergencyVehicle;
            if (siren != null && siren != _car.transform)
            {
                Vector2 rel = (Vector2)siren.position - pos;
                if (rel.magnitude < 30f && Vector2.Dot(rel, fwd) < 0f)
                {
                    targetSpeed = Mathf.Min(targetSpeed, 3f);
                    aimOffset += (Vector2)_car.transform.right * 2.2f;
                }
            }

            // Knocked out of lane (collision, shove): steer back to the nearest point of the path first.
            if (_path.Count > 0)
            {
                Vector2 a = _path[0];
                Vector2 laneDir = _path.Count > 1 ? (_path[1] - a).normalized : fwd;
                Vector2 rel = pos - a;
                float off = Mathf.Abs(rel.x * laneDir.y - rel.y * laneDir.x);
                if (off > 3f && _passBlend < 0.01f) targetSpeed = Mathf.Min(targetSpeed, 4f);
            }

            TurnSignal = _passBlend > 0.01f && _passSide != 0f ? (int)Mathf.Sign(_passSide) : JunctionSignal(pos);

            Vector2 target = (_path.Count > 0 ? _path[0] : pos + fwd * 4f) + aimOffset;
            float angle = Vector2.SignedAngle(fwd, target - pos);
            // Gentle proportional steering with yaw damping, softer at speed, so drivers don't weave.
            float yaw = _car.TryGetComponent(out Rigidbody2D rb) ? rb.angularVelocity : 0f;
            float gain = Mathf.Lerp(1f / 30f, 1f / 55f, Mathf.Clamp01(_car.ForwardSpeed / MaxCruise));
            _steer = Mathf.Clamp(-(angle - yaw * 0.12f) * gain, -0.85f, 0.85f);

            float speed = _car.ForwardSpeed;
            float error = targetSpeed - speed;
            _throttle = Mathf.Clamp(error * 0.25f, 0f, 0.55f);   // AI drivers accelerate gently
            _brake = error < -0.3f && speed > 0.6f ? Mathf.Clamp01(-error * 0.4f) : 0f;
            // Car2D reverses when braking from a standstill, so only hold the stop with the handbrake
            // once nearly stopped, wheel straight (the handbrake loosens grip and sharpens turning).
            _handbrake = targetSpeed < 0.5f && speed <= 0.6f;
            if (_handbrake) _steer = 0f;

            _stuckTimer = targetSpeed > 2f && Mathf.Abs(speed) < 0.5f ? _stuckTimer + Time.fixedDeltaTime : 0f;

            // Backing up to get an angle around something we can't pass from here (e.g. nosed into the player's car).
            if (_reverseTimer > 0f)
            {
                _reverseTimer -= Time.fixedDeltaTime;
                _throttle = 0f; _handbrake = false;
                _brake = 1f;                              // Car2D reverses when braking from a stop
                _steer = speed < -0.5f ? 0.7f : 0f;       // nose swings left (out toward the next lane)
                if (BehindBlocked(pos, fwd)) _reverseTimer = 0f;
            }
        }

        private void Hold() { _throttle = 0f; _brake = 0f; _steer = 0f; _handbrake = true; }

        /// <summary>Highest speed from which the car can still stop within <paramref name="distance"/>.</summary>
        private static float BrakeFor(float distance) => distance <= 0f ? 0f : Mathf.Sqrt(2f * ComfortDecel * distance);

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (c.relativeVelocity.magnitude > 2f) _crashTimer = 2.5f;   // shaken: crawl for a moment
        }

        private int JunctionSignal(Vector2 pos)
        {
            int e = _pendingEdge >= 0 ? _pendingEdge : (_edge >= 0 && _graph.Edges[_edge].ThroughJunction ? _edge : -1);
            if (e < 0) return 0;
            var edge = _graph.Edges[e];
            if (_pendingEdge >= 0 && Vector2.Distance(pos, _graph.Nodes[edge.From].Position) > 30f) return 0;
            float turn = Mathf.DeltaAngle(_graph.Nodes[edge.From].Heading, _graph.Nodes[edge.To].Heading);
            return turn > 30f ? -1 : turn < -30f ? 1 : 0;   // heading grows counter-clockwise: + is a left turn
        }

        /// <summary>
        /// Stuck behind something that isn't moving (and not just waiting at a light)? After a short
        /// wait, check the lane to the left is clear (including oncoming traffic) and swing out around it.
        /// </summary>
        private void UpdatePassing(Vector2 pos, Vector2 fwd, float clear, float targetSpeed)
        {
            float dt = Time.fixedDeltaTime;
            bool atLight = _heldByLight;
            bool inJunction = _edge >= 0 && _graph.Edges[_edge].ThroughJunction;
            bool blockerStill = _blocker != null && _blocker.linearVelocity.magnitude < 0.6f && clear < 9f && Passable(_blocker);

            if (_passSide == 0f)
            {
                _blockedTimer = blockerStill && !atLight && !inJunction && _car.ForwardSpeed < 1f ? _blockedTimer + dt : 0f;
                _blockedTotal = blockerStill && !atLight ? _blockedTotal + dt : 0f;
                if (_blockedTimer > 2f && SideClear(pos, fwd, -1f)) { _passSide = -1f; _blockedTimer = 0f; }
                else if (_blockedTimer > 2f && SideClear(pos, fwd, 1f)) { _passSide = 1f; _blockedTimer = 0f; }
                else if (_blockedTimer > 3.5f && _reverseTimer <= 0f && !BehindBlocked(pos, fwd))
                {
                    _reverseTimer = 1.4f;                 // can't get round: back up a car length and retry
                    _blockedTimer = 1f;
                }
            }
            else
            {
                // Back in once the stopped vehicle is behind us, or give up if it's gone.
                bool passed = _blocker == null || Vector2.Dot((Vector2)_blocker.position - pos, fwd) < -3.5f;
                if (passed || inJunction) _passSide = _passBlend < 0.05f ? 0f : _passSide;
            }
            bool wantOut = _passSide != 0f && !(_blocker == null || Vector2.Dot((Vector2)_blocker.position - pos, fwd) < -3.5f) && !inJunction;
            _passBlend = Mathf.MoveTowards(_passBlend, wantOut ? 1f : 0f, dt * 0.6f);
            if (!wantOut && _passBlend <= 0f) { _passSide = 0f; _blocker = null; }
        }

        /// <summary>Only go around vehicles that aren't simply queuing in traffic.</summary>
        private static bool Passable(Rigidbody2D body)
        {
            if (!body.TryGetComponent(out Car2D car)) return false;          // people, props: wait
            if (body.TryGetComponent(out TrafficDriver2D other) && car.Input == (IVehicleControlInput)other)
                return other.Stuck;                                          // normal traffic: only if it's stuck
            return true;                                                     // pulled over, parked, abandoned, player
        }

        private bool BehindBlocked(Vector2 pos, Vector2 fwd)
        {
            foreach (var h in Physics2D.CircleCastAll(pos - fwd * 2.6f, 1.1f, -fwd, 3f))
                if (h.collider != null && !h.collider.isTrigger && h.transform != _car.transform && Overpass2D.SameLevel(h.collider, _car)) return true;
            return false;
        }

        private bool SideClear(Vector2 pos, Vector2 fwd, float side)
        {
            Vector2 lane = pos + (Vector2)_car.transform.right * (side * LaneWidth);
            // Must be road, not pavement or a building.
            if (WalkMap2D.Instance != null && WalkMap2D.Instance.Walkable(WalkMap2D.Instance.CellOf(lane + fwd * 6f))) return false;
            var hits = Physics2D.BoxCastAll(lane - fwd * 4f, new Vector2(2.0f, 2f), Vector2.SignedAngle(Vector2.up, fwd), fwd, 26f);
            foreach (var h in hits)
                if (h.collider != null && !h.collider.isTrigger && h.transform != _car.transform && Overpass2D.SameLevel(h.collider, _car)) return false;
            return true;
        }

        private float ScanAhead(Vector2 pos, Vector2 fwd, float length, Rigidbody2D ignore)
        {
            // Look along the steering direction too, so cars see what's ahead in a turn.
            Vector2 dir = _path.Count > 0 ? ((_path[0] - pos).normalized + fwd).normalized : fwd;
            var hits = Physics2D.CircleCastAll(pos + fwd * 2.6f, 1.25f, dir, length);
            foreach (var hit in hits)
            {
                Rigidbody2D body = hit.rigidbody;
                if (body == null || body.transform == _car.transform || hit.collider.isTrigger || body == ignore) continue;
                if (!Overpass2D.SameLevel(hit.collider, _car)) continue;   // on the bridge above, or the road below
                if (_passBlend < 0.5f) _blocker = body;
                return hit.distance;
            }
            return float.MaxValue;
        }
    }
}
