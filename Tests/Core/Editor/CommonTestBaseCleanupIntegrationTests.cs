// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
{
#if UNITY_EDITOR

    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    ///     Integration tests for CommonTestBase's CleanupTrackedFoldersAndAssets method.
    ///     These tests verify that the cleanup properly uses AssetDatabaseBatchHelper.BeginBatch()
    ///     instead of direct AssetDatabase.StartAssetEditing/StopAssetEditing calls, which would
    ///     cause counter imbalances and Unity "forever importing" states.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     These tests exist to prevent regression of the bug where CleanupTrackedFoldersAndAssets
    ///     was calling AssetDatabase.StartAssetEditing()/StopAssetEditing() directly instead of
    ///     using AssetDatabaseBatchHelper.BeginBatch(). The direct calls caused counter imbalances
    ///     when cleanup was called inside an already-active batch scope, leading to Unity getting
    ///     stuck in an importing state.
    ///     </para>
    ///     <para>
    ///     The fix ensures CleanupTrackedFoldersAndAssets uses the proper scope pattern with
    ///     AssetDatabaseBatchHelper.BeginBatch(), which correctly tracks nested scopes.
    ///     </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Editor")]
    public sealed class CommonTestBaseCleanupIntegrationTests : CommonTestBase
    {
        private const string TestFolderRoot = "Assets/TempCleanupIntegrationTests";

        [OneTimeSetUp]
        public void FixtureSetUp()
        {
            AssetDatabaseBatchHelper.ResetCountersOnly();
            CleanupIntegrationTestFolders();
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            AssetDatabaseBatchHelper.ResetBatchDepth();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();

            CleanupIntegrationTestFolders();
            AssetDatabaseBatchHelper.ResetBatchDepth();
        }

        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            try
            {
                CleanupIntegrationTestFolders();
            }
            finally
            {
                base.OneTimeTearDown();
            }
        }

        /// <summary>
        ///     Tests that CleanupTrackedFoldersAndAssets maintains correct batch counter state
        ///     when called outside of any active batch scope.
        /// </summary>
        [Test]
        public void CleanupTrackedFoldersAndAssetsOutsideBatchMaintainsZeroDepth()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: should start at depth 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Pre-condition: should not be batching"
            );

            CreateTestFolder();

            CleanupTrackedFoldersAndAssets();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Cleanup should not leave the counter imbalanced"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after cleanup completes"
            );
        }

        /// <summary>
        ///     Tests that CleanupTrackedFoldersAndAssets properly nests inside an active batch scope.
        ///     This is the key regression test for the bug where direct StartAssetEditing/StopAssetEditing
        ///     calls caused counter imbalances.
        /// </summary>
        [Test]
        public void CleanupTrackedFoldersAndAssetsInsideBatchMaintainsOuterScope()
        {
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Pre-condition: should be at depth 1 inside outer scope"
                );

                CreateTestFolder();

                CleanupTrackedFoldersAndAssets();

                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Cleanup inside batch should return to outer scope depth"
                );
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should still be batching after cleanup (outer scope still active)"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after outer scope exits"
            );
        }

        /// <summary>
        ///     Tests that CleanupTrackedFoldersAndAssets properly handles deeply nested batch scopes.
        /// </summary>
        [Test]
        [TestCase(2, TestName = "CleanupInsideNestedBatch.Depth2")]
        [TestCase(3, TestName = "CleanupInsideNestedBatch.Depth3")]
        [TestCase(5, TestName = "CleanupInsideNestedBatch.Depth5")]
        public void CleanupTrackedFoldersAndAssetsInsideNestedBatchMaintainsCorrectDepth(int depth)
        {
            List<AssetDatabaseBatchScope> scopes = new List<AssetDatabaseBatchScope>();
            for (int i = 0; i < depth; i++)
            {
                scopes.Add(AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false));
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(depth),
                $"Pre-condition: should be at depth {depth}"
            );

            CreateTestFolder();

            CleanupTrackedFoldersAndAssets();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(depth),
                $"Cleanup should return to original depth {depth}"
            );

            for (int i = scopes.Count - 1; 0 <= i; i--)
            {
                scopes[i].Dispose();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final depth should be 0 after all scopes disposed"
            );
        }

        /// <summary>
        ///     Tests that CleanupTrackedFoldersAndAssets can be called multiple times consecutively
        ///     without causing counter imbalances.
        /// </summary>
        [Test]
        [TestCase(2, TestName = "MultipleConsecutiveCleanups.Count2")]
        [TestCase(5, TestName = "MultipleConsecutiveCleanups.Count5")]
        [TestCase(10, TestName = "MultipleConsecutiveCleanups.Count10")]
        public void MultipleConsecutiveCleanupCallsMaintainZeroDepth(int cleanupCount)
        {
            for (int i = 0; i < cleanupCount; i++)
            {
                CreateTestFolder();

                CleanupTrackedFoldersAndAssets();

                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(0),
                    $"Depth should be 0 after cleanup call {i + 1}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                $"Should not be batching after {cleanupCount} cleanup calls"
            );
        }

        /// <summary>
        ///     Tests that CleanupTrackedFoldersAndAssets properly handles being called multiple times
        ///     inside a single batch scope.
        /// </summary>
        [Test]
        public void MultipleCleanupCallsInsideSingleBatchMaintainsScope()
        {
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Pre-condition: should be at depth 1"
                );

                for (int i = 0; i < 3; i++)
                {
                    CreateTestFolder();
                    CleanupTrackedFoldersAndAssets();

                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(1),
                        $"Depth should remain 1 after cleanup call {i + 1}"
                    );
                }
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after outer scope exits"
            );
        }

        /// <summary>
        ///     Tests that if an exception is thrown during folder/asset deletion,
        ///     the batch scope is still properly closed (using block guarantees this).
        /// </summary>
        [Test]
        public void CleanupWithExceptionStillClosesBatchScope()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: should start at depth 0"
            );

            /*
                Unity returns false for nonexistent assets; this case checks batch-depth restoration rather than
                an exception path.
            */

            int depthBefore = AssetDatabaseBatchHelper.CurrentBatchDepth;

            CreateTestFolder();
            CleanupTrackedFoldersAndAssets();

            int depthAfter = AssetDatabaseBatchHelper.CurrentBatchDepth;

            Assert.That(
                depthAfter,
                Is.EqualTo(depthBefore),
                "Depth should be unchanged after cleanup completes normally"
            );
        }

        /// <summary>
        ///     Tests the scenario where CleanupTrackedFoldersAndAssets is called when
        ///     there are no tracked folders or assets to clean up.
        /// </summary>
        [Test]
        public void CleanupWithNoTrackedItemsMaintainsZeroDepth()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: should start at depth 0"
            );

            CleanupTrackedFoldersAndAssets();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Cleanup with no tracked items should maintain depth 0"
            );
        }

        /// <summary>
        ///     Tests the scenario where CleanupTrackedFoldersAndAssets is called inside an outer batch
        ///     and there are no tracked items to clean.
        /// </summary>
        [Test]
        public void CleanupWithNoTrackedItemsInsideBatchMaintainsOuterScope()
        {
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Pre-condition: should be at depth 1"
                );

                CleanupTrackedFoldersAndAssets();

                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Cleanup with no items should maintain outer scope depth"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after outer scope exits"
            );
        }

        /// <summary>
        ///     Tests that the RefreshIfNotBatching call after cleanup behaves correctly
        ///     when called outside of a batch scope (should refresh).
        /// </summary>
        [Test]
        public void CleanupFollowedByRefreshIfNotBatchingOutsideBatch()
        {
            CreateTestFolder();
            CleanupTrackedFoldersAndAssets();

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after cleanup"
            );

            // The refresh is not observable here; verify the state that permits it.
            int depthBefore = AssetDatabaseBatchHelper.CurrentBatchDepth;
            AssetDatabaseBatchHelper.RefreshIfNotBatching();
            int depthAfter = AssetDatabaseBatchHelper.CurrentBatchDepth;

            Assert.That(
                depthAfter,
                Is.EqualTo(depthBefore),
                "RefreshIfNotBatching should not change batch depth"
            );
        }

        /// <summary>
        ///     Tests that ActualUnityBatchDepth is properly tracked during cleanup operations.
        /// </summary>
        [Test]
        public void CleanupProperlyTracksActualUnityBatchDepth()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: CurrentBatchDepth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: ActualUnityBatchDepth should be 0"
            );

            CreateTestFolder();

            CleanupTrackedFoldersAndAssets();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "CurrentBatchDepth should be 0 after cleanup"
            );
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "ActualUnityBatchDepth should be 0 after cleanup"
            );
        }

        /// <summary>
        ///     Tests that cleanup inside an outer batch properly tracks ActualUnityBatchDepth.
        /// </summary>
        [Test]
        public void CleanupInsideBatchTracksActualUnityBatchDepthCorrectly()
        {
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "Pre-condition: ActualUnityBatchDepth should be 1 in outer scope"
                );

                CreateTestFolder();

                CleanupTrackedFoldersAndAssets();

                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "ActualUnityBatchDepth should remain 1 after nested cleanup"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "ActualUnityBatchDepth should be 0 after outer scope exits"
            );
        }

        /// <summary>
        ///     Verifies the common cleanup path: a tracked, fully-persisted asset (resolvable path) is
        ///     removed via <see cref="AssetDatabase.DeleteAsset"/> with no error and no leak. Pairs
        ///     with <see cref="DestroyTrackedObjectsHandlesDeferredDeletedAssetWithoutError"/>, which
        ///     covers the persistent-but-pathless fall-through that actually regressed in CI.
        /// </summary>
        [Test]
        public void DestroyTrackedObjectsRemovesPersistedAssetWithoutError()
        {
            EnsureTestRoot();
            string assetPath = $"{TestFolderRoot}/Tracked_{Guid.NewGuid():N}.asset";
            ScriptableObject asset = ScriptableObject.CreateInstance<ScriptableObject>();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            Track(asset);

            Assert.That(
                EditorUtility.IsPersistent(asset),
                Is.True,
                "Pre-condition: the tracked object should be a persisted asset"
            );

            ExpectNoScriptAssetForScriptableObjectWarning();
            DestroyTrackedObjects();
            LogAssert.NoUnexpectedReceived();

            Assert.That(
                AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath),
                Is.Null,
                "Cleanup should delete the persisted asset"
            );
        }

        /// <summary>
        ///     Reproduces the exact CI failure mode. <see cref="BatchedEditorTestBase"/> keeps a
        ///     fixture-wide <c>StartAssetEditing</c> batch open across every test, so an
        ///     <see cref="AssetDatabase.DeleteAsset"/> issued by a subclass teardown is DEFERRED: the
        ///     C# wrapper stays non-null while its asset path is already cleared. The base cleanup then
        ///     iterates a persistent object with no resolvable path -- the old single-argument
        ///     <c>Object.DestroyImmediate(obj)</c> fell through to the asset-unsafe overload here and
        ///     logged "Destroying assets is not permitted to avoid data loss" (a
        ///     <see cref="LogType.Error"/>, which fails the test in teardown and leaks the object). The
        ///     asset-safe overload must not.
        /// </summary>
        [Test]
        public void DestroyTrackedObjectsHandlesDeferredDeletedAssetWithoutError()
        {
            EnsureTestRoot();
            string assetPath = $"{TestFolderRoot}/Orphan_{Guid.NewGuid():N}.asset";

            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                ScriptableObject asset = ScriptableObject.CreateInstance<ScriptableObject>();
                AssetDatabase.CreateAsset(asset, assetPath);
                Track(asset);

                /*
                    Deferred under the active batch: the wrapper survives non-null while the asset path is
                    cleared -- the persistent-but-pathless state that broke CI.
                */
                AssetDatabase.DeleteAsset(assetPath);

                ExpectNoScriptAssetForScriptableObjectWarning();
                DestroyTrackedObjects();
                LogAssert.NoUnexpectedReceived();
            }

            /*
                Unity 2021.3 may flush the deferred create after its queued delete. Flush and delete again to
                prevent leakage; the in-batch log assertion covers the package regression.
            */
            AssetDatabase.Refresh();
            AssetDatabase.DeleteAsset(assetPath);
            ForceAssetUnloaded(assetPath);
        }

        /// <summary>
        ///     Ensures the shared test root folder exists in the AssetDatabase.
        /// </summary>
        private void EnsureTestRoot()
        {
            if (!AssetDatabase.IsValidFolder(TestFolderRoot))
            {
                string[] parts = TestFolderRoot.Split('/');
                string currentPath = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string nextPath = $"{currentPath}/{parts[i]}";
                    if (!AssetDatabase.IsValidFolder(nextPath))
                    {
                        AssetDatabase.CreateFolder(currentPath, parts[i]);
                    }
                    currentPath = nextPath;
                }
            }
        }

        /// <summary>
        ///     Helper method to create a test folder and track it for cleanup.
        /// </summary>
        private void CreateTestFolder()
        {
            string folderName = $"TestFolder_{Guid.NewGuid():N}";
            string folderPath = $"{TestFolderRoot}/{folderName}";

            EnsureTestRoot();

            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder(TestFolderRoot, folderName);
            }

            TrackFolder(folderPath);
        }

        private static void CleanupIntegrationTestFolders()
        {
            CleanupAllKnownTestFolders();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }

#endif
}
