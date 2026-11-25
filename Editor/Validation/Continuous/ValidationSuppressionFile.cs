// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    internal static class ValidationSuppressionFile
    {
        private const string ConflictMessage =
            "The suppression file changed since this action. Reload it before editing again.";

        /// <summary>Applies a suppression edit and returns its conditional undo, if changed.</summary>
        internal static bool TryChange(
            string path,
            string id,
            ValidationFinding? finding,
            bool suppress,
            out Func<string> undo,
            out string failure
        )
        {
            bool previousExists;
            string previous;
            try
            {
                previous = File.ReadAllText(path);
                previousExists = true;
            }
            catch (FileNotFoundException)
            {
                previous = string.Empty;
                previousExists = false;
            }
            catch (Exception error)
            {
                undo = null;
                failure = "Could not save suppression: " + error.Message;
                return false;
            }

            string updated;
            if (finding.HasValue)
            {
                updated = WithSuppression(previous, finding.Value, suppress);
            }
            else
            {
                updated = WithoutSuppression(previous, id);
            }

            if (string.Equals(previous, updated, StringComparison.Ordinal))
            {
                undo = null;
                failure = null;
                return true;
            }
            if (
                !DurableFile.TryCompareThenReplaceAllText(
                    path,
                    previousExists,
                    previous,
                    updated,
                    out bool exchanged,
                    out Exception writeError
                )
            )
            {
                undo = null;
                failure = "Could not save suppression: " + writeError.Message;
                return false;
            }
            if (!exchanged)
            {
                undo = null;
                failure = ConflictMessage;
                return false;
            }

            undo = () => UndoChange(path, updated, previous);
            failure = null;
            return true;
        }

        internal static string WithSuppression(
            string original,
            ValidationFinding finding,
            bool suppress
        )
        {
            string text = original ?? string.Empty;
            ValidationSuppressions existing = ValidationSuppressions.Parse(text);
            if (suppress)
            {
                return existing.IsSuppressed(in finding)
                    ? text
                    : text
                        + (text.EndsWith("\n", StringComparison.Ordinal) ? string.Empty : "\n")
                        + ValidationSuppressions.Render(new[] { finding });
            }
            return WithoutSuppression(text, finding.Id);
        }

        private static string WithoutSuppression(string text, string id)
        {
            using PooledResource<StringBuilder> remainingLease = Buffers.StringBuilder.Get(
                out StringBuilder remaining
            );
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
                if (!string.Equals(line.Trim(), id, StringComparison.Ordinal))
                    remaining.Append(line).Append('\n');
            return remaining.ToString();
        }

        private static string UndoChange(string path, string updated, string previous)
        {
            if (
                !DurableFile.TryCompareThenReplaceAllText(
                    path,
                    true,
                    updated,
                    previous,
                    out bool undone,
                    out Exception undoError
                )
            )
            {
                return "Could not undo suppression: " + undoError.Message;
            }
            if (!undone)
            {
                return ConflictMessage;
            }
            return null;
        }
    }
#endif
}
