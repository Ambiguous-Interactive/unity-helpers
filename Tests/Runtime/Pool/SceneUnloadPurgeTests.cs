// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System.Collections.Generic;
    using System.Threading;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Tests for scene unload integration with pool purging.
    /// Verifies that scene unload events trigger appropriate purge behavior.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Basic lifecycle settings tests (e.g., PurgeOnSceneUnload defaults, enable/disable)
    /// are covered in <see cref="PoolLifecycleHooksTests"/>. This test class focuses on
    /// scene-unload-specific purge behavior and integration scenarios.
    /// </para>
    /// <para>
    /// Note: Unity shutdown behavior (Application.quitting) is handled gracefully by the
    /// implementation through weak references in GlobalPoolRegistry and proper unsubscription
    /// in UnregisterLifecycleHooks. Explicit testing of shutdown scenarios is not feasible
    /// in the test environment as it would require terminating the test runner.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SceneUnloadPurgeTests
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
            GlobalPoolRegistry.Clear();
            // Disable memory pressure monitoring to ensure deterministic test behavior
            _wasMemoryPressureEnabled = MemoryPressureMonitor.Enabled;
            MemoryPressureMonitor.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            PoolPurgeSettings.ResetToDefaults();
            GlobalPoolRegistry.Clear();
            MemoryPressureMonitor.Enabled = _wasMemoryPressureEnabled;
        }

        [Test]
        public void PurgeOnSceneUnloadCanBeReEnabled()
        {
            PoolPurgeSettings.PurgeOnSceneUnload = false;
            Assert.IsFalse(PoolPurgeSettings.PurgeOnSceneUnload);

            PoolPurgeSettings.PurgeOnSceneUnload = true;
            Assert.IsTrue(PoolPurgeSettings.PurgeOnSceneUnload);
        }

        [Test]
        public void ResetToDefaultsRestoresPurgeOnSceneUnload()
        {
            PoolPurgeSettings.PurgeOnSceneUnload = false;
            Assert.IsFalse(PoolPurgeSettings.PurgeOnSceneUnload);

            PoolPurgeSettings.ResetToDefaults();
            Assert.IsTrue(PoolPurgeSettings.PurgeOnSceneUnload);
        }

        [Test]
        public void PurgeAllPoolsWithSceneUnloadedReasonPurgesAllPools()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
                MinRetainCount = 0, // Explicit min retain count
                WarmRetainCount = 0, // Disable warm buffer to allow full purge
            };

            using WallstopGenericPool<TestPoolItem> pool1 = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: options
            );

            using WallstopGenericPool<TestPoolItem> pool2 = new(
                () => new TestPoolItem(),
                preWarmCount: 3,
                options: options
            );

            Assert.AreEqual(5, pool1.Count);
            Assert.AreEqual(3, pool2.Count);

            int totalPurged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(8, totalPurged);
            Assert.AreEqual(0, pool1.Count);
            Assert.AreEqual(0, pool2.Count);
        }

        [Test]
        public void PurgeAllPoolsWithSceneUnloadedReasonTracksCorrectReason()
        {
            GlobalPoolRegistry.Clear();

            List<PurgeReason> reasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, reason) => reasons.Add(reason),
                TimeProvider = TestTimeProvider,
                MinRetainCount = 0, // Explicit min retain count
                WarmRetainCount = 0, // Disable warm buffer to allow full purge
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 3,
                options: options
            );

            PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(3, reasons.Count);
            foreach (PurgeReason reason in reasons)
            {
                Assert.AreEqual(PurgeReason.SceneUnloaded, reason);
            }
        }

        [Test]
        public void SceneUnloadedPurgeRespectsMinRetainCount()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                MinRetainCount = 2,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: options
            );

            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(8, purged);
            Assert.AreEqual(2, pool.Count);
        }

        [Test]
        public void SceneUnloadedPurgeRespectsHysteresis()
        {
            GlobalPoolRegistry.Clear();

            // Time 0 reads as uninitialized in the tracker.
            _currentTime = 1f;

            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                HysteresisSeconds = 60f,
                SpikeThresholdMultiplier = 0.1f, // Low threshold to ensure spike triggers on first rental
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: options
            );

            // Rent and return to trigger spike detection and enter hysteresis period
            pool.Get().Dispose();

            // Try to purge with SceneUnloaded while respecting hysteresis - should be blocked
            int purgedWithHysteresis = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(
                0,
                purgedWithHysteresis,
                "SceneUnloaded purge should be blocked when respecting hysteresis"
            );
            Assert.AreEqual(
                5,
                pool.Count,
                "Pool count should remain unchanged when hysteresis blocks purge"
            );
        }

        [Test]
        public void SceneUnloadedPurgeCanBypassHysteresisWhenIgnored()
        {
            GlobalPoolRegistry.Clear();

            // Time 0 reads as uninitialized in the tracker.
            _currentTime = 1f;

            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                HysteresisSeconds = 60f,
                SpikeThresholdMultiplier = 0.1f, // Low threshold to ensure spike triggers on first rental
                WarmRetainCount = 0, // Disable warm buffer for predictable purge count
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: options
            );

            // Rent and return to trigger spike detection and enter hysteresis period
            pool.Get().Dispose();

            // Purge with ignoreHysteresis: true should bypass hysteresis
            int purgedWithBypass = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: false,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(
                5,
                purgedWithBypass,
                "SceneUnloaded purge should proceed when ignoring hysteresis"
            );
            Assert.AreEqual(0, pool.Count, "Pool should be empty after ignoring hysteresis");
        }

        [Test]
        public void SceneUnloadedPurgeAfterHysteresisExpiresPurgesNormally()
        {
            GlobalPoolRegistry.Clear();

            // Time 0 reads as uninitialized in the tracker.
            _currentTime = 1f;

            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                HysteresisSeconds = 60f,
                SpikeThresholdMultiplier = 0.1f, // Low threshold to ensure spike triggers on first rental
                WarmRetainCount = 0, // Disable warm buffer for predictable purge count
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: options
            );

            // Rent and return to trigger spike detection
            pool.Get().Dispose();

            // Advance time past hysteresis period (1 + 60 = 61, so 121 should be past)
            _currentTime = 121f;

            // Now the purge should proceed
            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(5, purged, "Purge should proceed after hysteresis expires");
            Assert.AreEqual(0, pool.Count, "Pool should be empty after purge");
        }

        [Test]
        public void SceneUnloadedPurgeHandlesEmptyPools()
        {
            GlobalPoolRegistry.Clear();

            using WallstopGenericPool<TestPoolItem> emptyPool = new(
                () => new TestPoolItem(),
                preWarmCount: 0,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(0, purged);
            Assert.AreEqual(0, emptyPool.Count);
        }

        [Test]
        public void SceneUnloadedPurgeHandlesMixedPoolSizes()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                MinRetainCount = 0, // Explicit min retain count
                WarmRetainCount = 0, // Disable warm buffer for predictable purge count
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> emptyPool = new(
                () => new TestPoolItem(),
                preWarmCount: 0,
                options: options
            );

            using WallstopGenericPool<TestPoolItem> smallPool = new(
                () => new TestPoolItem(),
                preWarmCount: 2,
                options: options
            );

            using WallstopGenericPool<TestPoolItem> largePool = new(
                () => new TestPoolItem(),
                preWarmCount: 50,
                options: options
            );

            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(52, purged);
            Assert.AreEqual(0, emptyPool.Count);
            Assert.AreEqual(0, smallPool.Count);
            Assert.AreEqual(0, largePool.Count);
        }

        [Test]
        public void SceneUnloadedPurgeReturnsZeroWhenNoPoolsRegistered()
        {
            GlobalPoolRegistry.Clear();

            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(0, purged);
        }

        [Test]
        public void DirectPurgeWithSceneUnloadedReason()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                MinRetainCount = 0, // Explicit min retain count
                WarmRetainCount = 0, // Disable warm buffer for predictable purge count
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 7,
                options: options
            );

            int purged = pool.Purge(PurgeReason.SceneUnloaded);

            Assert.AreEqual(7, purged);
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void DirectPurgeWithSceneUnloadedReasonRespectsMinRetainCount()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                MinRetainCount = 3,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: options
            );

            int purged = pool.Purge(PurgeReason.SceneUnloaded);

            Assert.AreEqual(7, purged);
            Assert.AreEqual(3, pool.Count);
        }

        [Test]
        public void SceneUnloadedPurgeStatisticsAreTracked()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                WarmRetainCount = 0, // Disable warm buffer for predictable purge count
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: options
            );

            PoolStatistics statsBefore = pool.GetStatistics();
            long totalPurgesBefore = statsBefore.PurgeCount;

            _currentTime = 2f;
            pool.Purge(PurgeReason.SceneUnloaded);

            PoolStatistics statsAfter = pool.GetStatistics();

            Assert.AreEqual(
                totalPurgesBefore + 5,
                statsAfter.PurgeCount,
                "PurgeCount should be incremented by purge count"
            );
        }

        [Test]
        public void SceneUnloadedPurgeWithWarmRetainCount()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                MinRetainCount = 1,
                WarmRetainCount = 5,
                IdleTimeoutSeconds = 300f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 20,
                options: options
            );

            // Access the pool to make it "active"
            pool.Get().Dispose();

            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: false,
                reason: PurgeReason.SceneUnloaded
            );

            // An explicit purge respects MinRetainCount alone, not WarmRetainCount.
            TestContext.WriteLine(
                $"After SceneUnloaded purge: purged={purged}, pool count={pool.Count}"
            );
            Assert.AreEqual(19, purged);
            Assert.AreEqual(1, pool.Count);
        }

        [Test]
        public void SceneUnloadedPurgeIdlePoolIgnoresWarmRetain()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                MinRetainCount = 1,
                WarmRetainCount = 10,
                IdleTimeoutSeconds = 60f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 20,
                options: options
            );

            // Advance time to make pool idle
            _currentTime = 120f;

            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: false,
                reason: PurgeReason.SceneUnloaded
            );

            // Idle pool should purge to MinRetainCount, ignoring WarmRetainCount
            Assert.AreEqual(19, purged);
            Assert.AreEqual(1, pool.Count);
        }

