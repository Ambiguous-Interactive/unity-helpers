// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    // Bounds how long TestIMGUIExecutor.Run pumps editor frames while waiting for
    // the terminating Repaint event. The defaults are enormous headroom for a
    // healthy editor (a real IMGUI render completes in a handful of frames /
    // milliseconds); they only ever trip when the Repaint never arrives, which is
    // the un-bounded-hang failure mode the budget guards against.
    internal readonly struct TestIMGUIExecutorBudget
    {
        // A healthy editor repaints within a handful of frames / milliseconds, so
        // these are ~1000x headroom; they only trip on the never-repaints hang.
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
