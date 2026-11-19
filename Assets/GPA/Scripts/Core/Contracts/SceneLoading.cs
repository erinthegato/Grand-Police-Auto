namespace GPA.Core
{
    /// <summary>Scene names in one place so doors, spawners and loaders cannot drift apart.</summary>
    public static class GameScenes
    {
        public const string City = "City_Midtown";
        public const string PrecinctInterior = "Precinct_Interior";
    }

    /// <summary>
    /// Loads a scene behind a loading screen. Implemented by the HUD assembly so the world layer can
    /// request a scene change without depending on any UI code.
    /// </summary>
    public interface ISceneLoadingService
    {
        bool IsLoading { get; }
        /// <summary>Load <paramref name="sceneName"/>, then place the player at the named spawn point if given.</summary>
        void LoadScene(string sceneName, string spawnPointName = null);
    }
}
