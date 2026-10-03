// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Tags;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PeriodicEffectRuntimeStateTests
    {
        [TestCase(100f, 0f, true)]
        [TestCase(100f, 1f, false)]
        [TestCase(16777216f, 0f, true)]
        [TestCase(16777216f, 1f, false)]
        [TestCase(float.MaxValue, 0f, true)]
        [TestCase(float.MaxValue, 1f, false)]
        public void RepeatedClockDoesNotAdvancePeriodicCadence(
            float startTime,
            float initialDelay,
            bool firstTickDue
        )
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { initialDelay = initialDelay, interval = 1f },
                startTime
            );
            Assert.AreEqual(firstTickDue, state.TryConsumeTick(startTime));
            Assert.IsFalse(state.TryConsumeTick(startTime));
            Assert.AreEqual(firstTickDue ? 1 : 0, state.ExecutedTicks);
        }

        [Test]
        public void OppositeFiniteStartAndDelayPreserveSmallElapsedTime()
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { initialDelay = float.MaxValue, interval = 1f },
                -float.MaxValue
            );
            Assert.IsTrue(state.TryConsumeTick(1f));
            Assert.IsTrue(state.TryConsumeTick(1f));
            Assert.IsFalse(state.TryConsumeTick(1f));
        }

        [Test]
        public void SmallPositiveStartBeforeHugeDelayRemainsNotDue()
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { initialDelay = float.MaxValue, interval = 1f },
                1f
            );
            Assert.IsFalse(state.TryConsumeTick(float.MaxValue));
        }

        [Test]
        public void SmallInitialDelaySurvivesHugeElapsedIntervalComparison()
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { initialDelay = 1f, interval = float.MaxValue },
                -float.MaxValue
            );
            Assert.IsTrue(state.TryConsumeTick(1f));
            Assert.IsTrue(state.TryConsumeTick(1f));
            Assert.IsFalse(state.TryConsumeTick(1f));
            Assert.IsFalse(state.TryConsumeTick(float.MaxValue));
        }

        [Test]
        public void HugeIntervalRetainsSmallPositiveStartAtBoundary()
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { interval = float.MaxValue },
                1f
            );
            Assert.IsTrue(state.TryConsumeTick(1f));
            Assert.IsFalse(state.TryConsumeTick(float.MaxValue));
        }

        [Test]
        public void TinyIntervalAfterHugeDelayPreservesCadenceAtUnchangedClock()
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { initialDelay = float.MaxValue, interval = 1f },
                -float.MaxValue
            );
            Assert.IsTrue(state.TryConsumeTick(0f));
            Assert.IsFalse(state.TryConsumeTick(0f));
        }

        [Test]
        public void DueBoundaryAndBackwardClockPreserveScheduledPhase()
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { initialDelay = 1f, interval = 1f },
                100f
            );
            Assert.IsFalse(state.TryConsumeTick(99f));
            Assert.IsFalse(state.TryConsumeTick(100f));
            Assert.IsTrue(state.TryConsumeTick(101f));
            Assert.IsFalse(state.TryConsumeTick(101f));
            Assert.IsFalse(state.TryConsumeTick(100f));
            Assert.IsTrue(state.TryConsumeTick(102f));
            Assert.IsFalse(state.TryConsumeTick(102f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NegativeInfinity)]
        public void NonpositiveIntervalAndDelayKeepMinimumCadence(float authoredInterval)
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { initialDelay = -1f, interval = authoredInterval },
                0f
            );
            Assert.IsTrue(state.TryConsumeTick(0f));
            Assert.IsFalse(state.TryConsumeTick(0f));
            Assert.IsTrue(state.TryConsumeTick(0.01f));
            Assert.IsFalse(state.TryConsumeTick(0.01f));
        }

        [Test]
        public void FiniteCadenceContinuesAcrossFloatDeadlineOverflow()
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { interval = float.MaxValue },
                -float.MaxValue
            );
            Assert.IsTrue(state.TryConsumeTick(-float.MaxValue));
            Assert.IsFalse(state.TryConsumeTick(-float.MaxValue));
            Assert.IsTrue(state.TryConsumeTick(0f));
            Assert.IsFalse(state.TryConsumeTick(0f));
            Assert.IsTrue(state.TryConsumeTick(float.MaxValue));
            Assert.IsFalse(state.TryConsumeTick(float.MaxValue));
            Assert.AreEqual(3, state.ExecutedTicks);
        }

        [TestCase(float.NaN, 0f, 1f, 100f, true, true)]
        [TestCase(100f, 0f, 1f, float.NaN, true, true)]
        [TestCase(100f, float.NaN, 1f, 100f, true, true)]
        [TestCase(100f, float.PositiveInfinity, 1f, 100f, false, false)]
        [TestCase(100f, 0f, float.NaN, 100f, true, true)]
        [TestCase(100f, 0f, float.PositiveInfinity, 100f, true, false)]
        [TestCase(100f, 0f, float.PositiveInfinity, float.PositiveInfinity, true, true)]
        [TestCase(float.PositiveInfinity, 0f, 1f, float.PositiveInfinity, true, true)]
        [TestCase(float.NegativeInfinity, 0f, 1f, float.NegativeInfinity, true, true)]
        public void NonfiniteTimingPreservesTickComparisonPolicy(
            float startTime,
            float delay,
            float authoredInterval,
            float now,
            bool firstDue,
            bool secondDue
        )
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { initialDelay = delay, interval = authoredInterval },
                startTime
            );
            Assert.AreEqual(firstDue, state.TryConsumeTick(now));
            Assert.AreEqual(secondDue, state.TryConsumeTick(now));
        }

        [Test]
        public void PublicTickCounterWrapDoesNotResetScheduledCadence()
        {
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { interval = 1f },
                0f
            );
            state.SetExecutedTicksForTesting(int.MaxValue);
            Assert.IsTrue(state.TryConsumeTick(2147483648f));
            Assert.AreEqual(int.MinValue, state.ExecutedTicks);
            Assert.IsTrue(state.TryConsumeTick(2147483648f));
            Assert.AreEqual(int.MinValue + 1, state.ExecutedTicks);
            Assert.IsFalse(state.TryConsumeTick(2147483648f));
        }

        [Test]
        public void LargeOrdinalProductRemainderKeepsFutureTickNotDue()
        {
            const float StartTime = 158456325028528675187087900672f;
            PeriodicEffectRuntimeState state = new(
                new PeriodicEffectDefinition { interval = float.MaxValue / 2147483648f },
                StartTime
            );
            state.SetExecutedTicksForTesting(int.MaxValue);
            Assert.IsFalse(state.TryConsumeTick(float.MaxValue));
            Assert.AreEqual(int.MaxValue, state.ExecutedTicks);
        }

        [Test]
        public void TickLimitChangesRemainLiveWithoutResettingPhase()
        {
            PeriodicEffectDefinition definition = new() { interval = 1f, maxTicks = 1 };
            PeriodicEffectRuntimeState state = new(definition, 100f);
            Assert.IsTrue(state.TryConsumeTick(100f));
            Assert.IsTrue(state.IsComplete);
            definition.maxTicks = 2;
            Assert.IsFalse(state.IsComplete);
            Assert.IsFalse(state.TryConsumeTick(100f));
            Assert.IsTrue(state.TryConsumeTick(101f));
            Assert.IsTrue(state.IsComplete);
            definition.maxTicks = 0;
            Assert.IsTrue(state.TryConsumeTick(102f));
        }

        [Test]
        public void TryConsumeTickCatchesUpUsingScheduledIntervalsAfterLongFrame()
        {
            PeriodicEffectDefinition definition = new()
            {
                initialDelay = 0.05f,
                interval = 0.1f,
                maxTicks = 3,
            };
            PeriodicEffectRuntimeState state = new(definition, startTime: 0f);

            int consumedTicks = 0;
            while (state.TryConsumeTick(0.35f))
            {
                ++consumedTicks;
            }

            Assert.AreEqual(3, consumedTicks);
            Assert.AreEqual(3, state.ExecutedTicks);
            Assert.IsTrue(state.IsComplete);
        }

        [Test]
        public void TryConsumeTickStopsAtMaxTicksDuringCatchUp()
        {
            PeriodicEffectDefinition definition = new()
            {
                initialDelay = 0f,
                interval = 0.01f,
                maxTicks = 2,
            };
            PeriodicEffectRuntimeState state = new(definition, startTime: 0f);

            int consumedTicks = 0;
            while (state.TryConsumeTick(1f))
            {
                ++consumedTicks;
            }

            Assert.AreEqual(2, consumedTicks);
            Assert.AreEqual(2, state.ExecutedTicks);
            Assert.IsTrue(state.IsComplete);
        }
    }
}
