// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using NUnit.Framework;

    /// <summary>Reads a thread allocation counter only after its backend and control qualify.</summary>
    public static class ThreadAllocationCounter
    {
        /// <summary>Explains why unavailable measurements cannot qualify an allocation assertion.</summary>
        public const string UnavailableReason =
            "The thread allocation counter is unsafe in IL2CPP players or failed its retained-allocation control. Allocation evidence is unavailable.";

        private const int ControlLength = 4096;

        /// <summary>Reports whether this backend safely observes a retained allocation.</summary>
        public static bool IsAvailable => ControlLength <= RetainedControlBytes.GetValueOrDefault();

        /// <summary>Gets the observed control bytes, or null when the backend cannot safely be called.</summary>
        public static long? RetainedControlBytes { get; } = MeasureRetainedControl();

        /// <summary>Skips an allocation observation when its counter is unavailable.</summary>
        public static void IgnoreWhenUnavailable()
        {
            if (!IsAvailable)
            {
                Assert.Ignore(UnavailableReason);
            }
        }

        /// <summary>Reads the qualified counter, or skips the calling allocation observation.</summary>
        public static long Read()
        {
            IgnoreWhenUnavailable();
#if ENABLE_IL2CPP && !UNITY_EDITOR
            return 0;
#else
            return GC.GetAllocatedBytesForCurrentThread();
#endif
        }

        private static long? MeasureRetainedControl()
        {
#if ENABLE_IL2CPP && !UNITY_EDITOR
            return null;
#else
            try
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                byte[] control = new byte[ControlLength];
                control[0] = 1;
                long after = GC.GetAllocatedBytesForCurrentThread();
                GC.KeepAlive(control);
                return after - before;
            }
            catch (NotSupportedException)
            {
                return null;
            }
#endif
        }
    }
}
