// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tags
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using global::UnityEditor;
    using global::UnityEngine;
    using UnityHelpers.Core.Attributes;
    using UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using static UnityHelpers.Tags.AttributeMetadataCache;
    using ReflectionHelpers = WallstopStudios.UnityHelpers.Core.Helper.ReflectionHelpers;

    /// <summary>
    /// Editor script that generates AttributeMetadataCache at edit-time using TypeCache.
    /// This eliminates the need for runtime reflection.
    /// </summary>
    [InitializeOnLoad]
    public static class AttributeMetadataCacheGenerator
    {
        static AttributeMetadataCacheGenerator()
        {
            EditorApplication.delayCall += GenerateCache;
        }

        internal static void GenerateCache()
        {
            /*
                Skip automatic cache generation during test runs to avoid Unity's internal modal dialogs
                when asset operations fail, unless explicitly allowed.
            */
            if (
                EditorUi.Suppress
                && !ScriptableObjectSingletonCreator.AllowAssetCreationDuringSuppression
            )
            {
                return;
            }

            try
            {
                // Gather all types derived from AttributesComponent, with a robust fallback
                List<Type> attributeComponentTypes = FindAttributeComponentTypes();

                // Collect all unique attribute field names across all types
                HashSet<string> allAttributeNames = new(StringComparer.Ordinal);
                List<TypeFieldMetadata> typeMetadataList = new();

                foreach (Type type in attributeComponentTypes)
                {
                    FieldInfo[] fields = ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(
                        type
                    );

                    List<string> fieldNames = new();
                    foreach (FieldInfo field in fields)
                    {
                        if (field.FieldType == typeof(UnityHelpers.Tags.Attribute))
                        {
                            fieldNames.Add(field.Name);
                            allAttributeNames.Add(field.Name);
                        }
                    }

                    if (0 < fieldNames.Count)
                    {
                        typeMetadataList.Add(
                            new TypeFieldMetadata(
                                GetAssemblyQualifiedTypeName(type),
                                fieldNames.ToArray()
                            )
                        );
                    }
                }

                // Sort for consistency
                string[] sortedAttributeNames = allAttributeNames.OrderBy(name => name).ToArray();

                // Scan for relational attributes
                List<RelationalTypeMetadata> relationalMetadataList = ScanRelationalAttributes();

                AutoLoadSingletonEntry[] autoLoadEntries = BuildAutoLoadSingletonEntries();

                /*
                    Both attribute sets, because TypeCache reports only types carrying the attribute
                    DIRECTLY while the runtime resolves SingletonCreationAttribute with inherit: true.
                    An annotated abstract base with a concrete [AutoLoadSingleton] subclass is the
                    contradiction below, and enumerating either set alone cannot see it: the base is
                    skipped as abstract and the subclass carries no SingletonCreationAttribute of its
                    own.
                */
                HashSet<Type> singletonCandidates = new();
                foreach (
                    Type annotated in TypeCache.GetTypesWithAttribute<SingletonCreationAttribute>()
                )
                {
                    singletonCandidates.Add(annotated);
                }

                foreach (
                    Type annotated in TypeCache.GetTypesWithAttribute<AutoLoadSingletonAttribute>()
                )
                {
                    singletonCandidates.Add(annotated);
                }

                foreach (Type annotated in singletonCandidates)
                {
                    if (
                        annotated == null
                        || annotated.IsAbstract
                        || annotated.ContainsGenericParameters
                    )
                    {
                        continue;
                    }

                    if (
                        !ReflectionHelpers.TryGetAttributeSafe(
                            annotated,
                            out SingletonCreationAttribute creation,
                            inherit: true
                        )
                    )
                    {
                        creation = null;
                    }

                    if (
                        !ReflectionHelpers.TryGetAttributeSafe(
                            annotated,
                            out AutoLoadSingletonAttribute autoLoad,
                            inherit: false
                        )
                    )
                    {
                        autoLoad = null;
                    }

                    string problem = DescribeSingletonCreationProblem(
                        annotated,
                        creation,
                        autoLoad
                    );
                    if (problem != null)
                    {
                        Debug.LogWarning(problem);
                    }
                }

                // Get or create the cache asset
                AttributeMetadataCache cache = GetOrCreateCache(out bool metadataChanged);

                // Update the cache
                bool changed = cache.SetMetadata(
                    sortedAttributeNames,
                    typeMetadataList.ToArray(),
                    relationalMetadataList.ToArray(),
                    autoLoadEntries
                );

                if (changed || metadataChanged)
                {
                    AssetDatabase.SaveAssets();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to generate AttributeMetadataCache: {ex}");
            }
        }

        private static List<Type> FindAttributeComponentTypes()
        {
            // Primary: fast TypeCache-based discovery
            List<Type> types = ReflectionHelpers
                .GetTypesDerivedFrom<AttributesComponent>(includeAbstract: false)
                .Where(AttributeMetadataFilters.ShouldSerialize)
                .ToList();

            if (0 < types.Count)
            {
                return types;
            }

            // Fallback: reflection-based scan via ReflectionHelpers
            HashSet<Type> results = new();
            foreach (Type t in ReflectionHelpers.GetAllLoadedTypes())
            {
                if (
                    t is { IsAbstract: false, IsGenericTypeDefinition: false }
                    && typeof(AttributesComponent).IsAssignableFrom(t)
                    && AttributeMetadataFilters.ShouldSerialize(t)
                )
                {
                    results.Add(t);
                }
            }
            return results.ToList();
        }

        private static List<RelationalTypeMetadata> ScanRelationalAttributes()
        {
            List<RelationalTypeMetadata> result = new();

            // Get all Component types using TypeCache with a reflection fallback for robustness
            List<Type> componentTypes = ReflectionHelpers
                .GetTypesDerivedFrom<Component>(includeAbstract: false)
                .Where(type => !type.IsGenericType)
                .Where(AttributeMetadataFilters.ShouldSerialize)
                .ToList();

            if (componentTypes.Count == 0)
            {
                HashSet<Type> results = new();
                foreach (Type t in ReflectionHelpers.GetAllLoadedTypes())
                {
                    if (
                        t != null
                        && typeof(Component).IsAssignableFrom(t)
                        && !t.IsGenericType
                        && AttributeMetadataFilters.ShouldSerialize(t)
                    )
                    {
                        results.Add(t);
                    }
                }
                componentTypes = results.ToList();
            }

            foreach (Type type in componentTypes)
            {
                List<RelationalFieldMetadata> fieldMetadataList = new();

                FieldInfo[] fields = ReflectionHelpers.GetInstanceFieldsIncludingBaseTypes(type);

                foreach (FieldInfo field in fields)
                {
                    RelationalAttributeKind? attributeKind = null;
                    if (
                        ReflectionHelpers.HasAttributeSafe<ParentComponentAttribute>(
                            field,
                            inherit: false
                        )
                    )
                    {
                        attributeKind = RelationalAttributeKind.Parent;
                    }
                    else if (
                        ReflectionHelpers.HasAttributeSafe<ChildComponentAttribute>(
                            field,
                            inherit: false
                        )
                    )
                    {
                        attributeKind = RelationalAttributeKind.Child;
                    }
                    else if (
                        ReflectionHelpers.HasAttributeSafe<SiblingComponentAttribute>(
                            field,
                            inherit: false
                        )
                    )
                    {
                        attributeKind = RelationalAttributeKind.Sibling;
                    }

                    if (!attributeKind.HasValue)
                    {
                        continue;
                    }

                    // Determine field kind and element type
                    Type fieldType = field.FieldType;
                    FieldKind fieldKind;
                    Type elementType;

                    if (fieldType.IsArray)
                    {
                        fieldKind = FieldKind.Array;
                        elementType = fieldType.GetElementType();
                    }
                    else
                    {
                        switch (fieldType.IsGenericType)
                        {
                            case true when fieldType.GetGenericTypeDefinition() == typeof(List<>):
                                fieldKind = FieldKind.List;
                                elementType = fieldType.GenericTypeArguments[0];
                                break;
                            case true
                                when fieldType.GetGenericTypeDefinition() == typeof(HashSet<>):
                                fieldKind = FieldKind.HashSet;
                                elementType = fieldType.GenericTypeArguments[0];
                                break;
                            default:
                                fieldKind = FieldKind.Single;
                                elementType = fieldType;
                                break;
                        }
                    }

                    // Determine if element type is an interface or base type
                    bool isInterface =
                        elementType.IsInterface
                        || (!elementType.IsSealed && elementType != typeof(Component));

                    fieldMetadataList.Add(
                        new RelationalFieldMetadata(
                            field.Name,
                            attributeKind.Value,
                            fieldKind,
                            GetAssemblyQualifiedTypeName(elementType),
                            isInterface
                        )
                    );
                }

                if (0 < fieldMetadataList.Count)
                {
                    result.Add(
                        new RelationalTypeMetadata(
                            GetAssemblyQualifiedTypeName(type),
                            fieldMetadataList.ToArray()
                        )
                    );
                }
            }

            return result;
        }

        private static AutoLoadSingletonEntry[] BuildAutoLoadSingletonEntries()
        {
            List<AutoLoadSingletonEntry> entries = new();
            foreach (Type type in TypeCache.GetTypesWithAttribute<AutoLoadSingletonAttribute>())
            {
                if (type == null || type.IsAbstract || type.ContainsGenericParameters)
                {
                    continue;
                }

                AutoLoadSingletonAttribute attribute =
                    System
                        .Attribute.GetCustomAttributes(
                            type,
                            typeof(AutoLoadSingletonAttribute),
                            inherit: false
                        )
                        .FirstOrDefault() as AutoLoadSingletonAttribute;
                if (attribute == null)
                {
                    continue;
                }

                SingletonAutoLoadKind? kind = ResolveSingletonKind(type);
                if (!kind.HasValue)
                {
                    Debug.LogWarning(
                        $"AttributeMetadataCacheGenerator: {type.FullName} is marked with [AutoLoadSingleton] but does not derive from RuntimeSingleton<> or ScriptableObjectSingleton<>."
                    );
                    continue;
                }

                string typeName = GetAssemblyQualifiedTypeName(type);
                if (string.IsNullOrWhiteSpace(typeName))
                {
                    continue;
                }

                entries.Add(new AutoLoadSingletonEntry(typeName, kind.Value, attribute.LoadType));
            }

            entries.Sort((left, right) => string.CompareOrdinal(left.typeName, right.typeName));
            return entries.ToArray();
        }

        /// <summary>
        /// Reports a <see cref="SingletonCreationAttribute"/> that cannot do what its author meant,
        /// or <c>null</c> when the annotation is sound.
        /// </summary>
        /// <remarks>
        /// The attributes arrive as arguments rather than being read off <paramref name="type"/> so a
        /// test can drive every branch without annotating a deliberately wrong type -- which
        /// <see cref="TypeCache"/> would then find on every editor load, and this method would then
        /// complain about forever. It is the technique <c>RuntimeMismatchSingleton</c> already uses
        /// for the auto-loader's own mismatch rules.
        /// </remarks>
        /// <param name="type">The annotated type.</param>
        /// <param name="creation">Its <see cref="SingletonCreationAttribute"/>, or <c>null</c>.</param>
        /// <param name="autoLoad">Its <see cref="AutoLoadSingletonAttribute"/>, or <c>null</c>.</param>
        /// <returns>A message naming the problem, or <c>null</c>.</returns>
        internal static string DescribeSingletonCreationProblem(
            Type type,
            SingletonCreationAttribute creation,
            AutoLoadSingletonAttribute autoLoad
        )
        {
            if (type == null || creation == null)
            {
                return null;
            }

            if (
                !IsSubclassOfRawGeneric(
                    type,
                    typeof(WallstopStudios.UnityHelpers.Utils.RuntimeSingleton<>)
                )
            )
            {
                return $"AttributeMetadataCacheGenerator: {type.FullName} is marked with [{nameof(SingletonCreationAttribute)}] but does not derive from RuntimeSingleton<>, so the attribute has no effect. ScriptableObjectSingleton<> never creates an asset at runtime and needs no policy.";
            }

            if (creation.Policy != SingletonCreationPolicy.NeverCreate || autoLoad == null)
            {
                return null;
            }

            /*
                AfterSceneLoad is the one phase where an authored instance already exists, so
                auto-loading a NeverCreate singleton there binds the static cache to it. Every earlier
                phase runs before any GameObject exists, so the lookup can only find nothing and the
                pair is a contradiction rather than a choice.
            */
            if (autoLoad.LoadType == RuntimeInitializeLoadType.AfterSceneLoad)
            {
                return null;
            }

            return $"AttributeMetadataCacheGenerator: {type.FullName} is marked [{nameof(AutoLoadSingletonAttribute)}({nameof(RuntimeInitializeLoadType)}.{autoLoad.LoadType})] and [{nameof(SingletonCreationAttribute)}({nameof(SingletonCreationPolicy)}.{nameof(SingletonCreationPolicy.NeverCreate)})]. That phase runs before any scene has loaded, so the auto-load can only find nothing. Use {nameof(RuntimeInitializeLoadType)}.{nameof(RuntimeInitializeLoadType.AfterSceneLoad)} or drop one of the two attributes.";
        }

        private static SingletonAutoLoadKind? ResolveSingletonKind(Type type)
        {
            if (
                IsSubclassOfRawGeneric(
                    type,
                    typeof(WallstopStudios.UnityHelpers.Utils.RuntimeSingleton<>)
                )
            )
            {
                return SingletonAutoLoadKind.Runtime;
            }

            if (
                IsSubclassOfRawGeneric(
                    type,
                    typeof(WallstopStudios.UnityHelpers.Utils.ScriptableObjectSingleton<>)
                )
            )
            {
                return SingletonAutoLoadKind.ScriptableObject;
            }

            return null;
        }

        private static bool IsSubclassOfRawGeneric(Type derived, Type openGeneric)
        {
            Type current = derived;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == openGeneric)
                {
                    return true;
                }

                current = current.BaseType;
            }
            return false;
        }

        private static string GetAssemblyQualifiedTypeName(Type type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            return type.AssemblyQualifiedName ?? type.FullName ?? string.Empty;
        }

        private static AttributeMetadataCache GetOrCreateCache(out bool metadataChanged)
        {
            // Try loading from the expected path first
            const string assetPath =
                "Assets/Resources/Wallstop Studios/Unity Helpers/AttributeMetadataCache.asset";
            const string resourcesLoadPath =
                "Wallstop Studios/Unity Helpers/AttributeMetadataCache";
            const string resourcesFolder = "Wallstop Studios/Unity Helpers";

            AttributeMetadataCache cache = AssetDatabase.LoadAssetAtPath<AttributeMetadataCache>(
                assetPath
            );
            if (cache != null)
            {
                metadataChanged = UpdateMetadataEntry(
                    assetPath,
                    resourcesLoadPath,
                    resourcesFolder
                );
                return cache;
            }

            // Try loading via the singleton Instance (may work if already created)
            cache = AttributeMetadataCache.Instance;
            if (cache != null)
            {
                /*
                    Instance may discover objects at other paths (e.g., backup copies)
                    via Resources.FindObjectsOfTypeAll, which would bypass creation
                    at the correct location.
                */
                string instancePath = AssetDatabase.GetAssetPath(cache);
                if (string.Equals(instancePath, assetPath, StringComparison.OrdinalIgnoreCase))
                {
                    metadataChanged = UpdateMetadataEntry(
                        assetPath,
                        resourcesLoadPath,
                        resourcesFolder
                    );
                    return cache;
                }

                Debug.LogWarning(
                    $"AttributeMetadataCacheGenerator: Instance found at '{instancePath}' instead of expected '{assetPath}'. Creating new asset at the correct path."
                );
            }

            /*
                Create the asset ourselves. Route folder creation through the single batch-safe
                helper, which pauses any active batch and creates each missing segment via
                AssetDatabase.CreateFolder so the parent folder is registered before CreateAsset runs.
            */
            string directory = System.IO.Path.GetDirectoryName(assetPath);
            if (
                !string.IsNullOrEmpty(directory)
                && !AssetDatabaseBatchHelper.EnsureAssetFolder(directory)
            )
            {
                Debug.LogWarning(
                    $"AttributeMetadataCacheGenerator: Failed to ensure folder '{directory.SanitizePath()}'. Skipping cache asset creation."
                );
                metadataChanged = false;
                return null;
            }

            cache = ScriptableObject.CreateInstance<AttributeMetadataCache>();
            using (AssetDatabaseBatchHelper.PauseBatch())
            {
                try
                {
                    AssetDatabase.CreateAsset(cache, assetPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"AttributeMetadataCacheGenerator: Failed to create cache asset: {ex.Message}"
                    );
                    if (cache != null)
                    {
                        UnityEngine.Object.DestroyImmediate(cache);
                    }
                    metadataChanged = false;
                    return null;
                }
            }

            /*
                Reset the cached singleton instance so subsequent Instance calls find the new asset
                instead of returning the stale null cached during the earlier Instance lookup above.
            */
            WallstopStudios.UnityHelpers.Utils.ScriptableObjectSingleton<AttributeMetadataCache>.ClearInstance();

            metadataChanged = UpdateMetadataEntry(assetPath, resourcesLoadPath, resourcesFolder);

            return cache;
        }

        private static bool UpdateMetadataEntry(
            string assetPath,
            string resourcesLoadPath,
            string resourcesFolder
        )
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (!string.IsNullOrEmpty(guid))
            {
                return ScriptableObjectSingletonMetadataUtility.UpdateEntry(
                    typeof(AttributeMetadataCache),
                    resourcesLoadPath,
                    resourcesFolder,
                    guid
                );
            }

            return false;
        }
    }
}
