// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
    using WallstopStudios.UnityHelpers.Core.Extension;
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Enforces "the author must fill this" annotations against committed assets, so a build cannot
    /// ship the slot empty.
    /// </summary>
    /// <remarks>
    /// The subject set comes from the annotations through <c>TypeCache</c>, never a hand-listed set
    /// of fields, and "unfilled" is the drawer's own answer rather than a second one. A field the
    /// scan cannot read is a printed exemption, not a silent skip. See
    /// <see href="https://github.com/Ambiguous-Interactive/unity-helpers/blob/main/docs/features/editor-tools/authored-asset-validation.md">Authored Asset Validation</see>.
    /// </remarks>
    public static partial class AuthoredRequirementValidator
    {
        private const int MaximumDefaultValueDepth = 64;

        /// <summary>
        /// Reports every empty slot a <see cref="WNotNullAttribute"/> requires to be filled.
        /// </summary>
        /// <param name="assetPaths">The committed assets to read.</param>
        /// <param name="findings">Receives one entry per empty slot.</param>
        /// <param name="exemptions">Receives the annotated fields the scan could not judge.</param>
        /// <param name="documentsInspected">Receives how many documents named an annotated type.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        public static bool TryScan(
            IReadOnlyList<string> assetPaths,
            List<AuthoredRequirementFinding> findings,
            List<AuthoredRequirementExemption> exemptions,
            out int documentsInspected
        )
        {
            return TryScan(
                typeof(WNotNullAttribute),
                assetPaths,
                findings,
                exemptions,
                out documentsInspected
            );
        }

        /// <summary>
        /// Reports every empty slot a <see cref="WNotNullAttribute"/> requires to be filled, and
        /// every asset the scan could not read.
        /// </summary>
        /// <param name="assetPaths">The committed assets to read.</param>
        /// <param name="findings">Receives one entry per empty slot.</param>
        /// <param name="exemptions">Receives the annotated fields the scan could not judge.</param>
        /// <param name="unreadable">Receives the asset paths the scan could not open, sorted.</param>
        /// <param name="documentsInspected">Receives how many documents named an annotated type.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        public static bool TryScan(
            IReadOnlyList<string> assetPaths,
            List<AuthoredRequirementFinding> findings,
            List<AuthoredRequirementExemption> exemptions,
            List<string> unreadable,
            out int documentsInspected
        )
        {
            return TryScan(
                typeof(WNotNullAttribute),
                assetPaths,
                findings,
                exemptions,
                unreadable,
                out documentsInspected
            );
        }

        /// <summary>
        /// Reports every empty slot <paramref name="requirementAttributeType"/> requires to be filled.
        /// </summary>
        /// <param name="requirementAttributeType">The field attribute that means "the author must fill this".</param>
        /// <param name="assetPaths">The committed assets to read.</param>
        /// <param name="findings">Receives one entry per empty slot.</param>
        /// <param name="exemptions">Receives the annotated fields the scan could not judge.</param>
        /// <param name="documentsInspected">Receives how many documents named an annotated type.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        /// <remarks>
        /// The attribute is a parameter because the failure is structural rather than specific to
        /// one annotation: a consuming project's own "must be assigned" attribute has the same hole.
        /// </remarks>
        public static bool TryScan(
            Type requirementAttributeType,
            IReadOnlyList<string> assetPaths,
            List<AuthoredRequirementFinding> findings,
            List<AuthoredRequirementExemption> exemptions,
            out int documentsInspected
        )
        {
            using PooledResource<List<string>> unreadableLease = Buffers<string>.List.Get(
                out List<string> unreadable
            );
            return TryScan(
                requirementAttributeType,
                assetPaths,
                findings,
                exemptions,
                unreadable,
                out documentsInspected
            );
        }

        /// <summary>
        /// Reports every empty slot <paramref name="requirementAttributeType"/> requires to be
        /// filled, and every asset the scan could not read.
        /// </summary>
        /// <param name="requirementAttributeType">The field attribute that means "the author must fill this".</param>
        /// <param name="assetPaths">The committed assets to read.</param>
        /// <param name="findings">Receives one entry per empty slot.</param>
        /// <param name="exemptions">Receives the annotated fields the scan could not judge.</param>
        /// <param name="unreadable">Receives the asset paths the scan could not open, sorted.</param>
        /// <param name="documentsInspected">Receives how many documents named an annotated type.</param>
        /// <returns><c>false</c> when the scan could not run at all.</returns>
        /// <remarks>See <see cref="UnreadableAssetPaths"/> for why an unreadable asset is never a finding.</remarks>
        public static bool TryScan(
            Type requirementAttributeType,
            IReadOnlyList<string> assetPaths,
            List<AuthoredRequirementFinding> findings,
            List<AuthoredRequirementExemption> exemptions,
            List<string> unreadable,
            out int documentsInspected
        )
        {
            return TryScan(
                requirementAttributeType,
                AuthoredRequirementKind.Assigned,
                assetPaths,
                findings,
                exemptions,
                unreadable,
                out documentsInspected
            );
        }

        /// <summary>Reports fields that violate the selected requirement kind.</summary>
        /// <param name="requirementAttributeType">The attribute identifying required fields.</param>
        /// <param name="kind">The condition applied to those fields.</param>
        /// <param name="assetPaths">The authored assets to scan.</param>
        /// <param name="findings">Receives requirement violations.</param>
        /// <param name="exemptions">Receives fields whose representation cannot be judged.</param>
        /// <param name="documentsInspected">Receives the number of matching documents.</param>
        /// <returns>Whether the scan could run.</returns>
        public static bool TryScan(
            Type requirementAttributeType,
            AuthoredRequirementKind kind,
            IReadOnlyList<string> assetPaths,
            List<AuthoredRequirementFinding> findings,
            List<AuthoredRequirementExemption> exemptions,
            out int documentsInspected
        )
        {
            using PooledResource<List<string>> unreadableLease = Buffers<string>.List.Get(
                out List<string> unreadable
            );
            return TryScan(
                requirementAttributeType,
                kind,
                assetPaths,
                findings,
                exemptions,
                unreadable,
                out documentsInspected
            );
        }

        /// <summary>Reports requirement violations and unreadable assets.</summary>
        /// <param name="requirementAttributeType">The attribute identifying required fields.</param>
        /// <param name="kind">The condition applied to those fields.</param>
        /// <param name="assetPaths">The authored assets to scan.</param>
        /// <param name="findings">Receives requirement violations.</param>
        /// <param name="exemptions">Receives fields whose representation cannot be judged.</param>
        /// <param name="unreadable">Receives asset paths that could not be read.</param>
        /// <param name="documentsInspected">Receives the number of matching documents.</param>
        /// <returns>Whether the scan could run.</returns>
        public static bool TryScan(
            Type requirementAttributeType,
            AuthoredRequirementKind kind,
            IReadOnlyList<string> assetPaths,
            List<AuthoredRequirementFinding> findings,
            List<AuthoredRequirementExemption> exemptions,
            List<string> unreadable,
            out int documentsInspected
        )
        {
            if (
                requirementAttributeType == null
                || !typeof(Attribute).IsAssignableFrom(requirementAttributeType)
                || (
                    kind != AuthoredRequirementKind.Assigned
                    && kind != AuthoredRequirementKind.NotEmpty
                    && kind != AuthoredRequirementKind.NotDefault
                )
                || assetPaths == null
                || findings == null
                || exemptions == null
                || unreadable == null
            )
            {
                documentsInspected = 0;
                return false;
            }

            findings.Clear();
            exemptions.Clear();
            unreadable.Clear();

            Dictionary<string, List<AuthoredRequirementField>> byScriptGuid = FieldsByScriptGuid(
                requirementAttributeType,
                exemptions,
                kind
            );

            if (byScriptGuid.Count <= 0)
            {
                documentsInspected = 0;
                return true;
            }

            int inspected = 0;
            for (int index = 0; index < assetPaths.Count; ++index)
            {
                string assetPath = assetPaths[index];
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                if (
                    !AuthoredAssetYaml.TryReadDocuments(
                        AuthoredAssetPaths.ToFileSystemPath(assetPath),
                        out IReadOnlyList<string> lines,
                        out IReadOnlyList<AuthoredAssetDocument> documents
                    )
                )
                {
                    unreadable.Add(assetPath);
                    continue;
                }

                inspected += JudgeDocuments(assetPath, lines, documents, byScriptGuid, findings);
            }

            UnreadableAssetPaths.SortAndDeduplicate(unreadable);
            documentsInspected = inspected;
            return true;
        }

        /// <summary>
        /// Judges one asset's already-parsed documents against an already-built index.
        /// </summary>
        /// <param name="assetPath">The asset the documents came from.</param>
        /// <param name="lines">The file's lines, so a bare sequence element can be read.</param>
        /// <param name="documents">The documents parsed from that asset.</param>
        /// <param name="byScriptGuid">The annotated fields, keyed by carrying script guid.</param>
        /// <param name="findings">Receives one entry per empty slot.</param>
        /// <returns>How many documents named an annotated type.</returns>
        /// <remarks>
        /// Separated out so a continuous rule can judge one asset at a time against an index it
        /// built once. The index is the expensive half -- a <c>TypeCache</c> sweep plus a script
        /// lookup per carrying type -- so rebuilding it per asset would make the per-asset cost the
        /// index rebuild.
        /// </remarks>
        internal static int JudgeDocuments(
            string assetPath,
            IReadOnlyList<string> lines,
            IReadOnlyList<AuthoredAssetDocument> documents,
            IReadOnlyDictionary<string, List<AuthoredRequirementField>> byScriptGuid,
            List<AuthoredRequirementFinding> findings
        )
        {
            int inspected = 0;
            if (documents == null || byScriptGuid == null || findings == null)
            {
                return inspected;
            }

            for (int document = 0; document < documents.Count; ++document)
            {
                AuthoredAssetDocument candidate = documents[document];
                if (
                    candidate.IsStripped
                    || string.IsNullOrWhiteSpace(candidate.ScriptGuid)
                    || !byScriptGuid.TryGetValue(
                        candidate.ScriptGuid,
                        out List<AuthoredRequirementField> required
                    )
                )
                {
                    continue;
                }

                ++inspected;
                JudgeWithNestedValues(assetPath, lines, candidate, required, findings);
            }

            return inspected;
        }

        internal static bool IsEmptyValue(AuthoredRequirementField field, string value)
        {
            if (field.IsObjectReference)
            {
                return AuthoredAssetYaml.IsNullObjectReference(value);
            }

            if (field.Kind == AuthoredRequirementKind.NotDefault && field.IsNumeric)
            {
                return string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                    || double.TryParse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double number
                    )
                        && number == 0d;
            }

            return string.IsNullOrWhiteSpace(value)
                || string.Equals(value, "\"\"", StringComparison.Ordinal)
                || string.Equals(value, "''", StringComparison.Ordinal);
        }

        /// <summary>
        /// Maps every annotated field onto the script guid of every type that carries it.
        /// </summary>
        /// <param name="requirementAttributeType">The attribute that means "the author must fill this".</param>
        /// <param name="exemptions">Receives the annotated fields that cannot be judged from text.</param>
        /// <returns>The annotated fields, keyed by the guid of every script that carries them.</returns>
        internal static Dictionary<string, List<AuthoredRequirementField>> FieldsByScriptGuid(
            Type requirementAttributeType,
            List<AuthoredRequirementExemption> exemptions,
            AuthoredRequirementKind kind = AuthoredRequirementKind.Assigned
        )
        {
            Dictionary<string, List<AuthoredRequirementField>> byScriptGuid = new(
                StringComparer.Ordinal
            );
            using PooledResource<List<string>> guidsLease = Buffers<string>.List.Get(
                out List<string> guids
            );
            using PooledResource<Dictionary<Type, PooledResource<List<FieldInfo>>>> nestedLease =
                DictionaryBuffer<Type, PooledResource<List<FieldInfo>>>.Dictionary.Get(
                    out Dictionary<Type, PooledResource<List<FieldInfo>>> nested
                );
            try
            {
                foreach (
                    FieldInfo field in TypeCache.GetFieldsWithAttribute(requirementAttributeType)
                )
                {
                    Type declaringType = field.DeclaringType;
                    if (declaringType == null)
                    {
                        continue;
                    }

                    if (
                        !typeof(Object).IsAssignableFrom(declaringType)
                        && declaringType.ContainsGenericParameters
                    )
                    {
                        AddNestedDeclaration(nested, field);
                        continue;
                    }

                    if (!TryClassify(field, out AuthoredRequirementField required, kind))
                    {
                        exemptions.Add(
                            new AuthoredRequirementExemption(
                                declaringType,
                                field.Name,
                                AuthoredRequirementExemptionReason.ValueNotReadableAsText
                            )
                        );
                        continue;
                    }

                    if (!typeof(Object).IsAssignableFrom(declaringType))
                    {
                        AddNestedDeclaration(nested, field);
                        continue;
                    }

                    CollectCarrierGuids(declaringType, guids);
                    if (guids.Count <= 0)
                    {
                        exemptions.Add(
                            new AuthoredRequirementExemption(
                                declaringType,
                                field.Name,
                                AuthoredRequirementExemptionReason.NoBoundScript
                            )
                        );
                        continue;
                    }

                    foreach (string guid in guids)
                    {
                        if (
                            !byScriptGuid.TryGetValue(
                                guid,
                                out List<AuthoredRequirementField> fields
                            )
                        )
                        {
                            fields = new List<AuthoredRequirementField>();
                            byScriptGuid[guid] = fields;
                        }

                        fields.Add(required);
                    }
                }

                AddNestedCarriers(nested, byScriptGuid, exemptions, kind);
                return byScriptGuid;
            }
            finally
            {
                foreach (PooledResource<List<FieldInfo>> lease in nested.Values)
                {
                    lease.Dispose();
                }
            }
        }

        internal static bool TryClassify(
            FieldInfo field,
            out AuthoredRequirementField required,
            AuthoredRequirementKind kind = AuthoredRequirementKind.Assigned
        )
        {
            if (
                field == null
                || field.IsStatic
                || field.IsInitOnly
                || field.IsLiteral
                || field.IsDefined(typeof(NonSerializedAttribute), inherit: false)
            )
            {
                required = default;
                return false;
            }

            bool isManagedReference = field.IsDefined(typeof(SerializeReference), inherit: true);
            if (
                !field.IsPublic
                && !field.IsDefined(typeof(SerializeField), inherit: false)
                && !isManagedReference
            )
            {
                required = default;
                return false;
            }
            Type fieldType = field.FieldType;
            bool isCollection = false;
            if (fieldType.IsArray)
            {
                isCollection = true;
                fieldType = fieldType.GetElementType();
            }
            else if (
                fieldType.IsGenericType
                && fieldType.GetGenericTypeDefinition() == typeof(List<>)
            )
            {
                isCollection = true;
                fieldType = fieldType.GetGenericArguments()[0];
            }

            if (
                fieldType == null
                || isManagedReference && (kind != AuthoredRequirementKind.NotEmpty || !isCollection)
            )
            {
                required = default;
                return false;
            }

            bool isObjectReference = typeof(Object).IsAssignableFrom(fieldType);
            bool isNumeric =
                fieldType.IsEnum
                || fieldType.IsPrimitive
                    && fieldType != typeof(IntPtr)
                    && fieldType != typeof(UIntPtr);
            bool isComposite = fieldType.IsValueType && !isNumeric;
            bool supported =
                kind == AuthoredRequirementKind.NotEmpty
                    ? isCollection || fieldType == typeof(string)
                : kind == AuthoredRequirementKind.NotDefault
                    ? !isCollection
                        && (
                            isNumeric
                            || isComposite
                            || isObjectReference
                            || fieldType == typeof(string)
                        )
                : kind == AuthoredRequirementKind.Assigned
                    && (isObjectReference || fieldType == typeof(string));
            if (!supported)
            {
                required = default;
                return false;
            }

            required = new AuthoredRequirementField(
                field.DeclaringType,
                field.Name,
                isObjectReference,
                isCollection,
                AliasesOf(field),
                kind,
                isNumeric,
                isComposite
            );
            return true;
        }

        private static void Judge(
            string assetPath,
            IReadOnlyList<string> lines,
            AuthoredAssetDocument document,
            List<AuthoredRequirementField> required,
            List<AuthoredRequirementFinding> findings
        )
        {
            foreach (AuthoredRequirementField field in required)
            {
                if (!TryFindEntry(document, field, out AuthoredAssetEntry entry))
                {
                    continue;
                }

                if (field.IsCollection)
                {
                    if (field.Kind == AuthoredRequirementKind.NotEmpty)
                    {
                        bool hasElement =
                            !string.IsNullOrEmpty(entry.InlineValue)
                            && !string.Equals(entry.InlineValue, "[]", StringComparison.Ordinal);
                        foreach (
                            AuthoredSequenceElement element in AuthoredAssetYaml.EnumerateSequenceElements(
                                lines,
                                entry
                            )
                        )
                        {
                            hasElement = true;
                            break;
                        }
                        if (!hasElement)
                        {
                            findings.Add(
                                new AuthoredRequirementFinding(
                                    assetPath,
                                    entry.LineNumber,
                                    field.DeclaringType,
                                    field.Name,
                                    field.Kind
                                )
                            );
                        }
                        continue;
                    }
                    JudgeElements(assetPath, lines, field, entry, findings);
                    continue;
                }

                if (
                    field.Kind == AuthoredRequirementKind.NotDefault && field.IsComposite
                        ? !IsDefaultComposite(lines, document, entry)
                        : !IsEmptyValue(field, entry.InlineValue)
                )
                {
                    continue;
                }

                findings.Add(
                    new AuthoredRequirementFinding(
                        assetPath,
                        entry.LineNumber,
                        field.DeclaringType,
                        field.Name,
                        field.Kind
                    )
                );
            }
        }

        private static bool IsDefaultComposite(
            IReadOnlyList<string> lines,
            AuthoredAssetDocument document,
            AuthoredAssetEntry entry
        )
        {
            if (!string.IsNullOrEmpty(entry.InlineValue))
            {
                return IsDefaultFlowValue(entry.InlineValue.AsSpan(), 0);
            }
            bool sawValue = false;
            IReadOnlyList<AuthoredAssetEntry> entries = document.Entries;
            for (int index = 0; index < entries.Count; ++index)
            {
                AuthoredAssetEntry child = entries[index];
                if (
                    child.LineNumber <= entry.LineNumber
                    || entry.EndLineNumber <= child.LineNumber
                    || string.Equals(child.Key, "serializedVersion", StringComparison.Ordinal)
                )
                {
                    continue;
                }
                if (string.IsNullOrEmpty(child.InlineValue))
                {
                    foreach (
                        AuthoredSequenceElement element in AuthoredAssetYaml.EnumerateSequenceElements(
                            lines,
                            child
                        )
                    )
                    {
                        return false;
                    }
                    if (index + 1 < entries.Count && child.Indent < entries[index + 1].Indent)
                    {
                        continue;
                    }
                }
                sawValue = true;
                if (!IsDefaultFlowValue(child.InlineValue.AsSpan(), 0))
                {
                    return false;
                }
            }
            return sawValue;
        }

        private static bool IsDefaultFlowValue(ReadOnlySpan<char> value, int depth)
        {
            if (MaximumDefaultValueDepth <= depth)
            {
                return false;
            }
            value = value.Trim();
            if (
                value.IsEmpty
                || value.SequenceEqual("\"\"".AsSpan())
                || value.SequenceEqual("''".AsSpan())
                || value.SequenceEqual("[]".AsSpan())
                || value.Equals("false".AsSpan(), StringComparison.OrdinalIgnoreCase)
            )
            {
                return true;
            }
            if (value[0] != '{' || value[value.Length - 1] != '}')
            {
                return double.TryParse(
                        value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double number
                    )
                    && number == 0d;
            }
            value = value.Slice(1, value.Length - 2);
            bool sawValue = false;
            while (!value.IsEmpty)
            {
                int colon = value.IndexOf(':');
                if (colon < 0)
                {
                    return false;
                }
                ReadOnlySpan<char> key = value.Slice(0, colon).Trim();
                value = value.Slice(colon + 1);
                int end = 0;
                int nesting = 0;
                int valueLength = value.Length;
                for (; end < valueLength; ++end)
                {
                    char character = value[end];
                    if (character == '{' || character == '[')
                    {
                        ++nesting;
                    }
                    else if (character == '}' || character == ']')
                    {
                        --nesting;
                    }
                    else if (character == ',' && nesting == 0)
                    {
                        break;
                    }
                }
                if (!key.SequenceEqual("serializedVersion".AsSpan()))
                {
                    sawValue = true;
                    if (!IsDefaultFlowValue(value.Slice(0, end), depth + 1))
                    {
                        return false;
                    }
                }
                value = end < value.Length ? value.Slice(end + 1).Trim() : ReadOnlySpan<char>.Empty;
            }
            return sawValue;
        }

        /// <summary>
        /// Judges each element of an annotated collection, the way the drawer judges each element.
        /// </summary>
        /// <param name="assetPath">The asset carrying the collection.</param>
        /// <param name="lines">The file's lines, so a bare element can be read.</param>
        /// <param name="field">The annotated field.</param>
        /// <param name="entry">The key the collection is written under.</param>
        /// <param name="findings">Receives one entry per empty element.</param>
        /// <remarks>
        /// The elements are read from the lines rather than from the document's entries, because
        /// <c>- {fileID: 0}</c> declares no key and so is no entry. An empty collection is not
        /// itself a finding: the drawer reports a null element, not an unpopulated array.
        /// </remarks>
        private static void JudgeElements(
            string assetPath,
            IReadOnlyList<string> lines,
            AuthoredRequirementField field,
            AuthoredAssetEntry entry,
            List<AuthoredRequirementFinding> findings
        )
        {
            foreach (
                AuthoredSequenceElement element in AuthoredAssetYaml.EnumerateSequenceElements(
                    lines,
                    entry
                )
            )
            {
                if (!IsEmptyValue(field, element.Value))
                {
                    continue;
                }

                findings.Add(
                    new AuthoredRequirementFinding(
                        assetPath,
                        element.LineNumber,
                        field.DeclaringType,
                        field.Name,
                        field.Kind
                    )
                );
            }
        }

        private static bool TryFindEntry(
            AuthoredAssetDocument document,
            AuthoredRequirementField field,
            out AuthoredAssetEntry entry
        )
        {
            int fieldIndent = int.MaxValue;
            foreach (AuthoredAssetEntry candidate in document.Entries.AsValueEnumerableRequired())
            {
                fieldIndent = Math.Min(fieldIndent, candidate.Indent);
            }
            if (
                document.TryGetEntry(field.Name, out AuthoredAssetEntry named)
                && named.Indent == fieldIndent
            )
            {
                entry = named;
                return true;
            }
            for (int index = 0; index < field.Aliases.Count; ++index)
            {
                if (
                    document.TryGetEntry(field.Aliases[index], out AuthoredAssetEntry alias)
                    && alias.Indent == fieldIndent
                )
                {
                    entry = alias;
                    return true;
                }
            }
            entry = default;
            return false;
        }

        private static void CollectCarrierGuids(Type declaringType, List<string> guids)
        {
            guids.Clear();
            if (MonoScriptIndex.TryGetScriptGuid(declaringType, out string declaringGuid))
            {
                guids.Add(declaringGuid);
            }

            foreach (Type derived in TypeCache.GetTypesDerivedFrom(declaringType))
            {
                if (
                    derived.IsAbstract
                    || !MonoScriptIndex.TryGetScriptGuid(derived, out string derivedGuid)
                    || guids.Contains(derivedGuid)
                )
                {
                    continue;
                }

                guids.Add(derivedGuid);
            }
        }

        private static IReadOnlyList<string> AliasesOf(FieldInfo field)
        {
            object[] attributes = field.GetCustomAttributes(
                typeof(FormerlySerializedAsAttribute),
                inherit: true
            );

            if (attributes == null || attributes.Length <= 0)
            {
                return Array.Empty<string>();
            }

            List<string> aliases = new(attributes.Length);
            foreach (object attribute in attributes)
            {
                if (attribute is FormerlySerializedAsAttribute alias)
                {
                    aliases.Add(alias.oldName);
                }
            }

            return aliases;
        }
    }
#endif
}
