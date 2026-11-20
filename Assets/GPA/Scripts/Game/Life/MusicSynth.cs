using System;
using UnityEngine;

namespace GPA.Game
{
    /// <summary>
    /// A small real-time music synthesiser (no audio files): drums, bass, pads, lead/arpeggio and an
    /// echo, sequenced in 16th notes and rendered on the audio thread. Each <see cref="Style"/> is a
    /// genre recipe; every song is generated from a seed (key, tempo, chord progression, patterns,
    /// melody), so the radio never runs out of tracks and the same seed always sounds the same.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MusicSynth : MonoBehaviour
    {
        public enum Style { Synthwave, LoFi, Rock, Theme, DrumAndBass, House, Dubstep, Classical, Futurecore }

        // ---- song description (built on the main thread, read on the audio thread) ------------
        private sealed class Song
        {
            public Style Style;
            public float Bpm, Swing;
            public int Root;                      // MIDI note of the key
            public int[] Scale;
            public int[] Chords;                  // scale degree per bar, 4-bar loop
            public bool[] Kick = new bool[16], Snare = new bool[16], Hat = new bool[16], OpenHat = new bool[16];
            public int[] Bass = new int[16];      // -1 rest, else chord-tone index (0 root,1 third,2 fifth,3 octave)
            public int[] Melody = new int[64];    // 4 bars of 16ths: -99 rest, else scale step above the root
            public int[] MelodyB = new int[64];
            public int Bars;
            public bool NoDrums;
            public float Wobble;                  // bass filter LFO in cycles per beat (dubstep)
            public string Title;
        }

        private struct Voice
        {
            public bool On;
            public int Osc;                       // 0 sine, 1 saw, 2 square, 3 triangle
            public double Phase, Phase2;
            public float Freq, Detune, Amp, Env, Attack, Decay, Sustain, Release, Gate, Cut, Lp, Lp2;
            public float WobHz, Age;              // filter wobble (Hz, 0 = none) and time since note-on
        }

        private const int MaxVoices = 24;
        private readonly Voice[] _voices = new Voice[MaxVoices];
        private volatile Song _song, _next;
        private int _rate = 48000;
        private long _sample;
        private int _step = -1;
        private float _kickT = 9f, _snareT = 9f, _hatT = 9f, _hatLen = 0.04f;
        private float _kickPhase;
        private uint _noise = 22222;
        private float _hpState, _crackle;
        private float[] _delayL, _delayR;
        private int _delayPos, _delayLen;
        private float _master = 0.0f, _targetMaster = 0.7f;
        private bool _playing;

        /// <summary>Overall volume 0..1 (fades smoothly).</summary>
        public float Volume { get => _targetMaster; set => _targetMaster = Mathf.Clamp01(value); }
        public bool Playing => _playing;
        public string CurrentTitle => _song != null ? _song.Title : "";
        /// <summary>Raised on the main thread when a song finishes (Update polls the flag).</summary>
        public event Action SongEnded;
        private volatile bool _ended;

        private void Awake()
        {
            _rate = AudioSettings.outputSampleRate;
            _delayLen = Mathf.Max(1, (int)(_rate * 0.375f));
            _delayL = new float[_delayLen]; _delayR = new float[_delayLen];
            var src = GetComponent<AudioSource>();
            src.playOnAwake = false; src.loop = true; src.spatialBlend = 0f;
            src.clip = AudioClip.Create("synth", 1, 1, _rate, false);   // a silent 1-sample loop drives OnAudioFilterRead
            src.Play();
        }

        private void Update()
        {
            if (_ended) { _ended = false; SongEnded?.Invoke(); }
        }

        // ---- public control ------------------------------------------------------------------
        public void Play(Style style, int seed)
        {
            var s = Generate(style, seed);
            _delayLen = Mathf.Clamp((int)(_rate * 60f / s.Bpm * 0.75f), 1, _delayL.Length);
            _song = s; _sample = 0; _step = -1; _playing = true;
        }

        public void Stop() { _playing = false; }

        // ---- song generator ------------------------------------------------------------------
        private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 }, Minor = { 0, 2, 3, 5, 7, 8, 10 }, Dorian = { 0, 2, 3, 5, 7, 9, 10 };
        private static readonly string[] Adjectives = { "Neon", "Midnight", "Harbor", "Velvet", "Electric", "Silent", "Golden", "Broken", "Lonely", "Burning", "Paper", "Crystal", "Lost", "Heavy", "Slow" };
        private static readonly string[] Nouns = { "Highway", "Skyline", "Precinct", "Avenue", "Heartbeat", "Rain", "Signal", "Static", "Summer", "Shadows", "Motel", "Riverside", "Dreams", "Engine", "Lights" };
        private static readonly string[] Artists = { "The Night Shift", "Code Four", "Kestrel", "Mira Vale", "Lowtide", "DJ Brakelight", "The Pullovers", "Nova Halden", "Signal Ghost", "Sunday Static", "Iron Avenue", "Harbor Kids" };

