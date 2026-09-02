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
        /// Depth 1 = immediate parent only, depth 2 = parent and grandparent, etc.
        /// </summary>
        /// <remarks>
        /// Negative values are treated as 0 (unlimited). The search proceeds from closest
        /// to most distant ancestors.
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
        private static readonly Dictionary<
            Type,
            FieldMetadata<ParentComponentAttribute>[]
        > FieldsByType = new();

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
            /*
                Match AssignRelationalComponents: skip a null/destroyed component (also stops a leaked
                test coroutine from re-logging on an already-destroyed tester; see AssignChildComponents).
            */
            if (component == null)
            {
                return;
            }

            FieldMetadata<ParentComponentAttribute>[] fields = FieldsByType.GetOrAdd(
                component.GetType(),
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
                        /*
                            No GetComponentInParent fast path in front of this walk: the one that
                            was here could never run, because its gate refused both halves of an
                            exhaustive pair. Reinstating one is a behaviour change that wants
                            measurement (#644).
                        */
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
                        switch (field.kind)
                        {
                            case FieldKind.Array:
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
                                    break;
                                }

                                using PooledResource<List<Component>> parentComponentBuffer =
                                    Buffers<Component>.List.Get(
                                        out List<Component> parentComponents
                                    );
                                GetParentComponents(
                                    root,
                                    field.elementType,
                                    field.attribute,
                                    field.isInterface,
                                    parentComponents
                                );

                                int filteredCount =
                                    !filters.RequiresPostProcessing && field.attribute.MaxCount <= 0
                                        ? parentComponents.Count
                                        : FilterComponentsInPlace(
                                            parentComponents,
                                            filters,
                                            field.attribute,
                                            field.elementType,
                                            field.isInterface,
                                            filterDisabledComponents: false
                                        );

                                field.SetValue(
                                    component,
                                    CreateTypedArray(
                                        field.elementType,
                                        parentComponents,
                                        filteredCount
                                    )
                                );
                                foundParent = 0 < filteredCount;
                                break;
                            }
                            case FieldKind.List:
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
                                    break;
                                }

                                using PooledResource<List<Component>> parentComponentBuffer =
                                    Buffers<Component>.List.Get(
                                        out List<Component> parentComponents
                                    );
                                GetParentComponents(
                                    root,
                                    field.elementType,
                                    field.attribute,
                                    field.isInterface,
                                    parentComponents
                                );

                                int filteredCount =
                                    !filters.RequiresPostProcessing && field.attribute.MaxCount <= 0
                                        ? parentComponents.Count
                                        : FilterComponentsInPlace(
                                            parentComponents,
                                            filters,
                                            field.attribute,
                                            field.elementType,
                                            field.isInterface,
                                            filterDisabledComponents: false
                                        );

                                if (field.GetValue(component) is IList instance)
                                {
                                    instance.Clear();
                                }
                                else
                                {
                                    instance = field.listCreator(filteredCount);
                                    field.SetValue(component, instance);
                                }

                                for (int i = 0; i < filteredCount; ++i)
                                {
                                    instance.Add(parentComponents[i]);
                                }

                                foundParent = 0 < filteredCount;
                                break;
                            }
                            case FieldKind.HashSet:
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
                                    break;
                                }

                                using PooledResource<List<Component>> parentComponentBuffer =
                                    Buffers<Component>.List.Get(
                                        out List<Component> parentComponents
                                    );
                                GetParentComponents(
                                    root,
                                    field.elementType,
                                    field.attribute,
                                    field.isInterface,
                                    parentComponents
                                );

                                int filteredCount = FilterComponentsInPlace(
                                    parentComponents,
                                    filters,
                                    field.attribute,
                                    field.elementType,
                                    field.isInterface,
                                    filterDisabledComponents: false
                                );

                                object instance = field.GetValue(component);
                                if (instance != null && field.hashSetClearer != null)
                                {
                                    field.hashSetClearer(instance);
                                }
                                else
                                {
                                    instance = field.hashSetCreator(filteredCount);
                                    field.SetValue(component, instance);
                                }

                                for (int i = 0; i < filteredCount; ++i)
                                {
                                    field.hashSetAdder(instance, parentComponents[i]);
                                }

                                foundParent = 0 < filteredCount;
                                break;
                            }
                            default:
                            {
                                foundParent = false;
                                break;
                            }
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
                || 0 < attribute.MaxDepth
                || root == null
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
                    int count = FilterParents(metadata, parents);
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

        /// <summary>
        /// Applies <see cref="BaseRelationalComponentAttribute.MaxCount"/> in place and reports how
        /// many leading entries of <paramref name="source"/> survive.
        /// </summary>
        /// <remarks>
        /// Compaction only runs when a count limit is set, which is what the array-building version
        /// of this filter did: without a limit it returned its input untouched, destroyed entries
        /// included.
        /// </remarks>
        private static int FilterParents(
            FieldMetadata<ParentComponentAttribute> metadata,
            List<Component> source
        )
        {
            if (source == null || source.Count == 0)
            {
                return 0;
            }

            int maxCount = metadata.attribute.MaxCount;
            if (maxCount <= 0)
            {
                return source.Count;
            }

            int limit = Math.Min(maxCount, source.Count);
            int writeIndex = 0;

            for (int i = 0; i < source.Count && writeIndex < limit; ++i)
            {
                Component candidate = source[i];
                if (candidate == null)
                {
                    continue;
                }

                source[writeIndex++] = candidate;
            }

            return writeIndex;
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
            else if (parents.Count < count)
            {
                count = parents.Count;
            }

            switch (metadata.kind)
            {
                case FieldKind.Array:
                {
                    metadata.SetValue(
                        component,
                        CreateTypedArray(metadata.elementType, parents, count)
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

        private static List<Component> GetParentComponents(
            Transform root,
            Type elementType,
            ParentComponentAttribute attribute,
            bool isInterface,
            List<Component> buffer
        )
        {
            buffer.Clear();
            if (isInterface && attribute.AllowInterfaces)
            {
                // For interfaces, we need to manually traverse the hierarchy
                Transform current = root;
                int depth = 0;
                int maxDepth = 0 < attribute.MaxDepth ? attribute.MaxDepth : int.MaxValue;

                using PooledResource<List<Component>> parentComponentBuffer =
                    Buffers<Component>.List.Get(out List<Component> components);
                while (current != null && depth < maxDepth)
                {
                    GetComponentsOfType(
                        current,
                        elementType,
                        isInterface,
                        attribute.AllowInterfaces,
                        components
                    );
                    buffer.AddRange(components);

                    current = current.parent;
                    depth++;
                }

                return buffer;
            }

            // Use Unity's built-in method for concrete types
            Component[] allParents = root.GetComponentsInParent(
                elementType,
                includeInactive: attribute.IncludeInactive
            );

            // Filter by depth if needed
            if (0 < attribute.MaxDepth)
            {
                foreach (Component comp in allParents)
                {
                    int depth = GetDepthFromTransform(root, comp.transform);
                    /*
                        depth is steps from root: 0 = root itself, 1 = root.parent, etc.
                        MaxDepth is how many levels to search, so depth should be < MaxDepth
                    */
                    if (depth < attribute.MaxDepth)
                    {
                        buffer.Add(comp);
                    }
                }
            }
            else
            {
                buffer.AddRange(allParents);
            }

            return buffer;
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
            int maxDepth = 0 < attribute.MaxDepth ? attribute.MaxDepth : int.MaxValue;

            bool needsScratch = isInterface || filters.RequiresPostProcessing;
            List<Component> components = null;
            PooledResource<List<Component>> scratch = default;
            if (needsScratch)
            {
                scratch = Buffers<Component>.List.Get(out components);
            }

            try
            {
                while (current != null && depth < maxDepth)
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
                    depth++;
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

        private static int GetDepthFromTransform(Transform start, Transform target)
        {
            int depth = 0;
            Transform current = start;
            while (current != null && current != target)
            {
                current = current.parent;
                depth++;
            }
            return current == target ? depth : int.MaxValue;
        }
    }

    internal static class ParentComponentFastInvoker
    {
        /*
            Handed out and detached, so a re-entrant call gets its own list rather than refilling the
            one its caller is still reading. Re-entry is not hypothetical: a consumer's Equals or
            GetHashCode override runs inside a HashSet field's adds. Release puts the list back.
            Reused rather than pool-leased because the lease measured more per call than the
            allocation it removed; [ThreadStatic] keeps that safe off the main thread too.
            Each family owns its own buffer, so the three sequential passes of
            AssignRelationalComponents cannot collide either.
        */
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

            /*
                The non-generic Type overload has no caller-buffer sibling, so it allocates a
                Component[] on every assignment. Closing the generic query over the element type
                fills a reused buffer instead; a runtime that refuses that instantiation falls back
                here permanently rather than per call.
            */
            RelationalComponentCollector collector = RelationalComponentCollector.For(
                elementType,
                component
            );
            if (collector != null)
            {
                _ = collector.CollectParentsInto(component, includeInactive, results);
                return results;
            }

            /*
                AOT-safe fallback: the non-generic Type overload avoids the runtime generic-method +
                Expression.Compile path, which IL2CPP cannot service (the old compiled path threw
                at runtime in player builds).
            */
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
