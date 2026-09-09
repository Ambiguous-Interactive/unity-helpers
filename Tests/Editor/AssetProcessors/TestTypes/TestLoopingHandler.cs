// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test handler that counts invocations for loop detection testing.
    /// </summary>
    internal static class TestLoopingHandler
    {
        public static int InvocationCount => _invocationCount;

        private static int _invocationCount;

        public static void Clear()
        {
            _invocationCount = 0;
        }

        [DetectAssetChanged(typeof(TestDetectableAsset))]
        private static void OnLoopingChange(AssetChangeContext context)
        {
            _invocationCount++;
        }
    }
}
#endif
