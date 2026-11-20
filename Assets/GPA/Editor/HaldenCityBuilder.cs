using System.Collections.Generic;
using System.Linq;
using GPA.TopDown2D;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace GPA.EditorTools
{
    /// <summary>
    /// Builds the fixed city of New Halden on Halden Island, a Manhattan-style grid laid out south to
    /// north: Harbor Point (downtown, dense offices), Midtown (wide crosstown streets, Halden Square),
    /// Halden Park (a large Central-Park-style park with curving paths and a pond, crossed by one
    /// transverse road), and Linden Heights (residential, narrow brownstones). Riverside Park runs up
    /// the west shore and an esplanade along the east river. Avenues run north-south, streets
    /// east-west; roads may stop at a crossing road (T-junctions), e.g. Merchant Avenue ends at the
    /// park and continues north of it as Carroll Avenue.
    ///
    /// Output: a saved Tilemap scene plus a <see cref="CityLocationDatabase"/> asset (streets,
    /// intersections, addressed buildings, districts). The layout below is hand-authored data and
    /// every "random" choice uses a fixed seed, so the city comes out identical on every build.
    /// Rebuilding replaces the scene and database.
    /// </summary>
    public static class HaldenCityBuilder
    {
        private const string ScenePath = "Assets/GPA/Scenes/City_Halden.unity";
        private const string DatabasePath = "Assets/GPA/Data/City/Halden_Locations.asset";
        private const string TileFolder = "Assets/GPA/Art/Tiles2D";
        private const string MaterialFolder = "Assets/GPA/Art/Materials";
        private const string PondMeshPath = "Assets/GPA/Data/City/Halden_Pond.asset";
        private const string GameSystemsPrefab = "Assets/GPA/Prefabs/Systems/GameSystems.prefab";

        private const float T = 4f;          // metres per tile
        // Grid extents in tiles (may be negative: the west suburb and the mainland sit west/south of the island).
        private const int MinX = -132, MaxX = 252, MinY = -60, MaxY = 282;
        private const int MapW = MaxX - MinX, MapH = MaxY - MinY;
        private static bool InMap(int x, int y) => x >= MinX && y >= MinY && x < MaxX && y < MaxY;
        // The harbour: water around the island, Eastport and Northgate. Everything outside it is mainland.
        private static readonly RectInt Basin = new RectInt(-8, -20, 216, 260);      // x -8..207, y -20..239
        // Westbrook: a suburb on the mainland across the west bridge.
        private static readonly RectInt WestLand = new RectInt(-100, 16, 90, 140);   // x -100..-11, y 16..155
        // The beltway (outer highway) ring, in tiles.
        // Where the four connector roads meet the curved beltway (tile coordinates of the ring centreline).
        private const int RingGarnetX = -118, RingLakeviewY = -44, RingCrosstownX = 236, RingGrandY = 266;
        private const float RingHalf = 2.5f;
        private const float HighwaySpeed = 25f;   // m/s on the beltway (90 km/h); city streets stay ~30 km/h   // beltway half-width in tiles (20 m wide)
        private static bool OutsideHighway(int x, int y) => !InsidePolygon(new Vector2(x + 0.5f, y + 0.5f), RingCoarse);
        // Declared before the road tables: their default extents read it.
        private static readonly RectInt Land = new RectInt(4, 4, 120, 156);   // the island: x 4..123, y 4..159
        // New districts across the bridges (spec: open the bridges).
        private static readonly RectInt PortLand = new RectInt(140, 60, 56, 88);     // Eastport: x 140..195, y 60..147
        private static readonly RectInt NorthLand = new RectInt(24, 176, 92, 52);    // Northgate: x 24..115, y 176..227
        private static readonly RectInt[] Lands = { Land, PortLand, NorthLand };
        // Real (drivable) bridges: Crosstown Blvd east to Eastport, Grand Blvd north to Northgate.
        private static readonly RectInt EastBridge = new RectInt(124, 94, 16, 5);
        private static readonly RectInt NorthBridge = new RectInt(58, 160, 7, 16);
        private static bool IsLand(int x, int y) { if (!Basin.Contains(new Vector2Int(x, y))) return true; foreach (var l in Lands) if (l.Contains(new Vector2Int(x, y))) return true; return false; }

        // Bridges from the districts to the mainland.
        private static readonly RectInt WestBridge = new RectInt(-8, 52, 12, 5);       // Garnet St to Westbrook
        private static readonly RectInt PortBridge = new RectInt(196, 94, 12, 5);      // Crosstown Blvd east to the beltway
        private static readonly RectInt UplandBridge = new RectInt(58, 228, 7, 12);    // Grand Blvd north to the beltway
        private static readonly RectInt[] Bridges = { EastBridge, NorthBridge, WestBridge, PortBridge, UplandBridge };
        private static bool IsBridge(int x, int y) { foreach (var b in Bridges) if (b.Contains(new Vector2Int(x, y))) return true; return false; }
        private const string Borough = "Halden Island";
        // The ring road: Riverside Drive, Water Street, East River Drive and Linden Avenue loop around
        // the island. Every other road stops at the ring, so no street runs off into the shoreline.
        private const int RingWest = 12, RingEast = 120, RingSouth = 6, RingNorth = 155;

        private const int NoLimit = int.MinValue;

        private sealed class Road
        {
            public string Name, Abbrev, Direction;
            public bool Vertical;
            public int Start, Width;   // across the road, in tiles
            public int Min, Max;       // along the road, in tiles [Min, Max)

            public Road(string name, string abbrev, bool vertical, int start, int width, string dir, int min = NoLimit, int max = NoLimit)
            {
                Name = name; Abbrev = abbrev; Vertical = vertical; Start = start; Width = width; Direction = dir;
                Min = min != NoLimit ? min : (vertical ? RingSouth : RingWest);
                Max = max != NoLimit ? max : (vertical ? RingNorth : RingEast);
            }

            public bool Covers(int c) => c >= Start && c < Start + Width;
            public bool Spans(int a) => a >= Min && a < Max;
            public int Centre => Start + Width / 2;
        }

        // ---- Hand-authored street plan (tile coordinates). Widths in 4 m tiles.
        // Two-way: lanes each side of a centre-line tile. One-way: every tile a lane.
        private static readonly Road[] Streets =
        {
            // Harbor Point (downtown)
            new Road("Water Street",        "Water St",       false,   6, 5, "two-way"),
            new Road("Bridge Street",       "Bridge St",      false,  18, 3, "east"),
            new Road("Exchange Place",      "Exchange Pl",    false,  30, 5, "two-way"),   // index 2: the precinct's street
            new Road("Union Street",        "Union St",       false,  42, 3, "west"),
            // Midtown
            new Road("Garnet Street",       "Garnet St",      false,  52, 5, "two-way", RingGarnetX + 2),  // over the west bridge, through Westbrook to the beltway
            new Road("12th Street",         "12th St",        false,  63, 3, "east"),
            new Road("20th Street",         "20th St",        false,  73, 5, "two-way"),
            new Road("26th Street",         "26th St",        false,  84, 3, "west"),
            new Road("Crosstown Boulevard", "Crosstown Blvd", false,  94, 5, "two-way", NoLimit, RingCrosstownX - 2),  // through Eastport to the beltway
            // Halden Park
            new Road("Park South",          "Park South",     false, 104, 3, "east"),
            new Road("Park Transverse",     "Transverse Rd",  false, 121, 5, "two-way"),
            new Road("Park North",          "Park North",     false, 139, 3, "west"),
            // Linden Heights
            new Road("Linden Avenue",       "Linden Ave",     false, 150, 5, "two-way"),
            // Eastport (industrial port across the east bridge)
            new Road("Pier Street",         "Pier St",        false,  64, 5, "two-way", 142, 193),
            new Road("Canal Street",        "Canal St",       false,  78, 3, "east",    142, 193),
            new Road("Rail Street",         "Rail St",        false, 110, 3, "west",    142, 193),
            new Road("Container Way",       "Container Way",  false, 126, 5, "two-way", 142, 193),
            new Road("North Quay",          "North Quay",     false, 139, 5, "two-way", 142, 193),
            // Northgate (suburb across the north bridge)
            new Road("Northgate Road",      "Northgate Rd",   false, 178, 5, "two-way",  28, 113),
            new Road("Maple Street",        "Maple St",       false, 192, 3, "east",     28, 113),
            new Road("Oak Street",          "Oak St",         false, 205, 3, "west",     28, 113),
            new Road("Hillcrest Road",      "Hillcrest Rd",   false, 218, 5, "two-way",  28, 113),
            // Westbrook (suburb on the west mainland)
            new Road("Brook Lane",          "Brook Ln",       false,  22, 3, "two-way", -96, -23),
            new Road("Willow Street",       "Willow St",      false,  36, 3, "two-way", -96, -23),
            new Road("Chestnut Street",     "Chestnut St",    false,  68, 3, "two-way", -96, -23),
            new Road("Westbrook Road",      "Westbrook Rd",   false,  84, 5, "two-way", -96, -23),
            new Road("Cedar Street",        "Cedar St",       false, 100, 3, "two-way", -96, -23),
            new Road("Hemlock Street",      "Hemlock St",     false, 116, 3, "two-way", -96, -23),
            new Road("Orchard Lane",        "Orchard Ln",     false, 132, 3, "two-way", -96, -23),
            new Road("Meadow Lane",         "Meadow Ln",      false, 148, 3, "two-way", -96, -23),
        };

        private static readonly Road[] Avenues =
        {
            new Road("Riverside Drive",     "Riverside Dr",   true,  12, 5, "two-way"),
            new Road("Hudson Avenue",       "Hudson Ave",     true,  26, 3, "north"),
            // Merchant Avenue stops at the park; north of it the same line is Carroll Avenue.
            new Road("Merchant Avenue",     "Merchant Ave",   true,  42, 3, "south", -1, 107),
            new Road("Carroll Avenue",      "Carroll Ave",    true,  42, 3, "south", 139, -1),
            new Road("Grand Boulevard",     "Grand Blvd",     true,  58, 7, "two-way", NoLimit, RingGrandY - 2),   // through Northgate to the beltway
            new Road("Park Avenue",         "Park Ave",       true,  76, 5, "two-way"),
            new Road("Pearl Street",        "Pearl St",       true,  92, 3, "north"),
            new Road("Bowery",              "Bowery",         true, 106, 3, "south"),
            new Road("East River Drive",    "East River Dr",  true, 115, 5, "two-way"),
            // Eastport
            new Road("Dock Road",           "Dock Rd",        true, 142, 5, "two-way",  64, 144),
            new Road("Foundry Street",      "Foundry St",     true, 166, 3, "north",    64, 144),
            new Road("Harbor Freight Road", "Freight Rd",     true, 188, 5, "two-way",  64, 144),
            // Northgate
            new Road("West Hill Road",      "West Hill Rd",   true,  28, 5, "two-way", 178, 223),
            new Road("Birch Avenue",        "Birch Ave",      true,  86, 3, "south",   178, 223),
            new Road("Elm Avenue",          "Elm Ave",        true, 108, 5, "two-way", 178, 223),
            // Westbrook
            new Road("Sycamore Avenue",     "Sycamore Ave",   true, -96, 5, "two-way",  22, 151),
            new Road("Pine Avenue",         "Pine Ave",       true, -72, 3, "two-way",  22, 151),
            new Road("Lakeview Avenue",     "Lakeview Ave",   true, -48, 5, "two-way",  RingLakeviewY + 2, 151),  // runs south out of Westbrook to the beltway
            new Road("Ridge Avenue",        "Ridge Ave",      true, -26, 3, "two-way",  22, 151),
        };

        // ---- Curved roads. Control points in tile coordinates (Catmull-Rom through them).
        private sealed class CurveRoad
        {
            public string Name;
            public Vector2[] Controls;   // tiles
            public bool Loop, Highway;
            public float HalfWidth;      // metres
            public Vector2[] Line;       // sampled centreline, world metres
        }

        /// <summary>The beltway: a smooth closed loop through the four connector junctions, wandering through the woods.</summary>
        private static readonly CurveRoad Ring = new CurveRoad
        {
            Name = "Halden Beltway", Loop = true, Highway = true, HalfWidth = RingHalf * T,
            Controls = new[]
            {
                new Vector2(RingGarnetX, 54.5f), new Vector2(-121f, 130f), new Vector2(-108f, 205f), new Vector2(-70f, 250f),
                new Vector2(0f, 262f), new Vector2(61.5f, RingGrandY), new Vector2(130f, 258f), new Vector2(200f, 250f), new Vector2(233f, 200f),
                new Vector2(238f, 140f), new Vector2(RingCrosstownX, 96.5f), new Vector2(240f, 40f), new Vector2(222f, -20f), new Vector2(170f, -45f),
                new Vector2(80f, -40f), new Vector2(10f, -48f), new Vector2(-45.5f, RingLakeviewY), new Vector2(-95f, -35f), new Vector2(-118f, -5f),
            },
        };

        /// <summary>Crescents and bends: suburban loops in Westbrook and haul roads in Eastport. Each end meets a grid road at its kerb, at right angles.</summary>
        /// <summary>Local crescents and bends were removed: the beltway is the only curved road.</summary>
        private static readonly CurveRoad[] LocalCurves = new CurveRoad[0];

        private static IEnumerable<CurveRoad> AllCurves() { yield return Ring; foreach (var c in LocalCurves) yield return c; }

        /// <summary>Catmull-Rom spline through the control points, sampled about every <paramref name="step"/> metres (world).</summary>
        private static Vector2[] SampleCurve(CurveRoad c, float step)
        {
            var pts = c.Controls.Select(p => p * T).ToArray();
            int n = pts.Length, segs = c.Loop ? n : n - 1;
            var line = new List<Vector2>();
            for (int i = 0; i < segs; i++)
            {
                Vector2 p0 = pts[c.Loop ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[c.Loop ? (i + 2) % n : Mathf.Min(i + 2, n - 1)];
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / step));
                for (int k = 0; k < steps; k++)
                {
                    float t = k / (float)steps, t2 = t * t, t3 = t2 * t;
                    line.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            if (!c.Loop) line.Add(pts[n - 1]);
            return line.ToArray();
        }

        private static readonly Vector2[] RingCoarse = SampleCurve(Ring, 24f).Select(p => p / T).ToArray();   // tiles

        private static bool InsidePolygon(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            return inside;
        }

        /// <summary>Marks the cells under each curved road so lots, trees and sidewalks work around it.</summary>
        private static void StampCurves()
        {
            // Beltway ramps: keep lots, estates and trees off them.
            _ringFine = null;
            foreach (Ramp r in BeltwayRamps())
                if (!r.Far)   // the far ramps are stamped after the estates are planned (StampInterchanges)
                for (int i = 0; i + 1 < r.Points.Count; i++)
                {
                    Vector2 a = r.Points[i], b = r.Points[i + 1];
                    float rr = RampHalf + 3f;
                    int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - rr) / T), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + rr) / T);
                    int y0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - rr) / T), y1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + rr) / T);
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!InMap(x, y) || (_grid[x, y] != Cell.Lot && _grid[x, y] != Cell.Forest)) continue;
                        if (DistToSegment(new Vector2((x + 0.5f) * T, (y + 0.5f) * T), a, b) <= rr) _grid[x, y] = Cell.Highway;
                    }
                }
            foreach (var c in AllCurves())
            {
                c.Line = SampleCurve(c, 2f);
                float r = c.HalfWidth + (c.Highway ? 3f : 0.8f);
                Cell kind = c.Highway ? Cell.Highway : Cell.Curve;
                foreach (Vector2 p in c.Line)
                {
                    int x0 = Mathf.FloorToInt((p.x - r) / T), x1 = Mathf.FloorToInt((p.x + r) / T);
                    int y0 = Mathf.FloorToInt((p.y - r) / T), y1 = Mathf.FloorToInt((p.y + r) / T);
                    for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (!InMap(x, y)) continue;
                        Cell cur = _grid[x, y];
                        if (cur != Cell.Lot && cur != Cell.Forest) continue;
                        Vector2 centre = new Vector2((x + 0.5f) * T, (y + 0.5f) * T);
                        if ((centre - p).magnitude <= r) _grid[x, y] = kind;
                    }
                }
            }
        }

        /// <summary>
        /// The four access roads outside town: 1 = one of the three middle tiles that stay road (two lanes
        /// and the centre line), 2 = an outer tile that becomes grass verge (or water, on a bridge),
        /// 3 = a tile by the beltway where the road gives way to the overpass. 0 = anything else.
        /// </summary>
        private static int ConnectorKind(int x, int y)
        {
            int Kind(Road r, int across, int along, bool outside, float inward)
            {
                if (!outside || !r.Covers(across) || !r.Spans(along)) return 0;
                if (inward < StemCut) return 3;
                return Mathf.Abs(across - (r.Start + r.Width / 2)) <= 1 ? 1 : 2;
            }
            int k;
            if ((k = Kind(RoadNamed("Garnet Street"), y, x, x < WestLand.xMin, (x + 0.5f - RingGarnetX) * T)) != 0) return k;             // west
            if ((k = Kind(RoadNamed("Lakeview Avenue"), x, y, y < WestLand.yMin, (y + 0.5f - RingLakeviewY) * T)) != 0) return k;         // south
            if ((k = Kind(RoadNamed("Crosstown Boulevard"), y, x, x >= PortLand.xMax, (RingCrosstownX - x - 0.5f) * T)) != 0) return k;   // east
            if ((k = Kind(RoadNamed("Grand Boulevard"), x, y, y >= NorthLand.yMax, (RingGrandY - y - 0.5f) * T)) != 0) return k;          // north
            return 0;
        }

        private static readonly Dictionary<string, Road> _roadByName = new Dictionary<string, Road>();
        private static Road RoadNamed(string name)
        {
            if (!_roadByName.TryGetValue(name, out Road r)) _roadByName[name] = r = Streets.Concat(Avenues).First(q => q.Name == name);
            return r;
        }

        /// <summary>The access roads' bridges over the harbour, narrowed to match the roads.</summary>
        private static readonly RectInt PortBridgeDeck = new RectInt(196, 95, 12, 3);
        private static readonly RectInt UplandBridgeDeck = new RectInt(60, 228, 3, 12);

        /// <summary>
        /// Run after the estates are planned (so they come out the same as before): narrows the access
        /// roads outside town, ends them short of the beltway (the overpass carries them over it) and
        /// clears the ground under the far-side ramps. Returns how many estates the new ramps touch.
        /// </summary>
        private static int StampInterchanges()
        {
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
            {
                int k = ConnectorKind(x, y);
                if (k == 2) _grid[x, y] = IsBridge(x, y) ? Cell.Water : Cell.Highway;
                else if (k == 3) _grid[x, y] = Cell.Highway;
            }
            var ramps = BeltwayRamps();
            void ClearUnder(Vector2 a, Vector2 b, float rr)
            {
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - rr) / T), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x) + rr) / T);
                int y0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y) - rr) / T), y1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y) + rr) / T);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!InMap(x, y) || (_grid[x, y] != Cell.Lot && _grid[x, y] != Cell.Forest)) continue;
                    if (DistToSegment(new Vector2((x + 0.5f) * T, (y + 0.5f) * T), a, b) <= rr) _grid[x, y] = Cell.Highway;
                }
            }
            foreach (Ramp r in ramps)
                if (r.Far)
                    for (int i = 0; i + 1 < r.Points.Count; i++) ClearUnder(r.Points[i], r.Points[i + 1], RampHalf + 3f);
            foreach (Interchange ic in InterchangesWithRamps(ramps))
                ClearUnder(ic.P(0f, StemCut + 2f), ic.P(0f, BellY0), StemHalf + 3f);   // the access road, the bridge and the stub beyond it
            int touched = 0;
            foreach (Estate e in _estates)
            {
                bool hit = false;
                for (int y = e.Lot.yMin; y < e.Lot.yMax && !hit; y++)
                for (int x = e.Lot.xMin; x < e.Lot.xMax && !hit; x++)
                    if (Is(x, y, Cell.Highway)) hit = true;
                if (hit) { touched++; Debug.LogWarning($"[City] Interchange ramps run through the estate at {e.Number} {e.Street} ({e.Lot})."); }
            }
            return touched;
        }

        private sealed class DistrictInfo
        {
            public string Name, Prefix;
            public int YMin, YMax;               // tile rows [YMin, YMax)
            public int XMin = 0, XMax = 128;     // tile columns [XMin, XMax)
            public float Warehouse;              // share of warehouses (Eastport)
            public float Apartment, Shop, Office; // cumulative-ish weights; the rest is restaurants
            public int LotMin, LotMax;           // lot frontage in tiles
            public int OfficeFloorsMin, OfficeFloorsMax;
            public bool Suburban;                // detached houses with yards
        }

        private static readonly DistrictInfo[] Districts =
        {
            new DistrictInfo { Name = "Harbor Point",   Prefix = "HP", YMin = 0,   YMax = 52,  Apartment = 0.20f, Shop = 0.20f, Office = 0.50f, LotMin = 4, LotMax = 8, OfficeFloorsMin = 8, OfficeFloorsMax = 30 },
            new DistrictInfo { Name = "Midtown",        Prefix = "MT", YMin = 52,  YMax = 104, Apartment = 0.25f, Shop = 0.30f, Office = 0.35f, LotMin = 3, LotMax = 7, OfficeFloorsMin = 6, OfficeFloorsMax = 24 },
            new DistrictInfo { Name = "Parkside",       Prefix = "PS", YMin = 104, YMax = 141, Apartment = 0.55f, Shop = 0.20f, Office = 0.15f, LotMin = 3, LotMax = 6, OfficeFloorsMin = 4, OfficeFloorsMax = 12 },
            new DistrictInfo { Name = "Linden Heights", Prefix = "LH", YMin = 141, YMax = 168, Apartment = 0.70f, Shop = 0.15f, Office = 0.05f, LotMin = 2, LotMax = 4, OfficeFloorsMin = 3, OfficeFloorsMax = 6 },
            new DistrictInfo { Name = "Eastport",       Prefix = "EP", XMin = 128, XMax = 208, YMin = -20, YMax = 168, Apartment = 0.06f, Shop = 0.08f, Office = 0.14f, Warehouse = 0.62f, LotMin = 4, LotMax = 9, OfficeFloorsMin = 2, OfficeFloorsMax = 6 },
            new DistrictInfo { Name = "Northgate",      Prefix = "NG", XMin = -8, XMax = 208, YMin = 168, YMax = 240, Apartment = 0.74f, Shop = 0.14f, Office = 0.04f, LotMin = 2, LotMax = 3, OfficeFloorsMin = 2, OfficeFloorsMax = 4 },
            new DistrictInfo { Name = "Westbrook",      Prefix = "WB", XMin = -104, XMax = -8, YMin = 0, YMax = 168, Apartment = 0.86f, Shop = 0.08f, Office = 0.02f, LotMin = 3, LotMax = 5, OfficeFloorsMin = 2, OfficeFloorsMax = 3, Suburban = true },
            new DistrictInfo { Name = "Halden County",  Prefix = "HC", XMin = MinX, XMax = MaxX, YMin = MinY, YMax = MaxY, Apartment = 0.9f, LotMin = 3, LotMax = 5, OfficeFloorsMin = 1, OfficeFloorsMax = 2, Suburban = true },
        };

        private static DistrictInfo DistrictAt(int x, int y)
        {
            if (Basin.Contains(new Vector2Int(x, y)) || WestLand.Contains(new Vector2Int(x, y)))
                foreach (var d in Districts) if (x >= d.XMin && x < d.XMax && y >= d.YMin && y < d.YMax) return d;
            return Districts[Districts.Length - 1];   // the county (mainland outside the suburbs)
        }

        // ---- Green and open spaces (tile rects; only plain lots inside them change, sidewalks stay).
        private sealed class OpenSpace { public string Name; public RectInt Area; public bool Plaza; }

        private static readonly OpenSpace[] OpenSpaces =
        {
            new OpenSpace { Name = "Halden Park",          Area = new RectInt(29, 107, 29, 32) },   // x 29..57, y 107..138
            new OpenSpace { Name = "Battery Green",        Area = new RectInt(18, 12, 7, 5) },
            new OpenSpace { Name = "Pearl Gardens",        Area = new RectInt(82, 79, 9, 4) },
            new OpenSpace { Name = "Linden Playground",    Area = new RectInt(96, 143, 9, 6) },
            new OpenSpace { Name = "Riverside Park",       Area = new RectInt(4, 4, 8, 156) },
            new OpenSpace { Name = "North Cove Park",      Area = new RectInt(4, 155, 120, 5) },
            new OpenSpace { Name = "East River Esplanade", Area = new RectInt(121, 4, 3, 156) },
            new OpenSpace { Name = "Halden Square",        Area = new RectInt(66, 88, 9, 5), Plaza = true },
        };

        private static OpenSpace OpenSpaceAt(int x, int y)
        {
            foreach (var o in OpenSpaces) if (o.Area.Contains(new Vector2Int(x, y))) return o;
            return null;
        }

        private enum Cell { Water, Road, Sidewalk, Lot, Park, Plaza, Forest, Curve, Highway }

        private static readonly Dictionary<string, TileBase> Tiles = new Dictionary<string, TileBase>();
        /// <summary>A 2D array indexed by tile coordinates that may be negative (offset by MinX/MinY).</summary>
        private sealed class TileGrid<TV>
        {
            private readonly TV[,] _a = new TV[MapW, MapH];
            public TV this[int x, int y] { get => _a[x - MinX, y - MinY]; set => _a[x - MinX, y - MinY] = value; }
        }
        private static TileGrid<Cell> _grid;
        private static int _counter;

        // Halden Park features (world metres), computed before trees are planted so trees avoid them.
        private static readonly List<Vector2[]> _paths = new List<Vector2[]>();
        private static Vector2[] _pond;

        // ------------------------------------------------------------------ entry points

        [MenuItem("GPA/City/Build New Halden")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                !EditorUtility.DisplayDialog("Rebuild city", $"{ScenePath} already exists. Rebuilding replaces the scene and its location database (hand edits in that scene are lost).", "Rebuild", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildWithoutPrompts();
        }

        /// <summary>Same as the menu item but never shows a dialog (for automation). Unsaved scene changes are discarded.</summary>
        public static string BuildWithoutPrompts()
        {
            if (TopDown2DSceneBuilder.S("tile_water") == null) TopDown2DSceneBuilder.ReimportSprites();
            if (TopDown2DSceneBuilder.S("tile_water") == null) return "Sprite tile_water not found — copy the latest sprites first.";
            if (!Sorting2D.LayersAvailable)
                Debug.Log("[City] Project sorting layers not loaded in this editor session; using Default-layer draw order (renders the same).");

            var rng = new System.Random(2026);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Tiles.Clear();
            _counter = 0;

            Classify();
            PlanParkFeatures();
            PlanEstates();
            StampInterchanges();
            var db = CreateDatabase();
            var grid = new GameObject("City Grid").AddComponent<Grid>();
            grid.cellSize = new Vector3(T, T, 0f);
            Tilemap water = NewTilemap(grid, "Water", Sorting2D.Water);
            Tilemap ground = NewTilemap(grid, "Ground", Sorting2D.Ground);
            Tilemap roofs = NewTilemap(grid, "Roofs", Sorting2D.Roofs);

            PaintGround(water, ground);
            var buildingsRoot = new GameObject("Buildings").transform;
            var propsRoot = new GameObject("Props").transform;
            var parksRoot = new GameObject("Parks").transform;
            BuildLotsAndBuildings(roofs, buildingsRoot, parksRoot, db, rng);
            BuildParkFeatures(parksRoot);
            BuildEstates(buildingsRoot, db);
            PlantForest(new GameObject("Forest").transform);
            BuildCurvedRoadMeshes(new GameObject("Curved Roads").transform);
            PlaceStreetProps(propsRoot, rng);
            AddBoundary();
            FillStreetsAndIntersections(db);

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();

            // ---- systems, spawns, player
            var systems = AssetDatabase.LoadAssetAtPath<GameObject>(GameSystemsPrefab);
            if (systems != null) PrefabUtility.InstantiatePrefab(systems, scene);
            new GameObject("Haptics").AddComponent<GPA.Input.HapticsService>();
            var locations = new GameObject("City Locations").AddComponent<CityLocations>();
            var lso = new SerializedObject(locations);
            lso.FindProperty("_database").objectReferenceValue = db;
            lso.ApplyModifiedPropertiesWithoutUndo();

            BuildSpawnPoints();
            string graphReport = BuildRoadGraphAndSignals(propsRoot);
            BuildWalkMap();
            new GameObject("Records Database").AddComponent<GPA.Records.RecordsDatabase>();
            new GameObject("Traffic").AddComponent<TrafficManager2D>();
            new GameObject("Pedestrians").AddComponent<PedestrianManager2D>();
            var police = new GameObject("Police");
            police.AddComponent<GPA.Police.InteractionMenu2D>();
            police.AddComponent<GPA.Police.TrafficStopManager>();
            police.AddComponent<GPA.Police.TrafficStopHUD>();
            police.AddComponent<GPA.Police.ArrestPipeline2D>();
            police.AddComponent<GPA.Police.OfficerCommands2D>();
            police.AddComponent<GPA.Police.PoliceHUD2D>();
            new GameObject("Police Computer").AddComponent<GPA.Mdt.PoliceComputer>();
            new GameObject("Minimap").AddComponent<GPA.Minimap.Minimap2D>();
            var radio = new GameObject("Radio Audio Manager");
            radio.AddComponent<GPA.Radio.RadioAudioManager>();
            radio.AddComponent<GPA.Radio.RadioMenu>();
            new GameObject("Interiors").AddComponent<InteriorManager2D>();
            TopDown2DSceneBuilder.BuildPatrolSystems();
            // Splayed "lying down" body sprites (people knocked down or killed).
            var worldSprites = Object.FindAnyObjectByType<WorldSprites2D>();
            if (worldSprites != null)
                worldSprites.Downed = new[] { "civ_athletic_red", "civ_coat_green", "civ_dress_purple", "civ_hoodie_grey", "civ_jacket_brown", "civ_jacket_tan_f", "civ_suit_navy", "civ_vest_orange", "officer" }
                    .Select(n => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/GPA/Art/Sprites2D/{n}_down.png")).Where(sp => sp != null).ToArray();

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.17f, 0.29f, 0.37f);
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<Camera2DFollow>();

            // Player starts outside the 1st Precinct with a patrol car at the curb.
            CityLocationDatabase.Building precinct = db.Buildings.Find(b => b.Type == "police");
            Vector2 start = precinct != null ? precinct.Entrance : new Vector2(280f, 140f);
            camGo.transform.position = new Vector3(start.x, start.y, -20f);
            Road exchange = Streets[2];
            Vector2 carSpot = new Vector2(start.x + 6f, exchange.Start * T + T * 0.5f); // eastbound lane, south side
            Car2D cruiser = TopDown2DSceneBuilder.SpawnCar("car_police_cruiser", "Patrol 4127", carSpot, -90f, emergency: true);
            cruiser.gameObject.AddComponent<Car2DHaptics>();
            TopDown2DSceneBuilder.AddLightbar(cruiser);
            cruiser.gameObject.AddComponent<GPA.Police.CruiserTrunk2D>();
            GameObject officer = TopDown2DSceneBuilder.SpawnOfficer(start);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = officer;
            string report = $"[City] Built {ScenePath}: {db.Districts.Count} districts, {db.Streets.Count} streets, {db.Intersections.Count} intersections, " +
                            $"{db.Buildings.Count} buildings and spaces. {graphReport}";
            Debug.Log(report);
            return report;
        }

        // ------------------------------------------------------------------ layout

        private static Road AvenueAt(int x, int y) { foreach (var r in Avenues) if (r.Covers(x) && r.Spans(y)) return r; return null; }
        private static Road StreetAt(int x, int y) { foreach (var r in Streets) if (r.Covers(y) && r.Spans(x)) return r; return null; }

        /// <summary>True when the avenue and street really cross (each spans the other's full width).</summary>
        private static bool Crosses(Road avenue, Road street) =>
            avenue.Min <= street.Start && street.Start + street.Width <= avenue.Max &&
            street.Min <= avenue.Start && avenue.Start + avenue.Width <= street.Max;

        private static void Classify()
        {
            _grid = new TileGrid<Cell>();
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
            {
                if (IsBridge(x, y)) { _grid[x, y] = Cell.Road; continue; }
                if (!IsLand(x, y)) { _grid[x, y] = Cell.Water; continue; }
                bool road = AvenueAt(x, y) != null || StreetAt(x, y) != null;
                bool mainland = !Basin.Contains(new Vector2Int(x, y));
                _grid[x, y] = road ? Cell.Road : mainland && !WestLand.Contains(new Vector2Int(x, y)) ? Cell.Forest : Cell.Lot;
            }
            StampCurves();
            // Sidewalks: land next to a road (8-neighbourhood) or along the waterfront.
            var sidewalk = new List<Vector2Int>();
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
            {
                if (_grid[x, y] != Cell.Lot) continue;
                bool edge = false;
                for (int dy = -1; dy <= 1 && !edge; dy++)
                for (int dx = -1; dx <= 1 && !edge; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!InMap(nx, ny)) continue;
                    if (_grid[nx, ny] == Cell.Road || _grid[nx, ny] == Cell.Water || _grid[nx, ny] == Cell.Curve) edge = true;
                }
                if (edge) sidewalk.Add(new Vector2Int(x, y));
            }
            foreach (var p in sidewalk) _grid[p.x, p.y] = Cell.Sidewalk;

            // Parks and plazas replace plain lots only, so their sidewalks stay.
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
            {
                if (_grid[x, y] != Cell.Lot) continue;
                OpenSpace o = OpenSpaceAt(x, y);
                if (o != null) _grid[x, y] = o.Plaza ? Cell.Plaza : Cell.Park;
            }
        }

        private static bool Is(int x, int y, Cell c) => InMap(x, y) && _grid[x, y] == c;

        private static void PaintGround(Tilemap water, Tilemap ground)
        {
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                switch (_grid[x, y])
                {
                    case Cell.Water: water.SetTile(cell, GetTile("tile_water")); break;
                    case Cell.Park: ground.SetTile(cell, GetTile("tile_grass")); break;
                    case Cell.Forest when _tracks.Contains(new Vector2Int(x, y)): PaintDirt(ground, x, y); break;
                    case Cell.Forest when _cleared.Contains(new Vector2Int(x, y)): PaintGrass(ground, x, y, 0.9f); break;   // estate lawns
                    case Cell.Forest: PaintGrass(ground, x, y, OutsideHighway(x, y) ? 0.62f : 0.78f); break;
                    case Cell.Highway: PaintGrass(ground, x, y, 0.7f); break;
                    case Cell.Curve: ground.SetTile(cell, GetTile("tile_sidewalk")); break;
                    case Cell.Lot when DistrictAt(x, y).Suburban: PaintGrass(ground, x, y, 0.92f); break;
                    case Cell.Lot:
                    case Cell.Plaza: ground.SetTile(cell, GetTile("tile_sidewalk")); break;
                    case Cell.Sidewalk: PaintSidewalk(ground, x, y); break;
                    case Cell.Road: PaintRoad(ground, x, y); break;
                }
            }
            // Forest floor past the edge of the map, so the camera never sees the void.
            const int margin = 40;
            for (int y = MinY - margin; y < MaxY + margin; y++)
            for (int x = MinX - margin; x < MaxX + margin; x++)
                if (!InMap(x, y)) PaintGrass(ground, x, y, 0.55f);

            // Ferry pier off the south shore, below Grand Boulevard.
            for (int y = 0; y < Land.yMin; y++)
            for (int x = 57; x <= 65; x++)
                ground.SetTile(new Vector3Int(x, y, 0), GetTile("tile_pier"));

            // Drivable bridges between the districts and out to the mainland.
            PaintBridge(ground, horizontal: true,  across: EastBridge.yMin,   from: EastBridge.xMin,   to: EastBridge.xMax);
            PaintBridge(ground, horizontal: false, across: NorthBridge.xMin,  from: NorthBridge.yMin,  to: NorthBridge.yMax, width: 7);
            PaintBridge(ground, horizontal: true,  across: WestBridge.yMin,   from: WestBridge.xMin,   to: WestBridge.xMax);
            PaintBridge(ground, horizontal: true,  across: PortBridgeDeck.yMin,   from: PortBridgeDeck.xMin,   to: PortBridgeDeck.xMax,   width: 3);
            PaintBridge(ground, horizontal: false, across: UplandBridgeDeck.xMin, from: UplandBridgeDeck.yMin, to: UplandBridgeDeck.yMax, width: 3);
        }

        /// <summary>Grass tinted darker for woodland (lower shade = denser forest).</summary>
        private static void PaintGrass(Tilemap ground, int x, int y, float shade)
        {
            var cell = new Vector3Int(x, y, 0);
            ground.SetTile(cell, GetTile("tile_grass"));
            ground.SetTileFlags(cell, TileFlags.None);
            float n = ((x * 73856093) ^ (y * 19349663)) & 15;
            float k = shade + n * 0.006f;
            ground.SetColor(cell, new Color(k * 0.92f, k, k * 0.86f));
        }

        /// <summary>A two-lane bridge deck with kerbs, drawn over the water.</summary>
        private static void PaintBridge(Tilemap ground, bool horizontal, int across, int from, int to, int width = 5)
        {
            float rot = horizontal ? 90f : 0f;
            for (int a = from; a < to; a++)
            for (int i = -1; i <= width; i++)
            {
                int x = horizontal ? a : across + i, y = horizontal ? across + i : a;
                string sprite = i == -1 || i == width ? "tile_sidewalk_curb" : i == width / 2 ? "tile_asphalt_lane_double_yellow" : "tile_asphalt";
                float r = i == -1 ? (horizontal ? 90f : 0f) : i == width ? (horizontal ? -90f : 180f) : rot;
                Set(ground, x, y, sprite, r);
            }
        }

        private static void PaintSidewalk(Tilemap ground, int x, int y)
        {
            bool w = Is(x - 1, y, Cell.Road), e = Is(x + 1, y, Cell.Road), n = Is(x, y + 1, Cell.Road), s = Is(x, y - 1, Cell.Road);
            string sprite = "tile_sidewalk";
            float rot = 0f;
            if ((w || e) && (n || s))
            {
                sprite = "tile_sidewalk_corner";
                rot = w && s ? 0f : w && n ? -90f : e && n ? 180f : 90f;
            }
            else if (w || e || n || s)
            {
                sprite = "tile_sidewalk_curb";
                rot = w ? 0f : e ? 180f : n ? -90f : 90f;
            }
            Set(ground, x, y, sprite, rot);
        }

        private static void PaintRoad(Tilemap ground, int x, int y)
        {
            Road av = AvenueAt(x, y), st = StreetAt(x, y);
            if (av != null && st != null) { Set(ground, x, y, (x * 7 + y * 3) % 11 == 0 ? "tile_asphalt_manhole" : "tile_asphalt", 0f); return; }

            Road road = av ?? st;
            int across = road.Vertical ? x - road.Start : y - road.Start;
            int along = road.Vertical ? y : x;
            bool beforeJunction = road.Vertical
                ? StreetAt(x, y + 1) != null || StreetAt(x, y - 1) != null
                : AvenueAt(x + 1, y) != null || AvenueAt(x - 1, y) != null;
            float rot = road.Vertical ? 0f : 90f;

            if (beforeJunction) { Set(ground, x, y, "tile_crosswalk", rot); return; }
            bool twoWay = road.Direction == "two-way";
            if (ConnectorKind(x, y) == 1 && across != road.Width / 2) { Set(ground, x, y, "tile_asphalt", rot); return; }   // narrowed access road: one lane each way
            if (twoWay && across == road.Width / 2) { Set(ground, x, y, "tile_asphalt_lane_double_yellow", rot); return; }
            // Dashed divider on the low-side edge when the neighbouring tile is a lane going the same way.
            bool sameWayBelow = across > 0 && (!twoWay || (across - 1 != road.Width / 2 && (across < road.Width / 2) == (across - 1 < road.Width / 2)));
            if (sameWayBelow) { Set(ground, x, y, "tile_asphalt_lane_dash_edge", rot); return; }
            Set(ground, x, y, (along * 5 + across * 3) % 13 == 0 ? "tile_asphalt_oil" : "tile_asphalt", rot);
        }

        // ------------------------------------------------------------------ buildings, parks, plazas

        private static void BuildLotsAndBuildings(Tilemap roofs, Transform buildingsRoot, Transform parksRoot, CityLocationDatabase db, System.Random rng)
        {
            var visited = new TileGrid<bool>();
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
            {
                Cell kind = _grid[x, y];
                if (visited[x, y] || (kind != Cell.Lot && kind != Cell.Park && kind != Cell.Plaza)) continue;
                // Grow the largest rectangle of this kind: right, then up while the whole row matches.
                int x1 = x; while (x1 + 1 < MaxX && _grid[x1 + 1, y] == kind && !visited[x1 + 1, y]) x1++;
                int y1 = y;
                while (y1 + 1 < MaxY)
                {
                    bool rowOk = true;
                    for (int xx = x; xx <= x1 && rowOk; xx++) rowOk = _grid[xx, y1 + 1] == kind && !visited[xx, y1 + 1];
                    if (!rowOk) break;
                    y1++;
                }
                for (int yy = y; yy <= y1; yy++) for (int xx = x; xx <= x1; xx++) visited[xx, yy] = true;
                var block = new RectInt(x, y, x1 - x + 1, y1 - y + 1);

                if (kind == Cell.Park) { BuildPark(block, parksRoot, db, rng); continue; }
                if (kind == Cell.Plaza) { BuildPlaza(block, parksRoot, db); continue; }
                foreach (RectInt lot in SplitBlock(block, rng))
                    BuildBuilding(lot, roofs, buildingsRoot, db, rng);
            }
        }

        private static IEnumerable<RectInt> SplitBlock(RectInt block, System.Random rng)
        {
            DistrictInfo d = DistrictAt(block.xMin + block.width / 2, block.yMin + block.height / 2);
            int rows = block.height >= 6 ? 2 : 1;
            int southHeight = rows == 2 ? block.height / 2 : block.height;
            for (int row = 0; row < rows; row++)
            {
                int y0 = row == 0 ? block.yMin : block.yMin + southHeight;
                int h = row == 0 ? southHeight : block.height - southHeight;
                int x = block.xMin;
                while (x < block.xMax)
                {
                    int w = rng.Next(d.LotMin, d.LotMax);
                    if (block.xMax - (x + w) < d.LotMin) w = block.xMax - x; // don't leave a sliver
                    yield return new RectInt(x, y0, w, h);
                    x += w;
                }
            }
        }

        private static void BuildBuilding(RectInt lot, Tilemap roofs, Transform root, CityLocationDatabase db, System.Random rng)
        {
            Vector2 centre = new Vector2((lot.xMin + lot.width * 0.5f) * T, (lot.yMin + lot.height * 0.5f) * T);
            (Road frontage, Vector2 entrance) = Frontage(lot);
            // Suburban lots: the house sits back from the lot lines with a lawn all round.
            float inset = DistrictAt(lot.xMin + lot.width / 2, lot.yMin + lot.height / 2).Suburban ? 0.45f : 0f;
            // Leftover slivers (e.g. beside a curved road) stay as lawn or paving rather than squeezing in a building.
            int minSide = inset > 0f ? 3 : 2;
            if (lot.width < minSide || lot.height < minSide) return;
            if (frontage == null) return;
            int number = AddressNumber(frontage, lot, entrance);
            DistrictInfo district = DistrictAt(lot.xMin + lot.width / 2, lot.yMin + lot.height / 2);

            // The 1st Precinct sits on Exchange Place just east of Grand Boulevard.
            Road grand = Avenues[4], exchange = Streets[2];
            bool precinct = db.Buildings.Find(b => b.Type == "police") == null && frontage == exchange &&
                            lot.xMin >= grand.Start + grand.Width && lot.yMin >= exchange.Start + exchange.Width;
            string type, name; int floors; string roof;
            Color colour;
            if (precinct)
            {
                type = "police"; name = "1st Precinct — Harbor Point"; floors = 3; roof = "tile_roof_police"; colour = Color.white;
            }
            else
            {
                double r = rng.NextDouble();
                bool brownstone = false;
                if (district.Suburban && r < district.Apartment)
                {
                    type = "apartment"; name = "House"; floors = rng.Next(1, 3);
                }
                else if (r < district.Apartment)
                {
                    type = "apartment";
                    brownstone = lot.width <= 3 && district.Prefix != "HP";
                    name = brownstone ? "Brownstone" : "Apartment building";
                    floors = brownstone ? rng.Next(3, 5) : rng.Next(4, 9);
                }
                else if (r < district.Apartment + district.Shop) { type = "shop"; name = "Shop"; floors = rng.Next(1, 3); }
                else if (r < district.Apartment + district.Shop + district.Office) { type = "office"; name = "Office building"; floors = rng.Next(district.OfficeFloorsMin, district.OfficeFloorsMax); }
                else if (r < district.Apartment + district.Shop + district.Office + district.Warehouse) { type = "warehouse"; name = "Warehouse"; floors = rng.Next(1, 3); }
                else { type = "restaurant"; name = "Restaurant"; floors = 1; }

                if (district.Suburban && type == "apartment") roof = rng.NextDouble() < 0.5 ? "tile_roof_brick_edge" : "tile_roof_gravel";
                else if (brownstone) roof = "tile_roof_brick_edge";
                else if (type == "office" && floors > 15) roof = rng.NextDouble() < 0.6 ? "tile_roof_light" : "tile_roof_gravel";
                else roof = new[] { "tile_roof_tar", "tile_roof_gravel", "tile_roof_light" }[rng.Next(3)];

                float tint = 0.86f + (float)rng.NextDouble() * 0.14f;
                colour = brownstone
                    ? new Color(0.78f + (float)rng.NextDouble() * 0.12f, 0.6f + (float)rng.NextDouble() * 0.08f, 0.52f + (float)rng.NextDouble() * 0.06f)
                    : new Color(tint, tint * (0.97f + (float)rng.NextDouble() * 0.03f), tint * (0.95f + (float)rng.NextDouble() * 0.05f));
            }


            string id = $"{district.Prefix}-{++_counter:0000}";
            var go = new GameObject($"{id} {number} {frontage.Abbrev}");
            go.transform.SetParent(root);
            go.transform.position = centre;
            go.isStatic = true;
            go.AddComponent<BoxCollider2D>().size = new Vector2((lot.width - 2 * inset) * T, (lot.height - 2 * inset) * T);
            go.AddComponent<Building2D>().Configure(id, $"{number} {frontage.Abbrev}", type);

            // One coherent roof per building (replaces the old 4 m roof tiles).
            var roofGo = new GameObject("Roof");
            roofGo.transform.SetParent(go.transform, false);
            var roofPart = roofGo.AddComponent<Roof2D>();
            roofPart.Size = new Vector2((lot.width - 2 * inset) * T, (lot.height - 2 * inset) * T);
            roofPart.Seed = _counter * 7919 + lot.xMin * 31 + lot.yMin;
            roofPart.Tint = precinct ? Color.white : colour;
            roofPart.Kind = precinct ? Roof2D.Style.Police
                : roof == "tile_roof_brick_edge" ? Roof2D.Style.Brownstone
                : type == "office" && floors > 15 ? Roof2D.Style.Tower
                : roof == "tile_roof_tar" ? Roof2D.Style.Tar
                : roof == "tile_roof_gravel" ? Roof2D.Style.Gravel : Roof2D.Style.Light;

            db.Buildings.Add(new CityLocationDatabase.Building
            {
                Id = id, Name = name, Type = type, Number = number, Street = frontage.Abbrev, District = district.Name,
                Footprint = new Rect((lot.xMin + inset) * T, (lot.yMin + inset) * T, (lot.width - 2 * inset) * T, (lot.height - 2 * inset) * T),
                Entrance = entrance, Floors = floors,
            });
        }

        private static void BuildPark(RectInt block, Transform root, CityLocationDatabase db, System.Random rng)
        {
            OpenSpace space = OpenSpaceAt(block.xMin, block.yMin);
            string name = space != null ? space.Name : "Park";
            int trees = Mathf.Max(2, block.width * block.height / 6);
            int placed = 0;
            for (int attempt = 0; attempt < trees * 4 && placed < trees; attempt++)
            {
                Vector2 p = new Vector2((block.xMin + 0.4f + (float)rng.NextDouble() * (block.width - 0.8f)) * T,
                                        (block.yMin + 0.4f + (float)rng.NextDouble() * (block.height - 0.8f)) * T);
                if (TreeBlocked(p)) continue;
                PlaceTree(p, 0.8f + (float)rng.NextDouble() * 0.5f, rng, root);
                placed++;
            }
            (Road frontage, Vector2 entrance) = Frontage(block);
            DistrictInfo district = DistrictAt(block.xMin + block.width / 2, block.yMin + block.height / 2);
            db.Buildings.Add(new CityLocationDatabase.Building
            {
                Id = $"{district.Prefix}-{++_counter:0000}", Name = name, Type = "park",
                Number = frontage != null ? AddressNumber(frontage, block, entrance) : 0,
                Street = frontage != null ? frontage.Abbrev : "", District = district.Name, Entrance = entrance, Floors = 0,
                Footprint = new Rect(block.xMin * T, block.yMin * T, block.width * T, block.height * T),
            });
        }

        private static void BuildPlaza(RectInt block, Transform root, CityLocationDatabase db)
        {
            OpenSpace space = OpenSpaceAt(block.xMin, block.yMin);
            var rng = new System.Random(block.xMin * 1000 + block.yMin);
            // Rows of trees in planters around the edge of the square.
            for (int x = block.xMin; x < block.xMax; x += 2)
            {
                PlaceTree(new Vector2((x + 0.5f) * T, (block.yMin + 0.4f) * T), 0.7f, rng, root);
                PlaceTree(new Vector2((x + 0.5f) * T, (block.yMax - 0.4f) * T), 0.7f, rng, root);
            }
            (Road frontage, Vector2 entrance) = Frontage(block);
            DistrictInfo district = DistrictAt(block.xMin + block.width / 2, block.yMin + block.height / 2);
            db.Buildings.Add(new CityLocationDatabase.Building
            {
                Id = $"{district.Prefix}-{++_counter:0000}", Name = space != null ? space.Name : "Plaza", Type = "plaza",
                Number = frontage != null ? AddressNumber(frontage, block, entrance) : 0,
                Street = frontage != null ? frontage.Abbrev : "", District = district.Name, Entrance = entrance, Floors = 0,
                Footprint = new Rect(block.xMin * T, block.yMin * T, block.width * T, block.height * T),
            });
        }

        /// <summary>
        /// Upstate-style woodland on the mainland: dense outside the beltway, thinner between the beltway
        /// and the water. Trees keep a clear verge along every road so nothing grows into traffic.
        /// </summary>
        private static void PlantForest(Transform root)
        {
            var rng = new System.Random(87);
            int planted = 0;
            for (int y = MinY; y < MaxY; y += 1)
            for (int x = MinX; x < MaxX; x += 1)
            {
                if (_grid[x, y] != Cell.Forest || _cleared.Contains(new Vector2Int(x, y))) continue;   // estate lawns and tracks stay clear
                bool outside = OutsideHighway(x, y);
                if (rng.NextDouble() > (outside ? 0.26 : 0.09)) continue;
                bool verge = false;
                for (int dy = -2; dy <= 2 && !verge; dy++)
                for (int dx = -2; dx <= 2 && !verge; dx++)
                    if (Is(x + dx, y + dy, Cell.Road) || Is(x + dx, y + dy, Cell.Water) || Is(x + dx, y + dy, Cell.Sidewalk) || Is(x + dx, y + dy, Cell.Lot) || Is(x + dx, y + dy, Cell.Highway) || Is(x + dx, y + dy, Cell.Curve)) verge = true;
                if (verge) continue;
                Vector2 p = new Vector2((x + 0.15f + (float)rng.NextDouble() * 0.7f) * T, (y + 0.15f + (float)rng.NextDouble() * 0.7f) * T);
                var tree = PlaceTree(p, 1.0f + (float)rng.NextDouble() * 0.8f, rng, root);
                // Mixed hardwood and evergreen: darker, cooler greens for conifers.
                float g = 0.62f + (float)rng.NextDouble() * 0.3f;
                bool conifer = rng.NextDouble() < 0.45;
                tree.GetComponent<SpriteRenderer>().color = conifer ? new Color(g * 0.62f, g * 0.82f, g * 0.7f) : new Color(g * 0.9f, g, g * 0.78f);
                tree.isStatic = true;
                planted++;
            }
            Debug.Log($"[City] Forest: {planted} trees.");
        }

        // ------------------------------------------------------------------ estates: mansions and cabins in the county

        /// <summary>A buyable country property. Mansions sit on big lots along the connector roads, cabins in woodland clearings.</summary>
        private sealed class Estate
        {
            public bool Cabin;
            public RectInt Lot;          // cleared area (lawn), tiles
            public Rect Footprint;       // the house, metres
            public Vector2 Entrance;     // in front of the door, metres
            public Rect Pool;            // mansions: back-yard pool, metres (zero size = none)
            public string Street; public int Number;
        }

        private static readonly List<Estate> _estates = new List<Estate>();
        private static readonly HashSet<Vector2Int> _cleared = new HashSet<Vector2Int>();   // no trees here
        private static readonly HashSet<Vector2Int> _tracks = new HashSet<Vector2Int>();    // dirt driveways to cabins
        private const int MansionAlong = 12, MansionDepth = 11, MaxMansions = 10;
        private const int CabinW = 7, CabinH = 6, MaxCabins = 10;

        private static bool AllForest(RectInt r, int margin)
        {
            for (int y = r.yMin - margin; y < r.yMax + margin; y++)
            for (int x = r.xMin - margin; x < r.xMax + margin; x++)
                if (!Is(x, y, Cell.Forest) || _cleared.Contains(new Vector2Int(x, y))) return false;
            return true;
        }

        private static bool NearAny(int x, int y, int radius, params Cell[] kinds)
        {
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
                foreach (var k in kinds) if (Is(x + dx, y + dy, k)) return true;
            return false;
        }

        private static void Clear(RectInt r) { for (int y = r.yMin; y < r.yMax; y++) for (int x = r.xMin; x < r.xMax; x++) _cleared.Add(new Vector2Int(x, y)); }

        /// <summary>Plans estate lots (before trees are planted, so the forest grows around them). Deterministic.</summary>
        private static void PlanEstates()
        {
            _estates.Clear(); _cleared.Clear(); _tracks.Clear();

            // Mansions: both sides of every road that runs out through the woods (the four connectors).
            foreach (Road road in Avenues.Concat(Streets))
            {
                if (_estates.Count(e => !e.Cabin) >= MaxMansions) break;
                for (int side = -1; side <= 1; side += 2)
                for (int a = road.Min + 4; a < road.Max - MansionAlong - 4; a++)
                {
                    if (_estates.Count(e => !e.Cabin) >= MaxMansions) break;
                    const int gap = 2;   // verge + driveway between the kerb and the lot
                    RectInt lot = road.Vertical
                        ? new RectInt(side < 0 ? road.Start - gap - MansionDepth : road.Start + road.Width + gap, a, MansionDepth, MansionAlong)
                        : new RectInt(a, side < 0 ? road.Start - gap - MansionDepth : road.Start + road.Width + gap, MansionAlong, MansionDepth);
                    if (!AllForest(lot, 1) || OutsideHighway(lot.xMin, lot.yMin) || OutsideHighway(lot.xMax, lot.yMax)) continue;
                    if (NearAny((int)lot.center.x, (int)lot.center.y, Mathf.Max(MansionAlong, MansionDepth) / 2 + 3, Cell.Highway, Cell.Water, Cell.Lot, Cell.Sidewalk)) continue;

                    // House toward the road, back yard (with a pool) behind it.
                    Vector2 toRoad = road.Vertical ? new Vector2(-side, 0) : new Vector2(0, -side);
                    float depthHouse = 6f, alongHouse = 8f;
                    Vector2 lotMin = new Vector2(lot.xMin, lot.yMin) * T, lotSize = new Vector2(lot.width, lot.height) * T;
                    Rect house, pool;
                    if (road.Vertical)
                    {
                        float hx = side < 0 ? lot.xMax - 1.5f - depthHouse : lot.xMin + 1.5f;   // near the road edge
                        house = new Rect(hx * T, (lot.yMin + (MansionAlong - alongHouse) / 2f) * T, depthHouse * T, alongHouse * T);
                        float px = side < 0 ? lot.xMin + 0.8f : lot.xMax - 0.8f - 2f;
                        pool = new Rect(px * T, (lot.yMin + MansionAlong / 2f - 2f) * T, 2f * T, 4f * T);
                    }
                    else
                    {
                        float hy = side < 0 ? lot.yMax - 1.5f - depthHouse : lot.yMin + 1.5f;
                        house = new Rect((lot.xMin + (MansionAlong - alongHouse) / 2f) * T, hy * T, alongHouse * T, depthHouse * T);
                        float py = side < 0 ? lot.yMin + 0.8f : lot.yMax - 0.8f - 2f;
                        pool = new Rect((lot.xMin + MansionAlong / 2f - 2f) * T, py * T, 4f * T, 2f * T);
                    }
                    Vector2 door = house.center + Vector2.Scale(toRoad, house.size * 0.5f) + toRoad * (T * 0.5f);
                    var est = new Estate { Cabin = false, Lot = lot, Footprint = house, Pool = pool, Entrance = door, Street = road.Abbrev, Number = AddressNumber(road, lot, door) };
                    _estates.Add(est);
                    // Lawn plus the verge/driveway strip out to the kerb.
                    Clear(new RectInt(lot.xMin - 1, lot.yMin - 1, lot.width + 2, lot.height + 2));
                    if (road.Vertical) Clear(new RectInt(side < 0 ? lot.xMax : road.Start + road.Width, lot.yMin, gap, lot.height));
                    else Clear(new RectInt(lot.xMin, side < 0 ? lot.yMax : road.Start + road.Width, lot.width, gap));
                    a += MansionAlong + 6;
                }
            }

            // Cabins: clearings well away from traffic, joined to the nearest road by a dirt track.
            var rng = new System.Random(4242);
            var candidates = new List<Vector2Int>();
            for (int y = MinY + 4; y < MaxY - CabinH - 4; y += 3)
            for (int x = MinX + 4; x < MaxX - CabinW - 4; x += 3)
                candidates.Add(new Vector2Int(x, y));
            candidates = candidates.OrderBy(_ => rng.Next()).ToList();
            foreach (var c in candidates)
            {
                if (_estates.Count(e => e.Cabin) >= MaxCabins) break;
                var lot = new RectInt(c.x, c.y, CabinW, CabinH);
                if (!AllForest(lot, 2)) continue;
                Vector2Int mid = new Vector2Int(lot.xMin + CabinW / 2, lot.yMin + CabinH / 2);
                if (NearAny(mid.x, mid.y, 7, Cell.Road, Cell.Curve, Cell.Water, Cell.Lot, Cell.Sidewalk) || NearAny(mid.x, mid.y, 5, Cell.Highway)) continue;   // deep in the woods
                if (_estates.Any(e => Vector2.Distance(e.Lot.center, lot.center) < 34f)) continue;                                    // spread out

                // Nearest drivable cell (road or beltway) within reach, along a straight L-shaped track.
                Vector2Int? target = null; int best = int.MaxValue;
                for (int r = 3; r <= 30 && target == null; r++)
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    int x = mid.x + dx, y = mid.y + dy;
                    if (!(Is(x, y, Cell.Road) || Is(x, y, Cell.Highway))) continue;
                    int d = Mathf.Abs(dx) + Mathf.Abs(dy);
                    if (d < best) { best = d; target = new Vector2Int(x, y); }
                }
                if (target == null) continue;
                var path = TrackPath(mid, target.Value);
                if (path == null) continue;

                // House in the middle of the clearing, door facing the track.
                Vector2Int first = path.Count > 0 ? path[Mathf.Min(path.Count - 1, Mathf.Max(CabinW, CabinH) / 2 + 1)] : target.Value;
                Vector2 dir = ((Vector2)(first - mid));
                dir = Mathf.Abs(dir.x) > Mathf.Abs(dir.y) ? new Vector2(Mathf.Sign(dir.x), 0) : new Vector2(0, Mathf.Sign(dir.y));
                Rect house = new Rect((lot.xMin + 1.5f) * T, (lot.yMin + 1.5f) * T, (CabinW - 3f) * T, (CabinH - 3f) * T);
                Vector2 door = house.center + Vector2.Scale(dir, house.size * 0.5f) + dir * (T * 0.5f);
                bool beltway = Is(target.Value.x, target.Value.y, Cell.Highway);
                Road near = beltway ? null : AvenueAt(target.Value.x, target.Value.y) ?? StreetAt(target.Value.x, target.Value.y);
                _estates.Add(new Estate
                {
                    Cabin = true, Lot = lot, Footprint = house, Entrance = door,
                    Street = near != null ? near.Abbrev : "Beltway", Number = 2000 + _estates.Count(e => e.Cabin) * 14 + 1,
                });
                Clear(new RectInt(lot.xMin - 1, lot.yMin - 1, lot.width + 2, lot.height + 2));
                foreach (var p in path)
                {
                    if (!Is(p.x, p.y, Cell.Forest)) break;
                    if (!lot.Contains(p)) _tracks.Add(p);
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) _cleared.Add(new Vector2Int(p.x + dx, p.y + dy));   // verge
                }
            }
            Debug.Log($"[City] Estates: {_estates.Count(e => !e.Cabin)} mansions, {_estates.Count(e => e.Cabin)} cabins, {_tracks.Count} track tiles.");
        }

        /// <summary>L-shaped path of cells from <paramref name="from"/> to the first non-forest cell toward <paramref name="to"/>; null if it would cross water or a lot.</summary>
        private static List<Vector2Int> TrackPath(Vector2Int from, Vector2Int to)
        {
            foreach (bool xFirst in new[] { true, false })
            {
                var path = new List<Vector2Int>();
                var p = from; bool ok = true, arrived = false;
                for (int leg = 0; leg < 2 && ok && !arrived; leg++)
                {
                    bool alongX = (leg == 0) == xFirst;
                    while (ok && !arrived && (alongX ? p.x != to.x : p.y != to.y))
                    {
                        p += alongX ? new Vector2Int(System.Math.Sign(to.x - p.x), 0) : new Vector2Int(0, System.Math.Sign(to.y - p.y));
                        if (Is(p.x, p.y, Cell.Forest)) { path.Add(p); continue; }
                        if (Is(p.x, p.y, Cell.Road) || Is(p.x, p.y, Cell.Highway) || Is(p.x, p.y, Cell.Curve)) arrived = true;
                        else ok = false;   // water, lots, sidewalks: try the other way round
                    }
                }
                if (ok && (arrived || p == to)) return path;
            }
            return null;
        }

        private static void PaintDirt(Tilemap ground, int x, int y)
        {
            var cell = new Vector3Int(x, y, 0);
            ground.SetTile(cell, GetTile("tile_sidewalk"));   // pale paving tinted to packed earth
            ground.SetTileFlags(cell, TileFlags.None);
            float n = ((x * 73856093) ^ (y * 19349663)) & 7;
            float k = 0.92f + n * 0.012f;
            ground.SetColor(cell, new Color(0.8f * k, 0.66f * k, 0.48f * k));
        }

        private static void BuildEstates(Transform root, CityLocationDatabase db)
        {
            int mansion = 0, cabin = 0;
            var water = TopDown2DSceneBuilder.S("tile_water");
            foreach (var e in _estates)
            {
                string prefix = e.Cabin ? "NW" : "HH";
                string id = $"{prefix}-{(e.Cabin ? ++cabin : ++mansion):0000}";
                string address = $"{e.Number} {e.Street}";
                var go = new GameObject($"{id} {address}");
                go.transform.SetParent(root);
                go.transform.position = e.Footprint.center;
                go.isStatic = true;
                go.AddComponent<BoxCollider2D>().size = e.Footprint.size;
                go.AddComponent<Building2D>().Configure(id, address, "apartment");

                var roofGo = new GameObject("Roof");
                roofGo.transform.SetParent(go.transform, false);
                var roof = roofGo.AddComponent<Roof2D>();
                roof.Size = e.Footprint.size;
                roof.Seed = (e.Cabin ? 5000 : 7000) + (e.Cabin ? cabin : mansion) * 7919;
                roof.Kind = e.Cabin ? Roof2D.Style.Cabin : Roof2D.Style.Mansion;   // pitched roofs, slate/terracotta or cedar
                roof.Tint = Color.white;

                if (!e.Cabin && e.Pool.width > 0f && water != null)
                {
                    // Back-yard pool: a water tile stretched over a pale stone surround.
                    var deck = new GameObject("Pool deck");
                    deck.transform.SetParent(go.transform, false);
                    deck.transform.position = e.Pool.center;
                    var ds = deck.AddComponent<SpriteRenderer>();
                    ds.sprite = TopDown2DSceneBuilder.S("tile_sidewalk");
                    ds.drawMode = SpriteDrawMode.Sliced; ds.size = e.Pool.size + new Vector2(1.6f, 1.6f);
                    Sorting2D.Apply(ds, Sorting2D.Ground, 20);
                    var pool = new GameObject("Pool");
                    pool.transform.SetParent(deck.transform, false);
                    var ps = pool.AddComponent<SpriteRenderer>();
                    ps.sprite = water; ps.drawMode = SpriteDrawMode.Sliced; ps.size = e.Pool.size;
                    ps.color = new Color(0.55f, 0.9f, 1f);
                    Sorting2D.Apply(ps, Sorting2D.Ground, 21);
                    deck.isStatic = pool.isStatic = true;
                }

                db.Buildings.Add(new CityLocationDatabase.Building
                {
                    Id = id, Name = e.Cabin ? "Cabin" : "Mansion", Type = "apartment", Number = e.Number, Street = e.Street,
                    District = e.Cabin ? "North Woods" : "Halden Hills",
                    Footprint = e.Footprint, Entrance = e.Entrance, Floors = e.Cabin ? 1 : 2,
                });
            }
        }

        private static GameObject PlaceTree(Vector2 p, float scale, System.Random rng, Transform root)
        {
            var tree = TopDown2DSceneBuilder.Place("prop_tree", p, rng.Next(360), 45, root);
            tree.transform.localScale = Vector3.one * scale;
            Sorting2D.Apply(tree.GetComponent<SpriteRenderer>(), Sorting2D.Roofs, 1);
            tree.AddComponent<CircleCollider2D>().radius = 0.3f;
            return tree;
        }

        /// <summary>The road a lot faces (nearest road edge) and an entrance point on the sidewalk in front of it.</summary>
        private static (Road, Vector2) Frontage(RectInt lot)
        {
            Road best = null; float bestD = float.MaxValue; Vector2 entrance = default;
            Vector2 c = new Vector2(lot.xMin + lot.width * 0.5f, lot.yMin + lot.height * 0.5f);
            foreach (Road s in Streets)
            {
                if (!s.Spans(Mathf.FloorToInt(c.x))) continue;
                float edge = s.Start < lot.yMin ? s.Start + s.Width : s.Start;
                float d = Mathf.Abs(edge - (s.Start < lot.yMin ? lot.yMin : lot.yMax));
                if (d < bestD) { bestD = d; best = s; entrance = new Vector2(c.x, s.Start < lot.yMin ? lot.yMin - 0.5f : lot.yMax + 0.5f) * T; }
            }
            foreach (Road a in Avenues)
            {
                if (!a.Spans(Mathf.FloorToInt(c.y))) continue;
                float edge = a.Start < lot.xMin ? a.Start + a.Width : a.Start;
                float d = Mathf.Abs(edge - (a.Start < lot.xMin ? lot.xMin : lot.xMax)) + 0.5f; // prefer streets on ties
                if (d < bestD) { bestD = d; best = a; entrance = new Vector2(a.Start < lot.xMin ? lot.xMin - 0.5f : lot.xMax + 0.5f, c.y) * T; }
            }
            return (best, entrance);
        }

        /// <summary>House numbers rise northward on avenues and eastward on streets; odd on the west/south side.</summary>
        private static int AddressNumber(Road road, RectInt lot, Vector2 entrance)
        {
            float along = road.Vertical ? lot.yMin + lot.height * 0.5f : lot.xMin + lot.width * 0.5f;
            int baseNumber = 100 + Mathf.RoundToInt(Mathf.Abs(along)) * 4;   // west of the island (negative x) still gets positive numbers
            bool lowSide = road.Vertical ? entrance.x / T < road.Start : entrance.y / T < road.Start;
            return (baseNumber & ~1) + (lowSide ? 1 : 0);
        }

        // ------------------------------------------------------------------ Halden Park: paths and pond

        private static readonly RectInt HaldenPark = new RectInt(29, 107, 29, 32);

        /// <summary>Curving footpaths (Catmull-Rom through hand-placed points) and an irregular pond.</summary>
        private static void PlanParkFeatures()
        {
            _paths.Clear();
            Vector2 W(float tx, float ty) => new Vector2(tx * T, ty * T);

            // South meadow: a winding path from the Hudson Ave gate across to Grand Blvd, and one up
            // from the Park South gate to the transverse.
            _paths.Add(Spline(new[] { W(29.5f, 110f), W(33f, 113.5f), W(38f, 111.5f), W(44f, 115f), W(50f, 112f), W(57.5f, 116f) }));
            _paths.Add(Spline(new[] { W(43.5f, 107.5f), W(41f, 110f), W(43f, 113.5f), W(46.5f, 117f), W(44f, 120f) }));
            // North: a loop around the pond plus spurs to the gates.
            Vector2 pondCentre = W(43.5f, 132f);
            var loop = new List<Vector2>();
            for (int i = 0; i <= 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                loop.Add(pondCentre + new Vector2(Mathf.Cos(a) * 10.4f * T, Mathf.Sin(a) * 4.2f * T));   // ~2 m clear of the pond edge
            }
            _paths.Add(Spline(loop.ToArray()));
            _paths.Add(Spline(new[] { W(29.5f, 129f), W(32f, 130.5f), W(34.5f, 131f) }));
            _paths.Add(Spline(new[] { W(57.5f, 135f), W(54.5f, 133.5f), W(52.5f, 132.5f) }));
            _paths.Add(Spline(new[] { W(43.5f, 126.5f), W(42f, 127.5f), W(43f, 128.5f) }));
            _paths.Add(Spline(new[] { W(43.5f, 138.5f), W(45f, 137.2f), W(44f, 136f) }));

            // Pond: a lumpy ellipse (fixed seed).
            var rng = new System.Random(77);
            const int n = 32;
            _pond = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                float wobble = 1f + ((float)rng.NextDouble() - 0.5f) * 0.18f + Mathf.Sin(a * 3f) * 0.06f;
                _pond[i] = pondCentre + new Vector2(Mathf.Cos(a) * 8f * T, Mathf.Sin(a) * 3.2f * T) * wobble;
            }
        }

        private static Vector2[] Spline(Vector2[] pts)
        {
            var o = new List<Vector2>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector2 p0 = pts[Mathf.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Mathf.Min(pts.Length - 1, i + 2)];
                for (int s = 0; s < 8; s++)
                {
                    float t = s / 8f, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            o.Add(pts[pts.Length - 1]);
            return o.ToArray();
        }

        private static bool InPond(Vector2 p, float margin = 0f)
        {
            if (_pond == null) return false;
            if (margin > 0f)
                for (int i = 0; i < _pond.Length; i++)
                    if (DistanceToSegment(p, _pond[i], _pond[(i + 1) % _pond.Length]) < margin) return true;
            bool inside = false;
            for (int i = 0, j = _pond.Length - 1; i < _pond.Length; j = i++)
                if ((_pond[i].y > p.y) != (_pond[j].y > p.y) &&
                    p.x < (_pond[j].x - _pond[i].x) * (p.y - _pond[i].y) / (_pond[j].y - _pond[i].y) + _pond[i].x)
                    inside = !inside;
            return inside;
        }

        private static bool NearPath(Vector2 p, float distance)
        {
            foreach (Vector2[] path in _paths)
                for (int i = 0; i + 1 < path.Length; i++)
                    if (DistanceToSegment(p, path[i], path[i + 1]) < distance) return true;
            return false;
        }

        private static bool TreeBlocked(Vector2 p) => InPond(p, 2.5f) || NearPath(p, 2.6f);

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        private static void BuildParkFeatures(Transform root)
        {
            System.IO.Directory.CreateDirectory(MaterialFolder);
            Material pathMat = SolidMaterial("ParkPath", new Color(0.78f, 0.72f, 0.58f));
            Material pondMat = SolidMaterial("ParkPond", new Color(0.22f, 0.42f, 0.52f));
            Material rimMat = SolidMaterial("ParkPondRim", new Color(0.55f, 0.52f, 0.42f));

            var paths = new GameObject("Park Paths").transform;
            paths.SetParent(root);
            for (int i = 0; i < _paths.Count; i++)
            {
                var go = new GameObject($"Path {i + 1}");
                go.transform.SetParent(paths);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = _paths[i].Length;
                lr.SetPositions(_paths[i].Select(v => (Vector3)v).ToArray());
                lr.loop = false;
                lr.widthMultiplier = 2.6f;
                lr.numCornerVertices = 4;
                lr.numCapVertices = 4;
                lr.sharedMaterial = pathMat;
                lr.startColor = lr.endColor = Color.white;
                Sorting2D.Apply(lr, Sorting2D.Ground, 200);
            }

            // Pond: a filled mesh (saved as an asset so the scene keeps it) with a stone rim, and a
            // collider so nobody walks on the water.
            Vector2 c = Vector2.zero;
            foreach (var v in _pond) c += v;
            c /= _pond.Length;
            Mesh mesh = FanMesh(_pond, c, 1f);
            Mesh rim = FanMesh(_pond, c, 1.06f);
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(PondMeshPath);
            if (existing != null) AssetDatabase.DeleteAsset(PondMeshPath);
            AssetDatabase.CreateAsset(mesh, PondMeshPath);
            AssetDatabase.AddObjectToAsset(rim, PondMeshPath);

            var rimGo = new GameObject("Pond Rim");
            rimGo.transform.SetParent(root);
            rimGo.AddComponent<MeshFilter>().sharedMesh = rim;
            var rimRenderer = rimGo.AddComponent<MeshRenderer>();
            rimRenderer.sharedMaterial = rimMat;
            Sorting2D.Apply(rimRenderer, Sorting2D.Ground, 250);

            var pondGo = new GameObject("Pond");
            pondGo.transform.SetParent(root);
            pondGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var pondRenderer = pondGo.AddComponent<MeshRenderer>();
            pondRenderer.sharedMaterial = pondMat;
            Sorting2D.Apply(pondRenderer, Sorting2D.Ground, 260);
            pondGo.AddComponent<PolygonCollider2D>().points = _pond;
        }

        private static Mesh FanMesh(Vector2[] outline, Vector2 centre, float scale)
        {
            var verts = new Vector3[outline.Length + 1];
            verts[0] = centre;
            for (int i = 0; i < outline.Length; i++) verts[i + 1] = centre + (outline[i] - centre) * scale;
            var tris = new int[outline.Length * 3];
            for (int i = 0; i < outline.Length; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + (i + 1) % outline.Length;
                tris[i * 3 + 2] = 1 + i;
            }
            var colours = new Color[verts.Length];
            for (int i = 0; i < colours.Length; i++) colours[i] = Color.white;   // Sprites/Default multiplies by vertex colour
            var mesh = new Mesh { name = scale > 1f ? "Pond Rim" : "Pond", vertices = verts, triangles = tris, colors = colours };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material SolidMaterial(string name, Color colour)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = colour;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ props, bounds, records

        private static void PlaceStreetProps(Transform props, System.Random rng)
        {
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
            {
                if (_grid[x, y] != Cell.Sidewalk) continue;
                bool curb = Is(x - 1, y, Cell.Road) || Is(x + 1, y, Cell.Road) || Is(x, y - 1, Cell.Road) || Is(x, y + 1, Cell.Road);
                Vector2 p = new Vector2(x * T + T * 0.5f, y * T + T * 0.5f);
                Vector2 toRoad = Is(x - 1, y, Cell.Road) ? Vector2.left : Is(x + 1, y, Cell.Road) ? Vector2.right : Is(x, y + 1, Cell.Road) ? Vector2.up : Vector2.down;
                bool corner = (Is(x - 1, y, Cell.Road) || Is(x + 1, y, Cell.Road)) && (Is(x, y - 1, Cell.Road) || Is(x, y + 1, Cell.Road));
                if (curb && (x + y) % 5 == 0)
                {
                    var light = TopDown2DSceneBuilder.Place("prop_streetlight", p + toRoad * 1.4f, 0f, 5, props);
                    light.AddComponent<CircleCollider2D>().radius = 0.15f;
                }
                else if (curb && !corner && (x + y) % 5 == 2 && DistrictAt(x, y).Prefix != "HP" && rng.NextDouble() < 0.85)
                {
                    // Street trees: none downtown, plenty in Midtown, Parkside and Linden Heights.
                    PlaceTree(p + toRoad * 1.2f, 0.65f + (float)rng.NextDouble() * 0.2f, rng, props);
                }
                else if (rng.NextDouble() < 0.05)
                {
                    string prop = rng.NextDouble() < 0.5 ? "prop_hydrant" : "prop_trash_bags";
                    var go = TopDown2DSceneBuilder.Place(prop, p + new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f), rng.Next(360), 5, props);
                    if (prop == "prop_hydrant") go.AddComponent<CircleCollider2D>().radius = 0.2f;
                }
            }
        }

        private static void AddBoundary()
        {
            // Shoreline: a solid strip on every water cell that touches land or a bridge (so the bridge
            // decks have rails and the open ends of roads stop at the water). One composite collider.
            var go = new GameObject("Shoreline");
            var grid = go.AddComponent<Grid>();
            grid.cellSize = new Vector3(T, T, 0f);
            var mapGo = new GameObject("Water Edge");
            mapGo.transform.SetParent(go.transform, false);
            var map = mapGo.AddComponent<Tilemap>();
            var rb = mapGo.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Static;
            var tc = mapGo.AddComponent<TilemapCollider2D>();
            tc.compositeOperation = Collider2D.CompositeOperation.Merge;
            mapGo.AddComponent<CompositeCollider2D>().geometryType = CompositeCollider2D.GeometryType.Polygons;
            TileBase block = BlockTile();
            for (int y = MinY - 1; y <= MaxY; y++)
            for (int x = MinX - 1; x <= MaxX; x++)
            {
                // The edge of the world: a solid ring just outside the map, deep in the forest.
                if (!InMap(x, y)) { map.SetTile(new Vector3Int(x, y, 0), block); continue; }
                bool water = _grid[x, y] == Cell.Water;
                if (!water) continue;
                bool touches = false;
                for (int dy = -1; dy <= 1 && !touches; dy++)
                for (int dx = -1; dx <= 1 && !touches; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (InMap(nx, ny) && _grid[nx, ny] != Cell.Water) touches = true;
                }
                if (touches) map.SetTile(new Vector3Int(x, y, 0), block);
            }

        }

        /// <summary>Invisible tile with a full-cell collider, for the shoreline.</summary>
        private static TileBase BlockTile()
        {
            System.IO.Directory.CreateDirectory(TileFolder);
            string path = $"{TileFolder}/tile_shore_block.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null) { tile = ScriptableObject.CreateInstance<Tile>(); AssetDatabase.CreateAsset(tile, path); }
            tile.sprite = TopDown2DSceneBuilder.S("tile_water");
            tile.color = new Color(1f, 1f, 1f, 0f);
            tile.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.Grid;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        private static void Barrier(Transform parent, Vector2 pos, float angle, float width)
        {
            var b = new GameObject("Bridge Closed");
            b.transform.SetParent(parent, false);
            b.transform.position = pos;
            b.transform.rotation = Quaternion.Euler(0, 0, angle);
            b.AddComponent<RoadBarrier2D>().Width = width;
        }

        private static CityLocationDatabase CreateDatabase()
        {
            System.IO.Directory.CreateDirectory("Assets/GPA/Data/City");
            var db = AssetDatabase.LoadAssetAtPath<CityLocationDatabase>(DatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<CityLocationDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
            }
            db.CityName = "New Halden";
            db.Districts.Clear(); db.Streets.Clear(); db.Intersections.Clear(); db.Buildings.Clear();
            foreach (DistrictInfo d in Districts)
            {
                if (d.Prefix == "HC") continue;   // the county is everything else; added last below
                RectInt land = d.Prefix == "EP" ? PortLand : d.Prefix == "NG" ? NorthLand : d.Prefix == "WB" ? WestLand : Land;
                int y0 = Mathf.Max(d.YMin, land.yMin), y1 = Mathf.Min(d.YMax, land.yMax);
                if (y1 <= y0) continue;
                db.Districts.Add(new CityLocationDatabase.District
                {
                    Name = d.Name, Borough = Borough,
                    Bounds = new Rect(land.xMin * T, y0 * T, land.width * T, (y1 - y0) * T),
                });
            }
            db.Districts.Add(new CityLocationDatabase.District
            {
                Name = "Halden County", Borough = "Halden County",
                Bounds = new Rect(MinX * T, MinY * T, MapW * T, MapH * T),
            });
            db.Land.Clear();
            // Mainland: everything outside the harbour basin.
            db.Land.Add(new Rect(MinX * T, MinY * T, (Basin.xMin - MinX) * T, MapH * T));
            db.Land.Add(new Rect(Basin.xMax * T, MinY * T, (MaxX - Basin.xMax) * T, MapH * T));
            db.Land.Add(new Rect(Basin.xMin * T, MinY * T, Basin.width * T, (Basin.yMin - MinY) * T));
            db.Land.Add(new Rect(Basin.xMin * T, Basin.yMax * T, Basin.width * T, (MaxY - Basin.yMax) * T));
            foreach (RectInt b in new[] { WestBridge, PortBridgeDeck, UplandBridgeDeck })
                db.Land.Add(new Rect(b.xMin * T, b.yMin * T, b.width * T, b.height * T));
            foreach (RectInt l in Lands) db.Land.Add(new Rect(l.xMin * T, l.yMin * T, l.width * T, l.height * T));
            db.Land.Add(new Rect(EastBridge.xMin * T, EastBridge.yMin * T, EastBridge.width * T, EastBridge.height * T));
            db.Land.Add(new Rect(NorthBridge.xMin * T, NorthBridge.yMin * T, NorthBridge.width * T, NorthBridge.height * T));
            return db;
        }

        private static void FillStreetsAndIntersections(CityLocationDatabase db)
        {
            foreach (Road r in Avenues)
                db.Streets.Add(new CityLocationDatabase.Street
                {
                    Name = r.Abbrev, Abbrev = r.Abbrev, Vertical = true, Direction = r.Direction, District = DistrictAt(r.Centre, (r.Min + r.Max) / 2).Name,
                    Bounds = new Rect(r.Start * T, r.Min * T, r.Width * T, (r.Max - r.Min) * T),
                });
            foreach (Road r in Streets)
                db.Streets.Add(new CityLocationDatabase.Street
                {
                    Name = r.Abbrev, Abbrev = r.Abbrev, Vertical = false, Direction = r.Direction, District = DistrictAt((r.Min + r.Max) / 2, r.Start).Name,
                    Bounds = new Rect(r.Min * T, r.Start * T, (r.Max - r.Min) * T, r.Width * T),
                });
            foreach (Road a in Avenues)
            foreach (Road s in Streets)
                if (Crosses(a, s))
                    db.Intersections.Add(new CityLocationDatabase.Intersection
                    {
                        Name = $"{a.Abbrev} & {s.Abbrev}", StreetA = a.Abbrev, StreetB = s.Abbrev,
                        Position = new Vector2((a.Start + a.Width * 0.5f) * T, (s.Start + s.Width * 0.5f) * T),
                    });
        }

        // ------------------------------------------------------------------ spawn points (lanes drive on the right)

        private static void BuildSpawnPoints()
        {
            var road = new List<SpawnPoints2D.Point>();
            var walk = new List<SpawnPoints2D.Point>();
            foreach (Road a in Avenues)
                for (int y = a.Min; y < a.Max; y++)
                {
                    if (StreetAt(a.Start, y) != null || StreetAt(a.Start, y - 1) != null || StreetAt(a.Start, y + 1) != null) continue;
                    if (!HasJunctionBothSides(a, y)) continue;
                    foreach (Lane lane in LanesOf(a))
                        road.Add(new SpawnPoints2D.Point { Position = new Vector2(lane.Across, y * T + T * 0.5f), Heading = lane.Heading });
                }
            foreach (Road s in Streets)
                for (int x = s.Min; x < s.Max; x++)
                {
                    if (AvenueAt(x, s.Start) != null || AvenueAt(x - 1, s.Start) != null || AvenueAt(x + 1, s.Start) != null) continue;
                    if (!HasJunctionBothSides(s, x)) continue;
                    foreach (Lane lane in LanesOf(s))
                        road.Add(new SpawnPoints2D.Point { Position = new Vector2(x * T + T * 0.5f, lane.Across), Heading = lane.Heading });
                }
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
                if (_grid[x, y] == Cell.Sidewalk && (x + y) % 2 == 0)
                    walk.Add(new SpawnPoints2D.Point { Position = new Vector2(x * T + T * 0.5f, y * T + T * 0.5f) });

            var sp = new GameObject("Spawn Points").AddComponent<SpawnPoints2D>();
            sp.Road = road.ToArray();
            sp.Sidewalk = walk.ToArray();
        }

        /// <summary>True when <paramref name="along"/> lies between two junctions of the road (i.e. on a lane segment traffic uses).</summary>
        private static bool HasJunctionBothSides(Road r, int along)
        {
            bool below = false, above = false;
            foreach (Road c in r.Vertical ? Streets : Avenues)
            {
                if (!(r.Vertical ? Crosses(r, c) : Crosses(c, r))) continue;
                if (c.Start + c.Width <= along) below = true;
                if (c.Start > along) above = true;
            }
            return below && above;
        }

        // ------------------------------------------------------------------ traffic graph, signals, walk map

        private struct Lane { public Road Road; public float Across; public float Heading; }

        private static List<Lane> LanesOf(Road r)
        {
            var lanes = new List<Lane>();
            for (int i = 0; i < r.Width; i++)
            {
                bool twoWay = r.Direction == "two-way";
                if (twoWay && i == r.Width / 2) continue;
                float heading;
                if (r.Vertical)
                    heading = r.Direction == "north" || (twoWay && i > r.Width / 2) ? 0f : 180f;
                else
                    heading = r.Direction == "east" || (twoWay && i < r.Width / 2) ? -90f : 90f;
                lanes.Add(new Lane { Road = r, Across = (r.Start + i) * T + T * 0.5f, Heading = heading });
            }
            return lanes;
        }

        /// <summary>Lane position counted from the right-hand kerb (0 = rightmost) among lanes going the same way.</summary>
        private static (int rank, int count) LaneRank(Road road, Lane lane)
        {
            int rank = 0, count = 0;
            float h = lane.Heading * Mathf.Deg2Rad;
            Vector2 right = new Vector2(Mathf.Cos(h), Mathf.Sin(h));
            foreach (Lane other in LanesOf(road))
            {
                if (Mathf.Abs(Mathf.DeltaAngle(other.Heading, lane.Heading)) > 1f) continue;
                count++;
                float d = (other.Across - lane.Across) * (road.Vertical ? right.x : right.y);
                if (d > 0.01f) rank++;
            }
            return (rank, count);
        }

        private static string BuildRoadGraphAndSignals(Transform props)
        {
            var (nodes, edges, junctions, junctionOf, fallbacks, deadEnds) = ComputeRoadGraph();
            var graph = new GameObject("Road Graph").AddComponent<RoadGraph2D>();
            graph.Nodes = nodes.ToArray();
            graph.Edges = edges.ToArray();
            graph.Junctions = junctions.ToArray();
            return AddSignals(props, nodes, edges, junctions, junctionOf, fallbacks, deadEnds);
        }

        /// <summary>The whole traffic lane graph: grid lanes, junction turns, connector roads, the beltway and its ramps.</summary>
        private static (List<RoadGraph2D.Node> nodes, List<RoadGraph2D.Edge> edges, List<RoadGraph2D.Junction> junctions, Dictionary<(Road avenue, Road street), int> junctionOf, int fallbacks, int deadEnds) ComputeRoadGraph()
        {
            var laneRank = new Dictionary<int, (int rank, int count)>();
            var nodes = new List<RoadGraph2D.Node>();
            var edges = new List<RoadGraph2D.Edge>();
            var junctions = new List<RoadGraph2D.Junction>();
            var junctionOf = new Dictionary<(Road avenue, Road street), int>();
            foreach (Road a in Avenues)
            foreach (Road s in Streets)
            {
                if (!Crosses(a, s)) continue;
                junctionOf[(a, s)] = junctions.Count;
                junctions.Add(new RoadGraph2D.Junction
                {
                    Name = $"{a.Abbrev} & {s.Abbrev}",
                    Centre = new Vector2((a.Start + a.Width * 0.5f) * T, (s.Start + s.Width * 0.5f) * T),
                });
            }

            // Lane segments between consecutive junctions along each road. Stop lines sit before the crosswalk row.
            void AddSegments(Road[] roads, Road[] crossingAll, bool vertical)
            {
                foreach (Road road in roads)
                {
                    List<Road> crossing = crossingAll.Where(c => vertical ? Crosses(road, c) : Crosses(c, road)).OrderBy(c => c.Start).ToList();
                    foreach (Lane lane in LanesOf(road))
                    for (int k = 0; k + 1 < crossing.Count; k++)
                    {
                        Road low = crossing[k], high = crossing[k + 1];
                        float lowEdge = (low.Start + low.Width + 1) * T + 0.5f;    // just past the far crosswalk
                        float highEdge = (high.Start - 1) * T - 0.5f;               // stop line before the crosswalk
                        bool positive = vertical ? lane.Heading == 0f : lane.Heading == -90f;
                        float from = positive ? lowEdge : highEdge, to = positive ? highEdge : lowEdge;
                        Road jFromRoad = positive ? low : high, jToRoad = positive ? high : low;
                        int jFrom = vertical ? junctionOf[(road, jFromRoad)] : junctionOf[(jFromRoad, road)];
                        int jTo = vertical ? junctionOf[(road, jToRoad)] : junctionOf[(jToRoad, road)];
                        Vector2 P(float along) => vertical ? new Vector2(lane.Across, along) : new Vector2(along, lane.Across);
                        int entry = nodes.Count;
                        laneRank[entry] = laneRank[entry + 1] = LaneRank(road, lane);
                        nodes.Add(new RoadGraph2D.Node { Position = P(from), Heading = lane.Heading, Vertical = vertical, IsExit = false, Junction = jFrom });
                        nodes.Add(new RoadGraph2D.Node { Position = P(to), Heading = lane.Heading, Vertical = vertical, IsExit = true, Junction = jTo });
                        edges.Add(new RoadGraph2D.Edge { From = entry, To = entry + 1, ThroughJunction = false, Control = (P(from) + P(to)) * 0.5f });
                    }
                }
            }
            AddSegments(Avenues, Streets, true);
            AddSegments(Streets, Avenues, false);
            var ringLinks = AddRingConnectors(nodes, edges, junctionOf, laneRank);

            // Junction crossings: every exit to every entry leaving the same junction, except U-turns.
            // Lane discipline first (rights from the kerb lane, lefts from the inside lane, straight stays
            // in lane); if that leaves an exit with nowhere to go (T-junctions), any legal turn is allowed,
            // so no car can ever get stuck at a stop line.
            int fallbacks = 0, deadEnds = 0;
            for (int x = 0; x < nodes.Count; x++)
            {
                if (!nodes[x].IsExit || nodes[x].Junction < 0) continue;
                int added = 0;
                for (int pass = 0; pass < 2 && added == 0; pass++)
                {
                    bool strict = pass == 0;
                    for (int e = 0; e < nodes.Count; e++)
                    {
                        if (nodes[e].IsExit || nodes[e].Junction != nodes[x].Junction) continue;
                        float turn = Mathf.Abs(Mathf.DeltaAngle(nodes[x].Heading, nodes[e].Heading));
                        if (turn > 150f) continue;
                        Vector2 a = nodes[x].Position, b = nodes[e].Position;
                        if (strict)
                        {
                            float signed = Mathf.DeltaAngle(nodes[x].Heading, nodes[e].Heading);
                            var rx = laneRank[x]; var re = laneRank[e];
                            if (signed < -30f && (rx.rank != 0 || re.rank != 0)) continue;
                            if (signed > 30f && (rx.rank != rx.count - 1 || re.rank != re.count - 1)) continue;
                            if (turn < 10f && Mathf.Abs(nodes[x].Vertical ? a.x - b.x : a.y - b.y) > 0.5f) continue; // stay in lane going straight
                        }
                        else if (turn < 10f) continue;   // fallback only adds turns
                        Vector2 control = turn < 10f ? (a + b) * 0.5f : nodes[x].Vertical ? new Vector2(a.x, b.y) : new Vector2(b.x, a.y);
                        edges.Add(new RoadGraph2D.Edge { From = x, To = e, ThroughJunction = true, Control = control });
                        added++;
                    }
                    if (!strict && added > 0) fallbacks++;
                }
                if (added == 0) deadEnds++;
            }

            int ringNodes = AddRingTraffic(nodes, edges, ringLinks);
            int curveLinks = AddLocalCurveTraffic(nodes, edges);
            Debug.Log($"[City] Curved roads: beltway {ringNodes} lane nodes, local curves joined at {curveLinks} lane splits.");
            return (nodes, edges, junctions, junctionOf, fallbacks, deadEnds);
        }

        private static string AddSignals(Transform props, List<RoadGraph2D.Node> nodes, List<RoadGraph2D.Edge> edges, List<RoadGraph2D.Junction> junctions,
            Dictionary<(Road avenue, Road street), int> junctionOf, int fallbacks, int deadEnds)
        {
            // Signal heads on two opposite corners per axis.
            var signals = new GameObject("Traffic Signals").AddComponent<TrafficSignals2D>();
            var heads = new List<TrafficSignals2D.Head>();
            foreach (var kv in junctionOf)
            {
                Road a = kv.Key.avenue, st = kv.Key.street;
                float x0 = a.Start * T - 1.2f, x1 = (a.Start + a.Width) * T + 1.2f;
                float y0 = st.Start * T - 1.2f, y1 = (st.Start + st.Width) * T + 1.2f;
                var corners = new (Vector2 p, bool vertical)[] { (new Vector2(x1, y0), true), (new Vector2(x0, y1), true), (new Vector2(x0, y0), false), (new Vector2(x1, y1), false) };
                foreach (var (p, vertical) in corners)
                {
                    var head = TopDown2DSceneBuilder.Place("prop_signal_head", p, 0f, 5, props);
                    Sorting2D.Apply(head.GetComponent<SpriteRenderer>(), Sorting2D.Roofs, 5);
                    var lamp = TopDown2DSceneBuilder.Place("signal_lamp", Vector2.zero, 0f, 45, head.transform, local: true);
                    lamp.transform.localScale = Vector3.one * 0.9f;
                    heads.Add(new TrafficSignals2D.Head { Junction = kv.Value, Vertical = vertical, Lamp = lamp.GetComponent<SpriteRenderer>() });
                }
            }
            signals.Heads = heads.ToArray();
            string report = $"Road graph: {nodes.Count} nodes, {edges.Count} edges, {junctions.Count} junctions; {fallbacks} exits needed the T-junction fallback, {deadEnds} dead ends.";
            Debug.Log("[City] " + report);
            return report;
        }

        // ------------------------------------------------------------------ curved roads: traffic

        private sealed class RingLink { public Vector2 Meet, Inward; public readonly List<int> OutExits = new List<int>(), InEntries = new List<int>(); }

        /// <summary>
        /// Lane segments from each connector road's last junction out to the beltway (the grid graph
        /// stops at the last junction). Outbound lanes end at an unsignalled exit just short of the ring;
        /// inbound lanes start there and end at the junction's stop line.
        /// </summary>
        private static List<RingLink> AddRingConnectors(List<RoadGraph2D.Node> nodes, List<RoadGraph2D.Edge> edges,
            Dictionary<(Road avenue, Road street), int> junctionOf, Dictionary<int, (int rank, int count)> laneRank)
        {
            var links = new List<RingLink>();
            void Link(string roadName, bool ringAtHigh, float ringAlong)
            {
                Road road = Streets.Concat(Avenues).First(r => r.Name == roadName);
                Road[] crossingAll = road.Vertical ? Streets : Avenues;
                var crossing = crossingAll.Where(c => road.Vertical ? Crosses(road, c) : Crosses(c, road)).OrderBy(c => c.Start).ToList();
                if (crossing.Count == 0) return;
                Road j = ringAtHigh ? crossing[crossing.Count - 1] : crossing[0];
                int jIdx = road.Vertical ? junctionOf[(road, j)] : junctionOf[(j, road)];
                // Connector lanes start where the beltway ramps join the road (the flat crossing for the far side starts there too).
                float ringEdge = ringAlong * T + (ringAtHigh ? -RampJoin : RampJoin);
                float jEdge = ringAtHigh ? (j.Start + j.Width + 1) * T + 0.5f : (j.Start - 1) * T - 0.5f;
                if (ringAtHigh ? ringEdge <= jEdge + 4f : ringEdge >= jEdge - 4f)
                    Debug.LogError($"[City] {roadName}: too short between its last junction and the beltway for the ramps ({Mathf.Abs(ringEdge - jEdge):0} m).");
                float centreAcross = (road.Start + road.Width * 0.5f) * T;
                var link = new RingLink
                {
                    Meet = road.Vertical ? new Vector2(centreAcross, ringAlong * T) : new Vector2(ringAlong * T, centreAcross),
                    Inward = (road.Vertical ? Vector2.up : Vector2.right) * (ringAtHigh ? -1f : 1f),
                };
                foreach (Lane lane in LanesOf(road))
                {
                    if (Mathf.Abs(Mathf.Abs(lane.Across - centreAcross) - StemLane) > 0.1f) continue;   // narrowed: one lane each way
                    bool positive = road.Vertical ? lane.Heading == 0f : lane.Heading == -90f;
                    bool outbound = positive == ringAtHigh;
                    float from = outbound ? jEdge : ringEdge, to = outbound ? ringEdge : jEdge;
                    Vector2 P(float along) => road.Vertical ? new Vector2(lane.Across, along) : new Vector2(along, lane.Across);
                    int a = nodes.Count;
                    laneRank[a] = laneRank[a + 1] = (0, 1);
                    nodes.Add(new RoadGraph2D.Node { Position = P(from), Heading = lane.Heading, Vertical = road.Vertical, IsExit = false, Junction = outbound ? jIdx : -1 });
                    nodes.Add(new RoadGraph2D.Node { Position = P(to), Heading = lane.Heading, Vertical = road.Vertical, IsExit = true, Junction = outbound ? -1 : jIdx });
                    edges.Add(new RoadGraph2D.Edge { From = a, To = a + 1, ThroughJunction = false, Control = (P(from) + P(to)) * 0.5f });
                    if (outbound) link.OutExits.Add(a + 1); else link.InEntries.Add(a);
                }
                links.Add(link);
            }
            Link("Garnet Street", false, RingGarnetX);
            Link("Lakeview Avenue", false, RingLakeviewY);
            Link("Crosstown Boulevard", true, RingCrosstownX);
            Link("Grand Boulevard", true, RingGrandY);
            return links;
        }

        private static float HeadingOf(Vector2 dir) => Mathf.Atan2(-dir.x, dir.y) * Mathf.Rad2Deg;

        /// <summary>Two lanes each way round the beltway (no signals), with slip turns on and off at the four connectors.</summary>
        private static int AddRingTraffic(List<RoadGraph2D.Node> nodes, List<RoadGraph2D.Edge> edges, List<RingLink> links)
        {
            Vector2[] line = SampleCurve(Ring, 9f);
            int n = line.Length;
            Vector2 Tan(int i) => (line[(i + 1) % n] - line[(i - 1 + n) % n]).normalized;
            Vector2 Right(int i) { Vector2 t = Tan(i); return new Vector2(t.y, -t.x); }
            float[] offsets = { 3f, 7f };   // inner lane by the median, kerb lane
            var chain = new Dictionary<(int d, int o), int[]>();
            int first = nodes.Count;
            foreach (int d in new[] { 1, -1 })
            for (int o = 0; o < 2; o++)
            {
                var arr = new int[n];
                for (int i = 0; i < n; i++)
                {
                    arr[i] = nodes.Count;
                    nodes.Add(new RoadGraph2D.Node { Position = line[i] + Right(i) * (d * offsets[o]), Heading = HeadingOf(Tan(i) * d), Vertical = false, IsExit = false, Junction = -1 });
                }
                for (int i = 0; i < n; i++)
                {
                    int next = d > 0 ? (i + 1) % n : (i - 1 + n) % n;
                    edges.Add(new RoadGraph2D.Edge { From = arr[i], To = arr[next], ThroughJunction = false, Control = (nodes[arr[i]].Position + nodes[arr[next]].Position) * 0.5f, SpeedLimit = HighwaySpeed });
                }
                chain[(d, o)] = arr;
            }

            var ramps = BeltwayRamps();
            var ics = InterchangesWithRamps(ramps);
            int Nearest(IEnumerable<int> ids, Vector2 p) { int bestId = -1; float bd = float.MaxValue; foreach (int id in ids) { float dd = (nodes[id].Position - p).sqrMagnitude; if (dd < bd) { bd = dd; bestId = id; } } return bestId; }
            int Node(Vector2 p, Vector2 heading) { nodes.Add(new RoadGraph2D.Node { Position = p, Heading = HeadingOf(heading.normalized), Vertical = false, IsExit = false, Junction = -1 }); return nodes.Count - 1; }
            void Link2(int a, int b, float speed) => edges.Add(new RoadGraph2D.Edge { From = a, To = b, Control = (nodes[a].Position + nodes[b].Position) * 0.5f, SpeedLimit = speed });
            foreach (RingLink link in links)
            {
                int k = 0; float best = float.MaxValue;
                for (int i = 0; i < n; i++) { float dd = (line[i] - link.Meet).sqrMagnitude; if (dd < best) { best = dd; k = i; } }
                int inside = Vector2.Dot(Right(k), link.Inward) > 0f ? 1 : -1;
                Interchange ic = ics.First(q => (q.M - link.Meet).sqrMagnitude < 1f);
                int outExit = Nearest(link.OutExits, ic.P(-StemLane, RampJoin)), inEntry = Nearest(link.InEntries, ic.P(StemLane, RampJoin));

                // The access road over the bridge: out of town on the left lane, into town on the right.
                Vector2 outward = -ic.N, inwardV = ic.N;
                int o1 = Node(ic.P(-StemLane, DeckHalf), outward), o2 = Node(ic.P(-StemLane, -DeckHalf), outward), o3 = Node(ic.BellStart, outward);
                Link2(outExit, o1, StemSpeed); Link2(o1, o2, StemSpeed); Link2(o2, o3, StemSpeed);
                int i0 = Node(ic.LoopEnd, inwardV), i1 = Node(ic.P(StemLane, -DeckHalf), inwardV), i2 = Node(ic.P(StemLane, DeckHalf), inwardV);
                Link2(i0, i1, StemSpeed); Link2(i1, i2, StemSpeed); Link2(i2, inEntry, StemSpeed);

                foreach (Ramp r in ramps)
                {
                    if ((r.Meet - link.Meet).sqrMagnitude >= 1f) continue;
                    int d = r.Far ? -inside : inside;
                    int roadNode = r.Far ? (r.On ? o3 : i0) : (r.On ? outExit : inEntry);
                    AddRampTraffic(nodes, edges, r, chain[(d, 1)], chain[(d, 0)], d, roadNode);
                }
            }
            return nodes.Count - first;
        }

        /// <summary>
        /// One lane each way along every local curve. Where a curve meets its grid road, each lane
        /// segment of that road is split at the meeting point so cars can turn in and out there.
        /// </summary>
        private static int AddLocalCurveTraffic(List<RoadGraph2D.Node> nodes, List<RoadGraph2D.Edge> edges)
        {
            int splits = 0;
            List<int> SplitHost(Road host, Vector2 p)
            {
                var mids = new List<int>();
                float along = host.Vertical ? p.y : p.x;
                foreach (Lane lane in LanesOf(host))
                {
                    for (int e = 0; e < edges.Count; e++)
                    {
                        var ed = edges[e];
                        if (ed.ThroughJunction) continue;
                        var a = nodes[ed.From]; var b = nodes[ed.To];
                        if (a.Junction < 0 && b.Junction < 0) continue;            // only grid lane segments
                        if (a.Vertical != host.Vertical || Mathf.Abs(a.Heading - lane.Heading) > 0.5f) continue;
                        float acrossA = host.Vertical ? a.Position.x : a.Position.y, acrossB = host.Vertical ? b.Position.x : b.Position.y;
                        if (Mathf.Abs(acrossA - lane.Across) > 0.1f || Mathf.Abs(acrossB - lane.Across) > 0.1f) continue;
                        float la = host.Vertical ? a.Position.y : a.Position.x, lb = host.Vertical ? b.Position.y : b.Position.x;
                        if (along < Mathf.Min(la, lb) + 1f || along > Mathf.Max(la, lb) - 1f) continue;
                        Vector2 mp = host.Vertical ? new Vector2(lane.Across, along) : new Vector2(along, lane.Across);
                        int mid = nodes.Count;
                        nodes.Add(new RoadGraph2D.Node { Position = mp, Heading = lane.Heading, Vertical = host.Vertical, IsExit = false, Junction = -1 });
                        int oldTo = ed.To;
                        ed.To = mid; ed.Control = (a.Position + mp) * 0.5f; edges[e] = ed;
                        edges.Add(new RoadGraph2D.Edge { From = mid, To = oldTo, ThroughJunction = false, Control = (mp + b.Position) * 0.5f });
                        mids.Add(mid);
                        splits++;
                        break;
                    }
                }
                return mids;
            }
            Vector2 Corner(Road host, Vector2 hostPoint, Vector2 curvePoint) =>
                host.Vertical ? new Vector2(hostPoint.x, curvePoint.y) : new Vector2(curvePoint.x, hostPoint.y);

            foreach (CurveRoad c in LocalCurves)
            {
                Vector2[] line = SampleCurve(c, 6f);
                int L = line.Length;
                Vector2 in0 = (line[1] - line[0]).normalized, in1 = (line[L - 2] - line[L - 1]).normalized;
                Road HostAt(Vector2 p, Vector2 inward)
                {
                    Vector2 q = p - inward * 2f;
                    int tx = Mathf.FloorToInt(q.x / T), ty = Mathf.FloorToInt(q.y / T);
                    return AvenueAt(tx, ty) ?? StreetAt(tx, ty);
                }
                Road h0 = HostAt(line[0], in0), h1 = HostAt(line[L - 1], in1);
                if (h0 == null || h1 == null) { Debug.LogWarning($"[City] {c.Name}: an end does not meet a grid road."); continue; }

                // Lane chains, 2 m right of the centreline; the end nodes sit just inside the curve.
                int[] Chain(int d)
                {
                    var arr = new int[L];
                    for (int k = 0; k < L; k++)
                    {
                        int i = d > 0 ? k : L - 1 - k;
                        Vector2 t = (line[Mathf.Min(i + 1, L - 1)] - line[Mathf.Max(i - 1, 0)]).normalized * d;
                        Vector2 r = new Vector2(t.y, -t.x);
                        Vector2 pos = line[i] + r * 2f;
                        if (i == 0) pos += in0 * 1.5f;
                        if (i == L - 1) pos += in1 * 1.5f;
                        arr[k] = nodes.Count;
                        nodes.Add(new RoadGraph2D.Node { Position = pos, Heading = HeadingOf(t), Vertical = false, IsExit = k == L - 1, Junction = -1 });
                    }
                    for (int k = 0; k + 1 < L; k++)
                        edges.Add(new RoadGraph2D.Edge { From = arr[k], To = arr[k + 1], ThroughJunction = false, Control = (nodes[arr[k]].Position + nodes[arr[k + 1]].Position) * 0.5f });
                    return arr;
                }
                int[] fwd = Chain(1), back = Chain(-1);
                List<int> m0 = SplitHost(h0, line[0]), m1 = SplitHost(h1, line[L - 1]);
                foreach (int m in m0)
                {
                    Vector2 hp = nodes[m].Position;
                    edges.Add(new RoadGraph2D.Edge { From = m, To = fwd[0], ThroughJunction = true, Control = Corner(h0, hp, nodes[fwd[0]].Position) });
                    edges.Add(new RoadGraph2D.Edge { From = back[L - 1], To = m, ThroughJunction = true, Control = Corner(h0, hp, nodes[back[L - 1]].Position) });
                }
                foreach (int m in m1)
                {
                    Vector2 hp = nodes[m].Position;
                    edges.Add(new RoadGraph2D.Edge { From = m, To = back[0], ThroughJunction = true, Control = Corner(h1, hp, nodes[back[0]].Position) });
                    edges.Add(new RoadGraph2D.Edge { From = fwd[L - 1], To = m, ThroughJunction = true, Control = Corner(h1, hp, nodes[fwd[L - 1]].Position) });
                }
            }
            return splits;
        }

        /// <summary>
        /// Lane nodes along one ramp. Off-ramp: the beltway kerb lane → ramp → the connector's inbound kerb
        /// lane. On-ramp: the connector's outbound kerb lane → ramp → the beltway kerb lane further on.
        /// </summary>
        private static void AddRampTraffic(List<RoadGraph2D.Node> nodes, List<RoadGraph2D.Edge> edges, Ramp r, int[] kerbChain, int[] innerChain, int dir, int roadNode)
        {
            int Nearest(IEnumerable<int> ids, Vector2 p) { int bestId = -1; float bd = float.MaxValue; foreach (int id in ids) { float d = (nodes[id].Position - p).sqrMagnitude; if (d < bd) { bd = d; bestId = id; } } return bestId; }
            int n = kerbChain.Length;
            int ChainIndex(int node) => System.Array.IndexOf(kerbChain, node);
            Vector2 travel = (r.Points[1] - r.Points[0]).normalized;

            // Nodes for the ramp's own points (the end that is a connector lane already has a node).
            int first = r.On ? 1 : 0, last = r.On ? r.Points.Count - 1 : r.Points.Count - 2;
            var ids = new List<int>();
            for (int i = first; i <= last; i++)
            {
                Vector2 dirv = i + 1 < r.Points.Count ? r.Points[i + 1] - r.Points[i] : r.Points[i] - r.Points[i - 1];
                ids.Add(nodes.Count);
                nodes.Add(new RoadGraph2D.Node { Position = r.Points[i], Heading = HeadingOf(dirv.normalized), Vertical = false, IsExit = false, Junction = -1 });
            }
            for (int i = 0; i + 1 < ids.Count; i++)
                edges.Add(new RoadGraph2D.Edge { From = ids[i], To = ids[i + 1], Control = (nodes[ids[i]].Position + nodes[ids[i + 1]].Position) * 0.5f, SpeedLimit = r.Speed[first + i + 1] });

            if (!r.On)
            {
                // Leave the beltway from the kerb-lane node just before the taper (about one car in four takes it).
                int j = ChainIndex(Nearest(kerbChain, r.Points[0]));
                if (Vector2.Dot(r.Points[0] - nodes[kerbChain[j]].Position, travel) < 0f) j = ((j - dir) % n + n) % n;
                edges.Add(new RoadGraph2D.Edge { From = kerbChain[j], To = ids[0], Control = (nodes[kerbChain[j]].Position + nodes[ids[0]].Position) * 0.5f, SpeedLimit = RampTaperSpeed, Weight = 1f });
                // Before it, some inside-lane traffic moves over to the kerb lane (so the inside lane is never a closed loop).
                int from = ((j - 2 * dir) % n + n) % n;
                edges.Add(new RoadGraph2D.Edge { From = innerChain[from], To = kerbChain[j], Control = (nodes[innerChain[from]].Position + nodes[kerbChain[j]].Position) * 0.5f, SpeedLimit = HighwaySpeed, Weight = 1f });
                int entry = roadNode;
                edges.Add(new RoadGraph2D.Edge { From = ids[ids.Count - 1], To = entry, Control = (nodes[ids[ids.Count - 1]].Position + nodes[entry].Position) * 0.5f, SpeedLimit = r.Far ? LoopSpeed : RampCurveSpeed });
            }
            else
            {
                int exit = roadNode;
                edges.Add(new RoadGraph2D.Edge { From = exit, To = ids[0], Control = (nodes[exit].Position + nodes[ids[0]].Position) * 0.5f, SpeedLimit = r.Far ? BellSpeed : RampCurveSpeed });
                // Merge into the kerb lane at the first beltway node past the end of the taper.
                Vector2 end = r.Points[r.Points.Count - 1], endDir = (end - r.Points[r.Points.Count - 2]).normalized;
                int j = ChainIndex(Nearest(kerbChain, end));
                if (Vector2.Dot(nodes[kerbChain[j]].Position - end, endDir) < 1f) j = ((j + dir) % n + n) % n;
                edges.Add(new RoadGraph2D.Edge { From = ids[ids.Count - 1], To = kerbChain[j], Control = (end + nodes[kerbChain[j]].Position) * 0.5f, SpeedLimit = RampTaperSpeed });
                // After the merge, some kerb-lane traffic moves out to the inside lane (which feeds it).
                int to = ((j + 2 * dir) % n + n) % n;
                edges.Add(new RoadGraph2D.Edge { From = kerbChain[j], To = innerChain[to], Control = (nodes[kerbChain[j]].Position + nodes[innerChain[to]].Position) * 0.5f, SpeedLimit = HighwaySpeed, Weight = 1f });
            }
        }

        // ------------------------------------------------------------------ beltway ramps: geometry

        // Each connector junction gets a right-hand off-ramp and on-ramp for the beltway direction on the
        // city side: a taper out of (or into) the kerb lane, a parallel slow-down / speed-up lane beside
        // the beltway, and a curve onto the connector road around a small grass island.
        private const float RampOffset = 11f;     // ramp lane centre from the beltway centreline (m): just outside the kerb lane
        private const float RampRadius = 22f;     // curve between the beltway and the connector road (m)
        private const float RampHalf = 2.25f;     // half the ramp's width (m)
        private const float RampTaper = 45f;      // lane-change taper between the kerb lane and the ramp lane (m)
        private const float RampAux = 35f;        // parallel slow-down / speed-up lane (m)
        private const float KerbLaneOffset = 7f;  // the beltway's kerb lane centre (matches AddRingTraffic)
        private const float RampTaperSpeed = 20f, RampAuxSpeed = 15f, RampCurveSpeed = 9f;   // m/s
        private const float RampJoin = RampOffset + RampRadius;   // connector lanes start this far from the beltway centreline (m)

        // ---- Trumpet interchanges. Each access road (the old connector, narrowed to one lane each way
        // outside town) crosses the beltway on an overpass. City side: the two slip ramps above. Far
        // side: a 270° loop off the far carriageway onto the bridge, and a sweeping ramp from the bridge
        // round the outside of the loop onto the far carriageway (the "bell" of the trumpet).
        // Frame used below: x = along the far carriageway's direction of travel, y = towards town.
        private const float StemLane = T;           // access-road lane centres either side of its centre line (m)
        private const float StemHalf = 6f;          // access-road half-width (two 4 m lanes and a centre tile)
        private const float DeckHalf = 17f;         // overpass deck: this far either side of the beltway centreline (m)
        private const float LoopR = 15f;            // loop ramp radius (m)
        private const float BellR = 11f;            // bell ramp corner radius (m)
        private const float RampGap = 3f;           // grass between neighbouring ramps (m)
        private const float LoopSpeed = 7.5f, BellSpeed = 7f, StemSpeed = 14f;
        private const float StemCut = 20f;          // the access road's tiles stop this far from the beltway centreline (m)

        /// <summary>One access road's junction with the beltway, and the frame the interchange is laid out in.</summary>
        private sealed class Interchange
        {
            public string RoadName;
            public Vector2 M, N, Eu;     // beltway centreline at the road; towards town; city-side direction of travel
            public int K, Dir;           // nearest RingFine sample; the way the city-side lanes run along the samples
            public Vector2 P(float x, float y) => M - Eu * x + N * y;
            public float X(Vector2 p) => Vector2.Dot(p - M, -Eu);
            public float Y(Vector2 p) => Vector2.Dot(p - M, N);
            public Vector2 LoopEnd, BellStart;   // where the far ramps meet the access road's lanes
        }

        private static List<Interchange> Interchanges()
        {
            var list = new List<Interchange>();
            Vector2[] L = RingFine; int count = L.Length;
            foreach (var (meet, inward, _) in RingMouths())
            {
                int k = 0; float best = float.MaxValue;
                for (int i = 0; i < count; i++) { float d = (L[i] - meet).sqrMagnitude; if (d < best) { best = d; k = i; } }
                Vector2 t = (L[(k + 1) % count] - L[(k - 1 + count) % count]).normalized;
                int dir = Vector2.Dot(new Vector2(t.y, -t.x), inward) > 0f ? 1 : -1;
                Vector2 uhat = t * dir;
                var ic = new Interchange { M = meet, N = inward, Eu = (uhat - inward * Vector2.Dot(uhat, inward)).normalized, K = k, Dir = dir };
                ic.LoopEnd = ic.P(StemLane, -(RampOffset + LoopR));
                ic.BellStart = ic.P(-StemLane, BellY0);
                list.Add(ic);
            }
            string[] names = { "Garnet Street", "Lakeview Avenue", "Crosstown Boulevard", "Grand Boulevard" };
            for (int i = 0; i < list.Count && i < names.Length; i++) list[i].RoadName = names[i];
            return list;
        }

        // Bell layout (frame x/y), derived from the loop so the two ramps keep RampGap of grass between them.
        private const float LoopBottom = -(RampOffset + 2f * LoopR);                 // lowest point of the loop's centreline
        private const float BellClear = 2f * RampHalf + RampGap;                     // centre-to-centre spacing of neighbouring ramps
        private const float BellYEast = LoopBottom - BellClear;                      // the bell's leg along the bottom
        private const float BellXNorth = StemLane + 2f * LoopR + BellClear;          // the bell's leg back up beside the loop
        private const float BellY0 = BellYEast + BellR;                              // the access road's out lane turns off here

        private sealed class Ramp
        {
            public bool Far;                  // the far carriageway's ramps (loop and bell)
            public bool On;                   // true = on-ramp (connector → beltway), false = off-ramp
            public Vector2 Meet, Inward, Side;
            public float Half;                // the connector road's half-width
            public Vector2 RoadEnd;           // the connector-lane point the ramp starts or ends at
            public readonly List<Vector2> Points = new List<Vector2>();   // centreline in driving order
            public readonly List<float> Speed = new List<float>();        // limit of the segment ending at each point
            public readonly List<bool> Curve = new List<bool>();          // on the curve (kerbed), not beside the beltway
            public readonly List<bool> Aux = new List<bool>();            // on the full-width parallel lane
            public void Add(Vector2 p, float speed, bool curve, bool aux) { Points.Add(p); Speed.Add(speed); Curve.Add(curve); Aux.Add(aux); }
        }

        private static Vector2[] _ringFine;
        private static Vector2[] RingFine => _ringFine ??= SampleCurve(Ring, 2f);

        /// <summary>Point and direction of travel on the beltway, <paramref name="s"/> metres from sample <paramref name="k"/> (positive = the way <paramref name="dir"/> runs).</summary>
        private static void RingWalk(int k, int dir, float s, out Vector2 p, out Vector2 travel)
        {
            Vector2[] L = RingFine; int n = L.Length;
            int step = s >= 0f ? dir : -dir, i = k;
            float left = Mathf.Abs(s);
            while (true)
            {
                int j = ((i + step) % n + n) % n;
                float seg = Vector2.Distance(L[i], L[j]);
                if (left <= seg || seg < 1e-4f)
                {
                    p = Vector2.Lerp(L[i], L[j], seg < 1e-4f ? 0f : left / seg);
                    Vector2 t = (L[j] - L[i]).normalized;
                    travel = s >= 0f ? t : -t;
                    return;
                }
                left -= seg; i = j;
            }
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float l2 = ab.sqrMagnitude;
            float u = l2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return Vector2.Distance(p, a + ab * u);
        }

        private static bool NearRamp(List<Ramp> ramps, Vector2 p, float within, bool besideOnly = false)
        {
            foreach (Ramp r in ramps)
                for (int i = 0; i + 1 < r.Points.Count; i++)
                {
                    if (besideOnly && (r.Curve[i] || r.Curve[i + 1])) continue;
                    if (DistToSegment(p, r.Points[i], r.Points[i + 1]) < within) return true;
                }
            return false;
        }

        /// <summary>The polyline cut into pieces no longer than <paramref name="step"/> (so dashed markings dash).</summary>
        private static Vector2[] Densify(Vector2[] line, float step)
        {
            var o = new List<Vector2>();
            for (int i = 0; i + 1 < line.Length; i++)
            {
                int k = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(line[i], line[i + 1]) / step));
                for (int j = 0; j < k; j++) o.Add(Vector2.Lerp(line[i], line[i + 1], j / (float)k));
            }
            if (line.Length > 0) o.Add(line[line.Length - 1]);
            return o.ToArray();
        }

        /// <summary>
        /// Every interchange ramp. City side: an off-ramp and an on-ramp for the city-side carriageway.
        /// Far side: the loop (off the far carriageway, onto the bridge) and the bell (off the bridge,
        /// round the loop, onto the far carriageway). Deterministic: drawing, traffic and tree clearing all use it.
        /// </summary>
        private static List<Ramp> BeltwayRamps()
        {
            var ramps = new List<Ramp>();
            foreach (Interchange ic in Interchanges())
            {
                Vector2 meet = ic.M, inward = ic.N, eu = ic.Eu;
                int k = ic.K, dir = ic.Dir;
                Vector2 side = new Vector2(inward.y, -inward.x);                         // right-hand side driving in along the road
                float half = StemHalf;
                float R = RampRadius;
                Vector2 Beside(float s, float offset) { RingWalk(k, dir, s, out Vector2 p, out Vector2 tr); return p + new Vector2(tr.y, -tr.x) * offset; }
                Vector2 BesideFar(float s, float offset) { RingWalk(k, -dir, s, out Vector2 p, out Vector2 tr); return p + new Vector2(tr.y, -tr.x) * offset; }
                float TaperOffset(float x) => Mathf.Lerp(KerbLaneOffset, RampOffset, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x)));
                Vector2 uhat = eu;    // city-side direction of travel

                // Off-ramp: taper out of the kerb lane, run alongside, then curve right into the inbound lane.
                var off = new Ramp { On = false, Meet = meet, Inward = inward, Side = side, Half = half };
                off.RoadEnd = meet + side * StemLane + inward * RampJoin;
                Vector2 offCentre = off.RoadEnd - eu * R, offStart = offCentre - inward * R;
                float s0 = Vector2.Dot(offStart - meet, uhat), sStart = s0 - RampAux - RampTaper;
                for (float s = sStart; s < s0 - 2.5f; s += 5f)
                {
                    float x = (s - sStart) / RampTaper;
                    off.Add(Beside(s, TaperOffset(x)), x < 1f ? RampTaperSpeed : RampAuxSpeed, false, x >= 1f);
                }
                for (int a = 0; a <= 8; a++)
                {
                    float th = Mathf.PI * 0.5f * a / 8f;
                    off.Add(offCentre + eu * (R * Mathf.Sin(th)) - inward * (R * Mathf.Cos(th)), RampCurveSpeed, a > 0, a == 0);
                }
                ramps.Add(off);

                // On-ramp: curve right out of the outbound lane, speed up alongside, then taper into the kerb lane.
                var on = new Ramp { On = true, Meet = meet, Inward = inward, Side = side, Half = half };
                on.RoadEnd = meet - side * StemLane + inward * RampJoin;
                Vector2 onCentre = on.RoadEnd + eu * R;
                for (int a = 0; a <= 8; a++)
                {
                    float ph = Mathf.PI * 0.5f * a / 8f;
                    on.Add(onCentre - eu * (R * Mathf.Cos(ph)) - inward * (R * Mathf.Sin(ph)), RampCurveSpeed, a < 8, a == 8);
                }
                float s1 = Vector2.Dot(on.Points[on.Points.Count - 1] - meet, uhat);
                for (float s = s1 + 5f; s <= s1 + RampAux + RampTaper + 0.01f; s += 5f)
                {
                    float x = (s - s1 - RampAux) / RampTaper;
                    on.Add(Beside(s, TaperOffset(1f - x)), x > 0f ? RampTaperSpeed : RampAuxSpeed, false, x <= 0f);
                }
                ramps.Add(on);

                // Loop: taper out of the far kerb lane, run alongside (under the bridge), then turn right
                // through 270° to come up beside the bridge and onto it, heading into town.
                var loop = new Ramp { Far = true, On = false, Meet = meet, Inward = inward, Side = side, Half = half };
                float x0 = StemLane + LoopR;   // the loop leaves the beltway here (far-travel distance past the road)
                float lStart = x0 - RampAux - RampTaper;
                for (float s = lStart; s < x0 - 2.5f; s += 5f)
                {
                    float x = (s - lStart) / RampTaper;
                    loop.Add(BesideFar(s, TaperOffset(x)), x < 1f ? RampTaperSpeed : RampAuxSpeed, false, x >= 1f);
                }
                {
                    RingWalk(k, -dir, x0, out Vector2 pc, out Vector2 f);
                    Vector2 rr = new Vector2(f.y, -f.x);                    // outward, away from the beltway
                    Vector2 top = pc + rr * RampOffset, centre = top + rr * LoopR;
                    const int segs = 24;
                    for (int a = 0; a <= segs; a++)
                    {
                        float th = 1.5f * Mathf.PI * a / segs;
                        loop.Add(centre - rr * (LoopR * Mathf.Cos(th)) + f * (LoopR * Mathf.Sin(th)), a == 0 ? RampAuxSpeed : LoopSpeed, a > 0, a == 0);
                    }
                    ic.LoopEnd = loop.Points[loop.Points.Count - 1];
                }
                loop.RoadEnd = ic.LoopEnd;
                ramps.Add(loop);

                // Bell: off the bridge's out lane, down past the loop, along the bottom, back up beside it and
                // round to the right onto the far carriageway, then speed up alongside and taper in.
                var bell = new Ramp { Far = true, On = true, Meet = meet, Inward = inward, Side = side, Half = half };
                bell.RoadEnd = ic.BellStart;
                void Arc(Vector2 c, float r, float fromDeg, float toDeg, int segs, bool skipFirst)
                {
                    for (int a = skipFirst ? 1 : 0; a <= segs; a++)
                    {
                        float th = Mathf.Lerp(fromDeg, toDeg, a / (float)segs) * Mathf.Deg2Rad;
                        bell.Add(ic.P(c.x + r * Mathf.Cos(th), c.y + r * Mathf.Sin(th)), BellSpeed, true, false);
                    }
                }
                // Corner 1: heading away from town, turn left onto the bottom leg.
                Arc(new Vector2(-StemLane + BellR, BellY0), BellR, 180f, 270f, 6, false);
                // Corner 2: along the bottom, turn left to head back towards the beltway beside the loop.
                Arc(new Vector2(BellXNorth - BellR, BellY0), BellR, 270f, 360f, 6, false);
                // Corner 3: turn right to run alongside the far carriageway.
                float yTurn = -RampOffset - BellR;
                Arc(new Vector2(BellXNorth + BellR, yTurn), BellR, 180f, 90f, 6, false);
                float s3 = BellXNorth + BellR;
                bell.Aux[bell.Aux.Count - 1] = true;
                for (float s = s3 + 5f; s <= s3 + RampAux + RampTaper + 0.01f; s += 5f)
                {
                    float x = (s - s3 - RampAux) / RampTaper;
                    bell.Add(BesideFar(s, TaperOffset(1f - x)), x > 0f ? RampTaperSpeed : RampAuxSpeed, false, x <= 0f);
                }
                ramps.Add(bell);
            }
            return ramps;
        }

        /// <summary>The far ramps' meeting points with the access road, after BeltwayRamps has worked them out.</summary>
        private static List<Interchange> InterchangesWithRamps(List<Ramp> ramps)
        {
            var ics = Interchanges();
            foreach (Interchange ic in ics)
                foreach (Ramp r in ramps)
                    if (r.Far && !r.On && (r.Meet - ic.M).sqrMagnitude < 1f) ic.LoopEnd = r.RoadEnd;
            return ics;
        }

        /// <summary>
        /// Rebuilds only the beltway and its interchanges in the open city scene: the access roads'
        /// tiles (narrowed outside town, ended short of the beltway), the curved-road drawing with the
        /// overpasses, the traffic graph's lanes (junctions and signals are unchanged) and any trees or
        /// props standing on the new ramps. Everything else in the scene, including hand edits, is left alone.
        /// </summary>
        [MenuItem("GPA/City/Rebuild Interchanges (open scene)")]
        private static void RebuildRampsMenu()
        {
            string report = RebuildRampsInOpenScene();
            Debug.Log("[City] " + report);
            EditorUtility.DisplayDialog("Interchanges", report, "OK");
        }

        public static string RebuildRampsInOpenScene()
        {
            if (Application.isPlaying) return "Stop play mode first.";
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) return $"Open {ScenePath} first.";
            var graph = Object.FindAnyObjectByType<RoadGraph2D>();
            if (graph == null) return "No Road Graph in the open scene.";
            Tilemap water = null, ground = null, edge = null;
            foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Include))
            {
                if (tm.name == "Water") water = tm; else if (tm.name == "Ground") ground = tm; else if (tm.name == "Water Edge") edge = tm;
            }
            if (water == null || ground == null || edge == null) return "City tilemaps (Water, Ground, Water Edge) not found.";
            string layers = EnsureOverpassLayers();

            // 0. The deterministic layout, as a full build would make it.
            Tiles.Clear();
            _ringFine = null;
            Classify();
            PlanEstates();
            int estatesHit = StampInterchanges();
            Ring.Line = SampleCurve(Ring, 2f);
            var ramps = BeltwayRamps();
            var ics = InterchangesWithRamps(ramps);

            // 1. Ground tiles: the access roads and the ground round each interchange.
            int painted = 0;
            var block = BlockTile();
            void Repaint(int x, int y)
            {
                if (!InMap(x, y)) return;
                var cell = new Vector3Int(x, y, 0);
                ground.SetTransformMatrix(cell, Matrix4x4.identity);
                ground.SetColor(cell, Color.white);
                if (_grid[x, y] == Cell.Water)
                {
                    ground.SetTile(cell, null);
                    water.SetTile(cell, GetTile("tile_water"));
                    bool touches = false;
                    for (int dy = -1; dy <= 1 && !touches; dy++)
                    for (int dx = -1; dx <= 1 && !touches; dx++)
                        if (InMap(x + dx, y + dy) && _grid[x + dx, y + dy] != Cell.Water) touches = true;
                    if (touches) edge.SetTile(cell, block);
                }
                else
                {
                    water.SetTile(cell, null);
                    PaintCell(ground, x, y);
                }
                painted++;
            }
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
                if (ConnectorKind(x, y) != 0) Repaint(x, y);
            foreach (Interchange ic in ics)
            {
                int cx = Mathf.FloorToInt(ic.M.x / T), cy = Mathf.FloorToInt(ic.M.y / T);
                for (int y = cy - 45; y <= cy + 45; y++)
                for (int x = cx - 45; x <= cx + 45; x++)
                    if (InMap(x, y) && _grid[x, y] == Cell.Highway && ConnectorKind(x, y) == 0) Repaint(x, y);
            }
            PaintBridge(ground, horizontal: true,  across: PortBridgeDeck.yMin,   from: PortBridgeDeck.xMin,   to: PortBridgeDeck.xMax,   width: 3);
            PaintBridge(ground, horizontal: false, across: UplandBridgeDeck.xMin, from: UplandBridgeDeck.yMin, to: UplandBridgeDeck.yMax, width: 3);
            EditorUtility.SetDirty(water); EditorUtility.SetDirty(ground); EditorUtility.SetDirty(edge);

            // 2. Road drawing and the overpasses.
            foreach (GameObject go in scene.GetRootGameObjects()) if (go.name == "Curved Roads") Object.DestroyImmediate(go);
            BuildCurvedRoadMeshes(new GameObject("Curved Roads").transform);

            // 3. Traffic lanes. Junctions (and the signals that point at them) must come out identical.
            var g = ComputeRoadGraph();
            if (g.junctions.Count != graph.Junctions.Length) return $"Junction count changed ({graph.Junctions.Length} → {g.junctions.Count}); run a full rebuild instead.";
            graph.Nodes = g.nodes.ToArray();
            graph.Edges = g.edges.ToArray();
            EditorUtility.SetDirty(graph);

            // 4. Trees on ground that is now road or verge, and street props on the new ramps.
            int trees = 0, props = 0, buildings = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                bool forest = root.name == "Forest", isProps = root.name == "Props", isBuildings = root.name == "Buildings";
                if (!forest && !isProps && !isBuildings) continue;
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    Transform c = root.transform.GetChild(i);
                    Vector2 pos = c.position;
                    int tx = Mathf.FloorToInt(pos.x / T), ty = Mathf.FloorToInt(pos.y / T);
                    bool onNewGround = InMap(tx, ty) && (_grid[tx, ty] == Cell.Highway || _grid[tx, ty] == Cell.Water) && ics.Any(ic => (ic.M - pos).magnitude < 190f);
                    if (!onNewGround && !NearRamp(ramps, pos, RampHalf + (forest ? 2f : 1f))) continue;
                    if (isBuildings) { if (NearRamp(ramps, pos, RampHalf + 1f)) buildings++; continue; }   // never delete a building: just report it
                    Object.DestroyImmediate(c.gameObject);
                    if (forest) trees++; else props++;
                }
            }

            // 5. The location database's land: the access roads' bridges are narrower now.
            var locations = Object.FindAnyObjectByType<CityLocations>();
            if (locations != null && locations.Database != null)
            {
                var land = locations.Database.Land;
                Rect R(RectInt b) => new Rect(b.xMin * T, b.yMin * T, b.width * T, b.height * T);
                for (int i = 0; i < land.Count; i++)
                {
                    if (land[i] == R(PortBridge)) land[i] = R(PortBridgeDeck);
                    else if (land[i] == R(UplandBridge)) land[i] = R(UplandBridgeDeck);
                }
                EditorUtility.SetDirty(locations.Database);
                AssetDatabase.SaveAssets();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"Interchanges rebuilt: {ics.Count} overpasses, {ramps.Count} ramps; repainted {painted} tiles; road graph {g.nodes.Count} nodes, {g.edges.Count} edges; " +
                   $"removed {trees} trees and {props} props" + (buildings > 0 ? $"; WARNING {buildings} buildings sit on a ramp" : "") +
                   (estatesHit > 0 ? $"; WARNING the ramps cross {estatesHit} estate lots" : "") + (layers != null ? "; " + layers : "") + ".";
        }

        /// <summary>Adds the two physics layers the overpasses use, if the project doesn't have them yet.</summary>
        private static string EnsureOverpassLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            var added = new List<string>();
            foreach (string name in new[] { Overpass2D.RailLayerName, Overpass2D.UpperLayerName })
            {
                bool exists = false;
                for (int i = 8; i < 32; i++) if (layers.GetArrayElementAtIndex(i).stringValue == name) exists = true;
                if (exists) continue;
                for (int i = 16; i < 32; i++)
                {
                    var el = layers.GetArrayElementAtIndex(i);
                    if (!string.IsNullOrEmpty(el.stringValue)) continue;
                    el.stringValue = name; added.Add($"{name} = layer {i}");
                    break;
                }
            }
            if (added.Count == 0) return null;
            tagManager.ApplyModifiedPropertiesWithoutUndo();
            return "added physics layers " + string.Join(", ", added);
        }

        /// <summary>One ground cell, painted the way <see cref="PaintGround"/> paints it (not water).</summary>
        private static void PaintCell(Tilemap ground, int x, int y)
        {
            var cell = new Vector3Int(x, y, 0);
            switch (_grid[x, y])
            {
                case Cell.Park: ground.SetTile(cell, GetTile("tile_grass")); break;
                case Cell.Forest when _tracks.Contains(new Vector2Int(x, y)): PaintDirt(ground, x, y); break;
                case Cell.Forest when _cleared.Contains(new Vector2Int(x, y)): PaintGrass(ground, x, y, 0.9f); break;
                case Cell.Forest: PaintGrass(ground, x, y, OutsideHighway(x, y) ? 0.62f : 0.78f); break;
                case Cell.Highway: PaintGrass(ground, x, y, 0.7f); break;
                case Cell.Curve: ground.SetTile(cell, GetTile("tile_sidewalk")); break;
                case Cell.Lot when DistrictAt(x, y).Suburban: PaintGrass(ground, x, y, 0.92f); break;
                case Cell.Lot:
                case Cell.Plaza: ground.SetTile(cell, GetTile("tile_sidewalk")); break;
                case Cell.Sidewalk: PaintSidewalk(ground, x, y); break;
                case Cell.Road: PaintRoad(ground, x, y); break;
            }
        }

        // ------------------------------------------------------------------ curved roads: drawing

        private const string CurveMeshPath = "Assets/GPA/Data/City/Halden_CurvedRoads.asset";
        private const float MouthFillet = 7f;   // corner radius where a connector road joins the beltway (m)

        /// <summary>Where each connector road joins the beltway: the meeting point on the beltway
        /// centreline, the direction from the beltway out along the road, and the road's half-width.</summary>
        private static List<(Vector2 meet, Vector2 inward, float half)> RingMouths()
        {
            var list = new List<(Vector2, Vector2, float)>();
            void Add(string roadName, bool ringAtHigh, float ringAlong)
            {
                Road road = Streets.Concat(Avenues).First(r => r.Name == roadName);
                float centreAcross = (road.Start + road.Width * 0.5f) * T;
                Vector2 meet = road.Vertical ? new Vector2(centreAcross, ringAlong * T) : new Vector2(ringAlong * T, centreAcross);
                Vector2 inward = (road.Vertical ? Vector2.up : Vector2.right) * (ringAtHigh ? -1f : 1f);
                list.Add((meet, inward, road.Width * T * 0.5f));
            }
            Add("Garnet Street", false, RingGarnetX);
            Add("Lakeview Avenue", false, RingLakeviewY);
            Add("Crosstown Boulevard", true, RingCrosstownX);
            Add("Grand Boulevard", true, RingGrandY);
            return list;
        }

        /// <summary>Smooth road ribbons (asphalt, shoulders, kerbs, lane markings) drawn over the ground tiles.</summary>
        private static void BuildCurvedRoadMeshes(Transform root)
        {
            var parts = new Dictionary<string, (List<Vector3> v, List<int> t)>();
            Vector2 Normal(Vector2[] line, int i, bool loop)
            {
                int n = line.Length;
                Vector2 a = loop ? line[(i - 1 + n) % n] : line[Mathf.Max(i - 1, 0)];
                Vector2 b = loop ? line[(i + 1) % n] : line[Mathf.Min(i + 1, n - 1)];
                Vector2 t = (b - a).normalized;
                return new Vector2(t.y, -t.x);
            }
            void Strip(string key, Vector2[] line, bool loop, float offset, float half, float dash = 0f, float gap = 0f, System.Func<Vector2, bool> skip = null)
            {
                if (!parts.TryGetValue(key, out var m)) parts[key] = m = (new List<Vector3>(), new List<int>());
                int n = line.Length, segs = loop ? n : n - 1;
                float dist = 0f;
                for (int i = 0; i < segs; i++)
                {
                    Vector2 a = line[i], b = line[(i + 1) % n];
                    float len = (b - a).magnitude;
                    float phase = dash > 0f ? dist % (dash + gap) : 0f;
                    dist += len;
                    if (dash > 0f && phase > dash) continue;
                    Vector2 na = Normal(line, i, loop), nb = Normal(line, (i + 1) % n, loop);
                    if (skip != null && skip((a + na * offset + b + nb * offset) * 0.5f)) continue;
                    int k = m.v.Count;
                    m.v.Add(a + na * (offset - half)); m.v.Add(a + na * (offset + half));
                    m.v.Add(b + nb * (offset + half)); m.v.Add(b + nb * (offset - half));
                    m.t.Add(k); m.t.Add(k + 1); m.t.Add(k + 2); m.t.Add(k); m.t.Add(k + 2); m.t.Add(k + 3);
                }
            }

            // Beltway: gravel shoulders, asphalt, edge lines, dashed lane lines, double yellow median.
            // The access roads cross over it on bridges, so it runs unbroken past every interchange.
            Vector2[] ring = Ring.Line;
            float hw = Ring.HalfWidth;
            // The ramps run alongside the beltway: no shoulder or solid edge line where they join it.
            var ramps = BeltwayRamps();
            var ics = InterchangesWithRamps(ramps);
            bool Skip(Vector2 p) => NearRamp(ramps, p, RampHalf + 2.6f, besideOnly: true);   // by the curve the edge line stays: it is the gore line
            Strip("Shoulder", ring, true, hw + 1.25f, 1.25f, skip: Skip); Strip("Shoulder", ring, true, -(hw + 1.25f), 1.25f, skip: Skip);
            Strip("Asphalt", ring, true, 0f, hw);
            Strip("White", ring, true, hw - 0.6f, 0.15f, skip: Skip); Strip("White", ring, true, -(hw - 0.6f), 0.15f, skip: Skip);
            Strip("White", ring, true, 5f, 0.12f, 3f, 6f); Strip("White", ring, true, -5f, 0.12f, 3f, 6f);
            Strip("Yellow", ring, true, 0.35f, 0.1f); Strip("Yellow", ring, true, -0.35f, 0.1f);

            foreach (Interchange ic in ics)
            {
                // Town side: from under the bridge out past where the slip ramps join, over the end of the road's tiles.
                Vector2[] near = Densify(new[] { ic.P(0f, DeckHalf - 1f), ic.P(0f, RampJoin + 10f) }, 2f);
                bool AtRampJoin(Vector2 p) => NearRamp(ramps, p, RampHalf + 0.6f);
                Strip("Asphalt", near, false, 0f, StemHalf);
                Strip("White", near, false, StemHalf - 0.45f, 0.12f, skip: AtRampJoin); Strip("White", near, false, -(StemHalf - 0.45f), 0.12f, skip: AtRampJoin);
                Strip("Yellow", near, false, 0.3f, 0.1f); Strip("Yellow", near, false, -0.3f, 0.1f);
                // Far side: a short two-way stub off the bridge to where the loop comes up, then the bell's lane alone.
                float yLoop = ic.Y(ic.LoopEnd);
                Vector2[] stub = Densify(new[] { ic.P(0f, -DeckHalf + 1f), ic.P(0f, yLoop) }, 2f);
                Strip("Asphalt", stub, false, 0f, StemHalf);
                Strip("White", stub, false, StemHalf - 0.45f, 0.12f); Strip("White", stub, false, -(StemHalf - 0.45f), 0.12f);
                Strip("Yellow", stub, false, 0.3f, 0.1f); Strip("Yellow", stub, false, -0.3f, 0.1f);
                Vector2[] lane = { ic.P(-StemLane, yLoop + 0.5f), ic.BellStart };
                Strip("Asphalt", lane, false, 0f, RampHalf);
                Strip("Kerb", lane, false, RampHalf + 0.15f, 0.2f); Strip("Kerb", lane, false, -(RampHalf + 0.15f), 0.2f);
                // Grass nose between the bell's lane and the loop coming up, where the stub ends.
                Strip("Kerb", new[] { ic.P(-StemLane + RampHalf + 0.2f, yLoop), ic.P(StemLane - RampHalf - 0.2f, yLoop) }, false, 0f, 0.2f);

                // The overpass deck, drawn above the beltway's traffic: shadow, deck, parapets, markings.
                Vector2 sun = new Vector2(1.4f, -1.4f);
                Vector2[] deck = Densify(new[] { ic.P(0f, -DeckHalf), ic.P(0f, DeckHalf) }, 2f);
                Strip("DeckShadow", deck.Select(q => q + sun).ToArray(), false, 0f, StemHalf + 0.6f);
                Strip("Deck", deck, false, 0f, StemHalf + 0.6f);
                Strip("Parapet", deck, false, StemHalf + 0.35f, 0.25f); Strip("Parapet", deck, false, -(StemHalf + 0.35f), 0.25f);
                Strip("DeckWhite", deck, false, StemHalf - 0.45f, 0.12f); Strip("DeckWhite", deck, false, -(StemHalf - 0.45f), 0.12f);
                Strip("DeckYellow", deck, false, 0.3f, 0.1f); Strip("DeckYellow", deck, false, -0.3f, 0.1f);
                foreach (float y in new[] { -DeckHalf + 0.2f, DeckHalf - 0.2f })   // expansion joints at the ends
                    Strip("Parapet", new[] { ic.P(-StemHalf, y), ic.P(StemHalf, y) }, false, 0f, 0.12f);
                BuildOverpass(root, ic);
            }

            // Ramps: asphalt; alongside the beltway a solid outer edge line, gravel shoulder and a dashed
            // line against the kerb lane; on the curves, kerbs (which stop where the ramp meets a road).
            foreach (Ramp r in ramps)
            {
                Vector2[] all = r.Points.ToArray();
                Strip("Asphalt", all, false, 0f, RampHalf);
                Vector2[] beside = Enumerable.Range(0, all.Length).Where(i => !r.Curve[i]).Select(i => all[i]).ToArray();
                Vector2[] aux = Enumerable.Range(0, all.Length).Where(i => r.Aux[i]).Select(i => all[i]).ToArray();
                Vector2[] curve = Enumerable.Range(0, all.Length).Where(i => r.Curve[i] || (i > 0 && r.Curve[i - 1]) || (i + 1 < all.Length && r.Curve[i + 1])).Select(i => all[i]).ToArray();
                if (beside.Length > 1)
                {
                    Strip("Shoulder", beside, false, RampHalf + 1.25f, 1.25f);
                    Strip("White", beside, false, RampHalf - 0.45f, 0.12f);
                }
                if (aux.Length > 1) Strip("White", Densify(aux, 0.5f), false, -RampHalf, 0.12f, 2f, 3f);
                if (curve.Length > 1)
                {
                    Interchange ic = ics.First(q => (q.M - r.Meet).sqrMagnitude < 1f);
                    float yLoop = ic.Y(ic.LoopEnd);
                    bool OnRoadOrBeltway(Vector2 p)
                    {
                        float y = ic.Y(p), x = ic.X(p);
                        if (Mathf.Abs(y) < hw + 0.5f) return true;                                         // the beltway
                        return Mathf.Abs(x) < StemHalf - 0.1f && y > yLoop - 0.5f && y < RampJoin + 10f;    // the access road
                    }
                    Vector2[] dense = Densify(curve, 1f);
                    Strip("Kerb", dense, false, RampHalf + 0.15f, 0.2f, skip: OnRoadOrBeltway);    // outer side
                    Strip("Kerb", dense, false, -(RampHalf + 0.15f), 0.2f, skip: OnRoadOrBeltway); // inner side
                }
            }

            // Local curves: kerbs, asphalt, dashed centre line. Ends run a little into the grid road.
            foreach (CurveRoad c in LocalCurves)
            {
                var line = c.Line.ToList();
                Vector2 in0 = (line[1] - line[0]).normalized, in1 = (line[line.Count - 2] - line[line.Count - 1]).normalized;
                line.Insert(0, line[0] - in0 * 1.2f);
                line.Add(line[line.Count - 1] - in1 * 1.2f);
                Vector2[] l = line.ToArray();
                Strip("Kerb", l, false, c.HalfWidth + 0.15f, 0.25f); Strip("Kerb", l, false, -(c.HalfWidth + 0.15f), 0.25f);
                Strip("Asphalt", l, false, 0f, c.HalfWidth);
                Strip("Yellow", l, false, 0f, 0.1f, 2.5f, 3.5f);
            }

            var colours = new Dictionary<string, (Color c, int order)>
            {
                { "Shoulder", (new Color(0.47f, 0.44f, 0.38f), 300) }, { "Kerb", (new Color(0.74f, 0.74f, 0.72f), 305) },
                { "Asphalt", (new Color(0.25f, 0.25f, 0.27f), 310) }, { "White", (new Color(0.93f, 0.93f, 0.9f), 320) },
                { "Yellow", (new Color(0.95f, 0.76f, 0.16f), 320) },
                // The overpass decks sit above the beltway's cars (Roofs layer, below anything on the bridge).
                { "DeckShadow", (new Color(0f, 0f, 0f, 0.38f), -480) }, { "Deck", (new Color(0.29f, 0.29f, 0.31f), -470) },
                { "Parapet", (new Color(0.68f, 0.68f, 0.66f), -460) }, { "DeckWhite", (new Color(0.93f, 0.93f, 0.9f), -465) },
                { "DeckYellow", (new Color(0.95f, 0.76f, 0.16f), -465) },
            };
            if (AssetDatabase.LoadAssetAtPath<Mesh>(CurveMeshPath) != null) AssetDatabase.DeleteAsset(CurveMeshPath);
            bool firstMesh = true;
            foreach (var kv in parts)
            {
                var mesh = new Mesh { name = "Curved " + kv.Key, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.SetVertices(kv.Value.v);
                mesh.SetTriangles(kv.Value.t, 0);
                mesh.SetColors(Enumerable.Repeat(Color.white, kv.Value.v.Count).ToList());
                mesh.RecalculateBounds();
                if (firstMesh) { AssetDatabase.CreateAsset(mesh, CurveMeshPath); firstMesh = false; }
                else AssetDatabase.AddObjectToAsset(mesh, CurveMeshPath);
                var go = new GameObject("Curved " + kv.Key);
                go.transform.SetParent(root);
                go.isStatic = true;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = SolidMaterial("Road" + kv.Key, colours[kv.Key].c);
                Sorting2D.Apply(mr, colours[kv.Key].order < 0 ? Sorting2D.Roofs : Sorting2D.Ground, colours[kv.Key].order);
            }
        }

        /// <summary>
        /// The overpass's two-level physics: an <see cref="Overpass2D"/> that lifts cars and people onto the
        /// deck when they drive or walk onto it from either end, and parapet colliders that only the deck level hits.
        /// </summary>
        private static void BuildOverpass(Transform root, Interchange ic)
        {
            var go = new GameObject("Overpass " + ic.RoadName);
            go.transform.SetParent(root);
            go.transform.SetPositionAndRotation(ic.M, Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, ic.N)));
            var ov = go.AddComponent<Overpass2D>();
            ov.HalfLength = DeckHalf;
            ov.HalfWidth = StemHalf + 0.6f;
            int railLayer = LayerMask.NameToLayer(Overpass2D.RailLayerName);
            foreach (float sgn in new[] { -1f, 1f })
            {
                var rail = new GameObject(sgn < 0 ? "Parapet L" : "Parapet R");
                rail.transform.SetParent(go.transform, false);
                rail.transform.localPosition = new Vector3(sgn * (StemHalf + 0.35f), 0f, 0f);
                if (railLayer >= 0) rail.layer = railLayer;
                var box = rail.AddComponent<BoxCollider2D>();
                box.size = new Vector2(0.5f, DeckHalf * 2f + 2f);
                rail.isStatic = true;
            }
        }

        private static void BuildWalkMap()
        {
            var map = new GameObject("Walk Map").AddComponent<WalkMap2D>();
            map.Width = MapW; map.Height = MapH; map.CellSize = T; map.Origin = new Vector2(MinX * T, MinY * T);
            var cells = new byte[MapW * MapH];
            for (int y = MinY; y < MaxY; y++)
            for (int x = MinX; x < MaxX; x++)
            {
                WalkMap2D.Kind k = WalkMap2D.Kind.Blocked;
                Vector2 centre = new Vector2(x * T + T * 0.5f, y * T + T * 0.5f);
                if (_grid[x, y] == Cell.Sidewalk) k = WalkMap2D.Kind.Sidewalk;
                else if (_grid[x, y] == Cell.Park || _grid[x, y] == Cell.Plaza) k = InPond(centre, 1f) ? WalkMap2D.Kind.Blocked : WalkMap2D.Kind.Park;
                else if (_grid[x, y] == Cell.Road)
                {
                    Road av = AvenueAt(x, y), st = StreetAt(x, y);
                    bool junction = av != null && st != null;
                    bool crosswalk = !junction && (av != null
                        ? StreetAt(x, y + 1) != null || StreetAt(x, y - 1) != null
                        : AvenueAt(x + 1, y) != null || AvenueAt(x - 1, y) != null);
                    if (crosswalk) k = WalkMap2D.Kind.Crosswalk;
                }
                cells[(y - MinY) * MapW + (x - MinX)] = (byte)k;
            }
            map.Cells = cells;
        }

        // ------------------------------------------------------------------ tiles

        private static Tilemap NewTilemap(Grid grid, string name, string layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(grid.transform, false);
            var map = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.mode = TilemapRenderer.Mode.Chunk;
            Sorting2D.Apply(renderer, layer, 0);
            return map;
        }

        private static void Set(Tilemap map, int x, int y, string sprite, float rotation)
        {
            var cell = new Vector3Int(x, y, 0);
            map.SetTile(cell, GetTile(sprite));
            if (Mathf.Abs(rotation) > 0.01f)
            {
                map.SetTileFlags(cell, TileFlags.None);
                map.SetTransformMatrix(cell, Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, rotation)));
            }
        }

        /// <summary>Tile asset for a sprite, created once under Art/Tiles2D and reused.</summary>
        private static TileBase GetTile(string sprite)
        {
            if (Tiles.TryGetValue(sprite, out var cached) && cached != null) return cached;
            System.IO.Directory.CreateDirectory(TileFolder);
            string path = $"{TileFolder}/{sprite}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }
            tile.sprite = TopDown2DSceneBuilder.S(sprite);
            tile.colliderType = UnityEngine.Tilemaps.Tile.ColliderType.None;
            tile.flags = TileFlags.None;
            EditorUtility.SetDirty(tile);
            Tiles[sprite] = tile;
            return tile;
        }
    }
}
