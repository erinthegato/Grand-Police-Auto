using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// GTA 1/2-style overhead camera: orthographic, follows the officer or their car, zooms out with
    /// speed and leads in the direction of travel so you can see what is coming.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    [RequireComponent(typeof(Camera))]
    public sealed class Camera2DFollow : MonoBehaviour
    {
        [SerializeField] private float _onFootSize = 7.5f;
        [SerializeField] private float _vehicleSize = 10f;
        [Tooltip("On-foot size while sprinting (pulls back so you see further ahead).")]
        [SerializeField] private float _sprintSize = 9.5f;
        [Tooltip("Extra orthographic size per km/h.")]
        [SerializeField] private float _sizePerKmh = 0.06f;
        [SerializeField] private float _maxSize = 22f;
        [SerializeField] private float _zoomSmoothing = 1.4f;
        [SerializeField] private float _lookAheadSeconds = 0.7f;
        [SerializeField] private float _maxLookAhead = 14f;
        [SerializeField] private float _followSmoothing = 7f;
        [SerializeField] private float _leadSmoothing = 2.2f;

        private Camera _camera;
        private IPlayerAvatar _avatar;
        private Vector2 _focus, _lead;
        private bool _snapped;

        /// <summary>Length of the GTA-style drop-in (start of game / start of shift), seconds.</summary>
        public const float IntroSeconds = 2.6f;
        private const float IntroHeight = 75f, IntroRoll = 18f;
        private static float _introT = -1f;

        /// <summary>Drop the camera in from high above onto the player.</summary>
        public static void PlayIntro() { _introT = 0f; }

        /// <summary>Jump straight to the target next frame (after a teleport) instead of gliding there.</summary>
        public static void SnapAll() { foreach (var c in FindObjectsByType<Camera2DFollow>(FindObjectsInactive.Exclude)) c._snapped = false; }

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = _onFootSize;
        }

        private void LateUpdate()
        {
            if (_avatar == null && !Services.TryGet(out _avatar)) return;
            float dt = Time.deltaTime;
            IVehicle vehicle = _avatar.CurrentVehicle;
            Transform target = vehicle != null ? vehicle.Transform : _avatar.Transform;
            if (target == null) return;

            Vector2 velocity = vehicle != null ? (Vector2)vehicle.Velocity : Vector2.zero;
            float kmh = velocity.magnitude * 3.6f;
            bool sprinting = vehicle == null && _avatar is Officer2D officer && officer.IsSprinting;
            float size = vehicle != null ? Mathf.Min(_vehicleSize + kmh * _sizePerKmh, _maxSize) : sprinting ? _sprintSize : _onFootSize;
            if (vehicle == null && _avatar is Officer2D runner && runner.TryGetComponent(out Rigidbody2D body))
                velocity = body.linearVelocity * 0.6f;   // gentle lead in the running direction on foot
            Vector2 lead = Vector2.ClampMagnitude(velocity * _lookAheadSeconds, _maxLookAhead);

            if (!_snapped) { _focus = target.position; _lead = lead; _camera.orthographicSize = size; _snapped = true; }
            _focus = Vector2.Lerp(_focus, target.position, 1f - Mathf.Exp(-_followSmoothing * dt));
            _lead = Vector2.Lerp(_lead, lead, 1f - Mathf.Exp(-_leadSmoothing * dt));
            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, size, 1f - Mathf.Exp(-_zoomSmoothing * dt));

            float roll = 0f;
            if (_introT >= 0f)
            {
                _introT += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(_introT / IntroSeconds);
                float e = 1f - Mathf.Pow(1f - k, 3f);                 // fast at first, settling gently
                _camera.orthographicSize = Mathf.Lerp(IntroHeight, size, e);
                roll = Mathf.Lerp(IntroRoll, 0f, e);
                _focus = target.position; _lead = Vector2.zero;
                if (k >= 1f) _introT = -1f;
            }
            Vector2 p = _focus + _lead;
            transform.SetPositionAndRotation(new Vector3(p.x, p.y, -20f), Quaternion.Euler(0f, 0f, roll));
        }
    }
}
