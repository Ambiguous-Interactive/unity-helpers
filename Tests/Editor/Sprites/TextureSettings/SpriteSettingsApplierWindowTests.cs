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

        private static List<SpriteSettings> CreateSpriteProfile()
        {
            return new List<SpriteSettings>
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    applyTextureType = true,
                    textureType = TextureImporterType.Sprite,
                },
            };
        }

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

        [Test]
        public void BatchApplyPersistsTwoSpritesAndReportsNoOp()
        {
            CreateDefaultTextures(
                nameof(BatchApplyPersistsTwoSpritesAndReportsNoOp),
                out string firstPath,
                out string secondPath
            );
            List<string> paths = new() { firstPath.Replace('/', '\\'), secondPath };
            List<SpriteSettings> profiles = CreateSpriteProfile();

            ExecuteWithImmediateImport(() =>
            {
                Assert.IsTrue(
                    SpriteSettingsApplierAPI.TryApplyProfiles(
                        paths,
                        profiles,
                        out int changed,
                        out bool canceled,
                        out string error
                    ),
                    error
                );
                Assert.AreEqual(2, changed);
                Assert.IsFalse(canceled);
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Sprite>(firstPath) != null);
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Sprite>(secondPath) != null);

                Assert.IsTrue(
                    SpriteSettingsApplierAPI.TryApplyProfiles(
                        paths,
                        profiles,
                        out changed,
                        out canceled,
                        out error
                    ),
                    error
                );
                Assert.AreEqual(0, changed);
                Assert.IsFalse(canceled);
            });
        }

        [Test]
        public void BatchCancellationPersistsOnlyCompletedSprite()
        {
            CreateDefaultTextures(
                nameof(BatchCancellationPersistsOnlyCompletedSprite),
                out string firstPath,
                out string secondPath
            );
            List<string> paths = new() { firstPath, secondPath };
            List<SpriteSettings> profiles = CreateSpriteProfile();

            ExecuteWithImmediateImport(() =>
            {
                Assert.IsTrue(
                    SpriteSettingsApplierAPI.TryApplyProfiles(
                        paths,
                        profiles,
                        out int changed,
                        out bool canceled,
                        out string error,
                        cancelRequested: (index, total, path) => index == 1
                    ),
                    error
                );
                Assert.AreEqual(1, changed);
                Assert.IsTrue(canceled);
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Sprite>(firstPath) != null);
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Sprite>(secondPath) == null);
            });
        }

        [Test]
        public void BatchCallbackFailureReportsPartialResultAndCompletes()
        {
            CreateDefaultTextures(
                nameof(BatchCallbackFailureReportsPartialResultAndCompletes),
                out string firstPath,
                out string secondPath
            );
            List<string> paths = new() { firstPath, secondPath };
            List<SpriteSettings> profiles = CreateSpriteProfile();
            bool completed = false;

            ExecuteWithImmediateImport(() =>
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TryApplyProfiles(
                        paths,
                        profiles,
                        out int changed,
                        out bool canceled,
                        out string error,
                        cancelRequested: (index, total, path) =>
                            index == 1
                                ? throw new System.InvalidOperationException("callback failed")
                                : false,
                        beforeReimport: () => completed = true
                    )
                );
                Assert.AreEqual(1, changed);
                Assert.IsFalse(canceled);
                Assert.IsTrue(completed);
                StringAssert.Contains("callback failed", error);
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Sprite>(firstPath) != null);
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Sprite>(secondPath) == null);
            });
        }

        [Test]
        public void BatchApplyRejectsMissingInputs()
        {
            List<SpriteSettings> profiles = CreateSpriteProfile();
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TryApplyProfiles(
                    null,
                    profiles,
                    out int changed,
                    out bool canceled,
                    out string error
                )
            );
            Assert.AreEqual(0, changed);
            Assert.IsFalse(canceled);
            Assert.IsNotEmpty(error);

            Assert.IsFalse(
                SpriteSettingsApplierAPI.TryApplyProfiles(
                    new List<string>(),
                    null,
                    out changed,
                    out canceled,
                    out error
                )
            );
            Assert.AreEqual(0, changed);
            Assert.IsFalse(canceled);
            Assert.IsNotEmpty(error);
        }

        private void CreateDefaultTextures(
            string prefix,
            out string firstPath,
            out string secondPath
        )
        {
            firstPath = (Root + "/" + prefix + "_first.png").SanitizePath();
            secondPath = (Root + "/" + prefix + "_second.png").SanitizePath();
            string localFirstPath = firstPath;
            string localSecondPath = secondPath;
            ExecuteWithImmediateImport(() =>
            {
                CreatePng(localFirstPath, 8, 8, Color.white);
                CreatePng(localSecondPath, 8, 8, Color.white);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (string path in new[] { localFirstPath, localSecondPath })
                {
                    TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    Assert.IsTrue(importer != null);
                    if (importer.textureType != TextureImporterType.Default)
                    {
                        importer.textureType = TextureImporterType.Default;
                        importer.SaveAndReimport();
                    }
                }
            });
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
    }
#endif
}
