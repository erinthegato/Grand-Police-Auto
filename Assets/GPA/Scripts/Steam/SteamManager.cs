#if !(UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX)
#define DISABLESTEAMWORKS
#endif

using UnityEngine;
#if !DISABLESTEAMWORKS
using Steamworks;
#endif

namespace GPA.Steam
{
    /// <summary>
    /// Connects the game to Steam on PC. Created automatically when the first scene loads,
    /// so no scene needs editing. If Steam isn't running (or this isn't a Steam copy) the game
    /// still plays normally, just without Steam features.
    /// Phones and consoles never compile the Steam parts (see GPA.Steam.asmdef platforms).
    /// </summary>
    public sealed class SteamManager : MonoBehaviour
    {
        /// <summary>
        /// The game's Steam App ID. 480 is Valve's public test app ("Spacewar"), used until
        /// Lights &amp; Sirens: Halden gets its own ID. Change it here AND in steam_appid.txt.
        /// </summary>
        public const uint AppId = 480;

        /// <summary>True once Steam has started successfully.</summary>
        public static bool Initialized { get; private set; }

        private static SteamManager _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateOnStartup()
        {
            if (_instance != null) return;
            var go = new GameObject("Steam");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<SteamManager>();
        }

        /// <summary>The player's Steam name, or an empty string when Steam isn't running.</summary>
        public static string PlayerName
        {
            get
            {
#if !DISABLESTEAMWORKS
                if (Initialized) return SteamFriends.GetPersonaName();
#endif
                return "";
            }
        }

#if !DISABLESTEAMWORKS
        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;

            if (!Packsize.Test()) { Debug.LogError("[Steam] Wrong Steamworks.NET build for this platform."); return; }
            if (!DllCheck.Test()) { Debug.LogError("[Steam] steam_api64.dll is the wrong version."); return; }

#if !UNITY_EDITOR
            // Release builds with the real App ID: if someone opens the .exe directly,
            // relaunch it through Steam. Skipped for the 480 test ID and for test builds.
            if (AppId != 480 && !Debug.isDebugBuild)
            {
                try
                {
                    if (SteamAPI.RestartAppIfNecessary(new AppId_t(AppId))) { Application.Quit(); return; }
                }
                catch (System.DllNotFoundException e)
                {
                    Debug.LogError("[Steam] steam_api64.dll is missing: " + e.Message);
                    Application.Quit();
                    return;
                }
            }
#endif

            try { Initialized = SteamAPI.Init(); }
            catch (System.DllNotFoundException e) { Debug.LogWarning("[Steam] steam_api64.dll not found: " + e.Message); }

            if (Initialized) Debug.Log($"[Steam] Connected as {SteamFriends.GetPersonaName()} (App ID {AppId}).");
            else Debug.Log("[Steam] Steam isn't running; playing without Steam features.");
        }

        private void Update()
        {
            if (Initialized) SteamAPI.RunCallbacks();
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            _instance = null;
            if (!Initialized) return;
            SteamAPI.Shutdown();
            Initialized = false;
        }
#endif
    }
}
