// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tags
{
    using System;
    using System.Collections.Generic;
    using Core.Attributes;
    using Core.Helper;
    using UnityEngine;
    using Utils;

    /// <summary>
    /// Serialized cache of attribute metadata to avoid runtime reflection.
    /// This asset is automatically generated in the Editor.
    /// When the prewarm toggle is enabled, a runtime hook pre-initializes relational component
    /// reflection helpers before the first scene loads to avoid first-use stalls.
    /// </summary>
    [ScriptableSingletonPath("Wallstop Studios/Unity Helpers")]
    [AllowDuplicateCleanup]
    [AutoLoadSingleton(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public sealed class AttributeMetadataCache : ScriptableObjectSingleton<AttributeMetadataCache>
    {
        [Header("Initialization")]
        [Tooltip(
            "If enabled, pre-warms RelationalComponent reflection caches at runtime before the first scene loads. Useful to avoid first-use stalls on IL2CPP or slow devices."
        )]
        [SerializeField]
        private bool _prewarmRelationalOnLoad = false;

        [SerializeField]
        private string[] _allAttributeNames = Array.Empty<string>();

        [NonSerialized]
        private string[] _computedAllAttributeNames;

        [NonSerialized]
        private bool _computedAllAttributeNamesIncludesTests;

        [SerializeField]
        private TypeFieldMetadata[] _typeMetadata = Array.Empty<TypeFieldMetadata>();

        [SerializeField]
        internal RelationalTypeMetadata[] _relationalTypeMetadata =
            Array.Empty<RelationalTypeMetadata>();

        [SerializeField]
        private AutoLoadSingletonEntry[] _autoLoadSingletons =
            Array.Empty<AutoLoadSingletonEntry>();

        internal string[] SerializedAttributeNames => _allAttributeNames ?? Array.Empty<string>();

        internal TypeFieldMetadata[] SerializedTypeMetadata =>
            _typeMetadata ?? Array.Empty<TypeFieldMetadata>();

        internal RelationalTypeMetadata[] SerializedRelationalTypeMetadata =>
            _relationalTypeMetadata ?? Array.Empty<RelationalTypeMetadata>();

        internal AutoLoadSingletonEntry[] SerializedAutoLoadSingletons =>
            _autoLoadSingletons ?? Array.Empty<AutoLoadSingletonEntry>();

        private readonly object _lookupLock = new();

        // Runtime lookups using Type as key (much faster than string comparison)
        private Dictionary<Type, string[]> _typeFieldsLookup;
        private Dictionary<Type, RelationalFieldMetadata[]> _relationalFieldsLookup;
        private Dictionary<Type, ResolvedRelationalFieldMetadata[]> _resolvedRelationalFieldsLookup;
        private Dictionary<ElementTypeKey, Type> _elementTypeLookup; // Cache resolved element types

        private static readonly object _typeResolutionLock = new();
        private static readonly Dictionary<string, Type> _resolvedTypeCache = new(
            StringComparer.Ordinal
        );
        private static readonly object _missingTypeLock = new();
        private static readonly HashSet<string> _loggedMissingTypeKeys = new(
            StringComparer.Ordinal
        );

        /// <summary>
        /// Alphabetical list of all attribute field names discovered across registered <see cref="AttributesComponent"/> types.
        /// Includes test-only attributes when test assemblies are loaded.
        /// </summary>
        /// <remarks>
        /// Callers typically use this to drive tooling (e.g., dropdowns). The list is cached and refreshed automatically when test assemblies are present.
        /// </remarks>
        /// <example>
        /// <code language="csharp">
        /// AttributeMetadataCache cache = AttributeMetadataCache.Instance;
        /// foreach (string attributeName in cache.AllAttributeNames)
        /// {
        ///     Debug.Log($"Attribute: {attributeName}");
        /// }
        /// </code>
        /// </example>
        public string[] AllAttributeNames
        {
            get
            {
                bool hasTestAssemblies = AttributeMetadataFilters.HasTestAssembliesLoaded();

                if (
                    _computedAllAttributeNames == null
                    || (hasTestAssemblies && !_computedAllAttributeNamesIncludesTests)
                    || (!hasTestAssemblies && _computedAllAttributeNamesIncludesTests)
                )
                {
                    string[] baseNames = _allAttributeNames ?? Array.Empty<string>();

                    _computedAllAttributeNames = hasTestAssemblies
                        ? AttributeMetadataFilters.MergeWithExcludedAttributeNames(baseNames)
                        : baseNames;

                    _computedAllAttributeNamesIncludesTests = hasTestAssemblies;
                }

                return _computedAllAttributeNames;
            }
        }

        /// <summary>
        /// Auto-load singleton entries configured for this cache.
        /// </summary>
        /// <remarks>Entries are sorted for determinism and safe to iterate without additional allocation.</remarks>
        public AutoLoadSingletonEntry[] AutoLoadSingletons => SerializedAutoLoadSingletons;

        private void OnEnable()
        {
            _computedAllAttributeNames = null;
            _computedAllAttributeNamesIncludesTests = false;
            BuildLookup();
        }

#if UNITY_2019_1_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RuntimePrewarmRelationalIfEnabled()
        {
            try
            {
                AttributeMetadataCache inst = Instance;
                if (inst != null && inst._prewarmRelationalOnLoad)
                {
                    RelationalComponentInitializer.Report report =
                        RelationalComponentInitializer.Initialize(types: null, logSummary: false);
                    Debug.Log(
                        $"AttributeMetadataCache: Relational prewarm enabled. TypesWarmed={report.TypesWarmed}, FieldsWarmed={report.FieldsWarmed}, Warnings={report.Warnings}, Errors={report.Errors}."
                    );
                }
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"AttributeMetadataCache: Exception during relational prewarm on load: {e.Message}\n{e}"
                );
            }
        }
#endif

        private void BuildLookup()
        {
            lock (_lookupLock)
            {
                if (
                    _typeFieldsLookup != null
                    && _relationalFieldsLookup != null
                    && _resolvedRelationalFieldsLookup != null
                    && _elementTypeLookup != null
                )
                {
                    return;
                }

                Dictionary<Type, string[]> typeFieldsLookup = new(_typeMetadata?.Length ?? 0);
                Dictionary<Type, RelationalFieldMetadata[]> relationalFieldsLookup = new(
                    _relationalTypeMetadata?.Length ?? 0
                );
                Dictionary<Type, ResolvedRelationalFieldMetadata[]> resolvedRelationalFieldsLookup =
                    new(_relationalTypeMetadata?.Length ?? 0);
                Dictionary<ElementTypeKey, Type> elementTypeLookup = new(
                    _relationalTypeMetadata?.Length ?? 0
                );

                if (_typeMetadata != null)
                {
                    foreach (TypeFieldMetadata metadata in _typeMetadata)
                    {
                        if (
                            metadata == null
                            || !TryResolveType(metadata.typeName, out Type componentType)
                        )
                        {
                            LogMissingType(metadata?.typeName, "attribute component");
                            continue;
                        }

                        string[] fieldNames = metadata.fieldNames ?? Array.Empty<string>();
                        typeFieldsLookup[componentType] = fieldNames;
                    }
                }

                if (_relationalTypeMetadata != null)
                {
                    foreach (RelationalTypeMetadata metadata in _relationalTypeMetadata)
                    {
                        if (
                            metadata == null
                            || !TryResolveType(metadata.typeName, out Type relationalType)
                        )
                        {
                            LogMissingType(metadata?.typeName, "relational component");
                            continue;
                        }

                        RelationalFieldMetadata[] fields =
                            metadata.fields ?? Array.Empty<RelationalFieldMetadata>();

                        relationalFieldsLookup[relationalType] = fields;

                        ResolvedRelationalFieldMetadata[] resolvedFields =
                            new ResolvedRelationalFieldMetadata[fields.Length];

                        for (int i = 0; i < fields.Length; i++)
                        {
                            RelationalFieldMetadata field = fields[i];
                            Type elementType = null;

                            if (!string.IsNullOrWhiteSpace(field.elementTypeName))
                            {
                                if (TryResolveType(field.elementTypeName, out elementType))
                                {
                                    elementTypeLookup[
                                        new ElementTypeKey(relationalType, field.fieldName)
                                    ] = elementType;
                                }
                                else
                                {
                                    LogMissingType(field.elementTypeName, "relational element");
                                }
                            }

                            resolvedFields[i] = new ResolvedRelationalFieldMetadata(
                                field.fieldName,
                                field.attributeKind,
                                field.fieldKind,
                                elementType,
                                field.isInterface
                            );
                        }

                        resolvedRelationalFieldsLookup[relationalType] = resolvedFields;
                    }
                }

                _typeFieldsLookup = typeFieldsLookup;
                _relationalFieldsLookup = relationalFieldsLookup;
                _resolvedRelationalFieldsLookup = resolvedRelationalFieldsLookup;
                _elementTypeLookup = elementTypeLookup;
            }
        }

#if UNITY_INCLUDE_TESTS
        /// <summary>
        /// Rebuilds all cached lookup tables. Intended for editor and test usage.
        /// </summary>
        /// <remarks>
        /// This clears internal dictionaries and forces a full rebuild, ensuring test isolation.
        /// </remarks>
        public void ForceRebuildForTests()
        {
            lock (_lookupLock)
            {
                _typeFieldsLookup = null;
                _relationalFieldsLookup = null;
                _resolvedRelationalFieldsLookup = null;
                _elementTypeLookup = null;
            }

            BuildLookup();
        }
#endif

        /// <summary>
        /// Attempts to retrieve attribute field names for a given component type.
        /// </summary>
        /// <param name="type">Component type that owns attribute fields.</param>
        /// <param name="fieldNames">Output array of field names if present.</param>
        /// <returns><c>true</c> when metadata exists for the type; otherwise, <c>false</c>.</returns>
        /// <example>
        /// <code language="csharp">
        /// if (AttributeMetadataCache.Instance.TryGetFieldNames(typeof(AttributesComponent), out var names))
        /// {
        ///     Debug.Log($"Found {names.Length} attribute fields.");
        /// }
        /// </code>
        /// </example>
        public bool TryGetFieldNames(Type type, out string[] fieldNames)
        {
            if (_typeFieldsLookup == null)
            {
                BuildLookup();
            }
            // ReSharper disable once PossibleNullReferenceException
            return _typeFieldsLookup.TryGetValue(type, out fieldNames);
        }

        /// <summary>
        /// Attempts to retrieve relational field metadata for a given component type.
        /// </summary>
        /// <param name="type">Component type declaring relational attributes.</param>
        /// <param name="relationalFields">Output array of serialized relational metadata.</param>
        /// <returns><c>true</c> when relational metadata exists; otherwise, <c>false</c>.</returns>
        public bool TryGetRelationalFields(
            Type type,
            out RelationalFieldMetadata[] relationalFields
        )
        {
            if (_relationalFieldsLookup == null)
            {
                BuildLookup();
            }
            // ReSharper disable once PossibleNullReferenceException
            return _relationalFieldsLookup.TryGetValue(type, out relationalFields);
        }

        /// <summary>
        /// Populates <paramref name="destination"/> with the set of component types that declare
        /// relational component fields.
        /// </summary>
        /// <param name="destination">List that receives relational component types.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="destination"/> is null.</exception>
        public void CollectRelationalComponentTypes(List<Type> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (_relationalFieldsLookup == null)
            {
                BuildLookup();
            }

            // ReSharper disable once PossibleNullReferenceException
            foreach (KeyValuePair<Type, RelationalFieldMetadata[]> pair in _relationalFieldsLookup)
            {
                Type componentType = pair.Key;
                if (componentType == null)
                {
                    continue;
                }

                if (pair.Value is { Length: > 0 })
                {
                    destination.Add(componentType);
                }
            }
        }

        /// <summary>
        /// Attempts to retrieve relational metadata with resolved runtime <see cref="Type"/> references.
        /// </summary>
        /// <param name="type">Component type declaring relational attributes.</param>
        /// <param name="relationalFields">Output array of resolved relational metadata.</param>
        /// <returns><c>true</c> when resolved metadata exists; otherwise, <c>false</c>.</returns>
        public bool TryGetResolvedRelationalFields(
            Type type,
            out ResolvedRelationalFieldMetadata[] relationalFields
        )
        {
            if (_resolvedRelationalFieldsLookup == null)
            {
                BuildLookup();
            }
            // ReSharper disable once PossibleNullReferenceException
            return _resolvedRelationalFieldsLookup.TryGetValue(type, out relationalFields);
        }

        /// <summary>
        /// Attempts to resolve the element type for a relational field on a component.
        /// </summary>
        /// <param name="componentType">Component type declaring the field.</param>
        /// <param name="fieldName">Name of the relational field.</param>
        /// <param name="elementType">Resolved element type when available.</param>
        /// <returns><c>true</c> when a matching element type exists; otherwise, <c>false</c>.</returns>
        public bool TryGetElementType(Type componentType, string fieldName, out Type elementType)
        {
            if (_elementTypeLookup == null)
            {
                BuildLookup();
            }
            // ReSharper disable once PossibleNullReferenceException
            return _elementTypeLookup.TryGetValue(
                new ElementTypeKey(componentType, fieldName),
                out elementType
            );
        }

        private static bool TryResolveType(string typeName, out Type type)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                type = null;
                return false;
            }

            lock (_typeResolutionLock)
            {
                if (_resolvedTypeCache.TryGetValue(typeName, out type))
                {
                    return type != null;
                }

                Type resolved = ReflectionHelpers.TryResolveType(typeName);
                _resolvedTypeCache[typeName] = resolved;
                type = resolved;
                return resolved != null;
            }
        }

        private static void LogMissingType(string typeName, string context)
        {
            if (string.IsNullOrWhiteSpace(typeName) || string.IsNullOrWhiteSpace(context))
            {
                return;
            }

            string key = string.Concat(context, ":", typeName);
            lock (_missingTypeLock)
            {
                if (!_loggedMissingTypeKeys.Add(key))
                {
                    return;
                }
            }

            Debug.LogWarning(
                string.Format(
                    "AttributeMetadataCache: Unable to resolve {0} type '{1}'. The cached entry will be ignored. Regenerate the cache to refresh the metadata.",
                    context,
                    typeName
                )
            );
        }

#if UNITY_EDITOR
        /// <summary>
        /// Sets all serialized metadata fields and rebuilds internal lookups when the normalized data changes.
        /// </summary>
        /// <param name="allAttributeNames">All attribute names discovered.</param>
        /// <param name="typeMetadata">Attribute field metadata per component type.</param>
        /// <param name="relationalTypeMetadata">Relational field metadata per component type.</param>
        /// <param name="autoLoadSingletons">Auto-load singleton entries.</param>
        /// <returns><c>true</c> when the serialized cache changed; otherwise, <c>false</c>.</returns>
        public bool SetMetadata(
            string[] allAttributeNames,
            TypeFieldMetadata[] typeMetadata,
            RelationalTypeMetadata[] relationalTypeMetadata,
            AutoLoadSingletonEntry[] autoLoadSingletons
        )
        {
            string[] normalizedAttributeNames = SortAttributeNames(allAttributeNames);
            TypeFieldMetadata[] normalizedTypeMetadata = SortTypeMetadata(typeMetadata);
            RelationalTypeMetadata[] normalizedRelationalMetadata = SortRelationalTypeMetadata(
                relationalTypeMetadata
            );
            AutoLoadSingletonEntry[] normalizedAutoLoad = SortAutoLoadSingletonEntries(
                autoLoadSingletons
            );

            if (
                StringArraysEqual(_allAttributeNames, normalizedAttributeNames)
                && TypeMetadataArraysEqual(_typeMetadata, normalizedTypeMetadata)
                && RelationalMetadataArraysEqual(
                    _relationalTypeMetadata,
                    normalizedRelationalMetadata
                )
                && AutoLoadSingletonEntriesEqual(_autoLoadSingletons, normalizedAutoLoad)
            )
            {
                return false;
            }

            _allAttributeNames = normalizedAttributeNames;
            _typeMetadata = normalizedTypeMetadata;
            _relationalTypeMetadata = normalizedRelationalMetadata;
            _autoLoadSingletons = normalizedAutoLoad;
            _computedAllAttributeNames = null;
            _computedAllAttributeNamesIncludesTests = false;
            _typeFieldsLookup = null;
            _relationalFieldsLookup = null;
            _resolvedRelationalFieldsLookup = null;
            _elementTypeLookup = null;
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }

        private static string[] SortAttributeNames(string[] attributeNames)
        {
            if (attributeNames == null || attributeNames.Length == 0)
            {
                return Array.Empty<string>();
            }

            string[] result = new string[attributeNames.Length];
            Array.Copy(attributeNames, result, attributeNames.Length);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static TypeFieldMetadata[] SortTypeMetadata(TypeFieldMetadata[] typeMetadata)
        {
            if (typeMetadata == null || typeMetadata.Length == 0)
            {
                return Array.Empty<TypeFieldMetadata>();
            }

            int nonNullCount = 0;
            foreach (TypeFieldMetadata typeFieldMetadata in typeMetadata)
            {
                if (typeFieldMetadata != null)
                {
                    nonNullCount++;
                }
            }

            if (nonNullCount == 0)
            {
                return Array.Empty<TypeFieldMetadata>();
            }

            TypeFieldMetadata[] result = new TypeFieldMetadata[nonNullCount];
            int resultIndex = 0;
            foreach (TypeFieldMetadata metadata in typeMetadata)
            {
                if (metadata == null)
                {
                    continue;
                }

                string typeName = metadata.typeName ?? string.Empty;
                string[] fieldNames = metadata.fieldNames ?? Array.Empty<string>();
                string[] sortedFieldNames =
                    fieldNames.Length == 0 ? Array.Empty<string>() : CopyAndSort(fieldNames);

                result[resultIndex] = new TypeFieldMetadata(typeName, sortedFieldNames);
                resultIndex++;
            }

            Array.Sort(result, CompareTypeFieldMetadata);
            return result;
        }

        private static RelationalTypeMetadata[] SortRelationalTypeMetadata(
            RelationalTypeMetadata[] relationalTypeMetadata
        )
        {
            if (relationalTypeMetadata == null || relationalTypeMetadata.Length == 0)
            {
                return Array.Empty<RelationalTypeMetadata>();
            }

            int nonNullCount = 0;
            foreach (RelationalTypeMetadata typeMetadata in relationalTypeMetadata)
            {
                if (typeMetadata != null)
                {
                    nonNullCount++;
                }
            }

            if (nonNullCount == 0)
            {
                return Array.Empty<RelationalTypeMetadata>();
            }

            RelationalTypeMetadata[] result = new RelationalTypeMetadata[nonNullCount];
            int resultIndex = 0;
            foreach (RelationalTypeMetadata metadata in relationalTypeMetadata)
            {
                if (metadata == null)
                {
                    continue;
                }

                string typeName = metadata.typeName ?? string.Empty;
                RelationalFieldMetadata[] sortedFields = SortRelationalFields(metadata.fields);
                result[resultIndex] = new RelationalTypeMetadata(typeName, sortedFields);
                resultIndex++;
            }

            Array.Sort(result, CompareRelationalTypeMetadata);
            return result;
        }

        private static RelationalFieldMetadata[] SortRelationalFields(
            RelationalFieldMetadata[] relationalFields
        )
        {
            if (relationalFields == null || relationalFields.Length == 0)
            {
                return Array.Empty<RelationalFieldMetadata>();
            }

            int nonNullCount = 0;
            foreach (RelationalFieldMetadata relationalField in relationalFields)
            {
                if (relationalField != null)
                {
                    nonNullCount++;
                }
            }

            if (nonNullCount == 0)
            {
                return Array.Empty<RelationalFieldMetadata>();
            }

            RelationalFieldMetadata[] result = new RelationalFieldMetadata[nonNullCount];
            int resultIndex = 0;
            foreach (RelationalFieldMetadata field in relationalFields)
            {
                if (field == null)
                {
                    continue;
                }

                result[resultIndex] = new RelationalFieldMetadata(
                    field.fieldName,
                    field.attributeKind,
                    field.fieldKind,
                    field.elementTypeName,
                    field.isInterface
                );
                resultIndex++;
            }

            Array.Sort(result, CompareRelationalFieldMetadata);
            return result;
        }

        private static string[] CopyAndSort(string[] values)
        {
            string[] result = new string[values.Length];
            Array.Copy(values, result, values.Length);
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }

        private static int CompareTypeFieldMetadata(TypeFieldMetadata left, TypeFieldMetadata right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            int typeNameComparison = string.CompareOrdinal(left.typeName, right.typeName);
            if (typeNameComparison != 0)
            {
                return typeNameComparison;
            }

            string[] leftFields = left.fieldNames ?? Array.Empty<string>();
            string[] rightFields = right.fieldNames ?? Array.Empty<string>();

            int lengthComparison = leftFields.Length.CompareTo(rightFields.Length);
            if (lengthComparison != 0)
            {
                return lengthComparison;
            }

            for (int i = 0; i < leftFields.Length; i++)
            {
                int fieldComparison = string.CompareOrdinal(leftFields[i], rightFields[i]);
                if (fieldComparison != 0)
                {
                    return fieldComparison;
                }
            }

            return 0;
        }

        private static bool StringArraysEqual(string[] left, string[] right)
        {
            left ??= Array.Empty<string>();
            right ??= Array.Empty<string>();
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TypeMetadataArraysEqual(
            TypeFieldMetadata[] left,
            TypeFieldMetadata[] right
        )
        {
            left ??= Array.Empty<TypeFieldMetadata>();
            right ??= Array.Empty<TypeFieldMetadata>();
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (CompareTypeFieldMetadata(left[i], right[i]) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool RelationalMetadataArraysEqual(
            RelationalTypeMetadata[] left,
            RelationalTypeMetadata[] right
        )
        {
            left ??= Array.Empty<RelationalTypeMetadata>();
            right ??= Array.Empty<RelationalTypeMetadata>();
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (CompareRelationalTypeMetadata(left[i], right[i]) != 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AutoLoadSingletonEntriesEqual(
            AutoLoadSingletonEntry[] left,
            AutoLoadSingletonEntry[] right
        )
        {
            left ??= Array.Empty<AutoLoadSingletonEntry>();
            right ??= Array.Empty<AutoLoadSingletonEntry>();
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                AutoLoadSingletonEntry leftEntry = left[i];
                AutoLoadSingletonEntry rightEntry = right[i];
                if (ReferenceEquals(leftEntry, rightEntry))
                {
                    continue;
                }

                if (leftEntry == null || rightEntry == null)
                {
                    return false;
                }

                if (
                    !string.Equals(
                        leftEntry.typeName,
                        rightEntry.typeName,
                        StringComparison.Ordinal
                    )
                    || leftEntry.kind != rightEntry.kind
                    || leftEntry.loadType != rightEntry.loadType
                )
                {
                    return false;
                }
            }

            return true;
        }

        private static int CompareRelationalTypeMetadata(
            RelationalTypeMetadata left,
            RelationalTypeMetadata right
        )
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            int typeNameComparison = string.CompareOrdinal(left.typeName, right.typeName);
            if (typeNameComparison != 0)
            {
                return typeNameComparison;
            }

            RelationalFieldMetadata[] leftFields =
                left.fields ?? Array.Empty<RelationalFieldMetadata>();
            RelationalFieldMetadata[] rightFields =
                right.fields ?? Array.Empty<RelationalFieldMetadata>();

            int lengthComparison = leftFields.Length.CompareTo(rightFields.Length);
            if (lengthComparison != 0)
            {
                return lengthComparison;
            }

            for (int i = 0; i < leftFields.Length; i++)
            {
                int fieldComparison = CompareRelationalFieldMetadata(leftFields[i], rightFields[i]);
                if (fieldComparison != 0)
                {
                    return fieldComparison;
                }
            }

            return 0;
        }

        private static int CompareRelationalFieldMetadata(
            RelationalFieldMetadata left,
            RelationalFieldMetadata right
        )
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            int fieldNameComparison = string.CompareOrdinal(left.fieldName, right.fieldName);
            if (fieldNameComparison != 0)
            {
                return fieldNameComparison;
            }

            int attributeComparison = left.attributeKind.CompareTo(right.attributeKind);
            if (attributeComparison != 0)
            {
                return attributeComparison;
            }

            int fieldKindComparison = left.fieldKind.CompareTo(right.fieldKind);
            if (fieldKindComparison != 0)
            {
                return fieldKindComparison;
            }

            int elementTypeComparison = string.CompareOrdinal(
                left.elementTypeName,
                right.elementTypeName
            );
            if (elementTypeComparison != 0)
            {
                return elementTypeComparison;
            }

            if (left.isInterface == right.isInterface)
            {
                return 0;
            }

            return left.isInterface ? -1 : 1;
        }

        private static AutoLoadSingletonEntry[] SortAutoLoadSingletonEntries(
            AutoLoadSingletonEntry[] entries
        )
        {
            if (entries == null || entries.Length == 0)
            {
                return Array.Empty<AutoLoadSingletonEntry>();
            }

            List<AutoLoadSingletonEntry> result = new(entries.Length);
            foreach (AutoLoadSingletonEntry entry in entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.typeName))
                {
                    continue;
                }

                result.Add(new AutoLoadSingletonEntry(entry.typeName, entry.kind, entry.loadType));
            }

            if (result.Count == 0)
            {
                return Array.Empty<AutoLoadSingletonEntry>();
            }

            result.Sort((left, right) => string.CompareOrdinal(left.typeName, right.typeName));
            return result.ToArray();
        }
#endif

        /// <summary>
        /// Categorizes a relational attribute reference discovered on an <see cref="AttributesComponent"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Relational attributes allow a component to expose references to related components so modifications can propagate
        /// (e.g., parent/child links for hierarchical buffs). These values are serialized into the cache to avoid runtime reflection.
        /// </para>
        /// <para>
        /// Typical usage happens via auto-generated metadata; you generally do not set this manually.
        /// </para>
        /// </remarks>
        public enum RelationalAttributeKind : byte
        {
            [Obsolete("Default uninitialized value - should never be used")]
            Unknown = 0,

            /// <summary>
            /// The relational field points to a parent component.
            /// </summary>
            Parent = 1,

            /// <summary>
            /// The relational field points to a child component.
            /// </summary>
            Child = 2,

            /// <summary>
            /// The relational field points to a sibling component.
            /// </summary>
            Sibling = 3,
        }

        /// <summary>
        /// Describes the collection shape of a relational field captured in metadata.
        /// </summary>
        public enum FieldKind : byte
        {
            [Obsolete("Default uninitialized value - should never be used")]
            None = 0,

            /// <summary>
            /// A single reference value.
            /// </summary>
            Single = 1,

            /// <summary>
            /// An array of values.
            /// </summary>
            Array = 2,

            /// <summary>
            /// A <see cref="List{T}"/> of values.
            /// </summary>
            List = 3,

            /// <summary>
            /// A <see cref="HashSet{T}"/> of values.
            /// </summary>
            HashSet = 4,
        }

        /// <summary>
        /// Serializable entry describing attribute field names for a single component type.
        /// </summary>
        [Serializable]
        public sealed class TypeFieldMetadata
        {
            /// <summary>
            /// Assembly-qualified component type name.
            /// </summary>
            public string typeName;

            /// <summary>
            /// Attribute field names discovered on the component.
            /// </summary>
            public string[] fieldNames;

            /// <summary>
            /// Creates a new metadata entry for a component type.
            /// </summary>
            /// <param name="typeName">Assembly-qualified name of the component type.</param>
            /// <param name="fieldNames">Attribute field names found on that type.</param>
            public TypeFieldMetadata(string typeName, string[] fieldNames)
            {
                this.typeName = typeName;
                this.fieldNames = fieldNames;
            }
        }

        /// <summary>
        /// Serializable entry describing a relational attribute field on a component.
        /// </summary>
        [Serializable]
        public sealed class RelationalFieldMetadata
        {
            /// <summary>
            /// The name of the relational field on the component.
            /// </summary>
            public string fieldName;

            /// <summary>
            /// The relationship classification (parent/child/sibling).
            /// </summary>
            public RelationalAttributeKind attributeKind;

            /// <summary>
            /// The collection shape of the field (single, array, list, hashset).
            /// </summary>
            public FieldKind fieldKind;

            /// <summary>
            /// The assembly-qualified element type name for the field (for collections) or the field type (for singles).
            /// </summary>
            public string elementTypeName;

            /// <summary>
            /// Indicates whether the element type is an interface (affects resolution and validation).
            /// </summary>
            public bool isInterface;

            /// <summary>
            /// Creates a relational metadata entry for a component field.
            /// </summary>
            /// <param name="fieldName">The field name on the component.</param>
            /// <param name="attributeKind">How the field relates to other components.</param>
            /// <param name="fieldKind">Collection shape of the field.</param>
            /// <param name="elementTypeName">Assembly-qualified element or field type.</param>
            /// <param name="isInterface">Whether the element type is an interface.</param>
            public RelationalFieldMetadata(
                string fieldName,
                RelationalAttributeKind attributeKind,
                FieldKind fieldKind,
                string elementTypeName,
                bool isInterface
            )
            {
                this.fieldName = fieldName;
                this.attributeKind = attributeKind;
                this.fieldKind = fieldKind;
                this.elementTypeName = elementTypeName;
                this.isInterface = isInterface;
            }
        }

        /// <summary>
        /// Runtime-resolved relational field metadata with <see cref="Type"/> references resolved.
        /// </summary>
        public readonly struct ResolvedRelationalFieldMetadata
        {
            /// <summary>
            /// Creates a resolved relational metadata entry.
            /// </summary>
            /// <param name="fieldName">The relational field name on the component.</param>
            /// <param name="attributeKind">Relationship classification.</param>
            /// <param name="fieldKind">Collection shape of the field.</param>
            /// <param name="elementType">Resolved element type (or field type for singles).</param>
            /// <param name="isInterface">Whether the element type is an interface.</param>
            public ResolvedRelationalFieldMetadata(
                string fieldName,
                RelationalAttributeKind attributeKind,
                FieldKind fieldKind,
                Type elementType,
                bool isInterface
            )
            {
                FieldName = fieldName;
                AttributeKind = attributeKind;
                FieldKind = fieldKind;
                ElementType = elementType;
                IsInterface = isInterface;
            }

            /// <summary>
            /// The name of the relational field.
            /// </summary>
            public string FieldName { get; }

            /// <summary>
            /// Relationship classification for the field.
            /// </summary>
            public RelationalAttributeKind AttributeKind { get; }

            /// <summary>
            /// Collection shape of the field.
            /// </summary>
            public FieldKind FieldKind { get; }

            /// <summary>
            /// Resolved CLR type for the element/field.
            /// </summary>
            public Type ElementType { get; }

            /// <summary>
            /// Indicates if the element type is an interface.
            /// </summary>
            public bool IsInterface { get; }
        }

        /// <summary>
        /// Serializable entry describing all relational fields for a component type.
        /// </summary>
        [Serializable]
        public sealed class RelationalTypeMetadata
        {
            /// <summary>
            /// Assembly-qualified component type name.
            /// </summary>
            public string typeName;

            /// <summary>
            /// Relational attribute fields discovered on the component.
            /// </summary>
            public RelationalFieldMetadata[] fields;

            /// <summary>
            /// Creates relational metadata for a component type.
            /// </summary>
            /// <param name="typeName">Assembly-qualified name of the component type.</param>
            /// <param name="fields">Relational fields discovered on that type.</param>
            public RelationalTypeMetadata(string typeName, RelationalFieldMetadata[] fields)
            {
                this.typeName = typeName;
                this.fields = fields;
            }
        }

        /// <summary>
        /// Serialized entry describing an auto-loaded singleton dependency.
        /// </summary>
        [Serializable]
        public sealed class AutoLoadSingletonEntry
        {
            /// <summary>
            /// Assembly-qualified type name of the singleton to load.
            /// </summary>
            public string typeName;

            /// <summary>
            /// Whether the singleton should be created, fetched, or ignored.
            /// </summary>
            public SingletonAutoLoadKind kind;

            /// <summary>
            /// Unity load phase used to initialize the singleton.
            /// </summary>
            public RuntimeInitializeLoadType loadType;

            /// <summary>
            /// Default constructor for serialization.
            /// </summary>
            public AutoLoadSingletonEntry() { }

            /// <summary>
            /// Creates a new singleton auto-load entry.
            /// </summary>
            /// <param name="typeName">Assembly-qualified type name to load.</param>
            /// <param name="kind">How the singleton should be handled.</param>
            /// <param name="loadType">Unity load phase for initialization.</param>
            public AutoLoadSingletonEntry(
                string typeName,
                SingletonAutoLoadKind kind,
                RuntimeInitializeLoadType loadType
            )
            {
                this.typeName = typeName;
                this.kind = kind;
                this.loadType = loadType;
            }
        }

        // Compound key for element type lookup
        private readonly struct ElementTypeKey : IEquatable<ElementTypeKey>
        {
            private readonly Type _componentType;
            private readonly string _fieldName;

            public ElementTypeKey(Type componentType, string fieldName)
            {
                _componentType = componentType;
                _fieldName = fieldName;
            }

            public bool Equals(ElementTypeKey other)
            {
                return _componentType == other._componentType
                    && string.Equals(_fieldName, other._fieldName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is ElementTypeKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return Objects.HashCode(_componentType, _fieldName);
            }
        }
    }
}
