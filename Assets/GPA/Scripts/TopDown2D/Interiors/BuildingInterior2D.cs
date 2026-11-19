using System.Collections.Generic;
using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// The inside of one building: one child per floor, built the first time it is shown from
    /// <see cref="InteriorPlan"/>. Only the floor the player is on is active.
    /// </summary>
    public sealed class BuildingInterior2D : MonoBehaviour
    {
        public string BuildingId { get; private set; }
        public string BuildingName { get; private set; }
        public string Address { get; private set; }
        public string Type { get; private set; }
        public Rect Footprint { get; private set; }
        public int FloorCount { get; private set; }
        public int Floor { get; private set; } = -1;
        public System.Action<int> FloorChanged;

        private Vector2 _entrance;
        private int _seed;
        private readonly Dictionary<int, FloorView> _floors = new Dictionary<int, FloorView>();
        private readonly List<string> _residentIds = new List<string>();
        private readonly List<int> _residentLooks = new List<int>();
        private bool _residentsLoaded;
        private int _residentCursor;

        private sealed class RoomView { public InteriorPlan.Room Room; public GameObject Cover; public InteriorDoor2D Door; }
        private sealed class FloorView { public GameObject Root; public InteriorPlan Plan; public readonly List<RoomView> Rooms = new List<RoomView>(); }

        private static Sprite _pixel;
        public static Sprite Pixel
        {
            get
            {
                if (_pixel == null)
                {
                    var t = new Texture2D(1, 1) { filterMode = FilterMode.Point };
                    t.SetPixel(0, 0, Color.white); t.Apply();
                    _pixel = Sprite.Create(t, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                    _pixel.name = "InteriorPixel";
                }
                return _pixel;
            }
        }

        public void Configure(string id, string name, string address, string type, Rect footprint, Vector2 entrance, int dbFloors)
        {
            BuildingId = id; BuildingName = name; Address = address; Type = type;
            Footprint = footprint; _entrance = entrance;
            FloorCount = InteriorPlan.FloorCountFor(type, dbFloors);
            _seed = SeedFor(id);
        }

        // ---- queries for callouts (the plan is deterministic, so it can be read before anything is built)
        public static int SeedFor(string buildingId) => buildingId != null ? buildingId.GetHashCode() : 0;

        public static InteriorPlan PreviewPlan(CityLocationDatabase.Building b, int floor) =>
            InteriorPlan.Build(b.Footprint, b.Entrance, b.Type, floor, InteriorPlan.FloorCountFor(b.Type, b.Floors), SeedFor(b.Id));

        public InteriorPlan PlanFor(int floor) =>
            _floors.TryGetValue(floor, out var v) ? v.Plan : InteriorPlan.Build(Footprint, _entrance, Type, floor, FloorCount, _seed);

        /// <summary>The floor's root object once it has been built (actors spawned under it hide with the floor).</summary>
        public Transform FloorRoot(int floor) => _floors.TryGetValue(floor, out var v) ? v.Root.transform : null;

        public InteriorDoor2D DoorOf(int floor, int roomIndex) =>
            _floors.TryGetValue(floor, out var v) && roomIndex >= 0 && roomIndex < v.Rooms.Count ? v.Rooms[roomIndex].Door : null;

        public void ShowFloor(int floor)
        {
            floor = Mathf.Clamp(floor, 0, FloorCount - 1);
            foreach (var kv in _floors) kv.Value.Root.SetActive(kv.Key == floor);
            if (!_floors.ContainsKey(floor)) _floors[floor] = BuildFloor(floor);
            bool changed = Floor != floor;
            Floor = floor;
            if (changed) FloorChanged?.Invoke(floor);
        }

        public void Hide()
        {
            foreach (var kv in _floors) kv.Value.Root.SetActive(false);
            Floor = -1;
        }

        public bool Contains(Vector2 p, float margin = 0f) =>
            p.x >= Footprint.xMin - margin && p.x <= Footprint.xMax + margin && p.y >= Footprint.yMin - margin && p.y <= Footprint.yMax + margin;

        /// <summary>Hides rooms the player can't see into: not in them and the door shut.</summary>
        private void LateUpdate()
        {
            if (Floor < 0 || !_floors.TryGetValue(Floor, out var view)) return;
            Vector2 player = InteriorManager2D.PlayerPosition;
            foreach (var r in view.Rooms)
            {
                if (r.Cover == null) continue;
                bool inside = r.Room.Area.Contains(LocalOf(view.Plan, player));
                bool open = r.Door == null || r.Door.Open;
                bool show = !(inside || open);
                if (r.Cover.activeSelf != show) r.Cover.SetActive(show);
            }
        }

        private static Vector2 LocalOf(InteriorPlan p, Vector2 world)
        {
            Vector2 d = world - p.Origin;
            return new Vector2(Vector2.Dot(d, p.AxisU), Vector2.Dot(d, p.AxisV));
        }

        // ---- construction --------------------------------------------------------------------
        private FloorView BuildFloor(int floor)
        {
            var plan = InteriorPlan.Build(Footprint, _entrance, Type, floor, FloorCount, _seed);
            var view = new FloorView { Plan = plan, Root = new GameObject($"Floor {floor}") };
            view.Root.transform.SetParent(transform, false);
            Transform root = view.Root.transform;

            // Floor slab, then the hall in a slightly different tone.
            Color slab = Type == "police" ? new Color(0.72f, 0.74f, 0.78f) : new Color(0.78f, 0.72f, 0.62f);
            Quad(root, plan, new Rect(0, 0, plan.W, plan.D), slab, Sorting2D.Props, 0, "Slab");
            Quad(root, plan, plan.Hall, slab * 0.9f + new Color(0, 0, 0, 0.1f), Sorting2D.Props, 1, "Hall");
            _keep = WalkwaysOf(plan, floor);
            DressHall(root, plan, floor);

            foreach (var room in plan.Rooms)
            {
                var rv = new RoomView { Room = room };
                Quad(root, plan, room.Area, RoomTint(room.Kind), Sorting2D.Props, 1, room.Kind);
                Furnish(root, plan, room);
                if (room.HasDoor) rv.Door = MakeDoor(root, plan, room);
                if (room.HasDoor) rv.Cover = Quad(root, plan, room.Area, new Color(0.08f, 0.08f, 0.1f, 1f), Sorting2D.Roofs, 3, "Cover").gameObject;
                view.Rooms.Add(rv);
                if (Type == "apartment" && room.Kind == "unit") SpawnResident(root, plan, room);
            }

            foreach (var (a, b) in plan.Walls) Wall(root, plan, a, b);

            if (plan.HasStairs) MakeStairs(root, plan, floor);
            if (plan.HasElevator) MakeElevator(root, plan, floor);
            return view;
        }

        private static Color RoomTint(string kind)
        {
            switch (kind)
            {
                case "cell": return new Color(0.55f, 0.56f, 0.58f);
                case "kitchen": case "restroom": return new Color(0.86f, 0.88f, 0.9f);
                case "unit": return new Color(0.66f, 0.5f, 0.36f);
                case "dining": return new Color(0.6f, 0.38f, 0.3f);
                case "stock": return new Color(0.6f, 0.6f, 0.58f);
                default: return new Color(0.62f, 0.64f, 0.7f);
            }
        }

        private static Transform Quad(Transform parent, InteriorPlan p, Rect local, Color colour, string layer, int order, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = p.ToWorld(local.center);
            go.transform.rotation = Quaternion.Euler(0, 0, p.AngleU);
            go.transform.localScale = new Vector3(local.width, local.height, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Pixel; sr.color = colour;
            Sorting2D.Apply(sr, layer, order);
            return go.transform;
        }

        private static void Wall(Transform parent, InteriorPlan p, Vector2 a, Vector2 b)
        {
            const float t = 0.25f;
            Vector2 d = b - a; float len = d.magnitude;
            if (len < 0.05f) return;
            var go = new GameObject("Wall");
            go.transform.SetParent(parent, false);
            Vector2 wa = p.ToWorld(a), wb = p.ToWorld(b);
            go.transform.position = (wa + wb) * 0.5f;
            Vector2 wd = wb - wa;
            go.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(wd.y, wd.x) * Mathf.Rad2Deg);
            go.transform.localScale = new Vector3(len + t, t, 1);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Pixel; sr.color = new Color(0.22f, 0.22f, 0.25f);
            Sorting2D.Apply(sr, Sorting2D.Props, 5);
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
        }

        private InteriorDoor2D MakeDoor(Transform parent, InteriorPlan p, InteriorPlan.Room room)
        {
            Vector2 wa = p.ToWorld(room.DoorA), wb = p.ToWorld(room.DoorB);
            Vector2 d = wb - wa; float len = d.magnitude;
            var hinge = new GameObject("Door");
            hinge.transform.SetParent(parent, false);
            hinge.transform.position = wa;
            hinge.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);

            var leaf = new GameObject("Leaf");
            leaf.transform.SetParent(hinge.transform, false);
            var panel = new GameObject("Panel");
            panel.transform.SetParent(leaf.transform, false);
            panel.transform.localPosition = new Vector3(len * 0.5f, 0, 0);
            panel.transform.localScale = new Vector3(len, room.Bars ? 0.12f : 0.15f, 1);
            var sr = panel.AddComponent<SpriteRenderer>();
            sr.sprite = Pixel; sr.color = room.Bars ? new Color(0.35f, 0.37f, 0.4f) : new Color(0.45f, 0.3f, 0.18f);
            Sorting2D.Apply(sr, Sorting2D.Props, 6);

            var blocker = hinge.AddComponent<BoxCollider2D>();
            blocker.offset = new Vector2(len * 0.5f, 0); blocker.size = new Vector2(len, 0.2f);

            var useGo = new GameObject("Use");
            useGo.transform.SetParent(hinge.transform, false);
            useGo.transform.localPosition = new Vector3(len * 0.5f, 0, 0);
            var use = useGo.AddComponent<Interactable2D>();
            use.Range = 1.6f;
            var door = hinge.AddComponent<InteriorDoor2D>();
            door.Setup(leaf.transform, blocker, use, room.Locked);
            return door;
        }

        private void MakeStairs(Transform parent, InteriorPlan p, int floor)
        {
            var r = new Rect(p.Stairs.x - 1f, p.Stairs.y - 1.3f, 2f, 2.6f);
            Quad(parent, p, r, new Color(0.4f, 0.4f, 0.42f), Sorting2D.Props, 2, "Stairs");
            for (int i = 0; i < 6; i++)
                Quad(parent, p, new Rect(r.xMin, r.yMin + 0.2f + i * 0.42f, r.width, 0.08f), new Color(0.25f, 0.25f, 0.27f), Sorting2D.Props, 3, "Step");
            if (floor < FloorCount - 1) Stair(parent, p, p.Stairs + new Vector2(0, 0.7f), $"Go upstairs ({InteriorPlan.FloorName(floor + 1)})", floor + 1);
            if (floor > 0) Stair(parent, p, p.Stairs - new Vector2(0, 0.7f), $"Go downstairs ({InteriorPlan.FloorName(floor - 1)})", floor - 1);
        }

        private void Stair(Transform parent, InteriorPlan p, Vector2 local, string prompt, int to)
        {
            var go = new GameObject("StairUse");
            go.transform.SetParent(parent, false);
            go.transform.position = p.ToWorld(local);
            var it = go.AddComponent<Interactable2D>();
            it.Prompt = prompt; it.Range = 1.4f;
            it.Used += _ => ShowFloor(to);
        }

        private void MakeElevator(Transform parent, InteriorPlan p, int floor)
        {
            Quad(parent, p, new Rect(p.Elevator.x - 0.6f, p.Elevator.y - 1f, 1.2f, 2f), new Color(0.55f, 0.58f, 0.62f), Sorting2D.Props, 2, "Elevator");
            var go = new GameObject("ElevatorUse");
            go.transform.SetParent(parent, false);
            go.transform.position = p.ToWorld(p.Elevator);
            var it = go.AddComponent<Interactable2D>();
            int next = (floor + 1) % FloorCount;
            it.Prompt = $"Elevator to {InteriorPlan.FloorName(next)}"; it.Range = 1.3f;
            it.Used += _ => ShowFloor(next);
        }

        // ---- furniture -----------------------------------------------------------------------
        // Everything is drawn from flat quads in the room's own frame (u along the hall side, v into
        // the room), so it lines up however the building faces. Each piece gets a soft drop shadow
        // and, where it helps, a lighter top or a detail so it reads as an object from above.

        private static readonly Color Wood = new Color(0.52f, 0.36f, 0.22f), DarkWood = new Color(0.36f, 0.24f, 0.15f),
            Fabric = new Color(0.32f, 0.42f, 0.6f), Metal = new Color(0.52f, 0.54f, 0.58f), White = new Color(0.93f, 0.93f, 0.95f),
            Leaf = new Color(0.25f, 0.55f, 0.28f), Pot = new Color(0.62f, 0.38f, 0.26f), Screen = new Color(0.12f, 0.14f, 0.18f),
            Paper = new Color(0.96f, 0.95f, 0.88f);
        private static readonly Color[] Rugs = { new Color(0.6f, 0.2f, 0.22f), new Color(0.24f, 0.36f, 0.55f), new Color(0.55f, 0.48f, 0.3f), new Color(0.3f, 0.45f, 0.35f) };
        private static readonly Color[] Sofas = { new Color(0.32f, 0.42f, 0.6f), new Color(0.45f, 0.3f, 0.28f), new Color(0.38f, 0.38f, 0.4f), new Color(0.3f, 0.45f, 0.38f) };
        private static readonly Color[] Bedding = { new Color(0.85f, 0.85f, 0.9f), new Color(0.7f, 0.78f, 0.88f), new Color(0.88f, 0.76f, 0.7f), new Color(0.78f, 0.84f, 0.74f) };

        private sealed class Furnisher
        {
            public Transform Parent; public InteriorPlan Plan; public Rect Area; public System.Random Rng;
            /// <summary>Plan-local areas kept walkable (doorways, stairs, entrance): furniture there gets no collider.</summary>
            public List<Rect> Keep;
            public float W => Area.width;
            public float D => Area.height;

            /// <summary>A piece of furniture at room-local (u, v) with size (w, h).</summary>
            public void Item(float u, float v, float w, float h, Color c, int order = 4, bool shadow = true, bool solid = true)
            {
                if (w <= 0.05f || h <= 0.05f) return;
                u = Mathf.Clamp(u, 0.15f, Mathf.Max(0.15f, W - w - 0.15f));
                v = Mathf.Clamp(v, 0.15f, Mathf.Max(0.15f, D - h - 0.15f));
                var rect = new Rect(Area.xMin + u, Area.yMin + v, w, h);
                if (shadow) Quad(Parent, Plan, new Rect(rect.x + 0.12f, rect.y - 0.12f, w, h), new Color(0, 0, 0, 0.22f), Sorting2D.Props, order - 1, "Shadow");
                Transform t = Quad(Parent, Plan, rect, c, Sorting2D.Props, order, solid ? "Furniture" : "Decor");
                if (!solid || Blocks(rect)) return;
                t.gameObject.AddComponent<BoxCollider2D>().size = Vector2.one;   // static: no rigidbody
            }
            private bool Blocks(Rect r)
            {
                if (Keep != null) foreach (var k in Keep) if (k.Overlaps(r)) return true;
                return false;
            }
            /// <summary>Flat decoration (rug, mat, floor marking): no shadow, no collision, under the furniture.</summary>
            public void Flat(float u, float v, float w, float h, Color c) => Item(u, v, w, h, c, 2, false, false);
            /// <summary>Detail drawn on top of a piece (screen, pillow, books): the piece below already collides.</summary>
            public void Top(float u, float v, float w, float h, Color c) => Item(u, v, w, h, c, 6, false, false);
            public Color Pick(Color[] set) => set[Rng.Next(set.Length)];
            public bool Chance(float p) => Rng.NextDouble() < p;

            public void Plant(float u, float v, float size = 0.6f)
            {
                Item(u, v, size, size, Pot, 4);
                Top(u + size * 0.15f, v + size * 0.15f, size * 0.7f, size * 0.7f, Leaf);
                Top(u + size * 0.32f, v + size * 0.32f, size * 0.36f, size * 0.36f, Leaf * 1.25f);
            }
            public void Chair(float u, float v, Color c)
            {
                Item(u, v, 0.5f, 0.5f, c, 5);
                Top(u + 0.06f, v + 0.34f, 0.38f, 0.12f, c * 0.75f);
            }
            public void Desk(float u, float v, bool computer = true)
            {
                Item(u, v, 1.5f, 0.75f, Wood);
                if (computer) { Top(u + 0.45f, v + 0.45f, 0.6f, 0.12f, Screen); Top(u + 0.4f, v + 0.12f, 0.5f, 0.18f, Metal * 0.8f); }
                else Top(u + 0.2f, v + 0.2f, 0.4f, 0.3f, Paper);
                Chair(u + 0.5f, v - 0.55f, Metal * 0.6f);
            }
            public void Table(float u, float v, float w, float h, int seats, Color chair)
            {
                Item(u, v, w, h, Wood);
                for (int i = 0; i < seats; i++)
                {
                    float t = (i / 2 + 0.5f) / Mathf.Max(1, (seats + 1) / 2);
                    if (i % 2 == 0) Chair(u + w * t - 0.25f, v - 0.6f, chair);
                    else Chair(u + w * t - 0.25f, v + h + 0.1f, chair);
                }
            }
            public void Shelf(float u, float v, float w, float h, Color c)
            {
                Item(u, v, w, h, c);
                bool along = w > h;
                int n = Mathf.Max(2, Mathf.RoundToInt((along ? w : h) / 0.35f));
                for (int i = 0; i < n; i++)
                {
                    Color book = Color.HSVToRGB((float)Rng.NextDouble(), 0.45f, 0.6f);
                    if (along) Top(u + 0.05f + i * (w - 0.1f) / n, v + 0.08f, (w - 0.1f) / n - 0.04f, h - 0.16f, book);
                    else Top(u + 0.08f, v + 0.05f + i * (h - 0.1f) / n, w - 0.16f, (h - 0.1f) / n - 0.04f, book);
                }
            }
        }

        private List<Rect> _keep;

        /// <summary>Doorways (both sides), the entrance and the stairs/lift stay clear of solid furniture.</summary>
        private static List<Rect> WalkwaysOf(InteriorPlan p, int floor)
        {
            var keep = new List<Rect>();
            foreach (var room in p.Rooms)
            {
                if (!room.HasDoor) continue;
                Vector2 a = room.DoorA, b = room.DoorB;
                if (Mathf.Abs(a.x - b.x) < 0.01f) keep.Add(new Rect(a.x - 1.5f, Mathf.Min(a.y, b.y) - 0.15f, 3f, Mathf.Abs(b.y - a.y) + 0.3f));
                else keep.Add(new Rect(Mathf.Min(a.x, b.x) - 0.15f, a.y - 1.5f, Mathf.Abs(b.x - a.x) + 0.3f, 3f));
            }
            if (floor == 0) keep.Add(new Rect(p.Hall.center.x - 1.6f, 0f, 3.2f, 2.5f));
            if (p.HasStairs) keep.Add(new Rect(p.Stairs.x - 1.6f, p.Stairs.y - 2.6f, 3.2f, 4.2f));
            if (p.HasElevator) keep.Add(new Rect(p.Elevator.x - 1.2f, p.Elevator.y - 2.2f, 2.4f, 3.4f));
            return keep;
        }

        private void Furnish(Transform parent, InteriorPlan p, InteriorPlan.Room room)
        {
            var f = new Furnisher { Parent = parent, Plan = p, Area = room.Area, Keep = _keep, Rng = new System.Random(_seed ^ (int)(room.Area.x * 73 + room.Area.y * 131)) };
            float W = f.W, D = f.D;
            switch (room.Kind)
            {
                case "unit":
                {
                    // Living area at the front, bedroom at the back, kitchenette and a bathroom corner.
                    Color rug = f.Pick(Rugs), sofa = f.Pick(Sofas);
                    f.Flat(W * 0.45f, 0.5f, Mathf.Min(2.6f, W * 0.45f), Mathf.Min(2.2f, D * 0.35f), rug);
                    f.Item(W - 2.5f, 0.35f, 2.1f, 0.85f, sofa);                                  // sofa
                    f.Top(W - 2.4f, 0.4f, 1.9f, 0.25f, sofa * 0.8f);                             // backrest
                    f.Item(W - 2.1f, 1.5f, 1.2f, 0.6f, DarkWood);                                // coffee table
                    f.Top(W - 1.7f, 1.65f, 0.3f, 0.3f, Paper);                                   // magazine
                    f.Item(W - 2.0f, Mathf.Min(D * 0.45f, 3.2f), 1.4f, 0.25f, Screen);           // TV on a stand
                    f.Item(W - 2.1f, Mathf.Min(D * 0.45f, 3.2f) + 0.25f, 1.6f, 0.4f, DarkWood);
                    // Bed with pillows and a nightstand.
                    Color bed = f.Pick(Bedding);
                    f.Item(0.3f, D - 2.4f, 1.6f, 2.1f, bed);
                    f.Top(0.35f, D - 0.75f, 0.68f, 0.4f, White);
                    f.Top(0.97f, D - 0.75f, 0.68f, 0.4f, White);
                    f.Top(0.3f, D - 2.4f, 1.6f, 0.9f, bed * 0.85f);                              // duvet fold
                    f.Item(2.05f, D - 0.75f, 0.45f, 0.45f, Wood);                                // nightstand
                    f.Top(2.15f, D - 0.65f, 0.25f, 0.25f, new Color(1f, 0.9f, 0.6f));            // lamp
                    if (W > 4.5f) f.Item(2.7f, D - 0.6f, 1.2f, 0.45f, DarkWood);                 // dresser
                    // Kitchenette along the far wall: counter, sink, stove, fridge, and a small table.
                    if (D > 5f)
                    {
                        f.Item(W - 0.75f, D - 3.4f, 0.6f, 2.4f, new Color(0.8f, 0.8f, 0.78f));
                        f.Top(W - 0.65f, D - 3.1f, 0.4f, 0.5f, Metal);                           // sink
                        f.Top(W - 0.67f, D - 2.3f, 0.44f, 0.44f, Screen);                        // hob
                        f.Item(W - 0.8f, D - 0.95f, 0.65f, 0.75f, White);                        // fridge
                        f.Table(W - 2.4f, D - 2.6f, 0.9f, 0.9f, 2, Wood * 0.8f);
                    }
                    // Bathroom nook in the back corner by the hall.
                    if (W > 5f && D > 6f)
                    {
                        f.Flat(0.2f, D * 0.45f, 1.6f, 1.4f, new Color(0.85f, 0.88f, 0.9f));
                        f.Item(0.3f, D * 0.45f + 0.15f, 0.5f, 0.6f, White);                      // toilet
                        f.Item(0.95f, D * 0.45f + 0.15f, 0.7f, 1.1f, White);                     // tub
                        f.Top(1.05f, D * 0.45f + 0.25f, 0.5f, 0.9f, new Color(0.7f, 0.82f, 0.9f));
                    }
                    if (f.Chance(0.7f)) f.Plant(W - 0.7f, 0.25f);
                    if (f.Chance(0.5f)) f.Shelf(0.25f, 0.3f, 0.4f, Mathf.Min(1.6f, D * 0.3f), DarkWood);
                    break;
                }
                case "office": case "sergeant": case "detectives":
                {
                    int desks = room.Kind == "detectives" ? 4 : room.Kind == "sergeant" ? 1 : 1 + f.Rng.Next(3);
                    for (int i = 0; i < desks; i++) f.Desk(0.6f + (i % 2) * (W * 0.5f), 1.0f + (i / 2) * 2.4f);
                    f.Shelf(W - 0.6f, D - 2.2f, 0.45f, 1.8f, Metal);                             // filing cabinets
                    f.Plant(0.3f, D - 0.9f);
                    if (room.Kind == "detectives") { f.Item(0.3f, D - 0.4f, Mathf.Min(2.4f, W - 1.2f), 0.15f, White); f.Top(0.5f, D - 0.38f, 0.9f, 0.1f, new Color(0.8f, 0.2f, 0.2f)); } // case board
                    if (room.Kind == "sergeant") { f.Flat(W * 0.3f, D * 0.4f, 1.8f, 1.4f, f.Pick(Rugs)); f.Chair(W * 0.3f + 0.3f, 2.2f, Fabric); f.Chair(W * 0.3f + 1.1f, 2.2f, Fabric); }
                    break;
                }
                case "meeting": case "briefing":
                {
                    if (room.Kind == "briefing")
                    {
                        // Rows of seats facing a lectern and a whiteboard.
                        f.Item(W * 0.5f - 1.2f, D - 0.4f, 2.4f, 0.15f, White);
                        f.Item(W * 0.5f - 0.3f, D - 1.3f, 0.6f, 0.5f, DarkWood);
                        for (float v = 0.8f; v < D - 2.2f; v += 1.1f)
                            for (float u = 0.6f; u < W - 0.8f; u += 0.8f) f.Chair(u, v, Fabric);
                    }
                    else
                    {
                        f.Table(W * 0.5f - 0.9f, 1.2f, 1.8f, Mathf.Max(1f, D - 2.6f), 6, Fabric);
                        f.Item(0.3f, D * 0.5f - 0.7f, 0.12f, 1.4f, White);                       // screen on the wall
                        f.Plant(W - 0.8f, D - 0.8f);
                    }
                    break;
                }
                case "interview":
                    f.Item(W * 0.5f - 0.6f, D * 0.5f - 0.4f, 1.2f, 0.8f, Metal);
                    f.Chair(W * 0.5f - 0.25f, D * 0.5f - 1.0f, Metal * 0.7f);
                    f.Chair(W * 0.5f - 0.25f, D * 0.5f + 0.5f, Metal * 0.7f);
                    f.Item(0.25f, D - 0.4f, Mathf.Min(1.6f, W - 0.5f), 0.12f, new Color(0.15f, 0.18f, 0.22f)); // one-way mirror
                    break;
                case "reception": case "frontdesk":
                    f.Flat(0.4f, 0.4f, W - 0.8f, 1.2f, room.Kind == "frontdesk" ? new Color(0.2f, 0.25f, 0.45f) : f.Pick(Rugs));
                    f.Item(0.6f, D * 0.5f, Mathf.Min(3f, W - 1.2f), 0.8f, Wood);                 // counter
                    f.Top(1.0f, D * 0.5f + 0.5f, 0.6f, 0.12f, Screen);
                    if (room.Kind == "frontdesk") f.Item(0.4f, D * 0.5f + 0.9f, Mathf.Min(3.4f, W - 0.8f), 0.08f, new Color(0.7f, 0.85f, 0.95f)); // glass screen
                    for (float u = 0.6f; u < W - 0.8f; u += 0.7f) f.Chair(u, 0.25f, Fabric);      // waiting seats
                    f.Plant(W - 0.8f, D - 0.8f);
                    break;
                case "sales":
                {
                    f.Item(0.6f, D * 0.5f, Mathf.Min(3f, W - 1.2f), 0.8f, Wood);                 // till counter
                    f.Top(0.8f, D * 0.5f + 0.2f, 0.5f, 0.4f, Screen);
                    int aisles = Mathf.Max(2, Mathf.FloorToInt(W / 2.2f));
                    for (int i = 0; i < aisles; i++)
                        f.Shelf(0.5f + i * (W - 1f) / aisles, 1.4f, 0.55f, Mathf.Max(1.2f, D * 0.32f), Metal);
                    f.Flat(W * 0.5f - 0.8f, 0.2f, 1.6f, 0.6f, new Color(0.25f, 0.25f, 0.28f));  // door mat
                    break;
                }
                case "dining":
                    for (float u = 0.9f; u < W - 1.6f; u += 2.6f)
                        for (float v = 1.6f; v < D - 1.6f; v += 2.6f) f.Table(u, v, 1f, 1f, 4, DarkWood);
                    f.Item(W - 3.2f, D - 0.9f, 2.8f, 0.7f, DarkWood);                            // bar
                    f.Plant(0.3f, 0.3f); f.Plant(W - 0.9f, 0.3f);
                    break;
                case "kitchen":
                    f.Item(0.3f, D - 0.9f, W - 0.6f, 0.7f, Metal);
                    for (float u = 0.6f; u < W - 1.2f; u += 1.4f) f.Top(u, D - 0.75f, 0.5f, 0.4f, Screen);   // hobs
                    f.Item(W * 0.5f - 1f, D * 0.4f, 2f, 0.9f, new Color(0.75f, 0.76f, 0.78f));  // prep island
                    f.Item(0.3f, 0.3f, 0.8f, 0.8f, White);                                       // fridge
                    break;
                case "stock":
                    for (float u = 0.4f; u < W - 1f; u += 1.8f) f.Shelf(u, 0.6f, 0.6f, Mathf.Max(0.8f, D - 1.4f), new Color(0.6f, 0.48f, 0.3f));
                    for (int i = 0; i < 3; i++) f.Item(W - 1.2f - i * 0.2f, 0.4f + i * 0.7f, 0.6f, 0.6f, new Color(0.72f, 0.58f, 0.38f)); // boxes
                    break;
                case "cell":
                    f.Item(0.3f, D - 0.9f, Mathf.Min(1.8f, W - 0.6f), 0.6f, Metal);              // bunk
                    f.Top(0.35f, D - 0.85f, 0.5f, 0.5f, new Color(0.6f, 0.62f, 0.66f));          // thin pillow
                    f.Item(W - 0.8f, 0.4f, 0.5f, 0.5f, White);                                   // toilet
                    f.Item(W - 0.8f, 1.1f, 0.4f, 0.35f, White);                                  // sink
                    break;
                case "locker":
                    f.Shelf(0.2f, 0.4f, 0.5f, D - 0.8f, new Color(0.3f, 0.36f, 0.48f));
                    f.Shelf(W - 0.7f, 0.4f, 0.5f, D - 0.8f, new Color(0.3f, 0.36f, 0.48f));
                    f.Item(W * 0.5f - 0.3f, 1f, 0.6f, D - 2f, Wood);                             // bench
                    break;
                case "processing":
                    f.Desk(0.5f, 0.6f);
                    f.Item(W - 1.4f, D - 1.4f, 1.0f, 1.0f, Metal); f.Top(W - 1.2f, D - 1.2f, 0.6f, 0.6f, Screen); // fingerprint station
                    f.Item(W * 0.5f - 0.5f, D - 0.3f, 1.0f, 0.15f, White);                       // height chart
                    f.Item(0.3f, D - 1.2f, 1.6f, 0.5f, Metal * 0.8f);                            // bench with cuff rail
                    break;
                case "restroom":
                    f.Flat(0.2f, 0.2f, W - 0.4f, D - 0.4f, new Color(0.85f, 0.88f, 0.9f));
                    for (float u = 0.3f; u < W - 0.6f; u += 1.2f) { f.Item(u, D - 0.8f, 0.6f, 0.6f, White); f.Top(u + 0.1f, D - 0.7f, 0.4f, 0.3f, new Color(0.8f, 0.85f, 0.9f)); }
                    f.Item(0.3f, 0.3f, Mathf.Min(1.8f, W - 0.6f), 0.5f, White);                  // sinks
                    break;
                case "breakroom":
                    f.Item(0.3f, D - 0.9f, W - 0.6f, 0.6f, new Color(0.8f, 0.8f, 0.78f));
                    f.Top(0.6f, D - 0.8f, 0.4f, 0.4f, Screen);                                   // coffee machine
                    f.Item(W - 0.9f, D - 1.0f, 0.65f, 0.75f, White);                             // fridge
                    f.Table(W * 0.5f - 0.6f, 1.4f, 1.2f, 1.2f, 4, Fabric);
                    f.Item(0.3f, 0.3f, 1.8f, 0.8f, f.Pick(Sofas));
                    break;
            }
        }

        /// <summary>Hallway dressing: a runner, ceiling lights, plants; a lobby with mailboxes on the ground floor.</summary>
        private void DressHall(Transform root, InteriorPlan p, int floor)
        {
            Rect h = p.Hall;
            var f = new Furnisher { Parent = root, Plan = p, Area = h, Keep = _keep, Rng = new System.Random(_seed + floor * 17) };
            float runEnd = p.HasStairs ? h.height - 3.4f : h.height - 0.4f;
            if (runEnd > 1.5f) f.Flat(h.width * 0.5f - 0.5f, 0.4f, 1f, runEnd - 0.4f, Type == "police" ? new Color(0.22f, 0.27f, 0.42f) : new Color(0.5f, 0.24f, 0.22f));
            for (float v = 2f; v < runEnd; v += 4f) f.Top(h.width * 0.5f - 0.15f, v, 0.3f, 0.3f, new Color(1f, 0.95f, 0.75f, 0.85f)); // ceiling lights
            if (floor == 0)
            {
                f.Flat(h.width * 0.5f - 0.8f, 0.2f, 1.6f, 0.7f, new Color(0.25f, 0.25f, 0.28f));          // entrance mat
                if (Type == "apartment") f.Shelf(0.15f, 1.2f, 0.3f, Mathf.Min(2f, runEnd - 1.4f), new Color(0.55f, 0.55f, 0.6f)); // mailboxes
                if (Type == "police") f.Item(h.width - 0.45f, 1.0f, 0.3f, 1.2f, new Color(0.25f, 0.3f, 0.55f));   // notice board
            }
            if (runEnd > 5f) f.Plant(h.width - 0.75f, runEnd - 0.9f, 0.55f);
        }

        // ---- people --------------------------------------------------------------------------
        private void SpawnResident(Transform parent, InteriorPlan p, InteriorPlan.Room room)
        {
            var sprites = WorldSprites2D.Instance;
            if (sprites == null) return;
            if (!_residentsLoaded)
            {
                _residentsLoaded = true;
                if (Services.TryGet(out IRecordsDirectory records)) records.ResidentsOf(BuildingId, _residentIds, _residentLooks);
            }
            if (_residentCursor >= _residentIds.Count) return;
            int i = _residentCursor++;
            if (((_seed + i) & 3) == 0) return;   // about a quarter are out
            int look = _residentLooks[i];
            Sprite sprite = look >= 0 && look < sprites.Civilians.Length ? sprites.Civilians[look] : null;
            Vector2 pos = p.ToWorld(room.Area.center);
            var ped = sprites.SpawnPed(pos, Random.Range(0f, 360f), sprite, sprite != null ? look : -1);
            ped.CitizenId = _residentIds[i];
            ped.transform.SetParent(parent, true);
            ped.Idle();
        }
    }
}
