// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test MonoBehaviour handler that uses SearchSceneObjects option for asset change detection.
    /// This handler should be found when attached to GameObjects in open scenes.
    /// </summary>
    /// <remarks>
    /// This class must be in a non-Editor folder so it can be attached to GameObjects.
    /// </remarks>
    public sealed class TestSceneAssetChangeHandler : MonoBehaviour
    {
        public static IReadOnlyList<AssetChangeContext> RecordedContexts => Recorded;
        public static IReadOnlyList<TestSceneAssetChangeHandler> RecordedInstances =>
            InvokedInstances;

        private static readonly List<AssetChangeContext> Recorded = new();
        private static readonly List<TestSceneAssetChangeHandler> InvokedInstances = new();

        public static void Clear()
        {
            Recorded.Clear();
            InvokedInstances.Clear();
        }

        /// <summary>
        /// Asserts that the handler state is clean (no recorded data), then clears it.
        /// Use at the start of tests to detect test pollution from prior tests.
        /// </summary>
        public static void AssertCleanAndClear()
        {
            int recordedCount = Recorded.Count;
            int instanceCount = InvokedInstances.Count;
            Clear();
            if (recordedCount != 0 || instanceCount != 0)
            {
                throw new System.InvalidOperationException(
                    $"{nameof(TestSceneAssetChangeHandler)} was not clean at test start. "
                        + $"RecordedContexts.Count={recordedCount}, RecordedInstances.Count={instanceCount}. "
                        + "This indicates test pollution from a prior test."
                );
            }
        }

        [DetectAssetChanged(
            typeof(TestDetectableAsset),
            AssetChangeFlags.Created | AssetChangeFlags.Deleted,
            DetectAssetChangedOptions.SearchSceneObjects
        )]
        private void OnTestAssetChanged(AssetChangeContext context)
        {
            Recorded.Add(context);
            InvokedInstances.Add(this);
        }
    }
}
#endif
