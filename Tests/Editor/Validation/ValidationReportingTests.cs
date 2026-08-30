// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Pins the headless half of the validation engine: what a suppression file means, what the
    /// JSON report says, and what makes a batch run exit non-zero.
    /// </summary>
    /// <remarks>
    /// Everything here is driven from constructed findings and an injected loader rather than from
    /// the asset database, so the assertions are about the reporting contract rather than about
    /// whatever assets the test project happens to hold.
    /// </remarks>
    [TestFixture]
    public sealed class ValidationReportingTests : CommonTestBase
    {
        private const string FirstGuid = "00000000000000000000000000000001";
        private const string SecondGuid = "00000000000000000000000000000002";

        [Test]
        public void ParsingIgnoresBlankLinesCommentsAndDuplicates()
        {
            ValidationSuppressions suppressions = ValidationSuppressions.Parse(
                "# a comment\n\n  Rule|"
                    + FirstGuid
                    + "|\n"
                    + "   # an indented comment\n"
                    + "Rule|"
                    + FirstGuid
                    + "|\n"
            );

            CollectionAssert.AreEqual(new[] { "Rule|" + FirstGuid + "|" }, suppressions.Ids);
            Assert.AreEqual(1, suppressions.Count);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("# nothing but a comment\n")]
        public void AFileWithNoEntriesSuppressesNothing(string text)
        {
            ValidationSuppressions suppressions = ValidationSuppressions.Parse(text);

            Assert.AreEqual(0, suppressions.Count);
            Assert.IsFalse(suppressions.IsSuppressed(Finding("Rule", FirstGuid, null)));
        }

        [Test]
        public void SuppressionSurvivesAMoveAndAReword()
        {
            // The identity excludes the path and the message precisely so this holds. A suppression
            // that came back the moment somebody moved an asset would be worse than none, because
            // the reader would believe the decision had been made.
            ValidationSuppressions suppressions = ValidationSuppressions.Parse(
                ValidationSuppressions.Render(
                    new List<ValidationFinding>
                    {
                        Finding("Rule", FirstGuid, null, "Assets/Old.asset", "the old wording"),
                    }
                )
            );

            Assert.IsTrue(
                suppressions.IsSuppressed(
                    Finding("Rule", FirstGuid, null, "Assets/New/Moved.asset", "reworded entirely")
                )
            );
        }

        [Test]
        public void SuppressionDoesNotCrossRulesAssetsOrDiscriminators()
        {
            ValidationSuppressions suppressions = ValidationSuppressions.Parse(
                ValidationSuppressions.Render(
                    new List<ValidationFinding> { Finding("Rule", FirstGuid, "field") }
                )
            );

            Assert.IsTrue(suppressions.IsSuppressed(Finding("Rule", FirstGuid, "field")));
            Assert.IsFalse(suppressions.IsSuppressed(Finding("Other", FirstGuid, "field")));
            Assert.IsFalse(suppressions.IsSuppressed(Finding("Rule", SecondGuid, "field")));
            Assert.IsFalse(suppressions.IsSuppressed(Finding("Rule", FirstGuid, "otherField")));
        }

        [Test]
        public void ARenderedFileNamesTheAssetAndMessageForAReviewer()
        {
            // A rule name and a GUID tell a reviewer nothing about what is being switched off, and
            // the file exists to be reviewed. The comment is not decoration.
            string rendered = ValidationSuppressions.Render(
                new List<ValidationFinding>
                {
                    Finding("Rule", FirstGuid, null, "Assets/Audio/Theme.wav", "not streaming"),
                }
            );

            StringAssert.Contains("Assets/Audio/Theme.wav", rendered);
            StringAssert.Contains("not streaming", rendered);
            Assert.AreEqual(1, ValidationSuppressions.Parse(rendered).Count);
        }

        [Test]
        public void ARenderedFileFlattensAMultiLineMessageOntoItsComment()
        {
            // A message carrying a newline would otherwise put its own second line into the file as
            // an entry, which then suppresses nothing and reads as a decision somebody made.
            string rendered = ValidationSuppressions.Render(
                new List<ValidationFinding>
                {
                    Finding("Rule", FirstGuid, null, "Assets/A.asset", "first\nsecond"),
                }
            );

            CollectionAssert.AreEqual(
                new[] { "Rule|" + FirstGuid + "|" },
                ValidationSuppressions.Parse(rendered).Ids
            );
        }

        [Test]
        public void AnEntryThatMatchesNothingIsReported()
        {
            // A suppression that outlives its finding reads as a considered decision and is really
            // a line nobody has looked at, so the run says so rather than letting the file grow.
            ValidationSuppressions suppressions = ValidationSuppressions.Parse(
                "Rule|" + FirstGuid + "|\nGone|" + SecondGuid + "|\nnot even an id\n"
            );

            CollectionAssert.AreEqual(
                new[] { "Gone|" + SecondGuid + "|", "not even an id" },
                suppressions.UnusedIn(
                    new List<ValidationFinding> { Finding("Rule", FirstGuid, null) }
                )
            );
        }

        [Test]
        public void TheReportKeepsASuppressedFindingAndMarksIt()
        {
            // Dropping it would make a project with a suppression file indistinguishable from one
            // with nothing wrong, which is the difference a reviewer needs to see.
            ValidationRun run = RunOver(
                Finding("Rule", FirstGuid, null, "Assets/A.asset", "silenced"),
                Finding("Rule", SecondGuid, null, "Assets/B.asset", "loud")
            );
            ValidationSuppressions suppressions = ValidationSuppressions.Parse(
                "Rule|" + FirstGuid + "|"
            );

            ValidationReport.Document document = Read(ValidationReport.ToJson(run, suppressions));

            Assert.AreEqual(ValidationReport.SchemaVersion, document.schemaVersion);
            Assert.AreEqual(1, document.unsuppressedCount);
            Assert.AreEqual(2, document.findings.Count);
            Assert.IsTrue(
                document.findings.Exists(record =>
                    record.suppressed && record.message == "silenced"
                )
            );
            Assert.IsTrue(
                document.findings.Exists(record => !record.suppressed && record.message == "loud")
            );
        }

        [Test]
        public void TheReportSurvivesANullRunAndNullSuppressions()
        {
            // The batch path renders whatever it got. A report generator that threw on an empty
            // project would fail the build for the one state that is unambiguously fine.
            ValidationReport.Document document = Read(ValidationReport.ToJson(null, null));

            Assert.AreEqual(0, document.assetsConsidered);
            Assert.AreEqual(0, document.unsuppressedCount);
            Assert.IsEmpty(document.findings);
            Assert.IsEmpty(document.failures);
        }

        [Test]
        public void TheReportEscapesAMessageThatWouldBreakTheDocument()
        {
            // Rendered through JsonUtility precisely so this is Unity's problem rather than a
            // hand-rolled writer's, and asserted so a later "simplification" cannot take it away.
            ValidationRun run = RunOver(
                Finding("Rule", FirstGuid, null, "Assets/A.asset", "he said \"stop\"\nthen \\left")
            );

            // Round-tripped rather than pattern-matched: a document that reads back with the exact
            // message is the property, and a check for a backslash would pass on a document no
            // reader could parse.
            ValidationReport.Document document = Read(
                ValidationReport.ToJson(run, ValidationSuppressions.Empty)
            );

            Assert.AreEqual(1, document.findings.Count);
            Assert.AreEqual("he said \"stop\"\nthen \\left", document.findings[0].message);
        }

        [TestCase(ValidationSeverity.Info, ValidationSeverity.Warning, false)]
        [TestCase(ValidationSeverity.Warning, ValidationSeverity.Warning, true)]
        [TestCase(ValidationSeverity.Error, ValidationSeverity.Warning, true)]
        [TestCase(ValidationSeverity.Error, ValidationSeverity.Error, true)]
        [TestCase(ValidationSeverity.Warning, ValidationSeverity.Error, false)]
        public void OnlyFindingsAtOrAboveTheThresholdBlock(
            ValidationSeverity found,
            ValidationSeverity threshold,
            bool expected
        )
        {
            ValidationRun run = RunOver(
                Finding("Rule", FirstGuid, null, "Assets/A.asset", "message", found)
            );

            Assert.AreEqual(
                expected,
                ValidationReport.HasBlockingResults(run, ValidationSuppressions.Empty, threshold)
            );
        }

        [Test]
        public void ASuppressedFindingDoesNotBlock()
        {
            ValidationRun run = RunOver(
                Finding("Rule", FirstGuid, null, "Assets/A.asset", "message")
            );

            Assert.IsFalse(
                ValidationReport.HasBlockingResults(
                    run,
                    ValidationSuppressions.Parse("Rule|" + FirstGuid + "|"),
                    ValidationSeverity.Info
                )
            );
        }

        [Test]
        public void ARuleThatThrewBlocksWhateverTheThresholdIs()
        {
            // It produced no answer for that asset, which is not the same as answering "nothing
            // wrong". A build that passed on it would be reporting coverage the run does not have.
            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { new ThrowingRule() },
                new List<ValidationTarget>
                {
                    new ValidationTarget(FirstGuid, "Assets/A.asset", typeof(ScriptableObject)),
                },
                Never
            );
            while (!run.Step(double.MaxValue)) { }

            Assert.IsEmpty(run.Findings);
            Assert.AreEqual(1, run.Failures.Count);
            Assert.IsTrue(
                ValidationReport.HasBlockingResults(
                    run,
                    ValidationSuppressions.Empty,
                    ValidationSeverity.Error
                )
            );
            ValidationReport.Document document = Read(ValidationReport.ToJson(run, null));
            Assert.AreEqual(1, document.failures.Count);
            Assert.IsFalse(document.failures[0].loadFailure, "a rule threw, not the loader");
            Assert.AreEqual("Tests.Throwing", document.failures[0].ruleId);
        }

        [TestCase(null, ValidationSeverity.Error)]
        [TestCase("", ValidationSeverity.Error)]
        [TestCase("nonsense", ValidationSeverity.Error)]
        [TestCase("warning", ValidationSeverity.Warning)]
        [TestCase("WARNING", ValidationSeverity.Warning)]
        [TestCase("Info", ValidationSeverity.Info)]
        public void AnUnrecognizedThresholdFallsBackToTheStrictOne(
            string written,
            ValidationSeverity expected
        )
        {
            // A typo must not quietly turn the gate off, so the fallback is the strict end.
            Assert.AreEqual(
                expected,
                ValidationBatch.ParseSeverity(written, ValidationSeverity.Error)
            );
        }

        [Test]
        public void CommandLineValuesAreReadInOrderAndTolerateATrailingFlag()
        {
            string[] commandLine =
            {
                "Unity",
                ValidationBatch.FolderArgument,
                "Assets/A",
                ValidationBatch.FolderArgument,
                "Assets/B",
                ValidationBatch.OutputArgument,
                "out.json",
                ValidationBatch.SuppressionsArgument,
            };

            CollectionAssert.AreEqual(
                new[] { "Assets/A", "Assets/B" },
                ValidationBatch.ValuesOf(commandLine, ValidationBatch.FolderArgument)
            );
            Assert.AreEqual(
                "out.json",
                ValidationBatch.ValueOf(commandLine, ValidationBatch.OutputArgument)
            );
            Assert.IsTrue(
                ValidationBatch.ValueOf(commandLine, ValidationBatch.SuppressionsArgument) == null,
                "a flag with no value after it must not read past the end of the array"
            );
            Assert.IsTrue(ValidationBatch.ValueOf(null, ValidationBatch.OutputArgument) == null);
        }

        [Test]
        public void ARunThatWalkedNothingIsNotAPass()
        {
            // The same shape this repository refuses everywhere else: a gate that checked nothing
            // exits 0 unless something says so. A -validationFolder naming a renamed directory is
            // skipped silently by ValidationTargets.Enumerate, so this is reachable with nothing
            // looking wrong at the call site.
            CollectionAssert.IsEmpty(
                ValidationBatch.CoverageProblems(2, 17, null),
                "a run with rules and assets measured something"
            );

            Assert.AreEqual(
                1,
                ValidationBatch.CoverageProblems(2, 0, null).Count,
                "no assets is a run that proved nothing"
            );
            Assert.AreEqual(
                1,
                ValidationBatch.CoverageProblems(0, 17, null).Count,
                "no rules is a run that proved nothing"
            );
            Assert.AreEqual(
                2,
                ValidationBatch.CoverageProblems(0, 0, null).Count,
                "and both are reported, so fixing one does not hide the other"
            );
        }

        [Test]
        public void AnEmptyRunNamesTheFoldersItWasGiven()
        {
            // Without the folders in the message the reader cannot tell "the project is empty"
            // from "I typed the path wrong", which is the only actionable difference.
            string problem = ValidationBatch
                .CoverageProblems(1, 0, new List<string> { "Assets/Typo", "Assets/Audio" })
                .Find(entry => entry.Contains("no assets"));

            StringAssert.Contains("Assets/Typo", problem);
            StringAssert.Contains("Assets/Audio", problem);
            StringAssert.Contains(ValidationBatch.FolderArgument, problem);
        }

        [Test]
        public void EveryConstructibleRuleIsFoundInAStableOrder()
        {
            List<string> problems = new List<string>();

            List<IValidationRule> first = ValidationBatch.DiscoverRules(problems);
            List<IValidationRule> second = ValidationBatch.DiscoverRules(null);

            CollectionAssert.AreEqual(
                Names(first),
                Names(second),
                "TypeCache's order is not a property of the project, so discovery has to impose one"
            );
            Assert.IsTrue(
                first.Exists(rule =>
                    string.Equals(rule.RuleId, "Tests.Throwing", StringComparison.Ordinal)
                ),
                "this fixture's constructible rule has to be found, or the assertion is vacuous"
            );
        }

        [Test]
        public void ARuleWithNoParameterlessConstructorIsReportedRatherThanEndingTheRun()
        {
            // One rule that cannot be built must not hide every other rule's findings, and a silent
            // skip would report a clean project nobody had actually checked. ScriptedRule below
            // takes its findings as a constructor argument, so it is exactly that shape.
            List<string> problems = new List<string>();

            List<IValidationRule> rules = ValidationBatch.DiscoverRules(problems);

            Assert.IsTrue(
                problems.Exists(problem => problem.Contains(nameof(ScriptedRule))),
                "expected the unconstructible rule to be reported, got: "
                    + string.Join(" | ", problems)
            );
            Assert.IsTrue(
                rules.Exists(rule =>
                    string.Equals(rule.RuleId, "Tests.Throwing", StringComparison.Ordinal)
                ),
                "and its neighbours still have to be constructed"
            );
        }

        /// <summary>
        /// Reads a rendered report back, which is what makes the assertions about content rather
        /// than about how Unity happens to indent.
        /// </summary>
        /// <param name="json">The rendered document.</param>
        /// <returns>The parsed document; never <c>null</c>.</returns>
        private static ValidationReport.Document Read(string json)
        {
            ValidationReport.Document document = JsonUtility.FromJson<ValidationReport.Document>(
                json
            );
            Assert.IsTrue(
                document != null,
                "the report has to be a document a reader can parse: " + json
            );
            return document;
        }

        private static string[] Names(List<IValidationRule> rules)
        {
            List<string> names = new List<string>();
            for (int index = 0; index < rules.Count; index++)
            {
                names.Add(rules[index].GetType().FullName);
            }

            return names.ToArray();
        }

        private static ValidationFinding Finding(
            string ruleId,
            string guid,
            string discriminator,
            string path = "Assets/Asset.asset",
            string message = "message",
            ValidationSeverity severity = ValidationSeverity.Error
        )
        {
            return new ValidationFinding(
                ruleId,
                severity,
                null,
                guid,
                path,
                discriminator,
                message
            );
        }

        /// <summary>
        /// A finished run whose findings are exactly those given.
        /// </summary>
        /// <param name="findings">What the run should report.</param>
        /// <returns>The completed run.</returns>
        private static ValidationRun RunOver(params ValidationFinding[] findings)
        {
            List<ValidationTarget> targets = new List<ValidationTarget>
            {
                new ValidationTarget(FirstGuid, "Assets/Only.asset", typeof(ScriptableObject)),
            };
            ValidationRun run = new ValidationRun(
                new List<IValidationRule> { new ScriptedRule(findings) },
                targets,
                Never
            );
            while (!run.Step(double.MaxValue)) { }

            return run;
        }

        private static Object Never(ValidationTarget target)
        {
            return null;
        }

        /// <summary>A rule that reports whatever the fixture handed it, once.</summary>
        private sealed class ScriptedRule : IValidationRule
        {
            private readonly ValidationFinding[] _findings;

            internal ScriptedRule(ValidationFinding[] findings)
            {
                _findings = findings;
            }

            public string RuleId => "Tests.Scripted";

            public string DisplayName => "Scripted";

            public bool AppliesTo(in ValidationTarget target)
            {
                return true;
            }

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            )
            {
                findings.AddRange(_findings);
            }
        }

        /// <summary>A rule that throws, so a failure can be asserted without an asset.</summary>
        private sealed class ThrowingRule : IValidationRule
        {
            public string RuleId => "Tests.Throwing";

            public string DisplayName => "Throwing";

            public bool AppliesTo(in ValidationTarget target)
            {
                return true;
            }

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            )
            {
                throw new InvalidOperationException("rule failed");
            }
        }
    }
}
