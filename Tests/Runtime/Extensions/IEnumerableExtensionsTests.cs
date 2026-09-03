// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IEnumerableExtensionsTests : CommonTestBase
    {
        [Test]
        public void ToLinkedListPreservesOrder()
        {
            int[] values = { 1, 2, 3 };
            LinkedList<int> linked = values.ToLinkedList();
            CollectionAssert.AreEqual(values, linked);
        }

        [Test]
        public void AsListReturnsExistingListWhenPossible()
        {
            List<int> list = new() { 1, 2, 3 };
            Assert.AreSame(list, list.AsList());

            int[] array = { 1, 2, 3 };
            IList<int> converted = array.AsList();
            CollectionAssert.AreEqual(array, converted);
            Assert.IsInstanceOf<int[]>(converted);
        }

        [Test]
        public void AsListTurnsOtherCollectionsIntoLists()
        {
            HashSet<int> hashSet = new() { 1, 2, 3 };
            IList<int> list = hashSet.AsList();
            CollectionAssert.AreEqual(hashSet, list);

            Queue<int> queue = new();
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);
            list = queue.AsList();
            CollectionAssert.AreEqual(queue, list);

            Stack<int> stack = new();
            stack.Push(1);
            stack.Push(2);
            stack.Push(3);
            list = stack.AsList();
            CollectionAssert.AreEqual(stack, list);

            LinkedList<int> linked = new();
            linked.AddLast(1);
            linked.AddLast(2);
            linked.AddLast(3);
            list = linked.AsList();
            CollectionAssert.AreEqual(linked, list);

            SortedSet<int> sortedSet = new() { 1, 2, 3 };
            list = sortedSet.AsList();
            CollectionAssert.AreEqual(sortedSet, list);
        }

        [Test]
        public void OrderByUsesProvidedComparer()
        {
            int[] values = { 3, 1, 2 };
            IEnumerable<int> ordered = values.OrderBy((x, y) => y.CompareTo(x));
            CollectionAssert.AreEqual(new[] { 3, 2, 1 }, ordered.ToArray());
        }

        [Test]
        public void OrderedUsesNaturalOrdering()
        {
            int[] values = { 5, 2, 3 };
            CollectionAssert.AreEqual(new[] { 2, 3, 5 }, values.Ordered().ToArray());
        }

        [Test]
        public void ShuffledRetainsAllElements()
        {
            int[] values = Enumerable.Range(0, 10).ToArray();
            IReadOnlyCollection<int> shuffled = values.Shuffled().ToArray();
            CollectionAssert.AreEquivalent(values, shuffled);
        }

        [Test]
        public void InfiniteRepeatsSequence()
        {
            int[] values = { 1, 2 };
            int[] repeated = values.Infinite().Take(5).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 2, 1 }, repeated);
        }

        [Test]
        public void InfiniteQueueMaintainsFifoOrder()
        {
            Queue<int> queue = new();
            queue.Enqueue(10);
            queue.Enqueue(20);
            queue.Enqueue(30);

            int[] repeated = queue.Infinite().Take(7).ToArray();
            CollectionAssert.AreEqual(new[] { 10, 20, 30, 10, 20, 30, 10 }, repeated);
        }

        [Test]
        public void InfiniteStackUsesEnumeratorOrder()
        {
            Stack<int> stack = new();
            stack.Push(1);
            stack.Push(2);
            stack.Push(3);

            int[] repeated = stack.Infinite().Take(6).ToArray();
            CollectionAssert.AreEqual(new[] { 3, 2, 1, 3, 2, 1 }, repeated);
        }

        [Test]
        public void InfiniteBuffersNonCollectionEnumerables()
        {
            IEnumerable<int> source = StreamingSequence();
            int[] repeated = source.Infinite().Take(6).ToArray();
            CollectionAssert.AreEqual(new[] { 0, 1, 0, 1, 0, 1 }, repeated);
        }

        [Test]
        public void InfiniteHandlesEmptyEnumerable()
        {
            IEnumerable<int> source = Array.Empty<int>();
            Assert.IsFalse(source.Infinite().GetEnumerator().MoveNext());
        }

        [Test]
        public void InfiniteBufferedSequenceSupportsMultipleEnumerations()
        {
            IEnumerable<int> infinite = StreamingSequence().Infinite();
            int[] first = infinite.Take(4).ToArray();
            int[] second = infinite.Take(4).ToArray();

            CollectionAssert.AreEqual(new[] { 0, 1, 0, 1 }, first);
            CollectionAssert.AreEqual(first, second);
        }

        [Test]
        public void PartitionSplitsIntoChunks()
        {
            int[] values = { 1, 2, 3, 4, 5 };
            List<int[]> partitions = new();
            foreach (List<int> chunk in values.Partition(2))
            {
                partitions.Add(chunk.ToArray());
            }

            Assert.AreEqual(3, partitions.Count);
            CollectionAssert.AreEqual(new[] { 1, 2 }, partitions[0]);
            CollectionAssert.AreEqual(new[] { 3, 4 }, partitions[1]);
            CollectionAssert.AreEqual(new[] { 5 }, partitions[2]);
        }

        [Test]
        public void PartitionReturnsNoPartitionsForEmptySequence()
        {
            int[][] partitions = Array
                .Empty<int>()
                .Partition(3)
                .Select(chunk => chunk.ToArray())
                .ToArray();

            Assert.IsEmpty(partitions);
        }

        [Test]
        public void PartitionThrowsOnNonPositiveSize()
        {
            int[] values = { 1, 2, 3 };
            Assert.Throws<ArgumentOutOfRangeException>(() => values.Partition(0).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => values.Partition(-1).ToArray());
        }

        [Test]
        public void PartitionPooledDoesNotLeakADisposalSlotPerPartition()
        {
            List<int> values = new();
            for (int index = 0; index < 256; ++index)
            {
                values.Add(index);
            }

            /*
                The documented contract lets a consumer leave each Current undisposed and rely on
                the enumerator's own Dispose. When the enumerator wrapped every partition in a
                SECOND lease, that net claimed only the first, and a DisposalLeases slot reaches the
                free list only through a winning claim -- so every partition burned one for the life
                of the process. 32 passes over 32 partitions is 1,024 slots if the defect is back.
            */
            EnumerateWithoutDisposing(values);
            EnumerateWithoutDisposing(values);

            int before = DisposalLeases.SlotsCreated;
            for (int repetition = 0; repetition < 32; ++repetition)
            {
                EnumerateWithoutDisposing(values);
            }

            int created = DisposalLeases.SlotsCreated - before;
            Assert.LessOrEqual(
                created,
                16,
                $"32 undisposed passes over 32 partitions each created {created} disposal slots. "
                    + "A leak here is hundreds; anything within the budget is another thread's "
                    + "lease, not this."
            );
        }

        [Test]
        public void PartitionPooledReleasesItsRentWhenTheSourceThrows()
        {
            int before = DisposalLeases.SlotsCreated;
            for (int repetition = 0; repetition < 32; ++repetition)
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    foreach (
                        PooledResource<List<int>> partition in ThrowsPartWayThrough()
                            .PartitionPooled(4)
                    )
                    {
                        Assert.LessOrEqual(partition.resource.Count, 4);
                    }
                });
            }

            int created = DisposalLeases.SlotsCreated - before;
            Assert.LessOrEqual(
                created,
                16,
                $"32 throwing enumerations created {created} disposal slots. The rent is not in the "
                    + "enumerator's safety net until the partition is complete, so a source that "
                    + "throws mid-partition used to drop it entirely."
            );
        }

        private static IEnumerable<int> ThrowsPartWayThrough()
        {
            for (int index = 0; index < 6; ++index)
            {
                yield return index;
            }

            throw new InvalidOperationException("the source stopped part way through a partition");
        }

        private static void EnumerateWithoutDisposing(List<int> values)
        {
            using IEnumerator<PooledResource<List<int>>> enumerator = values
                .PartitionPooled(8)
                .GetEnumerator();
            while (enumerator.MoveNext())
            {
                Assert.LessOrEqual(enumerator.Current.resource.Count, 8);
            }
        }

        [Test]
        public void PartitionPooledThrowsOnNonPositiveSize()
        {
            int[] values = { 1, 2, 3 };
            Assert.Throws<ArgumentOutOfRangeException>(() => values.PartitionPooled(0).ToArray());
            Assert.Throws<ArgumentOutOfRangeException>(() => values.PartitionPooled(-1).ToArray());
        }

        [Test]
        public void PartitionPooledReturnsIndependentPooledLists()
        {
            int[] values = { 1, 2, 3, 4, 5 };
            List<int[]> partitions = new();
            foreach (PooledResource<List<int>> pooled in values.PartitionPooled(2))
            {
                using PooledResource<List<int>> lease = pooled; // ensure return to pool
                partitions.Add(lease.resource.ToArray());
            }

            Assert.AreEqual(3, partitions.Count);
            CollectionAssert.AreEqual(new[] { 1, 2 }, partitions[0]);
            CollectionAssert.AreEqual(new[] { 3, 4 }, partitions[1]);
            CollectionAssert.AreEqual(new[] { 5 }, partitions[2]);
        }

        [Test]
        public void PartitionHandlesRemainingElementsWithoutPadding()
        {
            int[] values = Enumerable.Range(1, 7).ToArray();
            List<int[]> partitions = values.Partition(5).Select(chunk => chunk.ToArray()).ToList();

            Assert.AreEqual(2, partitions.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5 }, partitions[0]);
            CollectionAssert.AreEqual(new[] { 6, 7 }, partitions[1]);
        }

        [Test]
        public void PartitionHandlesChunkSizeLargerThanSequence()
        {
            int[] values = { 1, 2, 3 };
            int[][] partitions = values.Partition(5).Select(chunk => chunk.ToArray()).ToArray();

            Assert.That(partitions.Length, Is.EqualTo(1));
            CollectionAssert.AreEqual(values, partitions[0]);
        }

        [Test]
        public void PartitionWithSizeOneProducesSingleElementChunks()
        {
            int[] values = { 9, 8, 7 };
            int[][] partitions = values.Partition(1).Select(chunk => chunk.ToArray()).ToArray();

            Assert.That(partitions.Length, Is.EqualTo(values.Length));
            for (int i = 0; i < values.Length; ++i)
            {
                CollectionAssert.AreEqual(new[] { values[i] }, partitions[i]);
            }
        }

        [Test]
        public void PartitionPooledRequiresDisposalToReturnBuffers()
        {
            int[] values = { 1, 2, 3, 4 };
            using IEnumerator<PooledResource<List<int>>> enumerator = values
                .PartitionPooled(2)
                .GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            PooledResource<List<int>> first = enumerator.Current;
            List<int> firstChunk = first.resource;
            int[] firstSnapshot = firstChunk.ToArray();
            CollectionAssert.AreEqual(new[] { 1, 2 }, firstSnapshot);

            Assert.IsTrue(enumerator.MoveNext());
            using PooledResource<List<int>> second = enumerator.Current;
            CollectionAssert.AreEqual(new[] { 3, 4 }, second.resource);

            first.Dispose();
            Assert.AreEqual(0, firstChunk.Count, "Disposed pooled list should be cleared.");
        }

        [Test]
        public void PartitionPooledReusesClearedBuffers()
        {
            int[] values = { 1, 2, 3, 4 };
            List<int> capturedBuffer = null;

            foreach (PooledResource<List<int>> pooled in values.PartitionPooled(2))
            {
                using PooledResource<List<int>> lease = pooled;
                if (capturedBuffer == null)
                {
                    capturedBuffer = lease.resource;
                }
            }

            Assert.IsTrue(capturedBuffer != null);
            Assert.AreEqual(0, capturedBuffer.Count);

            using IEnumerator<PooledResource<List<int>>> enumerator = values
                .PartitionPooled(2)
                .GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());
            using PooledResource<List<int>> leased = enumerator.Current;
            Assert.AreSame(
                capturedBuffer,
                leased.resource,
                "Buffers should be reused after disposal."
            );
            CollectionAssert.AreEqual(new[] { 1, 2 }, leased.resource);
        }

        [Test]
        public void PartitionPooledEnumeratorDisposesOutstandingChunks()
        {
            int[] values = { 1, 2, 3, 4 };
            using IEnumerator<PooledResource<List<int>>> enumerator = values
                .PartitionPooled(2)
                .GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            PooledResource<List<int>> chunk = enumerator.Current;
            List<int> buffer = chunk.resource;
            CollectionAssert.AreEqual(new[] { 1, 2 }, buffer);

            enumerator.Dispose();

            Assert.AreEqual(
                0,
                buffer.Count,
                "Enumerator disposal should release outstanding pooled lists."
            );
        }

        private static IEnumerable<int> StreamingSequence()
        {
            yield return 0;
            yield return 1;
        }

        [UnityTest]
        public IEnumerator PartitionPooledSupportsFrameDelayedDisposal()
        {
            int[] values = { 10, 11, 12, 13 };
            using IEnumerator<PooledResource<List<int>>> enumerator = values
                .PartitionPooled(2)
                .GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            PooledResource<List<int>> chunk = enumerator.Current;
            List<int> buffer = chunk.resource;
            CollectionAssert.AreEqual(new[] { 10, 11 }, buffer);

            // Simulate work across frames while the pooled list remains in use.
            yield return null;
            CollectionAssert.AreEqual(new[] { 10, 11 }, buffer);

            chunk.Dispose();
            Assert.AreEqual(
                0,
                buffer.Count,
                "Disposing after a frame should still release the pooled list."
            );
        }

        [UnityTest]
        public IEnumerator PartitionPooledReleasesChunksWhenExceptionOccurs()
        {
            int[] values = { 5, 6, 7, 8 };
            List<int> leakedBuffer = null;

            IEnumerator<PooledResource<List<int>>> enumerator = values
                .PartitionPooled(2)
                .GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            PooledResource<List<int>> chunk = enumerator.Current;
            leakedBuffer = chunk.resource;
            // Intentionally leak (no dispose) and mimic async work.
            yield return null;

            bool exceptionObserved = false;
            try
            {
                throw new InvalidOperationException("Simulated failure mid-enumeration.");
            }
            catch (InvalidOperationException)
            {
                exceptionObserved = true;
            }
            finally
            {
                enumerator.Dispose();
            }

            Assert.IsTrue(exceptionObserved, "The simulated failure should be observed.");
            Assert.IsTrue(leakedBuffer != null);
            Assert.AreEqual(
                0,
                leakedBuffer.Count,
                "Enumerator disposal should return leaked pooled lists even after exceptions."
            );
        }

        [UnityTest]
        public IEnumerator PartitionPooledHandlesMultipleOutstandingDisposals()
        {
            int[] values = { 1, 2, 3, 4, 5, 6 };
            using IEnumerator<PooledResource<List<int>>> enumerator = values
                .PartitionPooled(2)
                .GetEnumerator();

            Assert.IsTrue(enumerator.MoveNext());
            PooledResource<List<int>> first = enumerator.Current;

            Assert.IsTrue(enumerator.MoveNext());
            PooledResource<List<int>> second = enumerator.Current;

            // Release only one chunk.
            first.Dispose();
            Assert.AreEqual(0, first.resource.Count);

            // Enumerator disposal should release the other chunk automatically.
            enumerator.Dispose();
            Assert.AreEqual(0, second.resource.Count);
            yield break;
        }
    }
}
