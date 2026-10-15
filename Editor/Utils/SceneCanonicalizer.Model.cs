// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    public static partial class SceneCanonicalizer
    {
        private sealed class SceneModel
        {
            private static readonly List<long> NoComponents = new List<long>(0);

            private readonly Dictionary<long, SceneDocument> _byId;
            private readonly Dictionary<long, long> _transformByGameObject;
            private readonly Dictionary<long, long> _father;
            private readonly Dictionary<long, int> _childIndex;
            private readonly Dictionary<long, int> _rootIndex;
            private readonly Dictionary<long, int> _prefabSiblingIndex;
            private readonly Dictionary<long, string> _nameByGameObject;
            private readonly Dictionary<long, List<long>> _componentsByGameObject;

            internal SceneModel(List<SceneDocument> documents, Dictionary<long, SceneDocument> byId)
            {
                _byId = byId;
                _transformByGameObject = new Dictionary<long, long>(documents.Count);
                _father = new Dictionary<long, long>(documents.Count);
                _childIndex = new Dictionary<long, int>(documents.Count);
                _rootIndex = new Dictionary<long, int>();
                _prefabSiblingIndex = new Dictionary<long, int>();
                _nameByGameObject = new Dictionary<long, string>(documents.Count);
                _componentsByGameObject = new Dictionary<long, List<long>>(documents.Count);

                foreach (SceneDocument document in documents)
                {
                    if (document._classId == GameObjectClassId)
                    {
                        string name = FindFieldValue(document._lines, FieldName);
                        _nameByGameObject[document._fileId] = name ?? string.Empty;
                        _componentsByGameObject[document._fileId] = ParseRefListItems(
                            document._lines,
                            FieldComponent,
                            ComponentItemPrefix
                        );
                    }
                    else if (IsTransform(document._classId))
                    {
                        if (
                            document._gameObjectId != 0
                            && !_transformByGameObject.TryAdd(
                                document._gameObjectId,
                                document._fileId
                            )
                        )
                        {
                            throw new InvalidDataException(
                                "A GameObject cannot own multiple transforms."
                            );
                        }
                        if (
                            int.TryParse(
                                FindFieldValue(document._lines, FieldRootOrder),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out int rootOrder
                            )
                        )
                        {
                            _rootIndex[document._fileId] = rootOrder;
                            RecordPrefabSiblingIndex(document._fileId, rootOrder);
                        }
                        _father[document._fileId] = ParseFileIdFromRef(
                            FindFieldValue(document._lines, FieldFather)
                        );

                        List<long> children = ParseRefListItems(
                            document._lines,
                            FieldChildren,
                            ChildItemPrefix
                        );
                        for (int i = 0; i < children.Count; ++i)
                        {
                            _childIndex[children[i]] = i;
                            RecordPrefabSiblingIndex(children[i], i);
                        }
                    }
                    List<long> roots = ParseRefListItems(
                        document._lines,
                        FieldRoots,
                        ChildItemPrefix
                    );
                    for (int i = 0; i < roots.Count; ++i)
                    {
                        _rootIndex[roots[i]] = i;
                        RecordPrefabSiblingIndex(roots[i], i);
                    }
                }
                foreach (SceneDocument document in documents)
                {
                    if (document._classId == PrefabInstanceClassId)
                    {
                        RecordAddedGameObjects(document);
                    }
                }
                ValidateHierarchy(documents);
            }

            internal SceneDocument Find(long id)
            {
                SceneDocument document;
                return _byId.TryGetValue(id, out document) ? document : null;
            }

            internal List<long> Components(long gameObjectId)
            {
                List<long> components;
                return _componentsByGameObject.TryGetValue(gameObjectId, out components)
                    ? components
                    : NoComponents;
            }

            internal int SiblingIndex(long transformId)
            {
                bool hasFather = _father.TryGetValue(transformId, out long father) && father != 0;
                if (hasFather && _childIndex.TryGetValue(transformId, out int childIndex))
                {
                    return childIndex;
                }

                int rootIndex;
                return _rootIndex.TryGetValue(transformId, out rootIndex) ? rootIndex : 0;
            }

            internal int PrefabSiblingIndex(long prefabId)
            {
                return _prefabSiblingIndex.TryGetValue(prefabId, out int index) ? index : 0;
            }

            internal string HierarchyPath(long gameObjectId)
            {
                long current;
                if (!_transformByGameObject.TryGetValue(gameObjectId, out current))
                {
                    return string.Empty;
                }

                List<string> segments = new List<string>();
                int guard = 0;
                while (current != 0)
                {
                    if (MaxHierarchyDepth <= guard)
                    {
                        throw new InvalidDataException(
                            "The hierarchy is cyclic or exceeds the supported depth."
                        );
                    }
                    ++guard;
                    SceneDocument info;
                    if (!_byId.TryGetValue(current, out info))
                    {
                        break;
                    }

                    string name;
                    if (!_nameByGameObject.TryGetValue(info._gameObjectId, out name))
                    {
                        name = string.Empty;
                    }

                    segments.Add(
                        name.Replace("\\", "\\\\").Replace("/", "\\/").Replace("#", "\\#")
                            + KeyOrdinalSeparator
                            + SiblingIndex(current).ToString(CultureInfo.InvariantCulture)
                    );

                    if (!_father.TryGetValue(current, out long father) || father == 0)
                    {
                        break;
                    }

                    SceneDocument next;
                    bool haveNext = _byId.TryGetValue(father, out next);
                    if (haveNext && next._stripped)
                    {
                        segments.Add(StrippedHierarchyPath(next));
                        break;
                    }
                    if (!haveNext || !IsTransform(next._classId))
                    {
                        long source = haveNext
                            ? ParseFileIdFromRef(
                                FindFieldValue(next._lines, FieldCorrespondingSourceObject)
                            )
                            : father;
                        segments.Add(
                            KeyStrippedSource + source.ToString(CultureInfo.InvariantCulture)
                        );
                        break;
                    }

                    current = father;
                }

                StringBuilder builder = new StringBuilder();
                for (int i = segments.Count - 1; 0 <= i; --i)
                {
                    if (0 < builder.Length)
                    {
                        builder.Append('/');
                    }

                    builder.Append(segments[i]);
                }

                return builder.ToString();
            }

            internal string StrippedHierarchyPath(SceneDocument document)
            {
                string source = FindFieldValue(document._lines, FieldCorrespondingSourceObject);
                long ownerId = ParseFileIdFromRef(
                    FindFieldValue(document._lines, FieldPrefabInstance)
                );
                SceneDocument owner = Find(ownerId);
                string ownerKey =
                    owner == null ? string.Empty : BuildPrefabInstanceKey(owner, this);
                return KeyStrippedSource
                    + ParseFileIdFromRef(source).ToString(CultureInfo.InvariantCulture)
                    + KeyClassSeparator
                    + ExtractGuid(source)
                    + KeyClassSeparator
                    + ownerKey
                    + KeyOrdinalSeparator
                    + PrefabSiblingIndex(ownerId).ToString(CultureInfo.InvariantCulture);
            }

            private void ValidateHierarchy(List<SceneDocument> documents)
            {
                Dictionary<long, long> parents = new();
                foreach (SceneDocument document in documents)
                {
                    long parent = 0;
                    if (document._stripped)
                    {
                        parent = ParseFileIdFromRef(
                            FindFieldValue(document._lines, FieldPrefabInstance)
                        );
                    }
                    else if (document._classId == PrefabInstanceClassId)
                    {
                        parent = ParseFileIdFromRef(
                            FindFieldValue(document._lines, FieldTransformParent)
                        );
                    }
                    else if (IsTransform(document._classId))
                    {
                        parent = ParseFileIdFromRef(FindFieldValue(document._lines, FieldFather));
                    }
                    if (parent != 0)
                    {
                        parents[document._fileId] = parent;
                    }
                }
                Dictionary<long, int> depths = new();
                HashSet<long> visited = new();
                List<long> path = new();
                foreach (long start in parents.Keys)
                {
                    visited.Clear();
                    path.Clear();
                    long current = start;
                    while (
                        !depths.ContainsKey(current)
                        && parents.TryGetValue(current, out long parent)
                    )
                    {
                        if (!visited.Add(current) || MaxHierarchyDepth <= path.Count)
                        {
                            throw new InvalidDataException(
                                "The hierarchy is cyclic or exceeds the supported depth."
                            );
                        }
                        path.Add(current);
                        current = parent;
                    }
                    int depth = depths.TryGetValue(current, out int knownDepth) ? knownDepth : 0;
                    for (int i = path.Count - 1; 0 <= i; --i)
                    {
                        ++depth;
                        if (MaxHierarchyDepth < depth)
                        {
                            throw new InvalidDataException(
                                "The hierarchy exceeds the supported depth."
                            );
                        }
                        depths[path[i]] = depth;
                    }
                }
            }

            private void RecordAddedGameObjects(SceneDocument document)
            {
                int listIndent = -1;
                int ordinal = 0;
                foreach (string line in document._lines)
                {
                    int length = line.Length;
                    int start = 0;
                    while (start < length && line[start] == ' ')
                    {
                        ++start;
                    }
                    if (start == length)
                    {
                        continue;
                    }
                    if (listIndent < 0)
                    {
                        if (MatchesFieldName(line, start, FieldAddedGameObjects))
                        {
                            string value = line.Substring(start + FieldAddedGameObjects.Length + 1)
                                .Trim();
                            if (string.Equals(value, EmptyList, StringComparison.Ordinal))
                            {
                                return;
                            }
                            listIndent = start;
                        }
                        continue;
                    }
                    if (start <= listIndent && line[start] != '-')
                    {
                        break;
                    }
                    if (MatchesFieldName(line, start, FieldAddedObject))
                    {
                        long referenceId = ParseFileIdFromRef(
                            line.Substring(start + FieldAddedObject.Length + 1)
                        );
                        if (referenceId != 0)
                        {
                            long transformId = _transformByGameObject.TryGetValue(
                                referenceId,
                                out long transform
                            )
                                ? transform
                                : referenceId;
                            _childIndex[transformId] = ordinal;
                            RecordPrefabSiblingIndex(referenceId, ordinal);
                        }
                        ++ordinal;
                    }
                }
            }

            private void RecordPrefabSiblingIndex(long referenceId, int index)
            {
                if (!_byId.TryGetValue(referenceId, out SceneDocument document))
                {
                    return;
                }
                long prefabId =
                    document._classId == PrefabInstanceClassId
                        ? referenceId
                        : ParseFileIdFromRef(FindFieldValue(document._lines, FieldPrefabInstance));
                if (prefabId != 0)
                {
                    _prefabSiblingIndex[prefabId] = index;
                }
            }
        }
    }
}
