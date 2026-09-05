// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Runs the merge a duplicated field asks for inside Unity, on the editors and players CI
    /// builds.
    /// </summary>
    /// <remarks>
    /// protobuf says a parser "merges multiple instances of the same field, as if with
    /// <c>Message::MergeFrom</c>", so a sub-message field carried twice contributes both times. A
    /// non-repeated scalar does not merge -- it is last-wins -- and a struct sub-message merges
    /// exactly as a reference one does. Every expected value here was measured against protobuf-net
    /// 2.4.9 and 3.2.56 in <c>DuplicateFieldDifferentialTests</c>.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoDuplicateFieldContractTests
    {
        [Test]
        public void ADuplicatedSubMessageMergesRatherThanReplacing()
        {
            WProtoDuplicateHolder decoded = Decode<WProtoDuplicateHolder>("12020801" + "12021002");

            Assert.AreEqual(1, decoded.Child.A);
            Assert.AreEqual(2, decoded.Child.B);
        }

        [Test]
        public void ADuplicatedStructSubMessageMergesLikeAReferenceOne()
        {
            WProtoDuplicateHolder decoded = Decode<WProtoDuplicateHolder>("1A020801" + "1A021002");

            Assert.AreEqual(1, decoded.Where.X);
            Assert.AreEqual(2, decoded.Where.Y);
        }

        [Test]
        public void TheFirstOccurrenceMergesIntoTheConstructorsSubMessage()
        {
            /*
                protobuf reads a sub-message field as MergeFrom, so a member the payload never mentions keeps
                whatever the contract's constructor gave it.
            */
            WProtoSeededHolder decoded = Decode<WProtoSeededHolder>("0A021002");

            Assert.AreEqual(9, decoded.Child.A);
            Assert.AreEqual(2, decoded.Child.B);

            WProtoSeededHolder structural = Decode<WProtoSeededHolder>("12021002");
            Assert.AreEqual(9, structural.Where.X);
            Assert.AreEqual(2, structural.Where.Y);
        }

        [Test]
        public void ADuplicatedSubMessageMergesThroughAGenericMember()
        {
            /*
                Whether a generic member is a sub-message at all is a property of the closure, so the merge
                decision is made at run time rather than emitted.
            */
            WProtoDuplicateBox<WProtoDuplicateChild> reference = Decode<
                WProtoDuplicateBox<WProtoDuplicateChild>
            >("0A020801" + "0A021002");
            Assert.AreEqual(1, reference.Value.A);
            Assert.AreEqual(2, reference.Value.B);

            WProtoDuplicateBox<WProtoDuplicatePoint> structural = Decode<
                WProtoDuplicateBox<WProtoDuplicatePoint>
            >("0A020801" + "0A021002");
            Assert.AreEqual(1, structural.Value.X);
            Assert.AreEqual(2, structural.Value.Y);
        }

        [Test]
        public void ADuplicatedGenericScalarIsStillLastWins()
        {
            /*
                The discriminator: a string closure is length-delimited too, so merging on the wire type rather
                than on whether the closure is message-shaped would concatenate strings.
            */
            Assert.AreEqual("b", Decode<WProtoDuplicateBox<string>>("0A0161" + "0A0162").Value);
        }

        [Test]
        public void ADuplicatedNonRepeatedScalarIsLastWins()
        {
            Assert.AreEqual(5, Decode<WProtoDuplicateHolder>("0804" + "0805").Number);
            Assert.AreEqual(6, Decode<WProtoDuplicateHolder>("0804" + "0805" + "0806").Number);
        }

        [Test]
        public void AMergedSubMessageTakesTheLastOccurrenceOfEachScalarWithinIt()
        {
            Assert.AreEqual(
                "b",
                Decode<WProtoDuplicateHolder>("12031A0161" + "12031A0162").Child.Text
            );
        }

        [Test]
        public void AMergeReachesEveryLevelOfTheMessage()
        {
            WProtoDuplicateGrandparent decoded = Decode<WProtoDuplicateGrandparent>(
                "0A04" + "12020801" + "0A04" + "12021002"
            );

            Assert.AreEqual(1, decoded.Holder.Child.A);
            Assert.AreEqual(2, decoded.Holder.Child.B);
        }

        [Test]
        public void EveryShapeMergesInOnePayload()
        {
            WProtoDuplicateHolder decoded = Decode<WProtoDuplicateHolder>(
                "0804" + "12020801" + "1A020801" + "0805" + "12021002" + "1A021002"
            );

            Assert.AreEqual(5, decoded.Number);
            Assert.AreEqual(1, decoded.Child.A);
            Assert.AreEqual(2, decoded.Child.B);
            Assert.AreEqual(1, decoded.Where.X);
            Assert.AreEqual(2, decoded.Where.Y);
        }

        [Test]
        public void ATruncatedLaterOccurrenceIsRefusedRatherThanMerged()
        {
            // Accumulating occurrences must not turn a truncated payload into a partial value.
            foreach (string hex in new[] { "12020801" + "1202", "12020801" + "120210" })
            {
                byte[] payload = Parse(hex);
                WProtoReader reader = new(payload);
                Assert.IsFalse(
                    WProtoFormatterProvider
                        .Get<WProtoDuplicateHolder>()
                        .TryRead(ref reader, out WProtoDuplicateHolder _),
                    hex
                );
            }
        }

        [Test]
        public void ASingleOccurrenceStillRoundTrips()
        {
            WProtoDuplicateHolder original = new()
            {
                Number = 3,
                Child = new WProtoDuplicateChild
                {
                    A = 1,
                    B = 2,
                    Text = "t",
                },
                Where = new WProtoDuplicatePoint { X = 4, Y = 5 },
            };

            IWProtoFormatter<WProtoDuplicateHolder> formatter =
                WProtoFormatterProvider.Get<WProtoDuplicateHolder>();
            byte[] buffer = new byte[formatter.Measure(original)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, original));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");

            WProtoReader reader = new(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out WProtoDuplicateHolder restored));
            Assert.AreEqual(3, restored.Number);
            Assert.AreEqual(1, restored.Child.A);
            Assert.AreEqual(2, restored.Child.B);
            Assert.AreEqual("t", restored.Child.Text);
            Assert.AreEqual(4, restored.Where.X);
            Assert.AreEqual(5, restored.Where.Y);
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
    }
}
