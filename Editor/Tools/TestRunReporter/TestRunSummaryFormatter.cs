// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Globalization;
    using System.Text;
    using UnityEditor.TestTools.TestRunner.Api;

    /// <summary>
    ///     Turns a test run result tree into the line-oriented summary an outside process polls.
    /// </summary>
    internal static class TestRunSummaryFormatter
    {
        /// <summary>
        ///     The first token of the run's single summary line.
        /// </summary>
        internal const string SummaryPrefix = "SUMMARY";

        /// <summary>
        ///     The first token of a per-assembly line.
        /// </summary>
        internal const string AssemblyPrefix = "ASSEMBLY";

        /// <summary>
        ///     The first token of a per-failure line.
        /// </summary>
        internal const string FailurePrefix = "FAILURE";

        /// <summary>
        ///     The bare second token that marks a run as still in flight.
        /// </summary>
        internal const string RunningToken = "running";

        /// <summary>
        ///     The line separator every summary file uses, on every platform.
        /// </summary>
        internal const string LineSeparator = "\n";

        /// <summary>
        ///     The key naming when a run began.
        /// </summary>
        internal const string StartedKey = "started";

        /// <summary>
        ///     The key naming when a run ended.
        /// </summary>
        internal const string FinishedKey = "finished";

        /// <summary>
        ///     The key naming which test mode a run covers.
        /// </summary>
        internal const string ModeKey = "mode";

        /// <summary>
        ///     The key naming an assembly or a test case.
        /// </summary>
        internal const string NameKey = "name";

        /// <summary>
        ///     The key naming the assembly a failure belongs to.
        /// </summary>
        internal const string AssemblyKey = "assembly";

        /// <summary>
        ///     The key naming the source location a failure was reported from.
        /// </summary>
        internal const string LocationKey = "location";

        /// <summary>
        ///     The key naming a failure's message, always the last field on its line.
        /// </summary>
        internal const string MessageKey = "message";

        /// <summary>
        ///     The key naming when an assembly was last compiled.
        /// </summary>
        internal const string BuiltKey = "built";

        /// <summary>
        ///     The key naming an elapsed duration in seconds.
        /// </summary>
        internal const string SecondsKey = "seconds";

        /// <summary>
        ///     The key naming a count of passing test cases.
        /// </summary>
        internal const string PassKey = "pass";

        /// <summary>
        ///     The key naming a count of failing test cases.
        /// </summary>
        internal const string FailKey = "fail";

        /// <summary>
        ///     The key naming a count of skipped test cases.
        /// </summary>
        internal const string SkipKey = "skip";

        /// <summary>
        ///     The key naming a count of inconclusive test cases.
        /// </summary>
        internal const string InconclusiveKey = "inconclusive";

        /// <summary>
        ///     The deepest result tree that is walked, past which nodes are ignored.
        /// </summary>
        internal const int MaximumTreeDepth = 64;

        private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z'";
        private const string SecondsFormat = "F3";
        private const string StackFrameFileSeparator = " in ";
        private const char FieldSeparator = ' ';
        private const char KeyValueSeparator = '=';
        private const char EscapePrefix = '\\';

        /// <summary>
        ///     Formats the marker written before a run starts, whose second token is
        ///     <see cref="RunningToken"/>.
        /// </summary>
        /// <param name="mode">The test mode the run covers.</param>
        /// <param name="startedUtc">When the run was started.</param>
        /// <returns>A single terminated line.</returns>
        internal static string FormatRunningMarker(TestMode mode, DateTime startedUtc)
        {
            StringBuilder builder = new(96);
            builder.Append(SummaryPrefix);
            builder.Append(FieldSeparator);
            builder.Append(RunningToken);
            AppendField(builder, StartedKey, FormatTimestamp(startedUtc));
            AppendField(builder, ModeKey, mode.ToString());
            builder.Append(LineSeparator);
            return builder.ToString();
        }

        /// <summary>
        ///     Formats the completed summary: one <see cref="SummaryPrefix"/> line, one
        ///     <see cref="AssemblyPrefix"/> line per assembly, one <see cref="FailurePrefix"/> line
        ///     per failing test case.
        /// </summary>
        /// <param name="mode">The test mode the run covered.</param>
        /// <param name="startedUtc">When the run was started.</param>
        /// <param name="finishedUtc">When the run finished.</param>
        /// <param name="root">The root of the result tree, whose children are the assemblies.</param>
        /// <returns>The whole file content, terminated.</returns>
        internal static string FormatSummary(
            TestMode mode,
            DateTime startedUtc,
            DateTime finishedUtc,
            TestRunResultNode root
        )
        {
            int[] totals = NewCounts();
            CountLeaves(root, totals, 0);

            StringBuilder builder = new(1024);
            builder.Append(SummaryPrefix);
            AppendCounts(builder, totals);
            AppendField(
                builder,
                SecondsKey,
                FormatSeconds((finishedUtc - startedUtc).TotalSeconds)
            );
            AppendField(builder, ModeKey, mode.ToString());
            AppendField(builder, StartedKey, FormatTimestamp(startedUtc));
            AppendField(builder, FinishedKey, FormatTimestamp(finishedUtc));
            builder.Append(LineSeparator);

            if (root == null)
            {
                return builder.ToString();
            }

            for (int i = 0; i < root.children.Count; i++)
            {
                TestRunResultNode assembly = root.children[i];
                if (assembly == null)
                {
                    continue;
                }

                int[] counts = NewCounts();
                CountLeaves(assembly, counts, 0);
                builder.Append(AssemblyPrefix);
                AppendField(builder, NameKey, assembly.fullName);
                AppendCounts(builder, counts);
                AppendField(builder, SecondsKey, FormatSeconds(assembly.durationSeconds));
                AppendField(
                    builder,
                    BuiltKey,
                    assembly.assemblyBuiltUtc.HasValue
                        ? FormatTimestamp(assembly.assemblyBuiltUtc.Value)
                        : string.Empty
                );
                builder.Append(LineSeparator);
            }

            for (int i = 0; i < root.children.Count; i++)
            {
                TestRunResultNode assembly = root.children[i];
                if (assembly == null)
                {
                    continue;
                }

                AppendFailures(builder, assembly, assembly.fullName, 0);
            }

            return builder.ToString();
        }

        /// <summary>
        ///     Reports whether a line is the in-flight marker rather than a completed summary.
        /// </summary>
        /// <param name="line">A single line, without its terminator.</param>
        /// <returns><c>true</c> when the run that wrote the line has not finished.</returns>
        internal static bool IsRunningLine(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return false;
            }

            string[] tokens = line.Split(FieldSeparator);
            return 2 <= tokens.Length
                && string.Equals(tokens[0], SummaryPrefix, StringComparison.Ordinal)
                && string.Equals(tokens[1], RunningToken, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Reads one <c>key=value</c> field out of a line, unescaping the value.
        /// </summary>
        /// <param name="line">A single line, without its terminator.</param>
        /// <param name="key">The key to look for.</param>
        /// <param name="value">The unescaped value, empty when the key is absent.</param>
        /// <returns><c>true</c> when the key was present.</returns>
        internal static bool TryGetField(string line, string key, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(key))
            {
                return false;
            }

            string prefix = key + KeyValueSeparator;
            string[] tokens = line.Split(FieldSeparator);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (!tokens[i].StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                value = Unescape(tokens[i].Substring(prefix.Length));
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Reads the <see cref="StartedKey"/> timestamp out of a summary line.
        /// </summary>
        /// <param name="line">A single line, without its terminator.</param>
        /// <param name="startedUtc">The parsed timestamp, in UTC.</param>
        /// <returns><c>true</c> when the line carried a parseable timestamp.</returns>
        internal static bool TryParseStartedUtc(string line, out DateTime startedUtc)
        {
            startedUtc = default;
            if (!TryGetField(line, StartedKey, out string value))
            {
                return false;
            }

            return DateTime.TryParseExact(
                value,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out startedUtc
            );
        }

        /// <summary>
        ///     Escapes a value so it survives as a single whitespace-free token.
        /// </summary>
        /// <param name="value">The raw value.</param>
        /// <returns>The escaped value, empty when the input was null or empty.</returns>
        internal static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new(value.Length);
            Escape(builder, value);
            return builder.ToString();
        }

        /// <summary>
        ///     Reverses <see cref="Escape(string)"/>.
        /// </summary>
        /// <param name="value">The escaped value.</param>
        /// <returns>The raw value, empty when the input was null or empty.</returns>
        internal static string Unescape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character != EscapePrefix || value.Length <= i + 1)
                {
                    builder.Append(character);
                    continue;
                }

                i++;
                char escaped = value[i];
                switch (escaped)
                {
                    case 's':
                    {
                        builder.Append(FieldSeparator);
                        break;
                    }
                    case 'n':
                    {
                        builder.Append('\n');
                        break;
                    }
                    case 'r':
                    {
                        builder.Append('\r');
                        break;
                    }
                    case 't':
                    {
                        builder.Append('\t');
                        break;
                    }
                    default:
                    {
                        builder.Append(escaped);
                        break;
                    }
                }
            }

            return builder.ToString();
        }

        /// <summary>
        ///     Pulls the first <c>file:line</c> a stack trace names.
        /// </summary>
        /// <param name="stackTrace">The stack trace the test runner reported.</param>
        /// <returns>The location, or an empty string when none can be read.</returns>
        internal static string ExtractLocation(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return string.Empty;
            }

            string[] lines = stackTrace.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int separator = lines[i].IndexOf(StackFrameFileSeparator, StringComparison.Ordinal);
                if (separator < 0)
                {
                    continue;
                }

                string location = lines[i]
                    .Substring(separator + StackFrameFileSeparator.Length)
                    .Trim();
                if (0 < location.Length)
                {
                    return location;
                }
            }

            return string.Empty;
        }

        private static void AppendFailures(
            StringBuilder builder,
            TestRunResultNode node,
            string assemblyName,
            int depth
        )
        {
            if (node == null || MaximumTreeDepth <= depth)
            {
                return;
            }

            if (0 < node.children.Count)
            {
                for (int i = 0; i < node.children.Count; i++)
                {
                    AppendFailures(builder, node.children[i], assemblyName, depth + 1);
                }

                return;
            }

            if (node.status != TestStatus.Failed)
            {
                return;
            }

            builder.Append(FailurePrefix);
            AppendField(builder, AssemblyKey, assemblyName);
            AppendField(builder, NameKey, node.fullName);
            AppendField(builder, LocationKey, ExtractLocation(node.stackTrace));
            AppendField(builder, MessageKey, node.message);
            builder.Append(LineSeparator);
        }

        private static void AppendCounts(StringBuilder builder, int[] counts)
        {
            AppendField(builder, PassKey, CountOf(counts, TestStatus.Passed));
            AppendField(builder, FailKey, CountOf(counts, TestStatus.Failed));
            AppendField(builder, SkipKey, CountOf(counts, TestStatus.Skipped));
            AppendField(builder, InconclusiveKey, CountOf(counts, TestStatus.Inconclusive));
        }

        private static void AppendField(StringBuilder builder, string key, string value)
        {
            builder.Append(FieldSeparator);
            builder.Append(key);
            builder.Append(KeyValueSeparator);
            Escape(builder, value);
        }

        private static void Escape(StringBuilder builder, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case EscapePrefix:
                    {
                        builder.Append(EscapePrefix);
                        builder.Append(EscapePrefix);
                        break;
                    }
                    case FieldSeparator:
                    {
                        builder.Append(EscapePrefix);
                        builder.Append('s');
                        break;
                    }
                    case '\n':
                    {
                        builder.Append(EscapePrefix);
                        builder.Append('n');
                        break;
                    }
                    case '\r':
                    {
                        builder.Append(EscapePrefix);
                        builder.Append('r');
                        break;
                    }
                    case '\t':
                    {
                        builder.Append(EscapePrefix);
                        builder.Append('t');
                        break;
                    }
                    default:
                    {
                        builder.Append(character);
                        break;
                    }
                }
            }
        }

        private static int[] NewCounts()
        {
            return new int[1 + (int)TestStatus.Failed];
        }

        private static void CountLeaves(TestRunResultNode node, int[] counts, int depth)
        {
            if (node == null || MaximumTreeDepth <= depth)
            {
                return;
            }

            if (0 < node.children.Count)
            {
                for (int i = 0; i < node.children.Count; i++)
                {
                    CountLeaves(node.children[i], counts, depth + 1);
                }

                return;
            }

            int index = (int)node.status;
            if (0 <= index && index < counts.Length)
            {
                counts[index]++;
            }
        }

        private static string CountOf(int[] counts, TestStatus status)
        {
            int index = (int)status;
            int value = 0 <= index && index < counts.Length ? counts[index] : 0;
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatTimestamp(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
            return utc.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        }

        private static string FormatSeconds(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                return 0d.ToString(SecondsFormat, CultureInfo.InvariantCulture);
            }

            return value.ToString(SecondsFormat, CultureInfo.InvariantCulture);
        }
    }
#endif
}
