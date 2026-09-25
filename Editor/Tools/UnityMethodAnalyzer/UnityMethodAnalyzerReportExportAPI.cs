// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>Writes Unity Method Analyzer reports to explicit paths.</summary>
    public static class UnityMethodAnalyzerReportExportAPI
    {
        /// <summary>Writes a Markdown report from explicit findings and coverage status.</summary>
        public static bool TryExportMarkdown(
            string path,
            IReadOnlyList<AnalyzerIssue> issues,
            string coverageStatus,
            out string error
        )
        {
            return TryExport(path, issues, coverageStatus, false, out error);
        }

        /// <summary>Writes a JSON report from explicit findings and coverage status.</summary>
        public static bool TryExportJson(
            string path,
            IReadOnlyList<AnalyzerIssue> issues,
            string coverageStatus,
            out string error
        )
        {
            return TryExport(path, issues, coverageStatus, true, out error);
        }

        internal static string RenderIssueJson(AnalyzerIssue issue)
        {
            return Serializer.JsonStringify(new IssueJsonModel(issue), pretty: true);
        }

        internal static string RenderMarkdown(
            IReadOnlyList<AnalyzerIssue> issues,
            string coverageStatus
        )
        {
            using PooledResource<StringBuilder> builderLease = Buffers.StringBuilder.Get(
                out StringBuilder sb
            );

            sb.AppendLine("# Unity Method Analysis Report");
            sb.AppendLine();
            sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"**Compiler coverage:** {coverageStatus}");
            sb.AppendLine();
            sb.AppendLine($"**Total Issues Found:** {issues.Count}");
            sb.AppendLine();

            sb.AppendLine("## Summary by Severity");
            sb.AppendLine();
            sb.AppendLine("| Severity | Count |");
            sb.AppendLine("|----------|-------|");
            sb.AppendLine(
                $"| 🔴 Critical | {issues.Count(i => i.Severity == IssueSeverity.Critical)} |"
            );
            sb.AppendLine($"| 🟠 High | {issues.Count(i => i.Severity == IssueSeverity.High)} |");
            sb.AppendLine(
                $"| 🟡 Medium | {issues.Count(i => i.Severity == IssueSeverity.Medium)} |"
            );
            sb.AppendLine($"| 🟢 Low | {issues.Count(i => i.Severity == IssueSeverity.Low)} |");
            sb.AppendLine($"| 🔵 Info | {issues.Count(i => i.Severity == IssueSeverity.Info)} |");
            sb.AppendLine();

            sb.AppendLine("## Summary by Category");
            sb.AppendLine();
            sb.AppendLine("| Category | Count |");
            sb.AppendLine("|----------|-------|");
            sb.AppendLine(
                $"| 🎮 Unity Lifecycle | {issues.Count(i => i.Category == IssueCategory.UnityLifecycle)} |"
            );
            sb.AppendLine(
                $"| 🔷 Unity Inheritance | {issues.Count(i => i.Category == IssueCategory.UnityInheritance)} |"
            );
            sb.AppendLine(
                $"| 📦 General Inheritance | {issues.Count(i => i.Category == IssueCategory.GeneralInheritance)} |"
            );
            sb.AppendLine();

            sb.AppendLine("## Detailed Issues");
            sb.AppendLine();

            IOrderedEnumerable<IGrouping<string, AnalyzerIssue>> issuesByFile = issues
                .OrderBy(i => (int)i.Severity)
                .ThenBy(i => i.FilePath)
                .GroupBy(i => i.FilePath)
                .OrderBy(g => g.Key);

            foreach (IGrouping<string, AnalyzerIssue> fileGroup in issuesByFile)
            {
                sb.AppendLine($"### `{fileGroup.Key}`");
                sb.AppendLine();

                foreach (AnalyzerIssue issue in fileGroup.OrderBy(i => i.LineNumber))
                {
                    string severityEmoji = issue.Severity switch
                    {
                        IssueSeverity.Critical => "🔴",
                        IssueSeverity.High => "🟠",
                        IssueSeverity.Medium => "🟡",
                        IssueSeverity.Low => "🟢",
                        IssueSeverity.Info => "🔵",
                        _ => "⚪",
                    };

                    sb.AppendLine(
                        $"#### {severityEmoji} Line {issue.LineNumber}: `{issue.ClassName}.{issue.MethodName}` - {issue.IssueType}"
                    );
                    sb.AppendLine();
                    sb.AppendLine($"**Category:** {issue.Category}");
                    sb.AppendLine();
                    sb.AppendLine($"**Description:** {issue.Description}");
                    sb.AppendLine();

                    if (!string.IsNullOrWhiteSpace(issue.BaseClassName))
                    {
                        sb.AppendLine($"**Base Class:** `{issue.BaseClassName}`");
                        if (!string.IsNullOrWhiteSpace(issue.BaseMethodSignature))
                        {
                            sb.AppendLine($"**Base Method:** `{issue.BaseMethodSignature}`");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(issue.DerivedMethodSignature))
                    {
                        sb.AppendLine($"**Derived Method:** `{issue.DerivedMethodSignature}`");
                    }

                    sb.AppendLine();
                    sb.AppendLine($"**Recommended Fix:** {issue.RecommendedFix}");
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        internal static string RenderJson(
            IReadOnlyList<AnalyzerIssue> issues,
            string coverageStatus
        )
        {
            AnalysisReportJsonModel report = new()
            {
                GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                CoverageStatus = coverageStatus,
                TotalIssues = issues.Count,
                Summary = new SummaryJsonModel
                {
                    BySeverity = new SeveritySummaryJsonModel
                    {
                        Critical = issues.Count(i => i.Severity == IssueSeverity.Critical),
                        High = issues.Count(i => i.Severity == IssueSeverity.High),
                        Medium = issues.Count(i => i.Severity == IssueSeverity.Medium),
                        Low = issues.Count(i => i.Severity == IssueSeverity.Low),
                        Info = issues.Count(i => i.Severity == IssueSeverity.Info),
                    },
                    ByCategory = new CategorySummaryJsonModel
                    {
                        UnityLifecycle = issues.Count(i =>
                            i.Category == IssueCategory.UnityLifecycle
                        ),
                        UnityInheritance = issues.Count(i =>
                            i.Category == IssueCategory.UnityInheritance
                        ),
                        GeneralInheritance = issues.Count(i =>
                            i.Category == IssueCategory.GeneralInheritance
                        ),
                    },
                },
                Issues = issues.Select(i => new IssueJsonModel(i)).ToList(),
            };

            return Serializer.JsonStringify(report, pretty: true);
        }

        private static bool TryExport(
            string path,
            IReadOnlyList<AnalyzerIssue> issues,
            string coverageStatus,
            bool json,
            out string error
        )
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || issues == null
                || string.IsNullOrWhiteSpace(coverageStatus)
            )
            {
                error = "An output path, findings, and compiler coverage status are required.";
                return false;
            }
            try
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i] == null)
                    {
                        error = "Findings cannot contain a null issue.";
                        return false;
                    }
                }

                string report = json
                    ? RenderJson(issues, coverageStatus)
                    : RenderMarkdown(issues, coverageStatus);
                if (!DurableFile.TryWriteAllText(path, report, out Exception writeError))
                {
                    error = writeError?.Message ?? "Could not write the analysis report.";
                    return false;
                }
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }
#endif
}
