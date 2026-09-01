// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Reports authored <c>SerializableDictionary</c> blocks that will not load as they were written.
    /// </summary>
    /// <remarks>
    /// A dictionary whose value type is itself a collection stores its values in
    /// <c>_boxedValues</c>, so "no <c>_values</c>" is not by itself a defect and the carrying array
    /// is whichever of the two is present. See
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/docs/features/editor-tools/authored-asset-validation.md">Authored Asset Validation</see>.
    /// </remarks>
    public static class SerializableDictionaryAssetValidator
    {
        /// <summary>The key Unity writes a dictionary's keys under.</summary>
        public const string KeysKey = "_keys";

        /// <summary>The key Unity writes a dictionary's values under.</summary>
        public const string ValuesKey = "_values";

        /// <summary>The key Unity writes a dictionary's boxed values under.</summary>
        public const string BoxedValuesKey = "_boxedValues";

        /// <summary>
        /// Reports every authored dictionary in <paramref name="assetPaths"/> that lost its pairing.
        /// </summary>
        /// <param name="assetPaths">The committed assets to read.</param>
        /// <param name="findings">Receives one entry per defect.</param>
        /// <param name="dictionariesInspected">Receives how many <c>_keys</c> blocks were judged.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        /// <remarks>
        /// The count is an output because zero findings is what a passing scan reports and also what
        /// a scan whose subject list stopped matching reports. A caller that asserts the count is
        /// non-zero cannot be made green by a moved root or a renamed backing field.
        /// </remarks>
        public static bool TryScan(
            IReadOnlyList<string> assetPaths,
            List<SerializableDictionaryAssetFinding> findings,
            out int dictionariesInspected
        )
        {
            if (assetPaths == null || findings == null)
            {
                dictionariesInspected = 0;
                return false;
            }

            int inspected = 0;
            findings.Clear();
            for (int index = 0; index < assetPaths.Count; ++index)
            {
                string assetPath = assetPaths[index];
                if (
                    !AuthoredAssetYaml.TryReadDocuments(
                        AuthoredAssetPaths.ToFileSystemPath(assetPath),
                        out IReadOnlyList<string> lines,
                        out IReadOnlyList<AuthoredAssetDocument> documents
                    )
                )
                {
                    continue;
                }

                for (int document = 0; document < documents.Count; ++document)
                {
                    inspected += JudgeDocument(assetPath, lines, documents[document], findings);
                }
            }

            dictionariesInspected = inspected;
            return true;
        }

        private static int JudgeDocument(
            string assetPath,
            IReadOnlyList<string> lines,
            AuthoredAssetDocument document,
            List<SerializableDictionaryAssetFinding> findings
        )
        {
            int inspected = 0;
            IReadOnlyList<AuthoredAssetEntry> entries = document.Entries;
            for (int index = 0; index < entries.Count; ++index)
            {
                AuthoredAssetEntry keys = entries[index];
                if (!string.Equals(keys.Key, KeysKey, StringComparison.Ordinal))
                {
                    continue;
                }

                ++inspected;
                bool hasValues = TryFindSibling(
                    entries,
                    index,
                    ValuesKey,
                    out AuthoredAssetEntry values
                );
                bool hasBoxed = TryFindSibling(
                    entries,
                    index,
                    BoxedValuesKey,
                    out AuthoredAssetEntry boxed
                );

                if (!hasValues && !hasBoxed)
                {
                    findings.Add(
                        new SerializableDictionaryAssetFinding(
                            assetPath,
                            keys.LineNumber,
                            SerializableDictionaryAssetProblem.ValuesDropped,
                            "_keys was written and neither _values nor _boxedValues is present, so "
                                + "every lookup against this dictionary misses."
                        )
                    );
                    continue;
                }

                if (!TryCountElements(lines, keys, out int keyCount))
                {
                    continue;
                }

                AuthoredAssetEntry carrier = hasValues ? values : boxed;
                if (!TryCountElements(lines, carrier, out int valueCount))
                {
                    continue;
                }

                if (keyCount != valueCount)
                {
                    findings.Add(
                        new SerializableDictionaryAssetFinding(
                            assetPath,
                            keys.LineNumber,
                            SerializableDictionaryAssetProblem.ValueCountMismatch,
                            $"{keyCount} keys against {valueCount} values in {carrier.Key}, so the "
                                + "pairing is not recoverable and the dictionary loads empty."
                        )
                    );
                    continue;
                }

                ReportNullValues(assetPath, lines, carrier, findings);
            }

            return inspected;
        }

        private static void ReportNullValues(
            string assetPath,
            IReadOnlyList<string> lines,
            AuthoredAssetEntry carrier,
            List<SerializableDictionaryAssetFinding> findings
        )
        {
            foreach (
                AuthoredSequenceElement element in AuthoredAssetYaml.EnumerateSequenceElements(
                    lines,
                    carrier
                )
            )
            {
                if (!AuthoredAssetYaml.IsNullObjectReference(element.Value))
                {
                    continue;
                }

                findings.Add(
                    new SerializableDictionaryAssetFinding(
                        assetPath,
                        element.LineNumber,
                        SerializableDictionaryAssetProblem.NullValueBesideKey,
                        "a real key is paired with a value naming no object, so TryGetValue returns "
                            + "true with a null."
                    )
                );
            }
        }

        internal static bool TryFindSibling(
            IReadOnlyList<AuthoredAssetEntry> entries,
            int anchor,
            string key,
            out AuthoredAssetEntry sibling
        )
        {
            int indent = entries[anchor].Indent;

            for (int index = anchor + 1; index < entries.Count; ++index)
            {
                AuthoredAssetEntry candidate = entries[index];
                if (candidate.Indent < indent)
                {
                    break;
                }

                if (
                    candidate.Indent == indent
                    && string.Equals(candidate.Key, key, StringComparison.Ordinal)
                )
                {
                    sibling = candidate;
                    return true;
                }
            }

            for (int index = anchor - 1; 0 <= index; --index)
            {
                AuthoredAssetEntry candidate = entries[index];
                if (candidate.Indent < indent)
                {
                    break;
                }

                if (
                    candidate.Indent == indent
                    && string.Equals(candidate.Key, key, StringComparison.Ordinal)
                )
                {
                    sibling = candidate;
                    return true;
                }
            }

            sibling = default;
            return false;
        }

        internal static bool TryCountElements(
            IReadOnlyList<string> lines,
            AuthoredAssetEntry entry,
            out int count
        )
        {
            if (AuthoredAssetYaml.IsEmptySequence(entry.InlineValue))
            {
                count = 0;
                return true;
            }

            if (!string.IsNullOrEmpty(entry.InlineValue))
            {
                count = 0;
                return false;
            }

            int found = 0;
            foreach (
                AuthoredSequenceElement _ in AuthoredAssetYaml.EnumerateSequenceElements(
                    lines,
                    entry
                )
            )
            {
                ++found;
            }

            count = found;
            return true;
        }
    }
#endif
}
