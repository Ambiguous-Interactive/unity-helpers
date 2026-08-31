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
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IListExtensionTests : CommonTestBase
    {
        private const int NumTries = 1_000;

        public delegate IList<int> ContainerFactory(IEnumerable<int> source);

        /// <remarks>
        /// Shuffle, Shift, Reverse and Fill each take a different path per container: a bulk array
        /// primitive, a pooled copy written back in one Array.Copy, or the interface indexer. Every
        /// shape has to produce the same answer, so every behavior assertion below runs over all
        /// four rather than over whichever one the operation happens to be fastest on.
        /// </remarks>
        private static IEnumerable<TestCaseData> ContainerShapeCases()
        {
            yield return new TestCaseData(
                "T[]",
                (ContainerFactory)(source => source.ToArray())
            ).SetName("Array");
            yield return new TestCaseData(
                "List<T>",
                (ContainerFactory)(source => new List<int>(source))
            ).SetName("List");
            yield return new TestCaseData(
                "SerializableList<T>",
                (ContainerFactory)(
                    source =>
                    {
                        SerializableList<int> serializable = new();
                        serializable.AddRange(source);
                        return serializable;
                    }
                )
            ).SetName("SerializableList");
            yield return new TestCaseData(
                "IList<T>",
                (ContainerFactory)(
                    source =>
                    {
                        CustomList<int> custom = new();
                        foreach (int value in source)
                        {
                            custom.Add(value);
                        }

                        return custom;
                    }
                )
            ).SetName("CustomList");
        }

        public delegate void IntSortAlgorithm(IList<int> list, IComparer<int> comparer);

        public delegate void TupleSortAlgorithm(
            IList<ValueTuple<int, int>> list,
            IComparer<ValueTuple<int, int>> comparer
        );

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

        private static readonly int[] StabilityCounts = { 7, 64, 120, 513 };

        // A stable sort only reorders equal elements when it moves a block of them, and the shapes
        // that make it do so are descending: run detection that reverses a descending run must
        // refuse to take equal neighbors into it. An ascending shape alone proves nothing.
        private static readonly (string Name, Func<int, int, int> KeyOf)[] StabilityShapes =
        {
            ("ascending duplicates", static (i, _) => i / 3),
            ("descending duplicates", static (i, count) => (count - i) / 3),
            ("descending pairs", static (i, count) => (count - i) / 2),
            (
                "strict descent then equals",
                static (i, count) => (count - i) % 5 == 0 ? count - i : 0
            ),
            ("all equal", static (_, _) => 0),
            ("sawtooth duplicates", static (i, count) => (i % Math.Max(1, count / 4)) / 2),
        };

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
                    for (int i = runLength - 1; 0 <= i; --i)
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

        /// <remarks>
        /// The sorts run over an array and write the result back, and the write-back has a bulk path
        /// for the list types that offer one. A container that takes that path has to come out sorted,
        /// the same length, and holding the same elements.
        /// </remarks>
        [TestCaseSource(nameof(SortingAlgorithmCases))]
        public void SortingAlgorithmsWriteBackToEveryContainerShape(
            string algorithmName,
            IntSortAlgorithm algorithm
        )
        {
            int[] source = Enumerable.Range(0, 257).Select(i => (i * 37) % 101).ToArray();
            int[] expected = source.OrderBy(value => value).ToArray();

            int[] array = source.ToArray();
            algorithm(array, new IntComparer());
            Assert.That(array, Is.EqualTo(expected), $"{algorithmName} over T[]");

            List<int> list = new(source);
            algorithm(list, new IntComparer());
            Assert.That(list, Is.EqualTo(expected), $"{algorithmName} over List<T>");

            SerializableList<int> serializable = new();
            serializable.AddRange(source);
            algorithm(serializable, new IntComparer());
            Assert.That(serializable.Count, Is.EqualTo(expected.Length));
            Assert.That(
                serializable.ToArray(),
                Is.EqualTo(expected),
                $"{algorithmName} over SerializableList<T>"
            );
        }

        [TestCaseSource(nameof(StableSortingAlgorithmCases))]
        public void StableSortingAlgorithmsPreserveOrder(
            string algorithmName,
            TupleSortAlgorithm algorithm
        )
        {
            foreach ((string shapeName, Func<int, int, int> keyOf) in StabilityShapes)
            {
                foreach (int count in StabilityCounts)
                {
                    ValueTuple<int, int>[] actual = Enumerable
                        .Range(0, count)
                        .Select(i => ValueTuple.Create(keyOf(i, count), i))
                        .ToArray();

                    algorithm(actual, new StableTupleComparer());

                    for (int i = 1; i < actual.Length; ++i)
                    {
                        Assert.That(
                            actual[i - 1].Item1,
                            Is.LessThanOrEqualTo(actual[i].Item1),
                            $"{algorithmName} left {shapeName} (n={count}) unsorted at index {i}"
                        );
                        if (actual[i - 1].Item1 == actual[i].Item1)
                        {
                            Assert.That(
                                actual[i - 1].Item2,
                                Is.LessThan(actual[i].Item2),
                                $"{algorithmName} broke stability on {shapeName} (n={count}) at index {i}"
                            );
                        }
                    }
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

        /// <remarks>
        /// A range whose start is past its end is documented as a no-op. The bulk reverses take a
        /// length rather than two bounds, and both throw on a negative one, so this is the case the
        /// fast paths would break first.
        /// </remarks>
        [TestCaseSource(nameof(ContainerShapeCases))]
        public void ReverseWithStartAfterEndLeavesTheListAlone(
            string shapeName,
            ContainerFactory factory
        )
        {
            int[] source = Enumerable.Range(0, 10).ToArray();
            for (int start = 0; start < source.Length; ++start)
            {
                for (int end = 0; end < start; ++end)
                {
                    IList<int> list = factory(source);
                    list.Reverse(start, end);
                    Assert.That(
                        list.ToArray(),
                        Is.EqualTo(source),
                        $"Reverse({start}, {end}) modified a {shapeName}"
                    );
                }
            }
        }

        [TestCaseSource(nameof(ContainerShapeCases))]
        public void ReverseMatchesElementwiseSwapsInEveryContainer(
            string shapeName,
            ContainerFactory factory
        )
        {
            int[] source = Enumerable.Range(0, 33).ToArray();
            for (int start = 0; start < source.Length; ++start)
            {
                for (int end = start; end < source.Length; ++end)
                {
                    int[] expected = source.ToArray();
                    int left = start;
                    int right = end;
                    while (left < right)
                    {
                        (expected[left], expected[right]) = (expected[right], expected[left]);
                        left++;
                        right--;
                    }

                    IList<int> list = factory(source);
                    list.Reverse(start, end);
                    Assert.That(
                        list.ToArray(),
                        Is.EqualTo(expected),
                        $"Reverse({start}, {end}) over a {shapeName}"
                    );
                }
            }
        }

        /// <remarks>
        /// Shift no longer reverses anything on any path: an array rotates through three bulk
        /// reverses and everything else writes back two contiguous runs of a pooled copy. Both have
        /// to agree with the rotation the documentation describes, for every amount including the
        /// negative and out-of-range ones the modulo normalizes.
        /// </remarks>
        [TestCaseSource(nameof(ContainerShapeCases))]
        public void ShiftMatchesRotationInEveryContainer(string shapeName, ContainerFactory factory)
        {
            int[] source = Enumerable.Range(0, 17).ToArray();
            for (int amount = -2 * source.Length; amount <= 2 * source.Length; ++amount)
            {
                int normalized = amount.PositiveMod(source.Length);
                int[] expected = new int[source.Length];
                for (int i = 0; i < source.Length; ++i)
                {
                    expected[(i + normalized) % source.Length] = source[i];
                }

                IList<int> list = factory(source);
                list.Shift(amount);
                Assert.That(
                    list.ToArray(),
                    Is.EqualTo(expected),
                    $"Shift({amount}) over a {shapeName}"
                );
            }
        }

        /// <remarks>
        /// The array path shuffles in place and every other path shuffles a pooled copy, drawing the
        /// same number of times in the same order. A seeded generator therefore has to produce the
        /// identical permutation in all four, which is what pins the copy as a pure relocation.
        /// </remarks>
        [TestCaseSource(nameof(ContainerShapeCases))]
        public void ShuffleProducesTheSamePermutationInEveryContainer(
            string shapeName,
            ContainerFactory factory
        )
        {
            int[] source = Enumerable.Range(0, 64).ToArray();

            int[] expected = source.ToArray();
            expected.Shuffle(new SystemRandom(9_001));

            IList<int> list = factory(source);
            list.Shuffle(new SystemRandom(9_001));

            Assert.That(list.Count, Is.EqualTo(source.Length), $"Shuffle resized a {shapeName}");
            Assert.That(list.ToArray(), Is.EqualTo(expected), $"Shuffle over a {shapeName}");
        }

        /// <remarks>
        /// A covariant array satisfies <c>is T[]</c> without being a <c>Span&lt;T&gt;</c>: a
        /// <c>string[]</c> handed out as <c>IList&lt;object&gt;</c> passes <c>is object[]</c>, and
        /// <c>Span&lt;T&gt;</c>'s array constructor then throws ArrayTypeMismatchException. Measured
        /// against the shipped sources: without the exact-type guard on the array fast path this call
        /// throws, which is a break for any <c>Derived[]</c> a caller passes as <c>IList&lt;Base&gt;</c>.
        /// </remarks>
        [Test]
        public void ShuffleAcceptsACovariantArrayPresentedAsAList()
        {
            string[] backing = { "a", "b", "c", "d", "e", "f", "g", "h" };
            IList<object> covariant = backing;

            Assert.DoesNotThrow(() => covariant.Shuffle(new SystemRandom(9_001)));

            Assert.That(
                backing.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                Is.EqualTo(new[] { "a", "b", "c", "d", "e", "f", "g", "h" }),
                "A covariant array must still hold a permutation of what it started with"
            );
            Assert.That(
                backing,
                Is.Not.EqualTo(new[] { "a", "b", "c", "d", "e", "f", "g", "h" }),
                "The pooled fallback must actually shuffle rather than silently no-op"
            );
        }

        [TestCaseSource(nameof(ContainerShapeCases))]
        public void FillReplacesEveryElementInEveryContainer(
            string shapeName,
            ContainerFactory factory
        )
        {
            foreach (int count in new[] { 0, 1, 2, 33, 512 })
            {
                IList<int> list = factory(Enumerable.Range(0, count));
                list.Fill(-7);
                Assert.That(list.Count, Is.EqualTo(count), $"Fill resized a {shapeName}");
                Assert.That(
                    list.ToArray(),
                    Is.EqualTo(Enumerable.Repeat(-7, count).ToArray()),
                    $"Fill over a {shapeName} of {count}"
                );
            }
        }

        /// <remarks>
        /// A caller-supplied factory or predicate can shrink the list it is being run over, and the
        /// package never throws from a public API for it. So the loop bound of anything that calls
        /// back into caller code is re-read rather than hoisted - the array paths hoist, because an
        /// array cannot change length underneath one.
        /// </remarks>
        [Test]
        public void CallbacksThatShrinkTheListDoNotThrow()
        {
            // Every local is IList<int> deliberately: List<T> declares its own FindAll(Predicate<T>),
            // which wins overload resolution and would leave the extension untested.
            IList<int> filled = new List<int>(Enumerable.Range(0, 32));
            Assert.DoesNotThrow(() =>
                filled.Fill(index =>
                {
                    if (4 < filled.Count)
                    {
                        filled.RemoveAt(filled.Count - 1);
                    }

                    return index;
                })
            );

            IList<int> searched = new List<int>(Enumerable.Range(0, 32));
            Assert.DoesNotThrow(() =>
                searched.IndexOf(value =>
                {
                    if (4 < searched.Count)
                    {
                        searched.RemoveAt(searched.Count - 1);
                    }

                    return value < 0;
                })
            );

            IList<int> found = new List<int>(Enumerable.Range(0, 32));
            Assert.DoesNotThrow(() =>
                found.FindAll(value =>
                {
                    if (4 < found.Count)
                    {
                        found.RemoveAt(found.Count - 1);
                    }

                    return 0 <= value;
                })
            );

            IList<int> partitioned = new List<int>(Enumerable.Range(0, 32));
            Assert.DoesNotThrow(() =>
                partitioned.Partition(value =>
                {
                    if (4 < partitioned.Count)
                    {
                        partitioned.RemoveAt(partitioned.Count - 1);
                    }

                    return 0 <= value;
                })
            );
        }

        /// <remarks>
        /// The predicate scans take a direct-indexing path for an array. They must not copy, because
        /// a copy would read every element before a predicate that matches the first one ever runs -
        /// so the count of predicate invocations is the assertion, not the elapsed time.
        /// </remarks>
        [TestCaseSource(nameof(ContainerShapeCases))]
        public void PredicateSearchesStopAtTheFirstMatchInEveryContainer(
            string shapeName,
            ContainerFactory factory
        )
        {
            int[] source = Enumerable.Range(0, 128).ToArray();

            IList<int> forward = factory(source);
            int forwardCalls = 0;
            int firstIndex = forward.IndexOf(value =>
            {
                forwardCalls++;
                return value == 0;
            });
            Assert.That(firstIndex, Is.EqualTo(0), $"IndexOf over a {shapeName}");
            Assert.That(
                forwardCalls,
                Is.EqualTo(1),
                $"IndexOf scanned past its match on a {shapeName}"
            );

            IList<int> backward = factory(source);
            int backwardCalls = 0;
            int lastIndex = backward.LastIndexOf(value =>
            {
                backwardCalls++;
                return value == source.Length - 1;
            });
            Assert.That(
                lastIndex,
                Is.EqualTo(source.Length - 1),
                $"LastIndexOf over a {shapeName}"
            );
            Assert.That(
                backwardCalls,
                Is.EqualTo(1),
                $"LastIndexOf scanned past its match on a {shapeName}"
            );
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

        /// <summary>
        /// A short list used to take a size shortcut before it looked at the index, so
        /// <c>RemoveAtSwapBack(7)</c> on a one-element list emptied it -- a silent data loss where
        /// a longer list threw
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/645">#645</see>).
        /// Every rejected call leaves the list exactly as it found it.
        /// </summary>
        [Test]
        public void RemoveAtSwapBackRejectsAnInvalidIndexWithoutMutating()
        {
            int[] sizes = { 0, 1, 2, 5 };
            int[] indices = { -1, int.MinValue, int.MaxValue };
            foreach (int size in sizes)
            {
                foreach (int index in indices)
                {
                    AssertRemoveAtSwapBackRejects(size, index);
                }

                AssertRemoveAtSwapBackRejects(size, size);
                AssertRemoveAtSwapBackRejects(size, size + 1);
            }
        }

        private static void AssertRemoveAtSwapBackRejects(int size, int index)
        {
            List<int> list = new(size);
            for (int i = 0; i < size; ++i)
            {
                list.Add(i);
            }

            List<int> before = new(list);
            Assert.Throws<ArgumentOutOfRangeException>(
                () => list.RemoveAtSwapBack(index),
                "size {0}, index {1}",
                size,
                index
            );
            Assert.That(list, Is.EqualTo(before), "size {0}, index {1}", size, index);
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
            Assert.That(arr.IndexOf(x => 3 < x), Is.EqualTo(3));
            Assert.That(arr.IndexOf(x => x == 1), Is.EqualTo(0));
        }

        [Test]
        public void IndexOfNotFound()
        {
            int[] arr = { 1, 2, 3 };
            Assert.That(arr.IndexOf(x => 10 < x), Is.EqualTo(-1));
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
            Assert.That(arr.LastIndexOf(x => 10 < x), Is.EqualTo(-1));
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
            list.RemoveAll(x => 5 < x);
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
    }
}
