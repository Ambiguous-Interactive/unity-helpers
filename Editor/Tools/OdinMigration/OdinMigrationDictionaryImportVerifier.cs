// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using Object = UnityEngine.Object;

    internal static class OdinMigrationDictionaryImportVerifier
    {
        internal static bool TryVerify(
            OdinMigrationSerializedData payload,
            IReadOnlyList<OdinMigrationDictionaryField> fields,
            string owningAssetPath,
            Object importedOwner,
            IReadOnlyList<Object> referencedObjects,
            out string failure
        )
        {
            try
            {
                UnityMainThreadGuard.EnsureMainThread();
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    throw new InvalidOperationException(
                        "Wait for compilation and asset import to finish."
                    );
                }
                if (payload == null || importedOwner == null)
                {
                    throw new InvalidDataException(
                        "A retained payload and its loaded imported owner are required."
                    );
                }
                OdinMigrationTool.GetValidatedAssetPath(owningAssetPath);
                string guid = AssetDatabase.AssetPathToGUID(owningAssetPath);
                OdinMigrationObjectReferenceValidator references =
                    new OdinMigrationObjectReferenceValidator(payload, guid, referencedObjects);
                if (!references.MatchesIdentity(importedOwner, guid, payload.FileId))
                {
                    throw new InvalidDataException(
                        "The imported owner does not match the retained asset and document identifier."
                    );
                }
                MonoScript script =
                    importedOwner is MonoBehaviour component
                        ? MonoScript.FromMonoBehaviour(component)
                    : importedOwner is ScriptableObject asset
                        ? MonoScript.FromScriptableObject(asset)
                    : null;
                if (
                    script == null
                    || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                        script,
                        out string scriptGuid,
                        out long _
                    )
                    || !string.Equals(
                        scriptGuid,
                        payload.ScriptGuid,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    throw new InvalidDataException(
                        "The imported owner's script does not match the retained script GUID."
                    );
                }
                if (
                    !OdinMigrationDictionaryConverter.TryDecode(
                        payload,
                        fields,
                        references,
                        out IReadOnlyList<OdinMigrationConvertedDictionary> dictionaries,
                        out failure
                    )
                )
                {
                    return false;
                }
                VerifyValues(importedOwner, dictionaries, references, fields, null);
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        internal static bool TryVerifyCaptured(
            OdinMigrationApplyRecord.Document document,
            Object importedOwner,
            out string failure
        )
        {
            try
            {
                UnityMainThreadGuard.EnsureMainThread();
                if (
                    EditorApplication.isCompiling
                    || EditorApplication.isUpdating
                    || importedOwner == null
                )
                {
                    throw new InvalidOperationException(
                        "An imported owner and an idle editor are required."
                    );
                }
                OdinMigrationTool.GetValidatedAssetPath(document.assetPath);
                string guid = AssetDatabase.AssetPathToGUID(document.assetPath);
                OdinMigrationObjectReferenceValidator references =
                    new OdinMigrationObjectReferenceValidator(guid);
                MonoScript script =
                    importedOwner is MonoBehaviour component
                        ? MonoScript.FromMonoBehaviour(component)
                    : importedOwner is ScriptableObject asset
                        ? MonoScript.FromScriptableObject(asset)
                    : null;
                if (
                    !string.Equals(guid, document.owningGuid, StringComparison.OrdinalIgnoreCase)
                    || !references.MatchesIdentity(importedOwner, guid, document.fileId)
                    || script == null
                    || !string.Equals(
                        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script)),
                        document.scriptGuid,
                        StringComparison.OrdinalIgnoreCase
                    )
                    || !string.Equals(
                        importedOwner.GetType().AssemblyQualifiedName,
                        document.ownerTypeIdentity,
                        StringComparison.Ordinal
                    )
                )
                {
                    throw new InvalidDataException(
                        "An imported dictionary owner changed its saved or compiled identity."
                    );
                }
                List<OdinMigrationConvertedDictionary> dictionaries =
                    new List<OdinMigrationConvertedDictionary>();
                Dictionary<string, string> types = new Dictionary<string, string>(
                    StringComparer.Ordinal
                );
                foreach (OdinMigrationApplyRecord.Field field in document.fields)
                {
                    List<int> keys = new List<int>();
                    List<IReadOnlyList<string>> values = new List<IReadOnlyList<string>>();
                    foreach (OdinMigrationApplyRecord.Entry entry in field.entries)
                    {
                        keys.Add(entry.key);
                        List<string> addresses = new List<string>();
                        foreach (OdinMigrationApplyRecord.Reference reference in entry.references)
                        {
                            addresses.Add(reference.address);
                            if (
                                types.TryGetValue(reference.address, out string previous)
                                && !string.Equals(
                                    previous,
                                    reference.typeIdentity,
                                    StringComparison.Ordinal
                                )
                            )
                            {
                                throw new InvalidDataException(
                                    "A captured Unity reference has conflicting type identities."
                                );
                            }
                            types[reference.address] = reference.typeIdentity;
                        }
                        values.Add(addresses.AsReadOnly());
                    }
                    dictionaries.Add(
                        new OdinMigrationConvertedDictionary(field.name, keys, values)
                    );
                }
                VerifyValues(importedOwner, dictionaries, references, null, types);
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
            }
        }

        private static void VerifyValues(
            Object importedOwner,
            IReadOnlyList<OdinMigrationConvertedDictionary> dictionaries,
            OdinMigrationObjectReferenceValidator references,
            IReadOnlyList<OdinMigrationDictionaryField> fields,
            IReadOnlyDictionary<string, string> referenceTypes
        )
        {
            using SerializedObject serialized = new SerializedObject(importedOwner);
            serialized.Update();
            foreach (OdinMigrationConvertedDictionary dictionary in dictionaries)
            {
                OdinMigrationDictionaryField field =
                    fields == null ? null : FindField(fields, dictionary.FieldName);
                SerializedProperty property = serialized.FindProperty(dictionary.FieldName);
                SerializedProperty keys = property?.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Keys
                );
                SerializedProperty values = property?.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.BoxedValues
                );
                if (
                    keys == null
                    || values == null
                    || !keys.isArray
                    || !values.isArray
                    || keys.arraySize != dictionary.Keys.Count
                    || values.arraySize != dictionary.Values.Count
                )
                {
                    throw new InvalidDataException(
                        "Imported dictionary arrays do not match the retained entry count: "
                            + dictionary.FieldName
                    );
                }
                for (int entry = 0; entry < dictionary.Keys.Count; ++entry)
                {
                    SerializedProperty key = keys.GetArrayElementAtIndex(entry);
                    if (
                        (
                            key.propertyType != SerializedPropertyType.Enum
                            && key.propertyType != SerializedPropertyType.Integer
                        )
                        || key.intValue != dictionary.Keys[entry]
                    )
                    {
                        throw new InvalidDataException(
                            "An imported dictionary key differs from the retained key: "
                                + dictionary.FieldName
                        );
                    }
                    SerializedProperty data = values
                        .GetArrayElementAtIndex(entry)
                        .FindPropertyRelative(
                            SerializableDictionarySerializedPropertyNames.BoxedValueData
                        );
                    IReadOnlyList<string> expected = dictionary.Values[entry];
                    if (data == null || !data.isArray || data.arraySize != expected.Count)
                    {
                        throw new InvalidDataException(
                            "An imported dictionary list differs from the retained length: "
                                + dictionary.FieldName
                        );
                    }
                    for (int index = 0; index < expected.Count; ++index)
                    {
                        SerializedProperty value = data.GetArrayElementAtIndex(index);
                        if (value.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            throw new InvalidDataException(
                                "An imported dictionary value is not a Unity object reference."
                            );
                        }
                        ValidateValue(
                            referenceTypes,
                            references,
                            expected[index],
                            value.objectReferenceValue,
                            field
                        );
                    }
                }
            }
        }

        private static void ValidateValue(
            IReadOnlyDictionary<string, string> referenceTypes,
            OdinMigrationObjectReferenceValidator references,
            string address,
            Object value,
            OdinMigrationDictionaryField field
        )
        {
            if (field != null)
            {
                references.ValidateReference(address, value, field);
            }
            else
            {
                references.ValidateReferenceIdentity(address, value);
            }
            if (
                referenceTypes != null
                && value != null
                && (
                    !referenceTypes.TryGetValue(address, out string typeIdentity)
                    || !string.Equals(
                        value.GetType().AssemblyQualifiedName,
                        typeIdentity,
                        StringComparison.Ordinal
                    )
                )
            )
            {
                throw new InvalidDataException(
                    "An imported Unity reference changed its captured runtime type."
                );
            }
        }

        private static OdinMigrationDictionaryField FindField(
            IReadOnlyList<OdinMigrationDictionaryField> fields,
            string name
        )
        {
            foreach (OdinMigrationDictionaryField field in fields)
            {
                if (string.Equals(field.FieldName, name, StringComparison.Ordinal))
                {
                    return field;
                }
            }
            throw new InvalidDataException("A retained dictionary schema is missing.");
        }
    }
}