#if !SINGLE_THREADED
        [Test]
        public void ConcurrentSceneUnloadPurgesAreThreadSafe()
        {
            GlobalPoolRegistry.Clear();
            List<WallstopGenericPool<TestPoolItem>> pools = new();

            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            for (int i = 0; i < 10; i++)
            {
                pools.Add(
                    new WallstopGenericPool<TestPoolItem>(
                        () => new TestPoolItem(),
                        preWarmCount: 5,
                        options: options
                    )
                );
            }

            const int purgeCount = 20;
            System.Threading.Tasks.Task[] tasks = new System.Threading.Tasks.Task[purgeCount];

            for (int i = 0; i < purgeCount; i++)
            {
                tasks[i] = System.Threading.Tasks.Task.Run(() =>
                    PoolPurgeSettings.PurgeAllPools(
                        respectHysteresis: true,
                        reason: PurgeReason.SceneUnloaded
                    )
                );
            }

            Assert.DoesNotThrow(() => System.Threading.Tasks.Task.WaitAll(tasks));

            // Clean up
            foreach (WallstopGenericPool<TestPoolItem> pool in pools)
            {
                pool.Dispose();
            }
        }

        [Test]
        public void ConcurrentPurgeOnSceneUnloadToggleIsThreadSafe()
        {
            const int iterations = 100;
            System.Threading.Tasks.Task[] tasks = new System.Threading.Tasks.Task[4];

            tasks[0] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PoolPurgeSettings.PurgeOnSceneUnload = i % 2 == 0;
                    bool _ = PoolPurgeSettings.PurgeOnSceneUnload;
                }
            });

            tasks[1] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PoolPurgeSettings.PurgeOnSceneUnload = i % 2 != 0;
                    bool _ = PoolPurgeSettings.PurgeOnSceneUnload;
                }
            });

            tasks[2] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PoolPurgeSettings.ResetToDefaults();
                }
            });

            tasks[3] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    bool _ = PoolPurgeSettings.PurgeOnSceneUnload;
                    Thread.SpinWait(10);
                }
            });

            Assert.DoesNotThrow(() => System.Threading.Tasks.Task.WaitAll(tasks));
        }
