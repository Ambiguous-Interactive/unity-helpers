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
            if (binding == null || fields == null || fields.Count == 0)
            {
                preview = null;
                failure = "A compiled binding and original dictionary schemas are required.";
                return false;
            }
            Dictionary<
                OdinMigrationCompiledBinding,
                IReadOnlyList<OdinMigrationDictionaryField>
            > selections = new Dictionary<
                OdinMigrationCompiledBinding,
                IReadOnlyList<OdinMigrationDictionaryField>
            >
            {
                { binding, fields },
            };
            if (
                !TryMerge(
                    selections,
                    out Dictionary<string, OdinMigrationSourceProposal> sources,
                    out failure
                )
            )
            {
                preview = null;
                return false;
            }
            preview = new OdinMigrationDictionarySourcePreview(binding, sources);
            return true;
        }

        internal static bool TryMerge(
            IReadOnlyDictionary<
                OdinMigrationCompiledBinding,
                IReadOnlyList<OdinMigrationDictionaryField>
            > selections,
            out Dictionary<string, OdinMigrationSourceProposal> merged,
            out string failure
        )
        {
            try
            {
                if (selections == null || selections.Count == 0)
                {
                    throw new InvalidDataException(
                        "Compiled bindings and original dictionary schemas are required."
                    );
                }
                Dictionary<string, List<OdinMigrationCompiledSourceEdit>> edits = new Dictionary<
                    string,
                    List<OdinMigrationCompiledSourceEdit>
                >(StringComparer.Ordinal);
                Dictionary<string, OdinMigrationCompiledSource> snapshots = new Dictionary<
                    string,
                    OdinMigrationCompiledSource
                >(StringComparer.Ordinal);
                foreach (
                    KeyValuePair<
                        OdinMigrationCompiledBinding,
                        IReadOnlyList<OdinMigrationDictionaryField>
                    > selection in selections.AsValueEnumerableRequired()
                )
                {
                    Collect(selection.Key, selection.Value, edits);
                    foreach (
                        OdinMigrationCompiledSource source in selection.Key.Sources.AsValueEnumerableRequired()
                    )
                    {
                        if (source == null || string.IsNullOrWhiteSpace(source.Path))
                        {
                            throw new InvalidDataException(
                                "A compiled source snapshot is incomplete."
                            );
                        }
                        if (
                            snapshots.TryGetValue(
                                source.Path,
                                out OdinMigrationCompiledSource existing
                            )
                            && !string.Equals(
                                existing.Checksum,
                                source.Checksum,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            throw new InvalidDataException(
                                "Selected bindings disagree on an original source snapshot."
                            );
                        }
                        snapshots[source.Path] = source;
                    }
                }
                Dictionary<string, OdinMigrationSourceProposal> sources = new Dictionary<
                    string,
                    OdinMigrationSourceProposal
                >(StringComparer.Ordinal);
                foreach (OdinMigrationCompiledSource captured in snapshots.Values)
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
                merged = sources;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                merged = null;
                failure = exception.Message;
                return false;
            }
        }

        private static void Collect(
            OdinMigrationCompiledBinding binding,
            IReadOnlyList<OdinMigrationDictionaryField> fields,
            Dictionary<string, List<OdinMigrationCompiledSourceEdit>> edits
        )
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
            HashSet<string> remaining = new HashSet<string>(requested, StringComparer.Ordinal);
            foreach (
                OdinMigrationCompiledSourceEdit edit in binding.BaseEdits.AsValueEnumerableRequired()
            )
            {
                Add(edits, edit);
            }
            foreach (OdinMigrationCompiledField field in binding.Fields.AsValueEnumerableRequired())
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
