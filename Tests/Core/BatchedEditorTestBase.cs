// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
#if UNITY_EDITOR
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    /// Base class for Editor tests that wraps the entire test fixture in an AssetDatabase batch scope.
    /// This dramatically improves test performance by deferring all AssetDatabase operations until
    /// fixture teardown, reducing the number of expensive asset refreshes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class automatically:
    /// </para>
    /// <list type="bullet">
    /// <item>Sets <see cref="CommonTestBase.DeferAssetCleanupToOneTimeTearDown"/> to true</item>
    /// <item>Starts an AssetDatabase batch in <see cref="OneTimeSetUp"/></item>
    /// <item>Disposes the batch and performs cleanup in <see cref="OneTimeTearDown"/></item>
    /// </list>
    /// <para>
    /// For tests that require immediate asset import (e.g., SaveAndReimport), use
    /// <see cref="ExecuteWithImmediateImport"/> to temporarily pause the batch.
    /// </para>
    /// </remarks>
    public abstract class BatchedEditorTestBase : CommonTestBase
    {
        private IDisposable _batchScope;

        /// <summary>
        /// Called once before any tests in the fixture run.
        /// Starts the AssetDatabase batch scope that wraps all tests in this fixture.
        /// </summary>
        [OneTimeSetUp]
        public override void CommonOneTimeSetUp()
        {
            base.CommonOneTimeSetUp();

            DeferAssetCleanupToOneTimeTearDown = true;

            _batchScope = AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false);
        }

        /// <summary>
        /// Called once after all tests in the fixture have completed.
        /// Disposes the batch scope, refreshes the AssetDatabase, and cleans up deferred assets.
        /// </summary>
        [OneTimeTearDown]
        public override void OneTimeTearDown()
        {
            try
            {
                if (_batchScope != null)
                {
                    _batchScope.Dispose();
                    _batchScope = null;
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

                CleanupDeferredAssetsAndFolders();
            }
            finally
            {
                try
                {
                    base.OneTimeTearDown();
                }
                finally
                {
                    // Flush deletes before the next fixture can observe stale handler state, even if base teardown throws.
                    AssetPostprocessorDeferral.FlushForTesting();
                }
            }
        }
    }
#endif
}
