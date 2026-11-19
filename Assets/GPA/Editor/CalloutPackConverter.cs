using System.IO;
using GPA.Callouts;
using UnityEditor;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>Turns a JSON callout pack into ScriptableObject definitions plus a pack manifest.</summary>
    internal static class CalloutPackConverter
    {
        [MenuItem("Tools/GPA/Callouts/Convert JSON pack to assets...")]
        private static void Convert()
        {
            string json = EditorUtility.OpenFilePanel("Callout pack JSON", "Assets/GPA/Resources/CalloutPacks", "json");
            if (string.IsNullOrEmpty(json)) return;
            var pack = JsonUtility.FromJson<CalloutPack>(File.ReadAllText(json));
            if (pack?.callouts == null) { EditorUtility.DisplayDialog("Callouts", "That file isn't a callout pack.", "OK"); return; }

            string name = Path.GetFileNameWithoutExtension(json);
            string folder = $"Assets/GPA/Data/Callouts/{name}";
            Directory.CreateDirectory(folder);
            var manifest = ScriptableObject.CreateInstance<CalloutPackManifest>();
            manifest.PackName = pack.name ?? name; manifest.Author = pack.author; manifest.Version = pack.version;
            foreach (CalloutDefinition d in pack.callouts)
            {
                if (d == null || string.IsNullOrEmpty(d.id)) continue;
                var asset = ScriptableObject.CreateInstance<CalloutDefinitionAsset>();
                asset.Definition = d;
                AssetDatabase.CreateAsset(asset, $"{folder}/{d.id}.asset");
                manifest.Callouts.Add(asset);
            }
            Directory.CreateDirectory("Assets/GPA/Resources/CalloutPacks");
            AssetDatabase.CreateAsset(manifest, $"Assets/GPA/Resources/CalloutPacks/{name}_manifest.asset");
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("Callouts", $"Made {manifest.Callouts.Count} definition assets in {folder}.\n\nRemove or rename the JSON file if you no longer want it loaded too (JSON entries with the same id override the assets).", "OK");
        }
    }
}
