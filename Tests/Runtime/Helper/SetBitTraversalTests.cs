// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [Category("Fast")]
    public sealed class SetBitTraversalTests
    {
        private static void VerifyMask(ulong mask)
        {
            SetBitEnumerator actual = BitOps.SetBits(mask);
            int rank = 0;
            for (int bit = 0; bit < 64; ++bit)
            {
                if ((mask & (1UL << bit)) == 0)
                {
                    continue;
                }
                Assert.That(actual.MoveNext(), Is.True);
                Assert.That(actual.Current, Is.EqualTo(bit));
                Assert.That(BitOps.SelectBit(mask, rank), Is.EqualTo(bit));
                ++rank;
            }
            Assert.That(actual.MoveNext(), Is.False);
            Assert.That(actual.MoveNext(), Is.False);
            Assert.That(actual.Current, Is.EqualTo(-1));
            Assert.That(BitOps.SelectBit(mask, rank), Is.EqualTo(-1));
            Assert.That(BitOps.SelectBit(mask, -1), Is.EqualTo(-1));
            Assert.That(BitOps.SelectBit(mask, 64), Is.EqualTo(-1));
            Assert.That(BitOps.SelectBit(mask, int.MaxValue), Is.EqualTo(-1));
            Assert.That(BitOps.SelectBit(mask, int.MinValue), Is.EqualTo(-1));
        }

        [Test]
        public void EverySingleBitAndBoundaryMaskMatchesIndependentOracle()
        {
            VerifyMask(0);
            VerifyMask(ulong.MaxValue);
            VerifyMask(0xAAAAAAAAAAAAAAAAUL);
            VerifyMask(0x5555555555555555UL);
            for (int bit = 0; bit < 64; ++bit)
            {
                VerifyMask(1UL << bit);
                VerifyMask(ulong.MaxValue << bit);
            }
        }

        [Test]
        public void SeededMasksMatchIndependentOracle()
        {
            Random random = new(741);
            byte[] bytes = new byte[8];
            for (int sample = 0; sample < 256; ++sample)
            {
                random.NextBytes(bytes);
                VerifyMask(BitConverter.ToUInt64(bytes, 0));
            }
        }

        [Test]
        public void ForeachAndCopiedEnumeratorsHaveIndependentState()
        {
            SetBitEnumerator source = BitOps.SetBits(0x8000000000000005UL);
            SetBitEnumerator copy = source;
            Assert.That(copy, Is.EqualTo(source));
            Assert.That(copy.GetHashCode(), Is.EqualTo(source.GetHashCode()));
            Assert.That(copy.MoveNext(), Is.True);
            Assert.That(copy.Equals(source), Is.False);
            int count = 0;
            int sum = 0;
            foreach (int bit in source)
            {
                ++count;
                sum += bit;
            }
            Assert.That(count, Is.EqualTo(3));
            Assert.That(sum, Is.EqualTo(65));
            Assert.That(source.MoveNext(), Is.True);
            Assert.That(source.Current, Is.Zero);
            Assert.That(default(SetBitEnumerator).MoveNext(), Is.False);
        }

        [Test]
        public void SparseBitSetTraversalObservesEditsAheadOfItsCursor()
        {
            BitSet bits = new(192);
            bits.TrySet(1);
            bits.TrySet(130);
            using IEnumerator<int> iterator = bits.EnumerateSetIndices().GetEnumerator();
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(1));
            bits.TryClear(130);
            bits.TrySet(2);
            bits.TrySet(64);
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(2));
            bits.Resize(256);
            bits.TrySet(255);
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(64));
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(255));
            Assert.That(iterator.MoveNext(), Is.False);
        }

        [Test]
        public void SparseBitSetTraversalCanShrinkBetweenMoves()
        {
            BitSet bits = new(192);
            bits.TrySet(1);
            bits.TrySet(130);
            using IEnumerator<int> iterator = bits.EnumerateSetIndices().GetEnumerator();
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(1));
            bits.Resize(1);
            Assert.That(iterator.MoveNext(), Is.False);
        }

        [Test]
        public void WarmTraversalDoesNotAllocate()
        {
            BitOps.TrailingZeroCount(1UL);
            GCAssert.IgnoreIfAllocationMeasurementUnavailable();
            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
            byte[] control = new byte[128];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
            GC.KeepAlive(control);
            if (controlBytes == 0)
            {
                Assert.Ignore("The allocation counter did not observe a retained allocation.");
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            int sum = 0;
            for (int sample = 0; sample < 256; ++sample)
            {
                foreach (int bit in BitOps.SetBits(ulong.MaxValue))
                {
                    sum += bit;
                }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(sum, Is.EqualTo(256 * 2016));
            Assert.That(allocated, Is.Zero);
        }
    }
}
