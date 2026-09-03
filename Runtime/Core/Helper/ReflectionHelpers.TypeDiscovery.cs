// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if !((UNITY_WEBGL && !UNITY_EDITOR) || ENABLE_IL2CPP)
#define EMIT_DYNAMIC_IL
#define SUPPORT_EXPRESSION_COMPILE
#endif

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    // ReflectionHelpers.TypeDiscovery.cs - Type scanning, assembly discovery, and member lookup
    // See ReflectionHelpers.cs for full architecture documentation
    public static partial class ReflectionHelpers
    {
        /// <summary>
        /// The standard property name for C# indexers ("Item").
        /// Used to avoid magic strings when looking up indexer properties via reflection.
        /// </summary>
        private const string IndexerPropertyName = "Item";

        /// <summary>
        /// Returns all loaded types across accessible assemblies, swallowing reflection errors.
        /// </summary>
        public static IEnumerable<Type> GetAllLoadedTypes()
        {
            return GetAllLoadedAssemblies()
                .SelectMany(assembly => GetTypesFromAssembly(assembly))
                .Where(type => type != null);
        }

        /// <summary>
        /// Returns all loaded assemblies discoverable by the current AppDomain.
        /// </summary>
        public static IEnumerable<Assembly> GetAllLoadedAssemblies()
        {
            try
            {
                return AppDomain
                    .CurrentDomain.GetAssemblies()
                    .Where(assembly => assembly != null && !assembly.IsDynamic);
            }
            catch
            {
                return Enumerable.Empty<Assembly>();
            }
        }

        /// <summary>
        /// Safely gets all types from the specified assembly, returning an empty array on failure.
        /// </summary>
        public static Type[] GetTypesFromAssembly(Assembly assembly)
        {
            if (assembly == null)
            {
                return Type.EmptyTypes;
            }

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null).ToArray();
            }
            catch
            {
                return Type.EmptyTypes;
            }
        }

        /// <summary>
        /// Attempts to resolve a type by name using Type.GetType first, then scans loaded assemblies.
        /// Returns null if not found. Results are cached.
        /// </summary>
        public static Type TryResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            if (TypeResolutionCache.TryGetValue(typeName, out Type cached))
            {
                return cached;
            }

            Type resolved = null;
            try
            {
                resolved = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
            }
            catch
            {
                resolved = null;
            }

            if (resolved == null)
            {
                foreach (Assembly asm in GetAllLoadedAssemblies())
                {
                    try
                    {
                        resolved = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
                        if (resolved != null)
                        {
                            break;
                        }
                    }
                    catch
                    {
                        // swallow and continue
                    }
                }
            }

            TypeResolutionCache[typeName] = resolved;
            return resolved;
        }

        /// <summary>
        /// Gets all loaded types derived from T. In editor, uses TypeCache for speed.
        /// </summary>
        public static IEnumerable<Type> GetTypesDerivedFrom<T>(bool includeAbstract = false)
        {
#if UNITY_EDITOR
            try
            {
                TypeCache.TypeCollection list = TypeCache.GetTypesDerivedFrom<T>();
                return list.Where(t =>
                    t != null && (includeAbstract || (t.IsClass && !t.IsAbstract))
                );
            }
            catch
            {
                // fall through to runtime path
            }
#endif
            Type baseType = typeof(T);
            return GetAllLoadedTypes()
                .Where(t =>
                    t != null
                    && baseType.IsAssignableFrom(t)
                    && (includeAbstract || (t.IsClass && !t.IsAbstract))
                );
        }

        /// <summary>
        /// Gets all loaded types derived from the specified base type. In editor, uses TypeCache for speed.
        /// </summary>
        public static IEnumerable<Type> GetTypesDerivedFrom(
            Type baseType,
            bool includeAbstract = false
        )
        {
            if (baseType == null)
            {
                return Array.Empty<Type>();
            }
#if UNITY_EDITOR
            try
            {
                TypeCache.TypeCollection list = TypeCache.GetTypesDerivedFrom(baseType);
                return list.Where(t =>
                    t != null && (includeAbstract || (t.IsClass && !t.IsAbstract))
                );
            }
            catch
            {
                // fall through
            }
#endif
            return GetAllLoadedTypes()
                .Where(t =>
                    t != null
                    && baseType.IsAssignableFrom(t)
                    && (includeAbstract || (t.IsClass && !t.IsAbstract))
                );
        }

        /// <summary>
        /// Safely gets all types from the assembly with the specified name, if loaded.
        /// </summary>
        public static Type[] GetTypesFromAssemblyName(string assemblyName)
        {
            try
            {
                Assembly assembly = Assembly.Load(assemblyName);
                return GetTypesFromAssembly(assembly);
            }
            catch
            {
                return Type.EmptyTypes;
            }
        }

        /// <summary>
        /// Finds all types with a given attribute across loaded assemblies.
        /// </summary>
        public static IEnumerable<Type> GetTypesWithAttribute<TAttribute>()
            where TAttribute : Attribute
        {
            return GetAllLoadedTypes().Where(type => HasAttributeSafe<TAttribute>(type));
        }

        /// <summary>
        /// Finds all types with a given attribute, using TypeCache in editor when available.
        /// </summary>
        public static IEnumerable<Type> GetTypesWithAttribute<TAttribute>(bool includeAbstract)
            where TAttribute : Attribute
        {
#if UNITY_EDITOR
            try
            {
                TypeCache.TypeCollection types = TypeCache.GetTypesWithAttribute<TAttribute>();
                return types.Where(t =>
                    t != null && (includeAbstract || (t.IsClass && !t.IsAbstract))
                );
            }
            catch
            {
                // fall through
            }
#endif
            return GetAllLoadedTypes()
                .Where(t =>
                    t != null
                    && (includeAbstract || (t.IsClass && !t.IsAbstract))
                    && HasAttributeSafe<TAttribute>(t)
                );
        }

        /// <summary>
        /// Finds all types with a given attribute across loaded assemblies (non-generic overload).
        /// </summary>
        public static IEnumerable<Type> GetTypesWithAttribute(Type attributeType)
        {
            if (attributeType == null || !typeof(Attribute).IsAssignableFrom(attributeType))
            {
                return Enumerable.Empty<Type>();
            }

            return GetAllLoadedTypes().Where(type => HasAttributeSafe(type, attributeType));
        }

        /// <summary>
        /// Gets all types derived from <see cref="UnityEngine.Component"/>.
        /// </summary>
        public static IEnumerable<Type> GetComponentTypes(bool includeAbstract = false)
        {
            return GetTypesDerivedFrom(typeof(UnityEngine.Component), includeAbstract);
        }

        /// <summary>
        /// Gets all types derived from <see cref="UnityEngine.ScriptableObject"/>.
        /// </summary>
        public static IEnumerable<Type> GetScriptableObjectTypes(bool includeAbstract = false)
        {
            return GetTypesDerivedFrom(typeof(UnityEngine.ScriptableObject), includeAbstract);
        }

        /// <summary>
        /// Finds all methods with a given attribute. In editor, uses TypeCache for speed.
        /// </summary>
        public static IEnumerable<MethodInfo> GetMethodsWithAttribute<TAttribute>(
            Type within = null,
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
        )
            where TAttribute : Attribute
        {
#if UNITY_EDITOR
            try
            {
                TypeCache.MethodCollection methods =
                    TypeCache.GetMethodsWithAttribute<TAttribute>();
                IEnumerable<MethodInfo> filtered = methods;
                if (within != null)
                {
                    filtered = filtered.Where(m => m?.DeclaringType == within);
                }
                return filtered.Where(m => m != null);
            }
            catch
            {
                // fall through
            }
#endif
            if (within != null)
            {
                return SafeGetMethods(within, flags)
                    .Where(m => m != null && HasAttributeSafe<TAttribute>(m));
            }
            return GetAllLoadedTypes()
                .SelectMany(t => SafeGetMethods(t, flags))
                .Where(m => m != null && HasAttributeSafe<TAttribute>(m));
        }

        /// <summary>
        /// Finds all fields with a given attribute. In editor, uses TypeCache for speed.
        /// </summary>
        /// <remarks>
        /// A field <paramref name="within"/> inherits counts, whatever its accessibility. Both
        /// branches used to disagree with that and with each other: the editor branch compared
        /// <see cref="MemberInfo.DeclaringType"/> for equality, dropping every inherited field, and
        /// the reflection branch asked for fields without <see cref="BindingFlags.DeclaredOnly"/>,
        /// which never returns an inherited private one. So an <c>[Attribute]</c> on a base class's
        /// private field was invisible to both.
        /// Unlike relational binding, a name hidden with <c>new</c> reports both declarations here:
        /// each is a real field, and a caller asking which fields carry an attribute wants both.
        /// </remarks>
        public static IEnumerable<FieldInfo> GetFieldsWithAttribute<TAttribute>(
            Type within = null,
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
        )
            where TAttribute : Attribute
        {
#if UNITY_EDITOR
            try
            {
                TypeCache.FieldInfoCollection fields =
                    TypeCache.GetFieldsWithAttribute<TAttribute>();
                IEnumerable<FieldInfo> filtered = fields;
                if (within != null)
                {
                    filtered = filtered.Where(f =>
                        f?.DeclaringType != null && f.DeclaringType.IsAssignableFrom(within)
                    );
                }
                return filtered.Where(f => f != null);
            }
            catch
            {
                // fall through
            }
#endif
            if (within != null)
            {
                return EnumerateDeclaredAndInheritedFields(within, flags)
                    .Where(f => f != null && HasAttributeSafe<TAttribute>(f));
            }
            return GetAllLoadedTypes()
                .SelectMany(t => SafeGetFields(t, flags))
                .Where(f => f != null && HasAttributeSafe<TAttribute>(f));
        }

        /// <summary>
        /// Finds all properties with a given attribute.
        /// </summary>
        public static IEnumerable<PropertyInfo> GetPropertiesWithAttribute<TAttribute>(
            Type within = null,
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
        )
            where TAttribute : Attribute
        {
            if (within != null)
            {
                return SafeGetProperties(within, flags)
                    .Where(p => p != null && HasAttributeSafe<TAttribute>(p));
            }
            return GetAllLoadedTypes()
                .SelectMany(t => SafeGetProperties(t, flags))
                .Where(p => p != null && HasAttributeSafe<TAttribute>(p));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IEnumerable<MethodInfo> SafeGetMethods(Type t, BindingFlags flags)
        {
            try
            {
                return t?.GetMethods(flags) ?? Array.Empty<MethodInfo>();
            }
            catch
            {
                return Array.Empty<MethodInfo>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        /// <summary>
        /// Walks <paramref name="type"/> and its base types, asking each level only for what it
        /// declares, so an inherited private field is reported rather than skipped.
        /// </summary>
        private static IEnumerable<FieldInfo> EnumerateDeclaredAndInheritedFields(
            Type type,
            BindingFlags flags
        )
        {
            BindingFlags declaredFlags = flags | BindingFlags.DeclaredOnly;
            Type current = type;
            while (current != null && current != typeof(object))
            {
                foreach (FieldInfo field in SafeGetFields(current, declaredFlags))
                {
                    yield return field;
                }

                current = SafeGetBaseType(current);
            }
        }

        private static Type SafeGetBaseType(Type type)
        {
            try
            {
                return type.BaseType;
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<FieldInfo> SafeGetFields(Type t, BindingFlags flags)
        {
            try
            {
                return t?.GetFields(flags) ?? Array.Empty<FieldInfo>();
            }
            catch
            {
                return Array.Empty<FieldInfo>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IEnumerable<PropertyInfo> SafeGetProperties(Type t, BindingFlags flags)
        {
            try
            {
                return t?.GetProperties(flags) ?? Array.Empty<PropertyInfo>();
            }
            catch
            {
                return Array.Empty<PropertyInfo>();
            }
        }

        /// <summary>
        /// Tries to get a field by name from a type with caching.
        /// </summary>
        public static bool TryGetField(
            Type type,
            string name,
            out FieldInfo field,
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
        )
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                field = null;
                return false;
            }

            (Type type, string name, BindingFlags flags) key = (type, name, flags);
#if SINGLE_THREADED
            if (!FieldLookup.TryGetValue(key, out FieldInfo resolved))
            {
                resolved = type.GetField(name, flags);
                FieldLookup[key] = resolved;
            }
#else
            FieldInfo resolved = FieldLookup.GetOrAdd(
                key,
                static k => k.type.GetField(k.name, k.flags)
            );
#endif
            field = resolved;
            return resolved != null;
        }

        /// <summary>
        /// Tries to get a property by name from a type with caching.
        /// </summary>
        public static bool TryGetProperty(
            Type type,
            string name,
            out PropertyInfo property,
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
        )
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                property = null;
                return false;
            }

            (Type type, string name, BindingFlags flags) key = (type, name, flags);
#if SINGLE_THREADED
            if (!PropertyLookup.TryGetValue(key, out PropertyInfo resolved))
            {
                resolved = type.GetProperty(name, flags);
                PropertyLookup[key] = resolved;
            }
#else
            PropertyInfo resolved = PropertyLookup.GetOrAdd(
                key,
                static k => k.type.GetProperty(k.name, k.flags)
            );
#endif
            property = resolved;
            return resolved != null;
        }

        /// <summary>
        /// Tries to get an indexer property (named "Item") with specific return type and index parameter types.
        /// This is useful when a type implements multiple interfaces with indexers (e.g., both
        /// <c>IDictionary&lt;TKey, TValue&gt;</c> and <c>IDictionary</c>), which would cause
        /// <see cref="AmbiguousMatchException"/> with <see cref="Type.GetProperty(string, BindingFlags)"/>.
        /// </summary>
        /// <param name="type">The type to search for the indexer.</param>
        /// <param name="returnType">The return type of the indexer.</param>
        /// <param name="indexParameterTypes">The types of the index parameters.</param>
        /// <param name="property">The found property, or null if not found.</param>
        /// <returns>True if the indexer was found, false otherwise.</returns>
        /// <example>
        /// <code><![CDATA[
        /// // Get the generic indexer from SerializableDictionary<string, int>
        /// Type dictType = typeof(SerializableDictionary<string, int>);
        /// if (ReflectionHelpers.TryGetIndexerProperty(dictType, typeof(int), new[] { typeof(string) }, out var indexer))
        /// {
        ///     var setter = ReflectionHelpers.GetIndexerSetter(indexer);
        ///     setter(myDict, 42, new object[] { "key" });
        /// }
        /// ]]></code>
        /// </example>
        public static bool TryGetIndexerProperty(
            Type type,
            Type returnType,
            Type[] indexParameterTypes,
            out PropertyInfo property
        )
        {
            if (type == null || returnType == null || indexParameterTypes == null)
            {
                property = null;
                return false;
            }

            string paramsSig = BuildIndexerSignatureKey(indexParameterTypes);
            (Type type, Type returnType, string indexParamsSig) key = (type, returnType, paramsSig);
#if SINGLE_THREADED
            if (!IndexerLookup.TryGetValue(key, out PropertyInfo resolved))
            {
                PropertyInfo found = type.GetProperty(
                    IndexerPropertyName,
                    returnType,
                    indexParameterTypes
                );
                /*
                    Unity's Mono may not strictly validate return type in GetProperty,
                    so we explicitly check that the found property matches our criteria
                */
                resolved = ValidateIndexerProperty(found, returnType, indexParameterTypes);
                IndexerLookup[key] = resolved;
            }
#else
            PropertyInfo resolved = IndexerLookup.GetOrAdd(
                key,
                static (k, state) =>
                {
                    PropertyInfo found = k.type.GetProperty(
                        IndexerPropertyName,
                        k.returnType,
                        state
                    );
                    /*
                        Unity's Mono may not strictly validate return type in GetProperty,
                        so we explicitly check that the found property matches our criteria
                    */
                    return ValidateIndexerProperty(found, k.returnType, state);
                },
                indexParameterTypes
            );
#endif
            property = resolved;
            return resolved != null;
        }

        private static PropertyInfo ValidateIndexerProperty(
            PropertyInfo found,
            Type expectedReturnType,
            Type[] expectedIndexParameterTypes
        )
        {
            if (found == null)
            {
                return null;
            }

            if (found.PropertyType != expectedReturnType)
            {
                return null;
            }

            ParameterInfo[] indexParams = found.GetIndexParameters();
            if (indexParams.Length != expectedIndexParameterTypes.Length)
            {
                return null;
            }

            for (int i = 0; i < indexParams.Length; i++)
            {
                if (indexParams[i].ParameterType != expectedIndexParameterTypes[i])
                {
                    return null;
                }
            }

            return found;
        }

        private static string BuildIndexerSignatureKey(Type[] paramTypes)
        {
            if (paramTypes == null || paramTypes.Length == 0)
            {
                return "[]";
            }

            return "[" + string.Join(",", paramTypes.Select(t => t?.FullName ?? "null")) + "]";
        }

        /// <summary>
        /// Tries to get a method by name from a type with caching.
        /// </summary>
        public static bool TryGetMethod(
            Type type,
            string name,
            out MethodInfo method,
            Type[] paramTypes = null,
            BindingFlags flags =
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic
        )
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                method = null;
                return false;
            }

            string sig = BuildMethodSignatureKey(name, paramTypes);
            (Type type, string sig, BindingFlags flags) key = (type, sig, flags);
#if SINGLE_THREADED
            if (!MethodLookup.TryGetValue(key, out MethodInfo resolved))
            {
                resolved =
                    paramTypes == null
                        ? type.GetMethod(name, flags)
                        : type.GetMethod(
                            name,
                            flags,
                            binder: null,
                            types: paramTypes,
                            modifiers: null
                        );
                MethodLookup[key] = resolved;
            }
#else
            MethodInfo resolved = MethodLookup.GetOrAdd(
                key,
                static (tuple, state) =>
                {
                    if (state.parameterTypes == null)
                    {
                        return tuple.type.GetMethod(state.methodName, tuple.flags);
                    }
                    return tuple.type.GetMethod(
                        state.methodName,
                        tuple.flags,
                        binder: null,
                        types: state.parameterTypes,
                        modifiers: null
                    );
                },
                (methodName: name, parameterTypes: paramTypes)
            );
#endif
            method = resolved;
            return resolved != null;
        }

        private static string BuildMethodSignatureKey(string name, Type[] paramTypes)
        {
            if (paramTypes == null || paramTypes.Length == 0)
            {
                return name + "()";
            }

            return name
                + "("
                + string.Join(",", paramTypes.Select(t => t?.FullName ?? "null"))
                + ")";
        }
    }
}
