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
    using WallstopStudios.UnityHelpers.Editor.Validation;

    internal static class OdinMigrationDictionaryMigrationBuilder
    {
        private const int MaximumAssetBytes = 16 * 1024 * 1024 + 4;
        private const int ReadBufferBytes = 4096;
        private const int MaximumAssetCharacters = 4 * 1024 * 1024;
        private const int MaximumAssetLines = 65536;
        private const int MaximumTargets = 4096;

        internal static bool TryPreview(
            IReadOnlyList<OdinMigrationDictionaryTarget> targets,
            out OdinMigrationDictionaryMigrationPreview preview,
            out string failure,
            Func<string, int, int, bool> shouldCancel
        )
        {
            try
            {
                if (targets == null || targets.Count == 0)
                {
                    throw new InvalidDataException(
                        "At least one retained dictionary document is required."
                    );
                }
                Dictionary<string, List<OdinMigrationDictionaryTarget>> grouped = new Dictionary<
                    string,
                    List<OdinMigrationDictionaryTarget>
                >(StringComparer.Ordinal);
                int delivered = 0;
                List<OdinMigrationDictionaryTarget> capturedTargets =
                    new List<OdinMigrationDictionaryTarget>();
                foreach (
                    OdinMigrationDictionaryTarget target in targets.AsValueEnumerableRequired()
                )
                {
                    if (target == null || MaximumTargets <= delivered)
                    {
                        throw new InvalidDataException(
                            "Dictionary document targets are null or exceed the preview limit."
                        );
                    }
                    string path = target.AssetPath;
                    OdinMigrationTool.GetValidatedAssetPath(path);
                    if (
                        !(
                            path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                            || path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    {
                        throw new InvalidDataException(
                            "A dictionary target must name a Unity asset, prefab, or scene."
                        );
                    }
                    if (
                        !grouped.TryGetValue(
                            path,
                            out List<OdinMigrationDictionaryTarget> selections
                        )
                    )
                    {
                        selections = new List<OdinMigrationDictionaryTarget>();
                        grouped.Add(path, selections);
                    }
                    selections.Add(target);
                    capturedTargets.Add(target);
                    ++delivered;
                }
                Dictionary<string, OdinMigrationAssetProposal> assets = new Dictionary<
                    string,
                    OdinMigrationAssetProposal
                >(StringComparer.Ordinal);
                Dictionary<
                    OdinMigrationCompiledBinding,
                    List<OdinMigrationDictionaryField>
                > fieldsByBinding =
                    new Dictionary<
                        OdinMigrationCompiledBinding,
                        List<OdinMigrationDictionaryField>
                    >();
                int index = 0;
                foreach (KeyValuePair<string, List<OdinMigrationDictionaryTarget>> group in grouped)
                {
                    if (shouldCancel != null && shouldCancel(group.Key, index, grouped.Count))
                    {
                        throw new OperationCanceledException(
                            "The paired dictionary preview was cancelled."
                        );
                    }
                    string original = ReadAsset(group.Key);
                    List<string> lines = new List<string>();
                    List<int> offsets = new List<int>();
                    SplitLines(original, lines, offsets);
                    Dictionary<long, AuthoredAssetDocument> documents =
                        new Dictionary<long, AuthoredAssetDocument>();
                    foreach (
                        AuthoredAssetDocument document in AuthoredAssetYaml
                            .ReadDocuments(lines)
                            .AsValueEnumerableRequired()
                    )
                    {
                        if (!documents.TryAdd(document.FileId, document))
                        {
                            throw new InvalidDataException(
                                "A Unity document file identifier occurs more than once."
                            );
                        }
                    }
                    List<OdinMigrationCompiledSourceEdit> edits =
                        new List<OdinMigrationCompiledSourceEdit>();
                    HashSet<long> selectedIds = new HashSet<long>();
                    foreach (OdinMigrationDictionaryTarget target in group.Value)
                    {
                        if (!selectedIds.Add(target.FileId))
                        {
                            throw new InvalidDataException(
                                "A retained dictionary document is selected more than once."
                            );
                        }
                        if (
                            !documents.TryGetValue(
                                target.FileId,
                                out AuthoredAssetDocument document
                            )
                            || document.UnityTypeId != 114
                            || !string.Equals(
                                document.RootKey,
                                "MonoBehaviour",
                                StringComparison.Ordinal
                            )
                        )
                        {
                            throw new InvalidDataException(
                                "The selected Unity document has no original MonoBehaviour payload."
                            );
                        }
                        int documentStart = offsets[document.StartLineNumber - 1];
                        int documentEnd = offsets[document.EndLineNumber - 1];
                        string documentSource = original.Substring(
                            documentStart,
                            documentEnd - documentStart
                        );
                        if (
                            !OdinMigrationTool.TryReadSerializedNodes(
                                documentSource,
                                out IReadOnlyList<OdinMigrationSerializedData> payloads,
                                out string readFailure
                            )
                            || payloads.Count != 1
                        )
                        {
                            throw new InvalidDataException(
                                readFailure
                                    ?? "The selected Unity document must have exactly one retained payload."
                            );
                        }
                        OdinMigrationSerializedData payload = payloads[0];
                        foreach (
                            OdinMigrationDictionaryField field in target.Fields.AsValueEnumerableRequired()
                        )
                        {
                            if (
                                field != null
                                && document.TryGetEntry(
                                    field.FieldName,
                                    out AuthoredAssetEntry existing
                                )
                                && existing.Indent == 2
                            )
                            {
                                throw new InvalidDataException(
                                    "A converted dictionary field already exists in the owning Unity document."
                                );
                            }
                        }
                        if (
                            !OdinMigrationTool.TryConvertBoundSerializedDictionaries(
                                payload,
                                target.Fields,
                                group.Key,
                                target.ReferencedObjects,
                                out OdinMigrationCompiledBinding binding,
                                out string fragment,
                                out string conversionFailure
                            )
                        )
                        {
                            throw new InvalidDataException(conversionFailure);
                        }
                        if (
                            !payload.Source.StartsWith(
                                "  serializationData:",
                                StringComparison.Ordinal
                            )
                            && !payload.Source.StartsWith(
                                "  _serializationData:",
                                StringComparison.Ordinal
                            )
                        )
                        {
                            throw new InvalidDataException(
                                "The retained payload must be a direct field of its owning Unity document."
                            );
                        }
                        int start = offsets[document.StartLineNumber + payload.Line - 2];
                        int end = start + payload.Source.Length;
                        if (documentEnd < end)
                        {
                            throw new InvalidDataException(
                                "The retained payload extends beyond its owning document."
                            );
                        }
                        if (
                            !string.Equals(
                                original.Substring(start, end - start),
                                payload.Source,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            throw new InvalidDataException(
                                "The retained payload does not match its original asset text."
                            );
                        }
                        string newline = Newline(payload.Source);
                        edits.Add(
                            new OdinMigrationCompiledSourceEdit(
                                group.Key,
                                start,
                                end - start,
                                fragment.Replace("\n", newline)
                            )
                        );
                        if (
                            !fieldsByBinding.TryGetValue(
                                binding,
                                out List<OdinMigrationDictionaryField> fields
                            )
                        )
                        {
                            fields = new List<OdinMigrationDictionaryField>();
                            fieldsByBinding.Add(binding, fields);
                        }
                        foreach (
                            OdinMigrationDictionaryField field in target.Fields.AsValueEnumerableRequired()
                        )
                        {
                            bool captured = false;
                            foreach (OdinMigrationDictionaryField previous in fields)
                            {
                                if (
                                    string.Equals(
                                        previous.FieldName,
                                        field.FieldName,
                                        StringComparison.Ordinal
                                    )
                                )
                                {
                                    captured = true;
                                    break;
                                }
                            }
                            if (!captured)
                            {
                                fields.Add(field);
                            }
                        }
                    }
                    edits.Sort(CompareDescending);
                    StringBuilder proposed = new StringBuilder(original);
                    int boundary = original.Length;
                    foreach (OdinMigrationCompiledSourceEdit edit in edits)
                    {
                        if (boundary - edit.Start < edit.Length)
                        {
                            throw new InvalidDataException(
                                "Retained document conversions overlap."
                            );
                        }
                        proposed
                            .Remove(edit.Start, edit.Length)
                            .Insert(edit.Start, edit.Replacement);
                        boundary = edit.Start;
                    }
                    assets.Add(
                        group.Key,
                        new OdinMigrationAssetProposal(
                            group.Key,
                            original,
                            proposed.ToString(),
                            selectedIds.Count
                        )
                    );
                    ++index;
                }
                Dictionary<
                    OdinMigrationCompiledBinding,
                    IReadOnlyList<OdinMigrationDictionaryField>
                > selectionsByBinding =
                    new Dictionary<
                        OdinMigrationCompiledBinding,
                        IReadOnlyList<OdinMigrationDictionaryField>
                    >();
                List<OdinMigrationCompiledBinding> bindings =
                    new List<OdinMigrationCompiledBinding>();
                foreach (
                    KeyValuePair<
                        OdinMigrationCompiledBinding,
                        List<OdinMigrationDictionaryField>
                    > pair in fieldsByBinding
                )
                {
                    selectionsByBinding.Add(pair.Key, pair.Value.AsReadOnly());
                    bindings.Add(pair.Key);
                }
                if (
                    !OdinMigrationDictionarySourceRewriter.TryMerge(
                        selectionsByBinding,
                        out Dictionary<string, OdinMigrationSourceProposal> sources,
                        out string sourceFailure
                    )
                )
                {
                    throw new InvalidDataException(sourceFailure);
                }
                preview = new OdinMigrationDictionaryMigrationPreview(
                    sources,
                    assets,
                    bindings,
                    capturedTargets
                );
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

        internal static string ReadAsset(string assetPath)
        {
            string path = OdinMigrationTool.GetValidatedAssetPath(assetPath);
            using (FileStream stream = File.OpenRead(path))
            using (MemoryStream delivered = new MemoryStream())
            {
                byte[] buffer = new byte[ReadBufferBytes];
                int count;
                while (0 < (count = stream.Read(buffer, 0, buffer.Length)))
                {
                    if (MaximumAssetBytes - delivered.Length < count)
                    {
                        throw new InvalidDataException(
                            "The retained asset exceeds the paired preview size limit."
                        );
                    }
                    delivered.Write(buffer, 0, count);
                }
                if (
                    !OdinMigrationEncodedSource.TryDecode(
                        delivered.ToArray(),
                        out OdinMigrationDecodedSource decoded,
                        out string failure
                    )
                )
                {
                    throw new InvalidDataException(failure);
                }
                if (
                    MaximumAssetCharacters < decoded.Source.Length
                    || !OdinMigrationSerializedDataScanner.LooksLikeUnityYaml(decoded.Source)
                )
                {
                    throw new InvalidDataException(
                        "Expected Unity text YAML within the paired preview character limit."
                    );
                }
                return decoded.Source;
            }
        }

        internal static IReadOnlyList<AuthoredAssetDocument> ReadDocuments(string source)
        {
            List<string> lines = new List<string>();
            List<int> offsets = new List<int>();
            SplitLines(source, lines, offsets);
            return AuthoredAssetYaml.ReadDocuments(lines);
        }

        private static int CompareDescending(
            OdinMigrationCompiledSourceEdit left,
            OdinMigrationCompiledSourceEdit right
        )
        {
            return right.Start.CompareTo(left.Start);
        }

        private static string Newline(string source)
        {
            int length = source.Length;
            for (int index = 0; index < length; ++index)
            {
                if (source[index] == '\n')
                {
                    return "\n";
                }
                if (source[index] == '\r')
                {
                    return index + 1 < length && source[index + 1] == '\n' ? "\r\n" : "\r";
                }
            }
            return "\n";
        }

        private static void SplitLines(string source, List<string> lines, List<int> offsets)
        {
            int start = 0;
            int length = source.Length;
            for (int index = 0; index < length; ++index)
            {
                if (source[index] != '\r' && source[index] != '\n')
                {
                    continue;
                }
                if (MaximumAssetLines <= lines.Count)
                {
                    throw new InvalidDataException(
                        "The retained asset exceeds the paired preview line limit."
                    );
                }
                offsets.Add(start);
                lines.Add(source.Substring(start, index - start));
                if (source[index] == '\r' && index + 1 < length && source[index + 1] == '\n')
                {
                    ++index;
                }
                start = index + 1;
            }
            if (MaximumAssetLines <= lines.Count)
            {
                throw new InvalidDataException(
                    "The retained asset exceeds the paired preview line limit."
                );
            }
            offsets.Add(start);
            lines.Add(source.Substring(start));
            offsets.Add(length);
        }
    }
}
