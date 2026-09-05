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

        private static readonly uint[] Bounds32 = CreateBounds32();
        private static readonly ulong[] Bounds64 = CreateBounds64();

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

                    /*
                        Compare BigInteger on both sides; NUnit cannot coerce it, and narrowing the oracle could
                        hide overflow.
                    */
                    Assert.AreEqual(
                        ExpectedMultiplyHigh(draw, bound, 32),
                        new BigInteger(value),
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
                        ExpectedMultiplyHigh(draw, bound, 64),
                        new BigInteger(value),
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
            Assert.IsFalse(random.TryNextUint(10u, 13u, out uint ranged));
            Assert.AreEqual(0u, ranged, "Failure must not return the nonzero minimum.");
        }

        [Test]
        public void TryNextUlongReportsAnExhaustedSourceInsteadOfDegrading()
        {
            ScriptedRandom random = new();
            random.SetConstant(0u);

            bool sampled = random.TryNextUlong(3UL, out ulong value);

            Assert.IsFalse(sampled);
            Assert.AreEqual(0UL, value);
            Assert.IsFalse(random.TryNextUlong(10UL, 13UL, out ulong ranged));
            Assert.AreEqual(0UL, ranged, "Failure must not return the nonzero minimum.");
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

        [Test]
        public void TryNextDoubleReportsNestedBoundedEntropyExhaustion()
        {
            ScriptedRandom random = new();
            random.SetConstant(0u);

            bool sampled = random.TryNextDouble(2.0, double.PositiveInfinity, out double value);

            Assert.IsFalse(sampled, "A finite modulo fallback is not an exact sample.");
            Assert.AreEqual(default(double), value);
            Assert.AreEqual(2 * ((1 << 20) + 1), random.UintCalls);
        }

        [Test]
        public void TryNextDoubleStopsWhenNestedFailureWouldRetryInfinity()
        {
            ScriptedRandom random = new();
            random.SetConstant(0u);
            random.MaximumUintCalls = 2 * ((1 << 20) + 1);

            Assert.IsFalse(random.TryNextDouble(double.NegativeInfinity, 0, out double value));
            Assert.AreEqual(default(double), value);
            Assert.AreEqual(random.MaximumUintCalls, random.UintCalls);
        }

        [Test]
        public void LegacyDoubleRetainsItsNestedBoundedFallback()
        {
            ScriptedRandom random = new();
            random.SetConstant(0u);

            Assert.AreEqual(2.0, random.NextDouble(2.0, double.PositiveInfinity));
            Assert.AreEqual(2 * ((1 << 20) + 1), random.UintCalls);
        }

        [TestCase(65536, true, 65537)]
        [TestCase(65537, false, 65537)]
        public void TryNextUintHonoursTheLastPermittedDraw(
            int rejectedDraws,
            bool expectedSuccess,
            int expectedDraws
        )
        {
            ScriptedRandom random = new();
            random.SetPrefix(0u, rejectedDraws);
            random.SetConstant(uint.MaxValue);

            Assert.AreEqual(expectedSuccess, random.TryNextUint(3u, out uint value));
            Assert.AreEqual(expectedSuccess ? 2u : 0u, value);
            Assert.AreEqual(expectedDraws, random.UintCalls);
        }

        [TestCase(1048576, true, 1048577)]
        [TestCase(1048577, false, 1048577)]
        public void TryNextUlongHonoursTheLastPermittedDraw(
            int rejectedDraws,
            bool expectedSuccess,
            int expectedDraws
        )
        {
            ScriptedRandom random = new();
            random.SetPrefix(0u, 2 * rejectedDraws);
            random.SetConstant(uint.MaxValue);

            Assert.AreEqual(expectedSuccess, random.TryNextUlong(3UL, out ulong value));
            Assert.AreEqual(expectedSuccess ? 2UL : 0UL, value);
            Assert.AreEqual(2 * expectedDraws, random.UintCalls);
        }

        [TestCase(1048576, true, 1048577)]
        [TestCase(1048577, false, 1048577)]
        public void TryNextDoubleHonoursTheLastPermittedInfiniteCandidate(
            int rejectedDraws,
            bool expectedSuccess,
            int expectedDraws
        )
        {
            double maximum = BitConverter.Int64BitsToDouble(0x000FFFFFFFFFFFFFL);
            ScriptedRandom random = new();
            random.SetPrefix(0u, 2 * rejectedDraws);
            random.SetConstant(uint.MaxValue);
            random.MaximumUintCalls = 2 * expectedDraws;

            Assert.AreEqual(
                expectedSuccess,
                random.TryNextDouble(double.NegativeInfinity, maximum, out double value)
            );
            Assert.AreEqual(
                expectedSuccess ? 0x000FFFFFFFFFFFFEL : 0L,
                BitConverter.DoubleToInt64Bits(value)
            );
            Assert.AreEqual(2 * expectedDraws, random.UintCalls);
        }

        [TestCase(1048575, true, 1048576)]
        [TestCase(1048576, false, 1048578)]
        public void TryNextGaussianHonoursTheLastPermittedPair(
            int rejectedPairs,
            bool expectedSuccess,
            int expectedPairs
        )
        {
            ScriptedRandom random = new();
            random.SetPrefix(0u, 4 * rejectedPairs);
            random.SetConstant(0x40000000u);

            Assert.AreEqual(expectedSuccess, random.TryNextGaussian(0, 1, out double value));
            Assert.AreEqual(4 * expectedPairs, random.UintCalls);
            if (!expectedSuccess)
            {
                Assert.AreEqual(default(double), value);
            }
        }

        [Test]
        public void FailedGaussianDoesNotCacheItsDegradedPartner()
        {
            ScriptedRandom random = new();
            random.SetPrefix(0u, 4 * ((1 << 20) + 1));
            random.SetConstant(0x40000000u);

            Assert.IsFalse(random.TryNextGaussian(0, 1, out double failed));
            Assert.AreEqual(default(double), failed);
            int previousCalls = random.UintCalls;
            Assert.IsTrue(random.TryNextGaussian(0, 1, out double exact));
            Assert.IsFalse(double.IsNaN(exact));
            Assert.AreEqual(previousCalls + 4, random.UintCalls);
            Assert.IsTrue(random.TryNextGaussian(0, 1, out _));
            Assert.AreEqual(previousCalls + 4, random.UintCalls);
        }

        [Test]
        public void TryNextDoubleRefusesARoundedExclusiveEndpoint()
        {
            double maximum = BitConverter.Int64BitsToDouble(
                BitConverter.DoubleToInt64Bits(1.0) + 1
            );
            ScriptedRandom random = new();
            random.SetConstant(uint.MaxValue);

            Assert.IsFalse(random.TryNextDouble(1.0, maximum, out double value));
            Assert.AreEqual(default(double), value);
            Assert.AreEqual(2, random.UintCalls);
            Assert.AreEqual(
                maximum,
                random.NextDouble(1.0, maximum),
                "Legacy rounding and its stream remain unchanged."
            );

            random.SetConstant(0u);
            Assert.IsTrue(random.TryNextDouble(1.0, maximum, out double minimum));
            Assert.AreEqual(1.0, minimum);
        }

        [TestCase(1, 3221225472u)]
        [TestCase(-1, 1073741824u)]
        public void TryNextGaussianRefusesFiniteParameterOverflow(int sign, uint draw)
        {
            ScriptedRandom random = new();
            random.SetConstant(draw);

            Assert.IsFalse(
                random.TryNextGaussian(sign * double.MaxValue, double.MaxValue, out double value)
            );
            Assert.AreEqual(default(double), value);
            Assert.AreEqual(4, random.UintCalls);
            Assert.IsTrue(random.TryNextGaussian(0, 1, out double exact));
            Assert.IsFalse(double.IsNaN(exact));
            Assert.IsFalse(double.IsInfinity(exact));
            Assert.AreEqual(
                4,
                random.UintCalls,
                "A finite cached deviate remains exact after a transform overflows."
            );
            Assert.IsTrue(
                double.IsInfinity(random.NextGaussian(sign * double.MaxValue, double.MaxValue)),
                "Legacy overflow behavior remains unchanged."
            );
        }

        [Test]
        public void BoundedUintAcceptanceMatchesIndependentIntegerRemainders()
        {
            foreach (uint bound in Bounds32)
            {
                foreach (uint draw in Draws32)
                {
                    ScriptedRandom random = new();
                    random.EnqueueUint(draw);
                    random.SetConstant(uint.MaxValue);
                    bool accepted = IsAccepted32(draw, bound);

                    Assert.IsTrue(random.TryNextUint(bound, out uint value));
                    Assert.AreEqual(
                        accepted ? 1 : 2,
                        random.UintCalls,
                        "bound {0}, draw {1}",
                        bound,
                        draw
                    );
                    Assert.AreEqual(
                        ExpectedMultiplyHigh(accepted ? draw : uint.MaxValue, bound, 32),
                        new BigInteger(value)
                    );
                }
            }
        }

        [Test]
        public void BoundedUlongAcceptanceMatchesIndependentIntegerRemainders()
        {
            foreach (ulong bound in Bounds64)
            {
                foreach (uint seed in Draws32)
                {
                    ulong draw = ((ulong)seed << 32) | seed;
                    ScriptedRandom random = new();
                    random.EnqueueUlong(draw);
                    random.SetConstant(uint.MaxValue);
                    bool accepted = IsAccepted64(draw, bound);

                    Assert.IsTrue(random.TryNextUlong(bound, out ulong value));
                    Assert.AreEqual(
                        accepted ? 2 : 4,
                        random.UintCalls,
                        "bound {0}, draw {1}",
                        bound,
                        draw
                    );
                    Assert.AreEqual(
                        ExpectedMultiplyHigh(accepted ? draw : ulong.MaxValue, bound, 64),
                        new BigInteger(value)
                    );
                }
            }
        }

        private static uint[] CreateBounds32()
        {
            HashSet<uint> bounds = new() { uint.MaxValue, 1000u };
            for (int shift = 0; shift < 32; shift++)
            {
                uint power = 1u << shift;
                bounds.Add(power);
                bounds.Add(power + 1);
                if (1 < power)
                {
                    bounds.Add(power - 1);
                }
            }
            uint[] result = new uint[bounds.Count];
            bounds.CopyTo(result);
            return result;
        }

        private static ulong[] CreateBounds64()
        {
            HashSet<ulong> bounds = new() { ulong.MaxValue, 1000UL };
            for (int shift = 0; shift < 64; shift++)
            {
                ulong power = 1UL << shift;
                bounds.Add(power);
                bounds.Add(power + 1);
                if (1 < power)
                {
                    bounds.Add(power - 1);
                }
            }
            ulong[] result = new ulong[bounds.Count];
            bounds.CopyTo(result);
            return result;
        }

        private static bool IsAccepted32(uint draw, uint bound)
        {
            BigInteger domain = BigInteger.One << 32;
            return domain % bound <= new BigInteger(draw) * bound % domain;
        }

        private static bool IsAccepted64(ulong draw, ulong bound)
        {
            BigInteger domain = BigInteger.One << 64;
            return domain % bound <= new BigInteger(draw) * bound % domain;
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
            private uint _prefix;
            private int _prefixRemaining;

            public int UintCalls { get; private set; }

            public int MaximumUintCalls { get; set; } = int.MaxValue;

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

            public void SetPrefix(uint value, int count)
            {
                _prefix = value;
                _prefixRemaining = count;
            }

            public void SetConstant(uint value)
            {
                _hasConstant = true;
                _constant = value;
            }

            public override uint NextUint()
            {
                ++UintCalls;
                if (MaximumUintCalls < UintCalls)
                {
                    throw new InvalidOperationException(
                        "The sampling operation exceeded its draw budget."
                    );
                }
                if (0 < _prefixRemaining)
                {
                    --_prefixRemaining;
                    return _prefix;
                }
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
