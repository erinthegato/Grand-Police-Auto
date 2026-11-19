using GPA.Core;
using UnityEngine;

namespace GPA.TopDown2D
{
    /// <summary>
    /// Game sound effects for the top-down world. One-shots play from a small pool; volume falls off
    /// with distance from the camera and sounds pan left/right, so a gunshot across the map is faint
    /// and off to the side. Clips come from <see cref="SoundLibrary"/> in Resources; a missing clip is
    /// silently skipped. Also starts the city ambience and adds <see cref="OfficerAudio2D"/> to the officer
    /// when a scene loads. Cars add <see cref="VehicleAudio2D"/> themselves.
    /// </summary>
    public static class GameAudio
    {
        /// <summary>Beyond this distance (m) from the camera a sound is inaudible.</summary>
        public const float HearingRange = 60f;
        private const float FullVolumeRange = 9f;

        private static SoundLibrary _library;
        private static bool _loaded;
        private static AudioRunner _runner;

        public static SoundLibrary Library
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _library = Resources.Load<SoundLibrary>("SoundLibrary");
                    if (_library == null) Debug.LogWarning("[Audio] Resources/SoundLibrary asset not found; the game will be silent.");
                }
                return _library;
            }
        }

        /// <summary>Distance gain (0..1) and stereo pan (-1..1) of a world point, heard from the camera.</summary>
        public static void Spatial(Vector2 point, out float gain, out float pan)
        {
            Camera cam = Camera.main;
            if (cam == null) { gain = 1f; pan = 0f; return; }
            Vector2 d = point - (Vector2)cam.transform.position;
            gain = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FullVolumeRange, HearingRange, d.magnitude));
            pan = Mathf.Clamp(d.x / 25f, -0.75f, 0.75f);
        }

        public static void PlayAt(AudioClip clip, Vector2 point, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (clip == null) return;
            Spatial(point, out float gain, out float pan);
            if (gain <= 0.01f) return;
            Runner.Play(clip, volume * gain, pan, 1f + Random.Range(-pitchJitter, pitchJitter));
        }

        public static void Play2D(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip != null) Runner.Play(clip, volume, 0f, pitch);
        }

        public static void UiClick() => Play2D(Library != null ? Library.UiClick : null, 0.05f);

        /// <summary>Drives a looping source: plays it while audible, pauses it (keeping its place) when silent.</summary>
        public static void SetLoop(AudioSource source, float volume, float pan)
        {
            if (source == null) return;
            source.volume = volume;
            source.panStereo = pan;
            if (volume > 0.005f)
            {
                if (source.isPlaying) return;
                source.UnPause();
                if (!source.isPlaying)
                {
                    source.Play();
                    if (source.clip != null) source.time = Random.value * source.clip.length * 0.9f;
                }
            }
            else if (source.isPlaying) source.Pause();
        }

        /// <summary>A looping 2D source on <paramref name="host"/> (volume and pan are set every frame by its owner).</summary>
        public static AudioSource MakeLoop(GameObject host, AudioClip clip)
        {
            if (clip == null) return null;
            var s = host.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            s.dopplerLevel = 0f;
            s.volume = 0f;
            return s;
        }

        private static AudioRunner Runner
        {
            get
            {
                if (_runner == null)
                {
                    var go = new GameObject("Game Audio");
                    Object.DontDestroyOnLoad(go);
                    _runner = go.AddComponent<AudioRunner>();
                }
                return _runner;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            _loaded = false;   // re-read the library each play session (domain reload may be off)
            if (Object.FindAnyObjectByType<Officer2D>() == null) return;
            Runner.StartAmbience(Library != null ? Library.CityAmbience : null);
            foreach (Officer2D officer in Object.FindObjectsByType<Officer2D>(FindObjectsInactive.Exclude))
                if (officer.GetComponent<OfficerAudio2D>() == null) officer.gameObject.AddComponent<OfficerAudio2D>();
        }

        /// <summary>Pool of one-shot voices plus the ambience bed.</summary>
        private sealed class AudioRunner : MonoBehaviour
        {
            private const int Voices = 16;
            private AudioSource[] _pool;
            private AudioSource _ambience;
            private int _next;

            private void Awake()
            {
                _pool = new AudioSource[Voices];
                for (int i = 0; i < Voices; i++)
                {
                    var s = gameObject.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 0f;
                    _pool[i] = s;
                }
            }

            public void Play(AudioClip clip, float volume, float pan, float pitch)
            {
                AudioSource s = null;
                for (int i = 0; i < Voices && s == null; i++)
                {
                    AudioSource c = _pool[(_next + i) % Voices];
                    if (!c.isPlaying) s = c;
                }
                if (s == null) s = _pool[_next];
                _next = (_next + 1) % Voices;
                s.clip = clip;
                s.volume = Mathf.Clamp01(volume);
                s.panStereo = pan;
                s.pitch = pitch;
                s.Play();
            }

            public void StartAmbience(AudioClip clip)
            {
                if (clip == null) return;
                if (_ambience == null)
                {
                    _ambience = gameObject.AddComponent<AudioSource>();
                    _ambience.loop = true;
                    _ambience.playOnAwake = false;
                    _ambience.spatialBlend = 0f;
                    _ambience.volume = 0.22f;
                }
                if (_ambience.clip == clip && _ambience.isPlaying) return;
                _ambience.clip = clip;
                _ambience.Play();
            }
        }
    }
}
