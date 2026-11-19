using UnityEditor;
using UnityEngine;

namespace GPA.EditorTools
{
    /// <summary>
    /// Import rules for the generated 2D sprites: 64 pixels per metre (sprites are rendered at 2x,
    /// so a 4 m ground tile is 256 px), centred pivot, no mipmaps, no compression (keeps the grit
    /// crisp), and full-rect meshes on tiles so neighbours meet without gaps.
    /// </summary>
    public sealed class Sprites2DImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/GPA/Art/Sprites2D/";
        public const float PixelsPerUnit = 64f;

        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(Folder)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spriteMeshType = System.IO.Path.GetFileName(assetPath).StartsWith("tile_") ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
            importer.SetTextureSettings(settings);
        }
    }
}
