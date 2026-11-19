using System;

namespace GPA.Core
{
    /// <summary>
    /// Career reputation: earned on calls, stops and arrests, lost for failures. Levels unlock fleet
    /// vehicles (and anything else that wants to gate on rank).
    /// </summary>
    public interface IReputationService
    {
        int Points { get; }
        int Level { get; }
        string Rank { get; }
        /// <summary>Points where the current level started and where the next one starts (-1 at max).</summary>
        int LevelStart { get; }
        int NextLevelAt { get; }
        void Add(int points, string reason);
        event Action<int> LevelChanged;
    }

    public static class ReputationLevels
    {
        public static readonly int[] Thresholds = { 0, 400, 1200, 2500, 4500, 7500, 11000 };
        public static readonly string[] Ranks = { "Probationary Officer", "Officer", "Senior Officer", "Corporal", "Sergeant", "Lieutenant", "SWAT Commander" };
        public static int MaxLevel => Thresholds.Length;

        public static int LevelFor(int points)
        {
            int level = 1;
            for (int i = 0; i < Thresholds.Length; i++) if (points >= Thresholds[i]) level = i + 1;
            return level;
        }
    }
}
