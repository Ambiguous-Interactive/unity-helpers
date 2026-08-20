// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Globalization;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializedStringComparerTests
    {
        private CultureInfo _originalCulture;

        [SetUp]
        public void SetUp()
        {
            _originalCulture = CultureInfo.CurrentCulture;
        }

        [TearDown]
        public void TearDown()
        {
            CultureInfo.CurrentCulture = _originalCulture;
        }

        [Test]
        public void EqualsOrdinalModeIsCaseSensitive()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.Ordinal
            );

            Assert.IsTrue(comparer.Equals("Alpha", "Alpha"));
            Assert.IsFalse(comparer.Equals("Alpha", "alpha"));
        }

        [Test]
        public void EqualsOrdinalIgnoreCaseModeIsCaseInsensitive()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase
            );

            Assert.IsTrue(comparer.Equals("Alpha", "alpha"));
        }

        [Test]
        public void EqualsCurrentCultureRespectsCultureSettings()
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.CurrentCultureIgnoreCase
            );

            // In Turkish culture, 'i' uppercases to 'İ' (dotted I) and 'I' lowercases to 'ı' (dotless i)
            // So "file" and "FILE" are NOT equal in Turkish culture
            Assert.IsFalse(comparer.Equals("file", "FILE"));

            // But they should be equal with the same casing behavior
            Assert.IsTrue(comparer.Equals("test", "TEST"));
        }

        [Test]
        public void EqualsInvariantCultureMatchesInvariantRules()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.InvariantCultureIgnoreCase
            );
            Assert.IsTrue(comparer.Equals("Invariant", "INVARIANT"));
        }

        [Test]
        public void GetHashCodeMatchesEqualsSemantics()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase
            );

            int hashA = comparer.GetHashCode("Value");
            int hashB = comparer.GetHashCode("VALUE");

            Assert.AreEqual(hashA, hashB);
        }

        [Test]
        public void InvalidModeFallsBackToOrdinal()
        {
            SerializedStringComparer comparer = new(
                (SerializedStringComparer.StringCompareMode)999
            );
            SerializedStringComparer ordinal = new(
                SerializedStringComparer.StringCompareMode.Ordinal
            );

            Assert.IsTrue(comparer.Equals("a", "a"));
            Assert.IsFalse(comparer.Equals("a", "A"));
            Assert.IsFalse(comparer.Equals("a", "b"));
            Assert.AreEqual(ordinal.GetHashCode("a"), comparer.GetHashCode("a"));
        }

        [Test]
        public void NullKeysAreHandledInEveryMode()
        {
            foreach (SerializedStringComparer.StringCompareMode mode in AllModes())
            {
                SerializedStringComparer comparer = new(mode);

                Assert.AreEqual(0, comparer.GetHashCode(null), $"{mode} should hash null to zero");
                Assert.IsTrue(comparer.Equals(null, null), $"{mode} should equate two nulls");
                Assert.IsFalse(
                    comparer.Equals(null, "a"),
                    $"{mode} should separate null from text"
                );
            }
        }

        [Test]
        public void OrdinalModeCaseSensitive()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.Ordinal
            );

            Assert.IsTrue(comparer.Equals("Hello", "Hello"));
            Assert.IsFalse(comparer.Equals("Hello", "hello"));
            Assert.IsFalse(comparer.Equals("HELLO", "hello"));
        }

        [Test]
        public void OrdinalModeNullHandling()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.Ordinal
            );

            Assert.IsTrue(comparer.Equals(null, null));
            Assert.IsFalse(comparer.Equals("text", null));
            Assert.IsFalse(comparer.Equals(null, "text"));
        }

        [Test]
        public void OrdinalModeHashCodeConsistency()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.Ordinal
            );

            string str1 = "Test";
            string str2 = "Test";
            Assert.AreEqual(comparer.GetHashCode(str1), comparer.GetHashCode(str2));
            Assert.AreNotEqual(comparer.GetHashCode("Test"), comparer.GetHashCode("test"));
        }

        [Test]
        public void OrdinalIgnoreCaseModeCaseInsensitive()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase
            );

            Assert.IsTrue(comparer.Equals("Hello", "hello"));
            Assert.IsTrue(comparer.Equals("HELLO", "hello"));
            Assert.IsTrue(comparer.Equals("HeLLo", "hEllO"));
        }

        [Test]
        public void OrdinalIgnoreCaseModeSpecialCharacters()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase
            );

            Assert.IsTrue(comparer.Equals("Test123", "test123"));
            Assert.IsTrue(comparer.Equals("Test-Value", "test-value"));
            Assert.IsFalse(comparer.Equals("Test", "Test "));
        }

        [Test]
        public void OrdinalIgnoreCaseModeHashCodeConsistency()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.OrdinalIgnoreCase
            );

            Assert.AreEqual(comparer.GetHashCode("Test"), comparer.GetHashCode("test"));
            Assert.AreEqual(comparer.GetHashCode("HELLO"), comparer.GetHashCode("hello"));
        }

        [Test]
        public void CurrentCultureModeCaseSensitive()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.CurrentCulture
            );

            Assert.IsTrue(comparer.Equals("Hello", "Hello"));
            Assert.IsFalse(comparer.Equals("Hello", "hello"));
        }

        [Test]
        public void CurrentCultureModeHashCodeConsistency()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.CurrentCulture
            );

            string str1 = "Culture";
            string str2 = "Culture";
            Assert.AreEqual(comparer.GetHashCode(str1), comparer.GetHashCode(str2));
        }

        [Test]
        public void CurrentCultureIgnoreCaseModeCaseInsensitive()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.CurrentCultureIgnoreCase
            );

            Assert.IsTrue(comparer.Equals("Hello", "hello"));
            Assert.IsTrue(comparer.Equals("WORLD", "world"));
        }

        [Test]
        public void CurrentCultureIgnoreCaseModeHashCodeConsistency()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.CurrentCultureIgnoreCase
            );

            int hash1 = comparer.GetHashCode("Test");
            int hash2 = comparer.GetHashCode("test");
            Assert.AreEqual(hash1, hash2);
        }

        [Test]
        public void CurrentCultureIgnoreCaseModeTurkishCultureEdgeCase()
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.CurrentCultureIgnoreCase
            );

            // In Turkish, 'i' and 'I' are not case variants of each other
            Assert.IsFalse(comparer.Equals("file", "FILE"));
            // In Turkish, 'i' (U+0069) and 'ı' (U+0131) are different letters, not case variants
            Assert.IsFalse(comparer.Equals("file", "fıle")); // dotless i
        }

        [Test]
        public void InvariantCultureModeCaseSensitive()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.InvariantCulture
            );

            Assert.IsTrue(comparer.Equals("Invariant", "Invariant"));
            Assert.IsFalse(comparer.Equals("Invariant", "invariant"));
        }

        [Test]
        public void InvariantCultureModeConsistentAcrossCultures()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.InvariantCulture
            );

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            bool result1 = comparer.Equals("Test", "Test");

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            bool result2 = comparer.Equals("Test", "Test");

            Assert.AreEqual(result1, result2);
        }

        [Test]
        public void InvariantCultureModeHashCodeConsistency()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.InvariantCulture
            );

            Assert.AreEqual(comparer.GetHashCode("Value"), comparer.GetHashCode("Value"));
        }

        [Test]
        public void InvariantCultureIgnoreCaseModeCaseInsensitive()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.InvariantCultureIgnoreCase
            );

            Assert.IsTrue(comparer.Equals("Invariant", "invariant"));
            Assert.IsTrue(comparer.Equals("INVARIANT", "invariant"));
            Assert.IsTrue(comparer.Equals("InVaRiAnT", "iNvArIaNt"));
        }

        [Test]
        public void InvariantCultureIgnoreCaseModeConsistentAcrossCultures()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.InvariantCultureIgnoreCase
            );

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            bool result1 = comparer.Equals("Test", "test");

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            bool result2 = comparer.Equals("Test", "test");

            Assert.AreEqual(result1, result2);
            Assert.IsTrue(result1);
        }

        [Test]
        public void InvariantCultureIgnoreCaseModeHashCodeConsistency()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.InvariantCultureIgnoreCase
            );

            Assert.AreEqual(comparer.GetHashCode("Test"), comparer.GetHashCode("test"));
            Assert.AreEqual(comparer.GetHashCode("VALUE"), comparer.GetHashCode("value"));
        }

        [Test]
        public void InvariantCultureIgnoreCaseModeNotAffectedByCurrentCulture()
        {
            SerializedStringComparer comparer = new(
                SerializedStringComparer.StringCompareMode.InvariantCultureIgnoreCase
            );

            // Test that Turkish culture doesn't affect invariant comparison
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            // Unlike CurrentCultureIgnoreCase, this should be true in invariant culture
            Assert.IsTrue(comparer.Equals("FILE", "file"));
        }

        [Test]
        public void AllModesEmptyStringsAreEqual()
        {
            SerializedStringComparer.StringCompareMode[] allModes = AllModes();

            foreach (SerializedStringComparer.StringCompareMode mode in allModes)
            {
                SerializedStringComparer comparer = new(mode);
                Assert.IsTrue(
                    comparer.Equals(string.Empty, string.Empty),
                    $"Empty strings should be equal in {mode} mode"
                );
            }
        }

        [Test]
        public void AllModesWhitespaceMatters()
        {
            SerializedStringComparer.StringCompareMode[] allModes = AllModes();

            foreach (SerializedStringComparer.StringCompareMode mode in allModes)
            {
                SerializedStringComparer comparer = new(mode);
                Assert.IsFalse(
                    comparer.Equals("Test", "Test "),
                    $"Trailing whitespace should matter in {mode} mode"
                );
                Assert.IsFalse(
                    comparer.Equals("Test", " Test"),
                    $"Leading whitespace should matter in {mode} mode"
                );
            }
        }

        private static SerializedStringComparer.StringCompareMode[] AllModes()
        {
            return Enum.GetValues(typeof(SerializedStringComparer.StringCompareMode))
                .OfType<SerializedStringComparer.StringCompareMode>()
                .ToArray();
        }
    }
}
