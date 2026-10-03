// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    public sealed class BorrowedBase64Tests
    {
        private const int CallsPerIteration = 16;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly BufferFunc<byte, string, string> DecodeCallback = DecodeBuffer;

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
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(expected));
                Assert.AreEqual(expected, encoded.FromBase64());
                Assert.AreEqual(expected, DecodeBorrowed(encoded));
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
                string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(expected));
                Assert.AreEqual(expected, encoded.FromBase64());
                Assert.AreEqual(expected, DecodeBorrowed(encoded));
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
                string encoded = Convert.ToBase64String(invalid);
                Assert.AreEqual(string.Empty, encoded.FromBase64());
                Assert.AreEqual(string.Empty, DecodeBorrowed(encoded));
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
                Assert.AreEqual(string.Empty, malformed.FromBase64());
                Assert.AreEqual(string.Empty, DecodeBorrowed(malformed));
            }
        }

        [Test]
        public void BorrowedWholeCallAvoidsLeaseWorkWithShippedPositiveControl()
        {
            DisposalLease probe = DisposalLeases.Acquire();
            try
            {
                int slot = probe.SlotForTests;
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
            string expected;
            string input;
            switch (shape)
            {
                case "ascii":
                    expected = new string('a', length);
                    input = Convert.ToBase64String(Encoding.UTF8.GetBytes(expected));
                    break;
                case "multibyte":
                    expected = new string('\u754c', length) + "\ud83c\udf0d";
                    input = Convert.ToBase64String(Encoding.UTF8.GetBytes(expected));
                    break;
                case "invalidUtf8":
                    expected = string.Empty;
                    byte[] invalid = new byte[length];
                    Array.Fill(invalid, (byte)0xff);
                    input = Convert.ToBase64String(invalid);
                    break;
                case "invalidPadding":
                    expected = string.Empty;
                    input = "AAAA====";
                    break;
                case "invalidAlphabet":
                    expected = string.Empty;
                    input = "AA!A";
                    break;
                case "null":
                    expected = string.Empty;
                    input = null;
                    break;
                default:
                    expected = string.Empty;
                    input = string.Empty;
                    break;
            }
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
    }
}
