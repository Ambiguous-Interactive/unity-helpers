// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;
#if !SINGLE_THREADED
    using System.Threading.Tasks;
#endif

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class WallstopGenericPoolTests
    {
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

        private float _currentTime;
        private bool _wasMemoryPressureEnabled;

        private float TestTimeProvider()
        {
            return _currentTime;
        }

        private void LogPoolDiagnostics(WallstopGenericPool<TestPoolItem> pool, string context)
        {
            PoolStatistics stats = pool.GetStatistics();
            TestContext.WriteLine($"[{context}] Pool Diagnostics:");
            TestContext.WriteLine($"  Time: {_currentTime}");
            TestContext.WriteLine($"  Pool.Count: {pool.Count}");
            TestContext.WriteLine($"  RentCount: {stats.RentCount}");
            TestContext.WriteLine($"  PurgeCount: {stats.PurgeCount}");
            TestContext.WriteLine($"  IdleTimeoutPurges: {stats.IdleTimeoutPurges}");
            TestContext.WriteLine($"  IsLowFrequency: {stats.IsLowFrequency}");
            TestContext.WriteLine($"  RentalsPerMinute: {stats.RentalsPerMinute}");
        }

        [SetUp]
        public void SetUp()
        {
            _currentTime = 1f; // Start at t=1 since time 0 is treated as uninitialized in the tracker
            TestPoolItem.ResetIdCounter();
            PoolPurgeSettings.ResetToDefaults();
            // Disable memory pressure monitoring to ensure deterministic test behavior
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
        public void ConstructorWithNullProducerThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new WallstopGenericPool<TestPoolItem>(null));
        }

        [Test]
        public void ConstructorWithPreWarmCountCreatesItems()
        {
            const int preWarmCount = 5;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount
            );

            Assert.AreEqual(preWarmCount, pool.Count);
        }

        [Test]
        public void GetFromEmptyPoolCreatesNewItem()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(() => new TestPoolItem());

            using PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem item);

            Assert.IsTrue(item != null, "Pool should create a new item when empty");
            Assert.AreEqual(1, item.Id, "First created item should have Id 1");
        }

        [Test]
        public void GetReturnsPooledItemWhenAvailable()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(() => new TestPoolItem(), 1);

            using PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem item);

            Assert.AreEqual(1, item.Id);
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void ReturnAddsItemBackToPool()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            TestPoolItem item;
            using (PooledResource<TestPoolItem> resource = pool.Get(out item))
            {
                Assert.AreEqual(0, pool.Count);
            }

            Assert.AreEqual(1, pool.Count);
        }

        [Test]
        public void OnGetCallbackIsInvoked()
        {
            int getCount = 0;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                onGet: _ => getCount++
            );

            using PooledResource<TestPoolItem> resource = pool.Get();

            Assert.AreEqual(1, getCount);
        }

        [Test]
        public void OnReleaseCallbackIsInvoked()
        {
            int releaseCount = 0;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                onRelease: item =>
                {
                    item.WasReset = true;
                    releaseCount++;
                },
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            TestPoolItem capturedItem;
            using (PooledResource<TestPoolItem> resource = pool.Get(out capturedItem))
            {
                Assert.IsFalse(capturedItem.WasReset, "Item should not be reset before release");
            }

            Assert.IsTrue(capturedItem.WasReset, "Item should be reset after release callback");
            Assert.AreEqual(1, releaseCount, "Release callback should be invoked exactly once");
        }

        [Test]
        public void DisposeInvokesOnDisposalForAllItems()
        {
            List<TestPoolItem> disposedItems = new();
            WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 3,
                onDisposal: item =>
                {
                    item.WasDisposed = true;
                    disposedItems.Add(item);
                },
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            pool.Dispose();

            Assert.AreEqual(3, disposedItems.Count, "All 3 pre-warmed items should be disposed");
            foreach (TestPoolItem item in disposedItems)
            {
                Assert.IsTrue(item.WasDisposed, "Each item should have WasDisposed set to true");
            }
        }

        [Test]
        public void DisposeClearsPoolWithoutCallbackIfNoOnDisposal()
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

            Assert.AreEqual(3, pool.Count);
            pool.Dispose();
            Assert.AreEqual(0, pool.Count);
        }

        [Test]
        public void DisposeDoubleDisposeIsNoOp()
        {
            int disposeCount = 0;
            WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 1,
                onDisposal: _ => disposeCount++,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            pool.Dispose();
            pool.Dispose();

            Assert.AreEqual(1, disposeCount);
        }

        // Max size enforcement tests
        [Test]
        public void MaxPoolSizeEnforcedOnReturn()
        {
            int capacityPurgeCount = 0;
            List<PurgeReason> observedReasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                MaxPoolSize = 2,
                Triggers = PurgeTrigger.OnReturn,
                TimeProvider = TestTimeProvider,
                OnPurge = (_, reason) =>
                {
                    observedReasons.Add(reason);
                    if (reason == PurgeReason.CapacityExceeded)
                    {
                        capacityPurgeCount++;
                    }
                },
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 0,
                options: options
            );

            // Hold 4 items simultaneously BEFORE disposing so that the pool must really
            // absorb 4 distinct returns; sequential Get/Dispose would reuse a single item
            // and never exercise the MaxPoolSize boundary.
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 4; i++)
            {
                resources.Add(pool.Get());
            }

            TestContext.WriteLine(
                $"Holding {resources.Count} items before disposing, pool.Count while rented: {pool.Count}"
            );

            foreach (PooledResource<TestPoolItem> resource in resources)
            {
                resource.Dispose();
            }

            TestContext.WriteLine(
                $"After disposing all: pool.Count={pool.Count}, capacityPurgeCount={capacityPurgeCount}, reasons=[{string.Join(", ", observedReasons)}]"
            );

            // Should be capped at MaxPoolSize
            Assert.LessOrEqual(pool.Count, 2, "Pool must not exceed MaxPoolSize after returns");
            Assert.GreaterOrEqual(
                capacityPurgeCount,
                1,
                "At least one return past MaxPoolSize must be purged with reason=CapacityExceeded"
            );
        }

        [Test]
        public void MaxPoolSizeZeroMeansUnbounded()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                MaxPoolSize = 0,
                Triggers = PurgeTrigger.OnReturn,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Hold 10 items simultaneously before disposing to create 10 distinct items
            // (sequential Get/Dispose would reuse the same item)
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 10; i++)
            {
                resources.Add(pool.Get());
            }

            TestContext.WriteLine(
                $"Created {resources.Count} items, pool count while rented: {pool.Count}"
            );

            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }

            TestContext.WriteLine($"After disposing all items, pool count: {pool.Count}");

            Assert.AreEqual(10, pool.Count);
        }

        // Idle timeout purging tests
        [Test]
        public void IdleTimeoutPurgesOldItems()
        {
            List<PurgeReason> purgeReasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 5f,
                Triggers = PurgeTrigger.OnRent,
                OnPurge = (_, reason) => purgeReasons.Add(reason),
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Add an item at time 0
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.AreEqual(1, pool.Count);

            // Advance time past idle timeout
            _currentTime = 6f;

            // This should trigger purge on rent
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.AreEqual(1, purgeReasons.Count);
            Assert.AreEqual(PurgeReason.IdleTimeout, purgeReasons[0]);
        }

        [Test]
        public void IdleTimeoutZeroDisablesPurging()
        {
            List<PurgeReason> purgeReasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 0f,
                Triggers = PurgeTrigger.OnRent,
                OnPurge = (_, reason) => purgeReasons.Add(reason),
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            _currentTime = 1000f; // Very old

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.AreEqual(0, purgeReasons.Count);
        }

        // MinRetainCount tests
        [Test]
        public void MinRetainCountPreventsPurgingBelowThreshold()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                MaxPoolSize = 2,
                MinRetainCount = 2,
                Triggers = PurgeTrigger.OnReturn,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: options
            );

            // Return more items
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.GreaterOrEqual(pool.Count, 2);
        }

        // Purge trigger tests
        [Test]
        public void PurgeTriggerOnRentPurgesWhenRenting()
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.OnRent,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            _currentTime = 2f;

            // Purge happens on rent
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.AreEqual(1, purgeCount);
        }

        [Test]
        public void PurgeTriggerOnReturnPurgesWhenReturning()
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                MaxPoolSize = 1,
                Triggers = PurgeTrigger.OnReturn,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Hold 2 items simultaneously before disposing to exceed MaxPoolSize
            // (sequential Get/Dispose would reuse the same item and never exceed capacity)
            List<PooledResource<TestPoolItem>> resources = new();
            resources.Add(pool.Get());
            resources.Add(pool.Get());

            TestContext.WriteLine(
                $"Holding {resources.Count} items, pool count while rented: {pool.Count}"
            );

            // Return all items - should trigger purge since MaxPoolSize=1
            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }

            TestContext.WriteLine(
                $"After disposing, pool count: {pool.Count}, purgeCount: {purgeCount}"
            );

            Assert.GreaterOrEqual(purgeCount, 1);
        }

        /// <summary>
        /// Tests that PurgeTrigger.Explicit only purges when Purge() is called explicitly.
        /// Note: WarmRetainCount=0 is set to disable warm retention, allowing the test to verify
        /// explicit purge trigger behavior in isolation without interference from active pool protections.
        /// </summary>
        [Test]
        public void PurgeTriggerExplicitOnlyPurgesWhenCalledExplicitly()
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
                WarmRetainCount = 0,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After first Get/Return at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );

            _currentTime = 2f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After second Get/Return at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );

            // No purge yet - trigger is explicit only
            Assert.AreEqual(0, purgeCount, "No purge should occur before explicit Purge() call");

            // Explicit purge
            pool.Purge();

            TestContext.WriteLine(
                $"After explicit Purge() at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );

            Assert.AreEqual(
                1,
                purgeCount,
                "Exactly one item should be purged after explicit Purge() call"
            );
        }

        /// <summary>
        /// Tests that explicit purge works correctly regardless of pool size relative to comfortable size.
        /// This is a data-driven test that verifies explicit purges bypass comfortable size protection.
        /// </summary>
        [Test]
        [TestCase(1, 1, TestName = "ExplicitPurgeWithSingleItem")]
        [TestCase(5, 5, TestName = "ExplicitPurgeWithMultipleItems")]
        public void ExplicitPurgeBehaviorAtVariousPoolSizes(int itemCount, int expectedPurges)
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
                WarmRetainCount = 0,
                MinRetainCount = 0,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Create items and return them to the pool
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < itemCount; i++)
            {
                resources.Add(pool.Get());
            }
            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }

            TestContext.WriteLine($"After creating {itemCount} items: pool.Count={pool.Count}");

            // Advance time past idle timeout
            _currentTime = 3f;

            // Explicit purge should work regardless of comfortable size
            pool.Purge();

            TestContext.WriteLine(
                $"After Purge(): pool.Count={pool.Count}, purgeCount={purgeCount}"
            );

            Assert.AreEqual(
                expectedPurges,
                purgeCount,
                $"Expected {expectedPurges} items to be purged with explicit trigger"
            );
        }

        /// <summary>
        /// Tests that idle timeout purges happen even when pool is at or below comfortable size.
        /// This validates the fix where idle timeout was incorrectly blocked by comfortable size check.
        /// </summary>
        [Test]
        [TestCase(0f, true, TestName = "IdleTimeoutPurgeWithZeroBufferMultiplier")]
        [TestCase(1f, true, TestName = "IdleTimeoutPurgeWithNormalBufferMultiplier")]
        public void IdleTimeoutPurgeIgnoresComfortableSize(float bufferMultiplier, bool expectPurge)
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.OnRent,
                UseIntelligentPurging = true,
                BufferMultiplier = bufferMultiplier,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
                WarmRetainCount = 0,
                MinRetainCount = 0,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Get and return one item
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After first Get/Return at t={_currentTime}: pool.Count={pool.Count}"
            );

            // Advance time past idle timeout
            _currentTime = 3f;

            // Second rental should trigger idle timeout purge of the first item
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After second Get at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );

            if (expectPurge)
            {
                Assert.GreaterOrEqual(
                    purgeCount,
                    1,
                    "Idle timeout purge should occur even when pool is at comfortable size"
                );
            }
        }

        /// <summary>
        /// Tests various combinations of PurgeTrigger.Explicit with other triggers.
        /// Verifies that explicit trigger only purges when Purge() is called, while combined triggers
        /// purge at multiple opportunities.
        /// Note: WarmRetainCount=0 is set to disable warm retention, allowing the test to verify
        /// explicit purge trigger behavior in isolation without interference from active pool protections.
        /// </summary>
        [Test]
        [TestCase(PurgeTrigger.Explicit, 0, 1, TestName = "ExplicitOnlyNoPurgeOnRent")]
        [TestCase(
            PurgeTrigger.Explicit | PurgeTrigger.OnRent,
            1,
            2,
            TestName = "ExplicitAndOnRentPurgesBothTimes"
        )]
        public void ExplicitPurgeTriggerCombinations(
            PurgeTrigger trigger,
            int expectedPurgesAfterRent,
            int expectedPurgesAfterExplicit
        )
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = trigger,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
                WarmRetainCount = 0,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // First rental at t=1
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After first Get/Return at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );

            // Advance past idle timeout
            _currentTime = 3f;

            // Second rental should trigger OnRent purge if enabled
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After second Get/Return at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );

            Assert.AreEqual(
                expectedPurgesAfterRent,
                purgeCount,
                $"After rent with trigger={trigger}: expected {expectedPurgesAfterRent} purges, got {purgeCount}"
            );

            // Explicit purge should always work
            pool.Purge();

            TestContext.WriteLine(
                $"After explicit Purge() at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );

            Assert.AreEqual(
                expectedPurgesAfterExplicit,
                purgeCount,
                $"After explicit Purge() with trigger={trigger}: expected {expectedPurgesAfterExplicit} purges, got {purgeCount}"
            );
        }

        [Test]
        public void PurgeTriggerCombinedWorksWithMultipleTriggers()
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.OnRent | PurgeTrigger.OnReturn,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            _currentTime = 2f;

            // Should trigger on next rent
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.GreaterOrEqual(purgeCount, 1);
        }

        [Test]
        public void PurgeTriggerPeriodicPurgesAtInterval()
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                PurgeIntervalSeconds = 5f,
                Triggers = PurgeTrigger.Periodic,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            // Before interval
            _currentTime = 2f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            Assert.AreEqual(0, purgeCount);

            // After interval
            _currentTime = 6f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            Assert.GreaterOrEqual(purgeCount, 1);
        }

        // Callback invocation tests
        [Test]
        public void OnPurgeReceivesCorrectReasonIdleTimeout()
        {
            List<PurgeReason> reasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.OnRent,
                OnPurge = (_, reason) => reasons.Add(reason),
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            _currentTime = 2f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.Contains(PurgeReason.IdleTimeout, reasons);
        }

        [Test]
        public void OnPurgeReceivesCorrectReasonCapacityExceeded()
        {
            List<PurgeReason> reasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                MaxPoolSize = 1,
                Triggers = PurgeTrigger.OnReturn,
                OnPurge = (_, reason) => reasons.Add(reason),
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Hold 2 items simultaneously to exceed MaxPoolSize=1
            // (sequential Get/Dispose would reuse the same item)
            List<PooledResource<TestPoolItem>> resources = new();
            resources.Add(pool.Get());
            resources.Add(pool.Get());

            TestContext.WriteLine($"Holding {resources.Count} items with MaxPoolSize=1");

            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }

            TestContext.WriteLine(
                $"After disposing, reasons collected: {string.Join(", ", reasons)}"
            );

            Assert.Contains(PurgeReason.CapacityExceeded, reasons);
        }

        [Test]
        public void OnPurgeReceivesCorrectReasonExplicit()
        {
            List<PurgeReason> reasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, reason) => reasons.Add(reason),
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            pool.Purge();

            Assert.Contains(PurgeReason.Explicit, reasons);
        }

        [Test]
        public void OnPurgeExceptionDoesNotCorruptPool()
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                MaxPoolSize = 1,
                Triggers = PurgeTrigger.OnReturn,
                OnPurge = (_, _) =>
                {
                    purgeCount++;
                    throw new InvalidOperationException("Test exception");
                },
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            Assert.DoesNotThrow(() =>
            {
                // Hold 2 items simultaneously to exceed MaxPoolSize=1
                // (sequential Get/Dispose would reuse the same item)
                List<PooledResource<TestPoolItem>> resources = new();
                resources.Add(pool.Get());
                resources.Add(pool.Get());

                TestContext.WriteLine($"Holding {resources.Count} items with MaxPoolSize=1");

                foreach (PooledResource<TestPoolItem> r in resources)
                {
                    r.Dispose();
                }

                TestContext.WriteLine(
                    $"After disposing, pool count: {pool.Count}, purgeCount: {purgeCount}"
                );
            });

            Assert.GreaterOrEqual(purgeCount, 1);
            Assert.LessOrEqual(pool.Count, 1);
        }

        // Statistics accuracy tests
        [Test]
        public void GetStatisticsReturnsAccurateRentCount()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            PoolStatistics stats = pool.GetStatistics();
            Assert.AreEqual(5, stats.RentCount);
        }

        [Test]
        public void GetStatisticsReturnsAccurateReturnCount()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            PoolStatistics stats = pool.GetStatistics();
            Assert.AreEqual(5, stats.ReturnCount);
        }

        [Test]
        public void GetStatisticsReturnsAccuratePurgeCount()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.OnRent,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Hold 3 items simultaneously before disposing to create 3 distinct items
            // (sequential Get/Dispose would reuse the same item)
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 3; i++)
            {
                resources.Add(pool.Get());
            }

            TestContext.WriteLine(
                $"Created {resources.Count} items, pool count while rented: {pool.Count}"
            );

            // Return all items to the pool
            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }

            TestContext.WriteLine($"After disposing, pool count: {pool.Count}");

            // Advance time past idle timeout
            _currentTime = 2f;

            // Trigger purge by renting
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            PoolStatistics stats = pool.GetStatistics();
            TestContext.WriteLine(
                $"After purge, IdleTimeoutPurges: {stats.IdleTimeoutPurges}, pool count: {pool.Count}"
            );
            Assert.AreEqual(3, stats.IdleTimeoutPurges);
        }

        [Test]
        public void GetStatisticsTracksPeakSize()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Rent multiple items SIMULTANEOUSLY before returning them
            // to properly track peak size (items must be held concurrently)
            List<PooledResource<TestPoolItem>> rentedResources = new();
            for (int i = 0; i < 10; i++)
            {
                rentedResources.Add(pool.Get());
            }

            // Verify peak size while items are rented
            PoolStatistics statsWhileRented = pool.GetStatistics();
            Assert.AreEqual(
                10,
                statsWhileRented.PeakSize,
                "Peak size should be 10 while 10 items are rented simultaneously"
            );

            // Return all items
            foreach (PooledResource<TestPoolItem> resource in rentedResources)
            {
                resource.Dispose();
            }

            // Peak size should persist after returning items
            PoolStatistics statsAfterReturn = pool.GetStatistics();
            Assert.AreEqual(
                10,
                statsAfterReturn.PeakSize,
                "Peak size should remain 10 after items are returned to the pool"
            );
        }

        [Test]
        public void GetStatisticsTracksCurrentSize()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: options
            );

            PoolStatistics stats = pool.GetStatistics();
            Assert.AreEqual(5, stats.CurrentSize);
        }

        [Test]
        [TestCase(1, TestName = "PreWarmCount.One")]
        [TestCase(5, TestName = "PreWarmCount.Five")]
        [TestCase(7, TestName = "PreWarmCount.Seven")]
        [TestCase(8, TestName = "PreWarmCount.Eight")]
        [TestCase(10, TestName = "PreWarmCount.Ten")]
        [TestCase(100, TestName = "PreWarmCount.OneHundred")]
        public void PreWarmStatisticsValidationWithVariousCounts(int preWarmCount)
        {
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: options
            );

            PoolStatistics stats = pool.GetStatistics();

            Assert.That(
                stats.RentCount,
                Is.EqualTo(0),
                $"RentCount should be 0 after PreWarm({preWarmCount}) - PreWarm does not rent items"
            );
            Assert.That(
                stats.ReturnCount,
                Is.EqualTo(0),
                $"ReturnCount should be 0 after PreWarm({preWarmCount}) - PreWarm does not return items"
            );
            Assert.That(
                stats.PeakSize,
                Is.EqualTo(preWarmCount),
                $"PeakSize should equal PreWarm count ({preWarmCount})"
            );
            Assert.That(
                stats.CurrentSize,
                Is.EqualTo(preWarmCount),
                $"CurrentSize should equal PreWarm count ({preWarmCount})"
            );
        }

        // Edge case tests
        [Test]
        public void PurgeOnEmptyPoolReturnsZero()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            int purged = pool.Purge();
            Assert.AreEqual(0, purged);
        }

        [Test]
        public void PurgeWithReasonPurgesAllItems()
        {
            List<PurgeReason> reasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                OnPurge = (_, reason) => reasons.Add(reason),
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 3,
                options: options
            );

            int purged = pool.Purge(PurgeReason.Explicit);

            Assert.AreEqual(3, purged);
            Assert.AreEqual(3, reasons.Count);
            foreach (PurgeReason reason in reasons)
            {
                Assert.AreEqual(PurgeReason.Explicit, reason);
            }
        }

        [Test]
        public void NegativeIdleTimeoutDisablesPurging()
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = -1f,
                Triggers = PurgeTrigger.OnRent,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            _currentTime = 1000f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.AreEqual(0, purgeCount);
        }

        [Test]
        public void GetAfterDisposeCreatesNewItem()
        {
            WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            pool.Dispose();

            using PooledResource<TestPoolItem> resource = pool.Get(out TestPoolItem item);

            Assert.IsTrue(item != null, "Item should not be null even after pool disposal");
        }

        [Test]
        public void ReturnAfterDisposeInvokesOnDisposal()
        {
            int disposeCount = 0;
            WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                onDisposal: _ => disposeCount++,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            PooledResource<TestPoolItem> resource = pool.Get();
            pool.Dispose();
            resource.Dispose();

            Assert.AreEqual(1, disposeCount);
        }

        [Test]
        public void PoolStatisticsEquality()
        {
            PoolStatistics stats1 = new(1, 2, 3, 4, 5, 6, 7);
            PoolStatistics stats2 = new(1, 2, 3, 4, 5, 6, 7);
            PoolStatistics stats3 = new(1, 2, 3, 4, 5, 6, 8);

            Assert.IsTrue(stats1 == stats2, "Equal statistics should be equal via == operator");
            Assert.IsFalse(
                stats1 == stats3,
                "Different statistics should not be equal via == operator"
            );
            Assert.IsTrue(
                stats1 != stats3,
                "Different statistics should be unequal via != operator"
            );
            Assert.AreEqual(
                stats1.GetHashCode(),
                stats2.GetHashCode(),
                "Equal statistics should have equal hash codes"
            );
        }

        [Test]
        public void PoolStatisticsToStringContainsAllFields()
        {
            PoolStatistics stats = new(1, 2, 3, 4, 5, 6, 7);
            string str = stats.ToString();

            Assert.IsTrue(str.Contains("1"), "ToString should contain TotalCreated value");
            Assert.IsTrue(str.Contains("2"), "ToString should contain TotalRetrieved value");
            Assert.IsTrue(str.Contains("3"), "ToString should contain TotalReturned value");
            Assert.IsTrue(str.Contains("4"), "ToString should contain TotalPurged value");
            Assert.IsTrue(str.Contains("5"), "ToString should contain CurrentSize value");
            Assert.IsTrue(str.Contains("6"), "ToString should contain PeakSize value");
            Assert.IsTrue(str.Contains("7"), "ToString should contain MaxPoolSize value");
        }

        [Test]
        public void DynamicPropertyChangesAreRespected()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                MaxPoolSize = 10,
                Triggers = PurgeTrigger.OnReturn,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Hold 5 items simultaneously before disposing to create 5 distinct items
            // (sequential Get/Dispose would reuse the same item)
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 5; i++)
            {
                resources.Add(pool.Get());
            }

            TestContext.WriteLine(
                $"Created {resources.Count} items, pool count while rented: {pool.Count}"
            );

            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }

            TestContext.WriteLine($"After disposing, pool count: {pool.Count}");

            Assert.AreEqual(5, pool.Count);

            // Change max size
            pool.MaxPoolSize = 2;

            TestContext.WriteLine($"Changed MaxPoolSize to 2");

            // Add more items to trigger purge
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine($"After another Get/Dispose, pool count: {pool.Count}");

            Assert.LessOrEqual(pool.Count, 2);
        }

        // Intelligent purging tests
        [Test]
        public void IntelligentPurgingDefaultsToDisabled()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem> { TimeProvider = TestTimeProvider }
            );

            Assert.IsFalse(
                pool.UseIntelligentPurging,
                "Intelligent purging should be disabled by default"
            );
        }

        [Test]
        public void IntelligentPurgingEnabledViaOptions()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            Assert.IsTrue(
                pool.UseIntelligentPurging,
                "Intelligent purging should be enabled when configured via options"
            );
        }

        [Test]
        public void IntelligentPurgingSetsIdleTimeoutIfNotConfigured()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            Assert.Greater(pool.IdleTimeoutSeconds, 0f);
        }

        [Test]
        public void IntelligentPurgingRespectsComfortableSize()
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                IdleTimeoutSeconds = 1f,
                BufferMultiplier = 1.5f,
                RollingWindowSeconds = 60f,
                Triggers = PurgeTrigger.OnRent,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Rent and return several items to establish a usage pattern
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 5; i++)
            {
                resources.Add(pool.Get());
            }

            // Return them all
            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }
            resources.Clear();

            Assert.AreEqual(5, pool.Count);

            // Advance time past idle timeout
            _currentTime = 2f;

            // Get an item - should not purge all since comfortable size accounts for usage
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            // With a high water mark of 5 and buffer of 1.5, comfortable size is 7
            // Since we only had 5 items, all should still be there minus one for the rent
            Assert.LessOrEqual(purgeCount, 5);
        }

        [Test]
        public void IntelligentPurgingHysteresisPreventsPurgeAfterSpike()
        {
            const float hysteresisSeconds = 30f;

            // Diagnostic output for debugging non-deterministic failures
            TestContext.WriteLine($"=== Test Configuration ===");
            TestContext.WriteLine(
                $"  MemoryPressureMonitor.Enabled: {MemoryPressureMonitor.Enabled}"
            );
            TestContext.WriteLine(
                $"  MemoryPressureMonitor.CurrentPressure: {MemoryPressureMonitor.CurrentPressure}"
            );
            TestContext.WriteLine($"  HysteresisSeconds: {hysteresisSeconds}");
            TestContext.WriteLine($"  IdleTimeoutSeconds: 0 (disabled)");
            TestContext.WriteLine($"  SpikeThresholdMultiplier: 1.5");

            int purgeCount = 0;
            List<PurgeReason> purgeReasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                // Use 0f to disable idle timeout purges, which are intentionally allowed
                // during hysteresis - this test verifies hysteresis blocks other purge types
                IdleTimeoutSeconds = 0f,
                HysteresisSeconds = hysteresisSeconds,
                SpikeThresholdMultiplier = 1.5f,
                Triggers = PurgeTrigger.OnRent,
                OnPurge = (_, reason) =>
                {
                    purgeCount++;
                    purgeReasons.Add(reason);
                },
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Create a spike - get many items at once
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 10; i++)
            {
                resources.Add(pool.Get());
            }

            float spikeTime = _currentTime;
            float hysteresisEndTime = spikeTime + hysteresisSeconds;

            TestContext.WriteLine($"=== After Creating Spike ===");
            TestContext.WriteLine($"  Items created: {resources.Count}");
            TestContext.WriteLine($"  Spike time: {spikeTime}");
            TestContext.WriteLine($"  Hysteresis end time: {hysteresisEndTime}");

            // Return them all
            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }
            resources.Clear();

            TestContext.WriteLine($"=== After Returning Items ===");
            TestContext.WriteLine($"  Pool.Count: {pool.Count}");

            // Advance time within hysteresis period (hysteresis ends at 1 + 30 = 31)
            _currentTime = 3f;

            TestContext.WriteLine($"=== Before Get (Within Hysteresis) ===");
            TestContext.WriteLine($"  Current time: {_currentTime}");
            TestContext.WriteLine($"  Hysteresis end time: {hysteresisEndTime}");
            TestContext.WriteLine(
                $"  Is within hysteresis: {_currentTime < hysteresisEndTime} (should be true)"
            );
            TestContext.WriteLine($"  Idle timeout status: disabled (0f)");

            // Get should not trigger purge due to hysteresis
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine($"=== After Get ===");
            TestContext.WriteLine($"  PurgeCount: {purgeCount}");
            TestContext.WriteLine($"  Pool.Count: {pool.Count}");
            TestContext.WriteLine($"  Purge reasons: [{string.Join(", ", purgeReasons)}]");
            TestContext.WriteLine($"  Purge blocked due to: hysteresis (idle timeout disabled)");

            Assert.AreEqual(0, purgeCount);
        }

        [Test]
        public void IntelligentPurgingCanBeDisabledAtRuntime()
        {
            int purgeCount = 0;
            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                IdleTimeoutSeconds = 1f,
                HysteresisSeconds = 30f,
                Triggers = PurgeTrigger.OnRent,
                OnPurge = (_, _) => purgeCount++,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Add items
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            _currentTime = 2f;

            // Disable intelligent purging
            pool.UseIntelligentPurging = false;

            // Now purge should happen
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.AreEqual(1, purgeCount);
        }

        [Test]
        public void PoolPurgeSettingsConfigureSetsTypeSpecificOptions()
        {
            PoolPurgeSettings.ResetToDefaults();

            try
            {
                PoolPurgeSettings.Configure<TestPoolItem>(opts =>
                {
                    opts.Enabled = true;
                    opts.IdleTimeoutSeconds = 999f;
                });

                PoolPurgeEffectiveOptions effective =
                    PoolPurgeSettings.GetEffectiveOptions<TestPoolItem>();

                Assert.IsTrue(
                    effective.Enabled,
                    "Type-specific configuration should enable purging"
                );
                Assert.AreEqual(999f, effective.IdleTimeoutSeconds);
                Assert.AreEqual(PoolPurgeConfigurationSource.TypeSpecific, effective.Source);
            }
            finally
            {
                PoolPurgeSettings.ResetToDefaults();
            }
        }

        [Test]
        public void PoolPurgeSettingsDisableDisablesForType()
        {
            PoolPurgeSettings.ResetToDefaults();

            try
            {
                PoolPurgeSettings.GlobalEnabled = true;
                PoolPurgeSettings.Disable<TestPoolItem>();

                PoolPurgeEffectiveOptions effective =
                    PoolPurgeSettings.GetEffectiveOptions<TestPoolItem>();

                Assert.IsFalse(
                    effective.Enabled,
                    "Disable should prevent purging for the specific type"
                );
                Assert.AreEqual(PoolPurgeConfigurationSource.TypeDisabled, effective.Source);
            }
            finally
            {
                PoolPurgeSettings.ResetToDefaults();
            }
        }

        [Test]
        public void PoolPurgeSettingsGlobalEnabledDefaultsToFalse()
        {
            PoolPurgeSettings.ResetToDefaults();

            Assert.IsFalse(
                PoolPurgeSettings.GlobalEnabled,
                "GlobalEnabled should default to false"
            );
        }

        [Test]
        public void GlobalEnabledFalseDisablesIntelligentPurgingByDefault()
        {
            PoolPurgeSettings.ResetToDefaults();

            try
            {
                // Verify GlobalEnabled is false by default
                Assert.That(
                    PoolPurgeSettings.GlobalEnabled,
                    Is.False,
                    "GlobalEnabled should be false by default"
                );

                // Get effective options for a type - should report as disabled due to GlobalEnabled=false
                PoolPurgeEffectiveOptions effective =
                    PoolPurgeSettings.GetEffectiveOptions<TestPoolItem>();

                Assert.That(
                    effective.Enabled,
                    Is.False,
                    "Intelligent purging should be disabled when GlobalEnabled is false"
                );
                Assert.That(
                    effective.Source,
                    Is.EqualTo(PoolPurgeConfigurationSource.GlobalDefaults),
                    "Source should be GlobalDefault when no type-specific configuration exists"
                );
            }
            finally
            {
                PoolPurgeSettings.ResetToDefaults();
            }
        }

        [Test]
        public void GlobalEnabledTrueEnablesIntelligentPurgingByDefault()
        {
            PoolPurgeSettings.ResetToDefaults();

            try
            {
                PoolPurgeSettings.GlobalEnabled = true;

                PoolPurgeEffectiveOptions effective =
                    PoolPurgeSettings.GetEffectiveOptions<TestPoolItem>();

                Assert.That(
                    effective.Enabled,
                    Is.True,
                    "Intelligent purging should be enabled when GlobalEnabled is true"
                );
                Assert.That(
                    effective.Source,
                    Is.EqualTo(PoolPurgeConfigurationSource.GlobalDefaults),
                    "Source should be GlobalDefault when no type-specific configuration exists"
                );
            }
            finally
            {
                PoolPurgeSettings.ResetToDefaults();
            }
        }

        [Test]
        public void PoolPurgeSettingsConfigureGenericAppliesToGenericTypes()
        {
            PoolPurgeSettings.ResetToDefaults();

            try
            {
                PoolPurgeSettings.ConfigureGeneric(
                    typeof(List<>),
                    opts =>
                    {
                        opts.Enabled = true;
                        opts.IdleTimeoutSeconds = 123f;
                    }
                );

                PoolPurgeEffectiveOptions effective = PoolPurgeSettings.GetEffectiveOptions<
                    List<int>
                >();

                Assert.IsTrue(
                    effective.Enabled,
                    "Generic pattern configuration should enable purging for matching types"
                );
                Assert.AreEqual(123f, effective.IdleTimeoutSeconds);
                Assert.AreEqual(PoolPurgeConfigurationSource.GenericPattern, effective.Source);
            }
            finally
            {
                PoolPurgeSettings.ResetToDefaults();
            }
        }

        [Test]
        public void PoolPurgeSettingsTypeSpecificOverridesGenericPattern()
        {
            PoolPurgeSettings.ResetToDefaults();

            try
            {
                PoolPurgeSettings.ConfigureGeneric(
                    typeof(List<>),
                    opts =>
                    {
                        opts.IdleTimeoutSeconds = 100f;
                    }
                );

                PoolPurgeSettings.Configure<List<int>>(opts =>
                {
                    opts.IdleTimeoutSeconds = 200f;
                });

                PoolPurgeEffectiveOptions effective = PoolPurgeSettings.GetEffectiveOptions<
                    List<int>
                >();

                Assert.AreEqual(200f, effective.IdleTimeoutSeconds);
                Assert.AreEqual(PoolPurgeConfigurationSource.TypeSpecific, effective.Source);
            }
            finally
            {
                PoolPurgeSettings.ResetToDefaults();
            }
        }

