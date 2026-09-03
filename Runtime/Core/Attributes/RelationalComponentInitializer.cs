// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Explicit initializer for Relational Components.
    /// Pre-warms ReflectionHelpers caches (field getters/setters and collection creators)
    /// for all fields marked with relational component attributes across the provided types
    /// or, if none are provided, across all loaded <see cref="UnityEngine.Component"/> types.
    /// </summary>
    /// <remarks>
    /// - Uses <see cref="AttributeMetadataCache"/> when available to avoid heavy reflection.
    /// - Falls back to reflection-based discovery when a type is not present in the cache.
    /// - Logs warnings and continues when individual fields or element types cannot be resolved.
    /// - Call during a loading screen or bootstrap to eliminate first-use stalls.
    /// </remarks>
    public static class RelationalComponentInitializer
    {
        /// <summary>
        /// Pre-warms caches for relational attributes on the provided <paramref name="types"/>.
        /// If <paramref name="types"/> is null, scans all loaded <see cref="UnityEngine.Component"/> types.
        /// </summary>
        /// <param name="types">Specific types to pre-warm, or null to scan all loaded Component types.</param>
        /// <param name="logSummary">When true, logs a summary to the Unity Console.</param>
        /// <returns>Summary <see cref="Report"/> of work performed.</returns>
        public static Report Initialize(IEnumerable<Type> types = null, bool logSummary = true)
        {
            Report report = new();
            AttributeMetadataCache cache = AttributeMetadataCache.Instance;

            IEnumerable<Type> targetTypes = types ?? EnumerateAllComponentTypes();

            foreach (Type type in targetTypes)
            {
                if (type == null)
                {
                    continue;
                }

                report.TypesVisited++;

                try
                {
                    int warmed = WarmType(type, cache, report);
                    report.AddTypeResult(type, warmed);
                }
                catch (Exception e)
                {
                    report.Errors++;
                    Debug.LogError(
                        $"RelationalComponents.Initialize: Error pre-warming type '{type.FullName}': {e.Message}\n{e}"
                    );
                }
            }

            if (logSummary)
            {
                Debug.Log(
                    $"RelationalComponents.Initialize: Done. TypesVisited={report.TypesVisited}, TypesWarmed={report.TypesWarmed}, FieldsWarmed={report.FieldsWarmed}, Warnings={report.Warnings}, Errors={report.Errors}."
                );
            }

            return report;
        }

        private static IEnumerable<Type> EnumerateAllComponentTypes()
        {
            foreach (Type t in ReflectionHelpers.GetAllLoadedTypes())
            {
                if (t != null && typeof(Component).IsAssignableFrom(t))
                {
                    yield return t;
                }
            }
        }

        private static int WarmType(Type componentType, AttributeMetadataCache cache, Report report)
        {
            AttributeMetadataCache.ResolvedRelationalFieldMetadata[] resolved = null;
            bool usedCache =
                cache != null && cache.TryGetResolvedRelationalFields(componentType, out resolved);

            if (!usedCache)
            {
                // Fallback: discover relational fields via reflection
                resolved = DiscoverRelationalFieldsViaReflection(componentType);
            }

            if (resolved == null || resolved.Length == 0)
            {
                return 0;
            }

            /*
                The three attribute types each key their own FieldMetadata cache by component type,
                and every assignment path reads it. Warming the delegates without populating it left
                the largest first-use cost -- building the metadata, including its field accessor --
                to the first Awake that asked.
            */
            _ = SiblingComponentExtensions.GetOrCreateFields(componentType);
            _ = ChildComponentExtensions.GetOrCreateFields(componentType);
            _ = ParentComponentExtensions.GetOrCreateFields(componentType);

            int warmed = 0;

            foreach (AttributeMetadataCache.ResolvedRelationalFieldMetadata fieldMeta in resolved)
            {
                FieldInfo field = ReflectionHelpers.GetInstanceFieldIncludingBaseTypes(
                    componentType,
                    fieldMeta.FieldName
                );

                if (field == null)
                {
                    report.Warnings++;
                    Debug.LogWarning(
                        $"RelationalComponents.Initialize: Field '{fieldMeta.FieldName}' not found on '{componentType.FullName}'."
                    );
                    continue;
                }

                try
                {
                    // Force-create field accessors
                    _ = ReflectionHelpers.GetFieldGetter(field);
                    _ = ReflectionHelpers.GetFieldSetter(field);

                    // Determine the element type and prewarm collection creators where applicable
                    Type elementType = fieldMeta.ElementType ?? InferElementType(field.FieldType);
                    PrewarmCollectionCreators(fieldMeta.FieldKind, elementType);

                    warmed++;
                }
                catch (Exception e)
                {
                    report.Errors++;
                    Debug.LogError(
                        $"RelationalComponents.Initialize: Error warming field '{componentType.FullName}.{fieldMeta.FieldName}': {e.Message}\n{e}"
                    );
                }
            }

            return warmed;
        }

        private static void PrewarmCollectionCreators(
            AttributeMetadataCache.FieldKind kind,
            Type elementType
        )
        {
            if (elementType == null)
            {
                return;
            }

            switch (kind)
            {
                case AttributeMetadataCache.FieldKind.Array:
                    _ = ReflectionHelpers.GetArrayCreator(elementType);
                    break;
                case AttributeMetadataCache.FieldKind.List:
                    _ = ReflectionHelpers.GetListWithCapacityCreator(elementType);
                    break;
                case AttributeMetadataCache.FieldKind.HashSet:
                    _ = ReflectionHelpers.GetHashSetWithCapacityCreator(elementType);
                    _ = ReflectionHelpers.GetHashSetAdder(elementType);
                    /*
                        GetFieldMetadata requires the clearer for every HashSet field, so leaving it
                        cold meant a set-valued field still paid a first-use stall from a call that
                        advertises removing it.
                    */
                    _ = ReflectionHelpers.GetHashSetClearer(elementType);
                    break;
            }
        }

        private static Type InferElementType(Type fieldType)
        {
            if (fieldType == null)
            {
                return null;
            }
            if (fieldType.IsArray)
            {
                return fieldType.GetElementType();
            }
            if (fieldType.IsGenericType)
            {
                Type g = fieldType.GetGenericTypeDefinition();
                if (g == typeof(List<>))
                {
                    return fieldType.GenericTypeArguments[0];
                }

                if (g == typeof(HashSet<>))
                {
                    return fieldType.GenericTypeArguments[0];
                }
            }
            return fieldType;
        }

        private static AttributeMetadataCache.ResolvedRelationalFieldMetadata[] DiscoverRelationalFieldsViaReflection(
            Type componentType
        )
        {
            FieldInfo[] fields = ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(
                componentType
            );
            if (fields.Length == 0)
            {
                return Array.Empty<AttributeMetadataCache.ResolvedRelationalFieldMetadata>();
            }

            PooledResource<
                List<AttributeMetadataCache.ResolvedRelationalFieldMetadata>
            > resultLease = default;
            List<AttributeMetadataCache.ResolvedRelationalFieldMetadata> result = null;
            foreach (FieldInfo f in fields)
            {
                if (f == null)
                {
                    continue;
                }

                AttributeMetadataCache.RelationalAttributeKind kindMeta;

                if (ReflectionHelpers.HasAttributeSafe<ParentComponentAttribute>(f, inherit: false))
                {
                    kindMeta = AttributeMetadataCache.RelationalAttributeKind.Parent;
                }
                else if (
                    ReflectionHelpers.HasAttributeSafe<ChildComponentAttribute>(f, inherit: false)
                )
                {
                    kindMeta = AttributeMetadataCache.RelationalAttributeKind.Child;
                }
                else if (
                    ReflectionHelpers.HasAttributeSafe<SiblingComponentAttribute>(f, inherit: false)
                )
                {
                    kindMeta = AttributeMetadataCache.RelationalAttributeKind.Sibling;
                }
                else
                {
                    continue;
                }

                AttributeMetadataCache.FieldKind fieldKindMeta = AttributeMetadataCache
                    .FieldKind
                    .Single;
                Type elementType = null;

                Type ft = f.FieldType;
                if (ft.IsArray)
                {
                    fieldKindMeta = AttributeMetadataCache.FieldKind.Array;
                    elementType = ft.GetElementType();
                }
                else if (ft.IsGenericType)
                {
                    Type g = ft.GetGenericTypeDefinition();
                    if (g == typeof(List<>))
                    {
                        fieldKindMeta = AttributeMetadataCache.FieldKind.List;
                        elementType = ft.GenericTypeArguments[0];
                    }
                    else if (g == typeof(HashSet<>))
                    {
                        fieldKindMeta = AttributeMetadataCache.FieldKind.HashSet;
                        elementType = ft.GenericTypeArguments[0];
                    }
                    else
                    {
                        elementType = ft;
                    }
                }
                else
                {
                    elementType = ft;
                }

                bool isInterface =
                    elementType != null
                    && (
                        elementType.IsInterface
                        || (!elementType.IsSealed && elementType != typeof(Component))
                    );

                if (result == null)
                {
                    resultLease =
                        Buffers<AttributeMetadataCache.ResolvedRelationalFieldMetadata>.List.Get(
                            out result
                        );
                }
                result.Add(
                    new AttributeMetadataCache.ResolvedRelationalFieldMetadata(
                        f.Name,
                        kindMeta,
                        fieldKindMeta,
                        elementType,
                        isInterface
                    )
                );
            }

            if (result == null)
            {
                return Array.Empty<AttributeMetadataCache.ResolvedRelationalFieldMetadata>();
            }

            AttributeMetadataCache.ResolvedRelationalFieldMetadata[] array = result.ToArray();
            resultLease.Dispose();
            return array;
        }

        /// <summary>
        /// Report with summary statistics for a pre-warm run.
        /// </summary>
        public sealed class Report
        {
            /// <summary>Total types visited (either provided or discovered).</summary>
            public int TypesVisited { get; internal set; }

            /// <summary>Types that had at least one relational field warmed.</summary>
            public int TypesWarmed { get; internal set; }

            /// <summary>Total number of relational fields warmed.</summary>
            public int FieldsWarmed { get; internal set; }

            /// <summary>Non-fatal issues (e.g., missing fields/types) encountered.</summary>
            public int Warnings { get; internal set; }

            /// <summary>Fatal errors encountered while processing a type.</summary>
            public int Errors { get; internal set; }

            /// <summary>
            /// Optional per-type breakdown for diagnostics. Not serialized.
            /// </summary>
            public readonly Dictionary<Type, int> WarmedFieldsPerType = new();

            internal void AddTypeResult(Type type, int warmedCount)
            {
                if (warmedCount <= 0)
                {
                    return;
                }
                TypesWarmed++;
                FieldsWarmed += warmedCount;
                WarmedFieldsPerType[type] = warmedCount;
            }
        }
    }
}
