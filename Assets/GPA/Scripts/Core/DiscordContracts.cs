namespace GPA.Core
{
    /// <summary>
    /// Discord integration as the menus see it: whether it's on this build, whether this platform needs
    /// the player to link their Discord account (iPhone does; PC uses the running Discord app), and the
    /// player's own on/off choice. Implemented by the Discord presence component when the SDK is installed.
    /// </summary>
    public interface IDiscordService
    {
        /// <summary>The SDK is installed and an application ID is set.</summary>
        bool Supported { get; }
        /// <summary>This platform only shows activity after the player links their Discord account (iOS).</summary>
        bool NeedsLink { get; }
        bool Linked { get; }
        /// <summary>Short status for the settings row: "Connected", "Discord app not running", "Not linked"…</summary>
        string StatusText { get; }
        /// <summary>The player's choice to show what they're doing on their Discord profile.</summary>
        bool ShowActivity { get; set; }
        /// <summary>Opens Discord (app or browser) to link the player's account.</summary>
        void Link();
        void Unlink();
    }
}
