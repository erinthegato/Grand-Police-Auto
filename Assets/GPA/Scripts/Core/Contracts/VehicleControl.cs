using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Everything a <c>VehicleController</c> needs to drive a car, whether the values come from the
    /// player's input actions or from an AI driver. Implementations expose the raw axes only — no
    /// vehicle ever reads input devices directly.
    /// </summary>
    public interface IVehicleControlInput
    {
        /// <summary>0..1 forward drive. Ignored above the vehicle's top speed.</summary>
        float Throttle { get; }
        /// <summary>0..1 brake. Holding it from a standstill reverses.</summary>
        float Brake { get; }
        /// <summary>-1 (left) .. 1 (right).</summary>
        float Steer { get; }
        bool Handbrake { get; }
    }

    /// <summary>A controls nobody is touching: used when the car is parked or an AI has let go.</summary>
    public sealed class IdleVehicleControl : IVehicleControlInput
    {
        public static readonly IdleVehicleControl Instance = new IdleVehicleControl();
        public float Throttle => 0f;
        public float Brake => 0f;
        public float Steer => 0f;
        public bool Handbrake => false;
    }

    /// <summary>
    /// A vehicle running lights and/or a siren, published to the world so traffic can yield to it
    /// (M4 perception reads this instead of every driver scanning for police cars).
    /// </summary>
    public interface IEmergencySignalSource
    {
        Vector3 SignalPosition { get; }
        Vector3 SignalForward { get; }
        EmergencyLightMode Lights { get; }
        bool SirenOn { get; }
        IVehicle Vehicle { get; }
    }
}
