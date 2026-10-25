// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [TestFixture]
    [Category("Fast")]
    public sealed class CyclicBufferMembershipTests
    {
        private static void AssertMembership(
            CyclicBuffer<int> buffer,
            List<int> expected,
            int maximum
        )
        {
            CollectionAssert.AreEqual(expected, buffer);
            Assert.AreEqual(expected.Count, buffer.Count);
            for (int candidate = 0; candidate <= maximum; ++candidate)
            {
                Assert.AreEqual(
                    expected.Contains(candidate),
                    buffer.Contains(candidate),
                    $"Membership for {candidate}"
                );
            }
        }

        [Test]
        public void DefaultMembershipTracksRemainingDuplicates()
        {
            CyclicBuffer<int> buffer = new(4) { 0, 7, 0, 8 };
            Assert.IsTrue(buffer.TryPopFront(out _));
            Assert.IsTrue(buffer.Contains(0));
            buffer.RemoveAt(1);
            Assert.IsFalse(buffer.Contains(0));
            buffer.Add(0);
            Assert.IsTrue(buffer.Contains(0));
            Assert.IsTrue(buffer.Remove(0));
            Assert.IsFalse(buffer.Contains(0));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        public void MembershipMatchesLiveItemsAfterEveryRemoval(int capacity)
        {
            for (int added = 1; added <= capacity * 3; ++added)
            {
                for (
                    int removedIndex = 0;
                    removedIndex < capacity && removedIndex < added;
                    ++removedIndex
                )
                {
                    for (int operation = 0; operation < 3; ++operation)
                    {
                        CyclicBuffer<int> buffer = new(capacity);
                        List<int> expected = new();
                        for (int item = 1; item <= added; ++item)
                        {
                            buffer.Add(item);
                            expected.Add(item);
                            if (capacity < expected.Count)
                            {
                                expected.RemoveAt(0);
                            }
                        }

                        int index =
                            operation == 0 ? 0
                            : operation == 1 ? expected.Count - 1
                            : removedIndex;
                        if (operation == 0)
                        {
                            Assert.IsTrue(buffer.TryPopFront(out int popped));
                            Assert.AreEqual(expected[index], popped);
                        }
                        else if (operation == 1)
                        {
                            Assert.IsTrue(buffer.TryPopBack(out int popped));
                            Assert.AreEqual(expected[index], popped);
                        }
                        else
                        {
                            buffer.RemoveAt(index);
                        }
                        expected.RemoveAt(index);
                        AssertMembership(buffer, expected, added);
                        buffer.Add(added + 1);
                        expected.Add(added + 1);
                        AssertMembership(buffer, expected, added + 1);
                    }
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RemovedNullDoesNotRemainAMember(bool fromFront)
        {
            CyclicBuffer<string> buffer = new(3) { "first", null, "last" };
            if (fromFront)
            {
                Assert.IsTrue(buffer.TryPopFront(out _));
                Assert.IsTrue(buffer.Contains(null));
                Assert.IsTrue(buffer.TryPopFront(out _));
            }
            else
            {
                Assert.IsTrue(buffer.TryPopBack(out _));
                Assert.IsTrue(buffer.Contains(null));
                Assert.IsTrue(buffer.TryPopBack(out _));
            }
            Assert.IsFalse(buffer.Contains(null));
            Assert.AreEqual(1, buffer.Count);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DrainedBufferHasNoMembers(bool fromFront)
        {
            CyclicBuffer<int> buffer = new(4) { 0, 1, 2, 3, 4, 5 };
            while (0 < buffer.Count)
            {
                Assert.IsTrue(fromFront ? buffer.TryPopFront(out _) : buffer.TryPopBack(out _));
            }
            Assert.IsFalse(buffer.Contains(0));
            Assert.IsFalse(buffer.Contains(5));
            buffer.Add(7);
            Assert.IsTrue(buffer.Contains(7));
            Assert.IsFalse(buffer.Contains(0));
        }
    }
}
