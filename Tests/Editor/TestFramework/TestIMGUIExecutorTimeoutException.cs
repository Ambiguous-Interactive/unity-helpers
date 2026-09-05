// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    using System;
    using System.Globalization;

    // Include the exhausted budget so a runaway pump produces an actionable test failure.
    internal sealed class TestIMGUIExecutorTimeoutException : Exception
    {
        internal TestIMGUIExecutorTimeoutException(
            int framesPumped,
            double secondsElapsed,
            TestIMGUIExecutorBudget budget
        )
            : base(BuildMessage(framesPumped, secondsElapsed, budget)) { }

        private static string BuildMessage(
            int framesPumped,
            double secondsElapsed,
            TestIMGUIExecutorBudget budget
        )
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "TestIMGUIExecutor ran {0} IMGUI pass(es) over {1:0.000}s without completing "
                    + "(budget: {2} passes / {3:0.###}s). Failing fast so a single IMGUI test "
                    + "cannot stall the whole run.",
                framesPumped,
                secondsElapsed,
                budget.MaxFrames,
                budget.MaxSeconds
            );
        }
    }
#endif
}
