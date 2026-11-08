// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    public static partial class AuthoredAssetYaml
    {
        /// <summary>Creates a view of the document's direct fields in its original source lines.</summary>
        /// <remarks>The lines must be the unchanged source used to parse the document; invalid bounds return a default region.</remarks>
        public static AuthoredAssetRegion Whole(
            IReadOnlyList<string> lines,
            AuthoredAssetDocument document
        )
        {
            if (
                lines == null
                || document == null
                || document.StartLineNumber < 1
                || document.EndLineNumber <= document.StartLineNumber
                || (long)lines.Count + 1 < document.EndLineNumber
                || !IsDocumentHeader(lines[document.StartLineNumber - 1])
            )
            {
                return default;
            }
            return new AuthoredAssetRegion(
                document,
                lines,
                document.StartLineNumber + 1,
                document.EndLineNumber,
                ColumnStep,
                0
            );
        }

        /// <summary>Reads a unique direct field of a region without searching its descendants.</summary>
        /// <remarks>Names are exact, with an optional trailing colon; absence and duplicate keys return false and an empty value.</remarks>
        public static bool TryGetField(AuthoredAssetRegion region, string field, out string value)
        {
            if (FindRegionEntries(region, field, out AuthoredAssetEntry entry) == 1)
            {
                value = entry.InlineValue;
                return true;
            }
            value = string.Empty;
            return false;
        }

        /// <summary>Finds a nested mapping using candidate names and their explicit legacy spellings.</summary>
        /// <remarks>Candidate order is preserved; duplicate fields, scalar values, sequences, and literal blocks are refused.</remarks>
        public static bool TryDescend(
            AuthoredAssetRegion region,
            IEnumerable<string> names,
            out AuthoredAssetRegion nested
        )
        {
            if (
                !TryFindNamedRegionEntry(region, names, out AuthoredAssetEntry entry)
                || !CanReadBlock(region.SourceLines, entry)
            )
            {
                nested = default;
                return false;
            }
            foreach (
                AuthoredSequenceElement ignored in EnumerateSequenceElements(
                    region.SourceLines,
                    entry
                )
            )
            {
                nested = default;
                return false;
            }
            nested = new AuthoredAssetRegion(
                region.Document,
                region.SourceLines,
                entry.LineNumber + 1,
                entry.EndLineNumber,
                entry.Indent + ColumnStep,
                entry.SequenceDepth
            );
            return true;
        }

        /// <summary>Returns one bounded region per direct block-sequence item under a named field.</summary>
        /// <remarks>Candidate names include explicit legacy spellings; invalid or ambiguous fields return an empty list.</remarks>
        public static IReadOnlyList<AuthoredAssetRegion> Items(
            AuthoredAssetRegion region,
            IEnumerable<string> names
        )
        {
            if (!TryFindNamedRegionEntry(region, names, out AuthoredAssetEntry entry))
            {
                return Array.Empty<AuthoredAssetRegion>();
            }
            IReadOnlyList<AuthoredSequenceElement> starts = ReadBlockItems(
                region.SourceLines,
                entry
            );
            int count = starts.Count;
            List<AuthoredAssetRegion> items = new(count);
            for (int index = 0; index < count; ++index)
            {
                int end = index + 1 < count ? starts[index + 1].LineNumber : entry.EndLineNumber;
                AuthoredAssetRegion item = new AuthoredAssetRegion(
                    region.Document,
                    region.SourceLines,
                    starts[index].LineNumber,
                    end,
                    entry.Indent + ColumnStep,
                    entry.SequenceDepth + 1
                );
                items.Add(item);
            }
            return items;
        }

        /// <summary>Counts direct or nested block markers under one unique direct region field.</summary>
        public static int CountBlockItems(
            AuthoredAssetRegion region,
            string field,
            bool includeNested = true
        )
        {
            return FindRegionEntries(region, field, out AuthoredAssetEntry entry) == 1
                ? CountBlockItems(region.SourceLines, entry, includeNested)
                : 0;
        }

        /// <summary>Joins a unique region field's inline value and its direct child values.</summary>
        /// <remarks>Nested descendants, sequence members, literal block text, and serializedVersion are excluded.</remarks>
        public static string ValueWithDirectChildren(AuthoredAssetRegion region, string field)
        {
            if (FindRegionEntries(region, field, out AuthoredAssetEntry parent) != 1)
            {
                return string.Empty;
            }
            using PooledResource<StringBuilder> lease = Buffers.StringBuilder.Get(
                out StringBuilder builder
            );
            builder.Append(parent.InlineValue);
            foreach (
                AuthoredAssetEntry entry in region.Document.Entries.AsValueEnumerableRequired()
            )
            {
                if (
                    region.EndLineNumber <= entry.LineNumber
                    || !IsDirectChild(entry, parent)
                    || string.Equals(entry.Key, "serializedVersion", StringComparison.Ordinal)
                    || string.IsNullOrEmpty(entry.InlineValue)
                )
                {
                    continue;
                }
                if (0 < builder.Length)
                {
                    builder.Append(' ');
                }
                builder.Append(entry.InlineValue);
            }
            return builder.ToString();
        }

        private static int FindRegionEntries(
            AuthoredAssetRegion region,
            string field,
            out AuthoredAssetEntry entry
        )
        {
            if (
                !region.IsValid
                || string.IsNullOrWhiteSpace(field)
                || region.StartLineNumber < 1
                || region.EndLineNumber < region.StartLineNumber
                || (long)region.SourceLines.Count + 1 < region.EndLineNumber
            )
            {
                entry = default;
                return 0;
            }
            bool found = false;
            AuthoredAssetEntry selected = default;
            foreach (
                AuthoredAssetEntry candidate in region.Document.Entries.AsValueEnumerableRequired()
            )
            {
                if (
                    candidate.Key == null
                    || candidate.LineNumber < region.StartLineNumber
                    || region.EndLineNumber <= candidate.LineNumber
                    || candidate.Indent != region.FieldColumn
                    || candidate.SequenceDepth != region.SequenceDepth
                    || !MatchesFieldName(candidate.Key, field)
                )
                {
                    continue;
                }
                if (found)
                {
                    entry = default;
                    return 2;
                }
                selected = candidate;
                found = true;
            }
            if (!found)
            {
                entry = default;
                return 0;
            }
            int end = Math.Min(selected.EndLineNumber, region.EndLineNumber);
            entry = new AuthoredAssetEntry(
                selected.Key,
                selected.InlineValue,
                selected.Indent,
                selected.LineNumber,
                end,
                selected.SequenceDepth
            );
            return 1;
        }

        private static bool TryFindNamedRegionEntry(
            AuthoredAssetRegion region,
            IEnumerable<string> names,
            out AuthoredAssetEntry entry
        )
        {
            if (names != null)
            {
                foreach (string name in names.AsValueEnumerableRequired())
                {
                    foreach (string candidate in SerializedNames(name))
                    {
                        int count = FindRegionEntries(
                            region,
                            candidate,
                            out AuthoredAssetEntry selected
                        );
                        if (count == 2)
                        {
                            entry = default;
                            return false;
                        }
                        if (count == 1)
                        {
                            entry = selected;
                            return true;
                        }
                    }
                }
            }
            entry = default;
            return false;
        }
    }
#endif
}
