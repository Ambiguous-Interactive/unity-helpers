// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ObjectsSha256Tests
    {
        private const string EmptyStringDigest =
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

        private const string AbcDigest =
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        private const string FoxDigest =
            "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592";

        private const string AccentedDigest =
            "cdfab900629099e3849d71299159850f4efde07c77e95c41c1185dcb728a8bd9";

        private static IEnumerable<TestCaseData> KnownDigestCases()
        {
            yield return new TestCaseData(string.Empty, EmptyStringDigest).SetName(
                "Sha256Hex.Text.EmptyString"
            );
            yield return new TestCaseData("abc", AbcDigest).SetName("Sha256Hex.Text.Abc");
            yield return new TestCaseData(
                "The quick brown fox jumps over the lazy dog",
                FoxDigest
            ).SetName("Sha256Hex.Text.Fox");
            yield return new TestCaseData("áéíóú", AccentedDigest).SetName(
                "Sha256Hex.Text.NonAsciiUtf8"
            );
        }

        [Test]
        [TestCaseSource(nameof(KnownDigestCases))]
        public void Sha256HexMatchesKnownDigests(string text, string expected)
        {
            Assert.AreEqual(expected, Objects.Sha256Hex(text));
        }

        [Test]
        public void Sha256HexByteOverloadMatchesTextOverload()
        {
            byte[] payload = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
            Assert.AreEqual(
                Objects.Sha256Hex("The quick brown fox jumps over the lazy dog"),
                Objects.Sha256Hex(payload)
            );
        }

        [Test]
        public void Sha256HexIsCaseSensitiveHex()
        {
            string lower = Objects.Sha256Hex("abc");
            string upper = Objects.Sha256Hex("ABC");

            Assert.AreNotEqual(lower, upper);
            StringAssert.IsMatch("^[0-9a-f]{64}$", lower);
        }

        [Test]
        public void Sha256HexRejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => _ = Objects.Sha256Hex((string)null));
            Assert.Throws<ArgumentNullException>(() => _ = Objects.Sha256Hex((byte[])null));
        }

        [Test]
        public void TrySha256HexOfFileHashesFileContents()
        {
            string path = Path.Combine(Path.GetTempPath(), $"wuh-sha256-{Guid.NewGuid():N}.txt");
            try
            {
                File.WriteAllText(path, "abc");
                Assert.IsTrue(Objects.TrySha256HexOfFile(path, out string hex));
                Assert.AreEqual(AbcDigest, hex);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void TrySha256HexOfFileRefusesMissingFile()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                $"wuh-sha256-{Guid.NewGuid():N}.missing"
            );
            Assert.IsFalse(Objects.TrySha256HexOfFile(path, out string hex));
            Assert.IsTrue(hex == null);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void TrySha256HexOfFileRefusesNullOrEmptyPath(string path)
        {
            Assert.IsFalse(Objects.TrySha256HexOfFile(path, out string hex));
            Assert.IsTrue(hex == null);
        }
    }
}
