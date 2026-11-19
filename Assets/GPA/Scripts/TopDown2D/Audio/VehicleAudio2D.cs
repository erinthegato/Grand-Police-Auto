using GPA.Core;
using GPA.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Sounds of one car: engine (pitch follows speed and throttle), tyre squeal while sliding, crash on
    /// hard impacts, horn, and on police cars the siren. The player controls the siren with the
    /// Vehicle/Siren action (Q / RB), changes tone with Vehicle/SirenTone (T / R-stick), and honks with
    /// Vehicle/Horn (H / L-stick; the police air horn sounds while held). Added automatically by <see cref="Car2D"/>.
    /// </summary>
    [RequireComponent(typeof(Car2D))]
    public sealed class VehicleAudio2D : MonoBehaviour
    {
        public enum SirenTone { Wail, Yelp, Priority }

        private Car2D _car;
        private Lightbar2D _lightbar;
        private SoundLibrary _lib;
        private AudioSource _engine, _skid, _siren, _airHorn;
        private InputAction _sirenAction, _toneAction, _hornAction;
        private float _crashCooldown;

        public bool SirenOn { get; private set; }
        public SirenTone Tone { get; private set; }

        private void Awake()
        {
            _car = GetComponent<Car2D>();
            _lightbar = GetComponent<Lightbar2D>();
        }

        private void Start()
        {
            _lib = GameAudio.Library;
            if (_lib == null) { enabled = false; return; }
            _engine = GameAudio.MakeLoop(gameObject, _lib.Engine);
            _skid = GameAudio.MakeLoop(gameObject, _lib.Skid);
            if (_car.IsEmergencyVehicle) _siren = GameAudio.MakeLoop(gameObject, _lib.SirenWail);
            if (_car.IsEmergencyVehicle) _airHorn = GameAudio.MakeLoop(gameObject, _lib.PoliceHorn);   // held, not one-shot
            if (Services.TryGet(out IInputService input))
            {
                _sirenAction = TryGet(input, "Siren");
                _toneAction = TryGet(input, "SirenTone");
                _hornAction = TryGet(input, "Horn");
            }
        }

        private static InputAction TryGet(IInputService input, string action)
        {
            try { return input.Get(ActionMaps.Vehicle, action); }
            catch { return null; }
        }

        private void Update()
        {
            GameAudio.Spatial(transform.position, out float gain, out float pan);
            float speed01 = Mathf.Clamp01(Mathf.Abs(_car.ForwardSpeed) / 34f);
            bool running = _car.IsPlayerDriven || _car.IsAIDriven;

            if (_engine != null)
            {
                float baseVolume = _car.IsPlayerDriven ? 0.5f : 0.26f;
                GameAudio.SetLoop(_engine, running ? baseVolume * (0.55f + 0.45f * _car.EngineLoad) * gain : 0f, pan);
                _engine.pitch = 0.75f + speed01 * 1.0f + _car.EngineLoad * 0.15f;
            }

            if (_skid != null)
            {
                float slide = _car.RearSlide * Mathf.Clamp01(Mathf.Abs(_car.ForwardSpeed) / 6f);
                GameAudio.SetLoop(_skid, slide > 0.25f ? (slide - 0.25f) / 0.75f * 0.6f * gain : 0f, pan);
            }

            if (_car.IsPlayerDriven && !GameplayHotkeys.Blocked)
            {
                if (_siren != null && _sirenAction != null && _sirenAction.WasPerformedThisFrame()) SetSiren(!SirenOn);
                if (_siren != null && SirenOn && _toneAction != null && _toneAction.WasPerformedThisFrame())
                    SetTone((SirenTone)(((int)Tone + 1) % 3));
                if (_airHorn == null && _hornAction != null && _hornAction.WasPressedThisFrame())
                    GameAudio.PlayAt(_lib.CivilianHorn, transform.position, 0.6f, 0f);
            }

            if (_siren != null) GameAudio.SetLoop(_siren, SirenOn ? 0.55f * gain : 0f, pan);
            bool hornHeld = _car.IsPlayerDriven && !GameplayHotkeys.Blocked && _hornAction != null && _hornAction.IsPressed();
            if (_airHorn != null) GameAudio.SetLoop(_airHorn, hornHeld ? 0.7f * gain : 0f, pan);
            if (_crashCooldown > 0f) _crashCooldown -= Time.deltaTime;
        }

        public void SetSiren(bool on)
        {
            if (_siren == null || SirenOn == on) return;
            SirenOn = on;
            bool lights = _lightbar != null && _lightbar.On;
            GameEvents.Publish(new EmergencySignalChangedEvent(_car, lights ? EmergencyLightMode.Stage3 : EmergencyLightMode.Off, on));
        }

        public void SetTone(SirenTone tone)
        {
            if (_siren == null) return;
            Tone = tone;
            AudioClip clip = tone == SirenTone.Yelp ? _lib.SirenYelp : tone == SirenTone.Priority ? _lib.SirenPriority : _lib.SirenWail;
            if (clip == null || clip == _siren.clip) return;
            _siren.clip = clip;
            _siren.Play();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (_lib == null || _crashCooldown > 0f) return;
            float impact = collision.relativeVelocity.magnitude;
            if (impact < 4f || collision.collider.GetComponentInParent<Ped2D>() != null) return;
            // Car-on-car: only one of the two cars plays the crash.
            var other = collision.collider.GetComponentInParent<VehicleAudio2D>();
            if (other != null && other.GetHashCode() < GetHashCode()) return;
            _crashCooldown = 0.35f;
            float volume = 0.08f + Mathf.Clamp01((impact - 4f) / 14f) * 0.27f;   // crashes were far too loud
            GameAudio.PlayAt(_lib.Crash, collision.GetContact(0).point, volume, 0.1f);
        }
    }
}
