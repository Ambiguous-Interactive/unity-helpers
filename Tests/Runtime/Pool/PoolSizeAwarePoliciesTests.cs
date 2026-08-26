// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;
#if !SINGLE_THREADED
    using System.Threading;
    using System.Threading.Tasks;
#endif

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PoolSizeAwarePoliciesTests
    {
        private bool _wasMemoryPressureEnabled;

        [SetUp]
        public void SetUp()
        {
            PoolPurgeSettings.ResetToDefaults();
            PoolSizeEstimator.ClearCaches();
            _wasMemoryPressureEnabled = MemoryPressureMonitor.Enabled;
            MemoryPressureMonitor.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            PoolPurgeSettings.ResetToDefaults();
            PoolSizeEstimator.ClearCaches();
            MemoryPressureMonitor.Enabled = _wasMemoryPressureEnabled;
        }

        // ========================================
        // PoolSizeEstimator Tests
        // ========================================

        [Test]
        public void EstimateItemSizeBytesReturnsPositiveForValueTypes()
        {
            int intSize = PoolSizeEstimator.EstimateItemSizeBytes<int>();
            int longSize = PoolSizeEstimator.EstimateItemSizeBytes<long>();
            int doubleSize = PoolSizeEstimator.EstimateItemSizeBytes<double>();
            int guidSize = PoolSizeEstimator.EstimateItemSizeBytes<Guid>();

            Assert.Greater(intSize, 0, "int should have positive size");
            Assert.Greater(longSize, 0, "long should have positive size");
            Assert.Greater(doubleSize, 0, "double should have positive size");
            Assert.Greater(guidSize, 0, "Guid should have positive size");
        }

        [Test]
        public void EstimateItemSizeBytesReturnsExpectedSizesForPrimitives()
        {
            int byteSize = PoolSizeEstimator.EstimateItemSizeBytes<byte>();
            int shortSize = PoolSizeEstimator.EstimateItemSizeBytes<short>();
            int intSize = PoolSizeEstimator.EstimateItemSizeBytes<int>();
            int longSize = PoolSizeEstimator.EstimateItemSizeBytes<long>();

            Assert.AreEqual(1, byteSize, "byte should be 1 byte");
            Assert.AreEqual(2, shortSize, "short should be 2 bytes");
            Assert.AreEqual(4, intSize, "int should be 4 bytes");
            Assert.AreEqual(8, longSize, "long should be 8 bytes");
        }

        [Test]
        public void EstimateItemSizeBytesReturnsPositiveForReferenceTypes()
        {
            int objectSize = PoolSizeEstimator.EstimateItemSizeBytes<object>();
            int stringSize = PoolSizeEstimator.EstimateItemSizeBytes<string>();
            int smallClassSize = PoolSizeEstimator.EstimateItemSizeBytes<SmallClass>();

            Assert.Greater(objectSize, 0, "object should have positive size");
            Assert.Greater(stringSize, 0, "string should have positive size");
            Assert.Greater(smallClassSize, 0, "SmallClass should have positive size");
        }

        [Test]
        public void EstimateItemSizeBytesReturnsPositiveForCollectionTypes()
        {
            int listSize = PoolSizeEstimator.EstimateItemSizeBytes<List<int>>();
            int dictSize = PoolSizeEstimator.EstimateItemSizeBytes<Dictionary<string, int>>();
            int hashSetSize = PoolSizeEstimator.EstimateItemSizeBytes<HashSet<int>>();

            Assert.Greater(listSize, 0, "List<int> should have positive size");
            Assert.Greater(dictSize, 0, "Dictionary<string,int> should have positive size");
            Assert.Greater(hashSetSize, 0, "HashSet<int> should have positive size");
        }

        [Test]
        public void EstimateItemSizeBytesWithNullTypeReturnsPointerSize()
        {
            int size = PoolSizeEstimator.EstimateItemSizeBytes(null);
            Assert.AreEqual(
                IntPtr.Size,
                size,
                "Null type should return PointerSize as defensive default"
            );
        }

        [Test]
        public void EstimateArraySizeBytesScalesWithLength()
        {
            int size10 = PoolSizeEstimator.EstimateArraySizeBytes<int>(10);
            int size100 = PoolSizeEstimator.EstimateArraySizeBytes<int>(100);
            int size1000 = PoolSizeEstimator.EstimateArraySizeBytes<int>(1000);

            Assert.Less(size10, size100, "Array of 10 should be smaller than array of 100");
            Assert.Less(size100, size1000, "Array of 100 should be smaller than array of 1000");
        }

        [Test]
        public void EstimateArraySizeBytesWithZeroLengthReturnsMinimalSize()
        {
            int emptyArraySize = PoolSizeEstimator.EstimateArraySizeBytes<int>(0);

            Assert.Greater(emptyArraySize, 0, "Empty array should still have overhead");
        }

        [Test]
        public void EstimateArraySizeBytesWithNullTypeReturnsMinArrayOverhead()
        {
            int size = PoolSizeEstimator.EstimateArraySizeBytes(null, 10);
            Assert.Greater(size, 0, "Null element type should return minimum array overhead");
        }

        [TestCase(-1)]
        [TestCase(int.MinValue)]
        public void EstimateArraySizeBytesHandlesNegativeLength(int length)
        {
            int size = PoolSizeEstimator.EstimateArraySizeBytes<int>(length);
            Assert.Greater(size, 0, "Negative length should return minimum array overhead");
        }

        [Test]
        public void GetLohThresholdLengthReturnsReasonableValues()
        {
            int intThreshold = PoolSizeEstimator.GetLohThresholdLength<int>();
            int longThreshold = PoolSizeEstimator.GetLohThresholdLength<long>();
            int byteThreshold = PoolSizeEstimator.GetLohThresholdLength<byte>();

            // With 85KB threshold:
            // int (4 bytes) - roughly 21,250 elements
            // long (8 bytes) - roughly 10,625 elements
            // byte (1 byte) - roughly 85,000 elements
            Assert.Greater(intThreshold, 0, "int threshold should be positive");
            Assert.Greater(longThreshold, 0, "long threshold should be positive");
            Assert.Greater(byteThreshold, 0, "byte threshold should be positive");

            Assert.Greater(
                byteThreshold,
                intThreshold,
                "byte arrays need more elements than int arrays to reach LOH"
            );
            Assert.Greater(
                intThreshold,
                longThreshold,
                "int arrays need more elements than long arrays to reach LOH"
            );
        }

        [Test]
        public void GetLohThresholdLengthWithNullTypeReturnsMaxValue()
        {
            int threshold = PoolSizeEstimator.GetLohThresholdLength(null);
            Assert.AreEqual(
                int.MaxValue,
                threshold,
                "Null element type should return int.MaxValue"
            );
        }

        [Test]
        public void IsLargeObjectReturnsFalseForSmallTypes()
        {
            bool intIsLarge = PoolSizeEstimator.IsLargeObject<int>();
            bool smallStructIsLarge = PoolSizeEstimator.IsLargeObject<SmallStruct>();
            bool smallClassIsLarge = PoolSizeEstimator.IsLargeObject<SmallClass>();

            Assert.IsFalse(intIsLarge, "int should not be a large object");
            Assert.IsFalse(smallStructIsLarge, "SmallStruct should not be a large object");
            Assert.IsFalse(smallClassIsLarge, "SmallClass should not be a large object");
        }

        [Test]
        public void IsLargeObjectWithNullTypeReturnsFalse()
        {
            bool isLarge = PoolSizeEstimator.IsLargeObject(null);
            Assert.IsFalse(isLarge, "Null type should return false as defensive default");
        }

        [Test]
        public void ClearCachesRemovesCachedEstimates()
        {
            // Warm the cache
            int firstSize = PoolSizeEstimator.EstimateItemSizeBytes<SmallClass>();

            // Clear and verify cache is cleared (no exception thrown)
            PoolSizeEstimator.ClearCaches();

            int secondSize = PoolSizeEstimator.EstimateItemSizeBytes<SmallClass>();
            Assert.AreEqual(firstSize, secondSize, "Size should be consistent after cache clear");
        }

        // ========================================
        // PoolPurgeSettings Size-Aware Tests
        // ========================================

        [Test]
        public void SizeAwarePoliciesEnabledDefaultsToTrue()
        {
            Assert.IsTrue(PoolPurgeSettings.SizeAwarePoliciesEnabled);
        }

        [Test]
        public void SizeAwarePoliciesEnabledCanBeDisabled()
        {
            PoolPurgeSettings.SizeAwarePoliciesEnabled = false;

            Assert.IsFalse(PoolPurgeSettings.SizeAwarePoliciesEnabled);
        }

        [Test]
        public void LargeObjectThresholdBytesDefaultsToLohThreshold()
        {
            Assert.AreEqual(85000, PoolPurgeSettings.LargeObjectThresholdBytes);
        }

        [Test]
        public void LargeObjectThresholdBytesCanBeChanged()
        {
            PoolPurgeSettings.LargeObjectThresholdBytes = 50000;

            Assert.AreEqual(50000, PoolPurgeSettings.LargeObjectThresholdBytes);
        }

        [Test]
        public void LargeObjectThresholdBytesNormalizesNegativeToZero()
        {
            PoolPurgeSettings.LargeObjectThresholdBytes = -100;

            Assert.AreEqual(0, PoolPurgeSettings.LargeObjectThresholdBytes);
        }

        [Test]
        public void LargeObjectBufferMultiplierDefaultsToOne()
        {
            Assert.AreEqual(1.0f, PoolPurgeSettings.LargeObjectBufferMultiplier, 0.001f);
        }

        [Test]
        public void LargeObjectBufferMultiplierCanBeChanged()
        {
            PoolPurgeSettings.LargeObjectBufferMultiplier = 0.5f;

            Assert.AreEqual(0.5f, PoolPurgeSettings.LargeObjectBufferMultiplier, 0.001f);
        }

        [Test]
        public void LargeObjectBufferMultiplierNormalizesNegativeToZero()
        {
            PoolPurgeSettings.LargeObjectBufferMultiplier = -1.0f;

            Assert.AreEqual(0f, PoolPurgeSettings.LargeObjectBufferMultiplier, 0.001f);
        }

        [Test]
        public void LargeObjectIdleTimeoutMultiplierDefaultsToPointFive()
        {
            Assert.AreEqual(0.5f, PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier, 0.001f);
        }

        [Test]
        public void LargeObjectIdleTimeoutMultiplierCanBeChanged()
        {
            PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier = 0.25f;

            Assert.AreEqual(0.25f, PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier, 0.001f);
        }

        [Test]
        public void LargeObjectIdleTimeoutMultiplierClampsBetweenZeroAndOne()
        {
            PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier = -0.5f;
            Assert.AreEqual(0f, PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier, 0.001f);

            PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier = 1.5f;
            Assert.AreEqual(1.0f, PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier, 0.001f);
        }

        [Test]
        public void LargeObjectWarmRetainCountDefaultsToOne()
        {
            Assert.AreEqual(1, PoolPurgeSettings.LargeObjectWarmRetainCount);
        }

        [Test]
        public void LargeObjectWarmRetainCountCanBeChanged()
        {
            PoolPurgeSettings.LargeObjectWarmRetainCount = 0;

            Assert.AreEqual(0, PoolPurgeSettings.LargeObjectWarmRetainCount);
        }

        [Test]
        public void LargeObjectWarmRetainCountNormalizesNegativeToZero()
        {
            PoolPurgeSettings.LargeObjectWarmRetainCount = -5;

            Assert.AreEqual(0, PoolPurgeSettings.LargeObjectWarmRetainCount);
        }

        [Test]
        public void ResetToDefaultsResetsSizeAwareSettings()
        {
            PoolPurgeSettings.SizeAwarePoliciesEnabled = false;
            PoolPurgeSettings.LargeObjectThresholdBytes = 10000;
            PoolPurgeSettings.LargeObjectBufferMultiplier = 0.1f;
            PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier = 0.1f;
            PoolPurgeSettings.LargeObjectWarmRetainCount = 10;

            PoolPurgeSettings.ResetToDefaults();

            Assert.IsTrue(PoolPurgeSettings.SizeAwarePoliciesEnabled);
            Assert.AreEqual(85000, PoolPurgeSettings.LargeObjectThresholdBytes);
            Assert.AreEqual(1.0f, PoolPurgeSettings.LargeObjectBufferMultiplier, 0.001f);
            Assert.AreEqual(0.5f, PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier, 0.001f);
            Assert.AreEqual(1, PoolPurgeSettings.LargeObjectWarmRetainCount);
        }

        [Test]
        public void IsLargeObjectReflectsThresholdSetting()
        {
            // By default, small types are not large objects
            Assert.IsFalse(PoolPurgeSettings.IsLargeObject<int>());
            Assert.IsFalse(PoolPurgeSettings.IsLargeObject<SmallClass>());

            // Lower the threshold significantly
            PoolPurgeSettings.LargeObjectThresholdBytes = 1;

            // Now even small types should be considered large
            Assert.IsTrue(PoolPurgeSettings.IsLargeObject<int>());
        }

        [Test]
        public void PoolPurgeSettingsIsLargeObjectWithNullTypeReturnsFalse()
        {
            bool isLarge = PoolPurgeSettings.IsLargeObject(null);
            Assert.IsFalse(isLarge, "Null type should return false as defensive default");
        }

        // ========================================
        // GetSizeAwareEffectiveOptions Tests
        // ========================================

        [Test]
        public void GetSizeAwareEffectiveOptionsReturnsSameAsBaseForSmallTypes()
        {
            PoolPurgeEffectiveOptions baseOptions =
                PoolPurgeSettings.GetEffectiveOptions<SmallClass>();
            PoolPurgeEffectiveOptions sizeAwareOptions =
                PoolPurgeSettings.GetSizeAwareEffectiveOptions<SmallClass>();

            Assert.AreEqual(baseOptions.Enabled, sizeAwareOptions.Enabled);
            Assert.AreEqual(
                baseOptions.IdleTimeoutSeconds,
                sizeAwareOptions.IdleTimeoutSeconds,
                0.001f
            );
            Assert.AreEqual(
                baseOptions.BufferMultiplier,
                sizeAwareOptions.BufferMultiplier,
                0.001f
            );
            Assert.AreEqual(baseOptions.WarmRetainCount, sizeAwareOptions.WarmRetainCount);
            Assert.AreEqual(baseOptions.MinRetainCount, sizeAwareOptions.MinRetainCount);
        }

        [Test]
        public void GetSizeAwareEffectiveOptionsReturnsSameWhenDisabled()
        {
            PoolPurgeSettings.SizeAwarePoliciesEnabled = false;

            // Lower the threshold so everything is "large"
            PoolPurgeSettings.LargeObjectThresholdBytes = 1;

            PoolPurgeEffectiveOptions baseOptions = PoolPurgeSettings.GetEffectiveOptions<int>();
            PoolPurgeEffectiveOptions sizeAwareOptions =
                PoolPurgeSettings.GetSizeAwareEffectiveOptions<int>();

            Assert.AreEqual(
                baseOptions.IdleTimeoutSeconds,
                sizeAwareOptions.IdleTimeoutSeconds,
                0.001f
            );
            Assert.AreEqual(
                baseOptions.BufferMultiplier,
                sizeAwareOptions.BufferMultiplier,
                0.001f
            );
            Assert.AreEqual(baseOptions.WarmRetainCount, sizeAwareOptions.WarmRetainCount);
        }

        [Test]
        public void GetSizeAwareEffectiveOptionsAdjustsForLargeObjects()
        {
            // Lower the threshold so int is considered large
            PoolPurgeSettings.LargeObjectThresholdBytes = 1;

            PoolPurgeEffectiveOptions baseOptions = PoolPurgeSettings.GetEffectiveOptions<int>();
            PoolPurgeEffectiveOptions sizeAwareOptions =
                PoolPurgeSettings.GetSizeAwareEffectiveOptions<int>();

            // Verify adjustments are applied
            float expectedIdleTimeout =
                baseOptions.IdleTimeoutSeconds * PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier;
            Assert.AreEqual(expectedIdleTimeout, sizeAwareOptions.IdleTimeoutSeconds, 0.001f);

            Assert.AreEqual(
                PoolPurgeSettings.LargeObjectBufferMultiplier,
                sizeAwareOptions.BufferMultiplier,
                0.001f
            );
            Assert.AreEqual(
                PoolPurgeSettings.LargeObjectWarmRetainCount,
                sizeAwareOptions.WarmRetainCount
            );
        }

        [Test]
        public void GetSizeAwareEffectiveOptionsUsesMinimumOfConfiguredAndLargeObjectValues()
        {
            // Configure a type with very small values
            PoolPurgeSettings.Configure<int>(options =>
            {
                options.BufferMultiplier = 0.5f; // Less than default 1.0f for large objects
                options.WarmRetainCount = 0; // Less than default 1 for large objects
            });

            // Lower the threshold so int is considered large
            PoolPurgeSettings.LargeObjectThresholdBytes = 1;

            PoolPurgeEffectiveOptions sizeAwareOptions =
                PoolPurgeSettings.GetSizeAwareEffectiveOptions<int>();

            // Should use the smaller of configured and large object values
            Assert.AreEqual(0.5f, sizeAwareOptions.BufferMultiplier, 0.001f); // configured value is smaller
            Assert.AreEqual(0, sizeAwareOptions.WarmRetainCount); // configured value is smaller
        }

        [Test]
        public void GetSizeAwareEffectiveOptionsWithNullTypeReturnsGlobalDefaults()
        {
            PoolPurgeEffectiveOptions options = PoolPurgeSettings.GetSizeAwareEffectiveOptions(
                null
            );

            Assert.AreEqual(PoolPurgeConfigurationSource.GlobalDefaults, options.Source);
            Assert.AreEqual(
                PoolPurgeSettings.DefaultGlobalIdleTimeoutSeconds,
                options.IdleTimeoutSeconds,
                0.001f
            );
            Assert.AreEqual(
                PoolPurgeSettings.DefaultGlobalBufferMultiplier,
                options.BufferMultiplier,
                0.001f
            );
        }

        [Test]
        public void GetSizeAwareEffectiveOptionsPreservesNonAdjustedSettings()
        {
            // Lower the threshold so int is considered large
            PoolPurgeSettings.LargeObjectThresholdBytes = 1;

            PoolPurgeEffectiveOptions baseOptions = PoolPurgeSettings.GetEffectiveOptions<int>();
            PoolPurgeEffectiveOptions sizeAwareOptions =
                PoolPurgeSettings.GetSizeAwareEffectiveOptions<int>();

            // These should remain unchanged
            Assert.AreEqual(baseOptions.Enabled, sizeAwareOptions.Enabled);
            Assert.AreEqual(baseOptions.MinRetainCount, sizeAwareOptions.MinRetainCount);
            Assert.AreEqual(
                baseOptions.RollingWindowSeconds,
                sizeAwareOptions.RollingWindowSeconds,
                0.001f
            );
            Assert.AreEqual(
                baseOptions.HysteresisSeconds,
                sizeAwareOptions.HysteresisSeconds,
                0.001f
            );
            Assert.AreEqual(
                baseOptions.SpikeThresholdMultiplier,
                sizeAwareOptions.SpikeThresholdMultiplier,
                0.001f
            );
            Assert.AreEqual(
                baseOptions.MaxPurgesPerOperation,
                sizeAwareOptions.MaxPurgesPerOperation
            );
            Assert.AreEqual(baseOptions.Source, sizeAwareOptions.Source);
        }

        [Test]
        public void GetSizeAwareEffectiveOptionsUsesLargeObjectDefaultsWhenConfiguredIsLessAggressive()
        {
            // Configure a type with larger values than large-object defaults
            PoolPurgeSettings.Configure<int>(options =>
            {
                options.BufferMultiplier = 3.0f; // More than default 1.0f for large objects
                options.WarmRetainCount = 5; // More than default 1 for large objects
            });

            // Lower the threshold so int is considered large
            PoolPurgeSettings.LargeObjectThresholdBytes = 1;

            PoolPurgeEffectiveOptions sizeAwareOptions =
                PoolPurgeSettings.GetSizeAwareEffectiveOptions<int>();

            // Should use the large object values since they are more aggressive (smaller)
            Assert.AreEqual(
                PoolPurgeSettings.LargeObjectBufferMultiplier,
                sizeAwareOptions.BufferMultiplier,
                0.001f
            );
            Assert.AreEqual(
                PoolPurgeSettings.LargeObjectWarmRetainCount,
                sizeAwareOptions.WarmRetainCount
            );
        }

        // ========================================
        // Thread-Safety Tests
        // ========================================

#if !SINGLE_THREADED
        [Test]
        public void PoolSizeEstimatorIsThreadSafe()
        {
            const int threadCount = 8;
            const int iterationsPerThread = 1000;
            Exception capturedException = null;
            int successCount = 0;

            Thread[] threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadIndex = t;
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < iterationsPerThread; i++)
                        {
                            Type type = threadIndex % 2 == 0 ? typeof(int) : typeof(string);
                            int size = PoolSizeEstimator.EstimateItemSizeBytes(type);
                            if (size <= 0)
                            {
                                throw new InvalidOperationException("Size should be positive");
                            }
                            bool isLarge = PoolSizeEstimator.IsLargeObject(type);
                        }
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref capturedException, ex, null);
                    }
                });
            }

            for (int t = 0; t < threadCount; t++)
            {
                threads[t].Start();
            }

            for (int t = 0; t < threadCount; t++)
            {
                threads[t].Join(TimeSpan.FromSeconds(30));
            }

            Assert.IsTrue(capturedException == null, $"Exception in thread: {capturedException}");
            Assert.AreEqual(threadCount, successCount, "All threads should complete successfully");
        }

        [Test]
        public void SizeAwareSettingsAreThreadSafe()
        {
            const int threadCount = 8;
            const int iterationsPerThread = 1000;
            Exception capturedException = null;
            int successCount = 0;

            Thread[] threads = new Thread[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadIndex = t;
                threads[t] = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < iterationsPerThread; i++)
                        {
                            // Read settings
                            bool enabled = PoolPurgeSettings.SizeAwarePoliciesEnabled;
                            int threshold = PoolPurgeSettings.LargeObjectThresholdBytes;
                            float bufferMultiplier = PoolPurgeSettings.LargeObjectBufferMultiplier;

                            // Write settings (alternate threads)
                            if (threadIndex % 2 == 0)
                            {
                                PoolPurgeSettings.LargeObjectThresholdBytes = i % 100000 + 1;
                            }
                            else
                            {
                                PoolPurgeSettings.LargeObjectBufferMultiplier = (i % 10) * 0.1f;
                            }
                        }
                        Interlocked.Increment(ref successCount);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref capturedException, ex, null);
                    }
                });
            }

            for (int t = 0; t < threadCount; t++)
            {
                threads[t].Start();
            }

            for (int t = 0; t < threadCount; t++)
            {
                threads[t].Join(TimeSpan.FromSeconds(30));
            }

            Assert.IsTrue(capturedException == null, $"Exception in thread: {capturedException}");
            Assert.AreEqual(threadCount, successCount, "All threads should complete successfully");
        }
