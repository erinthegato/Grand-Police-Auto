using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Configurable radio codes (spec §15). Defaults ship in Resources/radio_codes.json; a player can
    /// override any of them with {persistentDataPath}/GPA/radio_codes.json (same format), e.g. to use
    /// their own department's 10-codes or plain-language codes.
    /// </summary>
    public static class RadioCodes
    {
        [System.Serializable] private class Entry { public string key; public string code; }
        [System.Serializable] private class FileModel { public List<Entry> codes = new List<Entry>(); }

        private static Dictionary<string, string> _codes;

        public static string OverridePath => Path.Combine(Application.persistentDataPath, "GPA", "radio_codes.json");

        public static string Get(string key, string fallback)
        {
            if (_codes == null) Load();
            return _codes.TryGetValue(key, out string c) && !string.IsNullOrEmpty(c) ? c : fallback;
        }

        public static void Load()
        {
            _codes = new Dictionary<string, string>();
            var builtIn = Resources.Load<TextAsset>("radio_codes");
            if (builtIn != null) Merge(builtIn.text);
            try { if (File.Exists(OverridePath)) Merge(File.ReadAllText(OverridePath)); }
            catch (System.Exception e) { Debug.LogWarning("[RadioCodes] " + e.Message); }
        }

        private static void Merge(string json)
        {
            try
            {
                var f = JsonUtility.FromJson<FileModel>(json);
                if (f?.codes != null) foreach (var e in f.codes) if (!string.IsNullOrEmpty(e.key)) _codes[e.key] = e.code;
            }
            catch (System.Exception e) { Debug.LogWarning("[RadioCodes] Bad file: " + e.Message); }
        }
    }

    /// <summary>Backup and officer-emergency requests (spec §15). Implemented by the police module.</summary>
    public interface IBackupService
    {
        /// <summary>Units responding or on scene.</summary>
        int UnitsAssigned { get; }
        /// <summary>Normal backup (one unit), or emergency "officer needs assistance" (two units, priority traffic).</summary>
        void RequestBackup(bool emergency);
        /// <summary>Code 4: send the backup units back into service.</summary>
        void CancelBackup();
    }
}
