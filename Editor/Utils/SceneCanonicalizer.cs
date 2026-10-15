// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Canonicalizes Unity text scenes and prefabs using structural object identities.</summary>
    /// <remarks>Generated IDs preserve legacy prefab sibling order, skip Unity-reserved values and leave external references unchanged.</remarks>
    public static partial class SceneCanonicalizer
    {
        private const string DocumentMarker = "--- !u!";
        private const string NewlineDocumentMarker = "\n" + DocumentMarker;
        private const string LocalRefPrefix = "{fileID: ";
        private const string LocalRefToken = "{L}";
        private const string AnchorToken = "ANCH";
        private const string FileIdLabel = "fileID: ";
        private const string GuidLabel = "guid: ";
        private const string StrippedSuffix = " stripped";
        private const string EmptyList = "[]";

        private const string FieldGameObject = "m_GameObject";
        private const string FieldFather = "m_Father";
        private const string FieldChildren = "m_Children";
        private const string FieldComponent = "m_Component";
        private const string FieldName = "m_Name";
        private const string FieldRoots = "m_Roots";
        private const string FieldRootOrder = "m_RootOrder";
        private const string FieldCorrespondingSourceObject = "m_CorrespondingSourceObject";
        private const string FieldTransformParent = "m_TransformParent";
        private const string FieldSourcePrefab = "m_SourcePrefab";
        private const string FieldPrefabInstance = "m_PrefabInstance";
        private const string FieldAddedGameObjects = "m_AddedGameObjects";
        private const string FieldAddedObject = "addedObject";

        private const string ChildItemPrefix = "- {fileID: ";
        private const string ComponentItemPrefix = "- component: {fileID: ";

        private const string KeyGameObject = "GO:";
        private const string KeyTransform = "TR:";
        private const string KeyComponent = "CO:";
        private const string KeyPrefabInstance = "PI:";
        private const string KeyStripped = "ST:";
        private const string KeySingleton = "ZZSINGLETON:";
        private const string KeyStrippedSource = "SRC#";
        private const string KeyClassSeparator = ":";
        private const string KeyOrdinalSeparator = "#";

        private const int GameObjectClassId = 1;
        private const int TransformClassId = 4;
        private const int RectTransformClassId = 224;
        private const int PrefabInstanceClassId = 1001;
        private const long SceneRootsSentinelFileId = long.MaxValue;

        private const int MaxHierarchyDepth = 1024;
        private const long FileIdMask = 0x7FFFFFFF;

        private const ulong FnvOffsetBasis = 0xcbf29ce484222325UL;
        private const ulong FnvPrime = 0x100000001b3UL;

        private static readonly long[] ReservedFileIds = { 0, 1, 3 };

        /// <summary>Canonicalizes a saved scene or prefab, returning true only when the file changes.</summary>
        /// <remarks>File replacement is outside Unity Undo. Reopen loaded assets after rewriting their identifiers.</remarks>
        public static bool CanonicalizeSceneFile(string path)
        {
            return TryCanonicalizeFile(path, out bool changed, out _) && changed;
        }

        /// <summary>Canonicalizes a saved UTF-8 scene or prefab and reports failure separately from a no-op.</summary>
        /// <remarks>Preserves the UTF-8 byte order mark. Compares the read bytes before staged replacement; non-cooperating external writers are not locked.</remarks>
        public static bool TryCanonicalizeFile(string path, out bool changed, out Exception error)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                changed = false;
                error = new ArgumentException("A scene or prefab path is required.", nameof(path));
                return false;
            }
            try
            {
                byte[] originalBytes = File.ReadAllBytes(path);
                bool hasPreamble =
                    3 <= originalBytes.Length
                    && originalBytes[0] == 0xef
                    && originalBytes[1] == 0xbb
                    && originalBytes[2] == 0xbf;
                UTF8Encoding encoding = new(hasPreamble, true);
                int offset = hasPreamble ? 3 : 0;
                string original = encoding.GetString(
                    originalBytes,
                    offset,
                    originalBytes.Length - offset
                );
                if (!TryCanonicalizeYaml(original, out string canonical, out Exception failure))
                {
                    changed = false;
                    error = failure;
                    return false;
                }
                if (string.Equals(original, canonical, StringComparison.Ordinal))
                {
                    changed = false;
                    error = null;
                    return true;
                }
                byte[] body = encoding.GetBytes(canonical);
                byte[] replacement = body;
                if (hasPreamble)
                {
                    replacement = new byte[body.Length + 3];
                    Buffer.BlockCopy(originalBytes, 0, replacement, 0, 3);
                    Buffer.BlockCopy(body, 0, replacement, 3, body.Length);
                }
                bool saved = DurableFile.TryCompareThenReplaceBytes(
                    path,
                    originalBytes,
                    replacement,
                    out Exception writeError
                );
                changed = saved;
                error = writeError;
                return saved;
            }
            catch (Exception failure)
            {
                changed = false;
                error = failure;
                return false;
            }
        }

        /// <summary>Returns canonical YAML, or the original input when it cannot be safely canonicalized.</summary>
        public static string CanonicalizeYaml(string yaml)
        {
            return TryCanonicalizeYaml(yaml, out string canonical, out _) ? canonical : yaml;
        }

        /// <summary>Canonicalizes YAML and reports unsupported or malformed documents without changing them.</summary>
        public static bool TryCanonicalizeYaml(
            string yaml,
            out string canonical,
            out Exception error
        )
        {
            try
            {
                string result = CanonicalizeCore(yaml);
                canonical = result;
                error = null;
                return true;
            }
            catch (Exception failure)
            {
                canonical = yaml;
                error = failure;
                return false;
            }
        }

        private static string CanonicalizeCore(string yaml)
        {
            if (string.IsNullOrEmpty(yaml))
            {
                return yaml;
            }

            List<int> starts = FindDocumentStarts(yaml);
            if (starts.Count == 0)
            {
                throw new InvalidDataException("The input contains no Unity YAML documents.");
            }

            string header = yaml.Substring(0, starts[0]);
            bool trailingNewline = yaml.EndsWith("\n", StringComparison.Ordinal);
            string newline = yaml.Contains("\r\n") ? "\r\n" : "\n";
            List<SceneDocument> documents = new List<SceneDocument>(starts.Count);
            Dictionary<long, SceneDocument> byId = new Dictionary<long, SceneDocument>(
                starts.Count
            );
            for (int i = 0; i < starts.Count; ++i)
            {
                int start = starts[i];
                int end = i + 1 < starts.Count ? starts[i + 1] : yaml.Length;
                string text = yaml.Substring(start, end - start);
                if (!text.EndsWith("\n", StringComparison.Ordinal))
                {
                    text += newline;
                }
                SceneDocument document = ParseDocument(text);
                documents.Add(document);
                if (document._fileId == 0 || !byId.TryAdd(document._fileId, document))
                {
                    throw new InvalidDataException(
                        "Unity document anchors must be unique and nonzero."
                    );
                }
            }

            SceneModel model = new SceneModel(documents, byId);
            HashSet<long> reservedReferences = new();
            AssignKeys(documents, model, reservedReferences);
            foreach (SceneDocument document in documents)
            {
                reservedReferences.Remove(document._fileId);
            }
            AssignNewFileIds(documents, reservedReferences);

            Dictionary<long, long> remap = new Dictionary<long, long>(documents.Count);
            foreach (SceneDocument document in documents)
            {
                remap[document._fileId] = document._newFileId;
            }

            documents.Sort(static (left, right) => CompareByNewFileId(left, right));

            StringBuilder builder = new StringBuilder(yaml.Length + 16);
            builder.Append(header);
            foreach (SceneDocument document in documents)
            {
                builder.Append(RewriteDocument(document, remap));
            }

            if (!trailingNewline)
            {
                builder.Length -= newline.Length;
            }
            return builder.ToString();
        }

        private static List<int> FindDocumentStarts(string yaml)
        {
            List<int> starts = new List<int>();
            if (yaml.StartsWith(DocumentMarker, StringComparison.Ordinal))
            {
                starts.Add(0);
            }

            int index = 0;
            while (true)
            {
                int found = yaml.IndexOf(NewlineDocumentMarker, index, StringComparison.Ordinal);
                if (found < 0)
                {
                    break;
                }

                starts.Add(found + 1);
                index = found + 1;
            }

            return starts;
        }

        private static SceneDocument ParseDocument(string text)
        {
            SceneDocument document = new SceneDocument();
            document._text = text;

            int lineEnd = text.IndexOf('\n');
            string first = (lineEnd < 0 ? text : text.Substring(0, lineEnd)).TrimEnd('\r');

            int pointer = DocumentMarker.Length;
            int classStart = pointer;
            while (pointer < first.Length && IsAsciiDigit(first[pointer]))
            {
                ++pointer;
            }

            document._classId = int.Parse(
                first.Substring(classStart, pointer - classStart),
                CultureInfo.InvariantCulture
            );

            int ampersand = first.IndexOf('&', pointer);
            int fileIdStart = ampersand + 1;
            if (ampersand < 0)
            {
                throw new InvalidDataException("A Unity document anchor is required.");
            }
            pointer = fileIdStart;
            if (pointer < first.Length && first[pointer] == '-')
            {
                ++pointer;
            }
            while (pointer < first.Length && IsAsciiDigit(first[pointer]))
            {
                ++pointer;
            }

            document._fileId = long.Parse(
                first.Substring(fileIdStart, pointer - fileIdStart),
                CultureInfo.InvariantCulture
            );
            string suffix = first.Substring(pointer);
            if (
                !string.IsNullOrEmpty(suffix)
                && !string.Equals(suffix, StrippedSuffix, StringComparison.Ordinal)
            )
            {
                throw new InvalidDataException("Unsupported Unity document header.");
            }
            document._stripped =
                0 <= first.IndexOf(StrippedSuffix, pointer, StringComparison.Ordinal);
            document._lines = SplitLines(text);
            document._gameObjectId = ParseFileIdFromRef(
                FindFieldValue(document._lines, FieldGameObject)
            );
            return document;
        }

        private static void AssignKeys(
            List<SceneDocument> documents,
            SceneModel model,
            HashSet<long> localIds
        )
        {
            foreach (SceneDocument document in documents)
            {
                document._normalized = NormalizeBody(document._text, localIds);
            }
            Dictionary<string, int> prefabKeyCounts = new(StringComparer.Ordinal);
            Dictionary<string, int> strippedKeyCounts = new(StringComparer.Ordinal);
            foreach (SceneDocument document in documents)
            {
                document._key = BuildKey(document, model);
                Dictionary<string, int> counts = document._stripped
                    ? strippedKeyCounts
                    : prefabKeyCounts;
                if (document._stripped || document._classId == PrefabInstanceClassId)
                {
                    int count = counts.TryGetValue(document._key, out int previous) ? previous : 0;
                    counts[document._key] = count + 1;
                }
            }
            foreach (SceneDocument document in documents)
            {
                if (
                    !document._stripped
                    && document._classId == PrefabInstanceClassId
                    && prefabKeyCounts.TryGetValue(document._key, out int prefabCount)
                    && 1 < prefabCount
                )
                {
                    document._key +=
                        KeyOrdinalSeparator
                        + model
                            .PrefabSiblingIndex(document._fileId)
                            .ToString(CultureInfo.InvariantCulture);
                }
            }
            foreach (SceneDocument document in documents)
            {
                if (
                    document._stripped
                    && strippedKeyCounts.TryGetValue(document._key, out int strippedCount)
                    && 1 < strippedCount
                )
                {
                    string source = FindFieldValue(document._lines, FieldCorrespondingSourceObject);
                    long ownerId = ParseFileIdFromRef(
                        FindFieldValue(document._lines, FieldPrefabInstance)
                    );
                    SceneDocument owner = model.Find(ownerId);
                    document._key +=
                        KeyClassSeparator
                        + ExtractGuid(source)
                        + KeyClassSeparator
                        + (owner == null ? string.Empty : owner._key);
                }
            }
        }

        private static string BuildKey(SceneDocument document, SceneModel model)
        {
            if (document._stripped)
            {
                long source = ParseFileIdFromRef(
                    FindFieldValue(document._lines, FieldCorrespondingSourceObject)
                );
                return KeyStripped + source.ToString(CultureInfo.InvariantCulture);
            }

            switch (document._classId)
            {
                case GameObjectClassId:
                    return KeyGameObject + model.HierarchyPath(document._fileId);
                case TransformClassId:
                case RectTransformClassId:
                    return KeyTransform + model.HierarchyPath(document._gameObjectId);
                case PrefabInstanceClassId:
                    return BuildPrefabInstanceKey(document, model);
            }

            if (document._gameObjectId != 0)
            {
                int ordinal = ComponentOrdinal(document, model);
                return KeyComponent
                    + model.HierarchyPath(document._gameObjectId)
                    + KeyClassSeparator
                    + document._classId.ToString(CultureInfo.InvariantCulture)
                    + KeyOrdinalSeparator
                    + ordinal.ToString(CultureInfo.InvariantCulture);
            }

            return KeySingleton + document._classId.ToString(CultureInfo.InvariantCulture);
        }

        private static string BuildPrefabInstanceKey(SceneDocument document, SceneModel model)
        {
            long transformParent = ParseFileIdFromRef(
                FindFieldValue(document._lines, FieldTransformParent)
            );
            string parentPath = string.Empty;
            if (transformParent != 0)
            {
                SceneDocument parent = model.Find(transformParent);
                if (parent != null && IsTransform(parent._classId))
                {
                    parentPath = parent._stripped
                        ? model.StrippedHierarchyPath(parent)
                        : model.HierarchyPath(parent._gameObjectId);
                }
            }

            string guid = ExtractGuid(FindFieldValue(document._lines, FieldSourcePrefab));
            string signature = FnvHash(document._normalized)
                .ToString("x", CultureInfo.InvariantCulture);
            return KeyPrefabInstance
                + parentPath
                + KeyClassSeparator
                + guid
                + KeyClassSeparator
                + signature;
        }

        private static int ComponentOrdinal(SceneDocument document, SceneModel model)
        {
            List<long> components = model.Components(document._gameObjectId);
            int ordinal = 0;
            foreach (long componentId in components)
            {
                if (componentId == document._fileId)
                {
                    break;
                }

                SceneDocument sibling = model.Find(componentId);
                if (sibling != null && sibling._classId == document._classId)
                {
                    ++ordinal;
                }
            }

            return ordinal;
        }

        private static void AssignNewFileIds(List<SceneDocument> documents, HashSet<long> reserved)
        {
            List<SceneDocument> ordered = new List<SceneDocument>(documents);
            ordered.Sort(static (left, right) => CompareByKeyThenNormalized(left, right));

            HashSet<long> used = reserved;
            foreach (long identifier in ReservedFileIds)
            {
                used.Add(identifier);
            }
            foreach (SceneDocument document in ordered)
            {
                if (document._fileId == SceneRootsSentinelFileId)
                {
                    document._newFileId = document._fileId;
                    used.Add(document._fileId);
                }
            }

            foreach (SceneDocument document in ordered)
            {
                if (document._fileId == SceneRootsSentinelFileId)
                {
                    continue;
                }

                long candidate = BaseFileId(document._key);
                while (!used.Add(candidate))
                {
                    ++candidate;
                    if (FileIdMask < candidate)
                    {
                        candidate = 1;
                    }
                }

                document._newFileId = candidate;
            }
        }

        private static string RewriteDocument(SceneDocument document, Dictionary<long, long> remap)
        {
            string withAnchor = ReplaceAnchorId(
                document._text,
                document._newFileId.ToString(CultureInfo.InvariantCulture)
            );
            return TransformLocalRefs(withAnchor, remap, false);
        }

        private static string NormalizeBody(string text, HashSet<long> localIds)
        {
            string withAnchor = ReplaceAnchorId(text, AnchorToken);
            return TransformLocalRefs(withAnchor, null, true, localIds);
        }

        private static string ReplaceAnchorId(string text, string replacement)
        {
            int ampersand = text.IndexOf('&');
            if (ampersand < 0)
            {
                return text;
            }

            int start = ampersand + 1;
            int end = start;
            if (end < text.Length && text[end] == '-')
            {
                ++end;
            }

            while (end < text.Length && IsAsciiDigit(text[end]))
            {
                ++end;
            }

            return text.Substring(0, start) + replacement + text.Substring(end);
        }

        private static string TransformLocalRefs(
            string text,
            Dictionary<long, long> remap,
            bool tokenize,
            HashSet<long> localIds = null
        )
        {
            bool[] protectedText = FindScalarCharacters(text);
            int index = text.IndexOf(LocalRefPrefix, StringComparison.Ordinal);
            if (index < 0)
            {
                return text;
            }

            StringBuilder builder = new StringBuilder(text.Length + 8);
            int cursor = 0;
            while (0 <= index)
            {
                builder.Append(text, cursor, index - cursor);

                int numberStart = index + LocalRefPrefix.Length;
                int pointer = numberStart;
                if (pointer < text.Length && text[pointer] == '-')
                {
                    ++pointer;
                }

                int digitsStart = pointer;
                while (pointer < text.Length && IsAsciiDigit(text[pointer]))
                {
                    ++pointer;
                }

                bool hasDigits = digitsStart < pointer;
                bool isLocal =
                    !protectedText[index]
                    && hasDigits
                    && pointer < text.Length
                    && text[pointer] == '}';
                if (isLocal)
                {
                    if (localIds != null)
                    {
                        localIds.Add(
                            long.Parse(
                                text.Substring(numberStart, pointer - numberStart),
                                NumberStyles.AllowLeadingSign,
                                CultureInfo.InvariantCulture
                            )
                        );
                    }
                    if (tokenize)
                    {
                        builder.Append(LocalRefToken);
                    }
                    else
                    {
                        long original = long.Parse(
                            text.Substring(numberStart, pointer - numberStart),
                            NumberStyles.AllowLeadingSign,
                            CultureInfo.InvariantCulture
                        );
                        long mapped;
                        if (remap != null && remap.TryGetValue(original, out mapped))
                        {
                            builder
                                .Append(LocalRefPrefix)
                                .Append(mapped.ToString(CultureInfo.InvariantCulture))
                                .Append('}');
                        }
                        else
                        {
                            builder
                                .Append(LocalRefPrefix)
                                .Append(text, numberStart, pointer - numberStart)
                                .Append('}');
                        }
                    }

                    cursor = pointer + 1;
                }
                else
                {
                    builder.Append(LocalRefPrefix);
                    cursor = numberStart;
                }

                index = text.IndexOf(LocalRefPrefix, cursor, StringComparison.Ordinal);
            }

            builder.Append(text, cursor, text.Length - cursor);
            return builder.ToString();
        }

        private static string[] SplitLines(string text)
        {
            string[] lines = text.Split('\n');
            for (int i = 0, lineCount = lines.Length; i < lineCount; ++i)
            {
                lines[i] = lines[i].TrimEnd('\r');
            }

            return lines;
        }

        private static string FindFieldValue(string[] lines, string name)
        {
            foreach (string line in lines)
            {
                int start = 0;
                while (start < line.Length && line[start] == ' ')
                {
                    ++start;
                }

                if (!MatchesFieldName(line, start, name))
                {
                    continue;
                }

                int after = start + name.Length + 1;
                if (line.Length <= after)
                {
                    return string.Empty;
                }

                if (line[after] == ' ')
                {
                    return line.Substring(after + 1);
                }
            }

            return null;
        }

        private static bool MatchesFieldName(string line, int start, string name)
        {
            int length = name.Length;
            if (line.Length < start + length + 1)
            {
                return false;
            }

            if (string.CompareOrdinal(line, start, name, 0, length) != 0)
            {
                return false;
            }

            return line[start + length] == ':';
        }

        private static List<long> ParseRefListItems(
            string[] lines,
            string listName,
            string itemPrefix
        )
        {
            List<long> items = new List<long>();
            int listLine = -1;
            for (int i = 0, lineCount = lines.Length; i < lineCount; ++i)
            {
                string line = lines[i];
                int start = 0;
                while (start < line.Length && line[start] == ' ')
                {
                    ++start;
                }

                if (MatchesFieldName(line, start, listName))
                {
                    int after = start + listName.Length + 1;
                    string remainder =
                        after < line.Length ? line.Substring(after).Trim() : string.Empty;
                    if (string.Equals(remainder, EmptyList, StringComparison.Ordinal))
                    {
                        return items;
                    }

                    listLine = i;
                    break;
                }
            }

            if (listLine < 0)
            {
                return items;
            }

            int itemPrefixLength = itemPrefix.Length;
            for (int i = listLine + 1, lineCount = lines.Length; i < lineCount; ++i)
            {
                string line = lines[i];
                int start = 0;
                while (start < line.Length && line[start] == ' ')
                {
                    ++start;
                }

                if (
                    line.Length < start + itemPrefixLength
                    || string.CompareOrdinal(line, start, itemPrefix, 0, itemPrefixLength) != 0
                )
                {
                    break;
                }

                items.Add(ParseFileIdFromRef(line.Substring(start)));
            }

            return items;
        }

        private static long ParseFileIdFromRef(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return 0;
            }

            int label = value.IndexOf(FileIdLabel, StringComparison.Ordinal);
            if (label < 0)
            {
                return 0;
            }

            int pointer = label + FileIdLabel.Length;
            bool negative = pointer < value.Length && value[pointer] == '-';
            if (negative)
            {
                ++pointer;
            }

            int digitsStart = pointer;
            while (pointer < value.Length && IsAsciiDigit(value[pointer]))
            {
                ++pointer;
            }

            if (pointer == digitsStart)
            {
                return 0;
            }

            int numberStart = negative ? digitsStart - 1 : digitsStart;
            return long.Parse(
                value.Substring(numberStart, pointer - numberStart),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture
            );
        }

        private static string ExtractGuid(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            int label = value.IndexOf(GuidLabel, StringComparison.Ordinal);
            if (label < 0)
            {
                return string.Empty;
            }

            int start = label + GuidLabel.Length;
            int end = start;
            while (end < value.Length && IsHexDigit(value[end]))
            {
                ++end;
            }

            return value.Substring(start, end - start);
        }

        private static long BaseFileId(string key)
        {
            long id = (long)(FnvHash(key) & (ulong)FileIdMask);
            return id == 0 ? 1 : id;
        }

        private static ulong FnvHash(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            ulong hash = FnvOffsetBasis;
            foreach (byte octet in bytes)
            {
                hash ^= octet;
                hash *= FnvPrime;
            }

            return hash;
        }

        private static int CompareByNewFileId(SceneDocument left, SceneDocument right)
        {
            return left._newFileId.CompareTo(right._newFileId);
        }

        private static int CompareByKeyThenNormalized(SceneDocument left, SceneDocument right)
        {
            int byKey = string.CompareOrdinal(left._key, right._key);
            if (byKey != 0)
            {
                return byKey;
            }

            return string.CompareOrdinal(left._normalized, right._normalized);
        }

        private static bool IsTransform(int classId)
        {
            return classId == TransformClassId || classId == RectTransformClassId;
        }

        private static bool IsAsciiDigit(char value)
        {
            return '0' <= value && value <= '9';
        }

        private static bool IsHexDigit(char value)
        {
            return ('0' <= value && value <= '9')
                || ('a' <= value && value <= 'f')
                || ('A' <= value && value <= 'F');
        }
    }
}
