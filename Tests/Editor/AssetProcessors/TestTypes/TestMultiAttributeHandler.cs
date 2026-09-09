// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test handler that watches multiple asset types using multiple attributes.
    /// </summary>
    internal static class TestMultiAttributeHandler
    {
        public static IReadOnlyList<AssetInvocationRecord> RecordedInvocations => Recorded;

        private static readonly List<AssetInvocationRecord> Recorded = new();

        public static void Clear()
        {
            Recorded.Clear();
        }

        [DetectAssetChanged(typeof(TestDetectableAsset))]
        [DetectAssetChanged(typeof(TestAlternateDetectableAsset), AssetChangeFlags.Deleted)]
        private static void OnAssetChanged(AssetChangeContext context)
        {
            Recorded.Add(new AssetInvocationRecord(context.AssetType, context.Flags));
        }
    }
}
#endif
