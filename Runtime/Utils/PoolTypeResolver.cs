// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Threading;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    /// <summary>
    /// Utility class for resolving and matching types, with special support for generic types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class provides methods for parsing type name strings (including simplified generic syntax),
    /// resolving them to <see cref="Type"/> instances, and matching concrete types against patterns
    /// including open generic types.
    /// </para>
    /// <para>
    /// Supported type name formats:
    /// <list type="bullet">
    ///   <item><description><c>System.Collections.Generic.List`1</c> - Open generic type definition</description></item>
    ///   <item><description><c>System.Collections.Generic.List`1[[System.Int32]]</c> - Closed generic type</description></item>
    ///   <item><description><c>List&lt;int&gt;</c> - Simplified closed generic syntax</description></item>
    ///   <item><description><c>List&lt;&gt;</c> - Simplified open generic syntax</description></item>
    ///   <item><description><c>Dictionary&lt;string, int&gt;</c> - Multiple type arguments</description></item>
    ///   <item><description><c>Dictionary&lt;,&gt;</c> - Open with multiple type arguments</description></item>
    ///   <item><description><c>List&lt;List&lt;int&gt;&gt;</c> - Nested generics</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class PoolTypeResolver
    {
        /// <summary>
        /// The default bound on distinct type-name spellings the resolver retains.
        /// </summary>
        /// <remarks>
        /// The pool type drawer resolves on every keystroke, and a spelling is the key -- so
        /// <c>Dictionary&lt;string,int&gt;</c> and <c>Dictionary&lt;string, int&gt;</c> are two
        /// entries. Sized well above the distinct pool types one project configures.
        /// </remarks>
        public const int DefaultMaxCachedTypeNames = 512;

        private static readonly Cache<string, Type> SimplifiedTypeNameCache = CacheBuilder<
            string,
            Type
        >
            .NewBuilder()
            .MaximumSize(DefaultMaxCachedTypeNames)
            .InitialCapacity(16)
            .Build();

        private static int _maxCachedTypeNames = DefaultMaxCachedTypeNames;
        private static readonly object CacheResizeLock = new();

        /// <summary>
        /// Gets or sets how many distinct type-name spellings the resolver retains. A value of 0 or
        /// less removes the bound.
        /// </summary>
        public static int MaxCachedTypeNames
        {
            get => Volatile.Read(ref _maxCachedTypeNames);
            set
            {
                lock (CacheResizeLock)
                {
                    SimplifiedTypeNameCache.Resize(value);
                    Volatile.Write(ref _maxCachedTypeNames, value);
                }
            }
        }

        internal static int CachedTypeNameCountForTesting => SimplifiedTypeNameCache.Count;

        private static readonly Dictionary<string, Type> BuiltInTypeAliases = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            // C# keywords (lowercase) - case-insensitive lookup handles PascalCase variants
            { "int", typeof(int) },
            { "uint", typeof(uint) },
            { "long", typeof(long) },
            { "ulong", typeof(ulong) },
            { "short", typeof(short) },
            { "ushort", typeof(ushort) },
            { "byte", typeof(byte) },
            { "sbyte", typeof(sbyte) },
            { "float", typeof(float) },
            { "double", typeof(double) },
            { "decimal", typeof(decimal) },
            { "bool", typeof(bool) },
            { "char", typeof(char) },
            { "string", typeof(string) },
            { "object", typeof(object) },
            { "void", typeof(void) },
            // .NET type names that differ from C# keywords (case-insensitive, so no duplicates)
            { "Int32", typeof(int) },
            { "UInt32", typeof(uint) },
            { "Int64", typeof(long) },
            { "UInt64", typeof(ulong) },
            { "Int16", typeof(short) },
            { "UInt16", typeof(ushort) },
            { "Single", typeof(float) },
            { "Boolean", typeof(bool) },
        };

        private static readonly Dictionary<string, Type> CommonGenericTypes = new(
            StringComparer.Ordinal
        )
        {
            { "List", typeof(List<>) },
            { "Dictionary", typeof(Dictionary<,>) },
            { "HashSet", typeof(HashSet<>) },
            { "Queue", typeof(Queue<>) },
            { "Stack", typeof(Stack<>) },
            { "LinkedList", typeof(LinkedList<>) },
            { "SortedList", typeof(SortedList<,>) },
            { "SortedDictionary", typeof(SortedDictionary<,>) },
            { "SortedSet", typeof(SortedSet<>) },
            { "KeyValuePair", typeof(KeyValuePair<,>) },
            { "Tuple", typeof(Tuple<>) },
            { "Nullable", typeof(Nullable<>) },
            { "IEnumerable", typeof(IEnumerable<>) },
            { "ICollection", typeof(ICollection<>) },
            { "IList", typeof(IList<>) },
            { "IDictionary", typeof(IDictionary<,>) },
            { "ISet", typeof(ISet<>) },
            { "IReadOnlyList", typeof(IReadOnlyList<>) },
            { "IReadOnlyCollection", typeof(IReadOnlyCollection<>) },
            { "IReadOnlyDictionary", typeof(IReadOnlyDictionary<,>) },
        };

        /// <summary>
        /// Parses a type name string and resolves it to a <see cref="Type"/>.
        /// </summary>
        /// <param name="typeName">
        /// The type name to resolve. Supports assembly-qualified names,
        /// open/closed generic CLR syntax, and simplified C# generic syntax.
        /// </param>
        /// <returns>
        /// The resolved <see cref="Type"/>, or <c>null</c> if the type could not be resolved.
        /// </returns>
        public static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            string trimmed = typeName.Trim();

            if (SimplifiedTypeNameCache.TryGet(trimmed, out Type cached))
            {
                return cached;
            }

            Type resolved = ResolveTypeInternal(trimmed);

            if (resolved != null)
            {
                SimplifiedTypeNameCache.Set(trimmed, resolved);
            }

            return resolved;
        }

        /// <summary>
        /// Checks if a concrete type matches a pattern type (including open generics).
        /// </summary>
        /// <param name="concreteType">The concrete type to check.</param>
        /// <param name="patternType">
        /// The pattern type to match against. Can be an exact type or an open generic definition.
        /// </param>
        /// <returns>
        /// <c>true</c> if the concrete type matches the pattern; otherwise, <c>false</c>.
        /// </returns>
        /// <remarks>
        /// <para>
        /// Matching rules:
        /// <list type="bullet">
        ///   <item><description>Exact type match always succeeds</description></item>
        ///   <item><description>Open generic definition matches any closed generic of the same type
        ///   (e.g., <c>List&lt;&gt;</c> matches <c>List&lt;int&gt;</c>)</description></item>
        ///   <item><description>For nested generics like <c>List&lt;List&lt;int&gt;&gt;</c>,
        ///   patterns like <c>List&lt;List&lt;&gt;&gt;</c> match any inner type argument</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TypeMatchesPattern(Type concreteType, Type patternType)
        {
            if (concreteType == null || patternType == null)
            {
                return false;
            }

            // Exact match
            if (concreteType == patternType)
            {
                return true;
            }

            // If pattern is open generic definition
            if (patternType.IsGenericTypeDefinition)
            {
                if (!concreteType.IsGenericType)
                {
                    return false;
                }

                Type concreteGenericDef = concreteType.IsGenericTypeDefinition
                    ? concreteType
                    : concreteType.GetGenericTypeDefinition();

                return concreteGenericDef == patternType;
            }

            /*
                If pattern is a partially open generic (e.g., List<List<>>)
                This is represented as a closed generic where some type arguments are open
            */
            if (patternType.IsGenericType && !patternType.IsGenericTypeDefinition)
            {
                if (!concreteType.IsGenericType)
                {
                    return false;
                }

                Type patternGenericDef = patternType.GetGenericTypeDefinition();
                Type concreteGenericDef = concreteType.GetGenericTypeDefinition();

                if (patternGenericDef != concreteGenericDef)
                {
                    return false;
                }

                Type[] patternArgs = patternType.GetGenericArguments();
                Type[] concreteArgs = concreteType.GetGenericArguments();

                if (patternArgs.Length != concreteArgs.Length)
                {
                    return false;
                }

                for (int i = 0; i < patternArgs.Length; i++)
                {
                    Type patternArg = patternArgs[i];
                    Type concreteArg = concreteArgs[i];

                    // If pattern argument is a generic parameter (open), it matches anything
                    if (patternArg.IsGenericParameter)
                    {
                        continue;
                    }

                    // Recursively check nested type arguments
                    if (!TypeMatchesPattern(concreteArg, patternArg))
                    {
                        return false;
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a concrete type matches a pattern specified as a type name string.
        /// </summary>
        /// <param name="concreteType">The concrete type to check.</param>
        /// <param name="patternTypeName">
        /// The pattern type name to match against. Supports simplified and full CLR syntax.
        /// </param>
        /// <returns>
        /// <c>true</c> if the concrete type matches the pattern; otherwise, <c>false</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TypeMatchesPattern(Type concreteType, string patternTypeName)
        {
            if (concreteType == null || string.IsNullOrWhiteSpace(patternTypeName))
            {
                return false;
            }

            Type patternType = ResolveType(patternTypeName);
            if (patternType == null)
            {
                return false;
            }

            return TypeMatchesPattern(concreteType, patternType);
        }

        /// <summary>
        /// Gets the generic type definition if the type is generic, otherwise returns the type itself.
        /// </summary>
        /// <param name="type">The type to process.</param>
        /// <returns>
        /// The generic type definition if <paramref name="type"/> is a generic type;
        /// otherwise, <paramref name="type"/> itself.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Type GetGenericPattern(Type type)
        {
            if (type == null)
            {
                return null;
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                return type.GetGenericTypeDefinition();
            }

            return type;
        }

        /// <summary>
        /// Gets all matching patterns for a type in order of specificity (most specific first).
        /// </summary>
        /// <param name="type">The type to get patterns for.</param>
        /// <returns>
        /// An enumerable of types representing matching patterns in order of decreasing specificity:
        /// <list type="number">
        ///   <item><description>Exact type</description></item>
        ///   <item><description>Inner generic open patterns (for nested generics)</description></item>
        ///   <item><description>Outer generic open pattern</description></item>
        /// </list>
        /// </returns>
        /// <remarks>
        /// <para>
        /// For <c>List&lt;List&lt;int&gt;&gt;</c>, this returns:
        /// <list type="bullet">
        ///   <item><description><c>List&lt;List&lt;int&gt;&gt;</c> (exact)</description></item>
        ///   <item><description><c>List&lt;List&lt;&gt;&gt;</c> (inner open)</description></item>
        ///   <item><description><c>List&lt;&gt;</c> (outer open)</description></item>
        /// </list>
        /// </para>
        /// </remarks>
        public static IEnumerable<Type> GetAllMatchingPatterns(Type type)
        {
            if (type == null)
            {
                yield break;
            }

            // First: exact type
            yield return type;

            if (!type.IsGenericType || type.IsGenericTypeDefinition)
            {
                yield break;
            }

            // For nested generics, get intermediate patterns
            Type[] genericArgs = type.GetGenericArguments();
            Type genericDef = type.GetGenericTypeDefinition();

            // Check if any type arguments are themselves generic types
            bool hasNestedGenerics = false;
            foreach (Type genericArg in genericArgs)
            {
                if (genericArg.IsGenericType && !genericArg.IsGenericTypeDefinition)
                {
                    hasNestedGenerics = true;
                    break;
                }
            }

            if (hasNestedGenerics)
            {
                // Build intermediate patterns with inner generics opened
                Type[] openedArgs = new Type[genericArgs.Length];
                for (int i = 0; i < genericArgs.Length; i++)
                {
                    Type arg = genericArgs[i];
                    if (arg.IsGenericType && !arg.IsGenericTypeDefinition)
                    {
                        openedArgs[i] = arg.GetGenericTypeDefinition();
                    }
                    else
                    {
                        openedArgs[i] = arg;
                    }
                }

                // Try to construct the partially open type
                Type partiallyOpen = TryMakeGenericType(genericDef, openedArgs);
                if (partiallyOpen != null && partiallyOpen != type)
                {
                    yield return partiallyOpen;
                }
            }

            // Last: fully open generic definition
            yield return genericDef;
        }

        /// <summary>
        /// Gets the match priority for a pattern type when matching against a concrete type.
        /// Lower values indicate higher priority (more specific match).
        /// </summary>
        /// <param name="concreteType">The concrete type being matched.</param>
        /// <param name="patternType">The pattern type to get priority for.</param>
        /// <returns>
        /// A priority value where:
        /// <list type="bullet">
        ///   <item><description>0 = exact match</description></item>
        ///   <item><description>1 = partially open generic (inner args open)</description></item>
        ///   <item><description>2 = fully open generic definition</description></item>
        ///   <item><description><see cref="int.MaxValue"/> = no match</description></item>
        /// </list>
        /// </returns>
        public static int GetMatchPriority(Type concreteType, Type patternType)
        {
            if (concreteType == null || patternType == null)
            {
                return int.MaxValue;
            }

            // Exact match has highest priority
            if (concreteType == patternType)
            {
                return 0;
            }

            if (!TypeMatchesPattern(concreteType, patternType))
            {
                return int.MaxValue;
            }

            // Open generic definition has lowest priority among matches
            if (patternType.IsGenericTypeDefinition)
            {
                return 2;
            }

            // Partially open generic (contains open type arguments)
            if (patternType.IsGenericType && ContainsOpenTypeArguments(patternType))
            {
                return 1;
            }

            // Exact match (should have been caught above, but just in case)
            return 0;
        }

        /// <summary>
        /// Clears the internal type resolution cache.
        /// </summary>
        public static void ClearCache()
        {
            SimplifiedTypeNameCache.Clear();
        }

        private static bool ContainsOpenTypeArguments(Type type)
        {
            if (!type.IsGenericType)
            {
                return false;
            }

            Type[] args = type.GetGenericArguments();
            foreach (Type arg in args)
            {
                if (arg.IsGenericParameter)
                {
                    return true;
                }

                if (arg.IsGenericType && ContainsOpenTypeArguments(arg))
                {
                    return true;
                }
            }

            return false;
        }

        private static Type TryMakeGenericType(Type genericDef, Type[] typeArgs)
        {
            try
            {
                return genericDef.MakeGenericType(typeArgs);
            }
            catch
            {
                return null;
            }
        }

        private static Type ResolveTypeInternal(string typeName)
        {
            // Try direct Type.GetType first (handles assembly-qualified names)
            Type directResolve = Type.GetType(typeName, throwOnError: false);
            if (directResolve != null)
            {
                return directResolve;
            }

            // Check if it's a built-in type alias
            if (BuiltInTypeAliases.TryGetValue(typeName, out Type aliasType))
            {
                return aliasType;
            }

            // Check for simplified generic syntax (contains < and >)
            if (typeName.Contains("<"))
            {
                return ParseSimplifiedGeneric(typeName);
            }

            // Check if it ends with arity marker (e.g., List`1)
            if (typeName.Contains("`"))
            {
                return ResolveBySearchingAssemblies(typeName);
            }

            // Try searching all loaded assemblies for the type name
            return ResolveBySearchingAssemblies(typeName);
        }

        private static Type ParseSimplifiedGeneric(string typeName)
        {
            // Parse generic syntax like "List<int>" or "Dictionary<string, List<int>>"
            int angleBracketIndex = typeName.IndexOf('<');
            if (angleBracketIndex < 0)
            {
                return null;
            }

            string genericTypeName = typeName.Substring(0, angleBracketIndex).Trim();
            string argsSection = typeName.Substring(
                angleBracketIndex + 1,
                typeName.Length - angleBracketIndex - 2
            );

            // Check for open generic syntax (e.g., "List<>" or "Dictionary<,>")
            if (IsOpenGenericArgs(argsSection))
            {
                int argCount = CountOpenGenericArgs(argsSection);
                return ResolveOpenGenericType(genericTypeName, argCount);
            }

            // Parse type arguments
            using PooledResource<List<string>> lease = Buffers<string>.List.Get(
                out List<string> typeArgStrings
            );
            ParseGenericArguments(argsSection, typeArgStrings);
            if (typeArgStrings.Count == 0)
            {
                return null;
            }

            // Resolve the generic type definition
            Type genericDef = ResolveOpenGenericType(genericTypeName, typeArgStrings.Count);
            if (genericDef == null)
            {
                return null;
            }

            // Resolve each type argument
            Type[] typeArgs = new Type[typeArgStrings.Count];
            for (int i = 0; i < typeArgStrings.Count; i++)
            {
                Type argType = ResolveType(typeArgStrings[i]);
                if (argType == null)
                {
                    return null;
                }

                typeArgs[i] = argType;
            }

            // Construct the closed generic type
            return TryMakeGenericType(genericDef, typeArgs);
        }

        private static bool IsOpenGenericArgs(string argsSection)
        {
            string trimmed = argsSection.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return true;
            }

            // Check if it's just commas (e.g., "," for Dictionary<,>)
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c != ',' && !char.IsWhiteSpace(c))
                {
                    return false;
                }
            }

            return true;
        }

        private static int CountOpenGenericArgs(string argsSection)
        {
            if (string.IsNullOrEmpty(argsSection.Trim()))
            {
                return 1;
            }

            int count = 1;
            for (int i = 0; i < argsSection.Length; i++)
            {
                if (argsSection[i] == ',')
                {
                    count++;
                }
            }

            return count;
        }

        private static Type ResolveOpenGenericType(string typeName, int arity)
        {
            // Check common generic types first
            if (CommonGenericTypes.TryGetValue(typeName, out Type commonType))
            {
                Type[] genericArgs = commonType.GetGenericArguments();
                if (genericArgs.Length == arity)
                {
                    return commonType;
                }
            }

            // Build CLR-style name with arity
            string clrName = $"{typeName}`{arity}";

            // Try direct resolution
            Type resolved = Type.GetType(clrName, throwOnError: false);
            if (resolved != null)
            {
                return resolved;
            }

            // Search assemblies
            return ResolveBySearchingAssemblies(clrName);
        }

        private static void ParseGenericArguments(string argsSection, List<string> args)
        {
            int depth = 0;
            int start = 0;

            for (int i = 0; i < argsSection.Length; i++)
            {
                char c = argsSection[i];
                if (c == '<')
                {
                    depth++;
                }
                else if (c == '>')
                {
                    depth--;
                }
                else if (c == ',' && depth == 0)
                {
                    string arg = argsSection.Substring(start, i - start).Trim();
                    if (!string.IsNullOrEmpty(arg))
                    {
                        args.Add(arg);
                    }

                    start = i + 1;
                }
            }

            // Add the last argument
            if (start < argsSection.Length)
            {
                string arg = argsSection.Substring(start).Trim();
                if (!string.IsNullOrEmpty(arg))
                {
                    args.Add(arg);
                }
            }
        }

        private static Type ResolveBySearchingAssemblies(string typeName)
        {
            // Common namespaces to try
            string[] commonNamespaces =
            {
                "System",
                "System.Collections.Generic",
                "System.Collections",
                "UnityEngine",
            };

            // Try with common namespace prefixes
            foreach (string ns in commonNamespaces)
            {
                string fullName = $"{ns}.{typeName}";
                Type resolved = Type.GetType(fullName, throwOnError: false);
                if (resolved != null)
                {
                    return resolved;
                }
            }

            // Search all loaded assemblies
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                try
                {
                    Type type = assembly.GetType(typeName, throwOnError: false);
                    if (type != null)
                    {
                        return type;
                    }

                    // Try with common namespace prefixes in this assembly
                    foreach (string ns in commonNamespaces)
                    {
                        string fullName = $"{ns}.{typeName}";
                        type = assembly.GetType(fullName, throwOnError: false);
                        if (type != null)
                        {
                            return type;
                        }
                    }
                }
                catch
                {
                    // Ignore assembly loading errors
                }
            }

            return null;
        }

        /// <summary>
        /// Gets a human-readable display name for a type.
        /// </summary>
        /// <param name="type">The type to get a display name for.</param>
        /// <returns>
        /// A simplified type name using C# syntax for generics.
        /// </returns>
        public static string GetDisplayName(Type type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            // Check for built-in type alias
            foreach (KeyValuePair<string, Type> alias in BuiltInTypeAliases)
            {
                if (alias.Value == type)
                {
                    return alias.Key;
                }
            }

            if (!type.IsGenericType)
            {
                return type.Name;
            }

            if (type.IsGenericTypeDefinition)
            {
                // Open generic: List<> or Dictionary<,>
                string name = type.Name;
                int backtickIndex = name.IndexOf('`');
                if (0 <= backtickIndex)
                {
                    name = name.Substring(0, backtickIndex);
                }

                Type[] args = type.GetGenericArguments();
                if (args.Length == 1)
                {
                    return $"{name}<>";
                }

                using PooledResource<StringBuilder> sbLease = Buffers.StringBuilder.Get(
                    out StringBuilder sb
                );
                sb.Append(name);
                sb.Append('<');
                for (int i = 1; i < args.Length; i++)
                {
                    sb.Append(',');
                }

                sb.Append('>');
                return sb.ToString();
            }
            else
            {
                // Closed generic: List<int> or Dictionary<string, int>
                string name = type.Name;
                int backtickIndex = name.IndexOf('`');
                if (0 <= backtickIndex)
                {
                    name = name.Substring(0, backtickIndex);
                }

                Type[] args = type.GetGenericArguments();
                using PooledResource<StringBuilder> sbLease = Buffers.StringBuilder.Get(
                    out StringBuilder sb
                );
                sb.Append(name);
                sb.Append('<');
                for (int i = 0; i < args.Length; i++)
                {
                    if (0 < i)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(GetDisplayName(args[i]));
                }

                sb.Append('>');
                return sb.ToString();
            }
        }
    }
}
