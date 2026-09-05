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

        private const string ResourcesRoot = "Assets/Resources";

        /// <summary>
        /// Whether the <c>Assets/Resources</c> root already existed before this fixture ran.
        /// Captured in <see cref="CommonOneTimeSetUp"/> so <see cref="OneTimeTearDown"/> only
        /// removes the root when this fixture is the one that created it.
        /// </summary>
        /// <remarks>
        /// Every <c>Assets/Resources/&lt;child&gt;</c> the fixture creates (via
        /// <see cref="CommonTestBase.EnsureFolderStatic"/>) materializes the bare
        /// <c>Assets/Resources</c> root as a side effect. <see cref="CleanupAllKnownTestFolders"/>
        /// deletes the known test child folders but deliberately preserves the root (it is treated
        /// as production data). In the ephemeral CI project nothing under <c>Assets/</c> is
        /// committed, so the root does NOT pre-exist and the leftover empty folder is reported by
        /// Unity's CleanupVerificationTask as an uncleaned new file -- failing the PlayMode run.
        /// Removing the root here when we created it restores the pre-fixture state.
        /// </remarks>
        private bool _resourcesRootExistedBeforeFixture;

        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();
            /*
                Snapshot root ownership before cleanup; the helper preserves Assets/Resources and teardown must
                not delete a pre-existing root.
            */
            _resourcesRootExistedBeforeFixture = AssetDatabase.IsValidFolder(ResourcesRoot);
            // Refresh explicitly after the batch, avoiding a duplicate disposal refresh.
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                CleanupAllKnownTestFolders();
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public override void OneTimeTearDown()
        {
            // Refresh explicitly after the batch, avoiding a duplicate disposal refresh.
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                CleanupAllKnownTestFolders();
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            /*
                Remove a fixture-created Resources root only after the batch refresh makes deleted children
                visible as absent.
            */
            RemoveResourcesRootIfCreatedByThisFixture();
            base.OneTimeTearDown();
        }

        /// <summary>
        /// Deletes the <c>Assets/Resources</c> root only when this fixture created it and it has no
        /// remaining contents. Safe against a pre-existing production <c>Assets/Resources</c> (it is
        /// left untouched) and against leftover real assets (a non-empty root is left untouched).
        /// </summary>
        private void RemoveResourcesRootIfCreatedByThisFixture()
        {
            if (_resourcesRootExistedBeforeFixture)
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder(ResourcesRoot))
            {
                return;
            }

            string[] subFolders = AssetDatabase.GetSubFolders(ResourcesRoot);
            if (subFolders is { Length: > 0 })
            {
                return;
            }

            string[] containedAssets = AssetDatabase.FindAssets(
                string.Empty,
                new[] { ResourcesRoot }
            );
            if (containedAssets is { Length: > 0 })
            {
                return;
            }

            if (AssetDatabase.DeleteAsset(ResourcesRoot))
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        /*
            Per-case cleanup would remove folders expected by later parameterized cases; clean up at fixture
            boundaries.
        */

        /// <summary>
        /// Creates folders and waits for AssetDatabase to fully recognize them.
        /// This handles the asynchronous nature of AssetDatabase.Refresh().
        /// </summary>
        /// <param name="folderPaths">The folder paths to create.</param>
        /// <returns>Coroutine that completes when folders are verified to exist.</returns>
        private IEnumerator CreateAndWaitForFolders(params string[] folderPaths)
        {
            foreach (string folderPath in folderPaths)
            {
                EnsureFolderStatic(folderPath);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            for (int frame = 0; frame < MaxAssetDatabaseWaitFrames; frame++)
            {
                yield return null;

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

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

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
            // Refresh explicitly after the batch, avoiding a duplicate disposal refresh.
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                CleanupAllKnownTestFolders();
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (foldersToVerify == null || foldersToVerify.Length == 0)
            {
                for (int frame = 0; frame < MaxAssetDatabaseWaitFrames; frame++)
                {
                    yield return null;
                }
                yield break;
            }

            for (int frame = 0; frame < MaxAssetDatabaseWaitFrames; frame++)
            {
                yield return null;

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

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

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
            string folderPath = "Assets/Resources/" + folderName;
            yield return CreateAndWaitForFolders(folderPath);

            yield return CleanupAndWait(folderPath);

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
            string folderPath = "Assets/" + folderName;
            yield return CreateAndWaitForFolders(folderPath);

            yield return CleanupAndWait(folderPath);

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
            string baseFolderName = "TempTestFolder";
            string baseFolder = "Assets/" + baseFolderName;
            string duplicate1 = "Assets/" + baseFolderName + " 1";
            string duplicate2 = "Assets/" + baseFolderName + " 2";

            yield return CreateAndWaitForFolders(baseFolder, duplicate1, duplicate2);

            yield return CleanupAndWait(baseFolder, duplicate1, duplicate2);
        }

        [UnityTest]
        public IEnumerator CleanupRemovesDuplicateFoldersInResources()
        {
            string baseFolderName = "CreatorTests";
            string baseFolder = "Assets/Resources/" + baseFolderName;
            string duplicate1 = "Assets/Resources/" + baseFolderName + " 1";
            string duplicate2 = "Assets/Resources/" + baseFolderName + " 2";

            yield return CreateAndWaitForFolders(baseFolder, duplicate1, duplicate2);

            yield return CleanupAndWait(baseFolder, duplicate1, duplicate2);
        }

        [UnityTest]
        public IEnumerator CleanupPreservesProtectedProductionFolders()
        {
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
                yield return CreateAndWaitForFolders(protectedFolders);

                yield return CleanupAndWait();

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
            string rootFolder = "Assets/TempTestFolder";
            string nestedFolder = rootFolder + "/Nested/Deep/Structure";

            yield return CreateAndWaitForFolders(nestedFolder);

            Assert.IsTrue(
                AssetDatabase.IsValidFolder(rootFolder),
                $"Root folder '{rootFolder}' should exist before cleanup"
            );
            Assert.IsTrue(
                AssetDatabase.IsValidFolder(nestedFolder),
                $"Nested folder '{nestedFolder}' should exist before cleanup"
            );

            yield return CleanupAndWait(rootFolder);
        }

        /// <summary>
        /// Verifies that the pattern lists in this test file are properly configured and contain expected patterns.
        /// </summary>
        [Test]
        public void PatternListsAreInSyncWithCommonTestBase()
        {
            HashSet<string> expectedResourcesPatterns = new(
                ResourcesTestFolderPatternsArray,
                StringComparer.Ordinal
            );
            HashSet<string> expectedAssetsPatterns = new(
                AssetsTestFolderPatternsArray,
                StringComparer.Ordinal
            );

            // Keep the test pattern inventory synchronized with the cleanup implementation.
            Assert.IsTrue(
                0 < expectedResourcesPatterns.Count,
                "ResourcesTestFolderPatternsArray should not be empty"
            );
            Assert.IsTrue(
                0 < expectedAssetsPatterns.Count,
                "AssetsTestFolderPatternsArray should not be empty"
            );

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
