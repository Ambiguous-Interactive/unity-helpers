// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Reflection;
    using Extensions;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    [CustomPropertyDrawer(typeof(WShowIfAttribute))]
    public sealed class WShowIfPropertyDrawer : PropertyDrawer
    {
        private const string ArrayDataMarker = ".Array.data[";

        /// <summary>
        /// The standard property name for C# indexers ("Item").
        /// </summary>
        private const string IndexerPropertyName = "Item";

        /// <summary>
        /// Maximum number of types to cache accessor dictionaries for.
        /// This prevents unbounded memory growth in large projects with many types.
        /// </summary>
        private const int MaxCachedAccessorTypes = 500;

        /// <summary>
        /// Maximum number of accessors to cache per type.
        /// This prevents unbounded growth for types with many conditional fields.
        /// </summary>
        private const int MaxAccessorsPerType = 100;

        /// <summary>
        /// Maximum number of condition property cache entries.
        /// While this cache is cleared every frame, this limit prevents runaway growth
        /// within a single frame when many properties are evaluated.
        /// </summary>
        private const int MaxConditionPropertyCacheSize = 10000;

        /// <summary>
        /// Maximum number of field info cache entries.
        /// This cache stores FieldInfo lookups to avoid repeated reflection.
        /// </summary>
        private const int MaxFieldInfoCacheSize = 2000;

        /// <summary>
        /// Maximum number of member info cache entries.
        /// This cache stores MemberInfo (FieldInfo, PropertyInfo, MethodInfo) lookups
        /// to avoid repeated reflection in ResolveMemberAccessor.
        /// </summary>
        private const int MaxMemberInfoCacheSize = 2000;

        private static readonly Dictionary<
            Type,
            Dictionary<string, Func<object, object>>
        > CachedAccessors = new();
        private static readonly object[] EmptyParameters = Array.Empty<object>();
        private static readonly Dictionary<
            (int serializedObjectHash, int instanceId, string propertyPath, string conditionField),
            SerializedProperty
        > ConditionPropertyCache = new();
        private static readonly Dictionary<
            (Type ownerType, string fieldName),
            FieldInfo
        > FieldInfoCache = new();

        /// <summary>
        /// Cache for MemberInfo lookups (FieldInfo, PropertyInfo, MethodInfo) keyed by type and member name.
        /// Uses LRU eviction when <see cref="MaxMemberInfoCacheSize"/> is reached.
        /// </summary>
        private static readonly Dictionary<
            (Type type, string memberName),
            MemberInfo
        > MemberInfoCache = new();

        [ThreadStatic]
        private static object[] _singleIndexArgs;

        private static int _lastConditionCacheFrame = -1;
        private WShowIfAttribute _overrideAttribute;

        /// <summary>
        /// Clears all caches used by WShowIfPropertyDrawer.
        /// Called during domain reload to prevent stale references.
        /// </summary>
        internal static void ClearCache()
        {
            CachedAccessors.Clear();
            ConditionPropertyCache.Clear();
            FieldInfoCache.Clear();
            MemberInfoCache.Clear();
            _lastConditionCacheFrame = -1;
        }

        /// <summary>
        /// Checks whether a property with [WShowIf] attribute should be visible.
        /// This method should be called by custom editors before drawing properties
        /// to properly handle conditional visibility for arrays/lists.
        /// </summary>
        /// <param name="property">The serialized property to check.</param>
        /// <returns>True if the property should be shown, false if it should be hidden.</returns>
        public static bool ShouldShowProperty(SerializedProperty property)
        {
            if (property == null)
            {
                return true;
            }

            WShowIfAttribute showIfAttribute = GetShowIfAttribute(property);
            if (showIfAttribute == null)
            {
                return true;
            }

            return EvaluateShowCondition(property, showIfAttribute);
        }

        private static WShowIfAttribute GetShowIfAttribute(SerializedProperty property)
        {
            object enclosingObject = property.GetEnclosingObject(out FieldInfo fieldInfo);
            if (fieldInfo == null)
            {
                return null;
            }

            return fieldInfo.GetCustomAttribute<WShowIfAttribute>();
        }

        private static bool EvaluateShowCondition(
            SerializedProperty property,
            WShowIfAttribute showIf
        )
        {
            if (
                TryGetConditionProperty(
                    property,
                    showIf.conditionField,
                    out SerializedProperty conditionProperty
                )
            )
            {
                if (TryEvaluateCondition(conditionProperty, showIf, out bool serializedResult))
                {
                    return serializedResult;
                }

                return true;
            }

            object enclosingObject = property.GetEnclosingObject(out _);
            if (enclosingObject == null)
            {
                return true;
            }

            Type ownerType = enclosingObject.GetType();
            Func<object, object> accessor = GetAccessor(ownerType, showIf.conditionField);
            object fieldValue = accessor(enclosingObject);
            return !ShowIfConditionEvaluator.TryEvaluateCondition(
                    fieldValue,
                    showIf,
                    out bool reflectedResult
                ) || reflectedResult;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!ShouldShowInternal(property))
            {
                return 0f;
            }

            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (ShouldShowInternal(property))
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }

        private bool ShouldShowInternal(SerializedProperty property)
        {
            return ShouldShow(property);
        }

        private static bool IsArrayElement(SerializedProperty property)
        {
            string propertyPath = property?.propertyPath;
            if (string.IsNullOrEmpty(propertyPath))
            {
                return false;
            }

            return propertyPath.Contains(ArrayDataMarker);
        }

        internal void InitializeForTesting(WShowIfAttribute attributeOverride)
        {
            _overrideAttribute = attributeOverride;
        }

        private WShowIfAttribute ResolveAttribute()
        {
            if (_overrideAttribute != null)
            {
                return _overrideAttribute;
            }

            return attribute as WShowIfAttribute;
        }

        internal bool ShouldShow(SerializedProperty property)
        {
            // When WShowIf is applied to an array/list field, Unity's PropertyDrawer system
            // invokes the drawer for each array *element* (paths like "field.Array.data[0]"),
            // not for the array field itself. The parent array draws its own container (foldout,
            // header, etc.) and then asks us for each element's height. If we return 0 for
            // hidden elements while the array container is still drawn, the layout breaks and
            // causes visual corruption/overdraw.
            //
            // Solution: If this property is an array element, always show it. The visibility
            // decision should be made at the array level, not the element level. If the drawer
            // is on the array itself (property.isArray && not an element), we handle it normally.
            if (IsArrayElement(property))
            {
                return true;
            }

            WShowIfAttribute showIf = ResolveAttribute();
            if (showIf == null)
            {
                return true;
            }

            if (
                TryGetConditionProperty(
                    property,
                    showIf.conditionField,
                    out SerializedProperty conditionProperty
                )
            )
            {
                if (TryEvaluateCondition(conditionProperty, showIf, out bool serializedResult))
                {
                    return serializedResult;
                }

                return true;
            }

            object enclosingObject = property.GetEnclosingObject(out _);
            if (enclosingObject == null)
            {
                return true;
            }

            Type ownerType = enclosingObject.GetType();
            Func<object, object> accessor = GetAccessor(ownerType, showIf.conditionField);
            object fieldValue = accessor(enclosingObject);
            return !ShowIfConditionEvaluator.TryEvaluateCondition(
                    fieldValue,
                    showIf,
                    out bool reflectedResult
                ) || reflectedResult;
        }

        private static bool TryEvaluateCondition(
            SerializedProperty conditionProperty,
            WShowIfAttribute showIf,
            out bool shouldShow
        )
        {
            if (conditionProperty == null)
            {
                shouldShow = true;
                return false;
            }

            object conditionValue;
            if (conditionProperty.propertyType == SerializedPropertyType.Boolean)
            {
                conditionValue = conditionProperty.boolValue;
            }
            else
            {
                conditionValue = conditionProperty.GetTargetObjectWithField(out _);
            }

            return ShowIfConditionEvaluator.TryEvaluateCondition(
                conditionValue,
                showIf,
                out shouldShow
            );
        }

        private static bool TryGetConditionProperty(
            SerializedProperty property,
            string conditionField,
            out SerializedProperty conditionProperty
        )
        {
            int currentFrame = Time.frameCount;
            if (_lastConditionCacheFrame != currentFrame)
            {
                ConditionPropertyCache.Clear();
                _lastConditionCacheFrame = currentFrame;
            }

            SerializedObject serializedObject = property.serializedObject;
            UnityEngine.Object target = serializedObject?.targetObject;
            int serializedObjectHash = serializedObject?.GetHashCode() ?? 0;
            int instanceId = target != null ? target.GetUnityObjectId() : 0;
            string propertyPath = property.propertyPath ?? string.Empty;
            (int, int, string, string) cacheKey = (
                serializedObjectHash,
                instanceId,
                propertyPath,
                conditionField
            );

            if (
                EditorCacheHelper.TryGetFromBoundedLRUCache(
                    ConditionPropertyCache,
                    cacheKey,
                    out conditionProperty
                )
            )
            {
                return conditionProperty != null;
            }

            conditionProperty = serializedObject.FindProperty(conditionField);
            if (conditionProperty != null)
            {
                EditorCacheHelper.AddToBoundedCache(
                    ConditionPropertyCache,
                    cacheKey,
                    conditionProperty,
                    MaxConditionPropertyCacheSize
                );
                return true;
            }

            if (!string.IsNullOrEmpty(propertyPath))
            {
                int separatorIndex = propertyPath.LastIndexOf('.');
                string siblingPath =
                    separatorIndex == -1
                        ? conditionField
                        : propertyPath.Substring(0, separatorIndex + 1) + conditionField;
                conditionProperty = serializedObject.FindProperty(siblingPath);
                if (conditionProperty != null)
                {
                    EditorCacheHelper.AddToBoundedCache(
                        ConditionPropertyCache,
                        cacheKey,
                        conditionProperty,
                        MaxConditionPropertyCacheSize
                    );
                    return true;
                }
            }

            EditorCacheHelper.AddToBoundedCache(
                ConditionPropertyCache,
                cacheKey,
                (SerializedProperty)null,
                MaxConditionPropertyCacheSize
            );
            conditionProperty = null;
            return false;
        }

        private static Func<object, object> GetAccessor(Type ownerType, string memberPath)
        {
            if (
                !CachedAccessors.TryGetValue(
                    ownerType,
                    out Dictionary<string, Func<object, object>> cachedForType
                )
            )
            {
                if (CachedAccessors.Count >= MaxCachedAccessorTypes)
                {
                    return BuildAccessor(ownerType, memberPath);
                }

                cachedForType = new Dictionary<string, Func<object, object>>(
                    StringComparer.Ordinal
                );
                CachedAccessors[ownerType] = cachedForType;
            }

            if (!cachedForType.TryGetValue(memberPath, out Func<object, object> accessor))
            {
                accessor = BuildAccessor(ownerType, memberPath);
                if (cachedForType.Count < MaxAccessorsPerType)
                {
                    cachedForType[memberPath] = accessor;
                }
            }

            return accessor;
        }

        private static Func<object, object> BuildAccessor(Type ownerType, string memberPath)
        {
            if (string.IsNullOrEmpty(memberPath))
            {
                return static _ => null;
            }

            using PooledResource<List<MemberPathSegment>> segmentsLease =
                Buffers<MemberPathSegment>.GetList(4, out List<MemberPathSegment> segments);

            ParseMemberPath(memberPath, segments);
            if (segments.Count == 0)
            {
                return static _ => null;
            }

            List<Func<object, object>> steps = new(segments.Count);
            Type currentType = ownerType;

            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex += 1)
            {
                MemberPathSegment segment = segments[segmentIndex];
                MemberAccessor memberAccessor = ResolveMemberAccessor(
                    currentType,
                    segment.MemberName
                );
                if (!memberAccessor.IsValid)
                {
                    Debug.LogError(
                        $"Failed to resolve conditional member '{segment.MemberName}' on {currentType.FullName} while evaluating '{memberPath}'."
                    );
                    return static _ => null;
                }

                steps.Add(memberAccessor.Getter);
                currentType = memberAccessor.ValueType ?? typeof(object);

                if (segment.Indices.Length == 0)
                {
                    continue;
                }

                for (
                    int indexPosition = 0;
                    indexPosition < segment.Indices.Length;
                    indexPosition += 1
                )
                {
                    IndexAccessor indexAccessor = CreateIndexAccessor(
                        currentType,
                        segment.Indices[indexPosition]
                    );
                    if (!indexAccessor.IsValid)
                    {
                        Debug.LogError(
                            $"Failed to resolve index accessor for '{segment.MemberName}' on {currentType.FullName} while evaluating '{memberPath}'."
                        );
                        return static _ => null;
                    }

                    steps.Add(indexAccessor.Getter);
                    currentType = indexAccessor.ElementType ?? typeof(object);
                }
            }

            return instance =>
            {
                object current = instance;
                for (int stepIndex = 0; stepIndex < steps.Count; stepIndex += 1)
                {
                    if (current == null)
                    {
                        return null;
                    }

                    current = steps[stepIndex](current);
                }

                return current;
            };
        }

        /// <summary>
        /// Gets a cached MemberInfo (FieldInfo, PropertyInfo, or MethodInfo) for the specified type and member name.
        /// Uses LRU eviction when cache capacity is reached.
        /// </summary>
        /// <param name="type">The type to search for the member.</param>
        /// <param name="memberName">The name of the member to find.</param>
        /// <param name="flags">The binding flags for the member lookup.</param>
        /// <returns>The cached or newly resolved MemberInfo, or null if not found.</returns>
        private static MemberInfo GetCachedMemberInfo(
            Type type,
            string memberName,
            BindingFlags flags
        )
        {
            (Type, string) key = (type, memberName);
            if (
                EditorCacheHelper.TryGetFromBoundedLRUCache(
                    MemberInfoCache,
                    key,
                    out MemberInfo cached
                )
            )
            {
                return cached;
            }

            MemberInfo memberInfo = type.GetField(memberName, flags);
            if (memberInfo == null)
            {
                memberInfo = type.GetProperty(memberName, flags);
            }

            if (memberInfo == null)
            {
                memberInfo = type.GetMethod(memberName, flags, null, Type.EmptyTypes, null);
            }

            if (memberInfo != null)
            {
                EditorCacheHelper.AddToBoundedCache(
                    MemberInfoCache,
                    key,
                    memberInfo,
                    MaxMemberInfoCacheSize
                );
            }

            return memberInfo;
        }

        private static MemberAccessor ResolveMemberAccessor(Type type, string memberName)
        {
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.FlattenHierarchy;

            MemberInfo memberInfo = GetCachedMemberInfo(type, memberName, flags);
            if (memberInfo == null)
            {
                return MemberAccessor.Invalid;
            }

            if (memberInfo is FieldInfo field)
            {
                return new MemberAccessor(ReflectionHelpers.GetFieldGetter(field), field.FieldType);
            }

            if (memberInfo is PropertyInfo propertyInfo && propertyInfo.CanRead)
            {
                return new MemberAccessor(
                    ReflectionHelpers.GetPropertyGetter(propertyInfo),
                    propertyInfo.PropertyType
                );
            }

            if (memberInfo is MethodInfo methodInfo)
            {
                if (methodInfo.ReturnType == typeof(void))
                {
                    Debug.LogWarning(
                        $"WShowIf member '{memberName}' on {type.FullName} returns void and cannot be used as a condition."
                    );
                    return MemberAccessor.Invalid;
                }

                Func<object, object[], object> invoker = ReflectionHelpers.GetMethodInvoker(
                    methodInfo
                );
                return new MemberAccessor(
                    instance => invoker(instance, EmptyParameters),
                    methodInfo.ReturnType
                );
            }

            return MemberAccessor.Invalid;
        }

        private static IndexAccessor CreateIndexAccessor(Type collectionType, int index)
        {
            if (collectionType == null)
            {
                return IndexAccessor.Invalid;
            }

            if (collectionType.IsArray)
            {
                Type elementType = collectionType.GetElementType() ?? typeof(object);
                return new IndexAccessor(
                    value =>
                    {
                        Array array = value as Array;
                        if (array == null || index < 0 || index >= array.Length)
                        {
                            return null;
                        }

                        return array.GetValue(index);
                    },
                    elementType
                );
            }

            if (typeof(IList).IsAssignableFrom(collectionType))
            {
                Type elementType = ResolveListElementType(collectionType);
                return new IndexAccessor(
                    value =>
                    {
                        IList list = value as IList;
                        if (list == null || index < 0 || index >= list.Count)
                        {
                            return null;
                        }

                        return list[index];
                    },
                    elementType
                );
            }

            Type readOnlyListInterface = GetGenericInterface(
                collectionType,
                typeof(IReadOnlyList<>)
            );
            if (readOnlyListInterface != null)
            {
                Type elementType = readOnlyListInterface.GetGenericArguments()[0];
                return new IndexAccessor(
                    value =>
                    {
                        if (value == null)
                        {
                            return null;
                        }

                        PropertyInfo indexer = readOnlyListInterface.GetProperty(
                            IndexerPropertyName
                        );
                        if (indexer == null)
                        {
                            return null;
                        }

                        try
                        {
                            _singleIndexArgs ??= new object[1];
                            _singleIndexArgs[0] = index;
                            return indexer.GetValue(value, _singleIndexArgs);
                        }
                        catch
                        {
                            return null;
                        }
                    },
                    elementType
                );
            }

            if (typeof(IEnumerable).IsAssignableFrom(collectionType))
            {
                Type elementType = ResolveEnumerableElementType(collectionType);
                return new IndexAccessor(
                    value =>
                    {
                        IEnumerable enumerable = value as IEnumerable;
                        if (enumerable == null)
                        {
                            return null;
                        }

                        int current = 0;
                        foreach (object item in enumerable)
                        {
                            if (current == index)
                            {
                                return item;
                            }

                            current += 1;
                        }

                        return null;
                    },
                    elementType
                );
            }

            return IndexAccessor.Invalid;
        }

        private static void ParseMemberPath(string memberPath, List<MemberPathSegment> segments)
        {
            if (segments == null)
            {
                throw new ArgumentNullException(nameof(segments));
            }

            segments.Clear();

            if (string.IsNullOrEmpty(memberPath))
            {
                return;
            }

            string[] rawSegments = memberPath.Split('.');
            using PooledResource<List<int>> indicesLease = Buffers<int>.List.Get(
                out List<int> indices
            );

            for (int index = 0; index < rawSegments.Length; index += 1)
            {
                string raw = rawSegments[index];
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }

                string name = raw;
                indices.Clear();

                int bracket = raw.IndexOf('[');
                if (bracket >= 0)
                {
                    name = raw.Substring(0, bracket);
                    int cursor = bracket;
                    while (cursor < raw.Length && (cursor = raw.IndexOf('[', cursor)) != -1)
                    {
                        int endBracket = raw.IndexOf(']', cursor + 1);
                        if (endBracket == -1)
                        {
                            break;
                        }

                        string slice = raw.Substring(cursor + 1, endBracket - cursor - 1);
                        if (int.TryParse(slice, out int parsedIndex))
                        {
                            indices.Add(parsedIndex);
                        }
                        cursor = endBracket + 1;
                    }
                }

                MemberPathSegment segment = new(
                    name,
                    indices.Count > 0 ? indices.ToArray() : Array.Empty<int>()
                );
                segments.Add(segment);
            }
        }

        private static Type ResolveListElementType(Type type)
        {
            if (type.IsGenericType)
            {
                Type[] args = type.GetGenericArguments();
                if (args.Length == 1)
                {
                    return args[0];
                }
            }

            return typeof(object);
        }

        private static Type ResolveEnumerableElementType(Type type)
        {
            Type genericInterface = GetGenericInterface(type, typeof(IEnumerable<>));
            if (genericInterface != null)
            {
                Type[] args = genericInterface.GetGenericArguments();
                if (args.Length == 1)
                {
                    return args[0];
                }
            }

            return typeof(object);
        }

        private static Type GetGenericInterface(Type type, Type interfaceTemplate)
        {
            if (
                type.IsInterface
                && type.IsGenericType
                && type.GetGenericTypeDefinition() == interfaceTemplate
            )
            {
                return type;
            }

            Type[] interfaces = type.GetInterfaces();
            for (int index = 0; index < interfaces.Length; index += 1)
            {
                Type candidate = interfaces[index];
                if (
                    candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == interfaceTemplate
                )
                {
                    return candidate;
                }
            }

            return null;
        }

        private readonly struct MemberAccessor
        {
            public static readonly MemberAccessor Invalid = new(null, null);

            public MemberAccessor(Func<object, object> getter, Type valueType)
            {
                Getter = getter;
                ValueType = valueType;
            }

            public Func<object, object> Getter { get; }

            public Type ValueType { get; }

            public bool IsValid => Getter != null;
        }

        private readonly struct IndexAccessor
        {
            public static readonly IndexAccessor Invalid = new(null, null);

            public IndexAccessor(Func<object, object> getter, Type elementType)
            {
                Getter = getter;
                ElementType = elementType;
            }

            public Func<object, object> Getter { get; }

            public Type ElementType { get; }

            public bool IsValid => Getter != null;
        }

        private readonly struct MemberPathSegment
        {
            public MemberPathSegment(string memberName, int[] indices)
            {
                MemberName = memberName;
                Indices = indices;
            }

            public string MemberName { get; }

            public int[] Indices { get; }
        }
    }
#endif
}
