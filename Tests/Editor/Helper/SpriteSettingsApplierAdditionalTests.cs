// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpriteSettingsApplierAdditionalTests : BatchedEditorTestBase
    {
        private const string TestFolder = "Assets/TempSpriteApplierAdditional";
        private string _assetPath;

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
            if (Application.isPlaying)
            {
                Assert.Ignore("AssetDatabase access requires edit mode.");
            }
            // Reset per-test state
            _assetPath = null;
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            // Per-test cleanup: track individual asset paths for deferred cleanup
            if (!string.IsNullOrEmpty(_assetPath))
            {
                TrackAssetPath(_assetPath);
            }
        }

        private string CreatePng(string name, bool asSprite)
        {
            Texture2D tex = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            byte[] png = tex.EncodeToPNG();
            string path = Path.Combine(TestFolder, name + ".png");
            File.WriteAllBytes(path, png);

            ExecuteWithImmediateImport(() =>
            {
                AssetDatabase.ImportAsset(path);
                TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(ti != null, "Importer not found for asset path: " + path);
                if (asSprite)
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.SaveAndReimport();
                }
            });
            return path;
        }

        [Test]
        public void DetectsChangeForNameContainsWithPriority()
        {
            string path = CreatePng("ui_button", asSprite: true);
            _assetPath = path;

            // Set initial filter mode to Point (different from what the higher-priority profile wants)
            // Unity's default is Bilinear, so we need to explicitly set a different value
            // to ensure WillTextureSettingsChange detects a change
            ExecuteWithImmediateImport(() =>
            {
                TextureImporter initialImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(initialImporter != null, "Initial importer not found");
                initialImporter.filterMode = FilterMode.Point;
                initialImporter.SaveAndReimport();
            });

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "ui_",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Point,
                },
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "button",
                    priority = 10,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(path, prepared);
            Assert.IsTrue(
                willChange,
                "Expected detection when matching profile has apply flags and initial filter mode differs. Path="
                    + path
            );

            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter importer
            );
            Assert.IsTrue(
                changed,
                "Expected TryUpdateTextureSettings to apply settings. Path=" + path
            );
            Assert.IsTrue(importer != null, "Expected non-null importer after change");
            ExecuteWithImmediateImport(() => importer.SaveAndReimport());
            Assert.AreEqual(
                FilterMode.Bilinear,
                importer.filterMode,
                "Expected higher-priority filter mode to win"
            );
        }

        [Test]
        public void DetectsChangeByExtensionAndEnforcesTextureType()
        {
            string path = CreatePng("any_name", asSprite: false);
            _assetPath = path;

            // Explicitly set texture type to Default to ensure it differs from the profile's Sprite type
            ExecuteWithImmediateImport(() =>
            {
                TextureImporter initialImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsTrue(initialImporter != null, "Initial importer not found");
                initialImporter.textureType = TextureImporterType.Default;
                initialImporter.SaveAndReimport();
            });

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Extension,
                    matchPattern = ".png",
                    priority = 5,
                    applyTextureType = true,
                    textureType = TextureImporterType.Sprite,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(path, prepared);
            Assert.IsTrue(willChange, "Expected change detection by extension for path: " + path);

            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter importer
            );
            Assert.IsTrue(changed, "Expected importer to be updated for path: " + path);
            Assert.IsTrue(importer != null, "Importer was null after update for path: " + path);
            ExecuteWithImmediateImport(() => importer.SaveAndReimport());
            Assert.AreEqual(
                TextureImporterType.Sprite,
                importer.textureType,
                "Expected texture type to be enforced"
            );
        }

        [Test]
        public void DetectsChangeWithBackslashPath()
        {
            string fwd = CreatePng("named_for_backslash", asSprite: true);
            _assetPath = fwd;
            string back = fwd.Replace('/', '\\');

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "backslash",
                    priority = 3,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(back, prepared);
            Assert.IsTrue(willChange, "Expected detection for Windows-style path: " + back);
        }

        [Test]
        public void TryUpdateTextureSettingsReturnsFalseWhenAllSettingsMatch()
        {
            string path = CreatePng("try_update_all_match", asSprite: true);
            _assetPath = path;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null, "Importer not found");
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.spritePixelsPerUnit = 32;
            ExecuteWithImmediateImport(() => importer.SaveAndReimport());

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                    applyWrapMode = true,
                    wrapMode = TextureWrapMode.Repeat,
                    applyPixelsPerUnit = true,
                    pixelsPerUnit = 32,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter outImporter
            );
            Assert.IsFalse(
                changed,
                "Expected no change when all configured settings match sprite settings"
            );
            Assert.IsTrue(outImporter != null, "Importer should still be returned");
        }

        [Test]
        public void TryUpdateTextureSettingsReturnsTrueWhenOnlyOneSettingDiffers()
        {
            string path = CreatePng("try_update_one_differs", asSprite: true);
            _assetPath = path;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null, "Importer not found");
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.spritePixelsPerUnit = 32;
            ExecuteWithImmediateImport(() => importer.SaveAndReimport());

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                    applyWrapMode = true,
                    wrapMode = TextureWrapMode.Clamp,
                    applyPixelsPerUnit = true,
                    pixelsPerUnit = 32,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter outImporter
            );
            Assert.IsTrue(
                changed,
                "Expected change when at least one configured setting differs from sprite"
            );
            Assert.IsTrue(outImporter != null, "Importer should be returned");
        }

        [Test]
        public void TryUpdateTextureSettingsReturnsFalseWhenNoApplyFlagsEnabled()
        {
            string path = CreatePng("try_update_no_flags", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = false,
                    filterMode = FilterMode.Trilinear,
                    applyWrapMode = false,
                    wrapMode = TextureWrapMode.Mirror,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter outImporter
            );
            Assert.IsFalse(
                changed,
                "Expected no change when no apply flags are enabled, regardless of configured values"
            );
            Assert.IsTrue(outImporter != null, "Importer should still be returned");
        }

        [Test]
        public void WillTextureSettingsChangeReturnsFalseWhenNoProfileMatches()
        {
            string path = CreatePng("nomatch_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "this_does_not_exist",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(path, prepared);
            Assert.IsFalse(willChange, "Expected no change when no profile matches the asset");
        }

        [Test]
        public void TryUpdateTextureSettingsReturnsFalseWhenNoProfileMatches()
        {
            string path = CreatePng("try_update_nomatch", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "this_does_not_exist",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter outImporter
            );
            Assert.IsFalse(changed, "Expected no change when no profile matches the asset");
            // The importer is still returned even when no profile matches,
            // allowing the caller to use it for other purposes if needed
            Assert.IsTrue(
                outImporter != null,
                "Importer should still be returned even when no profile matches"
            );
        }

        [Test]
        public void WillTextureSettingsChangeReturnsFalseForNullPath()
        {
            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(null, prepared);
            Assert.IsFalse(willChange, "Expected no change for null path");
        }

        [Test]
        public void WillTextureSettingsChangeReturnsFalseForEmptyPath()
        {
            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(
                string.Empty,
                prepared
            );
            Assert.IsFalse(willChange, "Expected no change for empty path");
        }

        [Test]
        public void WillTextureSettingsChangeReturnsFalseForNonExistentPath()
        {
            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(
                "Assets/NonExistent/fake.png",
                prepared
            );
            Assert.IsFalse(willChange, "Expected no change for non-existent path");
        }

        [Test]
        public void WillTextureSettingsChangeReturnsFalseForNullPreparedProfiles()
        {
            string path = CreatePng("null_profiles_test", asSprite: true);
            _assetPath = path;

            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(path, null);
            Assert.IsFalse(willChange, "Expected no change for null prepared profiles");
        }

        [Test]
        public void WillTextureSettingsChangeReturnsFalseForEmptyPreparedProfiles()
        {
            string path = CreatePng("empty_profiles_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(new List<SpriteSettings>());
            bool willChange = SpriteSettingsApplierAPI.WillTextureSettingsChange(path, prepared);
            Assert.IsFalse(willChange, "Expected no change for empty prepared profiles list");
        }

        [Test]
        public void TryUpdateTextureSettingsReturnsFalseForNullPath()
        {
            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                null,
                prepared,
                out TextureImporter outImporter
            );
            Assert.IsFalse(changed, "Expected no change for null path");
            Assert.IsTrue(outImporter == null, "Importer should be null for null path");
        }

        [Test]
        public void TryUpdateTextureSettingsReturnsFalseForEmptyPath()
        {
            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                string.Empty,
                prepared,
                out TextureImporter outImporter
            );
            Assert.IsFalse(changed, "Expected no change for empty path");
            Assert.IsTrue(outImporter == null, "Importer should be null for empty path");
        }

        [Test]
        public void TryUpdateTextureSettingsAppliesChangesCorrectly()
        {
            string path = CreatePng("apply_changes_test", asSprite: true);
            _assetPath = path;

            TextureImporter importerBefore = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importerBefore != null, "Importer not found");
            importerBefore.filterMode = FilterMode.Point;
            importerBefore.wrapMode = TextureWrapMode.Clamp;
            importerBefore.spritePixelsPerUnit = 100;
            ExecuteWithImmediateImport(() => importerBefore.SaveAndReimport());

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                    applyWrapMode = true,
                    wrapMode = TextureWrapMode.Mirror,
                    applyPixelsPerUnit = true,
                    pixelsPerUnit = 64,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter outImporter
            );
            Assert.IsTrue(changed, "Expected changes to be applied");
            Assert.IsTrue(outImporter != null, "Importer should be returned");
            ExecuteWithImmediateImport(() => outImporter.SaveAndReimport());

            TextureImporter importerAfter = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importerAfter != null, "Importer after not found");
            Assert.AreEqual(
                FilterMode.Trilinear,
                importerAfter.filterMode,
                "FilterMode should be updated"
            );
            Assert.AreEqual(
                TextureWrapMode.Mirror,
                importerAfter.wrapMode,
                "WrapMode should be updated"
            );
            Assert.AreEqual(64, importerAfter.spritePixelsPerUnit, "PPU should be updated");
        }

        [Test]
        public void TryUpdateTextureSettingsAppliesPivotCorrectly()
        {
            string path = CreatePng("pivot_apply_test", asSprite: true);
            _assetPath = path;

            TextureImporter importerBefore = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importerBefore != null, "Importer not found");
            importerBefore.spritePivot = new Vector2(0.5f, 0.5f);
            TextureImporterSettings settingsBefore = new TextureImporterSettings();
            importerBefore.ReadTextureSettings(settingsBefore);
            settingsBefore.spriteAlignment = (int)SpriteAlignment.Custom;
            settingsBefore.spritePivot = new Vector2(0.5f, 0.5f);
            importerBefore.SetTextureSettings(settingsBefore);
            ExecuteWithImmediateImport(() => importerBefore.SaveAndReimport());

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyPivot = true,
                    pivot = new Vector2(0.25f, 0.75f),
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter outImporter
            );
            Assert.IsTrue(changed, "Expected changes to be applied");
            Assert.IsTrue(outImporter != null, "Importer should be returned");
            ExecuteWithImmediateImport(() => outImporter.SaveAndReimport());

            TextureImporter importerAfter = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importerAfter != null, "Importer after not found");
            Assert.AreEqual(
                new Vector2(0.25f, 0.75f),
                importerAfter.spritePivot,
                "Pivot should be updated"
            );

            TextureImporterSettings settingsAfter = new TextureImporterSettings();
            importerAfter.ReadTextureSettings(settingsAfter);
            Assert.AreEqual(
                (int)SpriteAlignment.Custom,
                settingsAfter.spriteAlignment,
                "Alignment should be set to Custom"
            );
        }

        [Test]
        public void WillTextureSettingsChangeConsistentWithTryUpdate()
        {
            string path = CreatePng("consistency_test", asSprite: true);
            _assetPath = path;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null, "Importer not found");
            importer.filterMode = FilterMode.Point;
            ExecuteWithImmediateImport(() => importer.SaveAndReimport());

            List<SpriteSettings> matchingProfiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Point,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> preparedMatch =
                SpriteSettingsApplierAPI.PrepareProfiles(matchingProfiles);
            bool willChangeMatch = SpriteSettingsApplierAPI.WillTextureSettingsChange(
                path,
                preparedMatch
            );
            bool changedMatch = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                preparedMatch,
                out TextureImporter _
            );
            Assert.AreEqual(
                willChangeMatch,
                changedMatch,
                "WillTextureSettingsChange and TryUpdateTextureSettings should agree when settings match"
            );

            List<SpriteSettings> differingProfiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> preparedDiffer =
                SpriteSettingsApplierAPI.PrepareProfiles(differingProfiles);
            bool willChangeDiffer = SpriteSettingsApplierAPI.WillTextureSettingsChange(
                path,
                preparedDiffer
            );
            bool changedDiffer = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                preparedDiffer,
                out TextureImporter _
            );
            Assert.AreEqual(
                willChangeDiffer,
                changedDiffer,
                "WillTextureSettingsChange and TryUpdateTextureSettings should agree when settings differ"
            );
        }

        [Test]
        public void WillTextureSettingsChangeBufferReuseWorks()
        {
            string path1 = CreatePng("buffer_reuse_1", asSprite: true);
            _assetPath = path1;

            TextureImporter importer1 = AssetImporter.GetAtPath(path1) as TextureImporter;
            Assert.IsTrue(importer1 != null, "Importer not found for first asset");
            importer1.filterMode = FilterMode.Point;
            ExecuteWithImmediateImport(() => importer1.SaveAndReimport());

            string path2 = null;
            try
            {
                path2 = Path.Combine(TestFolder, "buffer_reuse_2.png");
                Texture2D tex2 = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false));
                byte[] png2 = tex2.EncodeToPNG();
                File.WriteAllBytes(path2, png2);

                TextureImporter importer2 = null;
                ExecuteWithImmediateImport(() =>
                {
                    AssetDatabase.ImportAsset(path2);
                    importer2 = AssetImporter.GetAtPath(path2) as TextureImporter;
                    Assert.IsTrue(importer2 != null, "Importer not found for second asset");
                    importer2.textureType = TextureImporterType.Sprite;
                    importer2.filterMode = FilterMode.Bilinear;
                    importer2.SaveAndReimport();
                });

                List<SpriteSettings> profiles = new()
                {
                    new SpriteSettings
                    {
                        matchBy = SpriteSettings.MatchMode.Any,
                        priority = 1,
                        applyFilterMode = true,
                        filterMode = FilterMode.Point,
                    },
                };

                List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                    SpriteSettingsApplierAPI.PrepareProfiles(profiles);

                TextureImporterSettings sharedBuffer = new TextureImporterSettings();
                bool willChange1 = SpriteSettingsApplierAPI.WillTextureSettingsChange(
                    path1,
                    prepared,
                    sharedBuffer
                );
                Assert.IsFalse(
                    willChange1,
                    "First asset should not need change (already Point filter mode)"
                );

                bool willChange2 = SpriteSettingsApplierAPI.WillTextureSettingsChange(
                    path2,
                    prepared,
                    sharedBuffer
                );
                Assert.IsTrue(willChange2, "Second asset should need change (Bilinear to Point)");

                bool willChange1Again = SpriteSettingsApplierAPI.WillTextureSettingsChange(
                    path1,
                    prepared,
                    sharedBuffer
                );
                Assert.AreEqual(
                    willChange1,
                    willChange1Again,
                    "Buffer reuse should not affect results for first asset on second call"
                );
            }
            finally
            {
                if (path2 != null)
                {
                    AssetDatabase.DeleteAsset(path2);
                }
            }
        }

        [Test]
        public void TryUpdateTextureSettingsBufferReuseWorks()
        {
            string path1 = CreatePng("try_buffer_reuse_1", asSprite: true);
            _assetPath = path1;

            TextureImporter importer1 = AssetImporter.GetAtPath(path1) as TextureImporter;
            Assert.IsTrue(importer1 != null, "Importer not found for first asset");
            importer1.filterMode = FilterMode.Point;
            importer1.wrapMode = TextureWrapMode.Clamp;
            ExecuteWithImmediateImport(() => importer1.SaveAndReimport());

            string path2 = null;
            try
            {
                path2 = Path.Combine(TestFolder, "try_buffer_reuse_2.png");
                Texture2D tex2 = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false));
                byte[] png2 = tex2.EncodeToPNG();
                File.WriteAllBytes(path2, png2);

                TextureImporter importer2 = null;
                ExecuteWithImmediateImport(() =>
                {
                    AssetDatabase.ImportAsset(path2);
                    importer2 = AssetImporter.GetAtPath(path2) as TextureImporter;
                    Assert.IsTrue(importer2 != null, "Importer not found for second asset");
                    importer2.textureType = TextureImporterType.Sprite;
                    importer2.filterMode = FilterMode.Bilinear;
                    importer2.wrapMode = TextureWrapMode.Repeat;
                    importer2.SaveAndReimport();
                });

                List<SpriteSettings> profiles = new()
                {
                    new SpriteSettings
                    {
                        matchBy = SpriteSettings.MatchMode.Any,
                        priority = 1,
                        applyFilterMode = true,
                        filterMode = FilterMode.Point,
                        applyWrapMode = true,
                        wrapMode = TextureWrapMode.Clamp,
                    },
                };

                List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                    SpriteSettingsApplierAPI.PrepareProfiles(profiles);

                TextureImporterSettings sharedBuffer = new TextureImporterSettings();
                bool changed1 = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                    path1,
                    prepared,
                    out TextureImporter outImporter1,
                    sharedBuffer
                );
                Assert.IsFalse(changed1, "First asset should not change (settings already match)");
                Assert.IsTrue(outImporter1 != null, "Importer should be returned for first asset");

                bool changed2 = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                    path2,
                    prepared,
                    out TextureImporter outImporter2,
                    sharedBuffer
                );
                Assert.IsTrue(changed2, "Second asset should change");
                Assert.IsTrue(outImporter2 != null, "Importer should be returned for second asset");
                ExecuteWithImmediateImport(() => outImporter2.SaveAndReimport());

                TextureImporter verifyImporter2 = AssetImporter.GetAtPath(path2) as TextureImporter;
                Assert.IsTrue(verifyImporter2 != null, "Verify importer not found");
                Assert.AreEqual(
                    FilterMode.Point,
                    verifyImporter2.filterMode,
                    "Second asset filter mode should be updated"
                );
                Assert.AreEqual(
                    TextureWrapMode.Clamp,
                    verifyImporter2.wrapMode,
                    "Second asset wrap mode should be updated"
                );
            }
            finally
            {
                if (path2 != null)
                {
                    AssetDatabase.DeleteAsset(path2);
                }
            }
        }

        [Test]
        public void TryUpdateTextureSettingsUpdatesBufferCorrectly()
        {
            string path = CreatePng("buffer_update_test", asSprite: true);
            _assetPath = path;

            TextureImporter importerBefore = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importerBefore != null, "Importer not found");
            importerBefore.spritePixelsPerUnit = 100;
            importerBefore.filterMode = FilterMode.Point;
            importerBefore.wrapMode = TextureWrapMode.Clamp;
            ExecuteWithImmediateImport(() => importerBefore.SaveAndReimport());

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyPixelsPerUnit = true,
                    pixelsPerUnit = 64,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                    applyWrapMode = true,
                    wrapMode = TextureWrapMode.Repeat,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);

            TextureImporterSettings buffer = new TextureImporterSettings();
            bool changed = SpriteSettingsApplierAPI.TryUpdateTextureSettings(
                path,
                prepared,
                out TextureImporter outImporter,
                buffer
            );
            Assert.IsTrue(changed, "Expected changes to be applied");
            Assert.IsTrue(outImporter != null, "Importer should be returned");

            Assert.AreEqual(
                64,
                buffer.spritePixelsPerUnit,
                "Buffer should contain updated PPU value"
            );
            Assert.AreEqual(
                FilterMode.Bilinear,
                buffer.filterMode,
                "Buffer should contain updated filter mode"
            );
            Assert.AreEqual(
                TextureWrapMode.Repeat,
                buffer.wrapMode,
                "Buffer should contain updated wrap mode"
            );
        }

        [Test]
        public void SamePriorityProfilesUseFirstMatchInList()
        {
            string path = CreatePng("tie_breaker_test", asSprite: true);
            _assetPath = path;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null, "Importer not found for path: " + path);
            importer.filterMode = FilterMode.Point;
            ExecuteWithImmediateImport(() => importer.SaveAndReimport());

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "tie",
                    priority = 5,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                },
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "breaker",
                    priority = 5,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(
                matched != null,
                "Expected a matching profile to be found for path: " + path
            );
            Assert.AreEqual(
                FilterMode.Bilinear,
                matched.filterMode,
                "Expected first profile with same priority to win. "
                    + "Matched filter mode was "
                    + matched.filterMode
                    + " but expected Bilinear"
            );
        }

        [Test]
        public void SamePriorityProfilesSecondMatchedFirstInListWins()
        {
            string path = CreatePng("priority_order_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "nonexistent",
                    priority = 10,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "order",
                    priority = 5,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                },
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "priority",
                    priority = 5,
                    applyFilterMode = true,
                    filterMode = FilterMode.Point,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(matched != null, "Expected a matching profile for path: " + path);
            Assert.AreEqual(
                FilterMode.Bilinear,
                matched.filterMode,
                "With same priority, first matching profile in list (order) should win. "
                    + "Actual filter mode: "
                    + matched.filterMode
            );
        }

        private static IEnumerable<TestCaseData> NullEmptyMatchPatternCases()
        {
            yield return new TestCaseData(SpriteSettings.MatchMode.Any, null, true).SetName(
                "MatchPattern.Null.Any.MatchesAnyFile"
            );

            yield return new TestCaseData(SpriteSettings.MatchMode.Any, "", true).SetName(
                "MatchPattern.Empty.Any.MatchesAnyFile"
            );

            yield return new TestCaseData(SpriteSettings.MatchMode.Any, "   ", true).SetName(
                "MatchPattern.Whitespace.Any.MatchesAnyFile"
            );

            yield return new TestCaseData(
                SpriteSettings.MatchMode.NameContains,
                null,
                false
            ).SetName("MatchPattern.Null.NameContains.NoMatch");

            yield return new TestCaseData(SpriteSettings.MatchMode.NameContains, "", false).SetName(
                "MatchPattern.Empty.NameContains.NoMatch"
            );

            yield return new TestCaseData(
                SpriteSettings.MatchMode.NameContains,
                "   ",
                false
            ).SetName("MatchPattern.Whitespace.NameContains.NoMatch");

            yield return new TestCaseData(
                SpriteSettings.MatchMode.PathContains,
                null,
                false
            ).SetName("MatchPattern.Null.PathContains.NoMatch");

            yield return new TestCaseData(SpriteSettings.MatchMode.PathContains, "", false).SetName(
                "MatchPattern.Empty.PathContains.NoMatch"
            );

            yield return new TestCaseData(SpriteSettings.MatchMode.Extension, null, false).SetName(
                "MatchPattern.Null.Extension.NoMatch"
            );

            yield return new TestCaseData(SpriteSettings.MatchMode.Extension, "", false).SetName(
                "MatchPattern.Empty.Extension.NoMatch"
            );

            yield return new TestCaseData(SpriteSettings.MatchMode.Regex, null, false).SetName(
                "MatchPattern.Null.Regex.NoMatch"
            );

            yield return new TestCaseData(SpriteSettings.MatchMode.Regex, "", false).SetName(
                "MatchPattern.Empty.Regex.NoMatch"
            );
        }

        [Test]
        [TestCaseSource(nameof(NullEmptyMatchPatternCases))]
        public void NullOrEmptyMatchPatternBehavesCorrectly(
            SpriteSettings.MatchMode matchMode,
            string matchPattern,
            bool expectedMatch
        )
        {
            string path = CreatePng("pattern_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = matchMode,
                    matchPattern = matchPattern,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            bool didMatch = matched != null;
            Assert.AreEqual(
                expectedMatch,
                didMatch,
                "MatchMode="
                    + matchMode
                    + ", Pattern="
                    + (matchPattern ?? "null")
                    + ". Expected match="
                    + expectedMatch
                    + ", actual match="
                    + didMatch
            );
        }

        private static IEnumerable<TestCaseData> CaseSensitivityCases()
        {
            yield return new TestCaseData(
                SpriteSettings.MatchMode.NameContains,
                "CASE",
                true
            ).SetName("CaseSensitivity.NameContains.UpperCase.Matches");

            yield return new TestCaseData(
                SpriteSettings.MatchMode.NameContains,
                "case",
                true
            ).SetName("CaseSensitivity.NameContains.LowerCase.Matches");

            yield return new TestCaseData(
                SpriteSettings.MatchMode.NameContains,
                "CaSe",
                true
            ).SetName("CaseSensitivity.NameContains.MixedCase.Matches");

            yield return new TestCaseData(
                SpriteSettings.MatchMode.NameContains,
                "SENSITIVE",
                true
            ).SetName("CaseSensitivity.NameContains.UpperSensitive.Matches");

            yield return new TestCaseData(
                SpriteSettings.MatchMode.PathContains,
                "TEMPSPR",
                true
            ).SetName("CaseSensitivity.PathContains.UpperCase.Matches");

            yield return new TestCaseData(
                SpriteSettings.MatchMode.PathContains,
                "tempspr",
                true
            ).SetName("CaseSensitivity.PathContains.LowerCase.Matches");

            yield return new TestCaseData(
                SpriteSettings.MatchMode.PathContains,
                "TeMpSpR",
                true
            ).SetName("CaseSensitivity.PathContains.MixedCase.Matches");

            yield return new TestCaseData(
                SpriteSettings.MatchMode.PathContains,
                "ADDITIONAL",
                true
            ).SetName("CaseSensitivity.PathContains.UpperAdditional.Matches");
        }

        [Test]
        [TestCaseSource(nameof(CaseSensitivityCases))]
        public void MatchModeIsCaseInsensitive(
            SpriteSettings.MatchMode matchMode,
            string matchPattern,
            bool expectedMatch
        )
        {
            string path = CreatePng("case_sensitive_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = matchMode,
                    matchPattern = matchPattern,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            bool didMatch = matched != null;
            Assert.AreEqual(
                expectedMatch,
                didMatch,
                "MatchMode="
                    + matchMode
                    + ", Pattern='"
                    + matchPattern
                    + "', Path='"
                    + path
                    + "'. "
                    + "Expected match="
                    + expectedMatch
                    + ", actual match="
                    + didMatch
                    + ". "
                    + "Pattern matching should be case-insensitive."
            );
        }

        [Test]
        public void NameContainsCaseInsensitiveWithActualCaseVariation()
        {
            string path = CreatePng("MixedCASEname", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "mixedcasename",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Point,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(
                matched != null,
                "NameContains should match case-insensitively. "
                    + "File name has 'MixedCASEname', pattern is 'mixedcasename'. "
                    + "Path: "
                    + path
            );
        }

        [Test]
        public void PathContainsCaseInsensitiveWithActualCaseVariation()
        {
            string path = CreatePng("pathtest", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.PathContains,
                    matchPattern = "TEMPSPRITEAPPLIERADDITIONAL",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Point,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(
                matched != null,
                "PathContains should match case-insensitively. "
                    + "Path contains 'TempSpriteApplierAdditional', pattern is 'TEMPSPRITEAPPLIERADDITIONAL'. "
                    + "Path: "
                    + path
            );
        }

        [Test]
        public void ExtensionMatchIsCaseInsensitive()
        {
            string path = CreatePng("extension_case_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Extension,
                    matchPattern = ".PNG",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(
                matched != null,
                "Extension matching should be case-insensitive. "
                    + "File has '.png' extension, pattern is '.PNG'. "
                    + "Path: "
                    + path
            );
        }

        [Test]
        public void ExtensionMatchWithoutDotPrefix()
        {
            string path = CreatePng("extension_no_dot_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Extension,
                    matchPattern = "png",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(
                matched != null,
                "Extension matching should work without leading dot. "
                    + "File has '.png' extension, pattern is 'png' (no dot). "
                    + "Path: "
                    + path
            );
        }

        [Test]
        public void HigherPriorityWinsOverListOrder()
        {
            string path = CreatePng("priority_wins", asSprite: true);
            _assetPath = path;

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsTrue(importer != null, "Importer not found for path: " + path);
            importer.filterMode = FilterMode.Point;
            ExecuteWithImmediateImport(() => importer.SaveAndReimport());

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "priority",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Point,
                },
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "wins",
                    priority = 100,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "priority_wins",
                    priority = 50,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(matched != null, "Expected a matching profile for path: " + path);
            Assert.AreEqual(
                FilterMode.Trilinear,
                matched.filterMode,
                "Profile with highest priority (100) should win regardless of list order. "
                    + "Expected Trilinear, got "
                    + matched.filterMode
                    + ". "
                    + "Priorities were: 1, 100, 50"
            );
        }

        [Test]
        public void FindMatchingSettingsWithNullPreparedListReturnsNull()
        {
            string path = CreatePng("null_prepared", asSprite: true);
            _assetPath = path;

            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, null);

            Assert.IsTrue(
                matched == null,
                "FindMatchingSettings should return null when prepared list is null"
            );
        }

        [Test]
        public void FindMatchingSettingsWithEmptyPreparedListReturnsNull()
        {
            string path = CreatePng("empty_prepared", asSprite: true);
            _assetPath = path;

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(new List<SpriteSettings>());
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(
                matched == null,
                "FindMatchingSettings should return null when prepared list is empty"
            );
        }

        [Test]
        public void FindMatchingSettingsWithNullPathReturnsNull()
        {
            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings { matchBy = SpriteSettings.MatchMode.Any, priority = 1 },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(null, prepared);

            Assert.IsTrue(matched == null, "FindMatchingSettings should return null for null path");
        }

        [Test]
        public void RegexMatchIsCaseInsensitive()
        {
            string path = CreatePng("regex_case_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Regex,
                    matchPattern = "REGEX_CASE",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(
                matched != null,
                "Regex matching should be case-insensitive. "
                    + "File name has 'regex_case', pattern is 'REGEX_CASE'. "
                    + "Path: "
                    + path
            );
        }

        [Test]
        public void InvalidRegexPatternDoesNotMatch()
        {
            string path = CreatePng("invalid_regex_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Regex,
                    matchPattern = "[invalid(regex",
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Trilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(
                matched == null,
                "Invalid regex pattern should not match. "
                    + "Pattern '[invalid(regex' is malformed. "
                    + "Path: "
                    + path
            );
        }

        [Test]
        public void PrepareProfilesSkipsNullEntries()
        {
            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Point,
                },
                null,
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.Any,
                    priority = 2,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);

            Assert.AreEqual(
                2,
                prepared.Count,
                "PrepareProfiles should skip null entries. "
                    + "Input had 3 items (2 valid + 1 null), expected 2 prepared profiles."
            );
        }

        [Test]
        public void PrepareProfilesWithNullListReturnsEmptyList()
        {
            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(null);

            Assert.IsTrue(prepared != null, "PrepareProfiles should never return null");
            Assert.AreEqual(
                0,
                prepared.Count,
                "PrepareProfiles with null input should return empty list"
            );
        }

        private static IEnumerable<TestCaseData> NegativePriorityCases()
        {
            yield return new TestCaseData(-1, 0, 0).SetName("Priority.NegativeVsZero.ZeroWins");
            yield return new TestCaseData(-100, -50, -50).SetName(
                "Priority.TwoNegatives.HigherNegativeWins"
            );
            yield return new TestCaseData(int.MinValue, 0, 0).SetName(
                "Priority.MinValueVsZero.ZeroWins"
            );
            yield return new TestCaseData(int.MinValue, int.MaxValue, int.MaxValue).SetName(
                "Priority.MinValueVsMaxValue.MaxValueWins"
            );
        }

        [Test]
        [TestCaseSource(nameof(NegativePriorityCases))]
        public void NegativePriorityHandledCorrectly(
            int priority1,
            int priority2,
            int expectedWinningPriority
        )
        {
            string path = CreatePng("negative_priority_test", asSprite: true);
            _assetPath = path;

            List<SpriteSettings> profiles = new()
            {
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "negative",
                    priority = priority1,
                    applyFilterMode = true,
                    filterMode = FilterMode.Point,
                },
                new SpriteSettings
                {
                    matchBy = SpriteSettings.MatchMode.NameContains,
                    matchPattern = "priority",
                    priority = priority2,
                    applyFilterMode = true,
                    filterMode = FilterMode.Bilinear,
                },
            };

            List<SpriteSettingsApplierAPI.PreparedProfile> prepared =
                SpriteSettingsApplierAPI.PrepareProfiles(profiles);
            SpriteSettings matched = SpriteSettingsApplierAPI.FindMatchingSettings(path, prepared);

            Assert.IsTrue(matched != null, "Expected a match for path: " + path);
            Assert.AreEqual(
                expectedWinningPriority,
                matched.priority,
                "Priority comparison failed. "
                    + "Profile priorities: "
                    + priority1
                    + ", "
                    + priority2
                    + ". "
                    + "Expected winning priority: "
                    + expectedWinningPriority
                    + ", "
                    + "actual: "
                    + matched.priority
            );
        }
    }
#endif
}
