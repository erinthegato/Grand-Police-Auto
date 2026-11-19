using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// Find a person on the pavement and talk to them. They may run (foot chase, detain on contact)
    /// or turn out to be carrying something arrestable; otherwise they're sent on their way.
    /// </summary>
    [CalloutType("suspicious_person")]
    public sealed class SuspiciousPersonScenario : CalloutScenario
    {
        private Ped2D _subject;
        private float _lostTimer;
        private bool _contacted;

        public override bool Setup()
        {
            if (Context.Spawns == null || Context.Sprites == null) return false;
            if (!Context.Spawns.TryPick(Context.Spawns.Sidewalk, PlayerPosition, Definition.minDistance, Definition.maxDistance, out var p)) return false;
            Location = p.Position;
            _subject = Track(Context.Sprites.SpawnPed(p.Position, Random.Range(0f, 360f)));
            _subject.name = "Callout Subject";
            _subject.Wander();
            Objective = "Respond to the area and locate the subject";
            return true;
        }

        public override void OnArrived()
        {
            Objective = "Find the subject on foot and speak with them";
            AddInteraction(_subject, "Speak with subject", Talk);
        }

        private void Talk()
        {
            if (_contacted) return;
            _contacted = true;
            ClearInteraction(_subject);
            _subject.Idle();

            if (Random.value < Definition.fleeChance)
            {
                _subject.Flee(Context.Player.Transform);
                Objective = "Subject is fleeing on foot — catch and detain them";
                Context.Scanner.Transmit($"{Context.Unit}, foot pursuit, subject running from my location.");
                Context.Haptics?.Pulse(0.5f, 0.6f, 0.25f);
                AddInteraction(_subject, "Detain", Detain, 2.0f);
            }
            else if (Random.value < Definition.offenceChance)
            {
                Objective = "Subject is carrying narcotics — place them under arrest";
                AddInteraction(_subject, "Arrest", Detain);
            }
            else
            {
                Succeed("Subject identified, no offence. Sent on their way.");
                _subject.Wander();
            }
        }

        private void Detain()
        {
            ClearInteraction(_subject);
            _subject.Detain();
            Arrests++;
            Context.Haptics?.Pulse(0.3f, 0.4f, 0.15f);
            Succeed("Subject detained and transported.");
        }

        public override void Tick(float dt)
        {
            if (_subject == null) { Fail("Subject gone."); return; }
            if (_subject.Current != Ped2D.Mode.Flee) return;
            _lostTimer = DistanceToPlayer(_subject) > 60f ? _lostTimer + dt : 0f;
            if (_lostTimer > 6f) Fail("Subject escaped on foot.");
        }
    }
}
