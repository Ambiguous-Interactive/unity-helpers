// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
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
        public void ProcessingFailureRestoresImporterSettingsAndCleansTemporaryTexture()
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

            /*
                A negative radius fails during kernel allocation, after the importer and destination
                texture have changed, so this deterministically exercises both cleanup paths.
            */
            Assert.Throws<OverflowException>(() =>
                window.TryWriteBlurredTexture(source, radius: -1)
            );

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
            for (int i = 0; i < textures.Length; i++)
            {
                Texture2D texture = textures[i];
                if (texture != null && texture.name == ImageBlurTool.TemporaryTextureName)
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
    }
#endif
}
