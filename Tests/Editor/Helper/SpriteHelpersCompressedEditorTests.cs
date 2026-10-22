// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Slow")]
    [Category("Integration")]
    public sealed class SpriteHelpersCompressedEditorTests : BatchedEditorTestBase
    {
        private string TexturePath => _testFolder + "/crunched.png";

        private readonly string _testFolder =
            "Assets/TempSpriteHelpersCompressedTests-" + Guid.NewGuid().ToString("N");

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            Assert.That(AssetDatabaseBatchHelper.EnsureAssetFolder(_testFolder), Is.True);
            TrackFolder(_testFolder);
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            try
            {
                CleanupTrackedFoldersAndAssets();
            }
            finally
            {
                base.OneTimeTearDown();
            }
            Assert.That(AssetDatabase.IsValidFolder(_testFolder), Is.False, _testFolder);
            Assert.That(Directory.Exists(_testFolder), Is.False, _testFolder);
            Assert.That(File.Exists(_testFolder + ".meta"), Is.False, _testFolder);
        }

        [Test]
        public void ExtractSpriteRectReadsSmallRegionFromCrunchCompressedTexture()
        {
            Texture2D source = Track(new Texture2D(8, 8, TextureFormat.RGBA32, false));
            Color32[] colors = new Color32[64];
            for (int index = 0; index < colors.Length; ++index)
            {
                colors[index] = new Color32(
                    (byte)((index % 8) * 32),
                    (byte)((index / 8) * 32),
                    0,
                    255
                );
            }
            source.SetPixels32(colors);
            source.Apply();
            File.WriteAllBytes(TexturePath, source.EncodeToPNG());
            TrackAssetPath(TexturePath);

            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.ImportAsset(TexturePath);
                TextureImporter importer = AssetImporter.GetAtPath(TexturePath) as TextureImporter;
                Assert.IsTrue(importer != null);
                importer.textureType = TextureImporterType.Sprite;
                importer.isReadable = true;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.crunchedCompression = true;
                importer.SaveAndReimport();
            });

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Assert.IsTrue(texture != null);
            Assert.IsTrue(
                texture.format == TextureFormat.DXT1Crunched
                    || texture.format == TextureFormat.DXT5Crunched
            );
            Sprite sprite = Track(
                Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f))
            );
            Texture2D extracted = Track(sprite.ExtractSpriteRect());

            Assert.IsTrue(extracted != null);
            Assert.AreEqual(TextureFormat.RGBA32, extracted.format);
            Color32[] decoded = texture.GetPixels32();
            Color32[] region = extracted.GetPixels32();
            for (int y = 0; y < 4; ++y)
            {
                for (int x = 0; x < 4; ++x)
                {
                    Assert.AreEqual(decoded[y * 8 + x], region[y * 4 + x]);
                }
            }
        }
    }
}