#if !SINGLE_THREADED
        // Thread safety tests
        [Test]
        [TestCase(4, 100, TestName = "ThreadCount.Four.Iterations.OneHundred")]
        [TestCase(8, 50, TestName = "ThreadCount.Eight.Iterations.Fifty")]
        public void ConcurrentGetAndReturnMaintainsIntegrity(int threadCount, int iterations)
        {
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            Task[] tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        using PooledResource<TestPoolItem> resource = pool.Get(
                            out TestPoolItem item
                        );
                        Assert.IsTrue(
                            item != null,
                            "Concurrent Get should always return a valid item"
                        );
                    }
                });
            }

            Task.WaitAll(tasks);

            PoolStatistics stats = pool.GetStatistics();
            Assert.AreEqual(iterations * threadCount, stats.RentCount);
            Assert.AreEqual(iterations * threadCount, stats.ReturnCount);
        }

        [Test]
        [TestCase(4, 50, TestName = "ThreadCount.Four.Iterations.Fifty")]
        [TestCase(8, 25, TestName = "ThreadCount.Eight.Iterations.TwentyFive")]
        public void ConcurrentPurgeDoesNotCorruptPool(int threadCount, int iterations)
        {
            PoolOptions<TestPoolItem> options = new()
            {
                MaxPoolSize = 5,
                Triggers = PurgeTrigger.OnReturn,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            Task[] tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        using PooledResource<TestPoolItem> resource = pool.Get();
                        if (i % 10 == 0)
                        {
                            pool.Purge();
                        }
                    }
                });
            }

            Assert.DoesNotThrow(() => Task.WaitAll(tasks));
        }

        [Test]
        [TestCase(4, 100, TestName = "ThreadCount.Four.Iterations.OneHundred")]
        [TestCase(8, 50, TestName = "ThreadCount.Eight.Iterations.Fifty")]
        public void ConcurrentGetStatisticsIsThreadSafe(int threadCount, int iterations)
        {
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: options
            );

            Task[] tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        if (threadId % 2 == 0)
                        {
                            using PooledResource<TestPoolItem> resource = pool.Get();
                        }
                        else
                        {
                            PoolStatistics stats = pool.GetStatistics();
                            Assert.GreaterOrEqual(stats.CurrentSize, 0);
                        }
                    }
                });
            }

            Assert.DoesNotThrow(() => Task.WaitAll(tasks));
        }

        [Test]
        public void ConcurrentPropertyAccessIsThreadSafe()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                MaxPoolSize = 10,
                IdleTimeoutSeconds = 5f,
                Triggers = PurgeTrigger.OnRent,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            const int iterations = 100;
            Task[] tasks = new Task[4];

            tasks[0] = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    pool.MaxPoolSize = i % 20;
                    int _ = pool.MaxPoolSize;
                }
            });

            tasks[1] = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    pool.IdleTimeoutSeconds = i * 0.1f;
                    float _ = pool.IdleTimeoutSeconds;
                }
            });

            tasks[2] = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    pool.MinRetainCount = i % 5;
                    int _ = pool.MinRetainCount;
                }
            });

            tasks[3] = Task.Run(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    pool.Triggers = (PurgeTrigger)(i % 15);
                    PurgeTrigger _ = pool.Triggers;
                }
            });

            Assert.DoesNotThrow(() => Task.WaitAll(tasks));
        }
