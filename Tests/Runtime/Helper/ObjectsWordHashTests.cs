// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [Category("Fast")]
    public sealed class ObjectsWordHashTests
    {
        private static IEnumerable<TestCaseData> Scalars()
        {
            foreach (ulong seed in new[] { 0UL, 17UL, ulong.MaxValue, Objects.Fnv64OffsetBasis })
            {
                foreach (
                    long value in new[]
                    {
                        0L,
                        1L,
                        -1L,
                        long.MinValue,
                        long.MaxValue,
                        0x0123456789abcdefL,
                    }
                )
                {
                    yield return new TestCaseData(seed, value).SetName(
                        $"Hash.Int64.{seed}.{value}"
                    );
                }
            }
        }

        private static IEnumerable<TestCaseData> Int32Scalars()
        {
            foreach (ulong seed in new[] { 0UL, 17UL, ulong.MaxValue, Objects.Fnv64OffsetBasis })
            {
                foreach (int value in new[] { 0, 1, -1, int.MinValue, int.MaxValue, 0x01234567 })
                {
                    yield return new TestCaseData(seed, value).SetName(
                        $"Hash.Int32.{seed}.{value}"
                    );
                }
            }
        }

        [TestCaseSource(nameof(Scalars))]
        public void Int64StepsMatchCanonicalLittleEndianBytes(ulong seed, long value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            ulong expected = Objects.StableHash64V1(bytes, seed);
            Assert.That(Objects.AppendStableHash64V1(seed, value), Is.EqualTo(expected));
        }

        [TestCaseSource(nameof(Int32Scalars))]
        public void Int32StepsMatchCanonicalLittleEndianBytes(ulong seed, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            ulong expected = Objects.StableHash64V1(bytes, seed);
            Assert.That(Objects.AppendStableHash64V1(seed, value), Is.EqualTo(expected));
        }

        [Test]
        public void MixedScalarStepsMatchOneByteSequence()
        {
            const int first = int.MinValue;
            const long second = long.MaxValue;
            byte[] bytes = { 0, 0, 0, 128, 255, 255, 255, 255, 255, 255, 255, 127 };
            ulong hash = Objects.AppendStableHash64V1(Objects.Fnv64OffsetBasis, first);
            hash = Objects.AppendStableHash64V1(hash, second);
            Assert.That(hash, Is.EqualTo(Objects.StableHash64V1(bytes)));
        }

        [TestCase("", Objects.Fnv64OffsetBasis)]
        [TestCase("a", 0x089be207b544f1e4UL)]
        [TestCase("foobar", 0xc882d5010e965f1aUL)]
        [TestCase("\u03bb", 0x09cdbd07b648cc9bUL)]
        [TestCase("\ud83d\ude42", 0xf47a500fb71281b8UL)]
        public void Utf16CodeUnitsMatchPinnedLittleEndianVectors(string text, ulong expected)
        {
            Assert.That(
                Objects.StableHashUtf16Le64V1(text.AsSpan(), Objects.Fnv64OffsetBasis),
                Is.EqualTo(expected)
            );
        }

        [TestCase(0xd800, 0x0831b007b4ea00e5UL)]
        [TestCase(0xdbff, 0x0a99ed07b6f6848fUL)]
        [TestCase(0xdc00, 0x0831ac07b4e9fa19UL)]
        [TestCase(0xdfff, 0x0a99e907b6f67dc3UL)]
        public void RawSurrogateCodeUnitsAreHashedWithoutReplacement(int codeUnit, ulong expected)
        {
            char[] raw = { (char)codeUnit };
            Assert.That((int)raw[0], Is.EqualTo(codeUnit));
            Assert.That(Objects.StableHashUtf16Le64V1(raw), Is.EqualTo(expected));
        }

        [TestCase(0UL)]
        [TestCase(17UL)]
        [TestCase(ulong.MaxValue)]
        [TestCase(Objects.Fnv64OffsetBasis)]
        public void Utf16ChunksMatchRawEncodingAndEverySplit(ulong seed)
        {
            const string text = "a\u03bb\0\ud83d\ude42z";
            byte[] encoded = Encoding.Unicode.GetBytes(text);
            ulong expected = Objects.StableHash64V1(encoded, seed);
            Assert.That(Objects.StableHashUtf16Le64V1(text.AsSpan(), seed), Is.EqualTo(expected));
            for (int split = 0; split <= text.Length; ++split)
            {
                ulong first = Objects.StableHashUtf16Le64V1(text.AsSpan(0, split), seed);
                ulong folded = Objects.StableHashUtf16Le64V1(text.AsSpan(split), first);
                Assert.That(folded, Is.EqualTo(expected));
            }
            Assert.That(
                Objects.StableHashUtf16Le64V1(ReadOnlySpan<char>.Empty, seed),
                Is.EqualTo(seed)
            );
            Assert.That(
                Objects.StableHashUtf16Le64V1(((string)null).AsSpan(), seed),
                Is.EqualTo(seed)
            );
        }

        [Test]
        public void WordFoldsMatchPinnedBitPatternsAndSignedValues()
        {
            ulong[] unsigned = { 0, 1, ulong.MaxValue, 1UL << 63 };
            long[] signed = { 0, 1, -1, long.MinValue };
            const ulong expected = 0x3b809e806717078dUL;
            Assert.That(Objects.Fnv1aWordFold64(unsigned), Is.EqualTo(expected));
            Assert.That(Objects.Fnv1aWordFold64(signed), Is.EqualTo(expected));
            byte[] littleEndian = new byte[32];
            littleEndian[8] = 1;
            for (int index = 16; index < 24; ++index)
            {
                littleEndian[index] = 255;
            }
            littleEndian[31] = 128;
            Assert.That(Objects.StableHash64V1(littleEndian), Is.Not.EqualTo(expected));
        }

        [TestCase(0UL)]
        [TestCase(17UL)]
        [TestCase(ulong.MaxValue)]
        [TestCase(Objects.Fnv64OffsetBasis)]
        public void WordFoldsPreserveEmptySeedsSlicesAndEverySplit(ulong seed)
        {
            ulong[] unsigned = { 99, 0, 1, ulong.MaxValue, 1UL << 63, 99 };
            long[] signed = { 99, 0, 1, -1, long.MinValue, 99 };
            ReadOnlySpan<ulong> words = unsigned.AsSpan(1, 4);
            ReadOnlySpan<long> signedWords = signed.AsSpan(1, 4);
            ulong expected = Objects.Fnv1aWordFold64(words, seed);
            Assert.That(Objects.Fnv1aWordFold64(signedWords, seed), Is.EqualTo(expected));
            for (int split = 0; split <= words.Length; ++split)
            {
                ulong first = Objects.Fnv1aWordFold64(words.Slice(0, split), seed);
                Assert.That(
                    Objects.Fnv1aWordFold64(words.Slice(split), first),
                    Is.EqualTo(expected)
                );
                ulong signedFirst = Objects.Fnv1aWordFold64(signedWords.Slice(0, split), seed);
                Assert.That(
                    Objects.Fnv1aWordFold64(signedWords.Slice(split), signedFirst),
                    Is.EqualTo(expected)
                );
            }
            Assert.That(Objects.Fnv1aWordFold64(ReadOnlySpan<ulong>.Empty, seed), Is.EqualTo(seed));
            Assert.That(Objects.Fnv1aWordFold64(ReadOnlySpan<long>.Empty, seed), Is.EqualTo(seed));
        }

        [Test]
        public void WordFoldHighBitDifferencesNeverReachLowerBits()
        {
            ulong[] first = { 0, 17, 23 };
            ulong[] second = { 1UL << 40, 17, 23 };
            ulong firstHash = Objects.Fnv1aWordFold64(first);
            ulong secondHash = Objects.Fnv1aWordFold64(second);
            const ulong lowBits = (1UL << 40) - 1;
            Assert.That(firstHash, Is.Not.EqualTo(secondHash));
            Assert.That(firstHash & lowBits, Is.EqualTo(secondHash & lowBits));
        }

        [Test]
        public void ExistingDefaultByteSpanCallsRemainUnambiguous()
        {
            Assert.That(Objects.StableHash64V1(default), Is.EqualTo(Objects.Fnv64OffsetBasis));
            Assert.That(Objects.StableHash64V1(default, 17UL), Is.EqualTo(17UL));
            Assert.That(Objects.StableHash64V1(default, 17), Is.EqualTo(17UL));
            Assert.That(Objects.StableHash64V1(default, default), Is.EqualTo(0UL));
        }
    }
}
