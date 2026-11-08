// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>Resolves component and hierarchy references within one authored YAML file.</summary>
    /// <remarks>
    /// Build one index per file. Zero and duplicate anchors cannot be resolved. External references
    /// never resolve through this index. Queries read authored data without loading Unity objects.
    /// </remarks>
    public sealed class AuthoredAssetHierarchy
    {
        private const int GameObjectTypeId = 1;
        private const int TransformTypeId = 4;
        private const int RectTransformTypeId = 224;
        private const int FieldIndent = 2;
        private const string ComponentsField = "m_Component";
        private const string ComponentField = "component";
        private const string FatherField = "m_Father";
        private const string NameField = "m_Name";

        private readonly Dictionary<long, AuthoredAssetDocument> _documents = new();

        /// <summary>Indexes nonzero document anchors, retaining duplicate anchors as ambiguous.</summary>
        public AuthoredAssetHierarchy(IReadOnlyList<AuthoredAssetDocument> documents)
        {
            if (documents == null)
            {
                return;
            }
            int count = documents.Count;
            for (int index = 0; index < count; ++index)
            {
                AuthoredAssetDocument document = documents[index];
                if (document == null || document.FileId == 0)
                {
                    continue;
                }
                if (_documents.ContainsKey(document.FileId))
                {
                    _documents[document.FileId] = null;
                }
                else
                {
                    _documents.Add(document.FileId, document);
                }
            }
        }

        private static bool TryReadFieldReference(
            AuthoredAssetDocument document,
            string field,
            out long fileId
        )
        {
            if (TryGetDirectEntry(document, field, out AuthoredAssetEntry entry))
            {
                return TryReadLocalReference(entry.InlineValue, out fileId);
            }
            fileId = 0;
            return false;
        }

        private static bool TryGetDirectEntry(
            AuthoredAssetDocument document,
            string field,
            out AuthoredAssetEntry entry
        )
        {
            return AuthoredAssetYaml.TryGetOnlyDirectEntry(document, field, out entry);
        }

        private static bool TryReadLocalReference(string value, out long fileId)
        {
            if (
                AuthoredAssetYaml.TryParseObjectReference(value, out long found, out string guid)
                && found != 0
                && string.IsNullOrWhiteSpace(guid)
            )
            {
                fileId = found;
                return true;
            }
            fileId = 0;
            return false;
        }

        /// <summary>Finds a uniquely anchored document in this file.</summary>
        public bool TryGetDocument(long fileId, out AuthoredAssetDocument document)
        {
            if (_documents.TryGetValue(fileId, out AuthoredAssetDocument found) && found != null)
            {
                document = found;
                return true;
            }
            document = null;
            return false;
        }

        /// <summary>Reads local, nonzero component anchors in the GameObject's authored list order.</summary>
        /// <remarks>Missing anchors and repeated list entries remain visible; external references are omitted.</remarks>
        public IEnumerable<long> ComponentIdsOf(AuthoredAssetDocument gameObject)
        {
            if (!Owns(gameObject) || gameObject.UnityTypeId != GameObjectTypeId)
            {
                yield break;
            }
            if (!TryGetDirectEntry(gameObject, ComponentsField, out AuthoredAssetEntry components))
            {
                yield break;
            }
            IReadOnlyList<AuthoredAssetEntry> entries = gameObject.Entries;
            for (int index = 0; index < entries.Count; ++index)
            {
                AuthoredAssetEntry entry = entries[index];
                if (
                    entry.Indent == FieldIndent * 2
                    && entry.SequenceDepth == 1
                    && components.LineNumber < entry.LineNumber
                    && entry.LineNumber < components.EndLineNumber
                    && string.Equals(entry.Key, ComponentField, StringComparison.Ordinal)
                    && TryReadLocalReference(entry.InlineValue, out long fileId)
                )
                {
                    yield return fileId;
                }
            }
        }

        /// <summary>Resolves existing documents in the GameObject's authored component list order.</summary>
        /// <remarks>Missing or ambiguous anchors are omitted; repeated list entries remain repeated.</remarks>
        public IEnumerable<AuthoredAssetDocument> ComponentsOf(AuthoredAssetDocument gameObject)
        {
            foreach (long fileId in ComponentIdsOf(gameObject))
            {
                if (TryGetDocument(fileId, out AuthoredAssetDocument document))
                {
                    yield return document;
                }
            }
        }

        /// <summary>Resolves the component's local GameObject reference.</summary>
        public bool TryGetOwner(AuthoredAssetDocument component, out AuthoredAssetDocument owner)
        {
            if (
                Owns(component)
                && TryReadFieldReference(
                    component,
                    AuthoredAssetYaml.GameObjectField,
                    out long fileId
                )
                && TryGetDocument(fileId, out AuthoredAssetDocument found)
                && found.UnityTypeId == GameObjectTypeId
            )
            {
                owner = found;
                return true;
            }
            owner = null;
            return false;
        }

        /// <summary>Resolves the component list of the component's owning GameObject.</summary>
        /// <remarks>The result includes the supplied component when the owner lists it.</remarks>
        public IEnumerable<AuthoredAssetDocument> SiblingComponents(AuthoredAssetDocument component)
        {
            return TryGetOwner(component, out AuthoredAssetDocument owner)
                ? ComponentsOf(owner)
                : Array.Empty<AuthoredAssetDocument>();
        }

        /// <summary>Reads the owner's authored name, or the supplied fallback when unavailable.</summary>
        public string OwnerName(
            AuthoredAssetDocument component,
            string fallback = "<unnamed object>"
        )
        {
            if (TryGetOwner(component, out AuthoredAssetDocument owner))
            {
                string name = AuthoredAssetYaml.Field(owner, NameField);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
            return fallback;
        }

        /// <summary>Walks the component's owner and its parents, nearest first, stopping before a cycle.</summary>
        /// <remarks>Missing, external, ambiguous or multiple transform links end the walk.</remarks>
        public IEnumerable<long> SelfAndAncestorObjectIds(AuthoredAssetDocument component)
        {
            if (!TryGetOwner(component, out AuthoredAssetDocument owner))
            {
                yield break;
            }
            using PooledResource<HashSet<long>> lease = Buffers<long>.HashSet.Get(
                out HashSet<long> visited
            );
            while (visited.Add(owner.FileId))
            {
                yield return owner.FileId;
                AuthoredAssetDocument transform = null;
                foreach (AuthoredAssetDocument candidate in ComponentsOf(owner))
                {
                    if (
                        candidate.UnityTypeId != TransformTypeId
                        && candidate.UnityTypeId != RectTransformTypeId
                    )
                    {
                        continue;
                    }
                    if (transform != null)
                    {
                        yield break;
                    }
                    transform = candidate;
                }
                if (
                    transform == null
                    || !TryGetOwner(transform, out AuthoredAssetDocument transformOwner)
                    || !ReferenceEquals(owner, transformOwner)
                    || !TryReadFieldReference(transform, FatherField, out long fatherId)
                    || !TryGetDocument(fatherId, out AuthoredAssetDocument father)
                    || (
                        father.UnityTypeId != TransformTypeId
                        && father.UnityTypeId != RectTransformTypeId
                    )
                    || !TryGetOwner(father, out owner)
                )
                {
                    yield break;
                }
            }
        }

        private bool Owns(AuthoredAssetDocument document)
        {
            return document != null
                && TryGetDocument(document.FileId, out AuthoredAssetDocument found)
                && ReferenceEquals(document, found);
        }
    }
#endif
}
