using System;
using UnityEngine;

namespace GPA.Core
{
    public interface ITimeOfDay
    {
        /// <summary>0..24 hours.</summary>
        float Hour { get; }
        int Day { get; }
        /// <summary>In-game minutes per real second.</summary>
        float TimeScale { get; set; }
        bool Paused { get; set; }
        void SetTime(float hour);
        /// <summary>Sets which in-game day it is (0 = the first day), keeping the time of day. Used when loading a save.</summary>
        void SetDay(int day);
        /// <summary>Fires when the whole hour changes.</summary>
        event Action<int> HourChanged;
        DateTime GameDateTime { get; }
    }

    /// <summary>Simulated clock. Ticked by the bootstrapper.</summary>
    public sealed class TimeOfDayService : ITimeOfDay
    {
        private readonly DateTime _epoch;
        private double _totalHours;

        public TimeOfDayService(float startHour, float minutesPerRealSecond, DateTime epoch)
        {
            _epoch = epoch.Date;
            _totalHours = startHour;
            TimeScale = minutesPerRealSecond;
        }

        public float Hour => (float)(_totalHours % 24.0);
        public int Day => (int)(_totalHours / 24.0);
        public float TimeScale { get; set; }
        public bool Paused { get; set; }
        public DateTime GameDateTime => _epoch.AddHours(_totalHours);
        public event Action<int> HourChanged;

        public void SetTime(float hour)
        {
            int before = Mathf.FloorToInt(Hour);
            _totalHours = Day * 24.0 + Mathf.Repeat(hour, 24f);
            int after = Mathf.FloorToInt(Hour);
            if (before != after) HourChanged?.Invoke(after);
        }

        public void SetDay(int day) => _totalHours = Math.Max(0, day) * 24.0 + Hour;

        public void Tick(float unscaledDelta)
        {
            if (Paused) return;
            int before = Mathf.FloorToInt(Hour);
            _totalHours += unscaledDelta * TimeScale / 60.0;
            int after = Mathf.FloorToInt(Hour);
            if (before != after) HourChanged?.Invoke(after);
        }
    }

    /// <summary>
    /// The in-game date and time, for anything written into records (arrests, citations, reports,
    /// dispatch logs). Falls back to the real clock only when there is no game clock (e.g. in tests).
    /// </summary>
    public static class GameClock
    {
        public static DateTime Now => Services.TryGet(out ITimeOfDay clock) ? clock.GameDateTime : DateTime.Now;
        /// <summary>"yyyy-MM-dd HH:mm", the format records store timestamps in.</summary>
        public static string Stamp => Now.ToString("yyyy-MM-dd HH:mm");
        /// <summary>"yyyy-MM-dd".</summary>
        public static string Today => Now.ToString("yyyy-MM-dd");
    }
}
