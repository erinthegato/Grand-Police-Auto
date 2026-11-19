using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Display defaults applied before the first scene loads.
    /// Handheld: mobile builds are driven by an MFi / standard gamepad (the Input System exposes both
    /// as <see cref="UnityEngine.InputSystem.Gamepad"/>), so there is no touch UI: the game runs at
    /// 60 fps, the screen is kept awake and the OS cursor stays hidden.
    /// Desktop: v-sync on, so the game runs at the monitor's refresh rate instead of rendering
    /// hundreds of frames a second (hot, loud GPU; drained laptop batteries). If the graphics driver
    /// overrides v-sync off, the frame-rate cap below still limits it to the refresh rate.
    /// </summary>
    internal static class MobileRuntime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Configure()
        {
            if (!Application.isMobilePlatform)
            {
                QualitySettings.vSyncCount = 1;
                double hz = Screen.currentResolution.refreshRateRatio.value;
                Application.targetFrameRate = hz >= 30 ? Mathf.RoundToInt((float)hz) : 60;
                return;
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Cursor.visible = false;
        }
    }
}
