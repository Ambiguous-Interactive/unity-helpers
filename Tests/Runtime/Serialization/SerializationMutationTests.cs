// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Mutates VALID payloads instead of generating random ones. Random fuzz explores the space a
    /// decoder refuses on sight; single-bit flips and truncations of a well-formed message explore
    /// the near-miss space where a decode bug actually lives, and every mutation is a potential
    /// kill the suite must survive honestly: never throw, and never answer success with the
    /// original value when the mutated bytes named a different one. The contract stays to scalar
    /// and string members: collection members route through serializers whose IL2CPP code was never
    /// generated for an unannotated test type (protobuf-net's repeated-field path, System.Text.Json's
    /// array converter), which fails the leg with an AOT exception before any mutation runs.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class SerializationMutationTests
    {
        [Test]
        public void EverySingleBitFlipOfAValidProtoPayloadNeverThrows()
        {
            byte[] valid = Serializer.ProtoSerialize(new MutationSample { Id = 7, Name = "ok" });
            Assert.IsTrue(4 <= valid.Length, "a payload too small to mutate proves nothing");

            for (int position = 0; position < valid.Length; position++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    byte[] mutated = (byte[])valid.Clone();
                    mutated[position] ^= (byte)(1 << bit);
                    Assert.DoesNotThrow(
                        () => Serializer.TryProtoDeserialize(mutated, out MutationSample _),
                        "bit {0} of byte {1} must never escape as a raw exception",
                        bit,
                        position
                    );
                }
            }
        }

        [Test]
        public void AFlippedIdByteNeverDecodesToTheOriginalId()
        {
            // The kill check: position 1 is the varint payload of field 1 (08 07 ...), so every
            // flip either refuses the message or decodes to a different Id. A success reporting
            // Id == 7 would mean the mutation was swallowed, and this suite would be theatre.
            byte[] valid = Serializer.ProtoSerialize(new MutationSample { Id = 7, Name = "ok" });
            Assert.AreEqual(0x08, valid[0], "field 1 key expected at byte 0");
            Assert.AreEqual(0x07, valid[1], "Id varint payload expected at byte 1");

            for (int bit = 0; bit < 8; bit++)
            {
                byte[] mutated = (byte[])valid.Clone();
                mutated[1] ^= (byte)(1 << bit);
                bool decoded = Serializer.TryProtoDeserialize(mutated, out MutationSample value);
                Assert.IsTrue(
                    !decoded || value.Id != 7,
                    "bit {0} of the Id byte decoded back to the original Id",
                    bit
                );
            }
        }

        [Test]
        public void EveryPrefixOfAValidProtoPayloadDecodesOrRefuses()
        {
            byte[] valid = Serializer.ProtoSerialize(
                new MutationSample { Id = 1234, Name = "truncate me" }
            );

            for (int length = 0; length < valid.Length; length++)
            {
                byte[] prefix = new byte[length];
                Array.Copy(valid, prefix, length);
                Assert.DoesNotThrow(
                    () => Serializer.TryProtoDeserialize(prefix, out MutationSample _),
                    "prefix length {0} must never escape as a raw exception",
                    length
                );
            }
        }

        [Test]
        public void EveryPrefixOfAValidJsonPayloadDecodesOrRefuses()
        {
            string valid = Serializer.JsonStringify(
                new MutationSample { Id = 55, Name = "cut here" }
            );

            for (int length = 1; length < valid.Length; length++)
            {
                string prefix = valid.Substring(0, length);
                Assert.DoesNotThrow(
                    () => Serializer.TryJsonDeserialize(prefix, out MutationSample _),
                    "prefix length {0} must never escape as a raw exception",
                    length
                );
            }
        }

        [Test]
        public void SingleCharacterCorruptionsOfValidJsonNeverThrow()
        {
            string valid = Serializer.JsonStringify(
                new MutationSample { Id = 56, Name = "corrupt me" }
            );
            char[] corruptions = { '"', ':', '{', '}', '[', ']', ',', '0', '\\', '\n' };

            for (int position = 0; position < valid.Length; position++)
            {
                foreach (char replacement in corruptions)
                {
                    string mutated =
                        valid.Substring(0, position) + replacement + valid.Substring(position + 1);
                    Assert.DoesNotThrow(
                        () => Serializer.TryJsonDeserialize(mutated, out MutationSample _),
                        "replacing byte {0} with {1} must never escape as a raw exception",
                        position,
                        replacement
                    );
                }
            }
        }

        [Test]
        public void EveryDigitMutationOfTheJsonIdNeverDecodesToTheOriginalId()
        {
            // The JSON kill check, matching the proto one: the id renders as the decimal token
            // 1234, so mutating one digit either refuses the payload or decodes to a different
            // id. A success reporting 1234 would mean the mutation was swallowed.
            string valid = Serializer.JsonStringify(
                new MutationSample { Id = 1234, Name = "digits" }
            );
            int idTokenStart = valid.IndexOf("1234", StringComparison.Ordinal);
            Assert.GreaterOrEqual(idTokenStart, 0, "the id token expected in the payload");

            for (int offset = 0; offset < 4; offset++)
            {
                for (char digit = '0'; digit <= '9'; digit++)
                {
                    if (digit == valid[idTokenStart + offset])
                    {
                        continue;
                    }

                    string mutated =
                        valid.Substring(0, idTokenStart + offset)
                        + digit
                        + valid.Substring(idTokenStart + offset + 1);
                    bool decoded = Serializer.TryJsonDeserialize(mutated, out MutationSample value);
                    Assert.IsTrue(
                        !decoded || value.Id != 1234,
                        "replacing digit {0} with {1} decoded back to the original id",
                        offset,
                        digit
                    );
                }
            }
        }

        [Test]
        public void StructuralInsertionsIntoValidJsonNeverThrow()
        {
            // Replacement corrupts a byte in place; insertion shifts everything after it and can
            // split an escape, a number or a key name, which is a different family of near miss.
            string valid = Serializer.JsonStringify(
                new MutationSample { Id = 41, Name = "insert" }
            );
            char[] insertions = { '"', ':', '{', '}', '[', ']', ',', '\\', '\n' };

            for (int position = 0; position <= valid.Length; position++)
            {
                foreach (char inserted in insertions)
                {
                    string mutated =
                        valid.Substring(0, position) + inserted + valid.Substring(position);
                    Assert.DoesNotThrow(
                        () => Serializer.TryJsonDeserialize(mutated, out MutationSample _),
                        "inserting {0} at position {1} must never escape as a raw exception",
                        inserted,
                        position
                    );
                }
            }
        }

        [Test]
        public void EscapeSequencesInsideTheJsonValueNeverThrowAndNeverFakeTheOriginal()
        {
            // The string member is where a decoder does its own state machine work: an escape can
            // be truncated mid-sequence or name a surrogate half. Every corruption must refuse or
            // decode to a different name -- a decoder that heals arbitrary bytes back into "user
            // data" would be lying about what the payload said.
            string valid = Serializer.JsonStringify(new MutationSample { Id = 63, Name = "abcd" });
            string[] fragments = { "\\\\", "\\\"", "\\u", "\\u004", "\\uZZZZ", "\\n", "\\", "é" };
            int nameStart = valid.IndexOf("abcd", StringComparison.Ordinal);
            Assert.GreaterOrEqual(nameStart, 0, "the name value expected in the payload");

            for (int offset = 0; offset < 4; offset++)
            {
                foreach (string fragment in fragments)
                {
                    string mutated =
                        valid.Substring(0, nameStart + offset)
                        + fragment
                        + valid.Substring(nameStart + offset + 1);
                    bool decoded = Serializer.TryJsonDeserialize(mutated, out MutationSample value);
                    Assert.IsTrue(
                        !decoded || value.Name != "abcd",
                        "fragment {0} at offset {1} decoded back to the original name",
                        fragment,
                        offset
                    );
                }
            }
        }

        [Test]
        public void EverySingleBitFlipOfTheUtf8JsonPayloadNeverThrows()
        {
            // Utf8JsonReader validates UTF-8 itself, so the byte-level near-miss space of the JSON
            // path -- a flipped bit can split a multibyte sequence or forge a control character --
            // must refuse or decode, never throw.
            byte[] valid = Serializer.JsonSerialize(new MutationSample { Id = 82, Name = "bits" });
            Assert.IsTrue(4 <= valid.Length, "a payload too small to mutate proves nothing");

            for (int position = 0; position < valid.Length; position++)
            {
                for (int bit = 0; bit < 8; bit++)
                {
                    byte[] mutated = (byte[])valid.Clone();
                    mutated[position] ^= (byte)(1 << bit);
                    Assert.DoesNotThrow(
                        () => Serializer.TryJsonDeserialize(mutated, out MutationSample _),
                        "bit {0} of byte {1} must never escape as a raw exception",
                        bit,
                        position
                    );
                }
            }
        }

        [ProtoContract]
        private sealed class MutationSample
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }
        }
    }
}
