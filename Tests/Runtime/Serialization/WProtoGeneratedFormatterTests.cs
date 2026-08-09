// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Proves the shipped Roslyn generator ran under Unity's own compiler and produced a working
    /// formatter.
    /// </summary>
    /// <remarks>
    /// If the analyzer failed to load, none of this compiles -- <c>WProtoGeneratedContract</c> would
    /// have no nested <c>WProtoFormatter</c> -- so the fixture failing to build is itself the
    /// signal. What the assertions add is that the emitted code is <i>correct</i> on the backend
    /// running it, which is the part a build cannot tell you: the standalone legs are IL2CPP, where
    /// the entire reason this serializer exists is that reflective serialization does not work.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoGeneratedFormatterTests
    {
        [Test]
        public void TheGeneratedFormatterRegistersItselfWithoutAnyoneAskingForIt()
        {
            // The generated registrar's [RuntimeInitializeOnLoadMethod] runs at BeforeSceneLoad,
            // after the built-ins at SubsystemRegistration, which is the ordering that lets a
            // consumer's generated formatter override one this package ships.
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<WProtoGeneratedContract>());
        }

        [Test]
        public void ADefaultContractEncodesToNoBytesAtAll()
        {
            Assert.AreEqual(string.Empty, Encode(new WProtoGeneratedContract()));
        }

        [Test]
        public void EveryMemberSurvivesARoundTrip()
        {
            WProtoGeneratedContract original = new()
            {
                Count = -1,
                Label = "generated",
                Ratio = 0.5d,
            };

            WProtoGeneratedContract restored = RoundTrip(original);

            Assert.AreEqual(original.Count, restored.Count);
            Assert.AreEqual(original.Label, restored.Label);
            Assert.AreEqual(original.Ratio, restored.Ratio);
            Assert.IsTrue(restored.Rebuilt, "The after-deserialization hook did not run");
        }

        [Test]
        public void AnEmptyStringIsWrittenAndANullOneIsOmitted()
        {
            Assert.AreEqual("1200", Encode(new WProtoGeneratedContract { Label = string.Empty }));
            Assert.AreEqual(string.Empty, Encode(new WProtoGeneratedContract { Label = null }));
            Assert.IsTrue(RoundTrip(new WProtoGeneratedContract { Label = null }).Label == null);
            Assert.AreEqual(
                string.Empty,
                RoundTrip(new WProtoGeneratedContract { Label = string.Empty }).Label
            );
        }

        [Test]
        public void ANullableHoldingZeroIsPresentAndANullOneIsAbsent()
        {
            Assert.AreEqual(
                "19" + "0000000000000000",
                Encode(new WProtoGeneratedContract { Ratio = 0d })
            );
            Assert.AreEqual(string.Empty, Encode(new WProtoGeneratedContract { Ratio = null }));
            Assert.AreEqual(0d, RoundTrip(new WProtoGeneratedContract { Ratio = 0d }).Ratio);
            Assert.IsTrue(RoundTrip(new WProtoGeneratedContract { Ratio = null }).Ratio == null);
        }

        [Test]
        public void ANegativeIntegerIsSignExtendedTheWayProtobufNetWritesIt()
        {
            // Ten bytes, not two. protobuf-net writes a negative int32 as a sign-extended 64-bit
            // varint, and a reader that rejected the extra bytes would reject real saved data.
            Assert.AreEqual(
                "08FFFFFFFFFFFFFFFFFF01",
                Encode(new WProtoGeneratedContract { Count = -1 })
            );
        }

        [Test]
        public void AMalformedPayloadIsRefusedRatherThanPartiallyAccepted()
        {
            WProtoReader reader = new(new byte[] { 0xFF });

            Assert.IsFalse(
                WProtoFormatterProvider
                    .Get<WProtoGeneratedContract>()
                    .TryRead(ref reader, out WProtoGeneratedContract value)
            );
            Assert.IsTrue(value == null);
        }

        private static string Encode(WProtoGeneratedContract value)
        {
            IWProtoFormatter<WProtoGeneratedContract> formatter =
                WProtoFormatterProvider.Get<WProtoGeneratedContract>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(
                buffer.Length,
                writer.Position,
                "Measure must predict Write exactly, or every enclosing message is corrupt"
            );

            StringBuilder builder = new(writer.Position * 2);
            foreach (byte current in writer.Written)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private static WProtoGeneratedContract RoundTrip(WProtoGeneratedContract value)
        {
            IWProtoFormatter<WProtoGeneratedContract> formatter =
                WProtoFormatterProvider.Get<WProtoGeneratedContract>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out WProtoGeneratedContract restored));
            return restored;
        }
    }
}
