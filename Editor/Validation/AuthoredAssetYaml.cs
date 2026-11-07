// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

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
        /// <summary>The <c>!u!</c> class id Unity writes for a <c>MonoBehaviour</c> document.</summary>
        public const int MonoBehaviourTypeId = 114;

        /// <summary>The serialized key of a component's owning GameObject reference.</summary>
        public const string GameObjectField = "m_GameObject";

        /// <summary>The serialized key of a behaviour's script reference.</summary>
        public const string ScriptField = AuthoredAssetDocument.ScriptKey;

        /// <summary>The value Unity writes for an object reference nobody assigned.</summary>
        public const string NullObjectReference = "{fileID: 0}";

        /// <summary>The value Unity writes for a sequence with no elements.</summary>
        public const string EmptySequence = "[]";

        private const int FieldIndent = 2;

        /// <summary>The extensions Unity writes authored objects into.</summary>
        public static readonly IReadOnlyList<string> AuthoredExtensions = new[]
        {
            ".unity",
            ".prefab",
            ".asset",
        };

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
            if (string.IsNullOrWhiteSpace(rootDirectory))
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

                        matches.Add(path.SanitizePath());
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
        /// Reads <paramref name="filePath"/> and parses the documents Unity wrote into it.
        /// </summary>
        /// <remarks>
        /// A filesystem path, not a Unity asset path: an asset path is project-relative, so passing
        /// one here would depend on the process working directory. Resolve it with
        /// <c>AuthoredAssetPaths.ToFileSystemPath</c> and keep the asset path for the report.
        /// </remarks>
        /// <param name="filePath">The file to read, as a path the filesystem can resolve.</param>
        /// <param name="lines">Receives the file's lines, so a caller can quote the offending one.</param>
        /// <param name="documents">Receives the parsed documents.</param>
        /// <returns><c>false</c> when the file could not be read or holds no Unity document.</returns>
        public static bool TryReadDocuments(
            string filePath,
            out IReadOnlyList<string> lines,
            out IReadOnlyList<AuthoredAssetDocument> documents
        )
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                lines = Array.Empty<string>();
                documents = Array.Empty<AuthoredAssetDocument>();
                return false;
            }

            string[] read;
            try
            {
                read = File.ReadAllLines(filePath);
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
        /// <returns><c>false</c> for non-mappings or repeated fileID or GUID keys.</returns>
        public static bool TryParseObjectReference(string value, out long fileId, out string guid)
        {
            return TryParseObjectReference(value, out fileId, out guid, out bool _);
        }

        /// <summary>Resolves an inline object reference, including references within one asset.</summary>
        /// <param name="value">The inline value to parse.</param>
        /// <param name="owningAssetGuid">The GUID of the file that contains the reference.</param>
        /// <param name="fileId">Receives the referenced local file id.</param>
        /// <param name="guid">Receives the referenced asset GUID.</param>
        /// <returns><c>false</c> when the value is invalid or a local reference has no owning GUID.</returns>
        public static bool TryResolveObjectReference(
            string value,
            string owningAssetGuid,
            out long fileId,
            out string guid
        )
        {
            if (
                !TryParseObjectReference(value, out fileId, out guid, out bool hasFileId)
                || !hasFileId
            )
            {
                fileId = 0;
                guid = null;
                return false;
            }

            if (fileId == 0 || !string.IsNullOrEmpty(guid))
            {
                return true;
            }

            if (string.IsNullOrEmpty(owningAssetGuid))
            {
                fileId = 0;
                guid = null;
                return false;
            }

            guid = owningAssetGuid;
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
                int textLength = text.Length;
                int indent = 0;
                while (indent < textLength && text[indent] == ' ')
                {
                    ++indent;
                }

                if (indent != entry.Indent || textLength <= indent || text[indent] != '-')
                {
                    continue;
                }

                yield return new AuthoredSequenceElement(
                    line + 1,
                    text.Substring(indent + 1).Trim()
                );
            }
        }

        /// <summary>Reads the first direct field of a Unity document, without matching nested names.</summary>
        /// <param name="document">The document to inspect.</param>
        /// <param name="field">The exact serialized key, with an optional trailing colon.</param>
        /// <param name="value">The inline value, or an empty string when absent.</param>
        /// <returns>Whether the document declares the direct field.</returns>
        public static bool TryGetField(
            AuthoredAssetDocument document,
            string field,
            out string value
        )
        {
            if (document != null && !string.IsNullOrEmpty(field))
            {
                foreach (AuthoredAssetEntry entry in document.Entries)
                {
                    if (
                        entry.SequenceDepth == 0
                        && entry.Indent == FieldIndent
                        && MatchesFieldName(entry.Key, field)
                    )
                    {
                        value = entry.InlineValue;
                        return true;
                    }
                }
            }
            value = string.Empty;
            return false;
        }

        /// <summary>Reads a field only when exactly one entry at any depth declares its name.</summary>
        /// <param name="document">The document to inspect.</param>
        /// <param name="field">The exact serialized key, with an optional trailing colon.</param>
        /// <param name="value">The unique inline value, or an empty string on absence or ambiguity.</param>
        /// <returns>Whether exactly one entry matches.</returns>
        public static bool TryGetOnlyField(
            AuthoredAssetDocument document,
            string field,
            out string value
        )
        {
            bool found = false;
            string unique = string.Empty;
            if (document != null && !string.IsNullOrEmpty(field))
            {
                foreach (AuthoredAssetEntry entry in document.Entries)
                {
                    if (!MatchesFieldName(entry.Key, field))
                    {
                        continue;
                    }
                    if (found)
                    {
                        value = string.Empty;
                        return false;
                    }
                    unique = entry.InlineValue;
                    found = true;
                }
            }
            value = unique;
            return found;
        }

        /// <summary>Reads a direct field's inline value, or an empty string when it is absent.</summary>
        /// <param name="document">The document to inspect.</param>
        /// <param name="field">The exact serialized key, with an optional trailing colon.</param>
        /// <returns>The first matching direct field's inline value.</returns>
        public static string Field(AuthoredAssetDocument document, string field)
        {
            return TryGetField(document, field, out string value) ? value : string.Empty;
        }

        /// <summary>Tests a MonoBehaviour's direct script GUID for an exact, case-insensitive match.</summary>
        /// <remarks>Empty GUIDs, null references and duplicate direct script fields never match.</remarks>
        public static bool IsScriptedBy(AuthoredAssetDocument document, string scriptGuid)
        {
            if (
                document == null
                || document.UnityTypeId != MonoBehaviourTypeId
                || string.IsNullOrWhiteSpace(scriptGuid)
                || !TryGetOnlyDirectField(document, ScriptField, out string reference)
                || !TryParseObjectReference(reference, out long fileId, out string guid)
                || fileId == 0
                || string.IsNullOrEmpty(guid)
            )
            {
                return false;
            }
            return string.Equals(guid, scriptGuid, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Finds one direct child of an entry, refusing duplicate child names.</summary>
        /// <param name="document">The document that owns the parent entry.</param>
        /// <param name="parent">The parent entry from the same document.</param>
        /// <param name="field">The exact child key, with an optional trailing colon.</param>
        /// <param name="child">The unique direct child, or default on failure.</param>
        /// <returns>Whether exactly one matching direct child exists.</returns>
        public static bool TryDescend(
            AuthoredAssetDocument document,
            AuthoredAssetEntry parent,
            string field,
            out AuthoredAssetEntry child
        )
        {
            if (document == null || string.IsNullOrEmpty(field))
            {
                child = default;
                return false;
            }
            bool ownsParent = false;
            foreach (AuthoredAssetEntry entry in document.Entries)
            {
                if (entry.Equals(parent))
                {
                    parent = entry;
                    ownsParent = true;
                    break;
                }
            }
            if (!ownsParent)
            {
                child = default;
                return false;
            }
            bool found = false;
            AuthoredAssetEntry selected = default;
            foreach (AuthoredAssetEntry entry in document.Entries)
            {
                if (!IsDirectChild(entry, parent) || !MatchesFieldName(entry.Key, field))
                {
                    continue;
                }
                if (found)
                {
                    child = default;
                    return false;
                }
                selected = entry;
                found = true;
            }
            child = found ? selected : default;
            return found;
        }

        /// <summary>Returns a serialized name and its legacy form without a leading underscore.</summary>
        /// <param name="field">The serialized key to expand.</param>
        /// <returns>The exact name first, then its legacy form when different.</returns>
        /// <remarks>Field reads remain exact; callers choose whether to accept a legacy spelling.</remarks>
        public static IEnumerable<string> SerializedNames(string field)
        {
            if (string.IsNullOrEmpty(field))
            {
                yield break;
            }
            yield return field;
            if (1 < field.Length && field[0] == '_')
            {
                yield return field.Substring(1);
            }
        }

        /// <summary>Joins a direct field's value and direct child values, excluding serializedVersion.</summary>
        /// <param name="document">The document to inspect.</param>
        /// <param name="field">The exact serialized key, with an optional trailing colon.</param>
        /// <returns>Space-separated values, or an empty string when the direct field is absent.</returns>
        /// <remarks>Nested descendants and block-scalar text are not flattened.</remarks>
        public static string ValueWithDirectChildren(AuthoredAssetDocument document, string field)
        {
            if (document == null || string.IsNullOrEmpty(field))
            {
                return string.Empty;
            }
            AuthoredAssetEntry parent = default;
            bool found = false;
            foreach (AuthoredAssetEntry entry in document.Entries)
            {
                if (
                    entry.SequenceDepth == 0
                    && entry.Indent == FieldIndent
                    && MatchesFieldName(entry.Key, field)
                )
                {
                    parent = entry;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                return string.Empty;
            }
            using PooledResource<StringBuilder> lease = Buffers.StringBuilder.Get(
                out StringBuilder builder
            );
            builder.Append(parent.InlineValue);
            foreach (AuthoredAssetEntry entry in document.Entries)
            {
                if (
                    !IsDirectChild(entry, parent)
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

        internal static bool TryGetOnlyDirectField(
            AuthoredAssetDocument document,
            string field,
            out string value
        )
        {
            bool found = false;
            string selected = string.Empty;
            IReadOnlyList<AuthoredAssetEntry> entries = document.Entries;
            int count = entries.Count;
            for (int index = 0; index < count; ++index)
            {
                AuthoredAssetEntry entry = entries[index];
                if (
                    entry.SequenceDepth != 0
                    || entry.Indent != FieldIndent
                    || !MatchesFieldName(entry.Key, field)
                )
                {
                    continue;
                }
                if (found)
                {
                    value = string.Empty;
                    return false;
                }
                selected = entry.InlineValue;
                found = true;
            }
            value = selected;
            return found;
        }

        internal static bool TrySplitEntry(string content, out string key, out string inlineValue)
        {
            if (string.IsNullOrEmpty(content))
            {
                key = null;
                inlineValue = string.Empty;
                return false;
            }

            char quote = content[0];
            if (quote == '\'' || quote == '"')
            {
                int quotedContentLength = content.Length;
                int closingQuote = 1;
                while (closingQuote < quotedContentLength && IsKeyCharacter(content[closingQuote]))
                {
                    ++closingQuote;
                }

                if (
                    closingQuote <= 1
                    || quotedContentLength <= closingQuote + 1
                    || content[closingQuote] != quote
                    || content[closingQuote + 1] != ':'
                    || (closingQuote + 2 < quotedContentLength && content[closingQuote + 2] != ' ')
                )
                {
                    key = null;
                    inlineValue = string.Empty;
                    return false;
                }

                key = content.Substring(1, closingQuote - 1);
                inlineValue = content.Substring(closingQuote + 2).Trim();
                return true;
            }

            int separator = -1;
            int contentLength = content.Length;
            for (int index = 0; index < contentLength; ++index)
            {
                char character = content[index];
                if (character == ':')
                {
                    bool terminated = contentLength <= index + 1 || content[index + 1] == ' ';
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

        /// <summary>Whether <paramref name="path"/> sits under one of <paramref name="prefixes"/>.</summary>
        /// <param name="path">The asset path to test.</param>
        /// <param name="prefixes">The prefixes to accept.</param>
        /// <returns><c>true</c> when a non-blank path matches a non-blank prefix.</returns>
        /// <remarks>
        /// Shared so two checks cannot drift into two contracts. They had: one returned false for a
        /// null path and the other threw, which is what exposing them for test found.
        /// </remarks>
        internal static bool IsUnderAnyPrefix(string path, IReadOnlyList<string> prefixes)
        {
            if (string.IsNullOrWhiteSpace(path) || prefixes == null)
            {
                return false;
            }

            string normalized = path.SanitizePath();
            for (int index = 0; index < prefixes.Count; ++index)
            {
                string prefix = prefixes[index];
                if (
                    !string.IsNullOrWhiteSpace(prefix)
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
            int lineLength = line.Length;
            int index = 0;
            while (index < lineLength && line[index] == ' ')
            {
                ++index;
            }

            return index;
        }

        private static bool IsDirectChild(AuthoredAssetEntry entry, AuthoredAssetEntry parent)
        {
            return entry.SequenceDepth == parent.SequenceDepth
                && entry.Indent == parent.Indent + FieldIndent
                && parent.LineNumber < entry.LineNumber
                && entry.LineNumber < parent.EndLineNumber;
        }

        private static bool MatchesFieldName(string key, string field)
        {
            if (key == null || string.IsNullOrEmpty(field))
            {
                return false;
            }
            int length = field.Length;
            if (field[length - 1] == ':')
            {
                --length;
            }
            return 0 < length
                && key.Length == length
                && string.CompareOrdinal(key, 0, field, 0, length) == 0;
        }

        private static bool TryParseObjectReference(
            string value,
            out long fileId,
            out string guid,
            out bool hasFileId
        )
        {
            if (string.IsNullOrEmpty(value))
            {
                fileId = 0;
                guid = null;
                hasFileId = false;
                return false;
            }

            string trimmed = value.Trim();
            int trimmedLength = trimmed.Length;
            if (trimmedLength < 2 || trimmed[0] != '{' || trimmed[trimmedLength - 1] != '}')
            {
                fileId = 0;
                guid = null;
                hasFileId = false;
                return false;
            }

            long readFileId = 0;
            string readGuid = null;
            bool readHasFileId = false;
            bool sawFileId = false;
            bool sawGuid = false;
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
                    if (sawFileId)
                    {
                        fileId = 0;
                        guid = null;
                        hasFileId = false;
                        return false;
                    }
                    sawFileId = true;
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
                        readHasFileId = true;
                    }
                }
                else if (string.Equals(key, "guid", StringComparison.Ordinal))
                {
                    if (sawGuid)
                    {
                        fileId = 0;
                        guid = null;
                        hasFileId = false;
                        return false;
                    }
                    sawGuid = true;
                    readGuid = entry;
                }
            }

            fileId = readFileId;
            guid = readGuid;
            hasFileId = readHasFileId;
            return true;
        }

        private static bool IsZeroGuid(string guid)
        {
            int guidLength = guid.Length;
            for (int index = 0; index < guidLength; ++index)
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
            using PooledResource<List<int>> sequenceLease = Buffers<int>.List.Get(
                out List<int> sequenceIndents
            );

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
                if (content.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                bool sequenceItem =
                    content.StartsWith("- ", StringComparison.Ordinal)
                    || string.Equals(content, "-", StringComparison.Ordinal);
                while (0 < sequenceIndents.Count)
                {
                    int sequenceIndex = sequenceIndents.Count - 1;
                    int sequenceIndent = sequenceIndents[sequenceIndex];
                    if (sequenceIndent < indent || (sequenceIndent == indent && sequenceItem))
                    {
                        break;
                    }
                    sequenceIndents.RemoveAt(sequenceIndex);
                }
                int sequenceDepth = sequenceIndents.Count;
                if (
                    sequenceItem
                    && (sequenceDepth == 0 || sequenceIndents[sequenceDepth - 1] < indent)
                )
                {
                    sequenceIndents.Add(indent);
                    ++sequenceDepth;
                }
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
                        FindEntryEnd(lines, index, bodyEnd, indent) + 1,
                        sequenceDepth
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
                if (line[lineIndent] == '#')
                {
                    ++index;
                    continue;
                }
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
            int lineLength = line.Length;
            int end = start;
            while (end < lineLength && !char.IsWhiteSpace(line[end]))
            {
                ++end;
            }

            return line.Substring(start, end - start);
        }
    }
#endif
}
