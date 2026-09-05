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
        // Six bytes claim int.MaxValue capacity in both shared wrapper shapes.
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
            // Sparse-set capacity defines its accepted universe; clamping would silently change restored behavior.
            Assert.Catch<Exception>(() =>
                Serializer.ProtoDeserialize<SparseSet>(HostileCapacityClaim)
            );
        }

        /// <remarks>
        /// A <c>CyclicBuffer</c>'s stated capacity costs nothing to honor, which is why the three
        /// restore paths do not bound it the way they bound a deque's or a sparse set's.
        /// <c>CyclicBufferTests.IntMaxCapacityOk</c> is what pins that premise; if it ever stops
        /// holding, those paths become an 8 GB allocation from six bytes and have to gain a limit.
        /// </remarks>
        [Test]
        public void ACyclicBufferCapacityClaimCostsNothingToHonor()
        {
            CyclicBuffer<int> restored = Serializer.ProtoDeserialize<CyclicBuffer<int>>(
                HostileCapacityClaim
            );

            Assert.IsTrue(restored != null);
            Assert.AreEqual(0, restored.Count);
            restored.Add(3);
            Assert.AreEqual(1, restored.Count);
        }

        [Test]
        public void ACyclicBufferWithinTheLimitStillRoundTrips()
        {
            CyclicBuffer<int> buffer = new(4);
            for (int index = 0; index < 6; index++)
            {
                buffer.Add(index);
            }

            CyclicBuffer<int> restored = Serializer.ProtoDeserialize<CyclicBuffer<int>>(
                Serializer.ProtoSerialize(buffer)
            );

            Assert.AreEqual(4, restored.Capacity);
            Assert.AreEqual(4, restored.Count);
            Assert.AreEqual(2, restored[0]);
            Assert.AreEqual(5, restored[3]);
        }

        /// <remarks>
        /// <para>
        /// A bit set states its capacity and delivers its words as separate members, so a payload
        /// can claim more bits than it carries. Every read indexes the word array from an index the
        /// capacity admitted, so a capacity the words cannot cover turns <c>TryGet</c> and
        /// <c>All</c> into throwing members on a value the caller was handed successfully.
        /// </para>
        /// <para>
        /// Asserted through the constructor rather than a hand-patched payload. The constructor is
        /// where the invariant is established and its only non-test callers are
        /// <c>BitSet.ToImmutable</c>, which maintains it, and the deserialization surrogate, which
        /// restores the two members independently and so can disagree. A patched payload would
        /// instead be asserting the wire encoding, which is not the same on every backend -- the
        /// first draft of this test passed on Mono and failed on a stripped IL2CPP player for
        /// exactly that reason -- `ProtoBuf.Internal.StructValueChecker&lt;ImmutableBitSet&gt;` has
        /// no AOT code there at all, which is issue #696 rather than anything this asserts.
        /// </para>
        /// </remarks>
        [Test]
        public void AnImmutableBitSetCapacityIsBoundedByTheWordsItCarries()
        {
            ImmutableBitSet claimed = new(new ulong[] { 1UL }, int.MaxValue);

            Assert.AreEqual(
                64,
                claimed.Capacity,
                "One delivered word carries 64 bits, whatever the payload claims."
            );
            Assert.IsTrue(claimed[0]);
            Assert.IsFalse(claimed.TryGet(64, out bool _));
            Assert.DoesNotThrow(() => claimed.All());
            Assert.DoesNotThrow(() => claimed.TryGet(1_000_000, out bool _));
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
            // The game sets restoration limits independently of untrusted payload claims.
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

        [TestCase(2000000000)]
        [TestCase(int.MaxValue)]
        public void ABitSetIndexBeyondTheLimitIsRefusedRatherThanAllocated(int index)
        {
            /*
                A tiny document must not allocate hundreds of megabytes; int.MaxValue also exposes overflow in
                index-plus-one capacity calculations.
            */
            Exception refusal = Assert.Catch<Exception>(() =>
                Serializer.JsonDeserialize<BitSet>(
                    "{\"capacity\":0,\"setIndices\":[" + index + "]}"
                )
            );

            // Inspect the wrapped converter failure so the right rejection, rather than any exception, is proven.
            Assert.IsTrue(
                refusal.ToString().Contains("capacity of"),
                "The payload must be refused by the capacity limit, not by whatever throws first: "
                    + refusal
            );
        }

        [Test]
        public void ANegativeBitSetIndexIsIgnoredRatherThanCountedAsARequirement()
        {
            BitSet restored = Serializer.JsonDeserialize<BitSet>(
                "{\"capacity\":8,\"setIndices\":[-5,3]}"
            );

            Assert.IsTrue(restored != null);
            Assert.IsTrue(restored.TryGet(3, out bool set));
            Assert.IsTrue(set);
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
            /*
                The delivered count is a floor rather than a cap: those elements arrived as bytes and have to
                fit, however large they are.
            */
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
