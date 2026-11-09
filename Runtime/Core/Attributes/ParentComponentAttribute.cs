// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Extension;
    using UnityEngine;
    using UnityEngine.Scripting;
    using WallstopStudios.UnityHelpers.Utils;
    using static RelationalComponentProcessor;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#endif

#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
    using Unity.Profiling;
#endif

    /// <summary>
    /// Automatically assigns parent components (components up the transform hierarchy) to the decorated field.
    /// Supports single components, <see cref="System.Array"/>s, <see cref="System.Collections.Generic.List{T}"/>,
    /// and <see cref="System.Collections.Generic.HashSet{T}"/> collection types.
    /// </summary>
    /// <remarks>
    /// Call <see cref="ParentComponentExtensions.AssignParentComponents(UnityEngine.Component)"/> (or
    /// <see cref="RelationalComponentExtensions.AssignRelationalComponents(UnityEngine.Component)"/>) to populate the field.
    /// This is typically done in <c>Awake()</c> or <c>OnEnable()</c>.
    ///
    /// By default, searches include the current <see cref="GameObject"/>; set <see cref="OnlyAncestors"/> to exclude it.
    /// Limit traversal with <see cref="MaxDepth"/> (depth 1 = immediate parent only). Combine with filters like
    /// <see cref="BaseRelationalComponentAttribute.TagFilter"/> and <see cref="BaseRelationalComponentAttribute.NameFilter"/>.
    /// Collection results exclude missing-script entries, including unfiltered MonoBehaviour and Behaviour searches.
    /// Bounded queries stop after the requested depth or number of eligible matches, in nearest-first order.
    /// Queries collect matches before rebuilding lists and sets, so comparer callbacks cannot change the current snapshot.
    /// Interfaces and base types are supported when <see cref="BaseRelationalComponentAttribute.AllowInterfaces"/> is true (default).
    ///
    /// IMPORTANT: This attribute populates fields at runtime, not during Unity serialization in Edit mode.
    /// Fields populated by this attribute will not be serialized by Unity.
    ///
    /// <seealso cref="BaseRelationalComponentAttribute"/>
    /// <seealso cref="ParentComponentExtensions.AssignParentComponents(UnityEngine.Component)"/>
    /// <seealso cref="RelationalComponentExtensions.AssignRelationalComponents(UnityEngine.Component)"/>
    /// </remarks>
    /// <example>
    /// Typical parent searches with depth and filters:
    /// <code><![CDATA[
    /// using UnityEngine;
    /// using WallstopStudios.UnityHelpers.Core.Attributes;
    ///
    /// public interface IHealth { int Current { get; } }
    ///
    /// public class ChildComponent : MonoBehaviour
    /// {
    ///     // Immediate parent only
    ///     [ParentComponent(OnlyAncestors = true, MaxDepth = 1)]
    ///     private Transform directParent;
    ///
    ///     // Search up to 3 levels for a specific tag
    ///     [ParentComponent(OnlyAncestors = true, MaxDepth = 3, TagFilter = "Player")]
    ///     private Collider2D playerAncestorCollider;
    ///
    ///     // Interface lookup up the chain
    ///     [ParentComponent]
    ///     private IHealth healthProvider;
    ///
    ///     // Collect multiple up the chain (stops at MaxCount)
    ///     [ParentComponent(MaxCount = 2)]
    ///     private Rigidbody2D[] firstTwoRigidbodies;
    ///
    ///     private void Awake()
    ///     {
    ///         this.AssignParentComponents();
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    [Preserve]
    public sealed class ParentComponentAttribute : BaseRelationalComponentAttribute
    {
        /// <summary>
        /// If true, excludes components on the current <see cref="GameObject"/> and only searches parent transforms.
        /// If false, includes components on the current <see cref="GameObject"/> in the search. Default: false.
        /// </summary>
        public bool OnlyAncestors { get; set; } = false;

        /// <summary>
        /// Maximum depth to search up the hierarchy. 0 means unlimited. Default: 0.
        /// Depth 1 includes the immediate parent, depth 2 includes the grandparent, etc.
        /// </summary>
        /// <remarks>
        /// Negative values are treated as 0 (unlimited). Depth is measured from the component's
        /// GameObject at zero, whether or not OnlyAncestors excludes it from the results.
        /// The search proceeds from closest to most distant ancestors.
        /// </remarks>
        public int MaxDepth
        {
            get => _maxDepth;
            set => _maxDepth = value < 0 ? 0 : value;
        }
        private int _maxDepth;
    }

    public static class ParentComponentExtensions
    {
#if SINGLE_THREADED
        private static readonly Dictionary<
            Type,
            FieldMetadata<ParentComponentAttribute>[]
        > FieldsByType = new();
#else
        private static readonly ConcurrentDictionary<
            Type,
            FieldMetadata<ParentComponentAttribute>[]
        > FieldsByType = new();
#endif

#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
        private static readonly ProfilerMarker ParentFastPathMarker = new(
            "RelationalComponents.Parent.FastPath"
        );
        private static readonly ProfilerMarker ParentFallbackMarker = new(
            "RelationalComponents.Parent.Fallback"
        );
#endif

        /// <summary>
        /// Assigns fields on <paramref name="component"/> marked with <see cref="ParentComponentAttribute"/>.
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
        ///     this.AssignParentComponents();
        /// }
        /// ]]></code>
        /// </example>
        public static void AssignParentComponents(this Component component)
        {
            if (component == null)
            {
                return;
            }

            Type componentType = component.GetType();
            FieldMetadata<ParentComponentAttribute>[] fields = FieldsByType.GetOrAdd(
                componentType,
                static type => GetFieldMetadata<ParentComponentAttribute>(type)
            );
            AssignParentComponents(component, fields);
        }

        internal static void AssignParentComponents(
            Component component,
            FieldMetadata<ParentComponentAttribute>[] fields
        )
        {
            if (component == null || fields == null || fields.Length == 0)
            {
                return;
            }

            foreach (FieldMetadata<ParentComponentAttribute> field in fields)
            {
                if (ShouldSkipAssignment(field, component))
                {
                    continue;
                }

                FilterParameters filters = field.Filters;
                Transform root = component.transform;
                if (field.attribute.OnlyAncestors)
                {
                    root = root.parent;
                }

                if (root == null)
                {
                    SetEmptyCollection(component, field);
                    LogMissingComponentError(component, field, "parent");
                    AssignNullToSingleField(component, field);
                    continue;
                }
                else
                {
                    bool foundParent;
                    if (field.kind == FieldKind.Single)
                    {
                        if (
                            TryGetFirstParentComponent(
                                root,
                                filters,
                                field.elementType,
                                field.attribute,
                                field.isInterface,
                                out Component parentComponent
                            )
                        )
                        {
                            field.SetValue(component, parentComponent);
                            foundParent = true;
                        }
                        else
                        {
                            foundParent = false;
                        }
                    }
                    else
                    {
                        if (
                            TryAssignParentCollectionFast(
                                component,
                                root,
                                field,
                                filters,
                                out bool assignedAny
                            )
                        )
                        {
                            foundParent = assignedAny;
                        }
                        else
                        {
                            using PooledResource<List<Component>> parentLease =
                                Buffers<Component>.List.Get(out List<Component> parents);
                            int count = CollectParentComponents(root, field, filters, parents);
                            foundParent = AssignParentComponentsFromList(
                                component,
                                field,
                                parents,
                                count
                            );
                        }
                    }

                    if (!foundParent)
                    {
                        LogMissingComponentError(component, field, "parent");
                        AssignNullToSingleField(component, field);
                    }
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

        internal static FieldMetadata<ParentComponentAttribute>[] GetOrCreateFields(Type type)
        {
            return FieldsByType.GetOrAdd(
                type,
                static t => GetFieldMetadata<ParentComponentAttribute>(t)
            );
        }

        private static bool TryAssignParentCollectionFast(
            Component component,
            Transform root,
            FieldMetadata<ParentComponentAttribute> metadata,
            FilterParameters filters,
            out bool assignedAny
        )
        {
            ParentComponentAttribute attribute = metadata.attribute;
            if (
                metadata.isInterface
                || filters.RequiresPostProcessing
                || root == null
                || (0 < attribute.MaxDepth || 0 < attribute.MaxCount) && root.parent != null
            )
            {
#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
                ParentFallbackMarker.Begin();
                ParentFallbackMarker.End();
#endif
                assignedAny = false;
                return false;
            }

#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
            using (ParentFastPathMarker.Auto())
#endif
            {
                List<Component> parents = ParentComponentFastInvoker.Collect(
                    root,
                    metadata.elementType,
                    attribute.IncludeInactive
                );
                try
                {
                    int count = FilterComponentsInPlace(
                        parents,
                        filters,
                        attribute,
                        metadata.elementType,
                        metadata.isInterface,
                        filterDisabledComponents: false
                    );
                    assignedAny = AssignParentComponentsFromList(
                        component,
                        metadata,
                        parents,
                        count
                    );
                    return true;
                }
                finally
                {
                    ParentComponentFastInvoker.Release(parents);
                }
            }
        }

        private static bool AssignParentComponentsFromList(
            Component component,
            FieldMetadata<ParentComponentAttribute> metadata,
            List<Component> parents,
            int count
        )
        {
            if (parents == null || count < 0)
            {
                count = 0;
            }
            else
            {
                int parentCount = parents.Count;
                if (parentCount < count)
                {
                    count = parentCount;
                }
            }

            switch (metadata.kind)
            {
                case FieldKind.Array:
                {
                    Array array = CreateTypedArray(metadata.elementType, parents, count);
                    metadata.SetValue(component, array);
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
                        list.Add(parents[i]);
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
                        metadata.hashSetAdder(hashSet, parents[i]);
                    }

                    return 0 < count;
                }
                default:
                {
                    return false;
                }
            }
        }

        private static int CollectParentComponents(
            Transform root,
            FieldMetadata<ParentComponentAttribute> metadata,
            FilterParameters filters,
            List<Component> results
        )
        {
            ParentComponentAttribute attribute = metadata.attribute;
            if (attribute.MaxCount == 1)
            {
                if (
                    TryGetFirstParentComponent(
                        root,
                        filters,
                        metadata.elementType,
                        attribute,
                        metadata.isInterface,
                        out Component match
                    )
                )
                {
                    results.Add(match);
                    return 1;
                }
                return 0;
            }
            int maximumDepth = GetMaximumDepthFromSearchRoot(attribute);
            int maximumCount = 0 < attribute.MaxCount ? attribute.MaxCount : int.MaxValue;
            bool limitMatches = maximumCount != int.MaxValue;
            int count = 0;
            int depth = 0;
            Transform current = root;
            using PooledResource<List<Component>> componentLease = Buffers<Component>.List.Get(
                out List<Component> components
            );
            while (current != null && depth <= maximumDepth)
            {
                GetComponentsOfType(
                    current,
                    metadata.elementType,
                    metadata.isInterface,
                    attribute.AllowInterfaces,
                    components
                );
                if (limitMatches)
                {
                    foreach (Component candidate in components)
                    {
                        if (
                            !PassesStateAndFilters(
                                candidate,
                                filters,
                                filterDisabledComponents: false
                            )
                        )
                        {
                            continue;
                        }
                        results.Add(candidate);
                        ++count;
                        if (maximumCount <= count)
                        {
                            return count;
                        }
                    }
                }
                else
                {
                    results.AddRange(components);
                }
                if (depth == maximumDepth)
                {
                    break;
                }
                current = current.parent;
                ++depth;
            }
            return limitMatches
                ? count
                : FilterComponentsInPlace(
                    results,
                    filters,
                    attribute,
                    metadata.elementType,
                    metadata.isInterface,
                    filterDisabledComponents: false
                );
        }

        private static bool TryGetFirstParentComponent(
            Transform root,
            FilterParameters filters,
            Type elementType,
            ParentComponentAttribute attribute,
            bool isInterface,
            out Component result
        )
        {
            Transform current = root;
            int depth = 0;
            int maxDepth = GetMaximumDepthFromSearchRoot(attribute);

            bool needsScratch = isInterface || filters.RequiresPostProcessing;
            List<Component> components = null;
            PooledResource<List<Component>> scratch = default;
            if (needsScratch)
            {
                scratch = Buffers<Component>.List.Get(out components);
            }

            try
            {
                while (current != null && depth <= maxDepth)
                {
                    if (
                        TryResolveSingleComponent(
                            current,
                            filters,
                            elementType,
                            isInterface,
                            attribute.AllowInterfaces,
                            components,
                            out Component resolved,
                            filterDisabledComponents: false
                        )
                    )
                    {
                        result = resolved;
                        return true;
                    }

                    current = current.parent;
                    ++depth;
                }
            }
            finally
            {
                if (needsScratch)
                {
                    scratch.Dispose();
                }
            }

            result = null;
            return false;
        }

        private static int GetMaximumDepthFromSearchRoot(ParentComponentAttribute attribute)
        {
            if (attribute.MaxDepth == 0)
            {
                return int.MaxValue;
            }
            return attribute.MaxDepth - (attribute.OnlyAncestors ? 1 : 0);
        }
    }

    internal static class ParentComponentFastInvoker
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
                _ = collector.CollectParentsInto(component, includeInactive, results);
                return results;
            }

            // The non-generic query remains usable when IL2CPP cannot instantiate the generic collector.
            Component[] matches = component.GetComponentsInParent(elementType, includeInactive);
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
