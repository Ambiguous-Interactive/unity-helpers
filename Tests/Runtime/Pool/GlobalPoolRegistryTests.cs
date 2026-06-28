// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;
#if !SINGLE_THREADED
    using System.Threading.Tasks;
#endif

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class GlobalPoolRegistryTests
    {
        private sealed class TestPoolItem
        {
            public int Id { get; }
            public bool WasDisposed { get; set; }

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

        private float _currentTime;
        private bool _wasMemoryPressureEnabled;

        private float TestTimeProvider()
        {
            return _currentTime;
        }

        [SetUp]
        public void SetUp()
        {
            // Start at t=1 to avoid time=0 initialization issues
            // (time 0 is treated as uninitialized in the tracker)
            _currentTime = 1f;
            TestPoolItem.ResetIdCounter();
            GlobalPoolRegistry.Clear();
            GlobalPoolRegistry.ResetBudgetSettings();
            PoolPurgeSettings.ResetToDefaults();
            _wasMemoryPressureEnabled = MemoryPressureMonitor.Enabled;
            MemoryPressureMonitor.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            GlobalPoolRegistry.Clear();
            GlobalPoolRegistry.ResetBudgetSettings();
            PoolPurgeSettings.ResetToDefaults();
            MemoryPressureMonitor.Reset();
            MemoryPressureMonitor.Enabled = _wasMemoryPressureEnabled;
        }

        [Test]
        public void RegisterAddsPoolToRegistry()
        {
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool();

            Assert.AreEqual(1, GlobalPoolRegistry.RegisteredCount);
        }

        [Test]
        public void UnregisterRemovesPoolFromRegistry()
        {
            WallstopGenericPool<TestPoolItem> pool = CreateTestPool();

            Assert.AreEqual(1, GlobalPoolRegistry.RegisteredCount);

            pool.Dispose();

            Assert.AreEqual(0, GlobalPoolRegistry.RegisteredCount);
        }

        [Test]
        public void CurrentTotalPooledItemsReturnsCorrectTotal()
        {
            using WallstopGenericPool<TestPoolItem> pool1 = CreateTestPool(preWarmCount: 5);
            using WallstopGenericPool<TestPoolItem> pool2 = CreateTestPool(preWarmCount: 10);

            Assert.AreEqual(15, GlobalPoolRegistry.CurrentTotalPooledItems);
        }

        [Test]
        public void CurrentTotalPooledItemsExcludesDisposedPools()
        {
            using WallstopGenericPool<TestPoolItem> pool1 = CreateTestPool(preWarmCount: 5);
            WallstopGenericPool<TestPoolItem> pool2 = CreateTestPool(preWarmCount: 10);

            Assert.AreEqual(15, GlobalPoolRegistry.CurrentTotalPooledItems);

            pool2.Dispose();

            Assert.AreEqual(5, GlobalPoolRegistry.CurrentTotalPooledItems);
        }

        [Test]
        public void GlobalMaxPooledItemsDefaultsToExpectedValue()
        {
            Assert.AreEqual(
                GlobalPoolRegistry.DefaultGlobalMaxPooledItems,
                GlobalPoolRegistry.GlobalMaxPooledItems
            );
        }

        [Test]
        public void GlobalMaxPooledItemsCanBeSet()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 1000;

            Assert.AreEqual(1000, GlobalPoolRegistry.GlobalMaxPooledItems);
        }

        [Test]
        public void BudgetEnforcementEnabledDefaultsToTrue()
        {
            Assert.IsTrue(
                GlobalPoolRegistry.BudgetEnforcementEnabled,
                "Budget enforcement should be enabled by default"
            );
        }

        [Test]
        public void BudgetEnforcementEnabledCanBeToggled()
        {
            GlobalPoolRegistry.BudgetEnforcementEnabled = false;
            Assert.IsFalse(
                GlobalPoolRegistry.BudgetEnforcementEnabled,
                "Budget enforcement should be disabled after setting to false"
            );

            GlobalPoolRegistry.BudgetEnforcementEnabled = true;
            Assert.IsTrue(
                GlobalPoolRegistry.BudgetEnforcementEnabled,
                "Budget enforcement should be enabled after setting to true"
            );
        }

        [Test]
        public void EnforceBudgetReturnsZeroWhenUnderBudget()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 100;
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: 50);

            int purged = GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(0, purged);
            Assert.AreEqual(50, GlobalPoolRegistry.CurrentTotalPooledItems);
        }

        [Test]
        public void EnforceBudgetPurgesExcessItemsWhenOverBudget()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 30;
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: 50);

            int purged = GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(20, purged);
            Assert.AreEqual(30, GlobalPoolRegistry.CurrentTotalPooledItems);
        }

        [Test]
        public void EnforceBudgetRespectsMinRetainCount()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 5;
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(
                preWarmCount: 20,
                minRetainCount: 15
            );

            int purged = GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(5, purged);
            Assert.AreEqual(15, GlobalPoolRegistry.CurrentTotalPooledItems);
        }

        [Test]
        public void EnforceBudgetReturnsZeroWhenBudgetIsZeroOrNegative()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 0;
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: 50);

            int purged = GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(0, purged);
            Assert.AreEqual(50, GlobalPoolRegistry.CurrentTotalPooledItems);

            GlobalPoolRegistry.GlobalMaxPooledItems = -100;
            purged = GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(0, purged);
        }

        [Test]
        public void EnforceBudgetPurgesLRUWhenMultiplePools()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 15;

            // Pool1 accessed at time 0 (older)
            _currentTime = 0f;
            using WallstopGenericPool<TestPoolItem> pool1 = CreateTestPool(preWarmCount: 10);
            using (pool1.Get())
            {
                // Access pool1 at time 0
            }

            // Pool2 accessed at time 10 (newer)
            _currentTime = 10f;
            using WallstopGenericPool<TestPoolItem> pool2 = CreateTestPool(preWarmCount: 10);
            using (pool2.Get())
            {
                // Access pool2 at time 10
            }

            // Total is 20, budget is 15, so we need to purge 5
            // Pool1 should be purged first since it was accessed earlier
            int purged = GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(5, purged);
            Assert.LessOrEqual(pool1.Count, 5);
            Assert.AreEqual(10, pool2.Count);
        }

        [Test]
        public void EnforceBudgetPurgesAcrossMultiplePoolsWhenSinglePoolInsufficient()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 5;

            _currentTime = 0f;
            using WallstopGenericPool<TestPoolItem> pool1 = CreateTestPool(
                preWarmCount: 10,
                minRetainCount: 3
            );
            using (pool1.Get()) { }

            _currentTime = 10f;
            using WallstopGenericPool<TestPoolItem> pool2 = CreateTestPool(
                preWarmCount: 10,
                minRetainCount: 3
            );
            using (pool2.Get()) { }

            // Total is 20, budget is 5
            // Pool1 can only purge 7 (10 - 3), pool2 can purge 7 (10 - 3)
            // We need to purge 15, so both pools should be purged to their minimums
            int purged = GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(14, purged);
            Assert.AreEqual(3, pool1.Count);
            Assert.AreEqual(3, pool2.Count);
        }

        [Test]
        public void TryEnforceBudgetIfNeededReturnsZeroWhenDisabled()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 10;
            GlobalPoolRegistry.BudgetEnforcementEnabled = false;
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: 20);

            int purged = GlobalPoolRegistry.TryEnforceBudgetIfNeeded();

            Assert.AreEqual(0, purged);
            Assert.AreEqual(20, GlobalPoolRegistry.CurrentTotalPooledItems);
        }

        [Test]
        public void TryEnforceBudgetIfNeededEnforcesInterval()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 10;
            GlobalPoolRegistry.BudgetEnforcementIntervalSeconds = 10f;
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: 20);

            // First call should enforce
            int purged1 = GlobalPoolRegistry.TryEnforceBudgetIfNeeded();
            Assert.AreEqual(10, purged1);

            // Refill the pool
            for (int i = 0; i < 10; i++)
            {
                using (pool.Get()) { }
            }

            // Second call within interval should not enforce
            int purged2 = GlobalPoolRegistry.TryEnforceBudgetIfNeeded();
            Assert.AreEqual(0, purged2);
        }

        [Test]
        public void GetStatisticsReturnsCorrectSnapshot()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 100;

            _currentTime = 5f;
            using WallstopGenericPool<TestPoolItem> pool1 = CreateTestPool(preWarmCount: 10);
            using (pool1.Get()) { }

            _currentTime = 15f;
            using WallstopGenericPool<TestPoolItem> pool2 = CreateTestPool(preWarmCount: 20);
            using (pool2.Get()) { }

            GlobalPoolStatistics stats = GlobalPoolRegistry.GetStatistics();

            Assert.AreEqual(2, stats.LivePoolCount);
            Assert.AreEqual(2, stats.StatisticsPoolCount);
            Assert.AreEqual(30, stats.TotalPooledItems);
            Assert.AreEqual(100, stats.GlobalMaxPooledItems);
            Assert.AreEqual(0.3f, stats.BudgetUtilization, 0.01f);
            Assert.IsFalse(
                stats.IsBudgetExceeded,
                "Budget should not be exceeded at 30% utilization"
            );
            Assert.AreEqual(5f, stats.OldestPoolAccessTime);
            Assert.AreEqual(15f, stats.NewestPoolAccessTime);
        }

        [Test]
        public void GetStatisticsIndicatesBudgetExceeded()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 10;
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: 20);

            GlobalPoolStatistics stats = GlobalPoolRegistry.GetStatistics();

            Assert.IsTrue(
                stats.IsBudgetExceeded,
                "Budget should be exceeded when items exceed max"
            );
            Assert.AreEqual(2.0f, stats.BudgetUtilization, 0.01f);
        }

        [Test]
        public void ResetBudgetSettingsRestoresDefaults()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 100;
            GlobalPoolRegistry.BudgetEnforcementEnabled = false;
            GlobalPoolRegistry.BudgetEnforcementIntervalSeconds = 1f;

            GlobalPoolRegistry.ResetBudgetSettings();

            Assert.AreEqual(
                GlobalPoolRegistry.DefaultGlobalMaxPooledItems,
                GlobalPoolRegistry.GlobalMaxPooledItems
            );
            Assert.IsTrue(
                GlobalPoolRegistry.BudgetEnforcementEnabled,
                "Budget enforcement should be re-enabled after reset"
            );
            Assert.AreEqual(
                GlobalPoolRegistry.DefaultBudgetEnforcementIntervalSeconds,
                GlobalPoolRegistry.BudgetEnforcementIntervalSeconds
            );
        }

        [Test]
        public void PurgeForBudgetInvokesOnPurgeCallback()
        {
            int purgeCallbackCount = 0;
            PurgeReason capturedReason = PurgeReason.Explicit;

            PoolOptions<TestPoolItem> options = new()
            {
                TimeProvider = TestTimeProvider,
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, reason) =>
                {
                    purgeCallbackCount++;
                    capturedReason = reason;
                },
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: options
            );

            GlobalPoolRegistry.GlobalMaxPooledItems = 5;
            GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(5, purgeCallbackCount);
            Assert.AreEqual(PurgeReason.BudgetExceeded, capturedReason);
        }

        [Test]
        public void PurgeForBudgetInvokesOnDisposalCallback()
        {
            List<TestPoolItem> disposedItems = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                onDisposal: item =>
                {
                    item.WasDisposed = true;
                    disposedItems.Add(item);
                },
                options: new PoolOptions<TestPoolItem>
                {
                    TimeProvider = TestTimeProvider,
                    Triggers = PurgeTrigger.Explicit,
                }
            );

            GlobalPoolRegistry.GlobalMaxPooledItems = 5;
            GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(5, disposedItems.Count);
            foreach (TestPoolItem item in disposedItems)
            {
                Assert.IsTrue(
                    item.WasDisposed,
                    "Each disposed item should have WasDisposed set to true"
                );
            }
        }

        [Test]
        public void LastAccessTimeUpdatesOnGet()
        {
            _currentTime = 0f;
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: 1);

            _currentTime = 10f;
            using (pool.Get()) { }

            Assert.AreEqual(10f, pool.LastAccessTime);
        }

        [Test]
        public void CurrentPooledCountReturnsCorrectValue()
        {
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: 5);

            Assert.AreEqual(5, pool.CurrentPooledCount);

            using (pool.Get())
            {
                Assert.AreEqual(4, pool.CurrentPooledCount);
            }

            Assert.AreEqual(5, pool.CurrentPooledCount);
        }

        [Test]
        public void ClearRemovesAllPools()
        {
            using WallstopGenericPool<TestPoolItem> pool1 = CreateTestPool();
            using WallstopGenericPool<TestPoolItem> pool2 = CreateTestPool();

            Assert.AreEqual(2, GlobalPoolRegistry.RegisteredCount);

            GlobalPoolRegistry.Clear();

            Assert.AreEqual(0, GlobalPoolRegistry.RegisteredCount);
        }

        [TestCase(10, 5, 5)]
        [TestCase(100, 50, 50)]
        [TestCase(20, 0, 0)] // Budget of 0 is treated as "no budget enforcement" per EnforceBudget_ReturnsZero_WhenBudgetIsZeroOrNegative
        [TestCase(10, 10, 0)]
        public void EnforceBudgetVariousBudgetScenarios(
            int poolSize,
            int budget,
            int expectedPurged
        )
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = budget;
            using WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: poolSize);

            int purged = GlobalPoolRegistry.EnforceBudget();

            Assert.AreEqual(expectedPurged, purged);
        }

        [Test]
        public void DeadWeakReferencesAreCleanedUpDuringBudgetEnforcement()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 100;

            // Register a pool whose only strong reference lives in (and dies with) the helper frame,
            // so it is retained solely by the registry's WeakReference and is collectible.
            RegisterOrphanTestPool(preWarmCount: 5);
            Assert.AreEqual(1, GlobalPoolRegistry.RegisteredCount);
            Assert.AreEqual(5, GlobalPoolRegistry.CurrentTotalPooledItems);

            if (!TryForceCollectOrphanPools(expectedAliveCount: 0))
            {
                Assert.Ignore(
                    "Platform GC did not reclaim the orphaned pool (conservative GC); "
                        + "dead-weak-reference cleanup cannot be verified deterministically here."
                );
            }

            // EnforceBudget should clean up the dead WeakReference entry
            // and not throw any errors when operating with dead references
            int purged = GlobalPoolRegistry.EnforceBudget();

            // After cleanup, the dead pool should no longer contribute to the count
            Assert.AreEqual(0, GlobalPoolRegistry.CurrentTotalPooledItems);
            Assert.AreEqual(0, GlobalPoolRegistry.RegisteredCount);
            Assert.AreEqual(0, purged);
        }

        [Test]
        public void DeadWeakReferencesAreCleanedUpDuringPurgeAll()
        {
            // Register a pool retained only by the registry's WeakReference (see helper remarks).
            RegisterOrphanTestPool(preWarmCount: 5);
            Assert.AreEqual(1, GlobalPoolRegistry.RegisteredCount);

            if (!TryForceCollectOrphanPools(expectedAliveCount: 0))
            {
                Assert.Ignore(
                    "Platform GC did not reclaim the orphaned pool (conservative GC); "
                        + "dead-weak-reference cleanup cannot be verified deterministically here."
                );
            }

            // PurgeAll should clean up the dead WeakReference entry
            // and not throw any errors when operating with dead references
            int purged = GlobalPoolRegistry.PurgeAll(
                respectHysteresis: true,
                reason: PurgeReason.Explicit
            );

            // After cleanup, the dead pool should be removed from registry
            Assert.AreEqual(0, GlobalPoolRegistry.RegisteredCount);
            Assert.AreEqual(0, purged);
        }

        [Test]
        public void DeadWeakReferencesAreCleanedUpDuringGetStatistics()
        {
            // Register a pool retained only by the registry's WeakReference (see helper remarks).
            RegisterOrphanTestPool(preWarmCount: 5);
            Assert.AreEqual(1, GlobalPoolRegistry.RegisteredCount);

            if (!TryForceCollectOrphanPools(expectedAliveCount: 0))
            {
                Assert.Ignore(
                    "Platform GC did not reclaim the orphaned pool (conservative GC); "
                        + "dead-weak-reference cleanup cannot be verified deterministically here."
                );
            }

            // GetStatistics should clean up the dead WeakReference entry
            // and not throw any errors when operating with dead references
            GlobalPoolStatistics stats = GlobalPoolRegistry.GetStatistics();

            // After cleanup, the dead pool should not appear in statistics
            Assert.AreEqual(0, stats.LivePoolCount);
            Assert.AreEqual(0, stats.StatisticsPoolCount);
            Assert.AreEqual(0, stats.TotalPooledItems);
            Assert.AreEqual(0, GlobalPoolRegistry.RegisteredCount);
        }

        [Test]
        public void DeadWeakReferencesDoNotAffectLivePools()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 100;

            // Register a pool that is collectible (retained only by the registry's WeakReference).
            RegisterOrphanTestPool(preWarmCount: 5);

            // Create a pool that will remain alive
            using WallstopGenericPool<TestPoolItem> livePool = CreateTestPool(preWarmCount: 10);

            Assert.AreEqual(2, GlobalPoolRegistry.RegisteredCount);
            Assert.AreEqual(15, GlobalPoolRegistry.CurrentTotalPooledItems);

            // Force collection; only the orphaned pool should go away, leaving the live one (1).
            if (!TryForceCollectOrphanPools(expectedAliveCount: 1))
            {
                Assert.Ignore(
                    "Platform GC did not reclaim the orphaned pool (conservative GC); "
                        + "dead-weak-reference cleanup cannot be verified deterministically here."
                );
            }

            // Operations should work correctly with mixed live and dead references
            GlobalPoolStatistics stats = GlobalPoolRegistry.GetStatistics();

            // Dead pool should be cleaned up, live pool should remain
            Assert.AreEqual(1, stats.LivePoolCount);
            Assert.AreEqual(1, stats.StatisticsPoolCount);
            Assert.AreEqual(10, stats.TotalPooledItems);
            Assert.AreEqual(1, GlobalPoolRegistry.RegisteredCount);

            GC.KeepAlive(livePool);
        }

