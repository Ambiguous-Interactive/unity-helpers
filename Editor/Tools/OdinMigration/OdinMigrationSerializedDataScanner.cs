// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    internal static class OdinMigrationSerializedDataScanner
    {
        private const string SerializationData = "serializationData";
        private const string PrivateSerializationData = "_serializationData";
        private const string PropertyPath = "propertyPath";

        internal static bool TryAnalyze(
            string source,
            out IReadOnlyList<OdinMigrationFinding> findings
        )
        {
            List<OdinMigrationFinding> parsedFindings = new List<OdinMigrationFinding>();
            if (string.IsNullOrEmpty(source))
            {
                findings = parsedFindings;
                return false;
            }

            List<string> lines = new List<string>();
            using (StringReader reader = new StringReader(source))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }
            }

            IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(lines);
            int documentCount = documents.Count;
            if (documentCount == 0)
            {
                findings = parsedFindings;
                return false;
            }
            for (int documentIndex = 0; documentIndex < documentCount; documentIndex++)
            {
                IReadOnlyList<AuthoredAssetEntry> entries = documents[documentIndex].Entries;
                int entryCount = entries.Count;
                for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
                {
                    AuthoredAssetEntry entry = entries[entryIndex];
                    if (
                        string.Equals(entry.Key, SerializationData, StringComparison.Ordinal)
                        || string.Equals(
                            entry.Key,
                            PrivateSerializationData,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        parsedFindings.Add(
                            new OdinMigrationFinding(
                                entry.LineNumber,
                                "A serialized-data key may hold Odin state and requires a staged data migration review."
                            )
                        );
                        continue;
                    }

                    string value = entry.InlineValue;
                    if (
                        string.Equals(entry.Key, PropertyPath, StringComparison.Ordinal)
                        && TryReadScalar(
                            value,
                            0,
                            value.Length,
                            out int valueStart,
                            out int valueEnd
                        )
                        && TargetsSerializationData(value, valueStart, valueEnd)
                    )
                    {
                        parsedFindings.Add(
                            new OdinMigrationFinding(
                                entry.LineNumber,
                                "A prefab override targets a serialized-data key that may be Odin-owned and requires review."
                            )
                        );
                    }
                }
            }

            findings = parsedFindings;
            return true;
        }

        internal static bool LooksLikeUnityYaml(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            int position = 0;
            if (source[0] == '\ufeff')
            {
                position++;
            }
            if (0 <= source.IndexOf('\0'))
            {
                return false;
            }
            SkipWhitespace(source, ref position, source.Length);
            return StartsWith(source, position, "%YAML 1.1")
                || StartsWith(source, position, "--- !u!");
        }

        private static bool IsKey(string source, int start, int length, string key)
        {
            return length == key.Length
                && string.CompareOrdinal(source, start, key, 0, length) == 0;
        }

        private static bool StartsWith(string source, int start, string value)
        {
            return start + value.Length <= source.Length
                && string.CompareOrdinal(source, start, value, 0, value.Length) == 0;
        }

        private static bool TargetsSerializationData(string source, int start, int end)
        {
            for (int position = start; position < end; position++)
            {
                if (position != start && source[position - 1] != '.')
                {
                    continue;
                }

                int segmentEnd = position;
                while (segmentEnd < end && source[segmentEnd] != '.' && source[segmentEnd] != '[')
                {
                    segmentEnd++;
                }
                int length = segmentEnd - position;
                if (
                    IsKey(source, position, length, SerializationData)
                    || IsKey(source, position, length, PrivateSerializationData)
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadScalar(
            string source,
            int start,
            int end,
            out int valueStart,
            out int valueEnd
        )
        {
            if (start == end || source[start] == '#')
            {
                valueStart = start;
                valueEnd = start;
                return false;
            }

            char quote = source[start];
            if (quote == '\'' || quote == '"')
            {
                int scalarStart = start + 1;
                for (int position = scalarStart; position < end; position++)
                {
                    if (quote == '"' && source[position] == '\\')
                    {
                        position++;
                        continue;
                    }
                    if (
                        quote == '\''
                        && source[position] == '\''
                        && position + 1 < end
                        && source[position + 1] == '\''
                    )
                    {
                        position++;
                        continue;
                    }
                    if (source[position] == quote)
                    {
                        valueStart = scalarStart;
                        valueEnd = position;
                        return true;
                    }
                }
                valueStart = start;
                valueEnd = start;
                return false;
            }

            int scalarEnd = end;
            for (int position = start; position < end; position++)
            {
                if (
                    source[position] == '#'
                    && (position == start || char.IsWhiteSpace(source[position - 1]))
                )
                {
                    scalarEnd = position;
                    break;
                }
            }
            int unquotedStart = start;
            while (unquotedStart < scalarEnd && char.IsWhiteSpace(source[unquotedStart]))
            {
                unquotedStart++;
            }
            while (unquotedStart < scalarEnd && char.IsWhiteSpace(source[scalarEnd - 1]))
            {
                scalarEnd--;
            }
            valueStart = unquotedStart;
            valueEnd = scalarEnd;
            return valueStart < valueEnd;
        }

        private static void SkipWhitespace(string source, ref int position, int end)
        {
            while (position < end && char.IsWhiteSpace(source[position]))
            {
                position++;
            }
        }
    }
}
