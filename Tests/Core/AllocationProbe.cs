// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using NUnit.Framework;
    using UnityEngine.TestTools.Constraints;
    using Is = UnityEngine.TestTools.Constraints.Is;

    /// <summary>
    /// The control an allocation assertion needs before it means anything.
    /// </summary>
    /// <remarks>
    /// <c>Is.Not.AllocatingGCMemory()</c> needs an instrument that can see an allocation. On an
    /// IL2CPP standalone player it cannot, so the constraint there is the absence of a measurement
    /// rather than a pass. Run this FIRST and skip when it says no: a control asserted after the
    /// subject turns an unmeasurable platform into a red build.
    /// </remarks>
    public static class AllocationProbe
    {
        /// <summary>
        /// How many iterations a probe runs, control and subject alike.
        /// </summary>
        public const int Iterations = 256;

        /*
            Static so nothing can prove the control's allocations dead and optimize them away.
        */
        private static string _sink;

        /// <summary>
        /// Skips the calling test when this platform cannot report an allocation it definitely made.
        /// </summary>
        public static void IgnoreWhenUnmeasurable()
        {
            if (RecorderCanSeeAnAllocation())
            {
                return;
            }

            Assert.Ignore(
                "GC allocation recording is inert on this player, so a 'did not allocate' "
                    + "verdict would prove nothing"
            );
        }

        /// <summary>
        /// Whether an allocation the control definitely causes is one the recorder reports.
        /// </summary>
        public static bool RecorderCanSeeAnAllocation()
        {
            try
            {
                Assert.That(
                    () =>
                    {
                        for (int index = 0; index < Iterations; ++index)
                        {
                            _sink = new string('x', 8 + (index & 7));
                        }
                    },
                    Is.AllocatingGCMemory()
                );
                return true;
            }
            catch (AssertionException)
            {
                return false;
            }
        }
    }
}
