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
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using Object = UnityEngine.Object;

    public static partial class AuthoredRequirementValidator
    {
        private static void AddNestedDeclaration(
            Dictionary<Type, List<FieldInfo>> nested,
            FieldInfo field
        )
        {
            if (!nested.TryGetValue(field.DeclaringType, out List<FieldInfo> fields))
            {
                fields = new List<FieldInfo>();
                nested.Add(field.DeclaringType, fields);
            }
            fields.Add(field);
        }

        private static bool TryBindNestedRequirement(
            Type type,
            FieldInfo declaration,
            AuthoredRequirementKind kind,
            out AuthoredRequirementField required
        )
        {
            if (declaration.DeclaringType == type)
            {
                return TryClassify(declaration, out required, kind);
            }
            foreach (FieldInfo field in ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(type))
            {
                if (
                    field.DeclaringType == type
                    && string.Equals(field.Name, declaration.Name, StringComparison.Ordinal)
                )
                {
                    return TryClassify(field, out required, kind);
                }
            }
            required = default;
            return false;
        }

        private static void AddNestedCarriers(
            Dictionary<Type, List<FieldInfo>> nested,
            Dictionary<string, List<AuthoredRequirementField>> byScriptGuid,
            List<AuthoredRequirementExemption> exemptions,
            AuthoredRequirementKind kind
        )
        {
            if (nested.Count == 0)
            {
                return;
            }
            HashSet<Type> bound = new();
            HashSet<Type> visited = new();
            List<AuthoredRequirementField> reachable = new();
            List<Type> carriers = new();
            foreach (Type type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
            {
                carriers.Add(type);
            }
            foreach (Type type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                carriers.Add(type);
            }
            foreach (Type carrier in carriers)
            {
                if (carrier.IsAbstract || carrier.ContainsGenericParameters)
                {
                    continue;
                }
                visited.Clear();
                reachable.Clear();
                CollectReachableRequirements(
                    carrier,
                    nested,
                    visited,
                    reachable,
                    exemptions,
                    kind,
                    0
                );
                if (
                    reachable.Count == 0
                    || !MonoScriptIndex.TryGetScriptGuid(carrier, out string guid)
                )
                {
                    continue;
                }
                if (!byScriptGuid.TryGetValue(guid, out List<AuthoredRequirementField> fields))
                {
                    fields = new List<AuthoredRequirementField>();
                    byScriptGuid.Add(guid, fields);
                }
                foreach (AuthoredRequirementField field in reachable)
                {
                    fields.Add(field);
                    bound.Add(
                        field.DeclaringType.IsGenericType
                            ? field.DeclaringType.GetGenericTypeDefinition()
                            : field.DeclaringType
                    );
                }
            }
            foreach (KeyValuePair<Type, List<FieldInfo>> pair in nested)
            {
                if (bound.Contains(pair.Key))
                {
                    continue;
                }
                foreach (FieldInfo field in pair.Value)
                {
                    exemptions.Add(
                        new AuthoredRequirementExemption(
                            field.DeclaringType,
                            field.Name,
                            AuthoredRequirementExemptionReason.NoBoundScript
                        )
                    );
                }
            }
        }

        private static void CollectReachableRequirements(
            Type type,
            Dictionary<Type, List<FieldInfo>> nested,
            HashSet<Type> visited,
            List<AuthoredRequirementField> reachable,
            List<AuthoredRequirementExemption> exemptions,
            AuthoredRequirementKind kind,
            int depth
        )
        {
            if (MaximumDefaultValueDepth <= depth || !visited.Add(type))
            {
                return;
            }
            for (Type current = type; current != null; current = current.BaseType)
            {
                Type declarationType = current.IsGenericType
                    ? current.GetGenericTypeDefinition()
                    : current;
                if (nested.TryGetValue(declarationType, out List<FieldInfo> fields))
                {
                    foreach (FieldInfo declaration in fields)
                    {
                        if (
                            !TryBindNestedRequirement(
                                current,
                                declaration,
                                kind,
                                out AuthoredRequirementField field
                            )
                        )
                        {
                            bool alreadyExempt = false;
                            foreach (AuthoredRequirementExemption exemption in exemptions)
                            {
                                if (
                                    exemption.DeclaringType == current
                                    && string.Equals(
                                        exemption.FieldName,
                                        declaration.Name,
                                        StringComparison.Ordinal
                                    )
                                )
                                {
                                    alreadyExempt = true;
                                    break;
                                }
                            }
                            if (!alreadyExempt)
                            {
                                exemptions.Add(
                                    new AuthoredRequirementExemption(
                                        current,
                                        declaration.Name,
                                        AuthoredRequirementExemptionReason.ValueNotReadableAsText
                                    )
                                );
                            }
                            continue;
                        }
                        bool present = false;
                        foreach (AuthoredRequirementField existing in reachable)
                        {
                            if (
                                existing.DeclaringType == field.DeclaringType
                                && string.Equals(
                                    existing.Name,
                                    field.Name,
                                    StringComparison.Ordinal
                                )
                            )
                            {
                                present = true;
                                break;
                            }
                        }
                        if (!present)
                        {
                            reachable.Add(field);
                        }
                    }
                }
            }
            foreach (FieldInfo field in ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(type))
            {
                Type valueType = SerializedNestedType(field, out bool _);
                if (valueType != null)
                {
                    CollectReachableRequirements(
                        valueType,
                        nested,
                        visited,
                        reachable,
                        exemptions,
                        kind,
                        depth + 1
                    );
                }
            }
        }

        private static Type SerializedNestedType(FieldInfo field, out bool collection)
        {
            collection = false;
            if (
                field.IsStatic
                || field.IsInitOnly
                || field.IsLiteral
                || field.IsDefined(typeof(NonSerializedAttribute), false)
                || field.IsDefined(typeof(SerializeReference), false)
                || (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false))
            )
            {
                return null;
            }
            Type type = field.FieldType;
            if (type.IsArray)
            {
                if (type.GetArrayRank() != 1)
                {
                    return null;
                }
                collection = true;
                type = type.GetElementType();
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                collection = true;
                type = type.GetGenericArguments()[0];
            }
            if (
                type == null
                || type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || typeof(Object).IsAssignableFrom(type)
                || typeof(Delegate).IsAssignableFrom(type)
                || !type.IsDefined(typeof(SerializableAttribute), false)
            )
            {
                return null;
            }
            return type;
        }

        private static void JudgeWithNestedValues(
            string assetPath,
            IReadOnlyList<string> lines,
            AuthoredAssetDocument document,
            List<AuthoredRequirementField> required,
            List<AuthoredRequirementFinding> findings
        )
        {
            bool hasNestedRequirement = false;
            foreach (AuthoredRequirementField field in required)
            {
                if (!typeof(Object).IsAssignableFrom(field.DeclaringType))
                {
                    hasNestedRequirement = true;
                    break;
                }
            }
            if (
                !hasNestedRequirement
                || !MonoScriptIndex.TryGetScriptType(document.ScriptGuid, out Type type)
            )
            {
                Judge(assetPath, lines, document, required, findings);
                return;
            }
            JudgeObject(assetPath, lines, document, type, required, findings, 0);
        }

        private static void JudgeObject(
            string assetPath,
            IReadOnlyList<string> lines,
            AuthoredAssetDocument document,
            Type type,
            List<AuthoredRequirementField> required,
            List<AuthoredRequirementFinding> findings,
            int depth
        )
        {
            if (MaximumDefaultValueDepth <= depth)
            {
                return;
            }
            using PooledResource<List<AuthoredRequirementField>> lease =
                Buffers<AuthoredRequirementField>.List.Get(
                    out List<AuthoredRequirementField> direct
                );
            foreach (AuthoredRequirementField field in required)
            {
                if (field.DeclaringType.IsAssignableFrom(type))
                {
                    direct.Add(field);
                }
            }
            Judge(assetPath, lines, document, direct, findings);
            foreach (FieldInfo field in ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(type))
            {
                Type valueType = SerializedNestedType(field, out bool collection);
                if (valueType == null)
                {
                    continue;
                }
                AuthoredRequirementField descriptor = new(
                    field.DeclaringType,
                    field.Name,
                    false,
                    collection,
                    AliasesOf(field)
                );
                if (!TryFindEntry(document, descriptor, out AuthoredAssetEntry entry))
                {
                    continue;
                }
                if (!collection)
                {
                    JudgeNestedRange(
                        assetPath,
                        lines,
                        document,
                        entry.LineNumber + 1,
                        entry.EndLineNumber,
                        valueType,
                        required,
                        findings,
                        depth + 1
                    );
                    continue;
                }
                int start = 0;
                foreach (
                    AuthoredSequenceElement element in AuthoredAssetYaml.EnumerateSequenceElements(
                        lines,
                        entry
                    )
                )
                {
                    if (0 < start)
                    {
                        JudgeNestedRange(
                            assetPath,
                            lines,
                            document,
                            start,
                            element.LineNumber,
                            valueType,
                            required,
                            findings,
                            depth + 1
                        );
                    }
                    start = element.LineNumber;
                }
                if (0 < start)
                {
                    JudgeNestedRange(
                        assetPath,
                        lines,
                        document,
                        start,
                        entry.EndLineNumber,
                        valueType,
                        required,
                        findings,
                        depth + 1
                    );
                }
            }
        }

        private static void JudgeNestedRange(
            string assetPath,
            IReadOnlyList<string> lines,
            AuthoredAssetDocument document,
            int start,
            int end,
            Type type,
            List<AuthoredRequirementField> required,
            List<AuthoredRequirementFinding> findings,
            int depth
        )
        {
            using PooledResource<List<AuthoredAssetEntry>> lease =
                Buffers<AuthoredAssetEntry>.List.Get(out List<AuthoredAssetEntry> entries);
            int lower = 0;
            int upper = document.Entries.Count;
            while (lower < upper)
            {
                int middle = lower + (upper - lower) / 2;
                if (document.Entries[middle].LineNumber < start)
                {
                    lower = middle + 1;
                }
                else
                {
                    upper = middle;
                }
            }
            for (int index = lower; index < document.Entries.Count; ++index)
            {
                AuthoredAssetEntry entry = document.Entries[index];
                if (end <= entry.LineNumber)
                {
                    break;
                }
                entries.Add(entry);
            }
            if (entries.Count == 0)
            {
                return;
            }
            AuthoredAssetDocument nested = new(
                document.FileId,
                document.UnityTypeId,
                document.RootKey,
                false,
                start,
                end,
                entries
            );
            JudgeObject(assetPath, lines, nested, type, required, findings, depth);
        }
    }
#endif
}
