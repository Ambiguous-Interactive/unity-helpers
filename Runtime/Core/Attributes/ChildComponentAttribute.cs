// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Extension;
    using UnityEngine;
    using UnityEngine.Scripting;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using static RelationalComponentProcessor;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#endif

#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
    using Unity.Profiling;
#endif

    /// <summary>
    /// Automatically assigns child components (components down the transform hierarchy) to the decorated field.
    /// Supports single components, <see cref="System.Array"/>s, <see cref="System.Collections.Generic.List{T}"/>,
    /// and <see cref="System.Collections.Generic.HashSet{T}"/> collection types.
    /// </summary>
    /// <remarks>
    /// Call <see cref="ChildComponentExtensions.AssignChildComponents(UnityEngine.Component)"/> (or
    /// <see cref="RelationalComponentExtensions.AssignRelationalComponents(UnityEngine.Component)"/>) to populate the field.
    /// This is typically done in <c>Awake()</c> or <c>OnEnable()</c>.
    ///
    /// By default, searches include the current <see cref="GameObject"/>; set <see cref="OnlyDescendants"/> to exclude it.
    /// Limit traversal with <see cref="MaxDepth"/> (depth 1 = immediate children only). Children are visited in breadth-first order. Single fields select the first eligible match;
    /// collection limits apply after filtering in that same order.
    /// Collection queries collect matches before rebuilding lists and sets; comparer callbacks cannot change the current query snapshot.
    /// Combine with filters like <see cref="BaseRelationalComponentAttribute.TagFilter"/> and
    /// <see cref="BaseRelationalComponentAttribute.NameFilter"/>. Interfaces and base types are supported when
    /// <see cref="BaseRelationalComponentAttribute.AllowInterfaces"/> is true (default).
    ///
    /// IMPORTANT: This attribute populates fields at runtime, not during Unity serialization in Edit mode.
    /// Fields populated by this attribute will not be serialized by Unity.
    ///
    /// <seealso cref="BaseRelationalComponentAttribute"/>
    /// <seealso cref="ChildComponentExtensions.AssignChildComponents(UnityEngine.Component)"/>
    /// <seealso cref="RelationalComponentExtensions.AssignRelationalComponents(UnityEngine.Component)"/>
    /// </remarks>
    /// <example>
    /// Typical child searches with depth limits and collections:
    /// <code><![CDATA[
    /// using UnityEngine;
    /// using WallstopStudios.UnityHelpers.Core.Attributes;
    ///
    /// public class EnemyRoot : MonoBehaviour
    /// {
    ///     // Immediate children only
    ///     [ChildComponent(OnlyDescendants = true, MaxDepth = 1)]
    ///     private Transform[] immediateChildren;
    ///
    ///     // Find the first matching descendant with a tag
    ///     [ChildComponent(OnlyDescendants = true, TagFilter = "Weapon")]
    ///     private Collider2D weaponCollider;
    ///
    ///     // Gather into a hash set (no duplicates)
    ///     [ChildComponent(OnlyDescendants = true, MaxCount = 10)]
    ///     private HashSet<Rigidbody2D> firstTenRigidbodies;
    ///
    ///     private void Awake()
    ///     {
    ///         this.AssignChildComponents();
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    [Preserve]
    public sealed class ChildComponentAttribute : BaseRelationalComponentAttribute
    {
        /// <summary>
        /// If true, excludes components on the current <see cref="GameObject"/> and only searches descendant transforms.
        /// If false, includes components on the current <see cref="GameObject"/> in the search. Default: false.
        /// </summary>
        public bool OnlyDescendants { get; set; } = false;

        /// <summary>
        /// Maximum depth to search down the hierarchy. 0 means unlimited. Default: 0.
        /// Depth 1 = immediate children only, depth 2 = children and grandchildren, etc.
        /// </summary>
        /// <remarks>
        /// Negative values are treated as 0 (unlimited). The search is breadth-first, so closer
        /// descendants are found before distant ones regardless of depth limit.
        /// </remarks>
        public int MaxDepth
        {
            get => _maxDepth;
            set => _maxDepth = value < 0 ? 0 : value;
        }
        private int _maxDepth;
    }

    public static class ChildComponentExtensions
    {
#if SINGLE_THREADED
        private static readonly Dictionary<
            Type,
            FieldMetadata<ChildComponentAttribute>[]
        > FieldsByType = new();
#else
        private static readonly ConcurrentDictionary<
            Type,
            FieldMetadata<ChildComponentAttribute>[]
        > FieldsByType = new();
#endif

#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
        private static readonly ProfilerMarker ChildFastPathMarker = new(
            "RelationalComponents.Child.FastPath"
        );
        private static readonly ProfilerMarker ChildFallbackMarker = new(
            "RelationalComponents.Child.Fallback"
        );
#endif

        /// <summary>
        /// Assigns fields on <paramref name="component"/> marked with <see cref="ChildComponentAttribute"/>.
        /// </summary>
        /// <param name="component">The component whose fields will be populated.</param>
        /// <remarks>
        /// Typical call site is <c>Awake()</c> or <c>OnEnable()</c>. For convenience, you can also call
        /// <see cref="RelationalComponentExtensions.AssignRelationalComponents(UnityEngine.Component)"/> to assign all relational attributes.
        /// </remarks>
        /// <example>
        /// <code><![CDATA[
        /// void Awake()
        /// {
        ///     this.AssignChildComponents();
        /// }
        /// ]]></code>
        /// </example>
        public static void AssignChildComponents(this Component component)
        {
            if (component == null)
            {
                return;
            }

            FieldMetadata<ChildComponentAttribute>[] fields = FieldsByType.GetOrAdd(
                component.GetType(),
                static type => GetFieldMetadata<ChildComponentAttribute>(type)
            );
            AssignChildComponents(component, fields);
        }

        internal static void AssignChildComponents(
            Component component,
            FieldMetadata<ChildComponentAttribute>[] fields
        )
        {
            if (component == null || fields == null || fields.Length == 0)
            {
                return;
            }

            foreach (FieldMetadata<ChildComponentAttribute> metadata in fields)
            {
                if (ShouldSkipAssignment(metadata, component))
                {
                    continue;
                }

                bool foundChild = false;
                if (metadata.kind == FieldKind.Single)
                {
                    FilterParameters filters = metadata.Filters;
                    using PooledResource<List<Transform>> childBufferResource =
                        Buffers<Transform>.List.Get(out List<Transform> childBuffer);
                    if (
                        TryAssignChildSingleFallback(
                            component,
                            metadata,
                            filters,
                            childBuffer,
                            out Component childComponent
                        )
                    )
                    {
                        metadata.SetValue(component, childComponent);
                        foundChild = true;
                    }
                }
                else
                {
                    FilterParameters filters = metadata.Filters;
                    if (
                        TryAssignChildCollectionFast(
                            component,
                            metadata,
                            filters,
                            out bool assignedAny
                        )
                    )
                    {
                        foundChild = assignedAny;
                    }
                    else
                    {
                        foundChild = AssignChildCollectionFallback(component, metadata);
                    }
                }

                if (!foundChild)
                {
                    LogMissingComponentError(component, metadata, "child");
                    AssignNullToSingleField(component, metadata);
                }
            }
        }

        internal static bool HasCachedFieldMetadata(Type type)
        {
            return type != null && FieldsByType.ContainsKey(type);
        }

        internal static void ClearCachedFieldMetadata()
        {
            FieldsByType.Clear();
        }

        internal static FieldMetadata<ChildComponentAttribute>[] GetOrCreateFields(Type type)
        {
            return FieldsByType.GetOrAdd(
                type,
                static t => GetFieldMetadata<ChildComponentAttribute>(t)
            );
        }

        internal static int FilterChildren(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            List<Component> source
        )
        {
            if (source == null)
            {
                return 0;
            }
            int sourceCount = source.Count;
            if (sourceCount == 0 || sourceCount == 1 && source[0] == null)
            {
                return 0;
            }

            ChildComponentAttribute attribute = metadata.attribute;
            bool onlyDescendants = attribute.OnlyDescendants;
            Transform self = component.transform;

            if (!onlyDescendants)
            {
                return sourceCount;
            }

            int writeIndex = 0;

            for (int i = 0; i < sourceCount; ++i)
            {
                Component candidate = source[i];
                if (candidate == null)
                {
                    continue;
                }

                if (onlyDescendants && candidate.transform == self)
                {
                    continue;
                }

                source[writeIndex++] = candidate;
            }

            return writeIndex;
        }

        private static bool TryAssignChildCollectionFast(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            FilterParameters filters,
            out bool assignedAny
        )
        {
            ChildComponentAttribute attribute = metadata.attribute;
            if (
                metadata.isInterface
                || filters.RequiresPostProcessing
                || 0 < attribute.MaxDepth
                || 0 < attribute.MaxCount
            )
            {
#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
                ChildFallbackMarker.Begin();
                ChildFallbackMarker.End();
#endif
                assignedAny = false;
                return false;
            }

#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
            using (ChildFastPathMarker.Auto())
#endif
            {
                List<Component> children = ChildComponentFastInvoker.Collect(
                    component,
                    metadata.elementType,
                    attribute.IncludeInactive
                );
                try
                {
                    int count = FilterChildren(component, metadata, children);
                    if (1 < count)
                    {
                        children.Clear();
                        using PooledResource<List<Transform>> traversalLease =
                            Buffers<Transform>.List.Get(out List<Transform> traversal);
                        count = CollectChildComponents(component, metadata, traversal, children);
                    }
                    assignedAny = AssignChildComponentsFromList(
                        component,
                        metadata,
                        children,
                        count
                    );
                    return true;
                }
                finally
                {
                    ChildComponentFastInvoker.Release(children);
                }
            }
        }

        private static bool AssignChildCollectionFallback(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata
        )
        {
            using PooledResource<List<Transform>> childBufferResource = Buffers<Transform>.List.Get(
                out List<Transform> childBuffer
            );

            using PooledResource<List<Component>> componentLease = Buffers<Component>.List.Get(
                out List<Component> components
            );
            components.Clear();
            int count = CollectChildComponents(component, metadata, childBuffer, components);
            return AssignChildComponentsFromList(component, metadata, components, count);
        }

        private static bool AssignChildComponentsFromList(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            List<Component> components,
            int count
        )
        {
            if (components == null || count < 0)
            {
                count = 0;
            }
            else
            {
                int componentCount = components.Count;
                if (componentCount < count)
                {
                    count = componentCount;
                }
            }

            switch (metadata.kind)
            {
                case FieldKind.Array:
                {
                    metadata.SetValue(
                        component,
                        CreateTypedArray(metadata.elementType, components, count)
                    );
                    return 0 < count;
                }
                case FieldKind.List:
                {
                    if (metadata.GetValue(component) is IList list)
                    {
                        list.Clear();
                    }
                    else
                    {
                        list = metadata.listCreator(count);
                        metadata.SetValue(component, list);
                    }

                    for (int i = 0; i < count; ++i)
                    {
                        list.Add(components[i]);
                    }

                    return 0 < count;
                }
                case FieldKind.HashSet:
                {
                    object hashSet = metadata.GetValue(component);
                    if (hashSet != null && metadata.hashSetClearer != null)
                    {
                        metadata.hashSetClearer(hashSet);
                    }
                    else
                    {
                        hashSet = metadata.hashSetCreator(count);
                        metadata.SetValue(component, hashSet);
                    }

                    for (int i = 0; i < count; ++i)
                    {
                        metadata.hashSetAdder(hashSet, components[i]);
                    }

                    return 0 < count;
                }
                default:
                {
                    return false;
                }
            }
        }

        private static bool TryAssignChildSingleFallback(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            FilterParameters filters,
            List<Transform> childBuffer,
            out Component childComponent
        )
        {
            bool needsScratch = metadata.isInterface || filters.RequiresPostProcessing;
            List<Component> scratchList = null;
            PooledResource<List<Component>> scratch = default;
            if (needsScratch)
            {
                scratch = Buffers<Component>.List.Get(out scratchList);
            }

            Component resolvedChild = null;

            bool foundChild = false;

            try
            {
                foreach (
                    Transform child in component.IterateOverAllChildrenRecursivelyBreadthFirst(
                        childBuffer,
                        includeSelf: !metadata.attribute.OnlyDescendants,
                        maxDepth: metadata.attribute.MaxDepth
                    )
                )
                {
                    if (
                        TryResolveSingleComponent(
                            child,
                            filters,
                            metadata.elementType,
                            metadata.isInterface,
                            metadata.attribute.AllowInterfaces,
                            scratchList,
                            out Component resolved
                        )
                    )
                    {
                        resolvedChild = resolved;
                        foundChild = true;
                        break;
                    }
                }
            }
            finally
            {
                if (needsScratch)
                {
                    scratch.Dispose();
                }
            }

            childComponent = resolvedChild;
            return foundChild;
        }

        private static int CollectChildComponents(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            List<Transform> traversal,
            List<Component> results
        )
        {
            ChildComponentAttribute attribute = metadata.attribute;
            FilterParameters filters = metadata.Filters;
            int maximum = 0 < attribute.MaxCount ? attribute.MaxCount : int.MaxValue;
            int count = results.Count;
            using PooledResource<List<Component>> componentLease = Buffers<Component>.List.Get(
                out List<Component> components
            );
            foreach (
                Transform child in component.IterateOverAllChildrenRecursivelyBreadthFirst(
                    traversal,
                    includeSelf: !attribute.OnlyDescendants,
                    maxDepth: attribute.MaxDepth
                )
            )
            {
                GetComponentsOfType(
                    child,
                    metadata.elementType,
                    metadata.isInterface,
                    attribute.AllowInterfaces,
                    components
                );
                foreach (Component candidate in components)
                {
                    if (!PassesStateAndFilters(candidate, filters, filterDisabledComponents: true))
                    {
                        continue;
                    }
                    results.Add(candidate);
                    ++count;
                    if (maximum <= count)
                    {
                        return count;
                    }
                }
            }
            return count;
        }
    }

    internal static class ChildComponentFastInvoker
    {
        // Detach the buffer before use: HashSet callbacks can reenter assignment and must receive another list.
        [ThreadStatic]
        private static List<Component> Scratch;

        internal static List<Component> Collect(
            Component component,
            Type elementType,
            bool includeInactive
        )
        {
            List<Component> results = Scratch;
            if (results == null)
            {
                results = new List<Component>();
            }
            else
            {
                Scratch = null;
            }

            results.Clear();

            RelationalComponentCollector collector = RelationalComponentCollector.For(
                elementType,
                component
            );
            if (collector != null)
            {
                _ = collector.CollectChildrenInto(component, includeInactive, results);
                return results;
            }

            // The non-generic query remains usable when IL2CPP cannot instantiate the generic collector.
            Component[] matches = component.GetComponentsInChildren(elementType, includeInactive);
            foreach (Component match in matches)
            {
                results.Add(match);
            }

            return results;
        }

        internal static void Release(List<Component> results)
        {
            if (results == null || Scratch != null)
            {
                return;
            }

            results.Clear();
            if (MaximumRetainedScratchCapacity < results.Capacity)
            {
                results.Capacity = 0;
            }

            Scratch = results;
        }
    }
}
