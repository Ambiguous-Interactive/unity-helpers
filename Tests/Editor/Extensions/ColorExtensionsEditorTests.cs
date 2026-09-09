// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
#if UNITY_EDITOR
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ColorExtensionsEditorTests
    {
        private const string TempFolder = "Assets/TempColorExtensionTests";
        private const string TempTexturePath = TempFolder + "/ReadableTest.png";

        private static void CleanupTempAssets()
        {
            bool refreshed = false;
            if (AssetDatabase.LoadAssetAtPath<Object>(TempTexturePath) != null)
            {
                if (AssetDatabase.DeleteAsset(TempTexturePath))
                {
                    refreshed = true;
                }
            }

            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                if (AssetDatabase.DeleteAsset(TempFolder))
                {
                    refreshed = true;
                }
            }

            if (refreshed)
            {
                AssetDatabaseBatchHelper.RefreshIfNotBatching();
            }
        }

        [TearDown]
        public void TearDown()
        {
            CleanupTempAssets();
        }

        [Test]
        public void GetAverageColorSpriteForcesTextureReadableViaMakeReadable()
        {
            Sprite sprite = CreateNonReadableSprite(new Color(0.1f, 0.8f, 0.2f, 1f));
            Assert.IsTrue(sprite != null, "Sprite asset failed to load.");
            Assert.IsFalse(
                sprite.texture.isReadable,
                "Test setup requires a non-readable texture."
            );

            Color result = new[] { sprite }.GetAverageColor(ColorAveragingMethod.Weighted, 0.01f);

            Assert.Greater(result.g, 0.7f);
            Assert.Less(result.r, result.g);
            Assert.Less(result.b, result.g);
        }

        private Sprite CreateNonReadableSprite(Color color)
        {
            Directory.CreateDirectory(TempFolder);
            Texture2D texture = new(2, 1, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { color, color });
            texture.Apply();

            byte[] png = texture.EncodeToPNG();
            Object.DestroyImmediate(texture); // UNH-SUPPRESS: Intentional cleanup of temp texture
            File.WriteAllBytes(TempTexturePath, png);
            AssetDatabase.ImportAsset(TempTexturePath, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = AssetImporter.GetAtPath(TempTexturePath) as TextureImporter;
            Assert.IsTrue(importer != null, "TextureImporter not found for temp texture.");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = false;
            importer.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TempTexturePath);
            return sprite;
        }
    }
#endif
}
