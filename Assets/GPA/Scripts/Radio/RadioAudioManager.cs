using System;
using System.IO;
using GPA.Core;
using UnityEngine;

namespace GPA.Radio
{
    /// <summary>
    /// Dispatch radio volume and mute, saved to {persistentDataPath}/GPA/radio.json.
    /// (The live internet police-radio feed that used to live here has been removed.)
    /// </summary>
    public sealed class RadioAudioManager : MonoBehaviour, IRadioAudioManager
    {
        [Serializable]
        private sealed class SettingsFile
        {
            public int Version = 2;
            public float DispatchVolume = 1f;
            public bool DispatchMuted;
        }

        private SettingsFile _s = new SettingsFile();
        public static RadioAudioManager Instance { get; private set; }
        private static string FilePath => Path.Combine(Application.persistentDataPath, "GPA", "radio.json");

        private void Awake()
        {
            Instance = this;
            Load();
            Services.Register<IRadioAudioManager>(this);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Services.Unregister<IRadioAudioManager>(this);
        }

        public float DispatchVolume { get => _s.DispatchVolume; set { _s.DispatchVolume = Mathf.Clamp01(value); Save(); } }
        public bool DispatchMuted { get => _s.DispatchMuted; set { _s.DispatchMuted = value; Save(); } }
        public float DispatchGain => _s.DispatchMuted ? 0f : _s.DispatchVolume;

        private void Load()
        {
            try { if (File.Exists(FilePath)) _s = JsonUtility.FromJson<SettingsFile>(File.ReadAllText(FilePath)) ?? new SettingsFile(); }
            catch (Exception) { _s = new SettingsFile(); }
        }

        private void Save()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, JsonUtility.ToJson(_s, true)); }
            catch (Exception e) { Debug.LogWarning("[Radio] " + e.Message); }
        }
    }
}
