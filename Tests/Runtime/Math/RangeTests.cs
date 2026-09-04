// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Math
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Math;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RangeTests
    {
        private static IEnumerable<TestCaseData> WithinRangeTestCases()
        {
            yield return new TestCaseData(0, 10, true, true, 5, true).SetName(
                "WithinRange.InsideInclusive.ReturnsTrue"
            );
            yield return new TestCaseData(0, 10, true, true, 0, true).SetName(
                "WithinRange.AtMinInclusive.ReturnsTrue"
            );
            yield return new TestCaseData(0, 10, true, true, 10, true).SetName(
                "WithinRange.AtMaxInclusive.ReturnsTrue"
            );
            yield return new TestCaseData(0, 10, false, true, 0, false).SetName(
                "WithinRange.AtMinExclusiveStart.ReturnsFalse"
            );
            yield return new TestCaseData(0, 10, false, true, 1, true).SetName(
                "WithinRange.InsideExclusiveStart.ReturnsTrue"
            );
            yield return new TestCaseData(0, 10, true, false, 10, false).SetName(
                "WithinRange.AtMaxExclusiveEnd.ReturnsFalse"
            );
            yield return new TestCaseData(0, 10, true, false, 9, true).SetName(
                "WithinRange.InsideExclusiveEnd.ReturnsTrue"
            );
            yield return new TestCaseData(0, 10, true, true, -1, false).SetName(
                "WithinRange.BelowMin.ReturnsFalse"
            );
            yield return new TestCaseData(0, 10, true, true, 11, false).SetName(
                "WithinRange.AboveMax.ReturnsFalse"
            );
            yield return new TestCaseData(0, 10, false, false, 0, false).SetName(
                "WithinRange.AtMinBothExclusive.ReturnsFalse"
            );
            yield return new TestCaseData(0, 10, false, false, 10, false).SetName(
                "WithinRange.AtMaxBothExclusive.ReturnsFalse"
            );
            yield return new TestCaseData(0, 10, false, false, 5, true).SetName(
                "WithinRange.InsideBothExclusive.ReturnsTrue"
            );
            yield return new TestCaseData(-5, 5, true, true, 0, true).SetName(
                "WithinRange.ZeroInNegativeRange.ReturnsTrue"
            );
            yield return new TestCaseData(-10, -5, true, true, -7, true).SetName(
                "WithinRange.InsideNegativeRange.ReturnsTrue"
            );
            yield return new TestCaseData(-10, -5, true, true, -3, false).SetName(
                "WithinRange.AboveNegativeRange.ReturnsFalse"
            );
        }

        [TestCaseSource(nameof(WithinRangeTestCases))]
        public void WithinRangeReturnsExpected(
            int min,
            int max,
            bool startInclusive,
            bool endInclusive,
            int value,
            bool expected
        )
        {
            Range<int> range = new(min, max, startInclusive, endInclusive);

            bool actual = range.WithinRange(value);

            Assert.AreEqual(expected, actual);
        }

        private static IEnumerable<TestCaseData> ContainsTestCases()
        {
            yield return new TestCaseData(0, 10, true, true, 10, true).SetName(
                "Contains.AtMaxInclusive.ReturnsTrue"
            );
            yield return new TestCaseData(0, 10, true, true, 11, false).SetName(
                "Contains.AboveMax.ReturnsFalse"
            );
            yield return new TestCaseData(0, 10, true, true, 0, true).SetName(
                "Contains.AtMinInclusive.ReturnsTrue"
            );
            yield return new TestCaseData(0, 10, true, true, -1, false).SetName(
                "Contains.BelowMin.ReturnsFalse"
            );
        }

        [TestCaseSource(nameof(ContainsTestCases))]
        public void ContainsReturnsExpected(
            int min,
            int max,
            bool startInclusive,
            bool endInclusive,
            int value,
            bool expected
        )
        {
            Range<int> range = new(min, max, startInclusive, endInclusive);

            bool actual = range.Contains(value);

            Assert.AreEqual(expected, actual);
        }

        private static IEnumerable<TestCaseData> ToStringTestCases()
        {
            yield return new TestCaseData(1, 2, true, true, "[1, 2]").SetName(
                "ToString.BothInclusive.UsesBrackets"
            );
            yield return new TestCaseData(1, 2, false, true, "(1, 2]").SetName(
                "ToString.ExclusiveStart.UsesParenBracket"
            );
            yield return new TestCaseData(1, 2, true, false, "[1, 2)").SetName(
                "ToString.ExclusiveEnd.UsesBracketParen"
            );
            yield return new TestCaseData(1, 2, false, false, "(1, 2)").SetName(
                "ToString.BothExclusive.UsesParens"
            );
            yield return new TestCaseData(-5, 10, true, true, "[-5, 10]").SetName(
                "ToString.NegativeMin.FormatsCorrectly"
            );
            yield return new TestCaseData(0, 0, true, true, "[0, 0]").SetName(
                "ToString.SameMinMax.FormatsCorrectly"
            );
        }

        [TestCaseSource(nameof(ToStringTestCases))]
        public void ToStringReturnsExpected(
            int min,
            int max,
            bool startInclusive,
            bool endInclusive,
            string expected
        )
        {
            Range<int> range = new(min, max, startInclusive, endInclusive);

            string actual = range.ToString();

            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ConstructorSetsValuesAndDefaults()
        {
            Range<int> range = new(1, 5);

            Assert.AreEqual(1, range.Min);
            Assert.AreEqual(5, range.Max);
            Assert.IsTrue(range.StartInclusive);
            Assert.IsTrue(range.EndInclusive);
        }

        [Test]
        public void ConstructorUsesProvidedInclusivityFlags()
        {
            Range<int> range = new(1, 5, false, false);

            Assert.IsFalse(range.StartInclusive);
            Assert.IsFalse(range.EndInclusive);
        }

        [Test]
        public void ConstructorThrowsWhenMinIsGreaterThanMax()
        {
            Assert.Throws<ArgumentException>(() => new Range<int>(5, 4));
        }

        [Test]
        public void OverlapsReturnsTrueWhenRangesIntersect()
        {
            Range<int> first = Range<int>.Inclusive(0, 10);
            Range<int> second = Range<int>.Inclusive(5, 15);

            Assert.IsTrue(first.Overlaps(second));
            Assert.IsTrue(second.Overlaps(first));
        }

        [Test]
        public void OverlapsReturnsFalseWhenRangesAreDisjoint()
        {
            Range<int> first = Range<int>.Inclusive(0, 10);
            Range<int> second = Range<int>.Inclusive(11, 20);

            Assert.IsFalse(first.Overlaps(second));
            Assert.IsFalse(second.Overlaps(first));
        }

        [Test]
        public void OverlapsHandlesExclusiveBoundaries()
        {
            Range<int> first = Range<int>.InclusiveExclusive(0, 10);
            Range<int> second = Range<int>.Inclusive(10, 20);
            Range<int> third = Range<int>.ExclusiveInclusive(10, 20);

            Assert.IsFalse(first.Overlaps(second));
            Assert.IsFalse(first.Overlaps(third));
        }

        [Test]
        public void OverlapsMatchesPointMembershipOracle(
            [Values(false, true)] bool firstStartInclusive,
            [Values(false, true)] bool firstEndInclusive,
            [Values(false, true)] bool secondStartInclusive,
            [Values(false, true)] bool secondEndInclusive
        )
        {
            double[] endpoints = { -2, -1, 0, 1, 2 };
            double[] samples = { -2, -1.5, -1, -0.5, 0, 0.5, 1, 1.5, 2 };
            foreach (double firstMin in endpoints)
            {
                foreach (double firstMax in endpoints)
                {
                    if (firstMax < firstMin)
                    {
                        continue;
                    }
                    Range<double> first = new(
                        firstMin,
                        firstMax,
                        firstStartInclusive,
                        firstEndInclusive
                    );
                    foreach (double secondMin in endpoints)
                    {
                        foreach (double secondMax in endpoints)
                        {
                            if (secondMax < secondMin)
                            {
                                continue;
                            }
                            Range<double> second = new(
                                secondMin,
                                secondMax,
                                secondStartInclusive,
                                secondEndInclusive
                            );
                            bool expected = false;
                            foreach (double point in samples)
                            {
                                bool inFirst =
                                    (firstMin < point || (firstStartInclusive && point == firstMin))
                                    && (
                                        point < firstMax || (firstEndInclusive && point == firstMax)
                                    );
                                bool inSecond =
                                    (
                                        secondMin < point
                                        || (secondStartInclusive && point == secondMin)
                                    )
                                    && (
                                        point < secondMax
                                        || (secondEndInclusive && point == secondMax)
                                    );
                                expected |= inFirst && inSecond;
                            }
                            Assert.That(
                                first.Overlaps(second),
                                Is.EqualTo(expected),
                                $"{first} and {second}"
                            );
                            Assert.That(
                                second.Overlaps(first),
                                Is.EqualTo(expected),
                                $"{second} and {first}"
                            );
                        }
                    }
                }
            }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void OverlapsRejectsInvertedMutableBounds(bool startInclusive, bool endInclusive)
        {
            Range<int> inverted = new(0, 10, startInclusive, endInclusive);
            inverted.min = 20;
            Range<int> containing = Range<int>.Inclusive(int.MinValue, int.MaxValue);
            Assert.That(inverted.Overlaps(containing), Is.False);
            Assert.That(containing.Overlaps(inverted), Is.False);
        }

        [Test]
        public void OverlapsComparesExtremeBoundsWithoutArithmetic()
        {
            Range<long> first = Range<long>.Exclusive(long.MinValue, long.MaxValue);
            Range<long> second = Range<long>.Exclusive(long.MinValue, long.MaxValue);
            Assert.That(first.Overlaps(second), Is.True);
            Assert.That(second.Overlaps(first), Is.True);
            Assert.That(
                first.Overlaps(Range<long>.Inclusive(long.MaxValue, long.MaxValue)),
                Is.False
            );
            Assert.That(
                first.Overlaps(Range<long>.Inclusive(long.MinValue, long.MinValue)),
                Is.False
            );
        }

        [Test]
        public void CompareToOrdersByMinThenMaxThenInclusivity()
        {
            Range<int> baseline = new(0, 10);
            Range<int> largerMin = new(1, 10);
            Range<int> smallerMax = new(0, 9);
            Range<int> exclusiveStart = new(0, 10, false, true);
            Range<int> inclusive = new(0, 10, true, true);

            Assert.Less(baseline.CompareTo(largerMin), 0);
            Assert.Greater(baseline.CompareTo(smallerMax), 0);
            Assert.Less(inclusive.CompareTo(exclusiveStart), 0);
            Assert.Greater(exclusiveStart.CompareTo(inclusive), 0);
            Assert.AreEqual(0, baseline.CompareTo(inclusive));
        }

        [Test]
        public void EqualsReturnsTrueForIdenticalRanges()
        {
            Range<int> first = new(0, 10);
            Range<int> second = new(0, 10);

            Assert.IsTrue(first.Equals(second));
            Assert.IsTrue(first == second);
        }

        [Test]
        public void EqualsReturnsFalseWhenAnyComponentDiffers()
        {
            Range<int> first = new(0, 10);
            Range<int> differentMin = new(1, 10);
            Range<int> differentMax = new(0, 11);
            Range<int> differentInclusivity = new(0, 10, false, true);

            Assert.IsFalse(first.Equals(differentMin));
            Assert.IsFalse(first.Equals(differentMax));
            Assert.IsFalse(first.Equals(differentInclusivity));
            Assert.IsTrue(first != differentInclusivity);
        }

        [Test]
        public void GetHashCodeMatchesForEqualRanges()
        {
            Range<int> first = new(0, 10, false, true);
            Range<int> second = new(0, 10, false, true);

            Assert.AreEqual(first.GetHashCode(), second.GetHashCode());
        }

        [Test]
        public void GetHashCodeDiffersForDifferentRanges()
        {
            Range<int> first = new(0, 10);
            Range<int> second = new(0, 9);

            Assert.AreNotEqual(first.GetHashCode(), second.GetHashCode());
        }

        [Test]
        public void StaticFactoriesConfigureInclusivity()
        {
            Range<int> inclusive = Range<int>.Inclusive(0, 1);
            Range<int> exclusive = Range<int>.Exclusive(0, 1);
            Range<int> inclusiveExclusive = Range<int>.InclusiveExclusive(0, 1);
            Range<int> exclusiveInclusive = Range<int>.ExclusiveInclusive(0, 1);

            Assert.IsTrue(inclusive.StartInclusive);
            Assert.IsTrue(inclusive.EndInclusive);
            Assert.IsFalse(exclusive.StartInclusive);
            Assert.IsFalse(exclusive.EndInclusive);
            Assert.IsTrue(inclusiveExclusive.StartInclusive);
            Assert.IsFalse(inclusiveExclusive.EndInclusive);
            Assert.IsFalse(exclusiveInclusive.StartInclusive);
            Assert.IsTrue(exclusiveInclusive.EndInclusive);
        }
    }
}
