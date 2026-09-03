// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_INCLUDE_TESTS
namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test handler that intentionally throws an exception to verify error isolation.
    /// Used to test that exceptions in one handler do not prevent other handlers from executing.
    /// </summary>
    /// <remarks>
    /// <see cref="ShouldThrow"/> defaults to <c>false</c> to prevent accidental test pollution.
    /// Tests that need exception throwing behavior should explicitly set <see cref="ShouldThrow"/> to <c>true</c>.
    /// </remarks>
    internal static class TestExceptionThrowingHandler
    {
        private static int _invocationCount;
        private static bool _shouldThrow;

        public static int InvocationCount => _invocationCount;

        /// <summary>
        /// Gets or sets whether this handler should throw an exception when invoked.
        /// Defaults to <c>false</c> to prevent accidental test pollution.
        /// </summary>
        public static bool ShouldThrow
        {
            get => _shouldThrow;
            set => _shouldThrow = value;
        }

        public static void Clear()
        {
            _invocationCount = 0;
            /*
                _shouldThrow is deliberately not reset here: Clear resets invocation state, and the
                base class ClearTestState resets the behavior configuration after calling it.
            */
        }

        [DetectAssetChanged(typeof(TestDetectableAsset))]
        private static void OnAssetChangedWithException(AssetChangeContext context)
        {
            _invocationCount++;
            if (_shouldThrow)
            {
                throw new InvalidOperationException(
                    "TestExceptionThrowingHandler intentionally threw an exception for testing."
                );
            }
        }
    }
}
#endif
