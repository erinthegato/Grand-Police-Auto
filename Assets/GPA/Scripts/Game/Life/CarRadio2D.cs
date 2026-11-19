using System.Collections.Generic;
using GPA.Callouts;
using GPA.Core;
using GPA.TopDown2D;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Game
{
    /// <summary>
    /// Car radio for civilian (off-duty) cars, GTA style. Police vehicles keep the dispatch radio.
    /// Stations: WAVE 104.1 (synthwave), Low Tide FM (lo-fi), Halden Rock 98 (rock), Pulse 174
    /// (drum &amp; bass), Club Halden 101.9 (electronic / house), Wobble FM (dubstep) and Halden
    /// Classical 89.5 — recorded songs (Resources/Music/&lt;Style&gt;, AI-generated tracks) shuffled with
    /// DJ links between them; a station with no recorded songs yet falls back to the procedural
    /// <see cref="MusicSynth"/> — plus KHLD Talk 880
    /// (news, weather and ads read by the text-to-speech voice). Change station with , and . (or the d-pad left / right).
    /// Also plays the main-menu theme while the title screen is up.
    /// </summary>
    public sealed class CarRadio2D : MonoBehaviour
    {
        private sealed class Station
        {
            public string Name, Tagline;
            public MusicSynth.Style? Style;   // null = talk
            public int Seed;
        }

        private static readonly Station[] Stations =
        {
            new Station { Name = "Radio off" },
            new Station { Name = "WAVE 104.1", Tagline = "New Halden's home of synthwave", Style = MusicSynth.Style.Synthwave },
            new Station { Name = "Low Tide FM", Tagline = "beats to patrol to", Style = MusicSynth.Style.LoFi },
            new Station { Name = "Halden Rock 98", Tagline = "turn it up, Halden", Style = MusicSynth.Style.Rock },
            new Station { Name = "Pulse 174", Tagline = "drum & bass, all night", Style = MusicSynth.Style.DrumAndBass },
            new Station { Name = "Club Halden 101.9", Tagline = "electronic & house", Style = MusicSynth.Style.House },
            new Station { Name = "Wobble FM", Tagline = "dubstep and bass music", Style = MusicSynth.Style.Dubstep },
            new Station { Name = "Halden Classical 89.5", Tagline = "the classics, beautifully played", Style = MusicSynth.Style.Classical },
            new Station { Name = "KHLD Talk 880", Tagline = "news, traffic and weather", Style = null },
        };

        private static readonly string[] TalkLines =
        {
            "Good evening New Halden, this is KHLD Talk eight eighty. Traffic on the Beltway is moving well in both directions.",
            "In local news, the Harbor Point business district reports a quiet week, and police thank residents for reporting suspicious activity.",
            "Weather now. Clear skies over Halden Island tonight, with a light breeze off the harbor. Patchy fog by morning near the river.",
            "Halden Motors on Twentieth Street. New and used. Sedans, trucks, SUVs. Drive home happy. Halden Motors.",
            "Feeling lucky? Harbor Lights Casino. Slots and blackjack, open all night, in the heart of Harbor Point. Please gamble responsibly.",
            "Westbrook residents are reminded that the Sycamore Avenue farmers market returns this weekend.",
            "Hungry? Stop by any New Halden diner for a hot meal and the city's best coffee.",
            "A reminder from the New Halden Police Department: buckle up, slow down, and never drive tired.",
            "Eastport shipping traffic remains heavy along Container Way. Expect delays near the docks.",
            "Northgate's annual spring fair has been confirmed for next month at Hillcrest Road park.",
            "Looking for a place to call home? Apartments in Linden Heights and houses in Westbrook are on the market now.",
            "Sports. The Halden Harbormen take on Eastport tonight. Kickoff at eight.",
        };

        /// <summary>Display names for the recorded tracks (file name → "Title — Artist").</summary>
        private static readonly Dictionary<string, string> TrackNames = new Dictionary<string, string>
        {
            { "menu_theme", "Code 4 (Main Theme) — New Halden PD" },
            { "synthwave_1", "Neon Patrol — Midnight Unit" }, { "synthwave_2", "Coastline 1986 — Sunset Grid" }, { "synthwave_3", "Afterglow Avenue — VHS Hearts" },
            { "lofi_1", "Night Shift Notes — Quiet Precinct" }, { "lofi_2", "Rain on the Cruiser — Paper Lanterns" }, { "lofi_3", "Coffee at 3AM — Slow Sirens" },
            { "rock_1", "Back Roads Burnin' — The Halden Hounds" }, { "rock_2", "Static Skyline — Hollow Bridges" }, { "rock_3", "Full Throttle — Iron Precinct" },
            { "dnb_1", "Harbor Lights — Liquid State" }, { "dnb_2", "Pursuit Protocol — Neural Unit" }, { "dnb_3", "Riverline — Subway Echo" },
            { "house_1", "Warehouse 4AM — Club Halden Allstars" }, { "house_2", "Skyline Rush — Prism Kids" }, { "house_3", "Deep Harbor — Velvet Room" },
            { "dubstep_1", "Grinder — Wobble Unit" }, { "dubstep_2", "Fallen Skyline — Bass Cartel" }, { "dubstep_3", "Riot Gear — Subzero" },
            { "classical_1", "Symphonic Movement in E-flat — Halden Philharmonic" }, { "classical_2", "Harbor Overture — Halden Chamber Players" }, { "classical_3", "Guitar Concerto in E minor — Halden Strings" },
        };

        public static CarRadio2D Instance { get; private set; }
        private MusicSynth _synth;
        private AudioSource _music;                       // recorded tracks
        private readonly Dictionary<MusicSynth.Style, List<AudioClip>> _tracks = new Dictionary<MusicSynth.Style, List<AudioClip>>();
        private readonly Dictionary<MusicSynth.Style, int> _trackCursor = new Dictionary<MusicSynth.Style, int>();
        private AudioClip _menuClip;
        private bool _recordedPlaying;
        private string _currentTitle = "";
        private float _musicTarget;
        private int _station = 1;
        private bool _tuned;              // radio audio currently live in a car
        private bool _menuTheme;
        private Officer2D _officer;
        private string _banner, _bannerSub;
        private float _bannerUntil;
        private float _talkNext;
        private int _talkIndex;
        private bool _djTalking;
        private int _songCounter;
        private GUIStyle _bigStyle, _subStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<CarRadio2D>() != null || FindAnyObjectByType<Officer2D>() == null) return;
            new GameObject("Car Radio").AddComponent<CarRadio2D>();
        }

        private void Awake()
        {
            Instance = this;
            var src = gameObject.AddComponent<AudioSource>();
            src.ignoreListenerPause = true;   // the title screen pauses the listener; the theme must still play
            _synth = gameObject.AddComponent<MusicSynth>();
            _synth.SongEnded += OnSongEnded;
            // Recorded tracks play from a child object: a second AudioSource beside MusicSynth's
            // OnAudioFilterRead is an error in Unity (the filter can't tell which source it belongs to).
            var musicGo = new GameObject("Recorded music");
            musicGo.transform.SetParent(transform, false);
            _music = musicGo.AddComponent<AudioSource>();
            _music.playOnAwake = false; _music.spatialBlend = 0f; _music.volume = 0f;
            LoadTracks();
            _station = PlayerPrefs.GetInt("GPA.RadioStation", 1);
            for (int i = 0; i < Stations.Length; i++) Stations[i].Seed = Random.Range(1, 100000);
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>Recorded songs per style from Resources/Music/&lt;Style&gt;; shuffled so each drive starts somewhere new.</summary>
        private void LoadTracks()
        {
            foreach (MusicSynth.Style style in System.Enum.GetValues(typeof(MusicSynth.Style)))
            {
                var clips = new List<AudioClip>(Resources.LoadAll<AudioClip>("Music/" + style));
                for (int i = clips.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (clips[i], clips[j]) = (clips[j], clips[i]); }
                if (clips.Count > 0) { _tracks[style] = clips; _trackCursor[style] = 0; }
            }
            var menu = Resources.LoadAll<AudioClip>("Music/Menu");
            _menuClip = menu.Length > 0 ? menu[0] : null;
        }

        private static string TitleOf(AudioClip clip) => clip != null && TrackNames.TryGetValue(clip.name, out string n) ? n : clip != null ? clip.name : "";

        private bool HasRecorded(MusicSynth.Style style) => _tracks.ContainsKey(style);

        /// <summary>Starts a recorded song (or the procedural synth if this style has none).</summary>
        private void StartSong(MusicSynth.Style style, int seed)
        {
            if (HasRecorded(style))
            {
                _synth.Stop();
                var list = _tracks[style];
                int i = _trackCursor[style] % list.Count;
                _trackCursor[style] = i + 1;
                _music.clip = list[i];
                _music.loop = false;
                _music.time = 0f;
                _music.Play();
                _recordedPlaying = true;
                _currentTitle = TitleOf(list[i]);
            }
            else
            {
                StopRecorded();
                _synth.Play(style, seed);
                _currentTitle = _synth.CurrentTitle;
            }
        }

        private void StopRecorded() { _recordedPlaying = false; if (_music != null) _music.Stop(); }

        private void StopAll() { StopRecorded(); _synth.Stop(); }

        private void LateUpdate()
        {
            if (_music == null) return;
            // Recorded music follows the same volume as the synth (ducking under the DJ, fades).
            _music.ignoreListenerPause = _menuTheme;   // the title pauses the listener; the theme keeps playing
            _music.volume = Mathf.MoveTowards(_music.volume, _recordedPlaying ? _musicTarget : 0f, Time.unscaledDeltaTime * 0.8f);
            if (_recordedPlaying && !_menuTheme && !_music.isPlaying && !AudioListener.pause && _music.clip != null && _music.time == 0f)
            {
                _recordedPlaying = false;
                OnSongEnded();
            }
        }

        private Car2D EligibleCar()
        {
            if (_officer == null) _officer = FindAnyObjectByType<Officer2D>();
            Car2D car = _officer != null ? _officer.CurrentCar : null;
            return car != null && !car.IsEmergencyVehicle && !car.Hover ? car : null;
        }

        private void Update()
        {
            // Main-menu theme.
            bool title = GameMenu2D.TitleShowing && !BootSplash2D.Showing;   // theme starts after the boot intro
            if (title != _menuTheme)
            {
                _menuTheme = title;
                if (title)
                {
                    _tuned = false;
                    _synth.Volume = 0.55f; _musicTarget = 0.7f;
                    if (_menuClip != null) { _synth.Stop(); _music.clip = _menuClip; _music.loop = true; _music.Play(); _recordedPlaying = true; _currentTitle = TitleOf(_menuClip); }
                    else _synth.Play(MusicSynth.Style.Theme, 7);
                }
                else StopAll();
            }
            if (_menuTheme) return;

            Car2D car = EligibleCar();
            bool want = car != null && _station != 0 && !GameplayHotkeys.Blocked;
            if (car != null && !GameplayHotkeys.Blocked) HandleInput();
            if (want && !_tuned) TuneIn();
            else if (!want && _tuned) { _tuned = false; StopAll(); if (_djTalking) RadioSpeech.Stop(); _djTalking = false; }

            if (_tuned) TickStation();
        }

        private void HandleInput()
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            int dir = 0;
            if (kb != null && kb.periodKey.wasPressedThisFrame) dir = 1;
            if (kb != null && kb.commaKey.wasPressedThisFrame) dir = -1;
            if (pad != null && pad.dpad.right.wasPressedThisFrame) dir = 1;
            if (pad != null && pad.dpad.left.wasPressedThisFrame) dir = -1;
            if (dir == 0) return;
            _station = (_station + dir + Stations.Length) % Stations.Length;
            PlayerPrefs.SetInt("GPA.RadioStation", _station);
            if (_djTalking) { RadioSpeech.Stop(); _djTalking = false; }
            _tuned = false;
            if (_station == 0) { StopAll(); Banner("Radio off", ""); }
            GameAudio.UiClick();
        }

        private void TuneIn()
        {
            _tuned = true;
            Station st = Stations[_station];
            _synth.Volume = 0.5f; _musicTarget = 0.6f;
            if (st.Style.HasValue) { StartSong(st.Style.Value, st.Seed + _songCounter * 7919); Banner(st.Name, $"{st.Tagline} · {_currentTitle}"); }
            else { StopAll(); _talkNext = Time.unscaledTime + 0.5f; Banner(st.Name, RadioSpeech.Available ? st.Tagline : "no signal"); }
        }

        private void TickStation()
        {
            Station st = Stations[_station];
            if (st.Style.HasValue)
            {
                // DJ link between songs, music ducked under it.
                if (_djTalking && !RadioSpeech.Speaking) { _djTalking = false; _synth.Volume = 0.5f; _musicTarget = 0.6f; NextSong(); }
                return;
            }
            // Talk station: one item after another.
            if (!RadioSpeech.Available || RadioSpeech.Speaking || Time.unscaledTime < _talkNext) return;
            RadioSpeech.Speak(TalkLines[_talkIndex % TalkLines.Length], RadioSpeech.Voice.Dispatcher, 0.9f);
            _talkIndex++;
            _talkNext = Time.unscaledTime + 2.5f;
        }

        private void OnSongEnded()
        {
            if (!_tuned || _menuTheme) return;
            Station st = Stations[_station];
            if (!st.Style.HasValue) return;
            if (RadioSpeech.Available && Random.value < 0.6f)
            {
                string title = _currentTitle;
                string[] links =
                {
                    $"That was {title}. You're listening to {st.Name}, {st.Tagline}.",
                    $"{st.Name}. Keep it locked. Here's another one for the night drivers of New Halden.",
                    $"Beautiful. {title}. More music coming right up on {st.Name}.",
                };
                _djTalking = true;
                RadioSpeech.Speak(links[Random.Range(0, links.Length)], RadioSpeech.Voice.Field, 0.9f);
            }
            else NextSong();
        }

        private void NextSong()
        {
            Station st = Stations[_station];
            if (!st.Style.HasValue) return;
            _songCounter++;
            StartSong(st.Style.Value, st.Seed + _songCounter * 7919);
            Banner(st.Name, _currentTitle);
        }

        private void Banner(string title, string sub) { _banner = title; _bannerSub = sub; _bannerUntil = Time.unscaledTime + 3.5f; }

        private void OnGUI()
        {
            if (_banner == null || Time.unscaledTime > _bannerUntil || _menuTheme) return;
            if (_bigStyle == null)
            {
                _bigStyle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
                _subStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.8f, 0.85f, 0.9f) } };
            }
            float a = Mathf.Clamp01((_bannerUntil - Time.unscaledTime) * 2f);
            Matrix4x4 old = UiScale.Begin(out Vector2 canvas);
            var r = new Rect(canvas.x * 0.5f - 300f, UiScale.Handheld ? 70f : 90f, 600f, 70f);
            GUI.color = new Color(0f, 0f, 0f, 0.55f * a); GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.Label(new Rect(r.x, r.y + 4, r.width, 36), _banner, _bigStyle);
            GUI.Label(new Rect(r.x, r.y + 38, r.width, 26), _bannerSub, _subStyle);
            GUI.color = Color.white;
            GUI.matrix = old;
        }
    }
}
