// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Drives one <see cref="ValidationRun"/> across editor ticks, a slice at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One run at a time, deliberately. Two project-wide scans sharing the editor's update loop
    /// would each get a full budget, so the editor would pay double for work whose whole design
    /// goal is a bounded cost.
    /// </para>
    /// <para>
    /// The subscription is removed when the run completes, is cancelled, or throws, so a finished
    /// run leaves nothing attached to <c>EditorApplication.update</c>.
    /// </para>
    /// <para>
    /// A domain reload abandons the run. The statics and the <c>EditorApplication.update</c>
    /// subscription are both destroyed, so the completion callback never fires. A caller that needs
    /// a run to survive a script compile has to restart it; nothing here resumes one.
    /// </para>
    /// </remarks>
    public static class ValidationScheduler
    {
        /// <summary>
        /// The slice length used when a caller does not name one: a quarter of a 60Hz frame.
        /// </summary>
        public const double DefaultBudgetMilliseconds = 4.0;

        /// <summary>
        /// The slice length for a run a user asked for and is watching: two 60Hz frames.
        /// </summary>
        /// <remarks>
        /// A slice is spent once per <c>EditorApplication.update</c>, so the budget is a duty
        /// cycle rather than a speed limit, and the background figure is the wrong one for a
        /// foreground scan. Measured 2026-09-01 on 40,008 assets: 4.0s of work, which at 4ms a
        /// tick needs about 1,000 ticks -- ten seconds with the editor focused and far longer
        /// once Unity throttles the update loop for a window nobody is looking at. The run stays
        /// cancellable either way, because the slice is bounded and the button is live between
        /// slices ([#634](https://github.com/Ambiguous-Interactive/unity-helpers/issues/634)).
        /// </remarks>
        public const double InteractiveBudgetMilliseconds = 33.0;

        private static ValidationRun _active;
        private static double _budgetMilliseconds = DefaultBudgetMilliseconds;
        private static Action<ValidationRun> _onComplete;

        /// <summary>The run currently being driven, or <c>null</c>.</summary>
        public static ValidationRun Active => _active;

        /// <summary>Whether a run is currently being driven.</summary>
        public static bool IsRunning => _active != null;

        /// <summary>
        /// The slice length in force, after clamping. Reads
        /// <see cref="DefaultBudgetMilliseconds"/> when nothing is running.
        /// </summary>
        public static double BudgetMilliseconds => _budgetMilliseconds;

        /// <summary>
        /// Begins driving <paramref name="run"/> from the editor's update loop.
        /// </summary>
        /// <param name="run">The run to advance. Ignored when <c>null</c>.</param>
        /// <param name="budgetMilliseconds">
        /// How long each tick may spend. Clamped to a positive value, because a non-positive budget
        /// would still advance one asset per tick and read as a hang rather than as a setting.
        /// </param>
        /// <param name="onComplete">Invoked once, on the main thread, when the run ends.</param>
        /// <returns>
        /// <c>false</c> when the run was <c>null</c>, already complete, or another run is active.
        /// </returns>
        public static bool TryStart(
            ValidationRun run,
            double budgetMilliseconds = DefaultBudgetMilliseconds,
            Action<ValidationRun> onComplete = null
        )
        {
            if (run == null || run.IsComplete || IsRunning)
            {
                return false;
            }

            _active = run;
            // A positive comparison rejects NaN as well as nonpositive budgets.
            _budgetMilliseconds =
                0.0 < budgetMilliseconds ? budgetMilliseconds : DefaultBudgetMilliseconds;
            _onComplete = onComplete;
            EditorApplication.update += Tick;
            return true;
        }

        /// <summary>
        /// Cancels the active run and stops driving it. The completion callback still fires.
        /// </summary>
        public static void Stop()
        {
            ValidationRun run = _active;
            if (run == null)
            {
                return;
            }

            run.Cancel();
            Finish(run);
        }

        private static void Tick()
        {
            ValidationRun run = _active;
            if (run == null)
            {
                EditorApplication.update -= Tick;
                return;
            }

            bool complete;
            try
            {
                complete = run.Step(_budgetMilliseconds);
            }
            catch (Exception thrown)
            {
                // Detach after an engine failure so it cannot throw on every editor tick.
                Debug.LogException(thrown);
                // Mark the abandoned run cancelled before publishing it.
                run.Cancel();
                Finish(run);
                return;
            }

            if (complete)
            {
                Finish(run);
            }
        }

        private static void Finish(ValidationRun run)
        {
            EditorApplication.update -= Tick;
            Action<ValidationRun> onComplete = _onComplete;
            _active = null;
            _onComplete = null;
            _budgetMilliseconds = DefaultBudgetMilliseconds;

            if (onComplete == null)
            {
                return;
            }

            try
            {
                onComplete(run);
            }
            catch (Exception thrown)
            {
                Debug.LogException(thrown);
            }
        }
    }
#endif
}
