// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
{
#if UNITY_EDITOR

    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    /// <summary>
    ///     Unit tests for <see cref="AssetDatabaseBatchScope"/> and <see cref="AssetDatabaseBatchHelper"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     <strong>Threading Limitations:</strong>
    ///     </para>
    ///     <para>
    ///     The <see cref="AssetDatabaseBatchHelper"/> provides best-effort thread-safety for reads during
    ///     concurrent writes. The internal lock protects counter increments/decrements from data corruption,
    ///     but reads of <see cref="AssetDatabaseBatchHelper.IsCurrentlyBatching"/> and
    ///     <see cref="AssetDatabaseBatchHelper.CurrentBatchDepth"/> may observe intermediate states during
    ///     concurrent modifications. For example, a read on thread A may see a depth of 1 even though
    ///     thread B has already started decrementing but hasn't completed.
    ///     </para>
    ///     <para>
    ///     This is acceptable because Unity's AssetDatabase APIs are main-thread-only anyway, so concurrent
    ///     batch scopes from multiple threads would be incorrect usage. The locking is primarily to prevent
    ///     corruption of the counter values themselves.
    ///     </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Editor")]
    public sealed class AssetDatabaseBatchScopeTests
    {
        /// <summary>
        ///     Called once before any tests in this fixture run.
        ///     Resets counters without calling Unity APIs to handle the case where
        ///     stale static state persists from previous test runs while Unity's
        ///     internal AssetDatabase state has been reset (e.g., domain reload).
        /// </summary>
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            AssetDatabaseBatchHelper.ResetCountersOnly();
        }

        [SetUp]
        public void SetUp()
        {
            AssetDatabaseBatchHelper.ResetBatchDepth();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabaseBatchHelper.ResetBatchDepth();
        }

        [Test]
        public void CounterIncrementsOnScopeEnter()
        {
            int depthBefore = AssetDatabaseBatchHelper.CurrentBatchDepth;

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                int depthDuring = AssetDatabaseBatchHelper.CurrentBatchDepth;

                Assert.That(
                    depthDuring,
                    Is.EqualTo(depthBefore + 1),
                    $"Batch depth should increment by 1 when entering scope. Before: {depthBefore}, During: {depthDuring}"
                );
            }
        }

        [Test]
        public void CounterDecrementsOnScopeExit()
        {
            int depthBefore = AssetDatabaseBatchHelper.CurrentBatchDepth;

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(depthBefore + 1),
                    "Depth should be incremented during scope"
                );
            }

            int depthAfter = AssetDatabaseBatchHelper.CurrentBatchDepth;
            Assert.That(
                depthAfter,
                Is.EqualTo(depthBefore),
                $"Batch depth should return to original value after scope exit. Before: {depthBefore}, After: {depthAfter}"
            );
        }

        [Test]
        public void IsCurrentlyBatchingReturnsTrueInsideScope()
        {
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching before entering scope"
            );

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should be batching inside scope"
                );
            }
        }

        [Test]
        public void IsCurrentlyBatchingReturnsFalseOutsideScope()
        {
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching initially"
            );

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should be batching during scope"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after scope exits"
            );
        }

        [Test]
        public void BeginBatchCreatesValidScope()
        {
            AssetDatabaseBatchScope scope = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.True,
                "BeginBatch should create active batching scope"
            );

            scope.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Disposing scope should end batching"
            );
        }

        [Test]
        public void RefreshIfNotBatchingDoesNothingInsideBatch()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should be batching"
                );

                AssetDatabaseBatchHelper.RefreshIfNotBatching();

                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should still be batching after RefreshIfNotBatching call inside batch"
                );
            }
        }

        [Test]
        public void RefreshIfNotBatchingExecutesOutsideBatch()
        {
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching initially"
            );

            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should still not be batching after RefreshIfNotBatching outside batch"
            );
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after RefreshIfNotBatching"
            );
        }

        [Test]
        public void NestedScopesTrackProperDepth()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Initial depth should be 0"
            );

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "First scope should set depth to 1"
                );

                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(2),
                        "Second nested scope should set depth to 2"
                    );

                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        Assert.That(
                            AssetDatabaseBatchHelper.CurrentBatchDepth,
                            Is.EqualTo(3),
                            "Third nested scope should set depth to 3"
                        );
                    }

                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(2),
                        "Depth should return to 2 after third scope exits"
                    );
                }

                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should return to 1 after second scope exits"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should return to 0 after all scopes exit"
            );
        }

        [Test]
        public void InnerScopeDisposalDoesNotTriggerRefresh()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should be batching in outer scope"
                );

                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.IsCurrentlyBatching,
                        Is.True,
                        "Should still be batching in inner scope"
                    );
                }

                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should still be batching after inner scope exits (outer still active)"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after outer scope exits"
            );
        }

        [Test]
        public void OnlyOutermostScopeTriggersBatchingEnd()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        Assert.That(
                            AssetDatabaseBatchHelper.CurrentBatchDepth,
                            Is.EqualTo(3),
                            "Should be at depth 3"
                        );
                    }

                    Assert.That(
                        AssetDatabaseBatchHelper.IsCurrentlyBatching,
                        Is.True,
                        "Should still be batching at depth 2"
                    );
                }

                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should still be batching at depth 1"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Only outermost scope ending should stop batching"
            );
        }

        [Test]
        [TestCase(true, TestName = "RefreshOnDispose.True.EndsNormally")]
        [TestCase(false, TestName = "RefreshOnDispose.False.SkipsRefresh")]
        public void RefreshOnDisposeParameterWorksCorrectly(bool refreshOnDispose)
        {
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: refreshOnDispose))
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    $"Should be batching with refreshOnDispose: {refreshOnDispose}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                $"Should not be batching after scope with refreshOnDispose: {refreshOnDispose} exits"
            );
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 regardless of refreshOnDispose setting"
            );
        }

        [Test]
        public void RefreshOnDisposeTrueIsDefault()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Default scope should enable batching"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Scope should end batching when disposed"
            );
        }

        [Test]
        [TestCase(1, TestName = "SequentialScopes.Count1")]
        [TestCase(5, TestName = "SequentialScopes.Count5")]
        [TestCase(10, TestName = "SequentialScopes.Count10")]
        [TestCase(50, TestName = "SequentialScopes.Count50", Category = "Stress")]
        public void MultipleSequentialScopesWorkCorrectly(int scopeCount)
        {
            for (int i = 0; i < scopeCount; i++)
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.False,
                    $"Should not be batching before scope {i}"
                );

                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.IsCurrentlyBatching,
                        Is.True,
                        $"Should be batching during scope {i}"
                    );
                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(1),
                        $"Depth should be 1 during scope {i}"
                    );
                }

                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.False,
                    $"Should not be batching after scope {i}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                $"Final depth should be 0 after {scopeCount} sequential scopes"
            );
        }

        [Test]
        public void ScopeProperlyCleansUpOnException()
        {
            try
            {
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.IsCurrentlyBatching,
                        Is.True,
                        "Should be batching before exception"
                    );
                    throw new InvalidOperationException("Test exception");
                }
            }
            catch (InvalidOperationException)
            {
                // Expected exception
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Scope should clean up even when exception is thrown"
            );
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Batch depth should be reset after exception"
            );
        }

        [Test]
        public void NestedScopeProperlyCleansUpOnException()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                try
                {
                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        Assert.That(
                            AssetDatabaseBatchHelper.CurrentBatchDepth,
                            Is.EqualTo(2),
                            "Should be at depth 2 before exception"
                        );
                        throw new InvalidOperationException("Test exception in nested scope");
                    }
                }
                catch (InvalidOperationException)
                {
                    // Expected exception
                }

                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Should return to depth 1 after nested scope exception"
                );
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Outer scope should still be active after inner scope exception"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after all scopes complete"
            );
        }

        [Test]
        [TestCaseSource(nameof(DeeplyNestedScopeCases))]
        public void DeeplyNestedScopesWorkCorrectly(int nestingLevel)
        {
            List<AssetDatabaseBatchScope> scopes = new List<AssetDatabaseBatchScope>();

            for (int i = 0; i < nestingLevel; i++)
            {
                scopes.Add(AssetDatabaseBatchHelper.BeginBatch());
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(i + 1),
                    $"Depth should be {i + 1} after entering scope {i}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(nestingLevel),
                $"Should be at depth {nestingLevel}"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.True,
                "Should be batching at maximum depth"
            );

            for (int i = nestingLevel - 1; i >= 0; i--)
            {
                scopes[i].Dispose();
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(i),
                    $"Depth should be {i} after disposing scope {i}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after all scopes disposed"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after all scopes disposed"
            );
        }

        private static IEnumerable<TestCaseData> DeeplyNestedScopeCases()
        {
            yield return new TestCaseData(10).SetName("NestedScope.Depth10.TracksCorrectly");
            yield return new TestCaseData(15).SetName("NestedScope.Depth15.TracksCorrectly");
            yield return new TestCaseData(20).SetName("NestedScope.Depth20.TracksCorrectly");
            yield return new TestCaseData(50).SetName("NestedScope.Depth50.TracksCorrectly");
            yield return new TestCaseData(100).SetName("NestedScope.Depth100.TracksCorrectly");
        }

        [Test]
        [TestCase(10, TestName = "RapidUnityCycles.Count10")]
        [TestCase(50, TestName = "RapidUnityCycles.Count50", Category = "Stress")]
        public void RapidOpenCloseCyclesWorkCorrectly(int cycleCount)
        {
            for (int cycle = 0; cycle < cycleCount; cycle++)
            {
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.IsCurrentlyBatching,
                        Is.True,
                        $"Should be batching in cycle {cycle}"
                    );
                }
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                $"Depth should be 0 after {cycleCount} rapid cycles"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after rapid cycles"
            );
        }

        [Test]
        [TestCase(10, TestName = "RapidNestedUnityCycles.Count10")]
        [TestCase(50, TestName = "RapidNestedUnityCycles.Count50", Category = "Stress")]
        public void RapidNestedOpenCloseCyclesWorkCorrectly(int cycleCount)
        {
            for (int cycle = 0; cycle < cycleCount; cycle++)
            {
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        Assert.That(
                            AssetDatabaseBatchHelper.CurrentBatchDepth,
                            Is.EqualTo(2),
                            $"Should be at depth 2 in cycle {cycle}"
                        );
                    }
                }
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after rapid nested cycles"
            );
        }

        [Test]
        public void IncrementBatchDepthReturnsCorrectlyForOutermostScope()
        {
            bool isOutermost = AssetDatabaseBatchHelper.IncrementBatchDepth();

            Assert.That(
                isOutermost,
                Is.True,
                "First increment should return true (outermost scope)"
            );
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Depth should be 1 after first increment"
            );

            AssetDatabaseBatchHelper.DecrementBatchDepth();
        }

        [Test]
        public void IncrementBatchDepthReturnsCorrectlyForNestedScope()
        {
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            bool isOutermost = AssetDatabaseBatchHelper.IncrementBatchDepth();

            Assert.That(
                isOutermost,
                Is.False,
                "Second increment should return false (nested scope)"
            );
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(2),
                "Depth should be 2 after second increment"
            );

            AssetDatabaseBatchHelper.DecrementBatchDepth();
            AssetDatabaseBatchHelper.DecrementBatchDepth();
        }

        [Test]
        [TestCase(2, TestName = "IncrementMultiple.Count2")]
        [TestCase(5, TestName = "IncrementMultiple.Count5")]
        [TestCase(10, TestName = "IncrementMultiple.Count10")]
        public void MultipleIncrementsReturnCorrectOutermostFlag(int incrementCount)
        {
            List<bool> results = new List<bool>();

            for (int i = 0; i < incrementCount; i++)
            {
                results.Add(AssetDatabaseBatchHelper.IncrementBatchDepth());
            }

            Assert.That(results[0], Is.True, "First increment should return true");

            for (int i = 1; i < incrementCount; i++)
            {
                Assert.That(
                    results[i],
                    Is.False,
                    $"Increment {i} should return false (not outermost)"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(incrementCount),
                $"Final depth should be {incrementCount}"
            );
        }

        [Test]
        public void DecrementBatchDepthReturnsCorrectlyWhenReturningToZero()
        {
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            bool wasOutermost = AssetDatabaseBatchHelper.DecrementBatchDepth();

            Assert.That(
                wasOutermost,
                Is.True,
                "Decrement from 1 to 0 should return true (was outermost)"
            );
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after decrement"
            );
        }

        [Test]
        public void DecrementBatchDepthReturnsCorrectlyWhenNotReturningToZero()
        {
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            bool wasOutermost = AssetDatabaseBatchHelper.DecrementBatchDepth();

            Assert.That(
                wasOutermost,
                Is.False,
                "Decrement from 2 to 1 should return false (not outermost)"
            );
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Depth should be 1 after decrement"
            );

            AssetDatabaseBatchHelper.DecrementBatchDepth();
        }

        [Test]
        public void DecrementBelowZeroResetsToZero()
        {
            bool wasOutermost = AssetDatabaseBatchHelper.DecrementBatchDepth();

            Assert.That(wasOutermost, Is.False, "Decrement below zero should return false");
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be reset to 0 when decremented below zero"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after decrement below zero"
            );
        }

        [Test]
        [TestCase(1, TestName = "DecrementBelowZero.Count1")]
        [TestCase(5, TestName = "DecrementBelowZero.Count5")]
        [TestCase(10, TestName = "DecrementBelowZero.Count10")]
        public void MultipleDecrementsFromZeroRemainAtZero(int decrementCount)
        {
            for (int i = 0; i < decrementCount; i++)
            {
                bool result = AssetDatabaseBatchHelper.DecrementBatchDepth();

                Assert.That(result, Is.False, $"Decrement {i} from zero should return false");
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(0),
                    $"Depth should remain 0 after decrement {i}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                $"Final depth should be 0 after {decrementCount} decrements from zero"
            );
        }

        [Test]
        public void ResetBatchDepthResetsToZero()
        {
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(3),
                "Depth should be 3 before reset"
            );

            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after reset"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after reset"
            );
        }

        [Test]
        [TestCase(1, TestName = "ResetFromDepth.Depth1")]
        [TestCase(10, TestName = "ResetFromDepth.Depth10")]
        [TestCase(50, TestName = "ResetFromDepth.Depth50")]
        public void ResetBatchDepthWorksFromAnyDepth(int initialDepth)
        {
            for (int i = 0; i < initialDepth; i++)
            {
                AssetDatabaseBatchHelper.IncrementBatchDepth();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(initialDepth),
                $"Depth should be {initialDepth} before reset"
            );

            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                $"Depth should be 0 after reset from depth {initialDepth}"
            );
        }

        [Test]
        public void ResetBatchDepthWhenAlreadyZeroRemainsZero()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Initial depth should be 0"
            );

            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after reset"
            );
        }

        /// <summary>
        ///     Tests that ResetBatchDepth properly cleans up internal state and Unity's AssetDatabase state.
        ///     This test verifies that after a reset, the system is in a clean, consistent state.
        /// </summary>
        [Test]
        public void ResetBatchDepthProperlyCleansUpState()
        {
            // Create nested scopes to establish a complex state
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(3),
                "Pre-condition: should be at depth 3"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.True,
                "Pre-condition: should be batching"
            );

            // Reset should clean up all state
            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Post-reset: depth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Post-reset: should not be batching"
            );

            // Verify the system is usable after reset
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "System should be functional after reset - new scope should work"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should return to 0 after using scope post-reset"
            );
        }

        /// <summary>
        ///     Tests that multiple consecutive ResetBatchDepth calls are idempotent.
        ///     Calling reset multiple times should have the same effect as calling it once.
        /// </summary>
        [Test]
        public void MultipleResetBatchDepthCallsAreIdempotent()
        {
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(2),
                "Pre-condition: depth should be 2"
            );

            // First reset
            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After first reset: depth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "After first reset: should not be batching"
            );

            // Second reset - should be idempotent
            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After second reset: depth should still be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "After second reset: should still not be batching"
            );

            // Third reset - still idempotent
            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After third reset: depth should still be 0"
            );
        }

        /// <summary>
        ///     Tests that multiple reset calls interleaved with increments work correctly.
        /// </summary>
        [Test]
        public void ResetBatchDepthInterleavedWithIncrementsWorksCorrectly()
        {
            // First sequence: increment, reset
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After first increment+reset: depth should be 0"
            );

            // Second sequence: multiple increments, reset
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After second increment+reset: depth should be 0"
            );

            // Third sequence: increment after reset works normally
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Increment after reset should work normally"
            );

            // Final cleanup
            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final reset should work"
            );
        }

        /// <summary>
        ///     Tests ForceResetAssetDatabase with depth 0.
        /// </summary>
        [Test]
        public void ForceResetAssetDatabaseAtDepthZeroIsIdempotent()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: depth should be 0"
            );

            // Force reset at depth 0 should be safe
            AssetDatabaseBatchHelper.ForceResetAssetDatabase();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After force reset at depth 0: depth should remain 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "After force reset at depth 0: should not be batching"
            );
        }

        /// <summary>
        ///     Tests ForceResetAssetDatabase with various depths using actual BeginBatch scopes.
        ///     This test verifies that ForceResetAssetDatabase correctly cleans up Unity state
        ///     when actual AssetDatabase API calls were made.
        /// </summary>
        [Test]
        [TestCase(1, TestName = "ForceResetFromDepth.Depth1")]
        [TestCase(5, TestName = "ForceResetFromDepth.Depth5")]
        [TestCase(10, TestName = "ForceResetFromDepth.Depth10")]
        public void ForceResetAssetDatabaseHandlesVariousDepths(int depth)
        {
            // Use BeginBatch to create actual batch scopes that call Unity's AssetDatabase APIs
            List<AssetDatabaseBatchScope> scopes = new List<AssetDatabaseBatchScope>();
            for (int i = 0; i < depth; i++)
            {
                scopes.Add(AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false));
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(depth),
                $"Pre-condition: depth should be {depth}"
            );

            AssetDatabaseBatchHelper.ForceResetAssetDatabase();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                $"After force reset from depth {depth}: depth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                $"After force reset from depth {depth}: should not be batching"
            );

            // Verify system is usable after force reset
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "System should be functional after force reset"
                );
            }
        }

        /// <summary>
        ///     Tests that ForceResetAssetDatabase and ResetBatchDepth are now equivalent
        ///     (since "aggressive" cleanup of more Unity calls than we made is unsafe).
        ///     Both methods only clean up actual Unity API calls made through BeginBatch.
        /// </summary>
        [Test]
        public void ForceResetAndResetBatchDepthAreEquivalentAndIdempotent()
        {
            // When the using scopes exit after the counter has been reset, the outer scope
            // (which was created as outermost) will trigger a warning because disposing at
            // depth 0 returns non-outermost (clamped). The inner scope won't warn because
            // it was created as non-outermost.
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );

            // Use BeginBatch to create actual scopes that make Unity API calls
            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(2),
                        "Pre-condition: should be at depth 2"
                    );

                    AssetDatabaseBatchHelper.ForceResetAssetDatabase();

                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(0),
                        "ForceResetAssetDatabase should reset depth to 0"
                    );
                }
            }

            // After force reset, multiple reset calls should be safe (idempotent when already at 0)
            AssetDatabaseBatchHelper.ResetBatchDepth();
            AssetDatabaseBatchHelper.ForceResetAssetDatabase();
            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Multiple reset types should be idempotent"
            );

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void CurrentBatchDepthReturnsCorrectValue()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Initial depth should be 0"
            );

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should be 1 in first scope"
                );

                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(2),
                        "Depth should be 2 in second scope"
                    );
                }
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should return to 0"
            );
        }

        [Test]
        [TestCaseSource(nameof(MixedRefreshOnDisposeCases))]
        public void MixedRefreshOnDisposeSettingsWorkCorrectly(bool[] refreshSettings)
        {
            List<AssetDatabaseBatchScope> scopes = new List<AssetDatabaseBatchScope>();

            for (int i = 0; i < refreshSettings.Length; i++)
            {
                scopes.Add(
                    AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: refreshSettings[i])
                );
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(i + 1),
                    $"Depth should be {i + 1} after adding scope with refreshOnDispose={refreshSettings[i]}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(refreshSettings.Length),
                $"Should be at depth {refreshSettings.Length} with mixed refresh settings"
            );

            for (int i = refreshSettings.Length - 1; i >= 0; i--)
            {
                scopes[i].Dispose();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after mixed refresh settings scopes"
            );
        }

        private static IEnumerable<TestCaseData> MixedRefreshOnDisposeCases()
        {
            yield return new TestCaseData(new[] { true, false, true }).SetName(
                "MixedRefresh.TrueFalseTrue"
            );
            yield return new TestCaseData(new[] { false, true, false }).SetName(
                "MixedRefresh.FalseTrueFalse"
            );
            yield return new TestCaseData(new[] { true, true, true, false, false }).SetName(
                "MixedRefresh.ThreeTrueTwoFalse"
            );
            yield return new TestCaseData(new[] { false, false, false, true }).SetName(
                "MixedRefresh.ThreeFalseOneTrue"
            );
            yield return new TestCaseData(new[] { true, false, true, false, true, false }).SetName(
                "MixedRefresh.Alternating"
            );
        }

        [Test]
        public void DisposingSameScopeTwiceDoesNotCauseNegativeDepth()
        {
            AssetDatabaseBatchScope scope = AssetDatabaseBatchHelper.BeginBatch();

            scope.Dispose();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after first dispose"
            );

            // Second dispose will trigger a scope mismatch warning because the scope was
            // created as outermost but the counter is already at 0
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );

            scope.Dispose();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after second dispose (not negative)"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after double dispose"
            );

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        [TestCase(2, TestName = "MultipleDispose.Count2")]
        [TestCase(5, TestName = "MultipleDispose.Count5")]
        [TestCase(10, TestName = "MultipleDispose.Count10")]
        public void DisposingSameScopeMultipleTimesDoesNotCauseNegativeDepth(int disposeCount)
        {
            AssetDatabaseBatchScope scope = AssetDatabaseBatchHelper.BeginBatch();

            for (int i = 0; i < disposeCount; i++)
            {
                // After the first dispose, all subsequent disposes will trigger a mismatch warning
                if (i > 0)
                {
                    LogAssert.Expect(
                        LogType.Warning,
                        new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
                    );
                }

                scope.Dispose();
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(0),
                    $"Depth should be 0 after dispose {i + 1}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                $"Should not be batching after {disposeCount} disposes"
            );

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void DoubleDisposeWithNestedScopesHandledCorrectly()
        {
            AssetDatabaseBatchScope outer = AssetDatabaseBatchHelper.BeginBatch();
            AssetDatabaseBatchScope inner = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(2),
                "Should be at depth 2"
            );

            inner.Dispose();

            // Second inner dispose: inner was created as non-outermost but this dispose
            // returns depth to 0 (outermost behavior), triggering a mismatch
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            inner.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after double inner dispose (protection against negative)"
            );

            // outer.Dispose: outer was created as outermost but the counter is already at 0
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            outer.Dispose();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after outer dispose"
            );

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ScopeCreatedViaConstructorWorksCorrectly()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Initial depth should be 0"
            );

            using (
                AssetDatabaseBatchScope scope = new AssetDatabaseBatchScope(refreshOnDispose: true)
            )
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should be 1 when using constructor directly"
                );
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should be batching when using constructor directly"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should return to 0 after scope disposed"
            );
        }

        [Test]
        [TestCase(true, TestName = "ConstructorDirect.RefreshTrue")]
        [TestCase(false, TestName = "ConstructorDirect.RefreshFalse")]
        public void ScopeCreatedViaConstructorWithDifferentRefreshSettings(bool refreshOnDispose)
        {
            using (
                AssetDatabaseBatchScope scope = new AssetDatabaseBatchScope(
                    refreshOnDispose: refreshOnDispose
                )
            )
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    $"Should be batching with constructor refreshOnDispose={refreshOnDispose}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                $"Depth should be 0 after constructor scope with refreshOnDispose={refreshOnDispose}"
            );
        }

        [Test]
        public void RefreshIfNotBatchingWithImportOptionsDoesNothingInsideBatch()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should be batching"
                );

                AssetDatabaseBatchHelper.RefreshIfNotBatching(ImportAssetOptions.ForceUpdate);

                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should still be batching after RefreshIfNotBatching with options"
                );
            }
        }

        [Test]
        [TestCase(ImportAssetOptions.Default, TestName = "RefreshOptions.Default")]
        [TestCase(ImportAssetOptions.ForceUpdate, TestName = "RefreshOptions.ForceUpdate")]
        [TestCase(
            ImportAssetOptions.ForceSynchronousImport,
            TestName = "RefreshOptions.ForceSynchronousImport"
        )]
        [TestCase(ImportAssetOptions.ImportRecursive, TestName = "RefreshOptions.ImportRecursive")]
        public void RefreshIfNotBatchingWithVariousImportOptionsInsideBatch(
            ImportAssetOptions options
        )
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                int depthBefore = AssetDatabaseBatchHelper.CurrentBatchDepth;

                AssetDatabaseBatchHelper.RefreshIfNotBatching(options);

                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(depthBefore),
                    $"Depth should remain unchanged after RefreshIfNotBatching with {options}"
                );
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    $"Should still be batching after RefreshIfNotBatching with {options}"
                );
            }
        }

        [Test]
        [TestCase(ImportAssetOptions.Default, TestName = "RefreshOptionsOutside.Default")]
        [TestCase(ImportAssetOptions.ForceUpdate, TestName = "RefreshOptionsOutside.ForceUpdate")]
        public void RefreshIfNotBatchingWithImportOptionsOutsideBatch(ImportAssetOptions options)
        {
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching initially"
            );

            AssetDatabaseBatchHelper.RefreshIfNotBatching(options);

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                $"Depth should remain 0 after RefreshIfNotBatching with {options} outside batch"
            );
        }

        [Test]
        [TestCaseSource(nameof(AlternatingDepthPatternCases))]
        public void AlternatingDepthPatternsWorkCorrectly(int[] depthPattern)
        {
            List<AssetDatabaseBatchScope> activeScopes = new List<AssetDatabaseBatchScope>();

            foreach (int targetDepth in depthPattern)
            {
                while (AssetDatabaseBatchHelper.CurrentBatchDepth < targetDepth)
                {
                    activeScopes.Add(AssetDatabaseBatchHelper.BeginBatch());
                }

                while (AssetDatabaseBatchHelper.CurrentBatchDepth > targetDepth)
                {
                    int lastIndex = activeScopes.Count - 1;
                    activeScopes[lastIndex].Dispose();
                    activeScopes.RemoveAt(lastIndex);
                }

                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(targetDepth),
                    $"Depth should match target depth {targetDepth}"
                );
            }

            while (activeScopes.Count > 0)
            {
                int lastIndex = activeScopes.Count - 1;
                activeScopes[lastIndex].Dispose();
                activeScopes.RemoveAt(lastIndex);
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after cleanup"
            );
        }

        private static IEnumerable<TestCaseData> AlternatingDepthPatternCases()
        {
            yield return new TestCaseData(new[] { 1, 2, 1, 3, 2, 4, 0 }).SetName(
                "AlternatingDepth.UpDownPattern.TracksCorrectly"
            );
            yield return new TestCaseData(new[] { 5, 3, 4, 2, 1, 0 }).SetName(
                "AlternatingDepth.DecreasingWithBumps.TracksCorrectly"
            );
            yield return new TestCaseData(new[] { 1, 1, 1, 2, 2, 0 }).SetName(
                "AlternatingDepth.PlateausPattern.TracksCorrectly"
            );
            yield return new TestCaseData(new[] { 0, 5, 0, 10, 0 }).SetName(
                "AlternatingDepth.SpikesPattern.TracksCorrectly"
            );
            yield return new TestCaseData(new[] { 1, 2, 3, 4, 5, 4, 3, 2, 1, 0 }).SetName(
                "AlternatingDepth.PyramidPattern.TracksCorrectly"
            );
        }

        [Test]
        public void IsCurrentlyBatchingReturnsCorrectValueInBatch()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                bool result1 = AssetDatabaseBatchHelper.IsCurrentlyBatching;
                bool result2 = AssetDatabaseBatchHelper.IsCurrentlyBatching;
                bool result3 = AssetDatabaseBatchHelper.IsCurrentlyBatching;

                Assert.That(result1, Is.True, "First read should return true");
                Assert.That(result2, Is.True, "Second read should return true");
                Assert.That(result3, Is.True, "Third read should return true");
            }
        }

        [Test]
        public void CurrentBatchDepthReturnsCorrectValueInBatch()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                int depth1 = AssetDatabaseBatchHelper.CurrentBatchDepth;
                int depth2 = AssetDatabaseBatchHelper.CurrentBatchDepth;
                int depth3 = AssetDatabaseBatchHelper.CurrentBatchDepth;

                Assert.That(depth1, Is.EqualTo(1), "First depth read should be 1");
                Assert.That(depth2, Is.EqualTo(1), "Second depth read should be 1");
                Assert.That(depth3, Is.EqualTo(1), "Third depth read should be 1");
            }
        }

        [Test]
        [TestCase(4, 100, TestName = "ConcurrentAccess.4Threads100Iterations")]
        [TestCase(8, 50, TestName = "ConcurrentAccess.8Threads50Iterations")]
        [TestCase(16, 25, TestName = "ConcurrentAccess.16Threads25Iterations")]
        public void ConcurrentIncrementDecrementMaintainsConsistency(
            int threadCount,
            int iterationsPerThread
        )
        {
            int totalOperations = threadCount * iterationsPerThread;
            CountdownEvent startSignal = new CountdownEvent(1);
            CountdownEvent completionSignal = new CountdownEvent(threadCount);
            Exception[] exceptions = new Exception[threadCount];

            Thread[] threads = new Thread[threadCount];

            for (int t = 0; t < threadCount; t++)
            {
                int threadIndex = t;
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        startSignal.Wait();

                        for (int i = 0; i < iterationsPerThread; i++)
                        {
                            AssetDatabaseBatchHelper.IncrementBatchDepth();
                            Thread.SpinWait(10);
                            AssetDatabaseBatchHelper.DecrementBatchDepth();
                        }
                    }
                    catch (Exception ex)
                    {
                        exceptions[threadIndex] = ex;
                    }
                    finally
                    {
                        completionSignal.Signal();
                    }
                });
                threads[t].Start();
            }

            startSignal.Signal();

            bool completed = completionSignal.Wait(TimeSpan.FromSeconds(30));
            Assert.That(completed, Is.True, "All threads should complete within timeout");

            foreach (Exception ex in exceptions)
            {
                Assert.That(ex, Is.Null, $"No thread should throw exception: {ex?.Message}");
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                $"Final depth should be 0 after {totalOperations} balanced increment/decrement operations across {threadCount} threads"
            );
        }

        [Test]
        public void ConcurrentReadsDuringBatchingAreConsistent()
        {
            const int readCount = 1000;
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                bool[] readResults = new bool[readCount];
                int[] depthResults = new int[readCount];

                Parallel.For(
                    0,
                    readCount,
                    i =>
                    {
                        readResults[i] = AssetDatabaseBatchHelper.IsCurrentlyBatching;
                        depthResults[i] = AssetDatabaseBatchHelper.CurrentBatchDepth;
                    }
                );

                for (int i = 0; i < readCount; i++)
                {
                    Assert.That(
                        readResults[i],
                        Is.True,
                        $"Read {i}: IsCurrentlyBatching should be true"
                    );
                    Assert.That(
                        depthResults[i],
                        Is.GreaterThanOrEqualTo(1),
                        $"Read {i}: CurrentBatchDepth should be >= 1"
                    );
                }
            }
        }

        [Test]
        public void ScopeStructIsReadonly()
        {
            AssetDatabaseBatchScope scope = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                typeof(AssetDatabaseBatchScope).IsValueType,
                Is.True,
                "AssetDatabaseBatchScope should be a value type (struct)"
            );

            scope.Dispose();
        }

        [Test]
        public void ScopeImplementsIDisposable()
        {
            Assert.That(
                typeof(IDisposable).IsAssignableFrom(typeof(AssetDatabaseBatchScope)),
                Is.True,
                "AssetDatabaseBatchScope should implement IDisposable"
            );
        }

        [Test]
        public void ScopeIsReadonlyStruct()
        {
            Type scopeType = typeof(AssetDatabaseBatchScope);

            Assert.That(scopeType.IsValueType, Is.True, "Should be a struct");

            System.Reflection.FieldInfo[] fields = scopeType.GetFields(
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public
            );

            foreach (System.Reflection.FieldInfo field in fields)
            {
                Assert.That(field.IsInitOnly, Is.True, $"Field '{field.Name}' should be readonly");
            }
        }

        [Test]
        public void BeginBatchDefaultParameterIsTrue()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Default parameter should enable batching"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Batching should end after scope with default parameter"
            );
        }

        [Test]
        [TestCase(100, TestName = "ExtremeRapidCounterCycles.Count100")]
        [TestCase(1000, TestName = "ExtremeRapidCounterCycles.Count1000")]
        public void ExtremelyRapidCounterCyclesDoNotBreakState(int cycleCount)
        {
            RunRapidCounterCycles(cycleCount);
        }

        [Test]
        [NUnit.Framework.Category("Stress")]
        [Timeout(60000)]
        [TestCase(5000, TestName = "ExtremeRapidCounterCycles.Count5000")]
        public void ExtremelyRapidCounterCyclesDoNotBreakStateStress(int cycleCount)
        {
            RunRapidCounterCycles(cycleCount);
        }

        private static void RunRapidCounterCycles(int cycleCount)
        {
            for (int i = 0; i < cycleCount; i++)
            {
                bool isOutermost = AssetDatabaseBatchHelper.IncrementBatchDepthWithUnityCall();
                if (!isOutermost)
                {
                    Assert.Fail($"Cycle {i} should start from an outermost batch counter state.");
                }

                bool shouldCleanUpUnity =
                    AssetDatabaseBatchHelper.DecrementBatchDepthWithUnityCleanup();
                if (!shouldCleanUpUnity)
                {
                    Assert.Fail($"Cycle {i} should return to an outermost batch cleanup state.");
                }
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                $"Depth should be 0 after {cycleCount} rapid cycles"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after extreme rapid cycles"
            );
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "Actual Unity batch depth should be 0 after extreme rapid cycles"
            );
        }

        [Test]
        [TestCase(25, TestName = "ManyNestedMixed.Depth25")]
        [TestCase(50, TestName = "ManyNestedMixed.Depth50")]
        public void ManyNestedScopesWithMixedRefreshSettings(int depth)
        {
            List<AssetDatabaseBatchScope> scopes = new List<AssetDatabaseBatchScope>();

            for (int i = 0; i < depth; i++)
            {
                bool shouldRefresh = i % 2 == 0;
                scopes.Add(AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: shouldRefresh));
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(depth),
                $"Should be at depth {depth}"
            );

            for (int i = depth - 1; i >= 0; i--)
            {
                scopes[i].Dispose();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after all scopes disposed"
            );
        }

        [Test]
        public void OutOfOrderDisposalHandledGracefully()
        {
            AssetDatabaseBatchScope scope1 = AssetDatabaseBatchHelper.BeginBatch();
            AssetDatabaseBatchScope scope2 = AssetDatabaseBatchHelper.BeginBatch();
            AssetDatabaseBatchScope scope3 = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(3),
                "Should be at depth 3"
            );

            // scope1 was created as outermost but disposing at depth 3 returns non-zero
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            scope1.Dispose();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(2),
                "Depth should be 2 after disposing first scope (out of order)"
            );

            // scope3 was non-outermost and still is, no warning
            scope3.Dispose();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Depth should be 1 after disposing third scope"
            );

            // scope2 was non-outermost but disposing now returns to 0 (outermost behavior)
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            scope2.Dispose();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after disposing second scope"
            );

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        [TestCaseSource(nameof(OutOfOrderDisposalPatternCases))]
        public void VariousOutOfOrderDisposalPatternsHandledGracefully(int[] disposalOrder)
        {
            int scopeCount = disposalOrder.Length;
            AssetDatabaseBatchScope[] scopes = new AssetDatabaseBatchScope[scopeCount];

            for (int i = 0; i < scopeCount; i++)
            {
                scopes[i] = AssetDatabaseBatchHelper.BeginBatch();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(scopeCount),
                $"Should be at depth {scopeCount} before disposal"
            );

            for (int i = 0; i < disposalOrder.Length; i++)
            {
                int scopeIndex = disposalOrder[i];
                bool isLastDisposal = i == disposalOrder.Length - 1;
                bool scopeWasCreatedAsOutermost = scopeIndex == 0;

                // A warning is expected when:
                // - Scope 0 (outermost at creation) is disposed but it's not the last disposal
                // - Scope > 0 (non-outermost at creation) is disposed and it IS the last disposal
                bool expectWarning =
                    (scopeWasCreatedAsOutermost && !isLastDisposal)
                    || (!scopeWasCreatedAsOutermost && isLastDisposal);

                if (expectWarning)
                {
                    LogAssert.Expect(
                        LogType.Warning,
                        new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
                    );
                }

                scopes[scopeIndex].Dispose();

                int expectedDepth = scopeCount - i - 1;
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(expectedDepth),
                    $"Depth should be {expectedDepth} after disposing scope {scopeIndex}"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final depth should be 0"
            );

            LogAssert.NoUnexpectedReceived();
        }

        private static IEnumerable<TestCaseData> OutOfOrderDisposalPatternCases()
        {
            yield return new TestCaseData(new[] { 0, 1, 2 }).SetName("OutOfOrder.FirstToLast");
            yield return new TestCaseData(new[] { 2, 1, 0 }).SetName("OutOfOrder.LastToFirst");
            yield return new TestCaseData(new[] { 1, 0, 2 }).SetName("OutOfOrder.MiddleFirstLast");
            yield return new TestCaseData(new[] { 1, 2, 0 }).SetName("OutOfOrder.MiddleLastFirst");
            yield return new TestCaseData(new[] { 0, 2, 1 }).SetName("OutOfOrder.FirstLastMiddle");
            yield return new TestCaseData(new[] { 2, 0, 1 }).SetName("OutOfOrder.LastFirstMiddle");
            yield return new TestCaseData(new[] { 0, 1, 2, 3, 4 }).SetName(
                "OutOfOrder.FiveInOrder"
            );
            yield return new TestCaseData(new[] { 4, 3, 2, 1, 0 }).SetName(
                "OutOfOrder.FiveReverse"
            );
            yield return new TestCaseData(new[] { 2, 0, 4, 1, 3 }).SetName(
                "OutOfOrder.FiveScattered"
            );
        }

        [Test]
        public void MixedDoubleDisposalAndNormalDisposal()
        {
            AssetDatabaseBatchScope scope1 = AssetDatabaseBatchHelper.BeginBatch();
            AssetDatabaseBatchScope scope2 = AssetDatabaseBatchHelper.BeginBatch();
            AssetDatabaseBatchScope scope3 = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(3),
                "Should start at depth 3"
            );

            scope2.Dispose();
            scope2.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Depth should be 1 after double-disposing scope2"
            );

            scope1.Dispose();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after disposing scope1"
            );

            scope3.Dispose();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after disposing scope3 (already below zero protection)"
            );

            // This particular disposal pattern does NOT trigger warnings because:
            // - scope2 was created as non-outermost and disposes as non-outermost (both times)
            // - scope1 was created as outermost and disposes as outermost
            // - scope3 was created as non-outermost and disposes as non-outermost (clamped case)
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ExceptionInMiddleOfNestedScopesHandledCorrectly()
        {
            AssetDatabaseBatchScope outer = AssetDatabaseBatchHelper.BeginBatch();

            try
            {
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        Assert.That(
                            AssetDatabaseBatchHelper.CurrentBatchDepth,
                            Is.EqualTo(3),
                            "Should be at depth 3"
                        );
                        throw new InvalidOperationException("Exception in middle scope");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Expected
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Depth should be 1 (outer scope still active)"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.True,
                "Should still be batching in outer scope"
            );

            outer.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after outer scope disposed"
            );
        }

        [Test]
        [TestCaseSource(nameof(MultipleExceptionScenarioCases))]
        public void MultipleExceptionsInNestedScopesHandledCorrectly(
            int throwAtDepth,
            int totalDepth
        )
        {
            List<AssetDatabaseBatchScope> scopes = new List<AssetDatabaseBatchScope>();

            for (int i = 0; i < totalDepth - throwAtDepth; i++)
            {
                scopes.Add(AssetDatabaseBatchHelper.BeginBatch());
            }

            try
            {
                for (int i = 0; i < throwAtDepth; i++)
                {
                    using (AssetDatabaseBatchHelper.BeginBatch())
                    {
                        if (i == throwAtDepth - 1)
                        {
                            throw new InvalidOperationException($"Exception at depth {totalDepth}");
                        }
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Expected
            }

            int expectedDepth = totalDepth - throwAtDepth;
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(expectedDepth),
                $"Depth should be {expectedDepth} after exception at depth {totalDepth}"
            );

            foreach (AssetDatabaseBatchScope scope in scopes)
            {
                scope.Dispose();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after cleanup"
            );
        }

        private static IEnumerable<TestCaseData> MultipleExceptionScenarioCases()
        {
            yield return new TestCaseData(1, 3).SetName("Exception.AtDepth3.BaseDepth2");
            yield return new TestCaseData(2, 4).SetName("Exception.AtDepth4.BaseDepth2");
            yield return new TestCaseData(3, 5).SetName("Exception.AtDepth5.BaseDepth2");
            yield return new TestCaseData(1, 1).SetName("Exception.AtDepth1.BaseDepth0");
            yield return new TestCaseData(5, 10).SetName("Exception.AtDepth10.BaseDepth5");
        }

        [Test]
        public void ScopeCreationAfterResetWorksCorrectly()
        {
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            AssetDatabaseBatchHelper.ResetBatchDepth();

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "Depth should be 1 after reset and new scope"
                );
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should be batching after reset and new scope"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should return to 0 after scope disposal"
            );
        }

        [Test]
        public void InterleaveIncrementDecrementWithScopes()
        {
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Depth should be 1 after increment"
            );

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(2),
                    "Depth should be 2 after scope"
                );

                AssetDatabaseBatchHelper.IncrementBatchDepth();
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(3),
                    "Depth should be 3 after nested increment"
                );

                AssetDatabaseBatchHelper.DecrementBatchDepth();
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(2),
                    "Depth should be 2 after nested decrement"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Depth should be 1 after scope exits"
            );

            AssetDatabaseBatchHelper.DecrementBatchDepth();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 after final decrement"
            );
        }

        [Test]
        public void BatchHelperStaticClassHasExpectedMembers()
        {
            Type helperType = typeof(AssetDatabaseBatchHelper);

            System.Reflection.PropertyInfo isCurrentlyBatching = helperType.GetProperty(
                nameof(AssetDatabaseBatchHelper.IsCurrentlyBatching)
            );
            Assert.That(
                isCurrentlyBatching,
                Is.Not.Null,
                "IsCurrentlyBatching property should exist"
            );
            Assert.That(
                isCurrentlyBatching.PropertyType,
                Is.EqualTo(typeof(bool)),
                "IsCurrentlyBatching should return bool"
            );

            System.Reflection.PropertyInfo currentBatchDepth = helperType.GetProperty(
                nameof(AssetDatabaseBatchHelper.CurrentBatchDepth)
            );
            Assert.That(currentBatchDepth, Is.Not.Null, "CurrentBatchDepth property should exist");
            Assert.That(
                currentBatchDepth.PropertyType,
                Is.EqualTo(typeof(int)),
                "CurrentBatchDepth should return int"
            );

            System.Reflection.MethodInfo beginBatch = helperType.GetMethod(
                nameof(AssetDatabaseBatchHelper.BeginBatch)
            );
            Assert.That(beginBatch, Is.Not.Null, "BeginBatch method should exist");
            Assert.That(
                beginBatch.ReturnType,
                Is.EqualTo(typeof(AssetDatabaseBatchScope)),
                "BeginBatch should return AssetDatabaseBatchScope"
            );
        }

        [Test]
        public void ZeroDepthStateIsConsistent()
        {
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Initial depth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching at depth 0"
            );

            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should still be 0 after reset"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should still not be batching after reset"
            );
        }

        [Test]
        public void HighDepthValuesWorkCorrectly()
        {
            const int highDepth = 1000;

            for (int i = 0; i < highDepth; i++)
            {
                AssetDatabaseBatchHelper.IncrementBatchDepth();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(highDepth),
                $"Depth should be {highDepth}"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.True,
                "Should be batching at high depth"
            );

            for (int i = 0; i < highDepth; i++)
            {
                AssetDatabaseBatchHelper.DecrementBatchDepth();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should return to 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching at depth 0"
            );
        }

        [Test]
        public void ScopeDefaultValueIsNotUsable()
        {
            AssetDatabaseBatchScope defaultScope = default;

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be 0 before default scope dispose"
            );

            defaultScope.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after default scope dispose (protected against negative)"
            );
        }

        [Test]
        public void RefreshIfNotBatchingDoesNotAffectBatchState()
        {
            AssetDatabaseBatchHelper.RefreshIfNotBatching();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after RefreshIfNotBatching"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after RefreshIfNotBatching"
            );
        }

        [Test]
        public void RefreshIfNotBatchingWithOptionsDoesNotAffectBatchState()
        {
            AssetDatabaseBatchHelper.RefreshIfNotBatching(ImportAssetOptions.ForceUpdate);

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should remain 0 after RefreshIfNotBatching with options"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Should not be batching after RefreshIfNotBatching with options"
            );
        }

        [Test]
        public void DisposeCalledOnScopeWithoutIncrementingHandledGracefully()
        {
            int depthBefore = AssetDatabaseBatchHelper.CurrentBatchDepth;

            AssetDatabaseBatchHelper.DecrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Depth should be clamped to 0, not negative"
            );
        }

        [Test]
        public void IncrementAndDecrementReturnValuesAreSymmetric()
        {
            bool firstIncrement = AssetDatabaseBatchHelper.IncrementBatchDepth();
            Assert.That(firstIncrement, Is.True, "First increment returns true (outermost)");

            bool secondIncrement = AssetDatabaseBatchHelper.IncrementBatchDepth();
            Assert.That(secondIncrement, Is.False, "Second increment returns false (nested)");

            bool firstDecrement = AssetDatabaseBatchHelper.DecrementBatchDepth();
            Assert.That(firstDecrement, Is.False, "First decrement returns false (still nested)");

            bool secondDecrement = AssetDatabaseBatchHelper.DecrementBatchDepth();
            Assert.That(secondDecrement, Is.True, "Second decrement returns true (now at zero)");
        }

        [Test]
        public void BatchingStateTransitionsAreAtomic()
        {
            bool previouslyBatching = AssetDatabaseBatchHelper.IsCurrentlyBatching;
            Assert.That(previouslyBatching, Is.False, "Should not be batching initially");

            AssetDatabaseBatchHelper.IncrementBatchDepth();

            bool nowBatching = AssetDatabaseBatchHelper.IsCurrentlyBatching;
            Assert.That(nowBatching, Is.True, "Should be batching after increment");

            int depth = AssetDatabaseBatchHelper.CurrentBatchDepth;
            Assert.That(depth, Is.EqualTo(1), "Depth should be 1");

            AssetDatabaseBatchHelper.DecrementBatchDepth();

            bool afterDecrement = AssetDatabaseBatchHelper.IsCurrentlyBatching;
            Assert.That(afterDecrement, Is.False, "Should not be batching after decrement");
        }

        /// <summary>
        ///     Tests that ActualUnityBatchDepth tracks actual Unity API calls.
        /// </summary>
        [Test]
        public void ActualUnityBatchDepthTracksUnityApiCalls()
        {
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "Initial ActualUnityBatchDepth should be 0"
            );

            // Manual increment doesn't affect ActualUnityBatchDepth
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "CurrentBatchDepth should be 1"
            );
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "ActualUnityBatchDepth should still be 0 after manual increment"
            );

            AssetDatabaseBatchHelper.DecrementBatchDepth();
        }

        /// <summary>
        ///     Tests that ResetCountersOnly clears counters without calling Unity APIs.
        /// </summary>
        [Test]
        public void ResetCountersOnlyDoesNotCallUnityApis()
        {
            // Manually increment counters
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(2),
                "Pre-condition: CurrentBatchDepth should be 2"
            );

            // ResetCountersOnly should clear without Unity API calls
            AssetDatabaseBatchHelper.ResetCountersOnly();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After ResetCountersOnly: CurrentBatchDepth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "After ResetCountersOnly: ActualUnityBatchDepth should be 0"
            );

            // System should be fully usable
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "System should be usable after ResetCountersOnly"
                );
            }
        }

        /// <summary>
        ///     Tests the domain reload scenario where ResetCountersOnly is called first
        ///     to clear stale state, then ResetBatchDepth is safe to call.
        /// </summary>
        [Test]
        public void ResetCountersOnlyFollowedByResetBatchDepthIsSafe()
        {
            // Simulate stale state that might persist after domain reload
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(2),
                "Pre-condition: should have stale depth"
            );

            // First, clear the stale counters without calling Unity APIs
            // (simulating what OneTimeSetUp does)
            AssetDatabaseBatchHelper.ResetCountersOnly();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After ResetCountersOnly: depth should be 0"
            );

            // Now ResetBatchDepth should be safe to call (like in SetUp/TearDown)
            // because currentDepth is 0, it won't try to clean up any Unity state
            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After ResetBatchDepth: depth should remain 0"
            );

            // System should work normally
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "BeginBatch should work after the resets"
                );
            }
        }

        /// <summary>
        ///     Tests that mixing IncrementBatchDepth (counter-only) with BeginBatch
        ///     correctly tracks which operations actually called Unity APIs.
        /// </summary>
        [Test]
        public void MixedCounterOnlyAndBeginBatchTracksCorrectly()
        {
            // Start with counter-only increment
            AssetDatabaseBatchHelper.IncrementBatchDepth();
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "After manual increment: depth should be 1"
            );
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "After manual increment: ActualUnityBatchDepth should be 0"
            );

            // BeginBatch when already at depth > 0 should NOT make Unity API calls
            // because it's not the outermost scope
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(2),
                    "During BeginBatch: depth should be 2"
                );
                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(0),
                    "During BeginBatch: ActualUnityBatchDepth should still be 0 (not outermost)"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "After BeginBatch dispose: depth should be 1"
            );

            // Clean up the manual increment
            AssetDatabaseBatchHelper.DecrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final: depth should be 0"
            );
        }

        /// <summary>
        ///     Tests that BeginBatch as the outermost scope correctly increments ActualUnityBatchDepth.
        /// </summary>
        [Test]
        public void BeginBatchAsOutermostIncrementsActualUnityBatchDepth()
        {
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "Initial: ActualUnityBatchDepth should be 0"
            );

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "During outermost BeginBatch: ActualUnityBatchDepth should be 1"
                );
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "During outermost BeginBatch: CurrentBatchDepth should be 1"
                );

                // Nested BeginBatch should NOT increment ActualUnityBatchDepth
                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                        Is.EqualTo(1),
                        "During nested BeginBatch: ActualUnityBatchDepth should still be 1"
                    );
                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(2),
                        "During nested BeginBatch: CurrentBatchDepth should be 2"
                    );
                }
            }

            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "Final: ActualUnityBatchDepth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final: CurrentBatchDepth should be 0"
            );
        }

        /// <summary>
        ///     Tests that ForceResetAssetDatabase safely handles zero-depth state with stale ActualUnityBatchDepth.
        ///     This scenario can occur after domain reload where counters persist but Unity state was reset.
        /// </summary>
        [Test]
        public void ForceResetHandlesZeroDepthWithStaleActualDepthSafely()
        {
            // First, reset to ensure clean state
            AssetDatabaseBatchHelper.ResetCountersOnly();

            // Now both counters should be 0
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: CurrentBatchDepth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: ActualUnityBatchDepth should be 0"
            );

            // ForceReset at clean state should be safe
            AssetDatabaseBatchHelper.ForceResetAssetDatabase();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After ForceReset: CurrentBatchDepth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "After ForceReset: ActualUnityBatchDepth should be 0"
            );

            // System should work normally
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "System should be functional after ForceReset"
                );
            }
        }

        /// <summary>
        ///     Tests that disposing a scope after the counters have been reset to zero
        ///     is safe and does not cause negative depth.
        /// </summary>
        [Test]
        public void DisposeAfterCounterResetRemainsAtZero()
        {
            AssetDatabaseBatchScope scope = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Pre-condition: should be at depth 1"
            );

            // Track Unity's actual batch depth before resetting counters.
            // ResetCountersOnly() clears our tracking but leaves Unity's actual state untouched.
            int actualUnityDepthBeforeReset = AssetDatabaseBatchHelper.ActualUnityBatchDepth;

            AssetDatabaseBatchHelper.ResetCountersOnly();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After ResetCountersOnly: depth should be 0"
            );

            // scope was created as outermost but the counter was reset to 0 externally
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            scope.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After dispose on reset counter: depth should remain 0 (protected against negative)"
            );

            LogAssert.NoUnexpectedReceived();

            // CRITICAL CLEANUP: ResetCountersOnly() cleared our tracking, but Unity's AssetDatabase
            // is still in StartAssetEditing mode. The scope.Dispose() didn't clean up because
            // wasOutermost was false (counter was at 0, decrement clamped and returned false).
            // We must manually clean up Unity's actual state to prevent leaving the editor in
            // a broken state where assembly reloads fail.
            for (int i = 0; i < actualUnityDepthBeforeReset; i++)
            {
                AssetDatabase.AllowAutoRefresh();
                AssetDatabase.StopAssetEditing();
            }
        }

        /// <summary>
        ///     Tests that the scope mismatch detection works correctly when an outer scope is disposed first.
        /// </summary>
        [Test]
        public void OutOfOrderDisposeHandlesMismatchGracefully()
        {
            AssetDatabaseBatchScope scope1 = AssetDatabaseBatchHelper.BeginBatch();
            AssetDatabaseBatchScope scope2 = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(2),
                "Pre-condition: should be at depth 2"
            );

            // scope1 was created as outermost but disposing at depth 2 doesn't return to 0
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            scope1.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "After out-of-order dispose: depth should be 1"
            );

            // scope2 was non-outermost but disposing now returns to 0 (outermost behavior)
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            scope2.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After second dispose: depth should be 0"
            );

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        ///     Tests that out-of-order disposal properly cleans up Unity's state, allowing subsequent
        ///     batch operations to function correctly. This verifies the production fix works.
        /// </summary>
        [Test]
        public void OutOfOrderDisposalCleanupAllowsSubsequentOperations()
        {
            AssetDatabaseBatchScope outerScope = AssetDatabaseBatchHelper.BeginBatch();
            AssetDatabaseBatchScope innerScope = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(2),
                "Pre-condition: should be at depth 2"
            );

            // Dispose out of order: outer first, then inner
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            outerScope.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "After out-of-order dispose of outer: depth should be 1"
            );

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            innerScope.Dispose();

            // Verify counters are at 0 after both scopes disposed
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After both scopes disposed: depth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "After both scopes disposed: should not be batching"
            );

            // Critical verification: Create a NEW batch scope and verify it works correctly
            // This proves the system is functional after out-of-order disposal cleanup
            using (AssetDatabaseBatchScope newScope = AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "New scope should increment depth to 1"
                );
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "New scope should be batching"
                );
            }

            // Verify clean exit from new scope
            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After new scope disposed: depth should be 0"
            );
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "After new scope disposed: should not be batching"
            );

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        ///     Tests that scope disposal continues working correctly after a ResetBatchDepth call.
        /// </summary>
        [Test]
        public void ScopeDisposeAfterResetBatchDepthHandledGracefully()
        {
            AssetDatabaseBatchScope scope = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "Pre-condition: should be at depth 1"
            );

            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After ResetBatchDepth: depth should be 0"
            );

            // scope was created as outermost but the counter was reset externally
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            scope.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After scope dispose: depth should still be 0"
            );

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        ///     Tests that multiple scopes can be disposed after ResetBatchDepth without causing issues.
        /// </summary>
        [Test]
        public void MultipleScopesDisposeAfterResetBatchDepthHandledGracefully()
        {
            AssetDatabaseBatchScope scope1 = AssetDatabaseBatchHelper.BeginBatch();
            AssetDatabaseBatchScope scope2 = AssetDatabaseBatchHelper.BeginBatch();
            AssetDatabaseBatchScope scope3 = AssetDatabaseBatchHelper.BeginBatch();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(3),
                "Pre-condition: should be at depth 3"
            );

            AssetDatabaseBatchHelper.ResetBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After ResetBatchDepth: depth should be 0"
            );

            // scope1 was created as outermost but the counter was reset to 0 externally
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );
            scope1.Dispose();

            // scope2 and scope3 were non-outermost and disposing at 0 also returns non-outermost (clamped)
            scope2.Dispose();
            scope3.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After all scopes disposed: depth should still be 0"
            );

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        ///     Tests that PauseBatch followed by scope disposal works correctly.
        /// </summary>
        [Test]
        public void PauseBatchWithScopeDisposalWorksCorrectly()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "Should be batching in outer scope"
                );

                using (AssetDatabaseBatchHelper.PauseBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(1),
                        "During pause: depth should still be 1 (counter not decremented)"
                    );
                }

                Assert.That(
                    AssetDatabaseBatchHelper.IsCurrentlyBatching,
                    Is.True,
                    "After pause scope ends: should be batching again"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "After outer scope: should not be batching"
            );
        }

        /// <summary>
        ///     Tests that PauseBatch when not batching does nothing.
        /// </summary>
        [Test]
        public void PauseBatchWhenNotBatchingDoesNothing()
        {
            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Pre-condition: should not be batching"
            );

            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(0),
                    "During pause when not batching: depth should still be 0"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After pause scope when not batching: depth should still be 0"
            );
        }

        /// <summary>
        ///     Tests that nested PauseBatch scopes work correctly.
        /// </summary>
        [Test]
        public void NestedPauseBatchScopesWorkCorrectly()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "Pre-condition: ActualUnityBatchDepth should be 1"
                );

                using (AssetDatabaseBatchHelper.PauseBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                        Is.EqualTo(0),
                        "During first pause: ActualUnityBatchDepth should be 0"
                    );

                    using (AssetDatabaseBatchHelper.PauseBatch())
                    {
                        Assert.That(
                            AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                            Is.EqualTo(0),
                            "During nested pause: ActualUnityBatchDepth should still be 0 (no-op)"
                        );
                    }
                }

                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "After pause ends: ActualUnityBatchDepth should be 1 again"
                );
            }
        }

        /// <summary>
        ///     Tests that ResetBatchDepth called multiple times in sequence is idempotent.
        /// </summary>
        [Test]
        public void ResetBatchDepthCalledMultipleTimesIsIdempotent()
        {
            // When the using scopes exit after the counter has been reset, the outer scope
            // (which was created as outermost) will trigger a warning because disposing at
            // depth 0 returns non-outermost (clamped). The inner scope won't warn because
            // it was created as non-outermost.
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );

            using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
            {
                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(2),
                        "Pre-condition: should be at depth 2"
                    );

                    AssetDatabaseBatchHelper.ResetBatchDepth();
                    AssetDatabaseBatchHelper.ResetBatchDepth();
                    AssetDatabaseBatchHelper.ResetBatchDepth();

                    Assert.That(
                        AssetDatabaseBatchHelper.CurrentBatchDepth,
                        Is.EqualTo(0),
                        "After multiple ResetBatchDepth: depth should be 0"
                    );
                }
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final: depth should be 0"
            );

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        ///     Tests that the system recovers correctly after a forced reset during active scopes.
        /// </summary>
        [Test]
        public void SystemRecoveryAfterForceResetDuringActiveScopes()
        {
            List<AssetDatabaseBatchScope> scopes = new List<AssetDatabaseBatchScope>();

            for (int i = 0; i < 5; i++)
            {
                scopes.Add(AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false));
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(5),
                "Pre-condition: should be at depth 5"
            );

            AssetDatabaseBatchHelper.ForceResetAssetDatabase();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After ForceReset: depth should be 0"
            );

            // Only the first scope (created as outermost) triggers a warning
            // The others were created as non-outermost and disposing at 0 also returns non-outermost
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"Scope disposal state mismatch.*out-of-order disposal")
            );

            foreach (AssetDatabaseBatchScope scope in scopes)
            {
                scope.Dispose();
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After disposing all scopes: depth should still be 0"
            );

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(1),
                    "New scope after recovery: should be at depth 1"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final: depth should be 0"
            );

            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        ///     Tests that disposing a default (uninitialized) scope does not cause issues.
        /// </summary>
        [Test]
        public void DefaultScopeDisposeIsHarmless()
        {
            AssetDatabaseBatchScope defaultScope = default;

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: depth should be 0"
            );

            defaultScope.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After disposing default scope: depth should still be 0"
            );
        }

        /// <summary>
        ///     Tests that disposing a default pause scope does not cause issues.
        /// </summary>
        [Test]
        public void DefaultPauseScopeDisposeIsHarmless()
        {
            AssetDatabasePauseScope defaultScope = default;

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Pre-condition: depth should be 0"
            );

            defaultScope.Dispose();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "After disposing default pause scope: depth should still be 0"
            );
        }

        /// <summary>
        ///     Tests that disposing a pause scope multiple times is safe.
        /// </summary>
        [Test]
        public void PauseScopeMultipleDisposeIsSafe()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                AssetDatabasePauseScope pauseScope = AssetDatabaseBatchHelper.PauseBatch();

                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(0),
                    "After pause: ActualUnityBatchDepth should be 0"
                );

                pauseScope.Dispose();

                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "After first dispose: ActualUnityBatchDepth should be 1"
                );

                pauseScope.Dispose();

                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "After second dispose: ActualUnityBatchDepth should still be 1 (no-op due to _disposed flag preventing double-resume)"
                );
            }
        }

        /// <summary>
        ///     Tests that the system handles interleaved batch and pause scopes correctly.
        /// </summary>
        [Test]
        public void InterleavedBatchAndPauseScopesWorkCorrectly()
        {
            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "Initial: ActualUnityBatchDepth should be 1"
                );

                using (AssetDatabaseBatchHelper.BeginBatch())
                {
                    Assert.That(
                        AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                        Is.EqualTo(1),
                        "Nested batch: ActualUnityBatchDepth should still be 1 (not outermost)"
                    );

                    using (AssetDatabaseBatchHelper.PauseBatch())
                    {
                        Assert.That(
                            AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                            Is.EqualTo(0),
                            "During pause: ActualUnityBatchDepth should be 0"
                        );
                    }

                    Assert.That(
                        AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                        Is.EqualTo(1),
                        "After pause: ActualUnityBatchDepth should be 1"
                    );
                }

                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "After nested batch: ActualUnityBatchDepth should be 1"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "Final: ActualUnityBatchDepth should be 0"
            );
        }

        /// <summary>
        ///     Tests that counter manipulation followed by BeginBatch works correctly.
        /// </summary>
        [Test]
        public void ManualIncrementFollowedByBeginBatchWorksCorrectly()
        {
            AssetDatabaseBatchHelper.IncrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "After manual increment: depth should be 1"
            );
            Assert.That(
                AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                Is.EqualTo(0),
                "After manual increment: ActualUnityBatchDepth should be 0"
            );

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                Assert.That(
                    AssetDatabaseBatchHelper.CurrentBatchDepth,
                    Is.EqualTo(2),
                    "During BeginBatch: depth should be 2"
                );
                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(0),
                    "During BeginBatch (not outermost): ActualUnityBatchDepth should still be 0"
                );
            }

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(1),
                "After BeginBatch scope: depth should be 1"
            );

            AssetDatabaseBatchHelper.DecrementBatchDepth();

            Assert.That(
                AssetDatabaseBatchHelper.CurrentBatchDepth,
                Is.EqualTo(0),
                "Final: depth should be 0"
            );
        }

        /// <summary>
        ///     Tests that PauseScope wasBatching field correctly tracks the pause state.
        /// </summary>
        [Test]
        public void PauseScopeTracksPauseStateCorrectly()
        {
            AssetDatabasePauseScope pauseWhenNotBatching = AssetDatabaseBatchHelper.PauseBatch();

            Assert.That(
                AssetDatabaseBatchHelper.IsCurrentlyBatching,
                Is.False,
                "Not batching: should remain false"
            );

            pauseWhenNotBatching.Dispose();

            using (AssetDatabaseBatchHelper.BeginBatch())
            {
                AssetDatabasePauseScope pauseWhenBatching = AssetDatabaseBatchHelper.PauseBatch();

                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(0),
                    "During pause when batching: ActualUnityBatchDepth should be 0"
                );

                pauseWhenBatching.Dispose();

                Assert.That(
                    AssetDatabaseBatchHelper.ActualUnityBatchDepth,
                    Is.EqualTo(1),
                    "After pause when batching: ActualUnityBatchDepth should be 1"
                );
            }
        }

        private const string EnsureFolderTestRoot = "Assets/__EnsureAssetFolderTests__";

        private static void DeleteEnsureFolderTestRoot()
        {
            if (AssetDatabase.IsValidFolder(EnsureFolderTestRoot))
            {
                AssetDatabase.DeleteAsset(EnsureFolderTestRoot);
            }
        }

        [Test]
        public void EnsureAssetFolderCreatesSingleLevelFolder()
        {
            DeleteEnsureFolderTestRoot();
            try
            {
                Assert.That(
                    AssetDatabase.IsValidFolder(EnsureFolderTestRoot),
                    Is.False,
                    "Precondition: test root should not exist."
                );

                bool created = AssetDatabaseBatchHelper.EnsureAssetFolder(EnsureFolderTestRoot);

                Assert.That(created, Is.True, "EnsureAssetFolder should report success.");
                Assert.That(
                    AssetDatabase.IsValidFolder(EnsureFolderTestRoot),
                    Is.True,
                    "Folder should be registered with the AssetDatabase."
                );
            }
            finally
            {
                DeleteEnsureFolderTestRoot();
            }
        }

        [Test]
        public void EnsureAssetFolderCreatesNestedFolders()
        {
            DeleteEnsureFolderTestRoot();
            try
            {
                string nested = EnsureFolderTestRoot + "/Level1/Level2/Level3";

                bool created = AssetDatabaseBatchHelper.EnsureAssetFolder(nested);

                Assert.That(created, Is.True, "EnsureAssetFolder should create the full chain.");
                Assert.That(
                    AssetDatabase.IsValidFolder(nested),
                    Is.True,
                    "Deepest nested folder should be valid."
                );
                Assert.That(
                    AssetDatabase.IsValidFolder(EnsureFolderTestRoot + "/Level1"),
                    Is.True,
                    "Intermediate folder should also be valid."
                );
            }
            finally
            {
                DeleteEnsureFolderTestRoot();
            }
        }

        [Test]
        public void EnsureAssetFolderIsIdempotentWhenFolderExists()
        {
            DeleteEnsureFolderTestRoot();
            try
            {
                Assert.That(
                    AssetDatabaseBatchHelper.EnsureAssetFolder(EnsureFolderTestRoot),
                    Is.True,
                    "First call should create the folder."
                );
                Assert.That(
                    AssetDatabaseBatchHelper.EnsureAssetFolder(EnsureFolderTestRoot),
                    Is.True,
                    "Second call on an existing folder should still report success."
                );
                Assert.That(
                    AssetDatabase.IsValidFolder(EnsureFolderTestRoot),
                    Is.True,
                    "Folder should remain valid after the idempotent second call."
                );
            }
            finally
            {
                DeleteEnsureFolderTestRoot();
            }
        }

        [Test]
        public void EnsureAssetFolderCreatesFolderWhileBatchIsOpen()
        {
            DeleteEnsureFolderTestRoot();
            try
            {
                // The core regression: while a StartAssetEditing batch is open, a folder created
                // through the helper must be immediately registered (it pauses the batch internally)
                // so a subsequent CreateAsset cannot fail with "Parent directory must exist".
                using (AssetDatabaseBatchHelper.BeginBatch(refreshOnDispose: false))
                {
                    bool created = AssetDatabaseBatchHelper.EnsureAssetFolder(EnsureFolderTestRoot);

                    Assert.That(
                        created,
                        Is.True,
                        "EnsureAssetFolder should succeed even inside an open batch."
                    );
                    Assert.That(
                        AssetDatabase.IsValidFolder(EnsureFolderTestRoot),
                        Is.True,
                        "Folder should be registered immediately, before the batch is disposed."
                    );
                }
            }
            finally
            {
                DeleteEnsureFolderTestRoot();
            }
        }

        [Test]
        public void EnsureAssetFolderRejectsNonAssetsRoot()
        {
            Assert.That(
                AssetDatabaseBatchHelper.EnsureAssetFolder("Packages/com.example/Foo"),
                Is.False,
                "Only the Assets tree is creatable via the AssetDatabase."
            );
        }

        [Test]
        public void EnsureAssetFolderRejectsNullOrEmpty()
        {
            Assert.That(
                AssetDatabaseBatchHelper.EnsureAssetFolder(null),
                Is.False,
                "Null input should be handled gracefully."
            );
            Assert.That(
                AssetDatabaseBatchHelper.EnsureAssetFolder(string.Empty),
                Is.False,
                "Empty input should be handled gracefully."
            );
            Assert.That(
                AssetDatabaseBatchHelper.EnsureAssetFolder("   "),
                Is.False,
                "Whitespace input should be handled gracefully."
            );
        }

        [Test]
        public void EnsureAssetFolderTreatsBareAssetsAsExisting()
        {
            Assert.That(
                AssetDatabaseBatchHelper.EnsureAssetFolder("Assets"),
                Is.True,
                "The Assets root always exists."
            );
        }

        [Test]
        public void EnsureAssetParentFolderCreatesParentForNestedAssetPath()
        {
            DeleteEnsureFolderTestRoot();
            try
            {
                string assetPath = EnsureFolderTestRoot + "/Sub/Payload.asset";

                bool ensured = AssetDatabaseBatchHelper.EnsureAssetParentFolder(assetPath);

                Assert.That(ensured, Is.True, "Parent folder chain should be created.");
                Assert.That(
                    AssetDatabase.IsValidFolder(EnsureFolderTestRoot + "/Sub"),
                    Is.True,
                    "The asset's parent folder should be valid."
                );
                Assert.That(
                    AssetDatabase.IsValidFolder(assetPath),
                    Is.False,
                    "The asset path itself must NOT be created as a folder."
                );
            }
            finally
            {
                DeleteEnsureFolderTestRoot();
            }
        }

        [Test]
        public void EnsureAssetParentFolderReturnsTrueForAssetDirectlyUnderAssets()
        {
            Assert.That(
                AssetDatabaseBatchHelper.EnsureAssetParentFolder("Assets/Payload.asset"),
                Is.True,
                "An asset directly under Assets has no folder to create."
            );
            Assert.That(
                AssetDatabaseBatchHelper.EnsureAssetParentFolder("Payload.asset"),
                Is.True,
                "A bare file name has no parent segment to create."
            );
        }
    }

#endif
}
