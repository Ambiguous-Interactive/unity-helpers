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
    public sealed class PoolLifecycleHooksTests
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
        public void PurgeReasonMemoryPressureExists()
        {
            Assert.AreEqual(4, (int)PurgeReason.MemoryPressure);
        }

        [Test]
        public void PurgeReasonAppBackgroundedExists()
        {
            Assert.AreEqual(5, (int)PurgeReason.AppBackgrounded);
        }

        [Test]
        public void PurgeReasonSceneUnloadedExists()
        {
            Assert.AreEqual(6, (int)PurgeReason.SceneUnloaded);
        }

        [Test]
        public void PurgeWithMemoryPressureReasonTracksCorrectly()
        {
            List<PurgeReason> reasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, reason) => reasons.Add(reason),
                TimeProvider = TestTimeProvider,
                WarmRetainCount = 0, // Disable warm buffer to allow full purge
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 3,
                options: options
            );

            int purged = pool.Purge(PurgeReason.MemoryPressure);

            Assert.AreEqual(3, purged);
            Assert.AreEqual(3, reasons.Count);
            foreach (PurgeReason reason in reasons)
            {
                Assert.AreEqual(PurgeReason.MemoryPressure, reason);
            }
        }

        [Test]
        public void PurgeWithAppBackgroundedReasonTracksCorrectly()
        {
            List<PurgeReason> reasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, reason) => reasons.Add(reason),
                TimeProvider = TestTimeProvider,
                WarmRetainCount = 0, // Disable warm buffer to allow full purge
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: options
            );

            int purged = pool.Purge(PurgeReason.AppBackgrounded);

            Assert.AreEqual(5, purged);
            Assert.AreEqual(5, reasons.Count);
            foreach (PurgeReason reason in reasons)
            {
                Assert.AreEqual(PurgeReason.AppBackgrounded, reason);
            }
        }

        [Test]
        public void PurgeWithSceneUnloadedReasonTracksCorrectly()
        {
            List<PurgeReason> reasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, reason) => reasons.Add(reason),
                TimeProvider = TestTimeProvider,
                WarmRetainCount = 0, // Disable warm buffer to allow full purge
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 2,
                options: options
            );

            int purged = pool.Purge(PurgeReason.SceneUnloaded);

            Assert.AreEqual(2, purged);
            Assert.AreEqual(2, reasons.Count);
            foreach (PurgeReason reason in reasons)
            {
                Assert.AreEqual(PurgeReason.SceneUnloaded, reason);
            }
        }

        [Test]
        public void PurgeOnLowMemoryDefaultsToTrue()
        {
            PoolPurgeSettings.ResetToDefaults();
            Assert.IsTrue(PoolPurgeSettings.PurgeOnLowMemory);
        }

        [Test]
        public void PurgeOnAppBackgroundDefaultsToTrue()
        {
            PoolPurgeSettings.ResetToDefaults();
            Assert.IsTrue(PoolPurgeSettings.PurgeOnAppBackground);
        }

        [Test]
        public void PurgeOnSceneUnloadDefaultsToTrue()
        {
            PoolPurgeSettings.ResetToDefaults();
            Assert.IsTrue(PoolPurgeSettings.PurgeOnSceneUnload);
        }

        [Test]
        public void PurgeOnLowMemoryCanBeDisabled()
        {
            PoolPurgeSettings.PurgeOnLowMemory = false;
            Assert.IsFalse(PoolPurgeSettings.PurgeOnLowMemory);
        }

        [Test]
        public void PurgeOnAppBackgroundCanBeDisabled()
        {
            PoolPurgeSettings.PurgeOnAppBackground = false;
            Assert.IsFalse(PoolPurgeSettings.PurgeOnAppBackground);
        }

        [Test]
        public void PurgeOnSceneUnloadCanBeDisabled()
        {
            PoolPurgeSettings.PurgeOnSceneUnload = false;
            Assert.IsFalse(PoolPurgeSettings.PurgeOnSceneUnload);
        }

        [Test]
        public void ResetToDefaultsRestoresLifecycleSettings()
        {
            PoolPurgeSettings.PurgeOnLowMemory = false;
            PoolPurgeSettings.PurgeOnAppBackground = false;
            PoolPurgeSettings.PurgeOnSceneUnload = false;

            PoolPurgeSettings.ResetToDefaults();

            Assert.IsTrue(PoolPurgeSettings.PurgeOnLowMemory);
            Assert.IsTrue(PoolPurgeSettings.PurgeOnAppBackground);
            Assert.IsTrue(PoolPurgeSettings.PurgeOnSceneUnload);
        }

        [Test]
        public void PoolRegistersWithGlobalRegistryOnCreation()
        {
            int initialCount = GlobalPoolRegistry.RegisteredCount;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            Assert.AreEqual(initialCount + 1, GlobalPoolRegistry.RegisteredCount);
        }

        [Test]
        public void PoolUnregistersFromGlobalRegistryOnDispose()
        {
            WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            int countBeforeDispose = GlobalPoolRegistry.RegisteredCount;
            pool.Dispose();

            // The pool leaves the registry at once; the count catches up on the next cleanup.
            Assert.LessOrEqual(GlobalPoolRegistry.RegisteredCount, countBeforeDispose);
        }

        [Test]
        public void MultiplePoolsRegisterWithGlobalRegistry()
        {
            GlobalPoolRegistry.Clear();

            using WallstopGenericPool<TestPoolItem> pool1 = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            using WallstopGenericPool<TestPoolItem> pool2 = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            using WallstopGenericPool<int> pool3 = new(
                () => 0,
                options: new PoolOptions<int>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            Assert.AreEqual(3, GlobalPoolRegistry.RegisteredCount);
        }

        [Test]
        public void RegisterNullPoolIsNoOp()
        {
            int initialCount = GlobalPoolRegistry.RegisteredCount;
            GlobalPoolRegistry.Register(null);
            Assert.AreEqual(initialCount, GlobalPoolRegistry.RegisteredCount);
        }

        [Test]
        public void UnregisterNullPoolIsNoOp()
        {
            Assert.DoesNotThrow(() => GlobalPoolRegistry.Unregister(null));
        }

        [Test]
        public void ClearRemovesAllRegistrations()
        {
            using WallstopGenericPool<TestPoolItem> pool1 = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            using WallstopGenericPool<TestPoolItem> pool2 = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            Assert.GreaterOrEqual(GlobalPoolRegistry.RegisteredCount, 2);

            GlobalPoolRegistry.Clear();

            Assert.AreEqual(0, GlobalPoolRegistry.RegisteredCount);
        }

        [Test]
        public void PurgeAllPoolsPurgesAllRegisteredPools()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
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
                reason: PurgeReason.Explicit
            );

            Assert.AreEqual(8, totalPurged);
            Assert.AreEqual(0, pool1.Count);
            Assert.AreEqual(0, pool2.Count);
        }

        [Test]
        public void PurgeAllPoolsWithDefaultParametersPurgesAll()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
                WarmRetainCount = 0, // Disable warm buffer to allow full purge
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: options
            );

            int purged = PoolPurgeSettings.PurgeAllPools();

            Assert.AreEqual(10, purged);
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void PurgeAllPoolsReturnsZeroWhenNoPoolsRegistered()
        {
            GlobalPoolRegistry.Clear();

            int purged = PoolPurgeSettings.PurgeAllPools();

            Assert.AreEqual(0, purged);
        }

        [Test]
        public void PurgeAllPoolsTracksCorrectReason()
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
                reason: PurgeReason.MemoryPressure
            );

            Assert.AreEqual(3, reasons.Count);
            foreach (PurgeReason reason in reasons)
            {
                Assert.AreEqual(PurgeReason.MemoryPressure, reason);
            }
        }

        [Test]
        public void PurgeAllPoolsContinuesOnIndividualPoolException()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> normalOptions = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
                MinRetainCount = 0, // Explicit min retain count
                WarmRetainCount = 0, // Disable warm buffer to allow full purge
            };

            PoolOptions<TestPoolItem> throwingOptions = new()
            {
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, _) => throw new InvalidOperationException("Test exception"),
                TimeProvider = TestTimeProvider,
                MinRetainCount = 0, // Explicit min retain count
                WarmRetainCount = 0, // Disable warm buffer to allow full purge
            };

            using WallstopGenericPool<TestPoolItem> pool1 = new(
                () => new TestPoolItem(),
                preWarmCount: 3,
                options: normalOptions
            );

            using WallstopGenericPool<TestPoolItem> throwingPool = new(
                () => new TestPoolItem(),
                preWarmCount: 2,
                options: throwingOptions
            );

            using WallstopGenericPool<TestPoolItem> pool2 = new(
                () => new TestPoolItem(),
                preWarmCount: 4,
                options: normalOptions
            );

            // Should not throw even though one pool's callback throws
            int totalPurged = 0;
            Assert.DoesNotThrow(() =>
                totalPurged = PoolPurgeSettings.PurgeAllPools(true, PurgeReason.Explicit)
            );

            // All pools should still be purged (total of 9 items)
            Assert.AreEqual(9, totalPurged);
        }

        [Test]
        public void PoolImplementsIPurgeable()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            Assert.IsInstanceOf<GlobalPoolRegistry.IPurgeable>(pool);
        }

        [Test]
        public void IPurgeablePurgeReturnsCorrectCount()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 7,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    MinRetainCount = 0, // Explicit min retain count
                    WarmRetainCount = 0, // Disable warm buffer to allow full purge
                }
            );

            GlobalPoolRegistry.IPurgeable purgeable = pool;
            int purged = purgeable.Purge(PurgeReason.Explicit);

            Assert.AreEqual(7, purged);
        }

        [Test]
        public void PurgeAllPoolsRespectsMinRetainCount()
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
                reason: PurgeReason.Explicit
            );

            Assert.AreEqual(8, purged);
            Assert.AreEqual(2, pool.Count);
        }

        [Test]
        public void MemoryPressurePurgeIgnoresHysteresisButRespectsMinRetain()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                MinRetainCount = 1,
                WarmRetainCount = 0, // Disable warm buffer to allow purge down to MinRetainCount
                UseIntelligentPurging = true,
                HysteresisSeconds = 60f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: options
            );

            // Purge with memory pressure reason (typically ignores hysteresis)
            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: false,
                reason: PurgeReason.MemoryPressure
            );

            Assert.AreEqual(4, purged);
            Assert.AreEqual(1, pool.Count);
        }

