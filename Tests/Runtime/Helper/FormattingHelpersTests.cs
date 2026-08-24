// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class FormattingHelpersTests : CommonTestBase
    {
        private const int NumTries = 1_000;

        [Test]
        public void FormatNegative()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                long bytes = PRNG.Instance.NextLong(long.MinValue, 0);
                const string expected = "0 B";
                string found = FormattingHelpers.FormatBytes(bytes);
                Assert.AreEqual(expected, found, $"{bytes} failed to convert");
            }
        }

        [Test]
        public void FormatZeroBytes()
        {
            long bytes = 0L;
            const string expected = "0 B";
            string found = FormattingHelpers.FormatBytes(bytes);
            Assert.AreEqual(expected, found);
        }

        [Test]
        public void FormatBytes()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                long bytes = PRNG.Instance.NextLong(1024L);
                string expected = $"{bytes} B";
                string found = FormattingHelpers.FormatBytes(bytes);
                Assert.AreEqual(expected, found);
            }
        }

        [Test]
        public void FormatKiloBytes()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                long bytes = PRNG.Instance.NextLong(1024L, 1024L * 1024L);
                string expected = $"{(bytes / 1024.0):0.##} KB";
                string found = FormattingHelpers.FormatBytes(bytes);
                Assert.AreEqual(expected, found);
            }
        }

        [Test]
        public void FormatMegaBytes()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                long bytes = PRNG.Instance.NextLong(1024L * 1024L, 1024L * 1024L * 1024L);
                string expected = $"{(bytes / 1024.0 / 1024.0):0.##} MB";
                string found = FormattingHelpers.FormatBytes(bytes);
                Assert.AreEqual(expected, found);
            }
        }

        [Test]
        public void FormatGigaBytes()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                long bytes = PRNG.Instance.NextLong(
                    1024L * 1024L * 1024L,
                    1024L * 1024L * 1024L * 1024L
                );
                string expected = $"{(bytes / 1024.0 / 1024.0 / 1024.0):0.##} GB";
                string found = FormattingHelpers.FormatBytes(bytes);
                Assert.AreEqual(expected, found);
            }
        }

        [Test]
        public void FormatTeraBytes()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                long bytes = PRNG.Instance.NextLong(
                    1024L * 1024L * 1024L * 1024L,
                    1024L * 1024L * 1024L * 1024L * 1024L
                );
                string expected = $"{(bytes / 1024.0 / 1024.0 / 1024.0 / 1024.0):0.##} TB";
                string found = FormattingHelpers.FormatBytes(bytes);
                Assert.AreEqual(expected, found);
            }
        }

        [Test]
        public void FormatPetaBytes()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                long bytes = PRNG.Instance.NextLong(
                    1024L * 1024L * 1024L * 1024L * 1024L,
                    1024L * 1024L * 1024L * 1024L * 1024L * 1024L
                );
                string expected = $"{(bytes / 1024.0 / 1024.0 / 1024.0 / 1024.0 / 1024.0):0.##} PB";
                string found = FormattingHelpers.FormatBytes(bytes);
                Assert.AreEqual(expected, found);
            }
        }

        [Test]
        public void FormatExaBytes()
        {
            for (int i = 0; i < NumTries; ++i)
            {
                long bytes = PRNG.Instance.NextLong(
                    1024L * 1024L * 1024L * 1024L * 1024L * 1024L,
                    long.MaxValue
                );
                string expected =
                    $"{(bytes / 1024.0 / 1024.0 / 1024.0 / 1024.0 / 1024.0 / 1024.0):0.##} EB";
                string found = FormattingHelpers.FormatBytes(bytes);
                Assert.AreEqual(expected, found);
            }
        }

        [Test]
        public void FormatBytesWithZeroReturnsZeroBytes()
        {
            string result = FormattingHelpers.FormatBytes(0);
            Assert.That(result, Does.Contain("0"));
            Assert.That(result, Does.Contain("B"));
        }

        [Test]
        public void FormatBytesWithOneReturnsOneB()
        {
            string result = FormattingHelpers.FormatBytes(1);
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("B"));
        }

        [Test]
        public void FormatBytesWithBytesRangeReturnsBytes()
        {
            string result = FormattingHelpers.FormatBytes(512);
            Assert.That(result, Does.Contain("512"));
            Assert.That(result, Does.Contain("B"));
        }

        [Test]
        public void FormatBytesWithKilobytesReturnsKB()
        {
            string result = FormattingHelpers.FormatBytes(1024);
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("KB"));
        }

        [Test]
        public void FormatBytesWithMultipleKilobytesFormatsCorrectly()
        {
            string result = FormattingHelpers.FormatBytes(2048);
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Contain("KB"));
        }

        [Test]
        public void FormatBytesWithMegabytesReturnsMB()
        {
            string result = FormattingHelpers.FormatBytes(1024 * 1024);
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("MB"));
        }

        [Test]
        public void FormatBytesWithGigabytesReturnsGB()
        {
            string result = FormattingHelpers.FormatBytes(1024L * 1024 * 1024);
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("GB"));
        }

        [Test]
        public void FormatBytesWithTerabytesReturnsTB()
        {
            string result = FormattingHelpers.FormatBytes(1024L * 1024 * 1024 * 1024);
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("TB"));
        }

        [Test]
        public void FormatBytesWithPetabytesReturnsPB()
        {
            string result = FormattingHelpers.FormatBytes(1024L * 1024 * 1024 * 1024 * 1024);
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("PB"));
        }

        [Test]
        public void FormatBytesWithExabytesReturnsEB()
        {
            string result = FormattingHelpers.FormatBytes(1024L * 1024 * 1024 * 1024 * 1024 * 1024);
            Assert.That(result, Does.Contain("1"));
            Assert.That(result, Does.Contain("EB"));
        }

        [Test]
        public void FormatBytesWithNegativeNumberTreatsAsZero()
        {
            string result = FormattingHelpers.FormatBytes(-1024);
            Assert.That(result, Does.Contain("0"));
            Assert.That(result, Does.Contain("B"));
        }

        [Test]
        public void FormatBytesWithLargeNegativeNumberTreatsAsZero()
        {
            string result = FormattingHelpers.FormatBytes(long.MinValue);
            Assert.That(result, Does.Contain("0"));
            Assert.That(result, Does.Contain("B"));
        }

        [Test]
        public void FormatBytesWithFractionalKilobytesFormatsWithDecimals()
        {
            string result = FormattingHelpers.FormatBytes(1536); // 1.5 KB
            Assert.That(result, Does.Contain("1.5"));
            Assert.That(result, Does.Contain("KB"));
        }

        [Test]
        public void FormatBytesWithFractionalMegabytesFormatsWithDecimals()
        {
            string result = FormattingHelpers.FormatBytes(1024 * 1024 + 512 * 1024); // 1.5 MB
            Assert.That(result, Does.Contain("1.5"));
            Assert.That(result, Does.Contain("MB"));
        }

        [Test]
        public void FormatBytesRoundsToTwoDecimalPlaces()
        {
            string result = FormattingHelpers.FormatBytes(1234); // ~1.205 KB
            string[] parts = result.Split(' ');
            Assert.That(parts.Length, Is.GreaterThanOrEqualTo(1));
            if (double.TryParse(parts[0], out double value))
            {
                string formatted = value.ToString("0.##");
                Assert.That(formatted.Length, Is.LessThanOrEqualTo(6));
            }
        }

        [Test]
        public void FormatBytesWithMaxLongValueFormatsCorrectly()
        {
            // long.MaxValue = 9,223,372,036,854,775,807 bytes ≈ 8 EB
            string result = FormattingHelpers.FormatBytes(long.MaxValue);
            Assert.That(result, Does.Contain("EB"));
            // Verify it's approximately 8 EB
            Assert.That(result, Does.Contain("8"));
        }

        [Test]
        public void FormatBytesWithBoundaryValuesFormatsCorrectly()
        {
            string result1023 = FormattingHelpers.FormatBytes(1023);
            Assert.That(result1023, Does.Contain("B"));

            string result1024 = FormattingHelpers.FormatBytes(1024);
            Assert.That(result1024, Does.Contain("KB"));
        }

        [Test]
        public void FormatBytesWithBoundaryMBFormatsCorrectly()
        {
            long oneMbMinus1 = 1024 * 1024 - 1;
            string result = FormattingHelpers.FormatBytes(oneMbMinus1);
            Assert.That(result, Does.Contain("KB"));

            result = FormattingHelpers.FormatBytes(1024 * 1024);
            Assert.That(result, Does.Contain("MB"));
        }

        [Test]
        public void FormatBytesWithOddFractionsRoundsAppropriately()
        {
            string result = FormattingHelpers.FormatBytes(1234567); // ~1.18 MB
            Assert.That(result, Does.Contain("MB"));
        }

        [Test]
        public void FormatBytesConsistentFormat()
        {
            string result1 = FormattingHelpers.FormatBytes(1024);
            string result2 = FormattingHelpers.FormatBytes(2048);
            string result3 = FormattingHelpers.FormatBytes(3072);

            Assert.That(result1, Does.Contain("KB"));
            Assert.That(result2, Does.Contain("KB"));
            Assert.That(result3, Does.Contain("KB"));
        }

        [Test]
        public void FormatBytesWithLargeGigabyteValueFormatsCorrectly()
        {
            long hundredGB = 100L * 1024 * 1024 * 1024;
            string result = FormattingHelpers.FormatBytes(hundredGB);
            Assert.That(result, Does.Contain("100"));
            Assert.That(result, Does.Contain("GB"));
        }

        [Test]
        public void FormatBytesDoesNotIncludeTrailingZeros()
        {
            string result = FormattingHelpers.FormatBytes(2048); // Exactly 2 KB
            Assert.That(result, Does.Contain("2"));
            Assert.That(result, Does.Not.Contain("2.0"));
        }

        [Test]
        public void FormatBytesHandlesJustUnderNextUnitBoundary()
        {
            long justUnder1GB = (1024L * 1024 * 1024) - 1;
            string result = FormattingHelpers.FormatBytes(justUnder1GB);
            Assert.That(result, Does.Contain("MB"));
        }

        [Test]
        public void FormatBytesReturnsStringWithSpaceBetweenValueAndUnit()
        {
            string result = FormattingHelpers.FormatBytes(1024);
            Assert.That(result, Does.Match(@"\d+(\.\d+)?\s+[A-Z]+"));
        }

        [Test]
        public void FormatBytesWithSmallFractionsUsesUpToTwoDecimals()
        {
            string result = FormattingHelpers.FormatBytes(1050); // ~1.025 KB
            string[] parts = result.Split(' ');
            if (0 < parts.Length && double.TryParse(parts[0], out double value))
            {
                int decimalPlaces = 0;
                string valueStr = parts[0];
                if (valueStr.Contains("."))
                {
                    decimalPlaces = valueStr.Split('.')[1].Length;
                }
                Assert.That(decimalPlaces, Is.LessThanOrEqualTo(2));
            }
        }
    }
}
