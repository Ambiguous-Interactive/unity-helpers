// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// A partitioning sort that fails to separate equal keys recurses once per element instead of
    /// once per halving. Sorting entities on a shared key -- a team, a layer, a priority -- is an
    /// ordinary workload, and the end of that recursion is a <c>StackOverflowException</c>, which no
    /// catch intercepts.
    /// </summary>
    /// <remarks>
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/645">#645</see>.
    /// <c>FluxSort</c> shipped without the depth limit its three siblings carry, and its dual-pivot
    /// partition removed exactly two elements per level when the pivots compared equal: 100,000
    /// equal ints overflowed at 47,572 frames, and 20,000 cost 199,999,687 comparisons.
    /// <c>IListSortCorrectnessTests</c> runs the same shape but stops at 1,000 elements, where the
    /// depth is 488 and it passes -- the gate existed, its corpus was one size short. Counting
    /// comparisons rather than measuring depth is deliberate: it fails with a number instead of
    /// killing the test runner.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IListSortDegenerateKeyTests : CommonTestBase
    {
        private const int Count = 20_000;

        /// <remarks>
        /// The worst of these measured 22 comparisons per element on the degenerate shapes, so the
        /// ceiling is roughly six times the slowest honest answer and seventy-eight times below the
        /// defect it exists to catch. Insertion sort is absent on purpose: it is quadratic by
        /// design and documented as such.
        /// </remarks>
        private const long ComparisonCeiling = 128L * Count;

        private static readonly SortAlgorithm[] PartitioningAlgorithms =
        {
            SortAlgorithm.Flux,
            SortAlgorithm.Ipn,
            SortAlgorithm.PatternDefeatingQuickSort,
            SortAlgorithm.Ska,
            SortAlgorithm.Ips4o,
        };

        [Test]
        public void EveryKeyEqualSortsInLinearithmicComparisons(
            [ValueSource(nameof(PartitioningAlgorithms))] SortAlgorithm algorithm
        )
        {
            AssertBounded(algorithm, new int[Count]);
        }

        [Test]
        public void NearlyEveryKeyEqualSortsInLinearithmicComparisons(
            [ValueSource(nameof(PartitioningAlgorithms))] SortAlgorithm algorithm
        )
        {
            int[] keys = new int[Count];
            for (int index = 0; index < Count; ++index)
            {
                keys[index] = index % 10 == 0 ? index : 0;
            }

            AssertBounded(algorithm, keys);
        }

        private static void AssertBounded(SortAlgorithm algorithm, int[] keys)
        {
            CountingIntComparer comparer = new();
            List<int> subject = new(keys);

            subject.Sort(comparer, algorithm);

            Assert.That(
                0 < comparer.Comparisons,
                Is.True,
                $"{algorithm} compared nothing, so the ceiling below measured nothing"
            );
            for (int index = 1; index < subject.Count; ++index)
            {
                Assert.That(
                    subject[index - 1] <= subject[index],
                    Is.True,
                    $"{algorithm} left element {index} out of order"
                );
            }

            Assert.That(
                comparer.Comparisons <= ComparisonCeiling,
                Is.True,
                $"{algorithm} used {comparer.Comparisons} comparisons for {Count} elements, over "
                    + $"the {ComparisonCeiling} ceiling -- the partition is not separating equal keys"
            );
        }

        private sealed class CountingIntComparer : IComparer<int>
        {
            public long Comparisons { get; private set; }

            public int Compare(int x, int y)
            {
                Comparisons++;
                return x.CompareTo(y);
            }
        }
    }
}