#endif

        [Test]
        public void MultiplePurgeReasonsCanBeMixed()
        {
            GlobalPoolRegistry.Clear();

            List<PurgeReason> reasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                MinRetainCount = 0, // Explicit min retain count
                WarmRetainCount = 0, // Disable warm buffer for predictable purge count
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, reason) => reasons.Add(reason),
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 15,
                options: options
            );

            // Purge with different reasons
            pool.Purge(PurgeReason.SceneUnloaded);
            TestContext.WriteLine(
                $"After SceneUnloaded purge: pool count={pool.Count}, reasons collected so far={reasons.Count}"
            );

            // Held simultaneously: a sequential get/dispose would hand back the same item.
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 10; i++)
            {
                resources.Add(pool.Get());
            }
            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }
            resources.Clear();

            TestContext.WriteLine($"After adding 10 items: pool count={pool.Count}");

            pool.Purge(PurgeReason.AppBackgrounded);
            TestContext.WriteLine(
                $"After AppBackgrounded purge: pool count={pool.Count}, reasons collected so far={reasons.Count}"
            );

            // Hold 5 items simultaneously before disposing
            for (int i = 0; i < 5; i++)
            {
                resources.Add(pool.Get());
            }
            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }
            resources.Clear();

            TestContext.WriteLine($"After adding 5 items: pool count={pool.Count}");

            pool.Purge(PurgeReason.Explicit);
            TestContext.WriteLine(
                $"After Explicit purge: pool count={pool.Count}, total reasons={reasons.Count}"
            );

            int sceneUnloadedCount = 0;
            int appBackgroundedCount = 0;
            int explicitCount = 0;

            foreach (PurgeReason reason in reasons)
            {
                switch (reason)
                {
                    case PurgeReason.SceneUnloaded:
                        sceneUnloadedCount++;
                        break;
                    case PurgeReason.AppBackgrounded:
                        appBackgroundedCount++;
                        break;
                    case PurgeReason.Explicit:
                        explicitCount++;
                        break;
                }
            }

            Assert.AreEqual(15, sceneUnloadedCount);
            Assert.AreEqual(10, appBackgroundedCount);
            Assert.AreEqual(5, explicitCount);
        }

        [Test]
        public void SceneUnloadedPurgeWithMaxPurgesPerOperationRespectsLimit()
        {
            const int preWarmCount = 25;
            const int maxPurgesPerOp = 5;

            PoolOptions<TestPoolItem> options = new()
            {
                MaxPurgesPerOperation = maxPurgesPerOp,
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: options
            );

            // Advance time past idle timeout
            _currentTime = 2f;

            // Regular purge should limit to MaxPurgesPerOperation
            int firstPurge = pool.Purge();

            Assert.AreEqual(maxPurgesPerOp, firstPurge);
            Assert.AreEqual(preWarmCount - maxPurgesPerOp, pool.Count);
            Assert.IsTrue(pool.HasPendingPurges);
        }

        [Test]
        public void SceneUnloadedPurgeWithReasonBypassesMaxPurgesPerOperation()
        {
            const int preWarmCount = 25;
            const int maxPurgesPerOp = 5;

            PoolOptions<TestPoolItem> options = new()
            {
                MaxPurgesPerOperation = maxPurgesPerOp,
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: options
            );

            // Advance time past idle timeout
            _currentTime = 2f;

            // Purge with explicit reason should bypass MaxPurgesPerOperation
            int purged = pool.Purge(PurgeReason.SceneUnloaded);

            Assert.AreEqual(preWarmCount, purged);
            Assert.AreEqual(0, pool.Count);
            Assert.IsFalse(pool.HasPendingPurges);
        }

        [Test]
        public void ForceFullPurgeAllWithSceneUnloadedReason()
        {
            GlobalPoolRegistry.Clear();

            const int preWarmCount = 25;
            const int maxPurgesPerOp = 5;

            PoolOptions<TestPoolItem> options = new()
            {
                MaxPurgesPerOperation = maxPurgesPerOp,
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: options
            );

            // Advance time past idle timeout
            _currentTime = 2f;

            // ForceFullPurgeAll should bypass MaxPurgesPerOperation
            int purged = GlobalPoolRegistry.ForceFullPurgeAll(
                respectHysteresis: true,
                reason: PurgeReason.SceneUnloaded
            );

            Assert.AreEqual(preWarmCount, purged);
            Assert.AreEqual(0, pool.Count);
            Assert.IsFalse(pool.HasPendingPurges);
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
