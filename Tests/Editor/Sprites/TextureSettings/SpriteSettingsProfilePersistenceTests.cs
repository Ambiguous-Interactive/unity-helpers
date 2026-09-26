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

        private static string[] StagedAssetFiles()
        {
            string rootPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), Root);
            return Directory.GetFiles(rootPath, "Profiles.staging.*.asset");
        }

        private static void AssertProfileValues(SpriteSettings expected, SpriteSettings actual)
        {
            Assert.AreEqual(expected.matchBy, actual.matchBy, nameof(SpriteSettings.matchBy));
            Assert.AreEqual(
                expected.matchPattern,
                actual.matchPattern,
                nameof(SpriteSettings.matchPattern)
            );
            Assert.AreEqual(expected.priority, actual.priority, nameof(SpriteSettings.priority));
            Assert.AreEqual(
                expected.applyPixelsPerUnit,
                actual.applyPixelsPerUnit,
                nameof(SpriteSettings.applyPixelsPerUnit)
            );
            Assert.AreEqual(
                expected.pixelsPerUnit,
                actual.pixelsPerUnit,
                nameof(SpriteSettings.pixelsPerUnit)
            );
            Assert.AreEqual(
                expected.applyPivot,
                actual.applyPivot,
                nameof(SpriteSettings.applyPivot)
            );
            Assert.AreEqual(expected.pivot, actual.pivot, nameof(SpriteSettings.pivot));
            Assert.AreEqual(
                expected.applySpriteMode,
                actual.applySpriteMode,
                nameof(SpriteSettings.applySpriteMode)
            );
            Assert.AreEqual(
                expected.spriteMode,
                actual.spriteMode,
                nameof(SpriteSettings.spriteMode)
            );
            Assert.AreEqual(
                expected.applyGenerateMipMaps,
                actual.applyGenerateMipMaps,
                nameof(SpriteSettings.applyGenerateMipMaps)
            );
            Assert.AreEqual(
                expected.generateMipMaps,
                actual.generateMipMaps,
                nameof(SpriteSettings.generateMipMaps)
            );
            Assert.AreEqual(
                expected.applyAlphaIsTransparency,
                actual.applyAlphaIsTransparency,
                nameof(SpriteSettings.applyAlphaIsTransparency)
            );
            Assert.AreEqual(
                expected.alphaIsTransparency,
                actual.alphaIsTransparency,
                nameof(SpriteSettings.alphaIsTransparency)
            );
            Assert.AreEqual(
                expected.applyReadWriteEnabled,
                actual.applyReadWriteEnabled,
                nameof(SpriteSettings.applyReadWriteEnabled)
            );
            Assert.AreEqual(
                expected.readWriteEnabled,
                actual.readWriteEnabled,
                nameof(SpriteSettings.readWriteEnabled)
            );
            Assert.AreEqual(
                expected.applyExtrudeEdges,
                actual.applyExtrudeEdges,
                nameof(SpriteSettings.applyExtrudeEdges)
            );
            Assert.AreEqual(
                expected.extrudeEdges,
                actual.extrudeEdges,
                nameof(SpriteSettings.extrudeEdges)
            );
            Assert.AreEqual(
                expected.applyWrapMode,
                actual.applyWrapMode,
                nameof(SpriteSettings.applyWrapMode)
            );
            Assert.AreEqual(expected.wrapMode, actual.wrapMode, nameof(SpriteSettings.wrapMode));
            Assert.AreEqual(
                expected.applyFilterMode,
                actual.applyFilterMode,
                nameof(SpriteSettings.applyFilterMode)
            );
            Assert.AreEqual(
                expected.filterMode,
                actual.filterMode,
                nameof(SpriteSettings.filterMode)
            );
            Assert.AreEqual(
                expected.applyCrunchCompression,
                actual.applyCrunchCompression,
                nameof(SpriteSettings.applyCrunchCompression)
            );
            Assert.AreEqual(
                expected.useCrunchCompression,
                actual.useCrunchCompression,
                nameof(SpriteSettings.useCrunchCompression)
            );
            Assert.AreEqual(
                expected.applyCompression,
                actual.applyCompression,
                nameof(SpriteSettings.applyCompression)
            );
            Assert.AreEqual(
                expected.compressionLevel,
                actual.compressionLevel,
                nameof(SpriteSettings.compressionLevel)
            );
            Assert.AreEqual(expected.name, actual.name, nameof(SpriteSettings.name));
            Assert.AreEqual(
                expected.applyTextureType,
                actual.applyTextureType,
                nameof(SpriteSettings.applyTextureType)
            );
            Assert.AreEqual(
                expected.textureType,
                actual.textureType,
                nameof(SpriteSettings.textureType)
            );
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            EnsureFolder(Root);
        }

        [TearDown]
        public override void TearDown()
        {
            AssetDatabase.DeleteAsset(Root);
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
                applySpriteMode = true,
                spriteMode = SpriteImportMode.Multiple,
                applyGenerateMipMaps = true,
                generateMipMaps = true,
                applyAlphaIsTransparency = false,
                alphaIsTransparency = false,
                applyReadWriteEnabled = false,
                readWriteEnabled = false,
                applyExtrudeEdges = true,
                extrudeEdges = 9,
                applyWrapMode = true,
                wrapMode = TextureWrapMode.Repeat,
                applyFilterMode = true,
                filterMode = FilterMode.Trilinear,
                applyCrunchCompression = true,
                useCrunchCompression = true,
                applyCompression = true,
                compressionLevel = TextureImporterCompression.Uncompressed,
                applyTextureType = true,
                textureType = TextureImporterType.Default,
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
            SpriteSettingsProfileCollection saved =
                AssetDatabase.LoadAssetAtPath<SpriteSettingsProfileCollection>(ProfilePath);
            Assert.IsTrue(saved != null);
            AssertProfileValues(source, saved.profiles[0]);
            Assert.AreNotSame(source, saved.profiles[0]);
            source.pixelsPerUnit = 999;
            Assert.AreEqual(16, saved.profiles[0].pixelsPerUnit);
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
            source.pixelsPerUnit = 16;
            AssertProfileValues(source, loaded[0]);
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
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    new List<SpriteSettings> { null },
                    false,
                    out string earlyError
                )
            );
            StringAssert.Contains("already exists", earlyError);
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
        public void FailedNewSavePreservesStagedAssetWithoutCallingDelete()
        {
            RestorableGlobal<Func<string, SpriteSettingsProfileCollection>> load = new(
                () => SpriteSettingsApplierAPI.LoadProfileAssetAction,
                action => SpriteSettingsApplierAPI.LoadProfileAssetAction = action
            );
            RestorableGlobal<Func<string, bool>> delete = new(
                () => SpriteSettingsApplierAPI.DeleteProfileAssetAction,
                action => SpriteSettingsApplierAPI.DeleteProfileAssetAction = action
            );
            int deleteCalls = 0;
            using (load.Borrow(_ => null))
            using (
                delete.Borrow(_ =>
                {
                    deleteCalls++;
                    return true;
                })
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        new List<SpriteSettings> { new SpriteSettings() },
                        false,
                        out string error
                    )
                );
                StringAssert.Contains("Could not create profiles asset", error);
                StringAssert.Contains("inspect it before removal", error);
                Assert.AreEqual(1, StagedAssetFiles().Length);
                Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(ProfilePath) == null);
            }
            Assert.AreEqual(0, deleteCalls);
        }

        [Test]
        public void AFailedSavePreservesStagedBytesWithoutAnOwnershipSnapshot()
        {
            byte[] laterBytes = { 8, 7, 6, 5 };
            RestorableGlobal<Action> save = new(
                () => SpriteSettingsApplierAPI.SaveProfileAssetsAction,
                action => SpriteSettingsApplierAPI.SaveProfileAssetsAction = action
            );
            using (
                save.Borrow(() =>
                {
                    AssetDatabase.SaveAssets();
                    File.WriteAllBytes(StagedAssetFiles()[0], laterBytes);
                    throw new IOException("forced post-save failure");
                })
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        new List<SpriteSettings> { new SpriteSettings() },
                        false,
                        out string error
                    )
                );
                StringAssert.Contains("forced post-save failure", error);
                StringAssert.Contains("inspect it before removal", error);
            }
            string[] staged = StagedAssetFiles();
            Assert.AreEqual(1, staged.Length);
            CollectionAssert.AreEqual(laterBytes, File.ReadAllBytes(staged[0]));
            Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(ProfilePath) == null);
        }

        [Test]
        public void FailedTypedReloadPreservesStagedAssets()
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
            Assert.AreEqual(2, StagedAssetFiles().Length);
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
            RestorableGlobal<Func<string, bool>> delete = new(
                () => SpriteSettingsApplierAPI.DeleteProfileAssetAction,
                action => SpriteSettingsApplierAPI.DeleteProfileAssetAction = action
            );
            using (delete.Borrow(_ => false))
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, first, true, out error)
                );
                StringAssert.Contains("Profiles.staging.", error);
                Assert.AreEqual(1, StagedAssetFiles().Length);
            }
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
        public void ANewSavePreservesALateCollisionAtTheDestination()
        {
            RestorableGlobal<Func<string, string, string>> move = new(
                () => SpriteSettingsApplierAPI.MoveProfileAssetAction,
                action => SpriteSettingsApplierAPI.MoveProfileAssetAction = action
            );
            using (
                move.Borrow(
                    (stagingPath, destinationPath) =>
                    {
                        Texture2D collision = new(1, 1);
                        AssetDatabase.CreateAsset(collision, destinationPath);
                        return AssetDatabase.MoveAsset(stagingPath, destinationPath);
                    }
                )
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        new List<SpriteSettings> { new SpriteSettings() },
                        false,
                        out string error
                    )
                );
                StringAssert.Contains("Could not move profiles asset", error);
            }
            Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(ProfilePath) is Texture2D);
            Assert.AreEqual(1, StagedAssetFiles().Length);
        }

        [Test]
        public void AnOverwriteRejectsAChangedSnapshotWithoutRestoringIt()
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
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                ProfilePath
            );
            byte[] originalBytes = File.ReadAllBytes(fullPath);
            byte[] changedBytes = { 1, 2, 3, 4 };
            RestorableGlobal<Action> save = new(
                () => SpriteSettingsApplierAPI.SaveProfileAssetsAction,
                action => SpriteSettingsApplierAPI.SaveProfileAssetsAction = action
            );
            using (
                save.Borrow(() =>
                {
                    AssetDatabase.SaveAssets();
                    File.WriteAllBytes(fullPath, changedBytes);
                })
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, first, true, out error)
                );
            }
            StringAssert.Contains("changed since it was read", error);
            CollectionAssert.AreEqual(changedBytes, File.ReadAllBytes(fullPath));
            File.WriteAllBytes(fullPath, originalBytes);
            AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceSynchronousImport);
            Assert.AreEqual(0, StagedAssetFiles().Length);
        }

        [Test]
        public void AFailedPostSwapLoadPreservesALaterWriterInsteadOfRollingItBack()
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
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                ProfilePath
            );
            byte[] originalBytes = File.ReadAllBytes(fullPath);
            byte[] laterBytes = { 5, 6, 7, 8 };
            RestorableGlobal<Func<string, SpriteSettingsProfileCollection>> load = new(
                () => SpriteSettingsApplierAPI.LoadProfileAssetAction,
                action => SpriteSettingsApplierAPI.LoadProfileAssetAction = action
            );
            using (
                load.Borrow(path =>
                {
                    if (string.Equals(path, ProfilePath, StringComparison.Ordinal))
                    {
                        File.WriteAllBytes(fullPath, laterBytes);
                        return null;
                    }
                    return AssetDatabase.LoadAssetAtPath<SpriteSettingsProfileCollection>(path);
                })
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, first, true, out error)
                );
            }
            StringAssert.Contains("Could not restore the prior asset", error);
            CollectionAssert.AreEqual(laterBytes, File.ReadAllBytes(fullPath));
            File.WriteAllBytes(fullPath, originalBytes);
            AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceSynchronousImport);
            Assert.AreEqual(0, StagedAssetFiles().Length);
        }

        [Test]
        public void FailedOverwriteDoesNotDeleteAReusedStagingPath()
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
            string stagingPath = null;
            RestorableGlobal<Func<string, SpriteSettingsProfileCollection>> load = new(
                () => SpriteSettingsApplierAPI.LoadProfileAssetAction,
                action => SpriteSettingsApplierAPI.LoadProfileAssetAction = action
            );
            RestorableGlobal<Action<string>> compare = new(
                () => DurableFile.BeforeCompareReadForTests,
                action => DurableFile.BeforeCompareReadForTests = action
            );
            using (
                load.Borrow(path =>
                {
                    if (!string.Equals(path, ProfilePath, StringComparison.Ordinal))
                    {
                        stagingPath = path;
                    }
                    return AssetDatabase.LoadAssetAtPath<SpriteSettingsProfileCollection>(path);
                })
            )
            using (
                compare.Borrow(_ =>
                {
                    Assert.IsTrue(stagingPath != null);
                    Texture2D replacement = new(1, 1);
                    AssetDatabase.CreateAsset(replacement, stagingPath);
                    throw new IOException("forced compare failure");
                })
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, first, true, out error)
                );
            }
            StringAssert.Contains("forced compare failure", error);
            Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(stagingPath) is Texture2D);
            Assert.AreEqual(
                "First",
                AssetDatabase
                    .LoadAssetAtPath<SpriteSettingsProfileCollection>(ProfilePath)
                    .profiles[0]
                    .name
            );
        }

        [Test]
        public void AFailedCreateAfterWritingPreservesItsUnverifiedStagedAsset()
        {
            RestorableGlobal<Action<UnityEngine.Object, string>> create = new(
                () => SpriteSettingsApplierAPI.CreateProfileAssetAction,
                action => SpriteSettingsApplierAPI.CreateProfileAssetAction = action
            );
            using (
                create.Borrow(
                    (asset, path) =>
                    {
                        AssetDatabase.CreateAsset(asset, path);
                        throw new IOException("forced post-create failure");
                    }
                )
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        new List<SpriteSettings> { new SpriteSettings() },
                        false,
                        out string error
                    )
                );
                StringAssert.Contains("forced post-create failure", error);
                StringAssert.Contains("inspect it before removal", error);
            }
            Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(ProfilePath) == null);
            Assert.AreEqual(1, StagedAssetFiles().Length);
        }

        [Test]
        public void AFailedImportAfterMoveIdentifiesTheCommittedDestination()
        {
            RestorableGlobal<Action<string, ImportAssetOptions>> import = new(
                () => SpriteSettingsApplierAPI.ImportProfileAssetAction,
                action => SpriteSettingsApplierAPI.ImportProfileAssetAction = action
            );
            using (import.Borrow((_, _) => throw new IOException("forced import failure")))
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        new List<SpriteSettings> { new SpriteSettings() },
                        false,
                        out string error
                    )
                );
                StringAssert.Contains("forced import failure", error);
                StringAssert.Contains("Inspect the destination", error);
                StringAssert.Contains(ProfilePath, error);
            }
            Assert.IsTrue(
                AssetDatabase.LoadMainAssetAtPath(ProfilePath) is SpriteSettingsProfileCollection
            );
            Assert.AreEqual(0, StagedAssetFiles().Length);
        }

        [Test]
        public void AnOverwriteDoesNotReportSuccessAfterALaterValidWrite()
        {
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    new List<SpriteSettings> { new SpriteSettings { name = "First" } },
                    false,
                    out string error
                ),
                error
            );
            string laterPath = Root + "/Later.asset";
            SpriteSettingsProfileCollection later = Track(
                ScriptableObject.CreateInstance<SpriteSettingsProfileCollection>()
            );
            later.profiles = new List<SpriteSettings> { new SpriteSettings { name = "Later" } };
            AssetDatabase.CreateAsset(later, laterPath);
            AssetDatabase.SaveAssets();
            byte[] laterBytes = File.ReadAllBytes(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), laterPath)
            );
            AssetDatabase.DeleteAsset(laterPath);
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                ProfilePath
            );
            RestorableGlobal<Func<string, SpriteSettingsProfileCollection>> load = new(
                () => SpriteSettingsApplierAPI.LoadProfileAssetAction,
                action => SpriteSettingsApplierAPI.LoadProfileAssetAction = action
            );
            using (
                load.Borrow(path =>
                {
                    if (string.Equals(path, ProfilePath, StringComparison.Ordinal))
                    {
                        File.WriteAllBytes(fullPath, laterBytes);
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                    }
                    return AssetDatabase.LoadAssetAtPath<SpriteSettingsProfileCollection>(path);
                })
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        new List<SpriteSettings> { new SpriteSettings { name = "Second" } },
                        true,
                        out error
                    )
                );
            }
            StringAssert.Contains("could not be verified", error);
            StringAssert.Contains("Could not restore the prior asset", error);
            SpriteSettingsProfileCollection persisted =
                AssetDatabase.LoadAssetAtPath<SpriteSettingsProfileCollection>(ProfilePath);
            Assert.AreEqual("Later", persisted.profiles[0].name);
            Assert.AreEqual(0, StagedAssetFiles().Length);
        }

        [Test]
        public void AMoveThatReportsAnErrorAfterCompletionStillProducesAValidAsset()
        {
            string failureError = null;
            RestorableGlobal<Func<string, string, string>> move = new(
                () => SpriteSettingsApplierAPI.MoveProfileAssetAction,
                action => SpriteSettingsApplierAPI.MoveProfileAssetAction = action
            );
            using (
                move.Borrow(
                    (stagingPath, destinationPath) =>
                    {
                        string moveError = AssetDatabase.MoveAsset(stagingPath, destinationPath);
                        Assert.IsEmpty(moveError);
                        throw new IOException("forced post-move failure");
                    }
                )
            )
            {
                Assert.IsTrue(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        new List<SpriteSettings> { new SpriteSettings() },
                        false,
                        out failureError
                    )
                );
                Assert.IsTrue(failureError == null);
            }
            Assert.IsTrue(
                AssetDatabase.LoadMainAssetAtPath(ProfilePath) is SpriteSettingsProfileCollection,
                failureError
            );
            Assert.AreEqual(0, StagedAssetFiles().Length);
        }

        [Test]
        public void ANewSaveNeverRemovesBytesWrittenAfterItsMove()
        {
            string laterPath = Root + "/Later.asset";
            SpriteSettingsProfileCollection later = Track(
                ScriptableObject.CreateInstance<SpriteSettingsProfileCollection>()
            );
            later.profiles = new List<SpriteSettings> { new SpriteSettings { name = "Later" } };
            AssetDatabase.CreateAsset(later, laterPath);
            AssetDatabase.SaveAssets();
            byte[] laterBytes = File.ReadAllBytes(
                Path.Combine(Path.GetDirectoryName(Application.dataPath), laterPath)
            );
            AssetDatabase.DeleteAsset(laterPath);
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                ProfilePath
            );
            RestorableGlobal<Func<string, string, string>> move = new(
                () => SpriteSettingsApplierAPI.MoveProfileAssetAction,
                action => SpriteSettingsApplierAPI.MoveProfileAssetAction = action
            );
            using (
                move.Borrow(
                    (stagingPath, destinationPath) =>
                    {
                        string moveError = AssetDatabase.MoveAsset(stagingPath, destinationPath);
                        Assert.IsEmpty(moveError);
                        File.WriteAllBytes(fullPath, laterBytes);
                        return moveError;
                    }
                )
            )
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(
                        ProfilePath,
                        new List<SpriteSettings> { new SpriteSettings() },
                        false,
                        out string error
                    )
                );
                StringAssert.Contains("inspect the destination", error);
            }
            SpriteSettingsProfileCollection persisted =
                AssetDatabase.LoadAssetAtPath<SpriteSettingsProfileCollection>(ProfilePath);
            Assert.IsTrue(persisted != null);
            Assert.AreEqual("Later", persisted.profiles[0].name);
            Assert.AreEqual(0, StagedAssetFiles().Length);
        }

        [Test]
        public void AnAbsentDestinationRecoveryPreservesALateCollision()
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
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                ProfilePath
            );
            byte[] originalBytes = File.ReadAllBytes(fullPath);
            byte[] laterBytes = { 6, 7, 8, 9 };
            RestorableGlobal<Action<string>> swap = new(
                () => DurableFile.BeforeStagedSwapForTests,
                action => DurableFile.BeforeStagedSwapForTests = action
            );
            RestorableGlobal<Action<string>> restore = new(
                () => SpriteSettingsApplierAPI.BeforeAbsentRestoreMoveForTests,
                action => SpriteSettingsApplierAPI.BeforeAbsentRestoreMoveForTests = action
            );
            using (
                swap.Borrow(_ =>
                {
                    File.Delete(fullPath);
                    throw new IOException("forced replacement failure");
                })
            )
            using (restore.Borrow(_ => File.WriteAllBytes(fullPath, laterBytes)))
            {
                Assert.IsFalse(
                    SpriteSettingsApplierAPI.TrySaveProfiles(ProfilePath, first, true, out error)
                );
            }
            StringAssert.Contains("Could not restore the prior asset", error);
            StringAssert.Contains("Inspect restore staging file at", error);
            CollectionAssert.AreEqual(laterBytes, File.ReadAllBytes(fullPath));
            File.WriteAllBytes(fullPath, originalBytes);
            AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceSynchronousImport);
            Assert.AreEqual(0, StagedAssetFiles().Length);
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
