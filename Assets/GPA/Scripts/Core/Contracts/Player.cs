using UnityEngine;

namespace GPA.Core
{
    /// <summary>The player officer as seen by other systems (vehicles, AI, HUD, callouts).</summary>
    public interface IPlayerAvatar
    {
        Transform Transform { get; }
        Camera Camera { get; }
        IInteractor Interactor { get; }
        IVehicle CurrentVehicle { get; }
        bool IsFirstPerson { get; }
        /// <summary>Seat the player in a vehicle. The vehicle system takes over camera control.</summary>
        void EnterVehicle(IVehicle vehicle, Transform seat);
        /// <summary>Place the player back on foot at the given pose.</summary>
        void ExitVehicle(Vector3 position, Quaternion rotation);
        /// <summary>Temporarily block on-foot movement and look (cutscenes, MDT, interactions).</summary>
        void SetControlLocked(object owner, bool locked);
    }
}
