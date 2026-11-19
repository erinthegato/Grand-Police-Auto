using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// SWAT call-out (SWAT Commander rank only): a barricaded suspect inside a building. You set up
    /// the perimeter at the front door; a SWAT truck drives in and its four-officer team stacks up on
    /// the door; you give the go and the team clears the building floor by floor on its own, opening
    /// doors room by room (you can follow them in and watch). They either bring the suspect out in
    /// cuffs, or flush them out the front, where you have to stop them.
    /// </summary>
    [CalloutType("swat_barricade")]
    public sealed class SwatScenario : CalloutScenario
    {
        private enum Stage { Respond, Perimeter, Staging, Stacked, Clearing, Runner, Done }

        private Stage _stage;
        private CityLocationDatabase.Building _building;
        private Vector2 _door, _outward;
        private int _targetFloor, _targetRoom;
        private float _timer;
        private Car2D _truck;
        private BackupDriver2D _truckDriver;
        private readonly List<Ped2D> _team = new List<Ped2D>();
        private Ped2D _suspect;
        private InteriorManager2D _interiors;
        // Clearing progress.
        private int _floor, _roomStep;
        private List<int> _rooms = new List<int>();

        public override float ArrivalRadius => 40f;

        public override bool Setup()
        {
            if (Context.Sprites == null) return false;
            var locations = Object.FindAnyObjectByType<CityLocations>();
            _interiors = Object.FindAnyObjectByType<InteriorManager2D>();
            if (locations == null || locations.Database == null || _interiors == null) return false;

            string[] types = string.IsNullOrEmpty(Definition.buildingType) ? new[] { "apartment" } : Definition.buildingType.Split(',');
            var options = new List<CityLocationDatabase.Building>();
            foreach (var b in locations.Database.Buildings)
            {
                if (System.Array.IndexOf(types, b.Type) < 0 || b.Footprint.width < 8f || b.Footprint.height < 8f) continue;
                float d = Vector2.Distance(b.Entrance, PlayerPosition);
                if (d >= Definition.minDistance && d <= Definition.maxDistance) options.Add(b);
            }
            if (options.Count == 0) return false;
            _building = options[Random.Range(0, options.Count)];
            _door = InteriorManager2D.DoorPoint(_building);
            _outward = (_door - _building.Footprint.center).normalized;

            int floors = InteriorPlan.FloorCountFor(_building.Type, _building.Floors);
            _targetFloor = Random.Range(0, floors);
            var plan = BuildingInterior2D.PreviewPlan(_building, _targetFloor);
            _targetRoom = -1;
            for (int i = 0; i < plan.Rooms.Count; i++) if (plan.Rooms[i].HasDoor && (_targetRoom < 0 || Random.value < 0.4f)) _targetRoom = i;
            if (_targetRoom < 0) return false;

            Location = _door;
            Tokens["building"] = _building.Name;
            Tokens["floor"] = InteriorPlan.FloorName(_targetFloor);
            Objective = $"SWAT call-out: respond to {_building.Address}";
            return true;
        }

        public override void OnArrived()
        {
            _stage = Stage.Perimeter;
            Objective = "Set up the perimeter: hold position by the front door";
        }

        public override void Tick(float dt)
        {
            if (_stage >= Stage.Perimeter && _stage < Stage.Runner && Vector2.Distance(PlayerPosition, _door) > 220f)
            {
                Fail("You left the perimeter. The suspect slipped away.");
                return;
            }
            switch (_stage)
            {
                case Stage.Perimeter:
                    _timer = PlayerOnFoot && Vector2.Distance(PlayerPosition, _door) < 14f ? _timer + dt : 0f;
                    if (_timer > 5f) StartStaging();
                    break;

                case Stage.Staging:
                    if (_truck == null) { Fail("SWAT couldn't reach the scene."); return; }
                    if (_truckDriver.Arrived && _team.Count == 0) SpawnTeam();
                    if (_team.Count > 0 && MoveTeamTo(StackPositions(), dt))
                    {
                        _stage = Stage.Stacked;
                        Objective = "SWAT is stacked up — give the go to the team lead";
                        AddInteraction(_team[0], "Give SWAT the go", Go, 2.6f);
                        Context.Scanner.Transmit("SWAT lead: stacked up on the front door. Your call, Commander.");
                    }
                    break;

                case Stage.Clearing:
                    TickClearing(dt);
                    break;

                case Stage.Runner:
                    if (_suspect == null) { Fail("Suspect escaped."); return; }
                    Location = _suspect.transform.position;
                    _timer = DistanceToPlayer(_suspect) > 60f ? _timer + dt : 0f;
                    if (_timer > 6f) Fail("The suspect got past the perimeter.");
                    break;
            }
        }

        // ---- staging ------------------------------------------------------------------------
        private void StartStaging()
        {
            _stage = Stage.Staging;
            _timer = 0f;
            Objective = "Perimeter set. Hold the front while SWAT arrives";
            Context.Scanner.Transmit(TransmissionPriorityLine(), "Perimeter established. SWAT is rolling, ETA one minute.");
            if (Services.TryGet(out IBackupService backup)) backup.RequestBackup(false);

            var graph = RoadGraph2D.Instance;
            var spec = VehicleCatalog.Instance != null ? VehicleCatalog.Instance.Get("bearcat") : null;
            Vector2 park = _door + _outward * 8f;
            if (graph == null || !BackupRoute.Plan(graph, park, 80f, 170f, out var path, out var start, out float heading)) { start = park + _outward * 6f; heading = 0f; path = new List<Vector2> { park }; }
            _truck = Track(Context.Sprites.SpawnCar(start, heading, spec != null && spec.Sprites.Length > 0 ? spec.Sprites[0] : null, "SWAT Truck"));
            if (spec != null) _truck.ApplySpec(spec);
            _truckDriver = _truck.gameObject.AddComponent<BackupDriver2D>();
            _truckDriver.Begin(_truck, path, park, true);
        }

        private string TransmissionPriorityLine() => $"{Context.Unit}, perimeter is set at {_building.Address}.";

        private void SpawnTeam()
        {
            Sprite officer = OfficerSprite();
            Vector2 rear = _truck.transform.position - _truck.transform.up * (_truck.Spec != null ? _truck.Spec.Length * 0.5f : 3f);
            for (int i = 0; i < 4; i++)
            {
                var p = Track(Context.Sprites.SpawnPed(rear + (Vector2)_truck.transform.right * (i - 1.5f) * 0.8f, _truck.transform.eulerAngles.z + 180f, officer));
                p.name = i == 0 ? "SWAT Lead" : "SWAT Officer";
                p.Idle();
                p.enabled = false;   // scripted teammates: no civilian AI (they'd panic and run from gunfire)
                foreach (var sr in p.GetComponentsInChildren<SpriteRenderer>()) if (!sr.name.Contains("shadow")) sr.color = new Color(0.42f, 0.46f, 0.5f);
                var rb = p.GetComponent<Rigidbody2D>();
                if (rb != null) rb.bodyType = RigidbodyType2D.Kinematic;
                _team.Add(p);
            }
            Context.Scanner.Transmit("SWAT on scene, moving up to the door.");
        }

        private List<Vector2> StackPositions()
        {
            Vector2 along = new Vector2(-_outward.y, _outward.x);
            var list = new List<Vector2>();
            for (int i = 0; i < 4; i++) list.Add(_door + _outward * 1.2f + along * (1.6f + i * 0.9f));
            return list;
        }

        /// <summary>Walks each team member toward their spot; true once everyone is there.</summary>
        private bool MoveTeamTo(List<Vector2> spots, float dt, float speed = 3.4f)
        {
            bool all = true;
            for (int i = 0; i < _team.Count && i < spots.Count; i++)
            {
                var p = _team[i];
                if (p == null) continue;
                Vector2 pos = p.transform.position, to = spots[i] - pos;
                if (to.magnitude > 0.15f)
                {
                    all = false;
                    Vector2 next = pos + to.normalized * Mathf.Min(speed * dt, to.magnitude);
                    p.transform.position = next;
                    var rb = p.GetComponent<Rigidbody2D>(); if (rb != null) rb.position = next;
                    p.transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(-to.x, to.y) * Mathf.Rad2Deg);
                }
            }
            return all;
        }

        /// <summary>The officer's own body sprite (armed if available), so teammates look like police.</summary>
        private Sprite OfficerSprite()
        {
            
            Sprite any = null;
            foreach (var sr in Context.Player.Transform.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr.sprite == null || !sr.sprite.name.StartsWith("officer")) continue;
                if (sr.sprite.name == "officer_armed") return sr.sprite;
                if (any == null || !sr.sprite.name.Contains("walk")) any = sr.sprite;
            }
            return any;
        }

        // ---- entry and clearing -----------------------------------------------------------
        private void Go()
        {
            ClearInteraction(_team[0]);
            _stage = Stage.Clearing;
            _floor = 0; _roomStep = -1; _timer = 0f;
            Objective = "SWAT is clearing the building. Hold the perimeter (or follow them in)";
            Context.Scanner.Transmit("SWAT lead: go, go, go! Making entry.");
            LoadFloor();
        }

        private void LoadFloor()
        {
            var plan = BuildingInterior2D.PreviewPlan(_building, _floor);
            _rooms.Clear();
            for (int i = 0; i < plan.Rooms.Count; i++) if (plan.Rooms[i].HasDoor) _rooms.Add(i);
            if (_floor == _targetFloor)
            {
                // Clear the target room last.
                _rooms.Remove(_targetRoom);
                _rooms.Add(_targetRoom);
            }
            _roomStep = -1;
        }

        private void TickClearing(float dt)
        {
            _timer += dt;
            BuildingInterior2D interior = _interiors != null ? _interiors.Current : null;
            bool watching = interior != null && interior.BuildingId == _building.Id && interior.Floor == _floor;

            // Team visible only where the officer can see them: on the floor being cleared.
            foreach (var p in _team) if (p != null && p.gameObject.activeSelf != watching) p.gameObject.SetActive(watching);

            if (_timer < 1.8f && _roomStep >= 0)
            {
                if (watching) MoveTeamTo(RoomPositions(interior), dt, 4.2f);
                return;
            }
            _timer = 0f;
            _roomStep++;
            if (_roomStep >= _rooms.Count)
            {
                if (_floor >= _targetFloor) { Contact(); return; }
                Context.Scanner.Transmit($"SWAT: {InteriorPlan.FloorName(_floor)} clear, moving up.");
                _floor++;
                LoadFloor();
                return;
            }
            if (watching)
            {
                InteriorDoor2D door = interior.DoorOf(_floor, _rooms[_roomStep]);
                if (door != null) door.ForceOpen();
                if (_roomStep == 0) { var pts = RoomPositions(interior); for (int i = 0; i < _team.Count; i++) if (_team[i] != null) _team[i].transform.position = pts[Mathf.Min(i, pts.Count - 1)]; }
            }
        }

        /// <summary>Lead inside the room being cleared, the rest covering from the hall.</summary>
        private List<Vector2> RoomPositions(BuildingInterior2D interior)
        {
            var plan = interior.PlanFor(_floor);
            var room = plan.Rooms[_rooms[Mathf.Clamp(_roomStep, 0, _rooms.Count - 1)]];
            Vector2 doorMid = (room.DoorA + room.DoorB) * 0.5f;
            Vector2 hallDir = (plan.Hall.center - doorMid).normalized;
            var list = new List<Vector2> { plan.ToWorld(room.Area.center) };
            for (int i = 1; i < 4; i++) list.Add(plan.ToWorld(doorMid + hallDir * 1.0f + new Vector2(0, (i - 2) * 0.9f)));
            return list;
        }

        private void Contact()
        {
            Vector2 outside = _door + _outward * 3f;
            foreach (var p in _team) if (p != null) p.gameObject.SetActive(true);
            if (Random.value < Definition.fleeChance)
            {
                _stage = Stage.Runner;
                _timer = 0f;
                _suspect = Track(Context.Sprites.SpawnPed(outside, Mathf.Atan2(-_outward.x, _outward.y) * Mathf.Rad2Deg));
                _suspect.name = "Barricaded Suspect";
                _suspect.Flee(_team[0] != null ? _team[0].transform : Context.Player.Transform);
                Context.Haptics?.Pulse(0.5f, 0.6f, 0.3f);
                Context.Scanner.Transmit($"SWAT: contact {Tokens["floor"]}! Suspect bailed out a window, coming out the front!");
                Objective = "The suspect is running out the front — stop them!";
                AddInteraction(_suspect, "Detain", () => { ClearInteraction(_suspect); _suspect.Detain(); Arrests++; Finish("Suspect stopped at the perimeter and taken into custody."); }, 2.2f);
                PlaceTeamOutside();
                return;
            }
            _suspect = Track(Context.Sprites.SpawnPed(outside, 0f));
            _suspect.name = "Barricaded Suspect";
            _suspect.Detain();
            Arrests++;
            PlaceTeamOutside();
            Context.Scanner.Transmit($"SWAT: contact {Tokens["floor"]}. Suspect is in custody, no injuries. Building is clear, Code 4.");
            Finish("SWAT cleared the building. Suspect in custody, no injuries.");
        }

        private void PlaceTeamOutside()
        {
            var spots = StackPositions();
            for (int i = 0; i < _team.Count; i++) if (_team[i] != null) _team[i].transform.position = spots[i] + _outward * 1.5f;
        }

        private void Finish(string text)
        {
            _stage = Stage.Done;
            Succeed(text);
        }

        /// <summary>Only cuffing the barricaded suspect counts — not a SWAT teammate.</summary>
        internal override void OnTrackedPedCuffed(Ped2D ped)
        {
            if (ped == null || ped != _suspect || Result != Status.Running) return;
            ClearInteraction(_suspect);
            Arrests++;
            Finish("Suspect stopped at the perimeter and taken into custody.");
        }
    }
}
