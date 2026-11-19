using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Police
{
    /// <summary>
    /// Verbal commands on foot: T / D-pad ← shouts at the person you're facing (or whoever is running
    /// from you). First shout: "Police! Stop!" — second, within a few seconds: "Get on the ground!".
    /// Whether they comply depends on their personality profile (and on how close you are when they're
    /// running); compliant or kneeling people can be cuffed without a struggle.
    /// </summary>
    public sealed class OfficerCommands2D : MonoBehaviour
    {
        public struct Speech { public Transform Who; public string Text; public float Until; public bool Officer; }

        public static OfficerCommands2D Instance { get; private set; }
        public readonly List<Speech> Lines = new List<Speech>();

        private IPlayerAvatar _player;
        private Ped2D _last;
        private float _lastTime = -10f, _cooldown;

        private static readonly Dictionary<Ped2D.Personality, float> Obey = new Dictionary<Ped2D.Personality, float>
        {
            { Ped2D.Personality.Cooperative, 0.95f }, { Ped2D.Personality.Nervous, 0.85f }, { Ped2D.Personality.Confused, 0.8f },
            { Ped2D.Personality.Suspicious, 0.7f }, { Ped2D.Personality.Uncooperative, 0.45f },
            { Ped2D.Personality.Aggressive, 0.3f }, { Ped2D.Personality.Fleeing, 0.2f },
        };

        private static readonly Dictionary<Ped2D.Personality, string> Refusals = new Dictionary<Ped2D.Personality, string>
        {
            { Ped2D.Personality.Cooperative, "Okay, okay!" }, { Ped2D.Personality.Nervous, "I— I didn't do anything!" },
            { Ped2D.Personality.Confused, "What? Me?" }, { Ped2D.Personality.Suspicious, "For what?" },
            { Ped2D.Personality.Uncooperative, "I know my rights." }, { Ped2D.Personality.Aggressive, "Back off!" },
            { Ped2D.Personality.Fleeing, "..." },
        };

        private void Awake() => Instance = this;
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            Lines.RemoveAll(l => l.Who == null || Time.time > l.Until);
            if (_player == null && !Services.TryGet(out _player)) return;
            if (_player.CurrentVehicle != null || (InteractionMenu2D.Instance != null && InteractionMenu2D.Instance.IsOpen)) return;
            if (GameplayHotkeys.Blocked) return;
            _cooldown -= Time.deltaTime;

            bool shout = (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame) ||
                         (Gamepad.current != null && Gamepad.current.dpad.left.wasPressedThisFrame);
            if (!shout || _cooldown > 0f) return;
            _cooldown = 0.6f;

            Ped2D target = PickTarget();
            bool escalate = target != null && target == _last && Time.time - _lastTime < 6f;
            Say(_player.Transform, escalate ? "Get on the ground!" : "Police! Stop!", true);
            if (target == null) return;
            _last = target; _lastTime = Time.time;

            float chance = Obey.TryGetValue(target.Profile, out float c) ? c : 0.5f;
            float dist = Vector2.Distance(target.transform.position, _player.Transform.position);
            if (target.Current == Ped2D.Mode.Flee) chance *= dist < 6f ? 1.1f : 0.6f;
            if (escalate && target.Compliant) chance += 0.15f;
            if (target.Subdued) chance = 1f;

            if (Random.value < Mathf.Clamp01(chance))
            {
                if (escalate) { target.Surrender(); Say(target.transform, "Okay! I'm down!"); }
                else { target.Halt(); Say(target.transform, Refusals.TryGetValue(target.Profile, out string ok) && target.Profile <= Ped2D.Personality.Confused ? ok : "Alright, alright."); }
                return;
            }
            Say(target.transform, Refusals.TryGetValue(target.Profile, out string no) ? no : "No.");
            if (target.Profile == Ped2D.Personality.Aggressive || target.Profile == Ped2D.Personality.Fleeing || target.Current == Ped2D.Mode.Flee)
                target.Flee(_player.Transform);
        }

        /// <summary>Whoever is running from you nearby first, else the closest person in front.</summary>
        private Ped2D PickTarget()
        {
            Vector2 pos = _player.Transform.position, fwd = _player.Transform.up;
            Ped2D best = null;
            float bestScore = float.MaxValue;
            foreach (var col in Physics2D.OverlapCircleAll(pos, 16f))
            {
                Ped2D p = col.GetComponentInParent<Ped2D>();
                if (p == null || p.Cuffed || p.Current == Ped2D.Mode.Downed) continue;
                Vector2 to = (Vector2)p.transform.position - pos;
                bool running = p.Current == Ped2D.Mode.Flee;
                if (!running && (to.magnitude > 12f || Vector2.Angle(fwd, to) > 55f)) continue;
                float score = to.magnitude - (running ? 20f : 0f);
                if (score < bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        private void Say(Transform who, string text, bool officer = false)
        {
            Lines.RemoveAll(l => l.Who == who);
            Lines.Add(new Speech { Who = who, Text = text, Until = Time.time + 2.2f, Officer = officer });
        }
    }
}
