// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// The findings a project has decided not to be told about again, read from a committed file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One <see cref="ValidationFinding.Id"/> per line, so the file is a review artifact rather
    /// than an opaque blob: a diff shows exactly which check somebody switched off. Blank lines and
    /// <c>#</c> comments are ignored, and <see cref="Render"/> writes the asset path and message
    /// above each entry as a comment, because a rule name and a GUID tell a reviewer nothing.
    /// </para>
    /// <para>
    /// Matching is on the finding's identity -- rule, asset GUID, discriminator -- and never on the
    /// path or the message, so moving the asset or rewording the rule does not silently un-suppress
    /// it. That is the same identity the finding already documents, and the reason it excludes
    /// those two fields.
    /// </para>
    /// <para>
    /// <see cref="UnusedIn"/> exists because a suppression that outlives the finding it silenced is
    /// the same defect class as a linter that cannot report: it reads as a considered decision and
    /// is really a stale line nobody has looked at. A headless run reports them rather than letting
    /// the file accumulate.
    /// </para>
    /// </remarks>
    public sealed class ValidationSuppressions
    {
        private static readonly string[] NoIds = Array.Empty<string>();

        private static readonly ValidationSuppressions EmptySuppressions =
            new ValidationSuppressions(
                new List<string>(),
                new HashSet<string>(StringComparer.Ordinal)
            );

        private readonly List<string> _ordered;
        private readonly HashSet<string> _ids;

        private ValidationSuppressions(List<string> ordered, HashSet<string> ids)
        {
            _ordered = ordered;
            _ids = ids;
        }

        /// <summary>A set that suppresses nothing.</summary>
        public static ValidationSuppressions Empty => EmptySuppressions;

        /// <summary>How many distinct findings this set suppresses.</summary>
        public int Count => _ordered.Count;

        /// <summary>The suppressed identities, in the order the file listed them.</summary>
        public IReadOnlyList<string> Ids => _ordered;

        /// <summary>
        /// Reads a suppression file.
        /// </summary>
        /// <param name="text">The file's contents; <c>null</c> or blank yields <see cref="Empty"/>.</param>
        /// <returns>The set; never <c>null</c>.</returns>
        /// <remarks>
        /// Nothing here throws or reports. A malformed line is a line that suppresses nothing,
        /// which the run then reports through <see cref="UnusedIn"/> along with every other entry
        /// that matched nothing -- one mechanism for "this line does not do what you think" rather
        /// than a parse error for some shapes and silence for the rest.
        /// </remarks>
        public static ValidationSuppressions Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return EmptySuppressions;
            }

            List<string> ordered = new List<string>();
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#')
                {
                    continue;
                }

                if (ids.Add(trimmed))
                {
                    ordered.Add(trimmed);
                }
            }

            return ordered.Count == 0
                ? EmptySuppressions
                : new ValidationSuppressions(ordered, ids);
        }

        /// <summary>
        /// Renders findings as a suppression file, newest decision first in file order.
        /// </summary>
        /// <param name="findings">What to suppress; <c>null</c> entries are skipped.</param>
        /// <returns>The complete file text, with a trailing newline.</returns>
        /// <remarks>
        /// The comment above each entry is what makes the file reviewable. It is regenerated from
        /// the finding rather than preserved from an earlier file, so it cannot drift into
        /// describing something the identity no longer points at.
        /// </remarks>
        public static string Render(IReadOnlyList<ValidationFinding> findings)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("# Validation suppressions.\n");
            builder.Append("# One finding identity per line: rule|assetGuid|discriminator.\n");
            builder.Append("# Delete a line to be told about that finding again. A line that\n");
            builder.Append("# matches nothing is reported by the headless run rather than kept.\n");

            HashSet<string> written = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<ValidationFinding> safe = Safe(findings);
            for (int index = 0; index < safe.Count; index++)
            {
                ValidationFinding finding = safe[index];
                if (!written.Add(finding.Id))
                {
                    continue;
                }

                builder.Append('\n');
                builder.Append("# ");
                builder.Append(
                    string.IsNullOrEmpty(finding.AssetPath) ? "(no path)" : finding.AssetPath
                );
                builder.Append(" -- ");
                builder.Append(Single(finding.Message));
                builder.Append('\n');
                builder.Append(finding.Id);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        /// <summary>Reports whether this set silences a finding.</summary>
        /// <param name="finding">The finding to test.</param>
        /// <returns><c>true</c> when the file lists the finding's identity.</returns>
        public bool IsSuppressed(in ValidationFinding finding)
        {
            return _ids.Contains(finding.Id);
        }

        /// <summary>
        /// The entries that silenced nothing in a run.
        /// </summary>
        /// <param name="findings">Every finding the run produced, suppressed ones included.</param>
        /// <returns>The unmatched identities, in file order; empty when every entry earned its place.</returns>
        /// <remarks>
        /// Only meaningful for a run that covered the whole project. A run scoped to one folder
        /// will not have seen the assets most entries name, so treating its answer as stale
        /// suppressions would delete decisions about assets nobody looked at.
        /// </remarks>
        public IReadOnlyList<string> UnusedIn(IReadOnlyList<ValidationFinding> findings)
        {
            if (_ordered.Count == 0)
            {
                return NoIds;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<ValidationFinding> safe = Safe(findings);
            for (int index = 0; index < safe.Count; index++)
            {
                seen.Add(safe[index].Id);
            }

            List<string> unused = new List<string>();
            for (int index = 0; index < _ordered.Count; index++)
            {
                if (!seen.Contains(_ordered[index]))
                {
                    unused.Add(_ordered[index]);
                }
            }

            return unused.Count == 0 ? NoIds : unused;
        }

        private static IReadOnlyList<T> Safe<T>(IReadOnlyList<T> values)
        {
            return values ?? (IReadOnlyList<T>)Array.Empty<T>();
        }

        /// <summary>
        /// Flattens a message onto one line, so it cannot become an entry of its own.
        /// </summary>
        /// <param name="message">The finding's message.</param>
        /// <returns>The message with newlines replaced by spaces.</returns>
        private static string Single(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return "(no message)";
            }

            return message.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        }
    }
#endif
}
