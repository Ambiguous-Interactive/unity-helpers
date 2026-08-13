// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.Serialization;

    /// <summary>
    /// Pins what a deserializer will allocate for a capacity a payload only claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="HostileCapacityClaim"/> is the whole attack: six bytes, field 2 of the collection
    /// wrappers, holding <see cref="int.MaxValue"/> and no elements at all. Honored literally it is
    /// <c>new T[2147483647]</c> -- 8 GB for a deque, twice that for a sparse set's two index arrays
    /// -- which is an out-of-memory crash in a shipped player, from a save file smaller than this
    /// sentence.
    /// </para>
    /// <para>
    /// A length prefix cannot do this, because a reader refuses one longer than the bytes it holds.
    /// A capacity has nothing behind it, which is why it is treated as a claim.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class SerializationCapacityLimitTests
    {
        // Field 2 (varint) = int.MaxValue, no field 1. The wrapper shape shared by Deque and
        // SparseSet, so the same six bytes attack both.
        private static readonly byte[] HostileCapacityClaim =
        {
            0x10,
            0xFF,
            0xFF,
            0xFF,
            0xFF,
            0x07,
        };

        [TearDown]
        public void RestoreTheLimit()
        {
            SerializationCapacityLimits.ResetMaximumRestoredCapacity();
        }

        [Test]
        public void ADequeCapacityClaimIsClampedRatherThanAllocated()
        {
            Deque<int> restored = Serializer.ProtoDeserialize<Deque<int>>(HostileCapacityClaim);

            Assert.IsTrue(restored != null);
            Assert.AreEqual(0, restored.Count);
            Assert.LessOrEqual(
                restored.Capacity,
                SerializationCapacityLimits.DefaultMaximumRestoredCapacity,
                "A payload that delivered no elements must not size a buffer beyond the limit."
            );

            // Clamping is not truncation: the deque still works, and grows past the clamp on demand.
            for (int index = 0; index < 32; index++)
            {
                restored.PushBack(index);
            }

            Assert.AreEqual(32, restored.Count);
            Assert.AreEqual(0, restored[0]);
            Assert.AreEqual(31, restored[31]);
        }

        [Test]
        public void ASparseSetCapacityClaimIsRefusedRatherThanAllocated()
        {
            // Refused rather than clamped: a sparse set's capacity is the universe it will accept
            // elements from, so shrinking it silently would change what the restored set does.
            Assert.Catch<Exception>(() =>
                Serializer.ProtoDeserialize<SparseSet>(HostileCapacityClaim)
            );
        }

        [Test]
        public void ACapacityWithinTheLimitStillRoundTrips()
        {
            Deque<int> deque = new(1000);
            deque.PushBack(7);
            deque.PushBack(8);
            deque.PushBack(9);

            Deque<int> restoredDeque = Serializer.ProtoDeserialize<Deque<int>>(
                Serializer.ProtoSerialize(deque)
            );
            Assert.AreEqual(1000, restoredDeque.Capacity);
            Assert.AreEqual(3, restoredDeque.Count);
            Assert.AreEqual(7, restoredDeque[0]);
            Assert.AreEqual(9, restoredDeque[2]);

            SparseSet sparse = new(1000);
            Assert.IsTrue(sparse.TryAdd(1));
            Assert.IsTrue(sparse.TryAdd(999));

            SparseSet restoredSparse = Serializer.ProtoDeserialize<SparseSet>(
                Serializer.ProtoSerialize(sparse)
            );
            Assert.AreEqual(1000, restoredSparse.Capacity);
            Assert.IsTrue(restoredSparse.Contains(1));
            Assert.IsTrue(restoredSparse.Contains(999));
        }

        [Test]
        public void RaisingTheLimitHonorsALargerClaim()
        {
            // The knob exists because the decision belongs to the consuming game, which knows how
            // big its own saves are, rather than to the payload.
            SerializationCapacityLimits.MaximumRestoredCapacity = 4_000_000;

            Deque<int> restored = Serializer.ProtoDeserialize<Deque<int>>(
                Serializer.ProtoSerialize(new Deque<int>(2_000_000))
            );
            Assert.AreEqual(2_000_000, restored.Capacity);
        }

        [Test]
        public void ABitSetCapacityClaimIsClampedAndEveryBitSurvives()
        {
            BitSet restored = Serializer.JsonDeserialize<BitSet>(
                "{\"capacity\":2000000000,\"setIndices\":[1,5,63]}"
            );

            Assert.IsTrue(restored != null);
            Assert.IsTrue(restored.TryGet(1, out bool first));
            Assert.IsTrue(first);
            Assert.IsTrue(restored.TryGet(5, out bool second));
            Assert.IsTrue(second);
            Assert.IsTrue(restored.TryGet(63, out bool third));
            Assert.IsTrue(third);
            Assert.IsTrue(restored.TryGet(2, out bool unset));
            Assert.IsFalse(unset);
        }

        [Test]
        public void ABitSetIndexBeyondTheLimitIsRefusedRatherThanAllocated()
        {
            // An index is data rather than a claim, so it cannot be dropped -- but a dense bit set
            // holding index two billion is 250 MB, which a document of a few bytes must not buy.
            Assert.Catch<Exception>(() =>
                Serializer.JsonDeserialize<BitSet>("{\"capacity\":0,\"setIndices\":[2000000000]}")
            );
        }

        [TestCase(0, 0, 0)]
        [TestCase(-5, 3, 3)]
        [TestCase(10, 3, 10)]
        [TestCase(int.MaxValue, 0, SerializationCapacityLimits.DefaultMaximumRestoredCapacity)]
        [TestCase(
            int.MaxValue,
            SerializationCapacityLimits.DefaultMaximumRestoredCapacity + 10,
            SerializationCapacityLimits.DefaultMaximumRestoredCapacity + 10
        )]
        public void ClampKeepsEveryDeliveredElementAndNothingMore(
            int stated,
            int delivered,
            int expected
        )
        {
            // The delivered count is a floor rather than a cap: those elements arrived as bytes and
            // have to fit, however large they are.
            Assert.AreEqual(expected, SerializationCapacityLimits.Clamp(stated, delivered));
        }

        [TestCase(10, 0, true, 10)]
        [TestCase(
            SerializationCapacityLimits.DefaultMaximumRestoredCapacity,
            0,
            true,
            SerializationCapacityLimits.DefaultMaximumRestoredCapacity
        )]
        [TestCase(SerializationCapacityLimits.DefaultMaximumRestoredCapacity + 1, 0, false, 0)]
        [TestCase(int.MaxValue, 0, false, 0)]
        public void TryAcceptRefusesOnlyWhatExceedsTheLimit(
            int stated,
            int delivered,
            bool expectedAccepted,
            int expectedCapacity
        )
        {
            Assert.AreEqual(
                expectedAccepted,
                SerializationCapacityLimits.TryAccept(stated, delivered, out int capacity)
            );
            Assert.AreEqual(expectedCapacity, capacity);
        }

        [Test]
        public void TheLimitCannotBeSetBelowOne()
        {
            SerializationCapacityLimits.MaximumRestoredCapacity = -100;
            Assert.AreEqual(1, SerializationCapacityLimits.MaximumRestoredCapacity);
        }
    }
}
