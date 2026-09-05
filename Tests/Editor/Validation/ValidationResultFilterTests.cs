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

        /// <summary>
        /// The destination-taking overload answers exactly what the allocating one does.
        /// </summary>
        /// <remarks>
        /// The window refilters on every keystroke, and every one of those allocated a snapshot
        /// list and a filtered list. Two shapes of the same answer only help if they agree.
        /// </remarks>
        [Test]
        public void FilteringIntoADestinationMatchesTheAllocatingOverload()
        {
            List<ValidationFinding> findings = new List<ValidationFinding>
            {
                Finding(ValidationSeverity.Error, "a", "first"),
                Finding(ValidationSeverity.Warning, "b", "second"),
                Finding(ValidationSeverity.Info, "c", "third"),
            };

            List<ValidationFinding> destination = new List<ValidationFinding>
            {
                Finding(ValidationSeverity.Info, "stale", "must be cleared"),
            };
            ValidationResultFilter.Apply(
                findings,
                ValidationSeverity.Warning,
                null,
                true,
                null,
                destination
            );

            Assert.AreEqual(
                ValidationResultFilter.Apply(
                    findings,
                    ValidationSeverity.Warning,
                    null,
                    true,
                    null
                ),
                destination
            );

            ValidationResultFilter.Apply(
                null,
                ValidationSeverity.Info,
                null,
                true,
                null,
                destination
            );
            CollectionAssert.IsEmpty(
                destination,
                "a null source clears rather than keeping stale rows"
            );
        }

        /// <summary>
        /// A finding's identity is built once, and the default struct still reads as it always did.
        /// </summary>
        /// <remarks>
        /// It is read four times per rendered list row plus once per hash, per suppression test and
        /// per report line, and each of those used to build a fresh string.
        /// </remarks>
        [Test]
        public void TheFindingIdentityIsBuiltOnce()
        {
            ValidationFinding finding = Finding(ValidationSeverity.Info, "slot", "message");

            Assert.IsTrue(
                ReferenceEquals(finding.Id, finding.Id),
                "the identity must not be rebuilt per read"
            );
            Assert.AreEqual("SampleRule|" + FirstGuid + "|slot", finding.Id);
            Assert.AreEqual("||", default(ValidationFinding).Id);
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
