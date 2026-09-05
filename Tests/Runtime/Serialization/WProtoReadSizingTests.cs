// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the three pieces that let a repeated member's read size its destination once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Growing an accumulator is what made reading a repeated field cost more than the value it
    /// produced: 128 <c>int</c>s decoded into an array allocated 1,744 bytes to return a 560-byte
    /// graph, against protobuf-net's 560, because every doubling left its predecessor behind. A
    /// packed run already carries the answer -- <see cref="WProtoReader.CountPackedElements"/> reads
    /// it off the bytes -- and these types are what spend it.
    /// </para>
    /// <para>
    /// The count has to be exact rather than an upper bound, because it is allocated from. That is
    /// the property most of these cases exist to hold.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoReadSizingTests
    {
        private const int ScratchSize = 4096;
        private const int MaximumVarint32Bytes = 10;

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(17)]
        [TestCase(128)]
        [TestCase(1000)]
        public void AVarintRunCountsItsElementsExactly(int count)
        {
            /*
                Deliberately mixed widths: a one-byte element and a five-byte one must both count once, which is
                the whole difference between counting elements and counting bytes.
            */
            int[] values = new int[count];
            for (int index = 0; index < count; index++)
            {
                values[index] = index % 3 == 0 ? index : -index;
            }

            WProtoReader packed = PackedVarints(values);
            Assert.AreEqual(count, packed.CountPackedElements(WProtoWireType.Varint));
            for (int index = 0; index < count; index++)
            {
                Assert.IsTrue(packed.TryReadInt32(out int decoded));
                Assert.AreEqual(values[index], decoded);
            }

            Assert.IsTrue(packed.End);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(64)]
        public void AFixedWidthRunCountsItsElementsWithoutWalkingThem(int count)
        {
            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            for (int index = 0; index < count; index++)
            {
                Assert.IsTrue(writer.TryWriteFixed32((uint)index));
            }

            WProtoReader thirtyTwo = new(writer.Written);
            Assert.AreEqual(count, thirtyTwo.CountPackedElements(WProtoWireType.Fixed32));

            writer = new WProtoWriter(scratch);
            for (int index = 0; index < count; index++)
            {
                Assert.IsTrue(writer.TryWriteFixed64((ulong)index));
            }

            WProtoReader sixtyFour = new(writer.Written);
            Assert.AreEqual(count, sixtyFour.CountPackedElements(WProtoWireType.Fixed64));
        }

        [TestCase(WProtoWireType.LengthDelimited)]
        [TestCase(WProtoWireType.StartGroup)]
        [TestCase(WProtoWireType.EndGroup)]
        [TestCase(-1)]
        [TestCase(7)]
        public void AWireTypeThatCannotBePackedCountsNothing(int wireType)
        {
            WProtoReader packed = PackedVarints(new[] { 1, 2, 3 });
            Assert.AreEqual(0, packed.CountPackedElements(wireType));
        }

        [Test]
        public void CountingReportsWhatIsLeftRatherThanWhatArrived()
        {
            WProtoReader packed = PackedVarints(new[] { 1, 2, 3, 4 });
            Assert.AreEqual(4, packed.CountPackedElements(WProtoWireType.Varint));
            Assert.IsTrue(packed.TryReadInt32(out int _));
            Assert.IsTrue(packed.TryReadInt32(out int _));
            Assert.AreEqual(2, packed.CountPackedElements(WProtoWireType.Varint));
        }

        [Test]
        public void ATruncatedTrailingVarintIsNotCounted()
        {
            /*
                Under-counting costs one grow; over-counting would size a buffer from bytes that are not there.
                The read of the same bytes fails either way, which is what makes the conservative answer the
                right one.
            */
            WProtoReader packed = new(new byte[] { 0x01, 0x02, 0x80 });
            Assert.AreEqual(2, packed.CountPackedElements(WProtoWireType.Varint));
        }

        [Test]
        public void AFailedReaderCountsNothing()
        {
            WProtoReader reader = new(new byte[] { 0xFF });
            Assert.IsFalse(reader.TryReadTag(out int _, out int _));
            Assert.IsTrue(reader.Malformed);
            Assert.AreEqual(0, reader.CountPackedElements(WProtoWireType.Varint));
        }

        [Test]
        public void AnEmptyRunCountsNothing()
        {
            WProtoReader packed = new(Array.Empty<byte>());
            Assert.AreEqual(0, packed.CountPackedElements(WProtoWireType.Varint));
        }

        [Test]
        public void AnExactlyReservedBuilderHandsOverItsBufferRatherThanCopyingIt()
        {
            WProtoArrayBuilder<int> builder = default;
            builder.Reserve(3);
            builder.Add(7);
            builder.Add(8);
            builder.Add(9);

            int[] first = builder.ToArray();
            CollectionAssert.AreEqual(new[] { 7, 8, 9 }, first);
            Assert.AreSame(
                first,
                builder.ToArray(),
                "An exactly filled builder must return its own buffer; copying it is the allocation "
                    + "reserving an exact count exists to remove."
            );
        }

        [Test]
        public void AnOverReservedBuilderReturnsExactlyWhatWasAdded()
        {
            WProtoArrayBuilder<int> builder = default;
            builder.Reserve(8);
            builder.Add(1);
            builder.Add(2);

            int[] produced = builder.ToArray();
            CollectionAssert.AreEqual(new[] { 1, 2 }, produced);
            Assert.AreEqual(2, builder.Count);
        }

        [Test]
        public void ABuilderWithNoReservationStillCollectsEverything()
        {
            WProtoArrayBuilder<int> builder = default;
            for (int index = 0; index < 100; index++)
            {
                builder.Add(index * 3);
            }

            int[] produced = builder.ToArray();
            Assert.AreEqual(100, produced.Length);
            for (int index = 0; index < produced.Length; index++)
            {
                Assert.AreEqual(index * 3, produced[index]);
            }
        }

        [Test]
        public void AnEmptyBuilderProducesAnEmptyArrayRatherThanNull()
        {
            WProtoArrayBuilder<string> builder = default;
            Assert.AreEqual(0, builder.Count);
            Assert.IsTrue(builder.ToArray() != null);
            Assert.AreEqual(0, builder.ToArray().Length);
        }

        [Test]
        public void ASeededBuilderCopiesWhatItWasGiven()
        {
            int[] seed = { 1, 2, 3 };
            WProtoArrayBuilder<int> builder = new(seed);
            seed[0] = 99;
            builder.Add(4);

            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, builder.ToArray());
        }

        [Test]
        public void BulkAddsAppendAndIgnoreNothing()
        {
            WProtoArrayBuilder<int> builder = new(null);
            builder.AddRange((int[])null);
            builder.AddRange(Array.Empty<int>());
            builder.AddRange((List<int>)null);
            builder.AddRange(new List<int>());
            Assert.AreEqual(0, builder.Count);

            builder.AddRange(new[] { 1, 2 });
            builder.AddRange(new List<int> { 3, 4 });
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, builder.ToArray());
        }

        [Test]
        public void ReservingAListSizesItOnceForEverythingItIsAboutToHold()
        {
            List<int> destination = new();
            WProtoRepeated.Reserve(destination, 128);
            Assert.AreEqual(128, destination.Capacity);

            destination.Add(1);
            destination.Add(2);
            WProtoRepeated.Reserve(destination, 200);
            Assert.GreaterOrEqual(destination.Capacity, 202);
        }

        /// <summary>
        /// Protobuf permits a repeated field as several runs and this reader accepts them
        /// interleaved, so the sizing hint arrives once per run. Sizing exactly each time
        /// reallocated and copied per run, which is quadratic in the run count rather than in the
        /// elements a payload delivers.
        /// </summary>
        [Test]
        public void ReservingOneElementAtATimeDoesNotReallocatePerRun()
        {
            const int runs = 4096;
            List<int> destination = new();
            int reallocations = 0;
            int lastCapacity = destination.Capacity;

            for (int run = 0; run < runs; ++run)
            {
                WProtoRepeated.Reserve(destination, 1);
                if (lastCapacity != destination.Capacity)
                {
                    reallocations++;
                    lastCapacity = destination.Capacity;
                }

                destination.Add(run);
            }

            Assert.AreEqual(runs, destination.Count);
            Assert.Greater(reallocations, 0, "the probe never grew, so it measured nothing");
            Assert.LessOrEqual(
                reallocations,
                32,
                $"{reallocations} reallocations for {runs} single-element runs is per-run growth"
            );
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(4)]
        public void ReservingNeverShrinksAListOrRefusesAnEmptyRequest(int additional)
        {
            List<int> destination = new(64) { 1, 2, 3 };
            WProtoRepeated.Reserve(destination, additional);
            Assert.AreEqual(64, destination.Capacity);
            Assert.AreEqual(3, destination.Count);
        }

        [Test]
        public void ReservingANullListIsIgnoredRatherThanThrowing()
        {
            Assert.DoesNotThrow(() => WProtoRepeated.Reserve((List<int>)null, 8));
        }

        private static WProtoReader PackedVarints(int[] values)
        {
            /*
                Size scratch space for ten-byte negative int32 varints; a fixed buffer formerly failed at 1,000
                elements.
            */
            byte[] scratch = new byte[(values.Length * MaximumVarint32Bytes) + 1];
            WProtoWriter writer = new(scratch);
            foreach (int value in values)
            {
                Assert.IsTrue(writer.TryWriteInt32(value));
            }

            return new WProtoReader(writer.Written);
        }
    }
}
