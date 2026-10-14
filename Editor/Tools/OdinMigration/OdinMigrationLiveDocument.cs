// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using Object = UnityEngine.Object;

    internal static class OdinMigrationLiveDocument
    {
        internal static OdinMigrationApplyRecord.Document Capture(
            OdinMigrationDictionaryTarget target,
            AuthoredAssetDocument document,
            HashSet<object> originalLists,
            out OdinMigrationCompiledBinding binding,
            out string fragment
        )
        {
            WallstopStudios.UnityHelpers.Core.Helper.UnityMainThreadGuard.EnsureMainThread();
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                throw new InvalidOperationException(
                    "Wait for compilation and asset import before live capture."
                );
            }
            Object owner = target.LiveOwner;
            if (owner == null || EditorUtility.IsDirty(owner))
            {
                throw new InvalidDataException(
                    "A saved, caller-loaded original owner is required for live dictionary migration."
                );
            }
            string owningGuid = AssetDatabase.AssetPathToGUID(target.AssetPath);
            OdinMigrationObjectReferenceValidator validator = new(owningGuid);
            if (!validator.MatchesIdentity(owner, owningGuid, target.FileId))
            {
                throw new InvalidDataException(
                    "The live owner does not match its saved asset and document identifier."
                );
            }
            if (
                document == null
                || document.IsStripped
                || !document.TryGetEntry(
                    AuthoredAssetDocument.ScriptKey,
                    out AuthoredAssetEntry script
                )
                || !AuthoredAssetYaml.TryParseObjectReference(
                    script.InlineValue,
                    out long _,
                    out string scriptGuid
                )
                || string.IsNullOrWhiteSpace(scriptGuid)
                || document.TryGetEntry("serializationData", out AuthoredAssetEntry _)
                || document.TryGetEntry("_serializationData", out _)
            )
            {
                throw new InvalidDataException(
                    "A live dictionary target requires an original document without retained Odin data."
                );
            }
            OdinMigrationSerializedData identity = new(
                target.FileId,
                scriptGuid,
                string.Empty,
                0,
                0,
                new List<OdinMigrationSerializedNode>(),
                new List<string>()
            );
            if (
                !OdinMigrationSourceBinder.TryBind(
                    identity,
                    target.Fields,
                    out OdinMigrationCompiledBinding compiled,
                    out string failure
                )
                || compiled.OriginalType != owner.GetType()
            )
            {
                throw new InvalidDataException(
                    failure ?? "The live owner's compiled type differs from its original script."
                );
            }
            List<OdinMigrationApplyRecord.Field> fields = new();
            StringBuilder yaml = new();
            int delivered = 0;
            foreach (
                OdinMigrationDictionaryField schema in target.Fields.AsValueEnumerableRequired()
            )
            {
                if (
                    document.TryGetEntry(schema.FieldName, out AuthoredAssetEntry existing)
                    && existing.Indent == 2
                )
                {
                    throw new InvalidDataException(
                        "A live dictionary field already exists in the saved Unity document."
                    );
                }
                OdinMigrationCompiledField reader = null;
                foreach (
                    OdinMigrationCompiledField field in compiled.Fields.AsValueEnumerableRequired()
                )
                {
                    if (string.Equals(field.Name, schema.FieldName, StringComparison.Ordinal))
                    {
                        reader = field;
                        break;
                    }
                }
                if (reader == null || !reader.CanRestore)
                {
                    throw new InvalidDataException(
                        "The live dictionary requires a generated capture and recovery writer: "
                            + schema.FieldName
                    );
                }
                if (
                    !reader.TryCapture(
                        owner,
                        out OdinMigrationLiveDictionary snapshot,
                        out string captureFailure
                    )
                )
                {
                    throw new InvalidDataException(
                        "The live dictionary has no complete generated capture: "
                            + schema.FieldName
                            + ": "
                            + captureFailure
                    );
                }
                if (snapshot.IsNull || !snapshot.HasDefaultComparer)
                {
                    throw new InvalidDataException(
                        "A null live dictionary or custom comparer requires a semantics-preserving conversion."
                    );
                }
                List<OdinMigrationApplyRecord.Entry> entries = new();
                List<IReadOnlyList<string>> values = new();
                int entryCount = snapshot.Keys.Count;
                for (int index = 0; index < entryCount; ++index)
                {
                    OdinMigrationApplyRecord.RequireItem(ref delivered);
                    IReadOnlyList<Object> references = snapshot.Values[index];
                    if (references == null || !originalLists.Add(snapshot.ListIdentities[index]))
                    {
                        throw new InvalidDataException(
                            "Null or shared live lists require an identity-preserving conversion."
                        );
                    }
                    List<OdinMigrationApplyRecord.Reference> captured = new();
                    List<string> addresses = new();
                    foreach (Object reference in references.AsValueEnumerableRequired())
                    {
                        OdinMigrationApplyRecord.RequireItem(ref delivered);
                        string address = Address(reference, owningGuid);
                        validator.ValidateReference(address, reference, schema);
                        addresses.Add(address);
                        captured.Add(
                            new OdinMigrationApplyRecord.Reference
                            {
                                address = address,
                                typeIdentity =
                                    reference == null
                                        ? string.Empty
                                        : reference.GetType().AssemblyQualifiedName,
                            }
                        );
                    }
                    values.Add(addresses.AsReadOnly());
                    entries.Add(
                        new OdinMigrationApplyRecord.Entry
                        {
                            key = snapshot.Keys[index],
                            references = captured.ToArray(),
                        }
                    );
                }
                yaml.Append(
                    OdinMigrationDictionaryConverter.Render(schema.FieldName, snapshot.Keys, values)
                );
                fields.Add(
                    new OdinMigrationApplyRecord.Field
                    {
                        name = schema.FieldName,
                        entries = entries.ToArray(),
                    }
                );
            }
            binding = compiled;
            fragment = yaml.ToString();
            return new OdinMigrationApplyRecord.Document
            {
                assetPath = target.AssetPath,
                fileId = target.FileId,
                owningGuid = owningGuid,
                scriptGuid = scriptGuid,
                ownerTypeIdentity = compiled.OriginalType.AssemblyQualifiedName,
                liveCaptured = true,
                fields = fields.ToArray(),
            };
        }

        internal static void Restore(
            OdinMigrationApplyRecord record,
            string root,
            OdinMigrationApplyRecord.Document document,
            Object owner
        )
        {
            if (
                !OdinMigrationCompiledBindings.TryGet(
                    owner.GetType(),
                    out OdinMigrationCompiledBinding binding
                )
                || !string.Equals(
                    owner.GetType().AssemblyQualifiedName,
                    document.ownerTypeIdentity,
                    StringComparison.Ordinal
                )
            )
            {
                throw new IOException(
                    "The original live dictionary owner has no matching recovery binding."
                );
            }
            OdinMigrationObjectReferenceValidator validator = new(document.owningGuid);
            foreach (OdinMigrationApplyRecord.Field saved in document.fields)
            {
                OdinMigrationCompiledField reader = null;
                foreach (
                    OdinMigrationCompiledField field in binding.Fields.AsValueEnumerableRequired()
                )
                {
                    if (string.Equals(field.Name, saved.name, StringComparison.Ordinal))
                    {
                        if (reader != null)
                        {
                            throw new IOException("The original recovery field is ambiguous.");
                        }
                        reader = field;
                    }
                }
                if (reader == null)
                {
                    throw new IOException("The original dictionary recovery field is missing.");
                }
                List<int> keys = new();
                List<IReadOnlyList<Object>> values = new();
                foreach (OdinMigrationApplyRecord.Entry entry in saved.entries)
                {
                    keys.Add(entry.key);
                    List<Object> references = new();
                    foreach (OdinMigrationApplyRecord.Reference reference in entry.references)
                    {
                        Object restored = null;
                        if (!AuthoredAssetYaml.IsNullObjectReference(reference.address))
                        {
                            if (
                                !AuthoredAssetYaml.TryResolveObjectReference(
                                    reference.address,
                                    document.owningGuid,
                                    out long fileId,
                                    out string guid
                                )
                            )
                            {
                                throw new IOException(
                                    "An original live reference has no saved identity."
                                );
                            }
                            string path = AssetDatabase.GUIDToAssetPath(guid);
                            foreach (
                                Object candidate in OdinMigrationApplySceneScope.LoadObjects(
                                    record,
                                    root,
                                    path
                                )
                            )
                            {
                                if (
                                    candidate != null
                                    && validator.MatchesIdentity(candidate, guid, fileId)
                                )
                                {
                                    restored = candidate;
                                    break;
                                }
                            }
                            if (
                                restored == null
                                || !string.Equals(
                                    restored.GetType().AssemblyQualifiedName,
                                    reference.typeIdentity,
                                    StringComparison.Ordinal
                                )
                            )
                            {
                                throw new IOException(
                                    "An original live reference is missing or has a different type."
                                );
                            }
                        }
                        references.Add(restored);
                    }
                    values.Add(references.AsReadOnly());
                }
                if (
                    !reader.TryRestore(
                        owner,
                        keys.AsReadOnly(),
                        values.AsReadOnly(),
                        out string failure
                    )
                    || !reader.TryCapture(
                        owner,
                        out OdinMigrationLiveDictionary snapshot,
                        out failure
                    )
                )
                {
                    throw new IOException(failure);
                }
                if (snapshot.IsNull || snapshot.Keys.Count != keys.Count)
                {
                    throw new IOException(
                        "The restored original dictionary differs from its capture."
                    );
                }
                int entryCount = keys.Count;
                for (int index = 0; index < entryCount; ++index)
                {
                    if (
                        snapshot.Keys[index] != keys[index]
                        || snapshot.Values[index] == null
                        || snapshot.Values[index].Count != values[index].Count
                    )
                    {
                        throw new IOException(
                            "The restored original dictionary keys or list lengths differ."
                        );
                    }
                    int referenceCount = values[index].Count;
                    for (int referenceIndex = 0; referenceIndex < referenceCount; ++referenceIndex)
                    {
                        if (
                            !ReferenceEquals(
                                snapshot.Values[index][referenceIndex],
                                values[index][referenceIndex]
                            )
                        )
                        {
                            throw new IOException(
                                "The restored original dictionary reference identity differs."
                            );
                        }
                    }
                }
            }
        }

        internal static string Address(Object reference, string owningGuid)
        {
            if (ReferenceEquals(reference, null))
            {
                return "{fileID: 0}";
            }
            if (reference == null)
            {
                throw new InvalidDataException(
                    "A destroyed live reference must be repaired before migration."
                );
            }
            if (EditorUtility.IsPersistent(reference))
            {
                if (
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        reference,
                        out string guid,
                        out long fileId
                    ) || string.IsNullOrWhiteSpace(AssetDatabase.GUIDToAssetPath(guid))
                )
                {
                    throw new InvalidDataException("A live object has no saved asset reference.");
                }
                OdinMigrationTool.GetValidatedAssetPath(AssetDatabase.GUIDToAssetPath(guid));
                return "{fileID: "
                    + fileId.ToString(CultureInfo.InvariantCulture)
                    + ", guid: "
                    + guid
                    + ", type: "
                    + (AssetDatabase.IsNativeAsset(reference) ? "2" : "3")
                    + "}";
            }
            GlobalObjectId global = GlobalObjectId.GetGlobalObjectIdSlow(reference);
            if (
                global.identifierType != 2
                || global.targetPrefabId != 0
                || !string.Equals(
                    global.assetGUID.ToString(),
                    owningGuid,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                throw new InvalidDataException(
                    "A live scene reference must belong to its saved owner scene and have no prefab override."
                );
            }
            return "{fileID: "
                + unchecked((long)global.targetObjectId).ToString(CultureInfo.InvariantCulture)
                + "}";
        }
    }
}
