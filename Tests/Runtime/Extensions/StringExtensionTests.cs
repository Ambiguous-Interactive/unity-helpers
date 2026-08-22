// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class StringExtensionTests : CommonTestBase
    {
        private static IEnumerable<TestCaseData> LevenshteinDistanceTestCases()
        {
            yield return new TestCaseData("test", "test", 0).SetName(
                "Levenshtein.Identical.ReturnsZero"
            );
            yield return new TestCaseData("", "", 0).SetName("Levenshtein.BothEmpty.ReturnsZero");
            yield return new TestCaseData("hello world", "hello world", 0).SetName(
                "Levenshtein.LongIdentical.ReturnsZero"
            );
            yield return new TestCaseData("A", "A", 0).SetName(
                "Levenshtein.SingleCharIdentical.ReturnsZero"
            );
            yield return new TestCaseData("test", "", 4).SetName(
                "Levenshtein.SecondEmpty.ReturnsFirstLength"
            );
            yield return new TestCaseData("", "hello", 5).SetName(
                "Levenshtein.FirstEmpty.ReturnsSecondLength"
            );
            yield return new TestCaseData("test", null, 4).SetName(
                "Levenshtein.SecondNull.ReturnsFirstLength"
            );
            yield return new TestCaseData(null, "hello", 5).SetName(
                "Levenshtein.FirstNull.ReturnsSecondLength"
            );
            yield return new TestCaseData(null, null, 0).SetName(
                "Levenshtein.BothNull.ReturnsZero"
            );
            yield return new TestCaseData("test", "text", 1).SetName(
                "Levenshtein.SingleCharDifference.ReturnsOne"
            );
            yield return new TestCaseData("cat", "bat", 1).SetName(
                "Levenshtein.SingleSubstitution.ReturnsOne"
            );
            yield return new TestCaseData("hello", "hallo", 1).SetName(
                "Levenshtein.SingleSubstitutionMiddle.ReturnsOne"
            );
            yield return new TestCaseData("cat", "cats", 1).SetName(
                "Levenshtein.SingleInsertion.ReturnsOne"
            );
            yield return new TestCaseData("cat", "catch", 2).SetName(
                "Levenshtein.TwoInsertions.ReturnsTwo"
            );
            yield return new TestCaseData("foo", "foobar", 3).SetName(
                "Levenshtein.ThreeInsertions.ReturnsThree"
            );
            yield return new TestCaseData("cats", "cat", 1).SetName(
                "Levenshtein.SingleDeletion.ReturnsOne"
            );
            yield return new TestCaseData("catch", "cat", 2).SetName(
                "Levenshtein.TwoDeletions.ReturnsTwo"
            );
            yield return new TestCaseData("foobar", "foo", 3).SetName(
                "Levenshtein.ThreeDeletions.ReturnsThree"
            );
            yield return new TestCaseData("abc", "xyz", 3).SetName(
                "Levenshtein.CompletelyDifferent.ReturnsLength"
            );
            yield return new TestCaseData("hello", "world", 4).SetName(
                "Levenshtein.DifferentWords.ReturnsFour"
            );
            yield return new TestCaseData("kitten", "sitting", 3).SetName(
                "Levenshtein.ClassicKitten.ReturnsThree"
            );
            yield return new TestCaseData("saturday", "sunday", 3).SetName(
                "Levenshtein.ClassicSaturday.ReturnsThree"
            );
            yield return new TestCaseData("book", "back", 2).SetName(
                "Levenshtein.ClassicBook.ReturnsTwo"
            );
            yield return new TestCaseData("Test", "test", 1).SetName(
                "Levenshtein.CaseDifferenceOne.ReturnsOne"
            );
            yield return new TestCaseData("TEST", "test", 4).SetName(
                "Levenshtein.CaseDifferenceAll.ReturnsFour"
            );
            yield return new TestCaseData("intention", "execution", 5).SetName(
                "Levenshtein.LongWords.ReturnsFive"
            );
            yield return new TestCaseData("algorithm", "altruistic", 6).SetName(
                "Levenshtein.ComplexWords.ReturnsSix"
            );
        }

        [TestCaseSource(nameof(LevenshteinDistanceTestCases))]
        public void LevenshteinDistanceReturnsExpected(string first, string second, int expected)
        {
            int actual = first.LevenshteinDistance(second);

            Assert.AreEqual(expected, actual);
        }

        private static IEnumerable<TestCaseData> NeedsLowerInvariantConversionTestCases()
        {
            yield return new TestCaseData(null, false).SetName("NeedsLower.Null.ReturnsFalse");
            yield return new TestCaseData("", false).SetName("NeedsLower.Empty.ReturnsFalse");
            yield return new TestCaseData("   ", false).SetName(
                "NeedsLower.Whitespace.ReturnsFalse"
            );
            yield return new TestCaseData("\t\n\r", false).SetName(
                "NeedsLower.TabNewline.ReturnsFalse"
            );
            yield return new TestCaseData("Test", true).SetName(
                "NeedsLower.LeadingUpper.ReturnsTrue"
            );
            yield return new TestCaseData("TEST", true).SetName("NeedsLower.AllUpper.ReturnsTrue");
            yield return new TestCaseData("Hello World", true).SetName(
                "NeedsLower.MixedWithSpace.ReturnsTrue"
            );
            yield return new TestCaseData("A", true).SetName(
                "NeedsLower.SingleUpperChar.ReturnsTrue"
            );
            yield return new TestCaseData("test", false).SetName(
                "NeedsLower.AllLower.ReturnsFalse"
            );
            yield return new TestCaseData("hello world", false).SetName(
                "NeedsLower.AllLowerWithSpace.ReturnsFalse"
            );
            yield return new TestCaseData("a", false).SetName(
                "NeedsLower.SingleLowerChar.ReturnsFalse"
            );
            yield return new TestCaseData("lowercase123", false).SetName(
                "NeedsLower.LowerWithNumbers.ReturnsFalse"
            );
            yield return new TestCaseData("helloWorld", true).SetName(
                "NeedsLower.CamelCase.ReturnsTrue"
            );
            yield return new TestCaseData("test123Test", true).SetName(
                "NeedsLower.NumbersThenUpper.ReturnsTrue"
            );
            yield return new TestCaseData("hello123", false).SetName(
                "NeedsLower.LowerThenNumbers.ReturnsFalse"
            );
        }

        [TestCaseSource(nameof(NeedsLowerInvariantConversionTestCases))]
        public void NeedsLowerInvariantConversionReturnsExpected(string input, bool expected)
        {
            bool actual = input.NeedsLowerInvariantConversion();

            Assert.AreEqual(expected, actual);
        }

        private static IEnumerable<TestCaseData> NeedsTrimTestCases()
        {
            yield return new TestCaseData(null, false).SetName("NeedsTrim.Null.ReturnsFalse");
            yield return new TestCaseData("", false).SetName("NeedsTrim.Empty.ReturnsFalse");
            yield return new TestCaseData(" test", true).SetName(
                "NeedsTrim.LeadingSpace.ReturnsTrue"
            );
            yield return new TestCaseData("  test", true).SetName(
                "NeedsTrim.TwoLeadingSpaces.ReturnsTrue"
            );
            yield return new TestCaseData("\ttest", true).SetName(
                "NeedsTrim.LeadingTab.ReturnsTrue"
            );
            yield return new TestCaseData("\ntest", true).SetName(
                "NeedsTrim.LeadingNewline.ReturnsTrue"
            );
            yield return new TestCaseData("test ", true).SetName(
                "NeedsTrim.TrailingSpace.ReturnsTrue"
            );
            yield return new TestCaseData("test  ", true).SetName(
                "NeedsTrim.TwoTrailingSpaces.ReturnsTrue"
            );
            yield return new TestCaseData("test\t", true).SetName(
                "NeedsTrim.TrailingTab.ReturnsTrue"
            );
            yield return new TestCaseData("test\n", true).SetName(
                "NeedsTrim.TrailingNewline.ReturnsTrue"
            );
            yield return new TestCaseData(" test ", true).SetName("NeedsTrim.BothEnds.ReturnsTrue");
            yield return new TestCaseData("  test  ", true).SetName(
                "NeedsTrim.MultipleSpacesBothEnds.ReturnsTrue"
            );
            yield return new TestCaseData("\ttest\n", true).SetName(
                "NeedsTrim.MixedWhitespaceBothEnds.ReturnsTrue"
            );
            yield return new TestCaseData("test", false).SetName(
                "NeedsTrim.NoWhitespace.ReturnsFalse"
            );
            yield return new TestCaseData("hello world", false).SetName(
                "NeedsTrim.MiddleWhitespaceOnly.ReturnsFalse"
            );
            yield return new TestCaseData("a", false).SetName("NeedsTrim.SingleChar.ReturnsFalse");
            yield return new TestCaseData("no trim needed", false).SetName(
                "NeedsTrim.NoTrimNeeded.ReturnsFalse"
            );
            yield return new TestCaseData("test\tvalue", false).SetName(
                "NeedsTrim.MiddleTab.ReturnsFalse"
            );
        }

        [TestCaseSource(nameof(NeedsTrimTestCases))]
        public void NeedsTrimReturnsExpected(string input, bool expected)
        {
            bool actual = input.NeedsTrim();

            Assert.AreEqual(expected, actual);
        }

        private static IEnumerable<TestCaseData> TruncateTestCases()
        {
            yield return new TestCaseData(null, 5, "...", null).SetName(
                "Truncate.Null.ReturnsNull"
            );
            yield return new TestCaseData("", 5, "...", "").SetName("Truncate.Empty.ReturnsEmpty");
            yield return new TestCaseData("test", -1, "...", "test").SetName(
                "Truncate.NegativeLength.ReturnsOriginal"
            );
            yield return new TestCaseData("test", 10, "...", "test").SetName(
                "Truncate.LongerThanInput.ReturnsOriginal"
            );
            yield return new TestCaseData("test", 4, "...", "test").SetName(
                "Truncate.ExactLength.ReturnsOriginal"
            );
            yield return new TestCaseData("hello world", 6, "...", "hel...").SetName(
                "Truncate.DefaultEllipsis.TruncatesCorrectly"
            );
            yield return new TestCaseData("hello", 3, "...", "...").SetName(
                "Truncate.VeryShortLimit.ReturnsEllipsisOnly"
            );
            yield return new TestCaseData("hello world", 10, "...", "hello w...").SetName(
                "Truncate.TenChars.TruncatesCorrectly"
            );
            yield return new TestCaseData("hello world", 5, "--", "hel--").SetName(
                "Truncate.CustomEllipsis.TruncatesCorrectly"
            );
            yield return new TestCaseData("hello world", 12, " [more]", "hello world").SetName(
                "Truncate.LongerThanWithCustom.ReturnsOriginal"
            );
            yield return new TestCaseData("hello world", 5, "", "hello").SetName(
                "Truncate.EmptyEllipsis.TruncatesExact"
            );
            yield return new TestCaseData("hello", 3, "", "hel").SetName(
                "Truncate.EmptyEllipsisShort.TruncatesExact"
            );
            yield return new TestCaseData("hello", 2, "...", "he").SetName(
                "Truncate.EllipsisLongerThanMax.DropsEllipsisToRespectLimit"
            );
            yield return new TestCaseData("hello", 0, "...", "").SetName(
                "Truncate.ZeroLimit.ReturnsEmpty"
            );
            yield return new TestCaseData("testing", 6, "...", "tes...").SetName(
                "Truncate.ExactAtLimit.TruncatesCorrectly"
            );
            yield return new TestCaseData("testing", 5, "...", "te...").SetName(
                "Truncate.OneUnderLimit.TruncatesCorrectly"
            );
        }

        [TestCaseSource(nameof(TruncateTestCases))]
        public void TruncateReturnsExpected(
            string input,
            int maxLength,
            string ellipsis,
            string expected
        )
        {
            string actual = input.Truncate(maxLength, ellipsis);

            Assert.AreEqual(expected, actual);
        }

        private static IEnumerable<TestCaseData> CenterTestCases()
        {
            yield return new TestCaseData(null, 10, null).SetName("Center.Null.ReturnsNull");
            yield return new TestCaseData("test", 4, "test").SetName(
                "Center.ExactWidth.ReturnsOriginal"
            );
            yield return new TestCaseData("test", 3, "test").SetName(
                "Center.ShorterWidth.ReturnsOriginal"
            );
            yield return new TestCaseData("test", 0, "test").SetName(
                "Center.ZeroWidth.ReturnsOriginal"
            );
            yield return new TestCaseData("test", -1, "test").SetName(
                "Center.NegativeWidth.ReturnsOriginal"
            );
            yield return new TestCaseData("test", 8, "  test  ").SetName(
                "Center.EvenPadding.PadsEvenly"
            );
            yield return new TestCaseData("test", 7, " test  ").SetName(
                "Center.OddPaddingLeft.PadsCorrectly"
            );
            yield return new TestCaseData("test", 9, "  test   ").SetName(
                "Center.OddPaddingRight.PadsCorrectly"
            );
            yield return new TestCaseData("a", 11, "     a     ").SetName(
                "Center.SingleChar.PadsEvenly"
            );
            yield return new TestCaseData("", 0, "").SetName("Center.EmptyZeroWidth.ReturnsEmpty");
            yield return new TestCaseData("", 5, "     ").SetName(
                "Center.EmptyWithWidth.ReturnsPadding"
            );
        }

        [TestCaseSource(nameof(CenterTestCases))]
        public void CenterReturnsExpected(string input, int width, string expected)
        {
            string actual = input.Center(width);

            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ToPascalCaseNominal()
        {
            Assert.AreEqual("PascalCase", "PascalCase".ToPascalCase());
            Assert.AreEqual("PascalCase", "pascalCase".ToPascalCase());
            Assert.AreEqual("PascalCase", "_pascalCase".ToPascalCase());
            Assert.AreEqual("PascalCase", "_PascalCase".ToPascalCase());
            Assert.AreEqual("PascalCase", "pascal case".ToPascalCase());
            Assert.AreEqual("PascalCase", "  __Pascal____   ___Case__ ".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseCustomSeparator()
        {
            const string separator = ".";
            Assert.AreEqual("Pascal.Case", "PascalCase".ToPascalCase(separator));
            Assert.AreEqual("Pascal.Case", "pascalCase".ToPascalCase(separator));
            Assert.AreEqual("Pascal.Case", "_pascalCase".ToPascalCase(separator));
            Assert.AreEqual("Pascal.Case", "_PascalCase".ToPascalCase(separator));
            Assert.AreEqual("Pascal.Case", "pascal case".ToPascalCase(separator));
            Assert.AreEqual("Pascal.Case", "  __Pascal____   ___Case__ ".ToPascalCase(separator));
        }

        [Test]
        public void ToPascalCaseEdgeCases()
        {
            Assert.AreEqual(string.Empty, string.Empty.ToPascalCase());
            Assert.AreEqual(string.Empty, ((string)null).ToPascalCase());
            Assert.AreEqual("A", "a".ToPascalCase());
            Assert.AreEqual("A", "A".ToPascalCase());
            Assert.AreEqual(string.Empty, "___".ToPascalCase());
            Assert.AreEqual("Abc", "ABC".ToPascalCase());
        }

        [Test]
        public void GetBytesHandlesNullAndEmpty()
        {
            byte[] nullResult = ((string)null).GetBytes();
            Assert.IsTrue(nullResult != null);
            Assert.AreEqual(0, nullResult.Length);

            byte[] emptyResult = string.Empty.GetBytes();
            Assert.IsTrue(emptyResult != null);
            Assert.AreEqual(0, emptyResult.Length);
        }

        [Test]
        public void GetBytesConvertsString()
        {
            byte[] result = "test".GetBytes();
            Assert.IsTrue(result != null);
            Assert.Greater(result.Length, 0);

            // Verify round-trip
            string converted = result.GetString();
            Assert.AreEqual("test", converted);
        }

        [Test]
        public void GetStringConvertsBytes()
        {
            byte[] bytes = { 116, 101, 115, 116 }; // "test" in ASCII
            string result = bytes.GetString();
            Assert.IsTrue(result != null);
            Assert.AreEqual("test", result);
        }

        [Test]
        public void GetBytesAndGetStringRoundTrip()
        {
            string original = "Hello, World! 123 @#$";
            byte[] bytes = original.GetBytes();
            string result = bytes.GetString();
            Assert.AreEqual(original, result);
        }

        [Test]
        public void ToJsonSerializesValue()
        {
            int intValue = 42;
            string json = intValue.ToJson();
            Assert.IsTrue(json != null);
            Assert.IsTrue(json.Contains("42"));

            string stringValue = "test";
            string stringJson = stringValue.ToJson();
            Assert.IsTrue(stringJson != null);
            Assert.IsTrue(stringJson.Contains("test"));
        }

        [Test]
        public void ToCamelCaseFromPascalCase()
        {
            Assert.AreEqual("pascalCase", "PascalCase".ToCamelCase());
            Assert.AreEqual("helloWorld", "HelloWorld".ToCamelCase());
            Assert.AreEqual("a", "A".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseFromSnakeCase()
        {
            Assert.AreEqual("snakeCase", "snake_case".ToCamelCase());
            Assert.AreEqual("helloWorldTest", "hello_world_test".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseEdgeCases()
        {
            Assert.AreEqual(string.Empty, ((string)null).ToCamelCase());
            Assert.AreEqual(string.Empty, string.Empty.ToCamelCase());
            Assert.AreEqual(string.Empty, "___".ToCamelCase());
            Assert.AreEqual("abc", "ABC".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseFromKebabCase()
        {
            Assert.AreEqual("kebabCase", "kebab-case".ToCamelCase());
            Assert.AreEqual("helloWorldTest", "hello-world-test".ToCamelCase());
            Assert.AreEqual("myVariable", "my-variable".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseFromSpaces()
        {
            Assert.AreEqual("helloWorld", "hello world".ToCamelCase());
            Assert.AreEqual("testCase", "test case".ToCamelCase());
            Assert.AreEqual("multipleWords", "multiple  words".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseMultipleConsecutiveUppercase()
        {
            Assert.AreEqual("xmlHttpRequest", "XMLHttpRequest".ToCamelCase());
            Assert.AreEqual("htmlParser", "HTMLParser".ToCamelCase());
            Assert.AreEqual("ioError", "IOError".ToCamelCase());
            Assert.AreEqual("ioException", "IOException".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseWithNumbers()
        {
            Assert.AreEqual("test123", "test123".ToCamelCase());
            Assert.AreEqual("test123Test", "test_123_test".ToCamelCase());
            Assert.AreEqual("version2", "version2".ToCamelCase());
            Assert.AreEqual("version2Beta", "version_2_beta".ToCamelCase());
            Assert.AreEqual("my3DModel", "my_3D_model".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseSingleCharacter()
        {
            Assert.AreEqual("a", "a".ToCamelCase());
            Assert.AreEqual("a", "A".ToCamelCase());
            Assert.AreEqual("x", "x".ToCamelCase());
            Assert.AreEqual("z", "Z".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseSingleWord()
        {
            Assert.AreEqual("test", "test".ToCamelCase());
            Assert.AreEqual("test", "TEST".ToCamelCase());
            Assert.AreEqual("test", "Test".ToCamelCase());
            Assert.AreEqual("hello", "hello".ToCamelCase());
            Assert.AreEqual("hello", "HELLO".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseOnlySeparators()
        {
            Assert.AreEqual(string.Empty, "___".ToCamelCase());
            Assert.AreEqual(string.Empty, "---".ToCamelCase());
            Assert.AreEqual(string.Empty, "   ".ToCamelCase());
            Assert.AreEqual(string.Empty, "_-_ ".ToCamelCase());
            Assert.AreEqual(string.Empty, "...".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseLeadingAndTrailingSeparators()
        {
            Assert.AreEqual("testCase", "_test_case".ToCamelCase());
            Assert.AreEqual("testCase", "test_case_".ToCamelCase());
            Assert.AreEqual("testCase", "_test_case_".ToCamelCase());
            Assert.AreEqual("testCase", "-test-case-".ToCamelCase());
            Assert.AreEqual("testCase", "__test__case__".ToCamelCase());
            Assert.AreEqual("testCase", "  test  case  ".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseMultipleSeparators()
        {
            Assert.AreEqual("testCase", "test__case".ToCamelCase());
            Assert.AreEqual("testCase", "test--case".ToCamelCase());
            Assert.AreEqual("testCase", "test__--__case".ToCamelCase());
            Assert.AreEqual("testCase", "test   case".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseMixedSeparators()
        {
            Assert.AreEqual("thisIsATest", "this_is-a_test".ToCamelCase());
            Assert.AreEqual("helloWorldTest", "hello-world_test".ToCamelCase());
            Assert.AreEqual("mixedCaseTest", "mixed_Case-Test".ToCamelCase());
            Assert.AreEqual("testCase", "test.case".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseAllSeparators()
        {
            Assert.AreEqual("testCase", "test_case".ToCamelCase());
            Assert.AreEqual("testCase", "test-case".ToCamelCase());
            Assert.AreEqual("testCase", "test case".ToCamelCase());
            Assert.AreEqual("testCase", "test.case".ToCamelCase());
            Assert.AreEqual("testCase", "test\tcase".ToCamelCase());
            Assert.AreEqual("testCase", "test\ncase".ToCamelCase());
            Assert.AreEqual("testCase", "test\rcase".ToCamelCase());
            Assert.AreEqual("testCase", "test\"Case".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseUppercaseWords()
        {
            Assert.AreEqual("thisIsATest", "THIS-IS-A-TEST".ToCamelCase());
            Assert.AreEqual("helloWorld", "HELLO_WORLD".ToCamelCase());
            Assert.AreEqual("abc", "ABC".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseMixedCamelAndSnake()
        {
            Assert.AreEqual("helloWorldTest", "helloWorld_test".ToCamelCase());
            Assert.AreEqual("testCaseExample", "testCase_example".ToCamelCase());
            Assert.AreEqual("myTestValue", "myTest_value".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseWithNumbersAndSeparators()
        {
            Assert.AreEqual("test123Case456", "test_123_case_456".ToCamelCase());
            Assert.AreEqual("version2Update", "version-2-update".ToCamelCase());
            Assert.AreEqual("test1Test2", "test1_test2".ToCamelCase());
            Assert.AreEqual("api2Endpoint", "api_2_endpoint".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseHandlesTurkishCharacters()
        {
            Assert.AreEqual("istanbulCity", "İSTANBUL_city".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseSpecialCharacterSeparators()
        {
            Assert.AreEqual("testCase", "test'Case".ToCamelCase());
            Assert.AreEqual("helloWorld", "hello\"World".ToCamelCase());
            Assert.AreEqual("mixedTest", "mixed.'Test".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseComplexStrings()
        {
            Assert.AreEqual("thisIsAComplexTest", "this_is_a_complex_test".ToCamelCase());
            Assert.AreEqual("thisIsAComplexTest", "THIS_IS_A_COMPLEX_TEST".ToCamelCase());
            Assert.AreEqual("thisIsAComplexTest", "this-is-a-complex-test".ToCamelCase());
            Assert.AreEqual("thisIsAComplexTest", "ThisIsAComplexTest".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseAlreadyCamelCase()
        {
            Assert.AreEqual("camelCase", "camelCase".ToCamelCase());
            Assert.AreEqual("alreadyCamelCase", "alreadyCamelCase".ToCamelCase());
            Assert.AreEqual("myVariableName", "myVariableName".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseNumbersAtBoundaries()
        {
            Assert.AreEqual("test123", "Test123".ToCamelCase());
            Assert.AreEqual("123test", "123test".ToCamelCase());
            Assert.AreEqual("123Test", "123Test".ToCamelCase());
            Assert.AreEqual("test123Test", "test123Test".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseConsecutiveNumbers()
        {
            Assert.AreEqual("test123456", "test123456".ToCamelCase());
            Assert.AreEqual("test123456End", "test123456End".ToCamelCase());
            Assert.AreEqual("abc123def456", "abc123def456".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseMixedNumbersAndUppercase()
        {
            Assert.AreEqual("user123Name", "user123Name".ToCamelCase());
            Assert.AreEqual("test456Value", "Test456Value".ToCamelCase());
            Assert.AreEqual("model3D", "model3D".ToCamelCase());
            Assert.AreEqual("http2Client", "http2Client".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseIdempotent()
        {
            // Applying ToCamelCase twice should give the same result
            Assert.AreEqual("testValue", "testValue".ToCamelCase());
            Assert.AreEqual("helloWorld", "helloWorld".ToCamelCase());

            // Verify idempotence
            string test1 = "TestValue123";
            string camel1 = test1.ToCamelCase();
            string camel2 = camel1.ToCamelCase();
            Assert.AreEqual(camel1, camel2);

            string test2 = "hello_world_test";
            string camel3 = test2.ToCamelCase();
            string camel4 = camel3.ToCamelCase();
            Assert.AreEqual(camel3, camel4);
        }

        [Test]
        public void ToCamelCasePerformanceEdgeCases()
        {
            // Very long strings
            string longString = "a".Repeat(100);
            Assert.AreEqual(longString, longString.ToCamelCase());

            string longPascal = "Test" + "Value".Repeat(50);
            string result = longPascal.ToCamelCase();
            Assert.IsTrue(char.IsLower(result[0]));

            // Many separators
            string manySeparators = "test_value_test_value_test_value_test_value";
            string camelResult = manySeparators.ToCamelCase();
            Assert.IsTrue(char.IsLower(camelResult[0]));
            Assert.IsFalse(camelResult.Contains("_"));
        }

        [Test]
        public void ToCamelCaseUnicodeCharacters()
        {
            // Test with non-ASCII characters
            Assert.AreEqual("tëstCäse", "tëst_cäse".ToCamelCase());
            Assert.AreEqual("hëlloWörld", "hëllo_wörld".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseWhitespaceVariations()
        {
            Assert.AreEqual("testCase", "test\tcase".ToCamelCase());
            Assert.AreEqual("testCase", "test\ncase".ToCamelCase());
            Assert.AreEqual("testCase", "test\rcase".ToCamelCase());
            Assert.AreEqual("testCase", "test\r\ncase".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseNumbersOnly()
        {
            Assert.AreEqual("123", "123".ToCamelCase());
            Assert.AreEqual("456789", "456789".ToCamelCase());
            Assert.AreEqual("0", "0".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseMixedCasingPatterns()
        {
            Assert.AreEqual("tEstCase", "TEstCase".ToCamelCase());
            Assert.AreEqual("hElLoWoRlD", "HElLO_WoRlD".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseAcronymsAndAbbreviations()
        {
            Assert.AreEqual("httpRequest", "HTTPRequest".ToCamelCase());
            Assert.AreEqual("xmlParser", "XMLParser".ToCamelCase());
            Assert.AreEqual("ioStream", "IOStream".ToCamelCase());
            Assert.AreEqual("urlPath", "URLPath".ToCamelCase());
            Assert.AreEqual("apiKey", "APIKey".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseQuotesAndApostrophes()
        {
            Assert.AreEqual("dontStop", "don't_stop".ToCamelCase());
            Assert.AreEqual("itsWorking", "it's_working".ToCamelCase());
            Assert.AreEqual("helloWorld", "hello\"World".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseDotsAndPeriods()
        {
            Assert.AreEqual("fileName", "file.name".ToCamelCase());
            Assert.AreEqual("testCaseValue", "test.case.value".ToCamelCase());
            Assert.AreEqual("myTestFile", "my.test.file".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseConversionFromAllFormats()
        {
            string expected = "myVariableName";
            Assert.AreEqual(expected, "MyVariableName".ToCamelCase()); // PascalCase
            Assert.AreEqual(expected, "my_variable_name".ToCamelCase()); // snake_case
            Assert.AreEqual(expected, "my-variable-name".ToCamelCase()); // kebab-case
            Assert.AreEqual(expected, "my variable name".ToCamelCase()); // space separated
            Assert.AreEqual(expected, "MY_VARIABLE_NAME".ToCamelCase()); // SCREAMING_SNAKE_CASE
        }

        [Test]
        public void ToCamelCaseExtremeEdgeCases()
        {
            // Only uppercase
            Assert.AreEqual("aaaa", "AAAA".ToCamelCase());

            // Only lowercase (should remain lowercase)
            Assert.AreEqual("aaaa", "aaaa".ToCamelCase());

            // Alternating case
            Assert.AreEqual("aBaBaB", "aBaBaB".ToCamelCase());

            // Mixed with all separator types
            Assert.AreEqual("testValueExample", "test_value-example".ToCamelCase());
            Assert.AreEqual("myTestCaseHere", "my test-case_here".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseVeryLongStrings()
        {
            // Test with very long input to ensure performance
            string longInput = string.Join("_", Enumerable.Range(0, 100).Select(i => "word" + i));
            string result = longInput.ToCamelCase();
            Assert.IsTrue(char.IsLower(result[0]));
            Assert.IsFalse(result.Contains("_"));
            Assert.IsTrue(result.Length > 0);

            // Verify it starts correctly
            Assert.IsTrue(result.StartsWith("word0"));
        }

        [Test]
        public void ToCamelCaseRepeatedConversions()
        {
            // Ensure repeated conversions are stable (idempotent)
            string[] testCases =
            {
                "hello_world",
                "HelloWorld",
                "HELLO_WORLD",
                "helloWorld",
                "hello-world",
                "hello world",
            };

            foreach (string testCase in testCases)
            {
                string first = testCase.ToCamelCase();
                string second = first.ToCamelCase();
                string third = second.ToCamelCase();

                Assert.AreEqual(first, second, $"Failed for {testCase}: first != second");
                Assert.AreEqual(second, third, $"Failed for {testCase}: second != third");
                Assert.IsTrue(
                    char.IsLower(first[0]) || char.IsDigit(first[0]) || !char.IsLetter(first[0]),
                    $"First character should be lowercase or non-letter for {testCase}"
                );
            }
        }

        [Test]
        public void ToCamelCaseWithManyConsecutiveCapitals()
        {
            Assert.AreEqual("abcdefg", "ABCDEFG".ToCamelCase());
            Assert.AreEqual("xmlHttpsApiRequest", "XmlHttpsAPIRequest".ToCamelCase());
            Assert.AreEqual("ioExceptionError", "IOExceptionError".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseWithNumbersBetweenWords()
        {
            Assert.AreEqual("word1Word2Word3", "word1_word2_word3".ToCamelCase());
            Assert.AreEqual("word1Word2Word3", "Word1_Word2_Word3".ToCamelCase());
            Assert.AreEqual("test123Middle456End", "test_123_middle_456_end".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseSingleLetterWords()
        {
            Assert.AreEqual("aBC", "a_b_c".ToCamelCase());
            Assert.AreEqual("aBC", "A_B_C".ToCamelCase());
            Assert.AreEqual("aBCDEF", "a_b_c_d_e_f".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseWithTrailingNumbers()
        {
            Assert.AreEqual("test1", "test1".ToCamelCase());
            Assert.AreEqual("test123", "Test123".ToCamelCase());
            Assert.AreEqual("myValue2", "my_value2".ToCamelCase());
            Assert.AreEqual("myValue42", "MyValue42".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseWithLeadingNumbers()
        {
            Assert.AreEqual("1test", "1test".ToCamelCase());
            Assert.AreEqual("123Test", "123Test".ToCamelCase());
            Assert.AreEqual("2FastAnd2Furious", "2_fast_and_2_furious".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseRoundTripWithOtherFormats()
        {
            string original = "myVariableName";

            // Convert to various formats and back
            string snake = original.ToSnakeCase();
            string camelFromSnake = snake.ToCamelCase();
            Assert.AreEqual(original, camelFromSnake);

            string kebab = original.ToKebabCase();
            string camelFromKebab = kebab.ToCamelCase();
            Assert.AreEqual(original, camelFromKebab);

            string pascal = original.ToPascalCase();
            string camelFromPascal = pascal.ToCamelCase();
            Assert.AreEqual(original, camelFromPascal);
        }

        [Test]
        public void ToCamelCaseEmptyPascalCaseResult()
        {
            // When ToPascalCase returns empty, ToCamelCase should too
            Assert.AreEqual(string.Empty, "___".ToCamelCase());
            Assert.AreEqual(string.Empty, "---".ToCamelCase());
            Assert.AreEqual(string.Empty, "   ".ToCamelCase());
        }

        [Test]
        public void ToCamelCasePreservesNumbersAndSpecialPatterns()
        {
            Assert.AreEqual("version2Point0", "version2Point0".ToCamelCase());
            Assert.AreEqual("version2Point0", "Version2Point0".ToCamelCase());
            Assert.AreEqual("iPhone11", "iPhone11".ToCamelCase());
            Assert.AreEqual("iOs14", "iOS14".ToCamelCase());
        }

        [Test]
        public void ToCamelCaseStressTest()
        {
            // Multiple conversions with different patterns
            string[] patterns =
            {
                "simple",
                "Simple",
                "SIMPLE",
                "simple_case",
                "SimpleCase",
                "SIMPLE_CASE",
                "simple-case",
                "simple case",
                "simpleCase",
                "a",
                "A",
                "aB",
                "AB",
                "a1",
                "A1",
                "1a",
                "1A",
                "XMLHttpRequest",
                "xml_http_request",
                "xml-http-request",
            };

            foreach (string pattern in patterns)
            {
                string result = pattern.ToCamelCase();
                // Basic invariants
                Assert.IsTrue(result != null, $"Result should not be null for: {pattern}");

                if (result.Length > 0)
                {
                    // First character should be lowercase or a non-letter
                    Assert.IsTrue(
                        char.IsLower(result[0]) || !char.IsLetter(result[0]),
                        $"First char should be lowercase or non-letter for {pattern}, got: {result}"
                    );
                }

                // Should not contain separators
                Assert.IsFalse(
                    result.Contains("_"),
                    $"Should not contain underscore: {pattern} -> {result}"
                );
                Assert.IsFalse(
                    result.Contains("-"),
                    $"Should not contain dash: {pattern} -> {result}"
                );
                Assert.IsFalse(
                    result.Contains(" "),
                    $"Should not contain space: {pattern} -> {result}"
                );

                // Idempotent
                string second = result.ToCamelCase();
                Assert.AreEqual(result, second, $"Should be idempotent for: {pattern}");
            }
        }

        [Test]
        public void ToSnakeCaseFromPascalCase()
        {
            Assert.AreEqual("pascal_case", "PascalCase".ToSnakeCase());
            Assert.AreEqual("hello_world", "HelloWorld".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseFromCamelCase()
        {
            Assert.AreEqual("camel_case", "camelCase".ToSnakeCase());
            Assert.AreEqual("hello_world_test", "helloWorldTest".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseHandlesConsecutiveUppercase()
        {
            Assert.AreEqual("html_parser", "HTMLParser".ToSnakeCase());
            Assert.AreEqual("xml_http_request", "XMLHttpRequest".ToSnakeCase());
            Assert.AreEqual("io_exception", "IOException".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseEdgeCases()
        {
            Assert.AreEqual(string.Empty, ((string)null).ToSnakeCase());
            Assert.AreEqual(string.Empty, string.Empty.ToSnakeCase());
            Assert.AreEqual("a", "A".ToSnakeCase());
            Assert.AreEqual("abc", "abc".ToSnakeCase());
            Assert.AreEqual(string.Empty, "___".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseHandlesSeparators()
        {
            Assert.AreEqual("hello_world", "hello world".ToSnakeCase());
            Assert.AreEqual("hello_world", "hello__world".ToSnakeCase());
            Assert.AreEqual("hello_world", "hello   world".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseStripsQuotesAndApostrophes()
        {
            Assert.AreEqual("test_case", "test'Case".ToSnakeCase());
            Assert.AreEqual("hello_world", "hello\"World".ToSnakeCase());
            Assert.AreEqual("dont_stop", "don't_stop".ToSnakeCase());
            Assert.AreEqual("its_working", "it's_working".ToSnakeCase());
            Assert.AreEqual("mixed_test", "mixed.'Test".ToSnakeCase());
        }

        [Test]
        public void ToKebabCaseFromPascalCase()
        {
            Assert.AreEqual("pascal-case", "PascalCase".ToKebabCase());
            Assert.AreEqual("hello-world", "HelloWorld".ToKebabCase());
            Assert.AreEqual("a", "A".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseFromCamelCase()
        {
            Assert.AreEqual("camel-case", "camelCase".ToKebabCase());
            Assert.AreEqual("hello-world-test", "helloWorldTest".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseFromSnakeCase()
        {
            Assert.AreEqual("snake-case", "snake_case".ToKebabCase());
            Assert.AreEqual("hello-world-test", "hello_world_test".ToKebabCase());
        }

        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("   ", "")]
        [TestCase("Hello World", "hello-world")]
        [TestCase("  Hello   World  ", "hello-world")]
        [TestCase("already-a-slug", "already-a-slug")]
        [TestCase("PlayerHP_Max", "player-hp-max")]
        [TestCase("The Quick! Brown? Fox.", "the-quick-brown-fox")]
        [TestCase("Level 10: The Descent", "level-10-the-descent")]
        [TestCase("---leading and trailing---", "leading-and-trailing")]
        [TestCase("tabs\tand\nnewlines", "tabs-and-newlines")]
        // Accents fold to their ASCII base rather than being dropped, so a word keeps its letters.
        [TestCase("Caf\u00e9 Ol\u00e9", "cafe-ole")]
        [TestCase("Zo\u00eb's Caf\u00e9 -- 50% Off!", "zoes-cafe-50-off")]
        // The example the docs and the XML remarks both print, so neither can drift from it.
        [TestCase("Caf\u00e9 Menu -- 50% Off!", "cafe-menu-50-off")]
        // A script with no ASCII form leaves nothing behind, which callers have to expect.
        [TestCase("\u65e5\u672c\u8a9e", "")]
        [TestCase("emoji \ud83c\udfae here", "emoji-here")]
        public void SlugifyProducesAUrlSafeSlug(string input, string expected)
        {
            Assert.AreEqual(expected, input.Slugify());
        }

        [Test]
        public void SlugifyIsAlwaysWellFormedOrEmpty()
        {
            // The shape is the contract, so it is asserted over arbitrary input rather than over a
            // list someone remembered: lowercase ASCII runs joined by single hyphens, no hyphen at
            // either end. Lone surrogates are included because Normalize refuses them and real data
            // contains them.
            const string alphabet = "aZ0 -_.!?%\t\n/\\:\u00e9\u00fc\u65e5\ud83c\udfae\ud800\udc00";
            System.Random random = new(20250822);
            for (int iteration = 0; iteration < 2000; ++iteration)
            {
                int length = random.Next(0, 24);
                char[] characters = new char[length];
                for (int i = 0; i < length; ++i)
                {
                    characters[i] = alphabet[random.Next(alphabet.Length)];
                }

                string input = new(characters);
                string slug = input.Slugify();
                Assert.IsTrue(slug != null, $"Slugify returned null for [{input}]");
                if (slug.Length == 0)
                {
                    continue;
                }

                Assert.AreNotEqual('-', slug[0], $"Leading hyphen for [{input}] -> [{slug}]");
                Assert.AreNotEqual(
                    '-',
                    slug[slug.Length - 1],
                    $"Trailing hyphen for [{input}] -> [{slug}]"
                );
                for (int i = 0; i < slug.Length; ++i)
                {
                    char current = slug[i];
                    bool allowed =
                        current is >= 'a' and <= 'z'
                        || current is >= '0' and <= '9'
                        || current == '-';
                    Assert.IsTrue(allowed, $"Illegal '{current}' in [{input}] -> [{slug}]");
                    Assert.IsFalse(
                        current == '-' && slug[i - 1] == '-',
                        $"Doubled hyphen in [{input}] -> [{slug}]"
                    );
                }
            }
        }

        [Test]
        public void SlugifyIsIdempotent()
        {
            // A slug re-slugged has to be itself, or a key stored and re-derived drifts.
            string[] inputs =
            {
                "Hello World",
                "Level 10: The Descent",
                "Caf\u00e9 Ol\u00e9",
                "---leading and trailing---",
                "PlayerHP_Max",
            };
            foreach (string input in inputs)
            {
                string once = input.Slugify();
                Assert.AreEqual(once, once.Slugify(), $"Slugify is not idempotent for [{input}]");
            }
        }

        [Test]
        public void ToKebabCaseEdgeCases()
        {
            Assert.AreEqual(string.Empty, ((string)null).ToKebabCase());
            Assert.AreEqual(string.Empty, string.Empty.ToKebabCase());
            Assert.AreEqual("html-parser", "HTMLParser".ToKebabCase());
            Assert.AreEqual(string.Empty, "___".ToKebabCase());
            Assert.AreEqual(string.Empty, "---".ToKebabCase());
            Assert.AreEqual("abc", "ABC".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseHandlesConsecutiveUppercase()
        {
            Assert.AreEqual("html-parser", "HTMLParser".ToKebabCase());
            Assert.AreEqual("xml-http-request", "XMLHttpRequest".ToKebabCase());
            Assert.AreEqual("io-exception", "IOException".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseHandlesSeparators()
        {
            Assert.AreEqual("hello-world", "hello world".ToKebabCase());
            Assert.AreEqual("hello-world", "hello__world".ToKebabCase());
            Assert.AreEqual("hello-world", "hello   world".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseFromSpaces()
        {
            Assert.AreEqual("hello-world", "hello world".ToKebabCase());
            Assert.AreEqual("test-case", "test case".ToKebabCase());
            Assert.AreEqual("multiple-words", "multiple  words".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseMultipleConsecutiveUppercase()
        {
            Assert.AreEqual("xml-http-request", "XMLHttpRequest".ToKebabCase());
            Assert.AreEqual("html-parser", "HTMLParser".ToKebabCase());
            Assert.AreEqual("io-error", "IOError".ToKebabCase());
            Assert.AreEqual("io-exception", "IOException".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseSingleCharacter()
        {
            Assert.AreEqual("a", "a".ToKebabCase());
            Assert.AreEqual("a", "A".ToKebabCase());
            Assert.AreEqual("x", "x".ToKebabCase());
            Assert.AreEqual("z", "Z".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseSingleWord()
        {
            Assert.AreEqual("test", "test".ToKebabCase());
            Assert.AreEqual("test", "TEST".ToKebabCase());
            Assert.AreEqual("test", "Test".ToKebabCase());
            Assert.AreEqual("hello", "hello".ToKebabCase());
            Assert.AreEqual("hello", "HELLO".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseOnlySeparators()
        {
            Assert.AreEqual(string.Empty, "___".ToKebabCase());
            Assert.AreEqual(string.Empty, "---".ToKebabCase());
            Assert.AreEqual(string.Empty, "   ".ToKebabCase());
            Assert.AreEqual(string.Empty, "_-_ ".ToKebabCase());
            Assert.AreEqual(string.Empty, "...".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseLeadingAndTrailingSeparators()
        {
            Assert.AreEqual("test-case", "_test_case".ToKebabCase());
            Assert.AreEqual("test-case", "test_case_".ToKebabCase());
            Assert.AreEqual("test-case", "_test_case_".ToKebabCase());
            Assert.AreEqual("test-case", "-test-case-".ToKebabCase());
            Assert.AreEqual("test-case", "__test__case__".ToKebabCase());
            Assert.AreEqual("test-case", "  test  case  ".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseMultipleSeparators()
        {
            Assert.AreEqual("test-case", "test__case".ToKebabCase());
            Assert.AreEqual("test-case", "test--case".ToKebabCase());
            Assert.AreEqual("test-case", "test__--__case".ToKebabCase());
            Assert.AreEqual("test-case", "test   case".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseMixedSeparators()
        {
            Assert.AreEqual("this-is-a-test", "this_is-a_test".ToKebabCase());
            Assert.AreEqual("hello-world-test", "hello-world_test".ToKebabCase());
            Assert.AreEqual("mixed-case-test", "mixed_Case-Test".ToKebabCase());
            Assert.AreEqual("test-case", "test.case".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseAllSeparators()
        {
            Assert.AreEqual("test-case", "test_case".ToKebabCase());
            Assert.AreEqual("test-case", "test-case".ToKebabCase());
            Assert.AreEqual("test-case", "test case".ToKebabCase());
            Assert.AreEqual("test-case", "test.case".ToKebabCase());
            Assert.AreEqual("test-case", "test\tcase".ToKebabCase());
            Assert.AreEqual("test-case", "test\ncase".ToKebabCase());
            Assert.AreEqual("test-case", "test\rcase".ToKebabCase());
            Assert.AreEqual("test-case", "test\"Case".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseUppercaseWords()
        {
            Assert.AreEqual("this-is-a-test", "THIS-IS-A-TEST".ToKebabCase());
            Assert.AreEqual("hello-world", "HELLO_WORLD".ToKebabCase());
            Assert.AreEqual("abc", "ABC".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseMixedCamelAndSnake()
        {
            Assert.AreEqual("hello-world-test", "helloWorld_test".ToKebabCase());
            Assert.AreEqual("test-case-example", "testCase_example".ToKebabCase());
            Assert.AreEqual("my-test-value", "myTest_value".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseWithNumbersAndSeparators()
        {
            Assert.AreEqual("test-123-case-456", "test_123_case_456".ToKebabCase());
            Assert.AreEqual("version-2-update", "version-2-update".ToKebabCase());
            Assert.AreEqual("test1-test2", "test1_test2".ToKebabCase());
            Assert.AreEqual("api-2-endpoint", "api_2_endpoint".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseSpecialCharacterSeparators()
        {
            Assert.AreEqual("test-case", "test'Case".ToKebabCase());
            Assert.AreEqual("hello-world", "hello\"World".ToKebabCase());
            Assert.AreEqual("mixed-test", "mixed.'Test".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseComplexStrings()
        {
            Assert.AreEqual("this-is-a-complex-test", "this_is_a_complex_test".ToKebabCase());
            Assert.AreEqual("this-is-a-complex-test", "THIS_IS_A_COMPLEX_TEST".ToKebabCase());
            Assert.AreEqual("this-is-a-complex-test", "this-is-a-complex-test".ToKebabCase());
            Assert.AreEqual("this-is-a-complex-test", "ThisIsAComplexTest".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseAlreadyKebabCase()
        {
            Assert.AreEqual("kebab-case", "kebab-case".ToKebabCase());
            Assert.AreEqual("already-kebab-case", "already-kebab-case".ToKebabCase());
            Assert.AreEqual("my-variable-name", "my-variable-name".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseNumbersAtBoundaries()
        {
            Assert.AreEqual("test-123", "Test123".ToKebabCase());
            Assert.AreEqual("123test", "123test".ToKebabCase());
            Assert.AreEqual("123-test", "123Test".ToKebabCase());
            Assert.AreEqual("test-123-test", "test123Test".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseConsecutiveNumbers()
        {
            Assert.AreEqual("test123456", "test123456".ToKebabCase());
            Assert.AreEqual("test-123456-end", "test123456End".ToKebabCase());
            Assert.AreEqual("abc123def456", "abc123def456".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseMixedNumbersAndUppercase()
        {
            Assert.AreEqual("user123name", "user123name".ToKebabCase());
            Assert.AreEqual("test-456-value", "Test456Value".ToKebabCase());
            Assert.AreEqual("model3d", "model3d".ToKebabCase());
            Assert.AreEqual("http2client", "http2client".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseIdempotent()
        {
            // Applying ToKebabCase twice should give the same result
            Assert.AreEqual("test-value", "test-value".ToKebabCase());
            Assert.AreEqual("hello-world", "hello-world".ToKebabCase());

            // Verify idempotence
            string test1 = "TestValue123";
            string kebab1 = test1.ToKebabCase();
            string kebab2 = kebab1.ToKebabCase();
            Assert.AreEqual(kebab1, kebab2);

            string test2 = "hello_world_test";
            string kebab3 = test2.ToKebabCase();
            string kebab4 = kebab3.ToKebabCase();
            Assert.AreEqual(kebab3, kebab4);
        }

        [Test]
        public void ToKebabCasePerformanceEdgeCases()
        {
            // Very long strings
            string longString = "a".Repeat(100);
            Assert.AreEqual(longString, longString.ToKebabCase());

            string longPascal = "Test" + "Value".Repeat(50);
            string result = longPascal.ToKebabCase();
            Assert.IsTrue(result.Contains("-"));
            Assert.IsFalse(result.Contains("--")); // No double dashes

            // Many separators
            string manySeparators = "test_value_test_value_test_value_test_value";
            string kebabResult = manySeparators.ToKebabCase();
            Assert.IsTrue(kebabResult.Contains("-"));
            Assert.IsFalse(kebabResult.Contains("_"));
        }

        [Test]
        public void ToKebabCaseUnicodeCharacters()
        {
            // Test with non-ASCII characters
            Assert.AreEqual("tëst-cäse", "tëst_cäse".ToKebabCase());
            Assert.AreEqual("hëllo-wörld", "hëllo_wörld".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseWhitespaceVariations()
        {
            Assert.AreEqual("test-case", "test\tcase".ToKebabCase());
            Assert.AreEqual("test-case", "test\ncase".ToKebabCase());
            Assert.AreEqual("test-case", "test\rcase".ToKebabCase());
            Assert.AreEqual("test-case", "test\r\ncase".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseNumbersOnly()
        {
            Assert.AreEqual("123", "123".ToKebabCase());
            Assert.AreEqual("456789", "456789".ToKebabCase());
            Assert.AreEqual("0", "0".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseMixedCasingPatterns()
        {
            Assert.AreEqual("t-est-case", "TEstCase".ToKebabCase());
            Assert.AreEqual("h-el-lo-wo-rl-d", "HElLO_WoRlD".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseAcronymsAndAbbreviations()
        {
            Assert.AreEqual("http-request", "HTTPRequest".ToKebabCase());
            Assert.AreEqual("xml-parser", "XMLParser".ToKebabCase());
            Assert.AreEqual("io-stream", "IOStream".ToKebabCase());
            Assert.AreEqual("url-path", "URLPath".ToKebabCase());
            Assert.AreEqual("api-key", "APIKey".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseQuotesAndApostrophes()
        {
            Assert.AreEqual("dont-stop", "don't_stop".ToKebabCase());
            Assert.AreEqual("its-working", "it's_working".ToKebabCase());
            Assert.AreEqual("hello-world", "hello\"World".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseDotsAndPeriods()
        {
            Assert.AreEqual("file-name", "file.name".ToKebabCase());
            Assert.AreEqual("test-case-value", "test.case.value".ToKebabCase());
            Assert.AreEqual("my-test-file", "my.test.file".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseConversionFromAllFormats()
        {
            string expected = "my-variable-name";
            Assert.AreEqual(expected, "MyVariableName".ToKebabCase()); // PascalCase
            Assert.AreEqual(expected, "my_variable_name".ToKebabCase()); // snake_case
            Assert.AreEqual(expected, "my-variable-name".ToKebabCase()); // kebab-case
            Assert.AreEqual(expected, "my variable name".ToKebabCase()); // space separated
            Assert.AreEqual(expected, "MY_VARIABLE_NAME".ToKebabCase()); // SCREAMING_SNAKE_CASE
            Assert.AreEqual(expected, "myVariableName".ToKebabCase()); // camelCase
        }

        [Test]
        public void ToKebabCaseExtremeEdgeCases()
        {
            // Only uppercase
            Assert.AreEqual("aaaa", "AAAA".ToKebabCase());

            // Only lowercase (should remain lowercase)
            Assert.AreEqual("aaaa", "aaaa".ToKebabCase());

            // Alternating case
            Assert.AreEqual("a-ba-ba-b", "aBaBaB".ToKebabCase());

            // Mixed with all separator types
            Assert.AreEqual("test-value-example", "test_value-example".ToKebabCase());
            Assert.AreEqual("my-test-case-here", "my test-case_here".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseVeryLongStrings()
        {
            // Test with very long input to ensure performance
            string longInput = string.Join("_", Enumerable.Range(0, 100).Select(i => "word" + i));
            string result = longInput.ToKebabCase();
            Assert.IsFalse(result.Contains("_"));
            Assert.IsTrue(result.Contains("-"));
            Assert.IsTrue(result.Length > 0);

            // Verify it starts correctly
            Assert.IsTrue(result.StartsWith("word0"));
        }

        [Test]
        public void ToKebabCaseRepeatedConversions()
        {
            // Ensure repeated conversions are stable (idempotent)
            string[] testCases =
            {
                "hello_world",
                "HelloWorld",
                "HELLO_WORLD",
                "helloWorld",
                "hello-world",
                "hello world",
            };

            foreach (string testCase in testCases)
            {
                string first = testCase.ToKebabCase();
                string second = first.ToKebabCase();
                string third = second.ToKebabCase();

                Assert.AreEqual(first, second, $"Failed for {testCase}: first != second");
                Assert.AreEqual(second, third, $"Failed for {testCase}: second != third");
                Assert.IsTrue(
                    char.IsLower(first[0]) || char.IsDigit(first[0]) || !char.IsLetter(first[0]),
                    $"First character should be lowercase or non-letter for {testCase}"
                );
            }
        }

        [Test]
        public void ToKebabCaseWithManyConsecutiveCapitals()
        {
            Assert.AreEqual("abcdefg", "ABCDEFG".ToKebabCase());
            Assert.AreEqual("xml-https-api-request", "XmlHttpsAPIRequest".ToKebabCase());
            Assert.AreEqual("io-exception-error", "IOExceptionError".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseWithNumbersBetweenWords()
        {
            // Underscores are converted to dashes, but digits within words are preserved
            Assert.AreEqual("word1-word2-word3", "word1_word2_word3".ToKebabCase());
            Assert.AreEqual("word-1-word-2-word-3", "Word1_Word2_Word3".ToKebabCase());
            Assert.AreEqual("test-123-middle-456-end", "test_123_middle_456_end".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseSingleLetterWords()
        {
            Assert.AreEqual("a-b-c", "a_b_c".ToKebabCase());
            Assert.AreEqual("a-b-c", "A_B_C".ToKebabCase());
            Assert.AreEqual("a-b-c-d-e-f", "a_b_c_d_e_f".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseWithTrailingNumbers()
        {
            Assert.AreEqual("test1", "test1".ToKebabCase());
            Assert.AreEqual("test-123", "Test123".ToKebabCase());
            Assert.AreEqual("my-value2", "my_value2".ToKebabCase());
            Assert.AreEqual("my-value-42", "MyValue42".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseWithLeadingNumbers()
        {
            Assert.AreEqual("1test", "1test".ToKebabCase());
            Assert.AreEqual("123-test", "123Test".ToKebabCase());
            Assert.AreEqual("2-fast-and-2-furious", "2_fast_and_2_furious".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseRoundTripWithOtherFormats()
        {
            string original = "my-variable-name";

            // Convert to various formats and back
            string snake = original.Replace('-', '_');
            string kebabFromSnake = snake.ToKebabCase();
            Assert.AreEqual(original, kebabFromSnake);

            string pascal = original.ToPascalCase();
            string kebabFromPascal = pascal.ToKebabCase();
            Assert.AreEqual(original, kebabFromPascal);

            string camel = original.ToCamelCase();
            string kebabFromCamel = camel.ToKebabCase();
            Assert.AreEqual(original, kebabFromCamel);
        }

        [Test]
        public void ToKebabCaseEmptyPascalCaseResult()
        {
            // When ToPascalCase returns empty, ToKebabCase should too
            Assert.AreEqual(string.Empty, "___".ToKebabCase());
            Assert.AreEqual(string.Empty, "---".ToKebabCase());
            Assert.AreEqual(string.Empty, "   ".ToKebabCase());
        }

        [Test]
        public void ToKebabCasePreservesNumbersAndSpecialPatterns()
        {
            // When uppercase letters are present, digit boundaries get separators
            Assert.AreEqual("version-2-point-0", "version2Point0".ToKebabCase());
            Assert.AreEqual("version-2-point-0", "Version2Point0".ToKebabCase());
            Assert.AreEqual("i-phone-11", "iPhone11".ToKebabCase());
            Assert.AreEqual("i-os-14", "iOS14".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseStressTest()
        {
            // Multiple conversions with different patterns
            string[] patterns =
            {
                "simple",
                "Simple",
                "SIMPLE",
                "simple_case",
                "SimpleCase",
                "SIMPLE_CASE",
                "simple-case",
                "simple case",
                "simpleCase",
                "a",
                "A",
                "aB",
                "AB",
                "a1",
                "A1",
                "1a",
                "1A",
                "XMLHttpRequest",
                "xml_http_request",
                "xml-http-request",
            };

            foreach (string pattern in patterns)
            {
                string result = pattern.ToKebabCase();
                // Basic invariants
                Assert.IsTrue(result != null, $"Result should not be null for: {pattern}");

                if (result.Length > 0)
                {
                    // First character should be lowercase or a non-letter
                    Assert.IsTrue(
                        char.IsLower(result[0]) || !char.IsLetter(result[0]),
                        $"First char should be lowercase or non-letter for {pattern}, got: {result}"
                    );
                }

                // Should not contain other separators
                Assert.IsFalse(
                    result.Contains("_"),
                    $"Should not contain underscore: {pattern} -> {result}"
                );
                Assert.IsFalse(
                    result.Contains(" "),
                    $"Should not contain space: {pattern} -> {result}"
                );

                // Idempotent
                string second = result.ToKebabCase();
                Assert.AreEqual(result, second, $"Should be idempotent for: {pattern}");
            }
        }

        [Test]
        public void ToKebabCasePreservesLowercaseWithNumbers()
        {
            // Pure lowercase strings with numbers should be preserved
            Assert.AreEqual("user123", "user123".ToKebabCase());
            Assert.AreEqual("test456", "test456".ToKebabCase());
            Assert.AreEqual("abc789xyz", "abc789xyz".ToKebabCase());
            Assert.AreEqual("model3d", "model3d".ToKebabCase());
            Assert.AreEqual("http2", "http2".ToKebabCase());

            // Pure numbers
            Assert.AreEqual("12345", "12345".ToKebabCase());
            Assert.AreEqual("0", "0".ToKebabCase());

            // Numbers at start
            Assert.AreEqual("2fast", "2fast".ToKebabCase());
            Assert.AreEqual("404error", "404error".ToKebabCase());

            // Numbers at end
            Assert.AreEqual("version2", "version2".ToKebabCase());
            Assert.AreEqual("player1", "player1".ToKebabCase());

            // Mixed numbers
            Assert.AreEqual("abc123def456", "abc123def456".ToKebabCase());
            Assert.AreEqual("test1middle2end3", "test1middle2end3".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseHandlesNumbersWithCasing()
        {
            // When uppercase letters are present, digit boundaries should get separators
            Assert.AreEqual("user-123-name", "user123Name".ToKebabCase());
            Assert.AreEqual("test-456-value", "Test456Value".ToKebabCase());
            Assert.AreEqual("model-3-d", "model3D".ToKebabCase());
            Assert.AreEqual("http-2-client", "http2Client".ToKebabCase());

            // Multiple transitions
            Assert.AreEqual("api-v-1-endpoint-2", "apiV1Endpoint2".ToKebabCase());
            Assert.AreEqual("test-123-abc-456-def", "test123Abc456Def".ToKebabCase());

            // Numbers with consecutive uppercase
            Assert.AreEqual("http-200-ok", "HTTP200OK".ToKebabCase());
            Assert.AreEqual("xml-2-parser", "XML2Parser".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseHandlesUnderscoresWithNumbers()
        {
            // Strings already with underscores
            Assert.AreEqual("test-123", "test_123".ToKebabCase());
            Assert.AreEqual("value-456-end", "value_456_end".ToKebabCase());
            Assert.AreEqual("my-3d-model", "my_3d_model".ToKebabCase());

            // Mixed underscores and uppercase
            Assert.AreEqual("test-123-value", "test_123Value".ToKebabCase());
            Assert.AreEqual("my-test-2-beta", "my_test2Beta".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseHandlesEdgeCasesWithNumbers()
        {
            // Single character with number
            Assert.AreEqual("a1", "a1".ToKebabCase());
            Assert.AreEqual("a-1", "A1".ToKebabCase());

            // Multiple single digits
            Assert.AreEqual("a1b2c3d4", "a1b2c3d4".ToKebabCase());
            Assert.AreEqual("a-1-b-2-c-3-d-4", "A1B2C3D4".ToKebabCase());

            // Consecutive numbers
            Assert.AreEqual("test123456", "test123456".ToKebabCase());
            Assert.AreEqual("test-123456-end", "test123456End".ToKebabCase());

            // Number at very start
            Assert.AreEqual("1test", "1test".ToKebabCase());
            Assert.AreEqual("1-test", "1Test".ToKebabCase());
            Assert.AreEqual("123test456", "123test456".ToKebabCase());
            Assert.AreEqual("123-test-456", "123Test456".ToKebabCase());
        }

        [Test]
        public void ToKebabCaseFromExistingKebabCase()
        {
            // These were the old tests, now incorporated above
            Assert.AreEqual("test-123-value", "test123Value".ToKebabCase());
        }

        [Test]
        public void ToTitleCaseNominal()
        {
            Assert.AreEqual("Hello World", "hello world".ToTitleCase());
            Assert.AreEqual("The Quick Brown Fox", "the quick brown fox".ToTitleCase());
            Assert.AreEqual("A", "a".ToTitleCase());
        }

        [Test]
        public void ToTitleCasePreservesWhitespace()
        {
            Assert.AreEqual("Hello  World", "hello  world".ToTitleCase());
            Assert.AreEqual("Hello\tWorld", "hello\tworld".ToTitleCase());
        }

        [Test]
        public void ToTitleCaseEdgeCases()
        {
            Assert.AreEqual(string.Empty, ((string)null).ToTitleCase());
            Assert.AreEqual(string.Empty, string.Empty.ToTitleCase());
            Assert.AreEqual("Hello World", "HELLO WORLD".ToTitleCase());
            Assert.AreEqual("Hello World", "hELLO wORLD".ToTitleCase());
        }

        [Test]
        public void ToTitleCaseHandlesSeparators()
        {
            Assert.AreEqual("Hello_World", "hello_world".ToTitleCase());
            Assert.AreEqual("Hello's World", "hello's world".ToTitleCase());
        }

        [Test]
        public void ToTitleCaseWithoutPreservingSeparatorsCollapsesDelimiters()
        {
            Assert.AreEqual(
                "Hello World 123 Beta",
                "hello_world-123__beta".ToTitleCase(preserveSeparators: false)
            );
            Assert.AreEqual("Foo Bar", "foo---bar".ToTitleCase(preserveSeparators: false));
        }

        [Test]
        public void ContainsIgnoreCaseNominal()
        {
            Assert.IsTrue("Hello World".ContainsIgnoreCase("hello"));
            Assert.IsTrue("Hello World".ContainsIgnoreCase("WORLD"));
            Assert.IsTrue("Hello World".ContainsIgnoreCase("o W"));
            Assert.IsTrue("test".ContainsIgnoreCase("TEST"));
        }

        [Test]
        public void ContainsIgnoreCaseReturnsFalseWhenNotFound()
        {
            Assert.IsFalse("Hello World".ContainsIgnoreCase("foo"));
            Assert.IsFalse("test".ContainsIgnoreCase("testing"));
        }

        [Test]
        public void ContainsIgnoreCaseHandlesNulls()
        {
            Assert.IsFalse(((string)null).ContainsIgnoreCase("test"));
            Assert.IsFalse("test".ContainsIgnoreCase(null));
            Assert.IsFalse(((string)null).ContainsIgnoreCase(null));
        }

        [Test]
        public void EqualsIgnoreCaseNominal()
        {
            Assert.IsTrue("Hello".EqualsIgnoreCase("hello"));
            Assert.IsTrue("WORLD".EqualsIgnoreCase("world"));
            Assert.IsTrue("TeSt".EqualsIgnoreCase("tEsT"));
        }

        [Test]
        public void EqualsIgnoreCaseReturnsFalseForDifferent()
        {
            Assert.IsFalse("Hello".EqualsIgnoreCase("World"));
            Assert.IsFalse("test".EqualsIgnoreCase("testing"));
        }

        [Test]
        public void EqualsIgnoreCaseHandlesNulls()
        {
            Assert.IsTrue(((string)null).EqualsIgnoreCase(null));
            Assert.IsFalse("test".EqualsIgnoreCase(null));
            Assert.IsFalse(((string)null).EqualsIgnoreCase("test"));
        }

        [Test]
        public void ReverseNominal()
        {
            Assert.AreEqual("tset", "test".Reverse());
            Assert.AreEqual("dlrow olleh", "hello world".Reverse());
            Assert.AreEqual("a", "a".Reverse());
        }

        [Test]
        public void ReverseEdgeCases()
        {
            Assert.AreEqual(null, ((string)null).Reverse());
            Assert.AreEqual(string.Empty, string.Empty.Reverse());
        }

        [Test]
        public void ReverseIsPalindrome()
        {
            Assert.AreEqual("racecar", "racecar".Reverse());
            Assert.AreEqual("noon", "noon".Reverse());
        }

        private static IEnumerable<TestCaseData> NonBasicPlaneStrings()
        {
            yield return new TestCaseData("ab\U0001F44Dcd").SetName("NonBmp.EmojiInMiddle");
            yield return new TestCaseData("\U0001F600").SetName("NonBmp.EmojiAlone");
            yield return new TestCaseData("\U0001F3F4\U000E0067b").SetName("NonBmp.TwoPairs");
            yield return new TestCaseData("x\U00020BB7").SetName("NonBmp.EmojiLast");
            yield return new TestCaseData("\U0001D11Ez").SetName("NonBmp.EmojiFirst");
        }

        [TestCaseSource(nameof(NonBasicPlaneStrings))]
        public void ReverseKeepsSurrogatePairsIntact(string input)
        {
            string reversed = input.Reverse();

            Assert.AreEqual(input.Length, reversed.Length);
            Assert.IsTrue(
                IsWellFormedUtf16(reversed),
                $"'{Describe(reversed)}' is not well-formed UTF-16"
            );
            Assert.AreEqual(input, reversed.Reverse());
        }

        [TestCaseSource(nameof(NonBasicPlaneStrings))]
        public void TruncateNeverExceedsTheLimitOrSplitsACharacter(string input)
        {
            string[] ellipses = { "...", "", "\u2026" };
            foreach (string ellipsis in ellipses)
            {
                for (int maxLength = 0; maxLength <= input.Length + 1; ++maxLength)
                {
                    string truncated = input.Truncate(maxLength, ellipsis);

                    Assert.LessOrEqual(
                        truncated.Length,
                        maxLength,
                        $"Truncate('{Describe(input)}', {maxLength}, '{ellipsis}') grew past its limit"
                    );
                    Assert.IsTrue(
                        IsWellFormedUtf16(truncated),
                        $"Truncate('{Describe(input)}', {maxLength}, '{ellipsis}') produced a lone surrogate"
                    );
                }
            }
        }

        [Test]
        public void TruncateRespectsTheLimitWhenTheEllipsisCannotFit()
        {
            Assert.AreEqual("he", "hello".Truncate(2, "..."));
            Assert.AreEqual(string.Empty, "hello".Truncate(0, "..."));
            Assert.AreEqual("...", "hello".Truncate(3, "..."));
        }

        private static bool IsWellFormedUtf16(string value)
        {
            for (int i = 0; i < value.Length; ++i)
            {
                if (char.IsHighSurrogate(value[i]))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    {
                        return false;
                    }

                    ++i;
                    continue;
                }

                if (char.IsLowSurrogate(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Describe(string value)
        {
            return string.Concat(value.Select(character => $"\\u{(int)character:X4}"));
        }

        [Test]
        public void RemoveWhitespaceNominal()
        {
            Assert.AreEqual("helloworld", "hello world".RemoveWhitespace());
            Assert.AreEqual("test", "  test  ".RemoveWhitespace());
            Assert.AreEqual("abc", "a b c".RemoveWhitespace());
        }

        [Test]
        public void RemoveWhitespaceHandlesAllTypes()
        {
            Assert.AreEqual("test", "t\te\ns\rt ".RemoveWhitespace());
            Assert.AreEqual("abc", "  a  b  c  ".RemoveWhitespace());
        }

        [Test]
        public void RemoveWhitespaceEdgeCases()
        {
            Assert.AreEqual(null, ((string)null).RemoveWhitespace());
            Assert.AreEqual(string.Empty, string.Empty.RemoveWhitespace());
            Assert.AreEqual(string.Empty, "   ".RemoveWhitespace());
        }

        [Test]
        public void CountOccurrencesCharNominal()
        {
            Assert.AreEqual(2, "hello".CountOccurrences('l'));
            Assert.AreEqual(1, "hello".CountOccurrences('h'));
            Assert.AreEqual(0, "hello".CountOccurrences('x'));
        }

        [Test]
        public void CountOccurrencesCharEdgeCases()
        {
            Assert.AreEqual(0, ((string)null).CountOccurrences('a'));
            Assert.AreEqual(0, string.Empty.CountOccurrences('a'));
            Assert.AreEqual(5, "aaaaa".CountOccurrences('a'));
        }

        [Test]
        public void CountOccurrencesStringNominal()
        {
            Assert.AreEqual(2, "hello world hello".CountOccurrences("hello"));
            Assert.AreEqual(1, "test".CountOccurrences("test"));
            Assert.AreEqual(0, "hello".CountOccurrences("world"));
        }

        [Test]
        public void CountOccurrencesStringOverlapping()
        {
            Assert.AreEqual(2, "aaaa".CountOccurrences("aa"));
            Assert.AreEqual(4, "abababab".CountOccurrences("ab"));
        }

        [Test]
        public void CountOccurrencesStringEdgeCases()
        {
            Assert.AreEqual(0, ((string)null).CountOccurrences("test"));
            Assert.AreEqual(0, "test".CountOccurrences(null));
            Assert.AreEqual(0, string.Empty.CountOccurrences("test"));
            Assert.AreEqual(0, "test".CountOccurrences(string.Empty));
        }

        [Test]
        public void IsNumericNominal()
        {
            Assert.IsTrue("123".IsNumeric());
            Assert.IsTrue("0".IsNumeric());
            Assert.IsTrue("9876543210".IsNumeric());
        }

        [Test]
        public void IsNumericReturnsFalseForNonNumeric()
        {
            Assert.IsFalse("abc".IsNumeric());
            Assert.IsFalse("123abc".IsNumeric());
            Assert.IsFalse("12.34".IsNumeric());
            Assert.IsFalse("-123".IsNumeric());
            Assert.IsFalse("1 2 3".IsNumeric());
        }

        [Test]
        public void IsNumericEdgeCases()
        {
            Assert.IsFalse(((string)null).IsNumeric());
            Assert.IsFalse(string.Empty.IsNumeric());
            Assert.IsFalse(" ".IsNumeric());
        }

        [Test]
        public void IsAlphabeticNominal()
        {
            Assert.IsTrue("abc".IsAlphabetic());
            Assert.IsTrue("ABC".IsAlphabetic());
            Assert.IsTrue("AbCdEf".IsAlphabetic());
        }

        [Test]
        public void IsAlphabeticReturnsFalseForNonAlphabetic()
        {
            Assert.IsFalse("abc123".IsAlphabetic());
            Assert.IsFalse("123".IsAlphabetic());
            Assert.IsFalse("a b c".IsAlphabetic());
            Assert.IsFalse("hello world".IsAlphabetic());
        }

        [Test]
        public void IsAlphabeticEdgeCases()
        {
            Assert.IsFalse(((string)null).IsAlphabetic());
            Assert.IsFalse(string.Empty.IsAlphabetic());
            Assert.IsFalse(" ".IsAlphabetic());
        }

        [Test]
        public void IsAlphanumericNominal()
        {
            Assert.IsTrue("abc123".IsAlphanumeric());
            Assert.IsTrue("ABC".IsAlphanumeric());
            Assert.IsTrue("123".IsAlphanumeric());
            Assert.IsTrue("Test123".IsAlphanumeric());
        }

        [Test]
        public void IsAlphanumericReturnsFalseForNonAlphanumeric()
        {
            Assert.IsFalse("abc 123".IsAlphanumeric());
            Assert.IsFalse("hello-world".IsAlphanumeric());
            Assert.IsFalse("test!".IsAlphanumeric());
        }

        [Test]
        public void IsAlphanumericEdgeCases()
        {
            Assert.IsFalse(((string)null).IsAlphanumeric());
            Assert.IsFalse(string.Empty.IsAlphanumeric());
            Assert.IsFalse(" ".IsAlphanumeric());
        }

        [Test]
        public void ToBase64AndFromBase64RoundTrip()
        {
            string original = "Hello, World!";
            string base64 = original.ToBase64();
            Assert.IsTrue(base64 != null);
            Assert.AreNotEqual(original, base64);

            string decoded = base64.FromBase64();
            Assert.AreEqual(original, decoded);
        }

        [Test]
        public void ToBase64HandlesSpecialCharacters()
        {
            string original = "Test @#$%^&*()";
            string base64 = original.ToBase64();
            string decoded = base64.FromBase64();
            Assert.AreEqual(original, decoded);
        }

        [Test]
        public void ToBase64EdgeCases()
        {
            Assert.AreEqual(string.Empty, ((string)null).ToBase64());
            Assert.AreEqual(string.Empty, string.Empty.ToBase64());
        }

        [Test]
        public void FromBase64HandlesInvalidInput()
        {
            Assert.AreEqual(string.Empty, "not valid base64!@#".FromBase64());
            Assert.AreEqual(string.Empty, ((string)null).FromBase64());
            Assert.AreEqual(string.Empty, string.Empty.FromBase64());
        }

        [Test]
        public void FromBase64RejectsLikelyButInvalidPadding()
        {
            Assert.AreEqual(string.Empty, "AAAA====".FromBase64());
        }

        [Test]
        public void RepeatNominal()
        {
            Assert.AreEqual("aaa", "a".Repeat(3));
            Assert.AreEqual("testtest", "test".Repeat(2));
            Assert.AreEqual("hello", "hello".Repeat(1));
        }

        [Test]
        public void RepeatEdgeCases()
        {
            Assert.AreEqual(string.Empty, ((string)null).Repeat(5));
            Assert.AreEqual(string.Empty, string.Empty.Repeat(5));
            Assert.AreEqual(string.Empty, "test".Repeat(0));
            Assert.AreEqual(string.Empty, "test".Repeat(-1));
        }

        [Test]
        public void RepeatLargeCount()
        {
            string result = "a".Repeat(100);
            Assert.AreEqual(100, result.Length);
            Assert.IsTrue(result.All(c => c == 'a'));
        }

        [Test]
        public void SplitCamelCaseNominal()
        {
            string[] result = "HelloWorld".SplitCamelCase();
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("Hello", result[0]);
            Assert.AreEqual("World", result[1]);
        }

        [Test]
        public void SplitCamelCaseHandlesConsecutiveUppercase()
        {
            string[] result = "HTMLParser".SplitCamelCase();
            Assert.AreEqual(2, result.Length);
            Assert.AreEqual("HTML", result[0]);
            Assert.AreEqual("Parser", result[1]);
        }

        [Test]
        public void SplitCamelCaseHandlesSeparators()
        {
            string[] result = "hello_world_test".SplitCamelCase();
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("hello", result[0]);
            Assert.AreEqual("world", result[1]);
            Assert.AreEqual("test", result[2]);
        }

        [Test]
        public void SplitCamelCaseEdgeCases()
        {
            Assert.AreEqual(0, ((string)null).SplitCamelCase().Length);
            Assert.AreEqual(0, string.Empty.SplitCamelCase().Length);
            Assert.AreEqual(1, "test".SplitCamelCase().Length);
        }

        [Test]
        public void SplitCamelCaseMixed()
        {
            string[] result = "XMLHttpRequest".SplitCamelCase();
            Assert.AreEqual(3, result.Length);
            Assert.AreEqual("XML", result[0]);
            Assert.AreEqual("Http", result[1]);
            Assert.AreEqual("Request", result[2]);
        }

        [Test]
        public void ReplaceFirstNominal()
        {
            Assert.AreEqual("bbc", "abc".ReplaceFirst("a", "b"));
            Assert.AreEqual("test world test", "hello world test".ReplaceFirst("hello", "test"));
            Assert.AreEqual("abcdefabc", "abcabc".ReplaceFirst("abc", "abcdef"));
        }

        [Test]
        public void ReplaceFirstOnlyReplacesFirst()
        {
            Assert.AreEqual("xc abc abc", "abc abc abc".ReplaceFirst("ab", "x"));
            Assert.AreEqual("test test hello", "hello test hello".ReplaceFirst("hello", "test"));
        }

        [Test]
        public void ReplaceFirstEdgeCases()
        {
            Assert.AreEqual(null, ((string)null).ReplaceFirst("a", "b"));
            Assert.AreEqual(string.Empty, string.Empty.ReplaceFirst("a", "b"));
            Assert.AreEqual("abc", "abc".ReplaceFirst("x", "y"));
            Assert.AreEqual("abc", "abc".ReplaceFirst("", "x"));
        }

        [Test]
        public void ReplaceFirstWithNull()
        {
            Assert.AreEqual("bc", "abc".ReplaceFirst("a", null));
        }

        [Test]
        public void ReplaceLastNominal()
        {
            Assert.AreEqual("abb", "abc".ReplaceLast("c", "b"));
            Assert.AreEqual("hello test world", "hello test test".ReplaceLast("test", "world"));
            Assert.AreEqual("abcabcdef", "abcabc".ReplaceLast("abc", "abcdef"));
        }

        [Test]
        public void ReplaceLastOnlyReplacesLast()
        {
            Assert.AreEqual("abc abc xc", "abc abc abc".ReplaceLast("ab", "x"));
            Assert.AreEqual("hello test test", "hello test hello".ReplaceLast("hello", "test"));
        }

        [Test]
        public void ReplaceLastEdgeCases()
        {
            Assert.AreEqual(null, ((string)null).ReplaceLast("a", "b"));
            Assert.AreEqual(string.Empty, string.Empty.ReplaceLast("a", "b"));
            Assert.AreEqual("abc", "abc".ReplaceLast("x", "y"));
            Assert.AreEqual("abc", "abc".ReplaceLast("", "x"));
        }

        [Test]
        public void ReplaceLastWithNull()
        {
            Assert.AreEqual("ab", "abc".ReplaceLast("c", null));
        }

        [Test]
        public void GetBytesHandlesUnicodeCharacters()
        {
            string unicode = "Hello 世界 🌍";
            byte[] bytes = unicode.GetBytes();
            Assert.IsTrue(bytes != null);
            Assert.Greater(bytes.Length, unicode.Length);

            string decoded = bytes.GetString();
            Assert.AreEqual(unicode, decoded);
        }

        [Test]
        public void GetStringHandlesEmptyArray()
        {
            Assert.AreEqual(string.Empty, Array.Empty<byte>().GetString());
            Assert.AreEqual(string.Empty, ((byte[])null).GetString());
        }

        [Test]
        public void ToPascalCaseMultipleConsecutiveUppercase()
        {
            Assert.AreEqual("XmlHttpRequest", "XMLHttpRequest".ToPascalCase());
            Assert.AreEqual("HtmlParser", "HTMLParser".ToPascalCase());
            Assert.AreEqual("IoError", "IOError".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseWithNumbers()
        {
            Assert.AreEqual("Test123", "test123".ToPascalCase());
            Assert.AreEqual("Test123Test", "test_123_test".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseComplexStrings()
        {
            Assert.AreEqual("ThisIsAComplexTest", "this_is_a_complex_test".ToPascalCase());
            Assert.AreEqual("ThisIsAComplexTest", "THIS_IS_A_COMPLEX_TEST".ToPascalCase());
            Assert.AreEqual("ThisIsAComplexTest", "this-is-a-complex-test".ToPascalCase());
        }

        [Test]
        public void ToSnakeCasePreservesNumbers()
        {
            Assert.AreEqual("test123", "test123".ToSnakeCase());
            Assert.AreEqual("test_123_value", "test123Value".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseWithNumbers()
        {
            // Letter to digit transitions (only when there are uppercase letters)
            Assert.AreEqual("test_456_end", "test456End".ToSnakeCase());

            // Digit to letter transitions (only when there are uppercase letters)
            Assert.AreEqual("version_2_beta", "version2Beta".ToSnakeCase());
            Assert.AreEqual("my_3_d_model", "my3DModel".ToSnakeCase());

            // Multiple number groups
            Assert.AreEqual("test_123_value_456", "test123Value456".ToSnakeCase());
            Assert.AreEqual("api_v_2_endpoint_3", "apiV2Endpoint3".ToSnakeCase());

            // Edge cases
            Assert.AreEqual("123", "123".ToSnakeCase());
            Assert.AreEqual("test_123_value_456_end", "test123Value456End".ToSnakeCase());

            // Combined with uppercase
            Assert.AreEqual("my_class_2_d", "MyClass2D".ToSnakeCase());
            Assert.AreEqual("http_2_client", "HTTP2Client".ToSnakeCase());

            // Already lowercase - should be preserved
            Assert.AreEqual("abc123", "abc123".ToSnakeCase());
            Assert.AreEqual("value1", "value1".ToSnakeCase());
            Assert.AreEqual("123abc", "123abc".ToSnakeCase());
            Assert.AreEqual("a1b2c3", "a1b2c3".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCasePreservesLowercaseWithNumbers()
        {
            // Pure lowercase strings with numbers should be preserved
            Assert.AreEqual("user123", "user123".ToSnakeCase());
            Assert.AreEqual("test456", "test456".ToSnakeCase());
            Assert.AreEqual("abc789xyz", "abc789xyz".ToSnakeCase());
            Assert.AreEqual("model3d", "model3d".ToSnakeCase());
            Assert.AreEqual("http2", "http2".ToSnakeCase());

            // Pure numbers
            Assert.AreEqual("12345", "12345".ToSnakeCase());
            Assert.AreEqual("0", "0".ToSnakeCase());

            // Numbers at start
            Assert.AreEqual("2fast", "2fast".ToSnakeCase());
            Assert.AreEqual("404error", "404error".ToSnakeCase());

            // Numbers at end
            Assert.AreEqual("version2", "version2".ToSnakeCase());
            Assert.AreEqual("player1", "player1".ToSnakeCase());

            // Mixed numbers
            Assert.AreEqual("abc123def456", "abc123def456".ToSnakeCase());
            Assert.AreEqual("test1middle2end3", "test1middle2end3".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseHandlesNumbersWithCasing()
        {
            // When uppercase letters are present, digit boundaries should get separators
            Assert.AreEqual("user_123_name", "user123Name".ToSnakeCase());
            Assert.AreEqual("test_456_value", "Test456Value".ToSnakeCase());
            Assert.AreEqual("model_3_d", "model3D".ToSnakeCase());
            Assert.AreEqual("http_2_client", "http2Client".ToSnakeCase());

            // Multiple transitions
            Assert.AreEqual("api_v_1_endpoint_2", "apiV1Endpoint2".ToSnakeCase());
            Assert.AreEqual("test_123_abc_456_def", "test123Abc456Def".ToSnakeCase());

            // Numbers with consecutive uppercase
            Assert.AreEqual("http_200_ok", "HTTP200OK".ToSnakeCase());
            Assert.AreEqual("xml_2_parser", "XML2Parser".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseHandlesUnderscoresWithNumbers()
        {
            // Strings already with underscores
            Assert.AreEqual("test_123", "test_123".ToSnakeCase());
            Assert.AreEqual("value_456_end", "value_456_end".ToSnakeCase());
            Assert.AreEqual("my_3d_model", "my_3d_model".ToSnakeCase());

            // Mixed underscores and uppercase
            Assert.AreEqual("test_123_value", "test_123Value".ToSnakeCase());
            Assert.AreEqual("my_test_2_beta", "my_test2Beta".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseHandlesEdgeCasesWithNumbers()
        {
            // Single character with number
            Assert.AreEqual("a1", "a1".ToSnakeCase());
            Assert.AreEqual("a_1", "A1".ToSnakeCase());

            // Multiple single digits
            Assert.AreEqual("a1b2c3d4", "a1b2c3d4".ToSnakeCase());
            Assert.AreEqual("a_1_b_2_c_3_d_4", "A1B2C3D4".ToSnakeCase());

            // Consecutive numbers
            Assert.AreEqual("test123456", "test123456".ToSnakeCase());
            Assert.AreEqual("test_123456_end", "test123456End".ToSnakeCase());

            // Number at very start
            Assert.AreEqual("1test", "1test".ToSnakeCase());
            Assert.AreEqual("1_test", "1Test".ToSnakeCase());
            Assert.AreEqual("123test456", "123test456".ToSnakeCase());
            Assert.AreEqual("123_test_456", "123Test456".ToSnakeCase());
        }

        [Test]
        public void ToSnakeCaseIdempotent()
        {
            // Applying ToSnakeCase twice should give the same result
            Assert.AreEqual("test_value", "test_value".ToSnakeCase());
            Assert.AreEqual("hello_world", "hello_world".ToSnakeCase());
            Assert.AreEqual("test123", "test123".ToSnakeCase());
            Assert.AreEqual("abc_123_def", "abc_123_def".ToSnakeCase());

            // Verify idempotence
            string test1 = "TestValue123";
            string snake1 = test1.ToSnakeCase();
            string snake2 = snake1.ToSnakeCase();
            Assert.AreEqual(snake1, snake2);

            string test2 = "user123Name";
            string snake3 = test2.ToSnakeCase();
            string snake4 = snake3.ToSnakeCase();
            Assert.AreEqual(snake3, snake4);
        }

        [Test]
        public void ToSnakeCasePerformanceEdgeCases()
        {
            // Very long strings
            string longString = "a".Repeat(100) + "123" + "b".Repeat(100);
            Assert.AreEqual(longString, longString.ToSnakeCase());

            string longMixed = "Test" + "Value".Repeat(50);
            string result = longMixed.ToSnakeCase();
            Assert.IsTrue(result.Contains("_"));
            Assert.IsFalse(result.Contains("__")); // No double underscores

            // Many number transitions
            string manyNumbers = "a1b2c3d4e5f6g7h8i9j0";
            Assert.AreEqual(manyNumbers, manyNumbers.ToSnakeCase());
        }

        [Test]
        public void RepeatWithMultiCharacterString()
        {
            Assert.AreEqual("abcabcabc", "abc".Repeat(3));
            Assert.AreEqual("hello hello ", "hello ".Repeat(2));
        }

        [Test]
        public void CountOccurrencesWithSingleCharSubstring()
        {
            Assert.AreEqual(2, "hello".CountOccurrences("l"));
            Assert.AreEqual(2, "test".CountOccurrences("t"));
        }

        [Test]
        public void ContainsIgnoreCaseEmptyString()
        {
            Assert.IsTrue("test".ContainsIgnoreCase(""));
            Assert.IsTrue("".ContainsIgnoreCase(""));
        }

        [Test]
        public void ToBase64HandlesEmptyString()
        {
            string base64 = string.Empty.ToBase64();
            Assert.AreEqual(string.Empty, base64);
        }

        [Test]
        public void FromBase64HandlesValidBase64()
        {
            string base64 = "SGVsbG8gV29ybGQ=";
            string decoded = base64.FromBase64();
            Assert.AreEqual("Hello World", decoded);
        }

        [Test]
        public void ToPascalCaseMixedSeparators()
        {
            Assert.AreEqual("ThisIsATest", "this_is-a_test".ToPascalCase());
            Assert.AreEqual("HelloWorldTest", "hello-world_test".ToPascalCase());
            Assert.AreEqual("MixedCaseTest", "mixed_Case-Test".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseMultipleSeparators()
        {
            Assert.AreEqual("TestCase", "test__case".ToPascalCase());
            Assert.AreEqual("TestCase", "test--case".ToPascalCase());
            Assert.AreEqual("TestCase", "test__--__case".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseLeadingAndTrailingSeparators()
        {
            Assert.AreEqual("TestCase", "_test_case".ToPascalCase());
            Assert.AreEqual("TestCase", "test_case_".ToPascalCase());
            Assert.AreEqual("TestCase", "_test_case_".ToPascalCase());
            Assert.AreEqual("TestCase", "-test-case-".ToPascalCase());
            Assert.AreEqual("TestCase", "__test__case__".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseAllSeparators()
        {
            Assert.AreEqual("TestCase", "test_case".ToPascalCase());
            Assert.AreEqual("TestCase", "test-case".ToPascalCase());
            Assert.AreEqual("TestCase", "test case".ToPascalCase());
            Assert.AreEqual("TestCase", "test.case".ToPascalCase());
            Assert.AreEqual("TestCase", "test\tcase".ToPascalCase());
            Assert.AreEqual("TestCase", "test\ncase".ToPascalCase());
            Assert.AreEqual("TestCase", "test\rcase".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseSingleWord()
        {
            Assert.AreEqual("Test", "test".ToPascalCase());
            Assert.AreEqual("Test", "TEST".ToPascalCase());
            Assert.AreEqual("Test", "Test".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseSingleCharacter()
        {
            Assert.AreEqual("A", "a".ToPascalCase());
            Assert.AreEqual("A", "A".ToPascalCase());
            Assert.AreEqual(string.Empty, "_".ToPascalCase());
            Assert.AreEqual(string.Empty, "-".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseOnlySeparators()
        {
            Assert.AreEqual(string.Empty, "___".ToPascalCase());
            Assert.AreEqual(string.Empty, "---".ToPascalCase());
            Assert.AreEqual(string.Empty, "   ".ToPascalCase());
            Assert.AreEqual(string.Empty, "_-_ ".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseUppercaseWords()
        {
            Assert.AreEqual("ThisIsATest", "THIS-IS-A-TEST".ToPascalCase());
            Assert.AreEqual("HelloWorld", "HELLO_WORLD".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseMixedCamelAndSnake()
        {
            Assert.AreEqual("HelloWorldTest", "helloWorld_test".ToPascalCase());
            Assert.AreEqual("TestCaseExample", "testCase_example".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseWithNumbersAndSeparators()
        {
            Assert.AreEqual("Test123Case456", "test_123_case_456".ToPascalCase());
            Assert.AreEqual("Version2Update", "version-2-update".ToPascalCase());
            Assert.AreEqual("Test1Test2", "test1_test2".ToPascalCase());
        }

        [Test]
        public void ToPascalCaseSpecialCharacterSeparators()
        {
            Assert.AreEqual("TestCase", "test'Case".ToPascalCase());
            Assert.AreEqual("HelloWorld", "hello\"World".ToPascalCase());
            Assert.AreEqual("MixedTest", "mixed.'Test".ToPascalCase());
        }

        private static readonly object[] ToCaseMatrixData =
        {
            new object[] { "mixed_input", StringCase.PascalCase, "MixedInput" },
            new object[] { "mixed_input", StringCase.CamelCase, "mixedInput" },
            new object[] { "mixed_input", StringCase.SnakeCase, "mixed_input" },
            new object[] { "mixed_input", StringCase.KebabCase, "mixed-input" },
            new object[] { "mixed_input", StringCase.TitleCase, "Mixed Input" },
            new object[] { "mixed_input", StringCase.LowerCase, "mixed_input" },
            new object[] { "mixed_input", StringCase.UpperCase, "MIXED_INPUT" },
            new object[] { "mixed_input", StringCase.LowerInvariant, "mixed_input" },
            new object[] { "mixed_input", StringCase.UpperInvariant, "MIXED_INPUT" },
#pragma warning disable CS0618
            new object[] { "mixed_input", StringCase.None, "mixed_input" },
#pragma warning restore CS0618
            new object[] { "HeLLo WoRLd", StringCase.LowerCase, "hello world" },
            new object[] { "HeLLo WoRLd", StringCase.UpperCase, "HELLO WORLD" },
            new object[] { "İSTANBUL", StringCase.LowerInvariant, "istanbul" },
            new object[] { "istanbul", StringCase.UpperInvariant, "ISTANBUL" },
        };

        [TestCaseSource(nameof(ToCaseMatrixData))]
        public void ToCaseMatrixCoversAllStringCases(
            string input,
            StringCase stringCase,
            string expected
        )
        {
            Assert.AreEqual(expected, input.ToCase(stringCase));
        }

        [Test]
        public void ToCaseNone()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.AreEqual("NoChange", "NoChange".ToCase(StringCase.None));
            Assert.AreEqual("test_value", "test_value".ToCase(StringCase.None));
            Assert.AreEqual("MixedCase", "MixedCase".ToCase(StringCase.None));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void ToCaseWithNullInput()
        {
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.PascalCase));
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.CamelCase));
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.SnakeCase));
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.KebabCase));
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.TitleCase));
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.LowerCase));
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.UpperCase));
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.LowerInvariant));
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.UpperInvariant));
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.AreEqual(string.Empty, ((string)null).ToCase(StringCase.None));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void ToCaseWithEmptyInput()
        {
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.PascalCase));
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.CamelCase));
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.SnakeCase));
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.KebabCase));
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.TitleCase));
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.LowerCase));
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.UpperCase));
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.LowerInvariant));
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.UpperInvariant));
#pragma warning disable CS0618 // Type or member is obsolete
            Assert.AreEqual(string.Empty, string.Empty.ToCase(StringCase.None));
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Test]
        public void ToCaseWithSingleCharacter()
        {
            Assert.AreEqual("A", "a".ToCase(StringCase.PascalCase));
            Assert.AreEqual("a", "A".ToCase(StringCase.CamelCase));
            Assert.AreEqual("a", "A".ToCase(StringCase.SnakeCase));
            Assert.AreEqual("a", "A".ToCase(StringCase.KebabCase));
            Assert.AreEqual("A", "a".ToCase(StringCase.TitleCase));
            Assert.AreEqual("a", "A".ToCase(StringCase.LowerCase));
            Assert.AreEqual("A", "a".ToCase(StringCase.UpperCase));
        }

        [Test]
        public void ToCaseWithNumbers()
        {
            Assert.AreEqual("Test123", "test123".ToCase(StringCase.PascalCase));
            Assert.AreEqual("test123", "Test123".ToCase(StringCase.CamelCase));
            Assert.AreEqual("test_123_value", "test123Value".ToCase(StringCase.SnakeCase));
            Assert.AreEqual("test-123-value", "test123Value".ToCase(StringCase.KebabCase));
        }

        [Test]
        public void ToCaseWithSpecialCharacters()
        {
            Assert.AreEqual("HelloWorld", "hello_world".ToCase(StringCase.PascalCase));
            Assert.AreEqual("helloWorld", "hello_world".ToCase(StringCase.CamelCase));
            Assert.AreEqual("hello_world", "hello-world".ToCase(StringCase.SnakeCase));
            Assert.AreEqual("hello-world", "hello_world".ToCase(StringCase.KebabCase));
        }

        [Test]
        public void ToCaseWithMultipleWords()
        {
            string input = "this is a test string";
            Assert.AreEqual("ThisIsATestString", input.ToCase(StringCase.PascalCase));
            Assert.AreEqual("thisIsATestString", input.ToCase(StringCase.CamelCase));
            Assert.AreEqual("this_is_a_test_string", input.ToCase(StringCase.SnakeCase));
            Assert.AreEqual("this-is-a-test-string", input.ToCase(StringCase.KebabCase));
            Assert.AreEqual("This Is A Test String", input.ToCase(StringCase.TitleCase));
        }

        [Test]
        public void ToCaseWithMixedInput()
        {
            string input = "Some_MIXED-Input String";
            Assert.AreEqual("SomeMixedInputString", input.ToCase(StringCase.PascalCase));
            Assert.AreEqual("someMixedInputString", input.ToCase(StringCase.CamelCase));
            Assert.AreEqual("some_mixed_input_string", input.ToCase(StringCase.SnakeCase));
            Assert.AreEqual("some-mixed-input-string", input.ToCase(StringCase.KebabCase));
        }

        [Test]
        public void ToCaseWithUnicodeCharacters()
        {
            Assert.AreEqual("CAFÉ", "café".ToCase(StringCase.UpperCase));
            Assert.AreEqual("café", "CAFÉ".ToCase(StringCase.LowerCase));
            Assert.AreEqual("CAFÉ", "café".ToCase(StringCase.UpperInvariant));
            Assert.AreEqual("café", "CAFÉ".ToCase(StringCase.LowerInvariant));
        }

        [Test]
        public void ToCaseWithConsecutiveUppercase()
        {
            Assert.AreEqual("XmlHttpRequest", "XMLHttpRequest".ToCase(StringCase.PascalCase));
            Assert.AreEqual("xmlHttpRequest", "XMLHttpRequest".ToCase(StringCase.CamelCase));
            Assert.AreEqual("xml_http_request", "XMLHttpRequest".ToCase(StringCase.SnakeCase));
            Assert.AreEqual("xml-http-request", "XMLHttpRequest".ToCase(StringCase.KebabCase));
        }

        [Test]
        public void ToCasePreservesCorrectFormat()
        {
            Assert.AreEqual("PascalCase", "PascalCase".ToCase(StringCase.PascalCase));
            Assert.AreEqual("camelCase", "camelCase".ToCase(StringCase.CamelCase));
            Assert.AreEqual("snake_case", "snake_case".ToCase(StringCase.SnakeCase));
        }

        [Test]
        public void ToCaseWithWhitespace()
        {
            Assert.AreEqual("TestValue", "  test  value  ".ToCase(StringCase.PascalCase));
            Assert.AreEqual("testValue", "  test  value  ".ToCase(StringCase.CamelCase));
            Assert.AreEqual("test_value", "  test  value  ".ToCase(StringCase.SnakeCase));
            Assert.AreEqual("test-value", "  test  value  ".ToCase(StringCase.KebabCase));
        }

        [Test]
        public void ToCaseWithUnderscoresAndDashes()
        {
            Assert.AreEqual("TestValue", "test___value".ToCase(StringCase.PascalCase));
            Assert.AreEqual("testValue", "test---value".ToCase(StringCase.CamelCase));
            Assert.AreEqual("test_value", "test---value".ToCase(StringCase.SnakeCase));
            Assert.AreEqual("test-value", "test___value".ToCase(StringCase.KebabCase));
        }

        [Test]
        public void ToCaseAllEnumValues()
        {
            string input = "testValue";

            Dictionary<StringCase, string> expectations = new()
            {
                { StringCase.PascalCase, "TestValue" },
                { StringCase.CamelCase, "testValue" },
                { StringCase.SnakeCase, "test_value" },
                { StringCase.KebabCase, "test-value" },
                { StringCase.TitleCase, "TestValue" },
                { StringCase.LowerCase, "testvalue" },
                { StringCase.UpperCase, "TESTVALUE" },
                { StringCase.LowerInvariant, "testvalue" },
                { StringCase.UpperInvariant, "TESTVALUE" },
#pragma warning disable CS0618 // Type or member is obsolete
                { StringCase.None, "testValue" },
#pragma warning restore CS0618 // Type or member is obsolete
            };

            StringCase[] enumValues = Enum.GetValues(typeof(StringCase))
                .Cast<StringCase>()
                .ToArray();

            CollectionAssert.AreEquivalent(
                enumValues,
                expectations.Keys,
                "Update ToCaseAllEnumValues test expectations when new StringCase values are added."
            );

            foreach (StringCase stringCase in enumValues)
            {
                string actual = input.ToCase(stringCase);

                if (!expectations.TryGetValue(stringCase, out string expected))
                {
                    Assert.Fail(
                        $"Missing expectation for StringCase.{stringCase}. Update ToCaseAllEnumValues test."
                    );
                }

                Assert.That(
                    actual,
                    Is.EqualTo(expected),
                    $"StringCase.{stringCase} on \"{input}\" produced \"{actual}\" instead of \"{expected}\"."
                );
            }
        }

        [Test]
        public void ToCaseEdgeCasesSingleUnderscore()
        {
            Assert.AreEqual(string.Empty, "_".ToCase(StringCase.PascalCase));
            Assert.AreEqual(string.Empty, "_".ToCase(StringCase.CamelCase));
            Assert.AreEqual(string.Empty, "_".ToCase(StringCase.SnakeCase));
            Assert.AreEqual(string.Empty, "_".ToCase(StringCase.KebabCase));
        }

        [Test]
        public void ToCaseEdgeCasesSingleDash()
        {
            Assert.AreEqual(string.Empty, "-".ToCase(StringCase.PascalCase));
            Assert.AreEqual(string.Empty, "-".ToCase(StringCase.CamelCase));
            Assert.AreEqual(string.Empty, "-".ToCase(StringCase.SnakeCase));
            Assert.AreEqual(string.Empty, "-".ToCase(StringCase.KebabCase));
        }

        [Test]
        public void ToCaseComplex()
        {
            string input = "get_HTTPResponse_from_URL";
            Assert.AreEqual("GetHttpResponseFromUrl", input.ToCase(StringCase.PascalCase));
            Assert.AreEqual("getHttpResponseFromUrl", input.ToCase(StringCase.CamelCase));
            Assert.AreEqual("get_http_response_from_url", input.ToCase(StringCase.SnakeCase));
            Assert.AreEqual("get-http-response-from-url", input.ToCase(StringCase.KebabCase));
        }

        [Test]
        public void ToCaseWithApostrophe()
        {
            Assert.AreEqual("DontStop", "don't stop".ToCase(StringCase.PascalCase));
            Assert.AreEqual("dontStop", "don't stop".ToCase(StringCase.CamelCase));
        }

        [Test]
        public void ToCaseWithQuotes()
        {
            Assert.AreEqual("HelloWorld", "hello\"world".ToCase(StringCase.PascalCase));
            Assert.AreEqual("helloWorld", "hello\"world".ToCase(StringCase.CamelCase));
        }

        [Test]
        public void ToCasePerformanceMultipleCalls()
        {
            string input = "TestValue";
            for (int i = 0; i < 1000; i++)
            {
                _ = input.ToCase(StringCase.PascalCase);
                _ = input.ToCase(StringCase.CamelCase);
                _ = input.ToCase(StringCase.SnakeCase);
                _ = input.ToCase(StringCase.KebabCase);
            }
        }

        [Test]
        public void ToCaseWithInvalidEnumValue()
        {
            Assert.AreEqual("testValue", "testValue".ToCase((StringCase)999));
        }

        [Test]
        public void ToCaseTitleCaseComplexInput()
        {
            Assert.AreEqual(
                "The Quick Brown Fox",
                "the_quick_brown_fox".ToCase(StringCase.TitleCase)
            );
            Assert.AreEqual("Hello World", "HELLO-WORLD".ToCase(StringCase.TitleCase));
        }
    }
}
