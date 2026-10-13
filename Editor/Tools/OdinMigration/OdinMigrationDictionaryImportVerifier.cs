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
                using SerializedObject serialized = new SerializedObject(importedOwner);
                serialized.Update();
                foreach (OdinMigrationConvertedDictionary dictionary in dictionaries)
                {
                    OdinMigrationDictionaryField field = FindField(fields, dictionary.FieldName);
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
                            references.ValidateReference(
                                expected[index],
                                value.objectReferenceValue,
                                field
                            );
                        }
                    }
                }
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                failure = exception.Message;
                return false;
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
