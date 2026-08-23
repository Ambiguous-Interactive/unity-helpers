// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.Scripting;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Utils;
    using static RelationalComponentProcessor;

    /// <summary>
    /// Automatically assigns sibling components (components on the same <see cref="GameObject"/>) to the decorated field.
    /// Supports single components, <see cref="System.Array"/>s, <see cref="System.Collections.Generic.List{T}"/>,
    /// and <see cref="System.Collections.Generic.HashSet{T}"/> collection types.
    /// </summary>
    /// <remarks>
    /// Call <see cref="SiblingComponentExtensions.AssignSiblingComponents"/> (or
    /// <see cref="RelationalComponentExtensions.AssignRelationalComponents(UnityEngine.Component)"/>) to populate the field.
    /// This is typically done in <c>Awake()</c> or <c>OnEnable()</c>.
    ///
    /// Use optional filters to refine results: <see cref="BaseRelationalComponentAttribute.TagFilter"/> (by tag),
    /// <see cref="BaseRelationalComponentAttribute.NameFilter"/> (substring match on name), and
    /// <see cref="BaseRelationalComponentAttribute.IncludeInactive"/> (include disabled/inactive components).
    ///
    /// IMPORTANT: This attribute populates fields at runtime, not during Unity serialization in Edit mode.
    /// Fields populated by this attribute will not be serialized by Unity.
    ///
    /// <seealso cref="BaseRelationalComponentAttribute"/>
    /// <seealso cref="SiblingComponentExtensions.AssignSiblingComponents(UnityEngine.Component)"/>
    /// <seealso cref="RelationalComponentExtensions.AssignRelationalComponents(UnityEngine.Component)"/>
    /// </remarks>
    /// <example>
    /// Assign common sibling components with filters and collections:
    /// <code><![CDATA[
    /// using UnityEngine;
    /// using WallstopStudios.UnityHelpers.Core.Attributes;
    ///
    /// public class Enemy : MonoBehaviour
    /// {
    ///     // Single assignment (required by default)
    ///     [SiblingComponent] private Animator animator;
    ///
    ///     // Optional – do not log an error if not present
    ///     [SiblingComponent(Optional = true)] private Rigidbody2D rb;
    ///
    ///     // Multiple results – collect all on the same GameObject
    ///     [SiblingComponent] private List<Collider2D> allSiblingColliders;
    ///
    ///     // Filter by tag and name substring
    ///     [SiblingComponent(TagFilter = "Visual", NameFilter = "Sprite")]
    ///     private Component[] visualComponents;
    ///
    ///     private void Awake()
    ///     {
    ///         this.AssignSiblingComponents();
    ///         // or: this.AssignRelationalComponents();
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    [AttributeUsage(AttributeTargets.Field)]
    [Preserve]
    public sealed class SiblingComponentAttribute : BaseRelationalComponentAttribute { }

    public static class SiblingComponentExtensions
    {
        private static readonly Dictionary<
            Type,
            FieldMetadata<SiblingComponentAttribute>[]
        > FieldsByType = new();

        /// <summary>
        /// Assigns fields on <paramref name="component"/> marked with <see cref="SiblingComponentAttribute"/>.
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
        ///     this.AssignSiblingComponents();
        /// }
        /// ]]></code>
        /// </example>
        public static void AssignSiblingComponents(this Component component)
        {
            // Match AssignRelationalComponents: skip a null/destroyed component (also stops a leaked
            // test coroutine from re-logging on an already-destroyed tester; see AssignChildComponents).
            if (component == null)
            {
                return;
            }

            FieldMetadata<SiblingComponentAttribute>[] fields = FieldsByType.GetOrAdd(
                component.GetType(),
                static type => GetFieldMetadata<SiblingComponentAttribute>(type)
            );
            AssignSiblingComponents(component, fields);
        }

        internal static void AssignSiblingComponents(
            Component component,
            FieldMetadata<SiblingComponentAttribute>[] fields
        )
        {
            if (component == null || fields == null || fields.Length == 0)
            {
                return;
            }

            foreach (FieldMetadata<SiblingComponentAttribute> metadata in fields)
            {
                if (ShouldSkipAssignment(metadata, component))
                {
                    continue;
                }

                bool foundSibling;

                if (metadata.kind == FieldKind.Single)
                {
                    foundSibling = TryAssignSingleSibling(component, metadata);
                }
                else
                {
                    FilterParameters filters = metadata.Filters;
                    if (
                        !metadata.isInterface
                        && !filters.RequiresPostProcessing
                        && metadata.attribute.MaxCount <= 0
                    )
                    {
                        foundSibling = TryAssignSiblingCollectionFast(component, metadata);
                    }
                    else
                    {
                        switch (metadata.kind)
                        {
                            case FieldKind.Array:
                            {
                                using PooledResource<List<Component>> componentBuffer =
                                    Buffers<Component>.List.Get(out List<Component> components);
                                GetComponentsOfType(
                                    component,
                                    metadata.elementType,
                                    metadata.isInterface,
                                    metadata.attribute.AllowInterfaces,
                                    components
                                );

                                int filteredCount =
                                    !filters.RequiresPostProcessing
                                    && metadata.attribute.MaxCount <= 0
                                        ? components.Count
                                        : FilterComponentsInPlace(
                                            components,
                                            filters,
                                            metadata.attribute,
                                            metadata.elementType,
                                            metadata.isInterface
                                        );

                                metadata.SetValue(
                                    component,
                                    CreateTypedArray(
                                        metadata.elementType,
                                        components,
                                        filteredCount
                                    )
                                );
                                foundSibling = filteredCount > 0;
                                break;
                            }
                            case FieldKind.List:
                            {
                                using PooledResource<List<Component>> componentBuffer =
                                    Buffers<Component>.List.Get(out List<Component> components);
                                GetComponentsOfType(
                                    component,
                                    metadata.elementType,
                                    metadata.isInterface,
                                    metadata.attribute.AllowInterfaces,
                                    components
                                );

                                int filteredCount =
                                    !filters.RequiresPostProcessing
                                    && metadata.attribute.MaxCount <= 0
                                        ? components.Count
                                        : FilterComponentsInPlace(
                                            components,
                                            filters,
                                            metadata.attribute,
                                            metadata.elementType,
                                            metadata.isInterface
                                        );

                                object existing = metadata.GetValue(component);
                                if (existing is IList instance)
                                {
                                    instance.Clear();
                                }
                                else
                                {
                                    instance = metadata.listCreator(filteredCount);
                                    metadata.SetValue(component, instance);
                                }
                                for (int i = 0; i < filteredCount; ++i)
                                {
                                    instance.Add(components[i]);
                                }

                                foundSibling = filteredCount > 0;
                                break;
                            }
                            case FieldKind.HashSet:
                            {
                                using PooledResource<List<Component>> componentBuffer =
                                    Buffers<Component>.List.Get(out List<Component> components);
                                GetComponentsOfType(
                                    component,
                                    metadata.elementType,
                                    metadata.isInterface,
                                    metadata.attribute.AllowInterfaces,
                                    components
                                );

                                int filteredCount =
                                    !filters.RequiresPostProcessing
                                    && metadata.attribute.MaxCount <= 0
                                        ? components.Count
                                        : FilterComponentsInPlace(
                                            components,
                                            filters,
                                            metadata.attribute,
                                            metadata.elementType,
                                            metadata.isInterface
                                        );

                                object instance = metadata.GetValue(component);
                                if (instance != null && metadata.hashSetClearer != null)
                                {
                                    metadata.hashSetClearer(instance);
                                }
                                else
                                {
                                    instance = metadata.hashSetCreator(filteredCount);
                                    metadata.SetValue(component, instance);
                                }
                                for (int i = 0; i < filteredCount; ++i)
                                {
                                    metadata.hashSetAdder(instance, components[i]);
                                }

                                foundSibling = filteredCount > 0;
                                break;
                            }
                            default:
                            {
                                foundSibling = TryAssignSingleSibling(component, metadata);
                                break;
                            }
                        }
                    }
                }

                if (!foundSibling)
                {
                    LogMissingComponentError(component, metadata, "sibling");
                    AssignNullToSingleField(component, metadata);
                }
            }
        }

        internal static FieldMetadata<SiblingComponentAttribute>[] GetOrCreateFields(Type type)
        {
            return FieldsByType.GetOrAdd(
                type,
                static t => GetFieldMetadata<SiblingComponentAttribute>(t)
            );
        }

        private static bool TryAssignSingleSibling(
            Component component,
            FieldMetadata<SiblingComponentAttribute> metadata
        )
        {
            SiblingComponentAttribute attribute = metadata.attribute;

            if (metadata.isInterface && !attribute.AllowInterfaces)
            {
                return false;
            }

            bool hasSimpleFilters =
                attribute.IncludeInactive
                && attribute.TagFilter == null
                && attribute.NameFilter == null;

            if (!metadata.isInterface && hasSimpleFilters)
            {
                if (component.TryGetComponent(metadata.elementType, out Component sibling))
                {
                    metadata.SetValue(component, sibling);
                    return true;
                }
                return false;
            }

            FilterParameters filters = new(attribute);
            if (
                TryResolveSingleComponent(
                    component,
                    filters,
                    metadata.elementType,
                    metadata.isInterface,
                    attribute.AllowInterfaces,
                    null,
                    out Component resolved
                )
            )
            {
                metadata.SetValue(component, resolved);
                return true;
            }
            return false;
        }

        private static bool TryAssignSiblingCollectionFast(
            Component component,
            FieldMetadata<SiblingComponentAttribute> metadata
        )
        {
            List<Component> matches = SiblingComponentFastInvoker.Collect(
                component,
                metadata.elementType
            );
            try
            {
                return AssignComponentsFromList(component, metadata, matches);
            }
            finally
            {
                SiblingComponentFastInvoker.Release(matches);
            }
        }

        private static bool AssignComponentsFromList(
            Component component,
            FieldMetadata<SiblingComponentAttribute> metadata,
            List<Component> components
        )
        {
            int count = components == null ? 0 : components.Count;

            switch (metadata.kind)
            {
                case FieldKind.Array:
                {
                    metadata.SetValue(
                        component,
                        CreateTypedArray(metadata.elementType, components, count)
                    );
                    return count > 0;
                }
                case FieldKind.List:
                {
                    if (metadata.GetValue(component) is IList instance)
                    {
                        instance.Clear();
                    }
                    else
                    {
                        instance = metadata.listCreator(count);
                        metadata.SetValue(component, instance);
                    }

                    for (int i = 0; i < count; ++i)
                    {
                        instance.Add(components[i]);
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
                        metadata.hashSetAdder(hashSet, components[i]);
                    }

                    return count > 0;
                }
                default:
                {
                    return TryAssignSingleSibling(component, metadata);
                }
            }
        }
    }

    internal static class SiblingComponentFastInvoker
    {
        // Handed out and detached, so a re-entrant call gets its own list rather than refilling the
        // one its caller is still reading. Re-entry is not hypothetical: a consumer's Equals or
        // GetHashCode override runs inside a HashSet field's adds. Release puts the list back.
        // Reused rather than pool-leased because the lease measured more per call than the
        // allocation it removed; [ThreadStatic] keeps that safe off the main thread too.
        [ThreadStatic]
        private static List<Component> Scratch;

        internal static List<Component> Collect(Component component, Type elementType)
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

            // AOT-safe: the non-generic Type overload avoids the runtime generic-method +
            // Expression.Compile path, which IL2CPP cannot service (the old compiled path threw
            // at runtime in player builds). The List overload of the same query fills a caller
            // buffer instead of allocating an array per call.
            results.Clear();
            component.GetComponents(elementType, results);
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
