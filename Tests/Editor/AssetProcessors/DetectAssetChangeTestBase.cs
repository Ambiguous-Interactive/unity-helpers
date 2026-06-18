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
        /// Deletes the test root folder (and anything under it) through the AssetDatabase.
        /// </summary>
        /// <remarks>
        /// Folder creation now goes exclusively through
        /// <see cref="AssetDatabaseBatchHelper.EnsureAssetFolder"/> (AssetDatabase-only, never raw
        /// disk), so Unity can no longer spawn "__DetectAssetChangedTests__ 1" duplicates and there
        /// are never orphaned on-disk folders to scrub. A single recursive
        /// <see cref="AssetDatabase.DeleteAsset(string)"/> on <see cref="TestRoot"/> is therefore
        /// sufficient to leave NO files under <c>Assets</c> after teardown.
        /// </remarks>
        protected static void CleanupTestFolders()
        {
            if (AssetDatabase.IsValidFolder(TestRoot))
            {
                AssetDatabase.DeleteAsset(TestRoot);
            }
        }

        /// <summary>
        /// Ensures the test folder is registered with the AssetDatabase.
        /// </summary>
        /// <remarks>
        /// Delegates to <see cref="AssetDatabaseBatchHelper.EnsureAssetFolder"/>, which pauses the
        /// fixture-wide <see cref="BatchedEditorTestBase"/> batch so the folder is created through
        /// the AssetDatabase synchronously. This avoids the raw <see cref="Directory.CreateDirectory"/>
        /// path that previously left the AssetDatabase out of sync (the cause of the
        /// "__DetectAssetChangedTests__ 1" duplicate folders) and never registered the folder while
        /// the batch was open.
        /// </remarks>
        protected static void EnsureTestFolder()
        {
            if (!AssetDatabaseBatchHelper.EnsureAssetFolder(TestRoot))
            {
                Debug.LogWarning(
                    $"EnsureTestFolder: Failed to register folder '{TestRoot}' in the AssetDatabase."
                );
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
        /// batch is open, <see cref="AssetDatabase.CreateFolder(string, string)"/> is DEFERRED
        /// and <see cref="AssetDatabase.Refresh()"/> is a no-op, so the parent folder is NOT
        /// registered with the AssetDatabase when <see cref="AssetDatabase.CreateAsset(Object, string)"/>
        /// runs. Unity then fails the create with "Parent directory must exist before creating
        /// asset" -- the exact mass failure these fixtures hit in CI. Likewise
        /// <see cref="AssetDatabase.CreateAsset(Object, string)"/> DEFERS the import, and the
        /// newly-created asset is not yet importable: <see cref="AssetDatabase.LoadAssetAtPath"/>
        /// returns <c>null</c> under <c>-batchmode</c>, so the change processor's
        /// <c>AppendCreatedAssets</c> sees ZERO created assets and the handler never fires.
        /// </para>
        /// <para>
        /// <see cref="CommonTestBase.ExecuteWithImmediateImport(Action, bool)"/> pauses the
        /// batch, force-imports synchronously, runs the supplied action, then force-imports
        /// again before the batch resumes. Ensuring the parent folder via
        /// <see cref="AssetDatabaseBatchHelper.EnsureAssetParentFolder"/> INSIDE that same paused
        /// action is what makes the folder real in the AssetDatabase before
        /// <see cref="AssetDatabase.CreateAsset(Object, string)"/> runs. The post-create load is a
        /// diagnostic tripwire: if a future change re-breaks the import contract, the test
        /// fails immediately with a clear cause instead of a cryptic "0 invocations".
        /// </para>
        /// </remarks>
        private void CreateAndImportAsset(Object asset, string assetPath)
        {
            ExecuteWithImmediateImport(
                () =>
                {
                    // Register the parent folder while the batch is paused so the
                    // synchronous CreateFolder takes effect before CreateAsset runs.
                    AssetDatabaseBatchHelper.EnsureAssetParentFolder(assetPath);
                    AssetDatabase.CreateAsset(asset, assetPath);
                },
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
            string subFolderPath = TestRoot + "/" + subFolderName;
            // EnsureAssetFolder recursively registers TestRoot and the subfolder through the
            // AssetDatabase while pausing the fixture batch, so the subfolder is immediately valid.
            AssetDatabaseBatchHelper.EnsureAssetFolder(subFolderPath);
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
