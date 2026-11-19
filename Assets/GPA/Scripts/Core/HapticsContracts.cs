using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Controller rumble and phone vibration. Systems request feedback; the service mixes requests,
    /// applies the player's intensity setting and silences everything while paused or unfocused.
    /// </summary>
    public interface IHapticsService
    {
        bool Enabled { get; set; }
        /// <summary>0..1 master scale.</summary>
        float Intensity { get; set; }
        /// <summary>One-shot burst. low = heavy motor, high = light motor, both 0..1.</summary>
        void Pulse(float low, float high, float duration);
        /// <summary>Sets a continuous rumble owned by <paramref name="owner"/> (engine, road). Strongest wins.</summary>
        void SetContinuous(object owner, float low, float high);
        void ClearContinuous(object owner);
    }

    public static class HapticsSettingKeys
    {
        public const string Enabled = "haptics.enabled";
        public const string Intensity = "haptics.intensity";
    }
}
