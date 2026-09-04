// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.AssetProcessors;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class TextureSettingsApplierAPITests : CommonTestBase
    {
        private const string Root = "Assets/Temp/TextureSettingsApplierAPITests";

        [SetUp]
        public override void BaseSetUp()
        {
            // Must precede base.BaseSetUp(); AssertCleanAndClearAll documents why it runs first.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            // Loop protection would otherwise trip as cleanup deletes several assets.
            DetectAssetChangeProcessor.ResetForTesting();
            CleanupTrackedFoldersAndAssets();
        }

        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            DeferAssetCleanupToOneTimeTearDown = true;
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            CleanupDeferredAssetsAndFolders();
            base.OneTimeTearDown();
        }

        [Test]
        public void AppliesDefaultPlatformSettingsViaAPI()
        {
            string texPath = (Root + "/api_tex.png").SanitizePath();
            CreatePng(texPath, 16, 16, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureSettingsApplierAPI.Config config = new()
            {
                applyPlatformMaxTextureSize = true,
                platformMaxTextureSize = 64,
                applyWrapMode = true,
                wrapMode = TextureWrapMode.Clamp,
                applyFilterMode = true,
                filterMode = FilterMode.Bilinear,
            };

            bool will = TextureSettingsApplierAPI.WillTextureSettingsChange(texPath, in config);
            Assert.IsTrue(will, "Expected WillTextureSettingsChange to detect differences");

            bool changed = TextureSettingsApplierAPI.TryUpdateTextureSettings(
                texPath,
                in config,
                out TextureImporter importer
            );
            Assert.IsTrue(changed, "Expected API to apply changes");
            Assert.IsTrue(importer != null);
            importer.SaveAndReimport();

            TextureImporterPlatformSettings ps = importer.GetDefaultPlatformTextureSettings();
            Assert.AreEqual(64, ps.maxTextureSize);
            Assert.AreEqual(TextureWrapMode.Clamp, importer.wrapMode);
            Assert.AreEqual(FilterMode.Bilinear, importer.filterMode);
        }

        [Test]
        public void AppliesNamedPlatformOverrideStandalone()
        {
            string texPath = (Root + "/api_tex_platform.png").SanitizePath();
            CreatePng(texPath, 32, 32, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureSettingsApplierAPI.PlatformOverride platform = new()
            {
                name = "Standalone",
                applyMaxTextureSize = true,
                maxTextureSize = 128,
            };
            TextureSettingsApplierAPI.Config config = new()
            {
                platformOverrides = new[] { platform },
            };

            bool changed = TextureSettingsApplierAPI.TryUpdateTextureSettings(
                texPath,
                in config,
                out TextureImporter importer
            );
            Assert.IsTrue(changed, "Expected override to apply");
            Assert.IsTrue(importer != null);
            importer.SaveAndReimport();

            TextureImporterPlatformSettings ops = importer.GetPlatformTextureSettings("Standalone");
            Assert.AreEqual(128, ops.maxTextureSize);
            Assert.IsTrue(ops.overridden);
        }

        [Test]
        public void UnknownPlatformOverrideDoesNotCrashAndIsApplied()
        {
            string texPath = (Root + "/api_tex_unknown.png").SanitizePath();
            CreatePng(texPath, 32, 32, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureSettingsApplierAPI.PlatformOverride platform = new()
            {
                name = "BogusPlatform",
                applyMaxTextureSize = true,
                maxTextureSize = 96,
            };
            TextureSettingsApplierAPI.Config config = new()
            {
                platformOverrides = new[] { platform },
            };

            bool changed = TextureSettingsApplierAPI.TryUpdateTextureSettings(
                texPath,
                in config,
                out TextureImporter importer
            );
            Assert.IsTrue(changed, "Expected override to apply even for unknown platform name");
            Assert.IsTrue(importer != null);
            importer.SaveAndReimport();

            TextureImporterPlatformSettings ops = importer.GetPlatformTextureSettings(
                "BogusPlatform"
            );
            Assert.AreEqual(96, ops.maxTextureSize);
            Assert.IsTrue(ops.overridden);
        }

        [Test]
        public void ApplyDefaultPlatformSettingsCanBeUndone()
        {
            string texPath = (Root + "/api_tex_undo.png").SanitizePath();
            CreatePng(texPath, 32, 32, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter importerBefore = AssetImporter.GetAtPath(texPath) as TextureImporter;
            Assert.IsTrue(importerBefore != null, "Importer should exist before update.");

            TextureImporterPlatformSettings before =
                importerBefore.GetDefaultPlatformTextureSettings();
            int originalMax = before.maxTextureSize;
            int targetMax = originalMax == 64 ? 128 : 64;

            TextureSettingsApplierAPI.Config config = new()
            {
                applyPlatformMaxTextureSize = true,
                platformMaxTextureSize = targetMax,
            };

            bool changed = TextureSettingsApplierAPI.TryUpdateTextureSettings(
                texPath,
                in config,
                out TextureImporter importerAfterApply
            );
            Assert.IsTrue(changed, "Expected settings update to apply.");
            Assert.IsTrue(importerAfterApply != null);

            importerAfterApply.SaveAndReimport();
            TextureImporterPlatformSettings applied =
                importerAfterApply.GetDefaultPlatformTextureSettings();
            Assert.AreEqual(targetMax, applied.maxTextureSize, "Expected applied max size.");

            Undo.PerformUndo();

            TextureImporter importerAfterUndo = AssetImporter.GetAtPath(texPath) as TextureImporter;
            Assert.IsTrue(importerAfterUndo != null, "Importer should exist after undo.");
            importerAfterUndo.SaveAndReimport();

            TextureImporterPlatformSettings undone =
                importerAfterUndo.GetDefaultPlatformTextureSettings();
            Assert.AreEqual(
                originalMax,
                undone.maxTextureSize,
                "Expected undo to restore original max size."
            );
        }

        private void CreatePng(string relPath, int w, int h, Color c)
        {
            EnsureFolder(Path.GetDirectoryName(relPath).SanitizePath());
            Texture2D t = new(w, h, TextureFormat.RGBA32, false);
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = c;
            }

            t.SetPixels(pix);
            t.Apply();
            File.WriteAllBytes(RelToFull(relPath), t.EncodeToPNG());
        }

        private static string RelToFull(string rel)
        {
            return Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    rel
                )
                .SanitizePath();
        }
    }
#endif
}
