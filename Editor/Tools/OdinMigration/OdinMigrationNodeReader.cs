// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    internal static class OdinMigrationNodeReader
    {
        private const int MaximumSourceCharacters = 4 * 1024 * 1024;
        private const int MaximumLines = 65536;
        private const int NodesFormat = 2;
        private const int FirstEntry = 1;
        private const int LastEntry = 16;
        private const string SerializedFormat = "SerializedFormat";
        private const string SerializedBytes = "SerializedBytes";
        private const string SerializedBytesString = "SerializedBytesString";
        private const string SerializationData = "serializationData";
        private const string PrivateSerializationData = "_serializationData";

        internal static bool TryRead(
            string source,
            out IReadOnlyList<OdinMigrationSerializedData> payloads,
            out string failure
        )
        {
            try
            {
                int sourceLength = source == null ? 0 : source.Length;
                if (
                    string.IsNullOrEmpty(source)
                    || MaximumSourceCharacters < sourceLength
                    || !OdinMigrationSerializedDataScanner.LooksLikeUnityYaml(source)
                )
                {
                    throw new InvalidDataException(
                        "Expected Unity text YAML within the source size limit."
                    );
                }
                List<string> lines = new List<string>();
                List<int> offsets = new List<int>();
                int start = 0;
                for (int index = 0; index < sourceLength; ++index)
                {
                    if (source[index] != '\r' && source[index] != '\n')
                    {
                        continue;
                    }
                    AddLine(source, start, index, lines, offsets);
                    if (
                        source[index] == '\r'
                        && index + 1 < sourceLength
                        && source[index + 1] == '\n'
                    )
                    {
                        ++index;
                    }
                    start = index + 1;
                }
                AddLine(source, start, sourceLength, lines, offsets);
                offsets.Add(sourceLength);
                IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(
                    lines
                );
                if (documents.Count == 0)
                {
                    throw new InvalidDataException("No Unity documents were found.");
                }
                List<OdinMigrationSerializedData> parsed = new List<OdinMigrationSerializedData>();
                int remainingSourceCharacters = sourceLength;
                foreach (AuthoredAssetDocument document in documents)
                {
                    IReadOnlyList<AuthoredAssetEntry> entries = document.Entries;
                    int entryCount = entries.Count;
                    for (int entryIndex = 0; entryIndex < entryCount; ++entryIndex)
                    {
                        AuthoredAssetEntry entry = entries[entryIndex];
                        if (
                            !string.Equals(entry.Key, SerializationData, StringComparison.Ordinal)
                            && !string.Equals(
                                entry.Key,
                                PrivateSerializationData,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            continue;
                        }
                        if (!string.IsNullOrEmpty(entry.InlineValue))
                        {
                            throw new InvalidDataException(
                                "Odin payloads must use block mappings."
                            );
                        }
                        Dictionary<string, AuthoredAssetEntry> fields = ReadFields(
                            entries,
                            entryIndex + 1,
                            entry
                        );
                        if (
                            !fields.TryGetValue(SerializedFormat, out AuthoredAssetEntry format)
                            || !string.Equals(
                                ReadScalar(lines, format),
                                NodesFormat.ToString(CultureInfo.InvariantCulture),
                                StringComparison.Ordinal
                            )
                        )
                        {
                            throw new InvalidDataException(
                                "Only Odin Nodes format (2) is supported by this reader."
                            );
                        }
                        foreach (string key in new[] { SerializedBytes, SerializedBytesString })
                        {
                            if (
                                fields.TryGetValue(key, out AuthoredAssetEntry bytes)
                                && ReadScalar(lines, bytes).Length != 0
                            )
                            {
                                throw new InvalidDataException(
                                    "A Nodes payload also contains byte data; it requires review."
                                );
                            }
                        }
                        if (
                            !fields.TryGetValue(
                                nameof(OdinMigrationSerializedData.SerializationNodes),
                                out AuthoredAssetEntry nodesEntry
                            )
                            || !fields.TryGetValue(
                                nameof(OdinMigrationSerializedData.ReferencedUnityObjects),
                                out AuthoredAssetEntry referencesEntry
                            )
                        )
                        {
                            throw new InvalidDataException(
                                "The payload must declare nodes and Unity references."
                            );
                        }
                        List<string> references = ReadReferences(lines, referencesEntry);
                        List<OdinMigrationSerializedNode> nodes = ReadNodes(lines, nodesEntry);
                        int offset = offsets[entry.LineNumber - 1];
                        int payloadLength = offsets[entry.EndLineNumber - 1] - offset;
                        if (remainingSourceCharacters < payloadLength)
                        {
                            throw new InvalidDataException(
                                "Overlapping payload text exceeds the source budget."
                            );
                        }
                        remainingSourceCharacters -= payloadLength;
                        parsed.Add(
                            new OdinMigrationSerializedData(
                                document.FileId,
                                document.ScriptGuid,
                                source.Substring(offset, payloadLength),
                                entry.LineNumber,
                                entry.EndLineNumber,
                                nodes,
                                references
                            )
                        );
                    }
                }
                payloads = parsed.AsReadOnly();
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                payloads = Array.Empty<OdinMigrationSerializedData>();
                failure = exception.Message;
                return false;
            }
        }

        private static void AddLine(
            string source,
            int start,
            int end,
            List<string> lines,
            List<int> offsets
        )
        {
            if (MaximumLines <= lines.Count)
            {
                throw new InvalidDataException("The Unity YAML line limit was exceeded.");
            }
            lines.Add(source.Substring(start, end - start));
            offsets.Add(start);
        }

        private static Dictionary<string, AuthoredAssetEntry> ReadFields(
            IReadOnlyList<AuthoredAssetEntry> entries,
            int start,
            AuthoredAssetEntry parent
        )
        {
            Dictionary<string, AuthoredAssetEntry> fields = new Dictionary<
                string,
                AuthoredAssetEntry
            >(StringComparer.Ordinal);
            int entryCount = entries.Count;
            for (int index = start; index < entryCount; ++index)
            {
                AuthoredAssetEntry entry = entries[index];
                if (parent.EndLineNumber <= entry.LineNumber)
                {
                    break;
                }
                if (
                    parent.LineNumber < entry.LineNumber
                    && entry.LineNumber < parent.EndLineNumber
                    && entry.Indent == parent.Indent + 2
                    && !fields.TryAdd(entry.Key, entry)
                )
                {
                    throw new InvalidDataException("An Odin payload declares a duplicate field.");
                }
            }
            return fields;
        }

        private static List<string> ReadReferences(
            IReadOnlyList<string> lines,
            AuthoredAssetEntry entry
        )
        {
            List<string> references = new List<string>();
            ValidateSequence(entry);
            for (int line = entry.LineNumber; line < entry.EndLineNumber - 1; ++line)
            {
                string content = lines[line].Trim();
                if (content.Length == 0 || content.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                if (
                    entry.InlineValue.Length != 0
                    || AuthoredAssetYaml.LeadingSpaces(lines[line]) != entry.Indent
                    || !content.StartsWith("- ", StringComparison.Ordinal)
                )
                {
                    throw new InvalidDataException(
                        "A Unity reference sequence has unsupported content."
                    );
                }
            }
            foreach (
                AuthoredSequenceElement element in AuthoredAssetYaml.EnumerateSequenceElements(
                    lines,
                    entry
                )
            )
            {
                if (!AuthoredAssetYaml.TryParseObjectReference(element.Value, out _, out _))
                {
                    throw new InvalidDataException(
                        "A Unity reference is not an inline object mapping."
                    );
                }
                references.Add(element.Value);
            }
            return references;
        }

        private static List<OdinMigrationSerializedNode> ReadNodes(
            IReadOnlyList<string> lines,
            AuthoredAssetEntry entry
        )
        {
            List<OdinMigrationSerializedNode> nodes = new List<OdinMigrationSerializedNode>();
            ValidateSequence(entry);
            List<AuthoredSequenceElement> records = new List<AuthoredSequenceElement>();
            bool hasRecord = false;
            for (int line = entry.LineNumber; line < entry.EndLineNumber - 1; ++line)
            {
                string content = lines[line].Trim();
                if (content.Length == 0 || content.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                int indent = AuthoredAssetYaml.LeadingSpaces(lines[line]);
                if (indent == entry.Indent && content.StartsWith("- ", StringComparison.Ordinal))
                {
                    hasRecord = true;
                }
                if (
                    entry.InlineValue.Length != 0
                    || !hasRecord
                    || indent < entry.Indent
                    || (
                        indent == entry.Indent
                        && !content.StartsWith("- ", StringComparison.Ordinal)
                    )
                )
                {
                    throw new InvalidDataException(
                        "A serialization node sequence has unsupported content."
                    );
                }
            }
            foreach (
                AuthoredSequenceElement element in AuthoredAssetYaml.EnumerateSequenceElements(
                    lines,
                    entry
                )
            )
            {
                records.Add(element);
            }
            if (records.Count == 0 && entry.HasBlockValue && entry.InlineValue.Length == 0)
            {
                for (int line = entry.LineNumber; line < entry.EndLineNumber - 1; ++line)
                {
                    string content = lines[line].Trim();
                    if (content.Length != 0 && !content.StartsWith("#", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "No records were read from a nonempty node sequence."
                        );
                    }
                }
            }
            int recordCount = records.Count;
            for (int index = 0; index < recordCount; ++index)
            {
                int endLine =
                    index + 1 < recordCount ? records[index + 1].LineNumber : entry.EndLineNumber;
                Dictionary<string, string> fields = new Dictionary<string, string>(
                    StringComparer.Ordinal
                );
                int recordLine = records[index].LineNumber;
                for (int line = recordLine - 1; line < endLine - 1; ++line)
                {
                    string content =
                        line == recordLine - 1 ? records[index].Value : lines[line].Trim();
                    if (
                        string.IsNullOrWhiteSpace(content)
                        || content.StartsWith("#", StringComparison.Ordinal)
                    )
                    {
                        continue;
                    }
                    if (!AuthoredAssetYaml.TrySplitEntry(content, out string key, out string value))
                    {
                        throw new InvalidDataException(
                            "A serialization record has an invalid mapping."
                        );
                    }
                    if (
                        !string.Equals(
                            key,
                            nameof(OdinMigrationSerializedNode.Name),
                            StringComparison.Ordinal
                        )
                        && !string.Equals(
                            key,
                            nameof(OdinMigrationSerializedNode.Entry),
                            StringComparison.Ordinal
                        )
                        && !string.Equals(
                            key,
                            nameof(OdinMigrationSerializedNode.Data),
                            StringComparison.Ordinal
                        )
                    )
                    {
                        throw new InvalidDataException(
                            "A serialization record has an unknown field."
                        );
                    }
                    int scalarEnd = line + 1;
                    while (
                        scalarEnd < endLine - 1
                        && entry.Indent + 2 < AuthoredAssetYaml.LeadingSpaces(lines[scalarEnd])
                    )
                    {
                        ++scalarEnd;
                    }
                    AuthoredAssetEntry scalar = new AuthoredAssetEntry(
                        key,
                        value,
                        entry.Indent + 2,
                        line + 1,
                        scalarEnd + 1
                    );
                    if (!fields.TryAdd(key, ReadScalar(lines, scalar)))
                    {
                        throw new InvalidDataException(
                            "A serialization record has a duplicate field."
                        );
                    }
                    line = scalarEnd - 1;
                }
                if (
                    fields.Count != 3
                    || !fields.TryGetValue(
                        nameof(OdinMigrationSerializedNode.Name),
                        out string name
                    )
                    || !fields.TryGetValue(
                        nameof(OdinMigrationSerializedNode.Data),
                        out string data
                    )
                    || !fields.TryGetValue(
                        nameof(OdinMigrationSerializedNode.Entry),
                        out string tag
                    )
                    || !int.TryParse(
                        tag,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out int kind
                    )
                    || kind < FirstEntry
                    || LastEntry < kind
                )
                {
                    throw new InvalidDataException(
                        "A serialization record needs Name, a valid Entry tag, and Data."
                    );
                }
                nodes.Add(new OdinMigrationSerializedNode(name, kind, data, recordLine));
            }
            return nodes;
        }

        private static void ValidateSequence(AuthoredAssetEntry entry)
        {
            if (
                entry.InlineValue.Length != 0
                && !AuthoredAssetYaml.IsEmptySequence(entry.InlineValue)
            )
            {
                throw new InvalidDataException(
                    "Expected a block sequence or an empty inline sequence."
                );
            }
        }

        private static string ReadScalar(IReadOnlyList<string> lines, AuthoredAssetEntry entry)
        {
            string value = entry.InlineValue;
            int valueLength = value.Length;
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                if (entry.HasBlockValue)
                {
                    throw new InvalidDataException(
                        "Multiline quoted strings require YAML-aware review."
                    );
                }
                return JsonSerializer.Deserialize<string>(value);
            }
            if (value.StartsWith("'", StringComparison.Ordinal))
            {
                if (entry.HasBlockValue || valueLength < 2 || value[valueLength - 1] != '\'')
                {
                    throw new InvalidDataException("An Odin scalar has invalid quoting.");
                }
                for (int index = 1; index < valueLength - 1; ++index)
                {
                    if (value[index] != '\'')
                    {
                        continue;
                    }
                    if (valueLength - 1 <= index + 1 || value[index + 1] != '\'')
                    {
                        throw new InvalidDataException("An Odin scalar has an unescaped quote.");
                    }
                    ++index;
                }
                return value.Substring(1, valueLength - 2).Replace("''", "'");
            }
            if (
                valueLength != 0
                && (
                    value[0] == '|'
                    || value[0] == '>'
                    || value[0] == '&'
                    || value[0] == '*'
                    || value[0] == '!'
                    || value[0] == '{'
                    || value[0] == '['
                    || value[0] == '#'
                )
            )
            {
                throw new InvalidDataException(
                    "Block scalars, aliases, anchors, and tags require YAML-aware review."
                );
            }
            if (
                0 <= value.IndexOf(" #", StringComparison.Ordinal)
                || 0 <= value.IndexOf(": ", StringComparison.Ordinal)
            )
            {
                throw new InvalidDataException("Inline scalar comments require YAML-aware review.");
            }
            StringBuilder folded = new StringBuilder(value);
            for (int line = entry.LineNumber; line < entry.EndLineNumber - 1; ++line)
            {
                string continuation = lines[line].Trim();
                if (
                    continuation.Length == 0
                    || continuation.StartsWith("#", StringComparison.Ordinal)
                    || 0 <= continuation.IndexOf(" #", StringComparison.Ordinal)
                    || 0 <= continuation.IndexOf(": ", StringComparison.Ordinal)
                )
                {
                    throw new InvalidDataException(
                        "Blank lines and comments inside folded scalars require review."
                    );
                }
                folded.Append(' ');
                folded.Append(continuation);
            }
            return folded.ToString();
        }
    }
}
