// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using PivotMode = WallstopStudios.UnityHelpers.Editor.Sprites.PivotMode;

    [TestFixture]
    public sealed class SpriteSheetExtractionAPITests : CommonTestBase
    {
        private const string Root = "Assets/SpriteSheetExtractionAPITests";
        private const string Source = Root + "/source.png";
        private const string Output = Root + "/output.png";
        private const string Prefab = Root + "/reference.prefab";
        private const string SecondPrefab = Root + "/second-reference.prefab";
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

        private static int CountNonMetaFiles()
        {
            int count = 0;
            foreach (string path in Directory.GetFiles(ToFullPath(Root)))
            {
                if (!path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    ++count;
                }
            }

            return count;
        }

        private static Dictionary<string, byte[]> SnapshotProjectRootConfigSidecars()
        {
            Dictionary<string, byte[]> result = new(StringComparer.Ordinal);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            foreach (
                string path in Directory.GetFiles(
                    projectRoot,
                    "*." + SpriteSheetConfig.FileExtension + "*"
                )
            )
            {
                result.Add(path, File.ReadAllBytes(path));
            }
            return result;
        }

        private static void AssertConfigEntryUnchanged(
            SpriteSheetExtractor.SpriteSheetEntry entry,
            string texturePath,
            SpriteSheetConfig config,
            string configJson,
            Dictionary<string, byte[]> sidecars,
            bool expectedLoaded = true
        )
        {
            Assert.That(entry._assetPath, Is.EqualTo(texturePath));
            Assert.That(entry._loadedConfig, Is.SameAs(config));
            Assert.That(Serializer.JsonStringify(entry._loadedConfig), Is.EqualTo(configJson));
            Assert.That(entry._configLoaded, Is.EqualTo(expectedLoaded));
            Assert.That(entry._configStale, Is.True);
            Assert.That(entry._useGlobalSettings, Is.False);
            Assert.That(entry._pivotModeOverride, Is.EqualTo(PivotMode.Custom));
            Assert.That(entry._customPivotOverride, Is.EqualTo(new Vector2(0.25f, 0.75f)));
            Assert.That(
                entry._autoDetectionAlgorithmOverride,
                Is.EqualTo(AutoDetectionAlgorithm.UniformGrid)
            );
            Assert.That(entry._expectedSpriteCountOverride, Is.EqualTo(7));
            Assert.That(entry._snapToTextureDivisorOverride, Is.False);
            Dictionary<string, byte[]> current = SnapshotProjectRootConfigSidecars();
            Assert.That(current.Keys, Is.EquivalentTo(sidecars.Keys));
            foreach (KeyValuePair<string, byte[]> sidecar in sidecars)
            {
                Assert.That(current.TryGetValue(sidecar.Key, out byte[] currentBytes), Is.True);
                Assert.That(currentBytes, Is.EqualTo(sidecar.Value));
            }
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
            AssetDatabase.DeleteAsset(SecondPrefab);
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
            byte[] firstBytes = File.ReadAllBytes(ToFullPath(Output));
            SpriteSheetExtractionResult skipped = SpriteSheetExtractionAPI.Extract(Requests());
            Assert.That(File.ReadAllBytes(ToFullPath(Output)), Is.EqualTo(firstBytes));
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
        public void PublishSkipsOutputCreatedAfterPathSelection()
        {
            string stagedPath = Path.GetTempFileName();
            string destinationPath = stagedPath + ".png";
            byte[] stagedBytes = { 1, 2, 3 };
            byte[] occupantBytes = { 4, 5, 6 };
            try
            {
                File.WriteAllBytes(stagedPath, stagedBytes);
                File.WriteAllBytes(destinationPath, occupantBytes);

                Assert.That(
                    SpriteSheetExtractionAPI.TryPublishNewFile(stagedPath, destinationPath, out _),
                    Is.False
                );
                Assert.That(File.ReadAllBytes(destinationPath), Is.EqualTo(occupantBytes));
                Assert.That(File.ReadAllBytes(stagedPath), Is.EqualTo(stagedBytes));
            }
            finally
            {
                File.Delete(stagedPath);
                File.Delete(destinationPath);
            }
        }

        [Test]
        public void ConcurrentPublishKeepsFirstCompletedOutput()
        {
            string firstStage = Path.GetTempFileName();
            string secondStage = Path.GetTempFileName();
            string destinationPath = firstStage + ".png";
            byte[] firstBytes = { 1, 2, 3 };
            byte[] secondBytes = { 4, 5, 6 };
            using ManualResetEventSlim start = new(false);
            try
            {
                File.WriteAllBytes(firstStage, firstBytes);
                File.WriteAllBytes(secondStage, secondBytes);

                Task<bool> first = Task.Run(() =>
                {
                    start.Wait();
                    return SpriteSheetExtractionAPI.TryPublishNewFile(
                        firstStage,
                        destinationPath,
                        out _
                    );
                });
                Task<bool> second = Task.Run(() =>
                {
                    start.Wait();
                    return SpriteSheetExtractionAPI.TryPublishNewFile(
                        secondStage,
                        destinationPath,
                        out _
                    );
                });
                start.Set();
                Task.WaitAll(first, second);

                Assert.That(first.Result, Is.Not.EqualTo(second.Result));
                CollectionAssert.AreEqual(
                    first.Result ? firstBytes : secondBytes,
                    File.ReadAllBytes(destinationPath)
                );
                Assert.That(File.Exists(first.Result ? firstStage : secondStage), Is.False);
                Assert.That(File.Exists(first.Result ? secondStage : firstStage), Is.True);
            }
            finally
            {
                File.Delete(firstStage);
                File.Delete(secondStage);
                File.Delete(destinationPath);
            }
        }

        [Test]
        public void FailedPublishRemovesStagedFileAndRestoresReadability()
        {
            string outputPath = ToFullPath(Output);
            Directory.CreateDirectory(outputPath);
            int initialFileCount = Directory.GetFiles(ToFullPath(Root)).Length;
            try
            {
                SpriteSheetExtractionResult result = SpriteSheetExtractionAPI.Extract(Requests());

                Assert.That(result.ExtractedCount, Is.Zero);
                Assert.That(result.SkippedCount, Is.Zero);
                Assert.That(result.Errors, Is.Not.Empty);
                Assert.That(
                    Directory.GetFiles(ToFullPath(Root)).Length,
                    Is.EqualTo(initialFileCount)
                );
                TextureImporter importer = AssetImporter.GetAtPath(Source) as TextureImporter;
                Assert.IsTrue(importer != null);
                Assert.That(importer.isReadable, Is.False);
            }
            finally
            {
                Directory.Delete(outputPath);
            }
        }

        [Test]
        public void PublishedOutputSurvivesAnActualStagingCleanupFailure()
        {
            string root = Path.Combine(
                Application.temporaryCachePath,
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(root);
            try
            {
                string outputPath = Path.Combine(root, "output.txt");
                File.WriteAllText(outputPath, "published");
                string stagedDirectory = Path.Combine(root, "staged");
                Directory.CreateDirectory(stagedDirectory);
                Exception warning = ExclusiveFilePublisher.RemoveStagedFileAfterPublication(
                    stagedDirectory,
                    outputPath
                );
                Assert.IsInstanceOf<IOException>(warning);
                StringAssert.Contains("Published", warning.Message);
                Assert.AreEqual("published", File.ReadAllText(outputPath));
                Assert.IsTrue(Directory.Exists(stagedDirectory));
            }
            finally
            {
                Directory.Delete(root, true);
            }
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
        public void MissingSheetConfigClearsLoadedState()
        {
            SpriteSheetExtractor window = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _assetPath = Root + "/missing-config.png",
                _configLoaded = true,
                _configStale = true,
                _loadedConfig = new SpriteSheetConfig(),
            };
            Assert.That(
                File.Exists(ToFullPath(SpriteSheetConfig.GetConfigPath(entry._assetPath))),
                Is.False
            );

            Assert.That(window.LoadConfig(entry), Is.False);
            Assert.That(entry._configLoaded, Is.False);
            Assert.That(entry._configStale, Is.False);
            Assert.That(entry._loadedConfig, Is.Null);
        }

        [TestCase(null, "", TestName = "ConfigPath.Null.Empty")]
        [TestCase("", "", TestName = "ConfigPath.Empty.Empty")]
        [TestCase(" ", "", TestName = "ConfigPath.Space.Empty")]
        [TestCase("\t\r\n", "", TestName = "ConfigPath.ControlWhitespace.Empty")]
        [TestCase("\u2003\u00a0", "", TestName = "ConfigPath.UnicodeWhitespace.Empty")]
        [TestCase(
            "Assets/source.png",
            "Assets/source.png.spritesheet.json",
            TestName = "ConfigPath.Normal.ExactSuffix"
        )]
        [TestCase(
            " Assets/source.png ",
            " Assets/source.png .spritesheet.json",
            TestName = "ConfigPath.Padded.ExactSuffix"
        )]
        [TestCase(
            "Assets/source sheet.png",
            "Assets/source sheet.png.spritesheet.json",
            TestName = "ConfigPath.InteriorSpace.ExactSuffix"
        )]
        public void ConfigPathRejectsBlankAndPreservesNonblankText(
            string texturePath,
            string expected
        )
        {
            Assert.That(SpriteSheetConfig.GetConfigPath(texturePath), Is.EqualTo(expected));
        }

        [TestCase(null, TestName = "ConfigEntry.NullPath.Unchanged")]
        [TestCase("", TestName = "ConfigEntry.EmptyPath.Unchanged")]
        [TestCase(" ", TestName = "ConfigEntry.SpacePath.Unchanged")]
        [TestCase("\t\r\n", TestName = "ConfigEntry.ControlWhitespacePath.Unchanged")]
        [TestCase("\u2003\u00a0", TestName = "ConfigEntry.UnicodeWhitespacePath.Unchanged")]
        public void BlankConfigEntryRoutesPreserveStateAndProjectSidecars(string texturePath)
        {
            SpriteSheetExtractor window = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            SpriteSheetConfig config = new()
            {
                expectedSpriteCount = 7,
                textureContentHash = "seeded",
            };
            string configJson = Serializer.JsonStringify(config);
            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _assetPath = texturePath,
                _loadedConfig = config,
                _configLoaded = true,
                _configStale = true,
                _useGlobalSettings = false,
                _pivotModeOverride = PivotMode.Custom,
                _customPivotOverride = new Vector2(0.25f, 0.75f),
                _autoDetectionAlgorithmOverride = AutoDetectionAlgorithm.UniformGrid,
                _expectedSpriteCountOverride = 7,
                _snapToTextureDivisorOverride = false,
            };
            Dictionary<string, byte[]> sidecars = SnapshotProjectRootConfigSidecars();

            Assert.That(window.SaveConfig(entry), Is.False);
            AssertConfigEntryUnchanged(entry, texturePath, config, configJson, sidecars);
            Assert.That(window.LoadConfig(entry), Is.False);
            AssertConfigEntryUnchanged(entry, texturePath, config, configJson, sidecars);
            Assert.DoesNotThrow(() => window.TryAutoLoadConfig(entry));
            AssertConfigEntryUnchanged(entry, texturePath, config, configJson, sidecars);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void NullConfigEntryRoutesReturnWithoutLogging()
        {
            SpriteSheetExtractor window = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            Assert.That(window.SaveConfig(null), Is.False);
            Assert.That(window.LoadConfig(null), Is.False);
            Assert.DoesNotThrow(() => window.TryAutoLoadConfig(null));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(false, TestName = "AutoLoad.InvalidRelativePath.PreservesState")]
        [TestCase(true, TestName = "AutoLoad.InvalidAbsolutePath.PreservesState")]
        public void AutoLoadInvalidPathLogsAndPreservesState(bool absolutePath)
        {
            string texturePath = Root + "/bad\0.png";
            if (absolutePath)
            {
                texturePath = ToFullPath(Root) + "/bad\0.png";
            }
            AssertAutoLoadPreservesState(texturePath, true, true);
        }

        [TestCase(true, TestName = "AutoLoad.MissingSidecar.LoadedStateUnchanged")]
        [TestCase(false, TestName = "AutoLoad.MissingSidecar.UnloadedStateUnchanged")]
        public void AutoLoadMissingSidecarPreservesState(bool loaded)
        {
            string texturePath = Root + "/missing auto-load.png";
            Assert.That(
                File.Exists(ToFullPath(SpriteSheetConfig.GetConfigPath(texturePath))),
                Is.False
            );
            AssertAutoLoadPreservesState(texturePath, loaded, false);
        }

        [Test]
        public void AutoLoadExistingSidecarWithSpacesLoadsWithoutChangingBytes()
        {
            string folder = Root + "/auto load with spaces";
            string texturePath = folder + "/source sheet.png";
            string configPath = ToFullPath(SpriteSheetConfig.GetConfigPath(texturePath));
            Assert.That(Directory.Exists(ToFullPath(folder)), Is.False);
            AssetDatabase.CreateFolder(Root, "auto load with spaces");
            try
            {
                File.Copy(ToFullPath(Source), ToFullPath(texturePath));
                SpriteSheetConfig config = new()
                {
                    pivotMode = PivotMode.Custom,
                    customPivot = new Vector2(0.25f, 0.75f),
                    algorithm = (int)AutoDetectionAlgorithm.UniformGrid,
                    expectedSpriteCount = 7,
                    snapToTextureDivisor = false,
                    textureContentHash = " ",
                };
                string json = Serializer.JsonStringify(config);
                File.WriteAllText(configPath, json);
                byte[] configBytes = File.ReadAllBytes(configPath);
                byte[] textureBytes = File.ReadAllBytes(ToFullPath(texturePath));
                SpriteSheetExtractor window = Track(
                    ScriptableObject.CreateInstance<SpriteSheetExtractor>()
                );
                SpriteSheetExtractor.SpriteSheetEntry entry = new() { _assetPath = texturePath };
                Assert.That(entry._loadedConfig, Is.Null);
                Assert.That(entry._configLoaded, Is.False);
                window.TryAutoLoadConfig(entry);
                Assert.That(entry._loadedConfig, Is.Not.Null);
                Assert.That(Serializer.JsonStringify(entry._loadedConfig), Is.EqualTo(json));
                Assert.That(entry._assetPath, Is.EqualTo(texturePath));
                Assert.That(entry._configLoaded, Is.True);
                Assert.That(entry._configStale, Is.True);
                Assert.That(entry._useGlobalSettings, Is.False);
                Assert.That(entry._pivotModeOverride, Is.EqualTo(PivotMode.Custom));
                Assert.That(entry._customPivotOverride, Is.EqualTo(config.customPivot));
                Assert.That(
                    entry._autoDetectionAlgorithmOverride,
                    Is.EqualTo(AutoDetectionAlgorithm.UniformGrid)
                );
                Assert.That(entry._expectedSpriteCountOverride, Is.EqualTo(7));
                Assert.That(entry._snapToTextureDivisorOverride, Is.False);
                Assert.That(File.ReadAllBytes(configPath), Is.EqualTo(configBytes));
                Assert.That(File.ReadAllBytes(ToFullPath(texturePath)), Is.EqualTo(textureBytes));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ConfigRoundTripPreservesSettingsForTexturePathWithSpaces()
        {
            string folder = Root + "/folder with spaces";
            string texturePath = folder + "/source sheet.png";
            string configPath = texturePath + "." + SpriteSheetConfig.FileExtension;
            Assert.That(Directory.Exists(ToFullPath(folder)), Is.False);
            AssetDatabase.CreateFolder(Root, "folder with spaces");
            try
            {
                File.Copy(ToFullPath(Source), ToFullPath(texturePath));
                SpriteSheetExtractor window = Track(
                    ScriptableObject.CreateInstance<SpriteSheetExtractor>()
                );
                SpriteSheetExtractor.SpriteSheetEntry saved = new()
                {
                    _assetPath = texturePath,
                    _useGlobalSettings = false,
                    _pivotModeOverride = PivotMode.Custom,
                    _customPivotOverride = new Vector2(0.25f, 0.75f),
                    _autoDetectionAlgorithmOverride = AutoDetectionAlgorithm.UniformGrid,
                    _expectedSpriteCountOverride = 4,
                    _snapToTextureDivisorOverride = false,
                };
                Assert.That(SpriteSheetConfig.GetConfigPath(texturePath), Is.EqualTo(configPath));
                Assert.That(window.SaveConfig(saved), Is.True);
                Assert.That(File.Exists(ToFullPath(configPath)), Is.True);
                string savedJson = Serializer.JsonStringify(saved._loadedConfig);
                SpriteSheetExtractor.SpriteSheetEntry loaded = new() { _assetPath = texturePath };
                Assert.That(window.LoadConfig(loaded), Is.True);
                Assert.That(Serializer.JsonStringify(loaded._loadedConfig), Is.EqualTo(savedJson));
                Assert.That(loaded._assetPath, Is.EqualTo(texturePath));
                Assert.That(loaded._configLoaded, Is.True);
                Assert.That(loaded._configStale, Is.False);
                Assert.That(loaded._useGlobalSettings, Is.False);
                Assert.That(loaded._pivotModeOverride, Is.EqualTo(PivotMode.Custom));
                Assert.That(loaded._customPivotOverride, Is.EqualTo(new Vector2(0.25f, 0.75f)));
                Assert.That(
                    loaded._autoDetectionAlgorithmOverride,
                    Is.EqualTo(AutoDetectionAlgorithm.UniformGrid)
                );
                Assert.That(loaded._expectedSpriteCountOverride, Is.EqualTo(4));
                Assert.That(loaded._snapToTextureDivisorOverride, Is.False);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [TestCase(null, false, true, TestName = "ConfigHash.Absent.Nonstale")]
        [TestCase(null, false, false, TestName = "ConfigHash.Null.Nonstale")]
        [TestCase("", false, false, TestName = "ConfigHash.Empty.Nonstale")]
        [TestCase(" ", true, false, TestName = "ConfigHash.Space.LiteralAndStale")]
        [TestCase("\t\r\n", true, false, TestName = "ConfigHash.ControlWhitespace.LiteralAndStale")]
        [TestCase(
            "\u2003\u00a0",
            true,
            false,
            TestName = "ConfigHash.UnicodeWhitespace.LiteralAndStale"
        )]
        public void ConfigHashRoundTripPreservesLiteralAndMarksStaleness(
            string hash,
            bool expectedStale,
            bool omitHash
        )
        {
            string configPath = ToFullPath(SpriteSheetConfig.GetConfigPath(Source));
            Assert.That(File.Exists(configPath), Is.False);
            Assert.That(File.Exists(configPath + ".meta"), Is.False);
            SpriteSheetConfig config = new() { textureContentHash = hash };
            string json = omitHash
                ? "{\""
                    + nameof(SpriteSheetConfig.version)
                    + "\":"
                    + SpriteSheetConfig.CurrentVersion
                    + "}"
                : Serializer.JsonStringify(config);
            Assert.That(
                Serializer.JsonDeserialize<SpriteSheetConfig>(json).textureContentHash,
                Is.EqualTo(hash)
            );
            try
            {
                File.WriteAllText(configPath, json);
                SpriteSheetExtractor window = Track(
                    ScriptableObject.CreateInstance<SpriteSheetExtractor>()
                );
                SpriteSheetExtractor.SpriteSheetEntry entry = new() { _assetPath = Source };
                Assert.That(window.LoadConfig(entry), Is.True);
                Assert.That(entry._loadedConfig.textureContentHash, Is.EqualTo(hash));
                Assert.That(entry._configLoaded, Is.True);
                Assert.That(entry._configStale, Is.EqualTo(expectedStale));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                File.Delete(configPath);
                File.Delete(configPath + ".meta");
            }
        }

        [Test]
        public void DiscoveryAndWindowFindSameSpriteTexture()
        {
            SpriteSheetDiscoveryResult discovery = SpriteSheetExtractionAPI.Discover(
                new[] { Root.Replace('/', '\\'), Root.Replace('/', '\\') },
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
            Assert.IsFalse(AssetDatabaseBatchHelper.IsCurrentlyBatching);
            Assert.That(noOp.ModifiedAssets, Is.Zero);
            Assert.That(prefab.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(source));

            SpriteReferenceReplacementResult callbackFailure =
                SpriteSheetReferenceReplacementAPI.Run(
                    mapping,
                    new[] { Prefab },
                    applyChanges: true,
                    cancelRequested: (_, _) =>
                        throw new System.InvalidOperationException("callback failure")
                );
            Assert.That(callbackFailure.Errors, Is.Not.Empty);
            Assert.That(callbackFailure.ModifiedAssets, Is.Zero);
            Assert.IsFalse(AssetDatabaseBatchHelper.IsCurrentlyBatching);

            bool sawBatch = false;
            SpriteReferenceReplacementResult applied = SpriteSheetReferenceReplacementAPI.Run(
                mapping,
                new[] { Prefab },
                applyChanges: true,
                cancelRequested: (_, _) =>
                {
                    sawBatch = AssetDatabaseBatchHelper.IsCurrentlyBatching;
                    return false;
                }
            );
            Assert.That(applied.Errors, Is.Empty);
            Assert.That(applied.ModifiedAssets, Is.EqualTo(1));
            Assert.IsTrue(sawBatch);
            Assert.IsFalse(AssetDatabaseBatchHelper.IsCurrentlyBatching);
            Assert.That(prefab.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(replacement));

            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(prefab.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(source));
        }

        [Test]
        public void ReferenceReplacementFlushesUndoForEveryChangedPrefab()
        {
            SpriteSheetExtractionResult extraction = SpriteSheetExtractionAPI.Extract(Requests());
            Assert.That(extraction.Errors, Is.Empty);
            Sprite source = AssetDatabase.LoadAssetAtPath<Sprite>(Source);
            Sprite replacement = AssetDatabase.LoadAssetAtPath<Sprite>(Output);
            Assert.IsTrue(source != null);
            Assert.IsTrue(replacement != null);

            GameObject firstOriginal = Track(new GameObject("FirstReferenceHolder"));
            firstOriginal.AddComponent<SpriteRenderer>().sprite = source;
            PrefabUtility.SaveAsPrefabAsset(firstOriginal, Prefab);
            GameObject secondOriginal = Track(new GameObject("SecondReferenceHolder"));
            secondOriginal.AddComponent<SpriteRenderer>().sprite = source;
            PrefabUtility.SaveAsPrefabAsset(secondOriginal, SecondPrefab);

            GameObject firstPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);
            GameObject secondPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SecondPrefab);
            Assert.IsTrue(firstPrefab != null);
            Assert.IsTrue(secondPrefab != null);

            Undo.IncrementCurrentGroup();
            SpriteReferenceReplacementResult result = SpriteSheetReferenceReplacementAPI.Run(
                new Dictionary<Sprite, Sprite> { { source, replacement } },
                new[] { Prefab, SecondPrefab },
                applyChanges: true
            );

            Assert.That(result.Errors, Is.Empty);
            Assert.That(result.ModifiedAssets, Is.EqualTo(2));
            Assert.That(firstPrefab.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(replacement));
            Assert.That(
                secondPrefab.GetComponent<SpriteRenderer>().sprite,
                Is.EqualTo(replacement)
            );

            Undo.PerformUndo();
            Assert.That(firstPrefab.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(source));
            Assert.That(secondPrefab.GetComponent<SpriteRenderer>().sprite, Is.EqualTo(source));
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

        private void AssertAutoLoadPreservesState(string texturePath, bool loaded, bool expectError)
        {
            SpriteSheetExtractor window = Track(
                ScriptableObject.CreateInstance<SpriteSheetExtractor>()
            );
            SpriteSheetConfig config = new()
            {
                expectedSpriteCount = 7,
                textureContentHash = "seeded",
            };
            string json = Serializer.JsonStringify(config);
            SpriteSheetExtractor.SpriteSheetEntry entry = new()
            {
                _assetPath = texturePath,
                _loadedConfig = config,
                _configLoaded = loaded,
                _configStale = true,
                _useGlobalSettings = false,
                _pivotModeOverride = PivotMode.Custom,
                _customPivotOverride = new Vector2(0.25f, 0.75f),
                _autoDetectionAlgorithmOverride = AutoDetectionAlgorithm.UniformGrid,
                _expectedSpriteCountOverride = 7,
                _snapToTextureDivisorOverride = false,
            };
            string controlPath = ToFullPath(Source + "." + SpriteSheetConfig.FileExtension);
            Assert.That(File.Exists(controlPath), Is.False);
            Assert.That(File.Exists(controlPath + ".meta"), Is.False);
            try
            {
                File.WriteAllText(controlPath, json);
                byte[] controlBytes = File.ReadAllBytes(controlPath);
                byte[] sourceBytes = File.ReadAllBytes(ToFullPath(Source));
                int fileCount = CountNonMetaFiles();
                Dictionary<string, byte[]> sidecars = SnapshotProjectRootConfigSidecars();
                if (expectError)
                {
                    LogAssert.Expect(
                        LogType.Error,
                        new Regex("Failed to auto-load config for", RegexOptions.CultureInvariant)
                    );
                }
                Assert.DoesNotThrow(() => window.TryAutoLoadConfig(entry));
                AssertConfigEntryUnchanged(entry, texturePath, config, json, sidecars, loaded);
                Assert.That(File.ReadAllBytes(controlPath), Is.EqualTo(controlBytes));
                Assert.That(File.ReadAllBytes(ToFullPath(Source)), Is.EqualTo(sourceBytes));
                Assert.That(CountNonMetaFiles(), Is.EqualTo(fileCount));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                File.Delete(controlPath);
                File.Delete(controlPath + ".meta");
            }
        }
    }
#endif
}
