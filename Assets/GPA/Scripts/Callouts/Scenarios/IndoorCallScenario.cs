using System.Collections.Generic;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// A call inside a building: dispatch gives the address, floor and apartment / room. The officer
    /// drives there, goes in, takes the stairs or lift to the right floor and finds the room (forcing
    /// the door if it's locked). The people are only spawned once the officer is on that floor, so the
    /// building doesn't need to be built in advance.
    /// </summary>
    [CalloutType("indoor_call")]
    public sealed class IndoorCallScenario : CalloutScenario
    {
        private CityLocationDatabase.Building _building;
        private int _floor, _roomIndex;
        private bool _locked, _spawned, _contacted, _resolved;
        private Ped2D _subject, _complainant;
        private InteriorManager2D _interiors;
        private float _lostTimer;

        public override float ArrivalRadius => 18f;

        public override bool Setup()
        {
            if (Context.Sprites == null) return false;
            var locations = Object.FindAnyObjectByType<CityLocations>();
            _interiors = Object.FindAnyObjectByType<InteriorManager2D>();
            if (locations == null || locations.Database == null || _interiors == null) return false;

            // A building of the right type at a sensible driving distance.
            var options = new List<CityLocationDatabase.Building>();
            foreach (var b in locations.Database.Buildings)
            {
                if (b.Type != Definition.buildingType || b.Footprint.width < 6f || b.Footprint.height < 6f) continue;
                float d = Vector2.Distance(b.Entrance, PlayerPosition);
                if (d >= Definition.minDistance && d <= Definition.maxDistance) options.Add(b);
            }
            if (options.Count == 0) return false;
            _building = options[Random.Range(0, options.Count)];

            // A floor and a matching room on it.
            int floors = InteriorPlan.FloorCountFor(_building.Type, _building.Floors);
            for (int attempt = 0; attempt < 8 && !_spawned; attempt++)
            {
                _floor = Random.Range(0, floors);
                var plan = BuildingInterior2D.PreviewPlan(_building, _floor);
                var rooms = new List<int>();
                for (int i = 0; i < plan.Rooms.Count; i++)
                {
                    var r = plan.Rooms[i];
                    bool kindOk = string.IsNullOrEmpty(Definition.roomKind) ? r.HasDoor : r.Kind == Definition.roomKind;
                    if (kindOk) rooms.Add(i);
                }
                if (rooms.Count == 0) continue;
                _roomIndex = rooms[Random.Range(0, rooms.Count)];
                _locked = plan.Rooms[_roomIndex].HasDoor && Random.value < Definition.lockedChance;
                Location = InteriorManager2D.DoorPoint(_building);
                Tokens["building"] = _building.Name;
                Tokens["floor"] = InteriorPlan.FloorName(_floor);
                Tokens["apt"] = RoomLabel(plan.Rooms[_roomIndex].Kind);
                Objective = $"Respond to {_building.Address}";
                return true;
            }
            return false;
        }

        private string RoomLabel(string kind)
        {
            if (kind == "unit") return $"apartment {_floor + 1}{(char)('A' + _roomIndex % 26)}";
            if (kind == "sales" || kind == "dining") return "the shop floor";
            return $"room {(_floor + 1) * 100 + _roomIndex + 1}";
        }

        public override void OnArrived()
        {
            Objective = $"Go inside — {Tokens["floor"]}, {Tokens["apt"]}";
        }

        public override void Tick(float dt)
        {
            if (_resolved) return;
            BuildingInterior2D interior = _interiors != null ? _interiors.Current : null;
            bool onFloor = interior != null && interior.BuildingId == _building.Id && interior.Floor == _floor;

            if (!onFloor)
            {
                if (interior != null && interior.BuildingId == _building.Id && !_contacted)
                    Objective = $"Take the stairs to the {Tokens["floor"]} — {Tokens["apt"]}";
                // Fleeing suspect who got away from the officer.
                if (_contacted && _subject != null && _subject.Current == Ped2D.Mode.Flee) TrackFlee(dt);
                return;
            }

            if (!_spawned || (_subject == null && !_contacted)) SpawnScene(interior);

            InteriorDoor2D door = interior.DoorOf(_floor, _roomIndex);
            bool canReach = door == null || door.Open;
            if (!_contacted)
                Objective = canReach ? $"Speak with the occupant of {Tokens["apt"]}"
                    : door.Locked ? $"No answer at {Tokens["apt"]} — force the door" : $"Knock and open the door of {Tokens["apt"]}";
            if (canReach && !_contacted && _subject != null && _subject.GetComponent<Interactable2D>() == null)
                AddInteraction(_subject, "Speak with occupant", Talk);

            if (_subject == null && _contacted) { Fail("Subject gone."); return; }
            if (_subject != null && _subject.Current == Ped2D.Mode.Flee) TrackFlee(dt);
        }

        private void SpawnScene(BuildingInterior2D interior)
        {
            Transform root = interior.FloorRoot(_floor);
            if (root == null) return;
            InteriorPlan plan = interior.PlanFor(_floor);
            Rect area = plan.Rooms[_roomIndex].Area;
            Vector2 c = plan.ToWorld(area.center);
            Vector2 side = plan.ToWorld(area.center + new Vector2(area.width * 0.25f, 0)) - c;

            _subject = Track(Context.Sprites.SpawnPed(c - side * 0.5f, Random.Range(0f, 360f)));
            _subject.name = "Callout Subject";
            _subject.transform.SetParent(root, true);
            _subject.Idle();
            if (Definition.complainant)
            {
                _complainant = Track(Context.Sprites.SpawnPed(c + side, Random.Range(0f, 360f)));
                _complainant.name = "Callout Complainant";
                _complainant.transform.SetParent(root, true);
                _complainant.Idle();
                AddInteraction(_complainant, "Speak with complainant", () =>
                    Context.Scanner.Transmit(string.IsNullOrEmpty(Definition.complainantLine) ? "Complainant: \"Please help, it's them over there.\"" : Definition.complainantLine));
            }
            InteriorDoor2D door = interior.DoorOf(_floor, _roomIndex);
            if (door != null && _locked) door.SetLocked(true);
            _spawned = true;
        }

        private void Talk()
        {
            if (_contacted) return;
            _contacted = true;
            ClearInteraction(_subject);
            if (Random.value < Definition.fleeChance)
            {
                _subject.Flee(Context.Player.Transform);
                Objective = "Occupant is running — catch and detain them";
                Context.Scanner.Transmit($"{Context.Unit}, subject fleeing on foot inside {_building.Address}.");
                Context.Haptics?.Pulse(0.5f, 0.6f, 0.25f);
                AddInteraction(_subject, "Detain", Detain, 2.0f);
            }
            else if (Random.value < Definition.offenceChance)
            {
                Objective = "Probable cause found — place the occupant under arrest";
                AddInteraction(_subject, "Arrest", Detain);
            }
            else
            {
                _resolved = true;
                Succeed("Spoke with the occupant. Matter resolved, no further action.");
            }
        }

        private void Detain()
        {
            ClearInteraction(_subject);
            _subject.Detain();
            Arrests++;
            _resolved = true;
            Context.Haptics?.Pulse(0.3f, 0.4f, 0.15f);
            Succeed("Subject detained and transported.");
        }

        private void TrackFlee(float dt)
        {
            _lostTimer = DistanceToPlayer(_subject) > 45f ? _lostTimer + dt : 0f;
            if (_lostTimer > 6f) Fail("Subject escaped.");
        }
    }
}
