// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpriteHelpersTests : BatchedEditorTestBase
    {
        private const string TestFolder = "Assets/TempSpriteHelpersTests";
        private string _testTexturePath;
        private Texture2D _testTexture;
        private static readonly int[] TextureSizeCases = { 1, 2, 4, 16, 64, 256, 512 };

        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            if (Application.isPlaying)
            {
                return;
            }

            EnsureFolder(TestFolder);
            TrackFolder(TestFolder);
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();

            _testTexturePath = null;
            _testTexture = null;
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();

            if (!string.IsNullOrEmpty(_testTexturePath))
            {
                TrackAssetPath(_testTexturePath);
            }
        }

        [Test]
        public void MakeReadableWithNullTextureDoesNotThrow()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            Texture2D nullTexture = null;
            Assert.DoesNotThrow(() => nullTexture.MakeReadable());
        }

        [Test]
        public void MakeReadableWithAlreadyReadableTextureDoesNotModify()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            CreateTestTexture(readable: true);

            bool wasReadableBefore = _testTexture.isReadable;
            _testTexture.MakeReadable();
            bool isReadableAfter = _testTexture.isReadable;

            Assert.IsTrue(
                wasReadableBefore,
                "Texture should be readable before calling MakeReadable"
            );
            Assert.IsTrue(
                isReadableAfter,
                "Texture should still be readable after calling MakeReadable"
            );
        }

        [Test]
        public void MakeReadableMakesNonReadableTextureReadable()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            CreateTestTexture(readable: false);

            Assert.IsFalse(_testTexture.isReadable, "Texture should not be readable initially");

            ExecuteWithImmediateImport(() =>
            {
                _testTexture.MakeReadable();
                _testTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(_testTexturePath);
            });

            Assert.IsTrue(
                _testTexture.isReadable,
                "Texture should be readable after calling MakeReadable"
            );
        }

        [Test]
        public void MakeReadableWithNonReadableTextureUpdatesImporterSettings()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            CreateTestTexture(readable: false);
            string assetPath = AssetDatabase.GetAssetPath(_testTexture);

            _testTexture.MakeReadable();

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            Assert.IsTrue(importer != null, "Should be able to get TextureImporter");
            Assert.IsTrue(importer.isReadable, "TextureImporter isReadable should be true");
        }

        [Test]
        public void MakeReadableWithRuntimeCreatedTextureWithoutAssetPathDoesNotThrow()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            Texture2D runtimeTexture = Track(new Texture2D(2, 2));
            Assert.DoesNotThrow(() => runtimeTexture.MakeReadable());
        }

        [Test]
        public void MakeReadableWithMultipleCallsDoesNotCauseIssues()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            CreateTestTexture(readable: false);

            Assert.DoesNotThrow(() =>
            {
                _testTexture.MakeReadable();
                _testTexture.MakeReadable();
                _testTexture.MakeReadable();
            });

            ExecuteWithImmediateImport(() =>
            {
                _testTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(_testTexturePath);
            });
            Assert.IsTrue(
                _testTexture.isReadable,
                "Texture should be readable after multiple calls"
            );
        }

        [Test]
        public void MakeReadablePreservesTextureData()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            CreateTestTexture(readable: false);
            int originalWidth = _testTexture.width;
            int originalHeight = _testTexture.height;
            TextureFormat originalFormat = _testTexture.format;

            ExecuteWithImmediateImport(() =>
            {
                _testTexture.MakeReadable();
                _testTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(_testTexturePath);
            });

            Assert.AreEqual(originalWidth, _testTexture.width, "Width should be preserved");
            Assert.AreEqual(originalHeight, _testTexture.height, "Height should be preserved");
            Assert.AreEqual(originalFormat, _testTexture.format, "Format should be preserved");
        }

        [TestCaseSource(nameof(TextureSizeCases))]
        public void MakeReadableWithDifferentTextureSizesWorksCorrectly(int size)
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            CreateTestTexture(readable: false, width: size, height: size);

            ExecuteWithImmediateImport(() =>
            {
                _testTexture.MakeReadable();
                _testTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(_testTexturePath);
            });

            Assert.IsTrue(
                _testTexture.isReadable,
                $"Texture of size {size}x{size} should be readable"
            );

            _testTexture = null;
            _testTexturePath = null;
        }

        [Test]
        public void MakeReadableWithNonSquareTexturesWorksCorrectly()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            CreateTestTexture(readable: false, width: 64, height: 128);

            ExecuteWithImmediateImport(() =>
            {
                _testTexture.MakeReadable();
                _testTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(_testTexturePath);
            });

            Assert.IsTrue(_testTexture.isReadable, "Non-square texture should be readable");
            Assert.AreEqual(64, _testTexture.width);
            Assert.AreEqual(128, _testTexture.height);
        }

        [Test]
        public void MakeReadableWithMinimalSizeTextureWorksCorrectly()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            CreateTestTexture(readable: false, width: 1, height: 1);

            ExecuteWithImmediateImport(() =>
            {
                _testTexture.MakeReadable();
                _testTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(_testTexturePath);
            });

            Assert.IsTrue(_testTexture.isReadable, "1x1 texture should be readable");
        }

        [Test]
        public void MakeReadableDoesNotAffectOtherImporterSettings()
        {
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }

            CreateTestTexture(readable: false);
            string assetPath = AssetDatabase.GetAssetPath(_testTexture);
            TextureImporter importerBefore = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            TextureImporterType originalType = importerBefore.textureType;
            FilterMode originalFilterMode = importerBefore.filterMode;
            int originalAnisoLevel = importerBefore.anisoLevel;

            _testTexture.MakeReadable();

            TextureImporter importerAfter = AssetImporter.GetAtPath(assetPath) as TextureImporter;

            Assert.AreEqual(
                originalType,
                importerAfter.textureType,
                "Texture type should not change"
            );
            Assert.AreEqual(
                originalFilterMode,
                importerAfter.filterMode,
                "Filter mode should not change"
            );
            Assert.AreEqual(
                originalAnisoLevel,
                importerAfter.anisoLevel,
                "Aniso level should not change"
            );
        }

        private void CreateTestTexture(bool readable, int width = 4, int height = 4)
        {
            _testTexturePath = Path.Combine(TestFolder, $"TestTexture_{System.Guid.NewGuid()}.png")
                .SanitizePath();

            Texture2D tempTexture = Track(
                new Texture2D(width, height, TextureFormat.RGBA32, false)
            );
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(
                    (float)i / pixels.Length,
                    (float)(pixels.Length - i) / pixels.Length,
                    0.5f,
                    1.0f
                );
            }
            tempTexture.SetPixels(pixels);
            tempTexture.Apply();

            byte[] pngData = tempTexture.EncodeToPNG();

            File.WriteAllBytes(_testTexturePath, pngData);

            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.ImportAsset(_testTexturePath);

                TextureImporter importer =
                    AssetImporter.GetAtPath(_testTexturePath) as TextureImporter;
                if (importer != null)
                {
                    importer.isReadable = readable;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }

                _testTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(_testTexturePath);
            });
        }
    }
}
