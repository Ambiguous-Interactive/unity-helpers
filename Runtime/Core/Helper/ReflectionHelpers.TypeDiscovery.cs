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
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Utils;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    public static partial class ReflectionHelpers
    {
        /// <summary>
        /// The standard property name for C# indexers ("Item").
        /// Used to avoid magic strings when looking up indexer properties via reflection.
        /// </summary>
        private const string IndexerPropertyName = "Item";

        /// <summary>
        /// Resolves a type by exact name, then by a unique loaded type when a composite name moved assemblies.
        /// </summary>
        /// <param name="typeName">The type name to resolve.</param>
        /// <returns>The resolved type, or <c>null</c> when no loaded assembly declares it.</returns>
        /// <remarks>
        /// Only a successful resolution is cached, so the cache is bounded by the types actually
        /// loaded. A name is routinely payload data -- a serialized <c>Type</c> field naming a class
        /// a later build renamed -- and caching those would grow without bound on a string the
        /// process can never use. It would also be wrong: an assembly loaded afterwards can make a
        /// name resolvable, and a cached failure would outlive it.
        /// </remarks>
        private static readonly Func<AssemblyName, Assembly> PlaceholderAssemblyResolver =
            ResolvePlaceholderAssembly;

        /// <summary>
        /// Returns all loaded types across accessible assemblies, swallowing reflection errors.
        /// </summary>
        /// <remarks>Captures the assembly selection when called and reads each assembly's types during enumeration.</remarks>
        public static IEnumerable<Type> GetAllLoadedTypes()
        {
            return EnumerateLoadedTypes(GetAllLoadedAssemblies());
        }

        /// <summary>
        /// Returns all loaded assemblies discoverable by the current AppDomain.
        /// </summary>
        /// <remarks>Repeated enumeration retains the captured assembly snapshot and excludes dynamic assemblies.</remarks>
        public static IEnumerable<Assembly> GetAllLoadedAssemblies()
        {
            try
            {
#if UNITY_6000_6_OR_NEWER
                return FilterLoadedAssemblies(
                    UnityEngine.Assemblies.CurrentAssemblies.GetLoadedAssemblies()
                );
#else
                return FilterLoadedAssemblies(AppDomain.CurrentDomain.GetAssemblies());
#endif
            }
            catch
            {
                return Array.Empty<Assembly>();
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
                Type[] types = e.Types;
                if (types == null || types.Length == 0)
                {
                    return Type.EmptyTypes;
                }
                int count = 0;
                foreach (Type type in types)
                {
                    if (type != null)
                    {
                        ++count;
                    }
                }
                if (count == 0)
                {
                    return Type.EmptyTypes;
                }
                Type[] loaded = new Type[count];
                int index = 0;
                foreach (Type type in types)
                {
                    if (type != null)
                    {
                        loaded[index++] = type;
                    }
                }
                return loaded;
            }
            catch
            {
                return Type.EmptyTypes;
            }
        }

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

            if (resolved != null)
            {
                TypeResolutionCache[typeName] = resolved;
                return resolved;
            }

            // Bypass Assembly.GetType for composites: IL2CPP can crash on stale array metadata.
            if (0 <= typeName.IndexOf('['))
            {
                return ResolveCompositeType(typeName);
            }

            foreach (Assembly asm in GetAllLoadedAssemblies())
            {
                try
                {
                    resolved = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
                    if (resolved != null)
                    {
                        TypeResolutionCache[typeName] = resolved;
                        return resolved;
                    }
                }
                catch { }
            }

            return null;
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
                return FilterCachedTypes(list, includeAbstract);
            }
            catch { }
#endif
            Type baseType = typeof(T);
            return FilterDerivedTypes(GetAllLoadedTypes(), baseType, includeAbstract);
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
                return FilterCachedTypes(list, includeAbstract);
            }
            catch { }
#endif
            return FilterDerivedTypes(GetAllLoadedTypes(), baseType, includeAbstract);
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
            return FilterAttributedTypes(
                GetAllLoadedTypes(),
                typeof(TAttribute),
                restrictKinds: false,
                includeAbstract: true
            );
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
                return FilterCachedTypes(types, includeAbstract);
            }
            catch { }
#endif
            return FilterAttributedTypes(
                GetAllLoadedTypes(),
                typeof(TAttribute),
                restrictKinds: true,
                includeAbstract
            );
        }

        /// <summary>
        /// Finds all types with a given attribute across loaded assemblies (non-generic overload).
        /// </summary>
        public static IEnumerable<Type> GetTypesWithAttribute(Type attributeType)
        {
            if (attributeType == null || !typeof(Attribute).IsAssignableFrom(attributeType))
            {
                return Type.EmptyTypes;
            }

            return FilterAttributedTypes(
                GetAllLoadedTypes(),
                attributeType,
                restrictKinds: false,
                includeAbstract: true
            );
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
                return FilterCachedMethods(methods, within);
            }
            catch { }
