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
                "2ef7a474e0c25cb1e4af3da8673a28fb0716f580002576a913eaf44087d605ea";
            const string expectedCoreSha256 =
                "8f2c189b4c8f979be4ac509208b2b0071853340563042dc0226f57f6d23e1701";
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
    }
}
