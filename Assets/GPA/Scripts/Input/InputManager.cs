using System;
using System.Collections.Generic;
using GPA.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.XInput;

namespace GPA.Input
{
    /// <summary>
    /// Owns the GPAControls asset. Enables action maps according to the current game state,
    /// tracks the active device family for glyph prompts and persists binding overrides.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    public sealed class InputManager : MonoBehaviour, IInputService
    {
        [Serializable]
        public class StateContext
        {
            public GameState State;
            [Tooltip("Also enable the maps of the underlying base state (OnFoot / InVehicle).")]
            public bool InheritBase;
            public string[] Maps;
        }

        [Header("Asset")]
        [SerializeField] private InputActionAsset _actions;

        [Header("Contexts")]
        [Tooltip("Which action maps are enabled for each game state.")]
        [SerializeField] private List<StateContext> _contexts = new List<StateContext>();

        private readonly Dictionary<(string, string), InputAction> _cache = new Dictionary<(string, string), InputAction>();
        private readonly HashSet<string> _desired = new HashSet<string>();
        private IGameStateService _state;
        private GameState _baseState = GameState.OnFoot;

        public InputActionAsset Asset => _actions;
        public InputDeviceFamily ActiveFamily { get; private set; } = InputDeviceFamily.KeyboardMouse;
        public bool IsGamepadActive => ActiveFamily != InputDeviceFamily.KeyboardMouse;
        public event Action<InputDeviceFamily> ActiveFamilyChanged;
        public event Action BindingsChanged;

        private void Reset() => _contexts = DefaultContexts();

        private void Awake()
        {
            if (_actions == null)
            {
                Debug.LogError("[Input] InputManager has no InputActionAsset assigned.", this);
                enabled = false;
                return;
            }
            if (_contexts == null || _contexts.Count == 0) _contexts = DefaultContexts();
            BindingPersistence.Load(_actions);
            Services.Register<IInputService>(this);
            if (Gamepad.current != null && Keyboard.current == null) SetFamily(FamilyOf(Gamepad.current));
        }

        private void OnEnable()
        {
            InputSystem.onActionChange += OnActionChange;
            if (Services.TryGet(out _state)) _state.StateChanged += OnStateChanged;
            else Services.ServiceRegistered += OnServiceRegistered;
            ApplyContext(_state != null ? _state.Current : GameState.OnFoot);
        }

        private void OnDisable()
        {
            InputSystem.onActionChange -= OnActionChange;
            Services.ServiceRegistered -= OnServiceRegistered;
            if (_state != null) _state.StateChanged -= OnStateChanged;
            _actions?.Disable();
        }

        private void OnDestroy() => Services.Unregister<IInputService>(this);

        private void OnServiceRegistered(Type type, object svc)
        {
            if (type != typeof(IGameStateService)) return;
            Services.ServiceRegistered -= OnServiceRegistered;
            _state = (IGameStateService)svc;
            _state.StateChanged += OnStateChanged;
            ApplyContext(_state.Current);
        }

        private void OnStateChanged(GameState prev, GameState current) => ApplyContext(current);

        private void ApplyContext(GameState state)
        {
            if (_actions == null) return;
            if (state == GameState.OnFoot || state == GameState.InVehicle) _baseState = state;

            _desired.Clear();
            var ctx = Find(state);
            if (ctx != null)
            {
                if (ctx.InheritBase)
                {
                    var b = Find(_baseState);
                    if (b?.Maps != null) foreach (var m in b.Maps) _desired.Add(m);
                }
                if (ctx.Maps != null) foreach (var m in ctx.Maps) _desired.Add(m);
            }

            foreach (var map in _actions.actionMaps)
            {
                bool want = _desired.Contains(map.name);
                if (want && !map.enabled) map.Enable();
                else if (!want && map.enabled) map.Disable();
            }
        }

        private StateContext Find(GameState s)
        {
            for (int i = 0; i < _contexts.Count; i++) if (_contexts[i].State == s) return _contexts[i];
            return null;
        }

        public InputAction Get(string map, string action)
        {
            if (_cache.TryGetValue((map, action), out var a)) return a;
            var m = _actions.FindActionMap(map, true);
            a = m.FindAction(action, true);
            _cache[(map, action)] = a;
            return a;
        }

        public string GetBindingLabel(string map, string action)
        {
            var m = _actions.FindActionMap(map);
            var a = m?.FindAction(action);
            return a == null ? "?" : BindingLabels.Get(a, ActiveFamily);
        }

        public void SaveBindings()
        {
            BindingPersistence.Save(_actions);
            NotifyBindingsChanged();
        }

        public void ResetBindings()
        {
            _actions.RemoveAllBindingOverrides();
            BindingPersistence.Delete();
            NotifyBindingsChanged();
        }

        public void NotifyBindingsChanged() => BindingsChanged?.Invoke();

        private void OnActionChange(object obj, InputActionChange change)
        {
            if (change != InputActionChange.ActionPerformed) return;
            if (!(obj is InputAction action) || action.activeControl == null) return;
            var device = action.activeControl.device;
            InputDeviceFamily family;
            if (device is Keyboard || device is Mouse) family = InputDeviceFamily.KeyboardMouse;
            else if (device is Gamepad gp) family = FamilyOf(gp);
            else return;
            SetFamily(family);
        }

        private static InputDeviceFamily FamilyOf(Gamepad gp)
        {
            if (gp is DualShockGamepad) return InputDeviceFamily.PlayStation;
            if (gp is XInputController) return InputDeviceFamily.Xbox;
            string n = (gp.description.manufacturer + " " + gp.description.product).ToLowerInvariant();
            if (n.Contains("sony") || n.Contains("dualsense") || n.Contains("playstation")) return InputDeviceFamily.PlayStation;
            if (n.Contains("xbox") || n.Contains("microsoft")) return InputDeviceFamily.Xbox;
            return InputDeviceFamily.Generic;
        }

        private void SetFamily(InputDeviceFamily family)
        {
            if (family == ActiveFamily) return;
            ActiveFamily = family;
            ActiveFamilyChanged?.Invoke(family);
        }

        /// <summary>Default state → action map table.</summary>
        public static List<StateContext> DefaultContexts() => new List<StateContext>
        {
            new StateContext { State = GameState.Boot, Maps = new[] { ActionMaps.UI } },
            new StateContext { State = GameState.Menu, Maps = new[] { ActionMaps.UI } },
            new StateContext { State = GameState.OnFoot, Maps = new[] { ActionMaps.OnFoot, ActionMaps.Interaction, ActionMaps.Firearms, ActionMaps.WeaponWheel, ActionMaps.PoliceRadio, ActionMaps.UI } },
            new StateContext { State = GameState.InVehicle, Maps = new[] { ActionMaps.Vehicle, ActionMaps.Interaction, ActionMaps.PoliceRadio, ActionMaps.UI } },
            new StateContext { State = GameState.PoliceComputer, Maps = new[] { ActionMaps.PoliceComputer, ActionMaps.UI } },
            new StateContext { State = GameState.WeaponWheel, Maps = new[] { ActionMaps.WeaponWheel, ActionMaps.UI } },
            new StateContext { State = GameState.Radio, InheritBase = true, Maps = new[] { ActionMaps.PoliceRadio, ActionMaps.UI } },
            new StateContext { State = GameState.Interaction, InheritBase = true, Maps = new[] { ActionMaps.Interaction, ActionMaps.UI } },
            new StateContext { State = GameState.Paused, Maps = new[] { ActionMaps.UI } },
        };
    }
}
