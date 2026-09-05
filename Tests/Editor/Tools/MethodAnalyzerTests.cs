// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.IO;
    using System.Threading;
    using NUnit.Framework;
    using UnityEditor.Compilation;
    using WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer;

    /// <summary>
    /// Checks compiler-report filtering and coverage without launching compilation.
    /// </summary>
    [TestFixture]
    public sealed class MethodAnalyzerTests
    {
        [TestCase("WUH015", IssueCategory.UnityLifecycle)]
        [TestCase("WUH016", IssueCategory.UnityInheritance)]
        [TestCase("CS0114", IssueCategory.GeneralInheritance)]
        [TestCase("CS0108", IssueCategory.GeneralInheritance)]
        [TestCase("CS0507", IssueCategory.GeneralInheritance)]
        [TestCase("CS0508", IssueCategory.GeneralInheritance)]
        [TestCase("CS0115", IssueCategory.GeneralInheritance)]
        public void CompilerDiagnosticsPreserveNavigationAndMeaning(
            string code,
            IssueCategory category
        )
        {
            CompilerMessage message = new()
            {
                file = "Assets/Scripts/Player.cs",
                line = 27,
                column = 12,
                message =
                    $"warning {code}: 'Game.Player.Update()' hides Unity callback 'Game.Base.Update()'.",
                type = CompilerMessageType.Warning,
            };
            Assert.That(MethodAnalyzer.TryCreateIssue(message, out AnalyzerIssue issue), Is.True);
            Assert.That(issue.IssueType, Is.EqualTo(code));
            Assert.That(issue.ClassName, Is.EqualTo("Game.Player"));
            Assert.That(issue.MethodName, Is.EqualTo("Update"));
            Assert.That(issue.LineNumber, Is.EqualTo(27));
            Assert.That(issue.Description, Is.EqualTo(message.message));
            Assert.That(issue.Category, Is.EqualTo(category));
            Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Medium));
        }

        [TestCase("warning WUH015: invalid callback", true)]
        [TestCase("warning WUH0150: another rule", false)]
        [TestCase("warning XWUH015: another rule", false)]
        [TestCase("warning CS0168: unused variable", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void OnlySupportedDiagnosticIdentifiersAreReported(string text, bool expected)
        {
            Assert.That(
                MethodAnalyzer.TryCreateIssue(new CompilerMessage { message = text }, out _),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void DirectoryFiltersDoNotIncludeSiblingsOrDuplicateRows()
        {
            string root = Path.GetFullPath(Path.GetTempPath());
            CompilerMessage included = Message("Assets/Scripts/Player.cs");
            CompilerMessage sibling = Message("Assets/ScriptsExtra/Player.cs");
            MethodAnalyzer analyzer = new(
                new[] { included, sibling },
                "Captured test compilation."
            );
            analyzer.Refresh(root, new[] { "Assets/Scripts", "Assets/Scripts" });
            Assert.That(analyzer.Issues.Count, Is.EqualTo(1));
            Assert.That(
                analyzer.Issues[0].FilePath,
                Is.EqualTo(Path.Combine(root, "Assets", "Scripts", "Player.cs"))
            );
            Assert.That(analyzer.Status, Is.EqualTo("Captured test compilation."));
            analyzer.Refresh(root, new[] { "Assets/Other" });
            Assert.That(analyzer.Issues, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EmptyDirectorySelectionsNeverClaimCoverage(bool includeWhitespace)
        {
            MethodAnalyzer analyzer = new(
                Array.Empty<CompilerMessage>(),
                "All assemblies captured."
            );
            analyzer.Refresh(
                Path.GetTempPath(),
                includeWhitespace ? new[] { " ", "" } : Array.Empty<string>()
            );
            Assert.That(analyzer.Issues, Is.Empty);
            Assert.That(analyzer.Status, Does.Contain("coverage is unverified"));
        }

        [Test]
        public void SourceOutsideCompilationIsNeverPresentedAsAnalyzed()
        {
            MethodAnalyzer analyzer = new(
                Array.Empty<CompilerMessage>(),
                "No compiler coverage captured."
            );
            analyzer.Refresh(Path.GetTempPath(), new[] { "." });
            Assert.That(analyzer.Issues, Is.Empty);
            Assert.That(analyzer.Status, Does.Contain("No compiler coverage"));
        }

        [TestCase(0, 10, false, false, "No compiler coverage")]
        [TestCase(3, 10, false, false, "Partial coverage")]
        [TestCase(10, 10, true, false, "running")]
        [TestCase(10, 10, false, true, "errors can prevent")]
        [TestCase(10, 10, false, false, "Player-only code")]
        public void CoverageNeverTreatsUnobservedOrFailedCompilationAsClean(
            int captured,
            int expected,
            bool compiling,
            bool errors,
            string expectedStatus
        )
        {
            Assert.That(
                UnityMethodCompilerReport.DescribeCoverage(captured, expected, compiling, errors),
                Does.Contain(expectedStatus)
            );
        }

        [Test]
        public void CancelledRefreshDoesNotReplaceTheCurrentReport()
        {
            MethodAnalyzer analyzer = new(new[] { Message("Assets/Player.cs") }, "Captured.");
            analyzer.Refresh(Path.GetTempPath(), new[] { "Assets" });
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.That(
                analyzer
                    .AnalyzeAsync(
                        Path.GetTempPath(),
                        new[] { "Other" },
                        cancellationToken: cancellation.Token
                    )
                    .IsCanceled,
                Is.True
            );
            Assert.That(analyzer.Issues.Count, Is.EqualTo(1));
        }

        [Test]
        public void CompilerErrorsRemainCriticalAndBaseSignaturesArePreserved()
        {
            CompilerMessage message = Message("Assets/Player.cs");
            message.type = CompilerMessageType.Error;
            message.message = "error WUH016: 'Game.Player.Start()' hides 'Game.Base.Start()'.";
            Assert.That(MethodAnalyzer.TryCreateIssue(message, out AnalyzerIssue issue), Is.True);
            Assert.That(issue.Severity, Is.EqualTo(IssueSeverity.Critical));
            Assert.That(issue.BaseMethodSignature, Is.EqualTo("Game.Base.Start()"));
        }

        private static CompilerMessage Message(string file)
        {
            return new CompilerMessage
            {
                file = file,
                line = 4,
                message =
                    "warning WUH015: 'Game.Player.Update()' has an invalid callback signature.",
                type = CompilerMessageType.Warning,
            };
        }
    }
}
