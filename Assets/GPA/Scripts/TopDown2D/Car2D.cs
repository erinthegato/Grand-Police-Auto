using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Top-down car with GTA 1-style arcade handling. The car turns at a fixed rate while you
    /// steer (scaled down only when crawling, so it can't spin on the spot), speeds up and stops
    /// quickly, and grips hard: sideways drift is bled off every step. The handbrake drops that
    /// grip so the tail swings out for tight turns. Collisions are still real 2D physics.
    /// The car faces +Y in local space. 1 unit = 1 m.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Car2D : MonoBehaviour, IVehicle
    {
        [Header("Identity")]
        [SerializeField] private string _plate = "4127";
        [SerializeField] private string _description = "Patrol Car";
        [SerializeField] private bool _emergencyVehicle;

        [Header("Visuals")]
        [Tooltip("Body sprite transform (lightbar glows are parented to it).")]
        [SerializeField] private Transform _body;

        [Header("Speed (m/s and m/s²)")]
        [SerializeField] private float _mass = 1500f;
        [SerializeField] private float _topSpeed = 34f;        // ~122 km/h
        [SerializeField] private float _reverseTopSpeed = 9f;
        [SerializeField] private float _acceleration = 7.5f;     // gentler pull-away (was 13)
        [SerializeField] private float _braking = 26f;
        [SerializeField] private float _reverseAcceleration = 8f;
        [Tooltip("Slow-down per second with no throttle or brake.")]
        [SerializeField] private float _coastDrag = 3.5f;

        [Header("Steering")]
        [Tooltip("Degrees per second the car rotates at full steer.")]
        [SerializeField] private float _turnRate = 190f;
        [Tooltip("Turn rate multiplier at top speed (1 = same as low speed).")]
        [SerializeField] private float _topSpeedTurnScale = 0.7f;
        [Tooltip("Speed (m/s) at which full turn rate is reached. Below it the car turns proportionally slower.")]
        [SerializeField] private float _fullTurnSpeed = 6f;

        [Header("Grip")]
        [Tooltip("How fast sideways slide is cancelled (per second). Higher = more on-rails.")]
        [SerializeField] private float _grip = 9f;
        [Tooltip("Grip while the handbrake is held.")]
        [SerializeField] private float _handbrakeGrip = 1.2f;
        [Tooltip("Extra turn rate while the handbrake is held, for sharp swing-arounds.")]
        [SerializeField] private float _handbrakeTurnBoost = 1.35f;
        [SerializeField] private float _handbrakeDrag = 10f;

        private Rigidbody2D _rb;
        private IVehicleControlInput _input = IdleVehicleControl.Instance;

        private string _recordId;

        public string VehicleRecordId => _recordId;
        public string Plate => _plate;
        public string Description => _description;
        /// <summary>Citizen record of whoever is behind the wheel (civilian traffic); null for the player's car.</summary>
        public string DriverCitizenId { get; private set; }

        /// <summary>Links this car to its database record (plate, description, driver).</summary>
        public void AssignRecord(string vehicleId, string plate, string description, string driverCitizenId)
        {
            _recordId = vehicleId;
            if (!string.IsNullOrEmpty(plate)) _plate = plate;
            if (!string.IsNullOrEmpty(description)) _description = description;
            DriverCitizenId = driverCitizenId;
        }
        public Transform Transform => transform;
        public Vector3 Velocity => _rb != null ? (Vector3)_rb.linearVelocity : Vector3.zero;
        public bool IsEmergencyVehicle => _emergencyVehicle;
        public bool IsPlayerDriven { get; private set; }
        public bool IsAIDriven => !IsPlayerDriven && !ReferenceEquals(_input, IdleVehicleControl.Instance);

        public float ForwardSpeed { get; private set; }
        public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;
        /// <summary>0..1 engine effort, for audio and haptics.</summary>
        public float EngineLoad { get; private set; }
        /// <summary>0..1 how much the car is sliding sideways (handbrake turns, impacts).</summary>
        public float RearSlide { get; private set; }
        public float FrontSlide => 0f;
        /// <summary>Where the driver steps out (left of the car, world space).</summary>
        public Vector3 DriverExitPoint => transform.TransformPoint(new Vector3(-2.0f, 0.3f, 0f));

        public IVehicleControlInput Input
        {
            get => _input;
            set => _input = value ?? IdleVehicleControl.Instance;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _rb.gravityScale = 0f;
            _rb.mass = _mass;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0f;
            _rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            if (GetComponent<CarLights2D>() == null) gameObject.AddComponent<CarLights2D>();
            if (GetComponent<VehicleAudio2D>() == null) gameObject.AddComponent<VehicleAudio2D>();
            if (GetComponent<CarFx2D>() == null) gameObject.AddComponent<CarFx2D>();
        }

        /// <summary>The catalog type this car was built as (null for scene-placed cars).</summary>
        public VehicleSpec Spec { get; private set; }
        /// <summary>Hovering vehicles (helicopter) turn on the spot and have no reverse limit.</summary>
        public bool Hover { get; private set; }

        /// <summary>
        /// Re-skins and re-tunes this car as a catalog vehicle: body sprite, collider, shadow, lightbar
        /// position and handling. Works on traffic, the player's fleet vehicles and backup units.
        /// </summary>
        public void ApplySpec(VehicleSpec spec, Sprite body = null)
        {
            if (spec == null) return;
            Spec = spec;
            _mass = spec.Mass; _topSpeed = spec.TopSpeed; _acceleration = spec.Acceleration;
            _braking = spec.Braking; _turnRate = spec.TurnRate; _grip = spec.Grip;
            _reverseTopSpeed = spec.Helicopter ? spec.TopSpeed * 0.5f : spec.Motorcycle ? 5f : 9f;
            _reverseAcceleration = spec.Helicopter ? spec.Acceleration : 8f;
            Hover = spec.Helicopter;
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            if (_rb != null) _rb.mass = _mass;
            _emergencyVehicle = spec.Role == VehicleRole.Police;

            var col = GetComponent<BoxCollider2D>();
            if (col != null) col.size = new Vector2(spec.Width * 0.92f, spec.Length * 0.96f);

            Sprite sprite = body != null ? body : spec.Sprites.Length > 0 ? spec.Sprites[0] : null;
            foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true))
            {
                string n = sr.name;
                if (n == "Body" && sprite != null) sr.sprite = sprite;
                else if (n.Contains("shadow")) sr.transform.localScale = new Vector3(spec.Width / 2.3f, spec.Length / 4.6f, 1f);
                else if (n.StartsWith("glow_"))
                {
                    float side = n.Contains("red") ? -1f : 1f;
                    sr.transform.localPosition = new Vector3(side * spec.LightbarHalfWidth, spec.LightbarForward, 0f);
                    sr.gameObject.SetActive(spec.Lightbar);
                }
            }
            if (spec.Helicopter)
            {
                var heli = GetComponent<HelicopterFx2D>();
                if (heli == null) heli = gameObject.AddComponent<HelicopterFx2D>();
                Transform shadow = null;
                foreach (var sr in GetComponentsInChildren<SpriteRenderer>(true)) if (sr.name.Contains("shadow")) shadow = sr.transform;
                heli.Setup(spec.RotorSprite, shadow);
            }
        }

        /// <summary>Whether the officer can get out here (a helicopter needs a clear spot on the ground).</summary>
        private static CityLocations _city;

        public bool CanExit(out string reason)
        {
            reason = null;
            if (!Hover) return true;
            if (Mathf.Abs(ForwardSpeed) > 3f) { reason = "Slow down to land"; return false; }
            foreach (var c in Physics2D.OverlapCircleAll(DriverExitPoint, 0.6f))
                if (!c.isTrigger && c.attachedRigidbody != _rb) { reason = "No room to land here — find open ground"; return false; }
            if (_city == null) _city = FindAnyObjectByType<CityLocations>();
            if (_city != null && _city.Database != null && !_city.Database.IsLand(transform.position)) { reason = "Can't land on the water"; return false; }
            return true;
        }

        public void SetPlayerDriven(bool driven, IVehicleControlInput input)
        {
            IsPlayerDriven = driven;
            Input = driven ? input : IdleVehicleControl.Instance;
            // Any car the player takes (personal, parked civilian, fleet) gets controller rumble.
            if (driven && GetComponent<Car2DHaptics>() == null) gameObject.AddComponent<Car2DHaptics>();
        }

        public void Park() => SetPlayerDriven(false, null);

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            bool parked = !IsPlayerDriven && ReferenceEquals(_input, IdleVehicleControl.Instance);
            float throttle = parked ? 0f : Mathf.Clamp01(_input.Throttle);
            float brake = parked ? 0f : Mathf.Clamp01(_input.Brake);
            float steer = parked ? 0f : Mathf.Clamp(_input.Steer, -1f, 1f);
            bool handbrake = !parked && _input.Handbrake;

            Vector2 forward = transform.up;
            Vector2 right = transform.right;
            Vector2 velocity = _rb.linearVelocity;
            float speed = Vector2.Dot(velocity, forward);
            float lateral = Vector2.Dot(velocity, right);

            // ---- Speed: simple accelerate / brake / reverse.
            if (throttle > 0.05f)
            {
                if (speed < 0f) speed = Mathf.MoveTowards(speed, 0f, _braking * throttle * dt);
                else speed = Mathf.MoveTowards(speed, _topSpeed, _acceleration * throttle * dt);
            }
            else if (brake > 0.05f)
            {
                if (speed > 0.3f) speed = Mathf.MoveTowards(speed, 0f, _braking * brake * dt);
                else speed = Mathf.MoveTowards(speed, -_reverseTopSpeed, _reverseAcceleration * brake * dt);
            }
            else
            {
                float drag = parked ? _braking : _coastDrag;
                speed = Mathf.MoveTowards(speed, 0f, drag * dt);
            }
            if (handbrake) speed = Mathf.MoveTowards(speed, 0f, _handbrakeDrag * dt);
            EngineLoad = Mathf.Clamp01(throttle * 0.7f + Mathf.Abs(speed) / _topSpeed * 0.3f);

            // ---- Turning: fixed rate, reversed when backing up, faded in from a standstill.
            float moving = Hover ? 1f : Mathf.Clamp01(Mathf.Abs(speed) / _fullTurnSpeed);
            float speedScale = Mathf.Lerp(1f, _topSpeedTurnScale, Mathf.Clamp01(Mathf.Abs(speed) / _topSpeed));
            float rate = -steer * _turnRate * moving * speedScale * Mathf.Sign(speed == 0f ? 1f : speed);
            if (handbrake) rate *= _handbrakeTurnBoost;
            _rb.angularVelocity = rate;

            // ---- Grip: bleed off sideways slide.
            lateral *= Mathf.Exp(-(handbrake ? _handbrakeGrip : _grip) * dt);
            RearSlide = Mathf.Clamp01(Mathf.Abs(lateral) / 6f);

            _rb.linearVelocity = forward * speed + right * lateral;
            ForwardSpeed = speed;
        }
    }
}
