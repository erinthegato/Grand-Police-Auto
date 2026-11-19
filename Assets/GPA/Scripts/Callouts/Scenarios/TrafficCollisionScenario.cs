using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// Two cars have collided. Secure the scene for a few seconds, then take statements from
    /// the drivers. Sometimes one driver is impaired and needs arresting.
    /// </summary>
    [CalloutType("traffic_collision")]
    public sealed class TrafficCollisionScenario : CalloutScenario
    {
        private Car2D _a, _b;
        private Ped2D _driverA, _driverB;
        private float _securedTimer;
        private bool _secured;
        private int _statements;

        public override float ArrivalRadius => 25f;

        public override bool Setup()
        {
            if (Context.Spawns == null || Context.Sprites == null) return false;
            if (!Context.Spawns.TryPick(Context.Spawns.Road, PlayerPosition, Definition.minDistance, Definition.maxDistance, out var p)) return false;
            Location = p.Position;
            Vector2 fwd = Quaternion.Euler(0, 0, p.Heading) * Vector3.up;
            Vector2 right = new Vector2(fwd.y, -fwd.x);
            _a = Track(Context.Sprites.SpawnCar(p.Position, p.Heading + Random.Range(-25f, 25f), null, "Collision Car A"));
            _b = Track(Context.Sprites.SpawnCar(p.Position + fwd * 4.6f + right * 1.2f, p.Heading + 160f + Random.Range(-30f, 30f), null, "Collision Car B"));
            _driverA = Track(Context.Sprites.SpawnPed(p.Position - right * 3.4f, 90f));
            _driverB = Track(Context.Sprites.SpawnPed(p.Position + fwd * 4.6f - right * 3.4f, 270f));
            Objective = "Respond to the collision";
            return true;
        }

        public override void OnArrived() => Objective = "Secure the scene — stay close for a moment";

        public override void Tick(float dt)
        {
            if (_secured) return;
            if (Vector2.Distance(PlayerPosition, Location) < 18f) _securedTimer += dt;
            if (_securedTimer < 5f) return;

            _secured = true;
            Objective = "Take statements from both drivers (0/2)";
            Context.Scanner.Transmit($"{Context.Unit}, scene secured, requesting a tow.");
            bool impaired = Random.value < Definition.offenceChance;
            AddInteraction(_driverA, "Take statement", () => Statement(_driverA, false));
            AddInteraction(_driverB, impaired ? "Take statement (smells of alcohol)" : "Take statement", () => Statement(_driverB, impaired));
        }

        private void Statement(Ped2D driver, bool impaired)
        {
            ClearInteraction(driver);
            if (impaired)
            {
                driver.Detain();
                Arrests++;
                Context.Scanner.Transmit($"{Context.Unit}, one under for DWI.");
            }
            _statements++;
            Objective = $"Take statements from both drivers ({_statements}/2)";
            if (_statements >= 2) Succeed(Arrests > 0 ? "Report filed, impaired driver arrested." : "Report filed, vehicles towed.");
        }
    }
}