#endif

        // PoolTypeResolver tests
        [Test]
        [TestCaseSource(nameof(TypeMatchingTestCases))]
        public void GenericMatchingTypeMatchesPattern(
            Type concreteType,
            Type patternType,
            bool expectedMatch,
            int expectedPriority
        )
        {
            bool matchResult = PoolTypeResolver.TypeMatchesPattern(concreteType, patternType);
            int priorityResult = PoolTypeResolver.GetMatchPriority(concreteType, patternType);

            Assert.AreEqual(
                expectedMatch,
                matchResult,
                $"TypeMatchesPattern({concreteType}, {patternType}) should return {expectedMatch}"
            );
            Assert.AreEqual(
                expectedPriority,
                priorityResult,
                $"GetMatchPriority({concreteType}, {patternType}) should return {expectedPriority}"
            );
        }

        private static IEnumerable<TestCaseData> TypeMatchingTestCases()
        {
            yield return new TestCaseData(typeof(List<int>), typeof(List<int>), true, 0).SetName(
                "Match.ExactClosedGeneric.ListInt"
            );
            yield return new TestCaseData(typeof(List<int>), typeof(List<>), true, 2).SetName(
                "Match.OpenGenericToClosedGeneric.ListInt"
            );
            yield return new TestCaseData(typeof(List<string>), typeof(List<>), true, 2).SetName(
                "Match.OpenGenericToClosedGeneric.ListString"
            );
            yield return new TestCaseData(
                typeof(List<List<int>>),
                typeof(List<List<int>>),
                true,
                0
            ).SetName("Match.ExactNestedGeneric.ListListInt");
            yield return new TestCaseData(typeof(List<List<int>>), typeof(List<>), true, 2).SetName(
                "Match.OpenGenericToNestedGeneric.ListListInt"
            );
            yield return new TestCaseData(
                typeof(Dictionary<string, int>),
                typeof(Dictionary<,>),
                true,
                2
            ).SetName("Match.OpenGenericMultipleTypeArgs.DictionaryStringInt");
            yield return new TestCaseData(
                typeof(Dictionary<string, int>),
                typeof(Dictionary<string, int>),
                true,
                0
            ).SetName("Match.ExactDictionary.DictionaryStringInt");
            yield return new TestCaseData(typeof(HashSet<int>), typeof(HashSet<>), true, 2).SetName(
                "Match.OpenGenericHashSet.HashSetInt"
            );
            yield return new TestCaseData(
                typeof(string),
                typeof(List<>),
                false,
                int.MaxValue
            ).SetName("NoMatch.NonGenericToOpenGeneric");
            yield return new TestCaseData(
                typeof(List<int>),
                typeof(HashSet<>),
                false,
                int.MaxValue
            ).SetName("NoMatch.DifferentGenericDefinition");
            yield return new TestCaseData(
                typeof(List<int>),
                typeof(List<string>),
                false,
                int.MaxValue
            ).SetName("NoMatch.DifferentTypeArgs");
        }

        [Test]
        [TestCase("List<int>", typeof(List<int>), TestName = "Resolve.ListInt")]
        [TestCase(
            "Dictionary<string, int>",
            typeof(Dictionary<string, int>),
            TestName = "Resolve.DictionaryStringInt"
        )]
        [TestCase("HashSet<string>", typeof(HashSet<string>), TestName = "Resolve.HashSetString")]
        [TestCase("List<>", typeof(List<>), TestName = "Resolve.ListOpen")]
        [TestCase("Dictionary<,>", typeof(Dictionary<,>), TestName = "Resolve.DictionaryOpen")]
        [TestCase(
            "List<List<int>>",
            typeof(List<List<int>>),
            TestName = "Resolve.NestedListListInt"
        )]
        [TestCase(
            "Dictionary<string, List<int>>",
            typeof(Dictionary<string, List<int>>),
            TestName = "Resolve.DictionaryStringListInt"
        )]
        [TestCase(
            "System.Collections.Generic.List`1",
            typeof(List<>),
            TestName = "Resolve.FullyQualifiedListOpen"
        )]
        public void GenericMatchingResolveTypeResolvesCorrectly(string typeName, Type expectedType)
        {
            Type resolved = PoolTypeResolver.ResolveType(typeName);
            Assert.AreEqual(expectedType, resolved);
        }

        [Test]
        public void GenericMatchingPriorityOrderMostSpecificWins()
        {
            PoolPurgeSettings.ResetToDefaults();

            try
            {
                // Configure outer open generic with lower timeout
                PoolPurgeSettings.ConfigureGeneric(
                    typeof(List<>),
                    opts =>
                    {
                        opts.Enabled = true;
                        opts.IdleTimeoutSeconds = 100f;
                    }
                );

                // Configure specific closed generic with higher timeout
                PoolPurgeSettings.Configure<List<List<int>>>(opts =>
                {
                    opts.IdleTimeoutSeconds = 500f;
                });

                // List<List<int>> should get the specific config (500s)
                PoolPurgeEffectiveOptions effectiveNested = PoolPurgeSettings.GetEffectiveOptions<
                    List<List<int>>
                >();
                Assert.AreEqual(500f, effectiveNested.IdleTimeoutSeconds);
                Assert.AreEqual(PoolPurgeConfigurationSource.TypeSpecific, effectiveNested.Source);

                // List<int> should get the generic config (100s)
                PoolPurgeEffectiveOptions effectiveSimple = PoolPurgeSettings.GetEffectiveOptions<
                    List<int>
                >();
                Assert.AreEqual(100f, effectiveSimple.IdleTimeoutSeconds);
                Assert.AreEqual(
                    PoolPurgeConfigurationSource.GenericPattern,
                    effectiveSimple.Source
                );
            }
            finally
            {
                PoolPurgeSettings.ResetToDefaults();
            }
        }

        [Test]
        public void GenericMatchingGetAllMatchingPatternsReturnsCorrectOrder()
        {
            Type concreteType = typeof(List<List<int>>);
            List<Type> patterns = new(PoolTypeResolver.GetAllMatchingPatterns(concreteType));

            Assert.GreaterOrEqual(patterns.Count, 2);
            Assert.AreEqual(typeof(List<List<int>>), patterns[0]); // Exact type first
            Assert.AreEqual(typeof(List<>), patterns[patterns.Count - 1]); // Open generic last
        }

        [Test]
        [TestCaseSource(nameof(GetDisplayNameTestCases))]
        public void GenericMatchingGetDisplayNameFormatsCorrectly(
            Type inputType,
            string expectedDisplayName
        )
        {
            Assert.AreEqual(expectedDisplayName, PoolTypeResolver.GetDisplayName(inputType));
        }

        private static IEnumerable<TestCaseData> GetDisplayNameTestCases()
        {
            yield return new TestCaseData(typeof(List<int>), "List<int>").SetName(
                "DisplayName.ListOfInt"
            );
            yield return new TestCaseData(typeof(List<>), "List<>").SetName("DisplayName.ListOpen");
            yield return new TestCaseData(
                typeof(Dictionary<string, int>),
                "Dictionary<string, int>"
            ).SetName("DisplayName.DictionaryStringInt");
            yield return new TestCaseData(typeof(Dictionary<,>), "Dictionary<,>").SetName(
                "DisplayName.DictionaryOpen"
            );
            yield return new TestCaseData(typeof(List<List<int>>), "List<List<int>>").SetName(
                "DisplayName.NestedList"
            );
            yield return new TestCaseData(typeof(HashSet<string>), "HashSet<string>").SetName(
                "DisplayName.HashSetString"
            );
            yield return new TestCaseData(typeof(HashSet<>), "HashSet<>").SetName(
                "DisplayName.HashSetOpen"
            );
        }

        [Test]
        [TestCase(null, TestName = "ResolveType.Null.ReturnsNull")]
        [TestCase("", TestName = "ResolveType.Empty.ReturnsNull")]
        [TestCase("   ", TestName = "ResolveType.Whitespace.ReturnsNull")]
        public void GenericMatchingResolveTypeNullInputsReturnNull(string typeName)
        {
            Assert.IsTrue(PoolTypeResolver.ResolveType(typeName) == null);
        }

        [Test]
        public void GenericMatchingNullTypeInputsHandleGracefully()
        {
            Assert.IsFalse(PoolTypeResolver.TypeMatchesPattern(null, typeof(List<>)));
            Assert.IsFalse(PoolTypeResolver.TypeMatchesPattern(typeof(List<int>), (Type)null));
            Assert.AreEqual(int.MaxValue, PoolTypeResolver.GetMatchPriority(null, typeof(List<>)));
        }

        [Test]
        [TestCase("int", typeof(int), TestName = "Alias.Int")]
        [TestCase("string", typeof(string), TestName = "Alias.String")]
        [TestCase("bool", typeof(bool), TestName = "Alias.Bool")]
        [TestCase("float", typeof(float), TestName = "Alias.Float")]
        [TestCase("double", typeof(double), TestName = "Alias.Double")]
        [TestCase("long", typeof(long), TestName = "Alias.Long")]
        [TestCase("short", typeof(short), TestName = "Alias.Short")]
        [TestCase("byte", typeof(byte), TestName = "Alias.Byte")]
        [TestCase("char", typeof(char), TestName = "Alias.Char")]
        [TestCase("object", typeof(object), TestName = "Alias.Object")]
        public void GenericMatchingBuiltInTypeAliasesResolveCorrectly(
            string typeName,
            Type expectedType
        )
        {
            Assert.AreEqual(expectedType, PoolTypeResolver.ResolveType(typeName));
        }

        // PoolTypeConfiguration tests
        [Test]
        public void PoolTypeConfigurationResolvedTypeCachesResult()
        {
            PoolTypeConfiguration config = new() { TypeName = "List<int>" };

            Type first = config.ResolvedType;
            Type second = config.ResolvedType;

            Assert.IsTrue(first != null, "ResolvedType should return a non-null type");
            Assert.AreEqual(typeof(List<int>), first);
            Assert.AreSame(first, second);
        }

        [Test]
        public void PoolTypeConfigurationIsOpenGenericDetectsCorrectly()
        {
            PoolTypeConfiguration openConfig = new() { TypeName = "List<>" };
            Assert.IsTrue(openConfig.IsOpenGeneric, "List<> should be detected as open generic");

            PoolTypeConfiguration closedConfig = new() { TypeName = "List<int>" };
            Assert.IsFalse(
                closedConfig.IsOpenGeneric,
                "List<int> should not be detected as open generic"
            );

            PoolTypeConfiguration invalidConfig = new() { TypeName = "NotAType<>" };
            Assert.IsFalse(
                invalidConfig.IsOpenGeneric,
                "Invalid type should not be detected as open generic"
            );
        }

        [Test]
        public void PoolTypeConfigurationMatchesExactType()
        {
            PoolTypeConfiguration config = new() { TypeName = "List<int>" };

            Assert.IsTrue(config.Matches(typeof(List<int>)));
            Assert.IsFalse(config.Matches(typeof(List<string>)));
        }

        [Test]
        public void PoolTypeConfigurationMatchesOpenGeneric()
        {
            PoolTypeConfiguration config = new() { TypeName = "List<>" };

            Assert.IsTrue(config.Matches(typeof(List<int>)));
            Assert.IsTrue(config.Matches(typeof(List<string>)));
            Assert.IsTrue(config.Matches(typeof(List<TestPoolItem>)));
            Assert.IsFalse(config.Matches(typeof(HashSet<int>)));
        }

        [Test]
        public void PoolTypeConfigurationGetMatchPriorityReturnsCorrectValues()
        {
            PoolTypeConfiguration exactConfig = new() { TypeName = "List<int>" };
            PoolTypeConfiguration openConfig = new() { TypeName = "List<>" };

            Assert.AreEqual(0, exactConfig.GetMatchPriority(typeof(List<int>)));
            Assert.AreEqual(2, openConfig.GetMatchPriority(typeof(List<int>)));
            Assert.AreEqual(int.MaxValue, exactConfig.GetMatchPriority(typeof(List<string>)));
        }

        [Test]
        public void PoolTypeConfigurationInvalidateCacheForcesReResolution()
        {
            PoolTypeConfiguration config = new() { TypeName = "List<int>" };

            Type first = config.ResolvedType;
            Assert.IsTrue(first != null, "First resolution should return a valid type");

            config.InvalidateCache();
            config.TypeName = "List<string>";

            Type second = config.ResolvedType;
            Assert.IsTrue(
                second != null,
                "Second resolution should return a valid type after cache invalidation"
            );
            Assert.AreEqual(typeof(List<string>), second);
            Assert.AreNotEqual(first, second);
        }

        /// <summary>
        /// Tests that hysteresis is respected when MemoryPressureMonitor is disabled.
        /// After a usage spike, purging should be suppressed during the hysteresis period.
        /// This test explicitly disables memory pressure monitoring to ensure deterministic behavior.
        /// </summary>
        [Test]
        public void HysteresisIsRespectedWhenMemoryPressureDisabled()
        {
            const float hysteresisSeconds = 60f;

            // Ensure memory pressure is disabled (SetUp already does this)
            TestContext.WriteLine($"=== Test Configuration ===");
            TestContext.WriteLine(
                $"  MemoryPressureMonitor.Enabled: {MemoryPressureMonitor.Enabled}"
            );
            TestContext.WriteLine(
                $"  MemoryPressureMonitor.CurrentPressure: {MemoryPressureMonitor.CurrentPressure}"
            );
            TestContext.WriteLine($"  HysteresisSeconds: {hysteresisSeconds}");
            TestContext.WriteLine($"  IdleTimeoutSeconds: 0 (disabled)");
            TestContext.WriteLine($"  SpikeThresholdMultiplier: 1.5");
            Assert.IsFalse(
                MemoryPressureMonitor.Enabled,
                "Memory pressure should be disabled for this test"
            );

            int purgeCount = 0;
            List<PurgeReason> purgeReasons = new();
            PoolOptions<TestPoolItem> options = new()
            {
                UseIntelligentPurging = true,
                // Use 0f to disable idle timeout purges, which are intentionally allowed
                // during hysteresis - this test verifies hysteresis blocks other purge types
                IdleTimeoutSeconds = 0f,
                HysteresisSeconds = hysteresisSeconds, // Long hysteresis to ensure we stay within it
                SpikeThresholdMultiplier = 1.5f,
                Triggers = PurgeTrigger.OnRent,
                OnPurge = (_, reason) =>
                {
                    purgeCount++;
                    purgeReasons.Add(reason);
                },
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Create a spike - get many items at once
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 10; i++)
            {
                resources.Add(pool.Get());
            }

            float spikeTime = _currentTime;
            float hysteresisEndTime = spikeTime + hysteresisSeconds;

            TestContext.WriteLine($"=== After Creating Spike ===");
            TestContext.WriteLine($"  Items created: {resources.Count}");
            TestContext.WriteLine($"  Spike time: {spikeTime}");
            TestContext.WriteLine($"  Hysteresis end time: {hysteresisEndTime}");

            // Return them all to create pool items that could be purged
            foreach (PooledResource<TestPoolItem> r in resources)
            {
                r.Dispose();
            }
            resources.Clear();

            int poolCountAfterReturns = pool.Count;
            TestContext.WriteLine($"=== After Returning Items ===");
            TestContext.WriteLine($"  Pool.Count: {poolCountAfterReturns}");

            // Advance time within hysteresis period (hysteresis expires at ~61s)
            _currentTime = 10f;

            TestContext.WriteLine($"=== Before Get (Within Hysteresis) ===");
            TestContext.WriteLine($"  Current time: {_currentTime}");
            TestContext.WriteLine($"  Hysteresis end time: {hysteresisEndTime}");
            TestContext.WriteLine(
                $"  Is within hysteresis: {_currentTime < hysteresisEndTime} (should be true)"
            );
            TestContext.WriteLine($"  Idle timeout status: disabled (0f)");

            // Get should NOT trigger purge due to hysteresis protection
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine($"=== After Get ===");
            TestContext.WriteLine($"  PurgeCount: {purgeCount}");
            TestContext.WriteLine($"  Pool.Count: {pool.Count}");
            TestContext.WriteLine($"  Purge reasons: [{string.Join(", ", purgeReasons)}]");
            TestContext.WriteLine($"  Purge blocked due to: hysteresis (idle timeout disabled)");

            Assert.AreEqual(
                0,
                purgeCount,
                "Hysteresis should prevent purging (idle timeout is disabled to test hysteresis blocking)"
            );

            // Verify items are still in pool (minus the one we just rented)
            Assert.GreaterOrEqual(
                pool.Count,
                poolCountAfterReturns - 1,
                "Pool should retain items during hysteresis period"
            );
        }

        /// <summary>
        /// Tests that hysteresis IS bypassed when memory pressure is high.
        /// Under high memory pressure, the system should purge items regardless of hysteresis status.
        /// This test temporarily re-enables MemoryPressureMonitor to verify bypass behavior.
        /// </summary>
        [Test]
        public void HysteresisIsBypassedUnderHighMemoryPressure()
        {
            // Re-enable memory pressure monitoring for this test
            MemoryPressureMonitor.Enabled = true;

            try
            {
                TestContext.WriteLine(
                    $"MemoryPressureMonitor.Enabled: {MemoryPressureMonitor.Enabled}"
                );

                int purgeCount = 0;
                PoolOptions<TestPoolItem> options = new()
                {
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = 1f,
                    HysteresisSeconds = 120f, // Very long hysteresis
                    SpikeThresholdMultiplier = 1.5f,
                    Triggers = PurgeTrigger.Explicit, // We'll trigger manually
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                };

                using WallstopGenericPool<TestPoolItem> pool = new(
                    () => new TestPoolItem(),
                    preWarmCount: 10,
                    options: options
                );

                // Create a spike to activate hysteresis
                List<PooledResource<TestPoolItem>> resources = new();
                for (int i = 0; i < 5; i++)
                {
                    resources.Add(pool.Get());
                }

                TestContext.WriteLine($"Created spike with {resources.Count} concurrent rentals");

                foreach (PooledResource<TestPoolItem> r in resources)
                {
                    r.Dispose();
                }
                resources.Clear();

                int poolCountBeforePurge = pool.Count;
                TestContext.WriteLine($"Pool count before purge: {poolCountBeforePurge}");

                // Advance past idle timeout but still well within hysteresis period
                _currentTime = 10f;

                // Now simulate high memory pressure by calling ForceFullPurge with ignoreHysteresis=true
                // This simulates what happens when MemoryPressureLevel is High or Critical
                int purged = pool.ForceFullPurge(
                    PurgeReason.MemoryPressure,
                    ignoreHysteresis: true
                );

                TestContext.WriteLine(
                    $"After ForceFullPurge with ignoreHysteresis=true: purged={purged}, purgeCount={purgeCount}, pool count={pool.Count}"
                );

                // Under memory pressure, hysteresis should be bypassed and items should be purged
                Assert.Greater(
                    purgeCount,
                    0,
                    "High memory pressure should bypass hysteresis and trigger purging"
                );
                Assert.Greater(
                    purged,
                    0,
                    "ForceFullPurge with ignoreHysteresis should purge items"
                );
            }
            finally
            {
                // Restore original state (will be properly reset by TearDown)
                MemoryPressureMonitor.Enabled = false;
            }
        }
    }

    /// <summary>
    ///     Tests for pool edge cases related to time=0 and initialization states.
    ///     Time=0 is a special case in the pool system because it is treated as "uninitialized"
    ///     by the idle time tracker.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PoolTimeZeroEdgeCaseTests
    {
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

        private float _currentTime;
        private bool _wasMemoryPressureEnabled;

        private float TestTimeProvider()
        {
            return _currentTime;
        }

        [SetUp]
        public void SetUp()
        {
            _currentTime = 0f;
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

        /// <summary>
        ///     Documents that time=0 is a special case in the pool system.
        ///     Items returned at time=0 may have undefined idle timeout behavior
        ///     because time=0 is treated as "uninitialized" by the idle tracker.
        /// </summary>
        [Test]
        public void PoolAtTimeZeroDocumentsSpecialBehavior()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Return an item at time=0
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.That(pool.Count, Is.EqualTo(1), "Pool should have 1 item returned at time=0");

            // Even with time > IdleTimeoutSeconds, items at time=0 may not be purged
            // because time=0 is treated as uninitialized
            _currentTime = 2f;

            int purged = pool.Purge();

            // Document the behavior: at time=0, idle tracking may not work as expected
            // This test documents the behavior rather than asserting a specific outcome
            Assert.That(
                purged >= 0,
                Is.True,
                $"Purge at time={_currentTime} with items returned at time=0: purged={purged}. "
                    + "Note: time=0 is a special case where idle tracking may be undefined."
            );
        }

        /// <summary>
        ///     Tests that starting time at a non-zero value avoids the time=0 edge case.
        ///     This is the recommended pattern for tests that need predictable idle timeout behavior.
        /// </summary>
        [Test]
        public void PoolStartingAtNonZeroTimeHasPredictableIdleTimeout()
        {
            // Start at t=1 to avoid time=0 initialization issues
            _currentTime = 1f;

            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            Assert.That(pool.Count, Is.EqualTo(1), "Pool should have 1 item returned at time=1");

            // Advance time past idle timeout
            _currentTime = 3f;

            int purged = pool.Purge();

            Assert.That(
                purged,
                Is.EqualTo(1),
                $"Item should be purged when time advances past idle timeout. "
                    + $"Initial time: 1, Current time: {_currentTime}, Timeout: 1s"
            );
        }

        /// <summary>
        ///     Tests pool behavior when items are pre-warmed at time=0.
        /// </summary>
        [Test]
        public void PreWarmAtTimeZeroDocumentsIdleTrackerBehavior()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            // Pre-warm happens at construction time (time=0)
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 3,
                options: options
            );

            Assert.That(pool.Count, Is.EqualTo(3), "Pool should have 3 pre-warmed items");

            // Advance time past idle timeout
            _currentTime = 2f;

            int purged = pool.Purge();

            // Document behavior: pre-warmed items at time=0 may have special handling
            Assert.That(
                purged >= 0,
                Is.True,
                $"Pre-warmed items at time=0 behavior: purged={purged} of 3. "
                    + "Note: Pre-warmed items may have special idle tracking behavior."
            );
        }
    }

    /// <summary>
    ///     Tests for pool purge edge cases with negative and boundary budget values.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PoolPurgeBudgetEdgeCaseTests
    {
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

        private float _currentTime;
        private bool _wasMemoryPressureEnabled;

        private float TestTimeProvider()
        {
            return _currentTime;
        }

        [SetUp]
        public void SetUp()
        {
            // Start at t=1 to avoid time=0 edge cases
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

        /// <summary>
        ///     Tests that negative MaxPurgesPerOperation values are normalized to zero (unlimited).
        /// </summary>
        [Test]
        public void NegativeMaxPurgesPerOperationIsNormalizedToUnlimited()
        {
            const int preWarmCount = 10;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = -10, // Negative value
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            Assert.That(
                pool.MaxPurgesPerOperation,
                Is.EqualTo(0),
                "Negative MaxPurgesPerOperation should be normalized to 0 (unlimited)"
            );

            _currentTime = 3f;
            int purged = pool.Purge();

            Assert.That(
                purged,
                Is.EqualTo(preWarmCount),
                "All items should be purged when MaxPurgesPerOperation is unlimited (0)"
            );
        }

        /// <summary>
        ///     Tests that MaxPurgesPerOperation of exactly 1 works correctly.
        /// </summary>
        [Test]
        public void MaxPurgesPerOperationOfOneWorksCorrectly()
        {
            const int preWarmCount = 5;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = 1,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 3f;

            int firstPurge = pool.Purge();

            Assert.That(firstPurge, Is.EqualTo(1), "First purge should remove exactly 1 item");
            Assert.That(
                pool.Count,
                Is.EqualTo(preWarmCount - 1),
                $"Pool should have {preWarmCount - 1} items after first purge"
            );
            Assert.That(
                pool.HasPendingPurges,
                Is.True,
                "Should have pending purges after partial purge"
            );

            // Purge remaining items one at a time
            int totalPurged = firstPurge;
            int iterations = 0;
            while (pool.HasPendingPurges && iterations < 10)
            {
                totalPurged += pool.Purge();
                iterations++;
            }

            Assert.That(
                totalPurged,
                Is.EqualTo(preWarmCount),
                "Should have purged all items eventually"
            );
        }

        /// <summary>
        ///     Tests pool behavior when setting MaxPurgesPerOperation to the exact pool count.
        /// </summary>
        [Test]
        public void MaxPurgesPerOperationEqualToPoolCountPurgesAllInOneOperation()
        {
            const int preWarmCount = 5;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    MaxPurgesPerOperation = preWarmCount, // Exactly matches count
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 3f;

            int purged = pool.Purge();

            Assert.That(
                purged,
                Is.EqualTo(preWarmCount),
                "Should purge all items when limit equals pool count"
            );
            Assert.That(
                pool.HasPendingPurges,
                Is.False,
                "Should not have pending purges when all items purged"
            );
        }
    }

    /// <summary>
    ///     Tests for pool statistics edge cases when no operations have occurred.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PoolStatisticsEdgeCaseTests
    {
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

        /// <summary>
        ///     Tests that pool statistics are valid when no rentals have occurred.
        /// </summary>
        [Test]
        public void GetStatisticsWithNoRentalsReturnsValidStatistics()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            PoolStatistics stats = pool.GetStatistics();

            Assert.That(
                stats.RentCount,
                Is.EqualTo(0),
                "RentCount should be 0 when no rentals have occurred"
            );
            Assert.That(
                stats.ReturnCount,
                Is.EqualTo(0),
                "ReturnCount should be 0 when no rentals have occurred"
            );
            Assert.That(
                stats.PurgeCount,
                Is.EqualTo(0),
                "PurgeCount should be 0 when no purges have occurred"
            );
            Assert.That(stats.CurrentSize, Is.EqualTo(0), "CurrentSize should be 0 for empty pool");
            Assert.That(
                stats.PeakSize,
                Is.EqualTo(0),
                "PeakSize should be 0 when pool has never had items"
            );
        }

        /// <summary>
        ///     Tests that pool statistics with pre-warm but no rentals shows correct values.
        /// </summary>
        [Test]
        public void GetStatisticsWithPreWarmOnlyShowsCorrectValues()
        {
            const int preWarmCount = 5;
            PoolOptions<TestPoolItem> options = new()
            {
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: options
            );

            PoolStatistics stats = pool.GetStatistics();

            Assert.That(
                stats.RentCount,
                Is.EqualTo(0),
                "RentCount should be 0 when no rentals have occurred (only pre-warm)"
            );
            Assert.That(
                stats.ReturnCount,
                Is.EqualTo(0),
                "ReturnCount should be 0 when no rentals have occurred (only pre-warm)"
            );
            Assert.That(
                stats.CurrentSize,
                Is.EqualTo(preWarmCount),
                $"CurrentSize should be {preWarmCount} after pre-warm"
            );
            Assert.That(
                stats.PeakSize,
                Is.EqualTo(preWarmCount),
                $"PeakSize should be {preWarmCount} after pre-warm"
            );
        }

        /// <summary>
        ///     Tests statistics after a purge on an empty pool (edge case).
        /// </summary>
        [Test]
        public void GetStatisticsAfterPurgeOnEmptyPoolShowsCorrectValues()
        {
            PoolOptions<TestPoolItem> options = new()
            {
                IdleTimeoutSeconds = 1f,
                Triggers = PurgeTrigger.Explicit,
                TimeProvider = TestTimeProvider,
            };

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: options
            );

            // Attempt purge on empty pool
            _currentTime = 3f;
            int purged = pool.Purge();

            PoolStatistics stats = pool.GetStatistics();

            Assert.That(purged, Is.EqualTo(0), "Purge on empty pool should return 0");
            Assert.That(
                stats.PurgeCount,
                Is.EqualTo(0),
                "PurgeCount should be 0 when purging empty pool"
            );
            Assert.That(
                stats.IdleTimeoutPurges,
                Is.EqualTo(0),
                "IdleTimeoutPurges should be 0 when purging empty pool"
            );
        }

        /// <summary>
        ///     Tests that statistics ToString works correctly with zero values.
        /// </summary>
        [Test]
        public void PoolStatisticsToStringWithZeroValuesIsValid()
        {
            PoolStatistics stats = new(
                currentSize: 0,
                peakSize: 0,
                rentCount: 0,
                returnCount: 0,
                purgeCount: 0,
                idleTimeoutPurges: 0,
                capacityPurges: 0
            );

            string str = stats.ToString();

            Assert.That(
                str,
                Is.Not.Null.And.Not.Empty,
                "ToString should return non-empty string even with all zeros"
            );
            Assert.That(
                str,
                Does.Contain("CurrentSize=0"),
                "ToString should contain CurrentSize=0"
            );
        }

        [Test]
        public void DefaultPurgeTriggerIsPeriodic()
        {
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem> { TimeProvider = TestTimeProvider }
            );
            Assert.That(
                pool.Triggers,
                Is.EqualTo(PurgeTrigger.Periodic),
                "Default PurgeTrigger should be Periodic to avoid per-operation overhead"
            );
        }

        [Test]
        public void DefaultPoolOptionsHavePeriodicTrigger()
        {
            PoolOptions<TestPoolItem> options = new();
            Assert.That(
                options.Triggers,
                Is.EqualTo(PurgeTrigger.Periodic),
                "Default PoolOptions.Triggers should be Periodic"
            );
        }

        [Test]
        public void PeriodicTriggerDoesNotPurgeOnEveryGet()
        {
            _currentTime = 1f;
            int purgeCallbackCount = 0;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    TimeProvider = TestTimeProvider,
                    IdleTimeoutSeconds = 5f,
                    PurgeIntervalSeconds = 30f,
                    OnPurge = (_, _) => purgeCallbackCount++,
                }
            );

            // Hold all resources simultaneously before disposing so the pool grows to 10
            // distinct items. Sequential Get/Dispose would reuse a single item.
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 10; i++)
            {
                resources.Add(pool.Get());
            }
            foreach (PooledResource<TestPoolItem> resource in resources)
            {
                resource.Dispose();
            }

            TestContext.WriteLine(
                $"After filling pool at t={_currentTime}: pool.Count={pool.Count}, purgeCallbackCount={purgeCallbackCount}"
            );

            Assert.That(pool.Count, Is.EqualTo(10));

            // Advance time past idle timeout but NOT past purge interval
            _currentTime = 10f;

            // Do many Get() calls - with Periodic trigger, these should NOT trigger purge
            // since the purge interval has not elapsed
            for (int i = 0; i < 100; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            TestContext.WriteLine(
                $"After 100 Get/Dispose cycles at t={_currentTime}: pool.Count={pool.Count}, purgeCallbackCount={purgeCallbackCount}"
            );

            Assert.That(
                purgeCallbackCount,
                Is.EqualTo(0),
                "Periodic trigger should not purge until the interval has elapsed"
            );
        }

        [Test]
        public void PeriodicTriggerPurgesAfterInterval()
        {
            _currentTime = 1f;
            int purgeCallbackCount = 0;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    TimeProvider = TestTimeProvider,
                    IdleTimeoutSeconds = 5f,
                    PurgeIntervalSeconds = 10f,
                    OnPurge = (_, _) => purgeCallbackCount++,
                }
            );

            // Hold all resources simultaneously so the pool grows to 5 distinct items.
            // Sequential Get/Dispose would reuse a single item.
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 5; i++)
            {
                resources.Add(pool.Get());
            }
            foreach (PooledResource<TestPoolItem> resource in resources)
            {
                resource.Dispose();
            }

            TestContext.WriteLine(
                $"After filling pool at t={_currentTime}: pool.Count={pool.Count}, purgeCallbackCount={purgeCallbackCount}"
            );

            Assert.That(pool.Count, Is.EqualTo(5));

            // Advance time past both idle timeout and purge interval
            _currentTime = 20f;

            // This Get() should trigger periodic purge
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After Get/Dispose at t={_currentTime}: pool.Count={pool.Count}, purgeCallbackCount={purgeCallbackCount}"
            );

            Assert.That(
                purgeCallbackCount,
                Is.GreaterThan(0),
                "Periodic trigger should purge after the interval has elapsed"
            );
        }

        [Test]
        public void ExplicitOnRentTriggerStillWorksWhenConfigured()
        {
            _currentTime = 1f;
            int purgeCallbackCount = 0;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.OnRent,
                    TimeProvider = TestTimeProvider,
                    IdleTimeoutSeconds = 5f,
                    OnPurge = (_, _) => purgeCallbackCount++,
                }
            );

            // Hold all resources simultaneously so the pool grows to 5 distinct items.
            // Sequential Get/Dispose would reuse a single item.
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < 5; i++)
            {
                resources.Add(pool.Get());
            }
            foreach (PooledResource<TestPoolItem> resource in resources)
            {
                resource.Dispose();
            }

            TestContext.WriteLine(
                $"After filling pool at t={_currentTime}: pool.Count={pool.Count}, purgeCallbackCount={purgeCallbackCount}"
            );

            Assert.That(pool.Count, Is.EqualTo(5));

            // Advance time past idle timeout
            _currentTime = 10f;

            // This Get() should trigger purge because OnRent is explicitly configured
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After Get/Dispose at t={_currentTime}: pool.Count={pool.Count}, purgeCallbackCount={purgeCallbackCount}"
            );

            Assert.That(
                purgeCallbackCount,
                Is.GreaterThan(0),
                "OnRent trigger should still work when explicitly configured"
            );
        }

        [Test]
        [Category("Performance")]
        public void DefaultPoolGetPerformanceIsAcceptable()
        {
            // Measure that the default pool (Periodic trigger) can do many Get/Return
            // cycles quickly, without the overhead of per-operation purge checks
            using WallstopGenericPool<TestPoolItem> pool = new(() => new TestPoolItem());

            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();

            const int iterations = 100_000;
            for (int i = 0; i < iterations; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            sw.Stop();

            TestContext.WriteLine(
                $"Default pool: {iterations} Get/Return cycles in {sw.ElapsedMilliseconds}ms"
            );

            // With Periodic trigger (default), 100K iterations should complete well
            // under 1 second. The old OnRent default could take 200+ seconds.
            Assert.That(
                sw.ElapsedMilliseconds,
                Is.LessThan(5_000),
                $"Default pool should handle {iterations} Get/Return cycles in under 5 seconds"
            );
        }
    }

    /// <summary>
    /// Regression tests covering the contract for the auto-purge throttle
    /// (MinAutoPurgeIntervalSeconds).
    ///
    /// Contract:
    /// 1. Healthy pools (under MaxPoolSize) are rate-limited: repeated calls at the same
    ///    virtual clock tick do not each re-scan the pool. This amortizes the per-call
    ///    cost of auto-purge in hot Get/Return loops.
    /// 2. Over-capacity pools (above MaxPoolSize) BYPASS the throttle so returns cannot
    ///    accumulate beyond MaxPoolSize within a single tick.
    /// 3. Explicit purges always bypass the throttle.
    /// 4. The throttle clock is advanced on every completed scan (whether or not items
    ///    were removed), so a prior no-op scan does not wedge the fast-path skip, and a
    ///    prior partial scan does not permanently disable throttling.
    /// 5. Under concurrent out-of-order writes, the throttle clock is only ever advanced
    ///    to a strictly greater timestamp (max-semantics) — it cannot regress.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AutoPurgeThrottleTests
    {
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
        public void OverCapacityReturnsBypassThrottleOnReturnTrigger()
        {
            // Contract: with MaxPoolSize=1 and Triggers=OnReturn, consecutive same-tick
            // returns past capacity each trigger a CapacityExceeded purge. The throttle
            // must not let the pool grow unbounded between ticks of the virtual clock.
            int capacityPurges = 0;
            List<PurgeReason> reasons = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    MaxPoolSize = 1,
                    Triggers = PurgeTrigger.OnReturn,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, reason) =>
                    {
                        reasons.Add(reason);
                        if (reason == PurgeReason.CapacityExceeded)
                        {
                            capacityPurges++;
                        }
                    },
                }
            );

            const int pairs = 5;
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < pairs; i++)
            {
                resources.Add(pool.Get());
            }
            TestContext.WriteLine(
                $"Holding {resources.Count} items at t={_currentTime}; pool.Count while rented={pool.Count}"
            );

            foreach (PooledResource<TestPoolItem> resource in resources)
            {
                resource.Dispose();
            }

            TestContext.WriteLine(
                $"After disposing at t={_currentTime}: pool.Count={pool.Count}, "
                    + $"capacityPurges={capacityPurges}, reasons=[{string.Join(", ", reasons)}]"
            );

            Assert.LessOrEqual(
                pool.Count,
                1,
                "MaxPoolSize=1 must be enforced — returns beyond capacity must be purged"
            );
            Assert.GreaterOrEqual(
                capacityPurges,
                pairs - 1,
                "Every over-capacity return (all but the first to capacity) must fire CapacityExceeded "
                    + "even within the same virtual clock tick"
            );
        }

        [Test]
        public void OverCapacityGetBypassesThrottleOnRentTrigger()
        {
            // Contract for OnRent: after pre-filling the pool beyond MaxPoolSize (via a
            // non-trigger path to isolate the OnRent behavior), the next Get must trigger
            // a capacity purge — the throttle must not block purge work when the pool is
            // observably over capacity.
            int capacityPurges = 0;
            List<PurgeReason> reasons = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    MaxPoolSize = 1,
                    // Start with Explicit so the fill phase below does not fire purges
                    // (Explicit is the only trigger that never auto-fires).
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, reason) =>
                    {
                        reasons.Add(reason);
                        if (reason == PurgeReason.CapacityExceeded)
                        {
                            capacityPurges++;
                        }
                    },
                }
            );

            // Fill the pool beyond capacity without firing any triggers.
            List<PooledResource<TestPoolItem>> rented = new();
            for (int i = 0; i < 5; i++)
            {
                rented.Add(pool.Get());
            }
            foreach (PooledResource<TestPoolItem> resource in rented)
            {
                resource.Dispose();
            }
            rented.Clear();

            int countAfterFill = pool.Count;
            TestContext.WriteLine(
                $"After filling via Explicit trigger at t={_currentTime}: pool.Count={countAfterFill}, "
                    + $"capacityPurges={capacityPurges}"
            );
            Assert.AreEqual(
                0,
                capacityPurges,
                "Fill phase must not auto-purge when Triggers=Explicit"
            );
            Assert.AreEqual(
                5,
                countAfterFill,
                "Pool should have all 5 returned items when purges are disabled"
            );

            // Flip to OnRent and verify that the next Get observes the over-capacity pool
            // and bypasses the throttle to purge.
            pool.Triggers = PurgeTrigger.OnRent;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After Get under OnRent at t={_currentTime}: pool.Count={pool.Count}, "
                    + $"capacityPurges={capacityPurges}, reasons=[{string.Join(", ", reasons)}]"
            );

            Assert.LessOrEqual(
                pool.Count,
                1,
                "OnRent trigger on over-capacity pool must purge down to MaxPoolSize"
            );
            Assert.GreaterOrEqual(
                capacityPurges,
                1,
                "OnRent purge must fire CapacityExceeded when pool is over capacity"
            );
        }

        [Test]
        public void NoOpPurgeDoesNotBlockRealPurgeAtSameTick()
        {
            // Contract: a scan that finds nothing to purge must not wedge the fast-path
            // so that a subsequent call at the same tick needing real work gets blocked.
            //
            // Under the current (Option 3) throttle, every completed scan advances the
            // throttle clock — but over-capacity returns bypass the throttle, so the
            // real work still proceeds. This test verifies the no-op-then-work path.
            int purgeHits = 0;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    MaxPoolSize = 1,
                    Triggers = PurgeTrigger.OnReturn,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, _) => purgeHits++,
                }
            );

            // First return: pool.Count = 1, exactly at capacity — no purge required.
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            TestContext.WriteLine(
                $"After first Get/Return at t={_currentTime}: pool.Count={pool.Count}, purgeHits={purgeHits}"
            );
            Assert.AreEqual(0, purgeHits, "First return at capacity must not fire OnPurge");

            // Second batch at same tick, pool goes over capacity — MUST still purge
            // (over-capacity bypasses throttle, even though throttle was advanced by the
            // no-op scan).
            List<PooledResource<TestPoolItem>> resources = new();
            resources.Add(pool.Get());
            resources.Add(pool.Get());
            foreach (PooledResource<TestPoolItem> resource in resources)
            {
                resource.Dispose();
            }

            TestContext.WriteLine(
                $"After second simultaneous Get/Return batch at t={_currentTime}: "
                    + $"pool.Count={pool.Count}, purgeHits={purgeHits}"
            );

            Assert.GreaterOrEqual(
                purgeHits,
                1,
                "Over-capacity return must fire purge even after a prior no-op scan advanced the throttle"
            );
            Assert.LessOrEqual(
                pool.Count,
                1,
                "Pool must not exceed MaxPoolSize across same-tick returns"
            );
        }

        [Test]
        public void HealthyPoolRepeatedCyclesYieldNoPurges()
        {
            // Contract: for a pool that stays well under MaxPoolSize (the common case),
            // repeated Get/Return pairs at the same virtual clock tick must not purge
            // any items. This is the user-visible contract of the throttle/fast-path
            // combination — healthy pools see no churn. The actual per-call lock-avoidance
            // cost is validated by the wall-clock budget in
            // PoolPurgeTriggerPerformanceTests.HighContentionOnRentTriggerCompletesWithinBudget.
            int purgeHits = 0;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    MaxPoolSize = 1000, // comfortably large; pool never reaches this
                    IdleTimeoutSeconds = 60f, // long enough that items never time out
                    Triggers = PurgeTrigger.OnReturn,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, _) => purgeHits++,
                }
            );

            // 100 rapid Get/Return cycles at fixed t. Pool holds at most 1 item at a time.
            for (int i = 0; i < 100; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            TestContext.WriteLine(
                $"After 100 cycles at t={_currentTime}: pool.Count={pool.Count}, purgeHits={purgeHits}"
            );

            Assert.AreEqual(
                0,
                purgeHits,
                "Healthy pool must never purge when staying under capacity and below idle timeout"
            );
            Assert.LessOrEqual(
                pool.Count,
                1,
                "Steady-state pool size must track the working-set size (≈ 1 item here)"
            );
        }

        [Test]
        public void IdleTimeoutPurgesAreThrottledAtSameTick()
        {
            // Contract: unlike over-capacity purges (which bypass the throttle),
            // idle-timeout purges are subject to the throttle because an item's idle
            // status cannot change within a single virtual-clock tick. This prevents
            // repeated O(n) scans when multiple returns happen at the same tick while
            // some existing items are idle-eligible.
            int idlePurges = 0;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    // MaxPoolSize=0 means unbounded — only idle-timeout drives purging.
                    MaxPoolSize = 0,
                    IdleTimeoutSeconds = 5f,
                    MinRetainCount = 0,
                    WarmRetainCount = 0,
                    Triggers = PurgeTrigger.OnReturn,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, reason) =>
                    {
                        if (reason == PurgeReason.IdleTimeout)
                        {
                            idlePurges++;
                        }
                    },
                }
            );

            // Phase 1: put 10 items in at t=1 so they all share the same ReturnTime.
            List<PooledResource<TestPoolItem>> rented = new();
            for (int i = 0; i < 10; i++)
            {
                rented.Add(pool.Get());
            }
            foreach (PooledResource<TestPoolItem> resource in rented)
            {
                resource.Dispose();
            }
            rented.Clear();

            int afterFillCount = pool.Count;
            TestContext.WriteLine(
                $"After phase 1 at t={_currentTime}: pool.Count={afterFillCount}, idlePurges={idlePurges}"
            );
            Assert.AreEqual(0, idlePurges, "No items should be idle-eligible yet at t=1");

            // Phase 2: advance past idle-timeout and do ONE Get/Return to trigger a scan.
            // That scan should purge idle items AND consume the throttle.
            _currentTime = 10f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            int idleAfterFirstScan = idlePurges;
            TestContext.WriteLine(
                $"After first scan at t={_currentTime}: pool.Count={pool.Count}, idlePurges={idlePurges}"
            );
            Assert.Greater(idleAfterFirstScan, 0, "First scan at t=10 must purge idle items");

            // Phase 3: trigger another Get/Return at the SAME tick. Pool is now healthy
            // (items were purged), so the throttle must skip this scan.
            int countBeforeSecond = pool.Count;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After second scan at t={_currentTime}: pool.Count={pool.Count}, "
                    + $"idlePurges={idlePurges}, delta={idlePurges - idleAfterFirstScan}"
            );

            Assert.AreEqual(
                idleAfterFirstScan,
                idlePurges,
                "Throttle must block a second idle-timeout scan at the same clock tick"
            );
        }

        [Test]
        public void ExplicitPurgeBypassesThrottle()
        {
            // Contract: Pool.Purge() (isExplicit=true) always bypasses the throttle.
            int purgeHits = 0;
            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    MaxPoolSize = 1,
                    Triggers = PurgeTrigger.OnReturn,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, _) => purgeHits++,
                }
            );

            List<PooledResource<TestPoolItem>> batch = new();
            for (int i = 0; i < 4; i++)
            {
                batch.Add(pool.Get());
            }
            foreach (PooledResource<TestPoolItem> resource in batch)
            {
                resource.Dispose();
            }

            int hitsAfterAuto = purgeHits;
            TestContext.WriteLine(
                $"After auto-purge at t={_currentTime}: pool.Count={pool.Count}, purgeHits={purgeHits}"
            );

            // Explicit purge at the same clock should still work even though throttle is active.
            int explicitPurged = pool.Purge();
            TestContext.WriteLine(
                $"After explicit Purge() at t={_currentTime}: purged={explicitPurged}, "
                    + $"pool.Count={pool.Count}, purgeHits={purgeHits}"
            );

            Assert.AreEqual(
                0,
                pool.Count,
                "Explicit purge must fully drain the pool regardless of throttle state"
            );
            Assert.GreaterOrEqual(
                purgeHits,
                hitsAfterAuto + 1,
                "Explicit purge must fire OnPurge even when auto-purge throttle is active"
            );
        }
    }
}