        private static Song Generate(Style style, int seed)
        {
            var r = new System.Random(seed);
            var s = new Song { Style = style, Bars = 32 };
            string[] forms = { "Nocturne", "Sonata", "Prelude", "Serenade", "Étude", "Adagio" };
            string[] keys = { "C", "D", "E", "F", "G", "A", "B flat" };
            s.Title = style == Style.Theme ? "Lights & Sirens: Halden — Main Theme"
                : style == Style.Classical ? $"{forms[r.Next(forms.Length)]} No. {1 + r.Next(9)} in {keys[r.Next(keys.Length)]} — Halden Chamber Players"
                : $"{Artists[r.Next(Artists.Length)]} — {Adjectives[r.Next(Adjectives.Length)]} {Nouns[r.Next(Nouns.Length)]}";
            switch (style)
            {
                case Style.Synthwave:
                    s.Bpm = 96 + r.Next(16); s.Scale = Minor; s.Root = 45 + r.Next(5); s.Swing = 0f;
                    s.Chords = Pick(r, new[] { new[] { 0, 5, 2, 6 }, new[] { 0, 3, 5, 4 }, new[] { 0, 6, 5, 6 }, new[] { 5, 3, 0, 4 } });
                    Beat(s, kick: "x...x...x...x...", snare: "....x.......x...", hat: "..x...x...x...x.", open: "..............x.");
                    BassLine(s, r, "x.x.x.x.x.x.x.x.");
                    break;
                case Style.LoFi:
                    s.Bpm = 72 + r.Next(12); s.Scale = r.Next(2) == 0 ? Dorian : Major; s.Root = 48 + r.Next(5); s.Swing = 0.18f;
                    s.Chords = Pick(r, new[] { new[] { 1, 4, 0, 5 }, new[] { 0, 5, 1, 4 }, new[] { 3, 2, 1, 4 }, new[] { 0, 3, 1, 4 } });
                    Beat(s, kick: "x......x..x.....", snare: "....x.......x...", hat: "x.x.x.x.x.x.x.x.", open: "................");
                    BassLine(s, r, "x......x..x.....");
                    break;
                case Style.Rock:
                    s.Bpm = 124 + r.Next(24); s.Scale = r.Next(2) == 0 ? Minor : Major; s.Root = 40 + r.Next(5); s.Swing = 0f;
                    s.Chords = Pick(r, new[] { new[] { 0, 6, 5, 6 }, new[] { 0, 3, 4, 3 }, new[] { 0, 4, 5, 3 }, new[] { 5, 3, 0, 4 } });
                    Beat(s, kick: "x.....x.x.....x.", snare: "....x.......x...", hat: "x.x.x.x.x.x.x.x.", open: "................");
                    BassLine(s, r, "xxxxxxxxxxxxxxxx");
                    break;
                case Style.DrumAndBass:
                    s.Bpm = 170 + r.Next(8); s.Scale = Minor; s.Root = 38 + r.Next(5); s.Swing = 0f;
                    s.Chords = Pick(r, new[] { new[] { 0, 0, 5, 3 }, new[] { 0, 6, 5, 4 }, new[] { 0, 3, 0, 4 } });
                    Beat(s, kick: "x.........x.....", snare: "....x.......x...", hat: "x.x.x.x.x.x.x.x.", open: "..............x.");
                    BassLine(s, r, "x.......x.x.....");
                    break;
                case Style.House:
                    s.Bpm = 120 + r.Next(8); s.Scale = r.Next(2) == 0 ? Minor : Dorian; s.Root = 45 + r.Next(5); s.Swing = 0.08f;
                    s.Chords = Pick(r, new[] { new[] { 0, 5, 3, 4 }, new[] { 0, 3, 0, 3 }, new[] { 1, 4, 0, 5 } });
                    Beat(s, kick: "x...x...x...x...", snare: "....x.......x...", hat: "..x...x...x...x.", open: "..x...x...x...x.");
                    BassLine(s, r, "..x...x...x...x.");
                    break;
                case Style.Dubstep:
                    s.Bpm = 140; s.Scale = Minor; s.Root = 36 + r.Next(5); s.Swing = 0f; s.Wobble = r.Next(2) == 0 ? 2f : 4f;
                    s.Chords = Pick(r, new[] { new[] { 0, 0, 5, 6 }, new[] { 0, 3, 0, 4 }, new[] { 0, 5, 0, 6 } });
                    Beat(s, kick: "x.........x.....", snare: "........x.......", hat: "x.x.x.x.x.x.x.x.", open: "................");
                    BassLine(s, r, "x.......x.......");
                    break;
                case Style.Futurecore:   // hardcore drums under bright future-bass supersaws
                    s.Bpm = 168 + r.Next(10); s.Scale = Major; s.Root = 45 + r.Next(5); s.Swing = 0f;
                    s.Chords = Pick(r, new[] { new[] { 3, 4, 5, 0 }, new[] { 5, 3, 0, 4 }, new[] { 0, 4, 5, 3 }, new[] { 3, 4, 2, 5 } });
                    Beat(s, kick: "x...x...x...x...", snare: "....x.......x...", hat: "..x...x...x...x.", open: "..x...x...x...x.");
                    BassLine(s, r, "..x...x...x...x.");
                    break;
                case Style.Classical:
                    s.Bpm = 76 + r.Next(16); s.Scale = r.Next(2) == 0 ? Major : Minor; s.Root = 48 + r.Next(7); s.Swing = 0f; s.NoDrums = true;
                    s.Chords = Pick(r, new[] { new[] { 0, 3, 4, 0 }, new[] { 0, 5, 3, 4 }, new[] { 0, 4, 5, 3 }, new[] { 5, 1, 4, 0 } });
                    Beat(s, kick: "................", snare: "................", hat: "................", open: "................");
                    BassLine(s, r, "x...x...x...x...");
                    break;
                default: // Theme: brooding, cinematic
                    s.Bpm = 88; s.Scale = Minor; s.Root = 45; s.Swing = 0f; s.Bars = 64;
                    s.Chords = new[] { 0, 5, 3, 4 };
                    Beat(s, kick: "x.......x.x.....", snare: "....x.......x..x", hat: "x.x.x.x.x.x.x.x.", open: "................");
                    BassLine(s, r, "x..x..x.x..x..x.");
                    break;
            }
            Melody(s.Melody, r, style, 0.55f);
            Melody(s.MelodyB, r, style, 0.7f);
            return s;
        }

