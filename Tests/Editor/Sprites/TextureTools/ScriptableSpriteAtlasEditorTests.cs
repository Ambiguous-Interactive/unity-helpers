// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.U2D;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Extensions;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ScriptableSpriteAtlasEditorTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/ScriptableSpriteAtlasEditorTests";

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
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();

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
        public void CreateConfigUsesExplicitPathAndPreservesOccupiedAsset()
        {
            string path = Path.Combine(Root, "CreatedConfig.asset").SanitizePath();
            Assert.IsTrue(
                ScriptableSpriteAtlasGenerator.TryCreateConfig(
                    path.Replace('/', '\\'),
                    out ScriptableSpriteAtlas created,
                    out string createError
                ),
                createError
            );
            TrackAssetPath(path);
            try
            {
                Assert.IsTrue(created != null);
                Assert.AreEqual(path, AssetDatabase.GetAssetPath(created));
                Assert.AreSame(created, AssetDatabase.LoadAssetAtPath<ScriptableSpriteAtlas>(path));

                byte[] originalBytes = File.ReadAllBytes(RelToFull(path));
                Assert.IsFalse(
                    ScriptableSpriteAtlasGenerator.TryCreateConfig(
                        path,
                        out ScriptableSpriteAtlas duplicate,
                        out string duplicateError
                    )
                );
                Assert.IsTrue(duplicate == null);
                StringAssert.Contains("occupied", duplicateError);
                CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(RelToFull(path)));
                Assert.AreSame(created, AssetDatabase.LoadAssetAtPath<ScriptableSpriteAtlas>(path));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        [TestCase("Packages/Other.asset")]
        [TestCase("Assets/../Other.asset")]
        [TestCase("Assets/./Other.asset")]
        [TestCase("Assets//Other.asset")]
        [TestCase("Assets/Other.txt")]
        [TestCase(" Assets/Other.asset")]
        [TestCase("Assets/Other.asset ")]
        public void CreateConfigRejectsInvalidPath(string path)
        {
            Assert.IsFalse(
                ScriptableSpriteAtlasGenerator.TryCreateConfig(
                    path,
                    out ScriptableSpriteAtlas created,
                    out string error
                )
            );
            Assert.IsTrue(created == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(error));
        }

        [Test]
        public void CreateConfigRejectsMissingParentFolder()
        {
            string path = Path.Combine(Root, "MissingFolder", "Other.asset").SanitizePath();
            Assert.IsFalse(
                ScriptableSpriteAtlasGenerator.TryCreateConfig(
                    path,
                    out ScriptableSpriteAtlas created,
                    out string error
                )
            );
            Assert.IsTrue(created == null);
            StringAssert.Contains("does not exist", error);
            Assert.IsFalse(File.Exists(RelToFull(path)));
        }

        [Test]
        public void CreateConfigRejectsActiveAssetBatchWithoutWriting()
        {
            string path = Path.Combine(Root, "BatchRejectedConfig.asset").SanitizePath();
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                Assert.IsFalse(
                    ScriptableSpriteAtlasGenerator.TryCreateConfig(
                        path,
                        out ScriptableSpriteAtlas created,
                        out string error
                    )
                );
                Assert.IsTrue(created == null);
                StringAssert.Contains("outside an asset batch", error);
                Assert.IsFalse(File.Exists(RelToFull(path)));
            }
            Assert.IsFalse(File.Exists(RelToFull(path)));
        }

        [Test]
        public void GenerateWithoutRefreshPersistsDirtyConfigBeforeCreatingAtlas()
        {
            string configPath = Path.Combine(Root, "BatchConfig.asset").SanitizePath();
            string atlasPath = Path.Combine(Root, "BatchGenerated.spriteatlas").SanitizePath();
            Assert.IsTrue(
                ScriptableSpriteAtlasGenerator.TryCreateConfig(
                    configPath,
                    out ScriptableSpriteAtlas config,
                    out string error
                ),
                error
            );
            TrackAssetPath(configPath);
            TrackAssetPath(atlasPath);
            try
            {
                config.outputSpriteAtlasDirectory = Root;
                config.outputSpriteAtlasName = "BatchGenerated";
                EditorUtility.SetDirty(config);

                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                {
                    bool generated = ScriptableSpriteAtlasGenerator.GenerateWithoutRefresh(config);
                    Assert.IsTrue(
                        generated,
                        $"Atlas file exists during batch: {File.Exists(RelToFull(atlasPath))}"
                    );
                }
                AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath) != null);
                Assert.AreEqual(Root, config.outputSpriteAtlasDirectory);
                Assert.AreEqual("BatchGenerated", config.outputSpriteAtlasName);

                config.sourceFolderEntries.Add(new SourceFolderEntry { folderPath = Root });
                EditorUtility.SetDirty(config);
                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                {
                    Assert.IsFalse(ScriptableSpriteAtlasGenerator.GenerateWithoutRefresh(config));
                }
                AssetDatabaseBatchHelper.RefreshIfNotBatching();
                ScriptableSpriteAtlas reloaded =
                    AssetDatabase.LoadAssetAtPath<ScriptableSpriteAtlas>(configPath);
                Assert.AreEqual(1, reloaded.sourceFolderEntries.Count);
                Assert.AreEqual(Root, reloaded.sourceFolderEntries[0].folderPath);
            }
            finally
            {
                AssetDatabase.DeleteAsset(atlasPath);
                AssetDatabase.DeleteAsset(configPath);
            }
        }

        [Test]
        public void GeneratesSpriteAtlasAssetFromConfig()
        {
            string spritePath = Path.Combine(Root, "icon.png").SanitizePath();
            CreatePng(spritePath, 8, 8, Color.red);
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            ScriptableSpriteAtlas config = ScriptableObject.CreateInstance<ScriptableSpriteAtlas>(); // UNH-SUPPRESS: Asset becomes persistent via CreateAsset below
            config.name = "TestAtlasConfig";
            using (SerializedObject serializedConfig = new(config))
            {
                SerializedProperty entries = serializedConfig.FindProperty(
                    nameof(ScriptableSpriteAtlas.sourceFolderEntries)
                );
                SerializedProperty entry = entries.AppendArrayElement();
                ScriptableSpriteAtlasEditor.InitializeSourceFolderEntry(entry);
                entry.FindPropertyRelative(nameof(SourceFolderEntry.folderPath)).stringValue = Root;
                serializedConfig.ApplyModifiedProperties();
            }
            config.outputSpriteAtlasDirectory = Root;
            string atlasPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(Root, "TestAtlas.spriteatlas").SanitizePath()
            );
            config.outputSpriteAtlasName = Path.GetFileNameWithoutExtension(atlasPath);
            config.overrideStandalone = true;
            string configPath = Path.Combine(Root, "TestAtlasConfig.asset").SanitizePath();
            AssetDatabase.CreateAsset(config, configPath);
            TrackAssetPath(configPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            config.outputSpriteAtlasDirectory = "Packages";
            string invalidOutput = config.FullOutputPath;
            string invalidMessage =
                $"'{config.name}': Output atlas path '{invalidOutput}' must be under Assets/ and cannot be empty or contain relative segments.";
            LogAssert.Expect(LogType.Error, invalidMessage);
            Assert.IsFalse(ScriptableSpriteAtlasGenerator.Generate(config));
            config.outputSpriteAtlasDirectory = Root;

            List<Sprite> toAdd = new();
            List<Sprite> toRemove = new();
            Assert.IsTrue(ScriptableSpriteAtlasGenerator.Scan(config, toAdd, toRemove));
            Assert.AreEqual(1, toAdd.Count);
            Assert.IsEmpty(toRemove);
            string backslashFolder = Root.Replace('/', '\\');
            config.sourceFolderEntries[0].folderPath = backslashFolder;
            Assert.IsTrue(ScriptableSpriteAtlasGenerator.Scan(config, toAdd, toRemove));
            Assert.AreEqual(1, toAdd.Count);
            Assert.IsEmpty(toRemove);
            Assert.AreEqual(backslashFolder, config.sourceFolderEntries[0].folderPath);
            config.sourceFolderEntries[0].folderPath = Root;
            Assert.IsTrue(ScriptableSpriteAtlasGenerator.Synchronize(config, toAdd, toRemove));
            config.spritesToPack.Add(null);
            TrackAssetPath(atlasPath);
            config.outputSpriteAtlasName = "Occupied";
            string occupiedPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(Root, "Occupied.spriteatlas").SanitizePath()
            );
            config.outputSpriteAtlasName = Path.GetFileNameWithoutExtension(occupiedPath);
            TrackAssetPath(occupiedPath);
            string occupiedFile = RelToFull(occupiedPath);
            File.WriteAllText(occupiedFile, "occupied");
            LogAssert.Expect(
                LogType.Error,
                $"'{config.name}': Output path '{occupiedPath}' is occupied by another asset."
            );
            Assert.IsFalse(ScriptableSpriteAtlasGenerator.Generate(config));
            Assert.AreEqual("occupied", File.ReadAllText(occupiedFile));
            Assert.AreEqual(2, config.spritesToPack.Count);
            File.Delete(occupiedFile);
            config.outputSpriteAtlasName = Path.GetFileNameWithoutExtension(atlasPath);
            Assert.IsTrue(
                ScriptableSpriteAtlasGenerator.Generate(config),
                "generate after collision"
            );
            CollectionAssert.DoesNotContain(config.spritesToPack, null);
            List<string> differences = new();
            Assert.IsTrue(
                ScriptableSpriteAtlasGenerator.TryFindDrift(config, differences),
                "drift after initial generate"
            );
            Assert.IsEmpty(differences);
            Assert.IsFalse(ScriptableSpriteAtlasGenerator.Generate(config));

            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            Assert.IsTrue(atlas != null, ".spriteatlas should be generated");
            ScriptableSpriteAtlasEditor window = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlasEditor>()
            );
            ScriptableSpriteAtlasEditor.ScanResult cachedScan = new() { hasScanned = true };
            cachedScan.spritesToRemove.Add(config.spritesToPack[0]);
            config.sourceFolderEntries[0].folderPath = Root + "/Missing";
            LogAssert.Expect(
                LogType.Error,
                $"'{config.name}': Invalid or empty folder path '{Root}/Missing' prevents a complete scan."
            );
            window.SyncListToScanResult(config, cachedScan);
            Assert.IsFalse(cachedScan.hasScanned);
            Assert.IsEmpty(cachedScan.spritesToRemove);
            Assert.AreEqual(1, config.spritesToPack.Count);
            config.sourceFolderEntries[0].folderPath = Root;
            config.padding = 16;
            config.overrideStandalone = false;
            Assert.IsTrue(ScriptableSpriteAtlasGenerator.TryFindDrift(config, differences));
            CollectionAssert.Contains(differences, "Packing settings differ.");
            CollectionAssert.Contains(differences, "Standalone platform settings differ.");
            Assert.IsTrue(ScriptableSpriteAtlasGenerator.Generate(config));
            Assert.IsTrue(ScriptableSpriteAtlasGenerator.TryFindDrift(config, differences));
            Assert.IsEmpty(differences);
        }

        [Test]
        public void SourceTextureImportSettingsPreviewAndApplyWithoutWindow()
        {
            string spritePath = Root + "/uncompressed-source.png";
            string opaquePath = Root + "/opaque-source.png";
            CreatePng(spritePath, 8, 8, new Color(1f, 0f, 0f, 0.5f));
            CreatePng(opaquePath, 8, 8, Color.red, TextureFormat.RGB24);
            AssetDatabase.ImportAsset(spritePath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(opaquePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            TextureImporter opaqueImporter = AssetImporter.GetAtPath(opaquePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(opaqueImporter, Is.Not.Null);
            importer.textureType = TextureImporterType.Sprite;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.crunchedCompression = true;
            importer.SaveAndReimport();
            opaqueImporter.textureType = TextureImporterType.Sprite;
            opaqueImporter.textureCompression = TextureImporterCompression.Compressed;
            opaqueImporter.crunchedCompression = true;
            opaqueImporter.SaveAndReimport();

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            Sprite opaqueSprite = AssetDatabase.LoadAssetAtPath<Sprite>(opaquePath);
            Assert.That(sprite, Is.Not.Null);
            Assert.That(opaqueSprite, Is.Not.Null);
            ScriptableSpriteAtlas config = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
            );
            config.spritesToPack.Add(sprite);
            config.spritesToPack.Add(sprite);
            config.spritesToPack.Add(opaqueSprite);
            string fullPath = RelToFull(spritePath);
            string opaqueFullPath = RelToFull(opaquePath);
            byte[] pngBefore = File.ReadAllBytes(fullPath);
            byte[] opaquePngBefore = File.ReadAllBytes(opaqueFullPath);
            byte[] metaBefore = File.ReadAllBytes(fullPath + ".meta");
            byte[] opaqueMetaBefore = File.ReadAllBytes(opaqueFullPath + ".meta");

            Assert.That(
                ScriptableSpriteAtlasGenerator.TrySetSourceTexturesUncompressed(
                    config,
                    false,
                    out int previewCount,
                    out string previewError
                ),
                Is.True,
                previewError
            );
            Assert.That(previewCount, Is.EqualTo(2));
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Compressed)
            );
            CollectionAssert.AreEqual(pngBefore, File.ReadAllBytes(fullPath));
            CollectionAssert.AreEqual(metaBefore, File.ReadAllBytes(fullPath + ".meta"));
            CollectionAssert.AreEqual(opaquePngBefore, File.ReadAllBytes(opaqueFullPath));
            CollectionAssert.AreEqual(
                opaqueMetaBefore,
                File.ReadAllBytes(opaqueFullPath + ".meta")
            );

            Assert.That(
                ScriptableSpriteAtlasGenerator.TrySetSourceTexturesUncompressed(
                    config,
                    true,
                    out int appliedCount,
                    out string applyError,
                    (path, index, total) =>
                    {
                        if (index == 2)
                        {
                            throw new System.InvalidOperationException(
                                "Injected second texture failure."
                            );
                        }
                    }
                ),
                Is.False,
                applyError
            );
            Assert.That(appliedCount, Is.EqualTo(1));
            StringAssert.Contains("Injected second texture failure.", applyError);
            importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed)
            );
            AssetDatabase.ImportAsset(spritePath, ImportAssetOptions.ForceSynchronousImport);
            importer = AssetImporter.GetAtPath(spritePath) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.crunchedCompression, Is.False);
            Assert.That(
                importer.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed)
            );
            TextureImporterPlatformSettings settings = importer.GetDefaultPlatformTextureSettings();
            Assert.That(settings.overridden, Is.True);
            Assert.That(settings.format, Is.EqualTo(TextureImporterFormat.RGBA32));
            Assert.That(
                settings.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed)
            );
            Assert.That(settings.crunchedCompression, Is.False);
            Assert.That(settings.compressionQuality, Is.EqualTo(100));
            CollectionAssert.AreEqual(pngBefore, File.ReadAllBytes(fullPath));

            opaqueImporter = AssetImporter.GetAtPath(opaquePath) as TextureImporter;
            Assert.That(
                opaqueImporter.textureCompression,
                Is.EqualTo(TextureImporterCompression.Compressed)
            );

            Assert.That(
                ScriptableSpriteAtlasGenerator.TrySetSourceTexturesUncompressed(
                    config,
                    true,
                    out int repeatCount,
                    out string repeatError
                ),
                Is.True,
                repeatError
            );
            Assert.That(repeatCount, Is.EqualTo(1));
            AssetDatabase.ImportAsset(opaquePath, ImportAssetOptions.ForceSynchronousImport);
            opaqueImporter = AssetImporter.GetAtPath(opaquePath) as TextureImporter;
            Assert.That(
                opaqueImporter.textureCompression,
                Is.EqualTo(TextureImporterCompression.Uncompressed)
            );
            Assert.That(
                opaqueImporter.GetDefaultPlatformTextureSettings().format,
                Is.EqualTo(TextureImporterFormat.RGB24)
            );
            CollectionAssert.AreEqual(opaquePngBefore, File.ReadAllBytes(opaqueFullPath));

            Assert.That(
                ScriptableSpriteAtlasGenerator.TrySetSourceTexturesUncompressed(
                    config,
                    true,
                    out int noOpCount,
                    out string noOpError
                ),
                Is.True,
                noOpError
            );
            Assert.That(noOpCount, Is.Zero);
        }

        [Test]
        public void SpriteCollectionHelpersPreserveValidSpriteOrder()
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite first = Track(Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), Vector2.zero));
            Sprite second = Track(Sprite.Create(texture, new Rect(1f, 1f, 1f, 1f), Vector2.zero));
            List<Sprite> sprites = new() { null, first, null, second };
            List<Sprite> destination = new() { second };

            Assert.AreEqual(0, ScriptableSpriteAtlasEditor.CountValidSprites(null));
            Assert.AreEqual(2, ScriptableSpriteAtlasEditor.CountValidSprites(sprites));

            ScriptableSpriteAtlasEditor.AppendSpritesWithTextures(null, destination);
            ScriptableSpriteAtlasEditor.AppendSpritesWithTextures(sprites, null);
            ScriptableSpriteAtlasEditor.AppendSpritesWithTextures(sprites, destination);

            CollectionAssert.AreEqual(new[] { second, first, second }, destination);
            CollectionAssert.AreEqual(
                new Object[] { null, first, null, second },
                ScriptableSpriteAtlasEditor.ToObjectArray(sprites)
            );
            CollectionAssert.IsEmpty(ScriptableSpriteAtlasEditor.ToObjectArray(null));

            ScriptableSpriteAtlas config = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
            );
            config.spritesToPack.Add(first);
            Assert.IsTrue(
                ScriptableSpriteAtlasGenerator.Synchronize(
                    config,
                    new[] { second },
                    new[] { first }
                )
            );
            CollectionAssert.AreEquivalent(new[] { first, second }, config.spritesToPack);
            Assert.IsTrue(
                ScriptableSpriteAtlasGenerator.Synchronize(
                    config,
                    System.Array.Empty<Sprite>(),
                    new[] { first },
                    removeUnmatchedSprites: true
                )
            );
            CollectionAssert.AreEqual(new[] { second }, config.spritesToPack);

            Object.DestroyImmediate(second); // UNH-SUPPRESS: Exercises destroyed Sprite filtering.

            Assert.AreEqual(1, ScriptableSpriteAtlasEditor.CountValidSprites(sprites));
            destination.Clear();
            ScriptableSpriteAtlasEditor.AppendSpritesWithTextures(sprites, destination);
            CollectionAssert.AreEqual(new[] { first }, destination);
        }

        [Test]
        public void AtlasConfigSortPreservesEqualNameOrder()
        {
            ScriptableSpriteAtlas secondSame = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
            );
            ScriptableSpriteAtlas firstSame = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
            );
            ScriptableSpriteAtlas alpha = Track(
                ScriptableObject.CreateInstance<ScriptableSpriteAtlas>()
            );
            secondSame.name = "Same";
            firstSame.name = "Same";
            alpha.name = "Alpha";
            List<ScriptableSpriteAtlas> configs = new() { secondSame, firstSame, alpha };

            ScriptableSpriteAtlasEditor.SortAtlasConfigs(configs);

            CollectionAssert.AreEqual(new[] { alpha, secondSame, firstSame }, configs);
            Assert.DoesNotThrow(() => ScriptableSpriteAtlasEditor.SortAtlasConfigs(null));
        }

        private void CreatePng(
            string relPath,
            int w,
            int h,
            Color c,
            TextureFormat format = TextureFormat.RGBA32
        )
        {
            string dir = Path.GetDirectoryName(relPath)?.SanitizePath();
            EnsureFolder(dir);
            Texture2D t = new(w, h, format, false);
            try
            {
                Color[] pix = new Color[w * h];
                for (int i = 0; i < pix.Length; i++)
                {
                    pix[i] = c;
                }

                t.SetPixels(pix);
                t.Apply();
                byte[] data = t.EncodeToPNG();
                File.WriteAllBytes(RelToFull(relPath), data);
                TrackAssetPath(relPath);
            }
            finally
            {
                Object.DestroyImmediate(t); // UNH-SUPPRESS: Cleanup temporary texture in finally block
            }
        }
    }
#endif
}
