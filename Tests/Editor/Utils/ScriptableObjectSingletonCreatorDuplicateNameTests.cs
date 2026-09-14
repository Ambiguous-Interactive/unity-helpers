// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ScriptableObjectSingletonCreatorDuplicateNameTests
    {
        [TestCase(null, null, ExpectedResult = false, TestName = "BothNull")]
        [TestCase(null, "Folder", ExpectedResult = false, TestName = "ActualNameNull")]
        [TestCase("Folder 1", null, ExpectedResult = false, TestName = "DesiredNameNull")]
        [TestCase("", "", ExpectedResult = false, TestName = "BothEmpty")]
        [TestCase("", "Folder", ExpectedResult = false, TestName = "ActualNameEmpty")]
        [TestCase("Folder 1", "", ExpectedResult = false, TestName = "DesiredNameEmpty")]
        [TestCase("Folder", "Folder", ExpectedResult = false, TestName = "SameLengthExactMatch")]
        [TestCase("FolderX", "FolderY", ExpectedResult = false, TestName = "SameLengthDifferent")]
        [TestCase(
            "Folder 1",
            "Folder",
            ExpectedResult = true,
            TestName = "ValidDuplicateSingleDigit"
        )]
        [TestCase(
            "Folder 10",
            "Folder",
            ExpectedResult = true,
            TestName = "ValidDuplicateDoubleDigit"
        )]
        [TestCase(
            "Folder 999",
            "Folder",
            ExpectedResult = true,
            TestName = "ValidDuplicateTripleDigit"
        )]
        [TestCase(
            "Resources 1",
            "Resources",
            ExpectedResult = true,
            TestName = "ValidDuplicateResourcesFolder"
        )]
        [TestCase(
            "Resources 42",
            "Resources",
            ExpectedResult = true,
            TestName = "ValidDuplicateResourcesLargeNumber"
        )]
        [TestCase(
            "My Folder 5",
            "My Folder",
            ExpectedResult = true,
            TestName = "ValidDuplicateWithSpaceInName"
        )]
        [TestCase("Folder 0", "Folder", ExpectedResult = false, TestName = "ZeroNotValidDuplicate")]
        [TestCase(
            "Folder -1",
            "Folder",
            ExpectedResult = false,
            TestName = "NegativeNumberNotValidDuplicate"
        )]
        [TestCase(
            "Folder -10",
            "Folder",
            ExpectedResult = false,
            TestName = "NegativeDoubleDigitNotValidDuplicate"
        )]
        [TestCase(
            "FOLDER 1",
            "folder",
            ExpectedResult = true,
            TestName = "CaseInsensitiveUpperToLower"
        )]
        [TestCase(
            "folder 1",
            "FOLDER",
            ExpectedResult = true,
            TestName = "CaseInsensitiveLowerToUpper"
        )]
        [TestCase(
            "FoLdEr 1",
            "fOlDeR",
            ExpectedResult = true,
            TestName = "CaseInsensitiveMixedCase"
        )]
        [TestCase(
            "Resources 1",
            "RESOURCES",
            ExpectedResult = true,
            TestName = "CaseInsensitiveResources"
        )]
        [TestCase("Folder1", "Folder", ExpectedResult = false, TestName = "NoSpaceSeparator")]
        [TestCase(
            "Resources1",
            "Resources",
            ExpectedResult = false,
            TestName = "NoSpaceSeparatorResources"
        )]
        [TestCase(
            "Folder10",
            "Folder",
            ExpectedResult = false,
            TestName = "NoSpaceSeparatorDoubleDigit"
        )]
        [TestCase("Folder  1", "Folder", ExpectedResult = false, TestName = "DoubleSpaceSeparator")]
        [TestCase(
            "Folder  10",
            "Folder",
            ExpectedResult = false,
            TestName = "DoubleSpaceDoubleDigit"
        )]
        [TestCase(
            "Folder abc",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixLetters"
        )]
        [TestCase(
            "Folder 1a",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixMixed"
        )]
        [TestCase(
            "Folder a1",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixLetterFirst"
        )]
        [TestCase(
            "Folder 1.5",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixDecimal"
        )]
        [TestCase(
            "Folder 1 2",
            "Folder",
            ExpectedResult = false,
            TestName = "NonNumericSuffixMultipleNumbers"
        )]
        [TestCase("Fol", "Folder", ExpectedResult = false, TestName = "ActualShorterThanDesired")]
        [TestCase("F", "Folder", ExpectedResult = false, TestName = "ActualMuchShorterThanDesired")]
        [TestCase(
            "Folder ",
            "Folder",
            ExpectedResult = false,
            TestName = "ActualHasOnlyTrailingSpace"
        )]
        public bool IsNumberedDuplicateReturnsExpectedResult(string actualName, string desiredName)
        {
            return ScriptableObjectSingletonCreator.IsNumberedDuplicate(actualName, desiredName);
        }
    }
#endif
}
