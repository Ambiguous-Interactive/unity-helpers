// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System.Collections.Generic;
    using System.Xml;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class ValidationWorkspaceReportTests : CommonTestBase
    {
        [Test]
        public void SuppressingPreservesUnobservedEntriesAndComments()
        {
            string original = "# reviewed by team\nunknown|other|field\n";
            ValidationFinding finding = Finding(ValidationSeverity.Error);
            string changed = ValidationWindow.WithSuppression(original, finding, true);
            StringAssert.Contains(original, changed);
            ValidationSuppressions parsed = ValidationSuppressions.Parse(changed);
            Assert.AreEqual(2, parsed.Count);
            Assert.IsTrue(parsed.IsSuppressed(in finding));
            Assert.AreEqual(changed, ValidationWindow.WithSuppression(changed, finding, true));
            string restored = ValidationWindow.WithSuppression(changed, finding, false);
            CollectionAssert.AreEqual(
                new[] { "unknown|other|field" },
                ValidationSuppressions.Parse(restored).Ids
            );
        }

        [TestCase(1)]
        [TestCase(4)]
        public void JUnitEscapesDataAndKeepsSuppressedFindings(int workers)
        {
            ValidationFinding finding = Finding(ValidationSeverity.Error);
            ValidationRun run = Run(finding);
            XmlDocument document = new XmlDocument();
            document.LoadXml(
                ValidationWorkspaceReport.ToJUnit(
                    run,
                    ValidationSuppressions.Empty,
                    ValidationSeverity.Error,
                    workers
                )
            );
            Assert.AreEqual("1", document.DocumentElement.GetAttribute("failures"));
            Assert.AreEqual(
                finding.Message,
                document.SelectSingleNode("//failure").Attributes["message"].Value
            );
            document.LoadXml(
                ValidationWorkspaceReport.ToJUnit(
                    run,
                    ValidationSuppressions.Parse(finding.Id),
                    ValidationSeverity.Error,
                    workers
                )
            );
            Assert.AreEqual("0", document.DocumentElement.GetAttribute("failures"));
            Assert.AreEqual("1", document.DocumentElement.GetAttribute("skipped"));
            Assert.IsTrue(document.SelectSingleNode("//testcase/skipped") != null);
        }

        [Test]
        public void JUnitThresholdDoesNotTurnWarningsIntoErrors()
        {
            ValidationRun run = Run(Finding(ValidationSeverity.Warning));
            XmlDocument document = new XmlDocument();
            document.LoadXml(
                ValidationWorkspaceReport.ToJUnit(run, null, ValidationSeverity.Error, 2)
            );
            Assert.AreEqual("0", document.DocumentElement.GetAttribute("failures"));
            document.LoadXml(
                ValidationWorkspaceReport.ToJUnit(run, null, ValidationSeverity.Warning, 2)
            );
            Assert.AreEqual("1", document.DocumentElement.GetAttribute("failures"));
        }

        [Test]
        public void JUnitRefusesToRepresentUnexercisedRunAsSuccessfulCoverage()
        {
            ValidationRun run = new ValidationRun(new IValidationRule[0], new ValidationTarget[0]);
            XmlDocument document = new XmlDocument();
            document.LoadXml(
                ValidationWorkspaceReport.ToJUnit(run, null, ValidationSeverity.Error, 1)
            );
            Assert.AreEqual("1", document.DocumentElement.GetAttribute("errors"));
            Assert.IsTrue(document.SelectSingleNode("//error") != null);
        }

        private static ValidationFinding Finding(ValidationSeverity severity)
        {
            return new ValidationFinding(
                "project<&rule",
                severity,
                null,
                "guid",
                "Assets/A<&.asset",
                "field",
                "Message <tag> & \"quotes\""
            );
        }

        private static ValidationRun Run(ValidationFinding finding)
        {
            ValidationRun run = new ValidationRun(
                new[] { new ReportRule(finding) },
                new[]
                {
                    new ValidationTarget("guid", "Assets/Test.asset", typeof(ScriptableObject)),
                },
                _ => null
            );
            while (!run.Step(double.MaxValue)) { }
            return run;
        }

        private sealed class ReportRule : IValidationRule
        {
            private readonly ValidationFinding _finding;

            internal ReportRule(ValidationFinding finding)
            {
                _finding = finding;
            }

            public string RuleId => "test.report";
            public string DisplayName => "Report fixture";

            public bool AppliesTo(in ValidationTarget target) => true;

            public void Validate(
                in ValidationTarget target,
                Object asset,
                List<ValidationFinding> findings
            )
            {
                findings.Add(_finding);
            }
        }
    }
}
