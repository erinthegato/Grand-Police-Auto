using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GPA.Input
{
    /// <summary>Saves and loads binding overrides as JSON under persistentDataPath/GPA.</summary>
    public static class BindingPersistence
    {
        public static string FilePath => Path.Combine(Application.persistentDataPath, "GPA", "bindings.json");

        public static void Save(InputActionAsset asset)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string json = asset.SaveBindingOverridesAsJson();
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Input] Failed to save bindings: {ex.Message}");
            }
        }

        public static bool Load(InputActionAsset asset)
        {
            if (!File.Exists(FilePath)) return false;
            try
            {
                string json = File.ReadAllText(FilePath);
                asset.LoadBindingOverridesFromJson(json, removeExisting: true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Input] Bindings file corrupted, reverting to defaults: {ex.Message}");
                asset.RemoveAllBindingOverrides();
                try { File.Move(FilePath, FilePath + ".corrupt." + DateTime.Now.Ticks); } catch { /* ignore */ }
                return false;
            }
        }

        public static void Delete()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
    }
}
