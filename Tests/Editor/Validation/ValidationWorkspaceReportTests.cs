// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Validation.Continuous;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class ValidationWorkspaceReportTests : CommonTestBase
    {
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

        private static ValidationRun Run(params ValidationFinding[] findings)
        {
            ReportRule[] rules = new ReportRule[findings.Length];
            for (int index = 0; index < findings.Length; index++)
            {
                rules[index] = new ReportRule(findings[index]);
            }
            ValidationRun run = new ValidationRun(
                rules,
                new[]
                {
                    new ValidationTarget("guid", "Assets/Test.asset", typeof(ScriptableObject)),
                },
                _ => null
            );
            while (!run.Step(double.MaxValue)) { }
            return run;
        }

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

        [Test]
        public void SuppressionCompareExchangePreservesBothWritersAndRefusesStaleUndo()
        {
            string path = Path.Combine(
                Application.temporaryCachePath,
                nameof(SuppressionCompareExchangePreservesBothWritersAndRefusesStaleUndo)
                    + Guid.NewGuid().ToString("N")
            );
            ValidationFinding first = Finding(ValidationSeverity.Error);
            ValidationFinding second = new ValidationFinding(
                "other rule",
                ValidationSeverity.Error,
                null,
                "other guid",
                "Assets/Other.asset",
                "other field",
                "Other message"
            );
            using ManualResetEventSlim firstRead = new ManualResetEventSlim(false);
            using ManualResetEventSlim secondSaved = new ManualResetEventSlim(false);
            try
            {
                Task<(bool applied, bool exchanged, Exception error)> staleWriter = Task.Run(() =>
                {
                    string snapshot = string.Empty;
                    firstRead.Set();
                    if (!secondSaved.Wait(TimeSpan.FromSeconds(10)))
                        return (false, false, new TimeoutException());
                    bool applied = DurableFile.TryCompareExchangeAllText(
                        path,
                        false,
                        snapshot,
                        ValidationWindow.WithSuppression(snapshot, first, true),
                        out bool exchanged,
                        out Exception error
                    );
                    return (applied, exchanged, error);
                });
                Assert.IsTrue(firstRead.Wait(TimeSpan.FromSeconds(10)));
                string secondSnapshot = string.Empty;
                string secondText = ValidationWindow.WithSuppression(secondSnapshot, second, true);
                Assert.IsTrue(
                    DurableFile.TryCompareExchangeAllText(
                        path,
                        false,
                        secondSnapshot,
                        secondText,
                        out bool secondExchanged,
                        out Exception secondError
                    ),
                    secondError?.ToString()
                );
                Assert.IsTrue(secondExchanged);
                secondSaved.Set();
                Assert.IsTrue(staleWriter.Wait(TimeSpan.FromSeconds(10)));
                (bool applied, bool exchanged, Exception error) stale = staleWriter.Result;
                Assert.IsTrue(stale.applied, stale.error?.ToString());
                Assert.IsFalse(stale.exchanged);
                Assert.AreEqual(secondText, File.ReadAllText(path));

                string latest = File.ReadAllText(path);
                string combined = ValidationWindow.WithSuppression(latest, first, true);
                Assert.IsTrue(
                    DurableFile.TryCompareExchangeAllText(
                        path,
                        true,
                        latest,
                        combined,
                        out bool retried,
                        out Exception retryError
                    ),
                    retryError?.ToString()
                );
                Assert.IsTrue(retried);
                ValidationSuppressions parsed = ValidationSuppressions.Parse(
                    File.ReadAllText(path)
                );
                Assert.IsTrue(parsed.IsSuppressed(in first));
                Assert.IsTrue(parsed.IsSuppressed(in second));

                Assert.IsTrue(
                    DurableFile.TryCompareExchangeAllText(
                        path,
                        true,
                        secondText,
                        string.Empty,
                        out bool undone,
                        out Exception undoError
                    ),
                    undoError?.ToString()
                );
                Assert.IsFalse(undone);
                Assert.AreEqual(combined, File.ReadAllText(path));
            }
            finally
            {
                secondSaved.Set();
                File.Delete(path);
            }
        }

        [Test]
        public void JUnitEscapesDataAndKeepsSuppressedFindings()
        {
            ValidationFinding finding = Finding(ValidationSeverity.Error);
            ValidationRun run = Run(finding);
            XmlDocument document = new XmlDocument();
            document.LoadXml(
                ValidationWorkspaceReport.ToJUnit(
                    run,
                    ValidationSuppressions.Empty,
                    ValidationSeverity.Error
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
                    ValidationSeverity.Error
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
                ValidationWorkspaceReport.ToJUnit(run, null, ValidationSeverity.Error)
            );
            Assert.AreEqual("0", document.DocumentElement.GetAttribute("failures"));
            document.LoadXml(
                ValidationWorkspaceReport.ToJUnit(run, null, ValidationSeverity.Warning)
            );
            Assert.AreEqual("1", document.DocumentElement.GetAttribute("failures"));
        }

        [Test]
        public void JUnitRefusesToRepresentUnexercisedRunAsSuccessfulCoverage()
        {
            ValidationRun run = new ValidationRun(new IValidationRule[0], new ValidationTarget[0]);
            XmlDocument document = new XmlDocument();
            document.LoadXml(
                ValidationWorkspaceReport.ToJUnit(run, null, ValidationSeverity.Error)
            );
            Assert.AreEqual("1", document.DocumentElement.GetAttribute("errors"));
            Assert.IsTrue(document.SelectSingleNode("//error") != null);
        }

        [Test]
        public void JUnitPreservesFindingOrder()
        {
            ValidationFinding first = new ValidationFinding(
                "first",
                ValidationSeverity.Info,
                null,
                "guid",
                "Assets/First.asset",
                "one",
                "First message"
            );
            ValidationFinding second = new ValidationFinding(
                "second",
                ValidationSeverity.Warning,
                null,
                "guid",
                "Assets/Second.asset",
                "two",
                "Second message"
            );
            XmlDocument document = new XmlDocument();

            document.LoadXml(
                ValidationWorkspaceReport.ToJUnit(
                    Run(first, second),
                    null,
                    ValidationSeverity.Error
                )
            );

            XmlNodeList cases = document.SelectNodes("//testcase");
            Assert.AreEqual(2, cases.Count);
            Assert.AreEqual("first", cases[0].Attributes["classname"].Value);
            Assert.AreEqual("second", cases[1].Attributes["classname"].Value);
        }

        private sealed class ReportRule : IValidationRule
        {
            public string RuleId => "test.report";
            public string DisplayName => "Report fixture";

            private readonly ValidationFinding _finding;

            internal ReportRule(ValidationFinding finding)
            {
                _finding = finding;
            }

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
