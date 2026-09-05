// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Pool
{
    using System;
    using System.Diagnostics;
    using NUnit.Framework;
    using UnityEngine.TestTools.Constraints;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;
    using Is = UnityEngine.TestTools.Constraints.Is;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    internal sealed class RollingHighWaterMarkTests
    {
        [SetUp]
        public void SetUp()
        {
            PoolPurgeSettings.ResetToDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            PoolPurgeSettings.ResetToDefaults();
        }

        [Test]
        public void RecordUpdatesPeakWhenNewValueExceedsCurrent()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);
            hwm.Record(1.0f, 5);
            hwm.Record(2.0f, 10);
            hwm.Record(3.0f, 3);

            int peak = hwm.GetPeak(3.0f);
            TestContext.WriteLine($"Peak after recording 5, 10, 3: {peak}");
            Assert.AreEqual(10, peak);
        }

        [Test]
        public void RecordDoesNotUpdatePeakWhenNewValueIsLower()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);
            hwm.Record(1.0f, 10);
            hwm.Record(2.0f, 5);

            int peak = hwm.GetPeak(2.0f);
            TestContext.WriteLine($"Peak after recording 10, 5: {peak}");
            Assert.AreEqual(10, peak);
        }

        [Test]
        public void GetPeakEvictsExpiredSamplesAndReturnsUpdatedPeak()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(10f);
            hwm.Record(1.0f, 100);
            hwm.Record(2.0f, 5);

            int peakBeforeExpiry = hwm.GetPeak(1.0f);
            TestContext.WriteLine($"Peak before expiry: {peakBeforeExpiry}");
            Assert.AreEqual(100, peakBeforeExpiry);

            int peakAfterExpiry = hwm.GetPeak(12.0f);
            TestContext.WriteLine($"Peak after expiry at t=12: {peakAfterExpiry}");
            Assert.AreEqual(5, peakAfterExpiry);
        }

        [Test]
        public void GetPeakReturnsZeroWhenAllSamplesExpired()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(10f);
            hwm.Record(1.0f, 50);

            int peak = hwm.GetPeak(20.0f);
            TestContext.WriteLine($"Peak after all samples expired: {peak}");
            Assert.AreEqual(0, peak);
        }

        [Test]
        public void GetPeakReturnsZeroWhenEmpty()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);

            int peak = hwm.GetPeak(1.0f);
            TestContext.WriteLine($"Peak on empty instance: {peak}");
            Assert.AreEqual(0, peak);
        }

        [Test]
        public void GetAverageReturnsCorrectValueWithMultipleSamples()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);
            hwm.Record(1.0f, 10);
            hwm.Record(2.0f, 20);
            hwm.Record(3.0f, 30);

            float average = hwm.GetAverage(3.0f);
            TestContext.WriteLine($"Average of 10, 20, 30: {average}");
            Assert.AreEqual(20.0f, average, 0.01f);
        }

        [Test]
        public void GetAverageReturnsZeroWhenEmpty()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);

            float average = hwm.GetAverage(1.0f);
            TestContext.WriteLine($"Average on empty instance: {average}");
            Assert.AreEqual(0f, average);
        }

        [Test]
        public void ClearResetsAllState()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);
            hwm.Record(1.0f, 42);
            hwm.Record(2.0f, 99);
            hwm.Record(3.0f, 7);

            hwm.Clear();

            int peak = hwm.GetPeak(4.0f);
            float average = hwm.GetAverage(4.0f);
            TestContext.WriteLine($"Peak after clear: {peak}, Average after clear: {average}");
            Assert.AreEqual(0, peak);
            Assert.AreEqual(0f, average);
        }

        [Test]
        [Category("Performance")]
        public void HighVolumeInsertionCompletesWithinBudget()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < 100000; i++)
            {
                hwm.Record(i * 0.001f, 1);
            }

            stopwatch.Stop();
            long elapsedMs = stopwatch.ElapsedMilliseconds;
            TestContext.WriteLine($"100,000 insertions completed in {elapsedMs}ms");
            Assert.Less(
                elapsedMs,
                500,
                "High volume insertion should complete within 500ms budget"
            );
        }

        [Test]
        public void RunningSumAccuracyAfterManyAddRemoveCycles()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);

            for (int i = 0; i < 20000; i++)
            {
                hwm.Record(i * 0.001f, i % 100);
            }

            float expectedAverage = 49.5f;
            float actual = hwm.GetAverage(20.0f);
            TestContext.WriteLine(
                $"Average after 20000 samples: {actual}, expected: {expectedAverage}"
            );
            Assert.AreEqual(expectedAverage, actual, 0.1f);
        }

        [Test]
        public void PeakTrackingWithMonotonicallyDecreasingValues()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);

            for (int i = 0; i < 15000; i++)
            {
                hwm.Record(i * 0.001f, 15000 - i);
            }

            int peak = hwm.GetPeak(15.0f);
            TestContext.WriteLine($"Peak after 15000 decreasing values: {peak}, expected: 15000");
            Assert.AreEqual(15000, peak);
        }

        [Test]
        public void PeakTrackingWithMixedValues()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(10f);

            for (int i = 0; i < 1000; i++)
            {
                int value = i % 2 == 0 ? 100 : 1;
                hwm.Record(i * 0.001f, value);
            }

            int peak = hwm.GetPeak(1.0f);
            TestContext.WriteLine($"Peak after alternating high/low: {peak}");
            Assert.AreEqual(100, peak);

            for (int i = 0; i < 100; i++)
            {
                hwm.Record(1.0f + i * 0.001f, 50);
            }

            int peakAfterFifties = hwm.GetPeak(1.1f);
            TestContext.WriteLine(
                $"Peak after adding 50s (100 still in window): {peakAfterFifties}"
            );
            Assert.AreEqual(100, peakAfterFifties);

            int peakAfterExpiry = hwm.GetPeak(1000.0f);
            TestContext.WriteLine($"Peak after all samples expired: {peakAfterExpiry}");
            Assert.AreEqual(0, peakAfterExpiry);
        }

        [Test]
        public void CleanupRemovesAllExpiredSamples()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(10f);

            for (int i = 0; i < 100; i++)
            {
                hwm.Record(1.0f, i + 1);
            }

            int peak = hwm.GetPeak(1000.0f);
            int sampleCount = hwm.SampleCount;
            TestContext.WriteLine($"Peak after all expired: {peak}, SampleCount: {sampleCount}");
            Assert.AreEqual(0, peak);
            Assert.AreEqual(0, sampleCount);
        }

        [Test]
        public void HighLoadKeepsEverySampleInsideTheWindow()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);
            int expectedMax = 0;

            for (int i = 0; i < 15000; i++)
            {
                int value = i % 500;
                hwm.Record(i * 0.01f, value);
                if (expectedMax < value)
                {
                    expectedMax = value;
                }
            }

            int sampleCount = hwm.SampleCount;
            int peak = hwm.GetPeak(150.0f);
            TestContext.WriteLine(
                $"Sample count after 15000 records: {sampleCount}, Peak: {peak}, Expected max: {expectedMax}"
            );
            Assert.AreEqual(15000, sampleCount);
            Assert.AreEqual(expectedMax, peak);
        }

        /// <remarks>
        /// Both timestamps land on the same slot of the bucket ring -- epochs 660 and 594, which
        /// are 66 apart -- and the older one is outside the window. Joining the ring there would
        /// replace a live bucket's contribution to the running totals rather than add to it,
        /// leaving two samples counted against one bucket.
        /// </remarks>
        [Test]
        public void ASampleFromBeforeTheWindowRestartsTheRingRatherThanTakingALiveSlot()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(10f);

            hwm.Record(103.125f, 7);
            hwm.Record(92.8125f, 5000);

            int sampleCount = hwm.SampleCount;
            int peak = hwm.GetPeak(92.8125f);
            float average = hwm.GetAverage(92.8125f);
            TestContext.WriteLine($"SampleCount: {sampleCount}, Peak: {peak}, Average: {average}");
            Assert.AreEqual(1, sampleCount);
            Assert.AreEqual(5000, peak);
            Assert.AreEqual(5000f, average, 0.001f);
        }

        /// <remarks>
        /// The same restart from the other direction: a record at an absurd time leaves a live
        /// sample behind it, so every ordinary reading that follows is more than a window older
        /// than the newest epoch.
        /// </remarks>
        [Test]
        public void ARecordAtAnAbsurdTimeDoesNotRefuseEverySampleAfterIt()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(10f);

            hwm.Record(1e12f, 1);
            hwm.Record(5f, 42);
            hwm.Record(6f, 7);

            int sampleCount = hwm.SampleCount;
            int peak = hwm.GetPeak(6f);
            TestContext.WriteLine($"SampleCount: {sampleCount}, Peak: {peak}");
            Assert.AreEqual(2, sampleCount);
            Assert.AreEqual(42, peak);
        }

        /// <remarks>
        /// A read establishes the newest epoch just as a record does, so a query at an absurd time
        /// against an empty tracker would otherwise refuse every sample that followed it, forever.
        /// </remarks>
        [Test]
        public void AnEmptyTrackerReSeedsRatherThanRefusingEverySampleAfterAFarFutureRead()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(10f);

            int peakBefore = hwm.GetPeak(1e12f);
            hwm.Record(5f, 42);

            int sampleCount = hwm.SampleCount;
            int peak = hwm.GetPeak(5f);
            TestContext.WriteLine(
                $"Peak before: {peakBefore}, SampleCount: {sampleCount}, Peak: {peak}"
            );
            Assert.AreEqual(0, peakBefore);
            Assert.AreEqual(1, sampleCount);
            Assert.AreEqual(42, peak);
        }

        [Test]
        public void RecordingDoesNotAllocateOnceConstructed()
        {
            AllocationProbe.IgnoreWhenUnmeasurable();

            RollingHighWaterMark hwm = new RollingHighWaterMark(300f);
            for (int i = 0; i < AllocationProbe.Iterations; ++i)
            {
                hwm.Record(i * 0.001f, i % 7);
            }

            float start = AllocationProbe.Iterations * 0.001f;
            Assert.That(
                () =>
                {
                    for (int i = 0; i < AllocationProbe.Iterations; ++i)
                    {
                        hwm.RecordAndGetAverage(start + i * 0.001f, i % 7);
                    }
                },
                Is.Not.AllocatingGCMemory(),
                "the sample ring is sized once at construction, so recording never grows it"
            );
        }

        [Test]
        public void WindowExpirationRemovesOldSamples()
        {
            RollingHighWaterMark hwm = new RollingHighWaterMark(10f);

            for (int i = 1; i <= 5; i++)
            {
                hwm.Record(i, i * 10);
            }

            int peakBeforeExpiry = hwm.GetPeak(5.0f);
            TestContext.WriteLine($"Peak before expiry: {peakBeforeExpiry}");
            Assert.Greater(peakBeforeExpiry, 0);

            int peakAfterExpiry = hwm.GetPeak(16.0f);
            TestContext.WriteLine($"Peak after expiry at t=16: {peakAfterExpiry}");
            Assert.AreEqual(0, peakAfterExpiry);
        }

        [Test]
        public void RecordAndGetAverageMatchesSeparateCalls()
        {
            RollingHighWaterMark combined = new RollingHighWaterMark(300f);
            RollingHighWaterMark separate = new RollingHighWaterMark(300f);

            int[] values = { 5, 12, 3, 20, 8, 15, 1, 10 };
            for (int i = 0; i < values.Length; i++)
            {
                float time = 1.0f + i;
                float combinedAvg = combined.RecordAndGetAverage(time, values[i]);
                separate.Record(time, values[i]);
                float separateAvg = separate.GetAverage(time);

                TestContext.WriteLine(
                    $"t={time}: value={values[i]}, combinedAvg={combinedAvg:F3}, separateAvg={separateAvg:F3}"
                );
                Assert.AreEqual(
                    separateAvg,
                    combinedAvg,
                    0.001f,
                    $"RecordAndGetAverage average mismatch at t={time}"
                );
            }

            int combinedPeak = combined.GetPeak(9.0f);
            int separatePeak = separate.GetPeak(9.0f);
            TestContext.WriteLine($"Final peaks: combined={combinedPeak}, separate={separatePeak}");
            Assert.AreEqual(
                separatePeak,
                combinedPeak,
                "Peaks should match after equivalent operations"
            );
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    internal sealed class PoolUsageTrackerTests
    {
        private PoolUsageTracker _tracker;
        private bool _wasMemoryPressureEnabled;

        [SetUp]
        public void SetUp()
        {
            PoolPurgeSettings.ResetToDefaults();
            _wasMemoryPressureEnabled = MemoryPressureMonitor.Enabled;
            MemoryPressureMonitor.Enabled = false;
            _tracker = new PoolUsageTracker(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 2.0f
            );
        }

        [TearDown]
        public void TearDown()
        {
            PoolPurgeSettings.ResetToDefaults();
            MemoryPressureMonitor.Enabled = _wasMemoryPressureEnabled;
        }

        [Test]
        public void RecordRentIncrementsCurrentlyRented()
        {
            _tracker.RecordRent(1.0f);

            int currentlyRented = _tracker.CurrentlyRented;
            TestContext.WriteLine($"Currently rented after one rent: {currentlyRented}");
            Assert.AreEqual(1, currentlyRented);
        }

        [Test]
        public void RecordReturnDecrementsCurrentlyRented()
        {
            _tracker.RecordRent(1.0f);
            _tracker.RecordRent(1.0f);

            int afterTwoRents = _tracker.CurrentlyRented;
            TestContext.WriteLine($"Currently rented after two rents: {afterTwoRents}");
            Assert.AreEqual(2, afterTwoRents);

            _tracker.RecordReturn(2.0f);

            int afterOneReturn = _tracker.CurrentlyRented;
            TestContext.WriteLine($"Currently rented after one return: {afterOneReturn}");
            Assert.AreEqual(1, afterOneReturn);
        }

        [Test]
        public void CurrentlyRentedDoesNotGoBelowZero()
        {
            _tracker.RecordReturn(1.0f);

            int currentlyRented = _tracker.CurrentlyRented;
            TestContext.WriteLine(
                $"Currently rented after return with no rents: {currentlyRented}"
            );
            Assert.GreaterOrEqual(currentlyRented, 0);
        }

        [Test]
        public void PeakConcurrentRentalsTracksMaximum()
        {
            _tracker.RecordRent(1.0f);
            _tracker.RecordRent(2.0f);
            _tracker.RecordRent(3.0f);
            _tracker.RecordRent(4.0f);
            _tracker.RecordRent(5.0f);

            _tracker.RecordReturn(6.0f);
            _tracker.RecordReturn(7.0f);
            _tracker.RecordReturn(8.0f);

            int peak = _tracker.PeakConcurrentRentals;
            TestContext.WriteLine($"Peak concurrent rentals: {peak}");
            Assert.AreEqual(5, peak);
        }

        [Test]
        public void GetPurgeParametersReturnsValidSnapshot()
        {
            _tracker.RecordRent(1.0f);
            _tracker.RecordRent(2.0f);
            _tracker.RecordReturn(3.0f);

            PurgeParameters parameters = _tracker.GetPurgeParameters(
                currentTime: 4.0f,
                baseIdleTimeoutSeconds: 60f,
                minRetainCount: 2,
                warmRetainCount: 5,
                useIntelligent: true,
                pressureLevel: MemoryPressureLevel.None
            );

            TestContext.WriteLine(
                $"PurgeParameters - IdleTimeout: {parameters.EffectiveIdleTimeout}, "
                    + $"MinRetain: {parameters.EffectiveMinRetainCount}, "
                    + $"ComfortableSize: {parameters.ComfortableSize}, "
                    + $"InHysteresis: {parameters.InHysteresis}"
            );
            Assert.Greater(parameters.EffectiveIdleTimeout, 0f);
            Assert.GreaterOrEqual(parameters.EffectiveMinRetainCount, 2);
            Assert.GreaterOrEqual(parameters.ComfortableSize, parameters.EffectiveMinRetainCount);
        }

        [Test]
        public void SpikeDetectionTriggers()
        {
            _tracker = new PoolUsageTracker(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.0f,
                bufferMultiplier: 2.0f
            );

            for (int i = 0; i < 5; i++)
            {
                _tracker.RecordRent(1.0f + i * 0.1f);
                _tracker.RecordReturn(1.5f + i * 0.1f);
            }

            for (int i = 0; i < 20; i++)
            {
                _tracker.RecordRent(10.0f + i * 0.01f);
            }

            PurgeParameters parameters = _tracker.GetPurgeParameters(
                currentTime: 10.5f,
                baseIdleTimeoutSeconds: 60f,
                minRetainCount: 0,
                warmRetainCount: 0,
                useIntelligent: true,
                pressureLevel: MemoryPressureLevel.None
            );

            TestContext.WriteLine(
                $"InHysteresis after spike: {parameters.InHysteresis}, "
                    + $"CurrentlyRented: {_tracker.CurrentlyRented}"
            );
            Assert.IsTrue(parameters.InHysteresis);
        }

        [Test]
        public void HysteresisExpiresAfterConfiguredDuration()
        {
            _tracker = new PoolUsageTracker(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 10f,
                spikeThresholdMultiplier: 2.0f,
                bufferMultiplier: 2.0f
            );

            for (int i = 0; i < 5; i++)
            {
                _tracker.RecordRent(1.0f + i * 0.1f);
                _tracker.RecordReturn(1.5f + i * 0.1f);
            }

            for (int i = 0; i < 20; i++)
            {
                _tracker.RecordRent(10.0f + i * 0.01f);
            }

            bool inHysteresisBeforeExpiry = _tracker.IsInHysteresisPeriod(10.5f);
            TestContext.WriteLine($"InHysteresis before expiry: {inHysteresisBeforeExpiry}");
            Assert.IsTrue(inHysteresisBeforeExpiry);

            bool inHysteresisAfterExpiry = _tracker.IsInHysteresisPeriod(25.0f);
            TestContext.WriteLine($"InHysteresis after expiry: {inHysteresisAfterExpiry}");
            Assert.IsFalse(inHysteresisAfterExpiry);
        }

        [Test]
        public void FrequencyStatisticsReportsCorrectRate()
        {
            for (int i = 0; i < 10; i++)
            {
                _tracker.RecordRent(1.0f + i * 3.0f);
            }

            PoolFrequencyStatistics stats = _tracker.GetFrequencyStatistics(31.0f);
            TestContext.WriteLine(
                $"RentalsPerMinute: {stats.RentalsPerMinute}, "
                    + $"TotalRentalCount: {stats.TotalRentalCount}"
            );
            Assert.AreEqual(10, stats.TotalRentalCount);
            Assert.Greater(stats.RentalsPerMinute, 0f);
        }

        [Test]
        public void ClearResetsTrackerState()
        {
            _tracker.RecordRent(1.0f);
            _tracker.RecordRent(2.0f);
            _tracker.RecordReturn(3.0f);

            _tracker.Clear();

            int currentlyRented = _tracker.CurrentlyRented;
            int peakRentals = _tracker.PeakConcurrentRentals;
            long totalRentals = _tracker.TotalRentalCount;
            TestContext.WriteLine(
                $"After clear - CurrentlyRented: {currentlyRented}, "
                    + $"PeakRentals: {peakRentals}, TotalRentals: {totalRentals}"
            );
            Assert.AreEqual(0, currentlyRented);
            Assert.AreEqual(0, peakRentals);
            Assert.AreEqual(0, totalRentals);
        }

        [Test]
        public void ComfortableSizeRespectsMinRetainCount()
        {
            _tracker.RecordRent(1.0f);
            _tracker.RecordRent(2.0f);

            int comfortableSize = _tracker.GetComfortableSize(
                currentTime: 3.0f,
                effectiveMinRetainCount: 5
            );

            TestContext.WriteLine(
                $"ComfortableSize with minRetain=5 and 2 rents: {comfortableSize}"
            );
            Assert.GreaterOrEqual(comfortableSize, 5);
        }

        [Test]
        public void ComfortableSizeUsesBufferMultiplier()
        {
            _tracker = new PoolUsageTracker(
                rollingWindowSeconds: 300f,
                hysteresisSeconds: 120f,
                spikeThresholdMultiplier: 2.5f,
                bufferMultiplier: 2.0f
            );

            for (int i = 0; i < 10; i++)
            {
                _tracker.RecordRent(1.0f + i * 0.1f);
            }

            int rollingPeak = _tracker.GetRollingHighWaterMark(2.0f);
            int comfortableSize = _tracker.GetComfortableSize(
                currentTime: 2.0f,
                effectiveMinRetainCount: 0
            );

            TestContext.WriteLine(
                $"Rolling peak: {rollingPeak}, ComfortableSize: {comfortableSize}, "
                    + $"Expected ~{rollingPeak * 2.0f}"
            );
            Assert.AreEqual(10, rollingPeak);
            Assert.GreaterOrEqual(comfortableSize, (int)(rollingPeak * 1.5f));
        }
    }
}
