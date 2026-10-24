// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    public sealed class DisjointSetPersistenceTests
    {
        [TestCase(-1, false)]
        [TestCase(3, false)]
        [TestCase(int.MinValue, false)]
        [TestCase(int.MaxValue, false)]
#if !WALLSTOP_PROTO_ONLY
        [TestCase(-1, true)]
        [TestCase(3, true)]
        [TestCase(int.MinValue, true)]
        [TestCase(int.MaxValue, true)]
#endif
        public void OutOfRangeParentRefusesQueries(int parent, bool legacy)
        {
            DisjointSet sets = ReadGraph(new[] { 1, parent, 2 }, new int[3], legacy);
            Assert.That(sets.TryFind(0, out int root), Is.False);
            Assert.That(root, Is.EqualTo(-1));
            Assert.That(sets.TryIsConnected(0, 2, out bool connected), Is.False);
            Assert.That(connected, Is.False);
            Assert.That(sets.TryGetSetSize(0, out int size), Is.False);
            Assert.That(size, Is.Zero);
            Assert.That(sets.TryGetSet(0, new List<int> { 99 }), Is.Empty);
            Assert.That(sets.TryUnion(0, 2), Is.False);
            Assert.That(sets.TryFind(2, out root), Is.True);
            Assert.That(root, Is.EqualTo(2));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
#if !WALLSTOP_PROTO_ONLY
        [TestCase(false, true)]
        [TestCase(true, true)]
#endif
        public void CyclicParentsRefuseWithoutChangingTheGraph(bool tail, bool legacy)
        {
            int[] parents = tail ? new[] { 1, 2, 1, 3 } : new[] { 1, 0, 2, 3 };
            DisjointSet sets = ReadGraph(parents, new int[4], legacy);
            byte[] before = WProtoFacade.Serialize(sets);
            Assert.That(sets.TryFind(0, out int root), Is.False);
            Assert.That(root, Is.EqualTo(-1));
            Assert.That(sets.TryUnion(0, 3), Is.False);
            Assert.That(WProtoFacade.Serialize(sets), Is.EqualTo(before));
            Assert.That(sets.TryGetSet(0, new List<int> { 99 }), Is.Empty);
            Assert.That(sets.TryGetAllSets(new List<List<int>>()).Count, Is.EqualTo(tail ? 1 : 2));
            sets.Reset();
            Assert.That(sets.SetCount, Is.EqualTo(4));
            Assert.That(sets.TryUnion(0, 3), Is.True);
        }

        [TestCase(1, false)]
        [TestCase(100000, false)]
#if !WALLSTOP_PROTO_ONLY
        [TestCase(1, true)]
        [TestCase(100000, true)]
#endif
        public void DeepParentChainFindsAndCompressesWithoutRecursion(int count, bool legacy)
        {
            int[] parents = new int[count];
            int root = count - 1;
            for (int index = 0; index < count; ++index)
            {
                parents[index] = Math.Min(index + 1, root);
            }
            DisjointSet sets = ReadGraph(parents, new int[count], legacy);
            Assert.That(sets.TryFind(0, out int found), Is.True);
            Assert.That(found, Is.EqualTo(root));
            for (int index = 0; index < count; ++index)
            {
                parents[index] = root;
            }
            DisjointSet expected = ReadGraph(parents, new int[count], false);
            Assert.That(WProtoFacade.Serialize(sets), Is.EqualTo(WProtoFacade.Serialize(expected)));
            Assert.That(sets.TryGetSetSize(0, out int size), Is.True);
            Assert.That(size, Is.EqualTo(count));
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(4, false)]
#if !WALLSTOP_PROTO_ONLY
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(4, true)]
#endif
        public void MismatchedRankStorageRefusesUnionAndResetRepairsIt(int rankCount, bool legacy)
        {
            DisjointSet sets = ReadGraph(new[] { 0, 1, 2 }, new int[rankCount], legacy);
            Assert.That(sets.TryUnion(0, 2), Is.False);
            Assert.That(sets.TryFind(2, out int root), Is.True);
            Assert.That(root, Is.EqualTo(2));
            sets.Reset();
            Assert.That(sets.TryUnion(0, 2), Is.True);
            Assert.That(sets.SetCount, Is.EqualTo(2));
        }

        [TestCase(-1, false)]
        [TestCase(int.MinValue, false)]
#if !WALLSTOP_PROTO_ONLY
        [TestCase(-1, true)]
        [TestCase(int.MinValue, true)]
#endif
        public void NegativeRankRefusesUnionWithoutChangingConnectivity(int rank, bool legacy)
        {
            DisjointSet sets = ReadGraph(new[] { 0, 1, 2 }, new[] { 0, 0, rank }, legacy);
            Assert.That(sets.TryUnion(0, 2), Is.False);
            Assert.That(sets.TryUnion(2, 0), Is.False);
            Assert.That(sets.TryIsConnected(0, 2, out bool connected), Is.True);
            Assert.That(connected, Is.False);
            Assert.That(sets.SetCount, Is.EqualTo(3));
        }

        [TestCase(false)]
#if !WALLSTOP_PROTO_ONLY
        [TestCase(true)]
#endif
        public void MaximumRanksDoNotOverflowDuringUnion(bool legacy)
        {
            DisjointSet sets = ReadGraph(
                new[] { 0, 1, 2 },
                new[] { int.MaxValue, int.MaxValue, int.MaxValue },
                legacy
            );
            Assert.That(sets.TryUnion(0, 1), Is.True);
            Assert.That(sets.TryUnion(0, 2), Is.True);
            Assert.That(sets.SetCount, Is.EqualTo(1));
            Assert.That(sets.TryGetSetSize(0, out int size), Is.True);
            Assert.That(size, Is.EqualTo(3));
        }

        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(4, false)]
        [TestCase(int.MinValue, false)]
        [TestCase(int.MaxValue, false)]
