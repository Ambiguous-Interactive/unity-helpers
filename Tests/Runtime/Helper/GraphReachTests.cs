// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class GraphReachTests
    {
        private static void CompareWithOracle(int nodeCount, (int from, int to)[] edges, int root)
        {
            HashSet<int> expected = new() { root };
            Queue<int> queue = new();
            queue.Enqueue(root);
            while (0 < queue.Count)
            {
                Assert.That(queue.TryDequeue(out int current), Is.True);
                foreach ((int from, int to) edge in edges)
                {
                    if (edge.from == current && expected.Add(edge.to))
                    {
                        queue.Enqueue(edge.to);
                    }
                }
            }
            BitSet actual = new(1);
            actual.TrySet(nodeCount + 20);
            Assert.That(GraphReach.Reachable(nodeCount, edges, root, actual), Is.True);
            for (int node = 0; node < nodeCount + 21; ++node)
            {
                Assert.That(actual.TryGet(node, out bool visited), Is.True);
                Assert.That(visited, Is.EqualTo(expected.Contains(node)), $"Node {node}");
            }
            if (nodeCount <= 64)
            {
                ulong expectedMask = 0;
                foreach (int node in expected)
                {
                    expectedMask |= 1UL << node;
                }
                Assert.That(GraphReach.Reachable(nodeCount, edges, root), Is.EqualTo(expectedMask));
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(32)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(1024)]
        public void ChainsCyclesDuplicatesAndDisconnectedRootsMatchOracle(int count)
        {
            List<(int from, int to)> edges = new();
            for (int node = count - 1; 0 < node; --node)
            {
                edges.Add((node - 1, node));
                edges.Add((node - 1, node));
                edges.Add((node, node));
            }
            (int from, int to)[] array = edges.ToArray();
            CompareWithOracle(count, array, 0);
            CompareWithOracle(count, array, count - 1);
            CompareWithOracle(count, Array.Empty<(int, int)>(), count - 1);
            edges.Add((count - 1, 0));
            CompareWithOracle(count, edges.ToArray(), count / 2);
        }

        [TestCase(1)]
        [TestCase(17)]
        [TestCase(64)]
        [TestCase(129)]
        public void SeededGraphsAndReorderedEdgesMatchOracle(int count)
        {
            Random random = new(672);
            (int from, int to)[] edges = new (int, int)[count * 3];
            for (int sample = 0; sample < 8; ++sample)
            {
                for (int i = 0; i < edges.Length; ++i)
                {
                    edges[i] = (random.Next(count), random.Next(count));
                }
                int root = random.Next(count);
                CompareWithOracle(count, edges, root);
                Array.Reverse(edges);
                CompareWithOracle(count, edges, root);
            }
        }

        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(1, -1)]
        [TestCase(1, 1)]
        [TestCase(64, 64)]
        public void InvalidDimensionsClearResults(int count, int root)
        {
            BitSet result = new(64);
            result.SetAll();
            Assert.That(
                GraphReach.Reachable(count, ReadOnlySpan<(int, int)>.Empty, root, result),
                Is.False
            );
            Assert.That(result.GetSetBits(new List<int>()), Is.Empty);
            Assert.That(GraphReach.Reachable(count, ReadOnlySpan<(int, int)>.Empty, root), Is.Zero);
        }

        [TestCase(-1, 0)]
        [TestCase(0, -1)]
        [TestCase(2, 0)]
        [TestCase(0, 2)]
        [TestCase(int.MaxValue, 0)]
        public void InvalidUnreachableEdgesFailBeforeTraversal(int from, int to)
        {
            (int from, int to)[] edges = { (1, 1), (from, to) };
            BitSet result = new(64);
            result.SetAll();
            Assert.That(GraphReach.Reachable(2, edges, 1, result), Is.False);
            Assert.That(result.GetSetBits(new List<int>()), Is.Empty);
            Assert.That(GraphReach.Reachable(2, edges, 1), Is.Zero);
            Assert.That(GraphReach.Reachable(2, edges, 1, null), Is.False);
        }

        [Test]
        public void MaskRejectsMoreThan64NodesAndIncludesHighestRoot()
        {
            Assert.That(GraphReach.Reachable(65, ReadOnlySpan<(int, int)>.Empty, 0), Is.Zero);
            Assert.That(
                GraphReach.Reachable(64, ReadOnlySpan<(int, int)>.Empty, 63),
                Is.EqualTo(1UL << 63)
            );
        }
    }
}
