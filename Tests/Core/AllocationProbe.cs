// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    /// <summary>Preserves the package test assembly's allocation-probe entry points.</summary>
    public static class AllocationProbe
    {
        /// <summary>Gets the shared probe iteration count.</summary>
        public const int Iterations = TestSupport.AllocationProbe.Iterations;

        /// <summary>Skips when the allocation control cannot be observed.</summary>
        public static void IgnoreWhenUnmeasurable()
        {
            TestSupport.AllocationProbe.IgnoreWhenUnmeasurable();
        }

        /// <summary>Reports whether the recorder sees a known allocation.</summary>
        public static bool RecorderCanSeeAnAllocation()
        {
            return TestSupport.AllocationProbe.RecorderCanSeeAnAllocation();
        }
    }
}
