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

        public void Tick(float unscaledDelta)
        {
            if (Paused) return;
            int before = Mathf.FloorToInt(Hour);
            _totalHours += unscaledDelta * TimeScale / 60.0;
            int after = Mathf.FloorToInt(Hour);
            if (before != after) HourChanged?.Invoke(after);
        }
    }
}
