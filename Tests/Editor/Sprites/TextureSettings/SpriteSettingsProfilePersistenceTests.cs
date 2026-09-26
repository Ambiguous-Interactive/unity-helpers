// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SpriteSettingsProfilePersistenceTests : CommonTestBase
    {
        private const string Root = "Assets/Temp/SpriteSettingsProfilePersistenceTests";
        private const string ProfilePath = Root + "/Profiles.asset";

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [TearDown]
        public override void TearDown()
        {
            AssetDatabase.DeleteAsset(Root + "/Profiles.staging.asset");
            AssetDatabase.DeleteAsset(ProfilePath);
            base.TearDown();
        }

        [Test]
        public void SaveReimportAndLoadPreservesValuesWithoutSharingProfiles()
        {
            SpriteSettings source = new()
            {
                name = "World",
                matchBy = SpriteSettings.MatchMode.PathContains,
                matchPattern = "Sprites/World",
                priority = 7,
                applyPixelsPerUnit = true,
                pixelsPerUnit = 16,
                applyPivot = true,
                pivot = new Vector2(0.2f, 0.8f),
                applyFilterMode = true,
                filterMode = FilterMode.Point,
            };
            List<SpriteSettings> input = new() { source };

            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    input,
                    false,
                    out string error
                ),
                error
            );
            source.pixelsPerUnit = 999;
            AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceSynchronousImport);
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryLoadProfiles(
                    ProfilePath,
                    out List<SpriteSettings> loaded,
                    out error
                ),
                error
            );
            Assert.AreEqual(1, loaded.Count);
            Assert.AreEqual("World", loaded[0].name);
            Assert.AreEqual(SpriteSettings.MatchMode.PathContains, loaded[0].matchBy);
            Assert.AreEqual("Sprites/World", loaded[0].matchPattern);
            Assert.AreEqual(7, loaded[0].priority);
            Assert.IsTrue(loaded[0].applyPixelsPerUnit);
            Assert.AreEqual(16, loaded[0].pixelsPerUnit);
            Assert.AreEqual(new Vector2(0.2f, 0.8f), loaded[0].pivot);
            Assert.AreEqual(FilterMode.Point, loaded[0].filterMode);
            Assert.AreNotSame(source, loaded[0]);

            loaded[0].pixelsPerUnit = 42;
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryLoadProfiles(
                    ProfilePath,
                    out List<SpriteSettings> again,
                    out error
                ),
                error
            );
            Assert.AreEqual(16, again[0].pixelsPerUnit);
            Assert.AreNotSame(loaded[0], again[0]);
        }

        [Test]
        public void ExistingProfileRequiresExplicitOverwriteAndWrongTypeIsPreserved()
        {
            List<SpriteSettings> first = new() { new SpriteSettings { name = "First" } };
            List<SpriteSettings> second = new() { new SpriteSettings { name = "Second" } };
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    first,
                    false,
                    out string error
                ),
                error
            );
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, second, false, out error)
            );
            string originalGuid = AssetDatabase.AssetPathToGUID(ProfilePath);
            Assert.IsNotEmpty(error);
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryLoadProfiles(
                    ProfilePath,
                    out List<SpriteSettings> loaded,
                    out error
                ),
                error
            );
            Assert.AreEqual("First", loaded[0].name);

            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, second, true, out error),
                error
            );
            Assert.AreEqual(originalGuid, AssetDatabase.AssetPathToGUID(ProfilePath));
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryLoadProfiles(ProfilePath, out loaded, out error),
                error
            );
            Assert.AreEqual("Second", loaded[0].name);

            AssetDatabase.DeleteAsset(ProfilePath);
            Texture2D texture = new(1, 1);
            AssetDatabase.CreateAsset(texture, ProfilePath);
            SpriteSettingsProfileCollection subasset = Track(
                ScriptableObject.CreateInstance<SpriteSettingsProfileCollection>()
            );
            subasset.profiles = new List<SpriteSettings>
            {
                new SpriteSettings { name = "Subasset" },
            };
            AssetDatabase.AddObjectToAsset(subasset, ProfilePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceSynchronousImport);
            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<SpriteSettingsProfileCollection>(ProfilePath) != null
            );
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, first, true, out error)
            );
            Assert.IsNotEmpty(error);
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TryLoadProfiles(ProfilePath, out loaded, out error)
            );
            Assert.IsTrue(loaded == null);
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Texture2D>(ProfilePath) != null);
        }

        [Test]
        public void FailedOverwriteRestoresOriginalWhenDestinationDisappears()
        {
            List<SpriteSettings> first = new() { new SpriteSettings { name = "First" } };
            List<SpriteSettings> second = new() { new SpriteSettings { name = "Second" } };
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    first,
                    false,
                    out string error
                ),
                error
            );
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                ProfilePath
            );
            byte[] originalBytes = File.ReadAllBytes(fullPath);
            bool failedOnce = false;
            RestorableGlobal<Action<string>> swap = new(
                () => DurableFile.BeforeStagedSwapForTests,
                action => DurableFile.BeforeStagedSwapForTests = action
            );
            using (
                swap.Borrow(_ =>
                {
                    if (failedOnce)
                    {
                        return;
                    }
                    failedOnce = true;
                    File.Delete(fullPath);
                    throw new IOException("forced replacement failure");
                })
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, second, true, out error)
                );
            }
            StringAssert.Contains("forced replacement failure", error);
            Assert.IsTrue(File.Exists(fullPath));
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(fullPath));
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryLoadProfiles(
                    ProfilePath,
                    out List<SpriteSettings> loaded,
                    out error
                ),
                error
            );
            Assert.AreEqual("First", loaded[0].name);
        }

        [Test]
        public void FailedNewSaveReportsPartialAssetWhenCleanupFails()
        {
            RestorableGlobal<Action> save = new(
                () => SpriteSettingsApplierAPI.SaveProfileAssetsAction,
                action => SpriteSettingsApplierAPI.SaveProfileAssetsAction = action
            );
            RestorableGlobal<Func<string, bool>> delete = new(
                () => SpriteSettingsApplierAPI.DeleteProfileAssetAction,
                action => SpriteSettingsApplierAPI.DeleteProfileAssetAction = action
            );
            using (save.Borrow(() => throw new IOException("forced save failure")))
            using (delete.Borrow(_ => false))
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        new List<SpriteSettings> { new SpriteSettings() },
                        false,
                        out string error
                    )
                );
                StringAssert.Contains("forced save failure", error);
                StringAssert.Contains("Could not remove partial profiles asset", error);
                Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(ProfilePath) != null);
            }
        }

        [Test]
        public void FailedTypedReloadCleansNewAndStagedAssets()
        {
            RestorableGlobal<Func<string, SpriteSettingsProfileCollection>> load = new(
                () => SpriteSettingsApplierAPI.LoadProfileAssetAction,
                action => SpriteSettingsApplierAPI.LoadProfileAssetAction = action
            );
            List<SpriteSettings> profiles = new() { new SpriteSettings { name = "Original" } };
            using (load.Borrow(_ => null))
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        profiles,
                        false,
                        out string error
                    )
                );
                StringAssert.Contains("Could not create profiles asset", error);
            }
            Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(ProfilePath) == null);

            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    profiles,
                    false,
                    out string saveError
                ),
                saveError
            );
            using (load.Borrow(_ => null))
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        profiles,
                        true,
                        out string error
                    )
                );
                StringAssert.Contains("Could not create profiles asset", error);
            }
            Assert.IsTrue(
                AssetDatabase.LoadMainAssetAtPath(Root + "/Profiles.staging.asset") == null
            );
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryLoadProfiles(
                    ProfilePath,
                    out List<SpriteSettings> loaded,
                    out string loadError
                ),
                loadError
            );
            Assert.AreEqual("Original", loaded[0].name);
        }

        [Test]
        public void FailedStagingCleanupPreservesExistingAssetAndReportsPath()
        {
            List<SpriteSettings> first = new() { new SpriteSettings { name = "First" } };
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    first,
                    false,
                    out string error
                ),
                error
            );
            string stagingPath = Root + "/Profiles.staging.asset";
            RestorableGlobal<Func<string, bool>> delete = new(
                () => SpriteSettingsApplierAPI.DeleteProfileAssetAction,
                action => SpriteSettingsApplierAPI.DeleteProfileAssetAction = action
            );
            using (delete.Borrow(_ => false))
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, first, true, out error)
                );
                StringAssert.Contains(stagingPath, error);
                Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(stagingPath) != null);
            }
            AssetDatabase.DeleteAsset(stagingPath);
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryLoadProfiles(
                    ProfilePath,
                    out List<SpriteSettings> loaded,
                    out error
                ),
                error
            );
            Assert.AreEqual("First", loaded[0].name);
        }

        [Test]
        public void InvalidInputsLeaveAssetAndCallerProfilesUntouched()
        {
            List<SpriteSettings> input = new() { new SpriteSettings { name = "Safe" } };
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    input,
                    false,
                    out string error
                ),
                error
            );
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TrySaveProfiles("../outside.asset", input, true, out error)
            );
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    Root + "/Missing/Profiles.asset",
                    input,
                    true,
                    out error
                )
            );
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    Root + "/bad\0.asset",
                    input,
                    true,
                    out error
                )
            );
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    new List<SpriteSettings> { null },
                    true,
                    out error
                )
            );
            Assert.AreEqual("Safe", input[0].name);
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryLoadProfiles(
                    ProfilePath,
                    out List<SpriteSettings> loaded,
                    out error
                ),
                error
            );
            Assert.AreEqual("Safe", loaded[0].name);
        }

        [Test]
        public void WindowDelegatesProfileSaveAndLoad()
        {
            SpriteSettingsApplierWindow window = Track(
                ScriptableObject.CreateInstance<SpriteSettingsApplierWindow>()
            );
            window.spriteSettings = new List<SpriteSettings>
            {
                new SpriteSettings { name = "Window" },
            };
            Assert.IsTrue(window.TrySaveProfilesAssetAtPath(ProfilePath, out string error), error);
            window.spriteSettings[0].name = "Changed";
            Assert.IsTrue(window.TryLoadProfilesAssetAtPath(ProfilePath, out error), error);
            Assert.AreEqual("Window", window.spriteSettings[0].name);
            Assert.IsFalse(window.TryLoadProfilesAssetAtPath("Assets/Missing.asset", out error));
            Assert.AreEqual("Window", window.spriteSettings[0].name);
        }
    }
#endif
}
