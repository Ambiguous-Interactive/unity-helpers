// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class PriorityQueueTests
    {
        [Test]
        public void DefaultConstructorStartsEmpty()
        {
            PriorityQueue<int> queue = new();

            Assert.AreEqual(0, queue.Count);
            Assert.IsTrue(queue.IsEmpty);
            Assert.GreaterOrEqual(queue.Capacity, 16);
        }

        [Test]
        public void EnqueueAndDequeueRespectMinPriorityOrdering()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(5);
            queue.Enqueue(1);
            queue.Enqueue(3);

            Assert.IsTrue(queue.TryDequeue(out int first));
            Assert.AreEqual(1, first);
            Assert.IsTrue(queue.TryDequeue(out int second));
            Assert.AreEqual(3, second);
            Assert.IsTrue(queue.TryDequeue(out int third));
            Assert.AreEqual(5, third);
            Assert.IsTrue(queue.IsEmpty);
        }

        [Test]
        public void CreateMaxOrdersDescendingValues()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMax();
            queue.Enqueue(5);
            queue.Enqueue(1);
            queue.Enqueue(3);

            Assert.IsTrue(queue.TryDequeue(out int first));
            Assert.AreEqual(5, first);
            Assert.IsTrue(queue.TryDequeue(out int second));
            Assert.AreEqual(3, second);
            Assert.IsTrue(queue.TryDequeue(out int third));
            Assert.AreEqual(1, third);
        }

        [Test]
        public void ConstructorFromCollectionHeapifiesInput()
        {
            int[] items = { 9, 2, 7, 4 };
            PriorityQueue<int> queue = new(items);

            Assert.AreEqual(items.Length, queue.Count);
            Assert.IsTrue(queue.TryDequeue(out int first));
            Assert.AreEqual(2, first);
            Assert.IsTrue(queue.TryDequeue(out int second));
            Assert.AreEqual(4, second);
            Assert.IsTrue(queue.TryDequeue(out int third));
            Assert.AreEqual(7, third);
            Assert.IsTrue(queue.TryDequeue(out int fourth));
            Assert.AreEqual(9, fourth);
            Assert.IsTrue(queue.IsEmpty);
        }

        [Test]
        public void CreateMaxFromCollectionUsesProvidedItems()
        {
            List<int> items = new() { 1, 3, 5, 7 };
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMax(items);

            foreach (int expected in new[] { 7, 5, 3, 1 })
            {
                Assert.IsTrue(queue.TryDequeue(out int value));
                Assert.AreEqual(expected, value);
            }
        }

        [Test]
        public void TryPeekDoesNotRemoveElement()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(5);
            queue.Enqueue(2);

            Assert.IsTrue(queue.TryPeek(out int peeked));
            Assert.AreEqual(2, peeked);
            Assert.AreEqual(2, queue.Count);
        }

        [Test]
        public void TryPeekReturnsFalseWhenQueueEmpty()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();

            Assert.IsFalse(queue.TryPeek(out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void TryDequeueReturnsFalseWhenQueueEmpty()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();

            Assert.IsFalse(queue.TryDequeue(out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void ContainsDetectsValuesAfterMixedOperations()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(10);
            queue.Enqueue(5);
            queue.Enqueue(15);
            Assert.IsTrue(queue.TryDequeue(out int removedValue));
            Assert.AreEqual(5, removedValue);

            Assert.IsTrue(queue.Contains(10));
            Assert.IsTrue(queue.Contains(15));
            Assert.IsFalse(queue.Contains(5));
        }

        [Test]
        public void ClearEmptiesQueueAndResetsCount()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);

            queue.Clear();

            Assert.AreEqual(0, queue.Count);
            Assert.IsTrue(queue.IsEmpty);
            int peekValue;
            Assert.IsFalse(queue.TryPeek(out peekValue));
            int dequeuedValue;
            Assert.IsFalse(queue.TryDequeue(out dequeuedValue));
        }

        [Test]
        public void TryUpdatePriorityPromotesElementUpwards()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(10);
            queue.Enqueue(20);
            queue.Enqueue(30);
            queue.Enqueue(40);

            int[] snapshot = queue.ToArray();
            int index = Array.IndexOf(snapshot, 40);
            Assert.GreaterOrEqual(index, 0);

            Assert.IsTrue(queue.TryUpdatePriority(index, 1));
            Assert.IsTrue(queue.TryPeek(out int result));
            Assert.AreEqual(1, result);
        }

        [Test]
        public void TryUpdatePriorityDemotesElementDownwards()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(1);
            queue.Enqueue(5);
            queue.Enqueue(10);

            Assert.IsTrue(queue.TryUpdatePriority(0, 50));
            Assert.IsTrue(queue.TryPeek(out int result));
            Assert.AreEqual(5, result);

            List<int> ordered = new();
            while (queue.TryDequeue(out int value))
            {
                ordered.Add(value);
            }

            CollectionAssert.AreEqual(new[] { 5, 10, 50 }, ordered);
        }

        [Test]
        public void TryUpdatePriorityReturnsFalseForInvalidIndex()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(1);

            Assert.IsFalse(queue.TryUpdatePriority(-1, 0));
            Assert.IsFalse(queue.TryUpdatePriority(3, 0));
        }

        [Test]
        public void TryGetReturnsElementAtIndex()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(1);
            queue.Enqueue(2);

            Assert.IsTrue(queue.TryGet(0, out int element));
            Assert.AreEqual(1, element);
        }

        [Test]
        public void TryGetReturnsFalseForInvalidIndex()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(1);

            int invalidResult;
            Assert.IsFalse(queue.TryGet(-1, out invalidResult));
            Assert.IsFalse(queue.TryGet(5, out invalidResult));
        }

        [Test]
        public void ToArrayReturnsHeapSnapshot()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(3);
            queue.Enqueue(1);
            queue.Enqueue(2);

            int[] snapshot = queue.ToArray();

            CollectionAssert.AreEquivalent(new[] { 1, 2, 3 }, snapshot);
            Assert.AreEqual(3, queue.Count);
        }

        [Test]
        public void ToArrayRefReusesProvidedBufferWhenLargeEnough()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(3);
            queue.Enqueue(1);
            queue.Enqueue(2);

            int[] buffer = new int[queue.Count];
            int[] alias = buffer;
            int count = queue.ToArray(ref alias);

            Assert.AreSame(buffer, alias);
            Assert.AreEqual(queue.Count, count);
            CollectionAssert.AreEquivalent(queue.ToArray(), alias);
        }

        [Test]
        public void EnumeratorIteratesAllItemsWithoutModifyingQueue()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(10);
            queue.Enqueue(20);
            queue.Enqueue(30);

            List<int> enumerated = new();
            foreach (int value in queue)
            {
                enumerated.Add(value);
            }

            CollectionAssert.AreEquivalent(queue.ToArray(), enumerated);
            Assert.AreEqual(3, queue.Count);
        }

        [Test]
        public void TrimExcessReducesCapacityWhenSparse()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin(32);
            int initialCapacity = queue.Capacity;
            Assert.GreaterOrEqual(initialCapacity, 32);

            queue.TrimExcess();

            Assert.Less(queue.Capacity, initialCapacity);
            Assert.GreaterOrEqual(queue.Capacity, queue.Count);
        }

        [Test]
        public void CustomComparerControlsOrdering()
        {
            IComparer<string> comparer = Comparer<string>.Create(
                (a, b) => a.Length.CompareTo(b.Length)
            );
            PriorityQueue<string> queue = new(comparer);
            queue.Enqueue("ccc");
            queue.Enqueue("a");
            queue.Enqueue("bb");

            Assert.IsTrue(queue.TryDequeue(out string first));
            Assert.AreEqual("a", first);
            Assert.IsTrue(queue.TryDequeue(out string second));
            Assert.AreEqual("bb", second);
            Assert.IsTrue(queue.TryDequeue(out string third));
            Assert.AreEqual("ccc", third);
        }

        [Test]
        public void DuplicateValuesAreHandledCorrectly()
        {
            PriorityQueue<int> queue = PriorityQueue<int>.CreateMin();
            queue.Enqueue(5);
            queue.Enqueue(5);
            queue.Enqueue(5);

            Assert.AreEqual(3, queue.Count);

            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(queue.TryDequeue(out int value));
                Assert.AreEqual(5, value);
            }

            Assert.IsTrue(queue.IsEmpty);
        }
    }
}
