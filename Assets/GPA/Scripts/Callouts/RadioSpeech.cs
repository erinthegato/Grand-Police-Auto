using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GPA.Callouts
{
    /// <summary>
    /// Speaks dispatch radio lines with the device's own text-to-speech, so the radio says what the
    /// text says. iPhone/iPad: AVSpeechSynthesizer (Plugins/iOS/GPASpeech.mm). Android: TextToSpeech.
    /// Windows (editor and PC builds): SAPI. macOS: the system "say" voice. Anything else: silent
    /// (the radio text still shows). Two voices: dispatcher, and field units (officers, SWAT, air).
    /// </summary>
    public static class RadioSpeech
    {
        public enum Voice { Dispatcher, Field }

        private static ISpeech _backend;
        private static bool _tried;

        public static bool Available { get { Init(); return _backend != null && _backend.Ready; } }
        public static bool Speaking { get { Init(); return _backend != null && _backend.Speaking; } }

        public static void Speak(string line, Voice voice, float volume)
        {
            Init();
            if (_backend == null || !_backend.Ready || volume <= 0.01f || string.IsNullOrWhiteSpace(line)) return;
            try { _backend.Speak(Normalize(line), voice, Mathf.Clamp01(volume)); }
            catch (Exception e) { Debug.LogWarning("[RadioSpeech] " + e.Message); _backend = null; }
        }

        public static void Stop() { try { _backend?.Stop(); } catch (Exception) { } }

        private static void Init()
        {
            if (_tried) return;
            _tried = true;
            try
            {
#if UNITY_IOS && !UNITY_EDITOR
                _backend = new IosSpeech();
#elif UNITY_ANDROID && !UNITY_EDITOR
                _backend = new AndroidSpeech();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                _backend = new WinSpeech();
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
                _backend = new MacSpeech();
#endif
            }
            catch (Exception e) { Debug.Log("[RadioSpeech] No text-to-speech on this device: " + e.Message); _backend = null; }
        }

        // ---- radio-speak text normalisation -------------------------------------------------
        private static readonly (Regex re, string say)[] Rules =
        {
            (new Regex(@"\bSt\b"), "Street"), (new Regex(@"\bAve\b"), "Avenue"), (new Regex(@"\bBlvd\b"), "Boulevard"),
            (new Regex(@"\bDr\b"), "Drive"), (new Regex(@"\bRd\b"), "Road"), (new Regex(@"\bPl\b"), "Place"),
            (new Regex(@"\bMDT\b"), "M D T"), (new Regex(@"\bBOLO\b"), "bolo"), (new Regex(@"\bDWI\b"), "D W I"), (new Regex(@"\bDUI\b"), "D U I"),
            (new Regex(@"\bSWAT\b"), "swat"), (new Regex(@"\bK-9\b"), "K nine"), (new Regex(@"&"), "and"),
        };

        private static readonly string[] Ones = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };

        /// <summary>Makes a line sound like radio speech: callsigns digit by digit, 10-codes as "ten fifty", plates letter by letter.</summary>
        public static string Normalize(string s)
        {
            s = Regex.Replace(s, @"<[^>]+>", "");                                  // rich-text tags
            s = Regex.Replace(s, @"\b10-(\d{1,2})\b", m => "ten " + m.Groups[1].Value);  // 10-97 -> ten 97 (TTS reads 97 naturally)
            s = Regex.Replace(s, @"\b([A-Z]{3})-(\d{4})\b", m => Spell(m.Groups[1].Value) + ", " + Digits(m.Groups[2].Value)); // plates
            s = Regex.Replace(s, @"\b(4\d{3})\b", m => Digits(m.Value));             // unit callsigns 4xxx
            foreach (var (re, say) in Rules) s = re.Replace(s, say);
            s = s.Replace("—", ", ").Replace("·", ", ");
            return s;
        }

        private static string Digits(string d) { var b = new StringBuilder(); foreach (char c in d) if (char.IsDigit(c)) b.Append(Ones[c - '0']).Append(' '); return b.ToString().Trim(); }
        private static string Spell(string l) => string.Join(" ", l.ToCharArray());

        // ---- backends -----------------------------------------------------------------------
        private interface ISpeech
        {
            bool Ready { get; }
            bool Speaking { get; }
            void Speak(string text, Voice voice, float volume);
            void Stop();
        }

#if UNITY_IOS && !UNITY_EDITOR
        private sealed class IosSpeech : ISpeech
        {
            [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void GPASpeak(string text, int voice, float rate, float pitch, float volume);
            [System.Runtime.InteropServices.DllImport("__Internal")] private static extern void GPAStopSpeaking();
            [System.Runtime.InteropServices.DllImport("__Internal")] private static extern bool GPAIsSpeaking();
            public bool Ready => true;
            public bool Speaking => GPAIsSpeaking();
            public void Speak(string text, Voice voice, float volume) =>
                GPASpeak(text, voice == Voice.Dispatcher ? 0 : 1, voice == Voice.Dispatcher ? 0.52f : 0.55f, voice == Voice.Dispatcher ? 1.1f : 0.85f, volume);
            public void Stop() => GPAStopSpeaking();
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AndroidSpeech : ISpeech
        {
            private sealed class InitListener : AndroidJavaProxy
            {
                public bool Ok;
                public InitListener() : base("android.speech.tts.TextToSpeech$OnInitListener") { }
                public void onInit(int status) { Ok = status == 0; }
            }
            private readonly AndroidJavaObject _tts;
            private readonly InitListener _listener = new InitListener();
            public AndroidSpeech()
            {
                var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");
                _tts = new AndroidJavaObject("android.speech.tts.TextToSpeech", activity, _listener);
            }
            public bool Ready => _listener.Ok;
            public bool Speaking => _tts.Call<bool>("isSpeaking");
            public void Speak(string text, Voice voice, float volume)
            {
                _tts.Call<int>("setPitch", voice == Voice.Dispatcher ? 1.1f : 0.8f);
                _tts.Call<int>("setSpeechRate", 1.1f);
                var bundle = new AndroidJavaObject("android.os.Bundle");
                bundle.Call("putFloat", "volume", volume);
                _tts.Call<int>("speak", text, 0, bundle, "gpa");
            }
            public void Stop() => _tts.Call<int>("stop");
        }
#endif

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        /// <summary>
        /// Windows: one hidden PowerShell process running System.Speech (SAPI), fed lines over stdin,
        /// so there's no start-up delay per line. (Mono can't create COM objects directly.)
        /// </summary>
        private sealed class WinSpeech : ISpeech
        {
            private const string Script =
                "Add-Type -AssemblyName System.Speech;" +
                "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer;$s.Rate=1;" +
                "while(($l=[Console]::In.ReadLine()) -ne $null){" +
                " if($l -eq 'X'){$s.SpeakAsyncCancelAll();continue};" +
                " $p=$l.Split('|',3);$s.SpeakAsyncCancelAll();" +
                " try{if($p[0] -eq '0'){$s.SelectVoiceByHints('Female')}else{$s.SelectVoiceByHints('Male')}}catch{};" +
                " $s.Volume=[int]$p[1];[void]$s.SpeakAsync($p[2])}";

            private readonly System.Diagnostics.Process _p;
            private float _until;

            public WinSpeech()
            {
                var info = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + Script + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true };
                _p = System.Diagnostics.Process.Start(info);
                if (_p == null) throw new Exception("PowerShell not available");
                void Kill() { try { if (!_p.HasExited) _p.Kill(); } catch (Exception) { } }
                Application.quitting += Kill;
                AppDomain.CurrentDomain.DomainUnload += (_, __) => Kill();   // editor: leaving play mode / recompiling
            }

            public bool Ready => _p != null && !_p.HasExited;
            // System.Speech doesn't report back over the pipe; estimate from the length (about 2.7 words/s).
            public bool Speaking => Time.unscaledTime < _until;

            public void Speak(string text, Voice voice, float volume)
            {
                text = text.Replace("\n", " ").Replace("\r", " ").Replace("|", " ");
                _p.StandardInput.WriteLine($"{(voice == Voice.Dispatcher ? 0 : 1)}|{Mathf.RoundToInt(volume * 100f)}|{text}");
                _p.StandardInput.Flush();
                int words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                _until = Time.unscaledTime + 0.4f + words / 2.7f;
            }

            public void Stop()
            {
                _until = 0f;
                if (Ready) { _p.StandardInput.WriteLine("X"); _p.StandardInput.Flush(); }
            }
        }
#endif

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private sealed class MacSpeech : ISpeech
        {
            private System.Diagnostics.Process _p;
            public bool Ready => true;
            public bool Speaking => _p != null && !_p.HasExited;
            public void Speak(string text, Voice voice, float volume)
            {
                Stop();
                string v = voice == Voice.Dispatcher ? "Samantha" : "Alex";
                _p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("say", $"-v {v} -r 200 \"{text.Replace("\"", "")}\"") { UseShellExecute = false, CreateNoWindow = true });
            }
            public void Stop() { try { if (Speaking) _p.Kill(); } catch (Exception) { } }
        }
#endif
    }
}
