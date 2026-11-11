// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Fast")]
    public sealed class WProtoCorpusTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(32)]
        [TestCase(1048576)]
        public void ByteOracleChecksLengthAndEveryBoundaryMutation(int length)
        {
            byte[] expected = new byte[length];
            for (int index = 0; index < length; ++index)
            {
                expected[index] = unchecked((byte)(index * 31 + 7));
            }
            byte[] actual = (byte[])expected.Clone();
            WProtoBenchmarkCorpus.AssertBytesEqual(expected, actual);
            byte[] differentLength = new byte[length + 1];
            Assert.Throws<AssertionException>(() =>
                WProtoBenchmarkCorpus.AssertBytesEqual(expected, differentLength)
            );
            if (length == 0)
            {
                return;
            }
            foreach (int index in new[] { 0, length / 2, length - 1 })
            {
                actual[index] ^= 1;
                Assert.Throws<AssertionException>(() =>
                    WProtoBenchmarkCorpus.AssertBytesEqual(expected, actual)
                );
                actual[index] ^= 1;
            }
            WProtoBenchmarkCorpus.AssertBytesEqual(expected, actual);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FrozenIdentitySetRejectsMissingOrExtraWorkloads(bool includeUnpacked)
        {
            HashSet<string> complete = new HashSet<string>(
                ProtoBenchmarkCorpusIdentity.Identifiers(includeUnpacked),
                StringComparer.Ordinal
            );
            Assert.IsTrue(ProtoBenchmarkCorpusIdentity.IsComplete(complete, includeUnpacked));
            Assert.AreEqual(includeUnpacked ? 32 : 28, complete.Count);
            Assert.IsTrue(complete.Remove("large-binary/1024"));
            Assert.IsFalse(ProtoBenchmarkCorpusIdentity.IsComplete(complete, includeUnpacked));
            complete.Add("large-binary/1024");
            complete.Add("unknown-workload");
            Assert.IsFalse(ProtoBenchmarkCorpusIdentity.IsComplete(complete, includeUnpacked));
        }

        [Test]
        public void FrozenIdentityRejectsDuplicateWorkloadAndChangedBytes()
        {
            byte[] payload = WProtoFacade.Serialize(new ProtoBenchmarkScalars());
            using SHA256 fingerprint = SHA256.Create();
            string hash = Convert.ToBase64String(fingerprint.ComputeHash(payload));
            HashSet<string> observed = new HashSet<string>(StringComparer.Ordinal);
            Assert.IsTrue(
                ProtoBenchmarkCorpusIdentity.TryAccept(
                    observed,
                    "scalar-heavy/0",
                    payload.Length,
                    hash
                )
            );
            Assert.IsFalse(
                ProtoBenchmarkCorpusIdentity.TryAccept(
                    observed,
                    "scalar-heavy/0",
                    payload.Length,
                    hash
                )
            );
            observed.Clear();
            Assert.IsFalse(
                ProtoBenchmarkCorpusIdentity.TryAccept(
                    observed,
                    "scalar-heavy/0",
                    payload.Length + 1,
                    hash
                )
            );
            payload[payload.Length - 1] ^= 1;
            string changed = Convert.ToBase64String(fingerprint.ComputeHash(payload));
            Assert.IsFalse(
                ProtoBenchmarkCorpusIdentity.TryAccept(
                    observed,
                    "scalar-heavy/0",
                    payload.Length,
                    changed
                )
            );
            Assert.IsEmpty(observed);
        }

        [Test]
        public void ReusableOracleBufferRewindsWithoutClearingPayload()
        {
            ProtoBenchmarkBufferWriter writer = new ProtoBenchmarkBufferWriter(8);
            writer.GetSpan(8)[0] = 123;
            writer.Advance(8);
            writer.Rewind();
            Assert.AreEqual(0, writer.WrittenCount);
            Assert.AreEqual(123, writer.GetSpan(8)[0]);
            writer.Advance(1);
            Assert.AreEqual(123, writer.WrittenSpan[0]);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void MapWitnessMatchesLegacyForReorderedEntries(bool reverse, bool referenceIdentity)
        {
            ProtoBenchmarkMap expected = new()
            {
                Value = new Dictionary<string, int>
                {
                    ["first"] = int.MinValue,
                    ["second"] = int.MaxValue,
                },
            };
            IEqualityComparer<string> comparer = referenceIdentity
                ? ReferenceIdentityComparer<string>.Instance
                : StringComparer.Ordinal;
            ProtoBenchmarkMap actual = new() { Value = new Dictionary<string, int>(comparer) };
            string first = new("first".ToCharArray());
            string second = new("second".ToCharArray());
            if (reverse)
            {
                actual.Value.Add(second, int.MaxValue);
                actual.Value.Add(first, int.MinValue);
            }
            else
            {
                actual.Value.Add(first, int.MinValue);
                actual.Value.Add(second, int.MaxValue);
            }
            CollectionAssert.AreEquivalent(expected.Value, actual.Value);
            WProtoBenchmarkCorpus.AssertMaps(expected, actual);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void MapWitnessRejectsChangedEntries(int mutation)
        {
            ProtoBenchmarkMap expected = new()
            {
                Value = new Dictionary<string, int> { ["first"] = 17, ["second"] = 34 },
            };
            ProtoBenchmarkMap actual = new()
            {
                Value = new Dictionary<string, int>(expected.Value, StringComparer.Ordinal),
            };
            switch (mutation)
            {
                case 0:
                    actual.Value["first"] = 18;
                    break;
                case 1:
                    actual.Value.Remove("first");
                    break;
                case 2:
                    actual.Value.Add("third", 51);
                    break;
                case 3:
                    actual.Value.Remove("first");
                    actual.Value.Add("FIRST", 17);
                    break;
                case 4:
                    actual.Value = null;
                    break;
                case 5:
                    actual.Value = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["FIRST"] = 17,
                        ["second"] = 34,
                    };
                    break;
                case 6:
                    actual.Value = new Dictionary<string, int>(
                        ReferenceIdentityComparer<string>.Instance
                    )
                    {
                        ["first"] = 17,
                        [new string("first".ToCharArray())] = 17,
                    };
                    break;
            }
            Assert.Throws<AssertionException>(() =>
                CollectionAssert.AreEquivalent(
                    expected.Value,
                    actual.Value ?? new Dictionary<string, int>()
                )
            );
            Assert.Throws<AssertionException>(() =>
                WProtoBenchmarkCorpus.AssertMaps(expected, actual)
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MapWitnessAcceptsMissingEmptyMap(bool missing)
        {
            ProtoBenchmarkMap expected = new() { Value = new Dictionary<string, int>() };
            ProtoBenchmarkMap actual = new()
            {
                Value = missing ? null : new Dictionary<string, int>(),
            };
            CollectionAssert.AreEquivalent(
                expected.Value,
                actual.Value ?? new Dictionary<string, int>()
            );
            WProtoBenchmarkCorpus.AssertMaps(expected, actual);
        }

        [Test]
        public void MapWitnessHandlesReversedLargeMap()
        {
            const int count = 1024;
            ProtoBenchmarkMap expected = new() { Value = new Dictionary<string, int>() };
            ProtoBenchmarkMap actual = new() { Value = new Dictionary<string, int>() };
            for (int index = 0; index < count; ++index)
            {
                expected.Value.Add(index.ToString(), index * 17);
            }
            for (int index = count - 1; 0 <= index; --index)
            {
                actual.Value.Add(index.ToString(), index * 17);
            }
            WProtoBenchmarkCorpus.AssertMaps(expected, actual);
        }

        [Test]
        public void FrozenCorpusHasIndependentSemanticWitnesses()
        {
            WProtoBenchmarkCorpus.VerifyFrozenCorpus();
        }
    }
}