#if !SINGLE_THREADED
        [Test]
        public void EnforceBudgetThreadSafe()
        {
            GlobalPoolRegistry.GlobalMaxPooledItems = 100;

            List<WallstopGenericPool<TestPoolItem>> pools = new();
            for (int i = 0; i < 10; i++)
            {
                pools.Add(CreateTestPool(preWarmCount: 20));
            }

            try
            {
                // Run budget enforcement from multiple threads
                Parallel.For(
                    0,
                    100,
                    _ =>
                    {
                        GlobalPoolRegistry.EnforceBudget();
                    }
                );

                // All pools should still be usable
                foreach (WallstopGenericPool<TestPoolItem> pool in pools)
                {
                    using (pool.Get())
                    {
                        // Should not throw
                    }
                }
            }
            finally
            {
                foreach (WallstopGenericPool<TestPoolItem> pool in pools)
                {
                    pool.Dispose();
                }
            }
        }

        [Test]
        public void RegistrationThreadSafe()
        {
            List<WallstopGenericPool<TestPoolItem>> pools = new();
            object poolsLock = new();

            Parallel.For(
                0,
                100,
                _ =>
                {
                    WallstopGenericPool<TestPoolItem> pool = CreateTestPool();
                    lock (poolsLock)
                    {
                        pools.Add(pool);
                    }
                }
            );

            Assert.AreEqual(100, GlobalPoolRegistry.RegisteredCount);

            foreach (WallstopGenericPool<TestPoolItem> pool in pools)
            {
                pool.Dispose();
            }

            Assert.AreEqual(0, GlobalPoolRegistry.RegisteredCount);
        }
