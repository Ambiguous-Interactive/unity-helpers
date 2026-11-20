// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
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

        [TestCaseSource(nameof(KnownDigestCases))]
        public void Sha256HexSpanMatchesKnownDigests(string text, string expected)
        {
            byte[] payload = Encoding.UTF8.GetBytes(text);
            Assert.AreEqual(expected, Objects.Sha256Hex(payload.AsSpan()));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(55)]
        [TestCase(56)]
        [TestCase(57)]
        [TestCase(63)]
        [TestCase(64)]
        [TestCase(65)]
        [TestCase(119)]
        [TestCase(120)]
        [TestCase(121)]
        [TestCase(128)]
        [TestCase(4097)]
        public void TrySha256MatchesPlatformDigestAtBlockBoundaries(int length)
        {
            const int sourceOffset = 3;
            const int destinationOffset = 5;
            const byte sentinel = 0xB7;
            byte[] buffer = new byte[length + sourceOffset + 2];
            new Random(1226).NextBytes(buffer);
            using SHA256 oracle = SHA256.Create();
            byte[] expected = oracle.ComputeHash(buffer, sourceOffset, length);
            byte[] output = new byte[destinationOffset + expected.Length + 1];
            Array.Fill(output, sentinel);

            Assert.IsTrue(
                Objects.TrySha256(
                    buffer.AsSpan(sourceOffset, length),
                    output.AsSpan(destinationOffset)
                )
            );
            byte[] expectedOutput = new byte[output.Length];
            Array.Fill(expectedOutput, sentinel);
            expected.CopyTo(expectedOutput, destinationOffset);
            CollectionAssert.AreEqual(expectedOutput, output);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(31)]
        public void TrySha256LeavesInsufficientDestinationUntouched(int destinationLength)
        {
            byte[] payload = Encoding.UTF8.GetBytes("abc");
            byte[] destination = new byte[32];
            new Random(1226).NextBytes(destination);
            byte[] original = (byte[])destination.Clone();

            Assert.IsFalse(Objects.TrySha256(payload, destination.AsSpan(0, destinationLength)));
            CollectionAssert.AreEqual(original, destination);
        }

        [TestCase(0)]
        [TestCase(3)]
        [TestCase(61)]
        public void TrySha256SupportsOverlappingDestination(int destinationOffset)
        {
            const int sourceOffset = 3;
            const int sourceLength = 96;
            const int digestLength = 32;

            byte[] buffer = new byte[sourceOffset + sourceLength + digestLength];
            new Random(256).NextBytes(buffer);
            byte[] original = (byte[])buffer.Clone();
            using SHA256 oracle = SHA256.Create();
            byte[] expected = oracle.ComputeHash(original, sourceOffset, sourceLength);

            Assert.IsTrue(
                Objects.TrySha256(
                    buffer.AsSpan(sourceOffset, sourceLength),
                    buffer.AsSpan(destinationOffset, digestLength)
                )
            );
            expected.CopyTo(original, destinationOffset);
            CollectionAssert.AreEqual(original, buffer);
        }

        [TestCase(0, -1, true)]
        [TestCase(1, 0, false)]
        [TestCase(32, -1, true)]
        [TestCase(32, 0, false)]
        [TestCase(129, 64, false)]
        [TestCase(129, 128, false)]
        public void BytesEqualComparesOnlySelectedBytes(int length, int mismatch, bool expected)
        {
            byte[] first = new byte[length + 2];
            byte[] second = new byte[length + 4];
            new Random(1226).NextBytes(first);
            first.AsSpan(1, length).CopyTo(second.AsSpan(3, length));
            if (0 <= mismatch)
            {
                second[3 + mismatch] ^= 0xFF;
            }

            Assert.AreEqual(
                expected,
                Objects.BytesEqual(first.AsSpan(1, length), second.AsSpan(3, length))
            );
            Assert.IsFalse(
                Objects.BytesEqual(first.AsSpan(1, length), second.AsSpan(3, length + 1))
            );
        }

#if UNITY_5_3_OR_NEWER
        [Test]
        public void SpanHashesAndEqualityHaveOnlyExpectedProfilerAllocations()
        {
            const int iterations = 128;
            const int capacity = iterations * 8;
            string retained = string.Empty;
            Action allocateString = () => retained = new string('x', 64);
            long positive = Core.ProfilerAllocationCounter.Measure(
                allocateString,
                iterations,
                capacity
            );
            if (positive == 0)
            {
                Assert.Ignore("The GC.Alloc recorder did not observe fresh strings.");
            }
            Assert.AreEqual(iterations, positive);
            Action reuseString = () => retained = string.Empty;
            Assert.AreEqual(
                0,
                Core.ProfilerAllocationCounter.Measure(reuseString, iterations, capacity)
            );

            byte[] payload = new byte[65];
            byte[] other = new byte[payload.Length];
            byte[] digest = new byte[32];
            bool wrote = false;
            bool matches = false;
            Action writeDigest = () => wrote = Objects.TrySha256(payload, digest);
            Action compare = () => matches = Objects.BytesEqual(payload, other);
            Action hexDigest = () => retained = Objects.Sha256Hex(payload.AsSpan());
            Assert.AreEqual(
                0,
                Core.ProfilerAllocationCounter.Measure(writeDigest, iterations, capacity)
            );
            Assert.AreEqual(
                0,
                Core.ProfilerAllocationCounter.Measure(compare, iterations, capacity)
            );
            Assert.AreEqual(
                positive,
                Core.ProfilerAllocationCounter.Measure(hexDigest, iterations, capacity)
            );
            Assert.IsTrue(wrote);
            Assert.IsTrue(matches);
            Assert.AreEqual(64, retained.Length);
        }
#endif

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
