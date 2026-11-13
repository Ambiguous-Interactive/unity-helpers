// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.TestDoubles;

    [TestFixture]
    [Category("Fast")]
    public sealed class RandomDistributionTests
    {
        [TestCase(-1d, false)]
        [TestCase(0d, false)]
        [TestCase(1d, true)]
        [TestCase(2d, true)]
        [TestCase(double.NaN, false)]
        [TestCase(double.NegativeInfinity, false)]
        [TestCase(double.PositiveInfinity, true)]
        public void SparingProbabilityEndpointsDoNotDraw(double probability, bool expected)
        {
            EdgeCaseRandom random = new(maxDoubleCalls: 0);
            Assert.That(random.NextBoolSparing(probability), Is.EqualTo(expected));
        }

        [TestCase(0d, true)]
        [TestCase(0.249d, true)]
        [TestCase(0.25d, false)]
        [TestCase(0.999d, false)]
        public void SparingProbabilityConsumesOneDouble(double sample, bool expected)
        {
            EdgeCaseRandom random = new(doubleFallback: sample, maxDoubleCalls: 1);
            Assert.That(random.NextBoolSparing(0.25d), Is.EqualTo(expected));
            Assert.Throws<InvalidOperationException>(() => random.NextDouble());
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidExponentialRateDoesNotDraw(double rate)
        {
            EdgeCaseRandom random = new(maxDoubleCalls: 0);
            Assert.That(random.NextExponential(rate), Is.NaN);
        }

        [TestCase(0d, 1d, 0d)]
        [TestCase(0.5d, 1d, 0.6931471805599453d)]
        [TestCase(0.5d, 2d, 0.34657359027997265d)]
        [TestCase(0.75d, 1d, 1.3862943611198906d)]
        public void ExponentialHasFrozenFormula(double sample, double rate, double expected)
        {
            EdgeCaseRandom random = new(doubleFallback: sample, maxDoubleCalls: 1);
            Assert.That(random.NextExponential(rate), Is.EqualTo(expected).Within(1e-14));
            Assert.Throws<InvalidOperationException>(() => random.NextDouble());
        }

        [TestCase(0d, -3.6037789929704576d)]
        [TestCase(1e-20d, -3.829764718801947d)]
        [TestCase(double.Epsilon, -6.612632354487146d)]
        [TestCase(0.5d, 0.36651292058166435d)]
        [TestCase(0.75d, 1.2458993237072382d)]
        public void GumbelHasFrozenFormula(double sample, double expected)
        {
            EdgeCaseRandom random = new(doubleFallback: sample, maxDoubleCalls: 1);
            Assert.That(random.NextGumbel(), Is.EqualTo(expected).Within(1e-14));
            Assert.Throws<InvalidOperationException>(() => random.NextDouble());
        }

        [TestCase(-1d)]
        [TestCase(1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidUniformsAreRefused(double sample)
        {
            EdgeCaseRandom random = new(doubleFallback: sample);
            Assert.That(random.NextExponential(1d), Is.NaN);
            Assert.That(random.NextGumbel(), Is.NaN);
        }

        [Test]
        public void NullGeneratorsFailWithoutThrowing()
        {
            IRandom random = null;
            Assert.That(random.NextBoolSparing(1d), Is.False);
            Assert.That(random.NextExponential(1d), Is.NaN);
            Assert.That(random.NextGumbel(), Is.NaN);
        }

        [Test]
        public void SparingEndpointsPreserveSeededState()
        {
            PcgRandom random = new(123u);
            IRandom control = random.Copy();
            Assert.That(random.NextBoolSparing(0d), Is.False);
            Assert.That(random.NextBoolSparing(1d), Is.True);
            Assert.That(random.NextDouble(), Is.EqualTo(control.NextDouble()));
        }
    }
}
