// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    public sealed class BorrowedBase64Tests
    {
        private const int CallsPerIteration = 16;
        private const int CorrectnessRowCount = 35;
        private const int CampaignRowCount = 12;
        private const byte CorrectnessSection = 1;
        private const byte CampaignSection = 2;
        private const string CorpusMagic = "UH-BORROWED-BASE64";
        private const string CorpusMarker = "BORROWED_BASE64_CORPUS";
        private const string AsciiShape = "ascii";
        private const string UnicodeShape = "unicode";
        private const string InvalidUtf8Shape = "invalidUtf8";
        private const string MalformedShape = "malformed";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly BufferFunc<byte, string, string> DecodeCallback = DecodeBuffer;
        private static readonly CorpusCase[] Corpus = CreateCorpus();

        /// <summary>Exports the exact correctness and campaign inputs as canonical bytes.</summary>
        public static byte[] CreateCanonicalCorpusBytes()
        {
            return SerializeCorpus(Corpus);
        }

        /// <summary>Hashes the canonical correctness and campaign inputs with SHA256.</summary>
        public static string GetCanonicalCorpusSha256()
        {
            return HashCorpus(CreateCanonicalCorpusBytes());
        }

        /// <summary>Provides campaign cases from the snapshot used by the decoder controls.</summary>
        public static IEnumerable<TestCaseData> GetCampaignTestCases(string methodName)
        {
            if (string.IsNullOrWhiteSpace(methodName))
            {
                yield break;
            }
            foreach (CorpusCase row in Corpus)
            {
                if (row.Section == CampaignSection)
                {
                    yield return new TestCaseData(row.Shape, row.DeclaredLength).SetName(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "{0}(\"{1}\",{2})",
                            methodName,
                            row.Shape,
                            row.DeclaredLength
                        )
                    );
                }
            }
        }

        private static CorpusCase[] CreateCorpus()
        {
            List<CorpusCase> rows = new List<CorpusCase>(CorrectnessRowCount + CampaignRowCount);
            foreach (
                int length in new[]
                {
                    1,
                    2,
                    3,
                    15,
                    16,
                    17,
                    31,
                    32,
                    33,
                    255,
                    256,
                    257,
                    8191,
                    8192,
                    8193,
                }
            )
            {
                StringBuilder builder = new StringBuilder(length);
                for (int index = 0; index < length; ++index)
                {
                    builder.Append((char)(' ' + index % 95));
                }
                string expected = builder.ToString();
                rows.Add(
                    new CorpusCase(
                        CorrectnessSection,
                        "c" + (rows.Count + 1).ToString("D2", CultureInfo.InvariantCulture),
                        AsciiShape,
                        length,
                        Convert.ToBase64String(StrictUtf8.GetBytes(expected)),
                        expected
                    )
                );
            }
            foreach (
                string expected in new[]
                {
                    "\u754c",
                    "\ud83c\udf0d",
                    "\0\t\r\n",
                    "a\u754c\ud83c\udf0dz",
                }
            )
            {
                rows.Add(
                    new CorpusCase(
                        CorrectnessSection,
                        "c" + (rows.Count + 1).ToString("D2", CultureInfo.InvariantCulture),
                        UnicodeShape,
                        expected.Length,
                        Convert.ToBase64String(StrictUtf8.GetBytes(expected)),
                        expected
                    )
                );
            }
            foreach (
                byte[] invalid in new[]
                {
                    new byte[] { 0xff },
                    new byte[] { 0xc0, 0xaf },
                    new byte[] { 0xe2, 0x82 },
                    new byte[] { 0xed, 0xa0, 0x80 },
                    new byte[] { 0xf4, 0x90, 0x80, 0x80 },
                }
            )
            {
                rows.Add(
                    new CorpusCase(
                        CorrectnessSection,
                        "c" + (rows.Count + 1).ToString("D2", CultureInfo.InvariantCulture),
                        InvalidUtf8Shape,
                        invalid.Length,
                        Convert.ToBase64String(invalid),
                        string.Empty
                    )
                );
            }
            foreach (
                string malformed in new[]
                {
                    null,
                    "",
                    "A",
                    "AA",
                    "AAA",
                    "AA!A",
                    "AA A",
                    "A=AA",
                    "=AAA",
                    "AAAA====",
                    "AAAA\r\n",
                }
            )
            {
                rows.Add(
                    new CorpusCase(
                        CorrectnessSection,
                        "c" + (rows.Count + 1).ToString("D2", CultureInfo.InvariantCulture),
                        MalformedShape,
                        malformed == null ? 0 : malformed.Length,
                        malformed,
                        string.Empty
                    )
                );
            }
            int campaignIndex = 0;
            foreach (int length in new[] { 1, 2, 3, 8192 })
            {
                string expected = new string('a', length);
                rows.Add(
                    new CorpusCase(
                        CampaignSection,
                        "p" + (++campaignIndex).ToString("D2", CultureInfo.InvariantCulture),
                        AsciiShape,
                        length,
                        Convert.ToBase64String(StrictUtf8.GetBytes(expected)),
                        expected
                    )
                );
            }
            foreach (int length in new[] { 1, 8192 })
            {
                string expected = new string('\u754c', length) + "\ud83c\udf0d";
                rows.Add(
                    new CorpusCase(
                        CampaignSection,
                        "p" + (++campaignIndex).ToString("D2", CultureInfo.InvariantCulture),
                        "multibyte",
                        length,
                        Convert.ToBase64String(StrictUtf8.GetBytes(expected)),
                        expected
                    )
                );
            }
            foreach (int length in new[] { 1, 8192 })
            {
                byte[] invalid = new byte[length];
                Array.Fill(invalid, (byte)0xff);
                rows.Add(
                    new CorpusCase(
                        CampaignSection,
                        "p" + (++campaignIndex).ToString("D2", CultureInfo.InvariantCulture),
                        InvalidUtf8Shape,
                        length,
                        Convert.ToBase64String(invalid),
                        string.Empty
                    )
                );
            }
            rows.Add(
                new CorpusCase(
                    CampaignSection,
                    "p09",
                    "invalidPadding",
                    1,
                    "AAAA====",
                    string.Empty
                )
            );
            rows.Add(
                new CorpusCase(CampaignSection, "p10", "invalidAlphabet", 1, "AA!A", string.Empty)
            );
            rows.Add(
                new CorpusCase(CampaignSection, "p11", "empty", 0, string.Empty, string.Empty)
            );
            rows.Add(new CorpusCase(CampaignSection, "p12", "null", 0, null, string.Empty));
            return rows.ToArray();
        }

        private static byte[] SerializeCorpus(CorpusCase[] rows)
        {
            using MemoryStream stream = new MemoryStream();
            byte[] magic = Encoding.ASCII.GetBytes(CorpusMagic);
            stream.Write(magic, 0, magic.Length);
            stream.WriteByte(0);
            WriteUnsignedLittleEndian(stream, 1);
            WriteUnsignedLittleEndian(stream, (uint)rows.Length);
            foreach (CorpusCase row in rows)
            {
                stream.WriteByte(row.Section);
                WriteCanonicalString(stream, row.Id);
                WriteCanonicalString(stream, row.Shape);
                WriteUnsignedLittleEndian(stream, (uint)row.DeclaredLength);
                WriteCanonicalString(stream, row.Input);
                WriteCanonicalString(stream, row.Expected);
            }
            return stream.ToArray();
        }

        private static void WriteUnsignedLittleEndian(Stream stream, uint value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 24));
        }

        private static void WriteCanonicalString(Stream stream, string value)
        {
            if (value == null)
            {
                stream.WriteByte(0);
                return;
            }
            stream.WriteByte(1);
            byte[] bytes = StrictUtf8.GetBytes(value);
            WriteUnsignedLittleEndian(stream, (uint)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string HashCorpus(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            byte[] digest = hash.ComputeHash(bytes);
            StringBuilder text = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
            {
                text.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        private static bool TryGetCampaignCase(string shape, int length, out CorpusCase selected)
        {
            foreach (CorpusCase row in Corpus)
            {
                if (
                    row.Section == CampaignSection
                    && row.DeclaredLength == length
                    && string.Equals(row.Shape, shape, StringComparison.Ordinal)
                )
                {
                    selected = row;
                    return true;
                }
            }
            selected = default;
            return false;
        }

        private static void VerifyCanonicalControls()
        {
            using MemoryStream strings = new MemoryStream();
            WriteCanonicalString(strings, null);
            WriteCanonicalString(strings, string.Empty);
            WriteCanonicalString(strings, "\u754c\ud83c\udf0d");
            CollectionAssert.AreEqual(
                new byte[]
                {
                    0,
                    1,
                    0,
                    0,
                    0,
                    0,
                    1,
                    7,
                    0,
                    0,
                    0,
                    0xe7,
                    0x95,
                    0x8c,
                    0xf0,
                    0x9f,
                    0x8c,
                    0x8d,
                },
                strings.ToArray()
            );
            using MemoryStream integers = new MemoryStream();
            WriteUnsignedLittleEndian(integers, 0x12345678);
            CollectionAssert.AreEqual(new byte[] { 0x78, 0x56, 0x34, 0x12 }, integers.ToArray());
            byte[] canonical = CreateCanonicalCorpusBytes();
            CollectionAssert.AreEqual(
                Encoding.ASCII.GetBytes(CorpusMagic + "\0"),
                new ArraySegment<byte>(canonical, 0, CorpusMagic.Length + 1)
            );
            string digest = HashCorpus(canonical);
            // Frozen independently from the original 35 vectors and 12 declarations.
            Assert.That(canonical.Length, Is.EqualTo(148757));
            Assert.That(
                digest,
                Is.EqualTo("d3933603fc240334440040824a7d56316a4e5bafc0124c611cffa6daf2ef8052")
            );
            Assert.That(
                HashCorpus(
                    SerializeCorpus(
                        new[] { new CorpusCase(CorrectnessSection, "x", "u", 2, null, "\u754c") }
                    )
                ),
                Is.EqualTo("90655ba5d4a96d79819275d8c6be93cf746771dde72e997fbb545bd8e60fdf52")
            );
            CorpusCase[] changed = new CorpusCase[Corpus.Length];
            Array.Copy(Corpus, changed, Corpus.Length);
            CorpusCase first = changed[0];
            changed[0] = new CorpusCase(
                first.Section,
                first.Id,
                first.Shape,
                first.DeclaredLength,
                first.Input,
                first.Expected + "x"
            );
            Assert.That(HashCorpus(SerializeCorpus(changed)), Is.Not.EqualTo(digest));
            changed[0] = Corpus[1];
            changed[1] = first;
            Assert.That(HashCorpus(SerializeCorpus(changed)), Is.Not.EqualTo(digest));
            Array.Copy(Corpus, changed, Corpus.Length);
            changed[1] = first;
            Assert.That(HashCorpus(SerializeCorpus(changed)), Is.Not.EqualTo(digest));
            Array.Copy(Corpus, changed, Corpus.Length);
            Array.Resize(ref changed, changed.Length - 1);
            Assert.That(HashCorpus(SerializeCorpus(changed)), Is.Not.EqualTo(digest));
            changed = new CorpusCase[Corpus.Length];
            Array.Copy(Corpus, changed, Corpus.Length);
            CorpusCase campaign = changed[CorrectnessRowCount];
            changed[CorrectnessRowCount] = new CorpusCase(
                campaign.Section,
                campaign.Id,
                campaign.Shape,
                campaign.DeclaredLength + 1,
                campaign.Input,
                campaign.Expected
            );
            Assert.That(HashCorpus(SerializeCorpus(changed)), Is.Not.EqualTo(digest));
            Assert.IsFalse(TryGetCampaignCase("unknown", 1, out _));
            Assert.IsFalse(TryGetCampaignCase(AsciiShape, 4, out _));
        }

        private static string DecodeBorrowed(string input)
        {
            if (string.IsNullOrEmpty(input) || !IsLikelyBase64(input))
            {
                return string.Empty;
            }
            int length = input.Length;
            int padding = input[length - 1] == '=' ? (input[length - 2] == '=' ? 2 : 1) : 0;
            int outputLength = (length >> 2) * 3 - padding;
            if (outputLength < 0)
            {
                return string.Empty;
            }
            return SystemArrayPool<byte>.TryWithBuffer(
                outputLength,
                input,
                DecodeCallback,
                out string result,
                out _
            )
                ? result
                : string.Empty;
        }

        private static long Run(string input, bool borrowed, int iterations)
        {
            long checksum = 0;
            for (int iteration = 0; iteration < iterations; ++iteration)
            {
                for (int call = 0; call < CallsPerIteration; ++call)
                {
                    string result = borrowed ? DecodeBorrowed(input) : input.FromBase64();
                    checksum = unchecked(checksum + result.Length);
                    if (result.Length != 0)
                    {
                        checksum = unchecked(checksum + result[0] + result[result.Length - 1]);
                    }
                }
            }
            return checksum;
        }

        private static bool IsLikelyBase64(string s)
        {
            int len = s.Length;
            if (len == 0 || (len & 3) != 0)
            {
                return false;
            }

            int padding = 0;
            if (s[len - 1] == '=')
            {
                padding = 1;
                if (s[len - 2] == '=')
                {
                    padding = 2;
                }
            }

            int effectiveLen = len - padding;
            for (int i = 0; i < effectiveLen; ++i)
            {
                char c = s[i];
                bool isAlphaUpper = c is >= 'A' and <= 'Z';
                bool isAlphaLower = c is >= 'a' and <= 'z';
                bool isDigit = c is >= '0' and <= '9';
                bool isPlusSlash = c == '+' || c == '/';
                if (!(isAlphaUpper || isAlphaLower || isDigit || isPlusSlash))
                {
                    return false;
                }
            }

            for (int i = 0; i < effectiveLen; ++i)
            {
                if (s[i] == '=')
                {
                    return false;
                }
            }

            return true;
        }

        private static int Base64Map(char c)
        {
            if (c is >= 'A' and <= 'Z')
            {
                return c - 'A';
            }
            if (c is >= 'a' and <= 'z')
            {
                return c - 'a' + 26;
            }
            if (c is >= '0' and <= '9')
            {
                return c - '0' + 52;
            }
            if (c == '+')
            {
                return 62;
            }
            if (c == '/')
            {
                return 63;
            }
            return -1;
        }

        private static string DecodeBuffer(Span<byte> buffer, string input)
        {
            string s = input;
            int len = s.Length;
            int outputLen = buffer.Length;
            int k = 0;
            for (int i = 0; i < len; i += 4)
            {
                int v0 = Base64Map(s[i]);
                int v1 = Base64Map(s[i + 1]);
                char c2 = s[i + 2];
                char c3 = s[i + 3];
                int v2 = c2 == '=' ? 0 : Base64Map(c2);
                int v3 = c3 == '=' ? 0 : Base64Map(c3);

                if (v0 < 0 || v1 < 0 || (c2 != '=' && v2 < 0) || (c3 != '=' && v3 < 0))
                {
                    return string.Empty;
                }

                if (k < outputLen)
                {
                    buffer[k++] = (byte)((v0 << 2) | (v1 >> 4));
                }
                if (k < outputLen)
                {
                    buffer[k++] = (byte)((v1 << 4) | (v2 >> 2));
                }
                if (k < outputLen)
                {
                    buffer[k++] = (byte)((v2 << 6) | v3);
                }
            }

            try
            {
                return StrictUtf8.GetString(buffer);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        [Test]
        public void BorrowedDecoderMatchesShippedDecoderAcrossPoolBoundariesAndUtf8Failures()
        {
            VerifyCanonicalControls();
            Assert.That(Corpus.Length, Is.EqualTo(CorrectnessRowCount + CampaignRowCount));
            int verifiedRows = 0;
            int declaredCampaignRows = 0;
            foreach (CorpusCase row in Corpus)
            {
                if (row.Section == CorrectnessSection)
                {
                    Assert.AreEqual(row.Expected, row.Input.FromBase64(), row.Id);
                    Assert.AreEqual(row.Expected, DecodeBorrowed(row.Input), row.Id);
                    ++verifiedRows;
                }
                else if (row.Section == CampaignSection)
                {
                    ++declaredCampaignRows;
                }
                else
                {
                    Assert.Fail("Unknown corpus section.");
                }
            }
            Assert.That(verifiedRows, Is.EqualTo(CorrectnessRowCount));
            Assert.That(declaredCampaignRows, Is.EqualTo(CampaignRowCount));
            UnityEngine.Debug.Log(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {{\"SchemaVersion\":1,\"CorpusSha256\":\"{1}\",\"VerifiedRows\":{2},\"DecoderAssertions\":{3},\"DeclaredCampaignRows\":{4}}}",
                    CorpusMarker,
                    GetCanonicalCorpusSha256(),
                    verifiedRows,
                    verifiedRows * 2,
                    declaredCampaignRows
                )
            );
        }

        [Test]
        public void BorrowedWholeCallAvoidsLeaseWorkWithShippedPositiveControl()
        {
            DisposalLease probe = DisposalLeases.Acquire();
            try
            {
                int slot = probe._slot;
                Assert.IsTrue(probe.TryClaim());
                long before = DisposalLeases.CurrentGeneration(slot);
                Assert.AreEqual("abc", "YWJj".FromBase64());
                Assert.AreEqual(before + 2, DisposalLeases.CurrentGeneration(slot));
                long afterControl = DisposalLeases.CurrentGeneration(slot);
                Assert.AreEqual("abc", DecodeBorrowed("YWJj"));
                Assert.AreEqual(afterControl, DisposalLeases.CurrentGeneration(slot));
            }
            finally
            {
                probe.TryClaim();
            }
        }

        /// <summary>Compares complete shipped and experimental borrowed Base64 calls.</summary>
        public void CompareWholeCall(string shape, int length)
        {
            Assert.IsTrue(
                TryGetCampaignCase(shape, length, out CorpusCase selected),
                "The campaign case must be declared before timing."
            );
            string expected = selected.Expected;
            string input = selected.Input;
            Action verify = () =>
            {
                Assert.AreEqual(expected, input.FromBase64(), "Shipped decoder oracle");
                Assert.AreEqual(expected, DecodeBorrowed(input), "Borrowed decoder parity");
                Assert.AreEqual(
                    Run(input, false, 1),
                    Run(input, true, 1),
                    "Observable work parity"
                );
            };
            CalibratedBenchmarkMeasurement samples = BenchmarkProtocol.MeasureCalibrated(
                iterations => Run(input, false, iterations),
                iterations => Run(input, true, iterations),
                verify,
                924
            );
            if (samples == null)
            {
                Assert.Ignore("Whole-call borrowed-buffer comparison could not calibrate.");
                return;
            }
            UnityEngine.Debug.Log($"BORROWED_BASE64 {shape} {length} {samples.ToJson()}");
            if (samples.SlotDiagnostics != null)
            {
                Assert.Ignore("Diagnostic timings cannot establish candidate adoption.");
                return;
            }
            if (
                !samples.HasSufficientTiming
                || !samples.Comparison.IsStable(BenchmarkProtocol.DefaultSpreadLimit)
            )
            {
                Assert.Ignore(
                    "Whole-call borrowed-buffer timings are unstable; no adoption evidence."
                );
                return;
            }
            if (!samples.HasTimingNonInferiority)
            {
                Assert.Ignore(
                    "Candidate rejected: five-percent non-inferiority is not established; production remains unchanged."
                );
            }
        }

        private readonly struct CorpusCase
        {
            public readonly byte Section;
            public readonly string Id;
            public readonly string Shape;
            public readonly int DeclaredLength;
            public readonly string Input;
            public readonly string Expected;

            public CorpusCase(
                byte section,
                string id,
                string shape,
                int declaredLength,
                string input,
                string expected
            )
            {
                Section = section;
                Id = id;
                Shape = shape;
                DeclaredLength = declaredLength;
                Input = input;
                Expected = expected;
            }
        }
    }
}
