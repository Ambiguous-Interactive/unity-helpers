// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System.Collections;
    using System.IO;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
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

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class TextureResizerWizardTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/TextureResizerWizardTests";
        private const string OutRoot = "Assets/Temp/TextureResizerWizardTests/Out";
        private const string PackageTexturePath =
            "Packages/com.wallstop-studios.unity-helpers/docs/images/editor-tools/texture-resizer.png";

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
        public void DirectApiDryRunLeavesSourceAndImporterUnchanged()
        {
            string path = Path.Combine(Root, "direct-dry.png").SanitizePath();
            CreatePng(path, 10, 6, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(texture != null);
            Assert.IsTrue(importer != null);
            byte[] originalBytes = File.ReadAllBytes(RelToFull(path));
            bool originalReadable = importer.isReadable;
            List<Texture2D> selected = new() { texture };

            bool succeeded = TextureResizerAPI.TryResizeTextures(
                selected,
                null,
                1,
                TextureResizerWizard.ResizeAlgorithm.Point,
                1,
                1f,
                1f,
                null,
                true
            );

            Assert.IsTrue(succeeded);
            Assert.That(selected, Has.Count.EqualTo(1));
            Assert.That(selected[0], Is.SameAs(texture));
            Assert.That(texture.width, Is.EqualTo(10));
            Assert.That(texture.height, Is.EqualTo(6));
            Assert.That(importer.isReadable, Is.EqualTo(originalReadable));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(path)));

            succeeded = TextureResizerAPI.TryResizeTextures(
                new InflatedTextureCollection(texture),
                null,
                1,
                TextureResizerWizard.ResizeAlgorithm.Point,
                1,
                1f,
                1f,
                null,
                true
            );
            Assert.IsTrue(succeeded);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(path)));
        }

        [Test]
        public void DirectApiFindsFolderTexturesAndWritesToOutputFolder()
        {
            string sourcePath = Path.Combine(Root, "direct-folder", "found.png").SanitizePath();
            CreatePng(sourcePath, 8, 4, Color.red);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            byte[] originalBytes = File.ReadAllBytes(RelToFull(sourcePath));

            bool succeeded = TextureResizerAPI.TryResizeTextures(
                null,
                new[] { Path.GetDirectoryName(sourcePath).SanitizePath() },
                1,
                TextureResizerWizard.ResizeAlgorithm.Point,
                1,
                1f,
                1f,
                OutRoot,
                false
            );

            Assert.IsTrue(succeeded);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            Texture2D output = AssetDatabase.LoadAssetAtPath<Texture2D>(
                Path.Combine(OutRoot, "found.png").SanitizePath()
            );
            Assert.IsTrue(output != null);
            Assert.That(output.width, Is.EqualTo(16));
            Assert.That(output.height, Is.EqualTo(8));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(sourcePath)));
        }

        [Test]
        public void DirectApiRejectsInvalidOutputFolderBeforeWriting()
        {
            string sourcePath = Path.Combine(Root, "direct-invalid-output.png").SanitizePath();
            CreatePng(sourcePath, 8, 4, Color.blue);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            byte[] originalBytes = File.ReadAllBytes(RelToFull(sourcePath));
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);

            LogAssert.Expect(LogType.Error, new Regex("The output folder is invalid"));
            bool succeeded = TextureResizerAPI.TryResizeTextures(
                new[] { texture },
                null,
                1,
                TextureResizerWizard.ResizeAlgorithm.Point,
                1,
                1f,
                1f,
                Path.Combine(Root, "missing").SanitizePath(),
                false
            );

            Assert.IsFalse(succeeded);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(sourcePath)));

            foreach (string invalidOutput in new[] { string.Empty, " " })
            {
                LogAssert.Expect(LogType.Error, new Regex("The output folder is invalid"));
                succeeded = TextureResizerAPI.TryResizeTextures(
                    new[] { texture },
                    null,
                    1,
                    TextureResizerWizard.ResizeAlgorithm.Point,
                    1,
                    1f,
                    1f,
                    invalidOutput,
                    false
                );
                Assert.IsFalse(succeeded);
                CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(sourcePath)));
            }
        }

        [Test]
        public void DirectApiRejectsInvalidSourceFolderBeforeWriting()
        {
            string sourcePath = Path.Combine(Root, "direct-invalid-source.png").SanitizePath();
            CreatePng(sourcePath, 8, 4, Color.blue);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            byte[] originalBytes = File.ReadAllBytes(RelToFull(sourcePath));
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);

            LogAssert.Expect(LogType.Error, new Regex("The source folder is invalid"));
            bool succeeded = TextureResizerAPI.TryResizeTextures(
                new[] { texture },
                new[] { Path.Combine(Root, "missing").SanitizePath() },
                1,
                TextureResizerWizard.ResizeAlgorithm.Point,
                1,
                1f,
                1f,
                null,
                false
            );

            Assert.IsFalse(succeeded);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(sourcePath)));
        }

        [Test]
        public void DirectApiRejectsPackageOverwriteBeforeChangingAssetTextures()
        {
            string sourcePath = Path.Combine(Root, "direct-package-guard.png").SanitizePath();
            CreatePng(sourcePath, 8, 4, Color.green);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            byte[] originalBytes = File.ReadAllBytes(RelToFull(sourcePath));
            Texture2D sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            Texture2D packageTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(PackageTexturePath);
            Assert.IsTrue(
                packageTexture != null,
                "Package texture must be imported for this guard test"
            );

            LogAssert.Expect(LogType.Error, new Regex("Cannot overwrite a texture outside Assets"));
            bool succeeded = TextureResizerAPI.TryResizeTextures(
                new[] { sourceTexture, packageTexture },
                null,
                1,
                TextureResizerWizard.ResizeAlgorithm.Point,
                1,
                1f,
                1f,
                null,
                false
            );

            Assert.IsFalse(succeeded);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(sourcePath)));
        }

        [Test]
        public void DirectApiCapsVeryLargePassCountDuringDryRun()
        {
            string sourcePath = Path.Combine(Root, "many-passes.png").SanitizePath();
            CreatePng(sourcePath, 2, 2, Color.green);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            byte[] originalBytes = File.ReadAllBytes(RelToFull(sourcePath));
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);

            bool succeeded = TextureResizerAPI.TryResizeTextures(
                new[] { texture },
                null,
                int.MaxValue,
                TextureResizerWizard.ResizeAlgorithm.Point,
                1,
                1f,
                1f,
                null,
                true
            );

            Assert.IsTrue(succeeded);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(sourcePath)));
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

            LogAssert.Expect(LogType.Error, new Regex("Resize settings produce an invalid"));
            bool succeeded = TextureResizerAPI.TryResizeTextures(
                new[] { tex },
                null,
                0,
                TextureResizerWizard.ResizeAlgorithm.Point,
                1,
                1f,
                1f,
                null,
                true
            );
            Assert.IsFalse(succeeded);
        }

        [Test]
        public void DryRunLeavesLoadedTextureAndFileUnchanged()
        {
            string path = Path.Combine(Root, "dry.png").SanitizePath();
            CreatePng(path, 10, 6, Color.white);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.isReadable = true;
            importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            byte[] originalBytes = File.ReadAllBytes(RelToFull(path));

            TextureResizerWizard wizard = Track(
                ScriptableObject.CreateInstance<TextureResizerWizard>()
            );
            wizard.textures = new System.Collections.Generic.List<Texture2D> { texture };
            wizard.numResizes = 1;
            wizard.pixelsPerUnit = 1;
            wizard.widthMultiplier = 1f;
            wizard.heightMultiplier = 1f;
            wizard.dryRun = true;
            wizard.scalingResizeAlgorithm = TextureResizerWizard.ResizeAlgorithm.Point;
            wizard.OnWizardCreate();

            Assert.That(texture.width, Is.EqualTo(10));
            Assert.That(texture.height, Is.EqualTo(6));
            Assert.That(texture.GetPixel(0, 0), Is.EqualTo(Color.white));
            Assert.IsTrue(importer.isReadable);
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(path)));

            importer.isReadable = false;
            importer.SaveAndReimport();
            Texture2D nonReadableTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            wizard.textures[0] = nonReadableTexture;
            wizard.OnWizardCreate();

            Assert.IsFalse(importer.isReadable);
            Assert.That(nonReadableTexture.width, Is.EqualTo(10));
            Assert.That(nonReadableTexture.height, Is.EqualTo(6));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(path)));
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

            string collidingPath = Path.Combine(Root, "nested", "out.png").SanitizePath();
            CreatePng(collidingPath, 12, 6, Color.blue);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            byte[] originalBytes = File.ReadAllBytes(RelToFull(path));
            byte[] collidingBytes = File.ReadAllBytes(RelToFull(collidingPath));
            byte[] outputBytes = File.ReadAllBytes(RelToFull(outPath));
            wizard.textures = new System.Collections.Generic.List<Texture2D>
            {
                AssetDatabase.LoadAssetAtPath<Texture2D>(path),
                AssetDatabase.LoadAssetAtPath<Texture2D>(collidingPath),
            };
            LogAssert.Expect(
                LogType.Error,
                new Regex("Multiple textures would write to the same output")
            );
            wizard.OnWizardCreate();

            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(path)));
            CollectionAssert.AreEqual(collidingBytes, File.ReadAllBytes(RelToFull(collidingPath)));
            CollectionAssert.AreEqual(outputBytes, File.ReadAllBytes(RelToFull(outPath)));
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
        public void InvalidSettingsAreRefusedAndReadabilityIsRestoredAfterRun()
        {
            string path = Path.Combine(Root, "restore.png").SanitizePath();
            CreatePng(path, 8, 8, Color.red);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.isReadable = false;
            importer.SaveAndReimport();
            byte[] originalBytes = File.ReadAllBytes(RelToFull(path));

            TextureResizerWizard wizard = Track(
                ScriptableObject.CreateInstance<TextureResizerWizard>()
            );
            wizard.textures = new System.Collections.Generic.List<Texture2D>
            {
                AssetDatabase.LoadAssetAtPath<Texture2D>(path),
            };
            wizard.numResizes = 1;
            wizard.scalingResizeAlgorithm = TextureResizerWizard.ResizeAlgorithm.Point;
            (int pixelsPerUnit, float widthMultiplier, float heightMultiplier)[] invalidSettings =
            {
                (0, 1f, 1f),
                (-1, 1f, 1f),
                (1, 0f, 1f),
                (1, -1f, 1f),
                (1, 1f, 0f),
                (1, 1f, -1f),
                (1, float.NaN, 1f),
                (1, float.PositiveInfinity, 1f),
                (1, float.Epsilon, 1f),
            };
            foreach (
                (
                    int currentPixelsPerUnit,
                    float currentWidthMultiplier,
                    float currentHeightMultiplier
                ) in invalidSettings
            )
            {
                wizard.pixelsPerUnit = currentPixelsPerUnit;
                wizard.widthMultiplier = currentWidthMultiplier;
                wizard.heightMultiplier = currentHeightMultiplier;
                LogAssert.Expect(LogType.Error, new Regex("Resize settings produce an invalid"));
                wizard.OnWizardCreate();
            }

            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(path)));
            Assert.IsFalse(importer.isReadable);

            wizard.pixelsPerUnit = 1000;
            wizard.widthMultiplier = 1000f;
            wizard.heightMultiplier = 1000f;
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

        private sealed class InflatedTextureCollection
            : ICollection<Texture2D>,
                IReadOnlyCollection<Texture2D>
        {
            public int Count => 100000;

            public bool IsReadOnly => true;

            private readonly Texture2D texture;

            public InflatedTextureCollection(Texture2D texture)
            {
                this.texture = texture;
            }

            public void Add(Texture2D item) => throw new System.NotSupportedException();

            public void Clear() => throw new System.NotSupportedException();

            public bool Contains(Texture2D item) => item == texture;

            public void CopyTo(Texture2D[] array, int arrayIndex) =>
                throw new System.NotSupportedException();

            public IEnumerator<Texture2D> GetEnumerator()
            {
                yield return texture;
            }

            public bool Remove(Texture2D item) => throw new System.NotSupportedException();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
#endif
}