#if !WALLSTOP_PROTO_ONLY
        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(4, true)]
        [TestCase(int.MinValue, true)]
        [TestCase(int.MaxValue, true)]
#endif
        public void InvalidSetCountRefusesUnionAndResetRepairsIt(int setCount, bool legacy)
        {
            DisjointSet sets = ReadGraph(new[] { 0, 1, 2 }, new int[3], legacy, setCount);
            Assert.That(sets.TryUnion(0, 2), Is.False);
            Assert.That(sets.SetCount, Is.EqualTo(setCount));
            sets.Reset();
            Assert.That(sets.SetCount, Is.EqualTo(3));
            Assert.That(sets.TryUnion(0, 2), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AliasedResultBuffersKeepPartitionsSeparate(bool generic)
        {
            List<int> shared = new List<int> { 99 };
            List<List<int>> results = new List<List<int>> { shared, null, shared };
            DisjointSet indices = generic ? null : new DisjointSet(3);
            DisjointSet<int> values = generic ? new DisjointSet<int>(new[] { 0, 1, 2 }) : null;
            for (int iteration = 0; iteration < 2; ++iteration)
            {
                if (generic)
                {
                    Assert.That(values.TryGetAllSets(results), Is.SameAs(results));
                }
                else
                {
                    Assert.That(indices.TryGetAllSets(results), Is.SameAs(results));
                }
                Assert.That(results.Count, Is.EqualTo(3));
                Assert.That(results[0], Is.EqualTo(new[] { 0 }));
                Assert.That(results[1], Is.EqualTo(new[] { 1 }));
                Assert.That(results[2], Is.EqualTo(new[] { 2 }));
                Assert.That(results[0], Is.Not.SameAs(results[1]));
                Assert.That(results[0], Is.Not.SameAs(results[2]));
                Assert.That(results[1], Is.Not.SameAs(results[2]));
                Assert.That(results, Does.Contain(shared));
            }
        }

#if UNITY_5_3_OR_NEWER
        [TestCase(false)]
        [TestCase(true)]
        public void ReusedResultBuffersAvoidObservableAllocations(bool generic)
        {
            object[] retained = new object[1];
            object preboxed = 42;
            long control = ValueEnumerationAllocationTests.MeasureProfilerAllocationCount(() =>
                retained[0] = (object)42
            );
            if (control == 0)
            {
                Assert.Ignore("The GC.Alloc marker did not observe fresh retained boxes.");
            }
            Assert.That(control, Is.EqualTo(ValueEnumerationAllocationTests.ProfilerCalls));
            Assert.That(
                ValueEnumerationAllocationTests.MeasureProfilerAllocationCount(() =>
                    retained[0] = preboxed
                ),
                Is.Zero
            );
            DisjointSet indices = new DisjointSet(4);
            DisjointSet<int> values = new DisjointSet<int>(new[] { 0, 1, 2, 3 });
            indices.TryUnion(0, 1);
            indices.TryUnion(2, 3);
            values.TryUnion(0, 1);
            values.TryUnion(2, 3);
            List<List<int>> results = new List<List<int>>();
            Action collect = generic
                ? () => values.TryGetAllSets(results)
                : () => indices.TryGetAllSets(results);
            Assert.That(
                ValueEnumerationAllocationTests.MeasureProfilerAllocationCount(collect),
                Is.Zero
            );
            Assert.That(results.Count, Is.EqualTo(2));
            GC.KeepAlive(retained);
        }
#endif

        [TestCase(false)]
#if !WALLSTOP_PROTO_ONLY
        [TestCase(true)]
#endif
        public void AllFourElementGraphsMatchIndependentReachability(bool legacy)
        {
            const int count = 4;
            const int choices = count + 2;
            const int graphCount = choices * choices * choices * choices;
            for (int graph = 0; graph < graphCount; ++graph)
            {
                int encoded = graph;
                int[] parents = new int[count];
                bool[,] reachable = new bool[count, count];
                for (int index = 0; index < count; ++index)
                {
                    int parent = encoded % choices - 1;
                    encoded /= choices;
                    parents[index] = parent;
                    if (0 <= parent && parent < count)
                    {
                        reachable[index, parent] = true;
                    }
                }
                for (int intermediate = 0; intermediate < count; ++intermediate)
                {
                    for (int from = 0; from < count; ++from)
                    {
                        for (int to = 0; to < count; ++to)
                        {
                            reachable[from, to] |=
                                reachable[from, intermediate] && reachable[intermediate, to];
                        }
                    }
                }
                DisjointSet sets = ReadGraph(parents, new int[count], legacy);
                for (int index = 0; index < count; ++index)
                {
                    int expected = -1;
                    for (int candidate = 0; candidate < count; ++candidate)
                    {
                        if (parents[candidate] == candidate && reachable[index, candidate])
                        {
                            expected = candidate;
                        }
                    }
                    Assert.That(
                        sets.TryFind(index, out int root),
                        Is.EqualTo(0 <= expected),
                        "Graph " + graph
                    );
                    Assert.That(root, Is.EqualTo(expected), "Graph " + graph);
                }
            }
        }

        [TestCase(false)]
#if !WALLSTOP_PROTO_ONLY
        [TestCase(true)]
#endif
        public void EmptyDeliveredGraphRemainsEmptyAfterReset(bool legacy)
        {
            DisjointSet sets = ReadGraph(Array.Empty<int>(), Array.Empty<int>(), legacy, 0);
            Assert.That(sets.Count, Is.Zero);
            Assert.That(sets.TryFind(0, out int root), Is.False);
            Assert.That(root, Is.EqualTo(-1));
            Assert.That(sets.TryGetAllSets(new List<List<int>> { new List<int> { 99 } }), Is.Empty);
            sets.Reset();
            Assert.That(sets.Count, Is.Zero);
            Assert.That(sets.SetCount, Is.Zero);
            Assert.That(sets.TryUnion(0, 0), Is.False);
        }

        private static DisjointSet ReadGraph(
            int[] parents,
            int[] ranks,
            bool legacy,
            int? setCount = null
        )
        {
            byte[] bytes = new byte[checked((parents.Length + ranks.Length) * 11 + 16)];
            WProtoWriter writer = new WProtoWriter(bytes);
            foreach (int parent in parents)
            {
                Assert.That(writer.TryWriteTag(1, WProtoWireType.Varint), Is.True);
                Assert.That(writer.TryWriteInt32(parent), Is.True);
            }
            foreach (int rank in ranks)
            {
                Assert.That(writer.TryWriteTag(2, WProtoWireType.Varint), Is.True);
                Assert.That(writer.TryWriteInt32(rank), Is.True);
            }
            Assert.That(writer.TryWriteTag(3, WProtoWireType.Varint), Is.True);
            Assert.That(writer.TryWriteInt32(setCount ?? parents.Length), Is.True);
            byte[] payload = writer.Written.ToArray();
#if !WALLSTOP_PROTO_ONLY
            if (legacy)
            {
                using MemoryStream stream = new MemoryStream(payload);
                return ProtoBuf.Serializer.Deserialize<DisjointSet>(stream);
            }
#endif
            return WProtoFacade.Deserialize<DisjointSet>(payload);
        }
    }
}
