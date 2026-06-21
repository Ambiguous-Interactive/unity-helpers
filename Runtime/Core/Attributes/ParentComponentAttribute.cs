// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Extension;
    using UnityEngine;
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
    /// Call <see cref="ParentComponentExtensions.AssignParentComponents"/> (or
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
            FieldMetadata<ParentComponentAttribute>[] fields = FieldsByType.GetOrAdd(
                component.GetType(),
                type => GetFieldMetadata<ParentComponentAttribute>(type)
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
                            TryAssignParentSingleFast(
                                root,
                                field,
                                filters,
                                out Component parentComponent
                            )
                            || TryGetFirstParentComponent(
                                root,
                                filters,
                                field.elementType,
                                field.attribute,
                                field.isInterface,
                                out parentComponent
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

                                Array correctTypedArray = field.arrayCreator(filteredCount);
                                for (int i = 0; i < filteredCount; ++i)
                                {
                                    correctTypedArray.SetValue(parentComponents[i], i);
                                }

                                field.SetValue(component, correctTypedArray);
                                foundParent = filteredCount > 0;
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

                                foundParent = filteredCount > 0;
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

                                foundParent = filteredCount > 0;
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
            return FieldsByType.GetOrAdd(type, t => GetFieldMetadata<ParentComponentAttribute>(t));
        }

        private static bool TryAssignParentCollectionFast(
            Component component,
            Transform root,
            FieldMetadata<ParentComponentAttribute> metadata,
            FilterParameters filters,
            out bool assignedAny
        )
        {
            assignedAny = false;
            ParentComponentAttribute attribute = metadata.attribute;
            if (
                metadata.isInterface
                || filters.RequiresPostProcessing
                || attribute.MaxDepth > 0
                || root == null
            )
            {
#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
                ParentFallbackMarker.Begin();
                ParentFallbackMarker.End();
#endif
                return false;
            }

#if UNITY_EDITOR && UNITY_2020_2_OR_NEWER
            using (ParentFastPathMarker.Auto())
#endif
            {
                Array parents = ParentComponentFastInvoker.GetArray(
                    root,
                    metadata.elementType,
                    attribute.IncludeInactive
                );

                Array filtered = FilterParentArray(metadata, parents);
                assignedAny = AssignParentComponentsFromArray(component, metadata, filtered);
                return true;
            }
        }

        private static Array FilterParentArray(
            FieldMetadata<ParentComponentAttribute> metadata,
            Array source
        )
        {
            Type elementType = metadata.elementType;
            if (source == null || source.Length == 0)
            {
                return Array.CreateInstance(elementType, 0);
            }

            int maxCount = metadata.attribute.MaxCount;
            if (maxCount <= 0)
            {
                return source;
            }

            int limit = Math.Min(maxCount, source.Length);
            Array staged = Array.CreateInstance(elementType, limit);
            int writeIndex = 0;

            for (int i = 0; i < source.Length && writeIndex < limit; ++i)
            {
                Component candidate = source.GetValue(i) as Component;
                if (candidate == null)
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

        private static bool AssignParentComponentsFromArray(
            Component component,
            FieldMetadata<ParentComponentAttribute> metadata,
            Array parents
        )
        {
            if (parents == null)
            {
                parents = Array.CreateInstance(metadata.elementType, 0);
            }

            int count = parents.Length;

            switch (metadata.kind)
            {
                case FieldKind.Array:
                {
                    Array instance = metadata.arrayCreator(count);
                    for (int i = 0; i < count; ++i)
                    {
                        instance.SetValue(parents.GetValue(i), i);
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
                        list.Add(parents.GetValue(i));
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
                        metadata.hashSetAdder(hashSet, parents.GetValue(i));
                    }

                    return count > 0;
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
                int maxDepth = attribute.MaxDepth > 0 ? attribute.MaxDepth : int.MaxValue;

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
            if (attribute.MaxDepth > 0)
            {
                foreach (Component comp in allParents)
                {
                    int depth = GetDepthFromTransform(root, comp.transform);
                    // depth is steps from root: 0 = root itself, 1 = root.parent, etc.
                    // MaxDepth is how many levels to search, so depth should be < MaxDepth
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

        private static bool TryAssignParentSingleFast(
            Transform root,
            FieldMetadata<ParentComponentAttribute> metadata,
            FilterParameters filters,
            out Component parentComponent
        )
        {
            parentComponent = null;

            if (
                root == null
                || metadata.isInterface
                || filters.RequiresPostProcessing
                || metadata.attribute.IncludeInactive
                || metadata.attribute.MaxDepth > 0
            )
            {
                return false;
            }

            Component candidate = root.GetComponentInParent(metadata.elementType);
            if (candidate == null)
            {
                return false;
            }

            parentComponent = candidate;
            return true;
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
            int maxDepth = attribute.MaxDepth > 0 ? attribute.MaxDepth : int.MaxValue;

            bool needsScratch = isInterface || filters.RequiresPostProcessing;
            List<Component> components = null;
            PooledResource<List<Component>> scratch = default;
            if (needsScratch)
            {
                scratch = Buffers<Component>.List.Get(out components);
            }

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
                    if (needsScratch)
                    {
                        scratch.Dispose();
                    }
                    result = resolved;
                    return true;
                }

                current = current.parent;
                depth++;
            }

            if (needsScratch)
            {
                scratch.Dispose();
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
        internal static Array GetArray(Component component, Type elementType, bool includeInactive)
        {
            // AOT-safe: the non-generic Type overload avoids the runtime generic-method +
            // Expression.Compile path, which IL2CPP cannot service (the old compiled path threw
            // at runtime in player builds). GetComponentsInParent(Type, bool) returns only
            // elementType instances; copy them into a typed array the caller can assign.
            Component[] matches = component.GetComponentsInParent(elementType, includeInactive);
            Array typed = Array.CreateInstance(elementType, matches.Length);
            Array.Copy(matches, typed, matches.Length);
            return typed;
        }
    }
}
