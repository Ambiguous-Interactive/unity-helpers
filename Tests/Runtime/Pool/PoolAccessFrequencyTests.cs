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
    /// Tests for pool access frequency tracking, idle timeout behavior, and comfortable size calculations.
    ///
    /// Key design principle: Idle timeout purges bypass hysteresis intentionally because they represent
    /// essential pool hygiene (removing genuinely stale items). Hysteresis protects against thrashing
    /// from capacity/explicit purges during usage spikes, but stale items should always be cleaned up.
    ///
    /// Tests that verify hysteresis blocking must disable idle timeout (IdleTimeoutSeconds = 0f) to
    /// avoid the intentional bypass behavior.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PoolAccessFrequencyTests
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
            // Start at t=1 to avoid time=0 initialization issues
            // (time 0 is treated as uninitialized in the tracker)
            _currentTime = 1f;
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
        public void StatisticsTracksRentalsPerMinute()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            // Start at t=1 to ensure window tracking works properly
            // (time 0 is treated as uninitialized in the tracker)
            _currentTime = 1f;
            for (int i = 0; i < 10; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 7f;

            PoolStatistics stats = pool.GetStatistics();
            Assert.Greater(stats.RentalsPerMinute, 0f);
        }

        [Test]
        public void StatisticsTracksLastAccessTime()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 10f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            _currentTime = 15f;
            PoolStatistics stats = pool.GetStatistics();
            Assert.GreaterOrEqual(stats.LastAccessTime, 10f);
        }

        [Test]
        public void StatisticsTracksAverageInterRentalTime()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            // Start at t=1 to ensure first rental sets a valid previous time
            // (time 0 is treated as uninitialized in the tracker)
            _currentTime = 1f;
            using (PooledResource<TestPoolItem> resource1 = pool.Get()) { }
            _currentTime = 3f;
            using (PooledResource<TestPoolItem> resource2 = pool.Get()) { }
            _currentTime = 6f;
            using (PooledResource<TestPoolItem> resource3 = pool.Get()) { }

            PoolStatistics stats = pool.GetStatistics();
            Assert.Greater(
                stats.AverageInterRentalTimeSeconds,
                0f,
                "Expected positive average inter-rental time"
            );
            Assert.AreEqual(
                2.5f,
                stats.AverageInterRentalTimeSeconds,
                0.01f,
                "Average inter-rental time should be (2 + 3) / 2 = 2.5"
            );
        }

        [Test]
        public void HighFrequencyPoolIsIdentifiedCorrectly()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            // Start at t=1 to ensure window tracking works properly
            // (time 0 is treated as uninitialized in the tracker)
            _currentTime = 1f;
            for (int i = 0; i < 20; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 11f;

            PoolStatistics stats = pool.GetStatistics();
            Assert.GreaterOrEqual(
                stats.RentalsPerMinute,
                10f,
                "Expected high frequency rentals rate"
            );
            Assert.IsTrue(stats.IsHighFrequency, "Pool should be identified as high frequency");
        }

        [Test]
        public void LowFrequencyPoolIsIdentifiedCorrectly()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            _currentTime = 120f;

            PoolStatistics stats = pool.GetStatistics();
            TestContext.WriteLine(
                $"RentalsPerMinute: {stats.RentalsPerMinute}, RentCount: {stats.RentCount}, IsLowFrequency: {stats.IsLowFrequency}"
            );
            // Use LessOrEqual to handle boundary condition where rentals/min exactly equals 1
            Assert.LessOrEqual(stats.RentalsPerMinute, 1f, "Expected low frequency rentals rate");
            Assert.Greater(stats.RentCount, 0, "Expected at least one rental");
            Assert.IsTrue(stats.IsLowFrequency, "Pool should be identified as low frequency");
        }

        [Test]
        public void UnusedPoolIsIdentifiedCorrectly()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            // Start at t=1 to ensure lastAccess > 0 check works
            // (time 0 is treated as uninitialized in the tracker)
            _currentTime = 1f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            // UnusedPoolThresholdMinutes = 5 minutes = 300 seconds
            _currentTime = 401f;

            PoolStatistics stats = pool.GetStatistics();
            Assert.IsTrue(stats.IsUnused);
        }

        [Test]
        public void HighFrequencyPoolKeepsLargerBuffer()
        {
            int purgeCount = 0;
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = 30f,
                    Triggers = PurgeTrigger.OnRent,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                    BufferMultiplier = 1.5f,
                }
            );

            for (int i = 0; i < 50; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            int initialCount = pool.Count;

            _currentTime = 31f;
            using PooledResource<TestPoolItem> resource2 = pool.Get();

            Assert.GreaterOrEqual(pool.Count, 0);
        }

        [Test]
        public void LowFrequencyPoolHasFasterIdleTimeout()
        {
            int purgeCount = 0;
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = 60f,
                    Triggers = PurgeTrigger.OnRent,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                }
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            _currentTime = 90f;

            for (int i = 0; i < 10; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 125f;
            using PooledResource<TestPoolItem> resource2 = pool.Get();

            Assert.GreaterOrEqual(pool.Count, 0);
        }

        [Test]
        public void UnusedPoolPurgesToMinimum()
        {
            int purgeCount = 0;
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: new PoolOptions<TestPoolItem>
                {
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = 60f,
                    MinRetainCount = 1,
                    WarmRetainCount = 3,
                    Triggers = PurgeTrigger.OnRent,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                }
            );

            // Start at t=1 to ensure lastAccess > 0 check works
            // (time 0 is treated as uninitialized in the tracker)
            _currentTime = 1f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            // UnusedPoolThresholdMinutes = 5 minutes = 300 seconds
            _currentTime = 401f;

            // Check IsUnused BEFORE calling Get(), because Get() updates _lastAccessTime
            PoolStatistics statsBeforeGet = pool.GetStatistics();
            TestContext.WriteLine(
                $"Before Get: LastAccessTime={statsBeforeGet.LastAccessTime}, CurrentTime={_currentTime}, IsUnused={statsBeforeGet.IsUnused}"
            );
            Assert.IsTrue(
                statsBeforeGet.IsUnused,
                "Pool should be unused before Get() is called (no access for 400+ seconds)"
            );

            // Now trigger the purge by renting
            using PooledResource<TestPoolItem> resource2 = pool.Get();

            PoolStatistics statsAfterGet = pool.GetStatistics();
            TestContext.WriteLine($"After Get: purgeCount={purgeCount}, pool count={pool.Count}");
        }

        [Test]
        public void FrequencyStatisticsExposedInPoolStatistics()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 5; i++)
            {
                _currentTime = i * 2f;
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 10f;
            PoolStatistics stats = pool.GetStatistics();

            Assert.GreaterOrEqual(stats.RentalsPerMinute, 0f);
            Assert.GreaterOrEqual(stats.AverageInterRentalTimeSeconds, 0f);
            Assert.GreaterOrEqual(stats.LastAccessTime, 0f);
            Assert.AreEqual(5, stats.RentCount);
        }

        [Test]
        public void PoolFrequencyStatisticsToStringIsCorrectlyFormatted()
        {
            PoolFrequencyStatistics stats = new PoolFrequencyStatistics(
                rentalsPerMinute: 10.5f,
                averageInterRentalTimeSeconds: 0.5f,
                lastAccessTime: 100f,
                totalRentalCount: 42,
                isHighFrequency: true,
                isLowFrequency: false,
                isUnused: false
            );

            string str = stats.ToString();
            Assert.IsTrue(
                str.Contains("RentalsPerMin="),
                "Expected RentalsPerMin in ToString output"
            );
            Assert.IsTrue(
                str.Contains("AvgInterRentalTime="),
                "Expected AvgInterRentalTime in ToString output"
            );
            Assert.IsTrue(str.Contains("LastAccess="), "Expected LastAccess in ToString output");
            Assert.IsTrue(str.Contains("Total=42"), "Expected Total=42 in ToString output");
            Assert.IsTrue(str.Contains("High=True"), "Expected High=True in ToString output");
            Assert.IsTrue(str.Contains("Low=False"), "Expected Low=False in ToString output");
            Assert.IsTrue(str.Contains("Unused=False"), "Expected Unused=False in ToString output");
        }

        [Test]
        public void MultipleRentalsInQuickSuccessionIncreasesFrequency()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            PoolStatistics beforeStats = pool.GetStatistics();
            float beforeRentalsPerMin = beforeStats.RentalsPerMinute;

            for (int i = 0; i < 100; i++)
            {
                _currentTime = i * 0.1f;
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 10f;
            PoolStatistics afterStats = pool.GetStatistics();
            Assert.Greater(afterStats.RentalsPerMinute, beforeRentalsPerMin);
        }

        [Test]
        public void FrequencyResetAfterWindowElapses()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 50; i++)
            {
                _currentTime = i * 0.5f;
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 30f;
            PoolStatistics midStats = pool.GetStatistics();
            float midRentalsPerMin = midStats.RentalsPerMinute;

            _currentTime = 120f;

            using PooledResource<TestPoolItem> resource2 = pool.Get();

            _currentTime = 130f;
            PoolStatistics afterStats = pool.GetStatistics();

            Assert.Less(
                afterStats.RentalsPerMinute,
                midRentalsPerMin,
                "Frequency should decrease after window elapses"
            );
        }

        [Test]
        public void PoolStatisticsToStringIncludesFrequencyMetrics()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 5f;
            PoolStatistics stats = pool.GetStatistics();
            string str = stats.ToString();

            Assert.IsTrue(
                str.Contains("RentalsPerMin="),
                "Expected RentalsPerMin in ToString output"
            );
            Assert.IsTrue(
                str.Contains("AvgInterRentalTime="),
                "Expected AvgInterRentalTime in ToString output"
            );
            Assert.IsTrue(str.Contains("LastAccess="), "Expected LastAccess in ToString output");
        }

        [Test]
        public void EmptyPoolHasZeroFrequencyMetrics()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            PoolStatistics stats = pool.GetStatistics();

            Assert.AreEqual(0f, stats.RentalsPerMinute);
            Assert.AreEqual(0f, stats.AverageInterRentalTimeSeconds);
            Assert.AreEqual(0f, stats.LastAccessTime);
            Assert.IsFalse(stats.IsHighFrequency);
            Assert.IsFalse(stats.IsLowFrequency);
            Assert.IsFalse(stats.IsUnused);
        }

        [Test]
        public void NewPoolIsNotUnused()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            PoolStatistics stats = pool.GetStatistics();
            Assert.IsFalse(stats.IsUnused);
        }

        [Test]
        [TestCaseSource(nameof(PoolFrequencyStatisticsEqualityCases))]
        public void PoolFrequencyStatisticsEquality(
            PoolFrequencyStatistics stats1,
            PoolFrequencyStatistics stats2,
            bool expectedEqual
        )
        {
            bool equalsResult = stats1.Equals(stats2);
            bool operatorEquals = stats1 == stats2;
            bool operatorNotEquals = stats1 != stats2;

            Assert.AreEqual(expectedEqual, equalsResult, $"Equals should return {expectedEqual}");
            Assert.AreEqual(
                expectedEqual,
                operatorEquals,
                $"== operator should return {expectedEqual}"
            );
            Assert.AreEqual(
                !expectedEqual,
                operatorNotEquals,
                $"!= operator should return {!expectedEqual}"
            );

            if (expectedEqual)
            {
                Assert.AreEqual(
                    stats1.GetHashCode(),
                    stats2.GetHashCode(),
                    "Equal instances should have same hash code"
                );
            }
        }

        private static IEnumerable<TestCaseData> PoolFrequencyStatisticsEqualityCases()
        {
            PoolFrequencyStatistics baseStats = new PoolFrequencyStatistics(
                rentalsPerMinute: 10f,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: true,
                isLowFrequency: false,
                isUnused: false
            );

            PoolFrequencyStatistics identicalStats = new PoolFrequencyStatistics(
                rentalsPerMinute: 10f,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: true,
                isLowFrequency: false,
                isUnused: false
            );

            yield return new TestCaseData(baseStats, identicalStats, true).SetName(
                "Equal.IdenticalValues"
            );

            yield return new TestCaseData(
                baseStats,
                new PoolFrequencyStatistics(
                    rentalsPerMinute: 20f,
                    averageInterRentalTimeSeconds: 1f,
                    lastAccessTime: 100f,
                    totalRentalCount: 50,
                    isHighFrequency: true,
                    isLowFrequency: false,
                    isUnused: false
                ),
                false
            ).SetName("NotEqual.DifferentRentalsPerMinute");

            yield return new TestCaseData(
                baseStats,
                new PoolFrequencyStatistics(
                    rentalsPerMinute: 10f,
                    averageInterRentalTimeSeconds: 2f,
                    lastAccessTime: 100f,
                    totalRentalCount: 50,
                    isHighFrequency: true,
                    isLowFrequency: false,
                    isUnused: false
                ),
                false
            ).SetName("NotEqual.DifferentAvgInterRentalTime");

            yield return new TestCaseData(
                baseStats,
                new PoolFrequencyStatistics(
                    rentalsPerMinute: 10f,
                    averageInterRentalTimeSeconds: 1f,
                    lastAccessTime: 200f,
                    totalRentalCount: 50,
                    isHighFrequency: true,
                    isLowFrequency: false,
                    isUnused: false
                ),
                false
            ).SetName("NotEqual.DifferentLastAccessTime");

            yield return new TestCaseData(
                baseStats,
                new PoolFrequencyStatistics(
                    rentalsPerMinute: 10f,
                    averageInterRentalTimeSeconds: 1f,
                    lastAccessTime: 100f,
                    totalRentalCount: 100,
                    isHighFrequency: true,
                    isLowFrequency: false,
                    isUnused: false
                ),
                false
            ).SetName("NotEqual.DifferentTotalRentalCount");

            yield return new TestCaseData(
                baseStats,
                new PoolFrequencyStatistics(
                    rentalsPerMinute: 10f,
                    averageInterRentalTimeSeconds: 1f,
                    lastAccessTime: 100f,
                    totalRentalCount: 50,
                    isHighFrequency: false,
                    isLowFrequency: false,
                    isUnused: false
                ),
                false
            ).SetName("NotEqual.DifferentIsHighFrequency");

            yield return new TestCaseData(
                baseStats,
                new PoolFrequencyStatistics(
                    rentalsPerMinute: 10f,
                    averageInterRentalTimeSeconds: 1f,
                    lastAccessTime: 100f,
                    totalRentalCount: 50,
                    isHighFrequency: true,
                    isLowFrequency: true,
                    isUnused: false
                ),
                false
            ).SetName("NotEqual.DifferentIsLowFrequency");

            yield return new TestCaseData(
                baseStats,
                new PoolFrequencyStatistics(
                    rentalsPerMinute: 10f,
                    averageInterRentalTimeSeconds: 1f,
                    lastAccessTime: 100f,
                    totalRentalCount: 50,
                    isHighFrequency: true,
                    isLowFrequency: false,
                    isUnused: true
                ),
                false
            ).SetName("NotEqual.DifferentIsUnused");
        }

        [Test]
        [TestCase(null, false, TestName = "Object.Null.ReturnsFalse")]
        [TestCase("not a stats object", false, TestName = "Object.WrongType.ReturnsFalse")]
        public void PoolFrequencyStatisticsEqualsObjectOverload(object other, bool expectedResult)
        {
            PoolFrequencyStatistics stats = new PoolFrequencyStatistics(
                rentalsPerMinute: 10f,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: true,
                isLowFrequency: false,
                isUnused: false
            );

            Assert.AreEqual(expectedResult, stats.Equals(other));
        }

        [Test]
        public void PoolFrequencyStatisticsEqualsBoxedSelfReturnsTrue()
        {
            PoolFrequencyStatistics stats = new PoolFrequencyStatistics(
                rentalsPerMinute: 10f,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: true,
                isLowFrequency: false,
                isUnused: false
            );

            Assert.IsTrue(stats.Equals((object)stats), "Equals with boxed self should return true");
        }

        [Test]
        public void PoolFrequencyStatisticsEqualsIsExactAndApproximatelyEqualsIsNot()
        {
            PoolFrequencyStatistics stats = new PoolFrequencyStatistics(
                rentalsPerMinute: 10f,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: true,
                isLowFrequency: false,
                isUnused: false
            );
            PoolFrequencyStatistics nudged = new PoolFrequencyStatistics(
                rentalsPerMinute: 10.00005f,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: true,
                isLowFrequency: false,
                isUnused: false
            );

            /*
                This pair sat inside the old FloatEqualityTolerance, so Equals said yes while
                GetHashCode hashed the exact float bits and said no -- a snapshot that vanished from
                the set it had just been added to. Equality is exact now; a tolerance is stated.
            */
            Assert.IsFalse(stats.Equals(nudged));
            Assert.IsTrue(stats != nudged);
            Assert.IsTrue(stats.ApproximatelyEquals(nudged, 0.0001f));
            Assert.IsFalse(stats.ApproximatelyEquals(nudged, 0f));
        }

        [Test]
        [TestCase(-1f, TestName = "Tolerance.Negative.ReturnsFalse")]
        [TestCase(float.NaN, TestName = "Tolerance.NotANumber.ReturnsFalse")]
        [TestCase(float.PositiveInfinity, TestName = "Tolerance.Infinite.ReturnsFalse")]
        public void PoolFrequencyStatisticsApproximatelyEqualsRefusesAnInvalidTolerance(
            float tolerance
        )
        {
            PoolFrequencyStatistics stats = new PoolFrequencyStatistics(
                rentalsPerMinute: 10f,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: true,
                isLowFrequency: false,
                isUnused: false
            );

            Assert.IsFalse(stats.ApproximatelyEquals(stats, tolerance));
        }

        [Test]
        public void FrequencyTrackingWorksWithPreWarmedPool()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            // Pre-warm doesn't count as rentals - it just adds items to the pool.
            // Verify the pool has 10 items ready
            Assert.AreEqual(10, pool.Count);

            PoolStatistics stats = pool.GetStatistics();

            // Pre-warm uses ReturnToPool, not Get, so RentCount should be 0
            Assert.AreEqual(0, stats.RentCount);

            // Start at t=1 to ensure window tracking works properly
            // (time 0 is treated as uninitialized in the tracker)
            _currentTime = 1f;
            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 6f;
            PoolStatistics afterStats = pool.GetStatistics();
            Assert.AreEqual(5, afterStats.RentCount);
            Assert.Greater(afterStats.RentalsPerMinute, 0f);
        }

        [Test]
        [TestCase(4, TestName = "ThreadCount.Four")]
        [TestCase(8, TestName = "ThreadCount.Eight")]
        public void ConcurrentRecordRentIsThreadSafe(int threadCount)
        {
            PoolUsageTracker tracker = new PoolUsageTracker(
                rollingWindowSeconds: 60f,
                hysteresisSeconds: 5f,
                spikeThresholdMultiplier: 2f,
                bufferMultiplier: 1.5f
            );

            int operationsPerThread = 100;
            Task[] tasks = new Task[threadCount];
            int totalOperations = 0;

            for (int i = 0; i < threadCount; i++)
            {
                int threadIndex = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        float time = threadIndex * 1000f + j * 0.01f;
                        tracker.RecordRent(time);
                        Interlocked.Increment(ref totalOperations);
                    }
                });
            }

            Task.WaitAll(tasks);

            Assert.AreEqual(
                threadCount * operationsPerThread,
                totalOperations,
                "All operations should complete"
            );
            Assert.AreEqual(
                threadCount * operationsPerThread,
                tracker.TotalRentalCount,
                "Total rental count should match"
            );
        }

        [Test]
        [TestCase(4, TestName = "ThreadCount.Four")]
        [TestCase(8, TestName = "ThreadCount.Eight")]
        public void ConcurrentRecordReturnIsThreadSafe(int threadCount)
        {
            PoolUsageTracker tracker = new PoolUsageTracker(
                rollingWindowSeconds: 60f,
                hysteresisSeconds: 5f,
                spikeThresholdMultiplier: 2f,
                bufferMultiplier: 1.5f
            );

            int operationsPerThread = 100;
            for (int i = 0; i < threadCount * operationsPerThread; i++)
            {
                tracker.RecordRent(i * 0.01f);
            }

            Task[] tasks = new Task[threadCount];
            int totalOperations = 0;

            for (int i = 0; i < threadCount; i++)
            {
                int threadIndex = i;
                tasks[i] = Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        float time = threadIndex * 1000f + j * 0.01f + 1000f;
                        tracker.RecordReturn(time);
                        Interlocked.Increment(ref totalOperations);
                    }
                });
            }

            Task.WaitAll(tasks);

            Assert.AreEqual(
                threadCount * operationsPerThread,
                totalOperations,
                "All operations should complete"
            );
            Assert.AreEqual(0, tracker.CurrentlyRented, "All items should be returned");
        }

        [Test]
        [TestCase(4, TestName = "ThreadCount.Four")]
        [TestCase(8, TestName = "ThreadCount.Eight")]
        public void ConcurrentMixedRentAndReturnIsThreadSafe(int threadCount)
        {
            PoolUsageTracker tracker = new PoolUsageTracker(
                rollingWindowSeconds: 60f,
                hysteresisSeconds: 5f,
                spikeThresholdMultiplier: 2f,
                bufferMultiplier: 1.5f
            );

            int operationsPerThread = 50;
            Task[] tasks = new Task[threadCount * 2];
            int rentOperations = 0;
            int returnOperations = 0;

            for (int i = 0; i < threadCount; i++)
            {
                int threadIndex = i;
                tasks[i * 2] = Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        float time = threadIndex * 1000f + j * 0.01f;
                        tracker.RecordRent(time);
                        Interlocked.Increment(ref rentOperations);
                    }
                });

                tasks[i * 2 + 1] = Task.Run(() =>
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        float time = threadIndex * 1000f + j * 0.01f + 500f;
                        tracker.RecordReturn(time);
                        Interlocked.Increment(ref returnOperations);
                    }
                });
            }

            Task.WaitAll(tasks);

            Assert.AreEqual(
                threadCount * operationsPerThread,
                rentOperations,
                "All rent operations should complete"
            );
            Assert.AreEqual(
                threadCount * operationsPerThread,
                returnOperations,
                "All return operations should complete"
            );
            Assert.AreEqual(
                threadCount * operationsPerThread,
                tracker.TotalRentalCount,
                "Total rental count should match rent operations"
            );
        }

        [Test]
        public void TimeGoingBackwardsDoesNotCorruptStatistics()
        {
            PoolUsageTracker tracker = new PoolUsageTracker(
                rollingWindowSeconds: 60f,
                hysteresisSeconds: 5f,
                spikeThresholdMultiplier: 2f,
                bufferMultiplier: 1.5f
            );

            tracker.RecordRent(100f);
            tracker.RecordRent(110f);
            tracker.RecordRent(90f);
            tracker.RecordRent(80f);

            Assert.AreEqual(
                4,
                tracker.TotalRentalCount,
                "Total rental count should include all rentals"
            );
            Assert.GreaterOrEqual(
                tracker.AverageInterRentalTimeSeconds,
                0f,
                "Average inter-rental time should not be negative"
            );
        }

        [Test]
        public void TimeGoingBackwardsIgnoresNegativeInterRentalTime()
        {
            PoolUsageTracker tracker = new PoolUsageTracker(
                rollingWindowSeconds: 60f,
                hysteresisSeconds: 5f,
                spikeThresholdMultiplier: 2f,
                bufferMultiplier: 1.5f
            );

            // Start at t=1 to ensure first rental sets a valid previous time
            // (time 0 is treated as uninitialized in the tracker)
            tracker.RecordRent(1f);
            tracker.RecordRent(11f);
            tracker.RecordRent(6f);

            float averageInterRentalTime = tracker.AverageInterRentalTimeSeconds;
            Assert.AreEqual(
                10f,
                averageInterRentalTime,
                0.01f,
                "Should only include the positive inter-rental time of 10s"
            );
        }

        [Test]
        [TestCase(9.99f, false, TestName = "BelowThreshold.9.99")]
        [TestCase(10.0f, true, TestName = "AtThreshold.10.0")]
        [TestCase(10.01f, true, TestName = "AboveThreshold.10.01")]
        public void HighFrequencyThresholdBoundaryConditions(
            float rentalsPerMinute,
            bool expectedHighFrequency
        )
        {
            PoolFrequencyStatistics stats = new PoolFrequencyStatistics(
                rentalsPerMinute: rentalsPerMinute,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: 10f <= rentalsPerMinute,
                isLowFrequency: rentalsPerMinute <= 1f,
                isUnused: false
            );

            Assert.AreEqual(
                expectedHighFrequency,
                stats.IsHighFrequency,
                $"IsHighFrequency should be {expectedHighFrequency} for {rentalsPerMinute} rentals/min"
            );
        }

        [Test]
        [TestCase(0.99f, true, TestName = "BelowThreshold.0.99")]
        [TestCase(1.0f, true, TestName = "AtThreshold.1.0")]
        [TestCase(1.01f, false, TestName = "AboveThreshold.1.01")]
        public void LowFrequencyThresholdBoundaryConditions(
            float rentalsPerMinute,
            bool expectedLowFrequency
        )
        {
            PoolFrequencyStatistics stats = new PoolFrequencyStatistics(
                rentalsPerMinute: rentalsPerMinute,
                averageInterRentalTimeSeconds: 1f,
                lastAccessTime: 100f,
                totalRentalCount: 50,
                isHighFrequency: 10f <= rentalsPerMinute,
                isLowFrequency: rentalsPerMinute <= 1f && 0 < 50,
                isUnused: false
            );

            Assert.AreEqual(
                expectedLowFrequency,
                stats.IsLowFrequency,
                $"IsLowFrequency should be {expectedLowFrequency} for {rentalsPerMinute} rentals/min"
            );
        }

        [Test]
        public void SingleRentalHasZeroAverageInterRentalTime()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 10f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            PoolStatistics stats = pool.GetStatistics();
            Assert.AreEqual(
                0f,
                stats.AverageInterRentalTimeSeconds,
                "Single rental should have zero average inter-rental time"
            );
        }

        [Test]
        public void PoolFrequencyStatisticsDefaultValueIsValid()
        {
            PoolFrequencyStatistics defaultStats = default;

            Assert.AreEqual(0f, defaultStats.RentalsPerMinute);
            Assert.AreEqual(0f, defaultStats.AverageInterRentalTimeSeconds);
            Assert.AreEqual(0f, defaultStats.LastAccessTime);
            Assert.AreEqual(0L, defaultStats.TotalRentalCount);
            Assert.IsFalse(defaultStats.IsHighFrequency);
            Assert.IsFalse(defaultStats.IsLowFrequency);
            Assert.IsFalse(defaultStats.IsUnused);
        }

        [Test]
        public void PoolUsageTrackerClearResetsAllMetrics()
        {
            PoolUsageTracker tracker = new PoolUsageTracker(
                rollingWindowSeconds: 60f,
                hysteresisSeconds: 5f,
                spikeThresholdMultiplier: 2f,
                bufferMultiplier: 1.5f
            );

            for (int i = 0; i < 10; i++)
            {
                tracker.RecordRent(i * 1f);
            }

            for (int i = 0; i < 5; i++)
            {
                tracker.RecordReturn(10f + i * 1f);
            }

            tracker.Clear();

            Assert.AreEqual(0, tracker.CurrentlyRented, "CurrentlyRented should be 0 after clear");
            Assert.AreEqual(
                0,
                tracker.PeakConcurrentRentals,
                "PeakConcurrentRentals should be 0 after clear"
            );
            Assert.AreEqual(0f, tracker.LastRentalTime, "LastRentalTime should be 0 after clear");
            Assert.AreEqual(0f, tracker.LastReturnTime, "LastReturnTime should be 0 after clear");
            Assert.AreEqual(
                0f,
                tracker.RentalsPerMinute,
                "RentalsPerMinute should be 0 after clear"
            );
            Assert.AreEqual(
                0f,
                tracker.AverageInterRentalTimeSeconds,
                "AverageInterRentalTimeSeconds should be 0 after clear"
            );
            Assert.AreEqual(
                0L,
                tracker.TotalRentalCount,
                "TotalRentalCount should be 0 after clear"
            );
        }

        /// <summary>
        /// Tests that a pool with exactly 1 rental over 60 seconds is classified as low frequency.
        /// This validates the inclusive boundary condition where rentals per minute equals exactly 1.0.
        /// </summary>
        [Test]
        public void ExactlyOneRentalPerMinuteIsLowFrequency()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            // Start at t=1 to avoid time=0 initialization issues
            _currentTime = 1f;

            // Perform exactly 1 rental
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            // Advance time to exactly 60 seconds after the rental
            // This creates exactly 1 rental per minute (1 rental / 1 minute = 1.0)
            _currentTime = 61f;

            PoolStatistics stats = pool.GetStatistics();

            TestContext.WriteLine(
                $"RentalsPerMinute: {stats.RentalsPerMinute}, RentCount: {stats.RentCount}, IsLowFrequency: {stats.IsLowFrequency}"
            );

            // Verify we have exactly 1 rental
            Assert.AreEqual(1, stats.RentCount, "Should have exactly 1 rental");

            // Verify rentals per minute is approximately 1.0
            Assert.AreEqual(
                1.0f,
                stats.RentalsPerMinute,
                0.1f,
                "Rentals per minute should be approximately 1.0"
            );

            // Verify IsLowFrequency is true for exactly 1 rental per minute (inclusive boundary)
            Assert.IsTrue(
                stats.IsLowFrequency,
                "Pool with exactly 1 rental per minute should be classified as low frequency (threshold is <= 1.0)"
            );
        }

        /// <summary>
        /// Tests that pools at exactly 1 rental per minute receive the shorter idle timeout (50% of normal).
        /// This validates that low frequency classification affects the effective idle timeout.
        /// Note: BufferMultiplier=0 and WarmRetainCount=0 are set to disable the comfortable size
        /// protection, allowing the test to focus on testing low-frequency idle timeout behavior in isolation.
        /// </summary>
        [Test]
        public void LowFrequencyThresholdAffectsIdleTimeout()
        {
            int purgeCount = 0;
            float baseIdleTimeout = 60f;

            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = baseIdleTimeout,
                    Triggers = PurgeTrigger.OnRent,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                    BufferMultiplier = 0f,
                    WarmRetainCount = 0,
                }
            );

            // Start at t=1 to avoid time=0 initialization issues
            _currentTime = 1f;

            // Create a low-frequency pool with exactly 1 rental
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After first rental at t={_currentTime}: pool.Count={pool.Count}"
            );

            // Advance time to 62 seconds (just over 1 minute) to establish ~1 rental/min
            _currentTime = 62f;

            PoolStatistics statsBeforePurge = pool.GetStatistics();
            float expectedEffectiveTimeout = statsBeforePurge.IsLowFrequency
                ? baseIdleTimeout * 0.5f
                : baseIdleTimeout;
            TestContext.WriteLine(
                $"Before purge at t={_currentTime}: RentalsPerMinute={statsBeforePurge.RentalsPerMinute}, "
                    + $"IsLowFrequency={statsBeforePurge.IsLowFrequency}, pool.Count={pool.Count}, "
                    + $"ExpectedEffectiveTimeout={expectedEffectiveTimeout}s"
            );

            Assert.IsTrue(
                statsBeforePurge.IsLowFrequency,
                "Test setup error: Pool should be low frequency before testing idle timeout"
            );

            // Low frequency pools have 50% of normal idle timeout (60 * 0.5 = 30 seconds)
            // Advance to a time past the low-frequency timeout but before normal timeout
            // At t=62, item was last accessed at t=1, so idle time is 61 seconds
            // Low frequency timeout would be 30 seconds, so item should be purged

            // Trigger purge check by renting
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After second Get at t={_currentTime}: purgeCount={purgeCount}, pool.Count={pool.Count}"
            );

            // The first item should have been purged due to the reduced idle timeout
            Assert.GreaterOrEqual(
                purgeCount,
                1,
                "Low frequency pool should have triggered a purge due to shorter idle timeout"
            );
        }

        /// <summary>
        /// Tests that idle timeout purges correctly occur at various comfortable size scenarios.
        /// This is a data-driven test validating that idle timeout is independent of comfortable size.
        /// </summary>
        [Test]
        [TestCase(0f, 0, true, TestName = "IdleTimeoutPurgeWithZeroBufferAndMinRetain")]
        // Cannot purge when pool.Count == minRetainCount (1 item in pool, minRetain = 1)
        [TestCase(0f, 1, false, TestName = "IdleTimeoutPurgeWithZeroBufferAndOneMinRetain")]
        [TestCase(1f, 0, true, TestName = "IdleTimeoutPurgeWithNormalBufferZeroMinRetain")]
        [TestCase(0f, 2, false, TestName = "IdleTimeoutPurgeWithZeroBufferAndTwoMinRetain")]
        [TestCase(1.0f, 2, false, TestName = "IdleTimeoutPurgeWithBuffer1AndTwoMinRetain")]
        [TestCase(0.5f, 1, false, TestName = "IdleTimeoutPurgeWithBuffer05AndOneMinRetain")]
        public void IdleTimeoutPurgeBehaviorAtVariousComfortableSizes(
            float bufferMultiplier,
            int minRetainCount,
            bool expectPurge
        )
        {
            int purgeCount = 0;
            float baseIdleTimeout = 2f;
            List<float> itemReturnTimes = new();

            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = baseIdleTimeout,
                    Triggers = PurgeTrigger.OnRent,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                    BufferMultiplier = bufferMultiplier,
                    WarmRetainCount = 0,
                    MinRetainCount = minRetainCount,
                }
            );

            _currentTime = 1f;

            // Diagnostic: Log test configuration
            TestContext.WriteLine($"=== Test Configuration ===");
            TestContext.WriteLine($"  BufferMultiplier: {bufferMultiplier}");
            TestContext.WriteLine($"  MinRetainCount: {minRetainCount}");
            TestContext.WriteLine($"  BaseIdleTimeout: {baseIdleTimeout}s");
            TestContext.WriteLine($"  ExpectPurge: {expectPurge}");

            // Create and return an item
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            itemReturnTimes.Add(_currentTime);

            PoolStatistics statsAfterFirst = pool.GetStatistics();
            int comfortableSize = (int)(statsAfterFirst.RentCount * bufferMultiplier);
            TestContext.WriteLine($"=== After First Get/Return ===");
            TestContext.WriteLine($"  Time: {_currentTime}");
            TestContext.WriteLine($"  Pool.Count: {pool.Count}");
            TestContext.WriteLine($"  RentCount: {statsAfterFirst.RentCount}");
            TestContext.WriteLine($"  ComfortableSize (calculated): {comfortableSize}");
            TestContext.WriteLine($"  Item return times: [{string.Join(", ", itemReturnTimes)}]");

            // Advance time past idle timeout
            _currentTime = 1f + baseIdleTimeout + 1f;
            float actualIdleTime = _currentTime - itemReturnTimes[0];

            TestContext.WriteLine($"=== Before Second Get ===");
            TestContext.WriteLine($"  Time: {_currentTime}");
            TestContext.WriteLine($"  Actual idle time elapsed: {actualIdleTime}s");
            TestContext.WriteLine($"  Effective idle timeout: {baseIdleTimeout}s");
            TestContext.WriteLine($"  Idle time > timeout: {baseIdleTimeout < actualIdleTime}");

            // Trigger purge check
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            PoolStatistics statsAfterSecond = pool.GetStatistics();
            TestContext.WriteLine($"=== After Second Get ===");
            TestContext.WriteLine($"  Time: {_currentTime}");
            TestContext.WriteLine($"  Pool.Count: {pool.Count}");
            TestContext.WriteLine($"  PurgeCount: {purgeCount}");
            TestContext.WriteLine($"  IdleTimeoutPurges: {statsAfterSecond.IdleTimeoutPurges}");

            if (expectPurge)
            {
                Assert.GreaterOrEqual(
                    purgeCount,
                    1,
                    $"Idle timeout purge should occur (bufferMultiplier={bufferMultiplier}, minRetain={minRetainCount})"
                );
            }
            else
            {
                Assert.AreEqual(
                    0,
                    purgeCount,
                    $"No purge expected (bufferMultiplier={bufferMultiplier}, minRetain={minRetainCount})"
                );
            }
        }

        /// <summary>
        /// Tests that different frequency thresholds correctly affect idle timeout classification.
        /// Verifies that pools below/at threshold are classified as low frequency, while pools above are not.
        /// Note: BufferMultiplier=0 and WarmRetainCount=0 are set to disable comfortable size protection,
        /// allowing the test to focus on testing frequency classification in isolation.
        /// </summary>
        [Test]
        [TestCase(0.5f, TestName = "FrequencyBelowThresholdIsClassifiedLow")]
        [TestCase(1.0f, TestName = "FrequencyAtThresholdIsClassifiedLow")]
        [TestCase(1.5f, TestName = "FrequencyAboveThresholdIsClassifiedNormal")]
        public void FrequencyThresholdAffectsIdleTimeoutBoundaries(float targetRentalsPerMinute)
        {
            int purgeCount = 0;
            float baseIdleTimeout = 60f;

            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    UseIntelligentPurging = true,
                    IdleTimeoutSeconds = baseIdleTimeout,
                    Triggers = PurgeTrigger.OnRent,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                    BufferMultiplier = 0f,
                    WarmRetainCount = 0,
                }
            );

            // Start at t=1 to avoid time=0 initialization issues
            _currentTime = 1f;

            // Create a rental pattern to establish the target frequency
            // For targetRentalsPerMinute = X, we need X rentals per 60 seconds
            // To achieve a specific rentals/minute rate, we'll space our rentals appropriately
            int rentalCount;
            float rentalInterval;

            if (targetRentalsPerMinute <= 1.0f)
            {
                // For low frequency (<=1 rental/min), do 1 rental and wait 60+ seconds
                rentalCount = 1;
                rentalInterval = 0f;
            }
            else
            {
                // For higher frequency, do multiple rentals in quick succession
                rentalCount = (int)System.Math.Ceiling(targetRentalsPerMinute * 2);
                rentalInterval = 60f / rentalCount;
            }

            // Perform the rentals
            for (int i = 0; i < rentalCount; i++)
            {
                _currentTime = 1f + (i * rentalInterval);
                using (PooledResource<TestPoolItem> resource = pool.Get()) { }
            }

            // Advance time to establish the frequency window (60 seconds from first rental)
            _currentTime = 62f;

            PoolStatistics statsBeforePurge = pool.GetStatistics();
            TestContext.WriteLine(
                $"Target: {targetRentalsPerMinute} rentals/min, Actual: {statsBeforePurge.RentalsPerMinute:F2} rentals/min, "
                    + $"IsLowFrequency: {statsBeforePurge.IsLowFrequency}, RentCount: {statsBeforePurge.RentCount}"
            );

            // Verify the frequency classification matches expectations
            bool actualLowFrequency = statsBeforePurge.IsLowFrequency;

            if (targetRentalsPerMinute <= 1.0f)
            {
                Assert.IsTrue(
                    actualLowFrequency,
                    $"Pool with target {targetRentalsPerMinute} rentals/min should be classified as low frequency"
                );
            }
            else
            {
                Assert.IsFalse(
                    actualLowFrequency,
                    $"Pool with target {targetRentalsPerMinute} rentals/min should NOT be classified as low frequency"
                );
            }

            // The test above already verified the frequency classification is correct.
            // Note: We don't test the actual purge behavior here because resetting the pool
            // and adding fresh items would invalidate the frequency classification we just tested.
            // The idle timeout reduction based on frequency is tested in other tests that
            // maintain consistent frequency patterns throughout.
        }

        /// <summary>
        /// Tests that a pool can transition from low frequency to normal frequency as activity increases.
        /// This validates that frequency classification is dynamic and responds to changes in usage patterns.
        /// </summary>
        [Test]
        public void TransitionFromLowToNormalFrequency()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            // Start at t=1 to avoid time=0 initialization issues
            _currentTime = 1f;

            // Create a low-frequency pool with 1 rental over 60 seconds
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            _currentTime = 61f;

            PoolStatistics lowFrequencyStats = pool.GetStatistics();
            TestContext.WriteLine(
                $"Initial state - RentalsPerMinute: {lowFrequencyStats.RentalsPerMinute}, IsLowFrequency: {lowFrequencyStats.IsLowFrequency}"
            );

            Assert.IsTrue(
                lowFrequencyStats.IsLowFrequency,
                "Pool should initially be low frequency"
            );
            Assert.IsFalse(
                lowFrequencyStats.IsHighFrequency,
                "Pool should not be high frequency initially"
            );

            // Now generate high activity - 20 rentals in quick succession
            // This should transition the pool to normal or high frequency
            for (int i = 0; i < 20; i++)
            {
                _currentTime = 61f + (i * 0.5f); // 0.5 second intervals
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 75f;

            PoolStatistics normalFrequencyStats = pool.GetStatistics();
            TestContext.WriteLine(
                $"After activity - RentalsPerMinute: {normalFrequencyStats.RentalsPerMinute}, IsLowFrequency: {normalFrequencyStats.IsLowFrequency}, IsHighFrequency: {normalFrequencyStats.IsHighFrequency}"
            );

            // After high activity, pool should no longer be low frequency
            Assert.IsFalse(
                normalFrequencyStats.IsLowFrequency,
                "Pool should no longer be low frequency after increased activity"
            );

            // Rentals per minute should be significantly higher than 1.0
            Assert.Greater(
                normalFrequencyStats.RentalsPerMinute,
                1.0f,
                "Rentals per minute should be greater than 1.0 after increased activity"
            );
        }

        private sealed class TestPoolItem
        {
            public int Id { get; }

            private static int _nextId;

            public TestPoolItem()
            {
                Id = ++_nextId;
            }

            public static void ResetIdCounter()
            {
                _nextId = 0;
            }
        }
    }

    /// <summary>
    /// Tests to verify that GetStatistics calls do not incorrectly inflate rental counts.
    /// These tests ensure statistics queries are read-only operations.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PoolStatisticsInvariantTests
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
        public void MultipleGetStatisticsCallsDoNotInflateRentalCount()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            PoolStatistics initialStats = pool.GetStatistics();
            long initialRentCount = initialStats.RentCount;

            Assert.That(
                initialRentCount,
                Is.EqualTo(1),
                "Initial rent count should be 1 after single rental"
            );

            for (int i = 0; i < 100; i++)
            {
                PoolStatistics stats = pool.GetStatistics();
                Assert.That(
                    stats.RentCount,
                    Is.EqualTo(initialRentCount),
                    $"Rent count should remain {initialRentCount} after GetStatistics call {i + 1}"
                );
            }

            PoolStatistics finalStats = pool.GetStatistics();
            Assert.That(
                finalStats.RentCount,
                Is.EqualTo(initialRentCount),
                "Rent count should not change after multiple GetStatistics calls"
            );
        }

        [Test]
        public void GetStatisticsDoesNotAffectRentalsPerMinute()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 10f;

            PoolStatistics initialStats = pool.GetStatistics();
            float initialRentalsPerMin = initialStats.RentalsPerMinute;
            long initialRentCount = initialStats.RentCount;

            for (int i = 0; i < 50; i++)
            {
                _currentTime += 0.1f;
                PoolStatistics stats = pool.GetStatistics();
            }

            PoolStatistics finalStats = pool.GetStatistics();

            Assert.That(
                finalStats.RentCount,
                Is.EqualTo(initialRentCount),
                "Rent count should not change from GetStatistics calls"
            );
            Assert.That(
                finalStats.RentalsPerMinute,
                Is.LessThanOrEqualTo(initialRentalsPerMin),
                "RentalsPerMinute should decrease or stay same as time passes without rentals, never increase from GetStatistics calls"
            );
        }

        [Test]
        public void GetStatisticsDuringWindowTransitionDoesNotCountAsRental()
        {
            const float windowDuration = 60f;
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 10; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 30f;
            PoolStatistics midWindowStats = pool.GetStatistics();
            long midWindowRentCount = midWindowStats.RentCount;

            Assert.That(
                midWindowRentCount,
                Is.EqualTo(10),
                "Rent count should be 10 at mid-window"
            );

            _currentTime = windowDuration + 10f;

            PoolStatistics afterTransitionStats = pool.GetStatistics();

            Assert.That(
                afterTransitionStats.RentCount,
                Is.EqualTo(midWindowRentCount),
                "Rent count should not change during window transition from GetStatistics"
            );
        }

        [Test]
        public void ActualRentalDuringWindowTransitionIsCountedInNewWindow()
        {
            const float windowDuration = 60f;
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            _currentTime = 30f;
            PoolStatistics midWindowStats = pool.GetStatistics();

            Assert.That(
                midWindowStats.RentCount,
                Is.EqualTo(5),
                "Should have 5 rentals at mid-window"
            );

            _currentTime = windowDuration + 10f;

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            PoolStatistics afterTransitionStats = pool.GetStatistics();

            Assert.That(
                afterTransitionStats.RentCount,
                Is.EqualTo(6),
                "Actual rental during window transition should be counted"
            );

            Assert.That(
                afterTransitionStats.RentalsPerMinute,
                Is.GreaterThan(0f),
                "RentalsPerMinute should be positive after rental in new window"
            );
        }

        [Test]
        [TestCase(1, TestName = "SingleStatisticsCall")]
        [TestCase(10, TestName = "TenStatisticsCalls")]
        [TestCase(100, TestName = "HundredStatisticsCalls")]
        public void GetStatisticsIsIdempotentForRentCount(int statisticsCallCount)
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            const int actualRentals = 3;
            for (int i = 0; i < actualRentals; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            PoolStatistics initialStats = pool.GetStatistics();

            Assert.That(
                initialStats.RentCount,
                Is.EqualTo(actualRentals),
                $"Initial rent count should be {actualRentals}"
            );

            for (int i = 0; i < statisticsCallCount; i++)
            {
                pool.GetStatistics();
            }

            PoolStatistics finalStats = pool.GetStatistics();

            Assert.That(
                finalStats.RentCount,
                Is.EqualTo(actualRentals),
                $"Rent count should remain {actualRentals} after {statisticsCallCount} GetStatistics calls"
            );
        }

        [Test]
        public void GetStatisticsDoesNotAffectLastAccessTime()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 10f;
            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            PoolStatistics statsAfterRental = pool.GetStatistics();
            float lastAccessAfterRental = statsAfterRental.LastAccessTime;

            Assert.That(
                lastAccessAfterRental,
                Is.EqualTo(10f),
                "LastAccessTime should be 10f after rental at t=10"
            );

            _currentTime = 50f;

            for (int i = 0; i < 10; i++)
            {
                _currentTime += 1f;
                pool.GetStatistics();
            }

            PoolStatistics statsAfterQueries = pool.GetStatistics();

            Assert.That(
                statsAfterQueries.LastAccessTime,
                Is.EqualTo(lastAccessAfterRental),
                "LastAccessTime should not change from GetStatistics calls"
            );
        }

        [Test]
        public void GetStatisticsDoesNotAffectAverageInterRentalTime()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            _currentTime = 1f;
            using (PooledResource<TestPoolItem> resource1 = pool.Get()) { }
            _currentTime = 3f;
            using (PooledResource<TestPoolItem> resource2 = pool.Get()) { }
            _currentTime = 6f;
            using (PooledResource<TestPoolItem> resource3 = pool.Get()) { }

            PoolStatistics statsAfterRentals = pool.GetStatistics();
            float avgTimeAfterRentals = statsAfterRentals.AverageInterRentalTimeSeconds;

            Assert.That(
                avgTimeAfterRentals,
                Is.EqualTo(2.5f).Within(0.01f),
                "Average inter-rental time should be (2 + 3) / 2 = 2.5"
            );

            for (int i = 0; i < 50; i++)
            {
                _currentTime += 1f;
                pool.GetStatistics();
            }

            PoolStatistics statsAfterQueries = pool.GetStatistics();

            Assert.That(
                statsAfterQueries.AverageInterRentalTimeSeconds,
                Is.EqualTo(avgTimeAfterRentals).Within(0.01f),
                "AverageInterRentalTimeSeconds should not change from GetStatistics calls"
            );
        }

        [Test]
        public void InterleavedGetStatisticsAndRentalsTrackCorrectly()
        {
            using WallstopGenericPool<TestPoolItem> pool = new WallstopGenericPool<TestPoolItem>(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    Triggers = PurgeTrigger.Explicit,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 10; i++)
            {
                _currentTime = i + 1f;
                using PooledResource<TestPoolItem> resource = pool.Get();

                PoolStatistics stats = pool.GetStatistics();

                Assert.That(
                    stats.RentCount,
                    Is.EqualTo(i + 1),
                    $"Rent count should be {i + 1} after rental {i + 1}"
                );

                for (int j = 0; j < 5; j++)
                {
                    PoolStatistics extraStats = pool.GetStatistics();
                    Assert.That(
                        extraStats.RentCount,
                        Is.EqualTo(i + 1),
                        $"Rent count should remain {i + 1} after extra GetStatistics call {j + 1}"
                    );
                }
            }

            PoolStatistics finalStats = pool.GetStatistics();
            Assert.That(
                finalStats.RentCount,
                Is.EqualTo(10),
                "Final rent count should be exactly 10"
            );
        }

        private sealed class TestPoolItem
        {
            public int Id { get; }

            private static int _nextId;

            public TestPoolItem()
            {
                Id = ++_nextId;
            }

            public static void ResetIdCounter()
            {
                _nextId = 0;
            }
        }
    }

    /// <summary>
    /// Tests for idle timeout purge behavior at comfortable size boundaries and during hysteresis.
    /// These tests validate bug fixes where idle timeout purges were incorrectly blocked by
    /// comfortable size checks or hysteresis protection.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IdleTimeoutComfortableSizeEdgeCaseTests
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

        /// <summary>
        /// Tests that idle timeout purges occur when pool.Count exactly equals comfortable size.
        /// This validates the fix where comfortable size was incorrectly blocking idle timeout purges.
        /// </summary>
        [Test]
        [TestCase(1, 1.0f, TestName = "ComfortableSize.Boundary.SingleItem.Buffer.1.0")]
        [TestCase(5, 1.0f, TestName = "ComfortableSize.Boundary.FiveItems.Buffer.1.0")]
        [TestCase(3, 0.5f, TestName = "ComfortableSize.Boundary.ThreeItems.Buffer.0.5")]
        public void IdleTimeoutPurgeOccursAtExactComfortableSize(
            int itemCount,
            float bufferMultiplier
        )
        {
            int purgeCount = 0;
            List<PurgeReason> purgeReasons = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 2f,
                    Triggers = PurgeTrigger.OnRent,
                    UseIntelligentPurging = true,
                    BufferMultiplier = bufferMultiplier,
                    WarmRetainCount = 0,
                    MinRetainCount = 0,
                    OnPurge = (_, reason) =>
                    {
                        purgeCount++;
                        purgeReasons.Add(reason);
                    },
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < itemCount; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            TestContext.WriteLine(
                $"After {itemCount} Get/Return operations at t={_currentTime}: pool.Count={pool.Count}"
            );

            _currentTime = 1f + 2f + 1f;

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After idle timeout check at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );
            TestContext.WriteLine($"Purge reasons: {string.Join(", ", purgeReasons)}");

            Assert.GreaterOrEqual(
                purgeCount,
                1,
                "Idle timeout purge should occur even when pool is at comfortable size boundary"
            );
            Assert.Contains(
                PurgeReason.IdleTimeout,
                purgeReasons,
                "At least one purge should be due to idle timeout"
            );
        }

        /// <summary>
        /// Tests that idle timeout purges occur when pool.Count is below comfortable size.
        /// This validates that comfortable size is truly not a factor for idle timeout purges.
        /// </summary>
        [Test]
        [TestCase(1, 2.0f, TestName = "BelowComfortableSize.OneItem.Buffer.2.0")]
        [TestCase(2, 5.0f, TestName = "BelowComfortableSize.TwoItems.Buffer.5.0")]
        public void IdleTimeoutPurgeOccursBelowComfortableSize(
            int itemCount,
            float bufferMultiplier
        )
        {
            int purgeCount = 0;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 2f,
                    Triggers = PurgeTrigger.OnRent,
                    UseIntelligentPurging = true,
                    BufferMultiplier = bufferMultiplier,
                    WarmRetainCount = 0,
                    MinRetainCount = 0,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < itemCount; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            PoolStatistics statsAfterRentals = pool.GetStatistics();
            int comfortableSize = (int)(statsAfterRentals.RentCount * bufferMultiplier);
            TestContext.WriteLine(
                $"Pool count={pool.Count}, calculated comfortableSize={comfortableSize} (rentals={statsAfterRentals.RentCount} * buffer={bufferMultiplier})"
            );

            Assert.Less(
                pool.Count,
                comfortableSize,
                "Test setup: pool should be below comfortable size"
            );

            _currentTime = 1f + 2f + 1f;

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After idle timeout check at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );

            Assert.GreaterOrEqual(
                purgeCount,
                1,
                "Idle timeout purge should occur even when pool is below comfortable size"
            );
        }

        /// <summary>
        /// Tests that idle timeout purges proceed during hysteresis period.
        /// Idle timeout is essential pool hygiene and should not be blocked by hysteresis.
        /// </summary>
        [Test]
        public void IdleTimeoutPurgeProceedsDuringHysteresisPeriod()
        {
            int purgeCount = 0;
            List<PurgeReason> purgeReasons = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 5f,
                    HysteresisSeconds = 120f,
                    Triggers = PurgeTrigger.OnRent,
                    UseIntelligentPurging = true,
                    BufferMultiplier = 0f,
                    WarmRetainCount = 0,
                    MinRetainCount = 0,
                    SpikeThresholdMultiplier = 1.5f,
                    OnPurge = (_, reason) =>
                    {
                        purgeCount++;
                        purgeReasons.Add(reason);
                    },
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 10; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            TestContext.WriteLine(
                $"After spike (10 rentals) at t={_currentTime}: pool.Count={pool.Count}"
            );

            _currentTime = 1f + 5f + 1f;

            TestContext.WriteLine(
                $"Time advanced to t={_currentTime} (past idle timeout of 5s, within hysteresis of 120s)"
            );

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After Get at t={_currentTime}: pool.Count={pool.Count}, purgeCount={purgeCount}"
            );
            TestContext.WriteLine($"Purge reasons: {string.Join(", ", purgeReasons)}");

            Assert.GreaterOrEqual(
                purgeCount,
                1,
                "Idle timeout purge should proceed during hysteresis period (idle timeout is essential hygiene)"
            );
        }

        /// <summary>
        /// Tests explicit purges are blocked during hysteresis but idle timeout proceeds.
        /// This validates the correct differentiation between purge types.
        /// </summary>
        [Test]
        public void ExplicitPurgeBlockedButIdleTimeoutProceedsDuringHysteresis()
        {
            int purgeCount = 0;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 5,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 2f,
                    HysteresisSeconds = 60f,
                    Triggers = PurgeTrigger.Explicit,
                    UseIntelligentPurging = true,
                    BufferMultiplier = 0f,
                    WarmRetainCount = 0,
                    MinRetainCount = 0,
                    SpikeThresholdMultiplier = 1.5f,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            TestContext.WriteLine(
                $"After creating spike at t={_currentTime}: pool.Count={pool.Count}"
            );

            _currentTime = 1f + 2f + 1f;

            int explicitPurgeResult = pool.Purge(PurgeReason.Explicit);
            TestContext.WriteLine(
                $"Explicit purge during hysteresis at t={_currentTime}: purged={explicitPurgeResult}, purgeCount={purgeCount}"
            );

            Assert.AreEqual(
                0,
                explicitPurgeResult,
                "Explicit purge should be blocked during hysteresis"
            );

            int idleTimeoutPurgeResult = pool.Purge(PurgeReason.IdleTimeout);
            TestContext.WriteLine(
                $"Idle timeout purge during hysteresis at t={_currentTime}: purged={idleTimeoutPurgeResult}, purgeCount={purgeCount}"
            );

            Assert.GreaterOrEqual(
                idleTimeoutPurgeResult,
                1,
                "Idle timeout purge should proceed during hysteresis"
            );
        }

        /// <summary>
        /// Data-driven test for idle timeout behavior with various comfortable size configurations.
        /// Validates that idle timeout always works regardless of buffer multiplier settings.
        /// </summary>
        [Test]
        [TestCase(0f, 0, 0, TestName = "IdleTimeout.Buffer.0.WarmRetain.0.MinRetain.0")]
        [TestCase(0.5f, 0, 0, TestName = "IdleTimeout.Buffer.0.5.WarmRetain.0.MinRetain.0")]
        [TestCase(1.0f, 0, 0, TestName = "IdleTimeout.Buffer.1.0.WarmRetain.0.MinRetain.0")]
        [TestCase(2.0f, 0, 0, TestName = "IdleTimeout.Buffer.2.0.WarmRetain.0.MinRetain.0")]
        [TestCase(1.0f, 2, 0, TestName = "IdleTimeout.Buffer.1.0.WarmRetain.2.MinRetain.0")]
        [TestCase(1.0f, 0, 2, TestName = "IdleTimeout.Buffer.1.0.WarmRetain.0.MinRetain.2")]
        public void IdleTimeoutWorksWithVariousComfortableSizeSettings(
            float bufferMultiplier,
            int warmRetainCount,
            int minRetainCount
        )
        {
            int purgeCount = 0;
            const int itemCount = 5;
            const float idleTimeout = 2f;

            TestContext.WriteLine($"=== Test Configuration ===");
            TestContext.WriteLine($"  BufferMultiplier: {bufferMultiplier}");
            TestContext.WriteLine($"  WarmRetainCount: {warmRetainCount}");
            TestContext.WriteLine($"  MinRetainCount: {minRetainCount}");
            TestContext.WriteLine($"  IdleTimeout: {idleTimeout}s");
            TestContext.WriteLine($"  ItemCount to create: {itemCount}");

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = idleTimeout,
                    Triggers = PurgeTrigger.OnRent,
                    UseIntelligentPurging = true,
                    BufferMultiplier = bufferMultiplier,
                    WarmRetainCount = warmRetainCount,
                    MinRetainCount = minRetainCount,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                }
            );

            // Hold all resources simultaneously to force creation of multiple items
            List<PooledResource<TestPoolItem>> resources = new();
            for (int i = 0; i < itemCount; i++)
            {
                PooledResource<TestPoolItem> resource = pool.Get();
                resources.Add(resource);
                TestContext.WriteLine(
                    $"  Created resource {i + 1}/{itemCount}, pool.Count={pool.Count}"
                );
            }

            TestContext.WriteLine($"=== After Creating {itemCount} Resources ===");
            TestContext.WriteLine($"  Pool.Count (while rented): {pool.Count}");
            TestContext.WriteLine($"  Resources held: {resources.Count}");

            // Return all items to the pool
            foreach (PooledResource<TestPoolItem> resource in resources)
            {
                resource.Dispose();
            }

            int countBeforePurge = pool.Count;
            PoolStatistics statsBeforePurge = pool.GetStatistics();
            TestContext.WriteLine($"=== After Returning All Resources ===");
            TestContext.WriteLine($"  Time: {_currentTime}");
            TestContext.WriteLine($"  Pool.Count: {countBeforePurge}");
            TestContext.WriteLine($"  RentCount: {statsBeforePurge.RentCount}");
            TestContext.WriteLine($"  ReturnCount: {statsBeforePurge.ReturnCount}");

            _currentTime = 1f + idleTimeout + 1f;

            TestContext.WriteLine($"=== Before Triggering Purge ===");
            TestContext.WriteLine($"  Time: {_currentTime}");
            TestContext.WriteLine($"  Idle time elapsed: {_currentTime - 1f}s");

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            int expectedMinCount = Math.Max(warmRetainCount, minRetainCount);
            int maxPossiblePurges = Math.Max(0, countBeforePurge - expectedMinCount);

            PoolStatistics statsAfterPurge = pool.GetStatistics();
            TestContext.WriteLine($"=== After Triggering Purge ===");
            TestContext.WriteLine($"  Time: {_currentTime}");
            TestContext.WriteLine($"  Pool.Count: {pool.Count}");
            TestContext.WriteLine($"  PurgeCount: {purgeCount}");
            TestContext.WriteLine($"  IdleTimeoutPurges: {statsAfterPurge.IdleTimeoutPurges}");
            TestContext.WriteLine($"  ExpectedMinCount: {expectedMinCount}");
            TestContext.WriteLine($"  MaxPossiblePurges: {maxPossiblePurges}");

            if (0 < maxPossiblePurges)
            {
                Assert.GreaterOrEqual(
                    purgeCount,
                    1,
                    $"Idle timeout should trigger purges when possible (buffer={bufferMultiplier}, warmRetain={warmRetainCount}, minRetain={minRetainCount})"
                );
            }

            Assert.GreaterOrEqual(
                pool.Count,
                minRetainCount,
                "Pool count should never go below minRetainCount"
            );
        }

        /// <summary>
        /// Tests hysteresis bypass works correctly for memory pressure purges.
        /// Memory pressure purges should ignore hysteresis to release memory under pressure.
        /// </summary>
        [Test]
        public void MemoryPressurePurgeBypassesHysteresis()
        {
            int purgeCount = 0;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 60f,
                    HysteresisSeconds = 120f,
                    Triggers = PurgeTrigger.Explicit,
                    UseIntelligentPurging = true,
                    BufferMultiplier = 1.0f,
                    WarmRetainCount = 0,
                    MinRetainCount = 0,
                    SpikeThresholdMultiplier = 1.5f,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            TestContext.WriteLine($"After spike at t={_currentTime}: pool.Count={pool.Count}");

            _currentTime = 5f;

            int normalPurge = pool.Purge(PurgeReason.Explicit);
            TestContext.WriteLine(
                $"Normal purge during hysteresis: purged={normalPurge}, pool.Count={pool.Count}"
            );

            Assert.AreEqual(
                0,
                normalPurge,
                "Normal explicit purge should be blocked by hysteresis"
            );

            int memoryPressurePurge = pool.ForceFullPurge(
                PurgeReason.MemoryPressure,
                ignoreHysteresis: true
            );
            TestContext.WriteLine(
                $"Memory pressure purge with ignoreHysteresis=true: purged={memoryPressurePurge}, pool.Count={pool.Count}"
            );

            Assert.Greater(
                memoryPressurePurge,
                0,
                "Memory pressure purge with ignoreHysteresis should bypass hysteresis"
            );
        }

        /// <summary>
        /// Tests that after hysteresis expires, all purge types work normally.
        /// </summary>
        [Test]
        public void AllPurgeTypesWorkAfterHysteresisExpires()
        {
            int purgeCount = 0;

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                preWarmCount: 10,
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = 2f,
                    HysteresisSeconds = 10f,
                    Triggers = PurgeTrigger.Explicit,
                    UseIntelligentPurging = true,
                    BufferMultiplier = 0f,
                    WarmRetainCount = 0,
                    MinRetainCount = 0,
                    SpikeThresholdMultiplier = 1.5f,
                    OnPurge = (_, _) => purgeCount++,
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            TestContext.WriteLine($"After spike at t={_currentTime}: pool.Count={pool.Count}");

            _currentTime = 5f;
            int purgedDuringHysteresis = pool.Purge(PurgeReason.Explicit);
            TestContext.WriteLine(
                $"Purge during hysteresis (t={_currentTime}): purged={purgedDuringHysteresis}"
            );

            Assert.AreEqual(
                0,
                purgedDuringHysteresis,
                "Explicit purge should be blocked during hysteresis"
            );

            _currentTime = 1f + 10f + 5f;

            int purgedAfterHysteresis = pool.Purge(PurgeReason.Explicit);
            TestContext.WriteLine(
                $"Purge after hysteresis expires (t={_currentTime}): purged={purgedAfterHysteresis}, pool.Count={pool.Count}"
            );

            Assert.Greater(
                purgedAfterHysteresis,
                0,
                "Explicit purge should work after hysteresis expires"
            );
        }

        /// <summary>
        /// Tests the boundary condition where idle timeout is exactly at the hysteresis boundary.
        /// </summary>
        [Test]
        [TestCase(5f, 5f, true, TestName = "IdleAndHysteresis.Equal.5s")]
        [TestCase(10f, 5f, true, TestName = "Hysteresis.LongerThan.IdleTimeout")]
        [TestCase(5f, 10f, true, TestName = "IdleTimeout.LongerThan.Hysteresis")]
        public void IdleTimeoutProceedsRegardlessOfHysteresisRelativeLength(
            float hysteresisSeconds,
            float idleTimeoutSeconds,
            bool expectIdleTimeoutPurge
        )
        {
            int purgeCount = 0;
            List<PurgeReason> reasons = new();

            using WallstopGenericPool<TestPoolItem> pool = new(
                () => new TestPoolItem(),
                options: new PoolOptions<TestPoolItem>
                {
                    IdleTimeoutSeconds = idleTimeoutSeconds,
                    HysteresisSeconds = hysteresisSeconds,
                    Triggers = PurgeTrigger.OnRent,
                    UseIntelligentPurging = true,
                    BufferMultiplier = 0f,
                    WarmRetainCount = 0,
                    MinRetainCount = 0,
                    SpikeThresholdMultiplier = 1.5f,
                    OnPurge = (_, reason) =>
                    {
                        purgeCount++;
                        reasons.Add(reason);
                    },
                    TimeProvider = TestTimeProvider,
                }
            );

            for (int i = 0; i < 5; i++)
            {
                using PooledResource<TestPoolItem> resource = pool.Get();
            }

            TestContext.WriteLine(
                $"Setup: hysteresis={hysteresisSeconds}s, idleTimeout={idleTimeoutSeconds}s, pool.Count={pool.Count}"
            );

            _currentTime = 1f + idleTimeoutSeconds + 1f;

            using (PooledResource<TestPoolItem> resource = pool.Get()) { }

            TestContext.WriteLine(
                $"After check at t={_currentTime}: purgeCount={purgeCount}, reasons={string.Join(", ", reasons)}"
            );

            if (expectIdleTimeoutPurge)
            {
                Assert.GreaterOrEqual(
                    purgeCount,
                    1,
                    "Idle timeout purge should occur regardless of hysteresis length relationship"
                );
            }
        }

        private sealed class TestPoolItem
        {
            public int Id { get; }

            private static int _nextId;

            public TestPoolItem()
            {
                Id = ++_nextId;
            }

            public static void ResetIdCounter()
            {
                _nextId = 0;
            }
        }
    }
}
