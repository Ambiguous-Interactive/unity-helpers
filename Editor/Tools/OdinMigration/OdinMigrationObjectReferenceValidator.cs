// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using Object = UnityEngine.Object;

    /// <summary>Checks retained reference addresses against live Unity objects.</summary>
    internal sealed class OdinMigrationObjectReferenceValidator
    {
        private const int SceneIdentifierType = 2;
        private const int SceneReadBufferCharacters = 4096;
        private const int MaximumSceneCharacters = 4 * 1024 * 1024;
        private const int MaximumSceneLines = 65536;
        private const string CorrespondingSourceObjectKey = "m_CorrespondingSourceObject";
        private const string PrefabInstanceKey = "m_PrefabInstance";

        private readonly Dictionary<
            string,
            IReadOnlyDictionary<long, AuthoredAssetDocument>
        > _scenes = new Dictionary<string, IReadOnlyDictionary<long, AuthoredAssetDocument>>(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly string _owningGuid;
        private readonly IReadOnlyList<string> _recorded;
        private readonly IReadOnlyList<Object> _objects;

        internal OdinMigrationObjectReferenceValidator(
            OdinMigrationSerializedData payload,
            string owningGuid,
            IReadOnlyList<Object> references
        )
        {
            if (payload == null || references == null || string.IsNullOrWhiteSpace(owningGuid))
            {
                throw new InvalidDataException(
                    "A retained payload, owning asset GUID, and live reference table are required."
                );
            }
            int required = payload.ReferencedUnityObjects.Count;
            List<Object> captured = new List<Object>();
            foreach (Object reference in references.AsValueEnumerableRequired())
            {
                if (captured.Count == required)
                {
                    throw new InvalidDataException("The live reference table has extra entries.");
                }
                captured.Add(reference);
            }
            if (captured.Count != required)
            {
                throw new InvalidDataException("The live reference table is incomplete.");
            }
            _owningGuid = owningGuid;
            _recorded = payload.ReferencedUnityObjects;
            _objects = captured.AsReadOnly();
        }

        private static IReadOnlyDictionary<long, AuthoredAssetDocument> ReadScene(string guid)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("A scene reference must name a saved Unity scene.");
            }
            string fullPath = OdinMigrationTool.GetValidatedAssetPath(assetPath);
            StringBuilder source = new StringBuilder();
            char[] buffer = new char[SceneReadBufferCharacters];
            using (StreamReader reader = new StreamReader(fullPath))
            {
                int read;
                while (0 < (read = reader.Read(buffer, 0, buffer.Length)))
                {
                    if (MaximumSceneCharacters - source.Length < read)
                    {
                        throw new InvalidDataException(
                            "The referenced scene exceeds the validation size limit."
                        );
                    }
                    source.Append(buffer, 0, read);
                }
            }
            List<string> lines = new List<string>();
            using (StringReader reader = new StringReader(source.ToString()))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (lines.Count == MaximumSceneLines)
                    {
                        throw new InvalidDataException(
                            "The referenced scene exceeds the validation line limit."
                        );
                    }
                    lines.Add(line);
                }
            }
            Dictionary<long, AuthoredAssetDocument> documents =
                new Dictionary<long, AuthoredAssetDocument>();
            foreach (
                AuthoredAssetDocument document in AuthoredAssetYaml
                    .ReadDocuments(lines)
                    .AsValueEnumerableRequired()
            )
            {
                if (documents.ContainsKey(document.FileId))
                {
                    throw new InvalidDataException(
                        "A referenced scene file identifier occurs more than once."
                    );
                }
                documents.Add(document.FileId, document);
            }
            return documents;
        }

        internal void Validate(int index, OdinMigrationDictionaryField field)
        {
            Object reference = _objects[index];
            string recorded = _recorded[index];
            if (AuthoredAssetYaml.IsNullObjectReference(recorded))
            {
                if (reference != null)
                {
                    throw new InvalidDataException(
                        "A recorded null reference names a live object."
                    );
                }
                return;
            }
            if (reference == null)
            {
                throw new InvalidDataException("A recorded Unity object is missing or destroyed.");
            }
            if (field == null || !field.AcceptsObject(reference))
            {
                throw new InvalidDataException(
                    "A live Unity object does not fit the dictionary element type."
                );
            }
            if (
                !AuthoredAssetYaml.TryResolveObjectReference(
                    recorded,
                    _owningGuid,
                    out long expectedFileId,
                    out string expectedGuid
                ) || !MatchesIdentity(reference, expectedGuid, expectedFileId)
            )
            {
                throw new InvalidDataException(
                    "A live Unity object does not match its retained GUID and file identifier."
                );
            }
        }

        private bool MatchesIdentity(Object reference, string expectedGuid, long expectedFileId)
        {
            if (EditorUtility.IsPersistent(reference))
            {
                return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        reference,
                        out string actualGuid,
                        out long actualFileId
                    )
                    && actualFileId == expectedFileId
                    && string.Equals(actualGuid, expectedGuid, StringComparison.OrdinalIgnoreCase);
            }
            GlobalObjectId global = GlobalObjectId.GetGlobalObjectIdSlow(reference);
            if (
                global.identifierType != SceneIdentifierType
                || !string.Equals(
                    global.assetGUID.ToString(),
                    expectedGuid,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return false;
            }
            if (global.targetPrefabId == 0)
            {
                return global.targetObjectId == unchecked((ulong)expectedFileId);
            }
            if (
                !_scenes.TryGetValue(
                    expectedGuid,
                    out IReadOnlyDictionary<long, AuthoredAssetDocument> documents
                )
            )
            {
                documents = ReadScene(expectedGuid);
                _scenes.Add(expectedGuid, documents);
            }
            return documents.TryGetValue(expectedFileId, out AuthoredAssetDocument matched)
                && matched.TryGetEntry(CorrespondingSourceObjectKey, out AuthoredAssetEntry source)
                && matched.TryGetEntry(PrefabInstanceKey, out AuthoredAssetEntry instance)
                && AuthoredAssetYaml.TryParseObjectReference(
                    source.InlineValue,
                    out long sourceId,
                    out string sourceGuid
                )
                && AuthoredAssetYaml.TryParseObjectReference(
                    instance.InlineValue,
                    out long instanceId,
                    out string _
                )
                && MatchesPrefabSource(reference, sourceGuid, sourceId)
                && global.targetPrefabId == unchecked((ulong)instanceId);
        }

        private static bool MatchesPrefabSource(
            Object reference,
            string expectedGuid,
            long expectedFileId
        )
        {
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(reference);
            return source != null
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    source,
                    out string actualGuid,
                    out long actualFileId
                )
                && actualFileId == expectedFileId
                && string.Equals(actualGuid, expectedGuid, StringComparison.OrdinalIgnoreCase);
        }
    }
}