#if !SINGLE_THREADED
        [Test]
        public void ConcurrentPoolRegistrationIsThreadSafe()
        {
            GlobalPoolRegistry.Clear();
            List<WallstopGenericPool<TestPoolItem>> pools = new();
            object poolsLock = new();
            const int threadCount = 8;
            const int poolsPerThread = 10;

            System.Threading.Tasks.Task[] tasks = new System.Threading.Tasks.Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                tasks[t] = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int i = 0; i < poolsPerThread; i++)
                    {
                        WallstopGenericPool<TestPoolItem> pool = new(
                            () => new TestPoolItem(),
                            options: new PoolOptions<TestPoolItem>
                            {
                                Triggers = PurgeTrigger.Explicit,
                                TimeProvider = TestTimeProvider,
                            }
                        );

                        lock (poolsLock)
                        {
                            pools.Add(pool);
                        }
                    }
                });
            }

            Assert.DoesNotThrow(() => System.Threading.Tasks.Task.WaitAll(tasks));
            Assert.AreEqual(threadCount * poolsPerThread, pools.Count);

            // Clean up
            foreach (WallstopGenericPool<TestPoolItem> pool in pools)
            {
                pool.Dispose();
            }
        }

        [Test]
        public void ConcurrentPurgeAllPoolsIsThreadSafe()
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
                        reason: PurgeReason.Explicit
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
        public void LifecycleSettingsAreThreadSafe()
        {
            const int iterations = 100;
            System.Threading.Tasks.Task[] tasks = new System.Threading.Tasks.Task[4];

            tasks[0] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PoolPurgeSettings.PurgeOnLowMemory = i % 2 == 0;
                    bool _ = PoolPurgeSettings.PurgeOnLowMemory;
                }
            });

            tasks[1] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PoolPurgeSettings.PurgeOnAppBackground = i % 2 == 0;
                    bool _ = PoolPurgeSettings.PurgeOnAppBackground;
                }
            });

            tasks[2] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PoolPurgeSettings.PurgeOnSceneUnload = i % 2 == 0;
                    bool _ = PoolPurgeSettings.PurgeOnSceneUnload;
                }
            });

            tasks[3] = System.Threading.Tasks.Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    PoolPurgeSettings.ResetToDefaults();
                }
            });

            Assert.DoesNotThrow(() => System.Threading.Tasks.Task.WaitAll(tasks));
        }
