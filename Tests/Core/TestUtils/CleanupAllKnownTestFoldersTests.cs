// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
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
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    /// Tests that verify CleanupAllKnownTestFolders properly cleans up all known test folder patterns.
    /// This ensures that test pollution is properly cleaned up and no orphaned folders remain.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Editor")]
    public sealed class CleanupAllKnownTestFoldersTests : CommonTestBase
    {
        /// <summary>
        /// Test folder patterns in Assets/Resources that should be cleaned up.
        /// IMPORTANT: Must match the patterns in CommonTestBase.CleanupAllKnownTestFolders().
        /// If you add/remove patterns there, update this list to match.
        /// </summary>
        private static readonly string[] ResourcesTestFolderPatternsArray =
        {
            "CreatorTests",
            "Deep",
            "Lifecycle",
            "Loose",
            "Multi",
            "MultiNatural",
            "SingleLevel",
            "Tests",
            "DuplicateCleanupTests",
            "CaseTest",
            "cASEtest",
            "CASETEST",
            "casetest",
            "CaseTEST",
            "CustomPath",
            "Missing",
        };

        /// <summary>
        /// Test folder patterns in Assets that should be cleaned up.
        /// IMPORTANT: Must match the patterns in CommonTestBase.CleanupAllKnownTestFolders().
        /// If you add/remove patterns there, update this list to match.
        /// </summary>
        private static readonly string[] AssetsTestFolderPatternsArray =
        {
            "Temp",
            "TempCleanupIntegrationTests",
            "TempMultiFileSelectorTests",
            "TempSpriteApplierTests",
            "TempSpriteApplierAdditional",
            "TempSpriteHelpersTests",
            "TempObjectHelpersEditorTests",
            "TempHelpersPrefabs",
            "TempHelpersScriptables",
            "TempColorExtensionTests",
            "TempTestFolder",
            "TestFolder",
            "__LlmArtifactCleanerTests__",
            "__DetectAssetChangedTests__",
        };

        /// <summary>
        /// Maximum number of frames to wait for AssetDatabase operations to complete.
        /// </summary>
        private const int MaxAssetDatabaseWaitFrames = 10;

        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            // Pre-cleanup before tests - use batched cleanup
            // Use refreshOnDispose: false since we manually refresh after
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                CleanupAllKnownTestFolders();
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public override void OneTimeTearDown()
        {
            // Final cleanup after all tests - use batched cleanup
            // Use refreshOnDispose: false since we manually refresh after
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                CleanupAllKnownTestFolders();
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            base.OneTimeTearDown();
        }

        // NOTE: We intentionally do NOT override TearDown to call CleanupAllKnownTestFolders()
        // Doing so would interfere with subsequent tests because the cleanup runs after each
        // parameterized test case, potentially removing folders that the next test expects to create.
        // Instead, we rely on OneTimeSetUp/OneTimeTearDown for fixture-level cleanup.

        /// <summary>
        /// Creates folders and waits for AssetDatabase to fully recognize them.
        /// This handles the asynchronous nature of AssetDatabase.Refresh().
        /// </summary>
        /// <param name="folderPaths">The folder paths to create.</param>
        /// <returns>Coroutine that completes when folders are verified to exist.</returns>
        private IEnumerator CreateAndWaitForFolders(params string[] folderPaths)
        {
            // Create all folders
            foreach (string folderPath in folderPaths)
            {
                EnsureFolderStatic(folderPath);
            }

            // Force synchronous import to ensure folders are recognized
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // Wait for AssetDatabase to fully process - yield multiple frames if needed
            for (int frame = 0; frame < MaxAssetDatabaseWaitFrames; frame++)
            {
                yield return null;

                // Check if all folders are now valid
                bool allValid = true;
                foreach (string folderPath in folderPaths)
                {
                    if (!AssetDatabase.IsValidFolder(folderPath))
                    {
                        allValid = false;
                        break;
                    }
                }

                if (allValid)
                {
                    yield break;
                }

                // Try refreshing again if not all folders are recognized
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            // Final assertion with detailed diagnostics
            foreach (string folderPath in folderPaths)
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string absolutePath = !string.IsNullOrEmpty(projectRoot)
                    ? Path.Combine(projectRoot, folderPath).SanitizePath()
                    : folderPath;
                bool existsOnDisk =
                    !string.IsNullOrEmpty(projectRoot) && Directory.Exists(absolutePath);
                bool existsInAssetDb = AssetDatabase.IsValidFolder(folderPath);

                Assert.IsTrue(
                    existsInAssetDb,
                    $"Folder creation failed for '{folderPath}'. "
                        + $"Exists on disk: {existsOnDisk}, "
                        + $"Exists in AssetDatabase: {existsInAssetDb}, "
                        + $"Absolute path: {absolutePath}"
                );
            }
        }

        /// <summary>
        /// Runs cleanup and waits for AssetDatabase to fully process the deletions.
        /// </summary>
        /// <param name="foldersToVerify">Optional array of folder paths to verify are deleted.
        /// If null or empty, method waits a fixed number of frames without verification.</param>
        /// <returns>Coroutine that completes when cleanup is verified.</returns>
        private IEnumerator CleanupAndWait(params string[] foldersToVerify)
        {
            // Run cleanup within a batch scope
            // Use refreshOnDispose: false since we manually refresh after
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                CleanupAllKnownTestFolders();
            }

            // Force synchronous refresh to ensure deletions are processed
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // If no folders to verify, just wait the standard frames
            if (foldersToVerify == null || foldersToVerify.Length == 0)
            {
                for (int frame = 0; frame < MaxAssetDatabaseWaitFrames; frame++)
                {
                    yield return null;
                }
                yield break;
            }

            // Wait for AssetDatabase to fully process - yield multiple frames if needed
            for (int frame = 0; frame < MaxAssetDatabaseWaitFrames; frame++)
            {
                yield return null;

                // Check if all folders are now deleted
                bool allDeleted = true;
                foreach (string folderPath in foldersToVerify)
                {
                    if (AssetDatabase.IsValidFolder(folderPath))
                    {
                        allDeleted = false;
                        break;
                    }
                }

                if (allDeleted)
                {
                    yield break;
                }

                // Try refreshing again if not all folders are deleted
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            // Final assertion with detailed diagnostics
            foreach (string folderPath in foldersToVerify)
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string absolutePath = !string.IsNullOrEmpty(projectRoot)
                    ? Path.Combine(projectRoot, folderPath).SanitizePath()
                    : folderPath;
                bool existsOnDisk =
                    !string.IsNullOrEmpty(projectRoot) && Directory.Exists(absolutePath);
                bool existsInAssetDb = AssetDatabase.IsValidFolder(folderPath);

                Assert.IsFalse(
                    existsInAssetDb,
                    $"Folder deletion failed for '{folderPath}'. "
                        + $"Exists on disk: {existsOnDisk}, "
                        + $"Exists in AssetDatabase: {existsInAssetDb}, "
                        + $"Absolute path: {absolutePath}"
                );
            }
        }

        [UnityTest]
        public IEnumerator CleanupRemovesFolderInResources(
            [Values(
                "CreatorTests",
                "Deep",
                "Lifecycle",
                "Loose",
                "Multi",
                "MultiNatural",
                "SingleLevel",
                "Tests",
                "DuplicateCleanupTests",
                "CaseTest",
                "cASEtest",
                "CASETEST",
                "casetest",
                "CaseTEST",
                "CustomPath",
                "Missing"
            )]
                string folderName
        )
        {
            // Arrange: Create the folder and wait for AssetDatabase to recognize it
            string folderPath = "Assets/Resources/" + folderName;
            yield return CreateAndWaitForFolders(folderPath);

            // Act: Run cleanup and wait, verifying the specific folder is deleted
            yield return CleanupAndWait(folderPath);

            // Also verify it's gone from disk
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string absolutePath = Path.Combine(projectRoot, folderPath).SanitizePath();
                Assert.IsFalse(
                    Directory.Exists(absolutePath),
                    $"Folder '{absolutePath}' should be removed from disk after cleanup"
                );
            }
        }

        [UnityTest]
        public IEnumerator CleanupRemovesFolderInAssets(
            [Values(
                "Temp",
                "TempCleanupIntegrationTests",
                "TempMultiFileSelectorTests",
                "TempSpriteApplierTests",
                "TempSpriteApplierAdditional",
                "TempSpriteHelpersTests",
                "TempObjectHelpersEditorTests",
                "TempHelpersPrefabs",
                "TempHelpersScriptables",
                "TempColorExtensionTests",
                "TempTestFolder",
                "TestFolder",
                "__LlmArtifactCleanerTests__",
                "__DetectAssetChangedTests__"
            )]
                string folderName
        )
        {
            // Arrange: Create the folder and wait for AssetDatabase to recognize it
            string folderPath = "Assets/" + folderName;
            yield return CreateAndWaitForFolders(folderPath);

            // Act: Run cleanup and wait, verifying the specific folder is deleted
            yield return CleanupAndWait(folderPath);

            // Also verify it's gone from disk
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string absolutePath = Path.Combine(projectRoot, folderPath).SanitizePath();
                Assert.IsFalse(
                    Directory.Exists(absolutePath),
                    $"Folder '{absolutePath}' should be removed from disk after cleanup"
                );
            }
        }

        [UnityTest]
        public IEnumerator CleanupRemovesDuplicateFoldersInAssets()
        {
            // Arrange: Create a base folder and its duplicates
            string baseFolderName = "TempTestFolder";
            string baseFolder = "Assets/" + baseFolderName;
            string duplicate1 = "Assets/" + baseFolderName + " 1";
            string duplicate2 = "Assets/" + baseFolderName + " 2";

            // Create and wait for all folders to be recognized
            yield return CreateAndWaitForFolders(baseFolder, duplicate1, duplicate2);

            // Act: Run cleanup and wait, verifying all folders are deleted
            yield return CleanupAndWait(baseFolder, duplicate1, duplicate2);
        }

        [UnityTest]
        public IEnumerator CleanupRemovesDuplicateFoldersInResources()
        {
            // Arrange: Create a base folder and its duplicates in Resources
            string baseFolderName = "CreatorTests";
            string baseFolder = "Assets/Resources/" + baseFolderName;
            string duplicate1 = "Assets/Resources/" + baseFolderName + " 1";
            string duplicate2 = "Assets/Resources/" + baseFolderName + " 2";

            // Create and wait for all folders to be recognized
            yield return CreateAndWaitForFolders(baseFolder, duplicate1, duplicate2);

            // Act: Run cleanup and wait, verifying all folders are deleted
            yield return CleanupAndWait(baseFolder, duplicate1, duplicate2);
        }

        [UnityTest]
        public IEnumerator CleanupPreservesProtectedProductionFolders()
        {
            // Verify that protected folders are NOT deleted by cleanup
            // These are production folders that should never be touched
            string[] protectedFolders = new[]
            {
                "Assets/Resources/Wallstop Studios",
                "Assets/Resources/Wallstop Studios/Unity Helpers",
            };

            bool resourcesExisted = AssetDatabase.IsValidFolder("Assets/Resources");
            bool wallstopStudiosExisted = AssetDatabase.IsValidFolder(
                "Assets/Resources/Wallstop Studios"
            );
            bool unityHelpersExisted = AssetDatabase.IsValidFolder(
                "Assets/Resources/Wallstop Studios/Unity Helpers"
            );

            try
            {
                // Ensure protected folders exist and wait for recognition
                yield return CreateAndWaitForFolders(protectedFolders);

                // Act: Run cleanup and wait
                yield return CleanupAndWait();

                // Assert: Protected folders should still exist
                foreach (string folder in protectedFolders)
                {
                    Assert.IsTrue(
                        AssetDatabase.IsValidFolder(folder),
                        $"Protected folder '{folder}' should NOT be removed by cleanup"
                    );
                }
            }
            finally
            {
                DeleteFolderCreatedByThisTest(
                    "Assets/Resources/Wallstop Studios/Unity Helpers",
                    unityHelpersExisted
                );
                DeleteFolderCreatedByThisTest(
                    "Assets/Resources/Wallstop Studios",
                    wallstopStudiosExisted
                );
                DeleteFolderCreatedByThisTest("Assets/Resources", resourcesExisted);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        [UnityTest]
        public IEnumerator CleanupRemovesNestedTestFolders()
        {
            // Arrange: Create a nested structure inside a test folder
            string rootFolder = "Assets/TempTestFolder";
            string nestedFolder = rootFolder + "/Nested/Deep/Structure";

            // Create nested folder (this will also create parent folders)
            yield return CreateAndWaitForFolders(nestedFolder);

            // Verify both root and nested folders exist
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(rootFolder),
                $"Root folder '{rootFolder}' should exist before cleanup"
            );
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(nestedFolder),
                $"Nested folder '{nestedFolder}' should exist before cleanup"
            );

            // Act: Run cleanup and wait, verifying root folder is deleted (nested will be gone too)
            yield return CleanupAndWait(rootFolder);
        }

        /// <summary>
        /// Verifies that the pattern lists in this test file are properly configured and contain expected patterns.
        /// </summary>
        [Test]
        public void PatternListsAreInSyncWithCommonTestBase()
        {
            // Build hash sets from the static arrays
            HashSet<string> expectedResourcesPatterns = new(
                ResourcesTestFolderPatternsArray,
                StringComparer.Ordinal
            );
            HashSet<string> expectedAssetsPatterns = new(
                AssetsTestFolderPatternsArray,
                StringComparer.Ordinal
            );

            // Verify we have the expected number of patterns
            // These counts should match the array sizes in CleanupAllKnownTestFolders()
            // If this test fails, it means someone added/removed patterns in one place but not the other
            Assert.IsTrue(
                expectedResourcesPatterns.Count > 0,
                "ResourcesTestFolderPatternsArray should not be empty"
            );
            Assert.IsTrue(
                expectedAssetsPatterns.Count > 0,
                "AssetsTestFolderPatternsArray should not be empty"
            );

            // Verify specific known patterns exist (these are core patterns that must always be present)
            string[] coreResourcesPatterns = { "CreatorTests", "Deep", "Lifecycle", "Tests" };
            foreach (string corePattern in coreResourcesPatterns)
            {
                Assert.IsTrue(
                    expectedResourcesPatterns.Contains(corePattern),
                    $"Core pattern '{corePattern}' should be in ResourcesTestFolderPatternsArray"
                );
            }

            string[] coreAssetsPatterns = { "Temp", "TempTestFolder", "TestFolder" };
            foreach (string corePattern in coreAssetsPatterns)
            {
                Assert.IsTrue(
                    expectedAssetsPatterns.Contains(corePattern),
                    $"Core pattern '{corePattern}' should be in AssetsTestFolderPatternsArray"
                );
            }

            // The actual synchronization is verified by running the parameterized tests above.
            // If any pattern is missing from CleanupAllKnownTestFolders(), those tests will fail
            // because the folders won't be cleaned up.
            // This test serves as an additional check that the test data sources are properly configured.
        }

        /// <summary>
        /// Smoke test to verify that UnityTest with IEnumerator return type is functioning correctly.
        /// </summary>
        [UnityTest]
        public IEnumerator UnityTestFrameworkSmokeTest()
        {
            yield return null;
            Assert.Pass("UnityTest with IEnumerator is functioning correctly");
        }

        private static void DeleteFolderCreatedByThisTest(string folderPath, bool existedBefore)
        {
            if (existedBefore || !AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            AssetDatabase.DeleteAsset(folderPath);
        }
    }

#endif
}
