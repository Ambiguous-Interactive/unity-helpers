// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using Object = UnityEngine.Object;

    /// <summary>
    /// One authored slot an attribute said must be filled, and is not.
    /// </summary>
    public readonly struct AuthoredRequirementFinding
    {
        /// <summary>Initializes a new instance of the <see cref="AuthoredRequirementFinding"/> struct.</summary>
        /// <param name="assetPath">The asset carrying the empty slot.</param>
        /// <param name="lineNumber">The one-based line the slot is written on.</param>
        /// <param name="declaringType">The type declaring the annotated field.</param>
        /// <param name="fieldName">The serialized key the field is written under.</param>
        public AuthoredRequirementFinding(
            string assetPath,
            int lineNumber,
            Type declaringType,
            string fieldName
        )
        {
            AssetPath = assetPath;
            LineNumber = lineNumber;
            DeclaringType = declaringType;
            FieldName = fieldName;
        }

        /// <summary>The asset carrying the empty slot.</summary>
        public string AssetPath { get; }

        /// <summary>The one-based line the slot is written on.</summary>
        public int LineNumber { get; }

        /// <summary>The type declaring the annotated field.</summary>
        public Type DeclaringType { get; }

        /// <summary>The serialized key the field is written under.</summary>
        public string FieldName { get; }

        /// <summary>Renders the finding as a location a reader can open.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"{AssetPath}:{LineNumber}: {DeclaringType?.Name}.{FieldName} is required and empty.";
        }
    }

    /// <summary>
    /// One annotated field the scan could not judge, and why.
    /// </summary>
    public readonly struct AuthoredRequirementExemption
    {
        /// <summary>Initializes a new instance of the <see cref="AuthoredRequirementExemption"/> struct.</summary>
        /// <param name="declaringType">The type declaring the annotated field.</param>
        /// <param name="fieldName">The field's name.</param>
        /// <param name="reason">Why the field could not be judged.</param>
        public AuthoredRequirementExemption(
            Type declaringType,
            string fieldName,
            AuthoredRequirementExemptionReason reason
        )
        {
            DeclaringType = declaringType;
            FieldName = fieldName;
            Reason = reason;
        }

        /// <summary>The type declaring the annotated field.</summary>
        public Type DeclaringType { get; }

        /// <summary>The field's name.</summary>
        public string FieldName { get; }

        /// <summary>Why the field could not be judged.</summary>
        public AuthoredRequirementExemptionReason Reason { get; }

        /// <summary>Renders the exemption as one budget line.</summary>
        /// <returns>A human-readable description.</returns>
        public override string ToString()
        {
            return $"{DeclaringType?.FullName}.{FieldName}: {Reason}";
        }
    }

    /// <summary>
    /// Enforces "the author must fill this" annotations against committed assets, so a build cannot
    /// ship the slot empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WNotNullAttribute"/> is a <c>PropertyAttribute</c>: it draws a warning beside the
    /// field and does nothing else, and a drawer needs somebody looking at the inspector. So the
    /// package tells an author "this must be assigned" and a build ships with the slot empty --
    /// an animation that never plays, a sprite that never draws, or a
    /// <c>NullReferenceException</c> from a callback that never checked.
    /// </para>
    /// <para>
    /// The subject set is derived from the annotations through <c>TypeCache</c>, never from a
    /// hand-listed set of fields: a list has to be updated by the change that adds a field, and
    /// forgetting makes the gate pass, which is the property that makes the gate worth having.
    /// </para>
    /// <para>
    /// "Unfilled" is the drawer's own answer rather than a second one. A reference is empty when it
    /// names no object, and a string when it is blank -- exactly what
    /// <c>ValidationShared.IsPropertyNull</c> reports for the same property. A field whose value has
    /// no text form that test applies to is exempted and printed, not guessed at.
    /// </para>
    /// <para>
    /// A key the document does not carry at all is <b>not</b> reported. That is a different state --
    /// the asset predates the field -- and reporting it would report every asset of a type the
    /// moment a field is added to it, which is noise rather than signal. The reported set is
    /// therefore the slots an author saw and left empty.
    /// </para>
    /// </remarks>
    public static class AuthoredRequirementValidator
    {
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
            documentsInspected = 0;
            if (
                requirementAttributeType == null
                || assetPaths == null
                || findings == null
                || exemptions == null
            )
            {
                return false;
            }

            findings.Clear();
            exemptions.Clear();

            Dictionary<string, List<RequiredField>> byScriptGuid = RequiredFieldsByScriptGuid(
                requirementAttributeType,
                exemptions
            );

            if (byScriptGuid.Count <= 0)
            {
                return true;
            }

            for (int index = 0; index < assetPaths.Count; ++index)
            {
                string assetPath = assetPaths[index];
                if (
                    !AuthoredAssetYaml.TryReadDocuments(
                        assetPath,
                        out IReadOnlyList<string> lines,
                        out IReadOnlyList<AuthoredAssetDocument> documents
                    )
                )
                {
                    continue;
                }

                for (int document = 0; document < documents.Count; ++document)
                {
                    AuthoredAssetDocument candidate = documents[document];
                    if (
                        candidate.IsStripped
                        || string.IsNullOrEmpty(candidate.ScriptGuid)
                        || !byScriptGuid.TryGetValue(
                            candidate.ScriptGuid,
                            out List<RequiredField> required
                        )
                    )
                    {
                        continue;
                    }

                    ++documentsInspected;
                    Judge(assetPath, lines, candidate, required, findings);
                }
            }

            return true;
        }

        private static void Judge(
            string assetPath,
            IReadOnlyList<string> lines,
            AuthoredAssetDocument document,
            List<RequiredField> required,
            List<AuthoredRequirementFinding> findings
        )
        {
            for (int index = 0; index < required.Count; ++index)
            {
                RequiredField field = required[index];
                if (!TryFindEntry(document, field, out AuthoredAssetEntry entry))
                {
                    continue;
                }

                if (field.IsCollection)
                {
                    JudgeElements(assetPath, lines, field, entry, findings);
                    continue;
                }

                if (!IsEmptyValue(field, entry.InlineValue))
                {
                    continue;
                }

                findings.Add(
                    new AuthoredRequirementFinding(
                        assetPath,
                        entry.LineNumber,
                        field.DeclaringType,
                        field.Name
                    )
                );
            }
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
            RequiredField field,
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
                        field.Name
                    )
                );
            }
        }

        private static bool TryFindEntry(
            AuthoredAssetDocument document,
            RequiredField field,
            out AuthoredAssetEntry entry
        )
        {
            if (document.TryGetEntry(field.Name, out entry))
            {
                return true;
            }

            for (int index = 0; index < field.Aliases.Count; ++index)
            {
                if (document.TryGetEntry(field.Aliases[index], out entry))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsEmptyValue(RequiredField field, string value)
        {
            if (field.IsObjectReference)
            {
                return AuthoredAssetYaml.IsNullObjectReference(value);
            }

            return string.IsNullOrEmpty(value);
        }

        private static Dictionary<string, List<RequiredField>> RequiredFieldsByScriptGuid(
            Type requirementAttributeType,
            List<AuthoredRequirementExemption> exemptions
        )
        {
            Dictionary<string, List<RequiredField>> byScriptGuid = new(StringComparer.Ordinal);
            foreach (FieldInfo field in TypeCache.GetFieldsWithAttribute(requirementAttributeType))
            {
                Type declaringType = field.DeclaringType;
                if (declaringType == null)
                {
                    continue;
                }

                if (!TryClassify(field, out RequiredField required))
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

                if (!MonoScriptIndex.TryGetScriptGuid(declaringType, out string guid))
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

                if (!byScriptGuid.TryGetValue(guid, out List<RequiredField> fields))
                {
                    fields = new List<RequiredField>();
                    byScriptGuid[guid] = fields;
                }

                fields.Add(required);
            }

            return byScriptGuid;
        }

        private static bool TryClassify(FieldInfo field, out RequiredField required)
        {
            required = default;
            if (field.IsDefined(typeof(SerializeReference), inherit: true))
            {
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

            if (fieldType == null)
            {
                return false;
            }

            bool isObjectReference = typeof(Object).IsAssignableFrom(fieldType);
            if (!isObjectReference && fieldType != typeof(string))
            {
                return false;
            }

            required = new RequiredField(
                field.DeclaringType,
                field.Name,
                isObjectReference,
                isCollection,
                AliasesOf(field)
            );
            return true;
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
            for (int index = 0; index < attributes.Length; ++index)
            {
                if (attributes[index] is FormerlySerializedAsAttribute alias)
                {
                    aliases.Add(alias.oldName);
                }
            }

            return aliases;
        }

        private readonly struct RequiredField
        {
            public RequiredField(
                Type declaringType,
                string name,
                bool isObjectReference,
                bool isCollection,
                IReadOnlyList<string> aliases
            )
            {
                DeclaringType = declaringType;
                Name = name;
                IsObjectReference = isObjectReference;
                IsCollection = isCollection;
                Aliases = aliases;
            }

            public Type DeclaringType { get; }

            public string Name { get; }

            public bool IsObjectReference { get; }

            public bool IsCollection { get; }

            public IReadOnlyList<string> Aliases { get; }
        }
    }
#endif
}
