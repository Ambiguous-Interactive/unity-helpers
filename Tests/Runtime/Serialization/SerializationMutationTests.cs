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
    /// original value when the mutated bytes named a different one.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class SerializationMutationTests
    {
        [Test]
        public void EverySingleBitFlipOfAValidProtoPayloadNeverThrows()
        {
            byte[] valid = Serializer.ProtoSerialize(
                new MutationSample
                {
                    Id = 7,
                    Name = "ok",
                    Scores = new[] { 1, -2, 3 },
                }
            );
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
                new MutationSample
                {
                    Id = 1234,
                    Name = "truncate me",
                    Scores = new[] { 9, 8 },
                }
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
                new MutationSample
                {
                    Id = 56,
                    Name = "corrupt me",
                    Scores = new[] { 4, 5 },
                }
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

        [ProtoContract]
        private sealed class MutationSample
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }

            [ProtoMember(3)]
            public int[] Scores { get; set; }
        }
    }
}
