// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Extension;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using static RelationalComponentProcessor;
#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
    using Unity.Profiling;
#endif

    /// <summary>
    /// Automatically assigns child components (components down the transform hierarchy) to the decorated field.
    /// Supports single components, <see cref="System.Array"/>s, <see cref="System.Collections.Generic.List{T}"/>,
    /// and <see cref="System.Collections.Generic.HashSet{T}"/> collection types.
    /// </summary>
    /// <remarks>
    /// Call <see cref="ChildComponentExtensions.AssignChildComponents"/> (or
    /// <see cref="RelationalComponentExtensions.AssignRelationalComponents(UnityEngine.Component)"/>) to populate the field.
    /// This is typically done in <c>Awake()</c> or <c>OnEnable()</c>.
    ///
    /// By default, searches include the current <see cref="GameObject"/>; set <see cref="OnlyDescendants"/> to exclude it.
    /// Limit traversal with <see cref="MaxDepth"/> (depth 1 = immediate children only). Children are visited in breadth-first order.
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
        private static readonly Dictionary<
            Type,
            FieldMetadata<ChildComponentAttribute>[]
        > FieldsByType = new();

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
            FieldMetadata<ChildComponentAttribute>[] fields = FieldsByType.GetOrAdd(
                component.GetType(),
                type => GetFieldMetadata<ChildComponentAttribute>(type)
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
                    if (TryAssignChildSingleFast(component, metadata, out Component childComponent))
                    {
                        metadata.SetValue(component, childComponent);
                        foundChild = true;
                    }
                    else
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
                                out childComponent
                            )
                        )
                        {
                            metadata.SetValue(component, childComponent);
                            foundChild = true;
                        }
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
                        using PooledResource<List<Transform>> childBufferResource =
                            Buffers<Transform>.List.Get(out List<Transform> childBuffer);

                        switch (metadata.kind)
                        {
                            case FieldKind.Array:
                            {
                                using PooledResource<List<Component>> cacheResource =
                                    Buffers<Component>.List.Get(out List<Component> cache);
                                cache.Clear();
                                int filteredCount = EnumerateFilteredChildComponents(
                                    component,
                                    metadata,
                                    filters,
                                    childBuffer,
                                    candidate =>
                                    {
                                        cache.Add(candidate);
                                        return true;
                                    }
                                );

                                Array correctTypedArray = metadata.arrayCreator(filteredCount);
                                for (int i = 0; i < filteredCount; ++i)
                                {
                                    correctTypedArray.SetValue(cache[i], i);
                                }

                                metadata.SetValue(component, correctTypedArray);
                                foundChild = filteredCount > 0;
                                break;
                            }
                            case FieldKind.List:
                            {
                                object existing = metadata.GetValue(component);
                                IList list = existing as IList;
                                if (list == null)
                                {
                                    int initialCapacity =
                                        metadata.attribute.MaxCount > 0
                                            ? metadata.attribute.MaxCount
                                            : 0;
                                    list = metadata.listCreator(initialCapacity);
                                    metadata.SetValue(component, list);
                                }
                                else
                                {
                                    list.Clear();
                                }

                                int added = EnumerateFilteredChildComponents(
                                    component,
                                    metadata,
                                    filters,
                                    childBuffer,
                                    candidate =>
                                    {
                                        list.Add(candidate);
                                        return true;
                                    }
                                );

                                foundChild = added > 0;
                                break;
                            }
                            case FieldKind.HashSet:
                            {
                                object instance = metadata.GetValue(component);
                                if (instance != null && metadata.hashSetClearer != null)
                                {
                                    metadata.hashSetClearer(instance);
                                }
                                else
                                {
                                    int initialCapacity =
                                        metadata.attribute.MaxCount > 0
                                            ? metadata.attribute.MaxCount
                                            : 0;
                                    instance = metadata.hashSetCreator(initialCapacity);
                                    metadata.SetValue(component, instance);
                                }

                                int added = EnumerateFilteredChildComponents(
                                    component,
                                    metadata,
                                    filters,
                                    childBuffer,
                                    candidate =>
                                    {
                                        metadata.hashSetAdder(instance, candidate);
                                        return true;
                                    }
                                );

                                foundChild = added > 0;
                                break;
                            }
                            default:
                            {
                                break;
                            }
                        }
                    }
                }

                if (!foundChild)
                {
                    LogMissingComponentError(component, metadata, "child");
                    AssignNullToSingleField(component, metadata);
                }
            }
        }

        internal static FieldMetadata<ChildComponentAttribute>[] GetOrCreateFields(Type type)
        {
            return FieldsByType.GetOrAdd(type, t => GetFieldMetadata<ChildComponentAttribute>(t));
        }

        private static bool TryAssignChildSingleFast(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            out Component childComponent
        )
        {
            childComponent = null;
            ChildComponentAttribute attribute = metadata.attribute;

            if (
                metadata.isInterface
                || attribute.MaxDepth != 0
                || attribute.TagFilter != null
                || attribute.NameFilter != null
            )
            {
                return false;
            }

            Component[] results = component.GetComponentsInChildren(
                metadata.elementType,
                attribute.IncludeInactive
            );

            if (results == null || results.Length == 0)
            {
                return false;
            }

            Transform componentTransform = component.transform;
            for (int i = 0; i < results.Length; ++i)
            {
                Component candidate = results[i];
                if (candidate == null)
                {
                    continue;
                }

                if (attribute.OnlyDescendants && candidate.transform == componentTransform)
                {
                    continue;
                }

                childComponent = candidate;
                return true;
            }

            return false;
        }

        private static bool TryAssignChildCollectionFast(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            FilterParameters filters,
            out bool assignedAny
        )
        {
            assignedAny = false;
            ChildComponentAttribute attribute = metadata.attribute;
            if (metadata.isInterface || filters.RequiresPostProcessing || attribute.MaxDepth > 0)
            {
#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
                ChildFallbackMarker.Begin();
                ChildFallbackMarker.End();
#endif
                return false;
            }

#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
            using (ChildFastPathMarker.Auto())
#endif
            {
                Array children = ChildComponentFastInvoker.GetArray(
                    component,
                    metadata.elementType,
                    attribute.IncludeInactive
                );

                Array filtered = FilterChildArray(component, metadata, children);
                Array ordered = EnsureBreadthFirstOrder(component, metadata, filtered);
                assignedAny = AssignChildComponentsFromArray(component, metadata, ordered);
                return true;
            }
        }

        private static Array FilterChildArray(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            Array source
        )
        {
            Type elementType = metadata.elementType;
            if (source == null || source.Length == 0)
            {
                return Array.CreateInstance(elementType, 0);
            }

            ChildComponentAttribute attribute = metadata.attribute;
            bool onlyDescendants = attribute.OnlyDescendants;
            Transform self = component.transform;

            int maxCount = attribute.MaxCount;
            if (!onlyDescendants && maxCount <= 0)
            {
                return source;
            }

            int limit = maxCount > 0 ? Math.Min(maxCount, source.Length) : source.Length;
            Array staged = Array.CreateInstance(elementType, limit);
            int writeIndex = 0;

            for (int i = 0; i < source.Length && writeIndex < limit; ++i)
            {
                Component candidate = source.GetValue(i) as Component;
                if (candidate == null)
                {
                    continue;
                }

                if (onlyDescendants && candidate.transform == self)
                {
                    continue;
                }

                staged.SetValue(candidate, writeIndex++);
            }

            if (writeIndex == staged.Length)
            {
                return staged;
            }

            Array result = Array.CreateInstance(elementType, writeIndex);
            if (writeIndex > 0)
            {
                Array.Copy(staged, 0, result, 0, writeIndex);
            }

            return result;
        }

        private static Array EnsureBreadthFirstOrder(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            Array source
        )
        {
            Type elementType = metadata.elementType;
            if (source == null)
            {
                return Array.CreateInstance(elementType, 0);
            }

            int length = source.Length;
            if (length <= 1)
            {
                return source;
            }

            ChildComponentAttribute attribute = metadata.attribute;
            using PooledResource<List<Transform>> traversalResource = Buffers<Transform>.List.Get(
                out List<Transform> traversal
            );
            component.IterateOverAllChildrenRecursivelyBreadthFirst(
                traversal,
                includeSelf: !attribute.OnlyDescendants,
                attribute.MaxDepth
            );

            using PooledResource<Dictionary<Transform, List<Component>>> groupedResource =
                DictionaryBuffer<Transform, List<Component>>.Dictionary.Get(
                    out Dictionary<Transform, List<Component>> grouped
                );
            using PooledResource<Dictionary<Transform, int>> positionsResource = DictionaryBuffer<
                Transform,
                int
            >.Dictionary.Get(out Dictionary<Transform, int> positions);
            using PooledResource<List<PooledResource<List<Component>>>> groupedLeaseTracker =
                Buffers<PooledResource<List<Component>>>.List.Get(
                    out List<PooledResource<List<Component>>> groupedListLeases
                );

            for (int i = 0; i < length; ++i)
            {
                Component candidate = source.GetValue(i) as Component;
                if (candidate == null)
                {
                    continue;
                }

                Transform key = candidate.transform;
                if (!grouped.TryGetValue(key, out List<Component> list))
                {
                    PooledResource<List<Component>> lease = Buffers<Component>.List.Get(out list);
                    groupedListLeases.Add(lease);
                    grouped.Add(key, list);
                    positions.Add(key, 0);
                }

                list.Add(candidate);
            }

            Array ordered = Array.CreateInstance(elementType, length);
            int writeIndex = 0;

            for (int i = 0; i < traversal.Count && writeIndex < length; ++i)
            {
                Transform transform = traversal[i];
                if (!grouped.TryGetValue(transform, out List<Component> list))
                {
                    continue;
                }

                int position = positions[transform];
                while (position < list.Count && writeIndex < length)
                {
                    ordered.SetValue(list[position], writeIndex++);
                    position++;
                }

                if (position >= list.Count)
                {
                    grouped.Remove(transform);
                    positions.Remove(transform);
                }
                else
                {
                    positions[transform] = position;
                }
            }

            if (writeIndex < length && grouped.Count > 0)
            {
                foreach (KeyValuePair<Transform, List<Component>> pair in grouped)
                {
                    List<Component> list = pair.Value;
                    int position = positions[pair.Key];
                    while (position < list.Count && writeIndex < length)
                    {
                        ordered.SetValue(list[position], writeIndex++);
                        position++;
                    }
                    if (writeIndex >= length)
                    {
                        break;
                    }
                }
            }

            if (writeIndex >= length)
            {
                DisposeGroupLeases(groupedListLeases);
                return ordered;
            }

            if (writeIndex == 0)
            {
                DisposeGroupLeases(groupedListLeases);
                return Array.CreateInstance(elementType, 0);
            }

            Array trimmed = Array.CreateInstance(elementType, writeIndex);
            Array.Copy(ordered, 0, trimmed, 0, writeIndex);
            DisposeGroupLeases(groupedListLeases);
            return trimmed;
        }

        private static void DisposeGroupLeases(
            List<PooledResource<List<Component>>> groupedListLeases
        )
        {
            if (groupedListLeases == null)
            {
                return;
            }

            for (int i = groupedListLeases.Count - 1; i >= 0; --i)
            {
                groupedListLeases[i].Dispose();
            }
            groupedListLeases.Clear();
        }

        private static bool AssignChildComponentsFromArray(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            Array componentsArray
        )
        {
            if (componentsArray == null)
            {
                componentsArray = Array.CreateInstance(metadata.elementType, 0);
            }

            int count = componentsArray.Length;

            switch (metadata.kind)
            {
                case FieldKind.Array:
                {
                    Array instance = metadata.arrayCreator(count);
                    for (int i = 0; i < count; ++i)
                    {
                        instance.SetValue(componentsArray.GetValue(i), i);
                    }

                    metadata.SetValue(component, instance);
                    return count > 0;
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
                        list.Add(componentsArray.GetValue(i));
                    }

                    return count > 0;
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
                        metadata.hashSetAdder(hashSet, componentsArray.GetValue(i));
                    }

                    return count > 0;
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

            childComponent = null;

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
                    childComponent = resolved;
                    break;
                }
            }

            if (needsScratch)
            {
                scratch.Dispose();
            }

            return childComponent != null;
        }

        private static int EnumerateFilteredChildComponents(
            Component component,
            FieldMetadata<ChildComponentAttribute> metadata,
            FilterParameters filters,
            List<Transform> childBuffer,
            Func<Component, bool> onComponent
        )
        {
            if (component == null)
            {
                return 0;
            }

            ChildComponentAttribute attribute = metadata.attribute;
            int maxAssignments = attribute.MaxCount > 0 ? attribute.MaxCount : int.MaxValue;
            int added = 0;

            using PooledResource<List<Component>> componentBuffer = Buffers<Component>.List.Get(
                out List<Component> components
            );

            foreach (
                Transform child in component.IterateOverAllChildrenRecursivelyBreadthFirst(
                    childBuffer,
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

                for (int i = 0; i < components.Count; ++i)
                {
                    Component candidate = components[i];
                    if (!PassesStateAndFilters(candidate, filters, filterDisabledComponents: true))
                    {
                        continue;
                    }

                    if (!onComponent(candidate))
                    {
                        return added;
                    }

                    added++;
                    if (added >= maxAssignments)
                    {
                        return added;
                    }
                }
            }

            return added;
        }
    }

    internal static class ChildComponentFastInvoker
    {
        internal static Array GetArray(Component component, Type elementType, bool includeInactive)
        {
            // AOT-safe: the non-generic Type overload avoids the runtime generic-method +
            // Expression.Compile path, which IL2CPP cannot service (the old compiled path threw
            // at runtime in player builds). GetComponentsInChildren(Type, bool) returns only
            // elementType instances; copy them into a typed array the caller can assign.
            Component[] matches = component.GetComponentsInChildren(elementType, includeInactive);
            Array typed = Array.CreateInstance(elementType, matches.Length);
            Array.Copy(matches, typed, matches.Length);
            return typed;
        }
    }
}
