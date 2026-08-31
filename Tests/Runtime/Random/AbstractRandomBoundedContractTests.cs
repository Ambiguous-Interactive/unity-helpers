// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the bounded-sampling contract against a scripted entropy source, so an exhausted or
    /// adversarial generator is a red test rather than a silently different distribution.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AbstractRandomBoundedContractTests
    {
        private const uint AllOnes32 = 0xFFFFFFFFu;

        private static readonly uint[] Bounds32 =
        {
            1u,
            2u,
            3u,
            5u,
            7u,
            16u,
            17u,
            1000u,
            65535u,
            65536u,
            65537u,
            int.MaxValue,
            2147483648u,
            uint.MaxValue - 1u,
            uint.MaxValue,
        };

        private static readonly ulong[] Bounds64 =
        {
            1UL,
            2UL,
            3UL,
            7UL,
            1024UL,
            1025UL,
            uint.MaxValue,
            1UL << 40,
            (1UL << 40) + 1UL,
            long.MaxValue,
            ulong.MaxValue - 1UL,
            ulong.MaxValue,
        };

        private static readonly uint[] Draws32 =
        {
            0u,
            1u,
            2u,
            0x7FFFFFFFu,
            0x80000000u,
            0xDEADBEEFu,
            0xFFFFFFFEu,
            AllOnes32,
        };

        [Test]
        public void NextExcludesIntMaxValueWhenTheDrawSaysOtherwise()
        {
            ScriptedRandom random = new();
            random.EnqueueUint(AllOnes32);
            random.EnqueueUint(0x1234_5678u);

            int value = random.Next();

            Assert.AreEqual(0x1234_5678 & int.MaxValue, value);
            Assert.AreEqual(2, random.UintCalls, "The out-of-contract draw must be rejected.");
        }

        [Test]
        public void NextLongExcludesLongMaxValueWhenTheDrawSaysOtherwise()
        {
            ScriptedRandom random = new();
            random.EnqueueUlong(ulong.MaxValue);
            random.EnqueueUlong(0x0123_4567_89AB_CDEFUL);

            long value = random.NextLong();

            Assert.AreEqual(0x0123_4567_89AB_CDEFL & long.MaxValue, value);
            Assert.AreEqual(4, random.UintCalls, "The out-of-contract draw must be rejected.");
        }

        [Test]
        public void NextStaysBelowIntMaxValueWhenTheSourceNeverYields()
        {
            ScriptedRandom random = new();
            random.SetConstant(AllOnes32);

            int value = random.Next();

            Assert.IsTrue(0 <= value, "A degraded answer is still non-negative.");
            Assert.IsTrue(value < int.MaxValue, "A degraded answer still honours the contract.");
        }

        [Test]
        public void NextLongStaysBelowLongMaxValueWhenTheSourceNeverYields()
        {
            ScriptedRandom random = new();
            random.SetConstant(AllOnes32);

            long value = random.NextLong();

            Assert.IsTrue(0 <= value, "A degraded answer is still non-negative.");
            Assert.IsTrue(value < long.MaxValue, "A degraded answer still honours the contract.");
        }

        [Test]
        public void BoundedUintMatchesTheMultiplyHighOracleForEveryAcceptedDraw()
        {
            foreach (uint bound in Bounds32)
            {
                foreach (uint draw in Draws32)
                {
                    if (!IsAccepted32(draw, bound))
                    {
                        continue;
                    }

                    ScriptedRandom random = new();
                    random.EnqueueUint(draw);

                    uint value = random.NextUint(bound);

                    Assert.AreEqual(
                        ExpectedMultiplyHigh(draw, bound, 32),
                        value,
                        "bound {0}, draw {1}",
                        bound,
                        draw
                    );
                    Assert.IsTrue(value < bound, "bound {0}, draw {1}", bound, draw);
                }
            }
        }

        [Test]
        public void BoundedUlongMatchesTheMultiplyHighOracleForEveryAcceptedDraw()
        {
            foreach (ulong bound in Bounds64)
            {
                foreach (uint seed in Draws32)
                {
                    ulong draw = ((ulong)seed << 32) | (seed ^ 0x9E37_79B9u);
                    if (!IsAccepted64(draw, bound))
                    {
                        continue;
                    }

                    ScriptedRandom random = new();
                    random.EnqueueUlong(draw);

                    ulong value = random.NextUlong(bound);

                    Assert.AreEqual(
                        (ulong)ExpectedMultiplyHigh(draw, bound, 64),
                        value,
                        "bound {0}, draw {1}",
                        bound,
                        draw
                    );
                    Assert.IsTrue(value < bound, "bound {0}, draw {1}", bound, draw);
                }
            }
        }

        [Test]
        public void AnExhaustedSourceDegradesRatherThanThrowing()
        {
            ScriptedRandom random = new();
            random.SetConstant(0u);

            uint value = random.NextUint(3u);

            Assert.IsTrue(value < 3u);
        }

        [Test]
        public void TryNextUintReportsAnExhaustedSourceInsteadOfDegrading()
        {
            ScriptedRandom random = new();
            random.SetConstant(0u);

            bool sampled = random.TryNextUint(3u, out uint value);

            Assert.IsFalse(sampled);
            Assert.AreEqual(0u, value);
        }

        [Test]
        public void TryNextUlongReportsAnExhaustedSourceInsteadOfDegrading()
        {
            ScriptedRandom random = new();
            random.SetConstant(0u);

            bool sampled = random.TryNextUlong(3UL, out ulong value);

            Assert.IsFalse(sampled);
            Assert.AreEqual(0UL, value);
        }

        [Test]
        public void TryNextUintRefusesAnEmptyRange()
        {
            ScriptedRandom random = new();
            random.SetConstant(0x1234_5678u);

            Assert.IsFalse(random.TryNextUint(0u, out uint zeroBound));
            Assert.AreEqual(0u, zeroBound);
            Assert.IsFalse(random.TryNextUint(7u, 7u, out uint emptyRange));
            Assert.AreEqual(0u, emptyRange);
            Assert.IsFalse(random.TryNextUint(9u, 7u, out uint invertedRange));
            Assert.AreEqual(0u, invertedRange);
        }

        [Test]
        public void TryNextUlongRefusesAnEmptyRange()
        {
            ScriptedRandom random = new();
            random.SetConstant(0x1234_5678u);

            Assert.IsFalse(random.TryNextUlong(0UL, out ulong zeroBound));
            Assert.AreEqual(0UL, zeroBound);
            Assert.IsFalse(random.TryNextUlong(7UL, 7UL, out ulong emptyRange));
            Assert.AreEqual(0UL, emptyRange);
            Assert.IsFalse(random.TryNextUlong(9UL, 7UL, out ulong invertedRange));
            Assert.AreEqual(0UL, invertedRange);
        }

        [Test]
        public void TryNextBoundedRangesLandInsideTheirBounds()
        {
            PcgRandom random = new(1234);
            for (int i = 0; i < 512; ++i)
            {
                Assert.IsTrue(random.TryNextUint(10u, 20u, out uint narrow));
                Assert.IsTrue(10u <= narrow && narrow < 20u, "narrow {0}", narrow);

                Assert.IsTrue(random.TryNextUlong(100UL, 105UL, out ulong wide));
                Assert.IsTrue(100UL <= wide && wide < 105UL, "wide {0}", wide);
            }
        }

        [Test]
        public void TryNextDoubleRefusesANonFiniteOrEmptyRange()
        {
            PcgRandom random = new(4321);

            Assert.IsFalse(random.TryNextDouble(double.NaN, 1.0, out double nanMinimum));
            Assert.AreEqual(0.0, nanMinimum);
            Assert.IsFalse(random.TryNextDouble(0.0, double.NaN, out double nanMaximum));
            Assert.AreEqual(0.0, nanMaximum);
            Assert.IsFalse(random.TryNextDouble(1.0, 1.0, out double emptyRange));
            Assert.AreEqual(0.0, emptyRange);
            Assert.IsFalse(random.TryNextDouble(2.0, 1.0, out double invertedRange));
            Assert.AreEqual(0.0, invertedRange);
        }

        [Test]
        public void TryNextDoubleSamplesFiniteAndInfiniteRanges()
        {
            PcgRandom random = new(8765);
            for (int i = 0; i < 256; ++i)
            {
                Assert.IsTrue(random.TryNextDouble(-3.5, 7.25, out double finite));
                Assert.IsTrue(-3.5 <= finite && finite < 7.25, "finite {0}", finite);

                Assert.IsTrue(
                    random.TryNextDouble(0.0, double.PositiveInfinity, out double unbounded)
                );
                Assert.IsFalse(double.IsNaN(unbounded));
                Assert.IsFalse(double.IsInfinity(unbounded));
                Assert.IsTrue(0.0 <= unbounded, "unbounded {0}", unbounded);
            }
        }

        [Test]
        public void TryNextGaussianRefusesNonFiniteOrNegativeParameters()
        {
            PcgRandom random = new(2468);

            Assert.IsFalse(random.TryNextGaussian(double.NaN, 1.0, out double nanMean));
            Assert.AreEqual(0.0, nanMean);
            Assert.IsFalse(
                random.TryNextGaussian(0.0, double.PositiveInfinity, out double infiniteDeviation)
            );
            Assert.AreEqual(0.0, infiniteDeviation);
            Assert.IsFalse(random.TryNextGaussian(0.0, -1.0, out double negativeDeviation));
            Assert.AreEqual(0.0, negativeDeviation);
            Assert.IsTrue(random.TryNextGaussian(0.0, 0.0, out double degenerate));
            Assert.AreEqual(0.0, degenerate);
        }

        [Test]
        public void TryNextGaussianSamplesTheStandardNormal()
        {
            PcgRandom random = new(1357);
            double sum = 0;
            const int samples = 4096;
            for (int i = 0; i < samples; ++i)
            {
                Assert.IsTrue(random.TryNextGaussian(0.0, 1.0, out double value));
                Assert.IsFalse(double.IsNaN(value));
                sum += value;
            }

            Assert.IsTrue(Math.Abs(sum / samples) < 0.1, "mean {0}", sum / samples);
        }

        private static bool IsAccepted32(uint draw, uint bound)
        {
            if ((bound & (bound - 1)) == 0)
            {
                return true;
            }

            ulong product = (ulong)draw * bound;
            uint low = (uint)product;
            if (bound <= low)
            {
                return true;
            }

            uint threshold = unchecked((0u - bound) % bound);
            return threshold <= low;
        }

        private static bool IsAccepted64(ulong draw, ulong bound)
        {
            if ((bound & (bound - 1)) == 0)
            {
                return true;
            }

            ulong productLow = unchecked(draw * bound);
            if (bound <= productLow)
            {
                return true;
            }

            ulong threshold = unchecked(0UL - bound) % bound;
            return threshold <= productLow;
        }

        private static BigInteger ExpectedMultiplyHigh(ulong draw, ulong bound, int width)
        {
            if ((bound & (bound - 1)) == 0)
            {
                return draw & (bound - 1);
            }

            return new BigInteger(draw) * bound >> width;
        }

        /// <remarks>
        /// WallstopProto resolves a subtype by a number the owning assembly's manifest has to
        /// declare, so an undeclared subclass throws on the first save. This one never reaches the
        /// serializer, and <c>[WProtoNotSerialized]</c> is where that decision is recorded rather
        /// than inferred from the absence of an attribute
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>).
        /// </remarks>
        [WProtoNotSerialized]
        private sealed class ScriptedRandom : AbstractRandom
        {
            private readonly Queue<uint> _values = new();
            private bool _hasConstant;
            private uint _constant;

            public int UintCalls { get; private set; }

            public override RandomState InternalState => new(0UL);

            public void EnqueueUint(uint value)
            {
                _values.Enqueue(value);
            }

            public void EnqueueUlong(ulong value)
            {
                _values.Enqueue((uint)(value >> 32));
                _values.Enqueue((uint)value);
            }

            public void SetConstant(uint value)
            {
                _hasConstant = true;
                _constant = value;
            }

            public override uint NextUint()
            {
                ++UintCalls;
                if (0 < _values.Count)
                {
                    return _values.Dequeue();
                }

                if (_hasConstant)
                {
                    return _constant;
                }

                throw new InvalidOperationException("No values scripted for ScriptedRandom.");
            }

            public override IRandom Copy()
            {
                throw new NotSupportedException("ScriptedRandom does not support cloning.");
            }
        }
    }
}
