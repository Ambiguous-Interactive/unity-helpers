// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpanSortTests
    {
        private static IEnumerable<TestCaseData> SortCases()
        {
            for (int algorithm = 1; algorithm <= 18; ++algorithm)
            {
                foreach (int length in new[] { 0, 1, 2, 17, 129, 257 })
                {
                    for (int pattern = 0; pattern < 4; ++pattern)
                    {
                        yield return new TestCaseData((SortAlgorithm)algorithm, length, pattern);
                    }
                }
            }
        }

        private static IEnumerable<SortAlgorithm> Algorithms()
        {
            for (int algorithm = 1; algorithm <= 18; ++algorithm)
            {
                yield return (SortAlgorithm)algorithm;
            }
        }

        [TestCaseSource(nameof(SortCases))]
        public void SortMatchesListAndIndependentOracleOnSlices(
            SortAlgorithm algorithm,
            int length,
            int pattern
        )
        {
            int[] values = new int[length + 2];
            values[0] = int.MaxValue;
            values[length + 1] = int.MinValue;
            for (int index = 0; index < length; ++index)
            {
                values[index + 1] = pattern switch
                {
                    0 => index,
                    1 => length - index,
                    2 => (index * 17 % 13) - 6,
                    _ => index % 2 == 0 ? int.MinValue : int.MaxValue,
                };
            }
            int[] expected = values.AsSpan(1, length).ToArray();
            int[] list = (int[])expected.Clone();
            Array.Sort(expected);
            ((IList<int>)list).Sort(Comparer<int>.Default, algorithm);
            values.AsSpan(1, length).Sort(Comparer<int>.Default, algorithm);
            CollectionAssert.AreEqual(expected, values.AsSpan(1, length).ToArray());
            CollectionAssert.AreEqual(list, values.AsSpan(1, length).ToArray());
            Assert.That(values[0], Is.EqualTo(int.MaxValue));
            Assert.That(values[length + 1], Is.EqualTo(int.MinValue));
        }

        [TestCaseSource(nameof(Algorithms))]
        public void SortSupportsStackStorageAndDescendingComparer(SortAlgorithm algorithm)
        {
            Span<int> values = stackalloc int[] { -7, 5, 0, int.MinValue, 5, int.MaxValue };
            values.Sort(
                Comparer<int>.Create(static (left, right) => right.CompareTo(left)),
                algorithm
            );
            CollectionAssert.AreEqual(
                new[] { int.MaxValue, 5, 5, 0, -7, int.MinValue },
                values.ToArray()
            );
        }

        [TestCaseSource(nameof(Algorithms))]
        public void SortOfDefaultSpanDoesNotInvokeComparer(SortAlgorithm algorithm)
        {
            Span<int> values = default;
            values.Sort(
                Comparer<int>.Create(static (left, right) => throw new InvalidOperationException()),
                algorithm
            );
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(19)]
        [TestCase(int.MaxValue)]
        public void SortRejectsInvalidAlgorithmBeforeChangingStorage(int algorithm)
        {
            int[] values = { 4, 2, 3, 1 };
            Assert.Throws<InvalidEnumArgumentException>(() =>
                values.AsSpan().Sort(Comparer<int>.Default, (SortAlgorithm)algorithm)
            );
            CollectionAssert.AreEqual(new[] { 4, 2, 3, 1 }, values);
            Assert.Throws<InvalidEnumArgumentException>(() =>
                Span<int>.Empty.Sort(Comparer<int>.Default, (SortAlgorithm)algorithm)
            );
        }

        [TestCaseSource(nameof(Algorithms))]
        public void SortWithThrowingComparerLeavesCallerStorageIntact(SortAlgorithm algorithm)
        {
            int[] values = { 8, 6, 4, 2 };
            IComparer<int> comparer = Comparer<int>.Create(
                static (left, right) => throw new InvalidOperationException()
            );
            Assert.Throws<InvalidOperationException>(() =>
                values.AsSpan().Sort(comparer, algorithm)
            );
            CollectionAssert.AreEqual(new[] { 8, 6, 4, 2 }, values);
            values.AsSpan().Sort(Comparer<int>.Default, algorithm);
            CollectionAssert.AreEqual(new[] { 2, 4, 6, 8 }, values);
        }

        [TestCase(SortAlgorithm.Insertion)]
        [TestCase(SortAlgorithm.Grail)]
        [TestCase(SortAlgorithm.Power)]
        [TestCase(SortAlgorithm.Tim)]
        [TestCase(SortAlgorithm.Green)]
        [TestCase(SortAlgorithm.Block)]
        [TestCase(SortAlgorithm.PowerPlus)]
        [TestCase(SortAlgorithm.Glide)]
        [TestCase(SortAlgorithm.Yam)]
        public void StableAlgorithmsPreserveEqualKeyIdentity(SortAlgorithm algorithm)
        {
            Tuple<int, int>[] values = new Tuple<int, int>[257];
            for (int index = 0; index < values.Length; ++index)
            {
                values[index] = Tuple.Create(index % 7, index);
            }
            values
                .AsSpan()
                .Sort(
                    Comparer<Tuple<int, int>>.Create(
                        static (left, right) => left.Item1.CompareTo(right.Item1)
                    ),
                    algorithm
                );
            for (int index = 1; index < values.Length; ++index)
            {
                Assert.That(values[index - 1].Item1, Is.LessThanOrEqualTo(values[index].Item1));
                if (values[index - 1].Item1 == values[index].Item1)
                {
                    Assert.That(values[index - 1].Item2, Is.LessThan(values[index].Item2));
                }
            }
        }

        [TestCaseSource(nameof(Algorithms))]
        public void NamedSortMatchesDispatch(SortAlgorithm algorithm)
        {
            int[] values = { 9, 1, -3, 1, 7, 0 };
            Span<int> span = values.AsSpan();
            switch (algorithm)
            {
                case SortAlgorithm.Ghost:
                    span.GhostSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Insertion:
                    span.InsertionSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Meteor:
                    span.MeteorSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.PatternDefeatingQuickSort:
                    span.PatternDefeatingQuickSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Grail:
                    span.GrailSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Power:
                    span.PowerSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Tim:
                    span.TimSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Jesse:
                    span.JesseSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Green:
                    span.GreenSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Ska:
                    span.SkaSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Ipn:
                    span.IpnSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Smooth:
                    span.SmoothSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Block:
                    span.BlockMergeSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Ips4o:
                    span.Ips4oSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.PowerPlus:
                    span.PowerSortPlus(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Glide:
                    span.GlideSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Flux:
                    span.FluxSort(Comparer<int>.Default);
                    break;
                case SortAlgorithm.Yam:
                    span.YamSort(Comparer<int>.Default);
                    break;
            }
            CollectionAssert.AreEqual(new[] { -3, 0, 1, 1, 7, 9 }, values);
        }

        [TestCaseSource(nameof(Algorithms))]
        public void SortHandlesLargeDuplicateInput(SortAlgorithm algorithm)
        {
            int[] values = new int[10_001];
            for (int index = 0; index < values.Length; ++index)
            {
                values[index] = (index * 17 % 31) - 15;
            }
            int[] expected = (int[])values.Clone();
            Array.Sort(expected);
            values.AsSpan().Sort(Comparer<int>.Default, algorithm);
            CollectionAssert.AreEqual(expected, values);
        }

        [TestCaseSource(nameof(Algorithms))]
        public void SortHandlesNullReferenceElements(SortAlgorithm algorithm)
        {
            string[] values = { "b", null, "a", "", null, "b" };
            string[] expected = (string[])values.Clone();
            Array.Sort(expected, StringComparer.Ordinal);
            values.AsSpan().Sort(StringComparer.Ordinal, algorithm);
            CollectionAssert.AreEqual(expected, values);
        }
    }
}
