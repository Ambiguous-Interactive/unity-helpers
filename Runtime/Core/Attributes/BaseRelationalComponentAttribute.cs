// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using Extension;
    using Helper;
    using Tags;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Base class for relational component attributes that provides common functionality
    /// for finding and assigning components based on hierarchy relationships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Used by <see cref="ParentComponentAttribute"/>, <see cref="SiblingComponentAttribute"/>, and
    /// <see cref="ChildComponentAttribute"/> to control search behavior, filtering, and assignment.
    /// </para>
    ///
    /// <para><b>Available Properties</b></para>
    /// <list type="bullet">
    /// <item><see cref="Optional"/> - Treat fields as required (false) or optional (true)</item>
    /// <item><see cref="IncludeInactive"/> - Include/exclude disabled components or inactive GameObjects</item>
    /// <item><see cref="SkipIfAssigned"/> - Skip assigning when a field is already populated</item>
    /// <item><see cref="MaxCount"/> - Limit results for collections (ignored for single fields)</item>
    /// <item><see cref="TagFilter"/> - Filter by tag (exact match)</item>
    /// <item><see cref="NameFilter"/> - Filter by name (substring match)</item>
    /// <item><see cref="AllowInterfaces"/> - Allow interface/base-type searches</item>
    /// </list>
    ///
    /// <para><b>Filter Interactions</b></para>
    /// <list type="bullet">
    /// <item><c>TagFilter</c> and <c>NameFilter</c> can be combined - both must match (AND logic)</item>
    /// <item>When <c>IncludeInactive</c> is false, filters are applied AFTER excluding inactive components</item>
    /// <item><c>MaxCount</c> is applied last, after all other filters</item>
    /// </list>
    ///
    /// <para><b>Parameter Validation</b></para>
    /// <list type="bullet">
    /// <item><c>MaxCount</c>: Negative values are treated as 0 (unlimited)</item>
    /// <item><c>MaxDepth</c> (Parent/Child only): Negative values are treated as 0 (unlimited)</item>
    /// </list>
    ///
    /// <para><b>Notes</b></para>
    /// <list type="bullet">
    /// <item>Tag filtering uses <see cref="GameObject.CompareTag(string)"/> for efficient exact matches</item>
    /// <item>Name filtering performs a case-sensitive substring match on <see cref="UnityEngine.Object.name"/></item>
    /// <item>When <see cref="IncludeInactive"/> is false, only enabled components on active-in-hierarchy GameObjects are considered</item>
    /// <item>For single fields, <see cref="MaxCount"/> has no effect</item>
    /// </list>
    /// </remarks>
    public abstract class BaseRelationalComponentAttribute : System.Attribute
    {
        /// <summary>
        /// When true, no error is logged when a matching component cannot be found.
        /// When false (default), a descriptive error is logged identifying the field and expected type.
        /// </summary>
        public bool Optional { get; set; } = false;

        /// <summary>
        /// When true (default), includes disabled <see cref="Behaviour"/>s and components on inactive GameObjects.
        /// When false, only enabled components on active-in-hierarchy GameObjects are assigned --
        /// except for <see cref="ParentComponentAttribute"/>, where this gates only
        /// <c>GameObject.activeInHierarchy</c> and a disabled ancestor component is still assigned.
        /// </summary>
        public bool IncludeInactive { get; set; } = true;

        /// <summary>
        /// When true, skips assignment if the field already has a non-null value (for single components)
        /// or a non-empty collection (for arrays/lists). Default: false.
        /// Useful to avoid stomping values set manually or from prior initialization.
        /// </summary>
        public bool SkipIfAssigned { get; set; } = false;

        /// <summary>
        /// Maximum number of components to assign to collection fields. 0 means unlimited (default).
        /// Applies to arrays, lists, and hash sets. Ignored for single component fields.
        /// </summary>
        /// <remarks>
        /// Negative values are treated as 0 (unlimited). For single-field assignments, this property
        /// has no effect since only one component can be assigned.
        /// </remarks>
        public int MaxCount
        {
            get => _maxCount;
            set => _maxCount = value < 0 ? 0 : value;
        }
        private int _maxCount;

        /// <summary>
        /// If set, only finds components on GameObjects with this tag.
        /// Uses <see cref="GameObject.CompareTag(string)"/> for matching.
        /// </summary>
        public string TagFilter { get; set; } = null;

        /// <summary>
        /// If set, only finds components on GameObjects whose names contain this string (case-sensitive substring).
        /// </summary>
        public string NameFilter { get; set; } = null;

        /// <summary>
        /// When true (default), allows searching by interface or base type and resolves matching components.
        /// Set to false to restrict assignment to exact concrete component types only.
        /// </summary>
        public bool AllowInterfaces { get; set; } = true;
    }

    /// <summary>
    /// Shared infrastructure for relational component attribute processing.
    /// </summary>
    internal static class RelationalComponentProcessor
    {
        private static readonly MethodInfo CreateFieldAccessorGenericMethod =
            typeof(RelationalComponentProcessor).GetMethod(
                nameof(CreateFieldAccessorGeneric),
                BindingFlags.NonPublic | BindingFlags.Static
            );

        /// <summary>
        /// Largest backing array a reused scratch list keeps between calls.
        /// </summary>
        /// <remarks>
        /// Matches the bound the pooled JSON reads use. A one-off huge hierarchy query must not
        /// leave its backing array parked for the lifetime of the process.
        /// </remarks>
        internal const int MaximumRetainedScratchCapacity = 4_096;

        private static FieldKind MapFieldKind(AttributeMetadataCache.FieldKind cacheKind)
        {
            return cacheKind switch
            {
#pragma warning disable CS0618
                AttributeMetadataCache.FieldKind.None => FieldKind.Single,
#pragma warning restore CS0618
                AttributeMetadataCache.FieldKind.Single => FieldKind.Single,
                AttributeMetadataCache.FieldKind.Array => FieldKind.Array,
                AttributeMetadataCache.FieldKind.List => FieldKind.List,
                AttributeMetadataCache.FieldKind.HashSet => FieldKind.HashSet,
                _ => FieldKind.Single,
            };
        }

        /// <summary>
        /// Picks between the element type the cache recorded and the one this field's own type
        /// gives, preferring the cache except where it cannot be describing this field.
        /// </summary>
        /// <remarks>
        /// The cache is keyed by component type and field name, so two same-named fields of
        /// different types share one slot and the later write wins. A recorded type unrelated to
        /// the live field's is therefore the other field's, and following it would search for the
        /// wrong component type.
        /// </remarks>
        private static Type ChooseElementType(Type cached, Type live, Type fieldType)
        {
            if (cached == null)
            {
                return live ?? fieldType;
            }

            if (live == null)
            {
                return cached;
            }

            bool related =
                cached == live || cached.IsAssignableFrom(live) || live.IsAssignableFrom(cached);
            return related ? cached : live;
        }

        private static FieldKind GetFieldKind(Type fieldType, out Type elementType)
        {
            if (fieldType == null)
            {
                elementType = null;
                return FieldKind.Single;
            }

            if (fieldType.IsArray)
            {
                elementType = fieldType.GetElementType();
                return FieldKind.Array;
            }

            if (fieldType.IsGenericType)
            {
                Type genericType = fieldType.GetGenericTypeDefinition();
                if (genericType == typeof(List<>))
                {
                    elementType = fieldType.GenericTypeArguments[0];
                    return FieldKind.List;
                }

                if (genericType == typeof(HashSet<>))
                {
                    elementType = fieldType.GenericTypeArguments[0];
                    return FieldKind.HashSet;
                }
            }

            elementType = fieldType;
            return FieldKind.Single;
        }

        private static FieldAccessor CreateFieldAccessor(Type componentType, FieldInfo field)
        {
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return FieldAccessor.Null;
            }

            // Use the field declaration type so inherited fields share a compiled accessor.
            Type declaringType = field.DeclaringType;
            Type accessorComponentType =
                declaringType != null && typeof(Component).IsAssignableFrom(declaringType)
                    ? declaringType
                    : componentType;

            MethodInfo generic = CreateFieldAccessorGenericMethod.MakeGenericMethod(
                accessorComponentType,
                field.FieldType
            );
            return (FieldAccessor)generic.Invoke(null, new object[] { field });
        }

        private static FieldAccessor CreateFieldAccessorGeneric<TComponent, TValue>(FieldInfo field)
            where TComponent : Component
        {
            return new FieldAccessor<TComponent, TValue>(field);
        }

        /// <summary>
        /// Builds the typed array a collection field of <paramref name="elementType"/> holds, taking
        /// the first <paramref name="count"/> entries of <paramref name="source"/>.
        /// </summary>
        internal static Array CreateTypedArray(Type elementType, List<Component> source, int count)
        {
            return ReflectionHelpers.CreateTypedArray(elementType, source, count);
        }

        internal static FieldMetadata<TAttribute>[] GetFieldMetadata<TAttribute>(Type componentType)
            where TAttribute : BaseRelationalComponentAttribute
        {
            AttributeMetadataCache cache = AttributeMetadataCache.Instance;
            AttributeMetadataCache.RelationalAttributeKind targetKind =
                GetRelationalKind<TAttribute>();

            if (
                cache != null
                && cache.TryGetRelationalFields(
                    componentType,
                    out AttributeMetadataCache.RelationalFieldMetadata[] cachedFields
                )
            )
            {
                using PooledResource<List<FieldMetadata<TAttribute>>> resultBuffer = Buffers<
                    FieldMetadata<TAttribute>
                >.List.Get(out List<FieldMetadata<TAttribute>> result);

                // Private base and derived fields can share a name; only live FieldInfo identifies the declaration.
                using PooledResource<HashSet<string>> namesLease = Buffers<string>.HashSet.Get(
                    out HashSet<string> namesForKind
                );
                foreach (AttributeMetadataCache.RelationalFieldMetadata cachedField in cachedFields)
                {
                    if (cachedField.attributeKind == targetKind)
                    {
                        namesForKind.Add(cachedField.fieldName);
                    }
                }

                foreach (
                    FieldInfo field in ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(
                        componentType
                    )
                )
                {
                    if (!namesForKind.Contains(field.Name))
                    {
                        continue;
                    }

                    if (!field.IsAttributeDefined(out TAttribute attribute, inherit: false))
                    {
                        continue;
                    }

                    if (!cache.TryGetElementType(componentType, field.Name, out Type elementType))
                    {
                        continue;
                    }

                    FieldKind kind = GetFieldKind(field.FieldType, out Type actualElementType);

                    Type resolvedElementType = ChooseElementType(
                        elementType,
                        actualElementType,
                        field.FieldType
                    );

                    if (
                        kind == FieldKind.HashSet
                        && field.FieldType.IsGenericType
                        && field.FieldType.GetGenericTypeDefinition() == typeof(HashSet<>)
                        && resolvedElementType == field.FieldType
                    )
                    {
                        resolvedElementType = field.FieldType.GenericTypeArguments[0];
                    }

                    Func<int, Array> arrayCreator = null;
                    Func<int, IList> listCreator = null;
                    Func<int, object> hashSetCreator = null;
                    Action<object, object> hashSetAdder = null;
                    Action<object> hashSetClearer = null;

                    switch (kind)
                    {
                        case FieldKind.Array:
                            arrayCreator = ReflectionHelpers.GetArrayCreator(resolvedElementType);
                            break;
                        case FieldKind.List:
                            listCreator = ReflectionHelpers.GetListWithCapacityCreator(
                                resolvedElementType
                            );
                            break;
                        case FieldKind.HashSet:
                            hashSetCreator = ReflectionHelpers.GetHashSetWithCapacityCreator(
                                resolvedElementType
                            );
                            hashSetAdder = ReflectionHelpers.GetHashSetAdder(resolvedElementType);
                            hashSetClearer = ReflectionHelpers.GetHashSetClearer(
                                resolvedElementType
                            );
                            break;
                    }

                    bool isInterface =
                        resolvedElementType != null
                        && (
                            resolvedElementType.IsInterface
                            || (
                                !resolvedElementType.IsSealed
                                && resolvedElementType != typeof(Component)
                            )
                        );

                    FilterParameters filters = new(attribute);

                    result.Add(
                        new FieldMetadata<TAttribute>(
                            field,
                            attribute,
                            filters,
                            CreateFieldAccessor(componentType, field),
                            kind,
                            resolvedElementType,
                            arrayCreator,
                            listCreator,
                            hashSetCreator,
                            hashSetAdder,
                            hashSetClearer,
                            isInterface
                        )
                    );
                }

                return result.ToArray();
            }

            FieldInfo[] fields = ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(
                componentType
            );

            using PooledResource<List<FieldMetadata<TAttribute>>> lease = Buffers<
                FieldMetadata<TAttribute>
            >.List.Get(out List<FieldMetadata<TAttribute>> results);

            foreach (FieldInfo field in fields)
            {
                if (!field.IsAttributeDefined(out TAttribute attribute, inherit: false))
                {
                    continue;
                }

                Type fieldType = field.FieldType;
                FieldKind kind = GetFieldKind(fieldType, out Type elementType);

                Func<int, Array> arrayCreator = null;
                Func<int, IList> listCreator = null;
                Func<int, object> hashSetCreator = null;
                Action<object, object> hashSetAdder = null;
                Action<object> hashSetClearer = null;

                switch (kind)
                {
                    case FieldKind.Array:
                        arrayCreator = ReflectionHelpers.GetArrayCreator(elementType);
                        break;
                    case FieldKind.List:
                        listCreator = ReflectionHelpers.GetListWithCapacityCreator(elementType);
                        break;
                    case FieldKind.HashSet:
                        hashSetCreator = ReflectionHelpers.GetHashSetWithCapacityCreator(
                            elementType
                        );
                        hashSetAdder = ReflectionHelpers.GetHashSetAdder(elementType);
                        hashSetClearer = ReflectionHelpers.GetHashSetClearer(elementType);
                        break;
                }

                bool isInterface =
                    elementType != null
                    && (
                        elementType.IsInterface
                        || (!elementType.IsSealed && elementType != typeof(Component))
                    );

                FilterParameters filters = new(attribute);

                results.Add(
                    new FieldMetadata<TAttribute>(
                        field,
                        attribute,
                        filters,
                        CreateFieldAccessor(componentType, field),
                        kind,
                        elementType,
                        arrayCreator,
                        listCreator,
                        hashSetCreator,
                        hashSetAdder,
                        hashSetClearer,
                        isInterface
                    )
                );
            }

            return results.ToArray();
        }

        private static AttributeMetadataCache.RelationalAttributeKind GetRelationalKind<TAttribute>()
            where TAttribute : BaseRelationalComponentAttribute
        {
            Type attributeType = typeof(TAttribute);

            if (attributeType == typeof(ParentComponentAttribute))
            {
                return AttributeMetadataCache.RelationalAttributeKind.Parent;
            }
            else if (attributeType == typeof(ChildComponentAttribute))
            {
                return AttributeMetadataCache.RelationalAttributeKind.Child;
            }
            else if (attributeType == typeof(SiblingComponentAttribute))
            {
                return AttributeMetadataCache.RelationalAttributeKind.Sibling;
            }

#pragma warning disable CS0618

            return AttributeMetadataCache.RelationalAttributeKind.Unknown;

#pragma warning restore CS0618
        }

        internal static bool ShouldSkipAssignment<TAttribute>(
            FieldMetadata<TAttribute> metadata,
            Component component
        )
            where TAttribute : BaseRelationalComponentAttribute
        {
            if (!metadata.attribute.SkipIfAssigned)
            {
                return false;
            }

            object currentValue = metadata.GetValue(component);

            return ValueHelpers.IsAssigned(currentValue);
        }

        // Stack capture costs about 400x the successful binding path; the message already identifies the field (#564).
        internal static void LogMissingComponentError<TAttribute>(
            Component component,
            FieldMetadata<TAttribute> metadata,
            string relationshipType
        )
            where TAttribute : BaseRelationalComponentAttribute
        {
            if (!metadata.attribute.Optional)
            {
                component.LogError(
                    $"Unable to find {relationshipType} component of type {metadata.field.FieldType} for field '{metadata.field.Name}'",
                    stackTrace: false
                );
            }
        }

        /// <summary>
        /// Assigns null to a single-component field when no matching component was found.
        /// This ensures the field is explicitly cleared rather than retaining stale references.
        /// </summary>
        /// <typeparam name="TAttribute">The relational component attribute type.</typeparam>
        /// <param name="component">The component whose field will be assigned.</param>
        /// <param name="metadata">The field metadata containing attribute and accessor information.</param>
        /// <remarks>
        /// This method only assigns null if:
        /// <list type="bullet">
        /// <item><description><see cref="BaseRelationalComponentAttribute.SkipIfAssigned"/> is false</description></item>
        /// <item><description>The field is a single-component type (not array, list, or hashset)</description></item>
        /// </list>
        /// Call this after <see cref="LogMissingComponentError{TAttribute}"/> to ensure single fields
        /// are explicitly nulled when no matching component is found.
        /// </remarks>
        internal static void AssignNullToSingleField<TAttribute>(
            Component component,
            FieldMetadata<TAttribute> metadata
        )
            where TAttribute : BaseRelationalComponentAttribute
        {
            if (metadata.attribute.SkipIfAssigned)
            {
                return;
            }

            if (metadata.kind != FieldKind.Single)
            {
                return;
            }

            metadata.SetValue(component, null);
        }

        internal static void SetEmptyCollection<TAttribute>(
            Component component,
            FieldMetadata<TAttribute> metadata
        )
            where TAttribute : BaseRelationalComponentAttribute
        {
            switch (metadata.kind)
            {
                case FieldKind.Array:

                    metadata.SetValue(component, metadata.arrayCreator(0));

                    break;

                case FieldKind.List:
                    {
                        object existing = metadata.GetValue(component);
                        if (existing is IList list)
                        {
                            list.Clear();
                        }
                        else
                        {
                            metadata.SetValue(component, metadata.listCreator(0));
                        }
                    }
                    break;

                case FieldKind.HashSet:
                    {
                        object existing = metadata.GetValue(component);
                        if (existing != null && metadata.hashSetClearer != null)
                        {
                            metadata.hashSetClearer(existing);
                        }
                        else
                        {
                            metadata.SetValue(component, metadata.hashSetCreator(0));
                        }
                    }
                    break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool PassesStateAndFilters(
            Component candidate,
            FilterParameters filters,
            bool filterDisabledComponents = true
        )
        {
            if (candidate == null)
            {
                return false;
            }

            if (!filters.RequiresPostProcessing)
            {
                return true;
            }

            GameObject candidateGameObject = null;

            if (filters._checkHierarchy)
            {
                candidateGameObject = candidate.gameObject;

                if (!candidateGameObject.activeInHierarchy)
                {
                    return false;
                }

                if (filterDisabledComponents && !candidate.IsComponentEnabled())
                {
                    return false;
                }
            }

            if (filters is { _checkTag: false, _checkName: false })
            {
                return true;
            }

            if (candidateGameObject == null)
            {
                candidateGameObject = candidate.gameObject;
            }

            if (filters._checkTag && !candidateGameObject.CompareTag(filters._tag))
            {
                return false;
            }

            if (filters._checkName && !candidateGameObject.name.Contains(filters._nameSubstring))
            {
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int FilterComponentsInPlace(
            List<Component> components,
            BaseRelationalComponentAttribute attribute,
            Type elementType,
            bool isInterface,
            bool filterDisabledComponents = true
        )
        {
            FilterParameters filters = new(attribute);
            return FilterComponentsInPlace(
                components,
                filters,
                attribute,
                elementType,
                isInterface,
                filterDisabledComponents
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int FilterComponentsInPlace(
            List<Component> components,
            FilterParameters filters,
            BaseRelationalComponentAttribute attribute,
            Type elementType,
            bool isInterface,
            bool filterDisabledComponents = true
        )
        {
            int componentCount = components.Count;
            if (componentCount == 0)
            {
                return 0;
            }

            if (isInterface && !attribute.AllowInterfaces)
            {
                components.Clear();
                return 0;
            }

            if (!filters.RequiresPostProcessing)
            {
                int maxCount = 0 < attribute.MaxCount ? attribute.MaxCount : int.MaxValue;
                if (maxCount < componentCount)
                {
                    components.RemoveRange(maxCount, componentCount - maxCount);
                    return maxCount;
                }

                return componentCount;
            }

            int writeIndex = 0;
            int maxAssignments = 0 < attribute.MaxCount ? attribute.MaxCount : int.MaxValue;

            // Unity queries already guarantee element-type membership.
            for (int readIndex = 0; readIndex < componentCount; readIndex++)
            {
                Component candidate = components[readIndex];

                if (PassesStateAndFilters(candidate, filters, filterDisabledComponents))
                {
                    components[writeIndex++] = candidate;

                    if (maxAssignments <= writeIndex)
                    {
                        break;
                    }
                }
            }

            if (writeIndex < components.Count)
            {
                components.RemoveRange(writeIndex, components.Count - writeIndex);
            }

            return writeIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // The Boolean result avoids another native Unity aliveness check (#529).
        private static bool TryFirstMatchingComponent(
            List<Component> components,
            FilterParameters filters,
            bool filterDisabledComponents,
            out Component match
        )
        {
            foreach (Component candidate in components)
            {
                if (PassesStateAndFilters(candidate, filters, filterDisabledComponents))
                {
                    match = candidate;
                    return true;
                }
            }

            match = null;
            return false;
        }

        internal static bool TryResolveSingleComponent(
            Component component,
            FilterParameters filters,
            Type elementType,
            bool isInterface,
            bool allowInterfaces,
            List<Component> scratch,
            out Component singleComponent,
            bool filterDisabledComponents = true
        )
        {
            bool requiresPostProcessing = filters.RequiresPostProcessing;

            if (isInterface && !allowInterfaces)
            {
                singleComponent = default;
                return false;
            }

            if (!requiresPostProcessing && !isInterface)
            {
                return component.TryGetComponent(elementType, out singleComponent);
            }

            if (
                component.TryGetComponent(elementType, out singleComponent)
                && (
                    !requiresPostProcessing
                    || PassesStateAndFilters(singleComponent, filters, filterDisabledComponents)
                )
            )
            {
                return true;
            }

            if (scratch != null)
            {
                // Unity clears caller-buffer component queries, including zero-match results.
                component.GetComponents(elementType, scratch);
                return TryFirstMatchingComponent(
                    scratch,
                    filters,
                    filterDisabledComponents,
                    out singleComponent
                );
            }

            using PooledResource<List<Component>> pooled = Buffers<Component>.List.Get(
                out List<Component> components
            );
            component.GetComponents(elementType, components);
            return TryFirstMatchingComponent(
                components,
                filters,
                filterDisabledComponents,
                out singleComponent
            );
        }

        internal static List<Component> GetComponentsOfType(
            Component component,
            Type elementType,
            bool isInterface,
            bool allowInterfaces,
            List<Component> buffer
        )
        {
            // Unity type queries resolve interfaces and base classes directly; no managed membership pass is needed.
            if (isInterface && !allowInterfaces)
            {
                buffer.Clear();
                return buffer;
            }

            // Unity clears caller-buffer component queries, including zero-match results.
            component.GetComponents(elementType, buffer);
            return buffer;
        }

        internal enum FieldKind : byte
        {
            Single = 0,
            Array = 1,
            List = 2,
            HashSet = 3,
        }

        internal readonly struct FilterParameters
        {
            internal readonly bool _checkHierarchy;
            internal readonly bool _checkTag;
            internal readonly bool _checkName;
            internal readonly string _tag;
            internal readonly string _nameSubstring;

            internal FilterParameters(BaseRelationalComponentAttribute attribute)
            {
                _checkHierarchy = !attribute.IncludeInactive;
                _tag = attribute.TagFilter;
                _nameSubstring = attribute.NameFilter;
                _checkTag = _tag != null;
                _checkName = _nameSubstring != null;
            }

            internal bool RequiresPostProcessing => _checkHierarchy || _checkTag || _checkName;
        }

        internal readonly struct FieldMetadata<TAttribute>
            where TAttribute : BaseRelationalComponentAttribute
        {
            public readonly FieldInfo field;
            public readonly TAttribute attribute;
            private readonly FieldAccessor accessor;
            private readonly FilterParameters filters;
            public readonly FieldKind kind;
            public readonly Type elementType;
            public readonly Func<int, Array> arrayCreator;
            public readonly Func<int, IList> listCreator;
            public readonly Func<int, object> hashSetCreator;
            public readonly Action<object, object> hashSetAdder;
            public readonly Action<object> hashSetClearer;
            public readonly bool isInterface;

            public FieldMetadata(
                FieldInfo field,
                TAttribute attribute,
                FilterParameters filters,
                FieldAccessor accessor,
                FieldKind kind,
                Type elementType,
                Func<int, Array> arrayCreator,
                Func<int, IList> listCreator,
                Func<int, object> hashSetCreator,
                Action<object, object> hashSetAdder,
                Action<object> hashSetClearer,
                bool isInterface
            )
            {
                this.field = field;
                this.attribute = attribute;
                this.accessor = accessor ?? FieldAccessor.Null;
                this.filters = filters;
                this.kind = kind;
                this.elementType = elementType;
                this.arrayCreator = arrayCreator;
                this.listCreator = listCreator;
                this.hashSetCreator = hashSetCreator;
                this.hashSetAdder = hashSetAdder;
                this.hashSetClearer = hashSetClearer;
                this.isInterface = isInterface;
            }

            public bool HasFilters => filters.RequiresPostProcessing;

            public FilterParameters Filters => filters;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public object GetValue(Component component)
            {
                return accessor.Get(component);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void SetValue(Component component, object value)
            {
                accessor.Set(component, value);
            }
        }

        internal abstract class FieldAccessor
        {
            public static readonly FieldAccessor Null = new NullFieldAccessor();

            public abstract object Get(Component component);
            public abstract void Set(Component component, object value);

            private sealed class NullFieldAccessor : FieldAccessor
            {
                public override object Get(Component component)
                {
                    return null;
                }

                public override void Set(Component component, object value) { }
            }
        }

        private sealed class FieldAccessor<TComponent, TValue> : FieldAccessor
            where TComponent : Component
        {
            private readonly FieldSetter<TComponent, TValue> setter;
            private readonly Func<TComponent, TValue> getter;

            public FieldAccessor(FieldInfo field)
            {
                setter = ReflectionHelpers.GetFieldSetter<TComponent, TValue>(field);
                getter = ReflectionHelpers.GetFieldGetter<TComponent, TValue>(field);
            }

            public override object Get(Component component)
            {
                if (component == null)
                {
                    return null;
                }

                TComponent typedComponent = (TComponent)component;
                return getter(typedComponent);
            }

            public override void Set(Component component, object value)
            {
                if (component == null)
                {
                    return;
                }

                TComponent typedComponent = (TComponent)component;
                TValue typedValue = value != null ? (TValue)value : default;
                setter(ref typedComponent, typedValue);
            }
        }
    }
}
