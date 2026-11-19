using System.Collections.Generic;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Minimap
{
    /// <summary>
    /// Street graph for the minimap's GPS line. Intersections are the nodes; neighbouring
    /// intersections along each street are joined. Shortest path by distance (the city only has a
    /// few dozen junctions, so a simple Dijkstra is plenty). One-way streets are ignored, so the line
    /// is a guide rather than a legal driving route.
    /// </summary>
    internal sealed class RoadNetwork
    {
        private struct Anchor
        {
            public CityLocationDatabase.Street Street;   // null when off-street
            public Vector2 Road;                         // point on the street's centre line
            public float Axis;                           // position along the street
            public List<(int node, float cost)> Nodes;   // junctions reachable from here
        }

        private readonly CityLocationDatabase _db;
        private readonly List<Vector2> _nodes = new List<Vector2>();
        private readonly List<List<(int to, float cost)>> _edges = new List<List<(int to, float cost)>>();
        private readonly Dictionary<string, List<int>> _byStreet = new Dictionary<string, List<int>>();

        public int NodeCount => _nodes.Count;

        public RoadNetwork(CityLocationDatabase db)
        {
            _db = db;
            foreach (var i in db.Intersections)
            {
                _nodes.Add(i.Position);
                _edges.Add(new List<(int to, float cost)>());
            }
            foreach (var s in db.Streets)
            {
                var list = new List<int>();
                for (int i = 0; i < db.Intersections.Count; i++)
                    if (db.Intersections[i].StreetA == s.Name || db.Intersections[i].StreetB == s.Name) list.Add(i);
                list.Sort((a, b) => AxisOf(s, _nodes[a]).CompareTo(AxisOf(s, _nodes[b])));
                _byStreet[s.Name] = list;
                for (int k = 0; k + 1 < list.Count; k++)
                {
                    float c = Vector2.Distance(_nodes[list[k]], _nodes[list[k + 1]]);
                    _edges[list[k]].Add((list[k + 1], c));
                    _edges[list[k + 1]].Add((list[k], c));
                }
            }
        }

        /// <summary>Fills <paramref name="into"/> with a polyline from <paramref name="from"/> to <paramref name="to"/> along the streets.</summary>
        public bool TryRoute(Vector2 from, Vector2 to, List<Vector2> into)
        {
            into.Clear();
            if (_nodes.Count == 0) return false;
            Anchor a = AnchorAt(from), b = AnchorAt(to);

            float direct = float.MaxValue;
            if (a.Street != null && b.Street == a.Street && SameSegment(a, b)) direct = Mathf.Abs(a.Axis - b.Axis);

            int n = _nodes.Count;
            var dist = new float[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            foreach (var (node, cost) in a.Nodes) if (cost < dist[node]) dist[node] = cost;

            for (int iter = 0; iter < n; iter++)
            {
                int u = -1;
                float best = float.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                foreach (var (v, c) in _edges[u])
                    if (dist[u] + c < dist[v]) { dist[v] = dist[u] + c; prev[v] = u; }
            }

            int end = -1;
            float total = float.MaxValue;
            foreach (var (node, cost) in b.Nodes)
                if (dist[node] < float.MaxValue && dist[node] + cost < total) { total = dist[node] + cost; end = node; }

            into.Add(from);
            into.Add(a.Road);
            if (direct <= total) { into.Add(b.Road); into.Add(to); return true; }
            if (end < 0) { into.Clear(); return false; }

            var path = new List<int>();
            for (int v = end; v >= 0; v = prev[v]) path.Add(v);
            path.Reverse();
            foreach (int v in path) into.Add(_nodes[v]);
            into.Add(b.Road);
            into.Add(to);
            return true;
        }

        private Anchor AnchorAt(Vector2 p)
        {
            var a = new Anchor { Nodes = new List<(int node, float cost)>() };
            a.Street = _db.StreetAt(p, 10f);
            if (a.Street != null && _byStreet.TryGetValue(a.Street.Name, out var list) && list.Count > 0)
            {
                Rect r = a.Street.Bounds;
                a.Road = a.Street.Vertical
                    ? new Vector2(r.center.x, Mathf.Clamp(p.y, r.yMin, r.yMax))
                    : new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), r.center.y);
                a.Axis = AxisOf(a.Street, a.Road);
                int below = -1, above = -1;
                foreach (int node in list)
                {
                    float ax = AxisOf(a.Street, _nodes[node]);
                    if (ax <= a.Axis) below = node;
                    else if (above < 0) above = node;
                }
                if (below >= 0) a.Nodes.Add((below, Mathf.Abs(a.Axis - AxisOf(a.Street, _nodes[below]))));
                if (above >= 0) a.Nodes.Add((above, Mathf.Abs(AxisOf(a.Street, _nodes[above]) - a.Axis)));
                return a;
            }

            a.Street = null;
            int nearest = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < _nodes.Count; i++)
            {
                float d = (_nodes[i] - p).sqrMagnitude;
                if (d < bestD) { bestD = d; nearest = i; }
            }
            a.Road = _nodes[nearest];
            a.Nodes.Add((nearest, 0f));
            return a;
        }

        /// <summary>True when no junction lies between the two points on their shared street.</summary>
        private bool SameSegment(Anchor a, Anchor b)
        {
            float lo = Mathf.Min(a.Axis, b.Axis), hi = Mathf.Max(a.Axis, b.Axis);
            foreach (int node in _byStreet[a.Street.Name])
            {
                float ax = AxisOf(a.Street, _nodes[node]);
                if (ax > lo + 1f && ax < hi - 1f) return false;
            }
            return true;
        }

        private static float AxisOf(CityLocationDatabase.Street s, Vector2 p) => s.Vertical ? p.y : p.x;
    }
}
