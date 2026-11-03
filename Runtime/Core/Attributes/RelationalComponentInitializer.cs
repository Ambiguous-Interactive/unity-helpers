// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Explicit initializer for Relational Components.
    /// Pre-warms ReflectionHelpers caches (field getters/setters and collection creators)
    /// for all fields marked with relational component attributes across the provided types
    /// or, if none are provided, across all loaded <see cref="UnityEngine.Component"/> types.
    /// </summary>
    /// <remarks>
    /// - Uses live field declarations and the same cached accessors and factories as assignment.
    /// - Includes fields missing from stale or incomplete serialized metadata.
    /// - Reports unsupported fields and continues warming supported declarations.
    /// - Skips unsupported field types without creating their accessors or collection factories.
    /// - Skips open generic types; closed types still warm inherited generic fields.
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
            IEnumerable<Type> targetTypes = types ?? EnumerateAllComponentTypes();

            foreach (Type type in targetTypes.AsValueEnumerableRequired())
            {
                if (type == null)
                {
                    continue;
                }

                ++report.TypesVisited;

                try
                {
                    int warmed = WarmType(type, report);
                    report.AddTypeResult(type, warmed);
                }
                catch (Exception e)
                {
                    ++report.Errors;
                    Debug.LogError(
                        $"RelationalComponents.Initialize: Error pre-warming type '{type.FullName}': {e}"
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

        private static int WarmType(Type componentType, Report report)
        {
            if (componentType.ContainsGenericParameters)
            {
                return 0;
            }

            int warmed = 0;
            bool hasRelationalFields = false;
            foreach (
                FieldInfo field in ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(
                    componentType
                )
            )
            {
                if (
                    !ReflectionHelpers.HasAttributeSafe<ParentComponentAttribute>(
                        field,
                        inherit: false
                    )
                    && !ReflectionHelpers.HasAttributeSafe<ChildComponentAttribute>(
                        field,
                        inherit: false
                    )
                    && !ReflectionHelpers.HasAttributeSafe<SiblingComponentAttribute>(
                        field,
                        inherit: false
                    )
                )
                {
                    continue;
                }

                hasRelationalFields = true;
                if (RelationalComponentProcessor.TryGetFieldKind(field.FieldType, out _, out _))
                {
                    ++warmed;
                }
                else
                {
                    ++report.Warnings;
                }
            }

            if (hasRelationalFields)
            {
                _ = SiblingComponentExtensions.GetOrCreateFields(componentType);
                _ = ChildComponentExtensions.GetOrCreateFields(componentType);
                _ = ParentComponentExtensions.GetOrCreateFields(componentType);
            }
            return warmed;
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

            /// <summary>Total supported relational field declarations warmed.</summary>
            public int FieldsWarmed { get; internal set; }

            /// <summary>Unsupported relational field declarations encountered.</summary>
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
                ++TypesWarmed;
                FieldsWarmed += warmedCount;
                WarmedFieldsPerType[type] = warmedCount;
            }
        }
    }
}