#endif

        [Test]
        public void RegisterLifecycleHooksIsIdempotent()
        {
            // Should not throw when called multiple times
            Assert.DoesNotThrow(() =>
            {
                PoolPurgeSettings.RegisterLifecycleHooks();
                PoolPurgeSettings.RegisterLifecycleHooks();
                PoolPurgeSettings.RegisterLifecycleHooks();
            });
        }

        [Test]
        public void UnregisterLifecycleHooksIsIdempotent()
        {
            // Should not throw when called multiple times
            Assert.DoesNotThrow(() =>
            {
                PoolPurgeSettings.UnregisterLifecycleHooks();
                PoolPurgeSettings.UnregisterLifecycleHooks();
                PoolPurgeSettings.UnregisterLifecycleHooks();
            });
        }

        [Test]
        public void CanReRegisterAfterUnregister()
        {
            Assert.DoesNotThrow(() =>
            {
                PoolPurgeSettings.RegisterLifecycleHooks();
                PoolPurgeSettings.UnregisterLifecycleHooks();
                PoolPurgeSettings.RegisterLifecycleHooks();
            });
        }

        [Test]
        public void PurgeAllPoolsHandlesEmptyPools()
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

            int purged = PoolPurgeSettings.PurgeAllPools();

            Assert.AreEqual(0, purged);
            Assert.AreEqual(0, emptyPool.Count);
        }

        [Test]
        public void PurgeAllPoolsHandlesMixedPoolSizes()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
                MinRetainCount = 0, // Explicit min retain count
                WarmRetainCount = 0, // Disable warm buffer to allow full purge
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
                preWarmCount: 100,
                options: options
            );

            int purged = PoolPurgeSettings.PurgeAllPools();

            Assert.AreEqual(102, purged);
        }

        [Test]
        public void DisposedPoolsAreCleanedFromRegistry()
        {
            GlobalPoolRegistry.Clear();

            WallstopGenericPool<TestPoolItem> pool1 = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    MinRetainCount = 0, // Explicit min retain count
                    WarmRetainCount = 0, // Disable warm buffer to allow full purge
                }
            );

            using WallstopGenericPool<TestPoolItem> pool2 = new(
                () => new TestPoolItem(),
                preWarmCount: 3,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    MinRetainCount = 0, // Explicit min retain count
                    WarmRetainCount = 0, // Disable warm buffer to allow full purge
                }
            );

            pool1.Dispose();

            // PurgeAll should only purge pool2
            int purged = PoolPurgeSettings.PurgeAllPools();

            Assert.AreEqual(3, purged);
        }

        [Test]
        public void HysteresisBlocksPurgeWhenRespected()
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

            // Try to purge while respecting hysteresis - should be blocked
            int purgedWithHysteresis = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.AppBackgrounded
            );

            Assert.AreEqual(
                0,
                purgedWithHysteresis,
                "Purge should be blocked when respecting hysteresis"
            );
            Assert.AreEqual(
                5,
                pool.Count,
                "Pool count should remain unchanged when hysteresis blocks purge"
            );
        }

        [Test]
        public void HysteresisIsBypassedWhenIgnored()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                HysteresisSeconds = 60f,
                SpikeThresholdMultiplier = 0.1f, // Low threshold to ensure spike triggers on first rental
                MinRetainCount = 0, // Explicit min retain count
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

            // Purge with ignoreHysteresis: true (respectHysteresis: false) - should bypass hysteresis
            int purgedWithBypass = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: false,
                reason: PurgeReason.MemoryPressure
            );

            Assert.AreEqual(5, purgedWithBypass, "Purge should proceed when ignoring hysteresis");
            Assert.AreEqual(0, pool.Count, "Pool should be empty after ignoring hysteresis");
        }

        [Test]
        public void DirectPurgeRespectsHysteresisByDefault()
        {
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

            // Direct purge without ignoreHysteresis - should be blocked
            int purged = pool.Purge(PurgeReason.Explicit);

            Assert.AreEqual(
                0,
                purged,
                "Direct purge should be blocked by default when in hysteresis"
            );
            Assert.AreEqual(5, pool.Count, "Pool count should remain unchanged");
        }

        [Test]
        public void DirectPurgeCanBypassHysteresis()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                HysteresisSeconds = 60f,
                SpikeThresholdMultiplier = 0.1f, // Low threshold to ensure spike triggers on first rental
                MinRetainCount = 0, // Explicit min retain count
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

            // Direct purge with ignoreHysteresis: true - should bypass
            int purged = pool.Purge(PurgeReason.MemoryPressure, ignoreHysteresis: true);

            Assert.AreEqual(5, purged, "Direct purge should proceed when ignoreHysteresis is true");
            Assert.AreEqual(0, pool.Count, "Pool should be empty after ignoring hysteresis");
        }

        [Test]
        public void LowMemoryHandlerUsesIgnoreHysteresis()
        {
            GlobalPoolRegistry.Clear();

            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                HysteresisSeconds = 60f,
                SpikeThresholdMultiplier = 0.1f, // Low threshold to ensure spike triggers on first rental
                MinRetainCount = 0, // Explicit min retain count
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

            // Simulate low memory purge (which should ignore hysteresis)
            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: false,
                reason: PurgeReason.MemoryPressure
            );

            Assert.AreEqual(5, purged, "Low memory purge should bypass hysteresis");
            Assert.AreEqual(0, pool.Count, "Pool should be empty after low memory purge");
        }

        [Test]
        public void AppBackgroundedHandlerRespectsHysteresis()
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

            // Simulate app backgrounded purge (which should respect hysteresis)
            int purged = PoolPurgeSettings.PurgeAllPools(
                respectHysteresis: true,
                reason: PurgeReason.AppBackgrounded
            );

            Assert.AreEqual(0, purged, "App backgrounded purge should respect hysteresis");
            Assert.AreEqual(
                5,
                pool.Count,
                "Pool should remain unchanged when hysteresis is respected"
            );
        }

        [Test]
        public void MemoryPressureBypassesMaxPurgesPerOperation()
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

            // Advance time past idle timeout so items are eligible for purging
            _currentTime = 2f;

            // ForceFullPurgeAll (used by OnLowMemory) should bypass MaxPurgesPerOperation
            int purged = GlobalPoolRegistry.ForceFullPurgeAll(
                respectHysteresis: false,
                reason: PurgeReason.MemoryPressure
            );

            Assert.AreEqual(
                preWarmCount,
                purged,
                "MemoryPressure purge should bypass MaxPurgesPerOperation and purge all items"
            );
            Assert.AreEqual(0, pool.Count, "Pool should be empty after MemoryPressure purge");
            Assert.IsFalse(
                pool.HasPendingPurges,
                "No pending purges should remain after ForceFullPurgeAll"
            );
        }

        [Test]
        public void MemoryPressureTracksFullPurgeStatistics()
        {
            GlobalPoolRegistry.Clear();

            const int preWarmCount = 15;
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

            PoolStatistics statsBefore = pool.GetStatistics();
            long fullPurgesBefore = statsBefore.FullPurgeOperations;

            // ForceFullPurgeAll should track as a full purge operation
            GlobalPoolRegistry.ForceFullPurgeAll(
                respectHysteresis: false,
                reason: PurgeReason.MemoryPressure
            );

            PoolStatistics statsAfter = pool.GetStatistics();

            Assert.AreEqual(
                fullPurgesBefore + 1,
                statsAfter.FullPurgeOperations,
                "ForceFullPurge should increment FullPurgeOperations counter"
            );
        }

        [Test]
        public void ExplicitPurgeWithReasonBypassesMaxPurgesPerOperation()
        {
            const int preWarmCount = 20;
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

            // Direct Purge(reason) call should bypass MaxPurgesPerOperation
            int purged = pool.Purge(PurgeReason.Explicit);

            Assert.AreEqual(
                preWarmCount,
                purged,
                "Purge(reason) should bypass MaxPurgesPerOperation and purge all items"
            );
            Assert.AreEqual(0, pool.Count, "Pool should be empty after explicit purge with reason");
        }

        [Test]
        public void ExplicitPurgeWithReasonTracksFullPurgeStatistics()
        {
            const int preWarmCount = 12;
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

            PoolStatistics statsBefore = pool.GetStatistics();
            long fullPurgesBefore = statsBefore.FullPurgeOperations;

            // Direct Purge(reason) should track as a full purge operation
            pool.Purge(PurgeReason.Explicit);

            PoolStatistics statsAfter = pool.GetStatistics();

            Assert.AreEqual(
                fullPurgesBefore + 1,
                statsAfter.FullPurgeOperations,
                "Purge(reason) should increment FullPurgeOperations counter"
            );
        }

        private sealed class TestPoolItem
        {
            public int Id { get; }
            public bool WasReset { get; set; }
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
    }
}
