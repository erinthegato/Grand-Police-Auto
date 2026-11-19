using System;
using GPA.Core;
using GPA.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Vehicles
{
    /// <summary>
    /// Feeds the Vehicle action map into a <see cref="VehicleController"/>. Kept separate from the
    /// controller so an AI driver (M4) can supply the same interface without touching input code.
    /// </summary>
    public sealed class PlayerVehicleInput : MonoBehaviour, IVehicleControlInput
    {
        private IInputService _inputService;
        private InputAction _throttle, _brake, _steer, _handbrake;
        private float _throttleValue, _brakeValue, _steerValue;
        private bool _handbrakeValue;
        private bool _warned;

        public float Throttle => _throttleValue;
        public float Brake => _brakeValue;
        public float Steer => _steerValue;
        public bool Handbrake => _handbrakeValue;
        /// <summary>True once the actions are bound; the controller falls back to idle until then.</summary>
        public bool Ready { get; private set; }

        private void Start()
        {
            if (!Services.TryGet(out _inputService) || _inputService == null)
            {
                if (!_warned) Debug.LogWarning("[VehicleInput] No IInputService registered; the car will not respond.", this);
                _warned = true;
                return;
            }

            _throttle = _inputService.Get(ActionMaps.Vehicle, "Throttle");
            _brake = _inputService.Get(ActionMaps.Vehicle, "Brake");
            _steer = _inputService.Get(ActionMaps.Vehicle, "Steer");
            _handbrake = _inputService.Get(ActionMaps.Vehicle, "Handbrake");
            Ready = true;
        }

        /// <summary>Samples the actions once per frame; the controller reads the cached values.</summary>
        public void Sample()
        {
            if (!Ready) return;
            _throttleValue = _throttle != null ? _throttle.ReadValue<float>() : 0f;
            _brakeValue = _brake != null ? _brake.ReadValue<float>() : 0f;
            _steerValue = _steer != null ? _steer.ReadValue<float>() : 0f;
            _handbrakeValue = _handbrake != null && _handbrake.IsPressed();
        }

        private void OnDisable()
        {
            _throttleValue = 0f;
            _brakeValue = 0f;
            _steerValue = 0f;
            _handbrakeValue = false;
        }
    }
}
