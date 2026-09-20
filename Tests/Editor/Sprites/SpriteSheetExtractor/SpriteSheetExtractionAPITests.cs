// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class SpriteSheetExtractionAPITests : CommonTestBase
    {
        private const string Root = "Assets/SpriteSheetExtractionAPITests";
        private const string Source = Root + "/source.png";
        private const string Output = Root + "/output.png";
        private const string Prefab = Root + "/reference.prefab";
        private const string WindowOutput = Root + "/window_000.png";

        private static List<SpriteSheetExtractionRequest> Requests()
        {
            return new List<SpriteSheetExtractionRequest>
            {
                new(Source, Output, new Rect(0, 0, 1, 1), new Vector2(0.25f, 0.75f), Vector4.zero),
            };
        }

        private static string ToFullPath(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!AssetDatabase.IsValidFolder(Root))
            {
                AssetDatabase.CreateFolder("Assets", nameof(SpriteSheetExtractionAPITests));
            }

            Texture2D texture = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            texture.SetPixels32(
                new[]
                {
                    new Color32(255, 0, 0, 255),
                    new Color32(0, 255, 0, 255),
                    new Color32(0, 0, 255, 255),
                    new Color32(255, 255, 255, 255),
                }
            );
            texture.Apply();
            File.WriteAllBytes(ToFullPath(Source), texture.EncodeToPNG());

            AssetDatabase.ImportAsset(Source);
            TextureImporter importer = AssetImporter.GetAtPath(Source) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.isReadable = false;
            importer.SaveAndReimport();
        }

        [TearDown]
        public override void TearDown()
        {
            AssetDatabase.DeleteAsset(Output);
            AssetDatabase.DeleteAsset(Prefab);
            AssetDatabase.DeleteAsset(WindowOutput);
            base.TearDown();
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            AssetDatabase.DeleteAsset(Root);
            base.OneTimeTearDown();
        }

        [Test]
        public void ExtractCopiesPixelsAndRestoresSourceReadability()
        {
            SpriteSheetExtractionResult result = SpriteSheetExtractionAPI.Extract(Requests());

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.ExtractedCount, Is.EqualTo(1));
            Assert.That(File.Exists(ToFullPath(Output)), Is.True);
            TextureImporter sourceImporter = AssetImporter.GetAtPath(Source) as TextureImporter;
            TextureImporter outputImporter = AssetImporter.GetAtPath(Output) as TextureImporter;
            Assert.IsTrue(sourceImporter != null);
            Assert.IsTrue(outputImporter != null);
            Assert.That(sourceImporter.isReadable, Is.False);
            Assert.That(outputImporter.isReadable, Is.False);
            Assert.That(outputImporter.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(outputImporter.spritePivot, Is.EqualTo(new Vector2(0.25f, 0.75f)));

            Texture2D output = Track(new Texture2D(1, 1, TextureFormat.RGBA32, false));
            Assert.That(output.LoadImage(File.ReadAllBytes(ToFullPath(Output))), Is.True);
            Color32 pixel = output.GetPixels32()[0];
            Assert.That(pixel.r, Is.EqualTo(255));
            Assert.That(pixel.g, Is.EqualTo(0));
            Assert.That(pixel.b, Is.EqualTo(0));
        }

        [Test]
        public void DryRunAndCancellationLeaveAssetsUntouched()
        {
            SpriteSheetExtractionResult preview = SpriteSheetExtractionAPI.Extract(
                Requests(),
                dryRun: true
            );
            SpriteSheetExtractionResult canceled = SpriteSheetExtractionAPI.Extract(
                Requests(),
                cancelRequested: (_, _) => true
            );

            Assert.That(preview.ExtractedCount, Is.EqualTo(1));
            Assert.That(preview.Errors, Is.Empty);
            Assert.That(canceled.Canceled, Is.True);
            Assert.That(canceled.ExtractedCount, Is.Zero);
            Assert.That(File.Exists(ToFullPath(Output)), Is.False);
            TextureImporter importer = AssetImporter.GetAtPath(Source) as TextureImporter;
            Assert.IsTrue(importer != null);
            Assert.That(importer.isReadable, Is.False);
        }

        [Test]
        public void RejectsPathTraversalBeforeWriting()
        {
            SpriteSheetExtractionRequest request = new(
                Source,
                "Assets/../output.png",
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f),
                Vector4.zero
            );

            SpriteSheetExtractionResult result = SpriteSheetExtractionAPI.Extract(
                new[] { request }
            );

            Assert.That(result.ExtractedCount, Is.Zero);
            Assert.That(result.Errors, Is.Not.Empty);
            Assert.That(File.Exists(ToFullPath(Output)), Is.False);
        }

        [Test]
        public void ExistingOutputIsSkippedUnlessOverwriteRequested()
        {
            SpriteSheetExtractionResult first = SpriteSheetExtractionAPI.Extract(Requests());
            SpriteSheetExtractionResult skipped = SpriteSheetExtractionAPI.Extract(Requests());
            SpriteSheetExtractionRequest replacement = new(
                Source,
                Output,
                new Rect(1, 0, 1, 1),
                new Vector2(0.5f, 0.5f),
                Vector4.zero
            );
            SpriteSheetExtractionResult overwritten = SpriteSheetExtractionAPI.Extract(
                new[] { replacement },
                overwriteExisting: true
            );

            Assert.That(first.ExtractedCount, Is.EqualTo(1));
            Assert.That(skipped.SkippedCount, Is.EqualTo(1));
            Assert.That(skipped.ExtractedCount, Is.Zero);
            Assert.That(overwritten.ExtractedCount, Is.EqualTo(1));
            Assert.That(overwritten.Errors, Is.Empty);
            Texture2D output = Track(new Texture2D(1, 1, TextureFormat.RGBA32, false));
            Assert.That(output.LoadImage(File.ReadAllBytes(ToFullPath(Output))), Is.True);
            Color32 pixel = output.GetPixels32()[0];
            Assert.That(pixel.r, Is.EqualTo(0));
            Assert.That(pixel.g, Is.EqualTo(255));
        }

        [Test]
        public void WindowExtractionUsesExplicitPipeline()
        {
            SpriteSheetExtractor window = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            window._outputDirectory = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(Root);
            window._namingPrefix = "window";
            window._discoveredSheets = new List<SpriteSheetExtractor.SpriteSheetEntry>
            {
                new()
                {
                    _assetPath = Source,
                    _isSelected = true,
                    _sprites = new List<SpriteSheetExtractor.SpriteEntryData>
                    {
                        new()
                        {
                            _originalName = "first",
                            _rect = new Rect(0, 0, 1, 1),
                            _isSelected = true,
                        },
                    },
                },
            };

            window.ExtractSelectedSprites();

            Assert.That(File.Exists(ToFullPath(WindowOutput)), Is.True);
            TextureImporter importer = AssetImporter.GetAtPath(WindowOutput) as TextureImporter;
            Assert.IsTrue(importer != null);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
        }

        [Test]
        public void DiscoveryAndWindowFindSameSpriteTexture()
        {
            SpriteSheetDiscoveryResult discovery = SpriteSheetExtractionAPI.Discover(
                new[] { Root, Root },
                "^source$"
            );
            SpriteSheetDiscoveryResult invalid = SpriteSheetExtractionAPI.Discover(
                new[] { Root },
                "["
            );

            Assert.That(discovery.Success, Is.True);
            Assert.That(discovery.AssetPaths, Is.EqualTo(new[] { Source }));
            Assert.That(invalid.Success, Is.False);
            Assert.That(invalid.AssetPaths, Is.Empty);

            SpriteSheetExtractor window = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            UnityEngine.Object folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(Root);
            Assert.IsTrue(folder != null);
            window._inputDirectories = new List<UnityEngine.Object> { folder, folder };
            window._spriteNameRegex = "^source$";
            window.DiscoverSpriteSheets(generatePreviews: false);

            Assert.That(window._discoveredSheets.Count, Is.EqualTo(1));
            Assert.That(window._discoveredSheets[0]._assetPath, Is.EqualTo(Source));
        }

        [Test]
        public void ReferenceReplacementPreviewsThenChangesPrefab()
        {
            SpriteSheetExtractionResult extraction = SpriteSheetExtractionAPI.Extract(Requests());
            Assert.That(extraction.Errors, Is.Empty);
            Sprite source = AssetDatabase.LoadAssetAtPath<Sprite>(Source);
            Sprite replacement = AssetDatabase.LoadAssetAtPath<Sprite>(Output);
            Assert.IsTrue(source != null);
            Assert.IsTrue(replacement != null);

            GameObject original = Track(new GameObject("ReferenceHolder"));
            SpriteRenderer renderer = original.AddComponent<SpriteRenderer>();
            renderer.sprite = source;
            PrefabUtility.SaveAsPrefabAsset(original, Prefab);

            Dictionary<Sprite, Sprite> mapping = new() { { source, replacement } };
            SpriteReferenceReplacementResult preview = SpriteSheetReferenceReplacementAPI.Run(
                mapping,
                new[] { Prefab }
            );
            Assert.That(preview.Errors, Is.Empty);
            Assert.That(preview.ModifiedAssets, Is.EqualTo(1));
            Assert.That(preview.MatchedReferences, Is.EqualTo(1));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            Assert.IsTrue(prefab != null);
            Assert.That(prefab.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(source));

            SpriteReferenceReplacementResult canceled = SpriteSheetReferenceReplacementAPI.Run(
                mapping,
                new[] { Prefab },
                applyChanges: true,
                cancelRequested: (_, _) => true
            );
            SpriteReferenceReplacementResult noOp = SpriteSheetReferenceReplacementAPI.Run(
                new Dictionary<Sprite, Sprite> { { source, source } },
                new[] { Prefab },
                applyChanges: true
            );
            Assert.That(canceled.Canceled, Is.True);
            Assert.That(canceled.ModifiedAssets, Is.Zero);
            Assert.That(noOp.ModifiedAssets, Is.Zero);
            Assert.That(prefab.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(source));

            SpriteReferenceReplacementResult applied = SpriteSheetReferenceReplacementAPI.Run(
                mapping,
                new[] { Prefab },
                applyChanges: true
            );
            Assert.That(applied.Errors, Is.Empty);
            Assert.That(applied.ModifiedAssets, Is.EqualTo(1));
            Assert.That(prefab.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(replacement));
        }

        [Test]
        public void ReferenceReplacementRejectsTransientSprites()
        {
            Sprite source = AssetDatabase.LoadAssetAtPath<Sprite>(Source);
            Assert.IsTrue(source != null);
            Texture2D texture = Track(new Texture2D(1, 1, TextureFormat.RGBA32, false));
            Sprite transient = Track(
                Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f))
            );

            SpriteReferenceReplacementResult result = SpriteSheetReferenceReplacementAPI.Run(
                new Dictionary<Sprite, Sprite> { { source, transient } },
                new[] { Prefab },
                applyChanges: true
            );

            Assert.That(result.Errors, Is.Not.Empty);
            Assert.That(result.ModifiedAssets, Is.Zero);
        }
    }
#endif
}
