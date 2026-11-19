using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// A reported stolen car sits nearby. When the officer gets close it takes off: pursue, stop
    /// the car (ram, box it in, or let it crash), then arrest the driver, who may bail and run.
    /// </summary>
    [CalloutType("stolen_vehicle")]
    public sealed class StolenVehicleScenario : CalloutScenario
    {
        private Car2D _car;
        private SuspectDriver2D _driver;
        private Ped2D _suspect;
        private float _stoppedTimer, _lostTimer;
        private enum Stage { Waiting, Pursuit, Stopped, FootChase }
        private Stage _stage;

        public override float ArrivalRadius => 45f;

        public override bool Setup()
        {
            if (Context.Spawns == null || Context.Sprites == null) return false;
            if (!Context.Spawns.TryPick(Context.Spawns.Road, PlayerPosition, Definition.minDistance, Definition.maxDistance, out var p)) return false;
            Location = p.Position;
            Sprite sprite = Context.Sprites.RandomCivilianCar();
            _car = Track(Context.Sprites.SpawnCar(p.Position, p.Heading, sprite, "Stolen Vehicle"));
            _driver = _car.gameObject.AddComponent<SuspectDriver2D>();
            Tokens["vehicle"] = Describe(sprite);
            Tokens["plate"] = RandomPlate();
            // Use a real registered vehicle when the database is up: it gets flagged stolen with a BOLO,
            // so the plate checks out as stolen on the MDT and in traffic stops.
            if (GPA.Core.Services.TryGet(out GPA.Core.IRecordsDirectory records) && records.Ready &&
                records.TryReportStolen(sprite != null ? sprite.name : null, out var info))
            {
                _car.AssignRecord(info.VehicleId, info.Plate, info.Description, info.DriverCitizenId);
                Tokens["vehicle"] = info.Description.ToLowerInvariant();
                Tokens["plate"] = info.Plate;
            }
            Objective = $"Locate the stolen {Tokens["vehicle"]}, plate {Tokens["plate"]}";
            return true;
        }

        public override void OnArrived()
        {
            _driver.Begin(_car, Context.Player.Transform);
            _stage = Stage.Pursuit;
            Objective = "Vehicle is fleeing — stop it";
            Context.Scanner.Transmit($"{Context.Unit}, in pursuit of the {Tokens["vehicle"]}.", "All units, {unit} in pursuit. Use caution.");
        }

        public override void Tick(float dt)
        {
            if (_car == null) { Fail("Vehicle lost."); return; }

            switch (_stage)
            {
                case Stage.Waiting:
                    // Keep the target location honest if something nudged the car.
                    Location = _car.transform.position;
                    break;

                case Stage.Pursuit:
                    Location = _car.transform.position;
                    float d = DistanceToPlayer(_car);
                    _stoppedTimer = _car.SpeedKmh < 6f && d < 30f ? _stoppedTimer + dt : 0f;
                    _lostTimer = d > 220f ? _lostTimer + dt : 0f;
                    if (_lostTimer > 8f) { _driver.Stop(); Fail("Suspect vehicle lost."); return; }
                    if (_stoppedTimer > 2.5f) StopVehicle();
                    break;

                case Stage.Stopped:
                    Location = _suspect != null ? (Vector2)_suspect.transform.position : (Vector2)_car.transform.position;
                    break;

                case Stage.FootChase:
                    Location = _suspect.transform.position;
                    _lostTimer = DistanceToPlayer(_suspect) > 60f ? _lostTimer + dt : 0f;
                    if (_lostTimer > 6f) Fail("Driver escaped on foot.");
                    break;
            }
        }

        private void StopVehicle()
        {
            _driver.Stop();
            _stage = Stage.Stopped;
            Vector2 door = _car.DriverExitPoint;
            CarDoors2D.Swing(_car, CarDoors2D.Door.DriverFront, 0.4f);   // suspect bails out, leaves it to swing shut
            _suspect = Track(Context.Sprites.SpawnPed(door, _car.transform.eulerAngles.z));
            _suspect.name = "Suspect Driver";
            Context.Scanner.Transmit($"{Context.Unit}, vehicle stopped, driver is out.");

            if (Random.value < Definition.fleeChance)
            {
                _suspect.Flee(Context.Player.Transform);
                _stage = Stage.FootChase;
                _lostTimer = 0f;
                Objective = "Driver bailed — catch and arrest them";
                AddInteraction(_suspect, "Arrest", Arrest, 2.0f);
            }
            else
            {
                Objective = "Get out and arrest the driver";
                AddInteraction(_suspect, "Arrest", Arrest);
            }
        }

        private void Arrest()
        {
            ClearInteraction(_suspect);
            _suspect.Detain();
            Arrests++;
            Succeed("Driver in custody, stolen vehicle recovered.");
        }

        private static string Describe(Sprite s)
        {
            string n = s != null ? s.name : "";
            if (n.Contains("taxi")) return "yellow taxi";
            if (n.Contains("maroon")) return "red sedan";
            if (n.Contains("gunmetal")) return "grey sedan";
            if (n.Contains("olive")) return "green sedan";
            return "sedan";
        }

        private static string RandomPlate()
        {
            const string letters = "ABCDEFGHJKLMNPRSTUVWXYZ";
            char L() => letters[Random.Range(0, letters.Length)];
            return $"{L()}{L()}{L()}-{Random.Range(1000, 9999)}";
        }
    }
}
