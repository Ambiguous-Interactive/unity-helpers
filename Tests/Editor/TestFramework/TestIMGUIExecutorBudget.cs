// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    // Bounds the synchronous IMGUI passes TestIMGUIExecutor.Run drives (Layout then
    // Repaint). A healthy run completes in those two passes / a fraction of a
    // millisecond; the defaults are enormous headroom and only trip if a future
    // change turns the pump into a runaway loop -- the budget keeps that a fast,
    // named single-test failure instead of a stall.
    internal readonly struct TestIMGUIExecutorBudget
    {
        // A healthy run completes in 2 passes / well under a millisecond, so these are
        // many orders of magnitude of headroom; they only trip on a runaway pump.
        // Single source of truth so the factories below never drift from Default.
        private const int DefaultMaxFrames = 10000;
        private const double DefaultMaxSeconds = 60d;

        internal int MaxFrames { get; }
        internal double MaxSeconds { get; }

        internal TestIMGUIExecutorBudget(int maxFrames, double maxSeconds)
        {
            MaxFrames = maxFrames;
            MaxSeconds = maxSeconds;
        }

        internal static TestIMGUIExecutorBudget Default
        {
            get { return new TestIMGUIExecutorBudget(DefaultMaxFrames, DefaultMaxSeconds); }
        }

        internal static TestIMGUIExecutorBudget WithFrames(int maxFrames)
        {
            return new TestIMGUIExecutorBudget(maxFrames, DefaultMaxSeconds);
        }

        internal static TestIMGUIExecutorBudget WithSeconds(double maxSeconds)
        {
            return new TestIMGUIExecutorBudget(DefaultMaxFrames, maxSeconds);
        }
    }
#endif
}
