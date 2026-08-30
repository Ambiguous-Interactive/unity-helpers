// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Runs every <see cref="IValidationRule"/> in the project from the command line and reports
    /// what it found, so a build can refuse to publish an asset nobody would have looked at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole point of the engine is continuous, lightweight checks. That only becomes a
    /// guarantee when something other than a person is running it, which is what this is for:
    /// </para>
    /// <code>
    /// Unity -batchmode -quit -projectPath &lt;project&gt; \
    ///   -executeMethod WallstopStudios.UnityHelpers.Editor.Validation.Continuous.ValidationBatch.ValidateFromCommandLine \
    ///   -validationOutput validation.json -validationFailOn Warning
    /// </code>
    /// <para>
    /// It exits non-zero when anything at or above the threshold stands unsuppressed, and when any
    /// rule threw -- a rule that threw produced no answer for that asset, so passing on it would be
    /// reporting coverage the run does not have.
    /// </para>
    /// <para>
    /// Rules are found through <c>TypeCache</c> and constructed with their parameterless
    /// constructor. A rule that cannot be constructed is reported and skipped rather than ending
    /// the run, because the alternative is one broken rule hiding every other rule's findings.
    /// </para>
    /// </remarks>
    public static class ValidationBatch
    {
        /// <summary>The argument naming where the JSON report is written.</summary>
        public const string OutputArgument = "-validationOutput";

        /// <summary>The argument naming the suppression file to read.</summary>
        public const string SuppressionsArgument = "-validationSuppressions";

        /// <summary>The argument naming the lowest severity that fails the run.</summary>
        public const string FailOnArgument = "-validationFailOn";

        /// <summary>The argument naming a folder to restrict the run to; repeatable.</summary>
        public const string FolderArgument = "-validationFolder";

        /// <summary>
        /// Validates the project and exits with 0 when nothing blocking stands.
        /// </summary>
        public static void ValidateFromCommandLine()
        {
            Result result = Run(Environment.GetCommandLineArgs());
            Debug.Log(result.Summary);
            EditorApplication.Exit(result.ExitCode);
        }

        /// <summary>
        /// Validates the project according to a command line, without exiting.
        /// </summary>
        /// <param name="commandLine">The process arguments; <c>null</c> is treated as none.</param>
        /// <returns>What happened, including the exit code the caller should use.</returns>
        /// <remarks>
        /// Separated from <see cref="ValidateFromCommandLine"/> so the decision can be made without
        /// killing the editor, which is what lets a menu item and a test reach it.
        /// </remarks>
        public static Result Run(string[] commandLine)
        {
            List<string> folders = ValuesOf(commandLine, FolderArgument);
            string outputPath = ValueOf(commandLine, OutputArgument);
            string suppressionsPath = ValueOf(commandLine, SuppressionsArgument);
            ValidationSeverity threshold = ParseSeverity(
                ValueOf(commandLine, FailOnArgument),
                ValidationSeverity.Error
            );

            List<string> problems = new List<string>();
            List<IValidationRule> rules = DiscoverRules(problems);
            ValidationSuppressions suppressions = ReadSuppressions(suppressionsPath, problems);

            ValidationRun run = new ValidationRun(
                rules,
                ValidationTargets.Enumerate(folders.ToArray())
            );
            while (!run.Step(double.MaxValue)) { }

            problems.AddRange(CoverageProblems(rules.Count, run.TotalCount, folders));

            string json = ValidationReport.ToJson(run, suppressions);
            if (
                !string.IsNullOrEmpty(outputPath) && !TryWrite(outputPath, json, out string failure)
            )
            {
                problems.Add(failure);
            }

            bool blocking = ValidationReport.HasBlockingResults(run, suppressions, threshold);
            return new Result(run, suppressions, json, problems, blocking || 0 < problems.Count);
        }

        /// <summary>
        /// Constructs one instance of every concrete rule the project defines.
        /// </summary>
        /// <param name="problems">Receives one line per rule that could not be constructed.</param>
        /// <returns>The rules, ordered by type name so two runs agree.</returns>
        public static List<IValidationRule> DiscoverRules(List<string> problems)
        {
            List<Type> candidates = new List<Type>();
            foreach (Type candidate in TypeCache.GetTypesDerivedFrom<IValidationRule>())
            {
                if (
                    candidate == null
                    || candidate.IsAbstract
                    || candidate.IsInterface
                    || candidate.ContainsGenericParameters
                )
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            // TypeCache's order is not a property of the project, and a report whose findings
            // arrive in a different order on two machines cannot be diffed.
            candidates.Sort(
                (left, right) =>
                    string.CompareOrdinal(left.AssemblyQualifiedName, right.AssemblyQualifiedName)
            );

            List<IValidationRule> rules = new List<IValidationRule>();
            for (int index = 0; index < candidates.Count; index++)
            {
                Type candidate = candidates[index];
                try
                {
                    if (Activator.CreateInstance(candidate) is IValidationRule rule)
                    {
                        rules.Add(rule);
                    }
                }
                catch (Exception exception)
                {
                    // Reported rather than thrown: one rule without a parameterless constructor
                    // would otherwise hide every other rule's findings, and a silent skip would
                    // report a clean project that nobody had actually checked.
                    problems?.Add(
                        candidate.FullName + " could not be constructed: " + exception.Message
                    );
                }
            }

            return rules;
        }

        /// <summary>
        /// Reports the ways a finished run proves nothing.
        /// </summary>
        /// <param name="ruleCount">How many rules the run was given.</param>
        /// <param name="targetCount">How many assets it considered.</param>
        /// <param name="folders">The folders it was restricted to, if any.</param>
        /// <returns>One line per reason; empty when the run actually measured something.</returns>
        /// <remarks>
        /// A run that walked nothing, or that had nothing to walk with, is the absence of a
        /// measurement rather than a pass -- and it exits 0 unless something says so. Both shapes
        /// are reachable without anything looking wrong: a folder argument naming a renamed
        /// directory yields no targets and is skipped silently by
        /// <see cref="ValidationTargets.Enumerate"/>, and a project that has not written a rule yet
        /// yields no rules. Either way the build would report validation passing having checked
        /// nothing, which is the shape #556 exists to refuse.
        ///
        /// Separated from <see cref="Run"/> so it can be asserted without an asset database.
        /// </remarks>
        internal static List<string> CoverageProblems(
            int ruleCount,
            int targetCount,
            IReadOnlyList<string> folders
        )
        {
            List<string> problems = new List<string>();
            if (ruleCount <= 0)
            {
                problems.Add(
                    "no IValidationRule implementation was found, so this run checked nothing. "
                        + "Write a rule, or drop this step until there is one."
                );
            }

            if (targetCount <= 0)
            {
                problems.Add(
                    folders != null && 0 < folders.Count
                        ? "no assets were found under "
                            + string.Join(", ", folders)
                            + ", so this run checked nothing. Check the "
                            + FolderArgument
                            + " paths -- a folder that does not exist is skipped silently."
                        : "no assets were found in the project, so this run checked nothing."
                );
            }

            return problems;
        }

        private static ValidationSuppressions ReadSuppressions(string path, List<string> problems)
        {
            if (string.IsNullOrEmpty(path))
            {
                return ValidationSuppressions.Empty;
            }

            try
            {
                return ValidationSuppressions.Parse(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                // A suppression file that was named and could not be read is not the same as none:
                // continuing with an empty set would report every already-accepted finding as new,
                // and continuing silently would hide that the file was never applied.
                problems?.Add(path + " could not be read: " + exception.Message);
                return ValidationSuppressions.Empty;
            }
        }

        private static bool TryWrite(string path, string contents, out string failure)
        {
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, contents);
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = path + " could not be written: " + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// Reads a severity name, accepting any casing.
        /// </summary>
        /// <param name="value">What the command line said, or <c>null</c>.</param>
        /// <param name="fallback">What to use when it said nothing usable.</param>
        /// <returns>The severity.</returns>
        /// <remarks>
        /// An unrecognized name falls back rather than failing. The fallback is the strictest
        /// useful threshold, so a typo cannot quietly turn the gate off.
        /// </remarks>
        internal static ValidationSeverity ParseSeverity(string value, ValidationSeverity fallback)
        {
            if (string.IsNullOrEmpty(value))
            {
                return fallback;
            }

            foreach (
                ValidationSeverity candidate in new[]
                {
                    ValidationSeverity.Info,
                    ValidationSeverity.Warning,
                    ValidationSeverity.Error,
                }
            )
            {
                if (string.Equals(value, candidate.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return fallback;
        }

        /// <summary>Reads the value following a named argument, or <c>null</c>.</summary>
        /// <param name="commandLine">The process arguments.</param>
        /// <param name="name">The argument to look for.</param>
        /// <returns>The first value given for it.</returns>
        internal static string ValueOf(string[] commandLine, string name)
        {
            List<string> values = ValuesOf(commandLine, name);
            return values.Count == 0 ? null : values[0];
        }

        /// <summary>Reads every value given for a named argument, in order.</summary>
        /// <param name="commandLine">The process arguments.</param>
        /// <param name="name">The argument to look for.</param>
        /// <returns>The values; empty when the argument was not given.</returns>
        internal static List<string> ValuesOf(string[] commandLine, string name)
        {
            List<string> values = new List<string>();
            if (commandLine == null)
            {
                return values;
            }

            for (int index = 0; index + 1 < commandLine.Length; index++)
            {
                if (string.Equals(commandLine[index], name, StringComparison.Ordinal))
                {
                    values.Add(commandLine[index + 1]);
                }
            }

            return values;
        }

        /// <summary>What a headless validation run decided.</summary>
        public sealed class Result
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Result"/> class.
            /// </summary>
            /// <param name="run">The finished run.</param>
            /// <param name="suppressions">What the project silences.</param>
            /// <param name="json">The rendered report.</param>
            /// <param name="problems">Anything that went wrong outside a rule.</param>
            /// <param name="failed">Whether the caller should exit non-zero.</param>
            public Result(
                ValidationRun run,
                ValidationSuppressions suppressions,
                string json,
                IReadOnlyList<string> problems,
                bool failed
            )
            {
                Run = run;
                Suppressions = suppressions;
                Json = json;
                Problems = problems ?? Array.Empty<string>();
                Failed = failed;
            }

            /// <summary>The finished run.</summary>
            public ValidationRun Run { get; }

            /// <summary>What the project silences.</summary>
            public ValidationSuppressions Suppressions { get; }

            /// <summary>The rendered JSON report.</summary>
            public string Json { get; }

            /// <summary>Anything that went wrong outside a rule: an unreadable file, an unbuildable rule.</summary>
            public IReadOnlyList<string> Problems { get; }

            /// <summary>Whether the caller should exit non-zero.</summary>
            public bool Failed { get; }

            /// <summary>The exit code the caller should use.</summary>
            public int ExitCode => Failed ? 1 : 0;

            /// <summary>A one-paragraph account of the run, for the console.</summary>
            public string Summary
            {
                get
                {
                    int findings = Run == null ? 0 : Run.Findings.Count;
                    int failures = Run == null ? 0 : Run.Failures.Count;
                    int considered = Run == null ? 0 : Run.TotalCount;
                    int unused =
                        Suppressions == null || Run == null
                            ? 0
                            : Suppressions.UnusedIn(Run.Findings).Count;

                    string text =
                        "Validation: "
                        + considered
                        + " asset(s), "
                        + findings
                        + " finding(s), "
                        + failures
                        + " failure(s), "
                        + unused
                        + " unused suppression(s).";
                    for (int index = 0; index < Problems.Count; index++)
                    {
                        text += "\n  " + Problems[index];
                    }

                    return text;
                }
            }
        }
    }
#endif
}
