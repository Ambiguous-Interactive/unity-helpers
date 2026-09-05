// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Which findings a reader currently wants to see, and the summary line describing what they
    /// are looking at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separated from the window because it is the only part with a right answer. A UI Toolkit
    /// window cannot be driven from an EditMode test -- a drawer test needs a real draw -- so the
    /// filtering, the counting and the wording all live here where a fixture can assert them, and
    /// the window is left holding nothing but element construction.
    /// </para>
    /// <para>
    /// A suppressed finding is kept and marked rather than dropped, for the reason the headless
    /// report keeps them: a view that hid them would make a suppression file indistinguishable
    /// from a project with nothing wrong.
    /// </para>
    /// </remarks>
    public static class ValidationResultFilter
    {
        /// <summary>
        /// Selects the findings matching a severity floor and a search query.
        /// </summary>
        /// <param name="findings">Everything currently known; <c>null</c> yields an empty result.</param>
        /// <param name="minimum">
        /// The least severe level to show. <see cref="ValidationSeverity.Info"/> shows everything.
        /// </param>
        /// <param name="query">
        /// Case-insensitive text matched against the rule, the asset path, the discriminator and
        /// the message. Blank matches everything.
        /// </param>
        /// <param name="includeSuppressed">Whether findings the file silences are kept.</param>
        /// <param name="suppressions">The suppression set; <c>null</c> suppresses nothing.</param>
        /// <returns>The matching findings, in the order given; never <c>null</c>.</returns>
        public static List<ValidationFinding> Apply(
            IReadOnlyList<ValidationFinding> findings,
            ValidationSeverity minimum,
            string query,
            bool includeSuppressed,
            ValidationSuppressions suppressions
        )
        {
            List<ValidationFinding> kept = new List<ValidationFinding>();
            Apply(findings, minimum, query, includeSuppressed, suppressions, kept);
            return kept;
        }

        /// <summary>
        /// Selects the matching findings into a caller's list, clearing it first.
        /// </summary>
        /// <param name="findings">Everything currently known; <c>null</c> yields an empty result.</param>
        /// <param name="minimum">The least severe level to show.</param>
        /// <param name="query">Case-insensitive text; blank matches everything.</param>
        /// <param name="includeSuppressed">Whether findings the file silences are kept.</param>
        /// <param name="suppressions">The suppression set; <c>null</c> suppresses nothing.</param>
        /// <param name="destination">The list to fill; <c>null</c> is ignored.</param>
        /// <remarks>
        /// The allocation-free half, for a window that refilters on every keystroke.
        /// </remarks>
        public static void Apply(
            IReadOnlyList<ValidationFinding> findings,
            ValidationSeverity minimum,
            string query,
            bool includeSuppressed,
            ValidationSuppressions suppressions,
            List<ValidationFinding> destination
        )
        {
            if (destination == null)
            {
                return;
            }

            destination.Clear();
            if (findings == null)
            {
                return;
            }

            string trimmed = query == null ? string.Empty : query.Trim();
            for (int index = 0; index < findings.Count; index++)
            {
                ValidationFinding finding = findings[index];
                if (finding.Severity < minimum)
                {
                    continue;
                }

                if (
                    !includeSuppressed
                    && suppressions != null
                    && suppressions.IsSuppressed(in finding)
                )
                {
                    continue;
                }

                if (trimmed.Length != 0 && !Matches(in finding, trimmed))
                {
                    continue;
                }

                destination.Add(finding);
            }
        }

        /// <summary>
        /// Counts findings by severity.
        /// </summary>
        /// <param name="findings">The findings to count; <c>null</c> counts nothing.</param>
        /// <param name="errors">How many are <see cref="ValidationSeverity.Error"/>.</param>
        /// <param name="warnings">How many are <see cref="ValidationSeverity.Warning"/>.</param>
        /// <param name="infos">How many are <see cref="ValidationSeverity.Info"/>.</param>
        public static void Count(
            IReadOnlyList<ValidationFinding> findings,
            out int errors,
            out int warnings,
            out int infos
        )
        {
            int foundErrors = 0;
            int foundWarnings = 0;
            int foundInfos = 0;
            int count = findings == null ? 0 : findings.Count;
            for (int index = 0; index < count; index++)
            {
                switch (findings[index].Severity)
                {
                    case ValidationSeverity.Error:
                    {
                        foundErrors++;
                        break;
                    }
                    case ValidationSeverity.Warning:
                    {
                        foundWarnings++;
                        break;
                    }
                    default:
                    {
                        foundInfos++;
                        break;
                    }
                }
            }

            errors = foundErrors;
            warnings = foundWarnings;
            infos = foundInfos;
        }

        /// <summary>
        /// Describes what a reader is looking at, in one line.
        /// </summary>
        /// <param name="hasRun">Whether anything has been checked since the last domain reload.</param>
        /// <param name="checkedAssets">How many assets have a recorded result.</param>
        /// <param name="findings">Everything currently known, before filtering.</param>
        /// <returns>The summary; never <c>null</c> or empty.</returns>
        /// <remarks>
        /// "Nothing checked yet" and "checked, and clean" are deliberately different sentences. A
        /// window that showed an empty list for both would report a project as healthy on the
        /// strength of never having looked at it, which is the shape
        /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/556">#556</see>
        /// exists to refuse.
        /// </remarks>
        public static string Summarize(
            bool hasRun,
            int checkedAssets,
            IReadOnlyList<ValidationFinding> findings
        )
        {
            if (!hasRun)
            {
                return "Nothing checked yet. Run a validation pass to see this project's state.";
            }

            Count(findings, out int errors, out int warnings, out int infos);
            string assets =
                checkedAssets == 1 ? "1 asset checked" : checkedAssets + " assets checked";
            if (errors == 0 && warnings == 0 && infos == 0)
            {
                return assets + ", no problems found.";
            }

            return assets
                + ": "
                + Plural(errors, "error")
                + ", "
                + Plural(warnings, "warning")
                + ", "
                + Plural(infos, "info")
                + ".";
        }

        private static bool Matches(in ValidationFinding finding, string query)
        {
            return Contains(finding.RuleId, query)
                || Contains(finding.AssetPath, query)
                || Contains(finding.Discriminator, query)
                || Contains(finding.Message, query);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value)
                && 0 <= value.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        }

        private static string Plural(int count, string noun)
        {
            return count == 1 ? "1 " + noun : count + " " + noun + "s";
        }
    }
#endif
}
