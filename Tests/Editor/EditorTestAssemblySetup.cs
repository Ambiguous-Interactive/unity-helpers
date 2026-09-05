// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core.TestUtils;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestAssets;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestUtils;
    using TestAssets_SharedTextureTestFixtures = WallstopStudios.UnityHelpers.Tests.Editor.TestAssets.SharedTextureTestFixtures;

    /// <summary>
    /// Assembly-level setup fixture that preloads all shared test assets once
    /// before any tests in this assembly run. This eliminates repeated AssetDatabase
    /// calls during individual test execution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By using <see cref="SetUpFixtureAttribute"/>, this class ensures that all
    /// shared test fixtures are initialized exactly once at the start of the test
    /// assembly execution, and cleaned up once at the end.
    /// </para>
    /// <para>
    /// This significantly improves test performance by:
    /// </para>
    /// <list type="bullet">
    /// <item>Loading all prefabs, textures, and ScriptableObjects once instead of per-test</item>
    /// <item>Caching asset references across test fixtures</item>
    /// <item>Performing cleanup in a single batch operation</item>
    /// </list>
    /// <para>
    /// The setup order is:
    /// </para>
    /// <list type="number">
    /// <item><see cref="SharedEditorTestFixtures.PreloadAllAssets"/></item>
    /// <item><see cref="SharedPrefabTestFixtures.PreloadAllAssets"/></item>
    /// <item><see cref="TestAssets_SharedTextureTestFixtures.PreloadAllAssets"/></item>
    /// <item><see cref="SharedAnimationTestFixtures.PreloadAllAssets"/></item>
    /// </list>
    /// <para>
    /// The teardown order is:
    /// </para>
    /// <list type="number">
    /// <item><see cref="SharedAnimationTestFixtures.ReleaseAllCachedAssets"/></item>
    /// <item><see cref="TestAssets_SharedTextureTestFixtures.ReleaseAllCachedAssets"/></item>
    /// <item><see cref="SharedPrefabTestFixtures.ReleaseAllCachedAssets"/></item>
    /// <item><see cref="SharedEditorTestFixtures.ReleaseAllCachedAssets"/></item>
    /// <item><see cref="FolderTemplateManager.ForceCleanupAll"/></item>
    /// </list>
    /// </remarks>
    [SetUpFixture]
    public sealed class EditorTestAssemblySetup
    {
        /// <summary>
        /// Called once before any tests in this assembly run.
        /// Preloads all shared test assets into cache.
        /// </summary>
        [OneTimeSetUp]
        public void AssemblySetUp()
        {
            SharedEditorTestFixtures.PreloadAllAssets();
            SharedPrefabTestFixtures.PreloadAllAssets();
            TestAssets_SharedTextureTestFixtures.PreloadAllAssets();
            SharedAnimationTestFixtures.PreloadAllAssets();
        }

        /// <summary>
        /// Called once after all tests in this assembly have completed.
        /// Releases all cached asset references and performs cleanup.
        /// </summary>
        [OneTimeTearDown]
        public void AssemblyTearDown()
        {
            SharedAnimationTestFixtures.ReleaseAllCachedAssets();
            TestAssets_SharedTextureTestFixtures.ReleaseAllCachedAssets();
            SharedPrefabTestFixtures.ReleaseAllCachedAssets();
            SharedEditorTestFixtures.ReleaseAllCachedAssets();
            FolderTemplateManager.ForceCleanupAll();

            // Each fixture cleanup can trigger AssetDatabase.Refresh(), which creates new duplicates.
            FinalTempFolderCleanup();
        }

        /// <summary>
        /// Performs final cleanup of "Temp N" duplicate folders with retry logic and verification.
        /// This is the last line of defense to ensure no stray temp folders persist after test runs.
        /// </summary>
        private void FinalTempFolderCleanup()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            // A higher retry count than a fixture's: duplicates accumulate across the whole run.
            int totalDeleted = TempFolderCleanupUtility.CleanupTempDuplicatesWithRetry(
                TempFolderCleanupUtility.AssemblyLevelRetryCount
            );
            if (0 < totalDeleted)
            {
                Debug.Log(
                    $"[EditorTestAssemblySetup] Cleaned up {totalDeleted} 'Temp N' duplicate folders."
                );
            }

            TempFolderCleanupUtility.VerifyNoTempDuplicatesRemain();

            TempFolderCleanupUtility.TryCleanupEmptyTempFolder();
        }
    }
#endif
}
