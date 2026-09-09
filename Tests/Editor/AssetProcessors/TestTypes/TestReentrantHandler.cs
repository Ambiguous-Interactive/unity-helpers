// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;

    /// <summary>
    /// Test handler that triggers reentrant asset change processing.
    /// </summary>
    internal static class TestReentrantHandler
    {
        public static int InvocationCount => _invocationCount;

        private static int _invocationCount;
        private static bool _triggerNestedChange;
        private static string _watchedPath;

        public static void Configure(string assetPath)
        {
            _watchedPath = assetPath;
            _triggerNestedChange = true;
        }

        public static void Clear()
        {
            _invocationCount = 0;
            _triggerNestedChange = false;
            _watchedPath = null;
        }

        [DetectAssetChanged(typeof(TestDetectableAsset))]
        private static void OnReentrantChange(AssetChangeContext context)
        {
            _invocationCount++;
            if (
                _triggerNestedChange
                && _invocationCount == 1
                && !string.IsNullOrEmpty(_watchedPath)
            )
            {
                DetectAssetChangeProcessor.ProcessChangesForTesting(
                    new[] { _watchedPath },
                    null,
                    null,
                    null
                );
            }
        }
    }
}
#endif
