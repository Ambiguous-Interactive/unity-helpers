// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Tests for P0.4 - Memory Pressure Detection feature.
    /// Verifies that memory pressure is tracked and purge aggressiveness scales accordingly.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class MemoryPressureTests
    {
        private float _currentTime;

        private float TestTimeProvider()
        {
            return _currentTime;
        }

        [SetUp]
        public void SetUp()
        {
            _currentTime = 0f;
            TestPoolItem.ResetIdCounter();
            MemoryPressureMonitor.Reset();
            PoolPurgeSettings.ResetToDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            MemoryPressureMonitor.Reset();
            PoolPurgeSettings.ResetToDefaults();
        }

        [Test]
        public void MemoryPressureLevelEnumHasExpectedValues()
        {
            Assert.AreEqual(0, (int)MemoryPressureLevel.None);
            Assert.AreEqual(1, (int)MemoryPressureLevel.Low);
            Assert.AreEqual(2, (int)MemoryPressureLevel.Medium);
            Assert.AreEqual(3, (int)MemoryPressureLevel.High);
            Assert.AreEqual(4, (int)MemoryPressureLevel.Critical);
        }

        [Test]
        public void MemoryPressureMonitorDefaultsAreCorrect()
        {
            Assert.AreEqual(
                512L * 1024 * 1024,
                MemoryPressureMonitor.DefaultMemoryPressureThresholdBytes
            );
            Assert.AreEqual(5f, MemoryPressureMonitor.DefaultCheckIntervalSeconds);
            Assert.AreEqual(2f, MemoryPressureMonitor.DefaultGCCollectionRateThreshold);
            Assert.AreEqual(
                50L * 1024 * 1024,
                MemoryPressureMonitor.DefaultMemoryGrowthRateThreshold
            );
        }

        [Test]
        public void MemoryPressureMonitorIsEnabledByDefault()
        {
            Assert.IsTrue(MemoryPressureMonitor.Enabled);
        }

        [Test]
        public void DisabledMonitorAlwaysReturnsNonePressure()
        {
            MemoryPressureMonitor.Enabled = false;
            MemoryPressureMonitor.ForceUpdate();

            Assert.AreEqual(MemoryPressureLevel.None, MemoryPressureMonitor.CurrentPressure);
        }

        [Test]
        public void CurrentPressureReturnsNoneWhenDisabled()
        {
            MemoryPressureMonitor.Enabled = false;

            Assert.AreEqual(MemoryPressureLevel.None, MemoryPressureMonitor.CurrentPressure);
        }

        [Test]
        public void MemoryPressureThresholdBytesCanBeConfigured()
        {
            long customThreshold = 256L * 1024 * 1024;
            MemoryPressureMonitor.MemoryPressureThresholdBytes = customThreshold;

            Assert.AreEqual(customThreshold, MemoryPressureMonitor.MemoryPressureThresholdBytes);
        }

        [Test]
        public void InvalidMemoryPressureThresholdUsesDefault()
        {
            MemoryPressureMonitor.MemoryPressureThresholdBytes = 0;

            Assert.AreEqual(
                MemoryPressureMonitor.DefaultMemoryPressureThresholdBytes,
                MemoryPressureMonitor.MemoryPressureThresholdBytes
            );

            MemoryPressureMonitor.MemoryPressureThresholdBytes = -100;

            Assert.AreEqual(
                MemoryPressureMonitor.DefaultMemoryPressureThresholdBytes,
                MemoryPressureMonitor.MemoryPressureThresholdBytes
            );
        }

        [Test]
        public void CheckIntervalSecondsCanBeConfigured()
        {
            float customInterval = 2.5f;
            MemoryPressureMonitor.CheckIntervalSeconds = customInterval;

            Assert.AreEqual(customInterval, MemoryPressureMonitor.CheckIntervalSeconds);
        }

        [Test]
        public void InvalidCheckIntervalUsesDefault()
        {
            MemoryPressureMonitor.CheckIntervalSeconds = 0f;

            Assert.AreEqual(
                MemoryPressureMonitor.DefaultCheckIntervalSeconds,
                MemoryPressureMonitor.CheckIntervalSeconds
            );

            MemoryPressureMonitor.CheckIntervalSeconds = -1f;

            Assert.AreEqual(
                MemoryPressureMonitor.DefaultCheckIntervalSeconds,
                MemoryPressureMonitor.CheckIntervalSeconds
            );
        }

        [Test]
        public void GCCollectionRateThresholdCanBeConfigured()
        {
            float customRate = 5f;
            MemoryPressureMonitor.GCCollectionRateThreshold = customRate;

            Assert.AreEqual(customRate, MemoryPressureMonitor.GCCollectionRateThreshold);
        }

        [Test]
        public void InvalidGCCollectionRateThresholdUsesDefault()
        {
            MemoryPressureMonitor.GCCollectionRateThreshold = 0f;

            Assert.AreEqual(
                MemoryPressureMonitor.DefaultGCCollectionRateThreshold,
                MemoryPressureMonitor.GCCollectionRateThreshold
            );
        }

        [Test]
        public void MemoryGrowthRateThresholdCanBeConfigured()
        {
            long customRate = 100L * 1024 * 1024;
            MemoryPressureMonitor.MemoryGrowthRateThreshold = customRate;

            Assert.AreEqual(customRate, MemoryPressureMonitor.MemoryGrowthRateThreshold);
        }

        [Test]
        public void InvalidMemoryGrowthRateThresholdUsesDefault()
        {
            MemoryPressureMonitor.MemoryGrowthRateThreshold = 0;

            Assert.AreEqual(
                MemoryPressureMonitor.DefaultMemoryGrowthRateThreshold,
                MemoryPressureMonitor.MemoryGrowthRateThreshold
            );
        }

        [Test]
        public void ForceUpdateReturnsCurrentPressureLevel()
        {
            MemoryPressureLevel level = MemoryPressureMonitor.ForceUpdate();

            Assert.That(level, Is.TypeOf<MemoryPressureLevel>());
        }

        [Test]
        public void LastTotalMemoryIsTracked()
        {
            MemoryPressureMonitor.ForceUpdate();

            Assert.GreaterOrEqual(MemoryPressureMonitor.LastTotalMemory, 0L);
        }

        [Test]
        public void LastGCCountIsTracked()
        {
            MemoryPressureMonitor.ForceUpdate();

            Assert.GreaterOrEqual(MemoryPressureMonitor.LastGCCount, 0);
        }

        [Test]
        public void UpdateIsIdempotentWithinCheckInterval()
        {
            MemoryPressureMonitor.CheckIntervalSeconds = 10f;
            MemoryPressureMonitor.ForceUpdate();

            MemoryPressureLevel levelAfterForce = MemoryPressureMonitor.CurrentPressure;

            MemoryPressureMonitor.Update();
            MemoryPressureMonitor.Update();
            MemoryPressureMonitor.Update();

            Assert.AreEqual(levelAfterForce, MemoryPressureMonitor.CurrentPressure);
        }

        [Test]
        public void ResetClearsAllState()
        {
            MemoryPressureMonitor.Enabled = false;
            MemoryPressureMonitor.MemoryPressureThresholdBytes = 100;
            MemoryPressureMonitor.CheckIntervalSeconds = 1f;

            MemoryPressureMonitor.Reset();

            Assert.IsTrue(MemoryPressureMonitor.Enabled);
            Assert.AreEqual(
                MemoryPressureMonitor.DefaultMemoryPressureThresholdBytes,
                MemoryPressureMonitor.MemoryPressureThresholdBytes
            );
            Assert.AreEqual(
                MemoryPressureMonitor.DefaultCheckIntervalSeconds,
                MemoryPressureMonitor.CheckIntervalSeconds
            );
        }

        [Test]
        public void PoolUsageTrackerGetComfortableSizeRespectsLowPressure()
        {
            PoolUsageTracker tracker = new(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 3.0f
            );

            tracker.RecordRent(0f);
            tracker.RecordReturn(0f);
            tracker.RecordRent(0f);
            tracker.RecordReturn(0f);

            int sizeNone = tracker.GetComfortableSize(1f, 0, MemoryPressureLevel.None);
            int sizeLow = tracker.GetComfortableSize(1f, 0, MemoryPressureLevel.Low);

            Assert.Greater(sizeNone, sizeLow, "Low pressure should reduce buffer multiplier");
        }

        [Test]
        public void PoolUsageTrackerGetComfortableSizeRespectsMediumPressure()
        {
            PoolUsageTracker tracker = new(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 3.0f
            );

            tracker.RecordRent(0f);
            tracker.RecordReturn(0f);
            tracker.RecordRent(0f);
            tracker.RecordReturn(0f);

            int sizeLow = tracker.GetComfortableSize(1f, 0, MemoryPressureLevel.Low);
            int sizeMedium = tracker.GetComfortableSize(1f, 0, MemoryPressureLevel.Medium);

            Assert.GreaterOrEqual(
                sizeLow,
                sizeMedium,
                "Medium pressure should reduce buffer more than Low"
            );
        }

        [Test]
        public void PoolUsageTrackerGetComfortableSizeReturnsMinRetainAtHighPressure()
        {
            PoolUsageTracker tracker = new(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 3.0f
            );

            for (int i = 0; i < 10; i++)
            {
                tracker.RecordRent((float)i);
                tracker.RecordReturn((float)i + 0.5f);
            }

            const int minRetain = 5;
            int sizeHigh = tracker.GetComfortableSize(15f, minRetain, MemoryPressureLevel.High);
            int sizeCritical = tracker.GetComfortableSize(
                15f,
                minRetain,
                MemoryPressureLevel.Critical
            );

            Assert.AreEqual(
                minRetain,
                sizeHigh,
                "High pressure should return effective min retain"
            );
            Assert.AreEqual(
                minRetain,
                sizeCritical,
                "Critical pressure should return effective min retain"
            );
        }

        [Test]
        public void PoolUsageTrackerGetEffectiveMinRetainCountIgnoresWarmAtMediumPressure()
        {
            PoolUsageTracker tracker = new(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 2.0f
            );

            tracker.RecordRent(0f);
            tracker.RecordReturn(0f);

            const int minRetain = 2;
            const int warmRetain = 10;
            const float idleTimeout = 300f;

            int effectiveNone = tracker.GetEffectiveMinRetainCount(
                1f,
                idleTimeout,
                minRetain,
                warmRetain,
                MemoryPressureLevel.None
            );

            int effectiveMedium = tracker.GetEffectiveMinRetainCount(
                1f,
                idleTimeout,
                minRetain,
                warmRetain,
                MemoryPressureLevel.Medium
            );

            Assert.AreEqual(
                warmRetain,
                effectiveNone,
                "No pressure should use warm retain for active pool"
            );
            Assert.AreEqual(
                minRetain,
                effectiveMedium,
                "Medium pressure should ignore warm retain"
            );
        }

        [Test]
        public void PoolUsageTrackerGetEffectiveMinRetainCountUsesMinAtHighPressure()
        {
            PoolUsageTracker tracker = new(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 2.0f
            );

            tracker.RecordRent(0f);
            tracker.RecordReturn(0f);

            const int minRetain = 1;
            const int warmRetain = 20;
            const float idleTimeout = 300f;

            int effectiveHigh = tracker.GetEffectiveMinRetainCount(
                1f,
                idleTimeout,
                minRetain,
                warmRetain,
                MemoryPressureLevel.High
            );

            int effectiveCritical = tracker.GetEffectiveMinRetainCount(
                1f,
                idleTimeout,
                minRetain,
                warmRetain,
                MemoryPressureLevel.Critical
            );

            Assert.AreEqual(minRetain, effectiveHigh, "High pressure should use min retain");
            Assert.AreEqual(
                minRetain,
                effectiveCritical,
                "Critical pressure should use min retain"
            );
        }

        [Test]
        public void LowPressureKeepsWarmRetainForActivePools()
        {
            PoolUsageTracker tracker = new(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 2.0f
            );

            tracker.RecordRent(0f);
            tracker.RecordReturn(0f);

            const int minRetain = 0;
            const int warmRetain = 5;
            const float idleTimeout = 300f;

            int effectiveLow = tracker.GetEffectiveMinRetainCount(
                1f,
                idleTimeout,
                minRetain,
                warmRetain,
                MemoryPressureLevel.Low
            );

            Assert.AreEqual(warmRetain, effectiveLow, "Low pressure should still use warm retain");
        }

        [Test]
        public void MemoryPressureMonitorUpdateDoesNotThrow()
        {
            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 100; i++)
                {
                    MemoryPressureMonitor.Update();
                }
            });
        }

        [Test]
        public void ForceUpdateDoesNotThrowWhenDisabled()
        {
            MemoryPressureMonitor.Enabled = false;

            Assert.DoesNotThrow(() =>
            {
                MemoryPressureLevel level = MemoryPressureMonitor.ForceUpdate();
                Assert.AreEqual(MemoryPressureLevel.None, level);
            });
        }

        [Test]
        public void PoolIntegrationMemoryPressureAffectsPurging()
        {
            const int preWarmCount = 20;
            List<PurgeReason> purgeReasons = new();

            MemoryPressureMonitor.Enabled = true;
            MemoryPressureMonitor.MemoryPressureThresholdBytes = long.MaxValue;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: preWarmCount,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 1f,
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                    OnPurge = (_, reason) => purgeReasons.Add(reason),
                }
            );

            Assert.AreEqual(preWarmCount, pool.Count);

            _currentTime = 2f;

            pool.Purge();

            Assert.Less(pool.Count, preWarmCount, "Pool should purge some items");
        }

        [Test]
        public void BufferMultiplierCappingAtLowPressure()
        {
            PoolUsageTracker tracker = new(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 10.0f
            );

            for (int i = 0; i < 5; i++)
            {
                tracker.RecordRent((float)i);
            }

            for (int i = 0; i < 5; i++)
            {
                tracker.RecordReturn((float)i + 0.5f);
            }

            int sizeNone = tracker.GetComfortableSize(6f, 0, MemoryPressureLevel.None);
            int sizeLow = tracker.GetComfortableSize(6f, 0, MemoryPressureLevel.Low);

            Assert.Greater(sizeNone, sizeLow, "Low pressure should cap buffer at 1.5x");
        }

        [Test]
        public void BufferMultiplierCappingAtMediumPressure()
        {
            PoolUsageTracker tracker = new(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 10.0f
            );

            for (int i = 0; i < 5; i++)
            {
                tracker.RecordRent((float)i);
            }

            for (int i = 0; i < 5; i++)
            {
                tracker.RecordReturn((float)i + 0.5f);
            }

            int sizeLow = tracker.GetComfortableSize(6f, 0, MemoryPressureLevel.Low);
            int sizeMedium = tracker.GetComfortableSize(6f, 0, MemoryPressureLevel.Medium);

            Assert.Greater(sizeLow, sizeMedium, "Medium pressure should cap buffer at 1.0x");
        }

        [TestCase(0.0f, 0.0f, 0.0f, MemoryPressureLevel.None)]
        [TestCase(0.5f, 0.0f, 0.0f, MemoryPressureLevel.None)]
        [TestCase(0.74f, 0.0f, 0.0f, MemoryPressureLevel.None)]
        [TestCase(0.75f, 0.0f, 0.0f, MemoryPressureLevel.Low)]
        [TestCase(0.85f, 0.0f, 0.0f, MemoryPressureLevel.Low)]
        [TestCase(0.89f, 0.0f, 0.0f, MemoryPressureLevel.Low)]
        [TestCase(0.9f, 0.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.95f, 0.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.99f, 0.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(1.0f, 0.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(1.1f, 0.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(1.24f, 0.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(1.25f, 0.0f, 0.0f, MemoryPressureLevel.High)]
        [TestCase(1.5f, 0.0f, 0.0f, MemoryPressureLevel.High)]
        [TestCase(2.0f, 0.0f, 0.0f, MemoryPressureLevel.High)]
        public void PressureCalculationMemoryRatioOnlyReturnsExpectedLevel(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier,
            MemoryPressureLevel expectedLevel
        )
        {
            MemoryPressureLevel actualLevel = MemoryPressureMonitor.CalculatePressureFromMetrics(
                memoryRatio,
                gcRateMultiplier,
                growthRateMultiplier
            );

            Assert.AreEqual(expectedLevel, actualLevel);
        }

        [TestCase(0.0f, 0.0f, 0.0f, MemoryPressureLevel.None)]
        [TestCase(0.0f, 0.5f, 0.0f, MemoryPressureLevel.None)]
        [TestCase(0.0f, 0.99f, 0.0f, MemoryPressureLevel.None)]
        [TestCase(0.0f, 1.0f, 0.0f, MemoryPressureLevel.Low)]
        [TestCase(0.0f, 1.5f, 0.0f, MemoryPressureLevel.Low)]
        [TestCase(0.0f, 2.9f, 0.0f, MemoryPressureLevel.Low)]
        [TestCase(0.0f, 3.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.0f, 5.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.0f, 10.0f, 0.0f, MemoryPressureLevel.Medium)]
        public void PressureCalculationGCRateOnlyReturnsExpectedLevel(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier,
            MemoryPressureLevel expectedLevel
        )
        {
            MemoryPressureLevel actualLevel = MemoryPressureMonitor.CalculatePressureFromMetrics(
                memoryRatio,
                gcRateMultiplier,
                growthRateMultiplier
            );

            Assert.AreEqual(expectedLevel, actualLevel);
        }

        [TestCase(0.0f, 0.0f, 0.0f, MemoryPressureLevel.None)]
        [TestCase(0.0f, 0.0f, 0.5f, MemoryPressureLevel.None)]
        [TestCase(0.0f, 0.0f, 0.99f, MemoryPressureLevel.None)]
        [TestCase(0.0f, 0.0f, 1.0f, MemoryPressureLevel.Low)]
        [TestCase(0.0f, 0.0f, 1.5f, MemoryPressureLevel.Low)]
        [TestCase(0.0f, 0.0f, 1.99f, MemoryPressureLevel.Low)]
        [TestCase(0.0f, 0.0f, 2.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.0f, 0.0f, 3.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.0f, 0.0f, 10.0f, MemoryPressureLevel.Medium)]
        public void PressureCalculationGrowthRateOnlyReturnsExpectedLevel(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier,
            MemoryPressureLevel expectedLevel
        )
        {
            MemoryPressureLevel actualLevel = MemoryPressureMonitor.CalculatePressureFromMetrics(
                memoryRatio,
                gcRateMultiplier,
                growthRateMultiplier
            );

            Assert.AreEqual(expectedLevel, actualLevel);
        }

        [TestCase(0.75f, 1.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.9f, 1.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(1.0f, 1.0f, 0.0f, MemoryPressureLevel.High)]
        [TestCase(1.25f, 1.0f, 0.0f, MemoryPressureLevel.High)]
        [TestCase(0.75f, 3.0f, 0.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.9f, 3.0f, 0.0f, MemoryPressureLevel.High)]
        [TestCase(1.0f, 3.0f, 0.0f, MemoryPressureLevel.High)]
        [TestCase(1.25f, 3.0f, 0.0f, MemoryPressureLevel.Critical)]
        public void PressureCalculationMemoryAndGCRateCombinedReturnsExpectedLevel(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier,
            MemoryPressureLevel expectedLevel
        )
        {
            MemoryPressureLevel actualLevel = MemoryPressureMonitor.CalculatePressureFromMetrics(
                memoryRatio,
                gcRateMultiplier,
                growthRateMultiplier
            );

            Assert.AreEqual(expectedLevel, actualLevel);
        }

        [TestCase(0.75f, 0.0f, 1.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.9f, 0.0f, 1.0f, MemoryPressureLevel.Medium)]
        [TestCase(1.0f, 0.0f, 1.0f, MemoryPressureLevel.High)]
        [TestCase(1.25f, 0.0f, 1.0f, MemoryPressureLevel.High)]
        [TestCase(0.75f, 0.0f, 2.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.9f, 0.0f, 2.0f, MemoryPressureLevel.High)]
        [TestCase(1.0f, 0.0f, 2.0f, MemoryPressureLevel.High)]
        [TestCase(1.25f, 0.0f, 2.0f, MemoryPressureLevel.Critical)]
        public void PressureCalculationMemoryAndGrowthRateCombinedReturnsExpectedLevel(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier,
            MemoryPressureLevel expectedLevel
        )
        {
            MemoryPressureLevel actualLevel = MemoryPressureMonitor.CalculatePressureFromMetrics(
                memoryRatio,
                gcRateMultiplier,
                growthRateMultiplier
            );

            Assert.AreEqual(expectedLevel, actualLevel);
        }

        [TestCase(0.0f, 1.0f, 1.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.0f, 3.0f, 1.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.0f, 1.0f, 2.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.0f, 3.0f, 2.0f, MemoryPressureLevel.High)]
        public void PressureCalculationGCAndGrowthRateCombinedReturnsExpectedLevel(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier,
            MemoryPressureLevel expectedLevel
        )
        {
            MemoryPressureLevel actualLevel = MemoryPressureMonitor.CalculatePressureFromMetrics(
                memoryRatio,
                gcRateMultiplier,
                growthRateMultiplier
            );

            Assert.AreEqual(expectedLevel, actualLevel);
        }

        [TestCase(0.75f, 1.0f, 1.0f, MemoryPressureLevel.Medium)]
        [TestCase(0.9f, 1.0f, 1.0f, MemoryPressureLevel.High)]
        [TestCase(1.0f, 1.0f, 1.0f, MemoryPressureLevel.High)]
        [TestCase(1.25f, 1.0f, 1.0f, MemoryPressureLevel.Critical)]
        [TestCase(0.75f, 3.0f, 2.0f, MemoryPressureLevel.High)]
        [TestCase(0.9f, 3.0f, 2.0f, MemoryPressureLevel.Critical)]
        [TestCase(1.0f, 3.0f, 2.0f, MemoryPressureLevel.Critical)]
        [TestCase(1.25f, 3.0f, 2.0f, MemoryPressureLevel.Critical)]
        public void PressureCalculationAllFactorsCombinedReturnsExpectedLevel(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier,
            MemoryPressureLevel expectedLevel
        )
        {
            MemoryPressureLevel actualLevel = MemoryPressureMonitor.CalculatePressureFromMetrics(
                memoryRatio,
                gcRateMultiplier,
                growthRateMultiplier
            );

            Assert.AreEqual(expectedLevel, actualLevel);
        }

        [TestCase(-0.5f, 0.0f, 0.0f, MemoryPressureLevel.None)]
        [TestCase(0.0f, -1.0f, 0.0f, MemoryPressureLevel.None)]
        [TestCase(0.0f, 0.0f, -1.0f, MemoryPressureLevel.None)]
        [TestCase(-1.0f, -1.0f, -1.0f, MemoryPressureLevel.None)]
        public void PressureCalculationNegativeValuesReturnNone(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier,
            MemoryPressureLevel expectedLevel
        )
        {
            MemoryPressureLevel actualLevel = MemoryPressureMonitor.CalculatePressureFromMetrics(
                memoryRatio,
                gcRateMultiplier,
                growthRateMultiplier
            );

            Assert.AreEqual(expectedLevel, actualLevel);
        }

        [TestCase(10.0f, 100.0f, 50.0f, MemoryPressureLevel.Critical)]
        [TestCase(5.0f, 10.0f, 10.0f, MemoryPressureLevel.Critical)]
        public void PressureCalculationExtremeValuesReturnCritical(
            float memoryRatio,
            float gcRateMultiplier,
            float growthRateMultiplier,
            MemoryPressureLevel expectedLevel
        )
        {
            MemoryPressureLevel actualLevel = MemoryPressureMonitor.CalculatePressureFromMetrics(
                memoryRatio,
                gcRateMultiplier,
                growthRateMultiplier
            );

            Assert.AreEqual(expectedLevel, actualLevel);
        }

        [Test]
        public void ConcurrentUpdateFromMultipleThreadsDoesNotThrow()
        {
            const int threadCount = 10;
            const int iterationsPerThread = 100;

            MemoryPressureMonitor.CheckIntervalSeconds = 0.001f;

            ManualResetEvent startEvent = new(false);
            int readyCount = 0;
            List<Exception> exceptions = new();
            object exceptionLock = new();

            Thread[] threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        Interlocked.Increment(ref readyCount);
                        startEvent.WaitOne();

                        for (int j = 0; j < iterationsPerThread; j++)
                        {
                            MemoryPressureMonitor.Update();
                            Thread.SpinWait(10);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptionLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            while (Volatile.Read(ref readyCount) < threadCount)
            {
                Thread.Sleep(1);
            }

            startEvent.Set();

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.IsEmpty(exceptions, "Concurrent Update calls should not throw");
        }

        [Test]
        public void ConcurrentCurrentPressureAccessWhileUpdatingDoesNotThrow()
        {
            const int threadCount = 8;
            const int iterationsPerThread = 200;

            MemoryPressureMonitor.CheckIntervalSeconds = 0.001f;

            ManualResetEvent startEvent = new(false);
            int readyCount = 0;
            List<Exception> exceptions = new();
            object exceptionLock = new();
            int readerThreads = threadCount / 2;
            int writerThreads = threadCount - readerThreads;

            Thread[] threads = new Thread[threadCount];

            for (int i = 0; i < writerThreads; i++)
            {
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        Interlocked.Increment(ref readyCount);
                        startEvent.WaitOne();

                        for (int j = 0; j < iterationsPerThread; j++)
                        {
                            MemoryPressureMonitor.Update();
                            MemoryPressureMonitor.ForceUpdate();
                            Thread.SpinWait(5);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptionLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            for (int i = writerThreads; i < threadCount; i++)
            {
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        Interlocked.Increment(ref readyCount);
                        startEvent.WaitOne();

                        for (int j = 0; j < iterationsPerThread; j++)
                        {
                            MemoryPressureLevel pressure = MemoryPressureMonitor.CurrentPressure;
                            Assert.That(
                                pressure,
                                Is.GreaterThanOrEqualTo(MemoryPressureLevel.None)
                                    .And.LessThanOrEqualTo(MemoryPressureLevel.Critical)
                            );
                            Thread.SpinWait(3);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptionLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            while (Volatile.Read(ref readyCount) < threadCount)
            {
                Thread.Sleep(1);
            }

            startEvent.Set();

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.IsEmpty(
                exceptions,
                "Concurrent CurrentPressure reads while Update is running should not throw"
            );
        }

        [Test]
        public void ConcurrentEnabledToggleFromMultipleThreadsDoesNotThrow()
        {
            const int threadCount = 10;
            const int iterationsPerThread = 500;

            ManualResetEvent startEvent = new(false);
            int readyCount = 0;
            List<Exception> exceptions = new();
            object exceptionLock = new();

            Thread[] threads = new Thread[threadCount];
            for (int i = 0; i < threadCount; i++)
            {
                int threadIndex = i;
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        Interlocked.Increment(ref readyCount);
                        startEvent.WaitOne();

                        for (int j = 0; j < iterationsPerThread; j++)
                        {
                            bool expectedValue = (threadIndex + j) % 2 == 0;
                            MemoryPressureMonitor.Enabled = expectedValue;

                            bool actualValue = MemoryPressureMonitor.Enabled;
                            Assert.That(actualValue, Is.TypeOf<bool>());

                            Thread.SpinWait(2);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptionLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            while (Volatile.Read(ref readyCount) < threadCount)
            {
                Thread.Sleep(1);
            }

            startEvent.Set();

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.IsEmpty(exceptions, "Concurrent Enabled toggle should not throw");
        }

        [Test]
        public void ConcurrentMixedOperationsDoNotThrow()
        {
            const int threadCount = 12;
            const int iterationsPerThread = 150;

            MemoryPressureMonitor.CheckIntervalSeconds = 0.001f;

            ManualResetEvent startEvent = new(false);
            int readyCount = 0;
            List<Exception> exceptions = new();
            object exceptionLock = new();

            Thread[] threads = new Thread[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                int operation = i % 4;
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        Interlocked.Increment(ref readyCount);
                        startEvent.WaitOne();

                        for (int j = 0; j < iterationsPerThread; j++)
                        {
                            switch (operation)
                            {
                                case 0:
                                    MemoryPressureMonitor.Update();
                                    break;
                                case 1:
                                    MemoryPressureMonitor.ForceUpdate();
                                    break;
                                case 2:
                                    MemoryPressureLevel _ = MemoryPressureMonitor.CurrentPressure;
                                    break;
                                case 3:
                                    MemoryPressureMonitor.Enabled = j % 2 == 0;
                                    break;
                            }
                            Thread.SpinWait(5);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptionLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            while (Volatile.Read(ref readyCount) < threadCount)
            {
                Thread.Sleep(1);
            }

            startEvent.Set();

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.IsEmpty(exceptions, "Concurrent mixed operations should not throw");
        }

        [Test]
        public void ConcurrentResetWhileUpdatingDoesNotThrow()
        {
            const int threadCount = 6;
            const int iterationsPerThread = 100;

            MemoryPressureMonitor.CheckIntervalSeconds = 0.001f;

            ManualResetEvent startEvent = new(false);
            int readyCount = 0;
            List<Exception> exceptions = new();
            object exceptionLock = new();

            Thread[] threads = new Thread[threadCount];

            for (int i = 0; i < threadCount / 2; i++)
            {
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        Interlocked.Increment(ref readyCount);
                        startEvent.WaitOne();

                        for (int j = 0; j < iterationsPerThread; j++)
                        {
                            MemoryPressureMonitor.Update();
                            MemoryPressureMonitor.ForceUpdate();
                            Thread.SpinWait(10);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptionLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            for (int i = threadCount / 2; i < threadCount; i++)
            {
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        Interlocked.Increment(ref readyCount);
                        startEvent.WaitOne();

                        for (int j = 0; j < iterationsPerThread; j++)
                        {
                            MemoryPressureMonitor.Reset();
                            Thread.SpinWait(20);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptionLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            while (Volatile.Read(ref readyCount) < threadCount)
            {
                Thread.Sleep(1);
            }

            startEvent.Set();

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.IsEmpty(exceptions, "Concurrent Reset while updating should not throw");
        }

        [Test]
        public void ConcurrentPropertyAccessDoesNotThrow()
        {
            const int threadCount = 8;
            const int iterationsPerThread = 200;

            ManualResetEvent startEvent = new(false);
            int readyCount = 0;
            List<Exception> exceptions = new();
            object exceptionLock = new();

            Thread[] threads = new Thread[threadCount];

            for (int i = 0; i < threadCount; i++)
            {
                int threadIndex = i;
                threads[i] = new Thread(() =>
                {
                    try
                    {
                        Interlocked.Increment(ref readyCount);
                        startEvent.WaitOne();

                        for (int j = 0; j < iterationsPerThread; j++)
                        {
                            switch (threadIndex % 4)
                            {
                                case 0:
                                    MemoryPressureMonitor.MemoryPressureThresholdBytes =
                                        (256L + j) * 1024 * 1024;
                                    long _ = MemoryPressureMonitor.MemoryPressureThresholdBytes;
                                    break;
                                case 1:
                                    MemoryPressureMonitor.CheckIntervalSeconds = 1f + (j * 0.01f);
                                    float __ = MemoryPressureMonitor.CheckIntervalSeconds;
                                    break;
                                case 2:
                                    MemoryPressureMonitor.GCCollectionRateThreshold =
                                        1f + (j * 0.1f);
                                    float ___ = MemoryPressureMonitor.GCCollectionRateThreshold;
                                    break;
                                case 3:
                                    MemoryPressureMonitor.MemoryGrowthRateThreshold =
                                        (25L + j) * 1024 * 1024;
                                    long ____ = MemoryPressureMonitor.MemoryGrowthRateThreshold;
                                    break;
                            }
                            Thread.SpinWait(3);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (exceptionLock)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });
                threads[i].Start();
            }

            while (Volatile.Read(ref readyCount) < threadCount)
            {
                Thread.Sleep(1);
            }

            startEvent.Set();

            foreach (Thread thread in threads)
            {
                thread.Join();
            }

            Assert.IsEmpty(exceptions, "Concurrent property reads and writes should not throw");
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
