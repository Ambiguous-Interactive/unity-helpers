// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Exercises the generator's output the way Unity will: compiled into this assembly by the
    /// generator itself, referenced as an analyzer rather than as a library.
    /// </summary>
    /// <remarks>
    /// These are not snapshot tests. A snapshot proves the emitter still emits the same characters;
    /// what has to hold is that the bytes match protobuf-net's rules -- ascending field number,
    /// defaults omitted, empty-but-not-null written -- and that a value survives a round trip. Every
    /// expected payload below is spelled out in hex for that reason.
    /// </remarks>
    [TestFixture]
    public sealed class GeneratedFormatterTests
    {
        [Test]
        public void TheGeneratorRegistersEveryContractItEmitted()
        {
            // No test calls Register. The generated registrar does, from a module initializer
            // outside Unity and from [RuntimeInitializeOnLoadMethod] inside it.
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<ScalarContract>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<OutOfOrderContract>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<HookedContract>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Outer.Point>());
        }

        [Test]
        public void AllDefaultMembersEncodeToNoBytesAtAll()
        {
            Assert.AreEqual(string.Empty, Encode(new ScalarContract()));
        }

        [Test]
        public void EveryScalarShapeRoundTrips()
        {
            ScalarContract original = new ScalarContract
            {
                Int32 = -1,
                Int64 = long.MinValue,
                UInt32 = uint.MaxValue,
                UInt64 = ulong.MaxValue,
                Flag = true,
                Single = 1.5f,
                Double = -2.25d,
                Text = "wallstop",
                Bytes = new byte[] { 1, 2, 3 },
                Enum = Mode.Careful,
                MaybeDouble = 0d,
                Int16 = -2,
                Hidden = 7,
                Counted = 9,
            };

            ScalarContract restored = RoundTrip(original);

            Assert.AreEqual(original.Int32, restored.Int32);
            Assert.AreEqual(original.Int64, restored.Int64);
            Assert.AreEqual(original.UInt32, restored.UInt32);
            Assert.AreEqual(original.UInt64, restored.UInt64);
            Assert.AreEqual(original.Flag, restored.Flag);
            Assert.AreEqual(original.Single, restored.Single);
            Assert.AreEqual(original.Double, restored.Double);
            Assert.AreEqual(original.Text, restored.Text);
            CollectionAssert.AreEqual(original.Bytes, restored.Bytes);
            Assert.AreEqual(original.Enum, restored.Enum);
            Assert.AreEqual(original.MaybeDouble, restored.MaybeDouble);
            Assert.AreEqual(original.Int16, restored.Int16);
            Assert.AreEqual(original.Hidden, restored.Hidden);
            Assert.AreEqual(original.Counted, restored.Counted);
        }

        [Test]
        public void ANullableHoldingZeroIsStillPresent()
        {
            // The distinction default-omission cannot express: 0d and null are different values, and
            // only HasValue decides. Encoding it as "absent because it equals the default" loses it.
            Assert.AreEqual(
                "59" + "0000000000000000",
                Encode(new ScalarContract { MaybeDouble = 0d })
            );
            Assert.AreEqual(string.Empty, Encode(new ScalarContract { MaybeDouble = null }));
            Assert.IsNull(RoundTrip(new ScalarContract { MaybeDouble = null }).MaybeDouble);
            Assert.AreEqual(0d, RoundTrip(new ScalarContract { MaybeDouble = 0d }).MaybeDouble);
        }

        [Test]
        public void EmptyIsWrittenAndNullIsOmitted()
        {
            // Measured against protobuf-net in session 172, not assumed: an empty-but-non-null
            // string or byte[] is a tag and a zero length, and only null is absent.
            Assert.AreEqual("4200", Encode(new ScalarContract { Text = string.Empty }));
            Assert.AreEqual("4A00", Encode(new ScalarContract { Bytes = Array.Empty<byte>() }));
            Assert.AreEqual(string.Empty, Encode(new ScalarContract { Text = null, Bytes = null }));
            Assert.AreEqual(
                string.Empty,
                Encode(RoundTrip(new ScalarContract { Text = null })),
                "An omitted string must read back as null rather than as empty"
            );
        }

        [Test]
        public void NegativeZeroDoesNotSurvive()
        {
            // protobuf-net's omission test is `value == 0`, and -0.0 == 0.0, so a -0.0 member is
            // dropped and reads back as +0. Wire compatibility beats fidelity here; the point is
            // that it is reproduced deliberately rather than discovered by a consumer.
            Assert.AreEqual(string.Empty, Encode(new ScalarContract { Double = -0d }));
            Assert.IsFalse(
                double.IsNegative(RoundTrip(new ScalarContract { Double = -0d }).Double)
            );
        }

        [Test]
        public void MembersAreWrittenInAscendingFieldNumberNotDeclarationOrder()
        {
            // FastVector3Int is tagged 1, 2, 4, 3 and protobuf-net writes 3 before 4. Declaration
            // order parses and round-trips while producing a payload protobuf-net never wrote.
            // Keys are (field << 3) | wireType: 0x08 is field 1, 0x18 field 3, 0x20 field 4. Field 3
            // is declared last and must still be written before field 4.
            Assert.AreEqual(
                "0801" + "1803" + "2004",
                Encode(
                    new OutOfOrderContract
                    {
                        First = 1,
                        Third = 3,
                        Fourth = 4,
                    }
                )
            );
        }

        [Test]
        public void HooksRunInTheDocumentedOrderAndExactlyOnce()
        {
            HookedContract original = new HookedContract { Value = 5 };
            IWProtoFormatter<HookedContract> formatter =
                WProtoFormatterProvider.Get<HookedContract>();

            int size = formatter.Measure(original);
            byte[] buffer = new byte[size];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, original));

            CollectionAssert.AreEqual(
                new[] { "OnBeforeSerialization", "OnAfterSerialization" },
                original.Trace,
                "Before-serialization belongs to Measure alone; repeating it in Write would leak "
                    + "anything the hook rented."
            );

            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out HookedContract restored));
            CollectionAssert.AreEqual(
                new[] { "OnBeforeDeserialization", "OnAfterDeserialization" },
                restored.Trace
            );
            Assert.AreEqual(5, restored.Value);
        }

        [TestCase(
            new byte[] { 0x08 },
            TestName = "AFailedReadDoesNotRunTheHookWhenAMemberIsTruncated"
        )]
        [TestCase(
            new byte[] { 0xFF },
            TestName = "AFailedReadDoesNotRunTheHookWhenTheKeyIsMalformed"
        )]
        public void AFailedReadDoesNotRunTheAfterDeserializationHook(byte[] payload)
        {
            // Two payloads, because they fail through different code paths: a truncated member
            // value returns from inside the switch, while an incomplete field key latches Malformed
            // and falls out of the loop. Emitting the hook above the Malformed check instead of
            // below it is invisible to the first case, and a test that only inspects the returned
            // value is invisible to both -- the object the hook ran on is the one being discarded.
            int before = HookedContract.AfterDeserializationRuns;
            WProtoReader reader = new WProtoReader(payload);

            Assert.IsFalse(
                WProtoFormatterProvider
                    .Get<HookedContract>()
                    .TryRead(ref reader, out HookedContract value)
            );
            Assert.IsNull(value);
            Assert.AreEqual(before, HookedContract.AfterDeserializationRuns);
        }

        [Test]
        public void AStructContractNestedInAnotherTypeRoundTrips()
        {
            Outer.Point original = new Outer.Point { X = 3, Y = -4 };
            IWProtoFormatter<Outer.Point> formatter = WProtoFormatterProvider.Get<Outer.Point>();

            int size = formatter.Measure(original);
            byte[] buffer = new byte[size];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, original));

            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out Outer.Point restored));
            Assert.AreEqual(original.X, restored.X);
            Assert.AreEqual(original.Y, restored.Y);
        }

        [Test]
        public void AnUnknownFieldIsSkippedRatherThanRejected()
        {
            // Forward compatibility: a payload from a newer build carries members this one has no
            // field for, and dropping the whole message would make every schema addition breaking.
            byte[] buffer = new byte[64];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt32(11));
            Assert.IsTrue(writer.TryWriteTag(999, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteString("from the future"));

            WProtoReader reader = new WProtoReader(writer.Written);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<ScalarContract>()
                    .TryRead(ref reader, out ScalarContract restored)
            );
            Assert.AreEqual(11, restored.Int32);
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryShape()
        {
            // The length-prefix protocol has no back-patching, so a parent emits a child's size
            // before the child's bytes exist. A formatter whose measurement disagrees with its
            // output corrupts every message that contains it.
            ScalarContract[] cases =
            {
                new ScalarContract(),
                new ScalarContract { Int32 = int.MinValue, Int64 = long.MaxValue },
                new ScalarContract { Text = string.Empty, Bytes = Array.Empty<byte>() },
                new ScalarContract { Text = "é中", Bytes = new byte[] { 0xFF } },
                new ScalarContract { Enum = Mode.Careful, MaybeDouble = double.NaN },
                new ScalarContract { UInt64 = ulong.MaxValue, Int16 = short.MinValue },
            };

            foreach (ScalarContract value in cases)
            {
                IWProtoFormatter<ScalarContract> formatter =
                    WProtoFormatterProvider.Get<ScalarContract>();
                int predicted = formatter.Measure(value);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new WProtoWriter(buffer);
                Assert.IsTrue(formatter.Write(ref writer, value));
                Assert.AreEqual(predicted, writer.Position);
            }
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            StringBuilder builder = new StringBuilder(writer.Position * 2);
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
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out T restored));
            return restored;
        }
    }
}
