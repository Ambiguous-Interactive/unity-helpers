// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Shared base class for DetectAssetChangeProcessor tests providing common utility methods
    /// for test folder management, state clearing, and test asset creation.
    /// </summary>
    public abstract class DetectAssetChangeTestBase : BatchedEditorTestBase
    {
        /// <summary>
        /// Root folder path for all DetectAssetChange tests.
        /// </summary>
        protected const string TestRoot = "Assets/__DetectAssetChangedTests__";

        /// <summary>
        /// Default path for the payload test asset.
        /// </summary>
        protected virtual string DefaultPayloadAssetPath => TestRoot + "/Payload.asset";

        /// <summary>
        /// Default path for the alternate payload test asset.
        /// </summary>
        protected const string DefaultAlternatePayloadAssetPath =
            TestRoot + "/AlternatePayload.asset";

        /// <summary>
        /// Path prefixes this fixture family is allowed to drive the processor through.
        /// Scoped to <see cref="TestRoot"/> so assets created by any OTHER fixture are
        /// structurally ignored even when
        /// <see cref="DetectAssetChangeProcessor.IncludeTestAssets"/> is <see langword="true"/>.
        /// Every setup / reset path in this base class and its derivatives must restore
        /// this allowlist after calling
        /// <see cref="DetectAssetChangeProcessor.ResetForTesting()"/> (which clears it).
        /// </summary>
        protected static readonly string[] FixtureAllowlist = { TestRoot + "/" };

        /// <summary>
        /// Cleans up all test folders including any duplicates created due to AssetDatabase issues.
        /// This handles scenarios like "__DetectAssetChangedTests__ 1", "__DetectAssetChangedTests__ 2", etc.
        /// </summary>
        protected static void CleanupTestFolders()
        {
            // Delete the main test folder
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.DeleteAsset(TestRoot);
            }

            // Clean up any duplicate folders that may have been created
            // These can be created when AssetDatabase.CreateFolder fails but Unity creates the folder anyway
            string[] allFolders = AssetDatabase.GetSubFolders("Assets");
            if (allFolders != null)
            {
                foreach (string folder in allFolders)
                {
                    string folderName = Path.GetFileName(folder);
                    if (
                        folderName != null
                        && folderName.StartsWith(
                            "__DetectAssetChangedTests__",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        AssetDatabase.DeleteAsset(folder);
                    }
                }
            }

            // Also clean up from disk to handle orphaned folders
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string assetsFolder = Path.Combine(projectRoot, "Assets");
                if (Directory.Exists(assetsFolder))
                {
                    try
                    {
                        foreach (
                            string dir in Directory.GetDirectories(
                                assetsFolder,
                                "__DetectAssetChangedTests__*"
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
                    catch
                    {
                        // Ignore enumeration errors
                    }
                }
            }
        }

        /// <summary>
        /// Ensures the test folder exists both on disk and in the AssetDatabase.
        /// </summary>
        protected static void EnsureTestFolder()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string absoluteDirectory = Path.Combine(projectRoot, TestRoot);
                if (!Directory.Exists(absoluteDirectory))
                {
                    Directory.CreateDirectory(absoluteDirectory);
                }
            }

            if (!AssetDatabase.IsValidFolder(TestRoot))
            {
                string result = AssetDatabase.CreateFolder("Assets", "__DetectAssetChangedTests__");
                if (string.IsNullOrEmpty(result))
                {
                    Debug.LogWarning(
                        $"EnsureTestFolder: Failed to create folder '{TestRoot}' in AssetDatabase"
                    );
                }

                // Refresh to ensure Unity recognizes the new folder
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        /// <summary>
        /// Clears all test handler state to ensure clean test isolation.
        /// Delegates to the centralized <see cref="AssetPostprocessorTestHandlers.FlushAndClearAll"/>
        /// helper so every <c>[DetectAssetChanged]</c> handler in the test assemblies is
        /// cleared — not just the ones this fixture personally uses. The helper
        /// internally flushes any pending <see cref="AssetPostprocessorDeferral"/>
        /// drains first so a late-arriving drain cannot re-populate the statics we
        /// just cleared.
        ///
        /// <para>Relationship to the teardown-flush contract: the contract test
        /// <c>TestTeardownsThatClearHandlerStateFlushDeferralsFirst</c> accepts
        /// three call sites as flush-equivalents — a direct
        /// <c>AssetPostprocessorDeferral.FlushForTesting()</c> call,
        /// <see cref="AssetPostprocessorTestHandlers.FlushAndClearAll"/>, or
        /// <see cref="AssetPostprocessorTestHandlers.AssertCleanAndClearAll"/>.
        /// Because this method's body IS a call to <c>FlushAndClearAll</c>,
        /// calling <c>ClearTestState()</c> (or <c>base.ClearTestState()</c>)
        /// from a derived fixture also satisfies the contract transitively;
        /// the scanner additionally whitelists the literal token
        /// <c>ClearTestState(</c> as flush-equivalent for that reason. The
        /// transitive delegation is guarded by
        /// <c>CentralizedClearHelpersActuallyFlush</c>, which fails loudly if
        /// this body ever stops routing through a terminal flush root.</para>
        /// </summary>
        protected virtual void ClearTestState()
        {
            AssetPostprocessorTestHandlers.FlushAndClearAll();
        }

        /// <summary>
        /// Resets the processor with a clean state and ensures the test folder is properly registered.
        /// This method should be called when a test needs to reinitialize the processor after the
        /// standard SetUp has already run. It ensures the test folder exists before enabling test
        /// asset inclusion to avoid "Folder not found" warnings from AssetDatabase.FindAssets.
        /// Re-applies <see cref="FixtureAllowlist"/> after the reset so the structural
        /// defense against cross-fixture pollution is preserved for the remainder of
        /// the test.
        /// </summary>
        protected static void ResetProcessorWithCleanState()
        {
            DetectAssetChangeProcessor.ResetForTesting();
            EnsureTestFolder();
            DetectAssetChangeProcessor.IncludeTestAssets = true;
            DetectAssetChangeProcessor.TestAssetFolderAllowlist = FixtureAllowlist;
        }

        /// <summary>
        /// Deletes an asset if it exists at the specified path.
        /// </summary>
        /// <param name="assetPath">The Unity-relative asset path.</param>
        protected static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        /// <summary>
        /// Creates a test payload asset (TestDetectableAsset) at the default path.
        /// </summary>
        protected void CreatePayloadAsset()
        {
            CreatePayloadAssetAt(DefaultPayloadAssetPath);
        }

        /// <summary>
        /// Creates a test payload asset (TestDetectableAsset) at the specified path.
        /// </summary>
        /// <param name="assetPath">The Unity-relative path where the asset should be created.</param>
        protected void CreatePayloadAssetAt(string assetPath)
        {
            EnsureTestFolder();
            TestDetectableAsset payload = Track(
                ScriptableObject.CreateInstance<TestDetectableAsset>()
            );
            CreateAndImportAsset(payload, assetPath);
        }

        /// <summary>
        /// Creates an alternate test payload asset (TestAlternateDetectableAsset) at the default path.
        /// </summary>
        protected void CreateAlternatePayloadAsset()
        {
            CreateAlternatePayloadAssetAt(DefaultAlternatePayloadAssetPath);
        }

        /// <summary>
        /// Creates an alternate test payload asset (TestAlternateDetectableAsset) at the specified path.
        /// </summary>
        /// <param name="assetPath">The Unity-relative path where the asset should be created.</param>
        protected void CreateAlternatePayloadAssetAt(string assetPath)
        {
            EnsureTestFolder();
            TestAlternateDetectableAsset payload = Track(
                ScriptableObject.CreateInstance<TestAlternateDetectableAsset>()
            );
            CreateAndImportAsset(payload, assetPath);
        }

        /// <summary>
        /// Creates and tracks a handler asset of the specified type.
        /// </summary>
        /// <typeparam name="T">The ScriptableObject handler type.</typeparam>
        /// <param name="assetPath">The Unity-relative path where the handler should be created.</param>
        protected void EnsureHandlerAsset<T>(string assetPath)
            where T : ScriptableObject
        {
            if (AssetDatabase.LoadAssetAtPath<T>(assetPath) != null)
            {
                return;
            }

            T handler = Track(ScriptableObject.CreateInstance<T>());
            CreateAndImportAsset(handler, assetPath);
        }

        /// <summary>
        /// Creates an asset and forces it to be imported and indexed by the
        /// AssetDatabase BEFORE control returns, even while the fixture-wide
        /// <see cref="BatchedEditorTestBase"/> <c>StartAssetEditing</c> batch is open.
        /// </summary>
        /// <remarks>
        /// <para>
        /// These fixtures derive from <see cref="BatchedEditorTestBase"/>, which holds a
        /// single <c>StartAssetEditing</c> batch open for the whole fixture. While that
        /// batch is open, <see cref="AssetDatabase.CreateAsset(Object, string)"/> DEFERS
        /// the import, and <see cref="AssetDatabaseBatchHelper.SaveAndRefreshIfNotBatching"/>
        /// is a no-op (it only refreshes when NOT batching). The newly-created asset is
        /// therefore not yet importable: <see cref="AssetDatabase.GetMainAssetTypeAtPath"/>
        /// and <see cref="AssetDatabase.LoadAssetAtPath"/> return <c>null</c> under
        /// <c>-batchmode</c>. The change processor's <c>AppendCreatedAssets</c> resolves a
        /// created asset only through those calls, so it sees ZERO created assets and the
        /// handler never fires -- which is exactly the "0 invocations" mass failure these
        /// fixtures hit in CI.
        /// </para>
        /// <para>
        /// <see cref="CommonTestBase.ExecuteWithImmediateImport(Action, bool)"/> pauses the
        /// batch, force-imports synchronously, runs the create, then force-imports again so
        /// the asset is fully indexed before the batch resumes. The post-create load is a
        /// diagnostic tripwire: if a future change re-breaks the import contract, the test
        /// fails immediately with a clear cause instead of a cryptic "0 invocations".
        /// </para>
        /// </remarks>
        private void CreateAndImportAsset(Object asset, string assetPath)
        {
            ExecuteWithImmediateImport(
                () => AssetDatabase.CreateAsset(asset, assetPath),
                refreshAfter: true
            );

            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) == null)
            {
                throw new InvalidOperationException(
                    $"Asset at '{assetPath}' was created but is not loadable from the "
                        + "AssetDatabase after a forced synchronous import. The change "
                        + "processor cannot resolve a created asset it cannot load, so "
                        + "every handler-invocation assertion would fail with 0 "
                        + "invocations. This indicates the batch-pause/import contract "
                        + "in ExecuteWithImmediateImport regressed under -batchmode."
                );
            }
        }

        /// <summary>
        /// Creates a subfolder within the test root folder.
        /// </summary>
        /// <param name="subFolderName">The name of the subfolder to create.</param>
        /// <returns>The full path to the created subfolder.</returns>
        protected static string CreateTestSubFolder(string subFolderName)
        {
            EnsureTestFolder();
            string subFolderPath = TestRoot + "/" + subFolderName;

            if (!AssetDatabase.IsValidFolder(subFolderPath))
            {
                AssetDatabase.CreateFolder(TestRoot, subFolderName);
            }

            return subFolderPath;
        }

        /// <summary>
        /// Verifies that all tracked test assets have been cleaned up properly.
        /// Useful for cleanup verification tests.
        /// </summary>
        /// <returns>True if all test assets have been cleaned up; otherwise, false.</returns>
        protected static bool VerifyTestFolderCleanedUp()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                return false;
            }

            // Also check for duplicates
            string[] allFolders = AssetDatabase.GetSubFolders("Assets");
            if (allFolders != null)
            {
                foreach (string folder in allFolders)
                {
                    string folderName = Path.GetFileName(folder);
                    if (
                        folderName != null
                        && folderName.StartsWith(
                            "__DetectAssetChangedTests__",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
