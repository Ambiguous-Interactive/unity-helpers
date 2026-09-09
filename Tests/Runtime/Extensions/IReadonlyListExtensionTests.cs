// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class IReadonlyListExtensionTests : CommonTestBase
    {
        [Test]
        public void IndexOfArrayUsesOptimizedPath()
        {
            IReadOnlyList<int> list = new[] { 1, 2, 3, 2 };

            Assert.AreEqual(0, list.IndexOf(1));
            Assert.AreEqual(1, list.IndexOf(2));
            Assert.AreEqual(-1, list.IndexOf(4));
        }

        [Test]
        public void IndexOfListRespectsStartIndexCountAndZeroCount()
        {
            IReadOnlyList<int> list = new List<int> { 1, 2, 3, 2, 1 };

            Assert.AreEqual(3, list.IndexOf(2, 2, 3));
            Assert.AreEqual(-1, list.IndexOf(2, 0, 1));
            Assert.AreEqual(-1, list.IndexOf(2, list.Count, 0));
        }

        [Test]
        public void IndexOfSupportsCustomComparer()
        {
            IReadOnlyList<string> list = new TestReadOnlyList<string>(
                new[] { "alpha", "Bravo", "charlie" }
            );

            int index = list.IndexOf("bravo", 0, list.Count, StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual(1, index);
        }

        [Test]
        public void IndexOfThrowsForInvalidArguments()
        {
            IReadOnlyList<int> list = new[] { 1, 2, 3 };

            Assert.Throws<ArgumentNullException>(() => ((IReadOnlyList<int>)null).IndexOf(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.IndexOf(2, -1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.IndexOf(2, 1, 5));
        }

        [Test]
        public void LastIndexOfHandlesDefaults()
        {
            IReadOnlyList<int> list = new[] { 1, 2, 3, 2, 1 };

            Assert.AreEqual(3, list.LastIndexOf(2));
            Assert.AreEqual(-1, list.LastIndexOf(5));
        }

        [Test]
        public void LastIndexOfRespectsStartIndexAndCount()
        {
            IReadOnlyList<int> list = new TestReadOnlyList<int>(new[] { 1, 2, 3, 2, 1 });

            Assert.AreEqual(1, list.LastIndexOf(2, 2, 2));
            Assert.AreEqual(-1, list.LastIndexOf(2, 2, 1));
            Assert.AreEqual(-1, list.LastIndexOf(2, 4, 0));
        }

        [Test]
        public void LastIndexOfSupportsCustomComparer()
        {
            IReadOnlyList<string> list = new[] { "alpha", "Bravo", "charlie", "BRAVO" };

            int index = list.LastIndexOf(
                "bravo",
                list.Count - 1,
                list.Count,
                StringComparer.OrdinalIgnoreCase
            );
            Assert.AreEqual(3, index);
        }

        [Test]
        public void LastIndexOfThrowsForInvalidArguments()
        {
            IReadOnlyList<int> list = new[] { 1, 2, 3 };

            Assert.Throws<ArgumentNullException>(() => ((IReadOnlyList<int>)null).LastIndexOf(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.LastIndexOf(2, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.LastIndexOf(2, 2, 5));
        }

        [Test]
        public void ContainsUsesComparerWhenProvided()
        {
            IReadOnlyList<string> list = new TestReadOnlyList<string>(
                new[] { "red", "green", "blue" }
            );

            Assert.IsTrue(list.Contains("GREEN", StringComparer.OrdinalIgnoreCase));
            Assert.IsFalse(list.Contains("purple", StringComparer.OrdinalIgnoreCase));
        }

        [Test]
        public void ContainsThrowsWhenListIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => ((IReadOnlyList<int>)null).Contains(1));
        }

        [Test]
        public void TryGetElementAtReturnsExpectedValues()
        {
            IReadOnlyList<int> list = new[] { 9, 8, 7 };

            Assert.IsTrue(list.TryGetElementAt(1, out int value));
            Assert.AreEqual(8, value);
            Assert.IsFalse(list.TryGetElementAt(3, out _));
            Assert.IsFalse(list.TryGetElementAt(-1, out _));
        }

        [Test]
        public void TryGetElementAtThrowsWhenListIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ((IReadOnlyList<int>)null).TryGetElementAt(0, out _)
            );
        }

        [Test]
        public void TryGetFirstAndLastHandleEmptyAndPopulatedLists()
        {
            IReadOnlyList<int> empty = Array.Empty<int>();
            Assert.IsFalse(empty.TryGetFirst(out _));
            Assert.IsFalse(empty.TryGetLast(out _));

            IReadOnlyList<int> list = new[] { 42, 43 };
            Assert.IsTrue(list.TryGetFirst(out int first));
            Assert.IsTrue(list.TryGetLast(out int last));
            Assert.AreEqual(42, first);
            Assert.AreEqual(43, last);
        }

        [Test]
        public void TryGetFirstAndLastThrowWhenListIsNull()
        {
            Assert.Throws<ArgumentNullException>(() =>
                ((IReadOnlyList<int>)null).TryGetFirst(out _)
            );
            Assert.Throws<ArgumentNullException>(() =>
                ((IReadOnlyList<int>)null).TryGetLast(out _)
            );
        }

        [Test]
        public void IsNullOrEmptyCoversAllBranches()
        {
            IReadOnlyList<int> list = new[] { 1 };
            Assert.IsTrue(((IReadOnlyList<int>)null).IsNullOrEmpty());
            Assert.IsTrue(Array.Empty<int>().IsNullOrEmpty());
            Assert.IsFalse(list.IsNullOrEmpty());
        }

        [Test]
        public void BinarySearchMatchesArrayBehavior()
        {
            IReadOnlyList<int> list = new[] { 1, 4, 9, 16, 25 };

            Assert.AreEqual(2, list.BinarySearch(9));
            Assert.AreEqual(~1, list.BinarySearch(2));
        }

        [Test]
        public void BinarySearchUsesListImplementation()
        {
            IReadOnlyList<int> list = new List<int> { 3, 6, 9, 12 };

            Assert.AreEqual(1, list.BinarySearch(6));
            Assert.AreEqual(~4, list.BinarySearch(0, list.Count, 15));
        }

        [Test]
        public void BinarySearchSupportsCustomComparer()
        {
            IReadOnlyList<string> list = new TestReadOnlyList<string>(
                new[] { "ant", "Bee", "cat" }
            );

            int index = list.BinarySearch(0, list.Count, "bee", StringComparer.OrdinalIgnoreCase);
            Assert.AreEqual(1, index);
            Assert.AreEqual(
                ~3,
                list.BinarySearch(0, list.Count, "dog", StringComparer.OrdinalIgnoreCase)
            );
        }

        [Test]
        public void BinarySearchThrowsForInvalidArguments()
        {
            IReadOnlyList<int> list = new[] { 1, 2, 3 };

            Assert.Throws<ArgumentNullException>(() => ((IReadOnlyList<int>)null).BinarySearch(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.BinarySearch(-1, 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.BinarySearch(1, 5, 2));
        }

        [Test]
        public void BinarySearchReturnsInsertionPointForEmptyList()
        {
            IReadOnlyList<int> list = Array.Empty<int>();

            Assert.AreEqual(~0, list.BinarySearch(5));
            Assert.AreEqual(~0, list.BinarySearch(0, 0, 5));
        }

        [Test]
        public void IndexBasedOperationsCoverCustomReadonlyList()
        {
            IReadOnlyList<int> list = new TestReadOnlyList<int>(new[] { 2, 4, 6, 8, 10 });

            Assert.AreEqual(3, list.IndexOf(8));
            Assert.AreEqual(3, list.LastIndexOf(8));
            Assert.AreEqual(~2, list.BinarySearch(5));
            Assert.IsTrue(list.Contains(4));
        }

        [Test]
        public void IndexOfSupportsCyclicBuffer()
        {
            CyclicBuffer<int> buffer = new(3) { 1, 2, 3 };
            IReadOnlyList<int> list = buffer;

            Assert.AreEqual(0, list.IndexOf(1));
            Assert.AreEqual(1, list.IndexOf(2));
            Assert.AreEqual(2, list.IndexOf(3));
            Assert.AreEqual(-1, list.IndexOf(4));
        }

        private sealed class TestReadOnlyList<T> : IReadOnlyList<T>
        {
            public T this[int index] => _items[index];

            public int Count => _items.Length;

            private readonly T[] _items;

            public TestReadOnlyList(IEnumerable<T> items)
            {
                _items = items.ToArray();
            }

            public IEnumerator<T> GetEnumerator()
            {
                return ((IEnumerable<T>)_items).GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return _items.GetEnumerator();
            }
        }
    }
}