#endif
            if (within != null)
            {
                return FilterAttributedMembers(SafeGetMethods(within, flags), typeof(TAttribute));
            }
            return EnumerateAttributedMethods(GetAllLoadedTypes(), typeof(TAttribute), flags);
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
                return FilterCachedFields(fields, within);
            }
            catch { }
#endif
            if (within != null)
            {
                return FilterAttributedMembers(
                    EnumerateDeclaredAndInheritedFields(within, flags),
                    typeof(TAttribute)
                );
            }
            return EnumerateAttributedFields(GetAllLoadedTypes(), typeof(TAttribute), flags);
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
                return FilterAttributedMembers(
                    SafeGetProperties(within, flags),
                    typeof(TAttribute)
                );
            }
            return EnumerateAttributedProperties(GetAllLoadedTypes(), typeof(TAttribute), flags);
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
                // Unity Mono can return a property with the wrong return type; validate it explicitly.
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
                    // Unity Mono can return a property with the wrong return type; validate it explicitly.
                    return ValidateIndexerProperty(found, k.returnType, state);
                },
                indexParameterTypes
            );
#endif
            property = resolved;
            return resolved != null;
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

        internal static Type ResolveUniqueLoadedType(string fullName, List<Assembly> assemblies)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Type resolved = null;
            foreach (Assembly assembly in assemblies)
            {
                try
                {
                    Type candidate = assembly.GetType(fullName, throwOnError: false);
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (resolved != null && !ReferenceEquals(resolved, candidate))
                    {
                        return null;
                    }

                    resolved = candidate;
                }
                catch { }
            }

            return resolved;
        }

        internal static string GetAssemblyQualifiedName(Type type)
        {
            if (type == null)
            {
                return null;
            }

            Type elementType = type;
            while (elementType.IsArray)
            {
                elementType = elementType.GetElementType();
            }

            if (
                !elementType.IsGenericType
                || elementType.IsGenericTypeDefinition
                || elementType.ContainsGenericParameters
            )
            {
                return type.AssemblyQualifiedName;
            }

            using PooledResource<StringBuilder> builderLease = Buffers.StringBuilder.Get(
                out StringBuilder builder
            );
            AppendAssemblyQualifiedTypeName(builder, type);
            builder.Append(", ").Append(type.Assembly.FullName);
            return builder.ToString();
        }

        private static void AppendAssemblyQualifiedTypeName(StringBuilder builder, Type type)
        {
            if (type.IsArray)
            {
                AppendAssemblyQualifiedTypeName(builder, type.GetElementType());
                builder.Append('[');
                int arrayRank = type.GetArrayRank();
                if (arrayRank == 1 && !type.IsSZArray)
                {
                    builder.Append('*');
                }

                for (int rank = 1; rank < arrayRank; ++rank)
                {
                    builder.Append(',');
                }

                builder.Append(']');
                return;
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                builder.Append(type.GetGenericTypeDefinition().FullName).Append('[');
                Type[] arguments = type.GetGenericArguments();
                int argumentsLength = arguments.Length;
                for (int index = 0; index < argumentsLength; ++index)
                {
                    if (0 < index)
                    {
                        builder.Append(',');
                    }

                    Type argument = arguments[index];
                    builder.Append('[');
                    AppendAssemblyQualifiedTypeName(builder, argument);
                    builder.Append(", ").Append(argument.Assembly.FullName).Append(']');
                }

                builder.Append(']');
                return;
            }

            builder.Append(type.FullName);
        }

        private static Type ResolveCompositeType(string typeName)
        {
            using PooledResource<List<Assembly>> assembliesLease = Buffers<Assembly>.List.Get(
                out List<Assembly> assemblies
            );
            foreach (Assembly assembly in GetAllLoadedAssemblies())
            {
                assemblies.Add(assembly);
            }

            try
            {
                return Type.GetType(
                    typeName,
                    PlaceholderAssemblyResolver,
                    (_, name, _) => ResolveUniqueLoadedType(name, assemblies),
                    throwOnError: false,
                    ignoreCase: false
                );
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<Assembly> FilterLoadedAssemblies(
            IEnumerable<Assembly> assemblies
        )
        {
            foreach (Assembly assembly in new ValueEnumerable<Assembly>(assemblies))
            {
                if (assembly != null && !assembly.IsDynamic)
                {
                    yield return assembly;
                }
            }
        }

        private static IEnumerable<Type> EnumerateLoadedTypes(IEnumerable<Assembly> assemblies)
        {
            foreach (Assembly assembly in assemblies)
            {
                foreach (Type type in GetTypesFromAssembly(assembly))
                {
                    if (type != null)
                    {
                        yield return type;
                    }
                }
            }
        }

        private static bool IsDiscoverableType(Type type, bool includeAbstract)
        {
            return type != null && (includeAbstract || (type.IsClass && !type.IsAbstract));
        }

        private static IEnumerable<Type> FilterDerivedTypes(
            IEnumerable<Type> types,
            Type baseType,
            bool includeAbstract
        )
        {
            foreach (Type type in types)
            {
                if (
                    type != null
                    && baseType.IsAssignableFrom(type)
                    && IsDiscoverableType(type, includeAbstract)
                )
                {
                    yield return type;
                }
            }
        }

        private static IEnumerable<Type> FilterAttributedTypes(
            IEnumerable<Type> types,
            Type attributeType,
            bool restrictKinds,
            bool includeAbstract
        )
        {
            foreach (Type type in types)
            {
                if (
                    (!restrictKinds || IsDiscoverableType(type, includeAbstract))
                    && HasAttributeSafe(type, attributeType)
                )
                {
                    yield return type;
                }
            }
        }

        private static IEnumerable<TMember> FilterAttributedMembers<TMember>(
            IEnumerable<TMember> members,
            Type attributeType
        )
            where TMember : MemberInfo
        {
            foreach (TMember member in new ValueEnumerable<TMember>(members))
            {
                if (member != null && HasAttributeSafe(member, attributeType))
                {
                    yield return member;
                }
            }
        }

        private static IEnumerable<MethodInfo> EnumerateAttributedMethods(
            IEnumerable<Type> types,
            Type attributeType,
            BindingFlags flags
        )
        {
            foreach (Type type in types)
            {
                foreach (MethodInfo method in SafeGetMethods(type, flags))
                {
                    if (method != null && HasAttributeSafe(method, attributeType))
                    {
                        yield return method;
                    }
                }
            }
        }

        private static IEnumerable<FieldInfo> EnumerateAttributedFields(
            IEnumerable<Type> types,
            Type attributeType,
            BindingFlags flags
        )
        {
            foreach (Type type in types)
            {
                foreach (FieldInfo field in SafeGetFields(type, flags))
                {
                    if (field != null && HasAttributeSafe(field, attributeType))
                    {
                        yield return field;
                    }
                }
            }
        }

        private static IEnumerable<PropertyInfo> EnumerateAttributedProperties(
            IEnumerable<Type> types,
            Type attributeType,
            BindingFlags flags
        )
        {
            foreach (Type type in types)
            {
                foreach (PropertyInfo property in SafeGetProperties(type, flags))
                {
                    if (property != null && HasAttributeSafe(property, attributeType))
                    {
                        yield return property;
                    }
                }
            }
        }

        private static void AppendParameterSignature(StringBuilder builder, Type[] parameterTypes)
        {
            int count = parameterTypes.Length;
            for (int index = 0; index < count; ++index)
            {
                if (0 < index)
                {
                    builder.Append(',');
                }
                builder.Append(parameterTypes[index]?.FullName ?? "null");
            }
        }

#if UNITY_EDITOR
        private static IEnumerable<Type> FilterCachedTypes(
            TypeCache.TypeCollection types,
            bool includeAbstract
        )
        {
            foreach (Type type in types)
            {
                if (IsDiscoverableType(type, includeAbstract))
                {
                    yield return type;
                }
            }
        }

        private static IEnumerable<MethodInfo> FilterCachedMethods(
            TypeCache.MethodCollection methods,
            Type within
        )
        {
            foreach (MethodInfo method in methods)
            {
                if (method != null && (within == null || method.DeclaringType == within))
                {
                    yield return method;
                }
            }
        }

        private static IEnumerable<FieldInfo> FilterCachedFields(
            TypeCache.FieldInfoCollection fields,
            Type within
        )
        {
            foreach (FieldInfo field in fields)
            {
                if (
                    field != null
                    && (
                        within == null
                        || (
                            field.DeclaringType != null
                            && field.DeclaringType.IsAssignableFrom(within)
                        )
                    )
                )
                {
                    yield return field;
                }
            }
        }
#endif

        private static Assembly ResolvePlaceholderAssembly(AssemblyName requestedAssembly)
        {
            return typeof(object).Assembly;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static MethodInfo[] SafeGetMethods(Type t, BindingFlags flags)
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

        private static FieldInfo[] SafeGetFields(Type t, BindingFlags flags)
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
        private static PropertyInfo[] SafeGetProperties(Type t, BindingFlags flags)
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

            int indexParamsLength = indexParams.Length;
            for (int i = 0; i < indexParamsLength; ++i)
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

            using PooledResource<StringBuilder> lease = Buffers.StringBuilder.Get(
                out StringBuilder builder
            );
            builder.Append('[');
            AppendParameterSignature(builder, paramTypes);
            return builder.Append(']').ToString();
        }

        private static string BuildMethodSignatureKey(string name, Type[] paramTypes)
        {
            if (paramTypes == null || paramTypes.Length == 0)
            {
                return name + "()";
            }

            using PooledResource<StringBuilder> lease = Buffers.StringBuilder.Get(
                out StringBuilder builder
            );
            builder.Append(name).Append('(');
            AppendParameterSignature(builder, paramTypes);
            return builder.Append(')').ToString();
        }
    }
}
