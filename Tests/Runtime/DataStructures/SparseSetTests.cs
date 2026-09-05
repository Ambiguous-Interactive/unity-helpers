// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SparseSetTests
    {
        [Test]
        public void ConstructorThrowsOnZeroUniverseSize()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SparseSet(0));
        }

        [Test]
        public void ConstructorThrowsOnNegativeUniverseSize()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SparseSet(-1));
        }

        [Test]
        public void ConstructorCreatesEmptySet()
        {
            SparseSet set = new(100);

            Assert.AreEqual(0, set.Count);
            Assert.IsTrue(set.IsEmpty);
            Assert.AreEqual(100, set.Capacity);
        }

        [Test]
        public void TryAddAddsElement()
        {
            SparseSet set = new(100);

            Assert.IsTrue(set.TryAdd(5));
            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.Contains(5));
        }

        [Test]
        public void TryAddReturnsFalseForDuplicate()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.IsFalse(set.TryAdd(5));
            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void TryAddReturnsFalseForNegativeValue()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.TryAdd(-1));
            Assert.AreEqual(0, set.Count);
        }

        [Test]
        public void TryAddReturnsFalseForValueEqualToCapacity()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.TryAdd(100));
            Assert.AreEqual(0, set.Count);
        }

        [Test]
        public void TryAddReturnsFalseForValueGreaterThanCapacity()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.TryAdd(101));
            Assert.AreEqual(0, set.Count);
        }

        [Test]
        public void TryAddWorksAtBoundaries()
        {
            SparseSet set = new(100);

            Assert.IsTrue(set.TryAdd(0));
            Assert.IsTrue(set.TryAdd(99));
            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains(0));
            Assert.IsTrue(set.Contains(99));
        }

        [Test]
        public void TryAddFillsEntireCapacity()
        {
            SparseSet set = new(10);

            for (int i = 0; i < 10; i++)
            {
                Assert.IsTrue(set.TryAdd(i));
            }

            Assert.AreEqual(10, set.Count);
        }

        [Test]
        public void TryRemoveRemovesElement()
        {
            SparseSet set = new(100);
            set.TryAdd(5);
            set.TryAdd(10);

            Assert.IsTrue(set.TryRemove(5));
            Assert.AreEqual(1, set.Count);
            Assert.IsFalse(set.Contains(5));
            Assert.IsTrue(set.Contains(10));
        }

        [Test]
        public void TryRemoveReturnsFalseForNonExistentElement()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.IsFalse(set.TryRemove(10));
            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void TryRemoveReturnsFalseForNegativeValue()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.TryRemove(-1));
        }

        [Test]
        public void TryRemoveReturnsFalseForValueEqualToCapacity()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.TryRemove(100));
        }

        [Test]
        public void TryRemoveReturnsFalseForValueGreaterThanCapacity()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.TryRemove(101));
        }

        [Test]
        public void TryRemoveFromEmptySetReturnsFalse()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.TryRemove(5));
        }

        [Test]
        public void TryRemoveLastElement()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.IsTrue(set.TryRemove(5));
            Assert.AreEqual(0, set.Count);
            Assert.IsTrue(set.IsEmpty);
        }

        [Test]
        public void TryRemoveMaintainsOrderForRemainingElements()
        {
            SparseSet set = new(100);
            set.TryAdd(1);
            set.TryAdd(2);
            set.TryAdd(3);

            set.TryRemove(2);

            Assert.AreEqual(2, set.Count);
            Assert.IsTrue(set.Contains(1));
            Assert.IsFalse(set.Contains(2));
            Assert.IsTrue(set.Contains(3));
        }

        [Test]
        public void TryRemoveSwapsWithLastElement()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);
            set.TryAdd(30);

            Assert.IsTrue(set.TryRemove(10));

            Assert.AreEqual(2, set.Count);
            Assert.IsFalse(set.Contains(10));
            Assert.IsTrue(set.Contains(20));
            Assert.IsTrue(set.Contains(30));
        }

        [Test]
        public void ContainsWorksCorrectly()
        {
            SparseSet set = new(100);
            set.TryAdd(42);

            Assert.IsTrue(set.Contains(42));
            Assert.IsFalse(set.Contains(43));
        }

        [Test]
        public void ContainsReturnsFalseForNegativeValue()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.Contains(-1));
        }

        [Test]
        public void ContainsReturnsFalseForValueEqualToCapacity()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.Contains(100));
        }

        [Test]
        public void ContainsReturnsFalseForValueGreaterThanCapacity()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.Contains(101));
        }

        [Test]
        public void ContainsReturnsFalseAfterRemoval()
        {
            SparseSet set = new(100);
            set.TryAdd(42);
            set.TryRemove(42);

            Assert.IsFalse(set.Contains(42));
        }

        [Test]
        public void ClearRemovesAllElements()
        {
            SparseSet set = new(100);
            set.TryAdd(1);
            set.TryAdd(2);
            set.TryAdd(3);

            set.Clear();

            Assert.AreEqual(0, set.Count);
            Assert.IsTrue(set.IsEmpty);
        }

        [Test]
        public void ClearOnEmptySetDoesNothing()
        {
            SparseSet set = new(100);

            set.Clear();

            Assert.AreEqual(0, set.Count);
            Assert.IsTrue(set.IsEmpty);
        }

        [Test]
        public void ClearAllowsReAddingElements()
        {
            SparseSet set = new(100);
            set.TryAdd(5);
            set.Clear();

            Assert.IsTrue(set.TryAdd(5));
            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.Contains(5));
        }

        [Test]
        public void IndexerReturnsCorrectElement()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);
            set.TryAdd(30);

            Assert.AreEqual(10, set[0]);
            Assert.AreEqual(20, set[1]);
            Assert.AreEqual(30, set[2]);
        }

        [Test]
        public void IndexerThrowsOnNegativeIndex()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.Throws<System.IndexOutOfRangeException>(() => _ = set[-1]);
        }

        [Test]
        public void IndexerThrowsOnIndexEqualToCount()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.Throws<System.IndexOutOfRangeException>(() => _ = set[1]);
        }

        [Test]
        public void IndexerThrowsOnIndexGreaterThanCount()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.Throws<System.IndexOutOfRangeException>(() => _ = set[10]);
        }

        [Test]
        public void IndexerThrowsOnEmptySet()
        {
            SparseSet set = new(100);

            Assert.Throws<System.IndexOutOfRangeException>(() => _ = set[0]);
        }

        [Test]
        public void TryGetReturnsElementAtValidIndex()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);

            Assert.IsTrue(set.TryGet(0, out int value));
            Assert.AreEqual(10, value);
            Assert.IsTrue(set.TryGet(1, out value));
            Assert.AreEqual(20, value);
        }

        [Test]
        public void TryGetReturnsFalseForNegativeIndex()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.IsFalse(set.TryGet(-1, out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void TryGetReturnsFalseForIndexEqualToCount()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.IsFalse(set.TryGet(1, out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void TryGetReturnsFalseForIndexGreaterThanCount()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.IsFalse(set.TryGet(10, out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void TryGetReturnsFalseOnEmptySet()
        {
            SparseSet set = new(100);

            Assert.IsFalse(set.TryGet(0, out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void CopyToCopiesAllElements()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);
            set.TryAdd(30);

            int[] array = new int[5];
            set.CopyTo(array, 0);

            Assert.AreEqual(10, array[0]);
            Assert.AreEqual(20, array[1]);
            Assert.AreEqual(30, array[2]);
        }

        [Test]
        public void CopyToCopiesWithOffset()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);

            int[] array = new int[5];
            set.CopyTo(array, 2);

            Assert.AreEqual(0, array[0]);
            Assert.AreEqual(0, array[1]);
            Assert.AreEqual(10, array[2]);
            Assert.AreEqual(20, array[3]);
        }

        [Test]
        public void CopyToThrowsOnNullArray()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.Throws<System.ArgumentNullException>(() => set.CopyTo(null, 0));
        }

        [Test]
        public void CopyToThrowsOnNegativeArrayIndex()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            int[] array = new int[10];
            Assert.Throws<System.ArgumentOutOfRangeException>(() => set.CopyTo(array, -1));
        }

        [Test]
        public void CopyToThrowsOnArrayIndexGreaterThanLength()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            int[] array = new int[10];
            Assert.Throws<System.ArgumentOutOfRangeException>(() => set.CopyTo(array, 11));
        }

        [Test]
        public void CopyToThrowsOnInsufficientSpace()
        {
            SparseSet set = new(100);
            set.TryAdd(1);
            set.TryAdd(2);
            set.TryAdd(3);

            int[] array = new int[2];
            Assert.Throws<System.ArgumentException>(() => set.CopyTo(array, 0));
        }

        [Test]
        public void CopyToThrowsOnInsufficientSpaceWithOffset()
        {
            SparseSet set = new(100);
            set.TryAdd(1);
            set.TryAdd(2);
            set.TryAdd(3);

            int[] array = new int[4];
            Assert.Throws<System.ArgumentException>(() => set.CopyTo(array, 2));
        }

        [Test]
        public void CopyToWorksOnEmptySet()
        {
            SparseSet set = new(100);

            int[] array = new int[5];
            set.CopyTo(array, 0);

            Assert.AreEqual(0, array[0]);
        }

        [Test]
        public void ToArrayReturnsAllElements()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);
            set.TryAdd(30);

            int[] array = set.ToArray();

            Assert.AreEqual(3, array.Length);
            CollectionAssert.Contains(array, 10);
            CollectionAssert.Contains(array, 20);
            CollectionAssert.Contains(array, 30);
        }

        [Test]
        public void ToArrayReturnsEmptyArrayForEmptySet()
        {
            SparseSet set = new(100);

            int[] array = set.ToArray();

            Assert.AreEqual(0, array.Length);
        }

        [Test]
        public void ToArrayWithRefParameterReusesArray()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);

            int[] array = new int[10];
            int count = set.ToArray(ref array);

            Assert.AreEqual(2, count);
            Assert.AreEqual(10, array.Length);
            Assert.AreEqual(10, array[0]);
            Assert.AreEqual(20, array[1]);
        }

        [Test]
        public void ToArrayWithRefParameterCreatesNewArrayIfNull()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);

            int[] array = null;
            int count = set.ToArray(ref array);

            Assert.AreEqual(2, count);
            Assert.IsTrue(array != null);
            Assert.AreEqual(2, array.Length);
        }

        [Test]
        public void ToArrayWithRefParameterCreatesNewArrayIfTooSmall()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);
            set.TryAdd(30);

            int[] array = new int[1];
            int count = set.ToArray(ref array);

            Assert.AreEqual(3, count);
            Assert.AreEqual(3, array.Length);
        }

        [Test]
        public void ToListPopulatesList()
        {
            SparseSet set = new(100);
            set.TryAdd(10);
            set.TryAdd(20);
            set.TryAdd(30);

            List<int> list = new();
            List<int> result = set.ToList(list);

            Assert.AreSame(list, result);
            Assert.AreEqual(3, list.Count);
            CollectionAssert.Contains(list, 10);
            CollectionAssert.Contains(list, 20);
            CollectionAssert.Contains(list, 30);
        }

        [Test]
        public void ToListClearsExistingList()
        {
            SparseSet set = new(100);
            set.TryAdd(10);

            List<int> list = new() { 99, 98, 97 };
            set.ToList(list);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(10, list[0]);
        }

        [Test]
        public void ToListThrowsOnNull()
        {
            SparseSet set = new(100);

            Assert.Throws<System.ArgumentNullException>(() => set.ToList(null));
        }

        [Test]
        public void ToListWorksOnEmptySet()
        {
            SparseSet set = new(100);

            List<int> list = new() { 1, 2, 3 };
            set.ToList(list);

            Assert.AreEqual(0, list.Count);
        }

        [Test]
        public void EnumerationWorks()
        {
            SparseSet set = new(100);
            set.TryAdd(5);
            set.TryAdd(10);
            set.TryAdd(15);

            List<int> elements = new();
            foreach (int element in set)
            {
                elements.Add(element);
            }

            Assert.AreEqual(3, elements.Count);
            CollectionAssert.Contains(elements, 5);
            CollectionAssert.Contains(elements, 10);
            CollectionAssert.Contains(elements, 15);
        }

        [Test]
        public void EnumerationWorksOnEmptySet()
        {
            SparseSet set = new(100);

            List<int> elements = new();
            foreach (int element in set)
            {
                elements.Add(element);
            }

            Assert.AreEqual(0, elements.Count);
        }

        [Test]
        public void EnumerationReflectsInsertionOrder()
        {
            SparseSet set = new(100);
            set.TryAdd(30);
            set.TryAdd(10);
            set.TryAdd(20);

            List<int> elements = new();
            foreach (int element in set)
            {
                elements.Add(element);
            }

            Assert.AreEqual(30, elements[0]);
            Assert.AreEqual(10, elements[1]);
            Assert.AreEqual(20, elements[2]);
        }

        [Test]
        public void MultipleEnumeratorsWork()
        {
            SparseSet set = new(100);
            set.TryAdd(1);
            set.TryAdd(2);

            using (SparseSet.SparseSetEnumerator e1 = set.GetEnumerator())
            using (SparseSet.SparseSetEnumerator e2 = set.GetEnumerator())
            {
                Assert.IsTrue(e1.MoveNext());
                Assert.IsTrue(e2.MoveNext());
                Assert.AreEqual(1, e1.Current);
                Assert.AreEqual(1, e2.Current);
            }
        }

        [Test]
        public void EnumeratorResetWorks()
        {
            SparseSet set = new(100);
            set.TryAdd(5);
            set.TryAdd(10);

            using (SparseSet.SparseSetEnumerator enumerator = set.GetEnumerator())
            {
                Assert.IsTrue(enumerator.MoveNext());
                Assert.AreEqual(5, enumerator.Current);
                enumerator.Reset();
                Assert.IsTrue(enumerator.MoveNext());
                Assert.AreEqual(5, enumerator.Current);
            }
        }

        [Test]
        public void IsEmptyReturnsTrueForEmptySet()
        {
            SparseSet set = new(100);

            Assert.IsTrue(set.IsEmpty);
        }

        [Test]
        public void IsEmptyReturnsFalseForNonEmptySet()
        {
            SparseSet set = new(100);
            set.TryAdd(5);

            Assert.IsFalse(set.IsEmpty);
        }

        [Test]
        public void IsEmptyReturnsTrueAfterClear()
        {
            SparseSet set = new(100);
            set.TryAdd(5);
            set.Clear();

            Assert.IsTrue(set.IsEmpty);
        }

        [Test]
        public void CapacityReturnsUniverseSize()
        {
            SparseSet set = new(50);

            Assert.AreEqual(50, set.Capacity);
        }

        [Test]
        public void AddRemoveAddSequenceWorks()
        {
            SparseSet set = new(100);

            set.TryAdd(5);
            Assert.IsTrue(set.Contains(5));

            set.TryRemove(5);
            Assert.IsFalse(set.Contains(5));

            set.TryAdd(5);
            Assert.IsTrue(set.Contains(5));
            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void StressTestAddRemovePattern()
        {
            SparseSet set = new(1000);

            for (int i = 0; i < 500; i++)
            {
                Assert.IsTrue(set.TryAdd(i));
            }

            for (int i = 0; i < 250; i++)
            {
                Assert.IsTrue(set.TryRemove(i));
            }

            Assert.AreEqual(250, set.Count);

            for (int i = 0; i < 250; i++)
            {
                Assert.IsFalse(set.Contains(i));
            }

            for (int i = 250; i < 500; i++)
            {
                Assert.IsTrue(set.Contains(i));
            }
        }

        [Test]
        public void GenericVersionConstructorThrowsOnZeroCapacity()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SparseSet<string>(0));
        }

        [Test]
        public void GenericVersionConstructorThrowsOnNegativeCapacity()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SparseSet<string>(-1));
        }

        [Test]
        public void GenericVersionConstructorCreatesEmptySet()
        {
            SparseSet<string> set = new(100);

            Assert.AreEqual(0, set.Count);
            Assert.IsTrue(set.IsEmpty);
            Assert.AreEqual(100, set.Capacity);
        }

        [Test]
        public void GenericVersionWorks()
        {
            SparseSet<string> set = new(100);

            Assert.IsTrue(set.TryAdd("hello"));
            Assert.IsTrue(set.TryAdd("world"));
            Assert.IsFalse(set.TryAdd("hello"));
            Assert.AreEqual(2, set.Count);
        }

        [Test]
        public void GenericVersionTryAddReturnsFalseAtCapacity()
        {
            SparseSet<string> set = new(2);

            Assert.IsTrue(set.TryAdd("first"));
            Assert.IsTrue(set.TryAdd("second"));
            Assert.IsFalse(set.TryAdd("third"));
            Assert.AreEqual(2, set.Count);
        }

        [Test]
        public void GenericVersionTryRemoveWorks()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("hello");
            set.TryAdd("world");

            Assert.IsTrue(set.TryRemove("hello"));
            Assert.AreEqual(1, set.Count);
            Assert.IsFalse(set.Contains("hello"));
            Assert.IsTrue(set.Contains("world"));
        }

        [Test]
        public void GenericVersionTryRemoveReturnsFalseForNonExistent()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("hello");

            Assert.IsFalse(set.TryRemove("world"));
            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void GenericVersionContainsWorks()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("test");

            Assert.IsTrue(set.Contains("test"));
            Assert.IsFalse(set.Contains("missing"));
        }

        [Test]
        public void GenericVersionClearWorks()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("a");
            set.TryAdd("b");

            set.Clear();

            Assert.AreEqual(0, set.Count);
            Assert.IsTrue(set.IsEmpty);
            Assert.IsFalse(set.Contains("a"));
        }

        [Test]
        public void GenericVersionClearAllowsReAdding()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("test");
            set.Clear();

            Assert.IsTrue(set.TryAdd("test"));
            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void GenericVersionIndexerWorks()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("first");
            set.TryAdd("second");

            Assert.AreEqual("first", set[0]);
            Assert.AreEqual("second", set[1]);
        }

        [Test]
        public void GenericVersionIndexerThrowsOnNegativeIndex()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("test");

            Assert.Throws<System.IndexOutOfRangeException>(() => _ = set[-1]);
        }

        [Test]
        public void GenericVersionIndexerThrowsOnIndexOutOfBounds()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("test");

            Assert.Throws<System.IndexOutOfRangeException>(() => _ = set[1]);
        }

        [Test]
        public void GenericVersionTryGetWorks()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("hello");

            Assert.IsTrue(set.TryGet(0, out string value));
            Assert.AreEqual("hello", value);
        }

        [Test]
        public void GenericVersionTryGetReturnsFalseForInvalidIndex()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("test");

            Assert.IsFalse(set.TryGet(1, out string value));
            Assert.IsTrue(value == null);
            Assert.IsFalse(set.TryGet(-1, out value));
            Assert.IsTrue(value == null);
        }

        [Test]
        public void GenericVersionCopyToWorks()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("a");
            set.TryAdd("b");
            set.TryAdd("c");

            string[] array = new string[5];
            set.CopyTo(array, 1);

            Assert.IsTrue(array[0] == null);
            Assert.AreEqual("a", array[1]);
            Assert.AreEqual("b", array[2]);
            Assert.AreEqual("c", array[3]);
        }

        [Test]
        public void GenericVersionCopyToThrowsOnNull()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("test");

            Assert.Throws<System.ArgumentNullException>(() => set.CopyTo(null, 0));
        }

        [Test]
        public void GenericVersionCopyToThrowsOnNegativeIndex()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("test");

            string[] array = new string[10];
            Assert.Throws<System.ArgumentOutOfRangeException>(() => set.CopyTo(array, -1));
        }

        [Test]
        public void GenericVersionCopyToThrowsOnInsufficientSpace()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("a");
            set.TryAdd("b");
            set.TryAdd("c");

            string[] array = new string[2];
            Assert.Throws<System.ArgumentException>(() => set.CopyTo(array, 0));
        }

        [Test]
        public void GenericVersionToArrayWorks()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("x");
            set.TryAdd("y");

            string[] array = set.ToArray();

            Assert.AreEqual(2, array.Length);
            CollectionAssert.Contains(array, "x");
            CollectionAssert.Contains(array, "y");
        }

        [Test]
        public void GenericVersionToArrayReturnsEmptyForEmptySet()
        {
            SparseSet<string> set = new(100);

            string[] array = set.ToArray();

            Assert.AreEqual(0, array.Length);
        }

        [Test]
        public void GenericVersionToListWorks()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("foo");
            set.TryAdd("bar");

            List<string> list = new();
            set.ToList(list);

            Assert.AreEqual(2, list.Count);
            CollectionAssert.Contains(list, "foo");
            CollectionAssert.Contains(list, "bar");
        }

        [Test]
        public void GenericVersionToListClearsExisting()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("new");

            List<string> list = new() { "old1", "old2" };
            set.ToList(list);

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("new", list[0]);
        }

        [Test]
        public void GenericVersionToListThrowsOnNull()
        {
            SparseSet<string> set = new(100);

            Assert.Throws<System.ArgumentNullException>(() => set.ToList(null));
        }

        [Test]
        public void GenericVersionEnumerationWorks()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("alpha");
            set.TryAdd("beta");

            List<string> elements = new();
            foreach (string element in set)
            {
                elements.Add(element);
            }

            Assert.AreEqual(2, elements.Count);
            CollectionAssert.Contains(elements, "alpha");
            CollectionAssert.Contains(elements, "beta");
        }

        [Test]
        public void GenericVersionEnumerationWorksOnEmptySet()
        {
            SparseSet<string> set = new(100);

            List<string> elements = new();
            foreach (string element in set)
            {
                elements.Add(element);
            }

            Assert.AreEqual(0, elements.Count);
        }

        [Test]
        public void GenericVersionEnumerationReflectsInsertionOrder()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("third");
            set.TryAdd("first");
            set.TryAdd("second");

            List<string> elements = new();
            foreach (string element in set)
            {
                elements.Add(element);
            }

            Assert.AreEqual("third", elements[0]);
            Assert.AreEqual("first", elements[1]);
            Assert.AreEqual("second", elements[2]);
        }

        [Test]
        public void GenericVersionWithCustomComparer()
        {
            SparseSet<string> set = new(100, System.StringComparer.OrdinalIgnoreCase);

            Assert.IsTrue(set.TryAdd("Hello"));
            Assert.IsFalse(set.TryAdd("HELLO"));
            Assert.IsTrue(set.Contains("hello"));
            Assert.AreEqual(1, set.Count);
        }

        [Test]
        public void GenericVersionWithNullElements()
        {
            SparseSet<string> set = new(100);

            Assert.Throws<System.ArgumentNullException>(() => set.TryAdd(null));
            Assert.Throws<System.ArgumentNullException>(() => set.TryRemove(null));
            Assert.Throws<System.ArgumentNullException>(() => set.Contains(null));
        }

        [Test]
        public void GenericVersionWithValueTypes()
        {
            SparseSet<int> set = new(100);

            Assert.IsTrue(set.TryAdd(42));
            Assert.IsTrue(set.Contains(42));
            Assert.IsFalse(set.TryAdd(42));
        }

        [Test]
        public void GenericVersionEnumeratorDisposal()
        {
            SparseSet<string> set = new(100);
            set.TryAdd("test");

            SparseSet<string>.SparseSetEnumerator enumerator = set.GetEnumerator();
            enumerator.MoveNext();
            enumerator.Dispose();

            enumerator.Dispose();
        }

        [Test]
        public void GenericVersionStressTestAddRemove()
        {
            SparseSet<int> set = new(1000);

            for (int i = 0; i < 500; i++)
            {
                Assert.IsTrue(set.TryAdd(i));
            }

            for (int i = 0; i < 250; i++)
            {
                Assert.IsTrue(set.TryRemove(i));
            }

            Assert.AreEqual(250, set.Count);

            for (int i = 250; i < 500; i++)
            {
                Assert.IsTrue(set.Contains(i));
            }
        }
    }
}
