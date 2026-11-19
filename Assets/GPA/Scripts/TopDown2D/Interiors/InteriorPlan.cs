using System.Collections.Generic;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Pure layout for one floor of a building: rooms, walls, doors, stairs. Works in a local frame
    /// where u runs along the front (street) wall and v runs from the front wall into the building,
    /// so the same rules lay out a building whatever way it faces.
    /// </summary>
    public sealed class InteriorPlan
    {
        public sealed class Room
        {
            public string Kind;
            public Rect Area;          // local u/v
            public Vector2 DoorA, DoorB; // door gap ends (local), on the hall side
            public bool HasDoor;
            public bool Locked;
            public bool Bars;
        }

        public readonly List<Room> Rooms = new List<Room>();
        public readonly List<(Vector2 a, Vector2 b)> Walls = new List<(Vector2, Vector2)>();
        public Rect Hall;
        public Vector2 Stairs;              // local centre
        public Vector2 Elevator;            // local centre
        public bool HasStairs, HasElevator;

        // ---- frame -------------------------------------------------------------------------------
        public readonly Vector2 Origin, AxisU, AxisV;
        public readonly float W, D;

        public Vector2 ToWorld(Vector2 local) => Origin + AxisU * local.x + AxisV * local.y;
        public float AngleU => Mathf.Atan2(AxisU.y, AxisU.x) * Mathf.Rad2Deg;

        private InteriorPlan(Rect footprint, Vector2 entrance)
        {
            // Pick the footprint side nearest the entrance as the front.
            float dS = Mathf.Abs(entrance.y - footprint.yMin), dN = Mathf.Abs(entrance.y - footprint.yMax);
            float dW = Mathf.Abs(entrance.x - footprint.xMin), dE = Mathf.Abs(entrance.x - footprint.xMax);
            float m = Mathf.Min(Mathf.Min(dS, dN), Mathf.Min(dW, dE));
            if (m == dS) { Origin = new Vector2(footprint.xMin, footprint.yMin); AxisU = Vector2.right; AxisV = Vector2.up; W = footprint.width; D = footprint.height; }
            else if (m == dN) { Origin = new Vector2(footprint.xMax, footprint.yMax); AxisU = Vector2.left; AxisV = Vector2.down; W = footprint.width; D = footprint.height; }
            else if (m == dW) { Origin = new Vector2(footprint.xMin, footprint.yMax); AxisU = Vector2.down; AxisV = Vector2.right; W = footprint.height; D = footprint.width; }
            else { Origin = new Vector2(footprint.xMax, footprint.yMin); AxisU = Vector2.up; AxisV = Vector2.left; W = footprint.height; D = footprint.width; }
        }

        public static string FloorName(int floor)
        {
            string[] n = { "ground floor", "second floor", "third floor", "fourth floor", "fifth floor", "sixth floor", "seventh floor", "eighth floor" };
            return floor >= 0 && floor < n.Length ? n[floor] : $"floor {floor + 1}";
        }

        public static int FloorCountFor(string type, int dbFloors)
        {
            switch (type)
            {
                case "apartment": return Mathf.Clamp(dbFloors, 2, 6);
                case "police": return 3;
                case "office": return Mathf.Clamp(dbFloors, 1, 5);
                case "shop": return Mathf.Clamp(dbFloors, 1, 2);
                default: return 1;
            }
        }

        public static InteriorPlan Build(Rect footprint, Vector2 entrance, string type, int floor, int floorCount, int seed)
        {
            var p = new InteriorPlan(footprint, entrance);
            var rng = new System.Random(seed * 31 + floor);
            bool openPlan = floor == 0 && (type == "shop" || type == "restaurant" || type == "warehouse");
            if (openPlan) p.LayoutOpen(type, floorCount, rng);
            else p.LayoutCorridor(type, floor, floorCount, rng);

            // Outer walls; the ground floor gets an entrance gap in the front wall.
            float gapC = p.Hall.center.x, gapH = 1.1f;
            Vector2 c0 = new Vector2(0, 0), c1 = new Vector2(p.W, 0), c2 = new Vector2(p.W, p.D), c3 = new Vector2(0, p.D);
            if (floor == 0) { p.Walls.Add((c0, new Vector2(gapC - gapH, 0))); p.Walls.Add((new Vector2(gapC + gapH, 0), c1)); }
            else p.Walls.Add((c0, c1));
            p.Walls.Add((c1, c2)); p.Walls.Add((c2, c3)); p.Walls.Add((c3, c0));
            return p;
        }

        // ---- corridor layout ---------------------------------------------------------------------
        private void LayoutCorridor(string type, int floor, int floorCount, System.Random rng)
        {
            const float hallW = 2.4f;
            bool centre = W >= 10f;
            float hl = centre ? W * 0.5f - hallW * 0.5f : 0f;
            Hall = new Rect(hl, 0, hallW, D);

            // Stairs (and a lift in taller buildings) at the back of the hall.
            HasStairs = floorCount > 1;
            HasElevator = floorCount >= 4 && centre;
            float coreDepth = HasStairs ? 3f : 0f;
            if (HasElevator) { Stairs = new Vector2(hl + hallW * 0.25f, D - coreDepth * 0.5f); Elevator = new Vector2(hl + hallW * 0.75f, D - coreDepth * 0.5f); }
            else Stairs = new Vector2(hl + hallW * 0.5f, D - coreDepth * 0.5f);

            var kinds = KindsFor(type, floor);
            int k = 0;
            if (centre)
            {
                k = Strip(0, hl, hl, kinds, k, rng);
                Strip(hl + hallW, W, hl + hallW, kinds, k, rng);
            }
            else Strip(hallW, W, hallW, kinds, 0, rng);
        }

        /// <summary>Fills the strip u0..u1 with rooms stacked along v; doors face the hall edge hallU.</summary>
        private int Strip(float u0, float u1, float hallU, List<string> kinds, int k, System.Random rng)
        {
            if (u1 - u0 < 2.5f) return k;
            float v = 0;
            while (D - v > 0.5f)
            {
                float depth = 4f + (float)rng.NextDouble() * 3f;
                if (D - v - depth < 4f) depth = D - v;     // last room takes the rest
                var room = new Room { Kind = kinds[k % kinds.Count], Area = new Rect(u0, v, u1 - u0, depth), HasDoor = true };
                k++;
                float dv = v + Mathf.Min(1.4f, depth * 0.5f - 0.6f);
                if (dv < v + 0.3f) dv = v + 0.3f;
                room.DoorA = new Vector2(hallU, dv);
                room.DoorB = new Vector2(hallU, dv + 1.2f);
                if (room.Kind == "cell") { room.Locked = true; room.Bars = true; }
                AddRoom(room, hallU);
                v += depth;
            }
            return k;
        }

        private void AddRoom(Room r, float doorU)
        {
            Rooms.Add(r);
            Rect a = r.Area;
            var bl = new Vector2(a.xMin, a.yMin); var br = new Vector2(a.xMax, a.yMin);
            var tr = new Vector2(a.xMax, a.yMax); var tl = new Vector2(a.xMin, a.yMax);
            Walls.Add((bl, br)); Walls.Add((tl, tr));
            // Side walls: the one on the hall side is split around the door.
            foreach (var (s, e) in new[] { (bl, tl), (br, tr) })
            {
                if (r.HasDoor && Mathf.Abs(s.x - doorU) < 0.01f)
                {
                    Walls.Add((s, r.DoorA)); Walls.Add((r.DoorB, e));
                }
                else Walls.Add((s, e));
            }
        }

        // ---- open plan (shop / restaurant ground floor) ---------------------------------------
        private void LayoutOpen(string type, int floorCount, System.Random rng)
        {
            float backD = Mathf.Clamp(D * 0.3f, 3f, 5f);
            Hall = new Rect(W * 0.5f - 1.2f, 0, 2.4f, D - backD);
            Rooms.Add(new Room { Kind = type == "restaurant" ? "dining" : type == "warehouse" ? "stock" : "sales", Area = new Rect(0, 0, W, D - backD) });
            HasStairs = floorCount > 1;
            float backW = HasStairs ? W - 3f : W;
            if (HasStairs) Stairs = new Vector2(W - 1.5f, D - backD * 0.5f);
            var back = new Room { Kind = type == "restaurant" ? "kitchen" : type == "warehouse" ? "office" : "stock", Area = new Rect(0, D - backD, backW, backD), HasDoor = true };
            float du = Mathf.Min(backW * 0.5f, 3f);
            back.DoorA = new Vector2(du - 0.6f, D - backD); back.DoorB = new Vector2(du + 0.6f, D - backD);
            Rooms.Add(back);
            Walls.Add((new Vector2(0, D - backD), back.DoorA)); Walls.Add((back.DoorB, new Vector2(W, D - backD)));
            if (HasStairs) Walls.Add((new Vector2(backW, D - backD), new Vector2(backW, D)));
        }

        private static List<string> KindsFor(string type, int floor)
        {
            switch (type)
            {
                case "apartment": return new List<string> { "unit" };
                case "police":
                    if (floor == 0) return new List<string> { "frontdesk", "briefing", "sergeant", "locker", "restroom", "breakroom" };
                    if (floor == 1) return new List<string> { "detectives", "interview", "interview", "office", "restroom" };
                    return new List<string> { "cell", "cell", "processing", "cell", "cell", "cell" };
                case "office":
                    return floor == 0 ? new List<string> { "reception", "office", "meeting", "restroom", "office" }
                                      : new List<string> { "office", "office", "meeting", "breakroom", "office", "restroom" };
                case "shop": return new List<string> { "office", "stock" };
                default: return new List<string> { "office", "stock" };
            }
        }
    }
}
