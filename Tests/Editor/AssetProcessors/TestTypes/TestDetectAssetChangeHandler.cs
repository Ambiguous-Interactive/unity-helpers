// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test handler using a static method for asset change detection.
    /// </summary>
    internal sealed class TestDetectAssetChangeHandler : ScriptableObject
    {
        public static IReadOnlyList<AssetChangeContext> RecordedContexts => Recorded;

        private static readonly List<AssetChangeContext> Recorded = new();

        public static void Clear()
        {
            Recorded.Clear();
        }

        [DetectAssetChanged(typeof(TestDetectableAsset))]
        private static void OnTestAssetChanged(AssetChangeContext context)
        {
            Recorded.Add(context);
        }
    }
}
#endif
