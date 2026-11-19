using System;
using System.IO;
using UnityEngine;

namespace GPA.Core
{
    /// <summary>
    /// Crash-safe text files for saves. Writes go to a temporary file first and then replace the
    /// real one, keeping the previous version as "&lt;name&gt;.bak". Reads fall back to that backup if
    /// the main file is missing, empty or can't be parsed, so a crash or power cut mid-save never
    /// wipes the player's progress.
    /// </summary>
    public static class SafeFile
    {
        public static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp", bak = path + ".bak";
            File.WriteAllText(tmp, text);
            if (File.Exists(path))
            {
                try { File.Replace(tmp, path, bak, true); return; }
                catch (Exception) { File.Copy(path, bak, true); File.Delete(path); }
            }
            File.Move(tmp, path);
        }

        /// <summary>Parses the save (or its backup). Returns null if neither exists or both are unreadable.</summary>
        public static T Read<T>(string path, string tag) where T : class
        {
            foreach (string p in new[] { path, path + ".bak" })
            {
                try
                {
                    if (!File.Exists(p)) continue;
                    string text = File.ReadAllText(p);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    T value = JsonUtility.FromJson<T>(text);
                    if (value == null) continue;
                    if (p != path) Debug.LogWarning($"[{tag}] Main save was unreadable; restored from backup.");
                    return value;
                }
                catch (Exception e) { Debug.LogWarning($"[{tag}] Could not read {Path.GetFileName(p)}: {e.Message}"); }
            }
            return null;
        }
    }
}
