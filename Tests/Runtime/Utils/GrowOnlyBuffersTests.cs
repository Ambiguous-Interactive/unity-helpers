// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class GrowOnlyBuffersTests
    {
        [TestCase(1)]
        [TestCase(8)]
        [TestCase(16384)]
        public void CapacityOnlyGrowsAndReusesEveryPreviouslyHeldSize(int count)
        {
            int[] buffer = null;
            Assert.IsTrue(GrowOnlyBuffers.EnsureCapacity(ref buffer, count));
            int[] first = buffer;
            foreach (int requested in new[] { count, count - 1, 0, count, 1, count / 2 })
            {
                Assert.IsFalse(GrowOnlyBuffers.EnsureCapacity(ref buffer, requested));
                Assert.AreSame(first, buffer);
            }

            buffer[0] = 41;
            Assert.IsTrue(GrowOnlyBuffers.EnsureCapacity(ref buffer, count + 1));
            Assert.AreNotSame(first, buffer);
            Assert.That(buffer.Length, Is.EqualTo(count + 1));
            Assert.That(buffer[0], Is.Zero);
            Assert.That(first[0], Is.EqualTo(41));
        }

        [TestCase(int.MinValue)]
        [TestCase(-1)]
        [TestCase(0)]
        public void NonpositiveRequestsInitializeNullWithoutReplacingExistingStorage(int count)
        {
            int[] buffer = null;
            Assert.IsFalse(GrowOnlyBuffers.EnsureCapacity(ref buffer, count));
            Assert.AreSame(Array.Empty<int>(), buffer);
            int[] existing = { 4, 5 };
            buffer = existing;
            Assert.IsFalse(GrowOnlyBuffers.EnsureCapacity(ref buffer, count));
            Assert.AreSame(existing, buffer);
            CollectionAssert.AreEqual(new[] { 4, 5 }, buffer);
        }

        [TestCase(int.MinValue, 0)]
        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(3, 3)]
        [TestCase(4, 4)]
        [TestCase(int.MaxValue, 4)]
        public void ClearOnlyTouchesTheBoundedLogicalPrefix(int count, int cleared)
        {
            object first = new();
            object second = new();
            object third = new();
            object fourth = new();
            object[] original = { first, second, third, fourth };
            object[] buffer = (object[])original.Clone();
            GrowOnlyBuffers.Clear(buffer, count);
            for (int index = 0; index < buffer.Length; ++index)
            {
                Assert.AreSame(index < cleared ? null : original[index], buffer[index]);
            }

            GrowOnlyBuffers.Clear<object>(null, count);
            GrowOnlyBuffers.Clear(Array.Empty<object>(), count);
        }

        [TestCase(int.MinValue, 0)]
        [TestCase(-1, 0)]
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(4, 4)]
        [TestCase(int.MaxValue, 4)]
        public void SpanExposesTheBoundedLivePrefixWithoutCopying(int count, int length)
        {
            int[] buffer = { 1, 2, 3, 4 };
            ReadOnlySpan<int> prefix = GrowOnlyBuffers.AsSpan(buffer, count);
            Assert.That(prefix.Length, Is.EqualTo(length));
            buffer[0] = 92;
            if (0 < length)
            {
                Assert.That(prefix[0], Is.EqualTo(92));
            }

            Assert.That(GrowOnlyBuffers.AsSpan<int>(null, count).Length, Is.Zero);
            Assert.That(GrowOnlyBuffers.AsSpan(Array.Empty<int>(), count).Length, Is.Zero);
        }

        [Test]
        public void LogicalComparisonIgnoresDifferentCapacitiesAndDirtySlack()
        {
            int[] large = { 1, 2, 3, 400, 500 };
            int[] small = { 1, 2, 3 };
            Assert.IsTrue(
                GrowOnlyBuffers.AsSpan(large, 3).SequenceEqual(GrowOnlyBuffers.AsSpan(small, 3))
            );
            GrowOnlyBuffers.Clear(large, 3);
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 400, 500 }, large);
        }
    }
}