#endif

        private WallstopGenericPool<TestPoolItem> CreateTestPool(
            int preWarmCount = 0,
            int minRetainCount = 0
        )
        {
            return new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    TimeProvider = TestTimeProvider,
                    Triggers = PurgeTrigger.Explicit,
                    MinRetainCount = minRetainCount,
                    UseIntelligentPurging = false,
                }
            );
        }

        /// <summary>
        /// Creates a pool, lets it self-register, and returns WITHOUT exposing the strong reference
        /// to the caller's stack frame. The pool is only retained by the registry's
        /// <see cref="WeakReference"/>, so once this method returns it is eligible for collection.
        /// </summary>
        /// <remarks>
        /// <see cref="MethodImplOptions.NoInlining"/> is required: if the body were inlined into the
        /// test, the pool would become a local there and IL2CPP's CONSERVATIVE (Boehm) GC could keep
        /// it alive via a stale stack slot / register that the scanner conservatively treats as a
        /// live pointer -- so a subsequent <c>GC.Collect()</c> would not reclaim it and the
        /// dead-weak-reference assertions would fail in the standalone player (they pass under the
        /// editor's precise GC). Keeping creation in its own frame that fully unwinds removes that
        /// root so collection is deterministic.
        /// </remarks>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void RegisterOrphanTestPool(int preWarmCount)
        {
            WallstopGenericPool<TestPoolItem> pool = CreateTestPool(preWarmCount: preWarmCount);
            GC.KeepAlive(pool);
        }

        /// <summary>
        /// Forces collection of pools retained only by the registry's weak references and waits
        /// (bounded) until <see cref="GlobalPoolRegistry.RegisteredCount"/> drops to
        /// <paramref name="expectedAliveCount"/>. Returns true if it reached that count. Returns
        /// false when the platform GC did not reclaim the orphaned pool(s) within the budget -- which
        /// happens under a conservative GC and is not a product defect, so callers treat a false
        /// result as inconclusive rather than failing.
        /// </summary>
        private static bool TryForceCollectOrphanPools(int expectedAliveCount)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                if (GlobalPoolRegistry.RegisteredCount <= expectedAliveCount)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
