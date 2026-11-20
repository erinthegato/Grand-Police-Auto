using System.Collections.Generic;
using GPA.Core;
using GPA.Input;
using GPA.Vehicles;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.TopDown2D
{
    /// <summary>
    /// The player officer on foot in the 2D city. Twin-stick: the left stick / WASD moves relative
    /// to the screen, the right stick or mouse sets facing (otherwise the officer faces where they
    /// walk). Interact gets into the nearest car; the vehicle's Exit action gets back out.
    /// Implements <see cref="IPlayerAvatar"/> so HUD, callouts and vehicles see the same player
    /// contract as the 3D build.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [DefaultExecutionOrder(-100)]
    public sealed class Officer2D : MonoBehaviour, IPlayerAvatar, IDamageable
    {
        [Header("Movement (m/s)")]
        [SerializeField] private float _walkSpeed = 2.4f;
        [SerializeField] private float _sprintSpeed = 6f;
        [SerializeField] private float _acceleration = 22f;
        [SerializeField] private float _turnSpeed = 900f;
        [Tooltip("Right-stick aim easing: time to settle on a new direction (s) and top turn speed (deg/s).")]
        [SerializeField] private float _stickAimSmoothing = 0.08f;
        [SerializeField] private float _stickAimMaxSpeed = 600f;
        private float _aimTurnVelocity;
        [SerializeField] private float _aimDeadzone = 0.3f;

        [Header("Stamina")]
        [Tooltip("Seconds of full sprint from a full bar.")]
        [SerializeField] private float _sprintDuration = 7f;
        [Tooltip("Seconds to refill from empty, once recovery starts.")]
        [SerializeField] private float _staminaRefill = 5f;
        [SerializeField] private float _recoveryDelay = 1.2f;
        [Tooltip("After running dry, sprint unlocks again at this fraction.")]
        [SerializeField] private float _winded = 0.3f;

        [Header("Vehicles")]
        [SerializeField] private float _enterRange = 3.2f;
        [Tooltip("Vehicle input bridge reused from the 3D build (reads the Vehicle action map).")]
        [SerializeField] private PlayerVehicleInput _vehicleInput;

        [Header("Visuals")]
        [SerializeField] private SpriteRenderer[] _renderers = new SpriteRenderer[0];

        private readonly HashSet<object> _locks = new HashSet<object>();
        private Rigidbody2D _rb;
        private Collider2D _collider;
        private IInputService _input;
        private IGameStateService _state;
        private InputAction _move, _look, _sprint, _interact, _exit;
        private Vector2 _facing = Vector2.up;
        private bool _mouseAiming;
        private float _vehicleCooldown;
        private Car2D _car;
        private float _recoverWait;
        private bool _exhausted;

        public Transform Transform => transform;
        public Camera Camera => Camera.main;
        public IInteractor Interactor => null;
        public IVehicle CurrentVehicle => _car;
        public bool IsFirstPerson => false;
        public Car2D CurrentCar => _car;
        /// <summary>What Interact would use right now (person/object first, then a car). For HUD prompts.</summary>
        public string InteractPrompt { get; private set; }
        /// <summary>A menu or scripted moment has taken the controls (no attacking, no walking).</summary>
        public bool ControlLocked => _locks.Count > 0;
        public Vector2 Facing => _facing;
        /// <summary>0..1 sprint stamina.</summary>
        public float Stamina { get; private set; } = 1f;
        /// <summary>Set by the needs system: below 1 the officer runs out of breath sooner and recovers slower.</summary>
        public float StaminaScale = 1f;
        /// <summary>Set by the needs system: below 1 the officer walks and runs a little slower (tired / hurt).</summary>
        public float SpeedScale = 1f;
        /// <summary>Raised when something hurts the officer (the needs system keeps health).
        /// Never raised while the officer is inside a vehicle: you can't be hurt in a car.</summary>
        public static event System.Action<DamageInfo> Damaged;
        public bool IsAlive => true;
        public void ApplyDamage(in DamageInfo info)
        {
            if (_car != null) return;
            if (info.Type == DamageType.Bullet || info.Type == DamageType.Melee)
                BloodFx2D.Spray(info.Point, info.Direction, info.Type == DamageType.Bullet ? 8 : 3, info.Type == DamageType.Bullet ? 1f : 0.5f);
            Damaged?.Invoke(info);
        }
        public bool IsSprinting { get; private set; }
        /// <summary>Sprint is a toggle: press to run, press again (or stop / run out of stamina) to walk.</summary>
        private bool _sprintToggled;
        /// <summary>Ran the bar dry: can't sprint until it recovers a little.</summary>
        public bool Exhausted => _exhausted;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _collider = GetComponent<Collider2D>();
            if (_vehicleInput == null) _vehicleInput = GetComponent<PlayerVehicleInput>();
            if (_vehicleInput == null) _vehicleInput = gameObject.AddComponent<PlayerVehicleInput>();
            Services.Register<IPlayerAvatar>(this);
        }

        private void OnDestroy() => Services.Unregister<IPlayerAvatar>(this);

        private void Start()
        {
            Services.TryGet(out _state);
            if (!Services.TryGet(out _input))
            {
                Debug.LogError("[Officer2D] No IInputService. Add the GameSystems prefab to the scene.", this);
                enabled = false;
                return;
            }
            _move = _input.Get(ActionMaps.OnFoot, "Move");
            _look = _input.Get(ActionMaps.OnFoot, "Look");
            _sprint = _input.Get(ActionMaps.OnFoot, "Sprint");
            _interact = _input.Get(ActionMaps.Interaction, "Interact");
            _exit = _input.Get(ActionMaps.Vehicle, "ExitVehicle");
            _state?.SetBase(GameState.OnFoot);
        }

        private void Update()
        {
            if (_input == null) return;
            _vehicleCooldown -= Time.deltaTime;

            if (_car != null)
            {
                InteractPrompt = null;
                _vehicleInput.Sample();
                transform.position = _car.transform.position;
                bool canExit = _car.CanExit(out string blocked);
                if (!canExit) InteractPrompt = blocked;
                if (_exit.WasPerformedThisFrame() && _vehicleCooldown <= 0f && canExit) ExitVehicle(_car.DriverExitPoint, _car.transform.rotation);
                return;
            }

            if (_sprint.WasPressedThisFrame() && _locks.Count == 0) _sprintToggled = !_sprintToggled;

            Interactable2D target = Interactable2D.FindNearest(transform.position);
            Car2D nearestCar = target == null ? FindNearestCar() : null;
            InteractPrompt = target != null ? target.Prompt : nearestCar != null ? "Enter vehicle" : null;

            if (_interact.WasPerformedThisFrame() && _vehicleCooldown <= 0f && _locks.Count == 0)
            {
                if (target != null) { target.Use(); return; }
                if (nearestCar != null) { InteractPrompt = null; EnterVehicle(nearestCar, nearestCar.transform); return; }
            }

            UpdateFacing();
        }

        private void FixedUpdate()
        {
            if (_car != null || _input == null) return;
            bool free = _locks.Count == 0 && _move.enabled;
            Vector2 move = free ? Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f) : Vector2.zero;
            float dt = Time.fixedDeltaTime;
            if (!free || _exhausted || (IsSprinting && move.sqrMagnitude <= 0.04f)) _sprintToggled = false;   // stopping after a run ends it
            IsSprinting = free && _sprintToggled && !_exhausted && move.sqrMagnitude > 0.04f && Stamina > 0f;
            if (IsSprinting)
            {
                Stamina = Mathf.Max(0f, Stamina - dt / (_sprintDuration * Mathf.Max(0.2f, StaminaScale)));
                _recoverWait = _recoveryDelay;
                if (Stamina <= 0f) _exhausted = true;
            }
            else if ((_recoverWait -= dt) <= 0f)
            {
                Stamina = Mathf.Min(1f, Stamina + dt / _staminaRefill * (move.sqrMagnitude > 0.04f ? 0.6f : 1f) * StaminaScale);
                if (_exhausted && Stamina >= _winded) _exhausted = false;
            }
            float speed = (IsSprinting ? _sprintSpeed : _exhausted ? _walkSpeed * 0.8f : _walkSpeed) * SpeedScale;
            _rb.linearVelocity = Vector2.MoveTowards(_rb.linearVelocity, move * speed, _acceleration * Time.fixedDeltaTime);
        }

        private OfficerEquipment2D _equipment;
        private bool _wheelAimLock, _lookHeldUntilCentred;

        private void UpdateFacing()
        {
            // The weapon wheel uses the aim stick / mouse to pick: freeze the officer's aim while it is
            // open, and after it closes ignore the stick until it is released (the mouse cursor is put
            // back on the current aim) so the pick doesn't swing the officer round.
            if (_equipment == null) TryGetComponent(out _equipment);
            if (_equipment != null && _equipment.WheelOpen) { _wheelAimLock = true; return; }
            if (_wheelAimLock)
            {
                _wheelAimLock = false;
                _lookHeldUntilCentred = true;
                Camera aimCam = Camera;
                if (Mouse.current != null && aimCam != null)
                {
                    Vector3 sp = aimCam.WorldToScreenPoint(transform.position + (Vector3)(_facing * 4f));
                    Mouse.current.WarpCursorPosition(new Vector2(sp.x, sp.y));
                }
            }

            Vector2 aim = Vector2.zero;
            bool stickAim = false;
            if (_input.ActiveFamily == InputDeviceFamily.KeyboardMouse && Mouse.current != null)
            {
                if (Mouse.current.delta.ReadValue().sqrMagnitude > 0.5f) _mouseAiming = true;
                Camera cam = Camera;
                if (_mouseAiming && cam != null)
                {
                    Vector3 world = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
                    aim = (Vector2)(world - transform.position);
                }
            }
            else
            {
                _mouseAiming = false;
                Vector2 stick = _look.enabled ? _look.ReadValue<Vector2>() : Vector2.zero;
                if (_lookHeldUntilCentred) { if (stick.magnitude < _aimDeadzone) _lookHeldUntilCentred = false; else stick = Vector2.zero; }
                if (stick.magnitude > _aimDeadzone) { aim = stick; stickAim = true; }
            }

            if (aim.sqrMagnitude < 0.01f && _rb.linearVelocity.sqrMagnitude > 0.2f) aim = _rb.linearVelocity;
            if (aim.sqrMagnitude > 0.0001f)
            {
                if (stickAim)
                {
                    // Right-stick aim follows the stick quickly but eases in, instead of snapping to it.
                    float current = Mathf.Atan2(_facing.y, _facing.x) * Mathf.Rad2Deg;
                    float wanted = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg;
                    float a = Mathf.SmoothDampAngle(current, wanted, ref _aimTurnVelocity, _stickAimSmoothing, _stickAimMaxSpeed);
                    _facing = new Vector2(Mathf.Cos(a * Mathf.Deg2Rad), Mathf.Sin(a * Mathf.Deg2Rad));
                }
                else { _facing = aim.normalized; _aimTurnVelocity = 0f; }   // mouse aim and walking stay direct
            }

            float targetAngle = Mathf.Atan2(_facing.y, _facing.x) * Mathf.Rad2Deg - 90f; // sprites face +Y
            float angle = Mathf.MoveTowardsAngle(transform.eulerAngles.z, targetAngle, _turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        private Car2D FindNearestCar()
        {
            Car2D best = null;
            float bestDistance = _enterRange;
            foreach (Car2D car in FindObjectsByType<Car2D>(FindObjectsInactive.Exclude))
            {
                if (car.IsAIDriven) continue; // can't jump into a car someone is driving
                float d = Vector2.Distance(car.transform.position, transform.position);
                if (d < bestDistance) { best = car; bestDistance = d; }
            }
            return best;
        }

        public void EnterVehicle(IVehicle vehicle, Transform seat)
        {
            if (!(vehicle is Car2D car) || _car != null) return;
            _car = car;
            _vehicleCooldown = 0.4f;
            _rb.linearVelocity = Vector2.zero;
            _rb.simulated = false;
            SetVisible(false);
            CarDoors2D.Swing(car, CarDoors2D.Door.DriverFront, 0.12f);   // open, hop in, shut
            car.SetPlayerDriven(true, _vehicleInput);
            _state?.SetBase(GameState.InVehicle);
            GameEvents.Publish(new PlayerVehicleChangedEvent(car));
        }

        public void ExitVehicle(Vector3 position, Quaternion rotation)
        {
            if (_car == null) return;
            Car2D car = _car;
            _car = null;
            _vehicleCooldown = 0.4f;
            car.SetPlayerDriven(false, null);
            CarDoors2D.Swing(car, CarDoors2D.Door.DriverFront, 0.45f);   // open, step out, shut
            transform.SetPositionAndRotation(new Vector3(position.x, position.y, 0f), Quaternion.Euler(0f, 0f, rotation.eulerAngles.z));
            _rb.position = position;
            _rb.simulated = true;
            SetVisible(true);
            _state?.SetBase(GameState.OnFoot);
            GameEvents.Publish(new PlayerVehicleChangedEvent(null));
        }

        public void SetControlLocked(object owner, bool locked)
        {
            if (owner == null) return;
            if (locked) _locks.Add(owner); else _locks.Remove(owner);
        }

        private void SetVisible(bool visible)
        {
            foreach (var r in _renderers) if (r != null) r.enabled = visible;
            if (_collider != null) _collider.enabled = visible;
        }
    }
}
