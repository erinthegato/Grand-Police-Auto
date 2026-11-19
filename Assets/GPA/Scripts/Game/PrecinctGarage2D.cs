using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>
    /// The 1st Precinct's basement garage.
    /// - Outside: an animated roll-up garage door in the precinct's side wall. It rolls up when the
    ///   officer (on foot or driving) comes near and rolls down again after they leave. Driving or
    ///   walking into the open door takes you down the ramp into the basement.
    /// - Inside the precinct: the ground-floor stairs also lead down to the basement.
    /// - The basement: a lit parking level with the fleet parked in bays, the vehicle sign-out desk,
    ///   stairs back up into the station and the ramp back out to the street.
    /// The basement is built away from the city map (like a separate level) and the player is moved
    /// between it and the street through the door, ramp and stairs.
    /// </summary>
    public sealed class PrecinctGarage2D : MonoBehaviour
    {
        public static PrecinctGarage2D Instance { get; private set; }
        /// <summary>True while the officer (or their car) is in the basement.</summary>
        public bool InBasement { get; private set; }

        // Basement level, in world metres (well away from the city).
        private static readonly Vector2 B0 = new Vector2(-3000f, 0f);
        private const float BW = 46f, BH = 30f;
        private const float LaneY0 = 10.5f, LaneY1 = 19.5f;   // ramp / driving lane (basement local)

        private CityLocationDatabase.Building _precinct;
        private Officer2D _officer;
        private InteriorManager2D _interiors;
        private Vector2 _door, _out, _along;          // door centre on the wall, outward normal, along the wall
        private const float DoorWidth = 5.2f;
        private float _open;                           // 0 closed .. 1 fully rolled up
        private float _cooldown = -10f;
        private Transform _shutter, _doorRoot;
        private SpriteRenderer _mouth;
        private readonly List<SpriteRenderer> _slats = new List<SpriteRenderer>();
        private BuildingInterior2D _stairsAddedTo;
        private GUIStyle _tag;
        private AudioSource _motor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<PrecinctGarage2D>() != null || FindAnyObjectByType<Officer2D>() == null || FindAnyObjectByType<CityLocations>() == null) return;
            new GameObject("Precinct Garage").AddComponent<PrecinctGarage2D>();
        }

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; DayNight2D.Suppressed = false; }

        private void Start()
        {
            _officer = FindAnyObjectByType<Officer2D>();
            _interiors = FindAnyObjectByType<InteriorManager2D>();
            var db = FindAnyObjectByType<CityLocations>()?.Database;
            if (db != null) _precinct = db.Buildings.Find(b => b.Type == "police");
            if (_precinct == null) { enabled = false; return; }

            PickDoorWall();
            BuildDoor();
            BuildBasement();

            // Fleet vehicles signed out in the basement appear in a bay, pointing at the ramp.
            if (FleetGarage2D.Instance != null)
                FleetGarage2D.Instance.SpawnOverride = spec => InBasement ? ((Vector2, float)?)(B0 + new Vector2(24f, 15f), 90f) : null;
        }

        // ------------------------------------------------------------------ outside: the door

        /// <summary>Side wall (not the front) nearest a road lane, so the car can drive straight in.</summary>
        private void PickDoorWall()
        {
            Rect f = _precinct.Footprint;
            Vector2 front = InteriorManager2D.DoorPoint(_precinct);
            var sides = new List<(Vector2 centre, Vector2 normal)>
            {
                (new Vector2(f.xMin, f.center.y), Vector2.left), (new Vector2(f.xMax, f.center.y), Vector2.right),
                (new Vector2(f.center.x, f.yMin), Vector2.down), (new Vector2(f.center.x, f.yMax), Vector2.up),
            };
            float best = float.MaxValue;
            foreach (var (c, n) in sides)
            {
                if (Vector2.Distance(c, front) < 1f || Vector2.Dot(n, (front - f.center).normalized) > 0.7f) continue;   // not the front
                float d = LaneDistance(c + n * 3f);
                if (d < best) { best = d; _door = c; _out = n; }
            }
            _along = new Vector2(-_out.y, _out.x);
        }

        private static float LaneDistance(Vector2 p)
        {
            var g = RoadGraph2D.Instance;
            if (g == null) return 0f;
            float best = float.MaxValue;
            foreach (int e in g.LaneEdges())
            {
                Vector2 a = g.Nodes[g.Edges[e].From].Position, b = g.Nodes[g.Edges[e].To].Position;
                Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        private void BuildDoor()
        {
            var root = new GameObject("Garage Door").transform;
            _doorRoot = root;
            root.SetParent(transform, false);
            root.position = _door;
            root.rotation = Quaternion.FromToRotation(Vector3.up, _out);   // local up = outward, local right = along the wall

            // The dark ramp mouth behind the door (visible once it rolls up), drawn over the roof.
            _mouth = Quad(root, new Vector2(0f, -1.6f), new Vector2(DoorWidth, 3.2f), new Color(0.06f, 0.06f, 0.07f), Sorting2D.Roofs, 20);
            Quad(root, new Vector2(0f, -2.8f), new Vector2(DoorWidth, 0.8f), new Color(0.02f, 0.02f, 0.02f), Sorting2D.Roofs, 21);
            // Frame: posts at each side and a header.
            Quad(root, new Vector2(-DoorWidth * 0.5f - 0.2f, -0.2f), new Vector2(0.4f, 0.9f), new Color(0.3f, 0.32f, 0.36f), Sorting2D.Roofs, 25);
            Quad(root, new Vector2(DoorWidth * 0.5f + 0.2f, -0.2f), new Vector2(0.4f, 0.9f), new Color(0.3f, 0.32f, 0.36f), Sorting2D.Roofs, 25);
            Quad(root, new Vector2(0f, -0.45f), new Vector2(DoorWidth + 0.8f, 0.35f), new Color(0.24f, 0.26f, 0.3f), Sorting2D.Roofs, 26);
            // Hazard stripes on the kerb in front.
            for (int i = 0; i < 6; i++)
            {
                var s = Quad(root, new Vector2(-DoorWidth * 0.5f + 0.45f + i * 0.86f, 0.35f), new Vector2(0.4f, 0.3f), new Color(0.95f, 0.75f, 0.1f), Sorting2D.Props, 12);
                s.transform.localRotation = Quaternion.Euler(0, 0, 35f);
            }
            // "POLICE VEHICLES ONLY" paint on the apron.
            Quad(root, new Vector2(0f, 1.6f), new Vector2(DoorWidth - 0.6f, 0.12f), new Color(1f, 1f, 1f, 0.7f), Sorting2D.Props, 12);

            // The shutter: a slatted panel that rolls up into the header (scales toward it).
            _shutter = new GameObject("Shutter").transform;
            _shutter.SetParent(root, false);
            _shutter.localPosition = new Vector2(0f, -0.3f);
            for (int i = 0; i < 9; i++)
            {
                var slat = Quad(_shutter, new Vector2(0f, -0.2f - i * 0.36f), new Vector2(DoorWidth, 0.3f),
                    i % 2 == 0 ? new Color(0.62f, 0.64f, 0.68f) : new Color(0.55f, 0.57f, 0.61f), Sorting2D.Roofs, 22);
                _slats.Add(slat);
            }

            _motor = gameObject.AddComponent<AudioSource>();
            _motor.playOnAwake = false; _motor.loop = true; _motor.spatialBlend = 0f; _motor.volume = 0.25f;
            _motor.clip = MotorHum();
        }

        // ------------------------------------------------------------------ the basement level

        private void BuildBasement()
        {
            var root = new GameObject("Basement Garage").transform;
            root.SetParent(transform, false);
            // Surroundings: solid rock, so nothing outside the walls shows.
            Quad(root, B0 + new Vector2(BW * 0.5f, BH * 0.5f), new Vector2(BW + 80f, BH + 80f), new Color(0.05f, 0.05f, 0.06f), Sorting2D.Ground, -20, world: true);
            Quad(root, B0 + new Vector2(BW * 0.5f, BH * 0.5f), new Vector2(BW, BH), new Color(0.42f, 0.43f, 0.45f), Sorting2D.Ground, 0, world: true);
            // Driving lane, slightly darker and oil-stained.
            Quad(root, B0 + new Vector2(BW * 0.5f, (LaneY0 + LaneY1) * 0.5f), new Vector2(BW, LaneY1 - LaneY0), new Color(0.36f, 0.37f, 0.39f), Sorting2D.Ground, 1, world: true);
            var rng = new System.Random(4127);
            for (int i = 0; i < 14; i++)
                Quad(root, B0 + new Vector2(6f + (float)rng.NextDouble() * (BW - 10f), LaneY0 + 1f + (float)rng.NextDouble() * 7f), Vector2.one * (0.6f + (float)rng.NextDouble()), new Color(0.2f, 0.2f, 0.22f, 0.35f), Sorting2D.Ground, 2, world: true);
            // Direction arrows in the lane.
            for (float x = 12f; x < BW - 6f; x += 10f)
            {
                Quad(root, B0 + new Vector2(x, 13f), new Vector2(2.2f, 0.25f), new Color(1f, 1f, 1f, 0.75f), Sorting2D.Ground, 3, world: true);
                Quad(root, B0 + new Vector2(x, 17f), new Vector2(2.2f, 0.25f), new Color(1f, 1f, 1f, 0.75f), Sorting2D.Ground, 3, world: true);
            }

            // The ramp up to the street at the west end.
            Quad(root, B0 + new Vector2(3.5f, 15f), new Vector2(7f, LaneY1 - LaneY0), new Color(0.5f, 0.5f, 0.52f), Sorting2D.Ground, 2, world: true);
            for (int i = 0; i < 8; i++)
                Quad(root, B0 + new Vector2(0.6f + i * 0.85f, 15f), new Vector2(0.18f, LaneY1 - LaneY0), new Color(0.38f, 0.38f, 0.4f), Sorting2D.Ground, 3, world: true);
            Quad(root, B0 + new Vector2(0.4f, 15f), new Vector2(0.8f, LaneY1 - LaneY0), new Color(0.85f, 0.85f, 0.75f, 0.9f), Sorting2D.Ground, 4, world: true);   // daylight at the top
            Label(root, B0 + new Vector2(8.5f, 20.6f), "EXIT ↑ STREET");

            // Parking bays: a row along each long wall.
            var fleet = new List<VehicleSpec>();
            if (VehicleCatalog.Instance != null) foreach (var v in VehicleCatalog.Instance.Fleet()) if (!v.Helicopter && v.Sprites != null && v.Sprites.Length > 0) fleet.Add(v);
            int bay = 0;
            for (float x = 10f; x + 3.2f <= BW - 6f; x += 3.4f, bay++)
            {
                foreach (bool top in new[] { false, true })
                {
                    float y0 = top ? LaneY1 : 0f, y1 = top ? BH : LaneY0;
                    Quad(root, B0 + new Vector2(x, (y0 + y1) * 0.5f), new Vector2(0.14f, y1 - y0 - 1f), new Color(0.95f, 0.95f, 0.9f, 0.85f), Sorting2D.Ground, 3, world: true);
                    // Wheel stop at the back of the bay.
                    Quad(root, B0 + new Vector2(x + 1.7f, top ? BH - 1.2f : 1.2f), new Vector2(1.8f, 0.3f), new Color(0.85f, 0.75f, 0.2f), Sorting2D.Props, 3, world: true);
                    // About two thirds of the bays hold a parked fleet car.
                    if (fleet.Count > 0 && (bay * 7 + (top ? 3 : 0)) % 3 != 0)
                        ParkedCar(root, fleet[(bay + (top ? 2 : 0)) % fleet.Count], B0 + new Vector2(x + 1.7f, top ? BH - 4.4f : 4.4f), top ? 0f : 180f, rng);
                }
            }
            // Concrete pillars between the bays and the lane.
            for (float x = 13.4f; x < BW - 6f; x += 10.2f)
            {
                Solid(root, B0 + new Vector2(x, LaneY0 - 0.6f), new Vector2(0.9f, 0.9f), new Color(0.6f, 0.6f, 0.62f), Sorting2D.Props, 10);
                Solid(root, B0 + new Vector2(x, LaneY1 + 0.6f), new Vector2(0.9f, 0.9f), new Color(0.6f, 0.6f, 0.62f), Sorting2D.Props, 10);
            }

            // Walls (the ramp end is open; an invisible stop sits past the top of the ramp).
            Color wall = new Color(0.22f, 0.23f, 0.26f);
            Solid(root, B0 + new Vector2(BW * 0.5f, -0.4f), new Vector2(BW + 0.8f, 0.8f), wall, Sorting2D.Props, 8);
            Solid(root, B0 + new Vector2(BW * 0.5f, BH + 0.4f), new Vector2(BW + 0.8f, 0.8f), wall, Sorting2D.Props, 8);
            Solid(root, B0 + new Vector2(BW + 0.4f, BH * 0.5f), new Vector2(0.8f, BH + 0.8f), wall, Sorting2D.Props, 8);
            Solid(root, B0 + new Vector2(-0.4f, LaneY0 * 0.5f), new Vector2(0.8f, LaneY0), wall, Sorting2D.Props, 8);
            Solid(root, B0 + new Vector2(-0.4f, (LaneY1 + BH) * 0.5f), new Vector2(0.8f, BH - LaneY1), wall, Sorting2D.Props, 8);
            Solid(root, B0 + new Vector2(-2.5f, 15f), new Vector2(1f, LaneY1 - LaneY0 + 2f), new Color(0, 0, 0, 0), Sorting2D.Props, 0);

            // Ceiling strip lights.
            for (float x = 6f; x < BW; x += 8f)
            for (float y = 5f; y < BH; y += 10f)
                Quad(root, B0 + new Vector2(x, y), new Vector2(3.2f, 0.35f), new Color(1f, 1f, 0.92f, 0.95f), Sorting2D.Roofs, 30, world: true);

            // Stairs up into the station, at the east end.
            Vector2 stairs = B0 + new Vector2(BW - 2.2f, 15f);
            Quad(root, stairs, new Vector2(3f, 4.4f), new Color(0.4f, 0.4f, 0.42f), Sorting2D.Props, 2, world: true);
            for (int i = 0; i < 8; i++)
                Quad(root, stairs + new Vector2(-1.2f + i * 0.34f, 0f), new Vector2(0.08f, 4.4f), new Color(0.26f, 0.26f, 0.28f), Sorting2D.Props, 3, world: true);
            Label(root, stairs + new Vector2(-1f, 3f), "STAIRS ↑ STATION");
            var up = new GameObject("Basement Stairs").AddComponent<Interactable2D>();
            up.transform.SetParent(root, false);
            up.transform.position = stairs + new Vector2(-1.8f, 0f);
            up.Prompt = "Go upstairs (1st Precinct, ground floor)";
            up.Range = 2f;
            up.Used += _ => GoUpstairs();

            // Vehicle sign-out desk by the stairs.
            Vector2 desk = B0 + new Vector2(BW - 7f, LaneY1 + 2.2f);
            Solid(root, desk, new Vector2(3f, 1.1f), new Color(0.36f, 0.24f, 0.15f), Sorting2D.Props, 10);
            Quad(root, desk + new Vector2(-0.6f, 0.1f), new Vector2(0.7f, 0.45f), new Color(0.12f, 0.14f, 0.18f), Sorting2D.Props, 11, world: true);
            Quad(root, desk + new Vector2(0f, -1.2f), new Vector2(1.6f, 1.6f), new Color(0.15f, 0.35f, 0.85f, 0.85f), Sorting2D.Props, 4, world: true);
            var signOut = new GameObject("Sign-out Desk").AddComponent<Interactable2D>();
            signOut.transform.SetParent(root, false);
            signOut.transform.position = desk + new Vector2(0f, -1.2f);
            signOut.Prompt = "Vehicle desk — sign out a fleet vehicle";
            signOut.Range = 2.2f;
            signOut.Used += _ => FleetGarage2D.Instance?.OpenMenu();
        }

        private void ParkedCar(Transform root, VehicleSpec spec, Vector2 pos, float heading, System.Random rng)
        {
            var go = new GameObject("Parked " + spec.DisplayName);
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, 0, heading));
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = spec.Sprites[rng.Next(spec.Sprites.Length)];
            Vector2 b = sr.sprite.bounds.size;
            go.transform.localScale = new Vector3(spec.Width / Mathf.Max(0.01f, b.x), spec.Length / Mathf.Max(0.01f, b.y), 1f);
            Sorting2D.Apply(sr, Sorting2D.Vehicles, 0);
            var col = go.AddComponent<BoxCollider2D>();
            col.size = b;
            go.isStatic = true;
        }

        // ------------------------------------------------------------------ moving between levels

        private void Update()
        {
            if (_officer == null) return;
            float dt = Time.deltaTime;
            _cooldown -= dt;
            Car2D car = _officer.CurrentCar;
            Vector2 p = car != null ? (Vector2)car.transform.position : (Vector2)_officer.transform.position;
            Vector2 vel = car != null ? (Vector2)car.Velocity : _officer.TryGetComponent(out Rigidbody2D rb) ? rb.linearVelocity : Vector2.zero;

            InBasement = p.x > B0.x - 20f && p.x < B0.x + BW + 20f && p.y > B0.y - 20f && p.y < B0.y + BH + 20f;
            DayNight2D.Suppressed = InBasement;

            // Inside the station the door art (drawn at roof height, reaching into the building)
            // would sit on top of the room behind that wall, so hide it and ignore the door.
            var inside = _interiors != null ? _interiors.Current : null;
            bool inStation = inside != null && inside.BuildingId == _precinct.Id;
            if (_doorRoot != null && _doorRoot.gameObject.activeSelf == inStation) _doorRoot.gameObject.SetActive(!inStation);
            if (inStation) { if (_motor.isPlaying) _motor.Stop(); return; }

            // The door rolls up for the officer when they are close outside (or have just driven out).
            Vector2 rel = p - _door;
            bool near = !InBasement && Vector2.Dot(rel, _out) > -0.5f && rel.magnitude < 10f && (car == null || !car.Hover);
            float target = near || _cooldown > -2f ? 1f : 0f;
            float before = _open;
            _open = Mathf.MoveTowards(_open, target, dt * 0.75f);
            bool moving = Mathf.Abs(_open - before) > 0.0001f;
            if (moving && !_motor.isPlaying) _motor.Play();
            else if (!moving && _motor.isPlaying) _motor.Stop();
            ApplyShutter();

            if (_cooldown > 0f) return;
            if (!InBasement)
            {
                // Into the open door: within the opening and heading into the wall.
                float across = Vector2.Dot(rel, _along), outward = Vector2.Dot(rel, _out);
                bool inOpening = Mathf.Abs(across) < DoorWidth * 0.5f - 0.3f && outward < (car != null ? 3.2f : 1.2f) && outward > -0.3f;
                bool heading = Vector2.Dot(vel, -_out) > 0.4f ||
                               (car != null ? Vector2.Dot(car.transform.up, -_out) > 0.7f && outward < 2.9f : Vector2.Dot(_officer.Facing, -_out) > 0.7f && outward < 0.9f);
                if (_open > 0.85f && inOpening && heading) GoDown(car);
            }
            else
            {
                // Up the ramp and out.
                Vector2 local = p - B0;
                bool westward = vel.x < -0.2f || (car != null ? car.transform.up.x < -0.6f : _officer.Facing.x < -0.6f);
                if (local.x < 1.6f && local.y > LaneY0 && local.y < LaneY1 && westward) GoOut(car);
            }
        }

        private void ApplyShutter()
        {
            // Slats disappear into the header from the bottom up; the last slats compress as it rolls.
            float shown = (1f - _open) * _slats.Count;
            for (int i = 0; i < _slats.Count; i++)
            {
                float k = Mathf.Clamp01(shown - (_slats.Count - 1 - i));
                _slats[i].enabled = k > 0.01f;
                var t = _slats[i].transform;
                t.localPosition = new Vector3(0f, -0.2f - i * 0.36f * (1f - _open * 0.6f), 0f);
            }
            _shutter.localPosition = new Vector3(0f, -0.3f + _open * 0.15f, 0f);
            _mouth.color = Color.Lerp(new Color(0.06f, 0.06f, 0.07f, 0f), new Color(0.06f, 0.06f, 0.07f, 1f), _open);
        }

        private void GoDown(Car2D car)
        {
            _cooldown = 1f;
            if (car != null) Teleport(car, B0 + new Vector2(8.5f, 13f), -90f, 4f);   // eastbound, keep to the right
            else MoveOfficer(B0 + new Vector2(5f, 15f));
            GameAudio.UiClick();
        }

        private void GoOut(Car2D car)
        {
            _cooldown = 1.5f;
            _open = 1f;
            float heading = Vector2.SignedAngle(Vector2.up, _out);
            Vector2 apron = _door + _out * (car != null ? 4f : 2f);
            if (car != null) Teleport(car, apron, heading, 4f);
            else MoveOfficer(apron);
        }

        private void GoUpstairs()
        {
            var plan = BuildingInterior2D.PreviewPlan(_precinct, 0);
            Vector2 at = plan.ToWorld(plan.Stairs - new Vector2(0f, 2f));
            MoveOfficer(at);
            _interiors?.Open(_precinct);
        }

        private void GoDownstairs()
        {
            _interiors?.Close();
            MoveOfficer(B0 + new Vector2(BW - 4.2f, 15f));
        }

        private void Teleport(Car2D car, Vector2 pos, float heading, float speed)
        {
            var rb = car.GetComponent<Rigidbody2D>();
            Quaternion rot = Quaternion.Euler(0, 0, heading);
            car.transform.SetPositionAndRotation(pos, rot);
            if (rb != null)
            {
                rb.position = pos; rb.rotation = heading;
                rb.linearVelocity = (Vector2)(rot * Vector3.up) * speed;
                rb.angularVelocity = 0f;
            }
            Camera2DFollow.SnapAll();
        }

        private void MoveOfficer(Vector2 pos)
        {
            _officer.transform.position = pos;
            if (_officer.TryGetComponent(out Rigidbody2D rb)) { rb.position = pos; rb.linearVelocity = Vector2.zero; }
            Camera2DFollow.SnapAll();
        }

        // ------------------------------------------------------------------ station stairs

        private void LateUpdate()
        {
            // The precinct's ground-floor stairs also go down to the basement.
            var interior = _interiors != null ? _interiors.Current : null;
            if (interior == null || interior.BuildingId != _precinct.Id || interior == _stairsAddedTo) return;
            Transform floor0 = interior.FloorRoot(0);
            if (floor0 == null) return;
            var plan = interior.PlanFor(0);
            var go = new GameObject("BasementStairUse");
            go.transform.SetParent(floor0, false);
            go.transform.position = plan.ToWorld(plan.Stairs - new Vector2(0f, 0.7f));
            var it = go.AddComponent<Interactable2D>();
            it.Prompt = "Go downstairs (basement garage)";
            it.Range = 1.4f;
            it.Used += _ => GoDownstairs();
            _stairsAddedTo = interior;
        }

        private void OnGUI()
        {
            if (!InBasement) return;
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            if (_tag == null) _tag = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 22 };
            var r = new Rect(canvas.x * 0.5f - 300, 40, 600, 36);
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(r, "1st Precinct · Basement garage", _tag);
            GUI.matrix = old;
        }

        // ------------------------------------------------------------------ helpers

        private static SpriteRenderer Quad(Transform parent, Vector2 pos, Vector2 size, Color c, string layer, int order, bool world = false)
        {
            var go = new GameObject("Quad");
            go.transform.SetParent(parent, false);
            if (world) go.transform.position = pos; else go.transform.localPosition = pos;
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = BuildingInterior2D.Pixel;
            sr.color = c;
            Sorting2D.Apply(sr, layer, order);
            return sr;
        }

        private static void Solid(Transform parent, Vector2 pos, Vector2 size, Color c, string layer, int order)
        {
            var sr = Quad(parent, pos, size, c, layer, order, world: true);
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.size = sr.sprite.bounds.size;
            col.offset = sr.sprite.bounds.center;
        }

        private static void Label(Transform parent, Vector2 pos, string text)
        {
            var go = new GameObject("Sign " + text);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.text = text; tm.characterSize = 0.12f; tm.fontSize = 48; tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(1f, 0.85f, 0.2f);
            var mr = go.GetComponent<MeshRenderer>();
            if (tm.font != null) mr.sharedMaterial = tm.font.material;
            Sorting2D.Apply(mr, Sorting2D.Props, 20);
        }

        /// <summary>A low electric-motor hum for the roll-up door.</summary>
        private static AudioClip MotorHum()
        {
            const int rate = 22050; int n = rate;
            var data = new float[n];
            var r = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                data[i] = 0.35f * Mathf.Sin(2f * Mathf.PI * 95f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * 190f * t)
                        + 0.12f * ((float)r.NextDouble() * 2f - 1f) * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 9f * t));
            }
            var clip = AudioClip.Create("GarageDoorMotor", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
