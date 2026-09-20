// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Tests.AssetProcessors;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Exercises Image Blur batches through real texture import and output paths.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ImageBlurToolIntegrationTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/ImageBlurToolIntegrationTests";
        private string _testRoot;

        private static void AssertImporterSettings(
            string relativePath,
            bool isReadable,
            TextureImporterCompression compression
        )
        {
            TextureImporter importer = AssetImporter.GetAtPath(relativePath) as TextureImporter;
            Assert.IsTrue(importer != null);
            Assert.That(importer.isReadable, Is.EqualTo(isReadable));
            Assert.That(importer.textureCompression, Is.EqualTo(compression));
        }

        private static int CountTemporaryTextures()
        {
            int count = 0;
            Texture2D[] textures = Resources.FindObjectsOfTypeAll<Texture2D>();
            foreach (Texture2D texture in textures)
            {
                if (
                    texture != null
                    && string.Equals(
                        texture.name,
                        ImageBlurTool.TemporaryTextureName,
                        System.StringComparison.Ordinal
                    )
                )
                {
                    count++;
                }
            }
            return count;
        }

        private static string RelToFull(string relativePath)
        {
            return Path.Combine(
                    Application.dataPath.Substring(
                        0,
                        Application.dataPath.Length - "Assets".Length
                    ),
                    relativePath
                )
                .SanitizePath();
        }

        [SetUp]
        public override void BaseSetUp()
        {
            // Must precede base.BaseSetUp(); AssertCleanAndClearAll documents why it runs first.
            AssetPostprocessorTestHandlers.AssertCleanAndClearAll();
            base.BaseSetUp();
            _testRoot = Path.Combine(Root, Guid.NewGuid().ToString("N")).SanitizePath();
            EnsureFolder(_testRoot);
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            DetectAssetChangeProcessor.ResetForTesting();
            CleanupTrackedFoldersAndAssets();
        }

        [Test]
        public void BatchRestoresImporterSettingsCleansTemporaryTexturesAndReportsOnce()
        {
            string firstPath = Path.Combine(_testRoot, "first.png").SanitizePath();
            string secondPath = Path.Combine(_testRoot, "second.png").SanitizePath();
            string firstOutputPath = Path.Combine(_testRoot, "first_blurred_2.png").SanitizePath();
            string secondOutputPath = Path.Combine(_testRoot, "second_blurred_2.png")
                .SanitizePath();
            CreatePng(firstPath, Color.red);
            CreatePng(secondPath, Color.blue);
            TrackAssetPath(firstOutputPath);
            TrackAssetPath(secondOutputPath);

            ConfigureImporter(
                firstPath,
                isReadable: false,
                TextureImporterCompression.CompressedHQ
            );
            ConfigureImporter(
                secondPath,
                isReadable: true,
                TextureImporterCompression.CompressedLQ
            );

            Texture2D first = AssetDatabase.LoadAssetAtPath<Texture2D>(firstPath);
            Texture2D second = AssetDatabase.LoadAssetAtPath<Texture2D>(secondPath);
            Assert.IsTrue(first != null);
            Assert.IsTrue(second != null);

            int temporaryTextureCount = CountTemporaryTextures();
            int reportCount = 0;
            string completionMessage = null;
            ImageBlurTool window = Track(ScriptableObject.CreateInstance<ImageBlurTool>());

            Assert.DoesNotThrow(() =>
                window.ApplyBlurToTextures(
                    new[] { first, second },
                    2,
                    (_, message) =>
                    {
                        reportCount++;
                        completionMessage = message;
                    }
                )
            );

            Assert.That(reportCount, Is.EqualTo(1), "The batch should show one completion report.");
            Assert.That(completionMessage, Does.Contain("2 of 2"));
            AssertImporterSettings(
                firstPath,
                isReadable: false,
                TextureImporterCompression.CompressedHQ
            );
            AssertImporterSettings(
                secondPath,
                isReadable: true,
                TextureImporterCompression.CompressedLQ
            );
            Assert.IsTrue(File.Exists(RelToFull(firstOutputPath)));
            Assert.IsTrue(File.Exists(RelToFull(secondOutputPath)));
            Assert.That(CountTemporaryTextures(), Is.EqualTo(temporaryTextureCount));
        }

        [Test]
        public void DirectApiWritesAndImportsBlurredAssetWithoutWindow()
        {
            string sourcePath = Path.Combine(_testRoot, "direct.png").SanitizePath();
            string expectedOutputPath = Path.Combine(_testRoot, "direct_blurred_2.png")
                .SanitizePath();
            CreatePng(sourcePath, Color.magenta);
            TrackAssetPath(expectedOutputPath);
            ConfigureImporter(
                sourcePath,
                isReadable: false,
                TextureImporterCompression.CompressedHQ
            );
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            Assert.IsTrue(source != null);
            int temporaryTextureCount = CountTemporaryTextures();

            bool success = ImageBlurAPI.TryWriteAsset(
                source,
                2,
                out string outputPath,
                out string error
            );

            Assert.IsTrue(success, error);
            Assert.IsTrue(error == null);
            Assert.That(outputPath, Is.EqualTo(expectedOutputPath));
            Assert.IsTrue(File.Exists(RelToFull(outputPath)));
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath) != null);
            AssertImporterSettings(
                sourcePath,
                isReadable: false,
                TextureImporterCompression.CompressedHQ
            );
            Assert.That(CountTemporaryTextures(), Is.EqualTo(temporaryTextureCount));
        }

        [Test]
        public void DirectApiKeepsOccupiedOutputAndUsesNextName()
        {
            string sourcePath = Path.Combine(_testRoot, "occupied.png").SanitizePath();
            string occupiedPath = Path.Combine(_testRoot, "occupied_blurred_2.png").SanitizePath();
            string nextPath = Path.Combine(_testRoot, "occupied_blurred_2_1.png").SanitizePath();
            CreatePng(sourcePath, Color.magenta);
            byte[] existingBytes = { 1, 2, 3, 4 };
            File.WriteAllBytes(RelToFull(occupiedPath), existingBytes);
            TrackAssetPath(occupiedPath);
            TrackAssetPath(nextPath);
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            int initialFileCount = Directory.GetFiles(RelToFull(_testRoot)).Length;

            bool success = ImageBlurAPI.TryWriteAsset(
                source,
                2,
                out string outputPath,
                out string error,
                importOutput: false
            );

            Assert.IsTrue(success, error);
            Assert.That(outputPath, Is.EqualTo(nextPath));
            Assert.That(File.ReadAllBytes(RelToFull(occupiedPath)), Is.EqualTo(existingBytes));
            Assert.That(File.Exists(RelToFull(nextPath)), Is.True);
            Assert.That(
                Directory.GetFiles(RelToFull(_testRoot)).Length,
                Is.EqualTo(initialFileCount + 1)
            );
        }

        [Test]
        public void InvalidRadiusRestoresImporterSettingsAndDoesNotLeakTexture()
        {
            string sourcePath = Path.Combine(_testRoot, "failure.png").SanitizePath();
            CreatePng(sourcePath, Color.green);
            ConfigureImporter(
                sourcePath,
                isReadable: false,
                TextureImporterCompression.CompressedHQ
            );
            Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(sourcePath);
            Assert.IsTrue(source != null);

            int temporaryTextureCount = CountTemporaryTextures();
            ImageBlurTool window = Track(ScriptableObject.CreateInstance<ImageBlurTool>());

            LogAssert.Expect(
                LogType.Error,
                new Regex("Failed to create blurred texture for: failure")
            );
            Assert.IsFalse(window.TryWriteBlurredTexture(source, radius: -1));

            AssertImporterSettings(
                sourcePath,
                isReadable: false,
                TextureImporterCompression.CompressedHQ
            );
            Assert.That(CountTemporaryTextures(), Is.EqualTo(temporaryTextureCount));
        }

        private void CreatePng(string relativePath, Color color)
        {
            Texture2D texture = new(8, 8, TextureFormat.RGBA32, false);
            try
            {
                Color[] pixels = new Color[texture.width * texture.height];
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = color;
                }
                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(RelToFull(relativePath), texture.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture); // UNH-SUPPRESS: Test cleanup
            }

            TrackAssetPath(relativePath);
            ExecuteWithImmediateImport(() =>
                AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceSynchronousImport)
            );
        }

        private void ConfigureImporter(
            string relativePath,
            bool isReadable,
            TextureImporterCompression compression
        )
        {
            ExecuteWithImmediateImport(() =>
            {
                TextureImporter importer = AssetImporter.GetAtPath(relativePath) as TextureImporter;
                Assert.IsTrue(importer != null);
                importer.isReadable = isReadable;
                importer.textureCompression = compression;
                importer.SaveAndReimport();
            });
        }
    }
#endif
}
