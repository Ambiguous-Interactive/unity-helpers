// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins what a field that appears more than once decodes to, against protobuf-net 2.4.9 and
    /// 3.2.56.
    /// </summary>
    /// <remarks>
    /// <para>
    /// protobuf says a parser "merges multiple instances of the same field, as if with
    /// <c>Message::MergeFrom</c>", and a payload carrying one is legal rather than corrupt -- it is
    /// what concatenating two encodings of the same message produces, which the spec makes a
    /// supported operation. A reader that takes the last occurrence instead loses the members only
    /// the first one carried, in silence, and the caller has no way to tell.
    /// </para>
    /// <para>
    /// The rule is not the same for every shape, so each is asked of the oracle rather than reasoned
    /// about: a non-repeated <b>scalar</b> is last-wins, a <b>sub-message</b> merges, and a
    /// <b>struct</b> sub-message merges exactly as a reference one does -- measured, not assumed.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class DuplicateFieldDifferentialTests
    {
        [Test]
        public void ADuplicatedSubMessageMergesRatherThanReplacing()
        {
            // The payload the defect was found on: field 2 is a message, the first occurrence sets
            // A and the second sets B. Replacing yields A=0, which is data loss on legal bytes.
            const string twice = "12020801" + "12021002";

            DuplicateHolder oracle = OracleDecode<DuplicateHolder>(twice);
            DuplicateHolder ours = Decode<DuplicateHolder>(twice);

            Assert.AreEqual(1, oracle.Child.A);
            Assert.AreEqual(2, oracle.Child.B);
            Assert.AreEqual(oracle.Child.A, ours.Child.A, twice);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, twice);
        }

        [Test]
        public void ADuplicatedStructSubMessageMergesLikeAReferenceOne()
        {
            // A struct member cannot be null, so "merge into the existing instance" has no obvious
            // meaning for it. protobuf-net answers that it merges anyway, and this is that
            // measurement rather than a decision made here.
            const string twice = "1A020801" + "1A021002";

            DuplicateHolder oracle = OracleDecode<DuplicateHolder>(twice);
            DuplicateHolder ours = Decode<DuplicateHolder>(twice);

            Assert.AreEqual(1, oracle.Where.X);
            Assert.AreEqual(2, oracle.Where.Y);
            Assert.AreEqual(oracle.Where.X, ours.Where.X, twice);
            Assert.AreEqual(oracle.Where.Y, ours.Where.Y, twice);
        }

        [Test]
        public void ADuplicatedNonRepeatedScalarIsLastWins()
        {
            // The other half of the rule, and the one no payload in either suite covered: a scalar
            // does NOT merge, it replaces. Both readers take 5.
            const string twice = "0804" + "0805";

            Assert.AreEqual(5, OracleDecode<DuplicateHolder>(twice).Number);
            Assert.AreEqual(5, Decode<DuplicateHolder>(twice).Number);

            const string thrice = "0804" + "0805" + "0806";
            Assert.AreEqual(
                OracleDecode<DuplicateHolder>(thrice).Number,
                Decode<DuplicateHolder>(thrice).Number,
                thrice
            );
        }

        [Test]
        public void AMergedSubMessageTakesTheLastOccurrenceOfEachScalarWithinIt()
        {
            // Merge is not "union": inside the merged message the ordinary rule applies again, so a
            // member both occurrences carry takes the later value.
            const string twice = "12031A0161" + "12031A0162";

            Assert.AreEqual("b", OracleDecode<DuplicateHolder>(twice).Child.Text);
            Assert.AreEqual("b", Decode<DuplicateHolder>(twice).Child.Text);

            const string mixed = "12031A0161" + "12021002";
            DuplicateHolder oracle = OracleDecode<DuplicateHolder>(mixed);
            DuplicateHolder ours = Decode<DuplicateHolder>(mixed);
            Assert.AreEqual("a", oracle.Child.Text);
            Assert.AreEqual(2, oracle.Child.B);
            Assert.AreEqual(oracle.Child.Text, ours.Child.Text, mixed);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, mixed);
        }

        [Test]
        public void AMergeReachesEveryLevelOfTheMessage()
        {
            // The grandparent's field 1 is duplicated, and the merge of its two payloads duplicates
            // the holder's field 2 in turn. Nothing here merges the inner message explicitly: the
            // outer merge produces bytes in which the inner field appears twice, and the same rule
            // applies again one level down.
            const string twice = "0A04" + "12020801" + "0A04" + "12021002";

            DuplicateGrandparent oracle = OracleDecode<DuplicateGrandparent>(twice);
            DuplicateGrandparent ours = Decode<DuplicateGrandparent>(twice);

            Assert.AreEqual(1, oracle.Holder.Child.A);
            Assert.AreEqual(2, oracle.Holder.Child.B);
            Assert.AreEqual(oracle.Holder.Child.A, ours.Holder.Child.A, twice);
            Assert.AreEqual(oracle.Holder.Child.B, ours.Holder.Child.B, twice);
        }

        [Test]
        public void ThreeOccurrencesMergeAsOne()
        {
            const string thrice = "12020801" + "12021002" + "12031A0163";

            DuplicateHolder oracle = OracleDecode<DuplicateHolder>(thrice);
            DuplicateHolder ours = Decode<DuplicateHolder>(thrice);

            Assert.AreEqual(1, oracle.Child.A);
            Assert.AreEqual(2, oracle.Child.B);
            Assert.AreEqual("c", oracle.Child.Text);
            Assert.AreEqual(oracle.Child.A, ours.Child.A, thrice);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, thrice);
            Assert.AreEqual(oracle.Child.Text, ours.Child.Text, thrice);
        }

        [Test]
        public void AnEveryShapeAtOncePayloadAgreesWithTheOracle()
        {
            // Scalar, reference sub-message and struct sub-message duplicated in one payload, with
            // the occurrences interleaved rather than adjacent -- which is the arrangement a merge
            // implemented as "remember the last one" would still get right by accident.
            const string mixed =
                "0804" + "12020801" + "1A020801" + "0805" + "12021002" + "1A021002";

            DuplicateHolder oracle = OracleDecode<DuplicateHolder>(mixed);
            DuplicateHolder ours = Decode<DuplicateHolder>(mixed);

            Assert.AreEqual(5, oracle.Number);
            Assert.AreEqual(1, oracle.Child.A);
            Assert.AreEqual(2, oracle.Child.B);
            Assert.AreEqual(1, oracle.Where.X);
            Assert.AreEqual(2, oracle.Where.Y);

            Assert.AreEqual(oracle.Number, ours.Number, mixed);
            Assert.AreEqual(oracle.Child.A, ours.Child.A, mixed);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, mixed);
            Assert.AreEqual(oracle.Where.X, ours.Where.X, mixed);
            Assert.AreEqual(oracle.Where.Y, ours.Where.Y, mixed);
        }

        [Test]
        public void ATruncatedLaterOccurrenceIsRefusedRatherThanMerged()
        {
            // Accumulating occurrences must not turn a truncated payload into a partial value. Both
            // readers refuse these; protobuf-net by throwing, which is what this package returns
            // false for instead.
            foreach (string hex in new[] { "12020801" + "1202", "12020801" + "120210" })
            {
                Assert.IsFalse(OracleAccepts<DuplicateHolder>(hex), hex);

                WProtoReader reader = new WProtoReader(Parse(hex));
                Assert.IsFalse(
                    WProtoFormatterProvider
                        .Get<DuplicateHolder>()
                        .TryRead(ref reader, out DuplicateHolder _),
                    hex
                );
            }
        }

        [Test]
        public void TheFirstOccurrenceMergesIntoTheConstructorsSubMessage()
        {
            // protobuf defines reading a sub-message field as MergeFrom, so the FIRST occurrence
            // merges into whatever the contract's constructor left on the member. Decoding into a
            // fresh instance instead drops the seed, on a payload that says nothing about it.
            const string once = "12021002";

            SeededHolder oracle = OracleDecode<SeededHolder>(once);
            SeededHolder ours = Decode<SeededHolder>(once);

            Assert.AreEqual(9, oracle.Child.A);
            Assert.AreEqual(2, oracle.Child.B);
            Assert.AreEqual(oracle.Child.A, ours.Child.A, once);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, once);

            // A payload that DOES mention the member overrides the seed rather than combining with
            // it -- merge is per member, not per value.
            const string twice = "12021002" + "12020801";
            Assert.AreEqual(
                OracleDecode<SeededHolder>(twice).Child.A,
                Decode<SeededHolder>(twice).Child.A,
                twice
            );

            // And a payload that never mentions the field at all leaves the seed entirely alone.
            Assert.AreEqual(9, Decode<SeededHolder>("0807").Child.A);
            Assert.AreEqual(0, Decode<SeededHolder>("0807").Child.B);
        }

        [Test]
        public void EverySeededMemberShapeAgreesWithTheOracle()
        {
            // One payload per shape, each setting the member the seed does NOT set, so "merged" and
            // "replaced" produce different answers for all four.
            const string reference = "0A021002";
            const string structural = "12021002";
            const string nullable = "1A021002";
            const string surrogate = "2205150000" + "0040";

            Assert.AreEqual(
                OracleDecode<SeededShapes>(reference).Reference.A,
                Decode<SeededShapes>(reference).Reference.A,
                reference
            );
            Assert.AreEqual(
                OracleDecode<SeededShapes>(structural).Where.X,
                Decode<SeededShapes>(structural).Where.X,
                structural
            );
            Assert.AreEqual(
                OracleDecode<SeededShapes>(nullable).Maybe.Value.X,
                Decode<SeededShapes>(nullable).Maybe.Value.X,
                nullable
            );
            Assert.AreEqual(
                OracleDecode<SeededShapes>(surrogate).Vector.x,
                Decode<SeededShapes>(surrogate).Vector.x,
                surrogate
            );

            // The member each payload DID set arrives whatever the seeding rule is.
            Assert.AreEqual(2, Decode<SeededShapes>(reference).Reference.B);
            Assert.AreEqual(2, Decode<SeededShapes>(structural).Where.Y);
            Assert.AreEqual(2, Decode<SeededShapes>(nullable).Maybe.Value.Y);
            Assert.AreEqual(2f, Decode<SeededShapes>(surrogate).Vector.y);
        }

        [Test]
        public void ASkipConstructorContractIgnoresTheSeedItsInitializerLeft()
        {
            // protobuf-net allocates this one uninitialized, so its member has no seed at all. This
            // package's generated read constructor necessarily runs field initializers, so the seed
            // exists and must be ignored anyway -- the rule a repeated member already follows.
            const string once = "0A021002";

            SeededSkipHolder oracle = OracleDecode<SeededSkipHolder>(once);
            SeededSkipHolder ours = Decode<SeededSkipHolder>(once);

            Assert.AreEqual(0, oracle.Child.A);
            Assert.AreEqual(oracle.Child.A, ours.Child.A, once);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, once);

            // The same rule for a repeated member, which is where the flag was already being
            // ignored: a contract declaring SkipConstructor and no constructor of its own kept the
            // initializer and appended to it, where the oracle's uninitialized instance has none.
            const string values = "10011002";
            CollectionAssert.AreEqual(
                OracleDecode<SeededSkipHolder>(values).Values,
                Decode<SeededSkipHolder>(values).Values,
                values
            );
            CollectionAssert.AreEqual(new[] { 1, 2 }, Decode<SeededSkipHolder>(values).Values);
        }

        [Test]
        public void AGenericMemberMergesIntoItsConstructorsSeed()
        {
            // The generic path reaches the same merge through WProtoGeneric<T>, whose closure is the
            // only thing that knows the member is a message at all.
            const string once = "0A021002";

            SeededBox<SeededChild> oracle = OracleDecode<SeededBox<SeededChild>>(once);
            SeededBox<SeededChild> ours = Decode<SeededBox<SeededChild>>(once);

            Assert.AreEqual(9, oracle.Value.A);
            Assert.AreEqual(2, oracle.Value.B);
            Assert.AreEqual(oracle.Value.A, ours.Value.A, once);
            Assert.AreEqual(oracle.Value.B, ours.Value.B, once);
        }

        [Test]
        public void MergingDoesNotWeakenTheNestingBound()
        {
            // The merge accumulates payloads and decodes them after the read loop, so the level a
            // sub-message costs is spent at the decode rather than at the read. It is still spent:
            // a chain past the bound is refused, and one within it is not.
            Assert.IsTrue(TryDecodeChain(WProtoReader.MaxNestingDepth - 1));
            Assert.IsFalse(TryDecodeChain(WProtoReader.MaxNestingDepth + 8));
        }

        [Test]
        public void AMergedSubMessageRunsItsAfterDeserializationHookOnce()
        {
            // The reason the merge concatenates payloads instead of decoding one occurrence into the
            // instance another produced: there is exactly one decode, so there is exactly one hook.
            int before = HookedContract.AfterDeserializationRuns;

            NestingContract decoded = Decode<NestingContract>("12020801" + "12020802");

            Assert.AreEqual(2, decoded.Child.Value);
            Assert.AreEqual(
                1,
                HookedContract.AfterDeserializationRuns - before,
                "a merged member must not run its lifecycle hooks once per occurrence"
            );
        }

        [Test]
        public void ADuplicatedSubMessageMergesThroughAGenericMember()
        {
            // The same merge one level of indirection away: the member's type is the contract's own
            // type parameter, so whether it is a sub-message at all is a property of the closure and
            // not of the emitted code. A reference closure first.
            const string twice = "0A020801" + "0A021002";

            Box<DuplicateChild> oracle = OracleDecode<Box<DuplicateChild>>(twice);
            Box<DuplicateChild> ours = Decode<Box<DuplicateChild>>(twice);

            Assert.AreEqual(1, oracle.Value.A);
            Assert.AreEqual(2, oracle.Value.B);
            Assert.AreEqual(oracle.Value.A, ours.Value.A, twice);
            Assert.AreEqual(oracle.Value.B, ours.Value.B, twice);
        }

        [Test]
        public void ADuplicatedStructSubMessageMergesThroughAGenericMember()
        {
            // The struct closure, where "merge into the existing instance" has no obvious meaning
            // and protobuf-net merges anyway -- the same answer its non-generic counterpart gives.
            const string twice = "0A020801" + "0A021002";

            Box<Outer.Point> oracle = OracleDecode<Box<Outer.Point>>(twice);
            Box<Outer.Point> ours = Decode<Box<Outer.Point>>(twice);

            Assert.AreEqual(1, oracle.Value.X);
            Assert.AreEqual(2, oracle.Value.Y);
            Assert.AreEqual(oracle.Value.X, ours.Value.X, twice);
            Assert.AreEqual(oracle.Value.Y, ours.Value.Y, twice);
        }

        [Test]
        public void ADuplicatedGenericScalarIsStillLastWins()
        {
            // The discriminator the branch has to get right. A string closure is length-delimited
            // too, so a reader that decided to merge from the WIRE type rather than from whether the
            // closure is message-shaped would concatenate two strings into one.
            const string texts = "0A0161" + "0A0162";
            Assert.AreEqual("b", OracleDecode<Box<string>>(texts).Value);
            Assert.AreEqual("b", Decode<Box<string>>(texts).Value, texts);

            const string numbers = "0801" + "0802";
            Assert.AreEqual(2, OracleDecode<Box<int>>(numbers).Value);
            Assert.AreEqual(2, Decode<Box<int>>(numbers).Value, numbers);
        }

        [Test]
        public void AMergedGenericSubMessageRunsItsAfterDeserializationHookOnce()
        {
            // The generic path accumulates and decodes once for the same reason the emitted one
            // does: two occurrences are one value, so they are one decode and one hook.
            int before = HookedContract.AfterDeserializationRuns;

            Box<HookedContract> decoded = Decode<Box<HookedContract>>("0A020801" + "0A020802");

            Assert.AreEqual(2, decoded.Value.Value);
            Assert.AreEqual(
                1,
                HookedContract.AfterDeserializationRuns - before,
                "a merged generic member must not run its lifecycle hooks once per occurrence"
            );
        }

        [Test]
        public void ATruncatedLaterOccurrenceOfAGenericSubMessageIsRefused()
        {
            // Accumulating must not turn a truncated payload into a partial value here either.
            foreach (string hex in new[] { "0A020801" + "0A02", "0A020801" + "0A0210" })
            {
                Assert.IsFalse(OracleAccepts<Box<DuplicateChild>>(hex), hex);

                WProtoReader reader = new WProtoReader(Parse(hex));
                Assert.IsFalse(
                    WProtoFormatterProvider
                        .Get<Box<DuplicateChild>>()
                        .TryRead(ref reader, out Box<DuplicateChild> _),
                    hex
                );
            }
        }

        private static bool TryDecodeChain(int links)
        {
            List<byte> payload = new List<byte>();
            for (int level = 0; level < links; level++)
            {
                List<byte> wrapped = new List<byte> { 0x12 };
                int length = payload.Count;
                while (0x7F < length)
                {
                    wrapped.Add((byte)((length & 0x7F) | 0x80));
                    length >>= 7;
                }

                wrapped.Add((byte)length);
                wrapped.AddRange(payload);
                payload = wrapped;
            }

            WProtoReader reader = new WProtoReader(payload.ToArray());
            return WProtoFormatterProvider
                .Get<ChainContract>()
                .TryRead(ref reader, out ChainContract _);
        }

        private static byte[] Parse(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private static T Decode<T>(string hex)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T value), hex);
            return value;
        }

        private static bool OracleAccepts<T>(string hex)
        {
            try
            {
                OracleDecode<T>(hex);
                return true;
            }
            catch (Exception)
            {
                // protobuf-net reports a malformed payload by throwing; this package returns false.
                // What is being compared is the verdict, not how it is delivered.
                return false;
            }
        }

        private static T OracleDecode<T>(string hex)
        {
            using (MemoryStream stream = new MemoryStream(Parse(hex)))
            {
                return ProtoBuf.Serializer.Deserialize<T>(stream);
            }
        }
    }
}
