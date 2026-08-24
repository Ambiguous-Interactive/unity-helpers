// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DequeTests
    {
        [Test]
        public void PushFrontAddsFront()
        {
            Deque<int> deque = new(Deque<int>.DefaultCapacity);
            deque.PushFront(1);
            deque.PushFront(2);

            Assert.AreEqual(2, deque.Count);
            Assert.AreEqual(2, deque[0]);
            Assert.AreEqual(1, deque[1]);
        }

        [Test]
        public void PushBackAddsBack()
        {
            Deque<int> deque = new(Deque<int>.DefaultCapacity);
            deque.PushBack(1);
            deque.PushBack(2);

            Assert.AreEqual(2, deque.Count);
            Assert.AreEqual(1, deque[0]);
            Assert.AreEqual(2, deque[1]);
        }

        [Test]
        public void TryPopFrontRemovesFront()
        {
            Deque<int> deque = new(Deque<int>.DefaultCapacity);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            Assert.IsTrue(deque.TryPopFront(out int result));
            Assert.AreEqual(1, result);
            Assert.AreEqual(2, deque.Count);
        }

        [Test]
        public void TryPopBackRemovesBack()
        {
            Deque<int> deque = new(Deque<int>.DefaultCapacity);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            Assert.IsTrue(deque.TryPopBack(out int result));
            Assert.AreEqual(3, result);
            Assert.AreEqual(2, deque.Count);
        }

        [Test]
        public void TryPopReturnsFalseWhenEmpty()
        {
            Deque<int> deque = new(Deque<int>.DefaultCapacity);

            Assert.IsFalse(deque.TryPopFront(out _));
            Assert.IsFalse(deque.TryPopBack(out _));
        }

        [Test]
        public void TryPeekFrontReturnsWithoutRemoving()
        {
            Deque<int> deque = new(Deque<int>.DefaultCapacity);
            deque.PushBack(1);
            deque.PushBack(2);

            Assert.IsTrue(deque.TryPeekFront(out int result));
            Assert.AreEqual(1, result);
            Assert.AreEqual(2, deque.Count);
        }

        [Test]
        public void TryPeekBackReturnsWithoutRemoving()
        {
            Deque<int> deque = new(Deque<int>.DefaultCapacity);
            deque.PushBack(1);
            deque.PushBack(2);

            Assert.IsTrue(deque.TryPeekBack(out int result));
            Assert.AreEqual(2, result);
            Assert.AreEqual(2, deque.Count);
        }

        [Test]
        public void GrowsAutomatically()
        {
            Deque<int> deque = new(2);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            Assert.AreEqual(3, deque.Count);
            Assert.IsTrue(3 <= deque.Capacity);
        }

        [Test]
        public void CircularWrapping()
        {
            Deque<int> deque = new(4);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.TryPopFront(out _);
            deque.TryPopFront(out _);
            deque.PushBack(3);
            deque.PushBack(4);
            deque.PushBack(5);

            Assert.AreEqual(3, deque.Count);
            Assert.AreEqual(3, deque[0]);
        }

        // Constructor Tests
        [Test]
        public void ConstructorWithCapacityCreatesEmptyDeque()
        {
            Deque<int> deque = new(10);
            Assert.AreEqual(0, deque.Count);
            Assert.AreEqual(10, deque.Capacity);
            Assert.IsTrue(deque.IsEmpty);
        }

        [Test]
        public void ConstructorWithZeroOrNegativeCapacityThrowsException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Deque<int>(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Deque<int>(-1));
        }

        [Test]
        public void ConstructorWithNullCollectionThrowsException()
        {
            Assert.Throws<ArgumentNullException>(() => new Deque<int>(null));
        }

        [Test]
        public void ConstructorWithIReadOnlyListCreatesDequeWithCorrectElements()
        {
            IReadOnlyList<int> list = new List<int> { 1, 2, 3, 4, 5 };
            Deque<int> deque = new(list);

            Assert.AreEqual(5, deque.Count);
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(i + 1, deque[i]);
            }
        }

        [Test]
        public void ConstructorWithICollectionCreatesDequeWithCorrectElements()
        {
            ICollection<int> collection = new HashSet<int> { 1, 2, 3 };
            Deque<int> deque = new(collection);

            Assert.AreEqual(3, deque.Count);
            Assert.IsTrue(deque.Contains(1));
            Assert.IsTrue(deque.Contains(2));
            Assert.IsTrue(deque.Contains(3));
        }

        [Test]
        public void ConstructorWithIEnumerableCreatesDequeWithCorrectElements()
        {
            IEnumerable<int> enumerable = Enumerable.Range(1, 5);
            Deque<int> deque = new(enumerable);

            Assert.AreEqual(5, deque.Count);
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(i + 1, deque[i]);
            }
        }

        // Indexer Tests
        [Test]
        public void IndexerGetReturnsCorrectElements()
        {
            Deque<int> deque = new(10);
            deque.PushBack(10);
            deque.PushBack(20);
            deque.PushFront(5);

            Assert.AreEqual(5, deque[0]);
            Assert.AreEqual(10, deque[1]);
            Assert.AreEqual(20, deque[2]);
        }

        [Test]
        public void IndexerSetUpdatesElements()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            deque[1] = 99;

            Assert.AreEqual(1, deque[0]);
            Assert.AreEqual(99, deque[1]);
            Assert.AreEqual(3, deque[2]);
        }

        [Test]
        public void IndexerGetWithNegativeIndexThrowsException()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);

            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                int unused = deque[-1];
            });
        }

        [Test]
        public void IndexerGetWithIndexEqualToCountThrowsException()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);

            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                int unused = deque[1];
            });
        }

        [Test]
        public void IndexerSetWithNegativeIndexThrowsException()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);

            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                deque[-1] = 99;
            });
        }

        [Test]
        public void IndexerSetWithIndexEqualToCountThrowsException()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);

            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                deque[1] = 99;
            });
        }

        // TryPeek Tests
        [Test]
        public void TryPeekFrontOnEmptyDequeReturnsFalse()
        {
            Deque<int> deque = new(10);
            Assert.IsFalse(deque.TryPeekFront(out int result));
            Assert.AreEqual(default(int), result);
        }

        [Test]
        public void TryPeekBackOnEmptyDequeReturnsFalse()
        {
            Deque<int> deque = new(10);
            Assert.IsFalse(deque.TryPeekBack(out int result));
            Assert.AreEqual(default(int), result);
        }

        // Clear Tests
        [Test]
        public void ClearRemovesAllElements()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            deque.Clear();

            Assert.AreEqual(0, deque.Count);
            Assert.IsTrue(deque.IsEmpty);
            Assert.IsFalse(deque.TryPeekFront(out _));
        }

        [Test]
        public void ClearOnEmptyDequeDoesNotThrow()
        {
            Deque<int> deque = new(10);
            Assert.DoesNotThrow(() => deque.Clear());
        }

        [Test]
        public void ClearWithWrappedBufferClearsCorrectly()
        {
            Deque<int> deque = new(4);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.TryPopFront(out _);
            deque.PushBack(3);
            deque.PushBack(4);
            deque.PushBack(5);

            deque.Clear();

            Assert.AreEqual(0, deque.Count);
            Assert.IsTrue(deque.IsEmpty);
        }

        // Contains Tests
        [Test]
        public void ContainsWithExistingElementReturnsTrue()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            Assert.IsTrue(deque.Contains(2));
        }

        [Test]
        public void ContainsWithNonExistingElementReturnsFalse()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);

            Assert.IsFalse(deque.Contains(99));
        }

        [Test]
        public void ContainsOnEmptyDequeReturnsFalse()
        {
            Deque<int> deque = new(10);
            Assert.IsFalse(deque.Contains(1));
        }

        [Test]
        public void ContainsWithNullElementWorksCorrectly()
        {
            Deque<string> deque = new(10);
            deque.PushBack("a");
            deque.PushBack(null);
            deque.PushBack("b");

            Assert.IsTrue(deque.Contains(null));
        }

        // CopyTo Tests
        [Test]
        public void CopyToCopiesAllElements()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            int[] array = new int[5];
            deque.CopyTo(array, 1);

            Assert.AreEqual(0, array[0]);
            Assert.AreEqual(1, array[1]);
            Assert.AreEqual(2, array[2]);
            Assert.AreEqual(3, array[3]);
            Assert.AreEqual(0, array[4]);
        }

        [Test]
        public void CopyToWithNullArrayThrowsException()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);

            Assert.Throws<ArgumentNullException>(() => deque.CopyTo(null, 0));
        }

        [Test]
        public void CopyToWithNegativeArrayIndexThrowsException()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            int[] array = new int[10];

            Assert.Throws<ArgumentOutOfRangeException>(() => deque.CopyTo(array, -1));
        }

        [Test]
        public void CopyToWithArrayIndexBeyondLengthThrowsException()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            int[] array = new int[5];

            Assert.Throws<ArgumentOutOfRangeException>(() => deque.CopyTo(array, 6));
        }

        [Test]
        public void CopyToWithInsufficientSpaceThrowsException()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);
            int[] array = new int[4];

            Assert.Throws<ArgumentException>(() => deque.CopyTo(array, 2));
        }

        // ToArray Tests
        [Test]
        public void ToArrayReturnsCorrectArray()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            int[] array = deque.ToArray();

            Assert.AreEqual(3, array.Length);
            Assert.AreEqual(1, array[0]);
            Assert.AreEqual(2, array[1]);
            Assert.AreEqual(3, array[2]);
        }

        [Test]
        public void ToArrayOnEmptyDequeReturnsEmptyArray()
        {
            Deque<int> deque = new(10);
            int[] array = deque.ToArray();

            Assert.AreEqual(0, array.Length);
        }

        [Test]
        public void ToArrayWithRefParameterReusesArray()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);

            int[] array = new int[10];
            int count = deque.ToArray(ref array);

            Assert.AreEqual(2, count);
            Assert.AreEqual(1, array[0]);
            Assert.AreEqual(2, array[1]);
        }

        [Test]
        public void ToArrayWithRefParameterAllocatesIfNull()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);

            int[] array = null;
            int count = deque.ToArray(ref array);

            Assert.AreEqual(2, count);
            Assert.IsTrue(array != null);
            Assert.AreEqual(2, array.Length);
        }

        [Test]
        public void ToArrayWithRefParameterAllocatesIfTooSmall()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            int[] array = new int[1];
            int count = deque.ToArray(ref array);

            Assert.AreEqual(3, count);
            Assert.AreEqual(3, array.Length);
        }

        // TrimExcess Tests
        [Test]
        public void TrimExcessReducesCapacity()
        {
            Deque<int> deque = new(100);
            deque.PushBack(1);
            deque.PushBack(2);

            deque.TrimExcess();

            Assert.AreEqual(2, deque.Count);
            Assert.IsTrue(deque.Capacity < 100);
        }

        [Test]
        public void TrimExcessDoesNotReduceBelowDefaultCapacity()
        {
            Deque<int> deque = new(100);
            deque.PushBack(1);

            deque.TrimExcess();

            Assert.AreEqual(Deque<int>.DefaultCapacity, deque.Capacity);
        }

        [Test]
        public void TrimExcessDoesNotTrimIfAboveThreshold()
        {
            Deque<int> deque = new(10);
            for (int i = 0; i < 10; i++)
            {
                deque.PushBack(i);
            }

            int capacityBefore = deque.Capacity;
            deque.TrimExcess();

            Assert.AreEqual(capacityBefore, deque.Capacity);
        }

        // Enumerator Tests
        [Test]
        public void GetEnumeratorIteratesInCorrectOrder()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);

            int[] expected = { 1, 2, 3 };
            int index = 0;

            foreach (int item in deque)
            {
                Assert.AreEqual(expected[index++], item);
            }

            Assert.AreEqual(3, index);
        }

        [Test]
        public void GetEnumeratorOnEmptyDequeDoesNotIterate()
        {
            Deque<int> deque = new(10);
            int count = 0;

            foreach (int _ in deque)
            {
                count++;
            }

            Assert.AreEqual(0, count);
        }

        [Test]
        public void GetEnumeratorWithWrappedBufferIteratesCorrectly()
        {
            Deque<int> deque = new(4);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.TryPopFront(out _);
            deque.TryPopFront(out _);
            deque.PushBack(3);
            deque.PushBack(4);
            deque.PushBack(5);

            int[] expected = { 3, 4, 5 };
            int index = 0;

            foreach (int item in deque)
            {
                Assert.AreEqual(expected[index++], item);
            }
        }

        [Test]
        public void GetEnumeratorResetRestartsEnumeration()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);

            using Deque<int>.DequeEnumerator enumerator = deque.GetEnumerator();
            enumerator.MoveNext();
            enumerator.MoveNext();

            enumerator.Reset();

            Assert.IsTrue(enumerator.MoveNext());
            Assert.AreEqual(1, enumerator.Current);
        }

        // PushFront Growth Tests
        [Test]
        public void PushFrontTriggersGrowth()
        {
            Deque<int> deque = new(2);
            deque.PushFront(1);
            deque.PushFront(2);

            int capacityBefore = deque.Capacity;
            deque.PushFront(3);

            Assert.Greater(deque.Capacity, capacityBefore);
            Assert.AreEqual(3, deque.Count);
            Assert.AreEqual(3, deque[0]);
            Assert.AreEqual(2, deque[1]);
            Assert.AreEqual(1, deque[2]);
        }

        // Mixed Operations Tests
        [Test]
        public void MixedPushOperationsMaintainCorrectOrder()
        {
            Deque<int> deque = new(10);
            deque.PushBack(3);
            deque.PushFront(2);
            deque.PushBack(4);
            deque.PushFront(1);
            deque.PushBack(5);

            int[] expected = { 1, 2, 3, 4, 5 };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], deque[i]);
            }
        }

        [Test]
        public void MixedPopOperationsWorkCorrectly()
        {
            Deque<int> deque = new(10);
            deque.PushBack(1);
            deque.PushBack(2);
            deque.PushBack(3);
            deque.PushBack(4);

            Assert.IsTrue(deque.TryPopFront(out int front1));
            Assert.AreEqual(1, front1);

            Assert.IsTrue(deque.TryPopBack(out int back1));
            Assert.AreEqual(4, back1);

            Assert.AreEqual(2, deque.Count);
            Assert.AreEqual(2, deque[0]);
            Assert.AreEqual(3, deque[1]);
        }

        [Test]
        public void AlternatingPushPopOperationsMaintainCorrectState()
        {
            Deque<int> deque = new(10);

            deque.PushBack(1);
            deque.PushFront(0);
            Assert.IsTrue(deque.TryPopBack(out int val1));
            Assert.AreEqual(1, val1);

            deque.PushBack(2);
            Assert.IsTrue(deque.TryPopFront(out int val2));
            Assert.AreEqual(0, val2);

            Assert.AreEqual(1, deque.Count);
            Assert.AreEqual(2, deque[0]);
        }

        // Large Scale Tests
        [Test]
        public void LargeNumberOfPushBackOperationsWorksCorrectly()
        {
            Deque<int> deque = new(10);

            for (int i = 0; i < 1000; i++)
            {
                deque.PushBack(i);
            }

            Assert.AreEqual(1000, deque.Count);
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(i, deque[i]);
            }
        }

        [Test]
        public void LargeNumberOfPushFrontOperationsWorksCorrectly()
        {
            Deque<int> deque = new(10);

            for (int i = 0; i < 1000; i++)
            {
                deque.PushFront(i);
            }

            Assert.AreEqual(1000, deque.Count);
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(999 - i, deque[i]);
            }
        }

        // Edge Case: Single Element
        [Test]
        public void SingleElementAllOperationsWorkCorrectly()
        {
            Deque<int> deque = new(10);
            deque.PushBack(42);

            Assert.AreEqual(1, deque.Count);
            Assert.AreEqual(42, deque[0]);
            Assert.IsTrue(deque.TryPeekFront(out int front));
            Assert.AreEqual(42, front);
            Assert.IsTrue(deque.TryPeekBack(out int back));
            Assert.AreEqual(42, back);
            Assert.IsTrue(deque.Contains(42));

            Assert.IsTrue(deque.TryPopFront(out int popped));
            Assert.AreEqual(42, popped);
            Assert.IsTrue(deque.IsEmpty);
        }

        // Edge Case: Capacity 1
        [Test]
        public void Capacity1GrowsWhenNeeded()
        {
            Deque<int> deque = new(1);
            deque.PushBack(1);
            deque.PushBack(2);

            Assert.AreEqual(2, deque.Count);
            Assert.AreEqual(1, deque[0]);
            Assert.AreEqual(2, deque[1]);
        }

        // Reference Type Tests
        [Test]
        public void WorksWithReferenceTypes()
        {
            Deque<string> deque = new(10);
            deque.PushBack("first");
            deque.PushFront("zero");
            deque.PushBack("second");

            Assert.AreEqual(3, deque.Count);
            Assert.AreEqual("zero", deque[0]);
            Assert.AreEqual("first", deque[1]);
            Assert.AreEqual("second", deque[2]);

            deque.Clear();
            Assert.AreEqual(0, deque.Count);
        }

        // Stress Test: Circular Buffer Wrapping
        [Test]
        public void ExtensiveWrappingMaintainsCorrectState()
        {
            Deque<int> deque = new(8);

            for (int i = 0; i < 100; i++)
            {
                deque.PushBack(i);
                if (i % 3 == 0)
                {
                    deque.TryPopFront(out _);
                }
                if (i % 5 == 0)
                {
                    deque.PushFront(-i);
                }
            }

            Assert.Greater(deque.Count, 0);

            int[] array = deque.ToArray();
            Assert.AreEqual(deque.Count, array.Length);

            for (int i = 0; i < array.Length; i++)
            {
                Assert.AreEqual(array[i], deque[i]);
            }
        }
    }
}
