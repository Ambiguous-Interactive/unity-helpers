// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
    public sealed class RandomStateSerializationTests
    {
        private static void AssertStateEqual(
            RandomState expected,
            RandomState actual,
            string context
        )
        {
            Assert.AreEqual(expected, actual, $"{context}: overall equality should hold");

            Assert.AreEqual(expected.State1, actual.State1, $"{context}: State1 mismatch");
            Assert.AreEqual(expected.State2, actual.State2, $"{context}: State2 mismatch");

            bool expectedHasGaussian = expected.Gaussian.HasValue;
            bool actualHasGaussian = actual.Gaussian.HasValue;
            Assert.AreEqual(
                expectedHasGaussian,
                actualHasGaussian,
                $"{context}: Gaussian presence mismatch"
            );
            if (expectedHasGaussian)
            {
                double ev = expected.Gaussian.Value;
                Assert.IsTrue(actual.Gaussian != null, "actual.Gaussian != null");
                double av = actual.Gaussian.Value;
                if (double.IsNaN(ev))
                {
                    Assert.IsTrue(double.IsNaN(av), $"{context}: Gaussian should be NaN");
                }
                else if (double.IsPositiveInfinity(ev))
                {
                    Assert.IsTrue(
                        double.IsPositiveInfinity(av),
                        $"{context}: Gaussian should be +Infinity"
                    );
                }
                else if (double.IsNegativeInfinity(ev))
                {
                    Assert.IsTrue(
                        double.IsNegativeInfinity(av),
                        $"{context}: Gaussian should be -Infinity"
                    );
                }
                else
                {
                    Assert.AreEqual(ev, av, $"{context}: Gaussian value mismatch");
                }
            }

            IReadOnlyList<byte> expectedPayload = expected.PayloadBytes;
            IReadOnlyList<byte> actualPayload = actual.PayloadBytes;
            if (expectedPayload == null)
            {
                Assert.IsTrue(actualPayload == null, $"{context}: Payload should be null");
            }
            else
            {
                Assert.IsTrue(actualPayload != null, $"{context}: Payload should not be null");
                Assert.AreEqual(
                    expectedPayload.Count,
                    actualPayload.Count,
                    $"{context}: Payload length mismatch"
                );
                Assert.IsTrue(
                    expectedPayload.SequenceEqual(actualPayload),
                    $"{context}: Payload content mismatch"
                );
            }

            Assert.AreEqual(expected.BitBuffer, actual.BitBuffer, $"{context}: BitBuffer mismatch");
            Assert.AreEqual(expected.BitCount, actual.BitCount, $"{context}: BitCount mismatch");
            Assert.AreEqual(
                expected.ByteBuffer,
                actual.ByteBuffer,
                $"{context}: ByteBuffer mismatch"
            );
            Assert.AreEqual(expected.ByteCount, actual.ByteCount, $"{context}: ByteCount mismatch");
        }

        [Test]
        public void JsonRoundTripMinimalState()
        {
            RandomState state = new(123UL);

            string json = Serializer.JsonStringify(state);
            Assert.IsTrue(json != null, "JSON should not be null");
            Assert.IsTrue(json.Contains("State1"), "JSON should include State1");

            RandomState clone = Serializer.JsonDeserialize<RandomState>(json);
            AssertStateEqual(state, clone, "JSON minimal");
        }

        [Test]
        public void JsonRoundTripWithAllFields()
        {
            byte[] payload = { 0, 255, 1, 2, 3 };
            RandomState state = new(
                state1: 0x1122334455667788UL,
                state2: 0x99AABBCCDDEEFF00UL,
                gaussian: -12.5,
                payload: payload,
                bitBuffer: 0xA5A5A5A5U,
                bitCount: 31,
                byteBuffer: 0xDEADBEEFU,
                byteCount: 3
            );

            string json = Serializer.JsonStringify(state);
            RandomState clone = Serializer.JsonDeserialize<RandomState>(json);

            AssertStateEqual(state, clone, "JSON all fields");
        }

        [Test]
        public void JsonRoundTripWithSpecialGaussianValues()
        {
            RandomState nan = new(1UL, gaussian: double.NaN);
            RandomState pinf = new(2UL, gaussian: double.PositiveInfinity);
            RandomState ninf = new(3UL, gaussian: double.NegativeInfinity);

            string nanJson = Serializer.JsonStringify(nan);
            string pinfJson = Serializer.JsonStringify(pinf);
            string ninfJson = Serializer.JsonStringify(ninf);

            RandomState nanClone = Serializer.JsonDeserialize<RandomState>(nanJson);
            RandomState pinfClone = Serializer.JsonDeserialize<RandomState>(pinfJson);
            RandomState ninfClone = Serializer.JsonDeserialize<RandomState>(ninfJson);

            AssertStateEqual(nan, nanClone, "JSON Gaussian NaN");
            AssertStateEqual(pinf, pinfClone, "JSON Gaussian +Infinity");
            AssertStateEqual(ninf, ninfClone, "JSON Gaussian -Infinity");
        }

        [Test]
        public void JsonDefaultsSupportNamedFloatingPointLiterals()
        {
            RandomState s1 = new(10UL, gaussian: double.NaN);
            RandomState s2 = new(11UL, gaussian: double.PositiveInfinity);
            RandomState s3 = new(12UL, gaussian: double.NegativeInfinity);

            string j1 = Serializer.JsonStringify(s1);
            string j2 = Serializer.JsonStringify(s2);
            string j3 = Serializer.JsonStringify(s3);

            Assert.IsTrue(j1.Contains("NaN"), "JSON should contain NaN");
            Assert.IsTrue(j2.Contains("Infinity"), "JSON should contain Infinity");
            Assert.IsTrue(j3.Contains("-Infinity"), "JSON should contain -Infinity");

            AssertStateEqual(s1, Serializer.JsonDeserialize<RandomState>(j1), "JSON default NaN");
            AssertStateEqual(
                s2,
                Serializer.JsonDeserialize<RandomState>(j2),
                "JSON default +Infinity"
            );
            AssertStateEqual(
                s3,
                Serializer.JsonDeserialize<RandomState>(j3),
                "JSON default -Infinity"
            );
        }

        [Test]
        public void JsonRoundTripWithNullAndEmptyPayload()
        {
            RandomState nullPayload = new(42UL, payload: null);
            RandomState emptyPayload = new(42UL, payload: Array.Empty<byte>());

            RandomState nullClone = Serializer.JsonDeserialize<RandomState>(
                Serializer.JsonStringify(nullPayload)
            );
            RandomState emptyClone = Serializer.JsonDeserialize<RandomState>(
                Serializer.JsonStringify(emptyPayload)
            );

            AssertStateEqual(nullPayload, nullClone, "JSON null payload");
            AssertStateEqual(emptyPayload, emptyClone, "JSON empty payload");
        }

        [Test]
        public void JsonRoundTripWithLargePayload()
        {
            byte[] payload = new byte[8192];
            for (int i = 0; i < payload.Length; ++i)
            {
                payload[i] = (byte)(i & 0xFF);
            }

            RandomState state = new(
                777UL,
                payload: payload,
                bitCount: int.MaxValue,
                byteCount: int.MinValue
            );
            RandomState clone = Serializer.JsonDeserialize<RandomState>(
                Serializer.JsonStringify(state)
            );

            AssertStateEqual(state, clone, "JSON large payload");
        }

        [Test]
        public void ProtobufRoundTripMinimalState()
        {
            RandomState state = new(123UL);
            byte[] bytes = Serializer.ProtoSerialize(state);
            Assert.IsTrue(bytes != null, "Protobuf should produce bytes");
            Assert.Greater(bytes.Length, 0, "Protobuf should produce non-empty bytes");

            RandomState clone = Serializer.ProtoDeserialize<RandomState>(bytes);
            AssertStateEqual(state, clone, "Protobuf minimal");
        }

        [Test]
        public void ProtobufRoundTripWithAllFields()
        {
            byte[] payload = { 10, 20, 30, 40 };
            RandomState state = new(
                state1: 0xCAFEBABEDEADBEEFUL,
                state2: 0x0102030405060708UL,
                gaussian: 0.0,
                payload: payload,
                bitBuffer: 0x12345678U,
                bitCount: 17,
                byteBuffer: 0x9ABCDEF0U,
                byteCount: 2
            );

            byte[] bytes = Serializer.ProtoSerialize(state);
            RandomState clone = Serializer.ProtoDeserialize<RandomState>(bytes);

            AssertStateEqual(state, clone, "Protobuf all fields");
        }

        [Test]
        public void ProtobufRoundTripWithSpecialGaussianValues()
        {
            RandomState nan = new(1UL, gaussian: double.NaN);
            RandomState pinf = new(2UL, gaussian: double.PositiveInfinity);
            RandomState ninf = new(3UL, gaussian: double.NegativeInfinity);

            RandomState nanClone = Serializer.ProtoDeserialize<RandomState>(
                Serializer.ProtoSerialize(nan)
            );
            RandomState pinfClone = Serializer.ProtoDeserialize<RandomState>(
                Serializer.ProtoSerialize(pinf)
            );
            RandomState ninfClone = Serializer.ProtoDeserialize<RandomState>(
                Serializer.ProtoSerialize(ninf)
            );

            AssertStateEqual(nan, nanClone, "Protobuf Gaussian NaN");
            AssertStateEqual(pinf, pinfClone, "Protobuf Gaussian +Infinity");
            AssertStateEqual(ninf, ninfClone, "Protobuf Gaussian -Infinity");
        }

        [Test]
        public void ProtobufRoundTripWithLargePayload()
        {
            byte[] payload = new byte[4096];
            for (int i = 0; i < payload.Length; ++i)
            {
                payload[i] = (byte)((i * 31) & 0xFF);
            }

            RandomState state = new(
                999UL,
                payload: payload,
                bitBuffer: 0xFFFFFFFFU,
                byteBuffer: 0x0U
            );
            RandomState clone = Serializer.ProtoDeserialize<RandomState>(
                Serializer.ProtoSerialize(state)
            );

            AssertStateEqual(state, clone, "Protobuf large payload");
        }

        [Test]
        public void GuidConstructorRoundTripsWithJsonAndProtobuf()
        {
            Guid g = Guid.Parse("12345678-1234-5678-9ABC-DEF012345678");
            RandomState state = new(g);

            RandomState jsonClone = Serializer.JsonDeserialize<RandomState>(
                Serializer.JsonStringify(state)
            );
            RandomState protoClone = Serializer.ProtoDeserialize<RandomState>(
                Serializer.ProtoSerialize(state)
            );

            AssertStateEqual(state, jsonClone, "JSON Guid constructor");
            AssertStateEqual(state, protoClone, "Protobuf Guid constructor");
        }
    }
}
