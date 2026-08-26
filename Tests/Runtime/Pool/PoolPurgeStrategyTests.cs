// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    internal sealed class PoolPurgeStrategyTests
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
        public void PeriodicAndOnReturnBothFireAppropriately()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Periodic | PurgeTrigger.OnReturn,
                    IdleTimeoutSeconds = 2f,
                    PurgeIntervalSeconds = 3f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            Assert.AreEqual(10, pool.Count);

            _currentTime = 5f;

            using (PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem _)) { }

            TestContext.WriteLine(
                $"After Get/Return cycle at t={_currentTime}: pool.Count={pool.Count}, purged={purgedItems.Count}"
            );

            Assert.Less(pool.Count, 10, "Items should have been purged via periodic or OnReturn");
            Assert.Greater(purgedItems.Count, 0, "At least some items should have been purged");
        }

        [Test]
        public void PeriodicAndExplicitPeriodicFiresAutomatically()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Periodic | PurgeTrigger.Explicit,
                    IdleTimeoutSeconds = 2f,
                    PurgeIntervalSeconds = 3f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            Assert.AreEqual(10, pool.Count);

            _currentTime = 5f;

            using (PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem _)) { }

            TestContext.WriteLine(
                $"After Get/Return at t={_currentTime}: pool.Count={pool.Count}, purged={purgedItems.Count}"
            );

            Assert.Less(
                pool.Count,
                10,
                "Periodic trigger should fire automatically without explicit Purge() call"
            );
        }

        [Test]
        public void AllAutomaticTriggersCanBeCombined()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Periodic | PurgeTrigger.OnRent | PurgeTrigger.OnReturn,
                    IdleTimeoutSeconds = 10f,
                    PurgeIntervalSeconds = 60f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                }
            );

            Assert.AreEqual(5, pool.Count);

            for (int i = 0; i < 20; i++)
            {
                _currentTime = 1f + i * 0.1f;
                using (PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem _)) { }
            }

            TestContext.WriteLine($"After 20 Get/Return cycles: pool.Count={pool.Count}");

            Assert.GreaterOrEqual(
                pool.Count,
                1,
                "Pool should still function normally with all triggers active"
            );
        }

        [Test]
        public void PeriodicTriggerDoesNotPurgeBeforeIntervalElapsed()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Periodic,
                    IdleTimeoutSeconds = 2f,
                    PurgeIntervalSeconds = 5f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            Assert.AreEqual(5, pool.Count);

            _currentTime = 3.5f;

            using (PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem _)) { }

            TestContext.WriteLine(
                $"At t={_currentTime} (before purge interval): pool.Count={pool.Count}, purged={purgedItems.Count}"
            );

            Assert.AreEqual(
                5,
                pool.Count,
                "Pool should not have purged before the periodic interval elapsed"
            );
        }

        [Test]
        public void PeriodicTriggerPurgesAfterMultipleIntervals()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Periodic,
                    IdleTimeoutSeconds = 1f,
                    PurgeIntervalSeconds = 2f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            Assert.AreEqual(10, pool.Count);

            for (int interval = 1; interval <= 3; interval++)
            {
                _currentTime = 1f + interval * 2f + 1f;
                using (PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem _)) { }

                TestContext.WriteLine(
                    $"After interval {interval} at t={_currentTime}: pool.Count={pool.Count}, totalPurged={purgedItems.Count}"
                );
            }

            Assert.Greater(
                purgedItems.Count,
                0,
                "Items should be purged across multiple periodic intervals"
            );
        }

        [Test]
        public void PeriodicTriggerWithCustomInterval()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Periodic,
                    IdleTimeoutSeconds = 0.5f,
                    PurgeIntervalSeconds = 1f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            Assert.AreEqual(5, pool.Count);

            _currentTime = 2.5f;

            using (PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem _)) { }

            TestContext.WriteLine(
                $"At t={_currentTime}: pool.Count={pool.Count}, purged={purgedItems.Count}"
            );

            Assert.Greater(purgedItems.Count, 0, "Purge should happen with short custom interval");
        }

        [Test]
        public void HysteresisExpiresThenPurgeProceeds()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = 1f,
                    HysteresisSeconds = 5f,
                    BufferMultiplier = 1.5f,
                    RollingWindowSeconds = 60f,
                    SpikeThresholdMultiplier = 2.0f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            List<PooledResource<TestPoolItem>> rented = new();
            for (int i = 0; i < 10; i++)
            {
                _currentTime = 1f + i * 0.01f;
                rented.Add(pool.Get(out TestPoolItem _));
            }

            TestContext.WriteLine($"Rented 10 items, pool.Count={pool.Count}");

            _currentTime = 2f;
            foreach (PooledResource<TestPoolItem> resource in rented)
            {
                resource.Dispose();
            }
            rented.Clear();

            TestContext.WriteLine(
                $"After returning all at t={_currentTime}: pool.Count={pool.Count}"
            );

            _currentTime = 10f;

            int purged = pool.Purge();

            TestContext.WriteLine(
                $"After purge at t={_currentTime} (post-hysteresis): purged={purged}, pool.Count={pool.Count}, totalPurged={purgedItems.Count}"
            );

            Assert.Greater(purged, 0, "Purge should proceed after hysteresis period expires");
        }

        [Test]
        public void PurgeIsBlockedDuringHysteresis()
        {
            // This test verifies the contract: after the rolling-window usage tracker detects
            // a rental spike, explicit/capacity purges are blocked for HysteresisSeconds so the
            // pool does not thrash freshly returned items that are likely to be re-rented.
            //
            // IdleTimeoutSeconds = 0f is required because idle-timeout purges are intentionally
            // exempt from hysteresis (they are pool hygiene, not sizing), and would otherwise
            // defeat the assertion.
            //
            // SpikeThresholdMultiplier = 1.5f is required because at 2.0f the monotonically
            // increasing _currentlyRented sequence [1..N] never satisfies n > avg * 2 and so
            // no spike would ever be recorded.
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = 0f,
                    HysteresisSeconds = 30f,
                    BufferMultiplier = 1.5f,
                    RollingWindowSeconds = 60f,
                    SpikeThresholdMultiplier = 1.5f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            List<PooledResource<TestPoolItem>> rented = new();
            for (int i = 0; i < 10; i++)
            {
                _currentTime = 1f + i * 0.01f;
                rented.Add(pool.Get(out TestPoolItem _));
            }

            _currentTime = 2f;
            foreach (PooledResource<TestPoolItem> resource in rented)
            {
                resource.Dispose();
            }
            rented.Clear();

            int countAfterReturn = pool.Count;
            TestContext.WriteLine(
                $"After returning all at t={_currentTime}: pool.Count={countAfterReturn}"
            );

            _currentTime = 4f;

            int purged = pool.Purge();

            TestContext.WriteLine(
                $"Purge during hysteresis at t={_currentTime}: purged={purged}, pool.Count={pool.Count}, purgedItemsTotal={purgedItems.Count}"
            );

            Assert.AreEqual(
                0,
                purged,
                "Explicit purge during hysteresis should return 0 items purged"
            );
            Assert.AreEqual(
                countAfterReturn,
                pool.Count,
                "Pool should retain all items during hysteresis period (purge blocked)"
            );
            Assert.AreEqual(
                0,
                purgedItems.Count,
                "No OnPurge callbacks should fire while hysteresis blocks purges"
            );
        }

        [Test]
        public void WarmRetainCountDominatesWhenPoolIsActive()
        {
            // This test verifies that WarmRetainCount acts as a floor on the pool size
            // while the pool is considered "active" (the last rental is within
            // IdleTimeoutSeconds of the current time).
            //
            // IdleTimeoutSeconds must be large enough that currentTime - lastRentalTime
            // is still within the active window at assert time. With the last rental at
            // t = 1 + 9*0.01 = 1.09 and the purge at t = 4, we need IdleTimeoutSeconds >= 3f.
            // 5f gives comfortable margin and matches the intent (pool is still in active
            // use even after some idle returns).
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    WarmRetainCount = 5,
                    MinRetainCount = 2,
                    IdleTimeoutSeconds = 5f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            List<PooledResource<TestPoolItem>> rented = new();
            for (int i = 0; i < 10; i++)
            {
                _currentTime = 1f + i * 0.01f;
                rented.Add(pool.Get(out TestPoolItem _));
            }

            _currentTime = 2f;
            foreach (PooledResource<TestPoolItem> resource in rented)
            {
                resource.Dispose();
            }
            rented.Clear();

            TestContext.WriteLine(
                $"After returning 10 items at t={_currentTime}: pool.Count={pool.Count}"
            );

            _currentTime = 4f;

            int totalPurged = 0;
            for (int i = 0; i < 5; i++)
            {
                totalPurged += pool.Purge();
            }

            TestContext.WriteLine(
                $"After purge at t={_currentTime}: pool.Count={pool.Count}, totalPurged={totalPurged}, purgedItemsTotal={purgedItems.Count}"
            );

            Assert.GreaterOrEqual(
                pool.Count,
                5,
                "Active pool should retain at least WarmRetainCount (5) items"
            );
        }

        [Test]
        public void MinRetainCountDominatesWhenGreaterThanWarm()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 15,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    MinRetainCount = 10,
                    WarmRetainCount = 5,
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            Assert.AreEqual(15, pool.Count);

            _currentTime = 5f;

            int totalPurged = 0;
            for (int i = 0; i < 10; i++)
            {
                totalPurged += pool.Purge();
            }

            TestContext.WriteLine(
                $"After aggressive purge at t={_currentTime}: pool.Count={pool.Count}, totalPurged={totalPurged}"
            );

            Assert.GreaterOrEqual(
                pool.Count,
                10,
                "Pool should retain at least MinRetainCount (10) even when WarmRetainCount is lower"
            );
        }

        [Test]
        public void MinRetainCountIsAbsoluteFloor()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    MinRetainCount = 3,
                    MaxPoolSize = 100,
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            Assert.AreEqual(10, pool.Count);

            _currentTime = 10f;

            int totalPurged = 0;
            for (int i = 0; i < 20; i++)
            {
                totalPurged += pool.Purge();
            }

            TestContext.WriteLine(
                $"After aggressive purge at t={_currentTime}: pool.Count={pool.Count}, totalPurged={totalPurged}"
            );

            Assert.GreaterOrEqual(pool.Count, 3, "Pool should never drop below MinRetainCount (3)");
        }

        [Test]
        public void MaxPoolSizeEnforcedDuringPurge()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    MaxPoolSize = 5,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            List<PooledResource<TestPoolItem>> rented = new();
            for (int i = 0; i < 10; i++)
            {
                rented.Add(pool.Get(out TestPoolItem _));
            }

            foreach (PooledResource<TestPoolItem> resource in rented)
            {
                resource.Dispose();
            }
            rented.Clear();

            Assert.AreEqual(10, pool.Count);

            int purged = pool.Purge();

            TestContext.WriteLine(
                $"After purge with MaxPoolSize=5: pool.Count={pool.Count}, purged={purged}"
            );

            Assert.LessOrEqual(pool.Count, 5, "Pool should be reduced to MaxPoolSize or less");
        }

        [Test]
        public void MaxPoolSizeWorksWithPeriodicTrigger()
        {
            List<TestPoolItem> purgedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Periodic,
                    MaxPoolSize = 3,
                    PurgeIntervalSeconds = 2f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (item, _) => purgedItems.Add(item),
                }
            );

            List<PooledResource<TestPoolItem>> rented = new();
            for (int i = 0; i < 10; i++)
            {
                rented.Add(pool.Get(out TestPoolItem _));
            }

            _currentTime = 2f;
            foreach (PooledResource<TestPoolItem> resource in rented)
            {
                resource.Dispose();
            }
            rented.Clear();

            int countBeforePeriodic = pool.Count;
            TestContext.WriteLine(
                $"Before periodic trigger at t={_currentTime}: pool.Count={countBeforePeriodic}"
            );

            _currentTime = 5f;

            using (PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem _)) { }

            TestContext.WriteLine(
                $"After Get/Return at t={_currentTime}: pool.Count={pool.Count}, purged={purgedItems.Count}"
            );

            Assert.Less(
                pool.Count,
                countBeforePeriodic,
                "Periodic trigger should enforce MaxPoolSize"
            );
        }

        [Test]
        public void DefaultPoolOptionsUsesPeriodicTrigger()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: new PoolOptions<TestPoolItem> { TimeProvider = TestTimeProvider }
            );

            Assert.AreEqual(5, pool.Count);

            int countBefore = pool.Count;

            using (PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem _)) { }

            Assert.AreEqual(
                countBefore,
                pool.Count,
                "Default pool should not purge on every Get (periodic, not OnRent)"
            );
        }

        [Test]
        public void DefaultPoolOptionsAllowsUnboundedSize()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            List<PooledResource<TestPoolItem>> rented = new();
            for (int i = 0; i < 1000; i++)
            {
                rented.Add(pool.Get(out TestPoolItem _));
            }

            foreach (PooledResource<TestPoolItem> resource in rented)
            {
                resource.Dispose();
            }
            rented.Clear();

            TestContext.WriteLine($"After returning 1000 items: pool.Count={pool.Count}");

            Assert.AreEqual(
                1000,
                pool.Count,
                "Default pool with no MaxPoolSize should hold all returned items"
            );
        }

        [Test]
        public void PurgeOnEmptyPoolReturnsZero()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                }
            );

            Assert.AreEqual(0, pool.Count);

            int purged = pool.Purge();

            TestContext.WriteLine($"Purge on empty pool returned: {purged}");

            Assert.AreEqual(0, purged, "Purging an empty pool should return 0");
            Assert.AreEqual(0, pool.Count, "Pool should remain empty");
        }

        [Test]
        public void RapidRentReturnCyclesDoNotCausePoolCorruption()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    MaxPurgesPerOperation = 0,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 10_000; i++)
            {
                using (PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem _)) { }
            }

            PoolStatistics stats = pool.GetStatistics();

            TestContext.WriteLine(
                $"After 10K cycles: pool.Count={pool.Count}, RentCount={stats.RentCount}, ReturnCount={stats.ReturnCount}"
            );

            Assert.AreEqual(
                10,
                pool.Count,
                "Pool count should be stable after rapid rent/return cycles"
            );
            Assert.AreEqual(10_000, stats.RentCount, "All rents should be recorded");
            Assert.AreEqual(10_000, stats.ReturnCount, "All returns should be recorded");
        }

        [Test]
        public void DisposedPoolStillCreatesNewItemsOnGet()
        {
            WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 3,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            pool.Dispose();

            using PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem item);

            TestContext.WriteLine($"After dispose, Get() returned item with Id={item.Id}");

            Assert.IsTrue(item != null, "Disposed pool should still produce items via Get()");
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
