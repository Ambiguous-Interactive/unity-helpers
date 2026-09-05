// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.AssetProcessors;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpriteCropperAdditionalTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/SpriteCropperAdditionalTests";

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
        public void AppliesPaddingAndAdjustsPivotCorrectly()
        {
            string src = (Root + "/pad_src.png").SanitizePath();

            CreatePngWithOpaqueRect(src, 20, 20, 5, 5, 10, 10, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter imp = AssetImporter.GetAtPath(src) as TextureImporter;
            Assert.IsTrue(imp != null, $"TextureImporter should exist for source file at '{src}'");
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.isReadable = true;
            imp.spritePivot = new Vector2(0.5f, 0.5f);
            imp.SaveAndReimport();

            SpriteCropper window = Track(ScriptableObject.CreateInstance<SpriteCropper>());
            window._overwriteOriginals = false;
            window._inputDirectories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };
            window._leftPadding = 2;
            window._bottomPadding = 3;
            window._rightPadding = 1;
            window._topPadding = 0;

            window.FindFilesToProcess();
            Assert.That(
                window._filesToProcess.Count,
                Is.GreaterThan(0),
                "Should have found files to process"
            );

            window.ProcessFoundSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string dst = (Root + "/Cropped_pad_src.png").SanitizePath();
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
            Assert.IsTrue(
                tex != null,
                $"Cropped texture should exist at '{dst}'. Check that ProcessFoundSprites completed successfully."
            );

            Assert.That(
                tex.width,
                Is.EqualTo(13),
                $"Expected width 13 (10 content + 2 left + 1 right padding), got {tex.width}"
            );
            Assert.That(
                tex.height,
                Is.EqualTo(13),
                $"Expected height 13 (10 content + 3 bottom + 0 top padding), got {tex.height}"
            );

            TextureImporter newImp = AssetImporter.GetAtPath(dst) as TextureImporter;
            Assert.IsTrue(
                newImp != null,
                $"TextureImporter should exist for cropped file at '{dst}'"
            );
            Vector2 pivot = newImp.spritePivot;

            float expectedPivotX = 7f / 13f;
            float expectedPivotY = 8f / 13f;
            Assert.That(
                pivot.x,
                Is.InRange(expectedPivotX - 1e-3f, expectedPivotX + 1e-3f),
                $"Expected pivot.x ~{expectedPivotX:F4}, got {pivot.x:F4}"
            );
            Assert.That(
                pivot.y,
                Is.InRange(expectedPivotY - 1e-3f, expectedPivotY + 1e-3f),
                $"Expected pivot.y ~{expectedPivotY:F4}, got {pivot.y:F4}"
            );
        }

        [Test]
        public void SkipsWhenOnlyNecessaryAndNoTrimNeeded()
        {
            string src = (Root + "/full_opaque.png").SanitizePath();

            CreatePngFilled(src, 8, 8, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter imp = AssetImporter.GetAtPath(src) as TextureImporter;
            Assert.IsTrue(imp != null, $"TextureImporter should exist for source file at '{src}'");
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.isReadable = true;
            imp.SaveAndReimport();

            SpriteCropper window = Track(ScriptableObject.CreateInstance<SpriteCropper>());
            window._overwriteOriginals = false;
            window._onlyNecessary = true;
            window._inputDirectories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };
            window.FindFilesToProcess();
            window.ProcessFoundSprites();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string dst = (Root + "/Cropped_full_opaque.png").SanitizePath();
            string fullPath = RelToFull(dst);
            Assert.That(
                File.Exists(fullPath),
                Is.False,
                $"Should not write cropped file when unnecessary. File found at: {fullPath}"
            );
        }

        [Test]
        public void RestoresOriginalReadabilityWhenWritingToOutput()
        {
            string src = (Root + "/readable_toggle.png").SanitizePath();
            CreatePngWithOpaqueRect(src, 10, 10, 2, 2, 6, 6, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter imp = AssetImporter.GetAtPath(src) as TextureImporter;
            Assert.IsTrue(imp != null, $"TextureImporter should exist for source file at '{src}'");
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.isReadable = false;
            imp.SaveAndReimport();

            SpriteCropper window = Track(ScriptableObject.CreateInstance<SpriteCropper>());
            window._overwriteOriginals = false;
            window._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(Root);
            window._inputDirectories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            window.FindFilesToProcess();
            window.ProcessFoundSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            imp = AssetImporter.GetAtPath(src) as TextureImporter;
            Assert.IsTrue(
                imp != null,
                $"TextureImporter should still exist for source file at '{src}' after processing"
            );
            Assert.That(
                imp.isReadable,
                Is.False,
                "Original texture should be restored to unreadable after processing"
            );

            string dst = (Root + "/Cropped_readable_toggle.png").SanitizePath();
            TextureImporter newImp = AssetImporter.GetAtPath(dst) as TextureImporter;
            Assert.IsTrue(
                newImp != null,
                $"TextureImporter should exist for cropped file at '{dst}'"
            );
            Assert.That(
                newImp.isReadable,
                Is.False,
                "MirrorSource should copy original readability (unreadable)"
            );

            window = Track(ScriptableObject.CreateInstance<SpriteCropper>());
            window._overwriteOriginals = false;
            window._outputDirectory = AssetDatabase.LoadAssetAtPath<Object>(Root);
            window._inputDirectories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };
            window._outputReadability = SpriteCropper.OutputReadability.Readable;

            window.FindFilesToProcess();
            window.ProcessFoundSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string dst2 = (Root + "/Cropped_readable_toggle.png").SanitizePath();
            newImp = AssetImporter.GetAtPath(dst2) as TextureImporter;
            Assert.IsTrue(
                newImp != null,
                $"TextureImporter should exist for cropped file at '{dst2}' after second processing"
            );
            Assert.That(
                newImp.isReadable,
                Is.True,
                "OutputReadability.Readable should force output to be readable"
            );
        }

        [Test]
        public void ProducesOneByOneForFullyTransparentImage()
        {
            string src = (Root + "/all_transparent.png").SanitizePath();
            CreateTransparentPng(src, 12, 9);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter imp = AssetImporter.GetAtPath(src) as TextureImporter;
            Assert.IsTrue(imp != null, $"TextureImporter should exist for source file at '{src}'");
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.isReadable = true;
            imp.SaveAndReimport();

            SpriteCropper window = Track(ScriptableObject.CreateInstance<SpriteCropper>());
            window._overwriteOriginals = false;
            window._inputDirectories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };
            window.FindFilesToProcess();
            window.ProcessFoundSprites();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            string dst = (Root + "/Cropped_all_transparent.png").SanitizePath();
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
            Assert.IsTrue(
                tex != null,
                $"Cropped texture should exist at '{dst}' for fully transparent image"
            );
            Assert.That(
                tex.width,
                Is.EqualTo(1),
                $"Fully transparent image should crop to 1x1, got width {tex.width}"
            );
            Assert.That(
                tex.height,
                Is.EqualTo(1),
                $"Fully transparent image should crop to 1x1, got height {tex.height}"
            );

            TextureImporter newImp = AssetImporter.GetAtPath(dst) as TextureImporter;
            Assert.IsTrue(
                newImp != null,
                $"TextureImporter should exist for cropped file at '{dst}'"
            );
            Vector2 pivot = newImp.spritePivot;
            Assert.That(
                pivot.x,
                Is.InRange(0.49f, 0.51f),
                $"Pivot.x should be ~0.5 for 1x1 texture, got {pivot.x}"
            );
            Assert.That(
                pivot.y,
                Is.InRange(0.49f, 0.51f),
                $"Pivot.y should be ~0.5 for 1x1 texture, got {pivot.y}"
            );
        }

        [Test]
        public void SkipsMultipleSpriteTextures()
        {
            string src = (Root + "/multi.png").SanitizePath();
            CreatePngWithOpaqueRect(src, 16, 16, 4, 4, 8, 8, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter imp = AssetImporter.GetAtPath(src) as TextureImporter;
            Assert.IsTrue(imp != null, $"TextureImporter should exist for source file at '{src}'");
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Multiple;
            imp.SaveAndReimport();

            SpriteCropper window = Track(ScriptableObject.CreateInstance<SpriteCropper>());
            window._overwriteOriginals = false;
            window._inputDirectories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };
            window.FindFilesToProcess();
            window.ProcessFoundSprites();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string dst = (Root + "/Cropped_multi.png").SanitizePath();
            Assert.That(
                File.Exists(RelToFull(dst)),
                Is.False,
                "Should not create cropped file for Multiple sprite mode textures"
            );
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(src);
            Assert.IsTrue(tex != null, $"Original texture should still exist at '{src}'");
            Assert.That(
                tex.width,
                Is.EqualTo(16),
                "Original texture dimensions should be unchanged"
            );
            Assert.That(
                tex.height,
                Is.EqualTo(16),
                "Original texture dimensions should be unchanged"
            );
        }

        private void CreatePngWithOpaqueRect(
            string relPath,
            int w,
            int h,
            int rectX,
            int rectY,
            int rectW,
            int rectH,
            Color color
        )
        {
            EnsureFolder(Path.GetDirectoryName(relPath).SanitizePath());
            Texture2D t = new(w, h, TextureFormat.RGBA32, false) { alphaIsTransparency = true };
            Color[] pix = new Color[w * h];
            for (int y = 0; y < h; ++y)
            for (int x = 0; x < w; ++x)
            {
                bool inRect = rectX <= x && x < rectX + rectW && rectY <= y && y < rectY + rectH;
                pix[y * w + x] = inRect ? color : new Color(0f, 0f, 0f, 0f);
            }
            t.SetPixels(pix);
            t.Apply();
            File.WriteAllBytes(RelToFull(relPath), t.EncodeToPNG());
        }

        private void CreatePngFilled(string relPath, int w, int h, Color c)
        {
            EnsureFolder(Path.GetDirectoryName(relPath).SanitizePath());
            Texture2D t = new(w, h, TextureFormat.RGBA32, false) { alphaIsTransparency = true };
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = c;
            }

            t.SetPixels(pix);
            t.Apply();
            File.WriteAllBytes(RelToFull(relPath), t.EncodeToPNG());
        }

        private void CreateTransparentPng(string relPath, int w, int h)
        {
            EnsureFolder(Path.GetDirectoryName(relPath).SanitizePath());
            Texture2D t = new(w, h, TextureFormat.RGBA32, false) { alphaIsTransparency = true };
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = new Color(0f, 0f, 0f, 0f);
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

        /// <summary>
        /// Tests that the skip count is never negative after processing.
        /// This was a regression where the formula for calculating skipped count
        /// would yield negative values when files needed reprocessing.
        /// </summary>
        [Test]
        public void ProcessFoundSpritesSkipCountIsNeverNegativeWhenSingleSpriteSucceeds()
        {
            string src = (Root + "/single_test.png").SanitizePath();
            CreatePngWithOpaqueRect(src, 20, 20, 5, 5, 10, 10, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter imp = AssetImporter.GetAtPath(src) as TextureImporter;
            Assert.IsTrue(
                imp != null,
                $"TextureImporter should be available for test sprite at '{src}'"
            );
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.isReadable = true;
            imp.SaveAndReimport();

            SpriteCropper window = Track(ScriptableObject.CreateInstance<SpriteCropper>());
            window._overwriteOriginals = false;
            window._inputDirectories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            window.FindFilesToProcess();
            Assert.That(
                window._filesToProcess.Count,
                Is.GreaterThan(0),
                "Should have found at least one file to process"
            );

            window.ProcessFoundSprites();

            LogAssert.Expect(
                LogType.Log,
                new System.Text.RegularExpressions.Regex(
                    @"\d+ sprites processed successfully\. Skipped: \d+"
                )
            );

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            string dst = (Root + "/Cropped_single_test.png").SanitizePath();
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
            Assert.IsTrue(tex != null, $"Cropped texture should exist at '{dst}'");
        }

        /// <summary>
        /// Tests that multiple sprites are counted correctly in the success message.
        /// </summary>
        [Test]
        public void ProcessFoundSpritesCountsMultipleSpritesCorrectly()
        {
            string src1 = (Root + "/multi_test1.png").SanitizePath();
            string src2 = (Root + "/multi_test2.png").SanitizePath();
            string src3 = (Root + "/multi_test3.png").SanitizePath();

            CreatePngWithOpaqueRect(src1, 20, 20, 5, 5, 10, 10, Color.white);
            CreatePngWithOpaqueRect(src2, 30, 30, 5, 5, 15, 15, Color.red);
            CreatePngWithOpaqueRect(src3, 25, 25, 3, 3, 12, 12, Color.blue);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            foreach (string src in new[] { src1, src2, src3 })
            {
                TextureImporter imp = AssetImporter.GetAtPath(src) as TextureImporter;
                Assert.IsTrue(imp != null, $"TextureImporter should be available for '{src}'");
                imp.textureType = TextureImporterType.Sprite;
                imp.spriteImportMode = SpriteImportMode.Single;
                imp.isReadable = true;
                imp.SaveAndReimport();
            }

            SpriteCropper window = Track(ScriptableObject.CreateInstance<SpriteCropper>());
            window._overwriteOriginals = false;
            window._inputDirectories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            window.FindFilesToProcess();
            int fileCount = window._filesToProcess.Count;
            Assert.That(
                fileCount,
                Is.GreaterThanOrEqualTo(3),
                $"Should have found at least 3 files to process, found {fileCount}"
            );

            window.ProcessFoundSprites();

            LogAssert.Expect(
                LogType.Log,
                new System.Text.RegularExpressions.Regex(
                    @"\d+ sprites processed successfully\. Skipped: \d+"
                )
            );

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            foreach (string src in new[] { src1, src2, src3 })
            {
                string baseName = Path.GetFileName(src);
                string dst = (Root + "/Cropped_" + baseName).SanitizePath();
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(dst);
                Assert.IsTrue(tex != null, $"Cropped texture should exist at '{dst}'");
            }
        }

        /// <summary>
        /// Tests that sprites that don't need cropping are counted as skipped correctly.
        /// </summary>
        [Test]
        public void ProcessFoundSpritesSkipsSpritesWithNoTransparentPixels()
        {
            string src = (Root + "/fully_opaque.png").SanitizePath();
            CreatePngFilled(src, 20, 20, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter imp = AssetImporter.GetAtPath(src) as TextureImporter;
            Assert.IsTrue(
                imp != null,
                $"TextureImporter should be available for test sprite at '{src}'"
            );
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.isReadable = true;
            imp.SaveAndReimport();

            SpriteCropper window = Track(ScriptableObject.CreateInstance<SpriteCropper>());
            window._overwriteOriginals = false;
            window._inputDirectories = new System.Collections.Generic.List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(Root),
            };

            window.FindFilesToProcess();
            Assert.That(
                window._filesToProcess.Count,
                Is.GreaterThan(0),
                "Should have found at least one file to process"
            );

            window.ProcessFoundSprites();

            LogAssert.Expect(
                LogType.Log,
                new System.Text.RegularExpressions.Regex(
                    @"\d+ sprites processed successfully\. Skipped: \d+"
                )
            );
        }
    }
#endif
}
