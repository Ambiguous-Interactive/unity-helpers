// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Diagnostics;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    public sealed class WallTimeProviderTests
    {
        [Test]
        public void SystemProviderUsesUtcAndTheStopwatchCounter()
        {
            WallTimeProvider clock = WallTimeProvider.System;

            Assert.AreEqual(TimeSpan.Zero, clock.GetUtcNow().Offset);
            Assert.AreEqual(Stopwatch.Frequency, clock.TimestampFrequency);

            long before = Stopwatch.GetTimestamp();
            long timestamp = clock.GetTimestamp();
            long after = Stopwatch.GetTimestamp();
            Assert.GreaterOrEqual(unchecked(timestamp - before), 0L);
            Assert.GreaterOrEqual(unchecked(after - timestamp), 0L);
            Assert.AreEqual(TimeSpan.FromSeconds(1), clock.GetElapsedTime(0, Stopwatch.Frequency));
        }

        [Test]
        public void CurrentCanBeReplacedAndRestored()
        {
            WallTimeProvider original = WallTimeProvider.Current;
            ManualTimeProvider manual = new();
            try
            {
                WallTimeProvider.Current = manual;
                Assert.AreSame(manual, WallTimeProvider.Current);
                WallTimeProvider captured = WallTimeProvider.Current;
                WallTimeProvider.Current = null;
                Assert.AreSame(WallTimeProvider.System, WallTimeProvider.Current);
                Assert.AreEqual(DateTimeOffset.UnixEpoch, captured.GetUtcNow());
            }
            finally
            {
                WallTimeProvider.Current = original;
            }
            Assert.AreSame(original, WallTimeProvider.Current);
        }

        [Test]
        public void ManualAdvanceMovesUtcAndElapsedTimeByTheSameDuration()
        {
            ManualTimeProvider clock = new();
            DateTimeOffset wallStart = clock.GetUtcNow();
            long timestamp = clock.GetTimestamp();
            TimeSpan duration = TimeSpan.FromTicks(120001);

            Assert.AreEqual(DateTimeOffset.UnixEpoch, wallStart);
            Assert.AreEqual(0L, timestamp);
            Assert.AreEqual(TimeSpan.TicksPerSecond, clock.TimestampFrequency);
            Assert.IsTrue(clock.TryAdvance(duration));
            Assert.AreEqual(wallStart + duration, clock.GetUtcNow());
            Assert.AreEqual(duration, clock.GetElapsedTime(timestamp));
            Assert.AreEqual(duration, clock.GetElapsedTime(timestamp, clock.GetTimestamp()));
        }

        [Test]
        public void SettingWallTimeNormalizesOffsetsAndKeepsTheTimestamp()
        {
            DateTimeOffset initial = new(2026, 1, 2, 12, 30, 0, TimeSpan.FromHours(2));
            ManualTimeProvider clock = new(initial);
            Assert.AreEqual(initial.ToUniversalTime(), clock.GetUtcNow());
            Assert.AreEqual(TimeSpan.Zero, clock.GetUtcNow().Offset);

            Assert.IsTrue(clock.TryAdvance(TimeSpan.FromTicks(81)));
            DateTimeOffset earlier = initial.AddDays(-1);
            clock.SetUtcNow(earlier);
            Assert.AreEqual(earlier.ToUniversalTime(), clock.GetUtcNow());
            Assert.AreEqual(TimeSpan.Zero, clock.GetUtcNow().Offset);
            Assert.AreEqual(81L, clock.GetTimestamp());
        }

        [Test]
        public void NegativeAndOversizedAdvancesPreserveBothClocks()
        {
            ManualTimeProvider clock = new(DateTimeOffset.MaxValue.AddTicks(-1));
            Assert.IsFalse(clock.TryAdvance(TimeSpan.FromTicks(-1)));
            Assert.IsFalse(clock.TryAdvance(TimeSpan.FromTicks(2)));
            Assert.AreEqual(DateTimeOffset.MaxValue.AddTicks(-1), clock.GetUtcNow());
            Assert.AreEqual(0L, clock.GetTimestamp());

            Assert.IsTrue(clock.TryAdvance(TimeSpan.Zero));
            Assert.IsTrue(clock.TryAdvance(TimeSpan.FromTicks(1)));
            Assert.AreEqual(DateTimeOffset.MaxValue, clock.GetUtcNow());
            Assert.AreEqual(1L, clock.GetTimestamp());
        }

        [Test]
        public void CounterOverflowLeavesWallTimeUnchanged()
        {
            long range = DateTimeOffset.MaxValue.Ticks - DateTimeOffset.MinValue.Ticks;
            ManualTimeProvider clock = new(DateTimeOffset.MinValue);
            for (int step = 0; step < 2; ++step)
            {
                Assert.IsTrue(clock.TryAdvance(TimeSpan.FromTicks(range)));
                clock.SetUtcNow(DateTimeOffset.MinValue);
            }

            long remainder = long.MaxValue - clock.GetTimestamp();
            Assert.IsTrue(clock.TryAdvance(TimeSpan.FromTicks(remainder)));
            DateTimeOffset current = clock.GetUtcNow();

            Assert.AreEqual(long.MaxValue, clock.GetTimestamp());
            Assert.IsFalse(clock.TryAdvance(TimeSpan.FromTicks(1)));
            Assert.AreEqual(current, clock.GetUtcNow());
            Assert.AreEqual(long.MaxValue, clock.GetTimestamp());
        }

        [Test]
        public void ElapsedTimeReadsOnlyTheEndingTimestamp()
        {
            CountingClock clock = new(3L);
            Assert.AreEqual(TimeSpan.FromTicks(6666666), clock.GetElapsedTime(40L));
            Assert.AreEqual(1, clock.TimestampReads);
            Assert.AreEqual(0, clock.WallReads);
            Assert.AreEqual(TimeSpan.FromTicks(-6666666), clock.GetElapsedTime(42L, 40L));
            Assert.AreEqual(1, clock.TimestampReads);
        }

        [TestCase(long.MaxValue, long.MinValue, 1L)]
        [TestCase(long.MinValue, long.MaxValue, -1L)]
        public void ElapsedTimePreservesCounterWrap(long start, long end, long ticks)
        {
            Assert.AreEqual(
                TimeSpan.FromTicks(ticks),
                new ManualTimeProvider().GetElapsedTime(start, end)
            );
        }

        [TestCase(long.MaxValue)]
        [TestCase(long.MinValue)]
        public void LowFrequencyElapsedTimeSaturates(long end)
        {
            CountingClock clock = new(1L);
            Assert.AreEqual(TimeSpan.FromTicks(end), clock.GetElapsedTime(0L, end));
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        public void InvalidCustomFrequencyReturnsZero(long frequency)
        {
            CountingClock clock = new(frequency);
            Assert.AreEqual(TimeSpan.Zero, clock.GetElapsedTime(0L, 1L));
        }

        private sealed class CountingClock : WallTimeProvider
        {
            public override long TimestampFrequency { get; }

            public int TimestampReads { get; private set; }
            public int WallReads { get; private set; }

            public CountingClock(long frequency)
            {
                TimestampFrequency = frequency;
            }

            public override DateTimeOffset GetUtcNow()
            {
                ++WallReads;
                return DateTimeOffset.UnixEpoch;
            }

            public override long GetTimestamp()
            {
                return 42L + TimestampReads++;
            }
        }
    }
}
