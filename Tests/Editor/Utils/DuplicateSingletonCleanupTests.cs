// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class DuplicateSingletonCleanupTests : CommonTestBase
    {
        private const string TestRoot = "Assets/Resources/DuplicateCleanupTests";
        private const string NestedFolder = "Assets/Resources/DuplicateCleanupTests/Nested";
        private const string DeeplyNestedFolder =
            "Assets/Resources/DuplicateCleanupTests/Nested/Deep";
        private bool _previousEditorUiSuppress;
        private bool _previousIgnoreCompilationState;
        private bool _cleanedUp;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            CleanupAllKnownTestFolders();
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _cleanedUp = false;
            _previousEditorUiSuppress = EditorUi.Suppress;
            EditorUi.Suppress = true;
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = true;
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = true;
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = true;
            // Unity may report isCompiling/isUpdating during a test run after AssetDatabase operations.
            _previousIgnoreCompilationState =
                ScriptableObjectSingletonCreator.IgnoreCompilationState;
            ScriptableObjectSingletonCreator.IgnoreCompilationState = true;
            ScriptableObjectSingletonCreator.TypeFilter = static type =>
                type == typeof(CleanupEnabledSingleton)
                || type == typeof(CleanupDisabledSingleton)
                || type == typeof(CleanupWithDataSingleton);

            // A stale asset from an earlier test would otherwise be found by EnsureSingletonAssets.
            CleanupAllTestAssetsAndFolders();

            EnsureFolder("Assets/Resources");
            EnsureFolder(TestRoot);
            // Ensure the metadata folder exists to prevent modal dialogs
            EnsureFolder("Assets/Resources/Wallstop Studios/Unity Helpers");
            ScriptableObjectSingletonCreator.ResetRetryStateForTests();
        }

        [TearDown]
        public override void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            CleanupTestAssets();

            ScriptableObjectSingletonCreator.TypeFilter = null;
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = false;
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = false;
            ScriptableObjectSingletonCreator.IgnoreCompilationState =
                _previousIgnoreCompilationState;
            ScriptableObjectSingletonCreator.ResetRetryStateForTests();

            base.TearDown();
            EditorUi.Suppress = _previousEditorUiSuppress;
        }

        [UnityTearDown]
        public override IEnumerator UnityTearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            // Before base teardown: the assets must go before any tracked object is destroyed.
            CleanupTestAssets();
            yield return null;

            ScriptableObjectSingletonCreator.TypeFilter = null;
            ScriptableObjectSingletonCreator.IncludeTestAssemblies = false;
            ScriptableObjectSingletonCreator.DisableAutomaticRetries = false;
            ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression = false;
            ScriptableObjectSingletonCreator.IgnoreCompilationState =
                _previousIgnoreCompilationState;
            ScriptableObjectSingletonCreator.ResetRetryStateForTests();

            IEnumerator baseEnumerator = base.UnityTearDown();
            while (baseEnumerator.MoveNext())
            {
                yield return baseEnumerator.Current;
            }

            EditorUi.Suppress = _previousEditorUiSuppress;

            CleanupAllKnownTestFolders();
        }

        public override void OneTimeTearDown()
        {
            base.OneTimeTearDown();

            CleanupAllKnownTestFolders();
        }

        private void CleanupTestAssets()
        {
            // Avoid double cleanup
            if (_cleanedUp)
            {
                return;
            }
            _cleanedUp = true;

            CleanupAllTestAssetsAndFolders();
        }

        /// <summary>
        /// Aggressively cleans up all test assets and folders.
        /// Can be called multiple times safely (from SetUp and TearDown).
        /// IMPORTANT: This also cleans up duplicate folders that Unity may create.
        /// </summary>
        private static void CleanupAllTestAssetsAndFolders()
        {
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            // Folder deletion can race the scan, so check validity immediately before FindAssets.
            TryDeleteAssetsInFolder(DeeplyNestedFolder);
            TryDeleteAssetsInFolder(NestedFolder);
            TryDeleteAssetsInFolder(TestRoot);

            TryDeleteFolder(DeeplyNestedFolder);
            TryDeleteFolder(NestedFolder);
            TryDeleteFolder(TestRoot);

            // Unity sometimes creates "DuplicateCleanupTests 1", "DuplicateCleanupTests 2", and so on.
            CleanupDuplicateTestFolders();

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string resourcesFolder = Path.Combine(projectRoot, "Assets", "Resources");
                if (Directory.Exists(resourcesFolder))
                {
                    try
                    {
                        foreach (
                            string dir in Directory.GetDirectories(
                                resourcesFolder,
                                "DuplicateCleanupTests*"
                            )
                        )
                        {
                            try
                            {
                                Directory.Delete(dir, recursive: true);
                            }
                            catch
                            {
                                // Ignore - folder may be locked
                            }
                        }
                    }
                    catch { }
                }
            }

            AssetDatabaseBatchHelper.RefreshIfNotBatching();
        }

        /// <summary>
        /// Cleans up any duplicate test folders in Assets/Resources that match our test pattern.
        /// </summary>
        private static void CleanupDuplicateTestFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                return;
            }

            string[] subFolders = AssetDatabase.GetSubFolders("Assets/Resources");
            if (subFolders == null)
            {
                return;
            }

            foreach (string folder in subFolders)
            {
                string folderName = Path.GetFileName(folder);
                if (
                    folderName != null
                    && folderName.StartsWith("DuplicateCleanupTests", StringComparison.Ordinal)
                )
                {
                    TryDeleteAssetsInFolder(folder);

                    DeleteFolderRecursive(folder);

                    TryDeleteFolder(folder);
                }
            }
        }

        /// <summary>
        /// Recursively deletes all subfolders within a folder.
        /// </summary>
        private static void DeleteFolderRecursive(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(folderPath);
            if (subFolders != null)
            {
                foreach (string sub in subFolders)
                {
                    DeleteFolderRecursive(sub);
                    TryDeleteFolder(sub);
                }
            }
        }

        private static void TryDeleteAssetsInFolder(string folderPath)
        {
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] guids;
            try
            {
                guids = AssetDatabase.FindAssets("t:Object", new[] { folderPath });
            }
            catch
            {
                // Folder may have been deleted between check and FindAssets
                return;
            }

            if (guids == null || guids.Length == 0)
            {
                return;
            }

            bool deletedAny = false;
            // Callers refresh after the delete batch, so disposal must not refresh again.
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsValidFolder(path))
                    {
                        if (AssetDatabase.DeleteAsset(path))
                        {
                            deletedAny = true;
                        }
                    }
                }
            }

            if (deletedAny)
            {
                AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            }
        }

        [UnityTest]
        public IEnumerator CleanupRemovesIdenticalDuplicateWhenOptedIn()
        {
            EnsureFolder(TestRoot);
            EnsureFolder(NestedFolder);

            string canonicalPath = TestRoot + "/CleanupEnabledSingleton.asset";
            string duplicatePath = NestedFolder + "/CleanupEnabledSingleton.asset";

            // Don't use Track() - these become persistent assets managed by CleanupTestAssets()
            CleanupEnabledSingleton canonical =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupEnabledSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(canonicalPath),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath),
                Is.Not.Null
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(canonicalPath),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath),
                Is.Null,
                "Duplicate should be removed when identical and opt-in attribute present"
            );
        }

        [UnityTest]
        public IEnumerator CleanupDoesNotRemoveDuplicateWhenNotOptedIn()
        {
            EnsureFolder(TestRoot);
            EnsureFolder(NestedFolder);

            string canonicalPath = TestRoot + "/CleanupDisabledSingleton.asset";
            string duplicatePath = NestedFolder + "/CleanupDisabledSingleton.asset";

            // Don't use Track() - these become persistent assets managed by CleanupTestAssets()
            CleanupDisabledSingleton canonical =
                ScriptableObject.CreateInstance<CleanupDisabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupDisabledSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupDisabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupDisabledSingleton>(canonicalPath),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupDisabledSingleton>(duplicatePath),
                Is.Not.Null
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupDisabledSingleton>(canonicalPath),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupDisabledSingleton>(duplicatePath),
                Is.Not.Null,
                "Duplicate should NOT be removed when opt-in attribute is missing"
            );
        }

        [UnityTest]
        public IEnumerator CleanupPreservesDuplicateWithDifferentContent()
        {
            EnsureFolder(TestRoot);
            EnsureFolder(NestedFolder);

            string canonicalPath = TestRoot + "/CleanupWithDataSingleton.asset";
            string duplicatePath = NestedFolder + "/CleanupWithDataSingleton.asset";

            // Don't use Track() - these become persistent assets managed by CleanupTestAssets()
            CleanupWithDataSingleton canonical =
                ScriptableObject.CreateInstance<CleanupWithDataSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            canonical.TestValue = 42;
            canonical.TestString = "canonical";
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupWithDataSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupWithDataSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            duplicate.TestValue = 99;
            duplicate.TestString = "different";
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupWithDataSingleton>(canonicalPath),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupWithDataSingleton>(duplicatePath),
                Is.Not.Null
            );

            LogAssert.Expect(
                LogType.Warning,
                new Regex(
                    "Found duplicate singleton asset.*CleanupWithDataSingleton.*different content",
                    RegexOptions.IgnoreCase
                )
            );
            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupWithDataSingleton>(canonicalPath),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupWithDataSingleton>(duplicatePath),
                Is.Not.Null,
                "Duplicate with different content should be preserved (requires manual resolution)"
            );
        }

        [UnityTest]
        public IEnumerator CleanupRemovesMultipleDuplicates()
        {
            EnsureFolder(TestRoot);
            EnsureFolder(NestedFolder);
            EnsureFolder(DeeplyNestedFolder);

            string canonicalPath = TestRoot + "/CleanupEnabledSingleton.asset";
            string duplicatePath1 = NestedFolder + "/CleanupEnabledSingleton.asset";
            string duplicatePath2 = DeeplyNestedFolder + "/CleanupEnabledSingleton.asset";

            // Don't use Track() - these become persistent assets managed by CleanupTestAssets()
            CleanupEnabledSingleton canonical =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupEnabledSingleton duplicate1 =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate1, duplicatePath1);

            CleanupEnabledSingleton duplicate2 =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate2, duplicatePath2);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(canonicalPath),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath1),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath2),
                Is.Not.Null
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(canonicalPath),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath1),
                Is.Null,
                "First duplicate should be removed"
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath2),
                Is.Null,
                "Second duplicate should be removed"
            );
        }

        [UnityTest]
        public IEnumerator CleanupRemovesEmptyFoldersAfterDeletion()
        {
            EnsureFolder(TestRoot);
            EnsureFolder(NestedFolder);
            EnsureFolder(DeeplyNestedFolder);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string canonicalPath = TestRoot + "/CleanupEnabledSingleton.asset";
            string duplicatePath = DeeplyNestedFolder + "/CleanupEnabledSingleton.asset";

            // Don't use Track() - these become persistent assets managed by CleanupTestAssets()
            CleanupEnabledSingleton canonical =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupEnabledSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.That(
                AssetDatabase.IsValidFolder(DeeplyNestedFolder),
                Is.True,
                $"Setup: Folder '{DeeplyNestedFolder}' should exist"
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath),
                Is.Not.Null,
                $"Setup: Duplicate asset should exist at '{duplicatePath}'"
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();

            // Two yields: the folder cleanup runs after StopAssetEditing.
            yield return null;
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath),
                Is.Null,
                $"Duplicate should be removed from '{duplicatePath}'"
            );

            string[] deepContents = AssetDatabase.IsValidFolder(DeeplyNestedFolder)
                ? AssetDatabase.FindAssets(string.Empty, new[] { DeeplyNestedFolder })
                : Array.Empty<string>();
            string[] nestedContents = AssetDatabase.IsValidFolder(NestedFolder)
                ? AssetDatabase.FindAssets(string.Empty, new[] { NestedFolder })
                : Array.Empty<string>();
            string[] deepSubfolders = AssetDatabase.IsValidFolder(DeeplyNestedFolder)
                ? AssetDatabase.GetSubFolders(DeeplyNestedFolder)
                : Array.Empty<string>();
            string[] nestedSubfolders = AssetDatabase.IsValidFolder(NestedFolder)
                ? AssetDatabase.GetSubFolders(NestedFolder)
                : Array.Empty<string>();

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string deepDiskPath =
                projectRoot != null
                    ? Path.Combine(projectRoot, DeeplyNestedFolder)
                    : DeeplyNestedFolder;
            string nestedDiskPath =
                projectRoot != null ? Path.Combine(projectRoot, NestedFolder) : NestedFolder;

            string deepDiagnostics =
                $"Contents: [{string.Join(", ", Array.ConvertAll(deepContents, AssetDatabase.GUIDToAssetPath))}], "
                + $"Subfolders: [{string.Join(", ", deepSubfolders)}], "
                + $"IsValidFolder={AssetDatabase.IsValidFolder(DeeplyNestedFolder)}, "
                + $"ExistsOnDisk={Directory.Exists(deepDiskPath)}";
            string nestedDiagnostics =
                $"Contents: [{string.Join(", ", Array.ConvertAll(nestedContents, AssetDatabase.GUIDToAssetPath))}], "
                + $"Subfolders: [{string.Join(", ", nestedSubfolders)}], "
                + $"IsValidFolder={AssetDatabase.IsValidFolder(NestedFolder)}, "
                + $"ExistsOnDisk={Directory.Exists(nestedDiskPath)}";

            Assert.That(
                AssetDatabase.IsValidFolder(DeeplyNestedFolder),
                Is.False,
                $"Deeply nested empty folder should be cleaned up. {deepDiagnostics}"
            );
            Assert.That(
                AssetDatabase.IsValidFolder(NestedFolder),
                Is.False,
                $"Intermediate empty folder should be cleaned up. {nestedDiagnostics}"
            );
        }

        [UnityTest]
        public IEnumerator CleanupPreservesNonEmptyFolders()
        {
            EnsureFolder(TestRoot);
            EnsureFolder(NestedFolder);

            string canonicalPath = TestRoot + "/CleanupEnabledSingleton.asset";
            string duplicatePath = NestedFolder + "/CleanupEnabledSingleton.asset";
            string otherAssetPath = NestedFolder + "/OtherAsset.asset";

            // Don't use Track() - these become persistent assets managed by CleanupTestAssets()
            CleanupEnabledSingleton canonical =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupEnabledSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            /*
                A singleton would be relocated by cleanup and empty this folder; use a non-singleton asset to
                keep it populated.
            */
            ScriptableObject otherAsset = ScriptableObject.CreateInstance<DummyScriptable>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(otherAsset, otherAssetPath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<Object>(otherAssetPath),
                Is.Not.Null,
                $"Setup: Other asset should exist at '{otherAssetPath}'"
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath),
                Is.Null,
                "Duplicate singleton should be removed"
            );
            Assert.That(
                AssetDatabase.IsValidFolder(NestedFolder),
                Is.True,
                $"Folder '{NestedFolder}' should be preserved when it still contains other assets"
            );

            Object remainingAsset = AssetDatabase.LoadAssetAtPath<Object>(otherAssetPath);
            Assert.That(
                remainingAsset,
                Is.Not.Null,
                $"Other asset should still exist at '{otherAssetPath}'. "
                    + $"Folder exists: {AssetDatabase.IsValidFolder(NestedFolder)}"
            );
        }

        [UnityTest]
        public IEnumerator CleanupHandlesNoCanonicalAsset()
        {
            EnsureFolder(TestRoot);
            EnsureFolder(NestedFolder);

            string duplicatePath = NestedFolder + "/CleanupEnabledSingleton.asset";

            // Don't use Track() - these become persistent assets managed by CleanupTestAssets()
            CleanupEnabledSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(duplicatePath),
                Is.Not.Null
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            string canonicalPath = TestRoot + "/CleanupEnabledSingleton.asset";
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(canonicalPath),
                Is.Not.Null,
                "Canonical asset should be created by EnsureSingletonAssets"
            );
        }

        [UnityTest]
        public IEnumerator CleanupHandlesSingleAssetNoDuplicates()
        {
            EnsureFolder(TestRoot);

            string canonicalPath = TestRoot + "/CleanupEnabledSingleton.asset";

            // Don't use Track() - these become persistent assets managed by CleanupTestAssets()
            CleanupEnabledSingleton canonical =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(canonicalPath),
                Is.Not.Null
            );

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupEnabledSingleton>(canonicalPath),
                Is.Not.Null,
                "Single canonical asset should remain untouched"
            );
        }

        [UnityTest]
        public IEnumerator CleanupComparesSerializedContentCorrectly()
        {
            EnsureFolder(TestRoot);
            EnsureFolder(NestedFolder);

            string canonicalPath = TestRoot + "/CleanupWithDataSingleton.asset";
            string duplicatePath = NestedFolder + "/CleanupWithDataSingleton.asset";

            // Don't use Track() - these become persistent assets managed by CleanupTestAssets()
            CleanupWithDataSingleton canonical =
                ScriptableObject.CreateInstance<CleanupWithDataSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            canonical.TestValue = 42;
            canonical.TestString = "test";
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupWithDataSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupWithDataSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            duplicate.TestValue = 42;
            duplicate.TestString = "test";
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();
            yield return null;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupWithDataSingleton>(canonicalPath),
                Is.Not.Null
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<CleanupWithDataSingleton>(duplicatePath),
                Is.Null,
                "Duplicate with identical serialized content should be removed"
            );
        }

        [UnityTest]
        public IEnumerator AllowDuplicateCleanupAttributeIsDetectedCorrectly()
        {
            Assert.That(
                ReflectionHelpers.TryGetAttributeSafe<AllowDuplicateCleanupAttribute>(
                    typeof(CleanupEnabledSingleton),
                    out _,
                    inherit: false
                ),
                Is.True,
                "CleanupEnabledSingleton should have AllowDuplicateCleanup attribute"
            );

            Assert.That(
                ReflectionHelpers.TryGetAttributeSafe<AllowDuplicateCleanupAttribute>(
                    typeof(CleanupDisabledSingleton),
                    out _,
                    inherit: false
                ),
                Is.False,
                "CleanupDisabledSingleton should NOT have AllowDuplicateCleanup attribute"
            );

            Assert.That(
                ReflectionHelpers.TryGetAttributeSafe<AllowDuplicateCleanupAttribute>(
                    typeof(CleanupWithDataSingleton),
                    out _,
                    inherit: false
                ),
                Is.True,
                "CleanupWithDataSingleton should have AllowDuplicateCleanup attribute"
            );

            yield break;
        }

        /// <summary>
        /// Tests that deeply nested folder hierarchies (3+ levels) are cleaned up bottom-up
        /// when all folders become empty after duplicate removal.
        /// </summary>
        [UnityTest]
        public IEnumerator CleanupRemovesDeeplyNestedFolderHierarchy()
        {
            const string Level1 = "Assets/Resources/DuplicateCleanupTests/Level1";
            const string Level2 = "Assets/Resources/DuplicateCleanupTests/Level1/Level2";
            const string Level3 = "Assets/Resources/DuplicateCleanupTests/Level1/Level2/Level3";
            const string Level4 =
                "Assets/Resources/DuplicateCleanupTests/Level1/Level2/Level3/Level4";

            EnsureFolder(TestRoot);
            EnsureFolder(Level1);
            EnsureFolder(Level2);
            EnsureFolder(Level3);
            EnsureFolder(Level4);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string canonicalPath = TestRoot + "/CleanupEnabledSingleton.asset";
            string duplicatePath = Level4 + "/CleanupEnabledSingleton.asset";

            CleanupEnabledSingleton canonical =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupEnabledSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();

            // Multiple yields to ensure Unity has time to process all AssetDatabase operations
            yield return null;
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string diagnostics =
                $"Level4Valid={AssetDatabase.IsValidFolder(Level4)}, "
                + $"Level3Valid={AssetDatabase.IsValidFolder(Level3)}, "
                + $"Level2Valid={AssetDatabase.IsValidFolder(Level2)}, "
                + $"Level1Valid={AssetDatabase.IsValidFolder(Level1)}";

            Assert.That(
                AssetDatabase.IsValidFolder(Level4),
                Is.False,
                $"Level4 folder should be cleaned up. {diagnostics}"
            );
            Assert.That(
                AssetDatabase.IsValidFolder(Level3),
                Is.False,
                $"Level3 folder should be cleaned up. {diagnostics}"
            );
            Assert.That(
                AssetDatabase.IsValidFolder(Level2),
                Is.False,
                $"Level2 folder should be cleaned up. {diagnostics}"
            );
            Assert.That(
                AssetDatabase.IsValidFolder(Level1),
                Is.False,
                $"Level1 folder should be cleaned up. {diagnostics}"
            );
        }

        /// <summary>
        /// Tests that folder cleanup stops at folders that still contain sibling assets.
        /// </summary>
        [UnityTest]
        public IEnumerator CleanupStopsAtFolderWithSiblingAsset()
        {
            const string SiblingFolder = "Assets/Resources/DuplicateCleanupTests/WithSibling";
            const string DeepFolder = "Assets/Resources/DuplicateCleanupTests/WithSibling/Deep";

            EnsureFolder(TestRoot);
            EnsureFolder(SiblingFolder);
            EnsureFolder(DeepFolder);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string canonicalPath = TestRoot + "/CleanupEnabledSingleton.asset";
            string duplicatePath = DeepFolder + "/CleanupEnabledSingleton.asset";
            string siblingAssetPath = SiblingFolder + "/SiblingAsset.asset";

            CleanupEnabledSingleton canonical =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupEnabledSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            ScriptableObject siblingAsset = ScriptableObject.CreateInstance<DummyScriptable>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(siblingAsset, siblingAssetPath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();

            // Multiple yields to ensure Unity has time to process all AssetDatabase operations
            yield return null;
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string diagnostics =
                $"DeepFolderValid={AssetDatabase.IsValidFolder(DeepFolder)}, "
                + $"SiblingFolderValid={AssetDatabase.IsValidFolder(SiblingFolder)}, "
                + $"SiblingAssetExists={AssetDatabase.LoadAssetAtPath<Object>(siblingAssetPath) != null}";

            Assert.That(
                AssetDatabase.IsValidFolder(DeepFolder),
                Is.False,
                $"Deep folder should be cleaned up. {diagnostics}"
            );
            Assert.That(
                AssetDatabase.IsValidFolder(SiblingFolder),
                Is.True,
                $"SiblingFolder should remain because it has a sibling asset. {diagnostics}"
            );
            Assert.That(
                AssetDatabase.LoadAssetAtPath<Object>(siblingAssetPath),
                Is.Not.Null,
                $"Sibling asset should still exist. {diagnostics}"
            );
        }

        /// <summary>
        /// Tests that folder cleanup correctly handles folders with empty subfolders
        /// but no assets.
        /// </summary>
        [UnityTest]
        public IEnumerator CleanupHandlesEmptySubfoldersCorrectly()
        {
            const string ParentFolder = "Assets/Resources/DuplicateCleanupTests/EmptyParent";
            const string EmptySubfolder1 =
                "Assets/Resources/DuplicateCleanupTests/EmptyParent/Sub1";
            const string EmptySubfolder2 =
                "Assets/Resources/DuplicateCleanupTests/EmptyParent/Sub2";
            const string AssetSubfolder =
                "Assets/Resources/DuplicateCleanupTests/EmptyParent/Sub1/AssetFolder";

            EnsureFolder(TestRoot);
            EnsureFolder(ParentFolder);
            EnsureFolder(EmptySubfolder1);
            EnsureFolder(EmptySubfolder2);
            EnsureFolder(AssetSubfolder);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string canonicalPath = TestRoot + "/CleanupEnabledSingleton.asset";
            string duplicatePath = AssetSubfolder + "/CleanupEnabledSingleton.asset";

            CleanupEnabledSingleton canonical =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(canonical, canonicalPath);

            CleanupEnabledSingleton duplicate =
                ScriptableObject.CreateInstance<CleanupEnabledSingleton>(); // UNH-SUPPRESS: UNH002 - Asset managed by CleanupTestAssets
            AssetDatabase.CreateAsset(duplicate, duplicatePath);

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();

            ScriptableObjectSingletonCreator.EnsureSingletonAssets();

            // Multiple yields to ensure Unity has time to process all AssetDatabase operations
            yield return null;
            yield return null;

            AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching();
            yield return null;

            string diagnostics =
                $"AssetSubfolderValid={AssetDatabase.IsValidFolder(AssetSubfolder)}, "
                + $"EmptySubfolder1Valid={AssetDatabase.IsValidFolder(EmptySubfolder1)}, "
                + $"EmptySubfolder2Valid={AssetDatabase.IsValidFolder(EmptySubfolder2)}, "
                + $"ParentFolderValid={AssetDatabase.IsValidFolder(ParentFolder)}";

            Assert.That(
                AssetDatabase.IsValidFolder(AssetSubfolder),
                Is.False,
                $"AssetSubfolder should be cleaned up. {diagnostics}"
            );
            Assert.That(
                AssetDatabase.IsValidFolder(EmptySubfolder1),
                Is.False,
                $"EmptySubfolder1 should be cleaned up. {diagnostics}"
            );
            /*
                Cleanup targets folders from deleted duplicates; do not assert removal of unrelated folders that
                were already empty.
            */
        }

        private static void TryDeleteFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.DeleteAsset(folderPath);
            }
        }
    }
}
#endif
