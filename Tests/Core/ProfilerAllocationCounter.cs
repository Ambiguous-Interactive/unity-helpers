// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
#if UNITY_5_3_OR_NEWER
    using System;
    using NUnit.Framework;
    using Unity.Profiling;

    /// <summary>Counts current-thread allocation events after warming a test action.</summary>
    public static class ProfilerAllocationCounter
    {
        private const string Marker = "GC.Alloc";
        private const int MaximumCapacity = 131072;
        private const int MaximumIterations = 20000;
        private const int MaximumWarmup = 1024;

        /// <summary>Returns event counts, refusing unavailable or truncated recorder results.</summary>
        public static long Measure(Action action, int iterations, int capacity, int warmup = 64)
        {
            if (
                action == null
                || iterations < 1
                || MaximumIterations < iterations
                || capacity < 1
                || MaximumCapacity < capacity
                || warmup < 0
                || MaximumWarmup < warmup
            )
            {
                Assert.Fail("The profiler measurement requires bounded inputs and an action.");
            }
            for (int index = 0; index < warmup; ++index)
            {
                action();
            }
            using ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                Marker,
                capacity,
                ProfilerRecorderOptions.CollectOnlyOnCurrentThread
                    | ProfilerRecorderOptions.WrapAroundWhenCapacityReached
            );
            if (!recorder.Valid)
            {
                Assert.Ignore("This runtime does not expose the allocation marker.");
            }
            for (int index = 0; index < iterations; ++index)
            {
                action();
            }
            recorder.Stop();
            Assert.IsFalse(
                recorder.WrappedAround,
                "Allocation samples exceeded recorder capacity."
            );
            long count = 0;
            for (int index = 0; index < recorder.Count; ++index)
            {
                ProfilerRecorderSample sample = recorder.GetSample(index);
                count += sample.Count;
            }
            return count;
        }
    }
#endif
}
