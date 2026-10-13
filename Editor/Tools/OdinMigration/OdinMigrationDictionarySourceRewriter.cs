// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;

    internal static class OdinMigrationDictionarySourceRewriter
    {
        internal static bool TryPreview(
            OdinMigrationCompiledBinding binding,
            IReadOnlyList<OdinMigrationDictionaryField> fields,
            out OdinMigrationDictionarySourcePreview preview,
            out string failure
        )
        {
            try
            {
                if (binding == null || fields == null || fields.Count == 0)
                {
                    throw new InvalidDataException(
                        "A compiled binding and original dictionary schemas are required."
                    );
                }
                HashSet<string> requested = new HashSet<string>(StringComparer.Ordinal);
                foreach (OdinMigrationDictionaryField field in fields.AsValueEnumerableRequired())
                {
                    if (field == null || !requested.Add(field.FieldName))
                    {
                        throw new InvalidDataException(
                            "Original field names must be non-null and unique."
                        );
                    }
                }
                Dictionary<string, List<OdinMigrationCompiledSourceEdit>> edits = new Dictionary<
                    string,
                    List<OdinMigrationCompiledSourceEdit>
                >(StringComparer.Ordinal);
                HashSet<string> remaining = new HashSet<string>(requested, StringComparer.Ordinal);
                foreach (
                    OdinMigrationCompiledSourceEdit edit in binding.BaseEdits.AsValueEnumerableRequired()
                )
                {
                    Add(edits, edit);
                }
                foreach (
                    OdinMigrationCompiledField field in binding.Fields.AsValueEnumerableRequired()
                )
                {
                    if (field == null || !requested.Contains(field.Name))
                    {
                        continue;
                    }
                    if (!string.IsNullOrEmpty(field.RewriteFailure) || field.SourceEdits.Count == 0)
                    {
                        throw new InvalidDataException(
                            field.RewriteFailure
                                ?? "The field has no compiler-produced source replacements."
                        );
                    }
                    foreach (
                        OdinMigrationCompiledField other in binding.Fields.AsValueEnumerableRequired()
                    )
                    {
                        if (
                            other != null
                            && string.Equals(
                                other.SourcePath,
                                field.SourcePath,
                                StringComparison.Ordinal
                            )
                            && other.TypeStart == field.TypeStart
                            && other.TypeLength == field.TypeLength
                            && !requested.Contains(other.Name)
                        )
                        {
                            throw new InvalidDataException(
                                "Every field in a shared dictionary declaration must be requested together."
                            );
                        }
                    }
                    foreach (
                        OdinMigrationCompiledSourceEdit edit in field.SourceEdits.AsValueEnumerableRequired()
                    )
                    {
                        Add(edits, edit);
                    }
                    remaining.Remove(field.Name);
                }
                if (remaining.Count != 0)
                {
                    throw new InvalidDataException(
                        "A requested field has no original compiler declaration."
                    );
                }
                Dictionary<string, OdinMigrationSourceProposal> sources = new Dictionary<
                    string,
                    OdinMigrationSourceProposal
                >(StringComparer.Ordinal);
                foreach (
                    OdinMigrationCompiledSource captured in binding.Sources.AsValueEnumerableRequired()
                )
                {
                    string original = OdinMigrationSourceBinder.ReadCompiledSource(captured);
                    if (
                        !edits.TryGetValue(
                            captured.Path,
                            out List<OdinMigrationCompiledSourceEdit> replacements
                        )
                    )
                    {
                        continue;
                    }
                    replacements.Sort(CompareDescending);
                    StringBuilder proposed = new StringBuilder(original);
                    int boundary = original.Length;
                    int priorStart = -1;
                    foreach (OdinMigrationCompiledSourceEdit edit in replacements)
                    {
                        if (
                            edit.Start < 0
                            || edit.Start == priorStart
                            || edit.Length < 0
                            || original.Length - edit.Start < edit.Length
                            || boundary - edit.Start < edit.Length
                            || edit.Replacement == null
                        )
                        {
                            throw new InvalidDataException(
                                "Source replacements overlap or fall outside the compiled snapshot."
                            );
                        }
                        proposed.Remove(edit.Start, edit.Length);
                        proposed.Insert(edit.Start, edit.Replacement);
                        boundary = edit.Start;
                        priorStart = edit.Start;
                    }
                    sources.Add(
                        captured.Path,
                        new OdinMigrationSourceProposal(
                            captured.Path,
                            original,
                            proposed.ToString()
                        )
                    );
                    edits.Remove(captured.Path);
                }
                if (edits.Count != 0 || sources.Count == 0)
                {
                    throw new InvalidDataException(
                        "A proposed source replacement has no captured original file."
                    );
                }
                preview = new OdinMigrationDictionarySourcePreview(binding, sources);
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                preview = null;
                failure = exception.Message;
                return false;
            }
        }

        private static int CompareDescending(
            OdinMigrationCompiledSourceEdit left,
            OdinMigrationCompiledSourceEdit right
        )
        {
            int start = right.Start.CompareTo(left.Start);
            return start != 0 ? start : right.Length.CompareTo(left.Length);
        }

        private static void Add(
            Dictionary<string, List<OdinMigrationCompiledSourceEdit>> files,
            OdinMigrationCompiledSourceEdit edit
        )
        {
            if (edit == null || string.IsNullOrWhiteSpace(edit.SourcePath))
            {
                throw new InvalidDataException("A source replacement is incomplete.");
            }
            if (
                !files.TryGetValue(edit.SourcePath, out List<OdinMigrationCompiledSourceEdit> edits)
            )
            {
                edits = new List<OdinMigrationCompiledSourceEdit>();
                files.Add(edit.SourcePath, edits);
            }
            foreach (OdinMigrationCompiledSourceEdit existing in edits)
            {
                if (
                    existing.Start == edit.Start
                    && existing.Length == edit.Length
                    && string.Equals(
                        existing.Replacement,
                        edit.Replacement,
                        StringComparison.Ordinal
                    )
                )
                {
                    return;
                }
            }
            edits.Add(edit);
        }
    }
}
