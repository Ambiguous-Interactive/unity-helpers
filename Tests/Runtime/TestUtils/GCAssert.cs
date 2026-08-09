// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.TestUtils
{
    using System;
    using NUnit.Framework;

    // Utility assertions for reliable GC allocation checks in tests.
    public static class GCAssert
    {
        // GC.GetAllocatedBytesForCurrentThread CRASHES the player, rather than throwing or
        // returning a wrong number, on IL2CPP before Unity 6. Measured on run 31292390551, the
        // first run whose standalone legs compiled with Release C++ (removing MSVC 14.51 from the
        // runners retired the C1001 that had blocked them): 2021.3.45f1 and 2022.3.45f1 both died
        // with an access violation whose top frame is this class, called from
        // EnumExtensionTests.CachedNameDoesNotAllocate, and no results.xml at all. The same test on
        // 6000.5.2f1, same C++ config, reports result="Passed" -- so this is a version boundary and
        // not an IL2CPP-wide limitation.
        //
        // It never surfaced before because standalone legs had always built Debug C++, where the
        // API survives; Release is what a shipped player uses, which is why validating it was worth
        // doing even though it cost this.
        private const bool AllocationMeasurementIsAvailable =
#if ENABLE_IL2CPP && !UNITY_6000_0_OR_NEWER
            false;
#else
            true;
#endif

        private const string UnavailableReason =
            "GC.GetAllocatedBytesForCurrentThread crashes the IL2CPP player before Unity 6, so "
            + "allocation cannot be measured on this platform. Skipped rather than passed: this "
            + "test verified nothing here, and reporting it green would hide a real regression.";

        // Measures allocated bytes for the current thread while executing the action.
        // Runs a short warmup to avoid JIT/cold path noise, then measures over multiple iterations.
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

            // Ignore BEFORE invoking the action: the crash is in this method, so running the action
            // first would take the player down before the skip could be recorded.
            if (!AllocationMeasurementIsAvailable)
            {
                Assert.Ignore(UnavailableReason);
            }

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

        // Asserts that executing the action does not allocate beyond an optional tolerance.
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
