using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>The officer's footsteps (faster when sprinting). Car door sounds come from <see cref="CarDoors2D"/>.</summary>
    [RequireComponent(typeof(Officer2D))]
    public sealed class OfficerAudio2D : MonoBehaviour
    {
        private Officer2D _officer;
        private Rigidbody2D _body;
        private SoundLibrary _lib;
        private AudioSource _steps;

        private void Start()
        {
            _officer = GetComponent<Officer2D>();
            _body = GetComponent<Rigidbody2D>();
            _lib = GameAudio.Library;
            if (_lib == null) { enabled = false; return; }
            _steps = GameAudio.MakeLoop(gameObject, _lib.Footsteps);
        }

        private void Update()
        {
            Car2D car = _officer.CurrentCar;
            float speed = car == null && _body != null ? _body.linearVelocity.magnitude : 0f;
            bool walking = speed > 0.6f && !_officer.ControlLocked;
            if (_steps != null)
            {
                _steps.pitch = _officer.IsSprinting ? 1.45f : Mathf.Clamp(speed / 3.5f, 0.85f, 1.2f);
                GameAudio.SetLoop(_steps, walking ? 0.35f : 0f, 0f);
            }
        }
    }
}