        private static int[] Pick(System.Random r, int[][] options) => options[r.Next(options.Length)];

        private static void Beat(Song s, string kick, string snare, string hat, string open)
        {
            for (int i = 0; i < 16; i++) { s.Kick[i] = kick[i] == 'x'; s.Snare[i] = snare[i] == 'x'; s.Hat[i] = hat[i] == 'x'; s.OpenHat[i] = open[i] == 'x'; }
        }

        private static void BassLine(Song s, System.Random r, string rhythm)
        {
            for (int i = 0; i < 16; i++) s.Bass[i] = rhythm[i] == 'x' ? (i % 8 == 6 && r.Next(3) == 0 ? 2 : i % 16 == 14 && r.Next(2) == 0 ? 3 : 0) : -1;
        }

        /// <summary>A 4-bar phrase that mostly steps through the scale, landing on chord tones on the beat.</summary>
        private static void Melody(int[] into, System.Random r, Style style, float density)
        {
            int step = 4 + r.Next(3);
            int grid = style == Style.LoFi || style == Style.Theme || style == Style.Classical || style == Style.Dubstep ? 4 : 2;   // note length in 16ths
            for (int i = 0; i < into.Length; i++) into[i] = -99;
            for (int i = 0; i < into.Length; i += grid)
            {
                if (r.NextDouble() > density && i % 16 != 0) continue;
                step += r.Next(5) - 2;
                if (i % 16 == 0) step = (step / 2) * 2;                     // strong beats on even steps (chord tones)
                step = Mathf.Clamp(step, 0, 11);
                into[i] = step;
            }
        }

