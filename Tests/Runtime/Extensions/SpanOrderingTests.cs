// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.TestDoubles;

    [TestFixture]
    public sealed class SpanOrderingTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void BoundsMatchLinearOracleForTiesSlicesAndExtremes(bool descending)
        {
            int[] ascending = { int.MinValue, -3, -3, 0, 1, 1, 1, 4, int.MaxValue };
            int[] values = (int[])ascending.Clone();
            if (descending)
            {
                Array.Reverse(values);
            }
            IntComparer comparer = new(descending);
            for (int offset = 0; offset <= values.Length; ++offset)
            {
                for (int count = 0; count <= values.Length - offset; ++count)
                {
                    Span<int> slice = values.AsSpan(offset, count);
                    foreach (
                        int search in new[]
                        {
                            int.MinValue,
                            -4,
                            -3,
                            -1,
                            0,
                            1,
                            2,
                            4,
                            5,
                            int.MaxValue,
                        }
                    )
                    {
                        int lower = 0;
                        while (lower < count && comparer.Compare(slice[lower], search) < 0)
                        {
                            ++lower;
                        }
                        int upper = lower;
                        while (upper < count && comparer.Compare(slice[upper], search) == 0)
                        {
                            ++upper;
                        }
                        Assert.AreEqual(lower, slice.LowerBound(search, comparer));
                        Assert.AreEqual(upper, slice.UpperBound(search, comparer));
                        ReadOnlySpan<int> readOnly = slice;
                        Assert.AreEqual(lower, readOnly.LowerBound(search, comparer));
                        Assert.AreEqual(upper, readOnly.UpperBound(search, comparer));
                    }
                }
            }
            int[] expected = (int[])ascending.Clone();
            if (descending)
            {
                Array.Reverse(expected);
            }
            CollectionAssert.AreEqual(expected, values);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(8)]
        [TestCase(10001)]
        public void DescendingShuffleMatchesFrozenDrawOrder(int count)
        {
            int[] expected = new int[count];
            for (int index = 0; index < count; ++index)
            {
                expected[index] = index;
            }
            int[] actual = (int[])expected.Clone();
            IRandom oracle = new PcgRandom(123u);
            IRandom tested = new PcgRandom(123u);
            for (int index = count - 1; 0 < index; --index)
            {
                int chosen = oracle.Next(index + 1);
                (expected[index], expected[chosen]) = (expected[chosen], expected[index]);
            }
            actual.AsSpan().ShuffleDescending(tested);
            CollectionAssert.AreEqual(expected, actual);
            Assert.AreEqual(oracle.Next(), tested.Next());
        }

        [Test]
        public void DescendingShuffleUsesOnlyDescendingSingleBoundCalls()
        {
            List<int> bounds = new();
            EdgeCaseRandom random = new(boundedIntSampler: bound =>
            {
                bounds.Add(bound);
                return 0;
            });
            int[] values = { 0, 1, 2, 3 };
            values.AsSpan().ShuffleDescending(random);
            CollectionAssert.AreEqual(new[] { 4, 3, 2 }, bounds);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 0 }, values);
        }

        [Test]
        public void PairedShuffleMatchesExistingAscendingDrawsAndKeepsPairs()
        {
            int[] first = { 0, 1, 2, 3, 4, 5 };
            string[] second = { "0", "1", "2", "3", "4", "5" };
            int[] expected = (int[])first.Clone();
            IRandom oracle = new PcgRandom(456u);
            IRandom tested = new PcgRandom(456u);
            expected.AsSpan().Shuffle(oracle);
            Assert.IsTrue(first.AsSpan().Shuffle(second.AsSpan(), tested));
            CollectionAssert.AreEqual(expected, first);
            for (int index = 0; index < first.Length; ++index)
            {
                Assert.AreEqual(first[index].ToString(), second[index]);
            }
            Assert.AreEqual(oracle.Next(), tested.Next());
        }

        [Test]
        public void SameTypePairsKeepTheirAlignment()
        {
            int[] first = { 0, 1, 2, 3 };
            int[] second = { 10, 11, 12, 13 };
            IRandom oracle = new PcgRandom(987u);
            IRandom tested = new PcgRandom(987u);
            int[] expected = (int[])first.Clone();
            expected.AsSpan().Shuffle(oracle);
            Assert.IsTrue(first.AsSpan().Shuffle(second.AsSpan(), tested));
            CollectionAssert.AreEqual(expected, first);
            for (int index = 0; index < first.Length; ++index)
            {
                Assert.AreEqual(first[index] + 10, second[index]);
            }
            Assert.AreEqual(oracle.Next(), tested.Next());
        }

        [Test]
        public void MismatchedLengthsFailBeforeDrawingOrWriting()
        {
            EdgeCaseRandom random = new(maxDoubleCalls: 0, maxFloatCalls: 0);
            int[] values = { 0, 1, 2, 3 };
            Assert.IsFalse(values.AsSpan().Shuffle(new int[3].AsSpan(), random));
            Assert.IsFalse(values.AsSpan().Shuffle(new string[3].AsSpan(), random));
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, values);
        }

        [Test]
        public void EmptyAndSinglePairsDoNotDrawOrTouchOuterSlices()
        {
            EdgeCaseRandom random = new();
            Span<int>.Empty.ShuffleDescending(random);
            Assert.IsTrue(Span<int>.Empty.Shuffle(Span<string>.Empty, random));
            int[] values = { 99, 1, 98 };
            string[] pairs = { "before", "one", "after" };
            values.AsSpan(1, 1).ShuffleDescending(random);
            Assert.IsTrue(values.AsSpan(1, 1).Shuffle(pairs.AsSpan(1, 1), random));
            CollectionAssert.AreEqual(new[] { 99, 1, 98 }, values);
            CollectionAssert.AreEqual(new[] { "before", "one", "after" }, pairs);
        }

        private readonly struct IntComparer : IComparer<int>
        {
            private readonly bool _descending;

            internal IntComparer(bool descending)
            {
                _descending = descending;
            }

            public int Compare(int x, int y)
            {
                return _descending ? y.CompareTo(x) : x.CompareTo(y);
            }
        }
    }
}
