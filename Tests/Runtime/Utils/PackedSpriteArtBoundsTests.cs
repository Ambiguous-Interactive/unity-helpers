// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.U2D;
    using UnityEngine;
    using UnityEngine.U2D;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Integration")]
    public sealed class PackedSpriteArtBoundsTests : CommonTestBase
    {
        private static readonly RestorableGlobal<SpritePackerMode> PackingMode = new(
            () => EditorSettings.spritePackerMode,
            value => EditorSettings.spritePackerMode = value
        );

        [TearDown]
        public override void TearDown()
        {
            CleanupTrackedFoldersAndAssets();
            base.TearDown();
        }

        [TestCase(1, true, false)]
        [TestCase(255, true, false)]
        [TestCase(1, false, false)]
        [TestCase(1, true, true)]
        public void ReadsSupportedAtlasSpritesWithoutChangingSourceReadability(
            int threshold,
            bool readable,
            bool tight
        )
        {
            using RestorableGlobal<SpritePackerMode>.Scope scope = PackingMode.Borrow(
                SpritePackerMode.AlwaysOnAtlas
            );
            string folder = "Assets/Temp/PackedSpriteArtBounds" + Guid.NewGuid().ToString("N");
            EnsureFolder(folder);
            Texture2D source = Track(new Texture2D(16, 16, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[256];
            pixels[4 * 16 + 3] = new Color32(255, 255, 255, 1);
            pixels[9 * 16 + 10] = new Color32(255, 255, 255, 255);
            source.SetPixels32(pixels);
            source.Apply();
            string path = folder + "/Source.png";
            TrackAssetPath(path);
            File.WriteAllBytes(path, source.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
            importer.spritePixelsPerUnit = 4;
            importer.spritePivot = new Vector2(0.25f, 0.75f);
            TextureImporterSettings importSettings = new();
            importer.ReadTextureSettings(importSettings);
            importSettings.spriteMeshType = SpriteMeshType.FullRect;
            importSettings.spriteAlignment = (int)SpriteAlignment.Custom;
            importer.SetTextureSettings(importSettings);
            importer.SaveAndReimport();
            Sprite original = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsTrue(original != null);
            string before = EditorJsonUtility.ToJson(importer);
            SpriteAtlas atlas = Track(new SpriteAtlas());
            string atlasPath = folder + "/Art.spriteatlas";
            TrackAssetPath(atlasPath);
            AssetDatabase.CreateAsset(atlas, atlasPath);
            SpriteAtlasPackingSettings packing = atlas.GetPackingSettings();
            packing.enableTightPacking = tight;
            packing.enableRotation = false;
            atlas.SetPackingSettings(packing);
            SpriteAtlasTextureSettings texture = atlas.GetTextureSettings();
            texture.readable = readable;
            atlas.SetTextureSettings(texture);
            TextureImporterPlatformSettings platform = atlas.GetPlatformSettings(
                "DefaultTexturePlatform"
            );
            platform.format = TextureImporterFormat.RGBA32;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            atlas.SetPlatformSettings(platform);
            atlas.Add(new UnityEngine.Object[] { original });
            SpriteAtlasUtility.PackAtlases(
                new[] { atlas },
                EditorUserBuildSettings.activeBuildTarget
            );
            Sprite packed = Track(atlas.GetSprite(original.name));
            Assert.IsTrue(packed != null);
            Assert.IsTrue(packed.packed);
            Assert.AreEqual(
                tight ? SpritePackingMode.Tight : SpritePackingMode.Rectangle,
                packed.packingMode
            );
            Assert.AreEqual(SpritePackingRotation.None, packed.packingRotation);
            Assert.AreEqual(readable, packed.texture.isReadable);
            if (!readable || tight)
            {
                Assert.IsFalse(
                    SpriteArtBounds.TryGetLocalArtBounds(
                        packed,
                        out Bounds unavailable,
                        out string reason
                    )
                );
                Assert.AreEqual(default(Bounds), unavailable);
                Assert.That(reason, Does.Contain("readable, rectangular, unrotated atlas"));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(importer));
                Assert.IsFalse(importer.isReadable);
                return;
            }
            Assert.IsTrue(
                SpriteArtBounds.TryGetLocalArtBounds(
                    packed,
                    out Bounds bounds,
                    out string error,
                    (byte)threshold
                ),
                error
            );
            Vector3 minimum =
                threshold == 1 ? new Vector3(-0.25f, -2f, 0f) : new Vector3(1.5f, -0.75f, 0f);
            Assert.That(Vector3.Distance(minimum, bounds.min), Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(new Vector3(1.75f, -0.5f, 0f), bounds.max),
                Is.LessThan(0.0001f)
            );
            Assert.AreEqual(before, EditorJsonUtility.ToJson(importer));
            Assert.IsFalse(importer.isReadable);
        }
    }
#endif
}
