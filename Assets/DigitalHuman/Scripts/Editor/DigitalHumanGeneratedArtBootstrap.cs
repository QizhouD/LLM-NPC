#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace DigitalHuman.Editor
{
    public static class DigitalHumanGeneratedArtBootstrap
    {
        private static readonly string[] AssetPaths =
        {
            "Assets/DigitalHuman/Textures/Generated/EVA_Character_KeyArt.png",
            "Assets/DigitalHuman/Textures/Generated/EVA_Dialogue_Panel.png",
            "Assets/DigitalHuman/Textures/Generated/EVA_Interact_Icon.png"
        };

        public static void ImportAndValidate()
        {
            try
            {
                for (int i = 0; i < AssetPaths.Length; i++)
                {
                    string path = AssetPaths[i];
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    Require(importer != null, $"TextureImporter missing for {path}.");

                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.mipmapEnabled = false;
                    importer.sRGBTexture = true;
                    importer.alphaIsTransparency = path.EndsWith(
                        "EVA_Interact_Icon.png",
                        StringComparison.Ordinal);
                    importer.maxTextureSize = 2048;
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.SaveAndReimport();

                    Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    Require(sprite != null, $"Sprite import failed for {path}.");
                    Require(sprite.texture.width > 0 && sprite.texture.height > 0,
                        $"Imported sprite has invalid dimensions: {path}.");
                }

                AssetDatabase.SaveAssets();
                Debug.Log("[DigitalHuman] Generated EVA visual assets imported and validated.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
                throw;
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
#endif
