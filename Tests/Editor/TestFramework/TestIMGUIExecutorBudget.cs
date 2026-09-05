// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    // A pass and time budget turns a runaway synchronous IMGUI pump into a named test failure.
    internal readonly struct TestIMGUIExecutorBudget
    {
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
