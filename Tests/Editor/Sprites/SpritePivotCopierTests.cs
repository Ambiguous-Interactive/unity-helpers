// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class SpritePivotCopierTests : BatchedEditorTestBase
    {
        private readonly List<string> _ownedFolders = new();
        private string _folder;

        private static void AssertPivot(string path, Vector2 expected)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            TextureImporterSettings settings = new();
            importer.ReadTextureSettings(settings);
            Assert.AreEqual((int)SpriteAlignment.Custom, settings.spriteAlignment);
            Assert.That(Vector2.Distance(expected, settings.spritePivot), Is.LessThan(0.00001f));
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsTrue(sprite != null);
            Assert.That(
                Vector2.Distance(expected, sprite.pivot / sprite.rect.size),
                Is.LessThan(0.00001f)
            );
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _folder = "Assets/" + nameof(SpritePivotCopierTests) + Guid.NewGuid().ToString("N");
            Assert.IsFalse(Directory.Exists(_folder));
            _ownedFolders.Add(_folder);
            ExecuteWithImmediateImport(() => EnsureFolder(_folder), refreshBefore: false);
        }

        public override void TearDown()
        {
            try
            {
                CleanupTrackedFoldersAndAssets();
            }
            finally
            {
                base.TearDown();
            }
        }

        public override void OneTimeTearDown()
        {
            base.OneTimeTearDown();
            foreach (string folder in _ownedFolders)
            {
                Assert.IsFalse(Directory.Exists(folder), folder);
                Assert.IsFalse(File.Exists(folder + ".meta"), folder);
            }
        }

        [TestCase(SpriteAlignment.Custom, 0.25f, 0.75f)]
        [TestCase(SpriteAlignment.Center, 0.5f, 0.5f)]
        [TestCase(SpriteAlignment.TopLeft, 0f, 1f)]
        public void CopiesEffectivePivotAcrossDimensionsWithoutChangingOtherSettings(
            SpriteAlignment alignment,
            float x,
            float y
        )
        {
            Sprite source = CreateSprite(
                "source_pivot_frame",
                new Vector2(0.25f, 0.75f),
                alignment,
                8,
                12
            );
            Sprite target = CreateSprite(
                "source_frame",
                Vector2.zero,
                SpriteAlignment.BottomLeft,
                16,
                20
            );
            string sourcePath = AssetDatabase.GetAssetPath(source);
            string targetPath = AssetDatabase.GetAssetPath(target);
            byte[] sourceMeta = File.ReadAllBytes(sourcePath + ".meta");
            TextureImporter targetImporter = AssetImporter.GetAtPath(targetPath) as TextureImporter;
            Assert.IsTrue(targetImporter != null);
            Vector4 border = targetImporter.spriteBorder;
            float pixelsPerUnit = targetImporter.spritePixelsPerUnit;
            int depth = AssetDatabaseBatchHelper.CurrentBatchDepth;
            Assert.That(depth, Is.GreaterThan(0));
            Assert.IsTrue(
                SpritePivotCopierAPI.TryCopyPivot(
                    source,
                    target,
                    out bool changed,
                    out string error
                ),
                error
            );
            Assert.IsTrue(changed);
            Assert.AreEqual(depth, AssetDatabaseBatchHelper.CurrentBatchDepth);
            AssertPivot(targetPath, new Vector2(x, y));
            targetImporter = AssetImporter.GetAtPath(targetPath) as TextureImporter;
            Assert.IsTrue(targetImporter != null);
            Assert.AreEqual(border, targetImporter.spriteBorder);
            Assert.AreEqual(pixelsPerUnit, targetImporter.spritePixelsPerUnit);
            Assert.IsFalse(targetImporter.isReadable);
            CollectionAssert.AreEqual(sourceMeta, File.ReadAllBytes(sourcePath + ".meta"));
            Sprite freshTarget = AssetDatabase.LoadAssetAtPath<Sprite>(targetPath);
            Assert.IsTrue(
                SpritePivotCopierAPI.TryCopyPivot(source, freshTarget, out changed, out error),
                error
            );
            Assert.IsFalse(changed);
        }

        [Test]
        public void BatchCapturesSourcePivotsBeforeCopyingAnOverlappingChain()
        {
            CreateSprite("chain_pivot_pivot_frame", new Vector2(0.125f, 0.25f));
            CreateSprite("chain_pivot_frame", new Vector2(0.75f, 0.875f));
            CreateSprite("chain_frame", Vector2.zero);
            Assert.IsTrue(
                SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                    _folder,
                    out int changed,
                    out string error
                ),
                error
            );
            Assert.AreEqual(2, changed);
            AssertPivot(_folder + "/chain_pivot_frame.png", new Vector2(0.125f, 0.25f));
            AssertPivot(_folder + "/chain_frame.png", new Vector2(0.75f, 0.875f));
        }

        [Test]
        public void ImporterUndoRestoresThePreviousPivotBeforeExplicitReimport()
        {
            Sprite source = CreateSprite("source", new Vector2(0.25f, 0.75f));
            Sprite target = CreateSprite("target", Vector2.zero);
            string path = AssetDatabase.GetAssetPath(target);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            Undo.IncrementCurrentGroup();
            try
            {
                Assert.IsTrue(
                    SpritePivotCopierAPI.TryCopyPivot(
                        source,
                        target,
                        out bool changed,
                        out string error
                    ),
                    error
                );
                Assert.IsTrue(changed);
                Undo.PerformUndo();
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(importer != null);
                TextureImporterSettings settings = new();
                importer.ReadTextureSettings(settings);
                Assert.AreEqual(Vector2.zero, settings.spritePivot);
                ExecuteWithImmediateImport(importer.SaveAndReimport, refreshBefore: false);
                AssertPivot(path, Vector2.zero);
            }
            finally
            {
                if (importer != null)
                {
                    Undo.ClearUndo(importer);
                }
                Undo.IncrementCurrentGroup();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AmbiguousSourceOrTargetNamesRefuseAllWrites(bool duplicateSource)
        {
            CreateSprite("art_pivot_frame", new Vector2(0.25f, 0.75f));
            CreateSprite("art_frame", Vector2.zero);
            string duplicateName = duplicateSource ? "art_pivot_frame" : "art_frame";
            CreateSprite("sub/" + duplicateName, Vector2.one);
            string targetPath = _folder + "/art_frame.png";
            byte[] before = File.ReadAllBytes(targetPath + ".meta");
            Assert.IsFalse(
                SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                    _folder,
                    out int changed,
                    out string error
                )
            );
            Assert.AreEqual(0, changed);
            Assert.That(error, Does.Contain("ambiguous"));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath + ".meta"));
        }

        [Test]
        public void MissingCounterpartRefusesValidNeighborChanges()
        {
            CreateSprite("valid_pivot_frame", new Vector2(0.25f, 0.75f));
            CreateSprite("valid_frame", Vector2.zero);
            CreateSprite("missing_pivot_frame", Vector2.one);
            string targetPath = _folder + "/valid_frame.png";
            byte[] before = File.ReadAllBytes(targetPath + ".meta");
            Assert.IsFalse(
                SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                    _folder,
                    out int changed,
                    out string error
                )
            );
            Assert.AreEqual(0, changed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath + ".meta"));
        }

        [Test]
        public void CustomMarkerReplacementAndRecursiveFoldersAreSupported()
        {
            CreateSprite("nested/art.origin", new Vector2(0.25f, 0.75f));
            CreateSprite("art.target", Vector2.zero);
            Assert.IsTrue(
                SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                    _folder,
                    out int changed,
                    out string error,
                    ".origin",
                    ".target"
                ),
                error
            );
            Assert.AreEqual(1, changed);
            AssertPivot(_folder + "/art.target.png", new Vector2(0.25f, 0.75f));
            Assert.IsTrue(
                SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                    _folder,
                    out changed,
                    out error,
                    ".origin",
                    ".target"
                ),
                error
            );
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void NullReplacementRemovesTheMarker()
        {
            CreateSprite("frame-pivot", new Vector2(0.25f, 0.75f));
            CreateSprite("frame", Vector2.zero);
            Assert.IsTrue(
                SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                    _folder,
                    out int changed,
                    out string error,
                    "-pivot",
                    null
                ),
                error
            );
            Assert.AreEqual(1, changed);
            AssertPivot(_folder + "/frame.png", new Vector2(0.25f, 0.75f));
        }

        [Test]
        public void MultipleSourcesForOneTargetRefuseAllWrites()
        {
            CreateSprite("foo-pivot-bar", new Vector2(0.25f, 0.75f));
            CreateSprite("foo-bar-pivot", Vector2.one);
            CreateSprite("foo-bar", Vector2.zero);
            Assert.IsFalse(
                SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                    _folder,
                    out int changed,
                    out string error,
                    "-pivot",
                    string.Empty
                )
            );
            Assert.AreEqual(0, changed);
            Assert.That(error, Does.Contain("Multiple pivot sources"));
            AssertPivot(_folder + "/foo-bar.png", Vector2.zero);
        }

        [Test]
        public void PendingMultipleModeIsRefusedWithoutChangingTheTarget()
        {
            Sprite source = CreateSprite("source", new Vector2(0.25f, 0.75f));
            Sprite target = CreateSprite("target", Vector2.zero);
            TextureImporter importer =
                AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(source)) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.spriteImportMode = SpriteImportMode.Multiple;
            Assert.IsFalse(
                SpritePivotCopierAPI.TryCopyPivot(
                    source,
                    target,
                    out bool changed,
                    out string error
                )
            );
            Assert.IsFalse(changed);
            Assert.That(error, Does.Contain("Single-mode"));
            AssertPivot(AssetDatabase.GetAssetPath(target), Vector2.zero);
        }

        [Test]
        public void NullAndUnimportedSpritesFailWithReasons()
        {
            Assert.IsFalse(
                SpritePivotCopierAPI.TryCopyPivot(null, null, out bool changed, out string error)
            );
            Assert.IsFalse(changed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            Assert.IsFalse(
                SpritePivotCopierAPI.TryCopyPivot(sprite, sprite, out changed, out error)
            );
            Assert.IsFalse(changed);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }

        [Test]
        public void EmptySelectionAndInvalidDirectoriesDoNotReportSuccess()
        {
            foreach (
                string path in new[]
                {
                    null,
                    string.Empty,
                    " ",
                    "Packages",
                    "Assets/../Outside",
                    "Assets/NoSuchPivotFolder",
                    _folder,
                }
            )
            {
                Assert.IsFalse(
                    SpritePivotCopierAPI.TryCopyPivotsInDirectory(
                        path,
                        out int changed,
                        out string error
                    ),
                    path
                );
                Assert.AreEqual(0, changed);
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            }
        }

        private Sprite CreateSprite(
            string name,
            Vector2 pivot,
            SpriteAlignment alignment = SpriteAlignment.Custom,
            int width = 8,
            int height = 8
        )
        {
            string path = _folder + "/" + name + ".png";
            ExecuteWithImmediateImport(
                () => EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/')),
                refreshBefore: false
            );
            Texture2D texture = Track(new Texture2D(width, height, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[width * height];
            Array.Fill(pixels, new Color32(255, 255, 255, 255));
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            ExecuteWithImmediateImport(
                () =>
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    Assert.IsTrue(importer != null);
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.spritePixelsPerUnit = 4f;
                    importer.isReadable = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    TextureImporterSettings settings = new();
                    importer.ReadTextureSettings(settings);
                    settings.spriteAlignment = (int)alignment;
                    settings.spritePivot = pivot;
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                    importer.SaveAndReimport();
                },
                refreshBefore: false
            );
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsTrue(sprite != null);
            return sprite;
        }
    }
}
