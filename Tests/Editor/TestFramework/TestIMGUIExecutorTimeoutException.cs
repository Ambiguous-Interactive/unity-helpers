// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.EditorFramework
{
#if UNITY_EDITOR
    using System;
    using System.Globalization;

    // Thrown when TestIMGUIExecutor.Run exhausts its budget before the terminating
    // Repaint event. Naming the frame/time budget makes the otherwise-silent
    // "no graphic device" hang an actionable, single-test failure.
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
                "TestIMGUIExecutor pumped {0} frame(s) over {1:0.000}s without receiving the "
                    + "terminating Repaint event (budget: {2} frames / {3:0.###}s). This usually "
                    + "means the editor is running -nographics with no repaint, or an editor tick "
                    + "stalled (e.g. an unreachable Unity Accelerator). Failing fast so a single "
                    + "IMGUI test cannot hang the whole run.",
                framesPumped,
                secondsElapsed,
                budget.MaxFrames,
                budget.MaxSeconds
            );
        }
    }
#endif
}
