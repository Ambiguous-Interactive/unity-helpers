// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Numerics;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class StopwatchTimestampTests
    {
        private static IEnumerable<TestCaseData> DurationCases()
        {
            foreach (
                long ticks in new[]
                {
                    long.MinValue,
                    long.MinValue + 1,
                    long.MinValue / 2,
                    -TimeSpan.TicksPerDay,
                    -TimeSpan.TicksPerSecond - 1,
                    -TimeSpan.TicksPerSecond,
                    -TimeSpan.TicksPerSecond + 1,
                    -1,
                    0,
                    1,
                    TimeSpan.TicksPerSecond - 1,
                    TimeSpan.TicksPerSecond,
                    TimeSpan.TicksPerSecond + 1,
                    TimeSpan.TicksPerDay,
                    long.MaxValue / 2,
                    long.MaxValue - 1,
                    long.MaxValue,
                }
            )
            {
                yield return new TestCaseData(ticks).SetName($"Stopwatch.Duration.Ticks{ticks}");
            }
        }

        private static IEnumerable<TestCaseData> CounterScaleCases()
        {
            foreach (
                long frequency in new[]
                {
                    1000L,
                    TimeSpan.TicksPerSecond,
                    1000000000L,
                    long.MaxValue,
                }
            )
            {
                foreach (
                    long value in new[]
                    {
                        long.MinValue,
                        long.MinValue + 1,
                        -1L,
                        0L,
                        1L,
                        long.MaxValue - 1,
                        long.MaxValue,
                    }
                )
                {
                    yield return new TestCaseData(
                        value,
                        frequency,
                        TimeSpan.TicksPerSecond
                    ).SetName($"CounterScale.ToCounter.Frequency{frequency}.Ticks{value}");
                    yield return new TestCaseData(
                        value,
                        TimeSpan.TicksPerSecond,
                        frequency
                    ).SetName($"CounterScale.ToDuration.Frequency{frequency}.Ticks{value}");
                }
            }
        }

        [TestCaseSource(nameof(CounterScaleCases))]
        public void CounterScalingMatchesExactArithmeticAtFrequencyAndDurationLimits(
            long value,
            long numerator,
            long denominator
        )
        {
            BigInteger exact = (BigInteger)value * numerator / denominator;
            long expected =
                exact < long.MinValue ? long.MinValue
                : long.MaxValue < exact ? long.MaxValue
                : (long)exact;
            Assert.AreEqual(expected, StopwatchTimestamp.ScaleTicks(value, numerator, denominator));
        }

        [TestCaseSource(nameof(DurationCases))]
        public void DurationConversionMatchesIndependentIntegerArithmetic(long ticks)
        {
            BigInteger exact = (BigInteger)ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
            long expected =
                exact < long.MinValue ? long.MinValue
                : long.MaxValue < exact ? long.MaxValue
                : (long)exact;
            TimeSpan duration = TimeSpan.FromTicks(ticks);
            Assert.AreEqual(expected, StopwatchTimestamp.TicksFrom(duration));
        }

        [TestCase(-1L)]
        [TestCase(0L)]
        [TestCase(1L)]
        [TestCase(long.MinValue)]
        [TestCase(long.MaxValue)]
        public void FloatingPointConversionsKeepSignAndMagnitude(long ticks)
        {
            double milliseconds = (double)ticks / Stopwatch.Frequency * 1000.0;
            double nanoseconds = (double)ticks / Stopwatch.Frequency * 1000000000.0;
            Assert.That(
                StopwatchTimestamp.ToMilliseconds(ticks),
                Is.EqualTo(milliseconds)
                    .Within(Math.Abs(milliseconds) * 0.000000000001 + double.Epsilon)
            );
            Assert.That(
                StopwatchTimestamp.ToNanoseconds(ticks),
                Is.EqualTo(nanoseconds)
                    .Within(Math.Abs(nanoseconds) * 0.000000000001 + double.Epsilon)
            );
        }

        [Test]
        public void OneCounterSecondConvertsToOneSecond()
        {
            Assert.That(
                StopwatchTimestamp.ToMilliseconds(Stopwatch.Frequency),
                Is.EqualTo(1000.0).Within(0.000001)
            );
            Assert.That(
                StopwatchTimestamp.ToNanoseconds(Stopwatch.Frequency),
                Is.EqualTo(1000000000.0).Within(0.001)
            );
            Assert.AreEqual(
                Stopwatch.Frequency,
                StopwatchTimestamp.TicksFrom(TimeSpan.FromSeconds(1))
            );
        }

        [Test]
        public void SinceFallsBetweenSurroundingCounterObservations()
        {
            long start = Stopwatch.GetTimestamp();
            TimeSpan elapsed = StopwatchTimestamp.Since(start);
            long end = Stopwatch.GetTimestamp();
            BigInteger maximum =
                (BigInteger)unchecked(end - start) * TimeSpan.TicksPerSecond / Stopwatch.Frequency;
            Assert.That(elapsed.Ticks, Is.GreaterThanOrEqualTo(0));
            Assert.IsTrue(elapsed.Ticks <= maximum);
        }

        [Test]
        public void FutureTimestampProducesANegativeDuration()
        {
            long start = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 60;
            Assert.That(StopwatchTimestamp.Since(start).Ticks, Is.LessThan(0));
        }
    }
}
