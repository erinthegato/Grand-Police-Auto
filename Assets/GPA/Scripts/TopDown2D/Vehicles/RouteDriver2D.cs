using System.Collections.Generic;
using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>Plans a road route to a point using the traffic road graph (lanes + junction turns).</summary>
    public static class BackupRoute
    {
        /// <summary>
        /// Finds a start node between <paramref name="minDist"/> and <paramref name="maxDist"/> metres of
        /// driving from the node nearest <paramref name="target"/>, and returns the route as points.
        /// </summary>
        public static bool Plan(RoadGraph2D g, Vector2 target, float minDist, float maxDist, out List<Vector2> path, out Vector2 start, out float heading)
        {
            path = null; start = default; heading = 0f;
            int n = g.Nodes.Length;
            int goal = -1; float best = float.MaxValue;
            for (int i = 0; i < n; i++) { float d = (g.Nodes[i].Position - target).sqrMagnitude; if (d < best) { best = d; goal = i; } }
            if (goal < 0) return false;

            // Reverse Dijkstra: distance from every node to the goal, and the edge to take next.
            var incoming = new List<int>[n];
            for (int i = 0; i < n; i++) incoming[i] = new List<int>();
            for (int e = 0; e < g.Edges.Length; e++) incoming[g.Edges[e].To].Add(e);
            var dist = new float[n]; var next = new int[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; next[i] = -1; }
            dist[goal] = 0f;
            var open = new List<int> { goal };
            while (open.Count > 0)
            {
                int bi = 0; for (int i = 1; i < open.Count; i++) if (dist[open[i]] < dist[open[bi]]) bi = i;
                int cur = open[bi]; open.RemoveAt(bi);
                foreach (int e in incoming[cur])
                {
                    int from = g.Edges[e].From;
                    float nd = dist[cur] + Vector2.Distance(g.Nodes[from].Position, g.Nodes[cur].Position);
                    if (nd < dist[from]) { if (dist[from] == float.MaxValue) open.Add(from); dist[from] = nd; next[from] = e; }
                }
            }

            // Start somewhere off screen at a sensible driving distance (prefer the middle of the range).
            var candidates = new List<int>();
            for (int i = 0; i < n; i++)
                if (dist[i] >= minDist && dist[i] <= maxDist && Vector2.Distance(g.Nodes[i].Position, target) > 45f) candidates.Add(i);
            if (candidates.Count == 0)
                for (int i = 0; i < n; i++) if (dist[i] < float.MaxValue && dist[i] > 30f) candidates.Add(i);
            if (candidates.Count == 0) return false;
            int s = candidates[Random.Range(0, candidates.Count)];

            path = new List<Vector2>();
            start = g.Nodes[s].Position;
            heading = g.Nodes[s].Heading;
            for (int node = s, guard = 0; node != goal && next[node] >= 0 && guard < 4000; guard++)
            {
                int e = next[node];
                g.Sample(e, path);
                node = g.Edges[e].To;
            }
            path.Add(target);
            return path.Count > 1;
        }
    }

    /// <summary>
    /// Drives a backup car along a planned route with lights on (pure-pursuit steering, slows for
    /// bends), stops near the officer, and can hand the car back to ordinary traffic afterwards.
    /// </summary>
    public sealed class BackupDriver2D : MonoBehaviour, IVehicleControlInput
    {
        private Car2D _car;
        private List<Vector2> _path;
        private Vector2 _target;
        private int _index;
        private bool _emergency, _released;
        private float _stuck;
        private float _throttle, _brake, _steer;

        public bool Arrived { get; private set; }
        public float Throttle => _throttle;
        public float Brake => _brake;
        public float Steer => _steer;
        public bool Handbrake => false;

        public void Begin(Car2D car, List<Vector2> path, Vector2 target, bool emergency)
        {
            _car = car; _path = path; _target = target; _emergency = emergency;
            _car.Input = this;
        }

        /// <summary>Back into service: lights off, then join normal traffic.</summary>
        public void Release()
        {
            _released = true;
            var lights = GetComponent<Lightbar2D>();
            if (lights != null) lights.SetOn(false);
            var graph = RoadGraph2D.Instance;
            if (graph == null) return;
            int bestEdge = -1; float best = float.MaxValue;
            foreach (int e in graph.LaneEdges())
            {
                float d = Vector2.Distance(graph.Nodes[graph.Edges[e].From].Position, transform.position);
                if (d < best) { best = d; bestEdge = e; }
            }
            if (bestEdge < 0) return;
            var traffic = gameObject.AddComponent<TrafficDriver2D>();
            traffic.Begin(_car, graph, bestEdge);
            enabled = false;
        }

        private void FixedUpdate()
        {
            if (_released || _car == null || _path == null) return;
            Vector2 pos = transform.position;
            Vector2 fwd = transform.up;

            float toTarget = Vector2.Distance(pos, _target);
            if (Arrived || toTarget < 14f)
            {
                Arrived = true;
                _throttle = 0f; _steer = 0f; _brake = _car.ForwardSpeed > 0.3f ? 1f : 0f;
                return;
            }

            // Advance along the route; look a few metres ahead.
            while (_index < _path.Count - 1 && Vector2.Distance(pos, _path[_index]) < 6f) _index++;
            Vector2 aim = _path[_index];
            float lookahead = Mathf.Clamp(_car.ForwardSpeed * 0.6f, 5f, 14f);
            for (int i = _index; i < _path.Count; i++) { aim = _path[i]; if (Vector2.Distance(pos, _path[i]) > lookahead) break; }

            float angle = Vector2.SignedAngle(fwd, aim - pos);    // + = left
            _steer = Mathf.Clamp(-angle / 35f, -1f, 1f);
            float cruise = _emergency ? 20f : 15f;
            float wanted = Mathf.Lerp(cruise, 6f, Mathf.Clamp01(Mathf.Abs(angle) / 60f));
            if (toTarget < 40f) wanted = Mathf.Min(wanted, 4f + toTarget * 0.3f);
            float speed = _car.ForwardSpeed;
            _throttle = speed < wanted ? 1f : 0f;
            _brake = speed > wanted + 2f ? 0.6f : 0f;

            // Stuck behind something: hop to the next route point (rare, and usually off screen).
            _stuck = Mathf.Abs(speed) < 0.8f ? _stuck + Time.fixedDeltaTime : 0f;
            if (_stuck > 5f && _index < _path.Count - 2)
            {
                _index = Mathf.Min(_index + 3, _path.Count - 1);
                var rb = GetComponent<Rigidbody2D>();
                Vector2 p = _path[_index];
                Vector2 dir = (_path[Mathf.Min(_index + 1, _path.Count - 1)] - p).normalized;
                if (rb != null) { rb.position = p; rb.rotation = Mathf.Atan2(-dir.x, dir.y) * Mathf.Rad2Deg; }
                _stuck = 0f;
            }
        }
    }
}
