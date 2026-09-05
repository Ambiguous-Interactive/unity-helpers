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
            // Interleaving duplicate occurrences prevents a last-adjacent-value shortcut from passing.
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
            // The first sub-message occurrence must merge into constructor-provided members.
            const string once = "12021002";

            SeededHolder oracle = OracleDecode<SeededHolder>(once);
            SeededHolder ours = Decode<SeededHolder>(once);

            Assert.AreEqual(9, oracle.Child.A);
            Assert.AreEqual(2, oracle.Child.B);
            Assert.AreEqual(oracle.Child.A, ours.Child.A, once);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, once);

            const string twice = "12021002" + "12020801";
            Assert.AreEqual(
                OracleDecode<SeededHolder>(twice).Child.A,
                Decode<SeededHolder>(twice).Child.A,
                twice
            );

            Assert.AreEqual(9, Decode<SeededHolder>("0807").Child.A);
            Assert.AreEqual(0, Decode<SeededHolder>("0807").Child.B);
        }

        [Test]
        public void AnImmutableContractsSubMessageMergesIntoItsSeed()
        {
            // Immutable construction must preserve seeded members absent from the payload.
            const string once = "0A021002";

            SeededImmutableHolder oracle = OracleDecode<SeededImmutableHolder>(once);
            SeededImmutableHolder ours = Decode<SeededImmutableHolder>(once);

            Assert.AreEqual(9, oracle.Child.A, once);
            Assert.AreEqual(2, oracle.Child.B, once);
            Assert.AreEqual(oracle.Child.A, ours.Child.A, once);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, once);

            Assert.AreEqual(9, Decode<SeededImmutableHolder>(string.Empty).Child.A);
        }

        [Test]
        public void EveryMemberKindOnAnImmutableContractKeepsItsConstructorsSeed()
        {
            /*
             * Each member shape combines constructor seeds differently: messages merge, sequences append, and
             * maps union by key.
             */
            string[] payloads = { "0A021002", "12021002", "1A0101", "22040800100D" };

            foreach (string payload in payloads)
            {
                SeededImmutableShapes oracle = OracleDecode<SeededImmutableShapes>(payload);
                SeededImmutableShapes ours = Decode<SeededImmutableShapes>(payload);

                Assert.AreEqual(oracle.Reference.A, ours.Reference.A, payload);
                Assert.AreEqual(oracle.Reference.B, ours.Reference.B, payload);
                Assert.AreEqual(oracle.Where.X, ours.Where.X, payload);
                Assert.AreEqual(oracle.Where.Y, ours.Where.Y, payload);
                CollectionAssert.AreEqual(oracle.Values, ours.Values, payload);
                CollectionAssert.AreEquivalent(oracle.Map, ours.Map, payload);

                Assert.AreEqual(9, ours.Reference.A, payload);
                Assert.AreEqual(9, ours.Where.X, payload);
                Assert.AreEqual(99, ours.Values[0], payload);
                Assert.AreEqual(9, ours.Map[7], payload);
            }

            Assert.AreEqual(2, Decode<SeededImmutableShapes>(payloads[0]).Reference.B);
            Assert.AreEqual(2, Decode<SeededImmutableShapes>(payloads[1]).Where.Y);
            CollectionAssert.AreEqual(
                new[] { 99, 1 },
                Decode<SeededImmutableShapes>(payloads[2]).Values
            );
            Assert.AreEqual(13, Decode<SeededImmutableShapes>(payloads[3]).Map[0]);
        }

        [Test]
        public void SkipConstructorBeatsImmutabilityAndLeavesNoSeed()
        {
            /*
             * SkipConstructor wins over immutability; retaining field initializers would disagree with both
             * oracles.
             */
            const string merges = "0A021002";
            SeededSkipImmutableHolder oracle = OracleDecode<SeededSkipImmutableHolder>(merges);
            SeededSkipImmutableHolder ours = Decode<SeededSkipImmutableHolder>(merges);

            Assert.AreEqual(0, oracle.Child.A, merges);
            Assert.AreEqual(oracle.Child.A, ours.Child.A, merges);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, merges);
            Assert.AreEqual(oracle.Number, ours.Number, merges);

            const string absent = "1007";
            Assert.AreEqual(0, OracleDecode<SeededSkipImmutableHolder>(absent).Child?.A ?? 0);
            Assert.AreEqual(
                OracleDecode<SeededSkipImmutableHolder>(absent).Number,
                Decode<SeededSkipImmutableHolder>(absent).Number,
                absent
            );
        }

        [Test]
        public void AnImmutableContractWithNoParameterlessConstructorHasNoSeedToKeep()
        {
            // A parameterized-only constructor provides no seed instance, and the oracle refuses that shape.
            Assert.Throws<ProtoBuf.ProtoException>(() =>
                OracleDecode<UnseededImmutableHolder>("0A021002")
            );

            UnseededImmutableHolder ours = Decode<UnseededImmutableHolder>("0A021002");
            Assert.AreEqual(0, ours.Child.A);
            Assert.AreEqual(2, ours.Child.B);
        }

        [Test]
        public void AnImmutableContractKeepsTheParameterlessConstructorItsAuthorNeverWrote()
        {
            /*
             * Emitting a constructor removes the implicit parameterless constructor unless a replacement is
             * emitted too.
             */
            ImplicitlyConstructedImmutable made = new ImplicitlyConstructedImmutable();
            Assert.AreEqual(0, made.Id);

            Assert.AreEqual(7, OracleDecode<ImplicitlyConstructedImmutable>("0807").Id);
            Assert.AreEqual(7, Decode<ImplicitlyConstructedImmutable>("0807").Id);
        }

        [Test]
        public void EverySeededMemberShapeAgreesWithTheOracle()
        {
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

            Assert.AreEqual(2, Decode<SeededShapes>(reference).Reference.B);
            Assert.AreEqual(2, Decode<SeededShapes>(structural).Where.Y);
            Assert.AreEqual(2, Decode<SeededShapes>(nullable).Maybe.Value.Y);
            Assert.AreEqual(2f, Decode<SeededShapes>(surrogate).Vector.y);
        }

        [Test]
        public void ASkipConstructorContractIgnoresTheSeedItsInitializerLeft()
        {
            // Generated construction runs initializers that SkipConstructor requires the reader to ignore.
            const string once = "0A021002";

            SeededSkipHolder oracle = OracleDecode<SeededSkipHolder>(once);
            SeededSkipHolder ours = Decode<SeededSkipHolder>(once);

            Assert.AreEqual(0, oracle.Child.A);
            Assert.AreEqual(oracle.Child.A, ours.Child.A, once);
            Assert.AreEqual(oracle.Child.B, ours.Child.B, once);

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
            // Deferred decoding must still charge the sub-message nesting depth.
            Assert.IsTrue(TryDecodeChain(WProtoReader.MaxNestingDepth - 1));
            Assert.IsFalse(TryDecodeChain(WProtoReader.MaxNestingDepth + 8));
        }

        [Test]
        public void AMergedSubMessageRunsItsAfterDeserializationHookOnce()
        {
            // Concatenated occurrences decode once, so the deserialization hook runs once.
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
            // Strings are also length-delimited; wire type alone cannot decide whether to merge.
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

        [Test]
        public void ASkipConstructorContractMergesIntoASeedItsParentBuilt()
        {
            /*
             * SkipConstructor controls creation, not the validity of an instance already seeded by its
             * parent.
             */
            const string nested = "0A040A021002";

            SkipSeedParent oracle = OracleDecode<SkipSeedParent>(nested);
            SkipSeedParent ours = Decode<SkipSeedParent>(nested);

            Assert.AreEqual(oracle.Child.Child.A, ours.Child.Child.A, nested);
            Assert.AreEqual(oracle.Child.Child.B, ours.Child.Child.B, nested);

            const string values = "0A0410011002";
            CollectionAssert.AreEqual(
                OracleDecode<SkipSeedParent>(values).Child.Values,
                Decode<SkipSeedParent>(values).Child.Values,
                values
            );
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
                // Compare rejection verdicts; the oracle throws where this API returns false.
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
