using UnityEngine;

namespace GPA.DiscordIntegration
{
    /// <summary>
    /// Discord settings, kept in Assets/GPA/Resources/DiscordSettings.asset.
    /// Tools > GPA > Discord > Settings opens it. Paste your application's Client ID from the
    /// Discord Developer Portal (OAuth2 tab), and upload art with the image keys below under
    /// Rich Presence > Art Assets.
    /// </summary>
    [CreateAssetMenu(menuName = "GPA/Discord Settings", fileName = "DiscordSettings")]
    public sealed class DiscordSettings : ScriptableObject
    {
        [Tooltip("Client ID (Application ID) from the Discord Developer Portal, OAuth2 tab. Leave empty to turn Discord off.")]
        public string ApplicationId = "";

        public bool EnableOnPc = true;
        [Tooltip("iPhone needs the player to link their Discord account once (Settings > Discord).")]
        public bool EnableOnIos = true;

        [Header("Rich Presence art (keys uploaded in the Developer Portal, lower case)")]
        public string LargeImage = "logo";
        public string LargeImageText = "Lights & Sirens: Halden";
        public string SmallImageCar = "cruiser";
        public string SmallImageFoot = "officer";
        public string SmallImageMenu = "";

        [Tooltip("Seconds between presence checks. Discord rate-limits updates, so only changes are sent.")]
        public float UpdateSeconds = 5f;

        public bool TryGetApplicationId(out ulong id) => ulong.TryParse((ApplicationId ?? "").Trim(), out id) && id != 0;

        public static DiscordSettings Load() => Resources.Load<DiscordSettings>("DiscordSettings");
    }
}
