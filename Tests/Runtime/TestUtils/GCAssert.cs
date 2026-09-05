// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using System;
    using NUnit.Framework;

    public static class GCAssert
    {
        /*
            GC.GetAllocatedBytesForCurrentThread access-violates in pre-Unity-6 IL2CPP Release players; managed
            exception handling cannot protect those versions.
        */
        private const string UnavailableReason =
            "GC.GetAllocatedBytesForCurrentThread crashes the IL2CPP player before Unity 6, so "
            + "allocation cannot be measured on this platform. Skipped rather than passed: this "
            + "test verified nothing here, and reporting it green would hide a real regression.";

        /// <summary>
        /// Skips the calling test when this platform cannot read the allocation counter.
        /// </summary>
        /// <remarks>
        /// Public because not every measurement goes through <see cref="MeasureAllocatedBytes"/> --
        /// a test that measures a delta around a specific operation calls
        /// <c>GC.GetAllocatedBytesForCurrentThread</c> itself, and on the affected players that call
        /// is an access violation rather than an exception. It cannot be caught, so it has to be
        /// avoided, and every direct caller has to route through here.
        /// </remarks>
        public static void IgnoreIfAllocationMeasurementUnavailable()
        {
#if ENABLE_IL2CPP && !UNITY_6000_0_OR_NEWER
            Assert.Ignore(UnavailableReason);
#endif
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

            for (int i = 0; i < warmupIterations; i++)
            {
                action();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < measuredIterations; i++)
            {
                action();
            }

            long after = GC.GetAllocatedBytesForCurrentThread();
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
