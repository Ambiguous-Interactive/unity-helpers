// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools.OdinMigration
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.Validation;

    internal static class OdinMigrationDictionaryConverter
    {
        private const int IntegerEntry = 3;
        private const int NullEntry = 6;
        private const int StartNodeEntry = 7;
        private const int EndNodeEntry = 8;
        private const int ExternalIndexEntry = 10;
        private const int StartArrayEntry = 12;
        private const int EndArrayEntry = 13;
        private const string ComparerName = "comparer";
        private const string KeyName = "$k";
        private const string ValueName = "$v";
        private const string PrefabName = "Prefab";
        private const string PrefabModificationsName = "PrefabModifications";
        private const string PrefabReferencesName = "PrefabModificationsReferencedUnityObjects";

        private static readonly HashSet<string> PayloadFields = new HashSet<string>(
            StringComparer.Ordinal
        )
        {
            "SerializedFormat",
            "SerializedBytes",
            "SerializedBytesString",
            nameof(OdinMigrationSerializedData.ReferencedUnityObjects),
            nameof(OdinMigrationSerializedData.SerializationNodes),
            PrefabName,
            PrefabModificationsName,
            PrefabReferencesName,
        };

        internal static bool TryConvert(
            OdinMigrationSerializedData payload,
            string fieldName,
            Type keyType,
            Type elementType,
            out string yaml,
            out string failure
        )
        {
            try
            {
                ValidateSchema(fieldName, keyType, elementType);
                if (payload == null)
                {
                    throw new InvalidDataException("A retained payload is required.");
                }
                ValidatePrefabState(payload);
                int position = 0;
                HashSet<int> identities = new HashSet<int>();
                string converted = ReadDictionary(
                    payload,
                    ref position,
                    fieldName,
                    keyType,
                    elementType,
                    identities
                );
                if (position != payload.SerializationNodes.Count)
                {
                    throw new InvalidDataException("Remaining records require further conversion.");
                }
                yaml = converted;
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                yaml = null;
                failure = exception.Message;
                return false;
            }
        }

        internal static bool TryConvert(
            OdinMigrationSerializedData payload,
            IReadOnlyList<OdinMigrationDictionaryField> fields,
            out string yaml,
            out string failure
        )
        {
            return TryConvert(payload, fields, null, out yaml, out failure);
        }

        internal static bool TryConvert(
            OdinMigrationSerializedData payload,
            IReadOnlyList<OdinMigrationDictionaryField> fields,
            OdinMigrationObjectReferenceValidator references,
            out string yaml,
            out string failure
        )
        {
            try
            {
                if (payload == null || fields == null || fields.Count == 0)
                {
                    throw new InvalidDataException(
                        "A retained payload and its dictionary field schemas are required."
                    );
                }
                Dictionary<string, OdinMigrationDictionaryField> remaining = new Dictionary<
                    string,
                    OdinMigrationDictionaryField
                >(StringComparer.Ordinal);
                int fieldCount = fields.Count;
                for (int index = 0; index < fieldCount; ++index)
                {
                    OdinMigrationDictionaryField field = fields[index];
                    if (field == null)
                    {
                        throw new InvalidDataException("A dictionary field schema is null.");
                    }
                    ValidateSchema(field.FieldName, field.KeyType, field.ElementType);
                    if (remaining.ContainsKey(field.FieldName))
                    {
                        throw new InvalidDataException("Dictionary field names must be unique.");
                    }
                    remaining.Add(field.FieldName, field);
                }
                ValidatePrefabState(payload);
                int position = 0;
                HashSet<int> identities = new HashSet<int>();
                StringBuilder result = new StringBuilder();
                int nodeCount = payload.SerializationNodes.Count;
                while (position < nodeCount)
                {
                    string name = payload.SerializationNodes[position].Name;
                    if (!remaining.TryGetValue(name, out OdinMigrationDictionaryField field))
                    {
                        throw new InvalidDataException(
                            "A retained field is undeclared or occurs more than once."
                        );
                    }
                    result.Append(
                        ReadDictionary(
                            payload,
                            ref position,
                            name,
                            field.KeyType,
                            field.ElementType,
                            identities,
                            references,
                            field
                        )
                    );
                    remaining.Remove(name);
                }
                if (remaining.Count != 0)
                {
                    throw new InvalidDataException(
                        "A declared dictionary field is absent from the retained payload."
                    );
                }
                yaml = result.ToString();
                failure = null;
                return true;
            }
            catch (Exception exception)
            {
                yaml = null;
                failure = exception.Message;
                return false;
            }
        }

        private static void ValidateSchema(string fieldName, Type keyType, Type elementType)
        {
            if (keyType == null || elementType == null || !IsIdentifier(fieldName))
            {
                throw new InvalidDataException(
                    "A field name, enum key type, and Unity object type are required."
                );
            }
            if (
                !keyType.IsEnum
                || Type.GetTypeCode(keyType) != TypeCode.Int32
                || !typeof(UnityEngine.Object).IsAssignableFrom(elementType)
            )
            {
                throw new InvalidDataException(
                    "This conversion requires an int-backed enum and a list of Unity objects."
                );
            }
        }

        private static string ReadDictionary(
            OdinMigrationSerializedData payload,
            ref int position,
            string fieldName,
            Type keyType,
            Type elementType,
            HashSet<int> identities,
            OdinMigrationObjectReferenceValidator objectReferences = null,
            OdinMigrationDictionaryField field = null
        )
        {
            IReadOnlyList<OdinMigrationSerializedNode> nodes = payload.SerializationNodes;
            string listType = "System.Collections.Generic.List`1<" + elementType.FullName + ">";
            string dictionaryType =
                "System.Collections.Generic.Dictionary`2<"
                + keyType.FullName
                + ","
                + listType
                + ">";
            RequireTypedNode(nodes, ref position, fieldName, dictionaryType, identities);
            ReadComparer(nodes, ref position, keyType.FullName, identities);
            long claimedPairs = ReadArrayStart(nodes, ref position);
            List<int> keys = new List<int>();
            List<IReadOnlyList<string>> values = new List<IReadOnlyList<string>>();
            HashSet<int> distinctKeys = new HashSet<int>();
            while (Peek(nodes, position).Entry != EndArrayEntry)
            {
                Require(nodes, ref position, StartNodeEntry, string.Empty, string.Empty);
                OdinMigrationSerializedNode key = Take(nodes, ref position, IntegerEntry, KeyName);
                if (
                    !int.TryParse(
                        key.Data,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int number
                    ) || !distinctKeys.Add(number)
                )
                {
                    throw new InvalidDataException(
                        "A dictionary key is outside the declared enum width or is duplicated."
                    );
                }
                RequireTypedNode(nodes, ref position, ValueName, listType, identities);
                long claimedValues = ReadArrayStart(nodes, ref position);
                List<string> references = new List<string>();
                while (Peek(nodes, position).Entry != EndArrayEntry)
                {
                    if (Peek(nodes, position).Entry == NullEntry)
                    {
                        Require(nodes, ref position, NullEntry, string.Empty, string.Empty);
                        references.Add("{fileID: 0}");
                        continue;
                    }
                    OdinMigrationSerializedNode reference = Take(
                        nodes,
                        ref position,
                        ExternalIndexEntry,
                        string.Empty
                    );
                    if (
                        !int.TryParse(
                            reference.Data,
                            NumberStyles.None,
                            CultureInfo.InvariantCulture,
                            out int index
                        )
                        || payload.ReferencedUnityObjects.Count <= index
                    )
                    {
                        throw new InvalidDataException(
                            "A dictionary value references an absent Unity object index."
                        );
                    }
                    if (objectReferences != null)
                    {
                        objectReferences.Validate(index, field);
                    }
                    references.Add(payload.ReferencedUnityObjects[index]);
                }
                Require(nodes, ref position, EndArrayEntry, string.Empty, string.Empty);
                if (claimedValues != references.Count)
                {
                    throw new InvalidDataException(
                        "A list length does not match its delivered values."
                    );
                }
                Require(nodes, ref position, EndNodeEntry, string.Empty, string.Empty);
                Require(nodes, ref position, EndNodeEntry, string.Empty, string.Empty);
                keys.Add(number);
                values.Add(references.AsReadOnly());
            }
            Require(nodes, ref position, EndArrayEntry, string.Empty, string.Empty);
            Require(nodes, ref position, EndNodeEntry, string.Empty, string.Empty);
            if (claimedPairs != keys.Count)
            {
                throw new InvalidDataException(
                    "The dictionary count or remaining records require further conversion."
                );
            }
            return Render(fieldName, keys, values);
        }

        private static bool IsIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            int length = name.Length;
            for (int index = 0; index < length; ++index)
            {
                char character = name[index];
                if (
                    character != '_'
                    && !(
                        ('A' <= character && character <= 'Z')
                        || ('a' <= character && character <= 'z')
                    )
                    && !(index != 0 && '0' <= character && character <= '9')
                )
                {
                    return false;
                }
            }
            return true;
        }

        private static OdinMigrationSerializedNode Peek(
            IReadOnlyList<OdinMigrationSerializedNode> nodes,
            int position
        )
        {
            if (nodes.Count <= position)
            {
                throw new InvalidDataException("The serialized dictionary is truncated.");
            }
            return nodes[position];
        }

        private static OdinMigrationSerializedNode Take(
            IReadOnlyList<OdinMigrationSerializedNode> nodes,
            ref int position,
            int entry,
            string name
        )
        {
            OdinMigrationSerializedNode node = Peek(nodes, position);
            if (node.Entry != entry || !string.Equals(node.Name, name, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A serialized dictionary record has an unexpected tag or name."
                );
            }
            ++position;
            return node;
        }

        private static void Require(
            IReadOnlyList<OdinMigrationSerializedNode> nodes,
            ref int position,
            int entry,
            string name,
            string data
        )
        {
            OdinMigrationSerializedNode node = Take(nodes, ref position, entry, name);
            if (!string.Equals(node.Data, data, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "A serialized dictionary boundary has unexpected data."
                );
            }
        }

        private static void RequireTypedNode(
            IReadOnlyList<OdinMigrationSerializedNode> nodes,
            ref int position,
            string name,
            string expectedShape,
            HashSet<int> identities
        )
        {
            OdinMigrationSerializedNode node = Take(nodes, ref position, StartNodeEntry, name);
            int separator = node.Data.IndexOf('|');
            if (
                separator <= 0
                || !int.TryParse(
                    node.Data.Substring(0, separator),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int identity
                )
                || !identities.Add(identity)
                || !string.Equals(
                    TypeShape(node.Data.Substring(separator + 1), 0),
                    expectedShape,
                    StringComparison.Ordinal
                )
            )
            {
                throw new InvalidDataException(
                    "A recorded type or reference identity does not match the conversion schema."
                );
            }
        }

        private static long ReadArrayStart(
            IReadOnlyList<OdinMigrationSerializedNode> nodes,
            ref int position
        )
        {
            OdinMigrationSerializedNode node = Take(
                nodes,
                ref position,
                StartArrayEntry,
                string.Empty
            );
            if (
                !long.TryParse(
                    node.Data,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long count
                )
            )
            {
                throw new InvalidDataException("An array length is invalid.");
            }
            return count;
        }

        private static void ReadComparer(
            IReadOnlyList<OdinMigrationSerializedNode> nodes,
            ref int position,
            string keyTypeName,
            HashSet<int> identities
        )
        {
            if (Peek(nodes, position).Entry == StartArrayEntry)
            {
                return;
            }
            if (Peek(nodes, position).Entry == NullEntry)
            {
                Require(nodes, ref position, NullEntry, ComparerName, string.Empty);
                return;
            }
            RequireTypedNode(
                nodes,
                ref position,
                ComparerName,
                "System.Collections.Generic.EnumEqualityComparer`1<" + keyTypeName + ">",
                identities
            );
            if (ReadArrayStart(nodes, ref position) != 0)
            {
                throw new InvalidDataException(
                    "A comparer with retained state requires a comparer-aware conversion."
                );
            }
            Require(nodes, ref position, EndArrayEntry, string.Empty, string.Empty);
            Require(nodes, ref position, EndNodeEntry, string.Empty, string.Empty);
        }

        private static string Render(
            string fieldName,
            IReadOnlyList<int> keys,
            IReadOnlyList<IReadOnlyList<string>> values
        )
        {
            StringBuilder result = new StringBuilder();
            result
                .Append("  ")
                .Append(fieldName)
                .Append(":\n    ")
                .Append(SerializableDictionarySerializedPropertyNames.Keys)
                .Append(": ");
            foreach (int key in keys)
            {
                uint bits = unchecked((uint)key);
                for (int shift = 0; shift < 32; shift += 8)
                {
                    result.Append(
                        ((bits >> shift) & 255).ToString("x2", CultureInfo.InvariantCulture)
                    );
                }
            }
            result
                .Append("\n    ")
                .Append(SerializableDictionarySerializedPropertyNames.BoxedValues)
                .Append(':');
            if (keys.Count == 0)
            {
                result.Append(" []\n");
                return result.ToString();
            }
            result.Append('\n');
            foreach (IReadOnlyList<string> references in values)
            {
                result
                    .Append("    - ")
                    .Append(SerializableDictionarySerializedPropertyNames.BoxedValueData)
                    .Append(':');
                if (references.Count == 0)
                {
                    result.Append(" []\n");
                    continue;
                }
                result.Append('\n');
                foreach (string reference in references)
                {
                    result.Append("      - ").Append(reference).Append('\n');
                }
            }
            return result.ToString();
        }

        private static string TypeShape(string text, int depth)
        {
            if (32 <= depth)
            {
                throw new InvalidDataException(
                    "Recorded type nesting exceeds the conversion limit."
                );
            }
            int length = text.Length;
            int position = 0;
            while (position < length && text[position] != ',' && text[position] != '[')
            {
                ++position;
            }
            string name = text.Substring(0, position).Trim();
            if (name.Length == 0)
            {
                throw new InvalidDataException("A recorded type name is empty.");
            }
            if (position == length || text[position] == ',')
            {
                return name;
            }
            if (length <= position + 1 || text[position + 1] != '[')
            {
                throw new InvalidDataException(
                    "A recorded generic type has an unsupported argument form."
                );
            }
            StringBuilder result = new StringBuilder(name);
            result.Append('<');
            ++position;
            int arguments = 0;
            while (position < length && text[position] == '[')
            {
                int start = ++position;
                int brackets = 1;
                while (position < length && brackets != 0)
                {
                    if (text[position] == '[')
                    {
                        ++brackets;
                    }
                    else if (text[position] == ']')
                    {
                        --brackets;
                    }
                    ++position;
                }
                if (brackets != 0)
                {
                    throw new InvalidDataException("A recorded generic type is truncated.");
                }
                if (arguments != 0)
                {
                    result.Append(',');
                }
                result.Append(TypeShape(text.Substring(start, position - start - 1), depth + 1));
                ++arguments;
                if (position < length && text[position] == ',')
                {
                    ++position;
                }
            }
            if (position == length || text[position] != ']' || arguments == 0)
            {
                throw new InvalidDataException(
                    "A recorded generic type has no complete argument list."
                );
            }
            ++position;
            if (position < length && text[position] != ',')
            {
                throw new InvalidDataException("A recorded type has unexpected trailing data.");
            }
            result.Append('>');
            return result.ToString();
        }

        private static void ValidatePrefabState(OdinMigrationSerializedData payload)
        {
            List<string> lines = new List<string> { "--- !u!114 &1", "MonoBehaviour:" };
            using (StringReader reader = new StringReader(payload.Source))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                }
            }
            IReadOnlyList<AuthoredAssetDocument> documents = AuthoredAssetYaml.ReadDocuments(lines);
            if (documents.Count == 1)
            {
                foreach (AuthoredAssetEntry entry in documents[0].Entries)
                {
                    if (entry.Indent == 4 && !PayloadFields.Contains(entry.Key))
                    {
                        throw new InvalidDataException(
                            "Unknown retained metadata requires a schema-aware conversion."
                        );
                    }
                }
            }
            if (
                documents.Count != 1
                || !documents[0].TryGetEntry(PrefabName, out AuthoredAssetEntry prefab)
                || !AuthoredAssetYaml.IsNullObjectReference(prefab.InlineValue)
                || HasAdditionalContent(lines, prefab)
            )
            {
                throw new InvalidDataException(
                    "A linked prefab requires coordinated prefab conversion."
                );
            }
            foreach (string name in new[] { PrefabModificationsName, PrefabReferencesName })
            {
                if (
                    !documents[0].TryGetEntry(name, out AuthoredAssetEntry modifications)
                    || !AuthoredAssetYaml.IsEmptySequence(modifications.InlineValue)
                    || HasAdditionalContent(lines, modifications)
                )
                {
                    throw new InvalidDataException(
                        "Prefab modifications require coordinated override conversion."
                    );
                }
            }
        }

        private static bool HasAdditionalContent(
            IReadOnlyList<string> lines,
            AuthoredAssetEntry entry
        )
        {
            for (int index = entry.LineNumber; index < entry.EndLineNumber - 1; ++index)
            {
                string line = lines[index].Trim();
                if (line.Length != 0 && !line.StartsWith("#", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
