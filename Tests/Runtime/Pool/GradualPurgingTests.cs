// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Tests for P0.3 - Gradual/Spread Purging feature.
    /// Verifies that large purge operations can be spread across multiple calls
    /// to prevent GC spikes from bulk deallocation.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class GradualPurgingTests
    {
        private float _currentTime;
        private bool _wasMemoryPressureEnabled;

        private float TestTimeProvider()
        {
            return _currentTime;
        }

        [SetUp]
        public void SetUp()
        {
            // Time 0 reads as uninitialized in the tracker.
            _currentTime = 1f;
            TestPoolItem.ResetIdCounter();
            PoolPurgeSettings.ResetToDefaults();
            _wasMemoryPressureEnabled = MemoryPressureMonitor.Enabled;
            MemoryPressureMonitor.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            PoolPurgeSettings.ResetToDefaults();
            MemoryPressureMonitor.Enabled = _wasMemoryPressureEnabled;
        }

        [Test]
        public void MaxPurgesPerOperationLimitsItemsPurgedPerCall()
        {
            const int preWarmCount = 25;
            const int maxPurgesPerOp = 5;
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            Assert.AreEqual(preWarmCount, pool.Count);

            _currentTime = 2f;

            int firstPurged = pool.Purge();

            Assert.AreEqual(maxPurgesPerOp, firstPurged);
            Assert.AreEqual(maxPurgesPerOp, purgedItems.Count);
            Assert.AreEqual(preWarmCount - maxPurgesPerOp, pool.Count);
            Assert.IsTrue(pool.HasPendingPurges);
        }

        [Test]
        public void PendingPurgesContinueOnSubsequentOperations()
        {
            const int preWarmCount = 25;
            const int maxPurgesPerOp = 5;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    Triggers = PurgeTrigger.OnRent,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 2f;

            int totalPurged = 0;
            int rentsNeeded = 0;

            while (1 < pool.Count)
            {
                using (PooledResource<TestPoolItem> _ = pool.Get()) { }

                rentsNeeded++;
                PoolStatistics stats = pool.GetStatistics();
                totalPurged = (int)stats.PurgeCount;

                // Prevent infinite loop
                if (20 < rentsNeeded)
                {
                    break;
                }
            }

            Assert.Greater(totalPurged, maxPurgesPerOp, "Should have purged more than one batch");
            Assert.LessOrEqual(pool.Count, 2);
        }

        [Test]
        public void ForceFullPurgeBypassesMaxPurgesPerOperationLimit()
        {
            const int preWarmCount = 25;
            const int maxPurgesPerOp = 5;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 2f;

            int purged = pool.ForceFullPurge();

            Assert.AreEqual(preWarmCount, purged);
            Assert.AreEqual(0, pool.Count);
            Assert.IsFalse(pool.HasPendingPurges);
        }

        [Test]
        public void ForceFullPurgeWithReasonBypassesLimit()
        {
            const int preWarmCount = 25;
            const int maxPurgesPerOp = 5;
            List<PurgeReason> purgeReasons = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, reason) => purgeReasons.Add(reason),
                }
            );

            _currentTime = 2f;

            int purged = pool.ForceFullPurge(PurgeReason.MemoryPressure, ignoreHysteresis: true);

            Assert.AreEqual(preWarmCount, purged);
            Assert.AreEqual(preWarmCount, purgeReasons.Count);

            foreach (PurgeReason reason in purgeReasons)
            {
                Assert.AreEqual(PurgeReason.MemoryPressure, reason);
            }
        }

        [Test]
        public void StatisticsTrackPartialVsFullPurges()
        {
            const int preWarmCount = 12;
            const int maxPurgesPerOp = 5;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 2f;

            pool.Purge();
            PoolStatistics stats1 = pool.GetStatistics();
            Assert.AreEqual(1, stats1.PartialPurgeOperations);
            Assert.AreEqual(0, stats1.FullPurgeOperations);

            pool.Purge();
            PoolStatistics stats2 = pool.GetStatistics();
            Assert.AreEqual(2, stats2.PartialPurgeOperations);
            Assert.AreEqual(0, stats2.FullPurgeOperations);

            pool.Purge();
            PoolStatistics stats3 = pool.GetStatistics();
            Assert.AreEqual(2, stats3.PartialPurgeOperations);
            Assert.AreEqual(1, stats3.FullPurgeOperations);
        }

        [Test]
        public void MaxPurgesPerOperationOfZeroMeansUnlimited()
        {
            const int preWarmCount = 25;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = 0,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 2f;

            int purged = pool.Purge();

            Assert.AreEqual(preWarmCount, purged);
            Assert.AreEqual(0, pool.Count);
            Assert.IsFalse(pool.HasPendingPurges);
        }

        [Test]
        public void MaxPurgesPerOperationPropertyCanBeSetAfterConstruction()
        {
            const int preWarmCount = 15;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = 0,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            pool.MaxPurgesPerOperation = 3;

            _currentTime = 2f;

            int purged = pool.Purge();

            Assert.AreEqual(3, purged);
            Assert.AreEqual(preWarmCount - 3, pool.Count);
            Assert.IsTrue(pool.HasPendingPurges);
        }

        [Test]
        public void GlobalDefaultMaxPurgesPerOperationIsUsedWhenNotSpecified()
        {
            const int customGlobalLimit = 7;
            PoolPurgeSettings.DefaultGlobalMaxPurgesPerOperation = customGlobalLimit;

            const int preWarmCount = 20;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,

                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 2f;

            int purged = pool.Purge();

            Assert.AreEqual(customGlobalLimit, purged);
        }

        [Test]
        public void HasPendingPurgesIsFalseWhenNoPendingWork()
        {
            const int preWarmCount = 3;
            const int maxPurgesPerOp = 10;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 2f;

            pool.Purge();

            Assert.IsFalse(pool.HasPendingPurges);
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void MinRetainCountIsRespectedDuringGradualPurge()
        {
            const int preWarmCount = 15;
            const int maxPurgesPerOp = 5;
            const int minRetain = 3;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    MinRetainCount = minRetain,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 2f;

            int iterations = 0;
            while (minRetain < pool.Count && iterations < 10)
            {
                pool.Purge();
                iterations++;
            }

            Assert.AreEqual(minRetain, pool.Count);
            Assert.IsFalse(pool.HasPendingPurges);
        }

        [Test]
        public void ForceFullPurgeClearsPendingFlag()
        {
            const int preWarmCount = 20;
            const int maxPurgesPerOp = 5;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 2f;

            pool.Purge();
            Assert.IsTrue(pool.HasPendingPurges);

            pool.ForceFullPurge();
            Assert.IsFalse(pool.HasPendingPurges);
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void NegativeMaxPurgesPerOperationIsNormalizedToZero()
        {
            const int preWarmCount = 10;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = -5,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            Assert.AreEqual(0, pool.MaxPurgesPerOperation);

            _currentTime = 2f;
            int purged = pool.Purge();

            Assert.AreEqual(preWarmCount, purged);
        }

        [Test]
        public void EffectiveOptionsIncludesMaxPurgesPerOperation()
        {
            const int customLimit = 15;
            PoolPurgeSettings.DefaultGlobalMaxPurgesPerOperation = customLimit;

            PoolPurgeEffectiveOptions options =
                PoolPurgeSettings.GetEffectiveOptions<TestPoolItem>();

            Assert.AreEqual(customLimit, options.MaxPurgesPerOperation);
        }

        [Test]
        public void PoolStatisticsToStringIncludesPurgeOperationCounts()
        {
            PoolStatistics stats = new(
                currentSize: 10,
                peakSize: 20,
                rentCount: 100,
                returnCount: 90,
                purgeCount: 50,
                idleTimeoutPurges: 30,
                capacityPurges: 20,
                fullPurgeOperations: 5,
                partialPurgeOperations: 3
            );

            string str = stats.ToString();

            Assert.That(str, Does.Contain("FullPurgeOps=5"));
            Assert.That(str, Does.Contain("PartialPurgeOps=3"));
        }

        [Test]
        public void PoolStatisticsEqualityIncludesPurgeOperationCounts()
        {
            PoolStatistics stats1 = new(
                currentSize: 10,
                peakSize: 20,
                rentCount: 100,
                returnCount: 90,
                purgeCount: 50,
                idleTimeoutPurges: 30,
                capacityPurges: 20,
                fullPurgeOperations: 5,
                partialPurgeOperations: 3
            );

            PoolStatistics stats2 = new(
                currentSize: 10,
                peakSize: 20,
                rentCount: 100,
                returnCount: 90,
                purgeCount: 50,
                idleTimeoutPurges: 30,
                capacityPurges: 20,
                fullPurgeOperations: 5,
                partialPurgeOperations: 3
            );

            PoolStatistics stats3 = new(
                currentSize: 10,
                peakSize: 20,
                rentCount: 100,
                returnCount: 90,
                purgeCount: 50,
                idleTimeoutPurges: 30,
                capacityPurges: 20,
                fullPurgeOperations: 6,
                partialPurgeOperations: 3
            );

            Assert.AreEqual(stats1, stats2);
            Assert.AreNotEqual(stats1, stats3);
        }

        /// <summary>
        /// Tests that minRetainCount is respected after multiple successive gradual purge operations.
        /// This validates the bug fix where minRetainCount was only checked at the beginning of
        /// purge operations, not enforced as items were being purged.
        /// </summary>
        [Test]
        [TestCase(0, 5, 20, TestName = "MinRetain.Zero.MaxPurge.5.Items.20")]
        [TestCase(1, 5, 20, TestName = "MinRetain.1.MaxPurge.5.Items.20")]
        [TestCase(3, 5, 20, TestName = "MinRetain.3.MaxPurge.5.Items.20")]
        [TestCase(5, 5, 20, TestName = "MinRetain.5.MaxPurge.5.Items.20")]
        [TestCase(3, 1, 15, TestName = "MinRetain.3.MaxPurge.1.Items.15")]
        [TestCase(3, 3, 15, TestName = "MinRetain.3.MaxPurge.3.Items.15")]
        [TestCase(5, 3, 12, TestName = "MinRetain.5.MaxPurge.3.Items.12")]
        public void MinRetainCountRespectedAfterMultipleGradualPurges(
            int minRetainCount,
            int maxPurgesPerOp,
            int preWarmCount
        )
        {
            int purgeCount = 0;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    MinRetainCount = minRetainCount,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, _) => purgeCount++,
                }
            );

            TestContext.WriteLine(
                $"Initial state: pool.Count={pool.Count}, minRetain={minRetainCount}, maxPurgesPerOp={maxPurgesPerOp}"
            );

            _currentTime = 2f;

            int iterations = 0;
            int previousCount = pool.Count;
            while (minRetainCount < pool.Count && iterations < 50)
            {
                int purgedThisRound = pool.Purge();
                iterations++;

                TestContext.WriteLine(
                    $"Iteration {iterations}: purged={purgedThisRound}, pool.Count={pool.Count}, total purged={purgeCount}"
                );

                Assert.GreaterOrEqual(
                    pool.Count,
                    minRetainCount,
                    $"Pool count should never go below minRetainCount ({minRetainCount}) during gradual purging. "
                        + $"Iteration={iterations}, purged this round={purgedThisRound}"
                );

                if (pool.Count == previousCount && purgedThisRound == 0)
                {
                    break;
                }
                previousCount = pool.Count;
            }

            Assert.AreEqual(
                minRetainCount,
                pool.Count,
                $"Final pool count should equal minRetainCount after all purges complete"
            );
            Assert.IsFalse(
                pool.HasPendingPurges,
                "Should have no pending purges when at minRetainCount"
            );

            int expectedPurged = preWarmCount - minRetainCount;
            Assert.AreEqual(
                expectedPurged,
                purgeCount,
                $"Total purged items should be {expectedPurged} (preWarm={preWarmCount} - minRetain={minRetainCount})"
            );
        }

        /// <summary>
        /// Tests minRetainCount with both explicit and implicit (OnRent) purge triggers.
        /// Verifies the fix works consistently regardless of purge trigger type.
        /// </summary>
        [Test]
        [TestCase(PurgeTrigger.Explicit, TestName = "PurgeTrigger.Explicit")]
        [TestCase(PurgeTrigger.OnRent, TestName = "PurgeTrigger.OnRent")]
        [TestCase(PurgeTrigger.OnReturn, TestName = "PurgeTrigger.OnReturn")]
        public void MinRetainCountRespectedWithDifferentPurgeTriggers(PurgeTrigger trigger)
        {
            const int preWarmCount = 15;
            const int maxPurgesPerOp = 3;
            const int minRetainCount = 4;
            int purgeCount = 0;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    MinRetainCount = minRetainCount,
                    Triggers = trigger,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, _) => purgeCount++,
                }
            );

            TestContext.WriteLine(
                $"Testing trigger={trigger}, preWarm={preWarmCount}, maxPurge={maxPurgesPerOp}, minRetain={minRetainCount}"
            );

            _currentTime = 2f;

            int iterations = 0;
            while (minRetainCount < pool.Count && iterations < 50)
            {
                iterations++;

                if (trigger == PurgeTrigger.Explicit)
                {
                    pool.Purge();
                }
                else if (trigger == PurgeTrigger.OnRent)
                {
                    using PooledResource<TestPoolItem> resource = pool.Get();
                }
                else if (trigger == PurgeTrigger.OnReturn)
                {
                    using (PooledResource<TestPoolItem> resource = pool.Get()) { }
                }

                TestContext.WriteLine(
                    $"Iteration {iterations}: pool.Count={pool.Count}, totalPurged={purgeCount}"
                );

                Assert.GreaterOrEqual(
                    pool.Count,
                    minRetainCount,
                    $"Pool count should never go below minRetainCount ({minRetainCount})"
                );
            }

            Assert.AreEqual(
                minRetainCount,
                pool.Count,
                $"Final pool count should equal minRetainCount"
            );
        }

        /// <summary>
        /// Tests that MaxPurgesPerOperation of 1 correctly respects minRetainCount,
        /// which is an important edge case where each purge operation removes exactly one item.
        /// </summary>
        [Test]
        [TestCase(0, 10, TestName = "MinRetain.Zero.Items.10")]
        [TestCase(1, 10, TestName = "MinRetain.1.Items.10")]
        [TestCase(5, 10, TestName = "MinRetain.5.Items.10")]
        [TestCase(9, 10, TestName = "MinRetain.9.Items.10")]
        public void MinRetainCountRespectedWithMaxPurgeOfOne(int minRetainCount, int preWarmCount)
        {
            const int maxPurgesPerOp = 1;
            List<int> purgedItemIds = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    MinRetainCount = minRetainCount,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItemIds.Add(item.Id),
                }
            );

            _currentTime = 2f;

            int iterations = 0;
            while (minRetainCount < pool.Count && iterations < preWarmCount + 5)
            {
                int purged = pool.Purge();
                iterations++;

                TestContext.WriteLine(
                    $"Iteration {iterations}: purged={purged}, pool.Count={pool.Count}"
                );

                if (purged == 0)
                {
                    break;
                }

                Assert.GreaterOrEqual(
                    pool.Count,
                    minRetainCount,
                    $"Pool count must never go below minRetainCount"
                );
            }

            Assert.AreEqual(
                minRetainCount,
                pool.Count,
                "Final pool count should equal minRetainCount"
            );

            int expectedPurged = preWarmCount - minRetainCount;
            Assert.AreEqual(
                expectedPurged,
                purgedItemIds.Count,
                $"Should have purged exactly {expectedPurged} items"
            );
        }

        /// <summary>
        /// Tests gradual purging behavior when minRetainCount equals pool size.
        /// No items should be purged in this case.
        /// </summary>
        [Test]
        public void MinRetainCountEqualToPoolSizePreventsAllPurges()
        {
            const int preWarmCount = 10;
            const int maxPurgesPerOp = 5;
            const int minRetainCount = 10;
            int purgeCount = 0;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    MinRetainCount = minRetainCount,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, _) => purgeCount++,
                }
            );

            _currentTime = 2f;

            int purged = pool.Purge();

            TestContext.WriteLine($"Purge result: purged={purged}, pool.Count={pool.Count}");

            Assert.AreEqual(
                0,
                purged,
                "No items should be purged when minRetainCount equals pool size"
            );
            Assert.AreEqual(preWarmCount, pool.Count, "Pool count should remain unchanged");
            Assert.AreEqual(0, purgeCount, "OnPurge callback should not be invoked");
            Assert.IsFalse(pool.HasPendingPurges, "Should have no pending purges");
        }

        /// <summary>
        /// Tests gradual purging behavior when minRetainCount exceeds pool size.
        /// No items should be purged in this case.
        /// </summary>
        [Test]
        public void MinRetainCountExceedingPoolSizePreventsAllPurges()
        {
            const int preWarmCount = 5;
            const int maxPurgesPerOp = 3;
            const int minRetainCount = 10;
            int purgeCount = 0;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    MinRetainCount = minRetainCount,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, _) => purgeCount++,
                }
            );

            _currentTime = 2f;

            int purged = pool.Purge();

            TestContext.WriteLine($"Purge result: purged={purged}, pool.Count={pool.Count}");

            Assert.AreEqual(
                0,
                purged,
                "No items should be purged when minRetainCount exceeds pool size"
            );
            Assert.AreEqual(preWarmCount, pool.Count, "Pool count should remain unchanged");
            Assert.AreEqual(0, purgeCount, "OnPurge callback should not be invoked");
        }

        /// <summary>
        /// Tests data-driven combinations of minRetainCount, maxPurgesPerOperation, and bufferMultiplier.
        /// This comprehensive test validates the interaction of multiple pool parameters.
        /// </summary>
        [Test]
        [TestCase(0, 1, 0f, 10, TestName = "Combo.MinRetain.0.MaxPurge.1.Buffer.0")]
        [TestCase(1, 3, 0.5f, 10, TestName = "Combo.MinRetain.1.MaxPurge.3.Buffer.0.5")]
        [TestCase(3, 5, 1.0f, 15, TestName = "Combo.MinRetain.3.MaxPurge.5.Buffer.1.0")]
        [TestCase(5, 0, 2.0f, 20, TestName = "Combo.MinRetain.5.MaxPurge.Unlimited.Buffer.2.0")]
        public void MinRetainCountRespectedWithVariousBufferMultipliers(
            int minRetainCount,
            int maxPurgesPerOp,
            float bufferMultiplier,
            int preWarmCount
        )
        {
            int purgeCount = 0;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = maxPurgesPerOp,
                    MinRetainCount = minRetainCount,
                    BufferMultiplier = bufferMultiplier,
                    UseIntelligentPurging = true,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, _) => purgeCount++,
                }
            );

            TestContext.WriteLine(
                $"Testing: minRetain={minRetainCount}, maxPurge={maxPurgesPerOp}, buffer={bufferMultiplier}, preWarm={preWarmCount}"
            );

            _currentTime = 2f;

            int iterations = 0;
            while (minRetainCount < pool.Count && iterations < 50)
            {
                pool.Purge();
                iterations++;

                Assert.GreaterOrEqual(
                    pool.Count,
                    minRetainCount,
                    $"Pool count should never go below minRetainCount ({minRetainCount})"
                );
            }

            TestContext.WriteLine(
                $"Final state: pool.Count={pool.Count}, iterations={iterations}, totalPurged={purgeCount}"
            );

            Assert.GreaterOrEqual(
                pool.Count,
                minRetainCount,
                "Final pool count should be at least minRetainCount"
            );
        }

        private sealed class TestPoolItem
        {
            public int Id { get; }

            private static int _nextId;

            public TestPoolItem()
            {
                Id = Interlocked.Increment(ref _nextId);
            }

            public static void ResetIdCounter()
            {
                _nextId = 0;
            }
        }
    }
}
