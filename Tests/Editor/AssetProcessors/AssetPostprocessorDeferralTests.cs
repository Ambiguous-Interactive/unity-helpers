// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.AssetProcessors;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    /// Behavioral unit tests for <see cref="AssetPostprocessorDeferral"/>. Focuses
    /// on the reentrant fan-out boundary (successful drain vs. iteration-cap
    /// warning) so that a future refactor that silently changes the cap, the
    /// reentrant append handling, or the dedup logic is caught by a dedicated
    /// regression test rather than by downstream fixture flakiness.
    /// </summary>
    [TestFixture]
    public sealed class AssetPostprocessorDeferralTests
    {
        [SetUp]
        public void SetUp()
        {
            // Any pending drains from a prior test must not leak into this one.
            // Order matters: reset BEFORE the skip check so pollution is wiped
            // even when this fixture is going to Inconclusive — we don't want
            // leaked state to roll forward into whatever fixture runs next.
            AssetPostprocessorDeferral.ResetForTesting();
            SkipIfDeferralDisabled();
        }

        [TearDown]
        public void TearDown()
        {
            // Cap-hit tests deliberately leave a drain queued; wipe state so the
            // next fixture starts clean.
            AssetPostprocessorDeferral.ResetForTesting();
            // Defense-in-depth: fail loudly if a test produced an unexpected
            // log (Debug.LogException / Debug.LogWarning) that was not matched
            // by a LogAssert.Expect. NUnit's LogAssert state is otherwise
            // process-global and can leak an un-consumed expectation into the
            // next test.
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Mirrors <c>AssetPostprocessorLogHygieneTests.SkipIfDeferralDisabled</c>.
        /// When <see cref="UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks"/>
        /// is <see langword="false"/>, <see cref="AssetPostprocessorDeferral.Schedule"/>
        /// runs drains inline instead of queueing them — which makes the cap-hit
        /// test recurse unboundedly (stack overflow, crashes Unity) and the dedup
        /// test run the same delegate three times inline (count=3, asserts fail).
        /// Skip rather than attempt to force-toggle the setting, because the
        /// tests specifically exercise the deferred-path internals.
        /// </summary>
        private static void SkipIfDeferralDisabled()
        {
            if (!UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks())
            {
                Assert.Inconclusive(
                    "Skipping: UnityHelpersSettings.GetDeferAssetPostprocessorCallbacks() is false. "
                        + "This fixture only exercises the deferred path; re-enable the setting to run it."
                );
            }
        }

        /// <summary>
        /// Verifies the reentrant fan-out happy path: when a drain re-schedules
        /// a fresh delegate (distinct from itself, so the per-caller dedup does
        /// not short-circuit the append), each reentrant append is processed in
        /// a subsequent iteration of <c>FlushForTesting</c>'s outer loop. With a
        /// finite fan-out well below the cap, the queue drains cleanly with no
        /// warning and no items remaining.
        /// </summary>
        [Test]
        public void FlushForTestingReEntrantAppendsUnderCapDrainsCleanly()
        {
            int callCount = 0;
            int remaining = 10;

            Action CreateSelfReschedulingAction()
            {
                Action action = null;
                action = () =>
                {
                    callCount++;
                    remaining--;
                    if (0 < remaining)
                    {
                        AssetPostprocessorDeferral.Schedule(CreateSelfReschedulingAction());
                    }
                };
                return action;
            }

            AssetPostprocessorDeferral.Schedule(CreateSelfReschedulingAction());

            AssetPostprocessorDeferral.FlushForTesting();

            Assert.AreEqual(10, callCount, "All 10 reentrant drains should have executed.");
            Assert.AreEqual(0, remaining, "Re-schedule counter should have exhausted.");
            Assert.AreEqual(
                0,
                AssetPostprocessorDeferral.PendingDrainCountForTesting,
                "Queue should be empty after clean drain."
            );
        }

        /// <summary>
        /// Verifies that a drain which re-schedules fresh delegates indefinitely
        /// hits the iteration cap, emits the documented warning, and leaves at
        /// most one drain pending (the most recently scheduled one). The exact
        /// call-count floor pins the documented cap value so a silent change to
        /// <c>FlushIterationCap</c> fails the test.
        /// </summary>
        [Test]
        public void FlushForTestingUnboundedReEntrantRescheduleHitsCapAndWarns()
        {
            int callCount = 0;

            Action CreateNeverTerminatingAction()
            {
                Action action = null;
                action = () =>
                {
                    callCount++;
                    AssetPostprocessorDeferral.Schedule(CreateNeverTerminatingAction());
                };
                return action;
            }

            AssetPostprocessorDeferral.Schedule(CreateNeverTerminatingAction());

            LogAssert.Expect(LogType.Warning, new Regex("FlushForTesting hit the iteration cap"));
            AssetPostprocessorDeferral.FlushForTesting();

            Assert.AreEqual(
                32,
                callCount,
                "Expected exactly FlushIterationCap (32) iterations before the cap fires. "
                    + "If this number changes, update FlushIterationCap's doc in AssetPostprocessorDeferral."
            );
            Assert.AreEqual(
                1,
                AssetPostprocessorDeferral.PendingDrainCountForTesting,
                "Cap-hit path should leave exactly one drain pending (the last re-schedule)."
            );
        }

        /// <summary>
        /// Verifies the per-caller dedup: scheduling the SAME delegate reference
        /// repeatedly before a flush coalesces into a single invocation. Protects
        /// against a regression where a handler that is scheduled from multiple
        /// asset-import call sites in one batch drains multiple times.
        /// </summary>
        [Test]
        public void ScheduleSameDelegateMultipleTimesDeduplicatesToSingleDrain()
        {
            int callCount = 0;
            Action drain = () => callCount++;

            AssetPostprocessorDeferral.Schedule(drain);
            AssetPostprocessorDeferral.Schedule(drain);
            AssetPostprocessorDeferral.Schedule(drain);

            AssetPostprocessorDeferral.FlushForTesting();

            Assert.AreEqual(
                1,
                callCount,
                "Dedup should have collapsed three schedules to one drain."
            );
        }

        /// <summary>
        /// Verifies that a delegate can safely schedule itself while it is
        /// being drained. The reentrant schedule must enqueue a next-iteration
        /// run rather than being dropped by dedup against the currently-running
        /// batch.
        /// </summary>
        [Test]
        public void ScheduleSameDelegateDuringDrainReschedulesForNextIteration()
        {
            int callCount = 0;
            int remaining = 5;

            Action drain = null;
            drain = () =>
            {
                callCount++;
                remaining--;
                if (0 < remaining)
                {
                    AssetPostprocessorDeferral.Schedule(drain);
                }
            };

            AssetPostprocessorDeferral.Schedule(drain);

            AssetPostprocessorDeferral.FlushForTesting();

            Assert.AreEqual(
                5,
                callCount,
                "Self-rescheduled drain should run once per requested iteration."
            );
            Assert.AreEqual(
                0,
                remaining,
                "Re-schedule loop should exhaust all planned iterations."
            );
            Assert.AreEqual(
                0,
                AssetPostprocessorDeferral.PendingDrainCountForTesting,
                "Queue should be empty after all self-rescheduled iterations run."
            );
        }

        /// <summary>
        /// Pins the ordering semantic of dedup: scheduling A, B, A should keep
        /// A in its original position and drop the second A. Protects against
        /// a refactor that moves dedup to a <c>HashSet&lt;Action&gt;</c> with
        /// non-deterministic iteration, or to a last-wins strategy that would
        /// invert ordering. Drain order matters for handlers that rely on
        /// schedule-order side effects.
        /// </summary>
        [Test]
        public void ScheduleDedupPreservesOriginalInsertionOrder()
        {
            List<string> order = new();
            Action a = () => order.Add("A");
            Action b = () => order.Add("B");

            AssetPostprocessorDeferral.Schedule(a);
            AssetPostprocessorDeferral.Schedule(b);
            AssetPostprocessorDeferral.Schedule(a);

            AssetPostprocessorDeferral.FlushForTesting();

            CollectionAssert.AreEqual(
                new[] { "A", "B" },
                order,
                "Dedup should keep A at its original position (before B) and drop the later duplicate."
            );
        }

        /// <summary>
        /// Pins the reference-equality semantic of <see cref="AssetPostprocessorDeferral.Schedule"/>'s
        /// dedup: two delegates that <see cref="Delegate.Equals(object)"/> considers equal (same
        /// Method + same Target) but which are distinct references must BOTH be scheduled. If the
        /// dedup ever regresses back to <c>List&lt;Action&gt;.Contains</c>, this test collapses
        /// the two schedules to one and fails. The test uses <see cref="Delegate.CreateDelegate(Type, object, MethodInfo)"/>
        /// to guarantee the structural-equal/distinct-reference premise across compiler versions
        /// (hand-rolled lambdas can be cached by Roslyn in outer display-class fields, producing
        /// reference-equal delegates and silently making the test premise unenforceable).
        /// </summary>
        [Test]
        public void ScheduleStructurallyEqualButDistinctDelegatesAreNotDeduplicated()
        {
            StructuralEqualityTarget target = new();
            MethodInfo drainMethod = typeof(StructuralEqualityTarget).GetMethod(
                nameof(StructuralEqualityTarget.Drain),
                BindingFlags.Instance | BindingFlags.Public
            );
            Assert.IsTrue(drainMethod != null, "Expected StructuralEqualityTarget.Drain to exist.");

            Action first = (Action)Delegate.CreateDelegate(typeof(Action), target, drainMethod);
            Action second = (Action)Delegate.CreateDelegate(typeof(Action), target, drainMethod);

            Assert.IsTrue(
                first.Equals(second),
                "Premise: Delegate.CreateDelegate with the same target+method must produce structurally-equal delegates."
            );
            Assert.IsFalse(
                ReferenceEquals(first, second),
                "Premise: Delegate.CreateDelegate must allocate a fresh delegate on each call."
            );

            AssetPostprocessorDeferral.Schedule(first);
            AssetPostprocessorDeferral.Schedule(second);

            AssetPostprocessorDeferral.FlushForTesting();

            Assert.AreEqual(
                2,
                target.CallCount,
                "Both structurally-equal-but-distinct delegates must fire. "
                    + "If count is 1, Schedule's dedup has regressed from reference equality back to structural Delegate.Equals."
            );
        }

        /// <summary>
        /// Verifies that a drain which throws an exception is caught, logged,
        /// and does not abort processing of subsequent drains queued in the
        /// same batch. Regression guard for the <c>RunSafely</c> exception
        /// filter.
        /// </summary>
        [Test]
        public void FlushForTestingDrainThrowsIsSwallowedAndLaterDrainsStillRun()
        {
            int secondCallCount = 0;

            AssetPostprocessorDeferral.Schedule(() =>
                throw new InvalidOperationException("synthetic drain failure")
            );
            AssetPostprocessorDeferral.Schedule(() => secondCallCount++);

            LogAssert.Expect(
                LogType.Exception,
                new Regex("InvalidOperationException: synthetic drain failure")
            );
            AssetPostprocessorDeferral.FlushForTesting();

            Assert.AreEqual(
                1,
                secondCallCount,
                "Second drain should still run even after the first threw."
            );
        }

        private sealed class StructuralEqualityTarget
        {
            public int CallCount;

            public void Drain()
            {
                CallCount++;
            }
        }
    }
}
