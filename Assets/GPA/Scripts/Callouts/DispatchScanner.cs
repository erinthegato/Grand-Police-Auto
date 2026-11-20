using System.Collections.Generic;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// The in-game police radio. Lines are queued and "read" one after another with a squelch
    /// chirp, light static and on-screen text. While it is transmitting it raises
    /// <see cref="DispatchTransmissionEvent"/> so the live scanner can duck out of the way.
    /// Voice lines can replace the text-only delivery later without changing callers.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class DispatchScanner : MonoBehaviour, IDispatchRadio
    {
        [SerializeField] private float _secondsPerLine = 3.2f;
        [SerializeField, Range(0f, 1f)] private float _volume = 0.55f;
        [SerializeField] private int _logLength = 7;

        private readonly List<(string text, TransmissionPriority priority)> _queue = new List<(string, TransmissionPriority)>();
        private readonly List<string> _log = new List<string>();
        private AudioSource _source;
        private AudioClip _chirpIn, _chirpOut, _static;
        private float _lineTimer;

        public string Unit { get; set; } = "4127";
        public bool Transmitting { get; private set; }
        public string CurrentLine { get; private set; }
        public IReadOnlyList<string> Log => _log;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            Services.Register<IDispatchRadio>(this);
            _chirpIn = Tone("chirp_in", new[] { 1850f, 2350f }, 0.09f);
            _chirpOut = Tone("chirp_out", new[] { 2350f, 1500f }, 0.12f);
            _static = Noise("static", 1.5f);
            var lib = GameAudio.Library;   // recorded radio sounds when available
            if (lib != null && lib.RadioChirp != null) _chirpIn = _chirpOut = lib.RadioChirp;
            // The recorded static clip (lib.RadioStatic) is placeholder chatter with babbling voices in
            // it, so the radio always uses the plain procedural hiss under the lines.
        }

        /// <summary>Queue lines to be read. {unit} is replaced with the player's unit number.</summary>
        public void Transmit(params string[] lines) => Transmit(TransmissionPriority.Routine, lines);

        /// <summary>
        /// Queue lines by priority: emergency and critical traffic goes ahead of routine messages
        /// (after whatever line is being read), equal priorities keep their order.
        /// </summary>
        public void Transmit(TransmissionPriority priority, params string[] lines)
        {
            int insert = _queue.Count;
            for (int i = 0; i < _queue.Count; i++)
                if (_queue[i].priority > priority) { insert = i; break; }
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                _queue.Insert(insert++, (line.Replace("{unit}", Unit), priority));
            }
        }

        private void OnDestroy() => Services.Unregister<IDispatchRadio>(this);

        /// <summary>Dispatch volume and mute from the Radio Audio Manager (text still shows when muted).</summary>
        private static float Gain => Services.TryGet(out IRadioAudioManager radio) ? radio.DispatchGain : 1f;

        private void Update()
        {
            if (!Transmitting)
            {
                if (_queue.Count == 0) return;
                Transmitting = true;
                _source.PlayOneShot(_chirpIn, _volume * Gain);
                _source.clip = _static;
                _source.loop = true;
                _source.volume = _volume * 0.12f * Gain;
                _source.Play();
                NextLine();
                GameEvents.Publish(new DispatchTransmissionEvent(true, CurrentLine));
                return;
            }

            _source.volume = _volume * 0.12f * Gain;   // follows dispatch volume / mute live
            _lineTimer -= Time.unscaledDeltaTime;
            _speechCap -= Time.unscaledDeltaTime;
            if (_lineTimer > 0f) return;
            if (RadioSpeech.Speaking && _speechCap > 0f) return;   // let the voice finish the line
            if (_queue.Count > 0) { NextLine(); return; }

            Transmitting = false;
            CurrentLine = null;
            RadioSpeech.Stop();
            _source.Stop();
            _source.loop = false;
            _source.PlayOneShot(_chirpOut, _volume * Gain);
            GameEvents.Publish(new DispatchTransmissionEvent(false, null));
        }

        private float _speechCap;

        /// <summary>Lines from officers, SWAT or the air unit (rather than the dispatcher) get the second voice.</summary>
        private bool IsFieldUnit(string line) =>
            line.StartsWith(Unit) || line.StartsWith("SWAT") || line.StartsWith("4") || line.StartsWith("Air") || line.StartsWith("Ground units");

        private void NextLine()
        {
            CurrentLine = _queue[0].text;
            GameEvents.Publish(new DispatchLineEvent(CurrentLine, _queue[0].priority)); // MDT dispatch history
            _queue.RemoveAt(0);
            bool voiced = RadioSpeech.Available && Gain > 0.01f;
            _lineTimer = voiced ? 0.6f : Mathf.Max(_secondsPerLine, CurrentLine.Length * 0.055f);
            _speechCap = 4f + CurrentLine.Length * 0.12f;
            if (voiced) RadioSpeech.Speak(CurrentLine, IsFieldUnit(CurrentLine) ? RadioSpeech.Voice.Field : RadioSpeech.Voice.Dispatcher, _volume * Gain);
            _log.Add(CurrentLine);
            while (_log.Count > _logLength) _log.RemoveAt(0);
        }

        // ---- procedural radio sounds (no audio assets needed)
        private static AudioClip Tone(string name, float[] freqs, float segment)
        {
            const int rate = 22050;
            int per = Mathf.RoundToInt(segment * rate);
            var data = new float[per * freqs.Length];
            for (int f = 0; f < freqs.Length; f++)
            for (int i = 0; i < per; i++)
            {
                float env = Mathf.Min(1f, i / 200f) * Mathf.Min(1f, (per - i) / 200f);
                data[f * per + i] = Mathf.Sin(2f * Mathf.PI * freqs[f] * i / rate) * 0.5f * env;
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static AudioClip Noise(string name, float seconds)
        {
            const int rate = 22050;
            var data = new float[Mathf.RoundToInt(seconds * rate)];
            var rng = new System.Random(7);
            float last = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float white = (float)rng.NextDouble() * 2f - 1f;
                last = last * 0.6f + white * 0.4f; // soften the hiss
                data[i] = last * 0.5f;
            }
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
