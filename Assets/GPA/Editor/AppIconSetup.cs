using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// Makes Assets/GPA/Art/AppIcon/AppIcon.png the game's icon on every platform (PC, iPhone, Android).
    /// Runs when Unity opens (and after scripts recompile); does nothing once the icon is already set.
    /// Menu: Tools/GPA/Apply App Icon.
    /// </summary>
    [InitializeOnLoad]
    internal static class AppIconSetup
    {
        private const string IconPath = "Assets/GPA/Art/AppIcon/AppIcon.png";

        static AppIconSetup() => EditorApplication.delayCall += () => Apply(false);

        [MenuItem("Tools/GPA/Apply App Icon")]
        private static void ApplyMenu() => Apply(true);

        private static void Apply(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer == null) return;

            // Icons should stay crisp: no compression, no mipmaps, full 1024 px.
            if (importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled || importer.maxTextureSize < 1024)
            {
                importer.textureType = TextureImporterType.Default;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 1024;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
            }

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null) return;
            var current = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            if (!force && current != null && current.Length > 0 && current[0] == icon) return;

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            AssetDatabase.SaveAssets();
            Debug.Log("[AppIcon] Game icon set to " + IconPath);
        }
    }
}
