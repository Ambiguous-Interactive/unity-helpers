// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpriteSettingsApplierTests : BatchedEditorTestBase
    {
        private const string TestFolder = "Assets/TempSpriteApplierTests";
        private string _assetPath;

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            if (Application.isPlaying)
            {
                return;
            }
            EnsureFolder(TestFolder);
            TrackFolder(TestFolder);
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            // Reset per-test state
            _assetPath = null;
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            // Per-test cleanup: track individual asset paths for deferred cleanup
            if (!string.IsNullOrEmpty(_assetPath))
            {
                TrackAssetPath(_assetPath);
            }
        }

        private string CreateTempTexture(bool asSprite = false)
        {
            Texture2D tex = Track(new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false));
            byte[] png = tex.EncodeToPNG();
            string path = Path.Combine(TestFolder, "ui_button.png");
            File.WriteAllBytes(path, png);

            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.ImportAsset(path);

                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(ti != null, "TextureImporter not found for path: " + path);
                if (asSprite)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.SaveAndReimport();
                }
            });
            return path;
        }

        [Test]
        public void AppliesProfileByNameContainsWithPriority()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }
            string path = CreateTempTexture(asSprite: true);
            _assetPath = path;

            // Unity's default is Bilinear, so a different starting value is what makes the change detectable.
            ExecuteWithImmediateImport(() =>
            {
                TextureImporter initialImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(initialImporter != null, "Initial importer not found");
                initialImporter.filterMode = FilterMode.Point;
                initialImporter.SaveAndReimport();
            });

            // lower priority sets FilterMode.Point; higher sets Bilinear
            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "ui_",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Point,
                },
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "button",
                    priority = 5,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(path, prepared);
            Assert.IsTrue(willChange, $"Expected change detection for path: {path}");

            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter importer
            );
            Assert.IsTrue(
                changed,
                $"Expected TryUpdateTextureSettings to apply settings. Path={path}"
            );
            Assert.IsTrue(importer != null, $"Importer was null for path: {path}");

            ExecuteWithImmediateImport(() =>
            {
                importer.SaveAndReimport();
            });

            // Verify final filter mode is from higher priority profile
            Assert.AreEqual(
                FilterMode.Bilinear,
                importer.filterMode,
                $"Expected higher-priority Bilinear filter; actual={importer.filterMode}"
            );
        }

        [Test]
        public void EnforcesTextureTypeWhenConfigured()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }
            string path = CreateTempTexture(asSprite: false);
            _assetPath = path;

            // Starting at Default is what makes the profile's Sprite type a detectable change.
            ExecuteWithImmediateImport(() =>
            {
                TextureImporter initialImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(initialImporter != null, "Initial importer not found");
                initialImporter.textureType = TextureImporterType.Default;
                initialImporter.SaveAndReimport();
            });

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Extension,
                    matchPattern = ".png",
                    priority = 10,
                    applyTextureType = true,
                    textureType = TextureImporterType.Sprite,
                },
            };
            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter importer
            );
            Assert.IsTrue(
                changed,
                $"Expected TryUpdateTextureSettings to update importer for path: {path}"
            );
            Assert.IsTrue(importer != null, $"Importer was null for path: {path}");

            ExecuteWithImmediateImport(() =>
            {
                importer.SaveAndReimport();
            });

            Assert.AreEqual(
                TextureImporterType.Sprite,
                importer.textureType,
                "Texture type should be Sprite after applying profile. "
                    + "Actual type: "
                    + importer.textureType
                    + ", Path: "
                    + path
            );
        }
    }
}
