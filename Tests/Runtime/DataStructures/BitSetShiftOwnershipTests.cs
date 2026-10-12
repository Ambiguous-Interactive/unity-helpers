// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.DataStructures
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class BitSetShiftOwnershipTests
    {
        private static BitSet CreateWithTrailingWords(int capacity)
        {
            byte[] payload = new byte[46];
            for (int word = 0; word < 4; ++word)
            {
                int offset = word * 11;
                payload[offset] = 0x08;
                for (int index = 1; index <= 10; ++index)
                {
                    payload[offset + index] = index == 10 ? (byte)0x01 : (byte)0xFF;
                }
            }
            payload[44] = 0x10;
            payload[45] = (byte)capacity;
            return Serializer.ProtoDeserialize<BitSet>(payload);
        }

        [Test]
        public void LeftShiftMatchesLogicalSnapshot(
            [Values(1, 2, 63, 64, 65, 127, 128, 129, 255, 256, 4097, 10240)] int capacity,
            [Values(int.MinValue, -1, 0, 1, 2, 31, 63, 64, 65, 127, int.MaxValue)] int shift,
            [Values(0, 1, 2, 3)] int pattern
        )
        {
            BitSet bits = new BitSet(capacity);
            bool[] before = new bool[capacity];
            for (int index = 0; index < capacity; ++index)
            {
                bool value =
                    pattern == 1
                    || (pattern == 2 && (index & 1) == 0)
                    || (pattern == 3 && (unchecked((uint)index * 2654435761U + 123U) & 128U) != 0);
                before[index] = value;
                bits[index] = value;
            }
            ImmutableBitSet snapshot = bits.ToImmutable();
            bits.LeftShift(shift);
            int expectedCount = 0;
            for (int index = 0; index < capacity; ++index)
            {
                bool expected =
                    shift <= 0 ? before[index] : shift <= index && before[index - shift];
                Assert.AreEqual(expected, bits[index], $"Bit {index} differs.");
                Assert.AreEqual(before[index], snapshot[index]);
                if (expected)
                {
                    ++expectedCount;
                }
            }
            Assert.AreEqual(capacity, bits.Capacity);
            Assert.AreEqual(expectedCount, bits.CountSetBits());
        }

        [Test]
        public void LeftShiftClearsTrailingSerializedWords(
            [Values(3, 65, 127)] int capacity,
            [Values(1, 2, 63, 64, 65, int.MaxValue)] int shift
        )
        {
            BitSet bits = CreateWithTrailingWords(capacity);
            Assert.AreEqual(capacity, bits.Capacity);
            Assert.AreEqual(256, bits.CountSetBits());
            bits.LeftShift(shift);
            int expectedCount = shift < capacity ? capacity - shift : 0;
            Assert.AreEqual(expectedCount, bits.CountSetBits());
            Assert.AreEqual(capacity, bits.Capacity);
            for (int index = 0; index < capacity; ++index)
            {
                Assert.AreEqual(shift <= index, bits[index]);
            }
        }

        [TestCase(65, 1)]
        [TestCase(129, 64)]
        [TestCase(129, 65)]
        [TestCase(4097, 1)]
        public void LeftShiftDoesNotAcquireADisposalLease(int capacity, int shift)
        {
            BitSet bits = new BitSet(capacity);
            bits.SetAll();
            bits.LeftShift(shift);
            DisposalLease probe = DisposalLeases.Acquire();
            try
            {
                int slot = probe._slot;
                bool released = probe.TryClaim();
                long before = DisposalLeases.CurrentGeneration(slot);
                bits.SetAll();
                bits.LeftShift(shift);
                long after = DisposalLeases.CurrentGeneration(slot);
                Assert.IsTrue(released);
                Assert.AreEqual(capacity - shift, bits.CountSetBits());
                Assert.AreEqual(before, after, "Left shift acquired and claimed a disposal lease.");
            }
            finally
            {
                probe.TryClaim();
            }
        }

        [Test]
        public void LeftShiftLeaseObservationDetectsBalancedLeaseWork()
        {
            DisposalLease probe = DisposalLeases.Acquire();
            DisposalLease balanced = default;
            try
            {
                int slot = probe._slot;
                bool released = probe.TryClaim();
                long before = DisposalLeases.CurrentGeneration(slot);
                balanced = DisposalLeases.Acquire();
                int reusedSlot = balanced._slot;
                bool balancedReleased = balanced.TryClaim();
                long after = DisposalLeases.CurrentGeneration(slot);
                Assert.IsTrue(released);
                Assert.IsTrue(balancedReleased);
                Assert.AreEqual(slot, reusedSlot);
                Assert.AreEqual(before + 2, after);
            }
            finally
            {
                balanced.TryClaim();
                probe.TryClaim();
            }
        }
    }
}
