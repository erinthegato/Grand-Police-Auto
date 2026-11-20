#if DISCORD_SOCIAL_SDK
using System;
using Discord.Sdk;
using UnityEngine;

namespace GPA.DiscordIntegration
{
    /// <summary>
    /// The Discord Social SDK side of <see cref="DiscordPresence"/>. Compiled only when the SDK package is in
    /// the project (the editor adds the DISCORD_SOCIAL_SDK define automatically when it finds it).
    ///  - PC: Rich Presence through the Discord app running on the same computer. No login needed.
    ///  - iPhone: Discord requires a linked account. The player links once from Settings; the refresh
    ///    token is kept so later launches reconnect by themselves.
    /// </summary>
    internal sealed class DiscordSdkBackend : IPresenceBackend
    {
        private const string RefreshPref = "GPA.Discord.RefreshToken";

        private readonly ulong _appId;
        private Client _client;
        private string _status = "Starting…";
        private bool _ready;
        private string _verifier;

        public DiscordSdkBackend(ulong appId)
        {
            _appId = appId;
            _client = new Client();
            _client.AddLogCallback((message, severity) => Debug.Log($"[Discord] {severity}: {message}"), LoggingSeverity.Error);
            _client.SetStatusChangedCallback(OnStatusChanged);
#if UNITY_IOS && !UNITY_EDITOR
            _status = "Not linked";
            string refresh = PlayerPrefs.GetString(RefreshPref, "");
            if (!string.IsNullOrEmpty(refresh)) Reconnect(refresh);
#else
            // Desktop: presence goes through the local Discord app, no account linking.
            _client.SetApplicationId(_appId);
            _ready = true;
            _status = "On (needs the Discord app running)";
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        public bool NeedsLink => true;
        public bool Linked => _ready;
#else
        public bool NeedsLink => false;
        public bool Linked => true;
#endif
        public bool CanPublish => _ready;
        public string Status => _status;

        private void OnStatusChanged(Client.Status status, Client.Error error, int errorCode)
        {
            if (status == Client.Status.Ready) { _ready = true; _status = "Linked"; }
            else if (status == Client.Status.Disconnected && NeedsLink) { _ready = false; _status = PlayerPrefs.HasKey(RefreshPref) ? "Reconnecting…" : "Not linked"; }
            if (error != Client.Error.None) Debug.LogWarning($"[Discord] {error} ({errorCode})");
        }

        public void Publish(PresenceInfo info, Action<bool> done)
        {
            if (_client == null) { done?.Invoke(false); return; }
            var activity = new Activity();
            activity.SetType(ActivityTypes.Playing);
            activity.SetDetails(info.Details ?? "");
            if (!string.IsNullOrEmpty(info.State)) activity.SetState(info.State);

            var assets = new ActivityAssets();
            if (!string.IsNullOrEmpty(info.LargeImage)) { assets.SetLargeImage(info.LargeImage); assets.SetLargeText(info.LargeText ?? ""); }
            if (!string.IsNullOrEmpty(info.SmallImage)) { assets.SetSmallImage(info.SmallImage); assets.SetSmallText(info.SmallText ?? ""); }
            activity.SetAssets(assets);

            if (info.StartUnix > 0)
            {
                var times = new ActivityTimestamps();
                times.SetStart(info.StartUnix);
                activity.SetTimestamps(times);
            }

            _client.UpdateRichPresence(activity, (ClientResult result) =>
            {
                bool ok = result.Successful();
                if (!ok) Debug.LogWarning("[Discord] Rich Presence update failed: " + result.Error());
#if !(UNITY_IOS && !UNITY_EDITOR)
                _status = ok ? "On" : "Discord app not running";
#endif
                done?.Invoke(ok);
            });
        }

        public void Clear()
        {
            try { _client?.ClearRichPresence(); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ account linking (iPhone)

        public void Link()
        {
            if (!NeedsLink || _client == null) return;
            _status = "Opening Discord…";
            var verifier = _client.CreateAuthorizationCodeVerifier();
            _verifier = verifier.Verifier();
            var args = new AuthorizationArgs();
            args.SetClientId(_appId);
            args.SetScopes(Client.GetDefaultPresenceScopes());
            args.SetCodeChallenge(verifier.Challenge());
            _client.Authorize(args, (ClientResult result, string code, string redirectUri) =>
            {
                if (!result.Successful()) { _status = "Linking cancelled"; return; }
                _client.GetToken(_appId, code, _verifier, redirectUri,
                    (tokenResult, token, refreshToken, tokenType, expiresIn, scope) => UseToken(token, refreshToken));
            });
        }

        private void Reconnect(string refreshToken)
        {
            _status = "Reconnecting…";
            _client.RefreshToken(_appId, refreshToken,
                (result, token, newRefresh, tokenType, expiresIn, scope) => UseToken(token, newRefresh));
        }

        private void UseToken(string token, string refreshToken)
        {
            if (string.IsNullOrEmpty(token)) { _status = "Couldn't link — try again"; PlayerPrefs.DeleteKey(RefreshPref); return; }
            if (!string.IsNullOrEmpty(refreshToken)) { PlayerPrefs.SetString(RefreshPref, refreshToken); PlayerPrefs.Save(); }
            _client.UpdateToken(AuthorizationTokenType.Bearer, token, (ClientResult r) => _client.Connect());
        }

        public void Unlink()
        {
            if (!NeedsLink) return;
            PlayerPrefs.DeleteKey(RefreshPref);
            PlayerPrefs.Save();
            _ready = false;
            _status = "Not linked";
            try { _client?.Disconnect(); } catch (Exception) { }
        }

        public void Dispose()
        {
            try { _client?.Disconnect(); } catch (Exception) { }
            _client = null;
        }
    }
}
#endif
