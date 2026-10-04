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
        public void MissingDestinationRecoveryRestoresOriginalBytes()
        {
            List<SpriteSettings> profiles = new() { new SpriteSettings { name = "First" } };
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    profiles,
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
            File.Delete(fullPath);
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryRestoreAbsentProfileBytes(
                    fullPath,
                    originalBytes,
                    out Exception restoreError
                ),
                restoreError?.Message
            );
            CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(fullPath));
            AssetDatabase.ImportAsset(ProfilePath, ImportAssetOptions.ForceSynchronousImport);
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
        public void RecoveryPreservesAnExistingDestination()
        {
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                Root,
                "collision.bytes"
            );
            byte[] laterBytes = { 6, 7, 8, 9 };
            File.WriteAllBytes(fullPath, laterBytes);
            Assert.IsFalse(
                SpriteSettingsApplierAPI.TryRestoreAbsentProfileBytes(
                    fullPath,
                    new byte[] { 1, 2, 3 },
                    out Exception error
                )
            );
            Assert.IsTrue(error != null);
            CollectionAssert.AreEqual(laterBytes, File.ReadAllBytes(fullPath));
        }

        [Test]
        public void AnOverwriteRejectsAChangedSnapshot()
        {
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                Root,
                "snapshot.bytes"
            );
            byte[] originalBytes = { 1, 2, 3 };
            byte[] laterBytes = { 6, 7, 8, 9 };
            File.WriteAllBytes(fullPath, laterBytes);
            Assert.IsFalse(
                DurableFile.TryCompareThenReplaceBytes(
                    fullPath,
                    originalBytes,
                    new byte[] { 4, 5 },
                    out Exception error
                )
            );
            Assert.IsInstanceOf<InvalidOperationException>(error);
            CollectionAssert.AreEqual(laterBytes, File.ReadAllBytes(fullPath));
        }

        [Test]
        public void APostSwapRollbackPreservesALaterWriter()
        {
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                Root,
                "rollback.bytes"
            );
            byte[] originalBytes = { 1, 2, 3 };
            byte[] committedBytes = { 4, 5 };
            byte[] laterBytes = { 6, 7, 8, 9 };
            File.WriteAllBytes(fullPath, originalBytes);
            Assert.IsTrue(
                DurableFile.TryCompareThenReplaceBytes(
                    fullPath,
                    originalBytes,
                    committedBytes,
                    out Exception error
                ),
                error?.Message
            );
            File.WriteAllBytes(fullPath, laterBytes);
            Assert.IsFalse(
                DurableFile.TryCompareThenReplaceBytes(
                    fullPath,
                    committedBytes,
                    originalBytes,
                    out error
                )
            );
            CollectionAssert.AreEqual(laterBytes, File.ReadAllBytes(fullPath));
        }

        [Test]
        public void ExclusivePublicationPreservesALateDestinationCollision()
        {
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                Root,
                "publish.bytes"
            );
            string stagedPath = fullPath + ".staged";
            byte[] stagedBytes = { 1, 2, 3 };
            byte[] laterBytes = { 6, 7, 8, 9 };
            File.WriteAllBytes(stagedPath, stagedBytes);
            File.WriteAllBytes(fullPath, laterBytes);
            Assert.IsFalse(
                DurableFile.TryPublishStagedFileWithoutOverwrite(
                    stagedPath,
                    fullPath,
                    out bool leavesStaged
                )
            );
            Assert.IsTrue(leavesStaged);
            CollectionAssert.AreEqual(laterBytes, File.ReadAllBytes(fullPath));
            CollectionAssert.AreEqual(stagedBytes, File.ReadAllBytes(stagedPath));
        }

        [Test]
        public void OwnershipRefusalPreservesAReusedStagingPath()
        {
            string fullPath = Path.Combine(
                Path.GetDirectoryName(Application.dataPath),
                Root,
                "reuse.bytes"
            );
            string stagedPath = fullPath + ".tmp";
            byte[] originalBytes = { 1, 2, 3 };
            byte[] stagedBytes = { 6, 7, 8, 9 };
            File.WriteAllBytes(fullPath, originalBytes);
            Assert.IsTrue(
                DurableFile.TryOpenStagingOwnership(
                    stagedPath,
                    out FileStream ownership,
                    out Exception error
                ),
                error?.Message
            );
            using (ownership)
            {
                File.WriteAllBytes(stagedPath, stagedBytes);
                Assert.IsFalse(
                    DurableFile.TryCompareThenReplaceBytes(
                        fullPath,
                        originalBytes,
                        new byte[] { 4, 5 },
                        out error
                    )
                );
                CollectionAssert.AreEqual(originalBytes, File.ReadAllBytes(fullPath));
                CollectionAssert.AreEqual(stagedBytes, File.ReadAllBytes(stagedPath));
            }
        }

        [TestCase("Expected", true)]
        [TestCase("Later", false)]
        public void CommittedProfilesMustMatchExpectedContent(string committedName, bool expected)
        {
            SpriteSettingsProfileCollection committed = Track(
                ScriptableObject.CreateInstance<SpriteSettingsProfileCollection>()
            );
            committed.profiles = new() { new SpriteSettings { name = committedName } };
            string expectedJson =
                WallstopStudios.UnityHelpers.Core.Serialization.Serializer.JsonStringify(
                    new List<SpriteSettings> { new() { name = "Expected" } }
                );
            Assert.AreEqual(
                expected,
                SpriteSettingsApplierAPI.ProfilesMatchExpected(committed, 1, expectedJson)
            );
            Assert.IsFalse(
                SpriteSettingsApplierAPI.ProfilesMatchExpected(committed, 2, expectedJson)
            );
            Assert.IsFalse(SpriteSettingsApplierAPI.ProfilesMatchExpected(null, 1, expectedJson));
        }

        [Test]
        public void SuccessfulStagingCleanupRemovesOnlyTheStagedAsset()
        {
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TrySaveProfiles(
                    ProfilePath,
                    new List<SpriteSettings> { new() { name = "Original" } },
                    false,
                    out string error
                ),
                error
            );
            string stagedPath = Root + "/owned.staging.asset";
            SpriteSettingsProfileCollection staged = Track(
                ScriptableObject.CreateInstance<SpriteSettingsProfileCollection>()
            );
            AssetDatabase.CreateAsset(staged, stagedPath);
            Assert.IsTrue(
                SpriteSettingsApplierAPI.TryDeleteProfileAsset(stagedPath, out error),
                error
            );
            Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(stagedPath) == null);
            Assert.IsTrue(AssetDatabase.LoadMainAssetAtPath(ProfilePath) != null);
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
