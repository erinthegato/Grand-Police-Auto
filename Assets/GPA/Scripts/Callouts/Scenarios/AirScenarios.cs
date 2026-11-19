using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// Air unit: a stolen car is running from ground units. Find it from the helicopter and keep it in
    /// sight long enough for the ground units to box it in. Losing sight for too long loses the car.
    /// </summary>
    [CalloutType("air_pursuit")]
    public sealed class AirPursuitScenario : CalloutScenario
    {
        private Car2D _car;
        private SuspectDriver2D _driver;
        private float _tracked, _lost;
        private bool _found;

        public override float ArrivalRadius => 60f;
        private float Needed => Mathf.Max(20f, Definition.offenceChance * 60f);

        public override bool Setup()
        {
            if (Context.Spawns == null || Context.Sprites == null) return false;
            if (!Context.Spawns.TryPick(Context.Spawns.Road, PlayerPosition, Definition.minDistance, Definition.maxDistance, out var p)) return false;
            Location = p.Position;
            Sprite sprite = Context.Sprites.RandomCivilianCar();
            _car = Track(Context.Sprites.SpawnCar(p.Position, p.Heading, sprite, "Pursuit Vehicle"));
            _driver = _car.gameObject.AddComponent<SuspectDriver2D>();
            // It's already running from the ground units.
            _driver.Begin(_car, Context.Player.Transform);
            Tokens["vehicle"] = sprite != null && sprite.name.Contains("taxi") ? "yellow taxi" : "sedan";
            if (Services.TryGet(out IRecordsDirectory records) && records.Ready && records.TryReportStolen(sprite != null ? sprite.name : null, out var info))
            {
                _car.AssignRecord(info.VehicleId, info.Plate, info.Description, info.DriverCitizenId);
                Tokens["vehicle"] = info.Description.ToLowerInvariant();
                Tokens["plate"] = info.Plate;
            }
            else Tokens["plate"] = "unknown";
            Objective = $"Air support: find the fleeing {Tokens["vehicle"]}";
            return true;
        }

        public override void Tick(float dt)
        {
            if (_car == null) { Fail("Suspect vehicle lost."); return; }
            Location = _car.transform.position;
            float d = DistanceToPlayer(_car);
            if (!_found)
            {
                if (d < 45f)
                {
                    _found = true;
                    Context.Scanner.Transmit($"{Context.Unit}, air unit has the {Tokens["vehicle"]} in sight.", "Copy air, ground units are closing in. Keep eyes on it.");
                }
                return;
            }
            if (d < 55f) { _tracked += dt; _lost = 0f; }
            else _lost += dt;
            Objective = d < 55f ? $"Keep the suspect in sight — {Mathf.CeilToInt(Needed - _tracked)} s until ground units box it in"
                                : $"You've lost sight of the suspect! ({Mathf.CeilToInt(10f - _lost)} s)";
            if (_lost > 10f) { _driver.Stop(); Fail("Air unit lost the suspect vehicle."); return; }
            if (_tracked >= Needed)
            {
                _driver.Stop();
                Arrests++;
                Context.Scanner.Transmit("Ground units have the vehicle boxed in. Suspect in custody, thanks air.");
                Succeed("Suspect vehicle stopped with air support. Driver in custody.");
            }
        }
    }

    /// <summary>
    /// Air unit: a suspect ran from officers and is hiding on foot. Search the area from the air,
    /// then hover over them (slow, within 18 m) to light them up so ground units can take them.
    /// </summary>
    [CalloutType("air_search")]
    public sealed class AirSearchScenario : CalloutScenario
    {
        private Ped2D _suspect;
        private float _lit, _time;
        private bool _seen;

        public override float ArrivalRadius => 80f;

        public override bool Setup()
        {
            if (Context.Spawns == null || Context.Sprites == null) return false;
            if (!Context.Spawns.TryPick(Context.Spawns.Sidewalk, PlayerPosition, Definition.minDistance, Definition.maxDistance, out var p)) return false;
            // The search area is roughly where they were last seen, not exactly where they are.
            Location = p.Position + Random.insideUnitCircle * 25f;
            _suspect = Track(Context.Sprites.SpawnPed(p.Position, Random.Range(0f, 360f)));
            _suspect.name = "Search Suspect";
            _suspect.Wander();
            Objective = "Air support: search the area for the suspect on foot";
            return true;
        }

        public override void Tick(float dt)
        {
            if (_suspect == null) { Fail("Suspect lost."); return; }
            _time += dt;
            float d = DistanceToPlayer(_suspect);
            if (!_seen && d < 40f)
            {
                _seen = true;
                _suspect.Flee(Context.Player.Transform);
                Context.Scanner.Transmit($"{Context.Unit}, air unit, I have the subject, he's running.");
            }
            bool slow = Context.Player.CurrentVehicle is Car2D heli && Mathf.Abs(heli.ForwardSpeed) < 9f;
            if (_seen)
            {
                Location = _suspect.transform.position;
                _lit = d < 18f && slow ? _lit + dt : Mathf.Max(0f, _lit - dt * 0.5f);
                Objective = d < 18f ? (slow ? $"Holding the spotlight on the suspect… {Mathf.CeilToInt(5f - _lit)}" : "Slow down and hover over the suspect")
                                    : "Follow the suspect and hover over them";
                if (_lit >= 5f)
                {
                    _suspect.Detain();
                    Arrests++;
                    Context.Scanner.Transmit("Ground units have him. Suspect in custody, good work air.");
                    Succeed("Suspect located from the air and taken into custody.");
                    return;
                }
                if (d > 120f) { Fail("Lost the suspect."); return; }
            }
            if (_time > 180f) Fail("The search was called off.");
        }
    }
}
