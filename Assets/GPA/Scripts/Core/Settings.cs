using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GPA.Core
{
    public interface ISettings
    {
        float GetFloat(string key, float fallback);
        int GetInt(string key, int fallback);
        bool GetBool(string key, bool fallback);
        string GetString(string key, string fallback);
        void Set(string key, float value);
        void Set(string key, int value);
        void Set(string key, bool value);
        void Set(string key, string value);
        void Save();
        event Action<string> Changed;
    }

    public static class SettingKeys
    {
        public const string CameraFirstPerson = "camera.firstPerson";
        public const string MouseSensitivity = "input.mouseSensitivity";
        public const string GamepadSensitivity = "input.gamepadSensitivity";
        public const string InvertY = "input.invertY";
        public const string FieldOfView = "camera.fov";
        public const string TrafficDensity = "gameplay.trafficDensity";
        public const string PedDensity = "gameplay.pedDensity";
        public const string WheelSlowMo = "gameplay.wheelSlowMo";
        public const string MinimapRotate = "hud.minimapRotate";
        public const string MasterVolume = "audio.master";
        public const string RadioVolume = "audio.radio";
    }

    /// <summary>
    /// JSON key/value settings stored at persistentDataPath/GPA/settings.json with atomic writes.
    /// A corrupted file is moved aside and defaults are used.
    /// </summary>
    public sealed class JsonSettings : ISettings
    {
        [Serializable] private class Entry { public string k; public string v; }
        [Serializable] private class FileModel { public int version = 1; public List<Entry> entries = new List<Entry>(); }

        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
        private readonly string _path;
        private bool _dirty;

        public event Action<string> Changed;

        public JsonSettings(string path = null)
        {
            _path = path ?? Path.Combine(Application.persistentDataPath, "GPA", "settings.json");
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_path)) return;
            try
            {
                var model = JsonUtility.FromJson<FileModel>(File.ReadAllText(_path));
                if (model?.entries == null) throw new InvalidDataException("empty");
                foreach (var e in model.entries) if (!string.IsNullOrEmpty(e.k)) _values[e.k] = e.v;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Settings] Corrupted settings file, using defaults ({ex.Message}).");
                try { File.Move(_path, _path + ".corrupt." + DateTime.Now.Ticks); } catch { /* ignore */ }
            }
        }

        public void Save()
        {
            if (!_dirty) return;
            try
            {
                var model = new FileModel();
                foreach (var kv in _values) model.entries.Add(new Entry { k = kv.Key, v = kv.Value });
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(model, true));
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
                _dirty = false;
            }
            catch (Exception ex) { Debug.LogError($"[Settings] Save failed: {ex.Message}"); }
        }

        private bool TryRaw(string key, out string v) => _values.TryGetValue(key, out v);

        public float GetFloat(string key, float fallback) =>
            TryRaw(key, out var v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : fallback;
        public int GetInt(string key, int fallback) => TryRaw(key, out var v) && int.TryParse(v, out var i) ? i : fallback;
        public bool GetBool(string key, bool fallback) => TryRaw(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;
        public string GetString(string key, string fallback) => TryRaw(key, out var v) ? v : fallback;

        public void Set(string key, float value) => SetRaw(key, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        public void Set(string key, int value) => SetRaw(key, value.ToString());
        public void Set(string key, bool value) => SetRaw(key, value.ToString());
        public void Set(string key, string value) => SetRaw(key, value ?? string.Empty);

        private void SetRaw(string key, string value)
        {
            if (_values.TryGetValue(key, out var old) && old == value) return;
            _values[key] = value;
            _dirty = true;
            Changed?.Invoke(key);
        }
    }
}
