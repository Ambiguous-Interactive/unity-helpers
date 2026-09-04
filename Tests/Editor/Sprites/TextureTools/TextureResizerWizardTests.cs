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
    public sealed class TextureResizerWizardTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/TextureResizerWizardTests";
        private const string OutRoot = "Assets/Temp/TextureResizerWizardTests/Out";

        [SetUp]
        public override void BaseSetUp()
        {
            // Must precede base.BaseSetUp(); AssertCleanAndClearAll documents why it runs first.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            EnsureFolder(Root);
            EnsureFolder(OutRoot);
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
        public void ResizesTextureAccordingToMultipliers()
        {
            string path = Path.Combine(Root, "tex.png").SanitizePath();
            CreatePng(path, 16, 10, Color.green);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureResizerWizard wizard = Track(
                ScriptableObject.CreateInstance<TextureResizerWizard>()
            );
            wizard.textures = new System.Collections.Generic.List<Texture2D>
            {
                AssetDatabase.LoadAssetAtPath<Texture2D>(path),
            };
            wizard.numResizes = 1;
            wizard.pixelsPerUnit = 1;
            wizard.widthMultiplier = 1f;
            wizard.heightMultiplier = 1f;
            wizard.scalingResizeAlgorithm = TextureResizerWizard.ResizeAlgorithm.Point;
            wizard.OnWizardCreate();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsTrue(tex != null, "Texture should exist after resize");
            Assert.That(tex.width, Is.EqualTo(32), "Width should double");
            Assert.That(tex.height, Is.EqualTo(20), "Height should double");
        }

        [Test]
        public void DoesNothingWhenNumResizesIsZero()
        {
            string path = Path.Combine(Root, "nochange.png").SanitizePath();
            CreatePng(path, 12, 7, Color.blue);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            int w0 = AssetDatabase.LoadAssetAtPath<Texture2D>(path).width;
            int h0 = AssetDatabase.LoadAssetAtPath<Texture2D>(path).height;

            TextureResizerWizard wizard = Track(
                ScriptableObject.CreateInstance<TextureResizerWizard>()
            );
            wizard.numResizes = 0;
            wizard.OnWizardCreate();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(tex.width, Is.EqualTo(w0), "Width should remain unchanged");
            Assert.That(tex.height, Is.EqualTo(h0), "Height should remain unchanged");
        }

        [Test]
        public void RespectsDryRunAndDoesNotModifyFile()
        {
            string path = Path.Combine(Root, "dry.png").SanitizePath();
            CreatePng(path, 10, 6, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureResizerWizard wizard = Track(
                ScriptableObject.CreateInstance<TextureResizerWizard>()
            );
            wizard.textures = new System.Collections.Generic.List<Texture2D>
            {
                AssetDatabase.LoadAssetAtPath<Texture2D>(path),
            };
            wizard.numResizes = 1;
            wizard.pixelsPerUnit = 1;
            wizard.widthMultiplier = 1f;
            wizard.heightMultiplier = 1f;
            wizard.dryRun = true;
            wizard.scalingResizeAlgorithm = TextureResizerWizard.ResizeAlgorithm.Point;
            wizard.OnWizardCreate();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(tex.width, Is.EqualTo(10));
            Assert.That(tex.height, Is.EqualTo(6));
        }

        [Test]
        public void WritesToOutputFolderLeavingOriginalUnchanged()
        {
            string path = Path.Combine(Root, "out.png").SanitizePath();
            CreatePng(path, 8, 4, Color.black);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureResizerWizard wizard = Track(
                ScriptableObject.CreateInstance<TextureResizerWizard>()
            );
            wizard.textures = new System.Collections.Generic.List<Texture2D>
            {
                AssetDatabase.LoadAssetAtPath<Texture2D>(path),
            };
            wizard.numResizes = 1;
            wizard.pixelsPerUnit = 1;
            wizard.widthMultiplier = 1f;
            wizard.heightMultiplier = 1f;
            wizard.scalingResizeAlgorithm = TextureResizerWizard.ResizeAlgorithm.Point;
            DefaultAsset outAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(OutRoot);
            Assert.IsTrue(outAsset != null, "Output folder asset missing");
            wizard.outputFolder = outAsset;
            wizard.OnWizardCreate();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            Texture2D orig = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(orig.width, Is.EqualTo(8));
            Assert.That(orig.height, Is.EqualTo(4));

            string outPath = Path.Combine(OutRoot, "out.png").SanitizePath();
            Texture2D outTex = AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
            Assert.IsTrue(outTex != null, "Expected resized texture in output folder");
            Assert.That(outTex.width, Is.EqualTo(16));
            Assert.That(outTex.height, Is.EqualTo(8));
        }

        [Test]
        public void MultiplePassesAccumulateSize()
        {
            string path = Path.Combine(Root, "multi.png").SanitizePath();
            CreatePng(path, 16, 10, Color.gray);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureResizerWizard wizard = Track(
                ScriptableObject.CreateInstance<TextureResizerWizard>()
            );
            wizard.textures = new System.Collections.Generic.List<Texture2D>
            {
                AssetDatabase.LoadAssetAtPath<Texture2D>(path),
            };
            wizard.numResizes = 2;
            wizard.pixelsPerUnit = 1;
            wizard.widthMultiplier = 1f;
            wizard.heightMultiplier = 1f;
            wizard.scalingResizeAlgorithm = TextureResizerWizard.ResizeAlgorithm.Point;
            wizard.OnWizardCreate();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.That(tex.width, Is.EqualTo(64));
            Assert.That(tex.height, Is.EqualTo(40));
        }

        [Test]
        public void RestoresImporterReadabilityAfterRun()
        {
            string path = Path.Combine(Root, "restore.png").SanitizePath();
            CreatePng(path, 8, 8, Color.red);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.isReadable = false;
            importer.SaveAndReimport();

            TextureResizerWizard wizard = Track(
                ScriptableObject.CreateInstance<TextureResizerWizard>()
            );
            wizard.textures = new System.Collections.Generic.List<Texture2D>
            {
                AssetDatabase.LoadAssetAtPath<Texture2D>(path),
            };
            wizard.numResizes = 1;
            wizard.pixelsPerUnit = 1000;
            wizard.widthMultiplier = 1000f;
            wizard.heightMultiplier = 1000f;
            wizard.scalingResizeAlgorithm = TextureResizerWizard.ResizeAlgorithm.Point;
            wizard.OnWizardCreate();

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            Assert.IsFalse(importer.isReadable, "Importer readability should be restored");
        }

        private void CreatePng(string relPath, int w, int h, Color c)
        {
            string dir = Path.GetDirectoryName(relPath).SanitizePath();
            EnsureFolder(dir);
            Texture2D t = new(w, h, TextureFormat.RGBA32, false);
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = c;
            }

            t.SetPixels(pix);
            t.Apply();
            byte[] data = t.EncodeToPNG();
            File.WriteAllBytes(RelToFull(relPath), data);
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
