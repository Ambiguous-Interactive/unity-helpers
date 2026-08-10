// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Runs generated polymorphic dispatch inside Unity, on the editors and players CI builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The standalone legs are IL2CPP, and this is the only place a runtime-type dispatch chain over
    /// generated formatters is AOT-compiled. protobuf-net's own answer to the same problem is
    /// reflective and is exactly what does not survive that compiler.
    /// </para>
    /// <para>
    /// Every expected payload was copied out of protobuf-net 3.2.56 serializing a contract with the
    /// same field numbers, so the guarantee holds here even though the oracle cannot run.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoIncludeContractTests
    {
        [Test]
        public void TheIncludeIsWrittenBeforeTheBaseMembersWhateverItsTagNumber()
        {
            // Tag 100 precedes tags 1 and 2...
            Assert.AreEqual(
                "A2060508071201780801120161",
                Encode<WProtoIncludeBase>(
                    new WProtoIncludeAlpha
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
                "1A02080908012805",
                Encode<WProtoLowTagBase>(
                    new WProtoLowTagSub
                    {
                        First = 1,
                        Fifth = 5,
                        SubOnly = 9,
                    }
                )
            );
        }

        [Test]
        public void EveryLevelWritesItsOwnIncludeThenItsOwnMembers()
        {
            Assert.AreEqual(
                "AA060EC20C02080109000000000000F83F0801120161",
                Encode<WProtoIncludeBase>(
                    new WProtoIncludeGamma
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
        public void AnAllDefaultSubtypeStillWritesItsIncludeSoItsTypeSurvives()
        {
            // A tag and a zero length. Omitting it because the payload is empty would downgrade the
            // value to its base type on read -- a type change disguised as a size optimization.
            Assert.AreEqual("A20600", Encode<WProtoIncludeBase>(new WProtoIncludeAlpha()));
            Assert.IsInstanceOf<WProtoIncludeAlpha>(Decode<WProtoIncludeBase>("A20600"));
        }

        [Test]
        public void ThePolymorphicRoundTripKeepsTheConcreteTypeUnderIl2cpp()
        {
            WProtoIncludeAlpha alpha = (WProtoIncludeAlpha)
                RoundTrip<WProtoIncludeBase>(
                    new WProtoIncludeAlpha
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

            WProtoIncludeGamma gamma = (WProtoIncludeGamma)
                RoundTrip<WProtoIncludeBase>(
                    new WProtoIncludeGamma
                    {
                        Id = 3,
                        Label = "g",
                        BetaOnly = 1.5,
                        GammaOnly = true,
                    }
                );
            Assert.AreEqual(3, gamma.Id);
            Assert.AreEqual(1.5, gamma.BetaOnly);
            Assert.IsTrue(gamma.GammaOnly);

            Assert.AreEqual(
                typeof(WProtoIncludeBase),
                RoundTrip<WProtoIncludeBase>(new WProtoIncludeBase { Id = 9 }).GetType()
            );
        }

        [Test]
        public void APolymorphicMemberRoundTripsInsideAnEnclosingMessage()
        {
            WProtoIncludeHolder restored = RoundTrip(
                new WProtoIncludeHolder
                {
                    Value = new WProtoIncludeGamma { GammaOnly = true, BetaOnly = 2.5 },
                    Trailer = 2,
                }
            );

            Assert.IsInstanceOf<WProtoIncludeGamma>(restored.Value);
            Assert.AreEqual(2.5, ((WProtoIncludeGamma)restored.Value).BetaOnly);
            Assert.AreEqual(2, restored.Trailer);

            Assert.IsTrue(RoundTrip(new WProtoIncludeHolder { Trailer = 2 }).Value == null);
        }

        [Test]
        public void AnIncludeAfterTheBaseMembersStillKeepsThem()
        {
            // protobuf-net always writes the include first, but a payload with it last is legal. A
            // reader that assigned base members straight onto a base instance would lose them when
            // the subtype arrived, so every member of a polymorphic contract is held aside.
            foreach (string hex in new[] { "A20602080708011201 61", "08011201 61A206020807" })
            {
                WProtoIncludeBase decoded = Decode<WProtoIncludeBase>(
                    hex.Replace(" ", string.Empty)
                );

                Assert.IsInstanceOf<WProtoIncludeAlpha>(decoded, hex);
                Assert.AreEqual(1, decoded.Id, hex);
                Assert.AreEqual("a", decoded.Label, hex);
                Assert.AreEqual(7, ((WProtoIncludeAlpha)decoded).AlphaOnly, hex);
            }
        }

        [Test]
        public void AnUnknownIncludeTagIsSkippedRatherThanFailing()
        {
            // Forward compatibility: a payload from a newer build names a subtype this one has never
            // heard of, and the base is still readable.
            WProtoIncludeBase decoded = Decode<WProtoIncludeBase>("0801" + "B009" + "02" + "0801");

            Assert.AreEqual(typeof(WProtoIncludeBase), decoded.GetType());
            Assert.AreEqual(1, decoded.Id);
        }

        [Test]
        public void AnAbstractContractReadsOnlyWhenThePayloadNamesASubtype()
        {
            // The AbstractRandom shape. There is no instance of the base to fall back to, so a
            // payload carrying only base members is malformed rather than an empty base.
            IWProtoFormatter<WProtoAbstractShape> formatter =
                WProtoFormatterProvider.Get<WProtoAbstractShape>();

            WProtoReader missing = new(new byte[] { 0x08, 0x03 });
            Assert.IsFalse(formatter.TryRead(ref missing, out WProtoAbstractShape none));
            Assert.IsTrue(none == null);

            WProtoAbstractShape decoded = Decode<WProtoAbstractShape>("A20602" + "0805" + "0803");
            Assert.IsInstanceOf<WProtoConcreteShape>(decoded);
            Assert.AreEqual(3, decoded.Sides);
            Assert.AreEqual(5, ((WProtoConcreteShape)decoded).Edge);
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryPolymorphicShape()
        {
            WProtoIncludeBase[] values =
            {
                new WProtoIncludeBase { Id = 1 },
                new WProtoIncludeAlpha { AlphaText = new string('x', 200) },
                new WProtoIncludeGamma { BetaOnly = 1.5, GammaOnly = true },
            };

            IWProtoFormatter<WProtoIncludeBase> formatter =
                WProtoFormatterProvider.Get<WProtoIncludeBase>();

            foreach (WProtoIncludeBase value in values)
            {
                int predicted = formatter.Measure(value);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new(buffer);
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

        private static T Decode<T>(string hex)
        {
            WProtoReader reader = new(Parse(hex));
            Assert.IsTrue(WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T value), hex);
            return value;
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            StringBuilder builder = new(writer.Position * 2);
            foreach (byte current in writer.Written)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private static T RoundTrip<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out T restored));
            return restored;
        }
    }
}
