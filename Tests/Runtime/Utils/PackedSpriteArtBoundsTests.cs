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
    using UnityEngine.TestTools;
    using UnityEngine.U2D;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Integration")]
    public sealed class PackedSpriteArtBoundsTests
        : CommonTestBase,
            IPrebuildSetup,
            IPostBuildCleanup
    {
        private const string FolderPrefix = "Assets/Temp/" + nameof(PackedSpriteArtBoundsTests);
        private const string FolderKey =
            nameof(PackedSpriteArtBoundsTests) + "." + nameof(FolderKey);
        private const string ModeKey = nameof(PackedSpriteArtBoundsTests) + "." + nameof(ModeKey);

        private const string ParentCreatedKey =
            nameof(PackedSpriteArtBoundsTests) + "." + nameof(ParentCreatedKey);

        private static string CaseName(bool readable, bool tight)
        {
            return !readable ? "Unreadable"
                : tight ? "Tight"
                : "Rectangular";
        }

        private static void CreateAtlas(string folder, bool readable, bool tight)
        {
            string name = CaseName(readable, tight);
            AssetDatabase.CreateFolder(folder, name);
            string path = folder + "/" + name + "/Source.png";
            Texture2D source = new(16, 16, TextureFormat.RGBA32, false);
            try
            {
                Color32[] pixels = new Color32[256];
                pixels[4 * 16 + 3] = new Color32(255, 255, 255, 1);
                pixels[9 * 16 + 10] = new Color32(255, 255, 255, 255);
                source.SetPixels32(pixels);
                source.Apply();
                File.WriteAllBytes(path, source.EncodeToPNG());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source); // UNH-SUPPRESS: No NUnit teardown.
            }
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
            SpriteAtlasAsset atlasSource = new();
            string atlasPath = folder + "/" + name + "/Art.spriteatlasv2";
            try
            {
                atlasSource.Add(new UnityEngine.Object[] { original });
#if !UNITY_2022_1_OR_NEWER
                SpriteAtlasPackingSettings packing = atlasSource.GetPackingSettings();
                packing.enableTightPacking = tight;
                packing.enableRotation = false;
                atlasSource.SetPackingSettings(packing);
                SpriteAtlasTextureSettings texture = atlasSource.GetTextureSettings();
                texture.readable = readable;
                atlasSource.SetTextureSettings(texture);
                string platformName = BuildPipeline
                    .GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)
                    .ToString();
                TextureImporterPlatformSettings platform = atlasSource.GetPlatformSettings(
                    platformName
                );
                platform.overridden = true;
                platform.format = TextureImporterFormat.RGBA32;
                platform.textureCompression = TextureImporterCompression.Uncompressed;
                atlasSource.SetPlatformSettings(platform);
#endif
                SpriteAtlasAsset.Save(atlasSource, atlasPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(atlasSource); // UNH-SUPPRESS: No NUnit teardown.
            }
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceSynchronousImport);
#if UNITY_2022_1_OR_NEWER
            SpriteAtlasImporter atlasImporter =
                AssetImporter.GetAtPath(atlasPath) as SpriteAtlasImporter;
            Assert.IsTrue(atlasImporter != null);
            SpriteAtlasPackingSettings packing = atlasImporter.packingSettings;
            packing.enableTightPacking = tight;
            packing.enableRotation = false;
            atlasImporter.packingSettings = packing;
            SpriteAtlasTextureSettings texture = atlasImporter.textureSettings;
            texture.readable = readable;
            atlasImporter.textureSettings = texture;
            string platformName = BuildPipeline
                .GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)
                .ToString();
            TextureImporterPlatformSettings platform = atlasImporter.GetPlatformSettings(
                platformName
            );
            platform.overridden = true;
            platform.format = TextureImporterFormat.RGBA32;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            atlasImporter.SetPlatformSettings(platform);
            atlasImporter.SaveAndReimport();
#endif
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            Assert.IsTrue(atlas != null);
            Assert.IsTrue(atlas.CanBindTo(original));
        }

        public void Setup()
        {
            bool createParent = !AssetDatabase.IsValidFolder("Assets/Temp");
            SessionState.SetBool(ParentCreatedKey, createParent);
            if (createParent)
            {
                AssetDatabase.CreateFolder("Assets", "Temp");
            }
            string folder = FolderPrefix + Guid.NewGuid().ToString("N");
            SessionState.SetString(FolderKey, folder);
            SessionState.SetInt(ModeKey, (int)EditorSettings.spritePackerMode);
            EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2;
            AssetDatabase.CreateFolder("Assets/Temp", Path.GetFileName(folder));
            CreateAtlas(folder, true, false);
            CreateAtlas(folder, false, false);
            CreateAtlas(folder, true, true);
        }

        public void Cleanup()
        {
            try
            {
                string folder = SessionState.GetString(FolderKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Assert.IsTrue(folder.StartsWith(FolderPrefix, StringComparison.Ordinal));
                    if (AssetDatabase.IsValidFolder(folder))
                    {
                        Assert.IsTrue(AssetDatabase.DeleteAsset(folder));
                    }
                    SessionState.EraseString(FolderKey);
                }
                if (
                    SessionState.GetBool(ParentCreatedKey, false)
                    && AssetDatabase.IsValidFolder("Assets/Temp")
                )
                {
                    Assert.That(Directory.GetFileSystemEntries("Assets/Temp"), Is.Empty);
                    Assert.IsTrue(AssetDatabase.DeleteAsset("Assets/Temp"));
                }
                SessionState.EraseBool(ParentCreatedKey);
            }
            finally
            {
                int previousMode = SessionState.GetInt(ModeKey, -1);
                if (0 <= previousMode)
                {
                    EditorSettings.spritePackerMode = (SpritePackerMode)previousMode;
                    SessionState.EraseInt(ModeKey);
                }
            }
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
            string folder = SessionState.GetString(FolderKey, string.Empty);
            Assert.IsFalse(string.IsNullOrWhiteSpace(folder));
            string caseFolder = folder + "/" + CaseName(readable, tight);
            string path = caseFolder + "/Source.png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null);
            Assert.IsFalse(importer.isReadable);
            Texture2D sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsTrue(sourceTexture != null);
            Assert.IsFalse(sourceTexture.isReadable);
            string before = EditorJsonUtility.ToJson(importer);
            Sprite original = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Assert.IsTrue(original != null);
            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(
                caseFolder + "/Art.spriteatlasv2"
            );
            Assert.IsTrue(atlas != null);
            Sprite packed = Track(atlas.GetSprite(original.name));
            Assert.IsTrue(packed != null);
            Assert.IsTrue(packed.packed);
            Assert.AreEqual(
                tight ? SpritePackingMode.Tight : SpritePackingMode.Rectangle,
                packed.packingMode
            );
            Assert.AreEqual(SpritePackingRotation.None, packed.packingRotation);
            Assert.AreEqual(readable, packed.texture.isReadable);
            Assert.IsFalse(
                SpriteMaskReader.TryRead(
                    packed,
                    out SpriteAlphaMask directMask,
                    out string directError
                )
            );
            Assert.IsTrue(directMask.Pixels == null);
            Assert.IsFalse(string.IsNullOrWhiteSpace(directError));
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
            }
            else
            {
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
            }
            Assert.AreEqual(before, EditorJsonUtility.ToJson(importer));
            Assert.IsFalse(importer.isReadable);
        }
    }
#endif
}
