// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.U2D;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.U2D;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Integration")]
    public sealed class SpriteMaskReaderEditorTests : CommonTestBase
    {
        private const string AlphaTexturePropertyPath = "m_RD.alphaTexture";

        private static readonly RestorableGlobal<SpritePackerMode> PackingMode = new(
            () => EditorSettings.spritePackerMode,
            value => EditorSettings.spritePackerMode = value
        );

        private static readonly RestorableGlobal<EnterPlayModeOptions> PlayModeOptions = new(
            () => EditorSettings.enterPlayModeOptions,
            value => EditorSettings.enterPlayModeOptions = value
        );

        private static readonly RestorableGlobal<bool> PlayModeOptionsEnabled = new(
            () => EditorSettings.enterPlayModeOptionsEnabled,
            value => EditorSettings.enterPlayModeOptionsEnabled = value
        );

        private string _folder;
        private RestorableGlobal<SpritePackerMode>.Scope _atlasPackingScope;
        private RestorableGlobal<EnterPlayModeOptions>.Scope _atlasPlayModeOptionsScope;
        private RestorableGlobal<bool>.Scope _atlasPlayModeOptionsEnabledScope;
        private bool _atlasPlayModeRequested;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _folder = "Assets/Temp/SpriteMaskReaderTests" + Guid.NewGuid().ToString("N");
            EnsureFolder(_folder);
        }

        [TearDown]
        public override void TearDown()
        {
            CleanupTrackedFoldersAndAssets();
            base.TearDown();
        }

        [UnityTearDown]
        public IEnumerator ReturnFromAtlasPlayMode()
        {
            try
            {
                if (_atlasPlayModeRequested && EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    yield return new ExitPlayMode();
                }
            }
            finally
            {
                _atlasPlayModeRequested = false;
                _atlasPackingScope.Dispose();
                _atlasPlayModeOptionsScope.Dispose();
                _atlasPlayModeOptionsEnabledScope.Dispose();
            }
        }

        [Test]
        public void SeparateAlphaTexturesFailInsteadOfMeasuringOpaqueColorPixels()
        {
            Texture2D color = Track(new Texture2D(2, 2, TextureFormat.RGB24, false));
            color.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            color.Apply();
            Texture2D alpha = Track(new Texture2D(2, 2, TextureFormat.RGB24, false));
            alpha.SetPixels(new[] { Color.black, Color.black, Color.black, Color.white });
            alpha.Apply();
            Sprite sprite = Track(Sprite.Create(color, new Rect(0, 0, 2, 2), Vector2.zero, 1f));
            using SerializedObject serialized = new(sprite);
            using SerializedProperty alphaProperty = serialized.FindProperty(
                AlphaTexturePropertyPath
            );
            Assert.IsTrue(alphaProperty != null);
            alphaProperty.objectReferenceValue = alpha;
            Assert.IsTrue(serialized.ApplyModifiedPropertiesWithoutUndo());
            Assert.AreSame(alpha, sprite.associatedAlphaSplitTexture);
            Assert.AreSame(color, sprite.texture);
            Assert.IsTrue(sprite.texture.isReadable);
            Assert.AreEqual(255, color.GetPixels32()[0].a);
            Assert.AreEqual(0, alpha.GetPixels32()[0].r);
            Assert.AreEqual(255, alpha.GetPixels32()[3].r);
            Assert.IsFalse(sprite.packed);
            Assert.IsFalse(
                SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out string readError)
            );
            Assert.IsTrue(mask.Pixels == null);
            Assert.That(readError, Does.Contain("separate alpha"));
            Assert.IsFalse(
                SpriteArtBounds.TryGetLocalArtBounds(sprite, out Bounds bounds, out string error)
            );
            Assert.AreEqual(default(Bounds), bounds);
            Assert.That(error, Does.Contain("separate alpha"));
        }

        [TestCase(false, 64)]
        [TestCase(true, 64)]
        [TestCase(false, 32)]
        public void ReadsOriginalPngWithoutChangingImporter(bool readable, int maximumSize)
        {
            string path = CreatePng();
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = readable;
            importer.maxTextureSize = maximumSize;
            importer.spritePixelsPerUnit = 16;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsTrue(sprite != null);
            string before = EditorJsonUtility.ToJson(importer);
            Assert.IsTrue(
                SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out string error),
                error
            );
            Assert.AreEqual(64, mask.Width);
            Assert.AreEqual(64, mask.Height);
            Assert.IsTrue(mask.Pixels[17 * 64 + 19]);
            Assert.IsFalse(mask.Pixels[17 * 64 + 18]);
            Assert.AreEqual(
                sprite.rect.width / sprite.pixelsPerUnit,
                mask.Width * mask.UnitsPerPixel.x,
                0.0001f
            );
            Assert.AreEqual(
                sprite.rect.height / sprite.pixelsPerUnit,
                mask.Height * mask.UnitsPerPixel.y,
                0.0001f
            );
            Assert.AreEqual(
                sprite.pivot.x / sprite.pixelsPerUnit,
                mask.Pivot.x * mask.UnitsPerPixel.x,
                0.0001f
            );
            Assert.IsTrue(
                SpriteArtBounds.TryGetLocalArtBounds(sprite, out Bounds art, out error),
                error
            );
            float unitX = sprite.rect.width / (64f * sprite.pixelsPerUnit);
            float unitY = sprite.rect.height / (64f * sprite.pixelsPerUnit);
            Assert.That(
                art.min.x,
                Is.EqualTo(19f * unitX - sprite.pivot.x / sprite.pixelsPerUnit).Within(0.0001f)
            );
            Assert.That(
                art.min.y,
                Is.EqualTo(17f * unitY - sprite.pivot.y / sprite.pixelsPerUnit).Within(0.0001f)
            );
            Assert.That(art.size.x, Is.EqualTo(unitX).Within(0.0001f));
            Assert.That(art.size.y, Is.EqualTo(unitY).Within(0.0001f));
            Assert.AreEqual(before, EditorJsonUtility.ToJson(importer));
            Assert.AreEqual(readable, importer.isReadable);
        }

        [Test]
        public void ReadsOnlySelectedSubspriteWithItsOwnPivot()
        {
            string path = CreatePng();
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
            importer.spritePixelsPerUnit = 16;
#pragma warning disable CS0618
            importer.spritesheet = new[]
            {
                new SpriteMetaData
                {
                    name = "First",
                    rect = new Rect(16, 16, 16, 16),
                    alignment = 9,
                    pivot = new Vector2(0.25f, 0.75f),
                },
                new SpriteMetaData
                {
                    name = "Second",
                    rect = new Rect(32, 32, 16, 16),
                    alignment = 9,
                    pivot = Vector2.zero,
                },
            };
#pragma warning restore CS0618
            importer.SaveAndReimport();
            int count = 0;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (asset is not Sprite sprite)
                {
                    continue;
                }
                ++count;
                Assert.IsTrue(
                    SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out string error),
                    error
                );
                Assert.AreEqual(16, mask.Width);
                Assert.AreEqual(16, mask.Height);
                Assert.AreEqual(sprite.pivot, mask.Pivot);
                int opaque = 0;
                foreach (bool pixel in mask.Pixels)
                {
                    if (pixel)
                    {
                        ++opaque;
                    }
                }
                Assert.AreEqual(
                    string.Equals(sprite.name, "First", System.StringComparison.Ordinal) ? 1 : 0,
                    opaque
                );
            }
            Assert.AreEqual(2, count);
            Assert.IsFalse(importer.isReadable);
        }

        [TestCase(TextureImporterAlphaSource.FromInput, false)]
        [TestCase(TextureImporterAlphaSource.FromInput, true)]
        [TestCase(TextureImporterAlphaSource.FromGrayScale, false)]
        [TestCase(TextureImporterAlphaSource.FromGrayScale, true)]
        [TestCase(TextureImporterAlphaSource.None, false)]
        [TestCase(TextureImporterAlphaSource.None, true)]
        public void ReadsImportedAlphaSourceWithoutChangingImporter(
            TextureImporterAlphaSource alphaSource,
            bool readable
        )
        {
            string path = CreatePng();
            if (alphaSource == TextureImporterAlphaSource.FromGrayScale)
            {
                Texture2D source = Track(new Texture2D(64, 64, TextureFormat.RGBA32, false));
                Color32[] pixels = new Color32[64 * 64];
                for (int index = 0; index < pixels.Length; ++index)
                {
                    pixels[index] = new Color32(0, 0, 0, 255);
                }
                pixels[17 * 64 + 19] = new Color32(255, 255, 255, 255);
                source.SetPixels32(pixels);
                source.Apply();
                File.WriteAllBytes(path, source.EncodeToPNG());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = 16;
            importer.alphaSource = alphaSource;
            importer.isReadable = readable;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsTrue(sprite != null);
            string before = EditorJsonUtility.ToJson(importer);
            Assert.IsTrue(
                SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out string error),
                error
            );
            bool fullRectangle = alphaSource == TextureImporterAlphaSource.None;
            Assert.AreEqual(fullRectangle, mask.Pixels[0]);
            Assert.IsTrue(mask.Pixels[17 * 64 + 19]);
            if (readable)
            {
                Color32[] imported = sprite.texture.GetPixels32();
                Assert.AreEqual(fullRectangle ? 255 : 0, imported[0].a);
                Assert.AreEqual(255, imported[17 * 64 + 19].a);
            }
            Assert.IsTrue(
                SpriteArtBounds.TryGetLocalArtBounds(sprite, out Bounds actual, out error),
                error
            );
            Bounds expected = fullRectangle
                ? new Bounds(Vector3.zero, new Vector3(4, 4, 0))
                : new Bounds(
                    new Vector3(-0.78125f, -0.90625f, 0),
                    new Vector3(0.0625f, 0.0625f, 0)
                );
            Assert.That(Vector3.Distance(expected.center, actual.center), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(expected.size, actual.size), Is.LessThan(0.0001f));
            Assert.AreEqual(before, EditorJsonUtility.ToJson(importer));
            Assert.AreEqual(readable, importer.isReadable);
        }

        [TestCase((byte)1)]
        [TestCase((byte)85)]
        [TestCase((byte)86)]
        [TestCase((byte)254)]
        [TestCase((byte)255)]
        public void GrayscaleMaskThresholdsMatchImportedPixels(byte threshold)
        {
            string path = CreatePng();
            Texture2D source = Track(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            Color32[] colors = new Color32[64 * 64];
            colors[17 * 64 + 1] = new Color32(0, 0, 1, 255);
            colors[17 * 64 + 2] = new Color32(0, 0, 2, 255);
            colors[17 * 64 + 3] = new Color32(0, 0, 3, 255);
            colors[17 * 64 + 4] = new Color32(255, 0, 0, 255);
            colors[17 * 64 + 5] = new Color32(255, 255, 254, 255);
            colors[17 * 64 + 6] = new Color32(255, 255, 255, 255);
            source.SetPixels32(colors);
            source.Apply();
            File.WriteAllBytes(path, source.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.alphaSource = TextureImporterAlphaSource.FromGrayScale;
            importer.isReadable = true;
            importer.SaveAndReimport();
            Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsTrue(imported != null);
            Color32[] expected = imported.GetPixels32();
            importer.isReadable = false;
            importer.SaveAndReimport();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsTrue(sprite != null);
            string before = EditorJsonUtility.ToJson(importer);
            Assert.IsTrue(
                SpriteMaskReader.TryRead(
                    sprite,
                    out SpriteAlphaMask mask,
                    out string error,
                    threshold
                ),
                error
            );
            Assert.AreEqual(expected.Length, mask.Pixels.Length);
            for (int index = 0; index < expected.Length; ++index)
            {
                byte alpha = expected[index].a;
                Assert.AreEqual(
                    0 < alpha && threshold <= alpha,
                    mask.Pixels[index],
                    $"Pixel {index}, imported alpha {alpha}"
                );
            }
            Assert.AreEqual(before, EditorJsonUtility.ToJson(importer));
            Assert.IsFalse(importer.isReadable);
        }

        [UnityTest]
        public IEnumerator RectangularAtlasReadsKeepSourceImportSettingsAndReportUnavailablePixels()
        {
            return AtlasReadsKeepSourceImportSettingsAndReportUnavailablePixels(false);
        }

        [UnityTest]
        public IEnumerator TightAtlasReadsKeepSourceImportSettingsAndReportUnavailablePixels()
        {
            return AtlasReadsKeepSourceImportSettingsAndReportUnavailablePixels(true);
        }

        private IEnumerator AtlasReadsKeepSourceImportSettingsAndReportUnavailablePixels(bool tight)
        {
            _atlasPackingScope = PackingMode.Borrow(SpritePackerMode.SpriteAtlasV2);
            string path = CreatePng();
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
            importer.spritePixelsPerUnit = 16;
            importer.SaveAndReimport();
            Sprite original = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsTrue(original != null);
            Assert.IsTrue(
                SpriteArtBounds.TryGetLocalArtBounds(
                    original,
                    out Bounds expected,
                    out string error
                ),
                error
            );
            string before = EditorJsonUtility.ToJson(importer);
            SpriteAtlasAsset atlasSource = Track(new SpriteAtlasAsset());
            atlasSource.Add(new UnityEngine.Object[] { original });
            string atlasPath = _folder + "/Bounds.spriteatlasv2";
            TrackAssetPath(atlasPath);
#if !UNITY_2022_1_OR_NEWER
            SpriteAtlasPackingSettings packing = atlasSource.GetPackingSettings();
            packing.enableTightPacking = tight;
            packing.enableRotation = true;
            atlasSource.SetPackingSettings(packing);
            SpriteAtlasTextureSettings texture = atlasSource.GetTextureSettings();
            texture.readable = false;
            atlasSource.SetTextureSettings(texture);
#endif
            SpriteAtlasAsset.Save(atlasSource, atlasPath);
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
#if UNITY_2022_1_OR_NEWER
            SpriteAtlasImporter atlasImporter =
                AssetImporter.GetAtPath(atlasPath) as SpriteAtlasImporter;
            Assert.IsTrue(atlasImporter != null);
            SpriteAtlasPackingSettings packing = atlasImporter.packingSettings;
            packing.enableTightPacking = tight;
            packing.enableRotation = true;
            atlasImporter.packingSettings = packing;
            SpriteAtlasTextureSettings texture = atlasImporter.textureSettings;
            texture.readable = false;
            atlasImporter.textureSettings = texture;
            atlasImporter.SaveAndReimport();
#endif
#if !UNITY_2022_1_OR_NEWER
            DestroyTrackedObjects();
            _atlasPlayModeOptionsEnabledScope = PlayModeOptionsEnabled.Borrow(true);
            _atlasPlayModeOptionsScope = PlayModeOptions.Borrow(
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload
            );
            _atlasPlayModeRequested = true;
            yield return new EnterPlayMode(false);
#endif
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            Assert.IsTrue(atlas != null);
            Assert.IsTrue(atlas.CanBindTo(original));
            Sprite packed = Track(atlas.GetSprite(original.name));
            Assert.IsTrue(packed != null);
#if !UNITY_2022_1_OR_NEWER
            Assert.IsTrue(packed.packed);
            Texture2D atlasTexture = packed.texture;
#else
            Texture2D atlasTexture = UnityEditor.Sprites.SpriteUtility.GetSpriteTexture(
                packed,
                true
            );
#endif
            Assert.IsTrue(atlasTexture != null);
            Assert.IsFalse(atlasTexture.isReadable);
            bool measured = SpriteArtBounds.TryGetLocalArtBounds(
                packed,
                out Bounds actual,
                out error
            );
            if (measured)
            {
                Assert.That(Vector3.Distance(expected.center, actual.center), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(expected.size, actual.size), Is.LessThan(0.0001f));
            }
            else
            {
                Assert.AreEqual(default(Bounds), actual);
                Assert.IsFalse(string.IsNullOrWhiteSpace(error));
            }
            Assert.AreEqual(before, EditorJsonUtility.ToJson(importer));
            Assert.IsFalse(importer.isReadable);
            yield break;
        }

        private string CreatePng()
        {
            Texture2D texture = Track(new Texture2D(64, 64, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[64 * 64];
            pixels[17 * 64 + 19] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            string path = _folder + "/Source.png";
            TrackAssetPath(path);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return path;
        }
    }
#endif
}
