using UnityEngine;

namespace GPA.Core
{
    /// <summary>Shared read-only view of any vehicle (player or AI).</summary>
    public interface IVehicle
    {
        /// <summary>Persistent record id in the vehicle database (null if unregistered).</summary>
        string VehicleRecordId { get; }
        string Plate { get; }
        /// <summary>Human readable description, e.g. "Blue Declasse Sedan".</summary>
        string Description { get; }
        Transform Transform { get; }
        Vector3 Velocity { get; }
        bool IsEmergencyVehicle { get; }
        bool IsPlayerDriven { get; }
        /// <summary>True when an AI driver is controlling this vehicle.</summary>
        bool IsAIDriven { get; }
    }

    public enum EmergencyLightMode { Off, Stage1, Stage2, Stage3 }

    /// <summary>Published whenever a police vehicle's lights or siren change.</summary>
    public readonly struct EmergencySignalChangedEvent
    {
        public readonly IVehicle Vehicle;
        public readonly EmergencyLightMode Lights;
        public readonly bool Siren;
        public EmergencySignalChangedEvent(IVehicle v, EmergencyLightMode lights, bool siren) { Vehicle = v; Lights = lights; Siren = siren; }
    }

    public readonly struct PlayerVehicleChangedEvent
    {
        public readonly IVehicle Vehicle; // null when the player exits
        public PlayerVehicleChangedEvent(IVehicle v) { Vehicle = v; }
    }
}
