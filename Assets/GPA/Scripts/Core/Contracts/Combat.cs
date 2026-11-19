using UnityEngine;

namespace GPA.Core
{
    public enum DamageType { Bullet, Melee, Vehicle, Electric, Fall, Explosion }

    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly DamageType Type;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly GameObject Source;

        public DamageInfo(float amount, DamageType type, Vector3 point, Vector3 direction, GameObject source)
        {
            Amount = amount; Type = type; Point = point; Direction = direction; Source = source;
        }
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        void ApplyDamage(in DamageInfo info);
    }

    /// <summary>Published for gunshots so AI can react (panic, flee, comply).</summary>
    public readonly struct GunshotEvent
    {
        public readonly Vector3 Position;
        public readonly float AudibleRadius;
        public readonly GameObject Shooter;
        public GunshotEvent(Vector3 pos, float radius, GameObject shooter) { Position = pos; AudibleRadius = radius; Shooter = shooter; }
    }
}
