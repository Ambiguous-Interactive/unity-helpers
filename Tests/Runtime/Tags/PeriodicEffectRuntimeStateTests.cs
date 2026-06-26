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
                consumedTicks++;
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
                consumedTicks++;
            }

            Assert.AreEqual(2, consumedTicks);
            Assert.AreEqual(2, state.ExecutedTicks);
            Assert.IsTrue(state.IsComplete);
        }
    }
}
