// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IListExtensionTests : CommonTestBase
    {
        private const int NumTries = 1_000;

        private readonly struct IntComparer : IComparer<int>
        {
            public int Compare(int x, int y) => x.CompareTo(y);
        }

        private sealed class CountingComparer : IComparer<int>
        {
            public int ComparisonCount { get; private set; }

            public int Compare(int x, int y)
            {
                ComparisonCount++;
                return x.CompareTo(y);
            }
        }

        private sealed class StableTupleComparer : IComparer<ValueTuple<int, int>>
        {
            public int Compare(ValueTuple<int, int> x, ValueTuple<int, int> y)
            {
                return x.Item1.CompareTo(y.Item1);
            }
        }

        private readonly struct IntEqualityComparer : IEqualityComparer<int>
        {
            public bool Equals(int x, int y) => x == y;

            public int GetHashCode(int obj) => obj.GetHashCode();
        }

        public delegate void IntSortAlgorithm(IList<int> list, IComparer<int> comparer);

        public delegate void TupleSortAlgorithm(
            IList<ValueTuple<int, int>> list,
            IComparer<ValueTuple<int, int>> comparer
        );

        private readonly struct SortDataset
        {
            private readonly Func<int[]> factory;

            public SortDataset(string label, Func<int[]> factory)
            {
                Label = label;
                this.factory = factory;
            }

            public string Label { get; }

            public int[] Create()
            {
                return factory();
            }
        }

        private static IEnumerable<TestCaseData> SortingAlgorithmCases
        {
            get
            {
                yield return new TestCaseData(
                    "InsertionSort",
                    (IntSortAlgorithm)((list, comparer) => list.InsertionSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortInsertionSort");
                yield return new TestCaseData(
                    "GhostSort",
                    (IntSortAlgorithm)((list, comparer) => list.GhostSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortGhostSort");
                yield return new TestCaseData(
                    "MeteorSort",
                    (IntSortAlgorithm)((list, comparer) => list.MeteorSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortMeteorSort");
                yield return new TestCaseData(
                    "PatternDefeatingQuickSort",
                    (IntSortAlgorithm)((list, comparer) => list.PatternDefeatingQuickSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortPdqSort");
                yield return new TestCaseData(
                    "GrailSort",
                    (IntSortAlgorithm)((list, comparer) => list.GrailSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortGrailSort");
                yield return new TestCaseData(
                    "PowerSort",
                    (IntSortAlgorithm)((list, comparer) => list.PowerSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortPowerSort");
                yield return new TestCaseData(
                    "TimSort",
                    (IntSortAlgorithm)((list, comparer) => list.TimSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortTimSort");
                yield return new TestCaseData(
                    "JesseSort",
                    (IntSortAlgorithm)((list, comparer) => list.JesseSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortJesseSort");
                yield return new TestCaseData(
                    "GreenSort",
                    (IntSortAlgorithm)((list, comparer) => list.GreenSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortGreenSort");
                yield return new TestCaseData(
                    "SkaSort",
                    (IntSortAlgorithm)((list, comparer) => list.SkaSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortSkaSort");
                yield return new TestCaseData(
                    "IpnSort",
                    (IntSortAlgorithm)((list, comparer) => list.IpnSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortIpnSort");
                yield return new TestCaseData(
                    "SmoothSort",
                    (IntSortAlgorithm)((list, comparer) => list.SmoothSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortSmoothSort");
                yield return new TestCaseData(
                    "BlockMergeSort",
                    (IntSortAlgorithm)((list, comparer) => list.BlockMergeSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortBlockMergeSort");
                yield return new TestCaseData(
                    "Ips4oSort",
                    (IntSortAlgorithm)((list, comparer) => list.Ips4oSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortIps4oSort");
                yield return new TestCaseData(
                    "PowerSortPlus",
                    (IntSortAlgorithm)((list, comparer) => list.PowerSortPlus(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortPowerSortPlus");
                yield return new TestCaseData(
                    "GlideSort",
                    (IntSortAlgorithm)((list, comparer) => list.GlideSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortGlideSort");
                yield return new TestCaseData(
                    "FluxSort",
                    (IntSortAlgorithm)((list, comparer) => list.FluxSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortFluxSort");
                yield return new TestCaseData(
                    "YamSort",
                    (IntSortAlgorithm)((list, comparer) => list.YamSort(comparer))
                ).SetName("SortingAlgorithmsMatchArraySortYamSort");
            }
        }

        private static IEnumerable<TestCaseData> StableSortingAlgorithmCases
        {
            get
            {
                yield return new TestCaseData(
                    "InsertionSort",
                    (TupleSortAlgorithm)((list, comparer) => list.InsertionSort(comparer))
                );
                yield return new TestCaseData(
                    "GrailSort",
                    (TupleSortAlgorithm)((list, comparer) => list.GrailSort(comparer))
                );
                yield return new TestCaseData(
                    "PowerSort",
                    (TupleSortAlgorithm)((list, comparer) => list.PowerSort(comparer))
                );
                yield return new TestCaseData(
                    "TimSort",
                    (TupleSortAlgorithm)((list, comparer) => list.TimSort(comparer))
                );
                yield return new TestCaseData(
                    "GreenSort",
                    (TupleSortAlgorithm)((list, comparer) => list.GreenSort(comparer))
                );
                yield return new TestCaseData(
                    "BlockMergeSort",
                    (TupleSortAlgorithm)((list, comparer) => list.BlockMergeSort(comparer))
                );
                yield return new TestCaseData(
                    "PowerSortPlus",
                    (TupleSortAlgorithm)((list, comparer) => list.PowerSortPlus(comparer))
                );
                yield return new TestCaseData(
                    "GlideSort",
                    (TupleSortAlgorithm)((list, comparer) => list.GlideSort(comparer))
                );
                yield return new TestCaseData(
                    "YamSort",
                    (TupleSortAlgorithm)((list, comparer) => list.YamSort(comparer))
                );
            }
        }

        private static IEnumerable<TestCaseData> SortAlgorithmEnumCases
        {
            get
            {
                foreach (SortAlgorithm algorithm in Enum.GetValues(typeof(SortAlgorithm)))
                {
#pragma warning disable CS0618 // Type or member is obsolete
                    if (algorithm == SortAlgorithm.None)
#pragma warning restore CS0618 // Type or member is obsolete
                    {
                        continue;
                    }

                    yield return new TestCaseData(algorithm).SetName(
                        $"SortAllAlgorithms{algorithm}"
                    );
                }
            }
        }

        private static IEnumerable<SortDataset> GetSortingDatasets()
        {
            yield return new SortDataset("Empty", () => Array.Empty<int>());
            yield return new SortDataset("Single", () => new[] { 42 });
            yield return new SortDataset("TwoElements", () => new[] { 5, -1 });
            yield return new SortDataset(
                "AlreadySorted",
                () => Enumerable.Range(-10, 21).ToArray()
            );
            yield return new SortDataset(
                "ReverseSorted",
                () => Enumerable.Range(0, 32).Reverse().ToArray()
            );
            yield return new SortDataset(
                "PrimeLength",
                () => Enumerable.Range(0, 31).Select(i => (i * 13 % 17) - 20).ToArray()
            );
            yield return new SortDataset(
                "SquareGrid225",
                () => BuildRandomDataset(225, seed: 1337)
            );
            yield return new SortDataset("Random64", () => BuildRandomDataset(64, seed: 42));
            yield return new SortDataset("Random257", () => BuildRandomDataset(257, seed: 99));
            yield return new SortDataset(
                "ExtremeValues",
                () => new[] { int.MaxValue, int.MinValue, 0, -1, 1, int.MaxValue }
            );
            yield return new SortDataset(
                "Duplicates",
                () => new[] { 5, 1, 5, 2, 2, 3, 3, 3, 4, 4, -1, -1 }
            );
        }

        private static int[] BuildRandomDataset(int length, int seed)
        {
            IRandom random = new PcgRandom(seed);
            int[] data = new int[length];
            for (int i = 0; i < length; ++i)
            {
                data[i] = random.Next(-50_000, 50_000);
            }
            return data;
        }

        [TestCaseSource(nameof(SortingAlgorithmCases))]
        public void SortingAlgorithmsMatchArraySort(
            string algorithmName,
            IntSortAlgorithm algorithm
        )
        {
            foreach (SortDataset dataset in GetSortingDatasets())
            {
                int[] source = dataset.Create();
                int[] expected = source.OrderBy(x => x).ToArray();
                int[] actual = source.ToArray();

                algorithm(actual, new IntComparer());

                Assert.That(
                    actual,
                    Is.EqualTo(expected),
                    $"{algorithmName} failed for dataset {dataset.Label}"
                );
            }
        }

        private static int[] BuildNearlySortedDataset(int length, int disturbanceStride)
        {
            int[] data = Enumerable.Range(0, length).ToArray();
            int stride = Math.Max(2, disturbanceStride);

            for (int i = 0; i + 1 < data.Length; i += stride)
            {
                (data[i], data[i + 1]) = (data[i + 1], data[i]);
            }

            return data;
        }

        private static int[] BuildAlternatingRunDataset(
            int length,
            int minRun,
            int maxRun,
            int seed
        )
        {
            IRandom random = new PcgRandom(seed);
            List<int> values = new List<int>(length);
            bool ascending = true;
            int current = 0;

            while (values.Count < length)
            {
                int runLength = Math.Min(length - values.Count, random.Next(minRun, maxRun + 1));
                if (ascending)
                {
                    for (int i = 0; i < runLength; ++i)
                    {
                        values.Add(current + i);
                    }
                }
                else
                {
                    for (int i = runLength - 1; i >= 0; --i)
                    {
                        values.Add(current + i);
                    }
                }

                current += runLength;
                ascending = !ascending;
            }

            return values.ToArray();
        }

        private static int MeasureSmoothSortComparisons(int[] source)
        {
            CountingComparer comparer = new CountingComparer();
            int[] actual = source.ToArray();
            int[] expected = actual.OrderBy(x => x).ToArray();

            actual.SmoothSort(comparer);
            Assert.That(actual, Is.EqualTo(expected));

            return comparer.ComparisonCount;
        }

        [Test]
        public void SmoothSortUsesFewerComparisonsOnNearlySortedData()
        {
            const int length = 4096;
            int[] nearlySorted = BuildNearlySortedDataset(length, disturbanceStride: 32);
            int[] randomDataset = BuildRandomDataset(length, seed: 1234);

            int nearlyComparisons = MeasureSmoothSortComparisons(nearlySorted);
            int randomComparisons = MeasureSmoothSortComparisons(randomDataset);

            TestContext.WriteLine(
                $"SmoothSort comparison counts — nearly sorted: {nearlyComparisons}, random: {randomComparisons}"
            );

            Assert.That(
                nearlyComparisons,
                Is.LessThan(randomComparisons * 0.85d),
                "SmoothSort should perform noticeably fewer comparisons on nearly sorted inputs."
            );
        }

        [Test]
        public void PowerSortPlusHandlesAlternatingRuns()
        {
            int[] dataset = BuildAlternatingRunDataset(2048, 4, 17, seed: 7);
            int[] expected = dataset.OrderBy(x => x).ToArray();

            int[] actual = dataset.ToArray();
            actual.PowerSortPlus(new IntComparer());
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void PowerSortPlusComparisonsStayCloseToPowerSortOnRunHeavyInputs()
        {
            int[] dataset = BuildAlternatingRunDataset(4096, 2, 9, seed: 11);
            int[] expected = dataset.OrderBy(x => x).ToArray();

            CountingComparer plusComparer = new CountingComparer();
            int[] plusInput = dataset.ToArray();
            plusInput.PowerSortPlus(plusComparer);
            Assert.That(plusInput, Is.EqualTo(expected));

            CountingComparer baseComparer = new CountingComparer();
            int[] baseInput = dataset.ToArray();
            baseInput.PowerSort(baseComparer);
            Assert.That(baseInput, Is.EqualTo(expected));

            TestContext.WriteLine(
                $"PowerSort+ comparisons: {plusComparer.ComparisonCount}, PowerSort comparisons: {baseComparer.ComparisonCount}"
            );

            Assert.That(
                plusComparer.ComparisonCount,
                Is.LessThanOrEqualTo((int)(baseComparer.ComparisonCount * 1.12d) + 1),
                $"PowerSort+ comparisons {plusComparer.ComparisonCount} vs PowerSort {baseComparer.ComparisonCount}"
            );
        }

        [Test]
        public void GlideSortHandlesZigZagRuns()
        {
            int[] dataset = BuildAlternatingRunDataset(3072, 3, 15, seed: 23);
            int[] expected = dataset.OrderBy(x => x).ToArray();

            int[] actual = dataset.ToArray();
            actual.GlideSort(new IntComparer());
            Assert.That(actual, Is.EqualTo(expected));
        }

        [Test]
        public void Ips4oSortHandlesHighlyDuplicateValues()
        {
            int[] input = Enumerable.Range(0, 4096).Select(i => (i / 8) % 11).ToArray();
            int[] expected = input.OrderBy(x => x).ToArray();

            int[] direct = input.ToArray();
            direct.Ips4oSort(new IntComparer());
            Assert.That(direct, Is.EqualTo(expected));

            int[] viaEnum = input.ToArray();
            viaEnum.Sort(new IntComparer(), SortAlgorithm.Ips4o);
            Assert.That(viaEnum, Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(StableSortingAlgorithmCases))]
        public void StableSortingAlgorithmsPreserveOrder(
            string algorithmName,
            TupleSortAlgorithm algorithm
        )
        {
            ValueTuple<int, int>[] input = Enumerable
                .Range(0, 120)
                .Select(i => ValueTuple.Create(i / 3, i))
                .ToArray();
            ValueTuple<int, int>[] actual = input.ToArray();

            algorithm(actual, new StableTupleComparer());

            for (int i = 1; i < actual.Length; ++i)
            {
                if (actual[i - 1].Item1 == actual[i].Item1)
                {
                    Assert.That(
                        actual[i - 1].Item2,
                        Is.LessThan(actual[i].Item2),
                        $"{algorithmName} broke stability at index {i}"
                    );
                }
            }
        }

        [Test]
        public void ShiftLeft()
        {
            int[] input = Enumerable.Range(0, 10).ToArray();
            for (int i = 0; i < input.Length * 2; ++i)
            {
                int[] shifted = input.ToArray();
                shifted.Shift(-1 * i);
                Assert.That(
                    input.Skip(i % input.Length).Concat(input.Take(i % input.Length)),
                    Is.EqualTo(shifted)
                );
            }
        }

        [Test]
        public void ShiftRight()
        {
            int[] input = Enumerable.Range(0, 10).ToArray();
            for (int i = 0; i < input.Length * 2; ++i)
            {
                int[] shifted = input.ToArray();
                shifted.Shift(i);
                Assert.That(
                    input
                        .Skip((input.Length * 3 - i) % input.Length)
                        .Concat(input.Take((input.Length * 3 - i) % input.Length)),
                    Is.EqualTo(shifted),
                    $"Shift failed for amount {i}."
                );
            }
        }

        [Test]
        public void Reverse()
        {
            int[] input = Enumerable.Range(0, 10).ToArray();
            for (int i = 0; i < input.Length; ++i)
            {
                int[] shifted = input.ToArray();
                shifted.Reverse(0, i);
                Assert.That(
                    input.Take(i + 1).Reverse().Concat(input.Skip(i + 1)),
                    Is.EqualTo(shifted),
                    $"Reverse failed for reversal from [0, {i}]."
                );
            }

            // Test various ranges
            for (int start = 0; start < input.Length; ++start)
            {
                for (int end = start; end < input.Length; ++end)
                {
                    int[] reversed = input.ToArray();
                    reversed.Reverse(start, end);

                    // Build expected result
                    int[] expected = input.ToArray();
                    int left = start;
                    int right = end;
                    while (left < right)
                    {
                        (expected[left], expected[right]) = (expected[right], expected[left]);
                        left++;
                        right--;
                    }

                    Assert.That(
                        expected,
                        Is.EqualTo(reversed),
                        $"Reverse failed for range [{start}, {end}]."
                    );
                }
            }
        }

        [Test]
        public void ReverseInvalidArguments()
        {
            int[] input = Enumerable.Range(0, 10).ToArray();
            Assert.Throws<ArgumentException>(() => input.Reverse(-1, 1));
            Assert.Throws<ArgumentException>(() => input.Reverse(input.Length, 1));
            Assert.Throws<ArgumentException>(() => input.Reverse(int.MaxValue, 1));
            Assert.Throws<ArgumentException>(() => input.Reverse(int.MinValue, 1));

            Assert.Throws<ArgumentException>(() => input.Reverse(1, -1));
            Assert.Throws<ArgumentException>(() => input.Reverse(1, input.Length));
            Assert.Throws<ArgumentException>(() => input.Reverse(1, int.MaxValue));
            Assert.Throws<ArgumentException>(() => input.Reverse(1, int.MinValue));
        }

        [Test]
        public void SortDefaultAlgorithm()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                int[] input = Enumerable
                    .Range(0, 100)
                    .Select(_ => PRNG.Instance.Next(int.MinValue, int.MaxValue))
                    .ToArray();
                int[] conventionalSorted = input.ToArray();
                Array.Sort(conventionalSorted);

                int[] insertionSorted = input.ToArray();
                insertionSorted.Sort(new IntComparer());
                Assert.That(conventionalSorted, Is.EqualTo(insertionSorted));
                Assert.That(input.OrderBy(x => x), Is.EqualTo(insertionSorted));
            }
        }

        [TestCaseSource(nameof(SortAlgorithmEnumCases))]
        public void SortAllAlgorithms(SortAlgorithm sortAlgorithm)
        {
            for (int i = 0; i < NumTries; ++i)
            {
                int[] input = Enumerable
                    .Range(0, 100)
                    .Select(_ => PRNG.Instance.Next(int.MinValue, int.MaxValue))
                    .ToArray();
                int[] conventionalSorted = input.ToArray();
                Array.Sort(conventionalSorted);

                int[] customSorted = input.ToArray();
                customSorted.Sort(new IntComparer(), sortAlgorithm);
                Assert.That(conventionalSorted, Is.EqualTo(customSorted));
                Assert.That(input.OrderBy(x => x), Is.EqualTo(customSorted));
            }
        }

        [Test]
        public void SortThrowsOnInvalidAlgorithm()
        {
            int[] input = { 2, 1 };
            Assert.Throws<InvalidEnumArgumentException>(() =>
                input.Sort(new IntComparer(), (SortAlgorithm)9999)
            );
        }

        // ===== New Method Tests =====

        [Test]
        public void ShuffleEmptyList()
        {
            int[] empty = Array.Empty<int>();
            empty.Shuffle();
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void ShuffleSingleElement()
        {
            int[] single = { 42 };
            single.Shuffle();
            Assert.That(single, Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void ShuffleActuallyShuffles()
        {
            int[] input = Enumerable.Range(0, 100).ToArray();
            int[] shuffled = input.ToArray();
            shuffled.Shuffle(new SystemRandom(42));

            // Should have same elements
            Assert.That(shuffled.OrderBy(x => x), Is.EqualTo(input));

            // Should be different order (very high probability)
            bool isDifferent = false;
            for (int i = 0; i < input.Length; ++i)
            {
                if (input[i] != shuffled[i])
                {
                    isDifferent = true;
                    break;
                }
            }
            Assert.That(isDifferent, Is.True, "Shuffle should change order");
        }

        [Test]
        public void ShuffleDifferentSeeds()
        {
            int[] input = Enumerable.Range(0, 50).ToArray();
            int[] shuffle1 = input.ToArray();
            int[] shuffle2 = input.ToArray();

            shuffle1.Shuffle(new SystemRandom(42));
            shuffle2.Shuffle(new SystemRandom(43));

            Assert.That(shuffle1, Is.Not.EqualTo(shuffle2));
        }

        [Test]
        public void ShiftEmptyList()
        {
            int[] empty = Array.Empty<int>();
            empty.Shift(5);
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void ShiftSingleElement()
        {
            int[] single = { 42 };
            single.Shift(10);
            Assert.That(single, Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void ShiftZero()
        {
            int[] input = Enumerable.Range(0, 10).ToArray();
            int[] expected = input.ToArray();
            input.Shift(0);
            Assert.That(input, Is.EqualTo(expected));
        }

        [Test]
        public void RemoveAtSwapBackSingleElement()
        {
            List<int> single = new() { 42 };
            single.RemoveAtSwapBack(0);
            Assert.That(single, Is.Empty);
        }

        [Test]
        public void RemoveAtSwapBackLastElement()
        {
            List<int> list = new() { 1, 2, 3, 4, 5 };
            list.RemoveAtSwapBack(4);
            Assert.That(list, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void RemoveAtSwapBackFirstElement()
        {
            List<int> list = new() { 1, 2, 3, 4, 5 };
            list.RemoveAtSwapBack(0);
            Assert.That(list, Is.EqualTo(new[] { 5, 2, 3, 4 }));
        }

        [Test]
        public void RemoveAtSwapBackMiddleElement()
        {
            List<int> list = new() { 1, 2, 3, 4, 5 };
            list.RemoveAtSwapBack(2);
            Assert.That(list, Is.EqualTo(new[] { 1, 2, 5, 4 }));
        }

        [Test]
        public void RemoveAtSwapBackInvalidIndexThrows()
        {
            List<int> list = new() { 1, 2, 3 };
            Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAtSwapBack(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAtSwapBack(10));
        }

        [Test]
        public void IsSortedEmptyList()
        {
            int[] empty = Array.Empty<int>();
            Assert.That(empty.IsSorted(), Is.True);
        }

        [Test]
        public void IsSortedSingleElement()
        {
            int[] single = { 42 };
            Assert.That(single.IsSorted(), Is.True);
        }

        [Test]
        public void IsSortedSorted()
        {
            int[] sorted = { 1, 2, 3, 4, 5 };
            Assert.That(sorted.IsSorted(), Is.True);
        }

        [Test]
        public void IsSortedNotSorted()
        {
            int[] notSorted = { 1, 3, 2, 4, 5 };
            Assert.That(notSorted.IsSorted(), Is.False);
        }

        [Test]
        public void IsSortedDuplicates()
        {
            int[] duplicates = { 1, 2, 2, 3, 3, 3, 4 };
            Assert.That(duplicates.IsSorted(), Is.True);
        }

        [Test]
        public void IsSortedCustomComparer()
        {
            int[] descending = { 5, 4, 3, 2, 1 };
            Assert.That(
                descending.IsSorted(Comparer<int>.Create((a, b) => b.CompareTo(a))),
                Is.True
            );
        }

        [Test]
        public void SwapValidIndices()
        {
            int[] arr = { 1, 2, 3, 4, 5 };
            arr.Swap(1, 3);
            Assert.That(arr, Is.EqualTo(new[] { 1, 4, 3, 2, 5 }));
        }

        [Test]
        public void SwapSameIndex()
        {
            int[] arr = { 1, 2, 3 };
            int[] expected = arr.ToArray();
            arr.Swap(1, 1);
            Assert.That(arr, Is.EqualTo(expected));
        }

        [Test]
        public void SwapInvalidIndices()
        {
            int[] arr = { 1, 2, 3 };
            Assert.Throws<ArgumentOutOfRangeException>(() => arr.Swap(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => arr.Swap(1, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => arr.Swap(3, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => arr.Swap(1, 3));
        }

        [Test]
        public void BinarySearchFound()
        {
            int[] sorted = { 1, 3, 5, 7, 9, 11, 13 };
            Assert.That(sorted.BinarySearch(7), Is.EqualTo(3));
            Assert.That(sorted.BinarySearch(1), Is.EqualTo(0));
            Assert.That(sorted.BinarySearch(13), Is.EqualTo(6));
        }

        [Test]
        public void BinarySearchNotFound()
        {
            int[] sorted = { 1, 3, 5, 7, 9 };
            int result = sorted.BinarySearch(4);
            Assert.That(result, Is.LessThan(0));
            Assert.That(~result, Is.EqualTo(2)); // Should insert at index 2
        }

        [Test]
        public void BinarySearchEmptyList()
        {
            int[] empty = Array.Empty<int>();
            int result = empty.BinarySearch(42);
            Assert.That(result, Is.LessThan(0));
            Assert.That(~result, Is.EqualTo(0));
        }

        [Test]
        public void BinarySearchSingleElement()
        {
            int[] single = { 42 };
            Assert.That(single.BinarySearch(42), Is.EqualTo(0));
            Assert.That(single.BinarySearch(41), Is.LessThan(0));
            Assert.That(single.BinarySearch(43), Is.LessThan(0));
        }

        [Test]
        public void FillValue()
        {
            int[] arr = new int[10];
            arr.Fill(42);
            Assert.That(arr, Is.All.EqualTo(42));
        }

        [Test]
        public void FillFactory()
        {
            int[] arr = new int[10];
            arr.Fill(i => i * 2);
            Assert.That(arr, Is.EqualTo(Enumerable.Range(0, 10).Select(i => i * 2)));
        }

        [Test]
        public void FillFactoryNull()
        {
            int[] arr = new int[10];
            Assert.Throws<ArgumentNullException>(() => arr.Fill(null));
        }

        [Test]
        public void IndexOfFound()
        {
            int[] arr = { 1, 2, 3, 4, 5 };
            Assert.That(arr.IndexOf(x => x > 3), Is.EqualTo(3));
            Assert.That(arr.IndexOf(x => x == 1), Is.EqualTo(0));
        }

        [Test]
        public void IndexOfNotFound()
        {
            int[] arr = { 1, 2, 3 };
            Assert.That(arr.IndexOf(x => x > 10), Is.EqualTo(-1));
        }

        [Test]
        public void IndexOfNullPredicate()
        {
            int[] arr = { 1, 2, 3 };
            Assert.Throws<ArgumentNullException>(() => arr.IndexOf(null));
        }

        [Test]
        public void LastIndexOfFound()
        {
            int[] arr = { 1, 2, 3, 2, 1 };
            Assert.That(arr.LastIndexOf(x => x == 2), Is.EqualTo(3));
            Assert.That(arr.LastIndexOf(x => x == 1), Is.EqualTo(4));
        }

        [Test]
        public void LastIndexOfNotFound()
        {
            int[] arr = { 1, 2, 3 };
            Assert.That(arr.LastIndexOf(x => x > 10), Is.EqualTo(-1));
        }

        [Test]
        public void FindAllFound()
        {
            int[] arr = { 1, 2, 3, 4, 5, 6 };
            List<int> result = arr.FindAll(x => x % 2 == 0);
            Assert.That(result, Is.EqualTo(new[] { 2, 4, 6 }));
        }

        [Test]
        public void FindAllNoneFound()
        {
            int[] arr = { 1, 3, 5 };
            List<int> result = arr.FindAll(x => x % 2 == 0);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void FindAllNullPredicate()
        {
            int[] arr = { 1, 2, 3 };
            Assert.Throws<ArgumentNullException>(() => arr.FindAll(null));
        }

        [Test]
        public void AddRangeToList()
        {
            List<int> list = new() { 1, 2, 3 };
            list.AddRange(new[] { 4, 5, 6 });
            Assert.That(list, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6 }));
        }

        [Test]
        public void AddRangeNullItems()
        {
            List<int> list = new() { 1, 2, 3 };
            Assert.Throws<ArgumentNullException>(() => list.AddRange(null));
        }

        [Test]
        public void RemoveAllSomeRemoved()
        {
            List<int> list = new() { 1, 2, 3, 4, 5, 6 };
            int removed = list.RemoveAll(x => x % 2 == 0);
            Assert.That(removed, Is.EqualTo(3));
            Assert.That(list, Is.EqualTo(new[] { 1, 3, 5 }));
        }

        [Test]
        public void RemoveAllNoneRemoved()
        {
            List<int> list = new() { 1, 3, 5 };
            int removed = list.RemoveAll(x => x % 2 == 0);
            Assert.That(removed, Is.EqualTo(0));
            Assert.That(list, Is.EqualTo(new[] { 1, 3, 5 }));
        }

        [Test]
        public void RemoveAllAllRemoved()
        {
            List<int> list = new() { 2, 4, 6 };
            int removed = list.RemoveAll(x => x % 2 == 0);
            Assert.That(removed, Is.EqualTo(3));
            Assert.That(list, Is.Empty);
        }

        [Test]
        public void RemoveAllNullPredicate()
        {
            List<int> list = new() { 1, 2, 3 };
            Assert.Throws<ArgumentNullException>(() => list.RemoveAll(null));
        }

        [Test]
        public void RotateLeftBasic()
        {
            int[] arr = { 1, 2, 3, 4, 5 };
            arr.RotateLeft(2);
            Assert.That(arr, Is.EqualTo(new[] { 3, 4, 5, 1, 2 }));
        }

        [Test]
        public void RotateRightBasic()
        {
            int[] arr = { 1, 2, 3, 4, 5 };
            arr.RotateRight(2);
            Assert.That(arr, Is.EqualTo(new[] { 4, 5, 1, 2, 3 }));
        }

        [Test]
        public void PartitionBasic()
        {
            int[] arr = { 1, 2, 3, 4, 5, 6 };
            (List<int> even, List<int> odd) = arr.Partition(x => x % 2 == 0);
            Assert.That(even, Is.EqualTo(new[] { 2, 4, 6 }));
            Assert.That(odd, Is.EqualTo(new[] { 1, 3, 5 }));
        }

        [Test]
        public void PartitionAllMatch()
        {
            int[] arr = { 2, 4, 6 };
            (List<int> matching, List<int> notMatching) = arr.Partition(x => x % 2 == 0);
            Assert.That(matching, Is.EqualTo(new[] { 2, 4, 6 }));
            Assert.That(notMatching, Is.Empty);
        }

        [Test]
        public void PartitionNoneMatch()
        {
            int[] arr = { 1, 3, 5 };
            (List<int> matching, List<int> notMatching) = arr.Partition(x => x % 2 == 0);
            Assert.That(matching, Is.Empty);
            Assert.That(notMatching, Is.EqualTo(new[] { 1, 3, 5 }));
        }

        [Test]
        public void PartitionNullPredicate()
        {
            int[] arr = { 1, 2, 3 };
            Assert.Throws<ArgumentNullException>(() => arr.Partition(null));
        }

        [Test]
        public void PopBackSuccess()
        {
            List<int> list = new() { 1, 2, 3, 4, 5 };
            int popped = list.PopBack();
            Assert.That(popped, Is.EqualTo(5));
            Assert.That(list, Is.EqualTo(new[] { 1, 2, 3, 4 }));
        }

        [Test]
        public void PopBackEmptyList()
        {
            List<int> list = new();
            Assert.Throws<InvalidOperationException>(() => list.PopBack());
        }

        [Test]
        public void PopFrontSuccess()
        {
            List<int> list = new() { 1, 2, 3, 4, 5 };
            int popped = list.PopFront();
            Assert.That(popped, Is.EqualTo(1));
            Assert.That(list, Is.EqualTo(new[] { 2, 3, 4, 5 }));
        }

        [Test]
        public void PopFrontEmptyList()
        {
            List<int> list = new();
            Assert.Throws<InvalidOperationException>(() => list.PopFront());
        }

        [Test]
        public void GetRandomElementSuccess()
        {
            int[] arr = { 1, 2, 3, 4, 5 };
            int element = arr.GetRandomElement(new SystemRandom(42));
            Assert.That(arr, Does.Contain(element));
        }

        [Test]
        public void GetRandomElementEmptyList()
        {
            int[] arr = Array.Empty<int>();
            Assert.Throws<InvalidOperationException>(() => arr.GetRandomElement());
        }

        [Test]
        public void GetRandomElementSingleElement()
        {
            int[] arr = { 42 };
            Assert.That(arr.GetRandomElement(), Is.EqualTo(42));
        }

        // ===== Edge Case Combination Tests =====

        [Test]
        public void SortEmptyList()
        {
            int[] empty = Array.Empty<int>();
            empty.Sort(new IntComparer());
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void SortSingleElement()
        {
            int[] single = { 42 };
            single.Sort(new IntComparer());
            Assert.That(single, Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void SortAllDuplicates()
        {
            int[] duplicates = { 5, 5, 5, 5, 5 };
            duplicates.Sort(new IntComparer());
            Assert.That(duplicates, Is.EqualTo(new[] { 5, 5, 5, 5, 5 }));
        }

        [Test]
        public void SortAlreadySorted()
        {
            int[] sorted = { 1, 2, 3, 4, 5 };
            sorted.Sort(new IntComparer());
            Assert.That(sorted, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        }

        [Test]
        public void SortReverseSorted()
        {
            int[] reversed = { 5, 4, 3, 2, 1 };
            reversed.Sort(new IntComparer());
            Assert.That(reversed, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        }

        [Test]
        public void InsertionSortEmptyList()
        {
            int[] empty = Array.Empty<int>();
            empty.InsertionSort(new IntComparer());
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void InsertionSortSingleElement()
        {
            int[] single = { 42 };
            single.InsertionSort(new IntComparer());
            Assert.That(single, Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void GhostSortEmptyList()
        {
            int[] empty = Array.Empty<int>();
            empty.GhostSort(new IntComparer());
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void GhostSortSingleElement()
        {
            int[] single = { 42 };
            single.GhostSort(new IntComparer());
            Assert.That(single, Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void PatternDefeatingQuickSortEmptyList()
        {
            int[] empty = Array.Empty<int>();
            empty.PatternDefeatingQuickSort(new IntComparer());
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void PatternDefeatingQuickSortSingleElement()
        {
            int[] single = { 42 };
            single.PatternDefeatingQuickSort(new IntComparer());
            Assert.That(single, Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void GrailSortEmptyList()
        {
            int[] empty = Array.Empty<int>();
            empty.GrailSort(new IntComparer());
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void GrailSortSingleElement()
        {
            int[] single = { 42 };
            single.GrailSort(new IntComparer());
            Assert.That(single, Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void PowerSortEmptyList()
        {
            int[] empty = Array.Empty<int>();
            empty.PowerSort(new IntComparer());
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void PowerSortSingleElement()
        {
            int[] single = { 42 };
            single.PowerSort(new IntComparer());
            Assert.That(single, Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void MeteorSortEmptyList()
        {
            int[] empty = Array.Empty<int>();
            empty.MeteorSort(new IntComparer());
            Assert.That(empty, Is.Empty);
        }

        [Test]
        public void MeteorSortSingleElement()
        {
            int[] single = { 42 };
            single.MeteorSort(new IntComparer());
            Assert.That(single, Is.EqualTo(new[] { 42 }));
        }

        [Test]
        public void CombinedOperationsShuffleThenSort()
        {
            int[] arr = Enumerable.Range(0, 100).ToArray();
            arr.Shuffle(new SystemRandom(42));
            arr.Sort(new IntComparer());
            Assert.That(arr.IsSorted(), Is.True);
            Assert.That(arr, Is.EqualTo(Enumerable.Range(0, 100)));
        }

        [Test]
        public void CombinedOperationsShiftThenReverse()
        {
            int[] arr = { 1, 2, 3, 4, 5 };
            arr.Shift(2);
            arr.Reverse(0, arr.Length - 1);
            Assert.That(arr, Is.EqualTo(new[] { 3, 2, 1, 5, 4 }));
        }

        [Test]
        public void CombinedOperationsFillThenPartition()
        {
            int[] arr = new int[10];
            arr.Fill(i => i);
            (List<int> even, List<int> odd) = arr.Partition(x => x % 2 == 0);
            Assert.That(even, Is.EqualTo(new[] { 0, 2, 4, 6, 8 }));
            Assert.That(odd, Is.EqualTo(new[] { 1, 3, 5, 7, 9 }));
        }

        [Test]
        public void CombinedOperationsRemoveAllThenIsSorted()
        {
            List<int> list = new() { 5, 2, 8, 1, 9, 3, 7, 4, 6 };
            list.RemoveAll(x => x > 5);
            list.Sort(new IntComparer());
            Assert.That(list.IsSorted(), Is.True);
            Assert.That(list, Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
        }

        [Test]
        public void StressTestMultipleOperations()
        {
            for (int i = 0; i < 100; ++i)
            {
                List<int> list = Enumerable.Range(0, 50).ToList();

                list.Shuffle(new SystemRandom(i));

                list.RemoveAll(x => x % 7 == 0);

                list.RotateLeft(3);

                list.Sort(new IntComparer());

                Assert.That(list.IsSorted(), Is.True);

                HashSet<int> seen = new();
                foreach (int val in list)
                {
                    Assert.That(seen.Add(val), Is.True, "No duplicates should exist");
                    Assert.That(val % 7, Is.Not.EqualTo(0), "Multiples of 7 should be removed");
                }
            }
        }

        [Test]
        public void SortByNameEmptyList()
        {
            List<GameObject> list = new();
            list.SortByName();
            Assert.That(list, Is.Empty);
        }

        [Test]
        public void SortByNameSingleElement()
        {
            GameObject obj = Track(new GameObject("SingleObject"));
            List<GameObject> list = new() { obj };
            list.SortByName();
            Assert.That(list, Has.Count.EqualTo(1));
            Assert.That(list[0].name, Is.EqualTo("SingleObject"));
        }

        [Test]
        public void SortByNameArray()
        {
            GameObject obj1 = Track(new GameObject("Zebra"));
            GameObject obj2 = Track(new GameObject("Alpha"));
            GameObject obj3 = Track(new GameObject("Bravo"));

            GameObject[] array = { obj1, obj2, obj3 };
            array.SortByName();
            Assert.That(array[0].name, Is.EqualTo("Alpha"));
            Assert.That(array[1].name, Is.EqualTo("Bravo"));
            Assert.That(array[2].name, Is.EqualTo("Zebra"));
        }

        [Test]
        public void SortByNameList()
        {
            GameObject obj1 = Track(new GameObject("Zebra"));
            GameObject obj2 = Track(new GameObject("Alpha"));
            GameObject obj3 = Track(new GameObject("Bravo"));
            GameObject obj4 = Track(new GameObject("Charlie"));

            List<GameObject> list = new() { obj1, obj2, obj3, obj4 };
            list.SortByName();
            Assert.That(list[0].name, Is.EqualTo("Alpha"));
            Assert.That(list[1].name, Is.EqualTo("Bravo"));
            Assert.That(list[2].name, Is.EqualTo("Charlie"));
            Assert.That(list[3].name, Is.EqualTo("Zebra"));
        }

        [Test]
        public void SortByNameCustomIList()
        {
            GameObject obj1 = Track(new GameObject("Zebra"));
            GameObject obj2 = Track(new GameObject("Alpha"));
            GameObject obj3 = Track(new GameObject("Bravo"));

            IList<GameObject> list = new CustomList<GameObject> { obj1, obj2, obj3 };
            list.SortByName();
            Assert.That(list[0].name, Is.EqualTo("Alpha"));
            Assert.That(list[1].name, Is.EqualTo("Bravo"));
            Assert.That(list[2].name, Is.EqualTo("Zebra"));
        }

        [Test]
        public void SortByNameDuplicateNames()
        {
            GameObject obj1 = Track(new GameObject("Same"));
            GameObject obj2 = Track(new GameObject("Same"));
            GameObject obj3 = Track(new GameObject("Alpha"));

            List<GameObject> list = new() { obj1, obj2, obj3 };
            list.SortByName();
            Assert.That(list[0].name, Is.EqualTo("Alpha"));
            Assert.That(list[1].name, Is.EqualTo("Same"));
            Assert.That(list[2].name, Is.EqualTo("Same"));
        }

        private sealed class CustomList<T> : IList<T>
        {
            private readonly List<T> _inner = new();

            public T this[int index]
            {
                get => _inner[index];
                set => _inner[index] = value;
            }

            public int Count => _inner.Count;
            public bool IsReadOnly => false;

            public void Add(T item) => _inner.Add(item);

            public void Clear() => _inner.Clear();

            public bool Contains(T item) => _inner.Contains(item);

            public void CopyTo(T[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

            public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

            public int IndexOf(T item) => _inner.IndexOf(item);

            public void Insert(int index, T item) => _inner.Insert(index, item);

            public bool Remove(T item) => _inner.Remove(item);

            public void RemoveAt(int index) => _inner.RemoveAt(index);

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
                _inner.GetEnumerator();
        }
    }
}
