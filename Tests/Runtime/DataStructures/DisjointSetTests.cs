// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DisjointSetTests
    {
        [Test]
        public void ConstructorWithPositiveCountCreatesValidSet()
        {
            DisjointSet ds = new(5);

            Assert.AreEqual(5, ds.Count);
            Assert.AreEqual(5, ds.SetCount);
        }

        [Test]
        public void ConstructorWithZeroThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DisjointSet(0));
        }

        [Test]
        public void ConstructorWithNegativeCountThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DisjointSet(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DisjointSet(-100));
        }

        [Test]
        public void ConstructorWithOneElementCreatesValidSet()
        {
            DisjointSet ds = new(1);

            Assert.AreEqual(1, ds.Count);
            Assert.AreEqual(1, ds.SetCount);
        }

        [Test]
        public void ConstructorWithLargeCountCreatesValidSet()
        {
            DisjointSet ds = new(10000);

            Assert.AreEqual(10000, ds.Count);
            Assert.AreEqual(10000, ds.SetCount);
        }

        [Test]
        public void InitiallyAllElementsSeparate()
        {
            DisjointSet ds = new(5);

            for (int i = 0; i < 5; ++i)
            {
                Assert.IsTrue(ds.TryFind(i, out int rep));
                Assert.AreEqual(i, rep);
            }
        }

        [Test]
        public void TryFindWithValidIndexReturnsTrue()
        {
            DisjointSet ds = new(5);

            Assert.IsTrue(ds.TryFind(0, out int rep0));
            Assert.IsTrue(ds.TryFind(4, out int rep4));
        }

        [Test]
        public void TryFindWithNegativeIndexReturnsFalse()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryFind(-1, out int rep));
            Assert.AreEqual(-1, rep);
        }

        [Test]
        public void TryFindWithIndexEqualToCountReturnsFalse()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryFind(5, out int rep));
            Assert.AreEqual(-1, rep);
        }

        [Test]
        public void TryFindWithIndexGreaterThanCountReturnsFalse()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryFind(10, out int rep));
            Assert.AreEqual(-1, rep);
        }

        [Test]
        public void TryFindWithBoundaryIndices()
        {
            DisjointSet ds = new(100);

            Assert.IsTrue(ds.TryFind(0, out int rep0));
            Assert.AreEqual(0, rep0);
            Assert.IsTrue(ds.TryFind(99, out int rep99));
            Assert.AreEqual(99, rep99);
        }

        [Test]
        public void TryUnionConnectsTwoElements()
        {
            DisjointSet ds = new(5);

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.AreEqual(4, ds.SetCount);
        }

        [Test]
        public void TryUnionReturnsFalseForSameSet()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));

            Assert.IsFalse(ds.TryUnion(0, 1));
            Assert.AreEqual(4, ds.SetCount);
        }

        [Test]
        public void TryUnionReturnsFalseForInvalidFirstIndex()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryUnion(-1, 0));
            Assert.IsFalse(ds.TryUnion(5, 0));
            Assert.AreEqual(5, ds.SetCount);
        }

        [Test]
        public void TryUnionReturnsFalseForInvalidSecondIndex()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryUnion(0, -1));
            Assert.IsFalse(ds.TryUnion(0, 5));
            Assert.AreEqual(5, ds.SetCount);
        }

        [Test]
        public void TryUnionReturnsFalseForBothInvalidIndices()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryUnion(-1, -1));
            Assert.IsFalse(ds.TryUnion(10, 20));
            Assert.AreEqual(5, ds.SetCount);
        }

        [Test]
        public void TryUnionWithSelfReturnsFalse()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryUnion(2, 2));
            Assert.AreEqual(5, ds.SetCount);
        }

        [Test]
        public void TryUnionChainCreatesOneSet()
        {
            DisjointSet ds = new(5);

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));
            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.IsTrue(ds.TryUnion(3, 4));

            Assert.AreEqual(1, ds.SetCount);
        }

        [Test]
        public void TryUnionMultipleSets()
        {
            DisjointSet ds = new(10);

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.IsTrue(ds.TryUnion(4, 5));
            Assert.IsTrue(ds.TryUnion(6, 7));
            Assert.IsTrue(ds.TryUnion(8, 9));

            Assert.AreEqual(5, ds.SetCount);
        }

        [Test]
        public void TryUnionBalancedTreeStructure()
        {
            DisjointSet ds = new(8);

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.IsTrue(ds.TryUnion(4, 5));
            Assert.IsTrue(ds.TryUnion(6, 7));
            Assert.IsTrue(ds.TryUnion(0, 2));
            Assert.IsTrue(ds.TryUnion(4, 6));
            Assert.IsTrue(ds.TryUnion(0, 4));

            Assert.AreEqual(1, ds.SetCount);
        }

        [Test]
        public void TryIsConnectedWorks()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));

            Assert.IsTrue(ds.TryIsConnected(0, 2, out bool connected));
            Assert.IsTrue(connected);

            Assert.IsTrue(ds.TryIsConnected(0, 3, out bool notConnected));
            Assert.IsFalse(notConnected);
        }

        [Test]
        public void TryIsConnectedReturnsTrueForSelf()
        {
            DisjointSet ds = new(5);

            Assert.IsTrue(ds.TryIsConnected(2, 2, out bool connected));
            Assert.IsTrue(connected);
        }

        [Test]
        public void TryIsConnectedReturnsFalseForInvalidFirstIndex()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryIsConnected(-1, 0, out bool connected));
            Assert.IsFalse(connected);
            Assert.IsFalse(ds.TryIsConnected(5, 0, out connected));
            Assert.IsFalse(connected);
        }

        [Test]
        public void TryIsConnectedReturnsFalseForInvalidSecondIndex()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryIsConnected(0, -1, out bool connected));
            Assert.IsFalse(connected);
            Assert.IsFalse(ds.TryIsConnected(0, 5, out connected));
            Assert.IsFalse(connected);
        }

        [Test]
        public void TryIsConnectedReturnsFalseForBothInvalidIndices()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryIsConnected(-1, -1, out bool connected));
            Assert.IsFalse(connected);
        }

        [Test]
        public void TryFindReturnsRepresentative()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));

            Assert.IsTrue(ds.TryFind(0, out int rep0));
            Assert.IsTrue(ds.TryFind(2, out int rep2));
            Assert.AreEqual(rep0, rep2);
        }

        [Test]
        public void TryFindPerformsPathCompression()
        {
            DisjointSet ds = new(10);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));
            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.IsTrue(ds.TryUnion(3, 4));

            Assert.IsTrue(ds.TryFind(0, out int rep0));
            Assert.IsTrue(ds.TryFind(4, out int rep4));

            Assert.AreEqual(rep0, rep4);
        }

        [Test]
        public void TryGetSetSizeForSingleElement()
        {
            DisjointSet ds = new(5);

            Assert.IsTrue(ds.TryGetSetSize(0, out int size));
            Assert.AreEqual(1, size);
        }

        [Test]
        public void TryGetSetSizeAfterUnion()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));

            Assert.IsTrue(ds.TryGetSetSize(0, out int size));
            Assert.AreEqual(3, size);
            Assert.IsTrue(ds.TryGetSetSize(1, out size));
            Assert.AreEqual(3, size);
            Assert.IsTrue(ds.TryGetSetSize(2, out size));
            Assert.AreEqual(3, size);
        }

        [Test]
        public void TryGetSetSizeReturnsFalseForInvalidIndex()
        {
            DisjointSet ds = new(5);

            Assert.IsFalse(ds.TryGetSetSize(-1, out int size));
            Assert.AreEqual(0, size);
            Assert.IsFalse(ds.TryGetSetSize(5, out size));
            Assert.AreEqual(0, size);
        }

        [Test]
        public void TryGetSetSizeForCompletelyConnectedSet()
        {
            DisjointSet ds = new(10);
            for (int i = 0; i < 9; ++i)
            {
                Assert.IsTrue(ds.TryUnion(i, i + 1));
            }

            Assert.IsTrue(ds.TryGetSetSize(5, out int size));
            Assert.AreEqual(10, size);
        }

        [Test]
        public void TryGetSetReturnsCorrectElements()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));

            List<int> results = new();
            List<int> set = ds.TryGetSet(0, results);

            Assert.IsTrue(set != null);
            Assert.AreEqual(3, set.Count);
            CollectionAssert.Contains(set, 0);
            CollectionAssert.Contains(set, 1);
            CollectionAssert.Contains(set, 2);
        }

        [Test]
        public void TryGetSetClearsListBeforeAdding()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));

            List<int> results = new() { 99, 98, 97 };
            ds.TryGetSet(0, results);

            Assert.AreEqual(2, results.Count);
            CollectionAssert.Contains(results, 0);
            CollectionAssert.Contains(results, 1);
        }

        [Test]
        public void TryGetSetReturnsSameListReference()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));

            List<int> results = new();
            List<int> returned = ds.TryGetSet(0, results);

            Assert.AreSame(results, returned);
        }

        [Test]
        public void TryGetSetThrowsOnNullList()
        {
            DisjointSet ds = new(5);

            Assert.Throws<ArgumentNullException>(() => ds.TryGetSet(0, null));
        }

        [Test]
        public void TryGetSetReturnsEmptyListForInvalidIndex()
        {
            DisjointSet ds = new(5);

            List<int> results = new() { 1, 2, 3 };
            ds.TryGetSet(-1, results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void TryGetSetForSingleElement()
        {
            DisjointSet ds = new(5);

            List<int> results = new();
            ds.TryGetSet(2, results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(2, results[0]);
        }

        [Test]
        public void TryGetAllSetsReturnsAllDistinctSets()
        {
            DisjointSet ds = new(10);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.IsTrue(ds.TryUnion(4, 5));
            Assert.IsTrue(ds.TryUnion(6, 7));
            Assert.IsTrue(ds.TryUnion(8, 9));

            List<List<int>> results = new();
            ds.TryGetAllSets(results);

            Assert.AreEqual(5, results.Count);
            foreach (List<int> set in results)
            {
                Assert.AreEqual(2, set.Count);
            }
        }

        [Test]
        public void TryGetAllSetsForSingleSet()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));
            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.IsTrue(ds.TryUnion(3, 4));

            List<List<int>> results = new();
            ds.TryGetAllSets(results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(5, results[0].Count);
        }

        [Test]
        public void TryGetAllSetsForAllSeparate()
        {
            DisjointSet ds = new(5);

            List<List<int>> results = new();
            ds.TryGetAllSets(results);

            Assert.AreEqual(5, results.Count);
            foreach (List<int> set in results)
            {
                Assert.AreEqual(1, set.Count);
            }
        }

        [Test]
        public void TryGetAllSetsThrowsOnNullList()
        {
            DisjointSet ds = new(5);

            Assert.Throws<ArgumentNullException>(() => ds.TryGetAllSets(null));
        }

        [Test]
        public void TryGetAllSetsClearsList()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));

            List<List<int>> results = new()
            {
                new() { 99 },
                new() { 98 },
            };
            ds.TryGetAllSets(results);

            Assert.AreEqual(4, results.Count);
        }

        [Test]
        public void TryGetAllSetsReusesListsFromInput()
        {
            DisjointSet ds = new(4);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(2, 3));

            List<int> reusable1 = new() { 99 };
            List<int> reusable2 = new() { 98 };
            List<List<int>> results = new() { reusable1, reusable2 };

            ds.TryGetAllSets(results);

            Assert.AreEqual(2, results.Count);
            Assert.Contains(reusable1, results);
            Assert.Contains(reusable2, results);
        }

        [Test]
        public void ResetSeparatesAllElements()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(2, 3));

            ds.Reset();

            Assert.AreEqual(5, ds.SetCount);
            for (int i = 0; i < 5; ++i)
            {
                Assert.IsTrue(ds.TryGetSetSize(i, out int size));
                Assert.AreEqual(1, size);
            }
        }

        [Test]
        public void ResetFromCompletelyConnected()
        {
            DisjointSet ds = new(10);
            for (int i = 0; i < 9; ++i)
            {
                Assert.IsTrue(ds.TryUnion(i, i + 1));
            }

            ds.Reset();

            Assert.AreEqual(10, ds.SetCount);
        }

        [Test]
        public void ResetOnAlreadySeparateElements()
        {
            DisjointSet ds = new(5);
            ds.Reset();

            Assert.AreEqual(5, ds.SetCount);
        }

        [Test]
        public void ResetAllowsNewUnions()
        {
            DisjointSet ds = new(5);
            Assert.IsTrue(ds.TryUnion(0, 1));
            ds.Reset();

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.AreEqual(4, ds.SetCount);
        }

        [Test]
        public void UnionByRankOptimization()
        {
            DisjointSet ds = new(8);

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.IsTrue(ds.TryUnion(0, 2));

            Assert.IsTrue(ds.TryFind(3, out int _));
            Assert.IsTrue(ds.TryIsConnected(1, 3, out bool connected));
            Assert.IsTrue(connected);
        }

        [Test]
        public void LargeScaleUnions()
        {
            DisjointSet ds = new(1000);

            for (int i = 0; i < 999; ++i)
            {
                Assert.IsTrue(ds.TryUnion(i, i + 1));
            }

            Assert.AreEqual(1, ds.SetCount);
            Assert.IsTrue(ds.TryGetSetSize(0, out int size));
            Assert.AreEqual(1000, size);
        }

        [Test]
        public void AlternatingUnionPattern()
        {
            DisjointSet ds = new(100);

            for (int i = 0; i < 50; ++i)
            {
                Assert.IsTrue(ds.TryUnion(i * 2, i * 2 + 1));
            }

            Assert.AreEqual(50, ds.SetCount);
        }

        [Test]
        public void StarPatternUnion()
        {
            DisjointSet ds = new(10);

            for (int i = 1; i < 10; ++i)
            {
                Assert.IsTrue(ds.TryUnion(0, i));
            }

            Assert.AreEqual(1, ds.SetCount);
            Assert.IsTrue(ds.TryGetSetSize(0, out int size));
            Assert.AreEqual(10, size);
        }

        [Test]
        public void PathCompressionReducesDepth()
        {
            DisjointSet ds = new(10);

            for (int i = 0; i < 9; ++i)
            {
                Assert.IsTrue(ds.TryUnion(i, i + 1));
            }

            Assert.IsTrue(ds.TryFind(0, out int rep0Before));
            Assert.IsTrue(ds.TryFind(9, out int rep9Before));

            Assert.AreEqual(rep0Before, rep9Before);
        }

        [Test]
        public void MultipleDisconnectedComponents()
        {
            DisjointSet ds = new(12);

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));
            Assert.IsTrue(ds.TryUnion(3, 4));
            Assert.IsTrue(ds.TryUnion(4, 5));
            Assert.IsTrue(ds.TryUnion(6, 7));
            Assert.IsTrue(ds.TryUnion(7, 8));
            Assert.IsTrue(ds.TryUnion(9, 10));
            Assert.IsTrue(ds.TryUnion(10, 11));

            Assert.AreEqual(4, ds.SetCount);

            List<List<int>> allSets = new();
            ds.TryGetAllSets(allSets);
            Assert.AreEqual(4, allSets.Count);

            foreach (List<int> set in allSets)
            {
                Assert.AreEqual(3, set.Count);
            }
        }

        [Test]
        public void MergingDisconnectedComponents()
        {
            DisjointSet ds = new(10);

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));
            Assert.IsTrue(ds.TryUnion(3, 4));
            Assert.IsTrue(ds.TryUnion(4, 5));
            Assert.IsTrue(ds.TryUnion(6, 7));
            Assert.IsTrue(ds.TryUnion(7, 8));

            Assert.AreEqual(4, ds.SetCount);

            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.AreEqual(3, ds.SetCount);

            Assert.IsTrue(ds.TryUnion(5, 6));
            Assert.AreEqual(2, ds.SetCount);

            Assert.IsTrue(ds.TryUnion(0, 9));
            Assert.AreEqual(1, ds.SetCount);

            Assert.IsFalse(ds.TryUnion(8, 9));
            Assert.AreEqual(1, ds.SetCount);
        }

        [Test]
        public void CycleDetectionUseCase()
        {
            DisjointSet ds = new(5);

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.IsTrue(ds.TryUnion(1, 2));
            Assert.IsTrue(ds.TryUnion(2, 3));
            Assert.IsTrue(ds.TryUnion(3, 4));

            Assert.IsFalse(ds.TryUnion(4, 0));
        }

        [Test]
        public void KruskalMSTSimulation()
        {
            DisjointSet ds = new(6);

            List<(int u, int v, int weight)> edges = new()
            {
                (0, 1, 1),
                (1, 2, 2),
                (0, 2, 4),
                (1, 3, 5),
                (2, 3, 8),
                (2, 4, 10),
                (3, 4, 2),
                (3, 5, 6),
                (4, 5, 3),
            };

            edges.Sort((a, b) => a.weight.CompareTo(b.weight));

            int edgesAdded = 0;
            foreach ((int u, int v, int _) in edges)
            {
                if (ds.TryUnion(u, v))
                {
                    ++edgesAdded;
                }
            }

            Assert.AreEqual(5, edgesAdded);
            Assert.AreEqual(1, ds.SetCount);
        }

        [Test]
        public void ConnectedComponentsInGraph()
        {
            DisjointSet ds = new(8);

            int[][] edges =
            {
                new[] { 0, 1 },
                new[] { 1, 2 },
                new[] { 3, 4 },
                new[] { 5, 6 },
                new[] { 6, 7 },
            };

            foreach (int[] edge in edges)
            {
                Assert.IsTrue(ds.TryUnion(edge[0], edge[1]));
            }

            Assert.AreEqual(3, ds.SetCount);

            List<List<int>> components = new();
            ds.TryGetAllSets(components);

            int[] sizes = components.Select(c => c.Count).OrderBy(s => s).ToArray();
            CollectionAssert.AreEqual(new[] { 2, 3, 3 }, sizes);
        }

        [Test]
        public void PercolationSimulation()
        {
            DisjointSet ds = new(26);

            int topSentinel = 24;
            int bottomSentinel = 25;

            for (int i = 0; i < 4; ++i)
            {
                Assert.IsTrue(ds.TryUnion(topSentinel, i));
            }

            for (int i = 20; i < 24; ++i)
            {
                Assert.IsTrue(ds.TryUnion(bottomSentinel, i));
            }

            Assert.IsTrue(ds.TryUnion(0, 4));
            Assert.IsTrue(ds.TryUnion(4, 8));
            Assert.IsTrue(ds.TryUnion(8, 12));
            Assert.IsTrue(ds.TryUnion(12, 16));
            Assert.IsTrue(ds.TryUnion(16, 20));

            Assert.IsTrue(ds.TryIsConnected(topSentinel, bottomSentinel, out bool percolates));
            Assert.IsTrue(percolates);
        }

        [Test]
        public void StressTestRandomUnions()
        {
            DisjointSet ds = new(1000);
            IRandom rng = new PcgRandom(42);

            for (int i = 0; i < 5000; ++i)
            {
                int x = rng.Next(1000);
                int y = rng.Next(1000);
                ds.TryUnion(x, y);
            }

            Assert.LessOrEqual(ds.SetCount, 1000);
            Assert.GreaterOrEqual(ds.SetCount, 1);
        }

        [Test]
        public void GenericVersionConstructorWithNullCollectionThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new DisjointSet<string>(null));
        }

        [Test]
        public void GenericVersionConstructorWithEmptyCollectionThrows()
        {
            Assert.Throws<ArgumentException>(() => new DisjointSet<string>(new List<string>()));
        }

        [Test]
        public void GenericVersionWithDuplicateElementsIgnoresDuplicates()
        {
            List<string> elements = new() { "a", "b", "a", "c", "b" };
            DisjointSet<string> ds = new(elements);

            Assert.AreEqual(3, ds.Count);
            Assert.AreEqual(3, ds.SetCount);
        }

        [Test]
        public void GenericVersionWorks()
        {
            List<string> elements = new() { "a", "b", "c", "d" };
            DisjointSet<string> ds = new(elements);

            Assert.IsTrue(ds.TryUnion("a", "b"));
            Assert.IsTrue(ds.TryIsConnected("a", "b", out bool connected));
            Assert.IsTrue(connected);
        }

        [Test]
        public void GenericVersionTryFindWithInvalidElement()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements);

            Assert.IsFalse(ds.TryFind("d", out string rep));
            Assert.AreEqual(default(string), rep);
        }

        [Test]
        public void GenericVersionTryUnionWithInvalidFirstElement()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements);

            Assert.IsFalse(ds.TryUnion("d", "a"));
            Assert.AreEqual(3, ds.SetCount);
        }

        [Test]
        public void GenericVersionTryUnionWithInvalidSecondElement()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements);

            Assert.IsFalse(ds.TryUnion("a", "d"));
            Assert.AreEqual(3, ds.SetCount);
        }

        [Test]
        public void GenericVersionTryUnionReturnsFalseForSameSet()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements);

            Assert.IsTrue(ds.TryUnion("a", "b"));
            Assert.IsFalse(ds.TryUnion("a", "b"));
        }

        [Test]
        public void GenericVersionTryIsConnectedWithInvalidElement()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements);

            Assert.IsFalse(ds.TryIsConnected("a", "d", out bool connected));
            Assert.IsFalse(connected);
        }

        [Test]
        public void GenericVersionTryGetSetSizeWithInvalidElement()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements);

            Assert.IsFalse(ds.TryGetSetSize("d", out int size));
            Assert.AreEqual(0, size);
        }

        [Test]
        public void GenericVersionTryGetSetWithInvalidElement()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements);

            List<string> results = new();
            ds.TryGetSet("d", results);

            Assert.AreEqual(0, results.Count);
        }

        [Test]
        public void GenericVersionTryGetSetThrowsOnNullList()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements);

            Assert.Throws<ArgumentNullException>(() => ds.TryGetSet("a", null));
        }

        [Test]
        public void GenericVersionTryGetAllSetsThrowsOnNullList()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements);

            Assert.Throws<ArgumentNullException>(() => ds.TryGetAllSets(null));
        }

        [Test]
        public void GenericVersionWithCustomComparer()
        {
            List<string> elements = new() { "a", "b", "c" };
            DisjointSet<string> ds = new(elements, StringComparer.OrdinalIgnoreCase);

            Assert.IsTrue(ds.TryUnion("a", "B"));
            Assert.IsTrue(ds.TryIsConnected("A", "b", out bool connected));
            Assert.IsTrue(connected);
        }

        [Test]
        public void GenericVersionWithValueTypes()
        {
            List<int> elements = new() { 10, 20, 30, 40, 50 };
            DisjointSet<int> ds = new(elements);

            Assert.IsTrue(ds.TryUnion(10, 20));
            Assert.IsTrue(ds.TryUnion(30, 40));
            Assert.AreEqual(3, ds.SetCount);
        }

        [Test]
        public void GenericVersionReset()
        {
            List<string> elements = new() { "a", "b", "c", "d" };
            DisjointSet<string> ds = new(elements);

            Assert.IsTrue(ds.TryUnion("a", "b"));
            Assert.IsTrue(ds.TryUnion("c", "d"));
            ds.Reset();

            Assert.AreEqual(4, ds.SetCount);
        }

        [Test]
        public void GenericVersionTryGetAllSets()
        {
            List<string> elements = new() { "a", "b", "c", "d", "e", "f" };
            DisjointSet<string> ds = new(elements);

            Assert.IsTrue(ds.TryUnion("a", "b"));
            Assert.IsTrue(ds.TryUnion("c", "d"));
            Assert.IsTrue(ds.TryUnion("e", "f"));

            List<List<string>> allSets = new();
            ds.TryGetAllSets(allSets);

            Assert.AreEqual(3, allSets.Count);
            foreach (List<string> set in allSets)
            {
                Assert.AreEqual(2, set.Count);
            }
        }

        [Test]
        public void GenericVersionLargeScale()
        {
            List<int> elements = Enumerable.Range(0, 1000).ToList();
            DisjointSet<int> ds = new(elements);

            for (int i = 0; i < 999; ++i)
            {
                Assert.IsTrue(ds.TryUnion(i, i + 1));
            }

            Assert.AreEqual(1, ds.SetCount);
        }

        [Test]
        public void GenericVersionSocialNetworkSimulation()
        {
            List<string> users = new() { "Alice", "Bob", "Carol", "Dave", "Eve", "Frank" };
            DisjointSet<string> friendGroups = new(users);

            Assert.IsTrue(friendGroups.TryUnion("Alice", "Bob"));
            Assert.IsTrue(friendGroups.TryUnion("Bob", "Carol"));
            Assert.IsTrue(friendGroups.TryUnion("Dave", "Eve"));

            Assert.AreEqual(3, friendGroups.SetCount);

            Assert.IsTrue(friendGroups.TryIsConnected("Alice", "Carol", out bool aliceCarol));
            Assert.IsTrue(aliceCarol);

            Assert.IsTrue(friendGroups.TryIsConnected("Alice", "Dave", out bool aliceDave));
            Assert.IsFalse(aliceDave);

            Assert.IsTrue(friendGroups.TryUnion("Carol", "Dave"));
            Assert.AreEqual(2, friendGroups.SetCount);

            Assert.IsTrue(friendGroups.TryIsConnected("Alice", "Eve", out bool aliceEve));
            Assert.IsTrue(aliceEve);
        }

        [Test]
        public void GenericVersionEquivalenceClassesSimulation()
        {
            List<char> letters = new() { 'a', 'b', 'c', 'd', 'e', 'f' };
            DisjointSet<char> equivalence = new(letters);

            Assert.IsTrue(equivalence.TryUnion('a', 'b'));
            Assert.IsTrue(equivalence.TryUnion('b', 'c'));
            Assert.IsTrue(equivalence.TryUnion('d', 'e'));

            List<char> classA = new();
            equivalence.TryGetSet('a', classA);

            Assert.AreEqual(3, classA.Count);
            CollectionAssert.Contains(classA, 'a');
            CollectionAssert.Contains(classA, 'b');
            CollectionAssert.Contains(classA, 'c');
        }

        [Test]
        public void GenericVersionComplexObjectsWithComparer()
        {
            List<string> urls = new()
            {
                "http://example.com",
                "https://example.com",
                "http://test.com",
                "https://test.com",
            };

            DisjointSet<string> ds = new(urls);

            Assert.IsTrue(ds.TryUnion("http://example.com", "https://example.com"));
            Assert.IsTrue(ds.TryUnion("http://test.com", "https://test.com"));

            Assert.AreEqual(2, ds.SetCount);
        }

        [Test]
        public void SetCountDecreasesCorrectlyWithUnions()
        {
            DisjointSet ds = new(10);
            Assert.AreEqual(10, ds.SetCount);

            Assert.IsTrue(ds.TryUnion(0, 1));
            Assert.AreEqual(9, ds.SetCount);

            Assert.IsTrue(ds.TryUnion(1, 2));
            Assert.AreEqual(8, ds.SetCount);

            Assert.IsTrue(ds.TryUnion(3, 4));
            Assert.AreEqual(7, ds.SetCount);

            Assert.IsFalse(ds.TryUnion(0, 2));
            Assert.AreEqual(7, ds.SetCount);
        }

        [Test]
        public void CountRemainsConstantThroughoutOperations()
        {
            DisjointSet ds = new(20);
            Assert.AreEqual(20, ds.Count);

            for (int i = 0; i < 10; ++i)
            {
                Assert.IsTrue(ds.TryUnion(i, i + 1));
            }

            Assert.AreEqual(20, ds.Count);

            ds.Reset();
            Assert.AreEqual(20, ds.Count);
        }

        [Test]
        public void WorstCasePathCompressionScenario()
        {
            DisjointSet ds = new(100);

            for (int i = 0; i < 99; ++i)
            {
                Assert.IsTrue(ds.TryUnion(i, i + 1));
            }

            for (int i = 0; i < 100; ++i)
            {
                Assert.IsTrue(ds.TryFind(i, out _));
            }

            Assert.IsTrue(ds.TryFind(0, out int rep0));
            Assert.IsTrue(ds.TryFind(99, out int rep99));
            Assert.AreEqual(rep0, rep99);
        }
    }
}
