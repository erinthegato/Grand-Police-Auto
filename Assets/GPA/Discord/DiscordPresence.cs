using System;
using GPA.Core;
using GPA.Game;
using GPA.TopDown2D;
using UnityEngine;

namespace GPA.DiscordIntegration
{
    /// <summary>What the player's Discord profile shows.</summary>
    internal struct PresenceInfo : IEquatable<PresenceInfo>
    {
        public string Details, State, LargeImage, LargeText, SmallImage, SmallText;
        public ulong StartUnix;

        public bool Equals(PresenceInfo o) =>
            Details == o.Details && State == o.State && LargeImage == o.LargeImage && LargeText == o.LargeText &&
            SmallImage == o.SmallImage && SmallText == o.SmallText && StartUnix == o.StartUnix;
    }

    /// <summary>The SDK side: PC publishes through the running Discord app, iPhone through a linked account.</summary>
    internal interface IPresenceBackend : IDisposable
    {
        bool NeedsLink { get; }
        bool Linked { get; }
        bool CanPublish { get; }
        string Status { get; }
        void Publish(PresenceInfo info, Action<bool> done);
        void Clear();
        void Link();
        void Unlink();
    }

    /// <summary>
    /// Discord Rich Presence for Lights & Sirens: Halden (PC and iPhone): "Playing Lights & Sirens: Halden · On patrol ·
    /// Harbor Point", "Responding to a call · 10-37 Suspicious Person", with elapsed time and art.
    /// Needs the Discord Social SDK (Developer Portal > Social SDK > Downloads) and an application ID in
    /// Resources/DiscordSettings. Without them it does nothing and the game builds and runs as normal.
    /// Never shows street addresses, only the district and the kind of call.
    /// </summary>
    public sealed class DiscordPresence : MonoBehaviour, IDiscordService
    {
        private const string ShowPref = "GPA.Discord.ShowActivity";

        private DiscordSettings _settings;
        private IPresenceBackend _backend;
        private PresenceInfo _sent;
        private bool _hasSent, _sending;
        private float _next;
        private ulong _launchUnix, _shiftUnix;
        private bool _wasOnDuty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<DiscordPresence>() != null) return;
            var settings = DiscordSettings.Load();
            if (settings == null || !settings.TryGetApplicationId(out _)) return;
#if UNITY_IOS
            if (!settings.EnableOnIos) return;
#elif UNITY_STANDALONE || UNITY_EDITOR
            if (!settings.EnableOnPc) return;
#else
            return;
#endif
            var go = new GameObject("Discord Presence");
            DontDestroyOnLoad(go);
            go.AddComponent<DiscordPresence>()._settings = settings;
        }

        private void Start()
        {
            _launchUnix = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _settings ??= DiscordSettings.Load();
            if (_settings == null || !_settings.TryGetApplicationId(out ulong appId)) { enabled = false; return; }
            _backend = CreateBackend(appId);
            if (_backend == null) { enabled = false; return; }   // SDK not installed: stay silent
            Services.Register<IDiscordService>(this);
        }

        private static IPresenceBackend CreateBackend(ulong appId)
        {
#if DISCORD_SOCIAL_SDK
            try { return new DiscordSdkBackend(appId); }
            catch (Exception e) { Debug.LogWarning("[Discord] Could not start the Discord SDK: " + e.Message); return null; }
#else
            return null;
#endif
        }

        private void OnDestroy()
        {
            if (_backend == null) return;
            Services.Unregister<IDiscordService>(this);
            try { _backend.Clear(); _backend.Dispose(); } catch (Exception) { }
            _backend = null;
        }

        // ------------------------------------------------------------------ IDiscordService (settings menu)

        public bool Supported => _backend != null;
        public bool NeedsLink => _backend != null && _backend.NeedsLink;
        public bool Linked => _backend != null && _backend.Linked;
        public string StatusText => _backend == null ? "Not available" : !ShowActivity ? "Hidden" : _backend.Status;

        public bool ShowActivity
        {
            get => PlayerPrefs.GetInt(ShowPref, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(ShowPref, value ? 1 : 0);
                PlayerPrefs.Save();
                if (!value) { _backend?.Clear(); _hasSent = false; }
                _next = 0f;
            }
        }

        public void Link() => _backend?.Link();

        public void Unlink()
        {
            _backend?.Clear();
            _backend?.Unlink();
            _hasSent = false;
        }

        // ------------------------------------------------------------------ presence

        private void Update()
        {
            if (_backend == null || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Mathf.Max(2f, _settings.UpdateSeconds);
            if (!ShowActivity || !_backend.CanPublish || _sending) return;

            PresenceInfo info = Build();
            if (_hasSent && info.Equals(_sent)) return;
            _sending = true;
            _backend.Publish(info, ok =>
            {
                _sending = false;
                if (ok) { _sent = info; _hasSent = true; }
            });
        }

        private PresenceInfo Build()
        {
            var info = new PresenceInfo
            {
                LargeImage = _settings.LargeImage,
                LargeText = _settings.LargeImageText,
                StartUnix = _launchUnix,
            };

            if (GameMenu2D.TitleShowing || !GameMenu2D.HasStarted)
            {
                info.Details = "In the main menu";
                info.State = "";
                info.SmallImage = _settings.SmallImageMenu;
                return info;
            }

            var officer = FindAnyObjectByType<Officer2D>();
            Vector2 pos = officer != null ? (Vector2)officer.transform.position : Vector2.zero;
            string district = Services.TryGet(out ILocationService loc) ? loc.DistrictAt(pos) : null;
            string city = loc != null ? loc.CityName : "New Halden";
            string where = string.IsNullOrEmpty(district) ? city : $"{district}, {city}";

            bool onDuty = Services.TryGet(out ICadCallSource cad) && cad.OnDuty;
            if (onDuty && !_wasOnDuty) _shiftUnix = (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _wasOnDuty = onDuty;
            if (onDuty && _shiftUnix > 0) info.StartUnix = _shiftUnix;   // the timer shows time on shift

            string rank = Services.TryGet(out IReputationService rep) ? rep.Rank : "Officer";
            OfficerStatus status = Services.TryGet(out IOfficerStatusService st) ? st.Status : OfficerStatus.OutOfService;
            CadCall current = default;
            bool hasCall = cad != null && cad.TryGetCurrentCall(out current) && current.Phase != CalloutPhase.Offered;

            if (!onDuty)
            {
                info.Details = "Off duty";
                info.State = where;
            }
            else if (hasCall && status == OfficerStatus.EnRoute)
            {
                info.Details = "Responding to a call";
                info.State = $"{current.Code} {current.Title}".Trim();
            }
            else if (hasCall)
            {
                info.Details = $"On scene · {current.Title}";
                info.State = where;
            }
            else
            {
                switch (status)
                {
                    case OfficerStatus.TrafficStop: info.Details = "Conducting a traffic stop"; break;
                    case OfficerStatus.Busy: info.Details = "Busy (10-6)"; break;
                    default: info.Details = $"On patrol · {rank}"; break;
                }
                info.State = where;
            }

            Car2D car = officer != null ? officer.CurrentCar : null;
            if (car != null)
            {
                info.SmallImage = _settings.SmallImageCar;
                info.SmallText = car.Hover ? "Air unit" : car.Spec != null && !string.IsNullOrEmpty(car.Spec.DisplayName) ? car.Spec.DisplayName : "Patrol car";
            }
            else
            {
                info.SmallImage = _settings.SmallImageFoot;
                info.SmallText = "On foot";
            }
            return info;
        }
    }
}