        // ---- audio thread --------------------------------------------------------------------
        private void OnAudioFilterRead(float[] data, int channels)
        {
            Song s = _song;
            float dt = 1f / _rate;
            for (int i = 0; i < data.Length; i += channels)
            {
                float target = _playing && s != null ? _targetMaster : 0f;
                _master += (target - _master) * 0.0004f;

                float mix = 0f;
                if (s != null && (_playing || _master > 0.0005f))
                {
                    double stepLen = 60.0 / s.Bpm / 4.0 * _rate;
                    long step = (long)(_sample / stepLen);
                    // Swing: delay odd 16ths.
                    double within = _sample - step * stepLen;
                    bool odd = (step & 1) == 1;
                    if (step != _step && (!odd || within >= stepLen * s.Swing)) { _step = (int)step; Trigger(s, _step); }
                    _sample++;
                    mix = Render(s, dt);
                }

                // Echo (stereo ping-pong-ish).
                int dl = _delayLen;
                float dL = _delayL[_delayPos % dl], dR = _delayR[_delayPos % dl];
                float wet = s == null ? 0.18f : s.Style == Style.Synthwave || s.Style == Style.Classical ? 0.32f : s.Style == Style.Theme ? 0.4f : s.Style == Style.Dubstep ? 0.22f : 0.18f;
                _delayL[_delayPos % dl] = mix + dR * 0.45f;
                _delayR[_delayPos % dl] = dL * 0.45f;
                _delayPos = (_delayPos + 1) % dl;
                float l = mix + dL * wet, rr = mix + dR * wet;
                l = Soft(l * _master); rr = Soft(rr * _master);
                data[i] = l;
                if (channels > 1) data[i + 1] = rr;
                for (int c = 2; c < channels; c++) data[i + c] = 0f;
            }
        }

        private static float Soft(float x) => x / (1f + Mathf.Abs(x));

        private float Noise() { _noise ^= _noise << 13; _noise ^= _noise >> 17; _noise ^= _noise << 5; return (_noise / (float)uint.MaxValue) * 2f - 1f; }

        private static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        private int Degree(Song s, int degree) => s.Root + (degree / 7) * 12 + s.Scale[((degree % 7) + 7) % 7];

