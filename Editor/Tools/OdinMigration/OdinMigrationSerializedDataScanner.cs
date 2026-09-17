// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;

    internal static class OdinMigrationSerializedDataScanner
    {
        private const string SerializationData = "serializationData";
        private const string PrivateSerializationData = "_serializationData";
        private const string PropertyPath = "propertyPath";

        internal static IReadOnlyList<OdinMigrationFinding> Analyze(string source)
        {
            List<OdinMigrationFinding> findings = new List<OdinMigrationFinding>();
            if (string.IsNullOrEmpty(source))
            {
                return findings;
            }

            int line = 1;
            int lineStart = 0;
            while (lineStart < source.Length)
            {
                int lineEnd = lineStart;
                while (
                    lineEnd < source.Length && source[lineEnd] != '\n' && source[lineEnd] != '\r'
                )
                {
                    lineEnd++;
                }

                AnalyzeLine(source, lineStart, lineEnd, line, findings);
                lineStart = lineEnd;
                if (lineStart < source.Length && source[lineStart] == '\r')
                {
                    lineStart++;
                }
                if (lineStart < source.Length && source[lineStart] == '\n')
                {
                    lineStart++;
                }
                line++;
            }

            return findings;
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

        private static void AnalyzeLine(
            string source,
            int start,
            int end,
            int line,
            List<OdinMigrationFinding> findings
        )
        {
            int position = start;
            SkipWhitespace(source, ref position, end);
            if (position == end || source[position] == '#')
            {
                return;
            }

            if (
                source[position] == '-'
                && position + 1 < end
                && char.IsWhiteSpace(source[position + 1])
            )
            {
                position++;
                SkipWhitespace(source, ref position, end);
            }

            int keyStart = position;
            while (
                position < end
                && (char.IsLetterOrDigit(source[position]) || source[position] == '_')
            )
            {
                position++;
            }
            int keyLength = position - keyStart;
            SkipWhitespace(source, ref position, end);
            if (keyLength == 0 || position == end || source[position] != ':')
            {
                return;
            }
            position++;
            SkipWhitespace(source, ref position, end);

            if (
                IsKey(source, keyStart, keyLength, SerializationData)
                || IsKey(source, keyStart, keyLength, PrivateSerializationData)
            )
            {
                findings.Add(
                    new OdinMigrationFinding(
                        line,
                        "A serialized-data key may hold Odin state and requires a staged data migration review."
                    )
                );
                return;
            }

            if (
                !IsKey(source, keyStart, keyLength, PropertyPath)
                || !TryReadScalar(source, position, end, out int valueStart, out int valueEnd)
            )
            {
                return;
            }

            if (TargetsSerializationData(source, valueStart, valueEnd))
            {
                findings.Add(
                    new OdinMigrationFinding(
                        line,
                        "A prefab override targets a serialized-data key that may be Odin-owned and requires review."
                    )
                );
            }
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
            valueStart = start;
            valueEnd = start;
            if (start == end || source[start] == '#')
            {
                return false;
            }

            char quote = source[start];
            if (quote == '\'' || quote == '"')
            {
                valueStart = start + 1;
                for (int position = valueStart; position < end; position++)
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
                        valueEnd = position;
                        return true;
                    }
                }
                return false;
            }

            valueEnd = end;
            for (int position = start; position < end; position++)
            {
                if (
                    source[position] == '#'
                    && (position == start || char.IsWhiteSpace(source[position - 1]))
                )
                {
                    valueEnd = position;
                    break;
                }
            }
            while (valueStart < valueEnd && char.IsWhiteSpace(source[valueStart]))
            {
                valueStart++;
            }
            while (valueStart < valueEnd && char.IsWhiteSpace(source[valueEnd - 1]))
            {
                valueEnd--;
            }
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
