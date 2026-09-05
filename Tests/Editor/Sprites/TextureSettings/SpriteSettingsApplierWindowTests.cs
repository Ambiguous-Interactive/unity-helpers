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
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Tests for <see cref="SpriteSettingsApplierWindow"/> that verify directory-based
    /// texture searching and settings application work correctly.
    /// </summary>
    /// <remarks>
    /// These tests specifically cover edge cases related to array pooling when
    /// passing directory arrays to AssetDatabase.FindAssets.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpriteSettingsApplierWindowTests : BatchedEditorTestBase
    {
        private const string Root = "Assets/Temp/SpriteSettingsApplierWindowTests";

        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            EnsureFolder(Root);
            TrackFolder(Root);
        }

        [Test]
        public void GetMatchingFilePathsWithSingleDirectorySucceeds()
        {
            string dir = (Root + "/SingleDir").SanitizePath();
            EnsureFolder(dir);
            string texPath = (dir + "/sprite.png").SanitizePath();
            CreatePng(texPath, 8, 8, Color.white);

            ExecuteWithImmediateImport(() =>
            {
                TextureImporter imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
                Assert.IsTrue(imp != null, $"Expected importer at path '{texPath}' to not be null");
                imp.textureType = TextureImporterType.Sprite;
                imp.SaveAndReimport();
            });

            SpriteSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<SpriteSettingsApplierWindow>()
            );

            window.sprites = new List<Sprite>();
            window.directories = new List<Object> { AssetDatabase.LoadAssetAtPath<Object>(dir) };
            window.spriteFileExtensions = new List<string> { ".png" };

            Assert.DoesNotThrow(
                () => window.CalculateStats(),
                "CalculateStats with single directory should not throw"
            );
        }

        /// <summary>
        /// Pins the pooled-array defect: SystemArrayPool handed back an array larger than the
        /// request, so the trailing nulls reached AssetDatabase.FindAssets.
        /// </summary>
        [Test]
        public void GetMatchingFilePathsWithMultipleDirectoriesSucceeds()
        {
            string[] dirs = new string[4];
            string[] textures = new string[4];

            for (int i = 0; i < dirs.Length; i++)
            {
                dirs[i] = (Root + "/MultiDir" + i).SanitizePath();
                EnsureFolder(dirs[i]);
                textures[i] = (dirs[i] + "/sprite" + i + ".png").SanitizePath();
                CreatePng(textures[i], 4, 4, Color.white);
            }

            ExecuteWithImmediateImport(() =>
            {
                foreach (string texturesElement in textures)
                {
                    TextureImporter imp =
                        AssetImporter.GetAtPath(texturesElement) as TextureImporter;
                    Assert.IsTrue(
                        imp != null,
                        $"Expected importer at path '{texturesElement}' to not be null"
                    );
                    imp.textureType = TextureImporterType.Sprite;
                    imp.SaveAndReimport();
                }
            });

            SpriteSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<SpriteSettingsApplierWindow>()
            );

            window.sprites = new List<Sprite>();
            window.directories = new List<Object>();

            foreach (string dirsElement in dirs)
            {
                Object dirAsset = AssetDatabase.LoadAssetAtPath<Object>(dirsElement);
                Assert.IsTrue(
                    dirAsset != null,
                    $"Expected directory asset at '{dirsElement}' to be loaded"
                );
                window.directories.Add(dirAsset);
            }

            window.spriteFileExtensions = new List<string> { ".png" };

            Assert.DoesNotThrow(
                () => window.CalculateStats(),
                "CalculateStats with multiple directories should not throw NullReferenceException"
            );
        }

        [Test]
        public void GetMatchingFilePathsWithEmptyDirectoriesListSucceeds()
        {
            string texPath = (Root + "/solo.png").SanitizePath();
            CreatePng(texPath, 8, 8, Color.white);

            Sprite sprite = null;
            ExecuteWithImmediateImport(() =>
            {
                TextureImporter imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
                Assert.IsTrue(imp != null, $"Expected importer at path '{texPath}' to not be null");
                imp.textureType = TextureImporterType.Sprite;
                imp.SaveAndReimport();

                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
            });
            Assert.IsTrue(sprite != null, $"Expected sprite at path '{texPath}' to not be null");

            SpriteSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<SpriteSettingsApplierWindow>()
            );

            window.sprites = new List<Sprite> { sprite };
            window.directories = new List<Object>();

            Assert.DoesNotThrow(
                () => window.CalculateStats(),
                "CalculateStats with empty directories list should not throw"
            );
        }

        [Test]
        public void GetMatchingFilePathsWithNullDirectoryEntriesSucceeds()
        {
            string dir = (Root + "/ValidDir").SanitizePath();
            EnsureFolder(dir);
            string texPath = (dir + "/valid.png").SanitizePath();
            CreatePng(texPath, 8, 8, Color.white);

            ExecuteWithImmediateImport(() =>
            {
                TextureImporter imp = AssetImporter.GetAtPath(texPath) as TextureImporter;
                Assert.IsTrue(imp != null, $"Expected importer at path '{texPath}' to not be null");
                imp.textureType = TextureImporterType.Sprite;
                imp.SaveAndReimport();
            });

            SpriteSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<SpriteSettingsApplierWindow>()
            );

            window.sprites = new List<Sprite>();
            window.directories = new List<Object>
            {
                null,
                AssetDatabase.LoadAssetAtPath<Object>(dir),
                null,
            };
            window.spriteFileExtensions = new List<string> { ".png" };

            Assert.DoesNotThrow(
                () => window.CalculateStats(),
                "CalculateStats with null directory entries should not throw"
            );
        }

        [Test]
        public void GetMatchingFilePathsWithEmptyDirectorySucceeds()
        {
            string emptyDir = (Root + "/EmptyDir").SanitizePath();
            EnsureFolder(emptyDir);

            SpriteSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<SpriteSettingsApplierWindow>()
            );

            window.sprites = new List<Sprite>();
            window.directories = new List<Object>
            {
                AssetDatabase.LoadAssetAtPath<Object>(emptyDir),
            };
            window.spriteFileExtensions = new List<string> { ".png" };

            Assert.DoesNotThrow(
                () => window.CalculateStats(),
                "CalculateStats with empty directory should not throw"
            );
        }

        private void CreatePng(string relPath, int w, int h, Color c)
        {
            EnsureFolder(Path.GetDirectoryName(relPath).SanitizePath());
            Texture2D t = new(w, h, TextureFormat.RGBA32, false);
            Color[] pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++)
            {
                pix[i] = c;
            }

            t.SetPixels(pix);
            t.Apply();
            File.WriteAllBytes(RelToFull(relPath), t.EncodeToPNG());
        }

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
    }
#endif
}
