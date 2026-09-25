// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer;

    [TestFixture]
    public sealed class UnityMethodAnalyzerReportExportTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(
                Path.GetTempPath(),
                nameof(UnityMethodAnalyzerReportExportTests),
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void ExplicitExportsPreserveFindingsCoverageAndSummaries()
        {
            AnalyzerIssue[] issues =
            {
                new AnalyzerIssue(
                    "Assets/Scripts/Player.cs",
                    "Game.Player",
                    "Update",
                    "WUH015",
                    "warning WUH015: invalid callback",
                    IssueSeverity.Medium,
                    "Fix callback signature.",
                    27,
                    IssueCategory.UnityLifecycle,
                    derivedMethodSignature: "Game.Player.Update(float)"
                ),
            };
            string markdownPath = Path.Combine(_directory, "report.md");
            string jsonPath = Path.Combine(_directory, "report.json");

            Assert.That(
                UnityMethodAnalyzerReportExportAPI.TryExportMarkdown(
                    markdownPath,
                    issues,
                    "Captured 1/1 assemblies.",
                    out string markdownError
                ),
                Is.True,
                markdownError
            );
            Assert.That(
                UnityMethodAnalyzerReportExportAPI.TryExportJson(
                    jsonPath,
                    issues,
                    "Captured 1/1 assemblies.",
                    out string jsonError
                ),
                Is.True,
                jsonError
            );

            string markdown = File.ReadAllText(markdownPath);
            Assert.That(markdown, Does.Contain("**Compiler coverage:** Captured 1/1 assemblies."));
            Assert.That(markdown, Does.Contain("**Total Issues Found:** 1"));
            Assert.That(markdown, Does.Contain("| 🟡 Medium | 1 |"));
            Assert.That(markdown, Does.Contain("Assets/Scripts/Player.cs"));
            Assert.That(markdown, Does.Contain("Line 27: `Game.Player.Update` - WUH015"));
            Assert.That(markdown, Does.Contain("warning WUH015: invalid callback"));
            string json = File.ReadAllText(jsonPath);
            Assert.That(json, Does.Contain("\"coverageStatus\": \"Captured 1/1 assemblies.\""));
            Assert.That(json, Does.Contain("\"totalIssues\": 1"));
            Assert.That(json, Does.Contain("\"medium\": 1"));
            Assert.That(json, Does.Contain("\"unityLifecycle\": 1"));
            Assert.That(json, Does.Contain("\"issueType\": \"WUH015\""));
            Assert.That(json, Does.Contain("\"lineNumber\": 27"));
            Assert.That(
                json,
                Does.Contain("\"derivedMethodSignature\": \"Game.Player.Update(float)\"")
            );
        }

        [Test]
        public void InvalidInputsAndFailedStagingPreserveExistingReport()
        {
            string path = Path.Combine(_directory, "report.md");
            const string original = "Previous complete report";
            File.WriteAllText(path, original);
            AnalyzerIssue[] issues = Array.Empty<AnalyzerIssue>();

            Assert.That(
                UnityMethodAnalyzerReportExportAPI.TryExportMarkdown(
                    path,
                    null,
                    "Captured.",
                    out string nullError
                ),
                Is.False
            );
            Assert.That(nullError, Is.Not.Empty);
            Assert.That(
                UnityMethodAnalyzerReportExportAPI.TryExportJson(
                    path,
                    issues,
                    null,
                    out string coverageError
                ),
                Is.False
            );
            Assert.That(coverageError, Is.Not.Empty);
            Assert.That(
                UnityMethodAnalyzerReportExportAPI.TryExportJson(
                    "",
                    issues,
                    "Captured.",
                    out string pathError
                ),
                Is.False
            );
            Assert.That(pathError, Is.Not.Empty);
            Assert.That(
                UnityMethodAnalyzerReportExportAPI.TryExportMarkdown(
                    path,
                    new AnalyzerIssue[] { null },
                    "Captured.",
                    out string issueError
                ),
                Is.False
            );
            Assert.That(issueError, Is.Not.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));

            Directory.CreateDirectory(path + DurableFile.TemporarySuffix);
            Assert.That(
                UnityMethodAnalyzerReportExportAPI.TryExportMarkdown(
                    path,
                    issues,
                    "Captured.",
                    out string writeError
                ),
                Is.False
            );
            Assert.That(writeError, Is.Not.Empty);
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
        }
    }
}
