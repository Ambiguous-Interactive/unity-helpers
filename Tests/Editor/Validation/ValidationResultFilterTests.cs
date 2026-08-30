// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Pins what the results window shows, which is the whole part of it that has a right answer.
    /// </summary>
    /// <remarks>
    /// A UI Toolkit window cannot be driven from an EditMode test, so the filtering, the counting
    /// and the wording were factored out of it deliberately. Asserting them here is the difference
    /// between a window that is tested and one that is merely written.
    /// </remarks>
    [TestFixture]
    public sealed class ValidationResultFilterTests : CommonTestBase
    {
        private const string FirstGuid = "00000000000000000000000000000001";

        [Test]
        public void NullFindingsFilterToNothingRatherThanThrowing()
        {
            Assert.IsEmpty(
                ValidationResultFilter.Apply(
                    null,
                    ValidationSeverity.Info,
                    null,
                    true,
                    ValidationSuppressions.Empty
                )
            );
        }

        [TestCase(ValidationSeverity.Info, 3)]
        [TestCase(ValidationSeverity.Warning, 2)]
        [TestCase(ValidationSeverity.Error, 1)]
        public void TheSeverityFloorKeepsThatLevelAndAbove(ValidationSeverity minimum, int expected)
        {
            // The enum is ordered so a numeric comparison is a severity comparison; this is what
            // asserts that ordering is load-bearing rather than incidental.
            List<ValidationFinding> all = new List<ValidationFinding>
            {
                Finding(ValidationSeverity.Info, "info", "counting things"),
                Finding(ValidationSeverity.Warning, "warn", "probably wrong"),
                Finding(ValidationSeverity.Error, "err", "definitely wrong"),
            };

            Assert.AreEqual(
                expected,
                ValidationResultFilter
                    .Apply(all, minimum, null, true, ValidationSuppressions.Empty)
                    .Count
            );
        }

        [TestCase("DEFINITELY", 1, TestName = "TheQueryMatchesTheMessageIgnoringCase")]
        [TestCase("Assets/", 3, TestName = "TheQueryMatchesTheAssetPath")]
        [TestCase("warn", 1, TestName = "TheQueryMatchesTheDiscriminator")]
        [TestCase("Rule", 3, TestName = "TheQueryMatchesTheRuleId")]
        [TestCase("   ", 3, TestName = "ABlankQueryMatchesEverything")]
        [TestCase("nothing at all", 0, TestName = "AQueryThatMatchesNothingKeepsNothing")]
        public void TheQuerySearchesEveryFieldAReaderCanSee(string query, int expected)
        {
            List<ValidationFinding> all = new List<ValidationFinding>
            {
                Finding(ValidationSeverity.Info, "info", "counting things"),
                Finding(ValidationSeverity.Warning, "warn", "probably wrong"),
                Finding(ValidationSeverity.Error, "err", "definitely wrong"),
            };

            Assert.AreEqual(
                expected,
                ValidationResultFilter
                    .Apply(all, ValidationSeverity.Info, query, true, ValidationSuppressions.Empty)
                    .Count
            );
        }

        /// <summary>
        /// A suppressed finding is kept and marked by default, and only hidden when asked.
        /// </summary>
        /// <remarks>
        /// The headless report keeps them for the same reason: a view that hid them would make a
        /// suppression file indistinguishable from a project with nothing wrong.
        /// </remarks>
        [Test]
        public void SuppressedFindingsAreHiddenOnlyWhenAsked()
        {
            ValidationFinding suppressed = Finding(ValidationSeverity.Error, "err", "hidden");
            List<ValidationFinding> all = new List<ValidationFinding>
            {
                suppressed,
                Finding(ValidationSeverity.Error, "other", "shown"),
            };
            ValidationSuppressions file = ValidationSuppressions.Parse(suppressed.Id);

            Assert.AreEqual(
                2,
                ValidationResultFilter.Apply(all, ValidationSeverity.Info, null, true, file).Count
            );
            Assert.AreEqual(
                1,
                ValidationResultFilter.Apply(all, ValidationSeverity.Info, null, false, file).Count
            );
        }

        [Test]
        public void CountingSortsFindingsIntoTheirSeverities()
        {
            List<ValidationFinding> all = new List<ValidationFinding>
            {
                Finding(ValidationSeverity.Info, "a", "one"),
                Finding(ValidationSeverity.Warning, "b", "two"),
                Finding(ValidationSeverity.Warning, "c", "three"),
                Finding(ValidationSeverity.Error, "d", "four"),
            };

            ValidationResultFilter.Count(all, out int errors, out int warnings, out int infos);

            Assert.AreEqual(1, errors);
            Assert.AreEqual(2, warnings);
            Assert.AreEqual(1, infos);
        }

        /// <summary>
        /// "Nothing checked yet" and "checked, and clean" have to read differently.
        /// </summary>
        /// <remarks>
        /// A window showing an empty list for both would report a project as healthy on the strength
        /// of never having looked at it, which is the shape #556 exists to refuse.
        /// </remarks>
        [Test]
        public void ANeverRunProjectIsNotDescribedAsAHealthyOne()
        {
            string never = ValidationResultFilter.Summarize(false, 0, null);
            string clean = ValidationResultFilter.Summarize(true, 12, null);

            Assert.AreNotEqual(never, clean);
            StringAssert.Contains("Nothing checked yet", never);
            StringAssert.Contains("12 assets checked", clean);
            StringAssert.Contains("no problems found", clean);
        }

        [Test]
        public void TheSummaryCountsEachSeverity()
        {
            List<ValidationFinding> all = new List<ValidationFinding>
            {
                Finding(ValidationSeverity.Error, "a", "one"),
                Finding(ValidationSeverity.Warning, "b", "two"),
                Finding(ValidationSeverity.Warning, "c", "three"),
            };

            string summary = ValidationResultFilter.Summarize(true, 1, all);

            StringAssert.Contains("1 asset checked", summary);
            StringAssert.Contains("1 error", summary);
            StringAssert.Contains("2 warnings", summary);
            StringAssert.Contains("0 infos", summary);
        }

        private static ValidationFinding Finding(
            ValidationSeverity severity,
            string discriminator,
            string message
        )
        {
            return new ValidationFinding(
                "SampleRule",
                severity,
                null,
                FirstGuid,
                "Assets/Sample.asset",
                discriminator,
                message
            );
        }
    }
}
