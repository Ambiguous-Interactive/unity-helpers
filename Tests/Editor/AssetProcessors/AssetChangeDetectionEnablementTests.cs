// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Covers when the asset-change watcher is allowed to initialize.
    /// </summary>
    /// <remarks>
    /// Discovering handlers is an all-types / all-methods reflection scan, and running it inside
    /// Unity's import phase has crashed a headless editor natively. The play-mode guard covered one
    /// door into that scan; batch mode went through the other. These tests pin the resulting
    /// policy and the consumer opt-out, including that the package's own suite -- which runs under
    /// batch mode in CI -- still drives the processor through its explicit test entry point.
    /// </remarks>
    [TestFixture]
    public sealed class AssetChangeDetectionEnablementTests : BatchedEditorTestBase
    {
        // The scope captures the enablement on construction and restores it on dispose, so these
        // tests can reassign it freely without any of them owning the restore.
        private AssetChangeDetectionEnabledScope _watcherScope;

        [SetUp]
        public override void BaseSetUp()
        {
            _watcherScope = AssetChangeDetectionUtility.EnabledScope(
                AssetChangeDetectionUtility.Enabled
            );
            base.BaseSetUp();
        }

        [TearDown]
        public override void TearDown()
        {
            // These tests drive initialization directly, so hand the next fixture a clean slate.
            DetectAssetChangeProcessor.ResetForTesting();
            _watcherScope?.Dispose();
            _watcherScope = null;
            base.TearDown();
        }

        [Test]
        public void DefaultsToDisabledInBatchMode()
        {
            AssetChangeDetectionUtility.ResetEnabledToDefault();

            Assert.AreEqual(!Application.isBatchMode, AssetChangeDetectionUtility.Enabled);
        }

        [Test]
        public void ConsumersCanForceTheWatcherOn()
        {
            AssetChangeDetectionUtility.Enabled = true;

            Assert.IsTrue(AssetChangeDetectionUtility.Enabled);
        }

        [Test]
        public void ConsumersCanForceTheWatcherOff()
        {
            AssetChangeDetectionUtility.Enabled = false;

            Assert.IsFalse(AssetChangeDetectionUtility.Enabled);
        }

        [Test]
        public void ResettingDropsAPreviousOverride()
        {
            AssetChangeDetectionUtility.Enabled = !Application.isBatchMode;
            AssetChangeDetectionUtility.ResetEnabledToDefault();

            Assert.IsFalse(DetectAssetChangeProcessor.EnabledOverride.HasValue);
            Assert.AreEqual(!Application.isBatchMode, AssetChangeDetectionUtility.Enabled);
        }

        // This is the fix: the reflection scan that crashed a headless editor must not run when the
        // watcher is disabled, and gating initialization rather than the postprocess callback is
        // what closes the delayCall door as well.
        [Test]
        public void DisablingPreventsInitialization()
        {
            DetectAssetChangeProcessor.ResetForTesting();
            AssetChangeDetectionUtility.Enabled = false;

            DetectAssetChangeProcessor.EnsureInitializedForTesting();

            Assert.IsFalse(DetectAssetChangeProcessor.GetSettingsForTesting().Initialized);
        }

        [Test]
        public void EnablingAllowsInitialization()
        {
            DetectAssetChangeProcessor.ResetForTesting();
            AssetChangeDetectionUtility.Enabled = true;

            DetectAssetChangeProcessor.EnsureInitializedForTesting();

            Assert.IsTrue(DetectAssetChangeProcessor.GetSettingsForTesting().Initialized);
        }

        // The fixtures that drive the processor through ProcessChangesForTesting run under
        // -batchmode in CI, so the explicit test entry point has to initialize regardless of the
        // policy. If this regresses, those fixtures all go quietly green with zero watchers.
        [Test]
        public void TheTestEntryPointInitializesEvenWhenTheWatcherIsDisabled()
        {
            DetectAssetChangeProcessor.ResetForTesting();
            AssetChangeDetectionUtility.Enabled = false;

            DetectAssetChangeProcessor.ProcessChangesForTesting(null, null, null, null);

            Assert.IsTrue(DetectAssetChangeProcessor.GetSettingsForTesting().Initialized);
        }
    }
}
