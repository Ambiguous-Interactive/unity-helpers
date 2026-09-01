// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;

    /// <summary>
    /// Reads a committed <c>.unity</c>, <c>.prefab</c> or <c>.asset</c> as the sequence of documents
    /// Unity wrote, without asking Unity to load it.
    /// </summary>
    /// <remarks>
    /// Reading rather than loading, because opening a scene runs every <c>OnValidate</c> in it, so
    /// inspecting dirties it and closing prompts for a save -- and a gate must not mutate what it
    /// measures. Nothing here touches <c>UnityEditor</c>. See
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/docs/features/editor-tools/authored-asset-validation.md">Authored Asset Validation</see>.
    /// </remarks>
    public static class AuthoredAssetYaml
    {
        /// <summary>The extensions Unity writes authored objects into.</summary>
        public static readonly IReadOnlyList<string> AuthoredExtensions = new[]
        {
            ".unity",
            ".prefab",
            ".asset",
        };

        /// <summary>The <c>!u!</c> class id Unity writes for a <c>MonoBehaviour</c> document.</summary>
        public const int MonoBehaviourTypeId = 114;

        /// <summary>The value Unity writes for an object reference nobody assigned.</summary>
        public const string NullObjectReference = "{fileID: 0}";

        /// <summary>The value Unity writes for a sequence with no elements.</summary>
        public const string EmptySequence = "[]";

        /// <summary>
        /// Every file under <paramref name="rootDirectory"/> whose extension is one of
        /// <paramref name="extensions"/>, sorted, or an empty list when the root cannot be walked.
        /// </summary>
        /// <param name="rootDirectory">The directory to walk, recursively.</param>
        /// <param name="extensions">The extensions to accept, leading dot included; defaults to <see cref="AuthoredExtensions"/>.</param>
        /// <returns>The matching paths, using forward slashes so they read as asset paths.</returns>
        /// <remarks>
        /// Everything under the root is enumerated and the extension tested here, rather than one
        /// glob per extension. Windows matches a search pattern against a file's 8.3 short name as
        /// well as its long one, so <c>*.unity</c> hands back a <c>.unityproj</c> and a check then
        /// reports findings about a file it cannot parse. There is no pattern to be wrong about.
        /// </remarks>
        public static IReadOnlyList<string> EnumerateAuthoredAssets(
            string rootDirectory,
            params string[] extensions
        )
        {
            List<string> matches = new();
            if (string.IsNullOrEmpty(rootDirectory))
            {
                return matches;
            }

            IReadOnlyList<string> accepted =
                extensions == null || extensions.Length <= 0 ? AuthoredExtensions : extensions;

            try
            {
                if (!Directory.Exists(rootDirectory))
                {
                    return matches;
                }

                foreach (
                    string path in Directory.EnumerateFiles(
                        rootDirectory,
                        "*",
                        SearchOption.AllDirectories
                    )
                )
                {
                    for (int index = 0; index < accepted.Count; ++index)
                    {
                        string extension = accepted[index];
                        if (
                            string.IsNullOrEmpty(extension)
                            || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
                        )
                        {
                            continue;
                        }

                        matches.Add(path.Replace('\\', '/'));
                        break;
                    }
                }
            }
            catch (Exception)
            {
                return matches;
            }

            matches.Sort(StringComparer.Ordinal);
            return matches;
        }

        /// <summary>
        /// Reads <paramref name="assetPath"/> and parses the documents Unity wrote into it.
        /// </summary>
        /// <param name="assetPath">The file to read.</param>
        /// <param name="lines">Receives the file's lines, so a caller can quote the offending one.</param>
        /// <param name="documents">Receives the parsed documents.</param>
        /// <returns><c>false</c> when the file could not be read or holds no Unity document.</returns>
        public static bool TryReadDocuments(
            string assetPath,
            out IReadOnlyList<string> lines,
            out IReadOnlyList<AuthoredAssetDocument> documents
        )
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                lines = Array.Empty<string>();
                documents = Array.Empty<AuthoredAssetDocument>();
                return false;
            }

            string[] read;
            try
            {
                read = File.ReadAllLines(assetPath);
            }
            catch (Exception)
            {
                lines = Array.Empty<string>();
                documents = Array.Empty<AuthoredAssetDocument>();
                return false;
            }

            IReadOnlyList<AuthoredAssetDocument> parsed = ReadDocuments(read);
            lines = read;
            documents = parsed;
            return 0 < parsed.Count;
        }

        /// <summary>
        /// Parses <paramref name="lines"/> into the <c>--- !u!</c> documents they declare.
        /// </summary>
        /// <param name="lines">The file's lines, in order.</param>
        /// <returns>One entry per document, in the order Unity wrote them.</returns>
        /// <remarks>
        /// A list rather than a map keyed by anchor: two documents in one file sharing an anchor is
        /// corruption, and a map would silently drop the evidence of it.
        /// </remarks>
        public static IReadOnlyList<AuthoredAssetDocument> ReadDocuments(
            IReadOnlyList<string> lines
        )
        {
            List<AuthoredAssetDocument> documents = new();
            if (lines == null)
            {
                return documents;
            }

            int index = 0;
            while (index < lines.Count)
            {
                if (!IsDocumentHeader(lines[index]))
                {
                    ++index;
                    continue;
                }

                int bodyStart = index + 1;
                int bodyEnd = bodyStart;
                while (bodyEnd < lines.Count && !IsDocumentHeader(lines[bodyEnd]))
                {
                    ++bodyEnd;
                }

                documents.Add(ReadDocument(lines, index, bodyEnd));
                index = bodyEnd;
            }

            return documents;
        }

        /// <summary>
        /// Splits an inline object reference such as <c>{fileID: 11500000, guid: ..., type: 3}</c>.
        /// </summary>
        /// <param name="value">The inline value to parse.</param>
        /// <param name="fileId">Receives the <c>fileID</c>, or zero when it declared none.</param>
        /// <param name="guid">Receives the <c>guid</c>, or <c>null</c> when it declared none.</param>
        /// <returns><c>false</c> when the value is not an inline mapping.</returns>
        public static bool TryParseObjectReference(string value, out long fileId, out string guid)
        {
            if (string.IsNullOrEmpty(value))
            {
                fileId = 0;
                guid = null;
                return false;
            }

            string trimmed = value.Trim();
            if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[trimmed.Length - 1] != '}')
            {
                fileId = 0;
                guid = null;
                return false;
            }

            long readFileId = 0;
            string readGuid = null;
            string body = trimmed.Substring(1, trimmed.Length - 2);
            foreach (string part in body.Split(','))
            {
                int separator = part.IndexOf(':');
                if (separator < 0)
                {
                    continue;
                }

                string key = part.Substring(0, separator).Trim();
                string entry = part.Substring(separator + 1).Trim();
                if (string.Equals(key, "fileID", StringComparison.Ordinal))
                {
                    if (
                        long.TryParse(
                            entry,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out long parsedFileId
                        )
                    )
                    {
                        readFileId = parsedFileId;
                    }
                }
                else if (string.Equals(key, "guid", StringComparison.Ordinal))
                {
                    readGuid = entry;
                }
            }

            fileId = readFileId;
            guid = readGuid;
            return true;
        }

        /// <summary>
        /// Whether <paramref name="value"/> is the object reference Unity writes for an empty slot.
        /// </summary>
        /// <param name="value">The inline value to judge.</param>
        /// <returns><c>true</c> when the value names no object.</returns>
        public static bool IsNullObjectReference(string value)
        {
            if (!TryParseObjectReference(value, out long fileId, out string guid))
            {
                return false;
            }

            return fileId == 0 && (string.IsNullOrEmpty(guid) || IsZeroGuid(guid));
        }

        /// <summary>Whether <paramref name="value"/> is the empty inline sequence.</summary>
        /// <param name="value">The inline value to judge.</param>
        /// <returns><c>true</c> when the value is <c>[]</c>.</returns>
        public static bool IsEmptySequence(string value)
        {
            return string.Equals(value?.Trim(), EmptySequence, StringComparison.Ordinal);
        }

        /// <summary>
        /// The block sequence elements <paramref name="entry"/>'s value is written as.
        /// </summary>
        /// <param name="lines">The file's lines, in order.</param>
        /// <param name="entry">The key whose value is a block sequence.</param>
        /// <returns>One entry per element, in the order Unity wrote them.</returns>
        /// <remarks>
        /// Read from the lines rather than from <see cref="AuthoredAssetDocument.Entries"/>, because
        /// a sequence element that is a bare scalar or an inline mapping -- <c>- {fileID: 0}</c>,
        /// which is every element of an array of references -- declares no key and so is no entry.
        /// A check that looked for entries alone would find nothing and report clean.
        /// </remarks>
        public static IEnumerable<AuthoredSequenceElement> EnumerateSequenceElements(
            IReadOnlyList<string> lines,
            AuthoredAssetEntry entry
        )
        {
            if (lines == null)
            {
                yield break;
            }

            for (int line = entry.LineNumber; line < entry.EndLineNumber - 1; ++line)
            {
                if (lines.Count <= line)
                {
                    yield break;
                }

                string text = lines[line];
                int indent = 0;
                while (indent < text.Length && text[indent] == ' ')
                {
                    ++indent;
                }

                if (indent != entry.Indent || text.Length <= indent || text[indent] != '-')
                {
                    continue;
                }

                yield return new AuthoredSequenceElement(
                    line + 1,
                    text.Substring(indent + 1).Trim()
                );
            }
        }

        private static bool IsZeroGuid(string guid)
        {
            for (int index = 0; index < guid.Length; ++index)
            {
                if (guid[index] != '0')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsDocumentHeader(string line)
        {
            return line != null && line.StartsWith("---", StringComparison.Ordinal);
        }

        private static AuthoredAssetDocument ReadDocument(
            IReadOnlyList<string> lines,
            int headerIndex,
            int bodyEnd
        )
        {
            string header = lines[headerIndex];
            int unityTypeId = 0;
            long fileId = 0;

            int tagStart = header.IndexOf("!u!", StringComparison.Ordinal);
            if (
                0 <= tagStart
                && int.TryParse(
                    ReadToken(header, tagStart + 3),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsedTypeId
                )
            )
            {
                unityTypeId = parsedTypeId;
            }

            int anchorStart = header.IndexOf('&');
            if (
                0 <= anchorStart
                && long.TryParse(
                    ReadToken(header, anchorStart + 1),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out long parsedFileId
                )
            )
            {
                fileId = parsedFileId;
            }

            bool isStripped = 0 <= header.IndexOf(" stripped", StringComparison.Ordinal);

            string rootKey = null;
            List<AuthoredAssetEntry> entries = new();
            int blockScalarIndent = -1;

            for (int index = headerIndex + 1; index < bodyEnd; ++index)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int indent = LeadingSpaces(line);
                if (0 <= blockScalarIndent)
                {
                    if (blockScalarIndent < indent)
                    {
                        continue;
                    }

                    blockScalarIndent = -1;
                }

                string content = line.Substring(indent);
                if (content.StartsWith("- ", StringComparison.Ordinal))
                {
                    indent += 2;
                    content = content.Substring(2);
                }
                else if (string.Equals(content, "-", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TrySplitEntry(content, out string key, out string inlineValue))
                {
                    continue;
                }

                if (indent <= 0 && rootKey == null)
                {
                    rootKey = key;
                    continue;
                }

                entries.Add(
                    new AuthoredAssetEntry(
                        key,
                        inlineValue,
                        indent,
                        index + 1,
                        FindEntryEnd(lines, index, bodyEnd, indent) + 1
                    )
                );

                if (IsBlockScalarIndicator(inlineValue))
                {
                    blockScalarIndent = indent;
                }
            }

            return new AuthoredAssetDocument(
                fileId,
                unityTypeId,
                rootKey,
                isStripped,
                headerIndex + 1,
                bodyEnd + 1,
                entries
            );
        }

        private static int FindEntryEnd(
            IReadOnlyList<string> lines,
            int keyIndex,
            int bodyEnd,
            int indent
        )
        {
            int index = keyIndex + 1;
            while (index < bodyEnd)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    ++index;
                    continue;
                }

                int lineIndent = LeadingSpaces(line);
                bool continues =
                    indent < lineIndent
                    || (
                        lineIndent == indent
                        && line.Substring(lineIndent).StartsWith("-", StringComparison.Ordinal)
                    );

                if (!continues)
                {
                    break;
                }

                ++index;
            }

            return index;
        }

        private static bool IsBlockScalarIndicator(string inlineValue)
        {
            if (string.IsNullOrEmpty(inlineValue))
            {
                return false;
            }

            char first = inlineValue[0];
            return first == '|' || first == '>';
        }

        internal static bool TrySplitEntry(string content, out string key, out string inlineValue)
        {
            int separator = -1;
            for (int index = 0; index < content.Length; ++index)
            {
                char character = content[index];
                if (character == ':')
                {
                    bool terminated = content.Length <= index + 1 || content[index + 1] == ' ';
                    if (terminated)
                    {
                        separator = index;
                    }

                    break;
                }

                if (!IsKeyCharacter(character))
                {
                    key = null;
                    inlineValue = string.Empty;
                    return false;
                }
            }

            if (separator <= 0)
            {
                key = null;
                inlineValue = string.Empty;
                return false;
            }

            key = content.Substring(0, separator);
            inlineValue = content.Substring(separator + 1).Trim();
            return true;
        }

        private static bool IsKeyCharacter(char character)
        {
            return character == '_'
                || character == '-'
                || character == '.'
                || character == '$'
                || character == '<'
                || character == '>'
                || char.IsLetterOrDigit(character);
        }

        private static string ReadToken(string line, int start)
        {
            int end = start;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
            {
                ++end;
            }

            return line.Substring(start, end - start);
        }

        /// <summary>Whether <paramref name="path"/> sits under one of <paramref name="prefixes"/>.</summary>
        /// <param name="path">The asset path to test.</param>
        /// <param name="prefixes">The prefixes to accept.</param>
        /// <returns><c>true</c> when the path is in scope.</returns>
        /// <remarks>
        /// Shared so two checks cannot drift into two contracts. They had: one returned false for a
        /// null path and the other threw, which is what exposing them for test found.
        /// </remarks>
        internal static bool IsUnderAnyPrefix(string path, IReadOnlyList<string> prefixes)
        {
            if (string.IsNullOrEmpty(path) || prefixes == null)
            {
                return false;
            }

            string normalized = path.Replace('\\', '/');
            for (int index = 0; index < prefixes.Count; ++index)
            {
                string prefix = prefixes[index];
                if (
                    !string.IsNullOrEmpty(prefix)
                    && normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }

            return false;
        }

        internal static int LeadingSpaces(string line)
        {
            int index = 0;
            while (index < line.Length && line[index] == ' ')
            {
                ++index;
            }

            return index;
        }
    }
#endif
}
