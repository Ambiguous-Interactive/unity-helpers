// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class PersistentDirectorySettingsMigrationTests : CommonTestBase
    {
        private const string WallstopRoot = "Assets/Resources/Wallstop Studios";

        private static readonly string[] FixtureOwnedCleanupFolders =
        {
            PersistentDirectorySettings.TargetFolder,
            PersistentDirectorySettings.LegacyFolder,
            WallstopRoot + "/Old",
            WallstopRoot + "/HasAsset",
            WallstopRoot + "/EmptyFolder",
            WallstopRoot + "/ToDelete",
            WallstopRoot + "/Branch1",
            WallstopRoot + "/Branch2",
            WallstopRoot + "/Branch3",
            WallstopRoot + "/EmptyBranch",
            WallstopRoot + "/FilledBranch",
            WallstopRoot + "/OldStuff",
            WallstopRoot + "/OtherTool",
            WallstopRoot + "/DeepEmpty",
            WallstopRoot + "/TestCleanup",
            WallstopRoot + "/Empty1",
            WallstopRoot + "/Empty2",
            WallstopRoot + "/Empty3",
        };

        private static readonly string[] FixtureOwnedParentFolders =
        {
            WallstopRoot + "/Unity Helpers",
            WallstopRoot,
        };

        private readonly List<string> _createdAssets = new();
        private readonly List<string> _createdFolders = new();
        private bool _previousEditorUiSuppress;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousEditorUiSuppress = EditorUi.Suppress;
            EditorUi.Suppress = true;
            yield return CleanupAllPersistentDirectorySettingsAssets();
        }

        [UnityTearDown]
        public override IEnumerator UnityTearDown()
        {
            yield return base.UnityTearDown();
            yield return CleanupAllPersistentDirectorySettingsAssets();
            foreach (string path in _createdAssets)
            {
                DeleteAssetIfExists(path);
                yield return null;
            }
            _createdAssets.Clear();

            foreach (string folder in _createdFolders)
            {
                DeleteFolderIfExists(folder);
                yield return null;
            }
            _createdFolders.Clear();
            EditorUi.Suppress = _previousEditorUiSuppress;
            AssetPostprocessorDeferral.FlushForTesting();
            yield return null;
        }

        [UnityTest]
        public IEnumerator MigrationCreatesNewAssetWhenNoneExist()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            yield return null;

            Assert.IsTrue(result != null, "RunMigration should return a non-null instance");

            Object asset = AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                PersistentDirectorySettings.TargetAssetPath
            );
            Assert.IsTrue(asset != null, "Asset should exist at target path after migration");
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
        }

        [UnityTest]
        public IEnumerator MigrationMovesLegacyAssetToTargetPath()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.LegacyFolder);
            _createdFolders.Add(PersistentDirectorySettings.LegacyFolder);
            yield return null;

            PersistentDirectorySettings legacy = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            legacy.RecordPath("TestTool", "Context", "Assets/TestPath");
            AssetDatabase.CreateAsset(legacy, PersistentDirectorySettings.LegacyAssetPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                    PersistentDirectorySettings.LegacyAssetPath
                ) != null,
                "Legacy asset should exist before migration"
            );

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            yield return null;

            Assert.IsTrue(result != null, "RunMigration should return a non-null instance");
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);

            PersistentDirectorySettings targetAsset =
                AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                    PersistentDirectorySettings.TargetAssetPath
                );
            Assert.IsTrue(targetAsset != null, "Asset should exist at target path after migration");

            Object legacyAsset = AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                PersistentDirectorySettings.LegacyAssetPath
            );
            Assert.IsTrue(legacyAsset == null, "Legacy asset should be deleted after migration");

            DirectoryUsageData[] paths = targetAsset.GetPaths("TestTool", "Context");
            Assert.IsTrue(paths != null, "Paths should not be null");
            Assert.AreEqual(1, paths.Length, "Should have one recorded path");
            Assert.AreEqual("Assets/TestPath", paths[0].path, "Path should be preserved");
        }

        [UnityTest]
        public IEnumerator MigrationMergesDuplicateAssets()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.TargetFolder);
            _createdFolders.Add(PersistentDirectorySettings.TargetFolder);
            EnsureFolderExists(PersistentDirectorySettings.LegacyFolder);
            _createdFolders.Add(PersistentDirectorySettings.LegacyFolder);
            yield return null;

            PersistentDirectorySettings target = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            target.RecordPath("ToolA", "ContextA", "Assets/PathA");
            AssetDatabase.CreateAsset(target, PersistentDirectorySettings.TargetAssetPath);
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            PersistentDirectorySettings legacy = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            legacy.RecordPath("ToolB", "ContextB", "Assets/PathB");
            AssetDatabase.CreateAsset(legacy, PersistentDirectorySettings.LegacyAssetPath);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            yield return null;

            Assert.IsTrue(result != null, "RunMigration should return a non-null instance");

            PersistentDirectorySettings finalAsset =
                AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                    PersistentDirectorySettings.TargetAssetPath
                );
            Assert.IsTrue(finalAsset != null, "Target asset should exist after migration");

            Object legacyAsset = AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                PersistentDirectorySettings.LegacyAssetPath
            );
            Assert.IsTrue(legacyAsset == null, "Legacy asset should be deleted after merge");

            DirectoryUsageData[] pathsA = finalAsset.GetPaths("ToolA", "ContextA");
            Assert.IsTrue(pathsA != null, "PathsA should not be null");
            Assert.AreEqual(1, pathsA.Length, "Should have one path for ToolA");
            Assert.AreEqual("Assets/PathA", pathsA[0].path, "PathA should be preserved");

            DirectoryUsageData[] pathsB = finalAsset.GetPaths("ToolB", "ContextB");
            Assert.IsTrue(pathsB != null, "PathsB should not be null");
            Assert.AreEqual(1, pathsB.Length, "Should have one path for ToolB");
            Assert.AreEqual("Assets/PathB", pathsB[0].path, "PathB should be preserved from merge");
        }

        [UnityTest]
        public IEnumerator MigrationMergesMultipleDuplicates()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.TargetFolder);
            _createdFolders.Add(PersistentDirectorySettings.TargetFolder);
            EnsureFolderExists(PersistentDirectorySettings.LegacyFolder);
            _createdFolders.Add(PersistentDirectorySettings.LegacyFolder);

            string customFolder = "Assets/Resources/Custom/Path";
            EnsureFolderExists(customFolder);
            _createdFolders.Add(customFolder);
            string customAssetPath = customFolder + "/PersistentDirectorySettings.asset";
            yield return null;

            PersistentDirectorySettings first = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            first.RecordPath("Tool1", "Ctx1", "Assets/Path1");
            AssetDatabase.CreateAsset(first, PersistentDirectorySettings.LegacyAssetPath);
            yield return null;

            PersistentDirectorySettings second = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            second.RecordPath("Tool2", "Ctx2", "Assets/Path2");
            AssetDatabase.CreateAsset(second, customAssetPath);
            _createdAssets.Add(customAssetPath);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            Assert.IsTrue(result != null, "RunMigration should return a non-null instance");

            Object legacyAsset = AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                PersistentDirectorySettings.LegacyAssetPath
            );
            Assert.IsTrue(legacyAsset == null, "Legacy asset should be deleted");

            Object customAsset = AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                customAssetPath
            );
            Assert.IsTrue(customAsset == null, "Custom location asset should be deleted");

            PersistentDirectorySettings finalAsset =
                AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                    PersistentDirectorySettings.TargetAssetPath
                );
            Assert.IsTrue(finalAsset != null, "Target asset should exist");

            DirectoryUsageData[] paths1 = finalAsset.GetPaths("Tool1", "Ctx1");
            DirectoryUsageData[] paths2 = finalAsset.GetPaths("Tool2", "Ctx2");

            Assert.IsTrue(paths1 != null, "Paths1 should exist");
            Assert.IsTrue(paths2 != null, "Paths2 should exist");
            Assert.AreEqual(1, paths1.Length, "Should have path from first duplicate");
            Assert.AreEqual(1, paths2.Length, "Should have path from second duplicate");
        }

        [UnityTest]
        public IEnumerator MigrationIsIdempotent()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            PersistentDirectorySettings first = PersistentDirectorySettings.RunMigration();
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            Assert.IsTrue(first != null, "First migration should succeed");
            first.RecordPath("TestTool", "TestContext", "Assets/TestDir");
            EditorUtility.SetDirty(first);
            AssetDatabase.SaveAssets();
            yield return null;

            string guid1 = AssetDatabase.AssetPathToGUID(
                PersistentDirectorySettings.TargetAssetPath
            );

            PersistentDirectorySettings second = PersistentDirectorySettings.RunMigration();
            yield return null;

            Assert.IsTrue(second != null, "Second migration should succeed");

            string guid2 = AssetDatabase.AssetPathToGUID(
                PersistentDirectorySettings.TargetAssetPath
            );
            Assert.AreEqual(
                guid1,
                guid2,
                "Asset GUID should remain the same after idempotent migration"
            );

            DirectoryUsageData[] paths = second.GetPaths("TestTool", "TestContext");
            Assert.IsTrue(paths != null, "Recorded data should persist");
            Assert.AreEqual(1, paths.Length, "Should still have recorded path");
            Assert.AreEqual("Assets/TestDir", paths[0].path, "Path data should be preserved");
        }

        [UnityTest]
        public IEnumerator MigrationCleansUpEmptyParentFolders()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.LegacyFolder);
            _createdFolders.Add("Assets/Resources/Wallstop Studios");
            yield return null;

            PersistentDirectorySettings legacy = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            AssetDatabase.CreateAsset(legacy, PersistentDirectorySettings.LegacyAssetPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.LegacyFolder),
                "Legacy folder should exist before migration"
            );

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            Assert.IsTrue(result != null, "Migration should succeed");

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.LegacyFolder),
                "Empty legacy folder should be deleted after migration"
            );
        }

        [UnityTest]
        public IEnumerator MigrationPreservesDataFromLegacyAsset()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.LegacyFolder);
            _createdFolders.Add(PersistentDirectorySettings.LegacyFolder);
            yield return null;

            PersistentDirectorySettings legacy = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            legacy.RecordPath("ExportTool", "Default", "Assets/Exports");
            legacy.RecordPath("ExportTool", "Default", "Assets/Exports");
            legacy.RecordPath("ExportTool", "Default", "Assets/Exports");
            legacy.RecordPath("ImportTool", "Audio", "Assets/Audio/Import");
            AssetDatabase.CreateAsset(legacy, PersistentDirectorySettings.LegacyAssetPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            Assert.IsTrue(result != null, "Migration should succeed");

            DirectoryUsageData[] exportPaths = result.GetPaths("ExportTool", "Default");
            Assert.IsTrue(exportPaths != null, "Export paths should exist");
            Assert.AreEqual(1, exportPaths.Length, "Should have one export path");
            Assert.AreEqual("Assets/Exports", exportPaths[0].path);
            Assert.AreEqual(3, exportPaths[0].count, "Usage count should be preserved");

            DirectoryUsageData[] importPaths = result.GetPaths("ImportTool", "Audio");
            Assert.IsTrue(importPaths != null, "Import paths should exist");
            Assert.AreEqual(1, importPaths.Length, "Should have one import path");
            Assert.AreEqual("Assets/Audio/Import", importPaths[0].path);
        }

        [UnityTest]
        public IEnumerator MergeSettingsCombinesToolHistories()
        {
            PersistentDirectorySettings target =
                CreateScriptableObject<PersistentDirectorySettings>();
            target.RecordPath("ToolA", "Context1", "Assets/A1");
            target.RecordPath("ToolA", "Context1", "Assets/A1");

            PersistentDirectorySettings other =
                CreateScriptableObject<PersistentDirectorySettings>();
            other.RecordPath("ToolB", "Context2", "Assets/B2");
            other.RecordPath("ToolA", "Context1", "Assets/A2");
            yield return null;

            PersistentDirectorySettings.MergeSettings(target, other);
            yield return null;

            DirectoryUsageData[] pathsA1 = target.GetPaths("ToolA", "Context1");
            Assert.IsTrue(pathsA1 != null);
            Assert.AreEqual(2, pathsA1.Length, "Should have both paths for ToolA/Context1");

            DirectoryUsageData[] pathsB = target.GetPaths("ToolB", "Context2");
            Assert.IsTrue(pathsB != null);
            Assert.AreEqual(1, pathsB.Length, "Should have path from other for ToolB");
        }

        [UnityTest]
        public IEnumerator MergeSettingsHandlesNullInputs()
        {
            PersistentDirectorySettings target =
                CreateScriptableObject<PersistentDirectorySettings>();
            target.RecordPath("Tool", "Ctx", "Assets/Path");
            yield return null;

            PersistentDirectorySettings.MergeSettings(target, null);
            PersistentDirectorySettings.MergeSettings(null, target);
            PersistentDirectorySettings.MergeSettings(target, target);
            yield return null;

            DirectoryUsageData[] paths = target.GetPaths("Tool", "Ctx");
            Assert.IsTrue(paths != null, "Data should be unchanged after null/self merge");
            Assert.AreEqual(1, paths.Length);
        }

        [UnityTest]
        public IEnumerator MergeSettingsAddsCountsForSamePath()
        {
            PersistentDirectorySettings target =
                CreateScriptableObject<PersistentDirectorySettings>();
            target.RecordPath("Tool", "Ctx", "Assets/Shared");
            target.RecordPath("Tool", "Ctx", "Assets/Shared");

            PersistentDirectorySettings other =
                CreateScriptableObject<PersistentDirectorySettings>();
            other.RecordPath("Tool", "Ctx", "Assets/Shared");
            other.RecordPath("Tool", "Ctx", "Assets/Shared");
            other.RecordPath("Tool", "Ctx", "Assets/Shared");
            yield return null;

            PersistentDirectorySettings.MergeSettings(target, other);
            yield return null;

            DirectoryUsageData[] paths = target.GetPaths("Tool", "Ctx");
            Assert.IsTrue(paths != null);
            Assert.AreEqual(1, paths.Length, "Should still be one unique path");
            Assert.AreEqual(5, paths[0].count, "Counts should be summed (2 + 3 = 5)");
        }

        [UnityTest]
        public IEnumerator TargetPathConstantsAreCorrect()
        {
            yield return null;

            Assert.AreEqual("Assets/Resources", PersistentDirectorySettings.ResourcesRoot);
            Assert.AreEqual(
                "Wallstop Studios/Unity Helpers/Editor",
                PersistentDirectorySettings.SubPath
            );
            Assert.AreEqual("Wallstop Studios/Editor", PersistentDirectorySettings.LegacySubPath);

            Assert.IsTrue(
                PersistentDirectorySettings.TargetAssetPath.EndsWith(
                    "PersistentDirectorySettings.asset"
                ),
                "Target asset path should end with PersistentDirectorySettings.asset"
            );
            Assert.IsTrue(
                PersistentDirectorySettings.TargetAssetPath.Contains(
                    "Wallstop Studios/Unity Helpers/Editor"
                ),
                "Target path should contain new subfolder"
            );
            Assert.IsTrue(
                PersistentDirectorySettings.LegacyAssetPath.Contains("Wallstop Studios/Editor"),
                "Legacy path should contain old subfolder"
            );
            Assert.IsFalse(
                PersistentDirectorySettings.LegacyAssetPath.Contains("Unity Helpers"),
                "Legacy path should NOT contain Unity Helpers"
            );
        }

        [UnityTest]
        public IEnumerator MigrationHandlesTargetExistsWithLegacyDuplicate()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.TargetFolder);
            _createdFolders.Add(PersistentDirectorySettings.TargetFolder);
            EnsureFolderExists(PersistentDirectorySettings.LegacyFolder);
            _createdFolders.Add(PersistentDirectorySettings.LegacyFolder);
            yield return null;

            PersistentDirectorySettings target = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            target.RecordPath("TargetTool", "TargetCtx", "Assets/TargetPath");
            AssetDatabase.CreateAsset(target, PersistentDirectorySettings.TargetAssetPath);
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            PersistentDirectorySettings legacy = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            legacy.RecordPath("LegacyTool", "LegacyCtx", "Assets/LegacyPath");
            AssetDatabase.CreateAsset(legacy, PersistentDirectorySettings.LegacyAssetPath);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            yield return null;

            Assert.IsTrue(result != null, "Migration should succeed");

            Object legacyCheck = AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                PersistentDirectorySettings.LegacyAssetPath
            );
            Assert.IsTrue(legacyCheck == null, "Legacy should be deleted when target exists");

            DirectoryUsageData[] targetPaths = result.GetPaths("TargetTool", "TargetCtx");
            DirectoryUsageData[] legacyPaths = result.GetPaths("LegacyTool", "LegacyCtx");

            Assert.IsTrue(targetPaths != null, "Target paths should exist");
            Assert.IsTrue(legacyPaths != null, "Legacy paths should be merged in");
            Assert.AreEqual(1, targetPaths.Length);
            Assert.AreEqual(1, legacyPaths.Length);
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersRemovesEmptyEditorFolder()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string legacyEditorFolder = "Assets/Resources/Wallstop Studios/Editor";
            EnsureFolderExists(legacyEditorFolder);
            _createdFolders.Add("Assets/Resources/Wallstop Studios");
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(legacyEditorFolder),
                "Legacy Editor folder should exist before cleanup"
            );

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(legacyEditorFolder),
                "Empty legacy Editor folder should be deleted after cleanup"
            );
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersRemovesNestedEmptyFolders()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string deepFolder = "Assets/Resources/Wallstop Studios/Old/Deep/Nested/Empty";
            EnsureFolderExists(deepFolder);
            _createdFolders.Add("Assets/Resources/Wallstop Studios");
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(deepFolder),
                "Deep nested folder should exist before cleanup"
            );

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(deepFolder),
                "Deep nested empty folder should be deleted"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios/Old"),
                "Parent empty folders should also be deleted"
            );
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersPreservesFoldersWithAssets()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string folderWithAsset = "Assets/Resources/Wallstop Studios/HasAsset";
            EnsureFolderExists(folderWithAsset);
            _createdFolders.Add("Assets/Resources/Wallstop Studios");
            yield return null;

            ScriptableObject dummy = Track(ScriptableObject.CreateInstance<ScriptableObject>());
            string dummyPath = folderWithAsset + "/DummyAsset.asset";
            AssetDatabase.CreateAsset(dummy, dummyPath);
            _createdAssets.Add(dummyPath);
            yield return null;

            string emptyFolder = "Assets/Resources/Wallstop Studios/EmptyFolder";
            EnsureFolderExists(emptyFolder);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(folderWithAsset),
                "Folder with asset should be preserved"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(emptyFolder),
                "Empty sibling folder should be deleted"
            );
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersPreservesUnityHelpersFolder()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.TargetFolder);
            _createdFolders.Add(PersistentDirectorySettings.TargetFolder);
            yield return null;

            PersistentDirectorySettings target = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            AssetDatabase.CreateAsset(target, PersistentDirectorySettings.TargetAssetPath);
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            string legacyFolder = "Assets/Resources/Wallstop Studios/Editor";
            EnsureFolderExists(legacyFolder);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.TargetFolder),
                "Unity Helpers/Editor folder should be preserved when it has assets"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(legacyFolder),
                "Empty legacy Editor folder should be deleted"
            );
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersIsIdempotent()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string emptyFolder = "Assets/Resources/Wallstop Studios/ToDelete";
            EnsureFolderExists(emptyFolder);
            _createdFolders.Add("Assets/Resources/Wallstop Studios");
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(emptyFolder),
                "Empty folder should be deleted"
            );
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersNeverDeletesWallstopStudiosRoot()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string wallstopRoot = PersistentDirectorySettings.WallstopStudiosRoot;

            EnsureFolderExists(wallstopRoot);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(wallstopRoot),
                "Setup: Wallstop Studios folder should exist before cleanup"
            );

            string[] subFoldersBefore = AssetDatabase.GetSubFolders(wallstopRoot);
            Assert.IsTrue(
                subFoldersBefore == null || subFoldersBefore.Length == 0,
                $"Setup: Wallstop Studios should be empty but has subfolders: {string.Join(", ", subFoldersBefore ?? Array.Empty<string>())}"
            );

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(wallstopRoot),
                "CRITICAL: Wallstop Studios root folder must NEVER be deleted - this is production data"
            );

            _createdFolders.Add(wallstopRoot);
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersNeverDeletesWallstopStudiosRootEvenWithOnlyEmptySubfolders()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string wallstopRoot = PersistentDirectorySettings.WallstopStudiosRoot;

            EnsureFolderExists(wallstopRoot + "/Empty1");
            EnsureFolderExists(wallstopRoot + "/Empty2/Nested");
            EnsureFolderExists(wallstopRoot + "/Empty3/Deep/Nested/Path");
            _createdFolders.Add(wallstopRoot);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(wallstopRoot),
                "Setup: Wallstop Studios folder should exist before cleanup"
            );

            string[] subFoldersBefore = AssetDatabase.GetSubFolders(wallstopRoot);
            Assert.IsTrue(
                subFoldersBefore != null && subFoldersBefore.Length == 3,
                $"Setup: Wallstop Studios should have 3 subfolders but has: {string.Join(", ", subFoldersBefore ?? Array.Empty<string>())}"
            );

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(wallstopRoot),
                "CRITICAL: Wallstop Studios root folder must NEVER be deleted even when all subfolders are empty"
            );

            string[] subFoldersAfter = AssetDatabase.GetSubFolders(wallstopRoot);
            Assert.IsTrue(
                subFoldersAfter == null || subFoldersAfter.Length == 0,
                $"All empty subfolders should be deleted but found: {string.Join(", ", subFoldersAfter ?? Array.Empty<string>())}"
            );
        }

        private static IEnumerable<int> RootCleanupInvocationCounts()
        {
            yield return 1;
            yield return 5;
            yield return 25;
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersCalledMultipleTimesNeverDeletesRoot(
            [ValueSource(nameof(RootCleanupInvocationCounts))] int cleanupInvocationCount
        )
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string wallstopRoot = PersistentDirectorySettings.WallstopStudiosRoot;

            EnsureFolderExists(wallstopRoot);
            _createdFolders.Add(wallstopRoot);
            yield return null;

            for (int i = 0; i < cleanupInvocationCount; i++)
            {
                PersistentDirectorySettings.CleanupLegacyEmptyFolders();
                yield return null;

                string diagnostics = GetWallstopRootDiagnostics();
                Assert.IsTrue(
                    AssetDatabase.IsValidFolder(wallstopRoot),
                    $"CRITICAL: Wallstop Studios root folder must NEVER be deleted (iteration {i + 1}/{cleanupInvocationCount}). "
                        + $"Diagnostics: {diagnostics}"
                );
            }
        }

        [UnityTest]
        public IEnumerator WallstopStudiosRootConstantMatchesExpectedPath()
        {
            Assert.AreEqual(
                "Assets/Resources/Wallstop Studios",
                PersistentDirectorySettings.WallstopStudiosRoot,
                "WallstopStudiosRoot constant should match the expected production path"
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersHandlesNoWallstopFolder()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            DeleteFolderIfExists(PersistentDirectorySettings.WallstopStudiosRoot);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.DoesNotThrow(
                () => PersistentDirectorySettings.CleanupLegacyEmptyFolders(),
                "CleanupLegacyEmptyFolders should not throw when Wallstop Studios folder doesn't exist"
            );
            yield return null;
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersDeletesSubfoldersButPreservesRoot()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string wallstopRoot = "Assets/Resources/Wallstop Studios";

            string[] preExistingSubFolders = AssetDatabase.IsValidFolder(wallstopRoot)
                ? AssetDatabase.GetSubFolders(wallstopRoot)
                : Array.Empty<string>();
            string preExistingInfo =
                0 < preExistingSubFolders.Length
                    ? $"Pre-existing subfolders after cleanup: {string.Join(", ", preExistingSubFolders)}"
                    : "No pre-existing subfolders after cleanup";

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(wallstopRoot),
                $"Setup failed: {wallstopRoot} should not exist after CleanupAllPersistentDirectorySettingsAssets. {preExistingInfo}"
            );

            string branch1 = "Assets/Resources/Wallstop Studios/Branch1/SubA/SubB";
            string branch2 = "Assets/Resources/Wallstop Studios/Branch2/SubC";
            string branch3 = "Assets/Resources/Wallstop Studios/Branch3";

            EnsureFolderExists(branch1);
            EnsureFolderExists(branch2);
            EnsureFolderExists(branch3);
            _createdFolders.Add(wallstopRoot);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(wallstopRoot),
                $"Setup failed: {wallstopRoot} should exist before cleanup"
            );
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(branch1),
                $"Setup failed: {branch1} should exist before cleanup"
            );

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            string[] remainingSubFolders = AssetDatabase.IsValidFolder(wallstopRoot)
                ? AssetDatabase.GetSubFolders(wallstopRoot)
                : Array.Empty<string>();
            string remainingFoldersInfo =
                0 < remainingSubFolders.Length ? string.Join(", ", remainingSubFolders) : "(none)";

            Assert.IsFalse(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios/Branch1"),
                "Branch1 should be deleted"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios/Branch2"),
                "Branch2 should be deleted"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios/Branch3"),
                "Branch3 should be deleted"
            );
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(wallstopRoot),
                $"Wallstop Studios root should be preserved even when all subfolders are empty (production protection). Remaining subfolders: {remainingFoldersInfo}"
            );
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersPreservesPartiallyFilledHierarchy()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string emptyBranch = "Assets/Resources/Wallstop Studios/EmptyBranch/Deep";
            string filledBranch = "Assets/Resources/Wallstop Studios/FilledBranch";

            EnsureFolderExists(emptyBranch);
            EnsureFolderExists(filledBranch);
            _createdFolders.Add("Assets/Resources/Wallstop Studios");
            yield return null;

            ScriptableObject dummy = Track(ScriptableObject.CreateInstance<ScriptableObject>());
            string dummyPath = filledBranch + "/KeepMe.asset";
            AssetDatabase.CreateAsset(dummy, dummyPath);
            _createdAssets.Add(dummyPath);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(emptyBranch),
                "Empty branch should be deleted"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios/EmptyBranch"),
                "Empty branch parent should be deleted"
            );
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(filledBranch),
                "Filled branch should be preserved"
            );
            Assert.IsTrue(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios"),
                "Wallstop Studios root should be preserved when it has non-empty children"
            );
        }

        [UnityTest]
        public IEnumerator MigrationAndCleanupRunTogetherSuccessfully()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.LegacyFolder);
            _createdFolders.Add("Assets/Resources/Wallstop Studios");
            yield return null;

            string extraEmptyFolder = "Assets/Resources/Wallstop Studios/OldStuff/Obsolete";
            EnsureFolderExists(extraEmptyFolder);
            yield return null;

            PersistentDirectorySettings legacy = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            legacy.RecordPath("Tool", "Ctx", "Assets/Path");
            AssetDatabase.CreateAsset(legacy, PersistentDirectorySettings.LegacyAssetPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsTrue(result != null, "Migration should succeed");

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.LegacyFolder),
                "Legacy folder should be cleaned up"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(extraEmptyFolder),
                "Extra empty folder should be cleaned up"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios/OldStuff"),
                "OldStuff folder should be cleaned up"
            );

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.TargetFolder),
                "Target folder should still exist"
            );

            DirectoryUsageData[] paths = result.GetPaths("Tool", "Ctx");
            Assert.IsTrue(paths != null);
            Assert.AreEqual(1, paths.Length);
        }

        [UnityTest]
        public IEnumerator MigrationCleansUpLegacyFolderAfterMove()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.LegacyFolder);
            _createdFolders.Add("Assets/Resources/Wallstop Studios");
            yield return null;

            PersistentDirectorySettings legacy = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            AssetDatabase.CreateAsset(legacy, PersistentDirectorySettings.LegacyAssetPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.LegacyFolder),
                "Legacy folder should exist before migration"
            );

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            Assert.IsTrue(result != null, "Migration should succeed");
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.LegacyFolder),
                "Legacy folder should be cleaned up after move"
            );
        }

        [UnityTest]
        public IEnumerator MigrationCleansUpLegacyFolderAfterMergeAndDelete()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            EnsureFolderExists(PersistentDirectorySettings.TargetFolder);
            _createdFolders.Add(PersistentDirectorySettings.TargetFolder);
            EnsureFolderExists(PersistentDirectorySettings.LegacyFolder);
            _createdFolders.Add("Assets/Resources/Wallstop Studios/Editor");
            yield return null;

            PersistentDirectorySettings target = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            target.RecordPath("TargetTool", "Ctx", "Assets/TargetPath");
            AssetDatabase.CreateAsset(target, PersistentDirectorySettings.TargetAssetPath);
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            PersistentDirectorySettings legacy = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            legacy.RecordPath("LegacyTool", "Ctx", "Assets/LegacyPath");
            AssetDatabase.CreateAsset(legacy, PersistentDirectorySettings.LegacyAssetPath);
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.LegacyFolder),
                "Legacy folder should exist before migration"
            );

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            yield return null;

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsTrue(result != null, "Migration should succeed");
            Assert.IsFalse(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.LegacyFolder),
                "Legacy folder should be cleaned up after merge"
            );

            DirectoryUsageData[] targetPaths = result.GetPaths("TargetTool", "Ctx");
            DirectoryUsageData[] legacyPaths = result.GetPaths("LegacyTool", "Ctx");
            Assert.IsTrue(targetPaths != null);
            Assert.IsTrue(legacyPaths != null);
            Assert.AreEqual(1, targetPaths.Length, "Target paths should be preserved");
            Assert.AreEqual(1, legacyPaths.Length, "Legacy paths should be merged");
        }

        [UnityTest]
        public IEnumerator FullMigrationWorkflowFromLegacyState()
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            string[] legacyFolders = new[]
            {
                PersistentDirectorySettings.LegacyFolder,
                "Assets/Resources/Wallstop Studios/OtherTool",
                "Assets/Resources/Wallstop Studios/DeepEmpty/Nested/Path",
            };

            foreach (string folder in legacyFolders)
            {
                EnsureFolderExists(folder);
            }
            _createdFolders.Add("Assets/Resources/Wallstop Studios");
            yield return null;

            PersistentDirectorySettings legacy = Track(
                ScriptableObject.CreateInstance<PersistentDirectorySettings>()
            );
            legacy.RecordPath("ExportTool", "Sprites", "Assets/Sprites/Export");
            legacy.RecordPath("ExportTool", "Sprites", "Assets/Sprites/Export");
            legacy.RecordPath("ExportTool", "Audio", "Assets/Audio/Export");
            legacy.RecordPath("ImportTool", "Default", "Assets/Import");
            AssetDatabase.CreateAsset(legacy, PersistentDirectorySettings.LegacyAssetPath);
            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            PersistentDirectorySettings result = PersistentDirectorySettings.RunMigration();
            _createdAssets.Add(PersistentDirectorySettings.TargetAssetPath);
            yield return null;

            PersistentDirectorySettings.CleanupLegacyEmptyFolders();
            yield return null;

            Assert.IsTrue(result != null, "Migration should succeed");

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.TargetFolder),
                "Target folder should exist"
            );
            Assert.IsTrue(
                AssetDatabase.LoadAssetAtPath<PersistentDirectorySettings>(
                    PersistentDirectorySettings.TargetAssetPath
                ) != null,
                "Target asset should exist"
            );

            Assert.IsFalse(
                AssetDatabase.IsValidFolder(PersistentDirectorySettings.LegacyFolder),
                "Legacy folder should not exist"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios/OtherTool"),
                "OtherTool folder should not exist"
            );
            Assert.IsFalse(
                AssetDatabase.IsValidFolder("Assets/Resources/Wallstop Studios/DeepEmpty"),
                "DeepEmpty folder should not exist"
            );

            DirectoryUsageData[] spritePaths = result.GetPaths("ExportTool", "Sprites");
            Assert.IsTrue(spritePaths != null);
            Assert.AreEqual(1, spritePaths.Length);
            Assert.AreEqual("Assets/Sprites/Export", spritePaths[0].path);
            Assert.AreEqual(2, spritePaths[0].count, "Usage count should be preserved");

            DirectoryUsageData[] audioPaths = result.GetPaths("ExportTool", "Audio");
            Assert.IsTrue(audioPaths != null);
            Assert.AreEqual(1, audioPaths.Length);

            DirectoryUsageData[] importPaths = result.GetPaths("ImportTool", "Default");
            Assert.IsTrue(importPaths != null);
            Assert.AreEqual(1, importPaths.Length);
        }

        private IEnumerator CleanupAllPersistentDirectorySettingsAssets()
        {
            string beforeDiagnostics = GetWallstopRootDiagnostics();

            string[] guids = AssetDatabase.FindAssets("t:" + nameof(PersistentDirectorySettings));
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.DeleteAsset(path);
                    yield return null;
                }
            }

            for (int i = 0; i < FixtureOwnedCleanupFolders.Length; i++)
            {
                DeleteFolderIfExists(FixtureOwnedCleanupFolders[i]);
                yield return null;
            }

            for (int i = 0; i < FixtureOwnedParentFolders.Length; i++)
            {
                TryDeleteEmptyFolder(FixtureOwnedParentFolders[i]);
                yield return null;
            }

            SaveAndRefreshFixtureAssets();
            yield return null;

            string remainingFixtureFolders = GetExistingFixtureOwnedFolderDiagnostics();
            string afterDiagnostics = GetWallstopRootDiagnostics();
            string unexpectedSubFolders = GetUnexpectedWallstopSubFolderDiagnostics();
            if (!string.Equals(remainingFixtureFolders, "(none)", StringComparison.Ordinal))
            {
                TestContext.WriteLine(
                    "CleanupAllPersistentDirectorySettingsAssets left fixture folders behind. "
                        + $"Before: {beforeDiagnostics}. "
                        + $"After: {afterDiagnostics}. "
                        + $"Remaining: {remainingFixtureFolders}"
                );
            }

            if (!string.Equals(unexpectedSubFolders, "(none)", StringComparison.Ordinal))
            {
                TestContext.WriteLine(
                    "CleanupAllPersistentDirectorySettingsAssets observed unexpected Wallstop root subfolders. "
                        + $"Unexpected: {unexpectedSubFolders}. "
                        + $"After: {afterDiagnostics}"
                );
            }
        }

        private static void SaveAndRefreshFixtureAssets()
        {
            AssetDatabase.SaveAssets();
            AssetPostprocessorDeferral.FlushForTesting();
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            AssetPostprocessorDeferral.FlushForTesting();
        }

        private static void EnsureFolderExists(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            folderPath = folderPath.SanitizePath();
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] parts = folderPath.Split('/');
            if (parts.Length == 0)
            {
                return;
            }

            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            Object existing = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void DeleteFolderIfExists(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            if (AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.DeleteAsset(folderPath);
            }
        }

        private static void TryDeleteEmptyFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(folderPath);
            if (subFolders != null && 0 < subFolders.Length)
            {
                return;
            }

            string[] assets = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            if (assets != null && 0 < assets.Length)
            {
                return;
            }

            AssetDatabase.DeleteAsset(folderPath);
        }

        private static string GetWallstopRootDiagnostics()
        {
            if (!AssetDatabase.IsValidFolder(WallstopRoot))
            {
                return WallstopRoot + " [missing]";
            }

            string[] subFolders = AssetDatabase.GetSubFolders(WallstopRoot);
            string subFolderList =
                subFolders != null && 0 < subFolders.Length
                    ? string.Join(", ", subFolders)
                    : "(none)";

            string[] assets = AssetDatabase.FindAssets(string.Empty, new[] { WallstopRoot });
            int assetCount = assets != null ? assets.Length : 0;

            return WallstopRoot + $" [subfolders: {subFolderList}] [assetGuids: {assetCount}]";
        }

        private static string GetExistingFixtureOwnedFolderDiagnostics()
        {
            List<string> existing = new();
            for (int i = 0; i < FixtureOwnedCleanupFolders.Length; i++)
            {
                string folderPath = FixtureOwnedCleanupFolders[i];
                if (AssetDatabase.IsValidFolder(folderPath))
                {
                    existing.Add(folderPath);
                }
            }

            return 0 < existing.Count ? string.Join(", ", existing) : "(none)";
        }

        private static string GetUnexpectedWallstopSubFolderDiagnostics()
        {
            if (!AssetDatabase.IsValidFolder(WallstopRoot))
            {
                return "(none)";
            }

            string[] subFolders = AssetDatabase.GetSubFolders(WallstopRoot);
            if (subFolders == null || subFolders.Length == 0)
            {
                return "(none)";
            }

            List<string> unexpected = new();
            for (int i = 0; i < subFolders.Length; i++)
            {
                string subFolder = subFolders[i];
                if (
                    string.Equals(
                        subFolder,
                        WallstopRoot + "/Unity Helpers",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    continue;
                }

                bool isFixtureOwned = false;
                string prefix = subFolder + "/";
                for (int j = 0; j < FixtureOwnedCleanupFolders.Length; j++)
                {
                    string fixtureFolder = FixtureOwnedCleanupFolders[j];
                    if (string.Equals(subFolder, fixtureFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        isFixtureOwned = true;
                        break;
                    }

                    if (fixtureFolder.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        isFixtureOwned = true;
                        break;
                    }
                }

                if (!isFixtureOwned)
                {
                    unexpected.Add(subFolder);
                }
            }

            return 0 < unexpected.Count ? string.Join(", ", unexpected) : "(none)";
        }

        private static IEnumerable<CleanupScenario> CleanupScenarios()
        {
            // Note: These tests use TestCleanup subfolder to avoid interfering with real production data
            // in Unity Helpers folder. The Wallstop Studios root is always preserved since Unity Helpers exists.
            yield return new CleanupScenario(
                "Single empty folder is deleted",
                new[] { "Assets/Resources/Wallstop Studios/TestCleanup/Empty" },
                Array.Empty<string>(),
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/Empty",
                    "Assets/Resources/Wallstop Studios/TestCleanup",
                },
                new[] { "Assets/Resources/Wallstop Studios" }
            );

            yield return new CleanupScenario(
                "Deeply nested empty folders are all deleted",
                new[] { "Assets/Resources/Wallstop Studios/TestCleanup/A/B/C/D/E" },
                Array.Empty<string>(),
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/A/B/C/D/E",
                    "Assets/Resources/Wallstop Studios/TestCleanup/A/B/C/D",
                    "Assets/Resources/Wallstop Studios/TestCleanup/A/B/C",
                    "Assets/Resources/Wallstop Studios/TestCleanup/A/B",
                    "Assets/Resources/Wallstop Studios/TestCleanup/A",
                    "Assets/Resources/Wallstop Studios/TestCleanup",
                },
                new[] { "Assets/Resources/Wallstop Studios" }
            );

            yield return new CleanupScenario(
                "Multiple parallel empty branches are all deleted",
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchA/SubA",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchB/SubB",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchC",
                },
                Array.Empty<string>(),
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchA",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchB",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchC",
                    "Assets/Resources/Wallstop Studios/TestCleanup",
                },
                new[] { "Assets/Resources/Wallstop Studios" }
            );

            yield return new CleanupScenario(
                "Folder with asset is preserved while empty siblings deleted",
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/WithAsset",
                    "Assets/Resources/Wallstop Studios/TestCleanup/Empty",
                },
                new[] { "Assets/Resources/Wallstop Studios/TestCleanup/WithAsset/Keep.asset" },
                new[] { "Assets/Resources/Wallstop Studios/TestCleanup/Empty" },
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/WithAsset",
                    "Assets/Resources/Wallstop Studios/TestCleanup",
                    "Assets/Resources/Wallstop Studios",
                }
            );

            yield return new CleanupScenario(
                "Asset in deep subfolder preserves all ancestor folders",
                new[] { "Assets/Resources/Wallstop Studios/TestCleanup/Deep/Nested/Folder" },
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/Deep/Nested/Folder/Asset.asset",
                },
                Array.Empty<string>(),
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/Deep/Nested/Folder",
                    "Assets/Resources/Wallstop Studios/TestCleanup/Deep/Nested",
                    "Assets/Resources/Wallstop Studios/TestCleanup/Deep",
                    "Assets/Resources/Wallstop Studios/TestCleanup",
                    "Assets/Resources/Wallstop Studios",
                }
            );

            yield return new CleanupScenario(
                "Empty folder with non-empty sibling preserves parent",
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/NonEmpty/Child",
                    "Assets/Resources/Wallstop Studios/TestCleanup/Empty",
                },
                new[] { "Assets/Resources/Wallstop Studios/TestCleanup/NonEmpty/Child/Data.asset" },
                new[] { "Assets/Resources/Wallstop Studios/TestCleanup/Empty" },
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/NonEmpty/Child",
                    "Assets/Resources/Wallstop Studios/TestCleanup/NonEmpty",
                    "Assets/Resources/Wallstop Studios/TestCleanup",
                    "Assets/Resources/Wallstop Studios",
                }
            );

            yield return new CleanupScenario(
                "Multiple parallel branches all deleted when all empty",
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchX/SubX1/SubX2",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchY/SubY1",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchZ",
                },
                Array.Empty<string>(),
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchX/SubX1/SubX2",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchX/SubX1",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchX",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchY/SubY1",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchY",
                    "Assets/Resources/Wallstop Studios/TestCleanup/BranchZ",
                    "Assets/Resources/Wallstop Studios/TestCleanup",
                },
                new[] { "Assets/Resources/Wallstop Studios" }
            );

            yield return new CleanupScenario(
                "Repeated cleanup invocations stay idempotent",
                new[] { "Assets/Resources/Wallstop Studios/TestCleanup/Repeat/Leaf" },
                Array.Empty<string>(),
                new[]
                {
                    "Assets/Resources/Wallstop Studios/TestCleanup/Repeat/Leaf",
                    "Assets/Resources/Wallstop Studios/TestCleanup/Repeat",
                    "Assets/Resources/Wallstop Studios/TestCleanup",
                },
                new[] { "Assets/Resources/Wallstop Studios" },
                cleanupInvocationCount: 4
            );
        }

        [UnityTest]
        public IEnumerator CleanupLegacyEmptyFoldersDataDriven(
            [ValueSource(nameof(CleanupScenarios))] CleanupScenario scenario
        )
        {
            yield return CleanupAllPersistentDirectorySettingsAssets();

            foreach (string folder in scenario.FoldersToCreate)
            {
                EnsureFolderExists(folder);
            }
            _createdFolders.Add("Assets/Resources/Wallstop Studios/TestCleanup");
            yield return null;

            foreach (string assetPath in scenario.AssetPaths)
            {
                string directory = Path.GetDirectoryName(assetPath)?.SanitizePath();
                if (!string.IsNullOrEmpty(directory))
                {
                    EnsureFolderExists(directory);
                }
                ScriptableObject dummy = Track(ScriptableObject.CreateInstance<ScriptableObject>());
                AssetDatabase.CreateAsset(dummy, assetPath);
                _createdAssets.Add(assetPath);
            }
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string[] subFoldersBeforeCleanup = AssetDatabase.IsValidFolder(
                "Assets/Resources/Wallstop Studios"
            )
                ? AssetDatabase.GetSubFolders("Assets/Resources/Wallstop Studios")
                : Array.Empty<string>();
            string diagnosticInfo =
                $"[{scenario.Description}] Before cleanup - "
                + $"SubFolders: [{string.Join(", ", subFoldersBeforeCleanup)}], "
                + $"FoldersCreated: [{string.Join(", ", scenario.FoldersToCreate)}], "
                + $"AssetsCreated: [{string.Join(", ", scenario.AssetPaths)}]";

            for (int i = 0; i < scenario.CleanupInvocationCount; i++)
            {
                PersistentDirectorySettings.CleanupLegacyEmptyFolders();
                yield return null;
            }

            string[] subFoldersAfterCleanup = AssetDatabase.IsValidFolder(
                "Assets/Resources/Wallstop Studios"
            )
                ? AssetDatabase.GetSubFolders("Assets/Resources/Wallstop Studios")
                : Array.Empty<string>();

            foreach (string expectedDeleted in scenario.ExpectedDeleted)
            {
                Assert.IsFalse(
                    AssetDatabase.IsValidFolder(expectedDeleted),
                    $"[{scenario.Description}] Folder should be deleted: {expectedDeleted}. "
                        + $"{diagnosticInfo}. "
                        + $"CleanupInvocationCount: {scenario.CleanupInvocationCount}. "
                        + $"After cleanup - SubFolders: [{string.Join(", ", subFoldersAfterCleanup)}]"
                );
            }

            foreach (string expectedPreserved in scenario.ExpectedPreserved)
            {
                Assert.IsTrue(
                    AssetDatabase.IsValidFolder(expectedPreserved),
                    $"[{scenario.Description}] Folder should be preserved: {expectedPreserved}. "
                        + $"{diagnosticInfo}. "
                        + $"CleanupInvocationCount: {scenario.CleanupInvocationCount}. "
                        + $"After cleanup - SubFolders: [{string.Join(", ", subFoldersAfterCleanup)}]"
                );
            }
        }

        public sealed class CleanupScenario
        {
            public string Description { get; }
            public string[] FoldersToCreate { get; }
            public string[] AssetPaths { get; }
            public string[] ExpectedDeleted { get; }
            public string[] ExpectedPreserved { get; }
            public int CleanupInvocationCount { get; }

            public CleanupScenario(
                string description,
                string[] foldersToCreate,
                string[] assetPaths,
                string[] expectedDeleted,
                string[] expectedPreserved,
                int cleanupInvocationCount = 1
            )
            {
                Description = description;
                FoldersToCreate = foldersToCreate ?? Array.Empty<string>();
                AssetPaths = assetPaths ?? Array.Empty<string>();
                ExpectedDeleted = expectedDeleted ?? Array.Empty<string>();
                ExpectedPreserved = expectedPreserved ?? Array.Empty<string>();
                CleanupInvocationCount = 0 < cleanupInvocationCount ? cleanupInvocationCount : 1;
            }

            public override string ToString() => Description;
        }
    }
#endif
}