        private void Trigger(Song s, int step)
        {
            int bar = step / 16, inBar = step % 16;
            if (bar >= s.Bars) { if (s == _song) { _playing = s.Style == Style.Theme; if (s.Style == Style.Theme) _sample = 0; else _ended = true; } return; }
            int chord = s.Chords[bar % 4];
            bool intro = bar < 4, outro = bar >= s.Bars - 2, breakdown = bar >= 16 && bar < 20;
            float stepSec = 60f / s.Bpm / 4f;

            // Drums.
            if (!s.NoDrums && (!intro || s.Style == Style.Rock))
            {
                if (s.Kick[inBar] && !breakdown) _kickT = 0f;
                if (s.Snare[inBar] && !breakdown) _snareT = 0f;
                // Fill on the last bar of every 8.
                if (bar % 8 == 7 && inBar >= 12 && s.Style != Style.LoFi) _snareT = 0f;
            }
            if (s.Hat[inBar] && !outro && !s.NoDrums) { _hatT = 0f; _hatLen = 0.035f; }
            if (s.OpenHat[inBar] && !intro) { _hatT = 0f; _hatLen = 0.2f; }

            // Pad: the bar's chord, re-struck each bar.
            if (inBar == 0)
            {
                int padOsc = s.Style == Style.LoFi || s.Style == Style.Classical ? 3 : 1;
                float padAmp = s.Style == Style.Rock ? 0.05f : s.Style == Style.Dubstep ? 0.04f : s.Style == Style.Classical ? 0.06f : 0.07f;
                float len = stepSec * 16f * 0.95f;
                for (int k = 0; k < 3; k++)
                    Note(Midi(Degree(s, chord + k * 2) + 12), padOsc, padAmp, s.Style == Style.LoFi ? 0.02f : 0.25f, 0.5f, 0.7f, 0.4f, len, s.Style == Style.Rock ? 1800f : 1400f, 0.004f);
                if (s.Style == Style.LoFi) Note(Midi(Degree(s, chord + 6) + 12), 3, 0.05f, 0.02f, 0.5f, 0.6f, 0.4f, len, 1600f, 0.003f);   // 7th
            }

            // Bass.
            int b = s.Bass[inBar];
            if (b >= 0 && !intro)
            {
                int[] tones = { 0, 2, 4, 7 };
                int note = Degree(s, chord + tones[b]) - 12;
                if (s.Style == Style.Dubstep)        // wobble bass: long detuned saw with a pulsing filter
                    Note(Midi(note), 1, 0.3f, 0.005f, 0.1f, 0.9f, 0.08f, stepSec * 7.5f, 1400f, 0.006f, s.Wobble * s.Bpm / 60f);
                else if (s.Style == Style.DrumAndBass) // reese: two detuned saws, dark
                    Note(Midi(note), 1, 0.26f, 0.01f, 0.2f, 0.85f, 0.1f, stepSec * 5.5f, 500f, 0.008f);
                else if (s.Style == Style.Classical)   // Alberti-style left hand: root, fifth, third, fifth
                {
                    int[] alberti = { 0, 4, 2, 4 };
                    Note(Midi(Degree(s, chord + alberti[(inBar / 4) % 4])), 3, 0.16f, 0.005f, 0.3f, 0.35f, 0.3f, stepSec * 3.5f, 2000f, 0f);
                }
                else
                {
                    int osc = s.Style == Style.Rock ? 1 : s.Style == Style.LoFi ? 0 : s.Style == Style.House ? 2 : 1;
                    Note(Midi(note), osc, s.Style == Style.LoFi ? 0.32f : 0.22f, 0.005f, 0.12f, 0.5f, 0.05f, stepSec * (s.Style == Style.LoFi ? 3f : 0.9f), s.Style == Style.Rock ? 900f : s.Style == Style.House ? 750f : 600f, 0f);
                }
            }

            // Lead / arpeggio.
            if (bar >= 4 && !outro)
            {
                int[] mel = (bar / 8) % 2 == 1 ? s.MelodyB : s.Melody;
                int m = mel[(bar % 4) * 16 + inBar];
                if (m != -99)
                {
                    int osc = s.Style == Style.Synthwave ? 2 : s.Style == Style.Futurecore ? 1 : s.Style == Style.LoFi || s.Style == Style.Classical ? 3 : s.Style == Style.House ? 2 : 1;
                    float amp = s.Style == Style.Rock ? 0.09f : 0.1f;
                    float len = stepSec * (s.Style == Style.LoFi || s.Style == Style.Theme ? 3.5f : 1.8f);
                    Note(Midi(Degree(s, m) + 12), osc, amp, 0.01f, 0.2f, 0.6f, 0.15f, len, s.Style == Style.LoFi ? 2200f : 3500f, 0.003f);
                }
                // Arpeggios: synthwave and house plucks, classical broken chords.
                if ((s.Style == Style.Synthwave || s.Style == Style.House || s.Style == Style.Futurecore || s.Style == Style.Classical && inBar % 2 == 0) && bar >= 8 && !breakdown)
                {
                    int[] arp = { 0, 2, 4, 7 };
                    Note(Midi(Degree(s, chord + arp[inBar % 4]) + 24), 1, 0.04f, 0.003f, 0.08f, 0.3f, 0.05f, stepSec * 0.8f, 2600f, 0f);
                }
            }
        }

        private void Note(float freq, int osc, float amp, float attack, float decay, float sustain, float release, float gate, float cutoff, float detune, float wobHz = 0f)
        {
            int slot = -1; float quietest = float.MaxValue;
            for (int i = 0; i < MaxVoices; i++)
            {
                if (!_voices[i].On) { slot = i; break; }
                if (_voices[i].Env < quietest) { quietest = _voices[i].Env; slot = i; }
            }
            _voices[slot] = new Voice { On = true, Osc = osc, Freq = freq, Detune = detune, Amp = amp, Attack = attack, Decay = decay, Sustain = sustain, Release = release, Gate = gate, Cut = cutoff, Env = 0f, WobHz = wobHz };
        }

