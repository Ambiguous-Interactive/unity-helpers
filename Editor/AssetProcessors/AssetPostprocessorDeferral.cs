// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    /// Shared deferral primitive for <see cref="AssetPostprocessor"/> callbacks.
    /// Routes work out of Unity's asset-import phase via <c>EditorApplication.delayCall</c>, so
    /// that reentering the <c>AssetDatabase</c> happens on an editor tick of its own rather than
    /// while Unity is still importing.
    /// </summary>
    /// <remarks>
    /// <b>Deferral is necessary and not sufficient, and this summary claimed otherwise for three
    /// sessions (#280).</b> It does not make <c>AssetDatabase.LoadAllAssetsAtPath</c> safe. Unity
    /// raises "SendMessage cannot be called during Awake, CheckConsistency, or OnValidate" around
    /// every <c>OnValidate</c> it runs, at any time -- and loading an asset deserializes it, which
    /// runs the consumer's <c>OnValidate</c> inside the drain, one tick later, exactly as it would
    /// have inside the callback. A question that asset metadata can answer must never be answered
    /// by a load.
    /// </remarks>
    internal static class AssetPostprocessorDeferral
    {
        private static readonly List<Action> PendingDrains = new();
        private static bool _scheduled;
        private static bool _draining;
        private static int? _mainThreadId;

        /// <summary>
        /// Enqueues <paramref name="drain"/> to run one editor tick after the current
        /// asset-import phase completes. Invocations are deduplicated by delegate
        /// reference (using <see cref="object.ReferenceEquals"/>, not
        /// <see cref="Delegate.Equals(object)"/>): scheduling the same delegate
        /// reference multiple times before the drain fires coalesces into a single
        /// invocation. Structurally-equal-but-distinct delegates (for example,
        /// lambdas produced by a local function that captures only outer-method
        /// variables — the C# compiler lowers all such lambdas to the same Method
        /// and Target) are intentionally NOT deduplicated: callers cache their drain
        /// in a <c>static readonly</c> field so the dedup target is identity-based
        /// (see <c>.llm/skills/asset-postprocessor-safety.md</c>). If
        /// <see cref="UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks"/> is
        /// <see langword="false"/>, drains inline for users who require synchronous
        /// callback invocation.
        /// </summary>
        internal static void Schedule(Action drain)
        {
            if (drain == null)
            {
                return;
            }

            AssertOnMainThread();

            if (!ShouldDefer())
            {
                // The setting affects future calls; previously queued drains still run on their scheduled tick.
                RunSafely(drain);
                return;
            }

            // Deduplicate delegate identities; structural equality can merge separately created callbacks sharing captured state.
            bool alreadyPending = false;
            foreach (System.Action pendingDrainsElement in PendingDrains)
            {
                if (ReferenceEquals(pendingDrainsElement, drain))
                {
                    alreadyPending = true;
                    break;
                }
            }
            if (!alreadyPending)
            {
                PendingDrains.Add(drain);
            }

            if (_scheduled)
            {
                return;
            }

            _scheduled = true;
            EditorApplication.delayCall += DrainScheduled;
        }

        /// <summary>
        /// Safety cap on <see cref="FlushForTesting"/> iterations. A handler whose
        /// drain re-schedules itself (directly or transitively) would loop forever;
        /// <see cref="FlushIterationCap"/> bounds that to the smallest number that
        /// still absorbs realistic reentrant fan-out (tests that create N assets,
        /// each of whose handlers re-schedules a cleanup). Reaching the cap surfaces
        /// a warning so the caller can investigate rather than silently leaking drains.
        /// </summary>
        private const int FlushIterationCap = 32;

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
        internal static void FlushForTesting()
        {
            if (_draining)
            {
                // Reentrant flush cannot drain its own active queue; warn without aborting the outer batch.
                Debug.LogWarning(
                    "FlushForTesting called reentrantly during drain — flush is a no-op; "
                        + "ensure tests don't call FlushForTesting from a handler callback."
                );
                return;
            }

            for (int iteration = 0; iteration < FlushIterationCap; iteration++)
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
                "FlushForTesting hit the iteration cap ("
                    + FlushIterationCap
                    + ") with "
                    + PendingDrains.Count
                    + " drain(s) still pending. A drain handler is likely re-scheduling itself. "
                    + "Remaining drains will fire on the next editor tick, which may pollute the next test."
            );
        }

        private static void DrainScheduled()
        {
            _scheduled = false;
            DrainPending();
        }

        private static void DrainPending()
        {
            if (PendingDrains.Count == 0)
            {
                return;
            }

            if (_draining)
            {
                return;
            }

            _draining = true;
            try
            {
                // Clear before invoking the snapshot so callbacks can schedule themselves for the next batch.
                Action[] drainsToRun = PendingDrains.ToArray();
                PendingDrains.Clear();

                foreach (System.Action drainsToRunElement in drainsToRun)
                {
                    RunSafely(drainsToRunElement);
                }

                if (0 < PendingDrains.Count && !_scheduled)
                {
                    _scheduled = true;
                    EditorApplication.delayCall += DrainScheduled;
                }
            }
            finally
            {
                _draining = false;
            }
        }

        private static void RunSafely(Action drain)
        {
            try
            {
                drain();
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                Debug.LogException(ex);
            }
        }

        private static bool ShouldDefer()
        {
            try
            {
                return UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks();
            }
            catch (Exception ex)
                when (ex is not OutOfMemoryException and not StackOverflowException)
            {
                // Default to safe deferral while settings are unavailable during reload.
                Debug.LogException(ex);
                return true;
            }
        }

        [InitializeOnLoadMethod]
        private static void RegisterDomainCleanup()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            AssemblyReloadEvents.beforeAssemblyReload -= ResetForDomainReload;
            AssemblyReloadEvents.beforeAssemblyReload += ResetForDomainReload;
        }

        private static void ResetForDomainReload()
        {
            PendingDrains.Clear();
            _scheduled = false;
            _draining = false;
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
        /// Consequence: do NOT treat <see cref="PendingDrainCountForTesting"/>
        /// as a proxy for "no delayCall callback is pending". It only reflects
        /// the drain queue; the delayCall multicast may still hold stale
        /// subscriptions that will quietly no-op when they fire.
        /// </summary>
        internal static void ResetForTesting()
        {
            ResetForDomainReload();
        }

        /// <summary>
        /// Test-only snapshot of the pending-drain count. Used by regression
        /// tests that verify cap/drain behavior without pulling in the full
        /// reflection machinery.
        /// </summary>
        internal static int PendingDrainCountForTesting => PendingDrains.Count;

        [System.Diagnostics.Conditional("UNITY_ASSERTIONS")]
        [System.Diagnostics.Conditional("DEBUG")]
        private static void AssertOnMainThread()
        {
            int? mainThreadId = _mainThreadId;
            if (mainThreadId == null)
            {
                // Main-thread identity is unavailable before initialization, so the assertion cannot measure this call yet.
                return;
            }

            if (Thread.CurrentThread.ManagedThreadId != mainThreadId.Value)
            {
                Debug.LogError(
                    "AssetPostprocessorDeferral.Schedule called from a background thread. "
                        + "Schedule must be invoked from the Unity main thread."
                );
            }
        }
    }
}
