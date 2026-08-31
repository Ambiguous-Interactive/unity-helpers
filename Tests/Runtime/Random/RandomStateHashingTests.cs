// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    /// <summary>
    /// <c>RandomState</c> carries the gaussian reservoir as two independent wire fields -- a flag and
    /// a value -- and nothing correlates them, so a payload may claim no reservoir while carrying a
    /// value in the slot. <c>Equals</c> reads the value only when the flag says to; the hash has to
    /// do the same, or a set reports a state absent and accepts a duplicate of it.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RandomStateHashingTests
    {
        private static int HashOf(bool hasGaussian, double gaussian)
        {
            return RandomState.ComputeHashCode(
                state1: 1UL,
                state2: 0UL,
                hasGaussian: hasGaussian,
                gaussian: gaussian,
                payload: null,
                bitBuffer: 0u,
                bitCount: 0,
                byteBuffer: 0u,
                byteCount: 0
            );
        }

        [Test]
        public void HashIgnoresTheGaussianSlotWhenTheStateHoldsNone()
        {
            Assert.AreEqual(
                HashOf(hasGaussian: false, gaussian: 0d),
                HashOf(hasGaussian: false, gaussian: 5d),
                "Equals ignores the gaussian slot when the flag is clear, so the hash must too"
            );
        }

        [Test]
        public void HashSeparatesAHeldGaussianFromAnUnheldOne()
        {
            Assert.AreNotEqual(
                HashOf(hasGaussian: false, gaussian: 0d),
                HashOf(hasGaussian: true, gaussian: 0d),
                "Whether a reservoir is held is part of what Equals compares"
            );
        }

        [Test]
        public void HashTreatsEveryNaNPayloadAsOneValue()
        {
            double quietNaN = BitConverter.Int64BitsToDouble(unchecked((long)0xFFF8000000000000UL));
            double taggedNaN = BitConverter.Int64BitsToDouble(
                unchecked((long)0x7FF8000000000001UL)
            );

            Assert.IsTrue(double.IsNaN(quietNaN) && double.IsNaN(taggedNaN));
            Assert.AreEqual(
                HashOf(hasGaussian: true, gaussian: quietNaN),
                HashOf(hasGaussian: true, gaussian: taggedNaN),
                "TotalEquals reports every NaN equal to every other, so they must share a hash"
            );
        }

        [Test]
        public void HashTreatsNegativeZeroAsPositiveZero()
        {
            double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);

            Assert.IsTrue(double.IsNegative(negativeZero));
            Assert.AreEqual(
                HashOf(hasGaussian: true, gaussian: 0d),
                HashOf(hasGaussian: true, gaussian: negativeZero),
                "TotalEquals reports negative zero equal to positive zero"
            );
        }

        [Test]
        public void ConstructedStatesStillAgreeWithTheirHash()
        {
            RandomState withGaussian = new(1, gaussian: 1.25d);
            RandomState clone = new(1, gaussian: 1.25d);
            RandomState withoutGaussian = new(1);

            Assert.AreEqual(withGaussian, clone);
            Assert.AreEqual(withGaussian.GetHashCode(), clone.GetHashCode());
            Assert.AreNotEqual(withGaussian, withoutGaussian);
        }
    }
}
