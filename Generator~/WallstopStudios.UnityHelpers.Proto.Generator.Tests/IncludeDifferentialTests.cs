// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins <c>[WProtoInclude]</c> polymorphism against protobuf-net 3.2.56.
    /// </summary>
    /// <remarks>
    /// The rule that matters here is the one this plan had recorded backwards: the subtype's include
    /// field is written <b>before</b> the base's own members, whatever its tag number. Sorting
    /// includes into ascending field order — which every other member obeys — produces bytes
    /// protobuf-net cannot read.
    /// </remarks>
    [TestFixture]
    public sealed class IncludeDifferentialTests
    {
        [Test]
        public void TheIncludeIsWrittenBeforeTheBaseMembersWhateverItsTagNumber()
        {
            // Measured. Tag 100 precedes tags 1 and 2...
            Assert.AreEqual(
                "A20605080712017808011201 61".Replace(" ", string.Empty),
                Encode<IncludeBase>(
                    new IncludeAlpha
                    {
                        Id = 1,
                        Label = "a",
                        AlphaOnly = 7,
                        AlphaText = "x",
                    }
                )
            );

            // ...and so does tag 3, sitting between base members numbered 1 and 5. That second case
            // is what rules out "includes happen to sort last because their tags are large".
            Assert.AreEqual(
                "1A0208090801 2805".Replace(" ", string.Empty),
                Encode<LowTagBase>(
                    new LowTagSub
                    {
                        First = 1,
                        Fifth = 5,
                        SubOnly = 9,
                    }
                )
            );
        }

        [Test]
        public void AnAllDefaultSubtypeStillWritesItsIncludeSoItsTypeSurvives()
        {
            // A tag and a zero length. Omitting it because the payload is empty would downgrade the
            // value to its base type on read -- a type change disguised as a size optimization.
            Assert.AreEqual("A20600", Encode<IncludeBase>(new IncludeAlpha()));
            Assert.IsInstanceOf<IncludeAlpha>(Decode<IncludeBase>("A20600"));
        }

        [Test]
        public void EveryLevelWritesItsOwnIncludeThenItsOwnMembers()
        {
            // Three levels: Beta's include holds Gamma's include followed by Beta's own member, and
            // the base's members trail the lot.
            Assert.AreEqual(
                "AA060EC20C02080109000000000000F83F08011201 61".Replace(" ", string.Empty),
                Encode<IncludeBase>(
                    new IncludeGamma
                    {
                        Id = 1,
                        Label = "a",
                        BetaOnly = 1.5,
                        GammaOnly = true,
                    }
                )
            );
        }

        [Test]
        public void EveryPolymorphicShapeMatchesTheOracleByteForByte()
        {
            IncludeBase[] values =
            {
                new IncludeBase(),
                new IncludeBase { Id = 1, Label = "a" },
                new IncludeAlpha(),
                new IncludeAlpha { AlphaOnly = 7 },
                new IncludeAlpha
                {
                    Id = -1,
                    Label = string.Empty,
                    AlphaOnly = int.MinValue,
                    AlphaText = "é中",
                },
                new IncludeBeta(),
                new IncludeBeta { Id = 2, BetaOnly = -0.5 },
                new IncludeGamma(),
                new IncludeGamma
                {
                    Id = 3,
                    Label = "g",
                    BetaOnly = double.MaxValue,
                    GammaOnly = true,
                },
            };

            foreach (IncludeBase value in values)
            {
                string label = value.GetType().Name;
                Assert.AreEqual(OracleHex(value), Encode(value), label);

                // And as a member of an enclosing message, where the whole chain sits under a
                // length prefix the parent has to predict.
                IncludeHolder holder = new IncludeHolder { Value = value, Trailer = 2 };
                Assert.AreEqual(OracleHex(holder), Encode(holder), label + " in a holder");
            }

            Assert.AreEqual(
                OracleHex(new IncludeHolder { Trailer = 2 }),
                Encode(new IncludeHolder { Trailer = 2 }),
                "a null polymorphic member"
            );
        }

        [Test]
        public void ThePolymorphicRoundTripKeepsTheConcreteType()
        {
            IncludeAlpha alpha = (IncludeAlpha)
                RoundTrip<IncludeBase>(
                    new IncludeAlpha
                    {
                        Id = 1,
                        Label = "a",
                        AlphaOnly = 7,
                        AlphaText = "x",
                    }
                );
            Assert.AreEqual(1, alpha.Id);
            Assert.AreEqual("a", alpha.Label);
            Assert.AreEqual(7, alpha.AlphaOnly);
            Assert.AreEqual("x", alpha.AlphaText);

            IncludeGamma gamma = (IncludeGamma)
                RoundTrip<IncludeBase>(
                    new IncludeGamma
                    {
                        Id = 3,
                        Label = "g",
                        BetaOnly = 1.5,
                        GammaOnly = true,
                    }
                );
            Assert.AreEqual(3, gamma.Id);
            Assert.AreEqual("g", gamma.Label);
            Assert.AreEqual(1.5, gamma.BetaOnly);
            Assert.IsTrue(gamma.GammaOnly);

            // The base itself still round-trips as the base.
            Assert.AreEqual(
                typeof(IncludeBase),
                RoundTrip<IncludeBase>(new IncludeBase { Id = 9 }).GetType()
            );
        }

        [Test]
        public void ADeeperSubtypeIsNotWrittenUnderItsBasesIncludeTag()
        {
            // `value is IncludeBeta` is true for an IncludeGamma, so a dispatch chain in declaration
            // order would write a Gamma under Beta's tag and lose the Gamma level silently. The
            // encoding above already covers it; this states the consequence directly.
            Assert.IsInstanceOf<IncludeGamma>(
                RoundTrip<IncludeBase>(new IncludeGamma { GammaOnly = true })
            );
            Assert.IsTrue(
                (
                    (IncludeGamma)RoundTrip<IncludeBase>(new IncludeGamma { GammaOnly = true })
                ).GammaOnly
            );
        }

        [Test]
        public void AnIncludeAfterTheBaseMembersStillKeepsThem()
        {
            // protobuf-net always writes the include first, but a payload with it last is legal and
            // decodes fine there -- so a reader that assigned base members straight onto a base
            // instance would lose them when the subtype arrived. Both orders are checked against the
            // oracle rather than against this session's reading of the spec.
            foreach (string payload in new[] { "A20602080708011201 61", "08011201 61A206020807" })
            {
                string hex = payload.Replace(" ", string.Empty);

                IncludeBase oracle;
                using (MemoryStream stream = new MemoryStream(Parse(hex)))
                {
                    oracle = ProtoBuf.Serializer.Deserialize<IncludeBase>(stream);
                }

                IncludeBase mine = Decode<IncludeBase>(hex);

                Assert.AreEqual(oracle.GetType(), mine.GetType(), hex);
                Assert.AreEqual(oracle.Id, mine.Id, hex);
                Assert.AreEqual(oracle.Label, mine.Label, hex);
                Assert.AreEqual(
                    ((IncludeAlpha)oracle).AlphaOnly,
                    ((IncludeAlpha)mine).AlphaOnly,
                    hex
                );
            }
        }

        [Test]
        public void AnUnknownIncludeTagIsSkippedRatherThanFailing()
        {
            // Forward compatibility: a payload from a newer build names a subtype this one has never
            // heard of. protobuf-net yields the base; so does this.
            IncludeBase decoded = Decode<IncludeBase>("0801" + "B009" + "02" + "0801");
            Assert.AreEqual(typeof(IncludeBase), decoded.GetType());
            Assert.AreEqual(1, decoded.Id);
        }

        [Test]
        public void TwoSiblingIncludeTagsResolveWithoutRecursion()
        {
            // protobuf-net 3.2.56 takes the process down with a stack overflow on this input, which
            // cannot be caught -- measured, from a plain Serializer.Deserialize. A save file is
            // attacker-controlled, so the only acceptable behaviour is a deterministic one. Last
            // include wins, and the assignment cannot recurse.
            IncludeBase decoded = Decode<IncludeBase>(
                "0801" + "A20602" + "0807" + "AA0609" + "09000000000000F83F"
            );

            Assert.IsInstanceOf<IncludeBeta>(decoded);
            Assert.AreEqual(1.5, ((IncludeBeta)decoded).BetaOnly);
            Assert.AreEqual(1, decoded.Id);
        }

        [Test]
        public void AnUndeclaredSubtypeIsRefusedRatherThanDowngradedToItsAncestor()
        {
            // `value is IncludeAlpha` is true for an UndeclaredAlpha, so without the guard it would
            // be written under Alpha's include tag and read back as an IncludeAlpha -- a level of
            // type identity gone from saved data, silently. protobuf-net raises "Unexpected
            // sub-type" on the same value; this names the type and the fix.
            IWProtoFormatter<IncludeBase> formatter = WProtoFormatterProvider.Get<IncludeBase>();

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                formatter.Measure(new UndeclaredAlpha { AlphaOnly = 7 })
            );
            StringAssert.Contains("UndeclaredAlpha", refused.Message);
            StringAssert.Contains("WProtoInclude", refused.Message);

            // Write guards on its own, because the interface is public and either half may be
            // called without the other. A `ref` local cannot be captured by Assert.Throws' lambda.
            bool wroteWithoutRefusing = false;
            try
            {
                WProtoWriter writer = new WProtoWriter(new byte[64]);
                formatter.Write(ref writer, new UndeclaredAlpha());
                wroteWithoutRefusing = true;
            }
            catch (InvalidOperationException)
            {
                // Expected.
            }

            Assert.IsFalse(wroteWithoutRefusing);
        }

        [Test]
        public void AnAbstractContractReadsOnlyWhenThePayloadNamesASubtype()
        {
            // The AbstractRandom shape. There is no instance of the base to fall back to, so a
            // payload carrying only base members is malformed rather than an empty base.
            IWProtoFormatter<AbstractShape> formatter =
                WProtoFormatterProvider.Get<AbstractShape>();

            WProtoReader missing = new WProtoReader(new byte[] { 0x08, 0x03 });
            Assert.IsFalse(formatter.TryRead(ref missing, out AbstractShape none));
            Assert.IsNull(none);

            WProtoReader empty = new WProtoReader(Array.Empty<byte>());
            Assert.IsFalse(formatter.TryRead(ref empty, out AbstractShape nothing));
            Assert.IsNull(nothing);

            // ...and with the include tag it produces the concrete type, base members included.
            AbstractShape decoded = Decode<AbstractShape>("A20602" + "0805" + "0803");
            Assert.IsInstanceOf<ConcreteShape>(decoded);
            Assert.AreEqual(3, decoded.Sides);
            Assert.AreEqual(5, ((ConcreteShape)decoded).Edge);
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryPolymorphicShape()
        {
            IncludeBase[] values =
            {
                new IncludeBase { Id = 1 },
                new IncludeAlpha { AlphaText = new string('x', 200) },
                new IncludeGamma { BetaOnly = 1.5, GammaOnly = true },
            };

            foreach (IncludeBase value in values)
            {
                IWProtoFormatter<IncludeBase> formatter =
                    WProtoFormatterProvider.Get<IncludeBase>();
                int predicted = formatter.Measure(value);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new WProtoWriter(buffer);
                Assert.IsTrue(formatter.Write(ref writer, value), value.GetType().Name);
                Assert.AreEqual(predicted, writer.Position, value.GetType().Name);
            }
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

        private static string OracleHex<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return ToHex(stream.ToArray());
            }
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte current in bytes)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private static T Decode<T>(string hex)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T value), hex);
            return value;
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return ToHex(buffer);
        }

        private static T RoundTrip<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out T restored));
            return restored;
        }
    }
}
