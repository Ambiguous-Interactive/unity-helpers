// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.IO;
    using System.Reflection;
    using System.Security.Cryptography;
    using System.Text;
    using NUnit.Framework;

    /// <summary>
    /// Proves each isolated differential process loaded the oracle it was built to test.
    /// </summary>
    [TestFixture]
    public sealed class OracleIdentityTests
    {
        private static string Sha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(stream);
                StringBuilder value = new StringBuilder(hash.Length * 2);
                foreach (byte current in hash)
                {
                    value.Append(current.ToString("x2"));
                }
                return value.ToString();
            }
        }

        [Test]
        public void LoadedOracleHasTheExpectedPhysicalIdentity()
        {
#if PROTOBUF_NET_ORACLE_V2
            const string expectedAssemblyVersion = "2.4.0.0";
            const string expectedInformationalVersion = "2.4.9.1+f4bacb1a94";
            const string expectedSha256 =
                "8f8f1c205ecebb5bd74d0bed130e8ce8745e8053f52838517d74535f2096742c";
#else
            const string expectedAssemblyVersion = "3.0.0.0";
            const string expectedInformationalVersion = "3.2.56+dfdfce61a7";
            const string expectedSha256 =
                "cc04f018f1a637c77c38f7d20de6fb57f99ce2b04d2a89f56e71d39d617c19d5";
            const string expectedCoreSha256 =
                "b5a43e5c4e84e69bcd3eb4055e78e4667756f195fb3e86b9b54e066036e97c8c";
#endif

            Assembly oracle = typeof(ProtoBuf.Serializer).Assembly;
            AssemblyName identity = oracle.GetName();
            AssemblyInformationalVersionAttribute information =
                oracle.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            Assert.AreEqual("protobuf-net", identity.Name);
            Assert.AreEqual(expectedAssemblyVersion, identity.Version.ToString());
            Assert.IsTrue(information != null, "The oracle must identify its source build.");
            Assert.AreEqual(expectedInformationalVersion, information.InformationalVersion);
            Assert.AreEqual(expectedSha256, Sha256(oracle.Location));

            Assembly core = typeof(ProtoBuf.ProtoContractAttribute).Assembly;
#if PROTOBUF_NET_ORACLE_V2
            Assert.AreEqual(oracle, core, "v2 keeps Serializer and contract attributes together");
#else
            AssemblyName coreIdentity = core.GetName();
            AssemblyInformationalVersionAttribute coreInformation =
                core.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            Assert.AreEqual("protobuf-net.Core", coreIdentity.Name);
            Assert.AreEqual("3.0.0.0", coreIdentity.Version.ToString());
            Assert.IsTrue(
                coreInformation != null,
                "The core oracle must identify its source build."
            );
            Assert.AreEqual(expectedInformationalVersion, coreInformation.InformationalVersion);
            Assert.AreEqual(expectedCoreSha256, Sha256(core.Location));
#endif
        }
    }
}
