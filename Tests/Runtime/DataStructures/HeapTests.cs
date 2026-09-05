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
    public sealed class MinHeapTests
    {
        [Test]
        public void ConstructorWithDefaultCapacityCreatesEmptyHeap()
        {
            Heap<int> heap = new();
            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void ConstructorWithSpecificCapacityCreatesEmptyHeap()
        {
            Heap<int> heap = new(32);
            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
            Assert.AreEqual(32, heap.Capacity);
        }

        [Test]
        public void ConstructorWithZeroCapacityThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Heap<int>(0));
        }

        [Test]
        public void ConstructorWithNegativeCapacityThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Heap<int>(-1));
        }

        [Test]
        public void ConstructorWithNullComparerDoesNotThrow()
        {
            _ = new Heap<int>(null, 16);
            Assert.Pass("Does not throw.");
        }

        [Test]
        public void ConstructorWithNullCollectionThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new Heap<int>((IEnumerable<int>)null));
        }

        [Test]
        public void ConstructorWithCollectionHeapifiesCorrectly()
        {
            int[] items = { 5, 3, 7, 1, 9, 2, 8 };
            Heap<int> heap = new(items);

            Assert.AreEqual(items.Length, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);
        }

        [Test]
        public void ConstructorWithEmptyCollectionCreatesEmptyHeap()
        {
            Heap<int> heap = new(Array.Empty<int>());
            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void CreateMinHeapCreatesMinHeap()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();
            heap.Add(5);
            heap.Add(3);
            heap.Add(7);

            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(3, result);
        }

        [Test]
        public void CreateMinHeapFromCollectionCreatesMinHeap()
        {
            int[] items = { 5, 3, 7, 1, 9 };
            Heap<int> heap = Heap<int>.CreateMinHeap(items);

            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);
            Assert.AreEqual(5, heap.Count);
        }

        [Test]
        public void AddInsertsElementsCorrectly()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();
            heap.Add(5);
            heap.Add(3);
            heap.Add(7);
            heap.Add(1);

            Assert.AreEqual(4, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);
        }

        [Test]
        public void AddMaintainsMinHeapProperty()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();
            int[] values = { 10, 20, 5, 15, 30, 25, 3 };

            foreach (int value in values)
            {
                heap.Add(value);
            }

            List<int> sorted = new();
            while (heap.TryPop(out int result))
            {
                sorted.Add(result);
            }

            CollectionAssert.AreEqual(values.OrderBy(x => x).ToList(), sorted);
        }

        [Test]
        public void AddTriggersResize()
        {
            Heap<int> heap = new(2) { 1, 2 };

            Assert.AreEqual(2, heap.Capacity);

            heap.Add(3);

            Assert.IsTrue(2 < heap.Capacity);
            Assert.AreEqual(3, heap.Count);
        }

        [Test]
        public void PeekReturnsMinElementWithoutRemoving()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            Assert.IsTrue(heap.TryPeek(out int result));

            Assert.AreEqual(1, result);
            Assert.AreEqual(4, heap.Count);
        }

        [Test]
        public void TryPeekReturnsTrueWhenNotEmpty()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            bool success = heap.TryPeek(out int result);

            Assert.IsTrue(success);
            Assert.AreEqual(3, result);
            Assert.AreEqual(3, heap.Count);
        }

        [Test]
        public void TryPeekReturnsFalseWhenEmpty()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            bool success = heap.TryPeek(out int result);

            Assert.IsFalse(success);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void TryGetReturnsTrueForValidIndex()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            bool success = heap.TryGet(0, out int result);

            Assert.IsTrue(success);
            Assert.AreEqual(3, result);
        }

        [Test]
        public void TryGetReturnsFalseForInvalidIndex()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            bool success1 = heap.TryGet(-1, out int result1);
            bool success2 = heap.TryGet(10, out int result2);

            Assert.IsFalse(success1);
            Assert.IsFalse(success2);
            Assert.AreEqual(0, result1);
            Assert.AreEqual(0, result2);
        }

        [Test]
        public void PopRemovesAndReturnsMinElement()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            Assert.IsTrue(heap.TryPop(out int result));

            Assert.AreEqual(1, result);
            Assert.AreEqual(3, heap.Count);
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(3, result);
        }

        [Test]
        public void PopMaintainsHeapProperty()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1, 9, 2, 8 });

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 5, 7, 8, 9 }, results);
        }

        [Test]
        public void TryPopReturnsTrueWhenNotEmpty()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            bool success = heap.TryPop(out int result);

            Assert.IsTrue(success);
            Assert.AreEqual(3, result);
            Assert.AreEqual(2, heap.Count);
        }

        [Test]
        public void TryPopReturnsFalseWhenEmpty()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            bool success = heap.TryPop(out int result);

            Assert.IsFalse(success);
            Assert.AreEqual(0, result);
        }

        [Test]
        public void TryPopMultipleTimes()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1, 9 });

            Assert.IsTrue(heap.TryPop(out int first));
            Assert.AreEqual(1, first);

            Assert.IsTrue(heap.TryPop(out int second));
            Assert.AreEqual(3, second);

            Assert.IsTrue(heap.TryPop(out int third));
            Assert.AreEqual(5, third);

            Assert.AreEqual(2, heap.Count);
        }

        [Test]
        public void ClearRemovesAllElements()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            heap.Clear();

            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
            Assert.IsFalse(heap.TryPeek(out _));
        }

        [Test]
        public void ContainsReturnsTrueForExistingElement()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            Assert.IsTrue(heap.Contains(3));
            Assert.IsTrue(heap.Contains(7));
        }

        [Test]
        public void ContainsReturnsFalseForNonExistingElement()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            Assert.IsFalse(heap.Contains(10));
        }

        [Test]
        public void ContainsReturnsFalseWhenEmpty()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            Assert.IsFalse(heap.Contains(5));
        }

        [Test]
        public void CopyToCopiesToArray()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });
            int[] array = new int[6];

            heap.CopyTo(array, 1);

            Assert.AreEqual(0, array[0]);
            Assert.AreEqual(4, array.Skip(1).Take(4).Count(x => x != 0));
        }

        [Test]
        public void CopyToThrowsWhenArrayIsNull()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            Assert.Throws<ArgumentNullException>(() => heap.CopyTo(null, 0));
        }

        [Test]
        public void CopyToThrowsWhenArrayIndexIsNegative()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });
            int[] array = new int[10];

            Assert.Throws<ArgumentOutOfRangeException>(() => heap.CopyTo(array, -1));
        }

        [Test]
        public void CopyToThrowsWhenArrayIsTooSmall()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });
            int[] array = new int[3];

            Assert.Throws<ArgumentException>(() => heap.CopyTo(array, 0));
        }

        [Test]
        public void ToArrayReturnsAllElements()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            int[] array = heap.ToArray();

            Assert.AreEqual(4, array.Length);
            CollectionAssert.AreEquivalent(new[] { 5, 3, 7, 1 }, array);
        }

        [Test]
        public void ToArrayReturnsEmptyArrayWhenEmpty()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            int[] array = heap.ToArray();

            Assert.AreEqual(0, array.Length);
        }

        [Test]
        public void TrimExcessReducesCapacity()
        {
            Heap<int> heap = new(100) { 1, 2, 3 };

            int capacityBefore = heap.Capacity;
            heap.TrimExcess();
            int capacityAfter = heap.Capacity;

            Assert.Less(capacityAfter, capacityBefore);
            Assert.AreEqual(3, heap.Count);
        }

        [Test]
        public void TrimExcessDoesNotReduceWhenNearCapacity()
        {
            Heap<int> heap = new(10);
            for (int i = 0; i < 9; i++)
            {
                heap.Add(i);
            }

            int capacityBefore = heap.Capacity;
            heap.TrimExcess();

            Assert.AreEqual(capacityBefore, heap.Capacity);
        }

        [Test]
        public void GetEnumeratorReturnsAllElements()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            List<int> results = new();
            foreach (int item in heap)
            {
                results.Add(item);
            }

            Assert.AreEqual(4, results.Count);
            CollectionAssert.AreEquivalent(new[] { 5, 3, 7, 1 }, results);
        }

        [Test]
        public void HeapHandlesDuplicateElements()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 3, 7, 1, 1 });

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(new[] { 1, 1, 3, 3, 5, 7 }, results);
        }

        [Test]
        public void HeapHandlesSingleElement()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();
            heap.Add(42);

            Assert.AreEqual(1, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(42, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(42, result);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void HeapHandlesLargeNumberOfElements()
        {
            int[] items = Enumerable.Range(0, 10000).Reverse().ToArray();
            Heap<int> heap = Heap<int>.CreateMinHeap(items);

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(Enumerable.Range(0, 10000).ToList(), results);
        }

        [Test]
        public void HeapHandlesCustomComparerForMinHeap()
        {
            IComparer<int> comparer = Comparer<int>.Default;
            Heap<int> heap = new(comparer) { 5, 3, 7 };

            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(3, result);
        }

        [Test]
        public void HeapHandlesReferenceTypes()
        {
            Heap<string> heap = Heap<string>.CreateMinHeap();
            heap.Add("banana");
            heap.Add("apple");
            heap.Add("cherry");

            Assert.IsTrue(heap.TryPeek(out string result));
            Assert.AreEqual("apple", result);

            List<string> results = new();
            while (heap.TryPop(out string item))
            {
                results.Add(item);
            }

            CollectionAssert.AreEqual(new[] { "apple", "banana", "cherry" }, results);
        }

        [Test]
        public void HeapMaintainsHeapPropertyAfterMultipleOperations()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            heap.Add(10);
            heap.Add(5);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(5, result);

            heap.Add(3);
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(3, result);

            Assert.IsTrue(heap.TryPop(out _));
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(5, result);

            heap.Add(1);
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(1, result);

            Assert.IsTrue(heap.TryPop(out _));
            Assert.IsTrue(heap.TryPop(out _));
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(10, result);
        }

        [Test]
        public void HeapSupportsNullableValueTypes()
        {
            Heap<int?> heap = Heap<int?>.CreateMinHeap();
            heap.Add(5);
            heap.Add(null);
            heap.Add(3);

            // Null should be treated as minimum by default comparer
            Assert.IsTrue(heap.TryPop(out int? result));
            Assert.AreEqual(null, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(3, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(5, result);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class MaxHeapTests
    {
        [Test]
        public void CreateMaxHeapCreatesMaxHeap()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap();
            heap.Add(5);
            heap.Add(3);
            heap.Add(7);

            Assert.IsTrue(heap.TryPop(out int result));
            Assert.AreEqual(7, result);
        }

        [Test]
        public void CreateMaxHeapFromCollectionCreatesMaxHeap()
        {
            int[] items = { 5, 3, 7, 1, 9 };
            Heap<int> heap = Heap<int>.CreateMaxHeap(items);

            Assert.IsTrue(heap.TryPop(out int result));
            Assert.AreEqual(9, result);
            Assert.AreEqual(4, heap.Count);
        }

        [Test]
        public void MaxHeapAddMaintainsMaxHeapProperty()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap();
            int[] values = { 10, 20, 5, 15, 30, 25, 3 };

            foreach (int value in values)
            {
                heap.Add(value);
            }

            List<int> sorted = new();
            while (heap.TryPop(out int result))
            {
                sorted.Add(result);
            }

            CollectionAssert.AreEqual(values.OrderByDescending(x => x).ToList(), sorted);
        }

        [Test]
        public void MaxHeapPeekReturnsMaxElement()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap(new[] { 5, 3, 7, 1 });

            Assert.IsTrue(heap.TryPop(out int result));
            Assert.AreEqual(7, result);
            Assert.AreEqual(3, heap.Count);
        }

        [Test]
        public void MaxHeapPopRemovesAndReturnsMaxElement()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap(new[] { 5, 3, 7, 1 });

            Assert.IsTrue(heap.TryPop(out int result));

            Assert.AreEqual(7, result);
            Assert.AreEqual(3, heap.Count);
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(5, result);
        }

        [Test]
        public void MaxHeapPopMaintainsHeapProperty()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap(new[] { 5, 3, 7, 1, 9, 2, 8 });

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(new[] { 9, 8, 7, 5, 3, 2, 1 }, results);
        }

        [Test]
        public void MaxHeapHandlesDuplicateElements()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap(new[] { 5, 3, 3, 7, 7, 1 });

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(new[] { 7, 7, 5, 3, 3, 1 }, results);
        }

        [Test]
        public void MaxHeapHandlesSingleElement()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap();
            heap.Add(42);

            Assert.AreEqual(1, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(42, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(42, result);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void MaxHeapHandlesLargeNumberOfElements()
        {
            int[] items = Enumerable.Range(0, 10000).ToArray();
            Heap<int> heap = Heap<int>.CreateMaxHeap(items);

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(Enumerable.Range(0, 10000).Reverse().ToList(), results);
        }

        [Test]
        public void MaxHeapHandlesReferenceTypes()
        {
            Heap<string> heap = Heap<string>.CreateMaxHeap();
            heap.Add("banana");
            heap.Add("apple");
            heap.Add("cherry");

            Assert.IsTrue(heap.TryPeek(out string item));
            Assert.AreEqual("cherry", item);

            List<string> results = new();
            while (heap.TryPop(out string result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(new[] { "cherry", "banana", "apple" }, results);
        }

        [Test]
        public void MaxHeapMaintainsHeapPropertyAfterMultipleOperations()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap();

            heap.Add(10);
            heap.Add(15);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(15, result);

            heap.Add(20);
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(20, result);

            Assert.IsTrue(heap.TryPop(out _));
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(15, result);

            heap.Add(25);
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(25, result);

            Assert.IsTrue(heap.TryPop(out _));
            Assert.IsTrue(heap.TryPop(out _));
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(10, result);
        }

        [Test]
        public void MaxHeapContainsWorksCorrectly()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap(new[] { 5, 3, 7, 1 });

            Assert.IsTrue(heap.Contains(7));
            Assert.IsTrue(heap.Contains(3));
            Assert.IsFalse(heap.Contains(10));
        }

        [Test]
        public void MaxHeapClearRemovesAllElements()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap(new[] { 5, 3, 7, 1 });

            heap.Clear();

            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class CustomComparerHeapTests
    {
        [Test]
        public void HeapWorksWithCustomComparer()
        {
            IComparer<Person> comparer = new PersonAgeComparer();
            Heap<Person> heap = new(comparer)
            {
                new Person("Alice", 30),
                new Person("Bob", 25),
                new Person("Charlie", 35),
            };

            Assert.IsTrue(heap.TryPeek(out Person youngest));
            Assert.AreEqual("Bob", youngest.Name);
            Assert.AreEqual(25, youngest.Age);
        }

        [Test]
        public void HeapWithCustomComparerMaintainsOrder()
        {
            IComparer<Person> comparer = new PersonAgeComparer();
            Person[] people =
            {
                new("Alice", 30),
                new("Bob", 25),
                new("Charlie", 35),
                new("David", 20),
            };
            Heap<Person> heap = new(people, comparer);

            List<string> names = new();
            while (heap.TryPop(out Person person))
            {
                names.Add(person.Name);
            }

            CollectionAssert.AreEqual(new[] { "David", "Bob", "Alice", "Charlie" }, names);
        }

        [Test]
        public void HeapWithReverseCustomComparerCreatesMaxHeap()
        {
            IComparer<Person> reverseComparer = Comparer<Person>.Create(
                (x, y) => new PersonAgeComparer().Compare(y, x)
            );
            Heap<Person> heap = new(reverseComparer)
            {
                new Person("Alice", 30),
                new Person("Bob", 25),
                new Person("Charlie", 35),
            };
            Assert.IsTrue(heap.TryPeek(out Person oldest));
            Assert.AreEqual("Charlie", oldest.Name);
            Assert.AreEqual(35, oldest.Age);
        }

        private class Person
        {
            public string Name { get; set; }
            public int Age { get; set; }

            public Person(string name, int age)
            {
                Name = name;
                Age = age;
            }
        }

        private class PersonAgeComparer : IComparer<Person>
        {
            public int Compare(Person x, Person y)
            {
                if (x == null && y == null)
                {
                    return 0;
                }

                if (x == null)
                {
                    return -1;
                }

                if (y == null)
                {
                    return 1;
                }

                return x.Age.CompareTo(y.Age);
            }
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class HeapEdgeCaseTests
    {
        [Test]
        public void HeapHandlesAlternatingAddAndPop()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            heap.Add(5);
            Assert.IsTrue(heap.TryPop(out int result));
            Assert.AreEqual(5, result);

            heap.Add(3);
            heap.Add(7);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(3, result);

            heap.Add(1);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(1, result);
        }

        [Test]
        public void HeapHandlesAllSameElements()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 5, 5, 5, 5 });

            Assert.AreEqual(5, heap.Count);
            while (heap.TryPop(out int result))
            {
                Assert.AreEqual(5, result);
            }
        }

        [Test]
        public void HeapHandlesAlreadySortedAscending()
        {
            int[] sorted = Enumerable.Range(1, 100).ToArray();
            Heap<int> heap = Heap<int>.CreateMinHeap(sorted);

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(sorted, results);
        }

        [Test]
        public void HeapHandlesAlreadySortedDescending()
        {
            int[] sorted = Enumerable.Range(1, 100).Reverse().ToArray();
            Heap<int> heap = Heap<int>.CreateMinHeap(sorted);

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(sorted.OrderBy(x => x).ToList(), results);
        }

        [Test]
        public void MaxHeapHandlesAlreadySortedAscending()
        {
            int[] sorted = Enumerable.Range(1, 100).ToArray();
            Heap<int> heap = Heap<int>.CreateMaxHeap(sorted);

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(sorted.Reverse().ToList(), results);
        }

        [Test]
        public void MaxHeapHandlesAlreadySortedDescending()
        {
            int[] sorted = Enumerable.Range(1, 100).Reverse().ToArray();
            Heap<int> heap = Heap<int>.CreateMaxHeap(sorted);

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(sorted, results);
        }

        [Test]
        public void HeapHandlesIntMinAndMax()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();
            heap.Add(int.MaxValue);
            heap.Add(int.MinValue);
            heap.Add(0);

            Assert.IsTrue(heap.TryPop(out int result));
            Assert.AreEqual(int.MinValue, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(0, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(int.MaxValue, result);
        }

        [Test]
        public void HeapHandlesDoubleNaN()
        {
            Heap<double> heap = Heap<double>.CreateMinHeap();
            heap.Add(5.0);
            heap.Add(double.NaN);
            heap.Add(3.0);

            Assert.AreEqual(3, heap.Count);
        }

        [Test]
        public void HeapHandlesDoubleInfinity()
        {
            Heap<double> heap = Heap<double>.CreateMinHeap();
            heap.Add(5.0);
            heap.Add(double.NegativeInfinity);
            heap.Add(double.PositiveInfinity);
            heap.Add(0.0);

            Assert.IsTrue(heap.TryPop(out double result));
            Assert.AreEqual(double.NegativeInfinity, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(0.0, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(5.0, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(double.PositiveInfinity, result);
        }

        [Test]
        public void HeapHandlesRepeatedClearAndAdd()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            for (int i = 0; i < 5; i++)
            {
                heap.Add(i);
                heap.Add(i + 10);
                heap.Clear();
            }

            Assert.IsTrue(heap.IsEmpty);

            heap.Add(42);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(42, result);
        }

        [Test]
        public void HeapToArrayDoesNotAffectHeap()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            int[] array1 = heap.ToArray();
            int[] array2 = heap.ToArray();

            Assert.AreEqual(4, heap.Count);
            Assert.AreEqual(4, array1.Length);
            Assert.AreEqual(4, array2.Length);
        }

        [Test]
        public void HeapEnumerationDoesNotModifyHeap()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            int count = 0;
            foreach (int _ in heap)
            {
                count++;
            }

            Assert.AreEqual(4, count);
            Assert.AreEqual(4, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class HeapUpdatePriorityTests
    {
        [Test]
        public void UpdatePriorityIncreasePriorityBubblesUp()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30, 40, 50 });

            int index = -1;
            for (int i = 0; i < heap.Count; i++)
            {
                if (heap[i] == 40)
                {
                    index = i;
                    break;
                }
            }

            Assert.IsTrue(heap.TryUpdatePriority(index, 5));
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(5, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(5, result);
        }

        [Test]
        public void UpdatePriorityDecreasePriorityBubblesDown()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30, 40, 50 });

            Assert.IsTrue(heap.TryUpdatePriority(0, 45));
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(20, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(20, result);
        }

        [Test]
        public void UpdatePrioritySameValueDoesNothing()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30 });

            Assert.IsTrue(heap.TryPeek(out int peekBefore));
            Assert.IsTrue(heap.TryUpdatePriority(0, 10));

            Assert.IsTrue(heap.TryPeek(out int peekAfter));
            Assert.AreEqual(peekBefore, peekAfter);
            Assert.AreEqual(3, heap.Count);
        }

        [Test]
        public void UpdatePriorityThrowsOnInvalidIndex()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30 });

            Assert.IsFalse(heap.TryUpdatePriority(-1, 5));
            Assert.IsFalse(heap.TryUpdatePriority(3, 5));
            Assert.IsFalse(heap.TryUpdatePriority(100, 5));
        }

        [Test]
        public void UpdatePriorityOnEmptyHeapThrows()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            Assert.IsFalse(heap.TryUpdatePriority(0, 5));
        }

        [Test]
        public void UpdatePriorityMaintainsHeapProperty()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 10, 15, 20, 25, 30 });

            Assert.IsTrue(heap.TryUpdatePriority(0, 100));
            Assert.IsTrue(heap.TryUpdatePriority(1, 3));

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(new[] { 3, 10, 15, 25, 30, 100 }, results);
        }

        [Test]
        public void UpdatePriorityWorksWithMaxHeap()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap(new[] { 50, 40, 30, 20, 10 });

            int index = -1;
            for (int i = 0; i < heap.Count; i++)
            {
                if (heap[i] == 20)
                {
                    index = i;
                    break;
                }
            }

            Assert.IsTrue(heap.TryUpdatePriority(index, 60));
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(60, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(60, result);
        }

        [Test]
        public void UpdatePriorityOnSingleElementHeap()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();
            heap.Add(42);

            Assert.IsTrue(heap.TryUpdatePriority(0, 100));
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(100, result);
            Assert.AreEqual(1, heap.Count);
        }

        [Test]
        public void TryUpdatePriorityReturnsTrueOnSuccess()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30 });

            bool success = heap.TryUpdatePriority(0, 5);

            Assert.IsTrue(success);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(5, result);
        }

        [Test]
        public void TryUpdatePriorityReturnsFalseOnInvalidIndex()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30 });

            bool success1 = heap.TryUpdatePriority(-1, 5);
            bool success2 = heap.TryUpdatePriority(3, 5);

            Assert.IsFalse(success1);
            Assert.IsFalse(success2);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(10, result);
        }

        [Test]
        public void TryUpdatePriorityReturnsFalseOnEmptyHeap()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            bool success = heap.TryUpdatePriority(0, 5);
            Assert.IsFalse(success);
        }

        [Test]
        public void UpdatePriorityWorksWithDuplicateValues()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 10, 10, 10 });

            Assert.IsTrue(heap.TryUpdatePriority(0, 5));
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(5, result);
            Assert.IsTrue(heap.TryPop(out _));
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(10, result);
        }

        [Test]
        public void UpdatePriorityStressTest()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(Enumerable.Range(0, 1000).ToArray());

            for (int i = 0; i < 1000; i += 10)
            {
                Assert.IsTrue(heap.TryUpdatePriority(i, -i));
            }

            Assert.IsTrue(heap.TryPop(out int first));
            Assert.Less(first, 0);
        }

        [Test]
        public void UpdatePriorityWithCustomComparerIncrease()
        {
            Heap<string> heap = Heap<string>.CreateMinHeap(
                new[] { "apple", "banana", "cherry", "date" }
            );

            int index = -1;
            for (int i = 0; i < heap.Count; i++)
            {
                if (heap[i] == "date")
                {
                    index = i;
                    break;
                }
            }

            Assert.IsTrue(heap.TryUpdatePriority(index, "aaa"));
            Assert.IsTrue(heap.TryPeek(out string result));
            Assert.AreEqual("aaa", result);
        }

        [Test]
        public void UpdatePriorityWithCustomComparerDecrease()
        {
            Heap<string> heap = Heap<string>.CreateMinHeap(new[] { "apple", "banana", "cherry" });

            Assert.IsTrue(heap.TryUpdatePriority(0, "zebra"));
            Assert.IsTrue(heap.TryPeek(out string result));
            Assert.AreEqual("banana", result);
        }

        [Test]
        public void UpdatePriorityOnLastElement()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30, 40, 50 });

            int lastIndex = heap.Count - 1;
            Assert.IsTrue(heap.TryUpdatePriority(lastIndex, 5));
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(5, result);
        }

        [Test]
        public void UpdatePriorityMultipleTimesOnSameIndex()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30 });

            Assert.IsTrue(heap.TryUpdatePriority(0, 25));
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(20, result);

            Assert.IsTrue(heap.TryUpdatePriority(0, 5));
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(5, result);

            Assert.IsTrue(heap.TryUpdatePriority(0, 15));
            Assert.IsTrue(heap.TryPeek(out result));
            Assert.AreEqual(15, result);
        }

        [Test]
        public void UpdatePriorityAtMiddleIndex()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30, 40, 50, 60, 70 });

            Assert.IsTrue(heap.TryUpdatePriority(3, 5));
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(5, result);

            heap = Heap<int>.CreateMinHeap(new[] { 10, 20, 30, 40, 50, 60, 70 });
            Assert.IsTrue(heap.TryUpdatePriority(2, 65));

            List<int> results = new();
            while (heap.TryPop(out int item))
            {
                results.Add(item);
            }

            Assert.IsTrue(results.Contains(65));
            Assert.AreEqual(results.Last(), 70);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class HeapIndexerAndArrayTests
    {
        [Test]
        public void IndexerGetReturnsCorrectElement()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            int element = heap[0];
            Assert.AreEqual(1, element);
        }

        [Test]
        public void IndexerThrowsOnNegativeIndex()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            Assert.Throws<IndexOutOfRangeException>(() => _ = heap[-1]);
        }

        [Test]
        public void IndexerThrowsOnIndexEqualToCount()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            Assert.Throws<IndexOutOfRangeException>(() => _ = heap[3]);
        }

        [Test]
        public void IndexerThrowsOnIndexGreaterThanCount()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            Assert.Throws<IndexOutOfRangeException>(() => _ = heap[100]);
        }

        [Test]
        public void IndexerThrowsOnEmptyHeap()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            Assert.Throws<IndexOutOfRangeException>(() => _ = heap[0]);
        }

        [Test]
        public void ToArrayWithRefParameterReusesArray()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });
            int[] array = new int[10];

            int count = heap.ToArray(ref array);

            Assert.AreEqual(4, count);
            Assert.AreEqual(10, array.Length);
            CollectionAssert.AreEquivalent(new[] { 5, 3, 7, 1 }, array.Take(4));
        }

        [Test]
        public void ToArrayWithRefParameterCreatesNewArrayWhenNull()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });
            int[] array = null;

            int count = heap.ToArray(ref array);

            Assert.AreEqual(4, count);
            Assert.IsTrue(array != null);
            Assert.AreEqual(4, array.Length);
            CollectionAssert.AreEquivalent(new[] { 5, 3, 7, 1 }, array);
        }

        [Test]
        public void ToArrayWithRefParameterCreatesNewArrayWhenTooSmall()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });
            int[] array = new int[2];

            int count = heap.ToArray(ref array);

            Assert.AreEqual(4, count);
            Assert.AreEqual(4, array.Length);
            CollectionAssert.AreEquivalent(new[] { 5, 3, 7, 1 }, array);
        }

        [Test]
        public void ToArrayWithRefParameterHandlesEmptyHeap()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();
            int[] array = new int[5];

            int count = heap.ToArray(ref array);

            Assert.AreEqual(0, count);
            Assert.AreEqual(5, array.Length);
        }

        [Test]
        public void CopyToThrowsWhenArrayIndexEqualsArrayLength()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });
            int[] array = new int[5];

            Assert.Throws<ArgumentException>(() => heap.CopyTo(array, 5));
        }

        [Test]
        public void CopyToThrowsWhenArrayIndexGreaterThanArrayLength()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });
            int[] array = new int[5];

            Assert.Throws<ArgumentOutOfRangeException>(() => heap.CopyTo(array, 10));
        }

        [Test]
        public void CopyToWithEmptyHeapDoesNotThrow()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();
            int[] array = new int[5];

            heap.CopyTo(array, 0);

            Assert.Pass("No exception thrown");
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class HeapEnumeratorTests
    {
        [Test]
        public void EnumeratorResetAllowsReEnumeration()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            using Heap<int>.HeapEnumerator enumerator = heap.GetEnumerator();

            List<int> firstPass = new();
            while (enumerator.MoveNext())
            {
                firstPass.Add(enumerator.Current);
            }

            enumerator.Reset();

            List<int> secondPass = new();
            while (enumerator.MoveNext())
            {
                secondPass.Add(enumerator.Current);
            }

            CollectionAssert.AreEqual(firstPass, secondPass);
            Assert.AreEqual(3, firstPass.Count);
        }

        [Test]
        public void EnumeratorCurrentDefaultBeforeMoveNext()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            using Heap<int>.HeapEnumerator enumerator = heap.GetEnumerator();

            Assert.AreEqual(default(int), enumerator.Current);
        }

        [Test]
        public void EnumeratorCurrentDefaultAfterLastElement()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5 });

            using Heap<int>.HeapEnumerator enumerator = heap.GetEnumerator();
            enumerator.MoveNext();
            enumerator.MoveNext();

            Assert.AreEqual(default(int), enumerator.Current);
        }

        [Test]
        public void MultipleEnumeratorsAreIndependent()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            using Heap<int>.HeapEnumerator enumerator1 = heap.GetEnumerator();
            using Heap<int>.HeapEnumerator enumerator2 = heap.GetEnumerator();

            enumerator1.MoveNext();
            int first1 = enumerator1.Current;
            enumerator1.MoveNext();
            int second1 = enumerator1.Current;

            enumerator2.MoveNext();
            int first2 = enumerator2.Current;

            Assert.AreEqual(first1, first2);
            Assert.AreNotEqual(second1, first2);
        }

        [Test]
        public void EnumeratorDisposeDoesNotThrow()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            Heap<int>.HeapEnumerator enumerator = heap.GetEnumerator();
            enumerator.MoveNext();

            Assert.DoesNotThrow(() => enumerator.Dispose());
        }

        [Test]
        public void NonGenericEnumeratorWorks()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            System.Collections.IEnumerable nonGeneric = heap;
            List<object> results = new();

            foreach (object item in nonGeneric)
            {
                results.Add(item);
            }

            Assert.AreEqual(4, results.Count);
            CollectionAssert.AreEquivalent(new object[] { 5, 3, 7, 1 }, results);
        }

        [Test]
        public void EnumeratorWorksOnEmptyHeap()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            using Heap<int>.HeapEnumerator enumerator = heap.GetEnumerator();

            Assert.IsFalse(enumerator.MoveNext());
            Assert.AreEqual(default(int), enumerator.Current);
        }

        [Test]
        public void EnumeratorResetWorksOnEmptyHeap()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            using Heap<int>.HeapEnumerator enumerator = heap.GetEnumerator();
            enumerator.Reset();

            Assert.IsFalse(enumerator.MoveNext());
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class HeapConstructorCollectionTests
    {
        [Test]
        public void ConstructorWithIReadOnlyListWorks()
        {
            IReadOnlyList<int> list = new List<int> { 5, 3, 7, 1 }.AsReadOnly();
            Heap<int> heap = new(list);

            Assert.AreEqual(4, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);
        }

        [Test]
        public void ConstructorWithICollectionWorks()
        {
            ICollection<int> collection = new List<int> { 5, 3, 7, 1 };
            Heap<int> heap = new(collection);

            Assert.AreEqual(4, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);
        }

        [Test]
        public void ConstructorWithIReadOnlyCollectionWorks()
        {
            IReadOnlyCollection<int> collection = new HashSet<int> { 5, 3, 7, 1 };
            Heap<int> heap = new(collection);

            Assert.AreEqual(4, heap.Count);
            Assert.IsTrue(heap.Contains(1));
            Assert.IsTrue(heap.Contains(3));
            Assert.IsTrue(heap.Contains(5));
            Assert.IsTrue(heap.Contains(7));
        }

        [Test]
        public void ConstructorWithPlainIEnumerableWorks()
        {
            IEnumerable<int> enumerable = GetNumbers();
            Heap<int> heap = new(enumerable);

            Assert.AreEqual(5, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);

            static IEnumerable<int> GetNumbers()
            {
                yield return 5;
                yield return 3;
                yield return 7;
                yield return 1;
                yield return 9;
            }
        }

        [Test]
        public void ConstructorWithIEnumerableTriggersGrowth()
        {
            IEnumerable<int> enumerable = GetManyNumbers();
            Heap<int> heap = new(enumerable);

            Assert.AreEqual(20, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(0, result);

            static IEnumerable<int> GetManyNumbers()
            {
                for (int i = 0; i < 20; i++)
                {
                    yield return i;
                }
            }
        }

        [Test]
        public void ConstructorWithEmptyIReadOnlyListCreatesEmptyHeap()
        {
            IReadOnlyList<int> list = new List<int>().AsReadOnly();
            Heap<int> heap = new(list);

            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void ConstructorWithEmptyICollectionCreatesEmptyHeap()
        {
            ICollection<int> collection = new List<int>();
            Heap<int> heap = new(collection);

            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void ConstructorWithEmptyIReadOnlyCollectionCreatesEmptyHeap()
        {
            IReadOnlyCollection<int> collection = new HashSet<int>();
            Heap<int> heap = new(collection);

            Assert.AreEqual(0, heap.Count);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void ConstructorWithSingleElementIReadOnlyList()
        {
            IReadOnlyList<int> list = new List<int> { 42 }.AsReadOnly();
            Heap<int> heap = new(list);

            Assert.AreEqual(1, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(42, result);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class HeapFactoryMethodTests
    {
        [Test]
        public void CreateMinHeapWithCapacityAndNullComparer()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(null, 32);

            Assert.AreEqual(0, heap.Count);
            Assert.AreEqual(32, heap.Capacity);

            heap.Add(5);
            heap.Add(3);
            heap.Add(7);

            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(3, result);
        }

        [Test]
        public void CreateMinHeapWithCapacityAndCustomComparer()
        {
            IComparer<int> comparer = Comparer<int>.Default;
            Heap<int> heap = Heap<int>.CreateMinHeap(comparer, 32);

            Assert.AreEqual(0, heap.Count);
            Assert.AreEqual(32, heap.Capacity);

            heap.Add(5);
            heap.Add(3);
            heap.Add(7);

            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(3, result);
        }

        [Test]
        public void CreateMaxHeapWithCapacityAndNullComparer()
        {
            Heap<int> heap = Heap<int>.CreateMaxHeap(null, 32);

            Assert.AreEqual(0, heap.Count);
            Assert.AreEqual(32, heap.Capacity);

            heap.Add(5);
            heap.Add(3);
            heap.Add(7);

            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(7, result);
        }

        [Test]
        public void CreateMaxHeapWithCapacityAndCustomComparer()
        {
            IComparer<int> comparer = Comparer<int>.Default;
            Heap<int> heap = Heap<int>.CreateMaxHeap(comparer, 32);

            Assert.AreEqual(0, heap.Count);
            Assert.AreEqual(32, heap.Capacity);

            heap.Add(5);
            heap.Add(3);
            heap.Add(7);

            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(7, result);
        }

        [Test]
        public void CreateMinHeapFromCollectionWithNullComparer()
        {
            int[] items = { 5, 3, 7, 1 };
            Heap<int> heap = Heap<int>.CreateMinHeap(items, null);

            Assert.AreEqual(4, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);
        }

        [Test]
        public void CreateMinHeapFromCollectionWithCustomComparer()
        {
            int[] items = { 5, 3, 7, 1 };
            IComparer<int> comparer = Comparer<int>.Default;
            Heap<int> heap = Heap<int>.CreateMinHeap(items, comparer);

            Assert.AreEqual(4, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);
        }

        [Test]
        public void CreateMaxHeapFromCollectionWithNullComparer()
        {
            int[] items = { 5, 3, 7, 1 };
            Heap<int> heap = Heap<int>.CreateMaxHeap(items, null);

            Assert.AreEqual(4, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(7, result);
        }

        [Test]
        public void CreateMaxHeapFromCollectionWithCustomComparer()
        {
            int[] items = { 5, 3, 7, 1 };
            IComparer<int> comparer = Comparer<int>.Default;
            Heap<int> heap = Heap<int>.CreateMaxHeap(items, comparer);

            Assert.AreEqual(4, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(7, result);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class HeapCapacityGrowthTests
    {
        [Test]
        public void AddToCapacity1TriggersGrowth()
        {
            Heap<int> heap = new(1) { 1 };

            Assert.AreEqual(1, heap.Capacity);

            heap.Add(2);

            Assert.IsTrue(1 < heap.Capacity);
            Assert.AreEqual(2, heap.Count);
        }

        [Test]
        public void MultipleGrowthsWork()
        {
            Heap<int> heap = new(2);

            for (int i = 0; i < 100; i++)
            {
                heap.Add(i);
            }

            Assert.AreEqual(100, heap.Count);
            Assert.IsTrue(100 <= heap.Capacity);
        }

        [Test]
        public void GrowthMaintainsHeapProperty()
        {
            Heap<int> heap = new(2);

            for (int i = 0; i < 50; i++)
            {
                heap.Add(50 - i);
            }

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(Enumerable.Range(1, 50).ToList(), results);
        }

        [Test]
        public void TrimExcessOnEmptyHeapMaintainsDefaultCapacity()
        {
            Heap<int> heap = new(100);
            heap.TrimExcess();

            Assert.IsTrue(16 <= heap.Capacity);
        }

        [Test]
        public void TrimExcessAfterAddAndClear()
        {
            Heap<int> heap = new(100) { 1, 2 };
            heap.Clear();
            heap.TrimExcess();

            Assert.IsTrue(16 <= heap.Capacity);
            Assert.AreEqual(0, heap.Count);
        }
    }

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class HeapCombinationTests
    {
        [Test]
        public void ClearThenAddThenPopWorks()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });
            heap.Clear();
            heap.Add(10);
            heap.Add(5);

            Assert.IsTrue(heap.TryPop(out int result));
            Assert.AreEqual(5, result);
            Assert.IsTrue(heap.TryPop(out result));
            Assert.AreEqual(10, result);
            Assert.IsTrue(heap.IsEmpty);
        }

        [Test]
        public void PopAllThenAddWorks()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            while (heap.TryPop(out _)) { }

            heap.Add(42);

            Assert.AreEqual(1, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(42, result);
        }

        [Test]
        public void UpdatePriorityThenClearThenAdd()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });

            heap.TryUpdatePriority(0, 10);
            heap.Clear();
            heap.Add(1);

            Assert.AreEqual(1, heap.Count);
            Assert.IsTrue(heap.TryPeek(out int result));
            Assert.AreEqual(1, result);
        }

        [Test]
        public void InterleavedOperationsMaintainCorrectness()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap();

            heap.Add(10);
            heap.Add(5);
            heap.TryPop(out _);
            heap.Add(15);
            heap.Add(3);
            heap.TryPop(out _);
            heap.Add(20);

            List<int> results = new();
            while (heap.TryPop(out int result))
            {
                results.Add(result);
            }

            CollectionAssert.AreEqual(new[] { 10, 15, 20 }, results);
        }

        [Test]
        public void ToArrayAfterUpdatePriority()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            heap.TryUpdatePriority(0, 10);

            int[] array = heap.ToArray();

            Assert.AreEqual(4, array.Length);
            CollectionAssert.Contains(array, 10);
            CollectionAssert.Contains(array, 3);
            CollectionAssert.Contains(array, 7);
            CollectionAssert.Contains(array, 5);
        }

        [Test]
        public void ContainsAfterMultipleOperations()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });

            heap.Add(9);
            heap.TryPop(out _);
            heap.TryUpdatePriority(0, 10);

            Assert.IsTrue(heap.Contains(10));
            Assert.IsFalse(heap.Contains(1));
            Assert.IsTrue(heap.Contains(9));
        }

        [Test]
        public void EnumerationAfterClearReturnsNothing()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7 });
            heap.Clear();

            int count = 0;
            foreach (int _ in heap)
            {
                count++;
            }

            Assert.AreEqual(0, count);
        }

        [Test]
        public void MultipleToArrayCallsWithRefParameter()
        {
            Heap<int> heap = Heap<int>.CreateMinHeap(new[] { 5, 3, 7, 1 });
            int[] array1 = new int[10];
            int[] array2 = new int[2];

            int count1 = heap.ToArray(ref array1);
            int count2 = heap.ToArray(ref array2);

            Assert.AreEqual(4, count1);
            Assert.AreEqual(4, count2);
            Assert.AreEqual(10, array1.Length);
            Assert.AreEqual(4, array2.Length);
        }
    }
}
