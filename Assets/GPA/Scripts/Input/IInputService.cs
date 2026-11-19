using System;
using UnityEngine.InputSystem;

namespace GPA.Input
{
    public enum InputDeviceFamily { KeyboardMouse, Xbox, PlayStation, Generic }

    public static class ActionMaps
    {
        public const string OnFoot = "OnFoot";
        public const string Interaction = "Interaction";
        public const string Firearms = "Firearms";
        public const string WeaponWheel = "WeaponWheel";
        public const string Vehicle = "Vehicle";
        public const string PoliceComputer = "PoliceComputer";
        public const string PoliceRadio = "PoliceRadio";
        public const string UI = "UI";
    }

    public interface IInputService
    {
        InputActionAsset Asset { get; }
        /// <summary>Returns the action or throws if it does not exist.</summary>
        InputAction Get(string map, string action);
        InputDeviceFamily ActiveFamily { get; }
        bool IsGamepadActive { get; }
        event Action<InputDeviceFamily> ActiveFamilyChanged;
        /// <summary>Raised after bindings change (rebind, reset, load).</summary>
        event Action BindingsChanged;
        /// <summary>Label for the binding of an action on the active device ("E", "A", "Cross").</summary>
        string GetBindingLabel(string map, string action);
        void SaveBindings();
        void ResetBindings();
        void NotifyBindingsChanged();
    }
}