#endif

        // ========================================
        // Integration Tests with Pool
        // ========================================

        [Test]
        public void PoolUsesDefaultSizeAwarePolicies()
        {
            // Lower the threshold so SmallClass is considered large
            PoolPurgeSettings.LargeObjectThresholdBytes = 1;

            float currentTime = 0f;
            float TestTimeProvider() => currentTime;

            using WallstopGenericPool<SmallClass> pool = new WallstopGenericPool<SmallClass>(
                () => new SmallClass(),
                options: new PoolOptions<SmallClass>
                {
                    Triggers = PurgeTrigger.OnRent,
                    TimeProvider = TestTimeProvider,
                }
            );

            // `Assert.IsTrue(stats != null)` was here, and PoolStatistics is a struct: the
            // compiler proves that always true (CS8073), so the test's only assertion could not
            // fail. Assert what its name claims instead -- the threshold above makes SmallClass a
            // large object, so the size-aware options must differ from the base ones in the three
            // ways GetSizeAwareEffectiveOptions documents.
            PoolPurgeEffectiveOptions baseOptions =
                PoolPurgeSettings.GetEffectiveOptions<SmallClass>();
            PoolPurgeEffectiveOptions sizeAware =
                PoolPurgeSettings.GetSizeAwareEffectiveOptions<SmallClass>();

            Assert.AreEqual(
                PoolPurgeSettings.LargeObjectWarmRetainCount,
                sizeAware.WarmRetainCount,
                "A large object keeps the large-object warm count"
            );
            Assert.LessOrEqual(
                sizeAware.BufferMultiplier,
                baseOptions.BufferMultiplier,
                "A large object never buffers more than the base policy"
            );
            Assert.LessOrEqual(
                sizeAware.IdleTimeoutSeconds,
                baseOptions.IdleTimeoutSeconds,
                "A large object is never held idle longer than the base policy"
            );

            PoolStatistics stats = pool.GetStatistics();
            Assert.AreEqual(0, stats.CurrentSize, "Nothing has been returned to the pool yet");
        }

        [Test]
        public void PoolWithLargeObjectsRespectsSizeAwarePolicies()
        {
            // Use default threshold (85KB)
            PoolPurgeSettings.SizeAwarePoliciesEnabled = true;

            float currentTime = 0f;
            float TestTimeProvider() => currentTime;

            // Create pool with items that are definitely below LOH threshold
            using WallstopGenericPool<SmallClass> pool = new WallstopGenericPool<SmallClass>(
                () => new SmallClass(),
                preWarmCount: 5,
                options: new PoolOptions<SmallClass>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = 60f,
                }
            );

            // Pool should work normally
            Assert.AreEqual(5, pool.Count);

            using (PooledResource<SmallClass> resource = pool.Get())
            {
                Assert.AreEqual(4, pool.Count);
            }

            Assert.AreEqual(5, pool.Count);
        }

        // ========================================
        // Edge Case Tests
        // ========================================

        [Test]
        public void EstimateItemSizeBytesWorksForEnums()
        {
            int sizeIntEnum = PoolSizeEstimator.EstimateItemSizeBytes<System.DayOfWeek>();
            int sizeLongEnum = PoolSizeEstimator.EstimateItemSizeBytes<System.DateTimeKind>();

            Assert.Greater(sizeIntEnum, 0, "Enum should have positive size");
            Assert.Greater(sizeLongEnum, 0, "Enum should have positive size");
        }

        [Test]
        public void EstimateItemSizeBytesWorksForArrayTypes()
        {
            int byteArraySize = PoolSizeEstimator.EstimateItemSizeBytes<byte[]>();
            int intArraySize = PoolSizeEstimator.EstimateItemSizeBytes<int[]>();
            int stringArraySize = PoolSizeEstimator.EstimateItemSizeBytes<string[]>();

            Assert.Greater(byteArraySize, 0, "byte[] should have positive size");
            Assert.Greater(intArraySize, 0, "int[] should have positive size");
            Assert.Greater(stringArraySize, 0, "string[] should have positive size");
        }

        [Test]
        public void EstimateItemSizeBytesWorksForNestedGenericTypes()
        {
            int nestedListSize = PoolSizeEstimator.EstimateItemSizeBytes<List<List<int>>>();
            int nestedDictSize = PoolSizeEstimator.EstimateItemSizeBytes<
                Dictionary<string, List<int>>
            >();

            Assert.Greater(nestedListSize, 0, "List<List<int>> should have positive size");
            Assert.Greater(
                nestedDictSize,
                0,
                "Dictionary<string, List<int>> should have positive size"
            );
        }

        [Test]
        public void CachingWorksCorrectlyForSameType()
        {
            // First call - populates cache
            int firstCall = PoolSizeEstimator.EstimateItemSizeBytes<SmallClass>();

            // Second call - should use cache
            int secondCall = PoolSizeEstimator.EstimateItemSizeBytes<SmallClass>();

            // Third call via Type overload - should also use cache
            int thirdCall = PoolSizeEstimator.EstimateItemSizeBytes(typeof(SmallClass));

            Assert.AreEqual(firstCall, secondCall, "Cached calls should return same value");
            Assert.AreEqual(firstCall, thirdCall, "Type overload should use same cache");
        }

        [Test]
        public void ZeroThresholdMakesEverythingLarge()
        {
            PoolPurgeSettings.LargeObjectThresholdBytes = 0;

            // With threshold of 0, everything should be considered large
            Assert.IsTrue(PoolPurgeSettings.IsLargeObject<int>());
            Assert.IsTrue(PoolPurgeSettings.IsLargeObject<byte>());
            Assert.IsTrue(PoolPurgeSettings.IsLargeObject<SmallClass>());
        }

        [Test]
        public void VeryHighThresholdMakesEverythingSmall()
        {
            PoolPurgeSettings.LargeObjectThresholdBytes = int.MaxValue;

            // With very high threshold, nothing should be considered large
            Assert.IsFalse(PoolPurgeSettings.IsLargeObject<int>());
            Assert.IsFalse(PoolPurgeSettings.IsLargeObject<List<int>>());
            Assert.IsFalse(PoolPurgeSettings.IsLargeObject<Dictionary<string, object>>());
        }

        // ========================================
        // Size Estimation Accuracy Tests for Test Types
        // ========================================

        [Test]
        public void EstimateItemSizeBytesReturnsReasonableSizeForGenuinelyLargeStruct()
        {
            int size = PoolSizeEstimator.EstimateItemSizeBytes<GenuinelyLargeStruct>();
            // GenuinelyLargeStruct has 128 longs (128 * 8 = 1024 bytes)
            Assert.GreaterOrEqual(
                size,
                1024,
                "GenuinelyLargeStruct should be at least 1024 bytes (128 longs)"
            );
        }

        [Test]
        public void EstimateItemSizeBytesReturnsPositiveSizeForStructWithArrayReference()
        {
            // StructWithArrayReference uses MarshalAs attribute which causes Marshal.SizeOf
            // to return the marshaled size (100,000 bytes = 25,000 * 4 bytes for int[]),
            // not the managed size. This is expected behavior for types with MarshalAs.
            int size = PoolSizeEstimator.EstimateItemSizeBytes<StructWithArrayReference>();
            Assert.Greater(size, 0, "StructWithArrayReference should have positive size");
            // The marshaled size is 25000 * 4 = 100000 bytes due to MarshalAs(ByValArray, SizeConst=25000)
            Assert.AreEqual(
                100000,
                size,
                "StructWithArrayReference should return marshaled size due to MarshalAs attribute"
            );
        }

        [Test]
        public void EstimateItemSizeBytesReturnsAccurateSizeForSmallStruct()
        {
            int size = PoolSizeEstimator.EstimateItemSizeBytes<SmallStruct>();
            // SmallStruct has 4 ints (4 * 4 = 16 bytes)
            Assert.AreEqual(16, size, "SmallStruct with 4 ints should be exactly 16 bytes");
        }

        [Test]
        public void GenuinelyLargeStructCanBeDetectedAsLargeWithLoweredThreshold()
        {
            // GenuinelyLargeStruct is 1024 bytes, well below the default 85KB LOH threshold
            // Lower the threshold to test detection
            PoolPurgeSettings.LargeObjectThresholdBytes = 500;

            Assert.IsTrue(
                PoolPurgeSettings.IsLargeObject<GenuinelyLargeStruct>(),
                "GenuinelyLargeStruct should be detected as large with threshold of 500 bytes"
            );
        }

        [Test]
        public void GenuinelyLargeStructIsNotLargeWithDefaultThreshold()
        {
            // At 1024 bytes, GenuinelyLargeStruct is well below the 85KB LOH threshold
            Assert.IsFalse(
                PoolPurgeSettings.IsLargeObject<GenuinelyLargeStruct>(),
                "GenuinelyLargeStruct at 1024 bytes should not exceed 85KB LOH threshold"
            );
        }

        [Test]
        public void SizeAwareOptionsAppliedForGenuinelyLargeStructWithLoweredThreshold()
        {
            // Lower threshold so GenuinelyLargeStruct is considered large
            PoolPurgeSettings.LargeObjectThresholdBytes = 500;

            PoolPurgeEffectiveOptions baseOptions =
                PoolPurgeSettings.GetEffectiveOptions<GenuinelyLargeStruct>();
            PoolPurgeEffectiveOptions sizeAwareOptions =
                PoolPurgeSettings.GetSizeAwareEffectiveOptions<GenuinelyLargeStruct>();

            // Size-aware options should have adjusted values for large objects
            float expectedIdleTimeout =
                baseOptions.IdleTimeoutSeconds * PoolPurgeSettings.LargeObjectIdleTimeoutMultiplier;
            Assert.AreEqual(expectedIdleTimeout, sizeAwareOptions.IdleTimeoutSeconds, 0.001f);
            Assert.AreEqual(
                PoolPurgeSettings.LargeObjectBufferMultiplier,
                sizeAwareOptions.BufferMultiplier,
                0.001f
            );
            Assert.AreEqual(
                PoolPurgeSettings.LargeObjectWarmRetainCount,
                sizeAwareOptions.WarmRetainCount
            );
        }

        /// <summary>
        /// A struct with a reference field for testing size estimation.
        /// Note: MarshalAs only affects P/Invoke marshalling, not managed size.
        /// The managed size is just a reference (pointer) to the array.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct StructWithArrayReference
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25000)]
            public int[] Data;
        }

        /// <summary>
        /// A small struct well below the LOH threshold.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct SmallStruct
        {
            public int Value1;
            public int Value2;
            public int Value3;
            public int Value4;
        }

        /// <summary>
        /// A class that wraps a large array.
        /// </summary>
        private sealed class LargeArrayWrapper
        {
            public byte[] Data { get; } = new byte[100000];
        }

        /// <summary>
        /// A simple small class.
        /// </summary>
        private sealed class SmallClass
        {
            public int Id { get; set; }
            public string Name { get; set; }
        }

        /// <summary>
        /// A genuinely large struct that will exceed common test thresholds.
        /// Uses multiple fields to ensure a known managed size without requiring unsafe code.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct GenuinelyLargeStruct
        {
            // 64 longs = 512 bytes
            public long L01,
                L02,
                L03,
                L04,
                L05,
                L06,
                L07,
                L08;
            public long L09,
                L10,
                L11,
                L12,
                L13,
                L14,
                L15,
                L16;
            public long L17,
                L18,
                L19,
                L20,
                L21,
                L22,
                L23,
                L24;
            public long L25,
                L26,
                L27,
                L28,
                L29,
                L30,
                L31,
                L32;
            public long L33,
                L34,
                L35,
                L36,
                L37,
                L38,
                L39,
                L40;
            public long L41,
                L42,
                L43,
                L44,
                L45,
                L46,
                L47,
                L48;
            public long L49,
                L50,
                L51,
                L52,
                L53,
                L54,
                L55,
                L56;
            public long L57,
                L58,
                L59,
                L60,
                L61,
                L62,
                L63,
                L64;

            // Another 64 longs = 512 bytes, total 1024 bytes
            public long M01,
                M02,
                M03,
                M04,
                M05,
                M06,
                M07,
                M08;
            public long M09,
                M10,
                M11,
                M12,
                M13,
                M14,
                M15,
                M16;
            public long M17,
                M18,
                M19,
                M20,
                M21,
                M22,
                M23,
                M24;
            public long M25,
                M26,
                M27,
                M28,
                M29,
                M30,
                M31,
                M32;
            public long M33,
                M34,
                M35,
                M36,
                M37,
                M38,
                M39,
                M40;
            public long M41,
                M42,
                M43,
                M44,
                M45,
                M46,
                M47,
                M48;
            public long M49,
                M50,
                M51,
                M52,
                M53,
                M54,
                M55,
                M56;
            public long M57,
                M58,
                M59,
                M60,
                M61,
                M62,
                M63,
                M64;
        }
    }
}
