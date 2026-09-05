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

            // A small include tag distinguishes include-first ordering from ordinary tag sorting.
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
            // Omitting an empty include would deserialize the value as its base type.
            Assert.AreEqual("A20600", Encode<IncludeBase>(new IncludeAlpha()));
            Assert.IsInstanceOf<IncludeAlpha>(Decode<IncludeBase>("A20600"));
        }

        [Test]
        public void EveryLevelWritesItsOwnIncludeThenItsOwnMembers()
        {
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

            Assert.AreEqual(
                typeof(IncludeBase),
                RoundTrip<IncludeBase>(new IncludeBase { Id = 9 }).GetType()
            );
        }

        [Test]
        public void ADeeperSubtypeIsNotWrittenUnderItsBasesIncludeTag()
        {
            /*
             * Assignability alone matches ancestors too, so dispatch must prefer the most derived registered
             * subtype.
             */
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
            // An include may arrive after base members; changing the instance must preserve those members.
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
            IncludeBase decoded = Decode<IncludeBase>("0801" + "B009" + "02" + "0801");
            Assert.AreEqual(typeof(IncludeBase), decoded.GetType());
            Assert.AreEqual(1, decoded.Id);
        }

        [Test]
        public void TwoSiblingIncludeTagsResolveWithoutRecursion()
        {
            /*
             * The protobuf-net 3 oracle stack-overflows on this payload; test deterministic handling
             * without invoking it.
             */
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
            // Undeclared runtime subtypes must not silently serialize as a registered ancestor.
            IWProtoFormatter<IncludeBase> formatter = WProtoFormatterProvider.Get<IncludeBase>();

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                formatter.Measure(new UndeclaredAlpha { AlphaOnly = 7 })
            );
            StringAssert.Contains("UndeclaredAlpha", refused.Message);
            StringAssert.Contains("WProtoInclude", refused.Message);

            // Write is public and needs its own guard; ref locals cannot be captured by Assert.Throws.
            bool wroteWithoutRefusing = false;
            try
            {
                WProtoWriter writer = new WProtoWriter(new byte[64]);
                formatter.Write(ref writer, new UndeclaredAlpha());
                wroteWithoutRefusing = true;
            }
            catch (InvalidOperationException) { }

            Assert.IsFalse(wroteWithoutRefusing);
        }

        [Test]
        public void AnAbstractContractReadsOnlyWhenThePayloadNamesASubtype()
        {
            // An abstract base cannot supply a fallback instance when the payload has no include.
            IWProtoFormatter<AbstractShape> formatter =
                WProtoFormatterProvider.Get<AbstractShape>();

            WProtoReader missing = new WProtoReader(new byte[] { 0x08, 0x03 });
            Assert.IsFalse(formatter.TryRead(ref missing, out AbstractShape none));
            Assert.IsNull(none);

            WProtoReader empty = new WProtoReader(Array.Empty<byte>());
            Assert.IsFalse(formatter.TryRead(ref empty, out AbstractShape nothing));
            Assert.IsNull(nothing);

            AbstractShape decoded = Decode<AbstractShape>("A20602" + "0805" + "0803");
            Assert.IsInstanceOf<ConcreteShape>(decoded);
            Assert.AreEqual(3, decoded.Sides);
            Assert.AreEqual(5, ((ConcreteShape)decoded).Edge);
        }

        [Test]
        public void ACollectionOnAnAbstractBaseSurvivesAnElementBeforeTheIncludeTag()
        {
            // An abstract base has no instance to seed collections from until the include arrives.
            PolyListBase decoded = Decode<PolyListBase>("0801" + "1009" + "A20602" + "0802");

            Assert.IsInstanceOf<PolyListSub>(decoded);
            Assert.AreEqual(2, ((PolyListSub)decoded).SubOnly);
            CollectionAssert.AreEqual(new[] { 5, 1 }, decoded.Items);
            CollectionAssert.AreEqual(new[] { 9 }, decoded.Extras);
        }

        [Test]
        public void ACollectionAppendsOntoTheSubtypesConstructorWhicheverOrderTheIncludeArrivesIn()
        {
            /*
             * Different constructor seeds expose provisional-instance seeding. This reader avoids the
             * oracle's include-last seed duplication.
             */
            foreach (string hex in new[] { "A20602" + "0802" + "0801", "0801" + "A20602" + "0802" })
            {
                PolyListBase decoded = Decode<PolyListBase>(hex);

                Assert.IsInstanceOf<PolyListSub>(decoded, hex);
                CollectionAssert.AreEqual(new[] { 5, 1 }, decoded.Items, hex);
            }

            WProtoReader reader = new WProtoReader(Parse("0801"));
            Assert.IsFalse(
                WProtoFormatterProvider.Get<PolyListBase>().TryRead(ref reader, out PolyListBase _)
            );
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
