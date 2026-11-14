// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using System;
    using NUnit.Framework;
    using ThreadAllocationCounter = WallstopStudios.UnityHelpers.Tests.Core.ThreadAllocationCounter;

    public static class GCAssert
    {
        /// <summary>Skips when the backend cannot safely observe a retained allocation.</summary>
        public static void IgnoreIfAllocationMeasurementUnavailable()
        {
            Core.ThreadAllocationCounter.IgnoreWhenUnavailable();
        }

        public static long MeasureAllocatedBytes(
            Action action,
            int warmupIterations = 5,
            int measuredIterations = 10
        )
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            // Skip before invoking the action because the allocation-counter crash prevents reporting any result.
            IgnoreIfAllocationMeasurementUnavailable();

            for (int i = 0; i < warmupIterations; ++i)
            {
                action();
            }

            long before = ThreadAllocationCounter.Read();

            for (int i = 0; i < measuredIterations; ++i)
            {
                action();
            }

            long after = ThreadAllocationCounter.Read();
            long delta = after - before;
            return delta < 0 ? 0 : delta;
        }

        public static void DoesNotAllocate(
            Action action,
            int warmupIterations = 5,
            int measuredIterations = 10,
            long toleranceBytes = 0
        )
        {
            long allocated = MeasureAllocatedBytes(action, warmupIterations, measuredIterations);
            Assert.LessOrEqual(
                allocated,
                toleranceBytes,
                $"Expected no GC allocations (<= {toleranceBytes} bytes), but measured {allocated} bytes."
            );
        }
    }
}
