// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One authored dictionary whose keys and values no longer describe the same mapping.
    /// </summary>
    public readonly struct SerializableDictionaryAssetFinding
    {
        /// <summary>Initializes a new instance of the <see cref="SerializableDictionaryAssetFinding"/> struct.</summary>
        /// <param name="assetPath">The asset carrying the dictionary.</param>
        /// <param name="lineNumber">The one-based line the evidence is on.</param>
        /// <param name="problem">Which state the dictionary is in.</param>
        /// <param name="detail">What the scan measured, in the caller's own words.</param>
        public SerializableDictionaryAssetFinding(
            string assetPath,
            int lineNumber,
            SerializableDictionaryAssetProblem problem,
            string detail
        )
        {
            AssetPath = assetPath;
            LineNumber = lineNumber;
            Problem = problem;
            Detail = detail;
        }

        /// <summary>The asset carrying the dictionary.</summary>
        public string AssetPath { get; }

        /// <summary>The one-based line the evidence is on.</summary>
        public int LineNumber { get; }

        /// <summary>Which state the dictionary is in.</summary>
        public SerializableDictionaryAssetProblem Problem { get; }

        /// <summary>What the scan measured.</summary>
        public string Detail { get; }

        /// <summary>Renders the finding as a location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"{AssetPath}:{LineNumber}: {Problem} -- {Detail}";
        }
    }

    /// <summary>
    /// Reports authored <c>SerializableDictionary</c> blocks that will not load as they were written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <em>type</em> no longer drops its values. Nothing looks at the <em>assets</em> that were
    /// authored while it did, or at the neighboring hole the type cannot prevent: an unassigned
    /// inspector slot stores a real key beside a null reference, so <c>TryGetValue</c> returns
    /// <c>true</c> with a null and a designer who adds a key and forgets the clip gets an entry
    /// that looks correct and does nothing.
    /// </para>
    /// <para>
    /// The scan is text rather than <c>SerializedObject</c>, because loading the asset asks the same
    /// serializer that dropped the values what it thinks they are, and it answers "there is no such
    /// field" -- indistinguishable from an empty dictionary. Text also reaches a baked scene without
    /// opening one.
    /// </para>
    /// <para>
    /// A dictionary whose value type is itself a collection stores its values in
    /// <c>_boxedValues</c>, because Unity drops an array whose element type is a collection. So
    /// "no <c>_values</c>" is not by itself a defect, and a check that assumed it were would report
    /// every such dictionary in the project. The carrying array is whichever of the two is present.
    /// </para>
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
            dictionariesInspected = 0;
            if (assetPaths == null || findings == null)
            {
                return false;
            }

            findings.Clear();
            for (int index = 0; index < assetPaths.Count; ++index)
            {
                string assetPath = assetPaths[index];
                if (
                    !AuthoredAssetYaml.TryReadDocuments(
                        assetPath,
                        out IReadOnlyList<string> lines,
                        out IReadOnlyList<AuthoredAssetDocument> documents
                    )
                )
                {
                    continue;
                }

                for (int document = 0; document < documents.Count; ++document)
                {
                    dictionariesInspected += JudgeDocument(
                        assetPath,
                        lines,
                        documents[document],
                        findings
                    );
                }
            }

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

        private static bool TryFindSibling(
            IReadOnlyList<AuthoredAssetEntry> entries,
            int anchor,
            string key,
            out AuthoredAssetEntry sibling
        )
        {
            sibling = default;
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

            return false;
        }

        private static bool TryCountElements(
            IReadOnlyList<string> lines,
            AuthoredAssetEntry entry,
            out int count
        )
        {
            count = 0;
            if (AuthoredAssetYaml.IsEmptySequence(entry.InlineValue))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(entry.InlineValue))
            {
                return false;
            }

            foreach (
                AuthoredSequenceElement _ in AuthoredAssetYaml.EnumerateSequenceElements(
                    lines,
                    entry
                )
            )
            {
                ++count;
            }

            return true;
        }
    }
#endif
}
