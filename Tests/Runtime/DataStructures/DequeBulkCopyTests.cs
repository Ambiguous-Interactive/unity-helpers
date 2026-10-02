// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [Category("Fast")]
    public sealed class DequeBulkCopyTests
    {
        private static void AssertCopyToDoesNotAllocate<T>(Deque<T> deque)
        {
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            long control = GCAssert.MeasureAllocatedBytes(() => GC.KeepAlive(new byte[4096]));
            if (control <= 0)
            {
                Assert.Ignore("Allocation counter did not detect the retained allocation control.");
            }
            T[] destination = new T[deque.Count];
            GCAssert.DoesNotAllocate(() => deque.CopyTo(destination, 0));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(5)]
        [TestCase(15)]
        [TestCase(16)]
        [TestCase(17)]
        [TestCase(31)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(128)]
        [TestCase(10000)]
        public void CopyToPreservesOrderAcrossHeadPositions(int count)
        {
            int capacity = Math.Max(1, count + 3);
            Deque<int> deque = new(capacity);
            for (int index = 0; index < count; ++index)
            {
                deque.PushBack(index + 1);
            }

            int[] destination = new int[count + 4];
            int headPositions = Math.Min(capacity, 1000 <= count ? 8 : 256);
            for (int head = 0; head < headPositions; ++head)
            {
                Array.Fill(destination, -1);
                deque.CopyTo(destination, 2);
                Assert.AreEqual(-1, destination[0]);
                Assert.AreEqual(-1, destination[1]);
                Assert.AreEqual(-1, destination[count + 2]);
                Assert.AreEqual(-1, destination[count + 3]);
                for (int index = 0; index < count; ++index)
                {
                    Assert.AreEqual(
                        deque[index],
                        destination[index + 2],
                        $"Head {head}, index {index}"
                    );
                }

                if (deque.TryPopFront(out int front))
                {
                    deque.PushBack(front);
                }
                else
                {
                    deque.PushBack(0);
                    Assert.IsTrue(deque.TryPopFront(out _));
                }
            }
        }

        [TestCase(1)]
        [TestCase(16)]
        [TestCase(32)]
        [TestCase(128)]
        public void CopyToPreservesOrderWhenFullAndHeadEqualsTail(int count)
        {
            Deque<int> deque = new(count);
            for (int index = 0; index < count; ++index)
            {
                deque.PushBack(index);
            }

            int[] destination = new int[count];
            for (int rotation = 0; rotation < count; ++rotation)
            {
                deque.CopyTo(destination, 0);
                for (int index = 0; index < count; ++index)
                {
                    Assert.AreEqual((rotation + index) % count, destination[index]);
                }
                Assert.IsTrue(deque.TryPopFront(out int front));
                deque.PushBack(front);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopyToPreservesNullsAndReferenceIdentity(bool wrapped)
        {
            object first = new();
            object second = new();
            Deque<object> deque = new(128);
            if (wrapped)
            {
                deque.PushFront(second);
            }
            for (int index = 0; index < 70; ++index)
            {
                deque.PushBack(index % 3 == 0 ? null : first);
            }
            if (!wrapped)
            {
                deque.PushBack(second);
            }

            object[] destination = new object[deque.Count + 2];
            deque.CopyTo(destination, 1);
            for (int index = 0; index < deque.Count; ++index)
            {
                Assert.IsTrue(ReferenceEquals(deque[index], destination[index + 1]));
            }
            Assert.IsTrue(destination[0] == null);
            Assert.IsTrue(destination[destination.Length - 1] == null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopyToCovariantDestinationRetainsPartialWriteOnIncompatibleElement(bool wrapped)
        {
            Deque<object> deque = new(128);
            if (wrapped)
            {
                deque.PushFront("first");
            }
            else
            {
                deque.PushBack("first");
            }
            deque.PushBack(null);
            deque.PushBack(new object());
            for (int index = 0; index < 70; ++index)
            {
                deque.PushBack("after");
            }

            string[] destination = new string[deque.Count + 2];
            Array.Fill(destination, "sentinel");
            Assert.Throws<ArrayTypeMismatchException>(() => deque.CopyTo(destination, 1));
            Assert.AreEqual("sentinel", destination[0]);
            Assert.AreEqual("first", destination[1]);
            Assert.IsTrue(destination[2] == null);
            Assert.AreEqual("sentinel", destination[3]);
            Assert.AreEqual("sentinel", destination[destination.Length - 1]);
            Assert.AreEqual(73, deque.Count);
        }

        [Test]
        public void CopyToCovariantDestinationAcceptsCompatibleElements()
        {
            Deque<object> deque = new(128);
            deque.PushFront("first");
            for (int index = 0; index < 70; ++index)
            {
                deque.PushBack(index % 2 == 0 ? "value" : null);
            }
            string[] destination = new string[deque.Count + 2];
            deque.CopyTo(destination, 1);
            for (int index = 0; index < deque.Count; ++index)
            {
                Assert.AreEqual(deque[index], destination[index + 1]);
            }
        }

        [Test]
        public void CopyToRejectsUndersizedBulkDestinationBeforeAnyWrites()
        {
            Deque<int> deque = new(128);
            for (int index = 0; index < 128; ++index)
            {
                deque.PushBack(index);
            }
            int[] destination = new int[129];
            Array.Fill(destination, -1);
            Assert.Throws<ArgumentException>(() => deque.CopyTo(destination, 2));
            foreach (int element in destination)
            {
                Assert.AreEqual(-1, element);
            }
        }

        [TestCase(-1)]
        [TestCase(133)]
        [TestCase(int.MaxValue)]
        public void CopyToRejectsInvalidBulkDestinationOffsetBeforeAnyWrites(int offset)
        {
            Deque<int> deque = new(128);
            for (int index = 0; index < 128; ++index)
            {
                deque.PushBack(index);
            }
            int[] destination = new int[132];
            Array.Fill(destination, -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => deque.CopyTo(destination, offset));
            foreach (int element in destination)
            {
                Assert.AreEqual(-1, element);
            }
        }

        [Test]
        public void CopyToEmptyDequeAcceptsDestinationEnd()
        {
            Deque<int> deque = new(1);
            int[] destination = { 123 };
            deque.CopyTo(destination, destination.Length);
            deque.CopyTo(Array.Empty<int>(), 0);
            Assert.AreEqual(123, destination[0]);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ToArrayReusesDestinationWithoutChangingUnusedTail(bool wrapped)
        {
            Deque<int> deque = new(128);
            if (wrapped)
            {
                deque.PushFront(42);
            }
            for (int index = 0; index < 70; ++index)
            {
                deque.PushBack(index);
            }
            int[] destination = new int[128];
            Array.Fill(destination, -1);
            int[] original = destination;
            Assert.AreEqual(deque.Count, deque.ToArray(ref destination));
            Assert.AreSame(original, destination);
            for (int index = 0; index < deque.Count; ++index)
            {
                Assert.AreEqual(deque[index], destination[index]);
            }
            for (int index = deque.Count; index < destination.Length; ++index)
            {
                Assert.AreEqual(-1, destination[index]);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopyToExactReferenceDestinationDoesNotAllocateWhenCounterIsObservable(
            bool wrapped
        )
        {
            Deque<object> deque = new(1024);
            object value = new();
            if (wrapped)
            {
                deque.PushFront(value);
            }
            for (int index = 0; index < 500; ++index)
            {
                deque.PushBack(index % 2 == 0 ? null : value);
            }
            AssertCopyToDoesNotAllocate(deque);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopyToExactDestinationDoesNotAllocateWhenCounterIsObservable(bool wrapped)
        {
            Deque<int> deque = new(1024);
            if (wrapped)
            {
                deque.PushFront(42);
            }
            for (int index = 0; index < 500; ++index)
            {
                deque.PushBack(index);
            }
            AssertCopyToDoesNotAllocate(deque);
        }
    }
}
