// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class BitOpsTests
    {
        private const ulong UlongHighestBit = 0x8000_0000_0000_0000UL;
        private const int UlongBits = 64;
        private const int UlongLog2Max = 63;

        private static readonly Random Rng = new Random(742);

        private static IEnumerable<ulong> SingleBitUlongs()
        {
            for (int bit = 0; bit < UlongBits; ++bit)
            {
                yield return 1UL << bit;
            }
        }

        private static IEnumerable<TestCaseData> PopCountUlongCases()
        {
            yield return new TestCaseData(0UL, 0).SetName("PopCount(ulong).Zero.ReturnsZero");
            yield return new TestCaseData(ulong.MaxValue, UlongBits).SetName(
                "PopCount(ulong).AllBitsSet.ReturnsSixtyFour"
            );
            yield return new TestCaseData(0x5555_5555_5555_5555UL, 32).SetName(
                "PopCount(ulong).AlternatingLowBits.ReturnsThirtyTwo"
            );
            yield return new TestCaseData(0xAAAA_AAAA_AAAA_AAAAUL, 32).SetName(
                "PopCount(ulong).AlternatingHighBits.ReturnsThirtyTwo"
            );
            yield return new TestCaseData(UlongHighestBit, 1).SetName(
                "PopCount(ulong).HighestBit.ReturnsOne"
            );
            int bit = 0;
            foreach (ulong value in SingleBitUlongs())
            {
                yield return new TestCaseData(value, 1).SetName(
                    $"PopCount(ulong).Bit{bit}.ReturnsOne"
                );
                ++bit;
            }
        }

        private static IEnumerable<TestCaseData> TrailingZeroCountUlongCases()
        {
            yield return new TestCaseData(0UL, UlongBits).SetName(
                "TrailingZeroCount(ulong).Zero.ReturnsSixtyFour"
            );
            yield return new TestCaseData(1UL, 0).SetName(
                "TrailingZeroCount(ulong).One.ReturnsZero"
            );
            yield return new TestCaseData(0xFFFF_FFFF_FFFF_FFFFUL, 0).SetName(
                "TrailingZeroCount(ulong).AllBitsSet.ReturnsZero"
            );
            yield return new TestCaseData(UlongHighestBit, UlongLog2Max).SetName(
                "TrailingZeroCount(ulong).HighestBit.ReturnsSixtyThree"
            );
            for (int bit = 0; bit < UlongBits; ++bit)
            {
                ulong value = (1UL << bit) * 0x1743UL;
                yield return new TestCaseData(value, bit).SetName(
                    $"TrailingZeroCount(ulong).PowerOfTwoTimesOdd.Bit{bit}"
                );
            }
        }

        private static IEnumerable<TestCaseData> Log2UlongCases()
        {
            yield return new TestCaseData(0UL, 0).SetName("Log2(ulong).Zero.ReturnsZero");
            yield return new TestCaseData(1UL, 0).SetName("Log2(ulong).One.ReturnsZero");
            yield return new TestCaseData(2UL, 1).SetName("Log2(ulong).Two.ReturnsOne");
            yield return new TestCaseData(3UL, 1).SetName("Log2(ulong).Three.ReturnsOne");
            yield return new TestCaseData(ulong.MaxValue, UlongLog2Max).SetName(
                "Log2(ulong).MaxValue.ReturnsSixtyThree"
            );
            for (int power = 0; power <= UlongLog2Max; ++power)
            {
                ulong exact = 1UL << power;
                yield return new TestCaseData(exact, power).SetName($"Log2(ulong).TwoPow{power}");
                if (0 < power)
                {
                    yield return new TestCaseData(exact - 1UL, power - 1).SetName(
                        $"Log2(ulong).TwoPow{power}MinusOne"
                    );
                    yield return new TestCaseData(exact + 1UL, power).SetName(
                        $"Log2(ulong).TwoPow{power}PlusOne"
                    );
                }
            }
        }

        private static IEnumerable<TestCaseData> HighestSetBitUlongCases()
        {
            yield return new TestCaseData(0UL, 0UL).SetName(
                "HighestSetBit(ulong).Zero.ReturnsZero"
            );
            yield return new TestCaseData(1UL, 1UL).SetName("HighestSetBit(ulong).One.ReturnsOne");
            yield return new TestCaseData(ulong.MaxValue, UlongHighestBit).SetName(
                "HighestSetBit(ulong).AllBitsSet.ReturnsHighestBit"
            );
            int bit = 0;
            foreach (ulong value in SingleBitUlongs())
            {
                yield return new TestCaseData(value, value).SetName(
                    $"HighestSetBit(ulong).Bit{bit}.ReturnsSameBit"
                );
                ++bit;
            }
        }

        private static IEnumerable<TestCaseData> IsPowerOfTwoUlongCases()
        {
            yield return new TestCaseData(0UL, false).SetName(
                "IsPowerOfTwo(ulong).Zero.ReturnsFalse"
            );
            yield return new TestCaseData(1UL, true).SetName("IsPowerOfTwo(ulong).One.ReturnsTrue");
            yield return new TestCaseData(2UL, true).SetName("IsPowerOfTwo(ulong).Two.ReturnsTrue");
            yield return new TestCaseData(3UL, false).SetName(
                "IsPowerOfTwo(ulong).Three.ReturnsFalse"
            );
            yield return new TestCaseData(UlongHighestBit, true).SetName(
                "IsPowerOfTwo(ulong).HighestBit.ReturnsTrue"
            );
            yield return new TestCaseData(ulong.MaxValue, false).SetName(
                "IsPowerOfTwo(ulong).AllBitsSet.ReturnsFalse"
            );
            for (int power = 0; power <= UlongLog2Max; ++power)
            {
                ulong exact = 1UL << power;
                yield return new TestCaseData(exact, true).SetName(
                    $"IsPowerOfTwo(ulong).TwoPow{power}.ReturnsTrue"
                );
                if (1 <= power && power < UlongLog2Max)
                {
                    yield return new TestCaseData(exact + 1UL, false).SetName(
                        $"IsPowerOfTwo(ulong).TwoPow{power}PlusOne.ReturnsFalse"
                    );
                }
            }
        }

        private static IEnumerable<TestCaseData> NextPowerOfTwoUintCases()
        {
            yield return new TestCaseData(0U, 1U).SetName("NextPowerOfTwo(uint).Zero.ReturnsOne");
            yield return new TestCaseData(1U, 1U).SetName("NextPowerOfTwo(uint).One.ReturnsOne");
            yield return new TestCaseData(2U, 2U).SetName("NextPowerOfTwo(uint).Two.ReturnsTwo");
            yield return new TestCaseData(3U, 4U).SetName("NextPowerOfTwo(uint).Three.ReturnsFour");
            yield return new TestCaseData(0x8000_0000U, 0x8000_0000U).SetName(
                "NextPowerOfTwo(uint).LargestPowerOfTwo.ReturnsSame"
            );
            yield return new TestCaseData(0x8000_0000U - 1U, 0x8000_0000U).SetName(
                "NextPowerOfTwo(uint).BelowLargestPowerOfTwo.RoundsUp"
            );
            for (int power = 0; power <= 31; ++power)
            {
                uint exact = 1U << power;
                yield return new TestCaseData(exact, exact).SetName(
                    $"NextPowerOfTwo(uint).TwoPow{power}.ReturnsSame"
                );
                if (power < 31)
                {
                    yield return new TestCaseData(exact + 1U, exact << 1).SetName(
                        $"NextPowerOfTwo(uint).TwoPow{power}PlusOne.RoundsUp"
                    );
                }
            }
        }

        private static ulong RandomUlong()
        {
            return ((ulong)(uint)Rng.Next() << 32) | (uint)Rng.Next();
        }

        private static int ReferencePopCount(ulong value)
        {
            int count = 0;
            while (value != 0)
            {
                value &= value - 1;
                ++count;
            }

            return count;
        }

        private static int ReferenceTrailingZeroCount(uint value)
        {
            if (value == 0)
            {
                return UlongBits / 2;
            }

            int count = 0;
            while ((value & 1) == 0)
            {
                ++count;
                value >>= 1;
            }

            return count;
        }

        private static int ReferenceLog2(uint value)
        {
            int result = 0;
            while (1 < value)
            {
                value >>= 1;
                ++result;
            }

            return result;
        }

        [TestCaseSource(nameof(PopCountUlongCases))]
        public void PopCountUlongReturnsExpectedCount(ulong value, int expected)
        {
            Assert.AreEqual(expected, BitOps.PopCount(value));
        }

        [Test]
        public void PopCountLongMatchesUlongBitPattern()
        {
            Assert.AreEqual(1, BitOps.PopCount(long.MinValue));
            Assert.AreEqual(UlongBits, BitOps.PopCount(-1L));
            Assert.AreEqual(0, BitOps.PopCount(0L));
            Assert.AreEqual(UlongBits, BitOps.PopCount(ulong.MaxValue));
            for (int iteration = 0; iteration < 1_000; ++iteration)
            {
                ulong pattern = RandomUlong();
                Assert.AreEqual(
                    BitOps.PopCount(pattern),
                    BitOps.PopCount(unchecked((long)pattern)),
                    $"PopCount(long) disagrees with PopCount(ulong) for pattern {pattern:X16}."
                );
            }
        }

        [Test]
        public void PopCountIntMatchesReferenceAcrossSignedRange()
        {
            Assert.AreEqual(1, BitOps.PopCount(int.MinValue));
            Assert.AreEqual(UlongBits / 2, BitOps.PopCount(-1));
            Assert.AreEqual(0, BitOps.PopCount(0));
            foreach (int value in new[] { 1, 2, 3, 0x5555_5555, int.MaxValue })
            {
                Assert.AreEqual(
                    ReferencePopCount((uint)value),
                    BitOps.PopCount(value),
                    $"value={value}"
                );
            }

            for (int iteration = 0; iteration < 1_000; ++iteration)
            {
                int value = Rng.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(
                    ReferencePopCount((uint)value),
                    BitOps.PopCount(value),
                    $"value={value}"
                );
            }
        }

        [TestCaseSource(nameof(TrailingZeroCountUlongCases))]
        public void TrailingZeroCountUlongReturnsExpectedIndex(ulong value, int expected)
        {
            Assert.AreEqual(expected, BitOps.TrailingZeroCount(value));
        }

        [Test]
        public void TrailingZeroCountIntMatchesReferenceAcrossSignedRange()
        {
            Assert.AreEqual(UlongBits / 2, BitOps.TrailingZeroCount(0));
            Assert.AreEqual(31, BitOps.TrailingZeroCount(int.MinValue));
            Assert.AreEqual(0, BitOps.TrailingZeroCount(-1));
            Assert.AreEqual(0, BitOps.TrailingZeroCount(1));
            for (int iteration = 0; iteration < 1_000; ++iteration)
            {
                int value = Rng.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(
                    ReferenceTrailingZeroCount((uint)value),
                    BitOps.TrailingZeroCount(value),
                    $"value={value}"
                );
            }
        }

        [TestCaseSource(nameof(Log2UlongCases))]
        public void Log2UlongReturnsExpectedFloor(ulong value, int expected)
        {
            Assert.AreEqual(expected, BitOps.Log2(value));
        }

        [Test]
        public void Log2IntMatchesReferenceAcrossSignedRange()
        {
            Assert.AreEqual(0, BitOps.Log2(0));
            Assert.AreEqual(30, BitOps.Log2(int.MaxValue));
            Assert.AreEqual(31, BitOps.Log2(int.MinValue));
            Assert.AreEqual(31, BitOps.Log2(-1));
            for (int iteration = 0; iteration < 1_000; ++iteration)
            {
                int value = Rng.Next(int.MinValue, int.MaxValue);
                Assert.AreEqual(ReferenceLog2((uint)value), BitOps.Log2(value), $"value={value}");
            }
        }

        [TestCaseSource(nameof(HighestSetBitUlongCases))]
        public void HighestSetBitUlongIsolatesTheTopBit(ulong value, ulong expected)
        {
            Assert.AreEqual(expected, BitOps.HighestSetBit(value));
        }

        [Test]
        public void HighestSetBitIntMatchesLog2AcrossSignedRange()
        {
            Assert.AreEqual(0, BitOps.HighestSetBit(0));
            Assert.AreEqual(int.MinValue, BitOps.HighestSetBit(int.MinValue));
            Assert.AreEqual(1 << 30, BitOps.HighestSetBit(int.MaxValue));
            for (int iteration = 0; iteration < 1_000; ++iteration)
            {
                int value = Rng.Next(int.MinValue, int.MaxValue);
                uint pattern = (uint)value;
                uint expected = pattern == 0U ? 0U : 1U << ReferenceLog2(pattern);
                Assert.AreEqual(
                    unchecked((int)expected),
                    BitOps.HighestSetBit(value),
                    $"value={value}"
                );
            }
        }

        [TestCaseSource(nameof(IsPowerOfTwoUlongCases))]
        public void IsPowerOfTwoUlongReturnsExpectedResult(ulong value, bool expected)
        {
            Assert.AreEqual(expected, BitOps.IsPowerOfTwo(value));
        }

        [Test]
        public void IsPowerOfTwoIntMatchesReferenceAcrossSignedRange()
        {
            Assert.AreEqual(true, BitOps.IsPowerOfTwo(int.MinValue));
            Assert.AreEqual(false, BitOps.IsPowerOfTwo(0));
            Assert.AreEqual(true, BitOps.IsPowerOfTwo(1));
            Assert.AreEqual(false, BitOps.IsPowerOfTwo(-3));
            for (int iteration = 0; iteration < 1_000; ++iteration)
            {
                int value = Rng.Next(int.MinValue, int.MaxValue);
                uint pattern = (uint)value;
                bool expected = pattern != 0U && (pattern & (pattern - 1U)) == 0U;
                Assert.AreEqual(expected, BitOps.IsPowerOfTwo(value), $"value={value}");
            }
        }

        [TestCaseSource(nameof(NextPowerOfTwoUintCases))]
        public void NextPowerOfTwoUintRoundsUpToTheNextPowerOfTwo(uint value, uint expected)
        {
            Assert.AreEqual(expected, BitOps.NextPowerOfTwo(value));
        }

        [Test]
        public void NextPowerOfTwoIntRoundsUpWithinTheRepresentableRange()
        {
            Assert.AreEqual(1, BitOps.NextPowerOfTwo(0));
            Assert.AreEqual(1, BitOps.NextPowerOfTwo(1));
            Assert.AreEqual(2, BitOps.NextPowerOfTwo(2));
            Assert.AreEqual(4, BitOps.NextPowerOfTwo(3));
            Assert.AreEqual(1 << 30, BitOps.NextPowerOfTwo(1 << 30));
            Assert.AreEqual(1 << 30, BitOps.NextPowerOfTwo((1 << 30) - 1));
            int previous = 0;
            for (int value = 2; value <= 1 << 20; value += 997)
            {
                int rounded = BitOps.NextPowerOfTwo(value);
                Assert.LessOrEqual(value, rounded, $"value={value}");
                Assert.Less(rounded, value * 2, $"NextPowerOfTwo({value}) over-rounded.");
                Assert.LessOrEqual(previous, rounded);
                previous = rounded;
            }
        }

        [Test]
        public void NextPowerOfTwoLongMatchesIntForSharedValues()
        {
            Assert.AreEqual(1L, BitOps.NextPowerOfTwo(0L));
            Assert.AreEqual(1L, BitOps.NextPowerOfTwo(1L));
            Assert.AreEqual(1L << 40, BitOps.NextPowerOfTwo((1L << 40) - 1L));
            Assert.AreEqual(1L << 40, BitOps.NextPowerOfTwo(1L << 40));
            Assert.AreEqual(1L << 62, BitOps.NextPowerOfTwo(1L << 62));
        }

        [Test]
        public void NextPowerOfTwoRefusesValuesWithoutAnExactResult()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BitOps.NextPowerOfTwo(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => BitOps.NextPowerOfTwo((1 << 30) + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => BitOps.NextPowerOfTwo(int.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() => BitOps.NextPowerOfTwo(uint.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BitOps.NextPowerOfTwo(0x8000_0000U + 1U)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => BitOps.NextPowerOfTwo(-1L));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BitOps.NextPowerOfTwo((1L << 62) + 1L)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => BitOps.NextPowerOfTwo(long.MaxValue));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                BitOps.NextPowerOfTwo(UlongHighestBit + 1UL)
            );
        }
    }
}
