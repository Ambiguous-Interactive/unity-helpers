// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [TestFixture]
    [Category("Fast")]
    public sealed class WProtoCorpusTests
    {
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

        [Test]
        public void FrozenCorpusHasIndependentSemanticWitnesses()
        {
            WProtoBenchmarkCorpus.VerifyFrozenCorpus();
        }
    }
}
