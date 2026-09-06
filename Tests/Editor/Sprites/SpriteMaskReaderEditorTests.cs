// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Integration")]
    public sealed class SpriteMaskReaderEditorTests : CommonTestBase
    {
        private string _folder;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _folder = "Assets/Temp/SpriteMaskReaderTests" + Guid.NewGuid().ToString("N");
            EnsureFolder(_folder);
        }

        [TearDown]
        public override void TearDown()
        {
            CleanupTrackedFoldersAndAssets();
            base.TearDown();
        }

        [TestCase(false, 64)]
        [TestCase(true, 64)]
        [TestCase(false, 32)]
        public void ReadsOriginalPngWithoutChangingImporter(bool readable, int maximumSize)
        {
            string path = CreatePng();
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = readable;
            importer.maxTextureSize = maximumSize;
            importer.spritePixelsPerUnit = 16;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsTrue(sprite != null);
            string before = EditorJsonUtility.ToJson(importer);
            Assert.IsTrue(
                SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out string error),
                error
            );
            Assert.AreEqual(64, mask.Width);
            Assert.AreEqual(64, mask.Height);
            Assert.IsTrue(mask.Pixels[17 * 64 + 19]);
            Assert.IsFalse(mask.Pixels[17 * 64 + 18]);
            Assert.AreEqual(
                sprite.rect.width / sprite.pixelsPerUnit,
                mask.Width * mask.UnitsPerPixel.x,
                0.0001f
            );
            Assert.AreEqual(
                sprite.rect.height / sprite.pixelsPerUnit,
                mask.Height * mask.UnitsPerPixel.y,
                0.0001f
            );
            Assert.AreEqual(
                sprite.pivot.x / sprite.pixelsPerUnit,
                mask.Pivot.x * mask.UnitsPerPixel.x,
                0.0001f
            );
            Assert.AreEqual(before, EditorJsonUtility.ToJson(importer));
            Assert.AreEqual(readable, importer.isReadable);
        }

        [Test]
        public void ReadsOnlySelectedSubspriteWithItsOwnPivot()
        {
            string path = CreatePng();
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
            importer.spritePixelsPerUnit = 16;
#pragma warning disable CS0618
            importer.spritesheet = new[]
            {
                new SpriteMetaData
                {
                    name = "First",
                    rect = new Rect(16, 16, 16, 16),
                    alignment = 9,
                    pivot = new Vector2(0.25f, 0.75f),
                },
                new SpriteMetaData
                {
                    name = "Second",
                    rect = new Rect(32, 32, 16, 16),
                    alignment = 9,
                    pivot = Vector2.zero,
                },
            };
#pragma warning restore CS0618
            importer.SaveAndReimport();
            int count = 0;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is not Sprite sprite)
                {
                    continue;
                }
                ++count;
                Assert.IsTrue(
                    SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out string error),
                    error
                );
                Assert.AreEqual(16, mask.Width);
                Assert.AreEqual(16, mask.Height);
                Assert.AreEqual(sprite.pivot, mask.Pivot);
                int opaque = 0;
                foreach (bool pixel in mask.Pixels)
                {
                    if (pixel)
                    {
                        ++opaque;
                    }
                }
                Assert.AreEqual(sprite.name == "First" ? 1 : 0, opaque);
            }
            Assert.AreEqual(2, count);
            Assert.IsFalse(importer.isReadable);
        }

        private string CreatePng()
        {
            Texture2D texture = Track(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[64 * 64];
            pixels[17 * 64 + 19] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            string path = _folder + "/Source.png";
            TrackAssetPath(path);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }
    }
#endif
}
