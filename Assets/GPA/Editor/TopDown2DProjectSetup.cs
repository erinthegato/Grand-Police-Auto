using System.Collections.Generic;
using GPA.TopDown2D;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.U2D;

namespace GPA.EditorTools
{
    /// <summary>
    /// One-click 2D project setup (spec §3 "2D rendering"): sorting layers, a sprite atlas over the
    /// generated sprites, and URP's 2D Renderer as the default renderer on the PC and Mobile
    /// pipeline assets. The previous 3D renderers stay in each asset's renderer list, so switching
    /// back is a one-field change. Safe to run more than once.
    /// </summary>
    public static class TopDown2DProjectSetup
    {
        private const string AtlasPath = "Assets/GPA/Art/Sprites2D.spriteatlasv2";
        private const string Renderer2DPath = "Assets/Settings/Renderer2D.asset";
        private static readonly string[] PipelineAssets = { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" };

        [MenuItem("GPA/Top-Down 2D/Setup 2D Project (layers, atlas, 2D renderer)")]
        public static void Run()
        {
            var report = new List<string>();
            report.Add(SetupSortingLayers());
            report.Add(SetupAtlas());
            report.Add(Setup2DRenderer());
            AssetDatabase.SaveAssets();
            Debug.Log("[2D Setup]\n" + string.Join("\n", report));
        }

        private static string SetupSortingLayers()
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("m_SortingLayers");
            var existing = new HashSet<string>();
            for (int i = 0; i < layers.arraySize; i++) existing.Add(layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);

            int added = 0;
            foreach (string name in Sorting2D.All)
            {
                if (existing.Contains(name)) continue;
                layers.InsertArrayElementAtIndex(layers.arraySize);
                SerializedProperty e = layers.GetArrayElementAtIndex(layers.arraySize - 1);
                e.FindPropertyRelative("name").stringValue = name;
                added++;
            }
            // Positive, stable, unique ids (Unity's own ids are positive; negative ones are not picked up).
            for (int i = 0; i < layers.arraySize; i++)
            {
                SerializedProperty e = layers.GetArrayElementAtIndex(i);
                string n = e.FindPropertyRelative("name").stringValue;
                int index = System.Array.IndexOf(Sorting2D.All, n);
                if (index >= 0) e.FindPropertyRelative("uniqueID").intValue = 0x47500000 + index * 7919 + 1;
            }
            // Keep our layers in the declared order after Default.
            var order = new List<string> { "Default" };
            order.AddRange(Sorting2D.All);
            for (int target = 0; target < order.Count; target++)
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == order[target] && i != target && target < layers.arraySize)
                { layers.MoveArrayElement(i, target); break; }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            // Ask the editor to reload its in-memory sorting layer list (what the Tags & Layers panel does).
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            typeof(UnityEditorInternal.InternalEditorUtility).GetMethod("UpdateSortingLayersOrder", flags)?.Invoke(null, null);
            bool live = System.Array.TrueForAll(Sorting2D.All, n => System.Array.Exists(SortingLayer.layers, l => l.name == n));
            return (live ? "" : "WARNING: restart Unity so the new sorting layers load. ") + $"Sorting layers: {added} added ({string.Join(", ", Sorting2D.All)}).";
        }

        private static string SetupAtlas()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(AtlasPath) != null) return "Sprite atlas: already exists.";
            EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
            var folder = AssetDatabase.LoadAssetAtPath<Object>(Sprites2DImporter.Folder.TrimEnd('/'));
            if (folder == null) return "Sprite atlas: sprite folder missing, skipped.";
            var atlas = new SpriteAtlasAsset();
            atlas.Add(new[] { folder });
            SpriteAtlasAsset.Save(atlas, AtlasPath);
            AssetDatabase.ImportAsset(AtlasPath);
            if (AssetImporter.GetAtPath(AtlasPath) is SpriteAtlasImporter importer)
            {
                importer.packingSettings = new SpriteAtlasPackingSettings { enableRotation = false, enableTightPacking = false, padding = 4 };
                importer.textureSettings = new SpriteAtlasTextureSettings { filterMode = FilterMode.Bilinear, generateMipMaps = false, sRGB = true };
                importer.SaveAndReimport();
            }
            return $"Sprite atlas: created {AtlasPath}.";
        }

        private static string Setup2DRenderer()
        {
            var data = AssetDatabase.LoadAssetAtPath<Renderer2DData>(Renderer2DPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(data, Renderer2DPath);
                AssetDatabase.SaveAssets();
            }

            var results = new List<string>();
            foreach (string path in PipelineAssets)
            {
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (pipeline == null) { results.Add($"{path}: not found"); continue; }
                var so = new SerializedObject(pipeline);
                SerializedProperty list = so.FindProperty("m_RendererDataList");
                int index = -1;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) index = i;
                if (index < 0)
                {
                    list.InsertArrayElementAtIndex(list.arraySize);
                    index = list.arraySize - 1;
                    list.GetArrayElementAtIndex(index).objectReferenceValue = data;
                }
                so.FindProperty("m_DefaultRendererIndex").intValue = index;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline);
                results.Add($"{System.IO.Path.GetFileNameWithoutExtension(path)} -> 2D renderer (index {index})");
            }
            return "2D Renderer: " + string.Join("; ", results);
        }
    }
}
