// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEditor.Compilation;

    /// <summary>
    /// Filters captured compiler diagnostics into a browsable method-analysis report.
    /// </summary>
    public sealed class MethodAnalyzer
    {
        private readonly List<AnalyzerIssue> _issues = new();
        private readonly IReadOnlyList<CompilerMessage> _suppliedMessages;
        private readonly string _suppliedStatus;

        /// <summary>
        /// Gets diagnostics in the selected source directories.
        /// </summary>
        public IReadOnlyList<AnalyzerIssue> Issues => _issues;

        /// <summary>
        /// Gets the coverage of the captured per-assembly compiler snapshots.
        /// </summary>
        public string Status { get; private set; } = "No compiler report loaded.";

        /// <summary>
        /// Retains the former source-parser API without claiming a compiler symbol inventory.
        /// </summary>
        [Obsolete("Source parsing was retired. Read Issues from the compiler report instead.")]
        public IReadOnlyDictionary<string, AnalyzerClassInfo> Classes { get; } =
            new Dictionary<string, AnalyzerClassInfo>();

        /// <summary>
        /// Creates a view of compiler diagnostics captured by the editor.
        /// </summary>
        public MethodAnalyzer() { }

        internal MethodAnalyzer(IReadOnlyList<CompilerMessage> messages, string status)
        {
            _suppliedMessages = messages;
            _suppliedStatus = status;
        }

        /// <summary>
        /// Clears this view without discarding captured compiler diagnostics.
        /// </summary>
        public void Clear()
        {
            _issues.Clear();
            Status = "No compiler report loaded.";
        }

        /// <summary>
        /// Reads the compiler report for the selected directories without compiling or parsing files.
        /// </summary>
        public void Refresh(string rootPath, IEnumerable<string> directories)
        {
            Clear();
            IReadOnlyList<CompilerMessage> messages = _suppliedMessages;
            if (messages == null)
            {
                messages = UnityMethodCompilerReport.GetMessages(out string status);
                Status = status;
            }
            else
            {
                Status = _suppliedStatus;
            }
            if (string.IsNullOrWhiteSpace(rootPath) || directories == null)
            {
                Status = "No source directories selected; report coverage is unverified.";
                return;
            }
            string root = Path.GetFullPath(rootPath);
            List<string> selectedPaths = new();
            foreach (string directory in directories)
            {
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    selectedPaths.Add(
                        Path.GetFullPath(
                                Path.IsPathRooted(directory)
                                    ? directory
                                    : Path.Combine(root, directory)
                            )
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar
                    );
                }
            }
            if (selectedPaths.Count == 0)
            {
                Status = "No source directories selected; report coverage is unverified.";
                return;
            }
            foreach (CompilerMessage message in messages)
            {
                if (
                    string.IsNullOrEmpty(message.file)
                    || !TryCreateIssue(message, out AnalyzerIssue issue)
                )
                {
                    continue;
                }
                string file = Path.GetFullPath(
                    Path.IsPathRooted(message.file)
                        ? message.file
                        : Path.Combine(root, message.file)
                );
                foreach (string directory in selectedPaths)
                {
                    StringComparison comparison =
                        Path.DirectorySeparatorChar == '\\'
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal;
                    if (file.StartsWith(directory, comparison))
                    {
                        issue.FilePath = file;
                        _issues.Add(issue);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Reads the current compiler snapshot; files outside compilation are not analyzed.
        /// </summary>
        [Obsolete(
            "Use Refresh to read captured compiler diagnostics. Recompile scripts in Unity to update them."
        )]
        public void Analyze(string rootPath, IEnumerable<string> directories) =>
            Refresh(rootPath, directories);

        /// <summary>
        /// Reads the current compiler snapshot with cancellation; this does not start compilation.
        /// </summary>
        public Task AnalyzeAsync(
            string rootPath,
            IEnumerable<string> directories,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default
        )
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }
            Refresh(rootPath, directories);
            progress?.Report(1f);
            return Task.CompletedTask;
        }

        internal static bool TryCreateIssue(CompilerMessage message, out AnalyzerIssue issue)
        {
            string text = message.message ?? string.Empty;
            string code = null;
            foreach (string candidate in DiagnosticCodes)
            {
                int index = text.IndexOf(candidate + ":", StringComparison.Ordinal);
                if (0 <= index && (index == 0 || !char.IsLetterOrDigit(text[index - 1])))
                {
                    code = candidate;
                    break;
                }
            }
            if (code == null)
            {
                issue = null;
                return false;
            }
            bool lifecycle = code == "WUH015";
            bool inheritance = code == "WUH016";
            string signature = ExtractQuoted(text, 0, out int next);
            string baseSignature = inheritance ? ExtractQuoted(text, next, out _) : null;
            int parenthesis = signature.IndexOf('(');
            string qualifiedName =
                0 <= parenthesis ? signature.Substring(0, parenthesis) : signature;
            int separator = qualifiedName.LastIndexOf('.');
            string className =
                0 <= separator ? qualifiedName.Substring(0, separator) : string.Empty;
            string methodName =
                0 <= separator ? qualifiedName.Substring(separator + 1) : qualifiedName;
            issue = new AnalyzerIssue(
                message.file,
                className,
                methodName,
                code,
                text,
                message.type == CompilerMessageType.Error
                    ? IssueSeverity.Critical
                    : IssueSeverity.Medium,
                "Follow the compiler diagnostic; use a scoped warning suppression when intentional.",
                message.line,
                lifecycle ? IssueCategory.UnityLifecycle
                    : inheritance ? IssueCategory.UnityInheritance
                    : IssueCategory.GeneralInheritance,
                baseMethodSignature: baseSignature,
                derivedMethodSignature: signature
            );
            return true;
        }

        private static string ExtractQuoted(string text, int start, out int next)
        {
            int open = text.IndexOf('\'', start);
            int close = open < 0 ? -1 : text.IndexOf('\'', open + 1);
            next = close < 0 ? text.Length : close + 1;
            return close < 0 ? string.Empty : text.Substring(open + 1, close - open - 1);
        }

        private static readonly string[] DiagnosticCodes =
        {
            "WUH015",
            "WUH016",
            "CS0108",
            "CS0114",
            "CS0115",
            "CS0506",
            "CS0507",
            "CS0508",
            "CS0533",
            "CS0534",
            "CS1715",
            "CS0462",
            "CS8830",
        };
    }
#endif
}
