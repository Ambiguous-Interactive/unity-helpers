// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using static WallstopStudios.UnityHelpers.Editor.AssetProcessors.AssetPostprocessorDeferral;

    /// <summary>Provides test-side setup and inspection for AssetPostprocessorDeferral.</summary>
    internal static class AssetPostprocessorDeferralTestAccess
    {
        private const int FlushIterationCap = 32;

        /// <summary>
        /// Test-only snapshot of the pending-drain count. Used by regression
        /// tests that verify cap/drain behavior without pulling in the full
        /// reflection machinery.
        /// </summary>
        internal static int PendingDrainCount => PendingDrains.Count;

        /// <summary>
        /// Synchronously drains any pending actions, iterating until the queue is
        /// stable so a drain that reentrantly calls <see cref="Schedule"/> does
        /// not leave items in the queue for the next test's setup to inherit.
        /// Intended for tests to avoid yielding an editor frame.
        ///
        /// Bounded by <see cref="FlushIterationCap"/> iterations to prevent a
        /// buggy handler that re-schedules itself from hanging the test run; if
        /// the cap is hit, the method returns with drains still pending, logs a
        /// warning, and those drains fire on the next editor tick (potentially
        /// polluting the next test — the warning is the caller's signal to
        /// investigate).
        ///
        /// Note on dormant delayCalls: when a drain appends to
        /// <see cref="PendingDrains"/> during its execution,
        /// <see cref="DrainPending"/> re-arms an <see cref="EditorApplication.delayCall"/>
        /// subscription for the next editor tick. This loop then drains that
        /// queue synchronously in the next iteration, so the delayCall (when it
        /// eventually fires) observes an empty queue and returns as a harmless
        /// no-op. Within a single reentrant iteration, at most ONE dormant
        /// delayCall is registered: both <see cref="Schedule"/> and
        /// <see cref="DrainPending"/> gate on <c>_scheduled</c> and will not
        /// double-register. Across a full flush cycle, however, the top of each
        /// iteration clears <c>_scheduled = false</c>, so up to
        /// <see cref="FlushIterationCap"/> dormant <c>DrainScheduled</c>
        /// callbacks can accumulate on <see cref="EditorApplication.delayCall"/>
        /// — each one a harmless no-op when it fires. Editor-tick telemetry may
        /// therefore show between zero and <see cref="FlushIterationCap"/>
        /// no-op <c>DrainScheduled</c> invocations per flush cycle (zero when
        /// no reentrant appends happened, one per iteration that had them).
        /// </summary>
        internal static void Flush()
        {
            if (_draining)
            {
                // Reentrant flush cannot drain its own active queue; warn without aborting the outer batch.
                Debug.LogWarning(
                    "Flush called reentrantly during drain — flush is a no-op; "
                        + "ensure tests don't call Flush from a handler callback."
                );
                return;
            }

            for (int iteration = 0; iteration < FlushIterationCap; ++iteration)
            {
                // Clear scheduling state before callbacks can reenter and enqueue another batch.
                _scheduled = false;
                DrainPending();

                if (PendingDrains.Count == 0)
                {
                    _scheduled = false;
                    return;
                }
                // Take synchronous ownership of the reentrant batch instead of racing its scheduled tick.
            }

            Debug.LogWarning(
                "Flush hit the iteration cap ("
                    + FlushIterationCap
                    + ") with "
                    + PendingDrains.Count
                    + " drain(s) still pending. A drain handler is likely re-scheduling itself. "
                    + "Remaining drains will fire on the next editor tick, which may pollute the next test."
            );
        }

        /// <summary>
        /// Test-only reset hook. Wipes <see cref="PendingDrains"/> and the
        /// scheduling flags, mirroring <see cref="ResetForDomainReload"/>. Tests
        /// that deliberately exercise edge cases (e.g. hitting
        /// <see cref="FlushIterationCap"/>) may leave drains queued; calling
        /// this from a TearDown guarantees the next test starts with a
        /// quiescent deferral.
        ///
        /// Caveat — dormant <see cref="EditorApplication.delayCall"/> subscriptions
        /// are NOT purged by this reset. Each call to <see cref="Schedule"/> or
        /// <see cref="DrainPending"/>'s fallback appends <see cref="DrainScheduled"/>
        /// to Unity's multicast <c>delayCall</c>, and Unity does not expose a
        /// safe way to dequeue a specific subscription mid-flight. Those
        /// subscriptions remain pending and fire on subsequent editor ticks —
        /// but because <see cref="DrainPending"/> early-returns on an empty
        /// <see cref="PendingDrains"/>, each dormant fire is a harmless no-op.
        /// Consequence: do NOT treat <see cref="PendingDrainCount"/>
        /// as a proxy for "no delayCall callback is pending". It only reflects
        /// the drain queue; the delayCall multicast may still hold stale
        /// subscriptions that will quietly no-op when they fire.
        /// </summary>
        internal static void Reset()
        {
            ResetForDomainReload();
        }
    }
#endif
}
