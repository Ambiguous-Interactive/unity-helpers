// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Fast")]
    [Category("Serialization")]
    public sealed class WProtoDecodedPayloadBudgetTests
    {
        private static byte[] Frame(byte[] payload)
        {
            Assert.LessOrEqual(payload.Length, 127);
            byte[] bytes = new byte[payload.Length + 1];
            bytes[0] = (byte)payload.Length;
            Array.Copy(payload, 0, bytes, 1, payload.Length);
            return bytes;
        }

        private static bool RefusesMaterialization(byte[] bytes, bool text, WProtoReadLimits limits)
        {
            WProtoReader reader = new WProtoReader(bytes, limits);
            if (text)
            {
                bool accepted = reader.TryReadString(out string value);
                return !accepted && value == null && reader.Malformed;
            }
            bool binaryAccepted = WProtoGeneric<byte[]>.TryReadValue(ref reader, out byte[] data);
            return !binaryAccepted && data == null && reader.Malformed;
        }

        private static void AssertContractBudget(
            WProtoDecodedPayloadContract expected,
            long allowed,
            long refused
        )
        {
            byte[] bytes = WProtoFacade.Serialize(expected);
            WProtoDecodedPayloadContract actual =
                WProtoFacade.Deserialize<WProtoDecodedPayloadContract>(
                    bytes,
                    new WProtoReadLimits().WithDecodedPayloadBytes(allowed)
                );
            CollectionAssert.AreEqual(bytes, WProtoFacade.Serialize(actual));
            WProtoReader reader = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithDecodedPayloadBytes(refused)
            );
            Assert.IsFalse(
                WProtoDecodedPayloadContract.WProtoFormatter.Instance.TryRead(
                    ref reader,
                    out WProtoDecodedPayloadContract rejected
                )
            );
            Assert.IsTrue(rejected == null);
            Assert.IsTrue(reader.Malformed);
            Assert.Throws<SerializationCorruptDataException>(() =>
                WProtoFacade.Deserialize<WProtoDecodedPayloadContract>(
                    bytes,
                    new WProtoReadLimits().WithDecodedPayloadBytes(refused)
                )
            );
        }

        [SetUp]
        public void SetUp()
        {
            WProtoBcl.RegisterAll();
        }

        [TestCase("", 0L, true)]
        [TestCase("A", 0L, false)]
        [TestCase("A", 1L, true)]
        [TestCase("AB", 1L, false)]
        [TestCase("AB", 2L, true)]
        [TestCase("界", 2L, false)]
        [TestCase("界", 3L, true)]
        [TestCase("🙂", 3L, false)]
        [TestCase("🙂", 4L, true)]
        public void StringsChargeDeliveredUtf8Bytes(string text, long maximum, bool expected)
        {
            byte[] bytes = Frame(Encoding.UTF8.GetBytes(text));
            WProtoReader reader = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithDecodedPayloadBytes(maximum)
            );
            Assert.AreEqual(expected, reader.TryReadString(out string actual));
            Assert.AreEqual(expected ? text : null, actual);
            Assert.AreEqual(!expected, reader.Malformed);
            Assert.AreEqual(bytes.Length, reader.Position);
        }

        [TestCase(0, 0L, true)]
        [TestCase(1, 0L, false)]
        [TestCase(1, 1L, true)]
        [TestCase(3, 2L, false)]
        [TestCase(3, 3L, true)]
        public void ByteArraysChargeTheirActualPayload(int length, long maximum, bool expected)
        {
            byte[] payload = new byte[length];
            byte[] bytes = Frame(payload);
            WProtoReader reader = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithDecodedPayloadBytes(maximum)
            );
            Assert.AreEqual(
                expected,
                WProtoGeneric<byte[]>.TryReadValue(ref reader, out byte[] actual)
            );
            if (expected)
            {
                CollectionAssert.AreEqual(payload, actual);
            }
            else
            {
                Assert.IsTrue(actual == null);
            }
            Assert.AreEqual(!expected, reader.Malformed);
        }

        [TestCase(2L, false)]
        [TestCase(3L, true)]
        public void RawUriTextSharesTheDecodedAllowance(long maximum, bool expected)
        {
            byte[] payload = Encoding.UTF8.GetBytes("界");
            WProtoReader reader = new WProtoReader(
                payload,
                new WProtoReadLimits().WithDecodedPayloadBytes(maximum)
            );
            Assert.AreEqual(
                expected,
                WProtoUriFormatter.Instance.TryRead(ref reader, out Uri value)
            );
            if (expected)
            {
                Assert.IsTrue(value != null);
                Assert.AreEqual("界", value.OriginalString);
            }
            else
            {
                Assert.IsTrue(value == null);
                Assert.IsTrue(reader.Malformed);
            }
        }

        [Test]
        public void ReaderCopiesSpendTheSameAllowanceAgain()
        {
            byte[] bytes = { 1, 65, 1, 66 };
            WProtoReader original = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithDecodedPayloadBytes(1)
            );
            WProtoReader replay = original;
            Assert.IsTrue(original.TryReadString(out string first));
            Assert.AreEqual("A", first);
            Assert.IsFalse(replay.TryReadString(out string repeated));
            Assert.IsTrue(repeated == null);
            Assert.IsTrue(replay.Malformed);
            Assert.IsFalse(original.TryReadString(out string second));
            Assert.IsTrue(second == null);
            Assert.IsTrue(original.Malformed);
        }

        [Test]
        public void ParentAwareReconstructionSharesTheAllowance()
        {
            byte[] bytes = { 1, 65 };
            WProtoReader parent = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithDecodedPayloadBytes(1)
            );
            WProtoReader child = new WProtoReader(bytes, in parent);
            Assert.IsTrue(child.TryReadString(out string first));
            Assert.AreEqual("A", first);
            Assert.IsFalse(parent.TryReadString(out string repeated));
            Assert.IsTrue(repeated == null);
            Assert.IsTrue(parent.Malformed);
        }

        [Test]
        public void NestedRegionReadersShareTheAllowance()
        {
            byte[] bytes = { 2, 1, 65, 2, 1, 66 };
            WProtoReader parent = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithDecodedPayloadBytes(1)
            );
            Assert.IsTrue(parent.TryReadMessage(out WProtoReader first));
            Assert.IsTrue(first.TryReadString(out string value));
            Assert.AreEqual("A", value);
            Assert.IsTrue(parent.TryReadMessage(out WProtoReader second));
            Assert.IsFalse(second.TryReadString(out string refused));
            Assert.IsTrue(refused == null);
            Assert.IsTrue(second.Malformed);
        }

        [Test]
        public void PackedRegionsRetainTheDecodedAllowance()
        {
            byte[] bytes = { 2, 1, 65, 1, 66 };
            WProtoReader parent = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithDecodedPayloadBytes(1)
            );
            Assert.IsTrue(parent.TryReadPackedRun(out WProtoReader packed));
            Assert.IsTrue(packed.TryReadString(out string first));
            Assert.AreEqual("A", first);
            Assert.IsFalse(parent.TryReadString(out string refused));
            Assert.IsTrue(refused == null);
            Assert.IsTrue(parent.Malformed);
        }

        [Test]
        public void BorrowedSpansAndUnknownFieldsDoNotSpendTheAllowance()
        {
            byte[] bytes = { 1, 65, 1, 66 };
            WProtoReader reader = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithDecodedPayloadBytes(1)
            );
            Assert.IsTrue(reader.TryReadBytes(out ReadOnlySpan<byte> borrowed));
            Assert.AreEqual(65, borrowed[0]);
            Assert.IsTrue(reader.TryReadString(out string text));
            Assert.AreEqual("B", text);
            byte[] unknown = { 0x0A, 1, 65, 0x82, 0x01, 1, 90 };
            WProtoDecodedPayloadContract value =
                WProtoFacade.Deserialize<WProtoDecodedPayloadContract>(
                    unknown,
                    new WProtoReadLimits().WithDecodedPayloadBytes(1)
                );
            Assert.AreEqual("A", value.Text);
        }

        [Test]
        public void TruncatedPayloadClaimsCannotSpendUndeliveredBytes()
        {
            WProtoReader reader = new WProtoReader(
                new byte[] { 2, 65 },
                new WProtoReadLimits().WithDecodedPayloadBytes(2)
            );
            WProtoReader priorCopy = reader;
            Assert.IsFalse(WProtoGeneric<byte[]>.TryReadValue(ref reader, out byte[] refused));
            Assert.IsTrue(refused == null);
            Assert.IsTrue(reader.Malformed);
            WProtoReader delivered = new WProtoReader(new byte[] { 1, 65, 1, 66 }, in priorCopy);
            Assert.IsTrue(delivered.TryReadString(out string first));
            Assert.IsTrue(delivered.TryReadString(out string second));
            Assert.AreEqual("A", first);
            Assert.AreEqual("B", second);
        }

        [Test]
        public void AServedBudgetRefusalKeepsTheDispatchFailureContract()
        {
            byte[] bytes = { 0x0A, 1, 65 };
            Assert.Throws<InvalidOperationException>(() =>
                WProtoFacade.TryDeserialize<WProtoDecodedPayloadContract>(
                    bytes,
                    new WProtoReadLimits().WithDecodedPayloadBytes(0),
                    out _
                )
            );
        }

        [Test]
        public void IndependentRootsHaveIndependentAllowances()
        {
            WProtoReadLimits limits = new WProtoReadLimits().WithDecodedPayloadBytes(1);
            byte[] bytes = { 1, 65 };
            WProtoReader first = new WProtoReader(bytes, limits);
            WProtoReader second = new WProtoReader(bytes, limits);
            Assert.IsTrue(first.TryReadString(out string left));
            Assert.IsTrue(second.TryReadString(out string right));
            Assert.AreEqual("A", left);
            Assert.AreEqual("A", right);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletedReaderValidationRejectsAnIndependentFiniteAllowance(bool finite)
        {
            WProtoReadLimits limits = new WProtoReadLimits().WithDecodedPayloadBytes(
                finite ? 1 : long.MaxValue
            );
            byte[] bytes = { 1, 65 };
            WProtoReader expected = new WProtoReader(bytes, limits);
            WProtoReader replacement = new WProtoReader(bytes, limits);
            Assert.IsTrue(replacement.TryReadString(out string text));
            Assert.AreEqual("A", text);
            Assert.AreEqual(!finite, WProtoReader.ReadCompleted(true, in replacement, in expected));
        }

        [Test]
        public void DuplicateAndNestedFieldsSpendTheirDeliveredBytes()
        {
            byte[] duplicate = { 0x0A, 1, 65, 0x0A, 1, 66 };
            WProtoDecodedPayloadContract value =
                WProtoFacade.Deserialize<WProtoDecodedPayloadContract>(
                    duplicate,
                    new WProtoReadLimits().WithDecodedPayloadBytes(2)
                );
            Assert.AreEqual("B", value.Text);
            Assert.Throws<SerializationCorruptDataException>(() =>
                WProtoFacade.Deserialize<WProtoDecodedPayloadContract>(
                    duplicate,
                    new WProtoReadLimits().WithDecodedPayloadBytes(1)
                )
            );
            AssertContractBudget(
                new WProtoDecodedPayloadContract
                {
                    Text = "A",
                    Child = new WProtoDecodedPayloadContract
                    {
                        Text = "B",
                        Data = new byte[] { 1, 2 },
                    },
                },
                4,
                3
            );
        }

        [Test]
        public void MapsRepeatedValuesAndUriMessagesShareOneRootAllowance()
        {
            AssertContractBudget(
                new WProtoDecodedPayloadContract
                {
                    Texts = new List<string> { "A", "界", "🙂" },
                    Values = new Dictionary<string, byte[]> { { "K", new byte[] { 1, 2 } } },
                    Address = new Uri("x", UriKind.Relative),
                    Addresses = new List<Uri> { new Uri("y", UriKind.Relative) },
                },
                13,
                12
            );
        }

        [Test]
        public void ZeroLengthValuesDoNotRequirePayloadCredit()
        {
            WProtoDecodedPayloadContract expected = new WProtoDecodedPayloadContract
            {
                Text = string.Empty,
                Data = Array.Empty<byte>(),
                Texts = new List<string> { string.Empty, string.Empty },
                Values = new Dictionary<string, byte[]> { { string.Empty, Array.Empty<byte>() } },
            };
            byte[] bytes = WProtoFacade.Serialize(expected);
            WProtoDecodedPayloadContract actual =
                WProtoFacade.Deserialize<WProtoDecodedPayloadContract>(
                    bytes,
                    new WProtoReadLimits().WithDecodedPayloadBytes(0)
                );
            CollectionAssert.AreEqual(bytes, WProtoFacade.Serialize(actual));
        }

        [Test]
        public void InvalidUtf8ConsumesItsAttemptAndLatchesFailure()
        {
            byte[] bytes = { 1, 0xFF };
            WProtoReader reader = new WProtoReader(
                bytes,
                new WProtoReadLimits().WithDecodedPayloadBytes(1)
            );
            WProtoReader priorCopy = reader;
            Assert.IsFalse(reader.TryReadString(out string value));
            Assert.IsTrue(value == null);
            Assert.IsTrue(reader.Malformed);
            WProtoReader validReplay = new WProtoReader(new byte[] { 1, 65 }, in priorCopy);
            Assert.IsFalse(validReplay.TryReadString(out string replay));
            Assert.IsTrue(replay == null);
            Assert.IsTrue(validReplay.Malformed);
        }

        [TestCase(long.MinValue, 0L)]
        [TestCase(-1L, 0L)]
        [TestCase(0L, 0L)]
        [TestCase(7L, 7L)]
        [TestCase(long.MaxValue, long.MaxValue)]
        public void ConfigurationClampsNegativesAndPreservesUnlimitedDefaults(
            long requested,
            long expected
        )
        {
            WProtoReadLimits original = new WProtoReadLimits(101, 102, 103, 4, 105);
            Assert.AreEqual(long.MaxValue, original.MaximumDecodedPayloadBytes);
            WProtoReadLimits configured = original.WithDecodedPayloadBytes(requested);
            Assert.AreEqual(expected, configured.MaximumDecodedPayloadBytes);
            Assert.AreEqual(101, configured.MaximumMessageBytes);
            Assert.AreEqual(102, configured.MaximumLengthDelimitedBytes);
            Assert.AreEqual(103, configured.MaximumFieldCount);
            Assert.AreEqual(4, configured.MaximumNestingDepth);
            Assert.AreEqual(105, configured.MaximumPackedElementCount);
            Assert.AreEqual(int.MaxValue, configured.MaximumTotalFieldCount);
            Assert.AreEqual(long.MaxValue, configured.MaximumMessageMergeCopyBytes);
            Assert.AreEqual(long.MaxValue, original.MaximumDecodedPayloadBytes);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void FluentLimitsPreserveEveryOtherAllowance(int order)
        {
            WProtoReadLimits original = new WProtoReadLimits(101, 102, 103, 4, 105);
            WProtoReadLimits limits =
                order == 0
                    ? original
                        .WithDecodedPayloadBytes(7)
                        .WithTotalFieldCount(11)
                        .WithMessageMergeCopyBytes(13)
                : order == 1
                    ? original
                        .WithMessageMergeCopyBytes(13)
                        .WithDecodedPayloadBytes(7)
                        .WithTotalFieldCount(11)
                : original
                    .WithTotalFieldCount(11)
                    .WithMessageMergeCopyBytes(13)
                    .WithDecodedPayloadBytes(7);
            Assert.AreEqual(7, limits.MaximumDecodedPayloadBytes);
            Assert.AreEqual(11, limits.MaximumTotalFieldCount);
            Assert.AreEqual(13, limits.MaximumMessageMergeCopyBytes);
            Assert.AreEqual(101, limits.MaximumMessageBytes);
            Assert.AreEqual(102, limits.MaximumLengthDelimitedBytes);
            Assert.AreEqual(103, limits.MaximumFieldCount);
            Assert.AreEqual(4, limits.MaximumNestingDepth);
            Assert.AreEqual(105, limits.MaximumPackedElementCount);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RefusalPrecedesPayloadSizedMaterialization(bool text)
        {
            const int payloadLength = 1 << 20;
            byte[] bytes = new byte[payloadLength + 3];
            bytes[0] = 0x80;
            bytes[1] = 0x80;
            bytes[2] = 0x40;
            for (int index = 3; index < bytes.Length; ++index)
            {
                bytes[index] = 65;
            }
            WProtoReadLimits limits = new WProtoReadLimits().WithDecodedPayloadBytes(0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            byte[] positive = new byte[payloadLength];
            positive[0] = 1;
            long sensitivity = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(positive);
            if (sensitivity < payloadLength)
            {
                Assert.Inconclusive(
                    "A known payload-sized allocation was not measured; this runtime cannot qualify the refusal allocation bound."
                );
            }
            Assert.IsTrue(RefusesMaterialization(bytes, text, limits));
            before = GC.GetAllocatedBytesForCurrentThread();
            bool refused = RefusesMaterialization(bytes, text, limits);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(refused);
            Assert.Less(allocated, payloadLength / 16);
        }
    }
}
