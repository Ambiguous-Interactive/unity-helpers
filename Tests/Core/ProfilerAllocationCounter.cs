// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
#if UNITY_5_3_OR_NEWER
    using System;

    /// <summary>Preserves the package test assembly's profiler-counter entry point.</summary>
    public static class ProfilerAllocationCounter
    {
        /// <summary>Measures allocation events using the shared bounded recorder.</summary>
        public static long Measure(Action action, int iterations, int capacity, int warmup = 64)
        {
            return TestSupport.ProfilerAllocationCounter.Measure(
                action,
                iterations,
                capacity,
                warmup
            );
        }
    }
#endif
}