        private float Render(Song s, float dt)
        {
            float sum = 0f;
            for (int i = 0; i < MaxVoices; i++)
            {
                ref Voice v = ref _voices[i];
                if (!v.On) continue;
                // Envelope.
                if (v.Gate > 0f)
                {
                    v.Gate -= dt;
                    if (v.Attack > 0f) { v.Env += dt / v.Attack; if (v.Env > 1f) { v.Env = 1f; v.Attack = 0f; } }
                    else v.Env += (v.Sustain - v.Env) * Mathf.Min(1f, dt / Mathf.Max(0.001f, v.Decay));
                }
                else
                {
                    v.Env -= dt / Mathf.Max(0.01f, v.Release) * Mathf.Max(0.05f, v.Env);
                    if (v.Env <= 0.0005f) { v.On = false; continue; }
                }
                v.Phase += v.Freq * dt; if (v.Phase >= 1.0) v.Phase -= 1.0;
                v.Phase2 += v.Freq * (1f + v.Detune) * dt; if (v.Phase2 >= 1.0) v.Phase2 -= 1.0;
                float o = Osc(v.Osc, (float)v.Phase);
                if (v.Detune > 0f) o = (o + Osc(v.Osc, (float)v.Phase2)) * 0.5f;
                // Two-pole low-pass for warmth.
                v.Age += dt;
                float cut = v.WobHz > 0f ? v.Cut * (0.08f + 0.92f * (0.5f + 0.5f * Mathf.Sin(v.Age * v.WobHz * 2f * Mathf.PI))) : v.Cut;
                float a = 1f - Mathf.Exp(-2f * Mathf.PI * cut * dt);
                v.Lp += (o - v.Lp) * a; v.Lp2 += (v.Lp - v.Lp2) * a;
                sum += v.Lp2 * v.Env * v.Amp;
            }

            // Drums.
            if (_kickT < 0.45f)
            {
                float f = 45f + 110f * Mathf.Exp(-_kickT * 35f);
                _kickPhase += f * dt; if (_kickPhase > 1f) _kickPhase -= 1f;
                float k = Mathf.Sin(_kickPhase * 2f * Mathf.PI) * Mathf.Exp(-_kickT * (s.Style == Style.DrumAndBass ? 14f : s.Style == Style.Futurecore ? 6f : 8f));
                if (s.Style == Style.Futurecore) k = Soft(k * 5f) * 0.9f;   // distorted hardcore kick
                sum += k * 0.55f;
                _kickT += dt;
            }
            if (_snareT < 0.3f)
            {
                float n = Noise();
                float body = Mathf.Sin(_snareT * 190f * 2f * Mathf.PI) * Mathf.Exp(-_snareT * 30f);
                sum += (n * Mathf.Exp(-_snareT * (s.Style == Style.LoFi ? 22f : 14f)) * 0.22f + body * 0.18f) * (s.Style == Style.LoFi ? 0.6f : 1f);
                _snareT += dt;
            }
            if (_hatT < _hatLen)
            {
                float n = Noise();
                _hpState += (n - _hpState) * 0.35f;
                sum += (n - _hpState) * Mathf.Exp(-_hatT / (_hatLen * 0.35f)) * (s.Style == Style.LoFi ? 0.06f : 0.09f);
                _hatT += dt;
            }
            // Lo-fi vinyl crackle.
            if (s.Style == Style.LoFi)
            {
                if (Noise() > 0.9993f) _crackle = 1f;
                sum += Noise() * 0.006f + _crackle * Noise() * 0.08f;
                _crackle *= 0.9f;
            }
            return sum;
        }

        private static float Osc(int type, float p)
        {
            switch (type)
            {
                case 1: return 2f * p - 1f;                                   // saw
                case 2: return p < 0.5f ? 0.7f : -0.7f;                       // square
                case 3: return 1f - 4f * Mathf.Abs(p - 0.5f);                 // triangle
                default: return Mathf.Sin(p * 2f * Mathf.PI);                 // sine
            }
        }
    }
}
