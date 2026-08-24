// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Text.RegularExpressions;
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Unity serializable wrapper for <see cref="Type"/> that survives JSON, ProtoBuf, and Unity serialization by storing normalized assembly-qualified names.
    /// Keeps inspector fields and saved data resilient to refactors by handling renames and namespace changes.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// [Serializable]
    /// public sealed class SpawnRule : MonoBehaviour
    /// {
    ///     [SerializeField]
    ///     private SerializableType behaviourType = new SerializableType(typeof(EnemyController));
    ///
    ///     public Type Resolve() => behaviourType.Value;
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
    [ProtoContract]
    [WProtoContract]
    [JsonConverter(typeof(SerializableTypeJsonConverter))]
    public partial struct SerializableType
        : IEquatable<SerializableType>,
            ISerializationCallbackReceiver,
            ISerializable
    {
        [SerializeField]
        [ProtoMember(1)]
        [WProtoMember(1)]
        [StringInList(
            typeof(SerializableTypeCatalog),
            nameof(SerializableTypeCatalog.GetAssemblyQualifiedNames)
        )]
        private string _assemblyQualifiedName;

        [NonSerialized]
        [ProtoIgnore]
        [WProtoIgnore]
        [JsonIgnore]
        private Type _cachedType;

        [NonSerialized]
        [ProtoIgnore]
        [WProtoIgnore]
        [JsonIgnore]
        private bool _resolutionAttempted;

        internal static class SerializedPropertyNames
        {
            internal const string AssemblyQualifiedName = nameof(_assemblyQualifiedName);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerializableType"/> struct.
        /// </summary>
        /// <param name="type">Type to wrap.</param>
        public SerializableType(Type type)
        {
            _assemblyQualifiedName = NormalizeTypeName(type);
            _cachedType = type;
            _resolutionAttempted = true;
        }

        private SerializableType(SerializationInfo info, StreamingContext context)
        {
            _assemblyQualifiedName = info.GetString(nameof(_assemblyQualifiedName)) ?? string.Empty;
            _cachedType = SerializableTypeCatalog.Resolve(_assemblyQualifiedName);
            _resolutionAttempted = true;
        }

        /// <summary>
        /// Gets a value indicating whether this instance does not reference a type.
        /// </summary>
        public bool IsEmpty => string.IsNullOrEmpty(_assemblyQualifiedName);

        /// <summary>
        /// Gets the stored assembly qualified type name.
        /// </summary>
        public string AssemblyQualifiedName =>
            string.IsNullOrEmpty(_assemblyQualifiedName) ? string.Empty : _assemblyQualifiedName;

        /// <summary>
        /// Gets a user-friendly display name for the wrapped type.
        /// </summary>
        public string DisplayName
        {
            get
            {
                Type resolved = GetResolvedType();
                if (resolved == null)
                {
                    return string.IsNullOrEmpty(_assemblyQualifiedName)
                        ? SerializableTypeCatalog.NoneDisplayName
                        : $"{SerializableTypeCatalog.NoneDisplayName}: {_assemblyQualifiedName}";
                }

                return SerializableTypeCatalog.GetDisplayName(resolved);
            }
        }

        /// <summary>
        /// Gets the resolved <see cref="Type"/> instance, or <c>null</c> when unresolved.
        /// </summary>
        public Type Value => GetResolvedType();

        /// <summary>
        /// Assigns the wrapper to the provided <see cref="Type"/>.
        /// </summary>
        /// <param name="type">Type to store.</param>
        public void SetType(Type type)
        {
            _assemblyQualifiedName = NormalizeTypeName(type);
            _cachedType = type;
            _resolutionAttempted = true;
        }

        /// <summary>
        /// Attempts to retrieve the wrapped <see cref="Type"/>.
        /// </summary>
        /// <param name="type">Receives the resolved type if available.</param>
        /// <returns>True when a type is available and could be resolved.</returns>
        public bool TryGetValue(out Type type)
        {
            type = GetResolvedType();
            return type != null;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return DisplayName;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return IsEmpty;
            }

            if (obj is SerializableType serializableType)
            {
                return Equals(serializableType);
            }

            if (obj is Type type)
            {
                return EqualsType(type);
            }

            return false;
        }

        /// <inheritdoc/>
        public bool Equals(SerializableType other)
        {
            string leftName = string.IsNullOrEmpty(_assemblyQualifiedName)
                ? string.Empty
                : _assemblyQualifiedName;
            string rightName = string.IsNullOrEmpty(other._assemblyQualifiedName)
                ? string.Empty
                : other._assemblyQualifiedName;
            return string.Equals(leftName, rightName, StringComparison.Ordinal);
        }

        /// <summary>
        /// Compares the wrapped type against a <see cref="Type"/> instance.
        /// </summary>
        /// <param name="other">Type to compare.</param>
        /// <returns>True when both represent the same type or both are empty.</returns>
        public bool EqualsType(Type other)
        {
            if (other == null)
            {
                return string.IsNullOrEmpty(_assemblyQualifiedName);
            }

            string normalized = NormalizeTypeName(other);
            return string.Equals(_assemblyQualifiedName, normalized, StringComparison.Ordinal);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            if (string.IsNullOrEmpty(_assemblyQualifiedName))
            {
                return 0;
            }

            return StringComparer.Ordinal.GetHashCode(_assemblyQualifiedName);
        }

        /// <summary>
        /// Equality comparison between two <see cref="SerializableType"/> instances.
        /// </summary>
        public static bool operator ==(SerializableType left, SerializableType right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality comparison between two <see cref="SerializableType"/> instances.
        /// </summary>
        public static bool operator !=(SerializableType left, SerializableType right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Equality comparison between a <see cref="SerializableType"/> and a nullable wrapper.
        /// </summary>
        public static bool operator ==(SerializableType left, SerializableType? right)
        {
            if (!right.HasValue)
            {
                return left.IsEmpty;
            }

            return left.Equals(right.Value);
        }

        /// <summary>
        /// Inequality comparison between a <see cref="SerializableType"/> and a nullable wrapper.
        /// </summary>
        public static bool operator !=(SerializableType left, SerializableType? right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Equality comparison between a nullable wrapper and a <see cref="SerializableType"/>.
        /// </summary>
        public static bool operator ==(SerializableType? left, SerializableType right)
        {
            if (!left.HasValue)
            {
                return right.IsEmpty;
            }

            return left.Value.Equals(right);
        }

        /// <summary>
        /// Inequality comparison between a nullable wrapper and a <see cref="SerializableType"/>.
        /// </summary>
        public static bool operator !=(SerializableType? left, SerializableType right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Implicitly converts the wrapper to a <see cref="Type"/>.
        /// </summary>
        public static implicit operator Type(SerializableType value)
        {
            return value.Value;
        }

        /// <summary>
        /// Creates a wrapper from the provided <see cref="Type"/>.
        /// </summary>
        /// <param name="type">Type to wrap.</param>
        /// <returns>The constructed <see cref="SerializableType"/>.</returns>
        public static SerializableType FromType(Type type)
        {
            return new SerializableType(type);
        }

        /// <inheritdoc/>
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (_cachedType != null)
            {
                _assemblyQualifiedName = NormalizeTypeName(_cachedType);
            }
        }

        /// <inheritdoc/>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            _cachedType = SerializableTypeCatalog.Resolve(_assemblyQualifiedName);
            _resolutionAttempted = true;
        }

        /// <inheritdoc/>
        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(_assemblyQualifiedName), _assemblyQualifiedName);
        }

        private Type GetResolvedType()
        {
            if (_cachedType != null)
            {
                return _cachedType;
            }

            if (string.IsNullOrEmpty(_assemblyQualifiedName))
            {
                _resolutionAttempted = true;
                return null;
            }

            if (!_resolutionAttempted)
            {
                _cachedType = SerializableTypeCatalog.Resolve(_assemblyQualifiedName);
                _resolutionAttempted = true;
            }
            else if (_cachedType == null)
            {
                _cachedType = SerializableTypeCatalog.Resolve(_assemblyQualifiedName);
            }

            return _cachedType;
        }

        internal static SerializableType FromSerializedName(string serialized)
        {
            SerializableType value = default;
            value._assemblyQualifiedName = string.IsNullOrEmpty(serialized)
                ? string.Empty
                : serialized;
            value._cachedType = SerializableTypeCatalog.Resolve(value._assemblyQualifiedName);
            value._resolutionAttempted = true;
            return value;
        }

        internal static string NormalizeTypeName(Type type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            string assemblyQualifiedName = type.AssemblyQualifiedName;
            if (!string.IsNullOrEmpty(assemblyQualifiedName))
            {
                return assemblyQualifiedName;
            }

            string fullName = type.FullName;
            if (!string.IsNullOrEmpty(fullName))
            {
                Assembly assembly = type.Assembly;
                string assemblyName = assembly.GetName().Name;
                if (!string.IsNullOrEmpty(assemblyName))
                {
                    return $"{fullName}, {assemblyName}";
                }

                return fullName;
            }

            string name = type.Name;
            if (!string.IsNullOrEmpty(name))
            {
                Assembly assembly = type.Assembly;
                string assemblyName = assembly.GetName().Name;
                if (!string.IsNullOrEmpty(assemblyName))
                {
                    return $"{name}, {assemblyName}";
                }

                return name;
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Catalog utilities for <see cref="SerializableType"/>.
    /// </summary>
    public static class SerializableTypeCatalog
    {
        internal const string NoneDisplayName = "<None>";

        private static readonly object SyncRoot = new();
        private static SerializableTypeDescriptor[] _descriptors;
        private static Dictionary<string, SerializableTypeDescriptor> _descriptorByName;
        private static string[] _assemblyQualifiedNames;
        private static string[] _displayNames;
        private static string[] _tooltips;

        private static readonly Dictionary<string, SerializableTypeDescriptor[]> FilterCache = new(
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly string[] DefaultIgnorePatternStrings =
        {
            @"^\$",
            "<>",
            "AnonymousType",
            "DisplayClass",
            "PrivateImplementationDetails",
            @"\bBee\.",
        };

        private static Regex[] _defaultIgnoreRegexes;
        private static Regex[] _configuredIgnoreRegexes;
        private static string[] _configuredIgnorePatterns;
        private static readonly Dictionary<string, PatternStats> PatternStatsCache = new(
            StringComparer.Ordinal
        );
        private static TypeSignature[] _typeSignatures;

        /// <summary>
        /// Exposes the default ignore patterns used when no explicit configuration is provided.
        /// </summary>
        internal static IReadOnlyList<string> GetDefaultIgnorePatterns()
        {
            return DefaultIgnorePatternStrings;
        }

        /// <summary>
        /// Retrieves the currently active ignore pattern strings. Falls back to defaults when no overrides exist.
        /// </summary>
        internal static IReadOnlyList<string> GetActiveIgnorePatterns()
        {
            return _configuredIgnorePatterns ?? DefaultIgnorePatternStrings;
        }

        /// <summary>
        /// Replaces the ignore pattern set used to filter candidate SerializableTypes. A <c>null</c> input reverts to defaults.
        /// </summary>
        /// <param name="patterns">Patterns to apply. Empty strings are discarded.</param>
        internal static void ConfigureTypeNameIgnorePatterns(IEnumerable<string> patterns)
        {
            string[] sanitized = SanitizePatternInput(patterns);

            lock (SyncRoot)
            {
                if (PatternsEqual(_configuredIgnorePatterns, sanitized))
                {
                    return;
                }

                _configuredIgnorePatterns = sanitized;
                _configuredIgnoreRegexes = sanitized == null ? null : CompilePatterns(sanitized);

                PatternStatsCache.Clear();
                _descriptors = null;
                _descriptorByName = null;
                _assemblyQualifiedNames = null;
                _displayNames = null;
                _tooltips = null;
                FilterCache.Clear();
            }
        }

        /// <summary>
        /// Provides statistics for the supplied pattern, including validity and match counts.
        /// </summary>
        internal static PatternStats GetPatternStats(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return new PatternStats(string.Empty, true, 0, null);
            }

            string trimmed = pattern.Trim();
            lock (SyncRoot)
            {
                if (PatternStatsCache.TryGetValue(trimmed, out PatternStats cached))
                {
                    return cached;
                }
            }

            PatternStats stats;
            try
            {
                Regex regex = new(trimmed, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                int count = CountTypesMatchingRegex(regex);
                stats = new PatternStats(trimmed, true, count, null);
            }
            catch (ArgumentException e)
            {
                stats = new PatternStats(trimmed, false, 0, e.Message);
            }

            lock (SyncRoot)
            {
                PatternStatsCache[trimmed] = stats;
            }

            return stats;
        }

        private static Regex[] GetActiveIgnoreRegexes()
        {
            Regex[] configured = _configuredIgnoreRegexes;
            if (configured != null)
            {
                return configured;
            }

            return _defaultIgnoreRegexes ??= CompilePatterns(DefaultIgnorePatternStrings);
        }

        internal static void WarmPatternStats(IEnumerable<string> patterns)
        {
            if (patterns == null)
            {
                return;
            }

            using (
                PooledResource<HashSet<string>> uniqueLease = SetBuffers<string>
                    .GetHashSetPool(StringComparer.Ordinal)
                    .Get(out HashSet<string> unique)
            )
            {
                foreach (string pattern in patterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        continue;
                    }

                    unique.Add(pattern.Trim());
                }

                if (unique.Count == 0)
                {
                    return;
                }

                foreach (string pattern in unique)
                {
                    GetPatternStats(pattern);
                }
            }
        }

        private static Regex[] CompilePatterns(IEnumerable<string> patterns)
        {
            if (patterns == null)
            {
                return null;
            }

            using (
                PooledResource<List<Regex>> listLease = Buffers<Regex>.List.Get(
                    out List<Regex> compiled
                )
            )
            {
                foreach (string pattern in patterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        continue;
                    }

                    string trimmed = pattern.Trim();
                    try
                    {
                        compiled.Add(
                            new Regex(
                                trimmed,
                                RegexOptions.Compiled | RegexOptions.CultureInvariant
                            )
                        );
                    }
                    catch (ArgumentException e)
                    {
                        Debug.LogWarning(
                            $"SerializableTypeCatalog ignore pattern '{trimmed}' is invalid: {e.Message}"
                        );
                    }
                }

                return compiled.Count == 0 ? Array.Empty<Regex>() : compiled.ToArray();
            }
        }

        private static string[] SanitizePatternInput(IEnumerable<string> patterns)
        {
            if (patterns == null)
            {
                return null;
            }

            using (
                PooledResource<List<string>> sanitizedLease = Buffers<string>.List.Get(
                    out List<string> sanitized
                )
            )
            {
                foreach (string pattern in patterns)
                {
                    if (string.IsNullOrWhiteSpace(pattern))
                    {
                        continue;
                    }

                    sanitized.Add(pattern.Trim());
                }

                return sanitized.Count == 0 ? Array.Empty<string>() : sanitized.ToArray();
            }
        }

        private static bool PatternsEqual(string[] left, string[] right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left.Length != right.Length)
            {
                return false;
            }

            for (int index = 0; index < left.Length; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MatchesConfiguredIgnorePattern(Type type)
        {
            Regex[] patterns = GetActiveIgnoreRegexes();
            if (patterns == null || patterns.Length == 0)
            {
                return false;
            }

            foreach (Regex regex in patterns)
            {
                if (regex == null)
                {
                    continue;
                }

                if (RegexMatchesType(type, regex))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RegexMatchesType(Type type, Regex regex)
        {
            if (type == null || regex == null)
            {
                return false;
            }

            string fullName = type.FullName;
            if (!string.IsNullOrEmpty(fullName) && regex.IsMatch(fullName))
            {
                return true;
            }

            string name = type.Name;
            if (!string.IsNullOrEmpty(name) && regex.IsMatch(name))
            {
                return true;
            }

            string assemblyQualifiedName = type.AssemblyQualifiedName;
            if (
                !string.IsNullOrEmpty(assemblyQualifiedName) && regex.IsMatch(assemblyQualifiedName)
            )
            {
                return true;
            }

            return false;
        }

        private static int CountTypesMatchingRegex(Regex regex)
        {
            if (regex == null)
            {
                return 0;
            }

            EnsureTypeSignatures();

            TypeSignature[] signatures = _typeSignatures;
            if (signatures == null || signatures.Length == 0)
            {
                return 0;
            }

            int matches = 0;
            foreach (TypeSignature signature in signatures)
            {
                if (RegexMatchesSignature(signature, regex))
                {
                    matches++;
                }
            }

            return matches;
        }

        /// <summary>
        /// Resolves a type from an assembly qualified name.
        /// </summary>
        /// <param name="assemblyQualifiedName">Assembly qualified name to resolve.</param>
        /// <returns>The resolved type when found; otherwise, <c>null</c>.</returns>
        public static Type Resolve(string assemblyQualifiedName)
        {
            if (string.IsNullOrEmpty(assemblyQualifiedName))
            {
                return null;
            }

            EnsureCache();

            if (
                _descriptorByName.TryGetValue(
                    assemblyQualifiedName,
                    out SerializableTypeDescriptor descriptor
                )
            )
            {
                return descriptor.Type;
            }

            Type direct = ReflectionHelpers.TryResolveType(assemblyQualifiedName);
            if (direct != null)
            {
                return direct;
            }

            string fullName = ExtractFullName(assemblyQualifiedName);
            if (string.IsNullOrEmpty(fullName))
            {
                return null;
            }

            Type fallback =
                ReflectionHelpers.TryResolveType(fullName) ?? ResolveByFullName(fullName);
            return fallback;
        }

        /// <summary>
        /// Provides a display name for a type using catalog formatting.
        /// </summary>
        /// <param name="type">Type to format.</param>
        /// <returns>A user-friendly display name.</returns>
        public static string GetDisplayName(Type type)
        {
            if (type == null)
            {
                return NoneDisplayName;
            }

            EnsureCache();

            string normalized = SerializableType.NormalizeTypeName(type);
            if (
                !string.IsNullOrEmpty(normalized)
                && _descriptorByName.TryGetValue(
                    normalized,
                    out SerializableTypeDescriptor descriptor
                )
            )
            {
                return descriptor.DisplayName;
            }

            return FormatDisplayName(type);
        }

        /// <summary>
        /// Attempts to retrieve catalog display information for an assembly-qualified type string.
        /// </summary>
        /// <param name="assemblyQualifiedName">Assembly-qualified type name.</param>
        /// <param name="displayName">Friendly display label.</param>
        /// <param name="tooltip">Tooltip (typically the assembly-qualified name).</param>
        /// <returns>True when the descriptor is known.</returns>
        public static bool TryGetDisplayInfo(
            string assemblyQualifiedName,
            out string displayName,
            out string tooltip
        )
        {
            EnsureCache();

            if (string.IsNullOrEmpty(assemblyQualifiedName))
            {
                displayName = NoneDisplayName;
                tooltip = string.Empty;
                return true;
            }

            if (
                _descriptorByName.TryGetValue(
                    assemblyQualifiedName,
                    out SerializableTypeDescriptor descriptor
                )
            )
            {
                displayName = descriptor.DisplayName;
                tooltip = descriptor.Tooltip;
                return true;
            }

            displayName = assemblyQualifiedName;
            tooltip = assemblyQualifiedName;
            return false;
        }

        /// <summary>
        /// Gets the assembly qualified names used by the inspector list.
        /// </summary>
        /// <returns>An array of assembly qualified names including the none entry.</returns>
        public static string[] GetAssemblyQualifiedNames()
        {
            EnsureCache();
            return _assemblyQualifiedNames;
        }

        /// <summary>
        /// Gets the display names aligned with <see cref="GetAssemblyQualifiedNames"/>.
        /// </summary>
        /// <returns>Array of display labels.</returns>
        public static string[] GetDisplayNames()
        {
            EnsureCache();
            return _displayNames;
        }

        /// <summary>
        /// Gets the tooltips aligned with <see cref="GetAssemblyQualifiedNames"/>.
        /// </summary>
        /// <returns>Array of tooltips.</returns>
        public static string[] GetTooltips()
        {
            EnsureCache();
            return _tooltips;
        }

        /// <summary>
        /// Retrieves filtered descriptors matching the provided search term.
        /// </summary>
        /// <param name="search">Search term to apply.</param>
        /// <returns>A cached filtered descriptor array.</returns>
        public static IReadOnlyList<SerializableTypeDescriptor> GetFilteredDescriptors(
            string search
        )
        {
            EnsureCache();

            string key = string.IsNullOrWhiteSpace(search)
                ? string.Empty
                : search.Trim().ToLowerInvariant();
            if (FilterCache.TryGetValue(key, out SerializableTypeDescriptor[] cached))
            {
                return cached;
            }

            if (string.IsNullOrEmpty(key))
            {
                FilterCache[key] = _descriptors;
                return _descriptors;
            }

            SerializableTypeDescriptor[] source = _descriptors;
            int keyLength = key.Length;
            if (1 < keyLength)
            {
                for (int length = keyLength - 1; 0 < length; length--)
                {
                    string parentKey = key.Substring(0, length);
                    if (FilterCache.TryGetValue(parentKey, out SerializableTypeDescriptor[] parent))
                    {
                        source = parent;
                        break;
                    }
                }
            }

            using (
                PooledResource<List<SerializableTypeDescriptor>> filteredLease =
                    Buffers<SerializableTypeDescriptor>.List.Get(
                        out List<SerializableTypeDescriptor> filtered
                    )
            )
            {
                if (filtered.Capacity < source.Length)
                {
                    filtered.Capacity = source.Length;
                }

                foreach (SerializableTypeDescriptor descriptor in source)
                {
                    if (descriptor.Matches(key))
                    {
                        filtered.Add(descriptor);
                    }
                }

                SerializableTypeDescriptor[] result = filtered.ToArray();
                FilterCache[key] = result;
                return result;
            }
        }

        private static string FormatDisplayName(Type type)
        {
            if (type == null)
            {
                return NoneDisplayName;
            }

            using PooledResource<StringBuilder> stringBuilderBuffer = Buffers.StringBuilder.Get(
                out StringBuilder builder
            );
            AppendTypeName(builder, type);
            return builder.ToString();
        }

        private static string FormatTooltip(Type type, string assemblyQualifiedName)
        {
            if (!string.IsNullOrEmpty(assemblyQualifiedName))
            {
                return assemblyQualifiedName;
            }

            return type?.AssemblyQualifiedName ?? string.Empty;
        }

        private static void EnsureCache()
        {
            if (_descriptors != null)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_descriptors != null)
                {
                    return;
                }

                using (
                    PooledResource<List<SerializableTypeDescriptor>> descriptorsLease =
                        Buffers<SerializableTypeDescriptor>.List.Get(
                            out List<SerializableTypeDescriptor> descriptors
                        )
                )
                {
                    descriptors.Add(
                        new SerializableTypeDescriptor(
                            type: null,
                            assemblyQualifiedName: string.Empty,
                            displayName: NoneDisplayName,
                            tooltip: string.Empty
                        )
                    );

                    using (
                        PooledResource<HashSet<string>> seenTypesLease = SetBuffers<string>
                            .GetHashSetPool(StringComparer.Ordinal)
                            .Get(out HashSet<string> seenTypes)
                    )
                    {
                        IEnumerable<Assembly> assemblies =
                            ReflectionHelpers.GetAllLoadedAssemblies();
                        foreach (Assembly assembly in assemblies)
                        {
                            if (assembly == null || assembly.IsDynamic)
                            {
                                continue;
                            }

                            Type[] exportedTypes = GetAssemblyTypes(assembly);
                            foreach (Type type in exportedTypes)
                            {
                                if (type == null)
                                {
                                    continue;
                                }

                                if (ShouldSkipType(type))
                                {
                                    continue;
                                }

                                string normalized = SerializableType.NormalizeTypeName(type);
                                if (string.IsNullOrEmpty(normalized))
                                {
                                    continue;
                                }

                                if (!seenTypes.Add(normalized))
                                {
                                    continue;
                                }

                                descriptors.Add(
                                    new SerializableTypeDescriptor(
                                        type,
                                        normalized,
                                        FormatDisplayName(type),
                                        FormatTooltip(type, normalized)
                                    )
                                );
                            }
                        }
                    }

                    descriptors.Sort(
                        static (left, right) =>
                            string.Compare(
                                left.DisplayName,
                                right.DisplayName,
                                StringComparison.Ordinal
                            )
                    );

                    Dictionary<string, int> displayCounts = new(StringComparer.Ordinal);
                    for (int i = 0; i < descriptors.Count; i++)
                    {
                        string displayName = descriptors[i].DisplayName;
                        if (string.IsNullOrEmpty(displayName))
                        {
                            continue;
                        }

                        displayCounts.TryGetValue(displayName, out int count);
                        displayCounts[displayName] = count + 1;
                    }

                    bool hasDuplicates = false;
                    foreach (KeyValuePair<string, int> pair in displayCounts)
                    {
                        if (1 < pair.Value)
                        {
                            hasDuplicates = true;
                            break;
                        }
                    }

                    if (hasDuplicates)
                    {
                        for (int i = 0; i < descriptors.Count; i++)
                        {
                            SerializableTypeDescriptor descriptor = descriptors[i];
                            if (
                                descriptor.Type == null
                                || string.IsNullOrEmpty(descriptor.DisplayName)
                                || !displayCounts.TryGetValue(
                                    descriptor.DisplayName,
                                    out int occurrences
                                )
                                || occurrences <= 1
                            )
                            {
                                continue;
                            }

                            string assemblyName =
                                descriptor.Type?.Assembly?.GetName()?.Name ?? string.Empty;
                            string disambiguated = string.IsNullOrEmpty(assemblyName)
                                ? descriptor.DisplayName
                                : $"{descriptor.DisplayName} ({assemblyName})";

                            descriptors[i] = new SerializableTypeDescriptor(
                                descriptor.Type,
                                descriptor.AssemblyQualifiedName,
                                disambiguated,
                                descriptor.Tooltip
                            );
                        }
                    }

                    _descriptors = descriptors.ToArray();
                }

                _descriptorByName = new Dictionary<string, SerializableTypeDescriptor>(
                    _descriptors.Length,
                    StringComparer.Ordinal
                );

                foreach (SerializableTypeDescriptor descriptor in _descriptors)
                {
                    if (!_descriptorByName.ContainsKey(descriptor.AssemblyQualifiedName))
                    {
                        _descriptorByName.Add(descriptor.AssemblyQualifiedName, descriptor);
                    }
                }

                _assemblyQualifiedNames = new string[_descriptors.Length];
                _displayNames = new string[_descriptors.Length];
                _tooltips = new string[_descriptors.Length];
                for (int index = 0; index < _descriptors.Length; index++)
                {
                    _assemblyQualifiedNames[index] = _descriptors[index].AssemblyQualifiedName;
                    _displayNames[index] = _descriptors[index].DisplayName;
                    _tooltips[index] = _descriptors[index].Tooltip;
                }

                FilterCache[string.Empty] = _descriptors;
            }
        }

        private static Type[] GetAssemblyTypes(Assembly assembly)
        {
            return ReflectionHelpers.GetTypesFromAssembly(assembly);
        }

        private static Type ResolveByFullName(string fullName)
        {
            IEnumerable<Assembly> assemblies = ReflectionHelpers.GetAllLoadedAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                if (assembly == null || assembly.IsDynamic)
                {
                    continue;
                }

                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static string ExtractFullName(string assemblyQualifiedName)
        {
            int commaIndex = assemblyQualifiedName.IndexOf(',');
            if (commaIndex < 0)
            {
                return assemblyQualifiedName;
            }

            return assemblyQualifiedName.Substring(0, commaIndex).Trim();
        }

        private static void AppendTypeName(StringBuilder builder, Type type)
        {
            if (type == null)
            {
                return;
            }

            if (type.IsArray)
            {
                Type elementType = type.GetElementType();
                AppendTypeName(builder, elementType);
                builder.Append('[');
                builder.Append(']');
                return;
            }

            if (type.IsGenericType)
            {
                string fullName = type.FullName ?? type.Name;
                if (string.IsNullOrEmpty(fullName))
                {
                    fullName = type.Name;
                }

                int backtickIndex = fullName.IndexOf('`');
                if (0 <= backtickIndex)
                {
                    fullName = fullName.Substring(0, backtickIndex);
                }

                builder.Append(fullName.Replace('+', '.'));
                builder.Append('<');

                Type[] arguments = type.GetGenericArguments();
                for (int index = 0; index < arguments.Length; index++)
                {
                    if (0 < index)
                    {
                        builder.Append(", ");
                    }

                    AppendTypeName(builder, arguments[index]);
                }

                builder.Append('>');
                return;
            }

            if (type.IsGenericParameter)
            {
                builder.Append(type.Name);
                return;
            }

            string name = type.FullName ?? type.Name;
            if (string.IsNullOrEmpty(name))
            {
                name = type.Name;
            }

            builder.Append(name.Replace('+', '.'));
        }

        private static bool RegexMatchesSignature(TypeSignature signature, Regex regex)
        {
            if (regex == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(signature.FullName) && regex.IsMatch(signature.FullName))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(signature.Name) && regex.IsMatch(signature.Name))
            {
                return true;
            }

            if (
                !string.IsNullOrEmpty(signature.AssemblyQualifiedName)
                && regex.IsMatch(signature.AssemblyQualifiedName)
            )
            {
                return true;
            }

            return false;
        }

        private static void EnsureTypeSignatures()
        {
            if (_typeSignatures != null)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (_typeSignatures != null)
                {
                    return;
                }

                using (
                    PooledResource<List<TypeSignature>> signaturesLease =
                        Buffers<TypeSignature>.List.Get(out List<TypeSignature> signatures)
                )
                using (
                    PooledResource<HashSet<string>> seenTypesLease = SetBuffers<string>
                        .GetHashSetPool(StringComparer.Ordinal)
                        .Get(out HashSet<string> seenTypes)
                )
                {
                    IEnumerable<Assembly> assemblies = ReflectionHelpers.GetAllLoadedAssemblies();
                    foreach (Assembly assembly in assemblies)
                    {
                        if (assembly == null || assembly.IsDynamic)
                        {
                            continue;
                        }

                        Type[] exportedTypes = GetAssemblyTypes(assembly);
                        foreach (Type type in exportedTypes)
                        {
                            if (type == null)
                            {
                                continue;
                            }

                            string assemblyQualifiedName =
                                type.AssemblyQualifiedName ?? string.Empty;
                            string uniquenessKey = string.IsNullOrEmpty(assemblyQualifiedName)
                                ? $"{type.FullName ?? type.Name}|{assembly.GetName()?.Name ?? string.Empty}"
                                : assemblyQualifiedName;
                            if (!seenTypes.Add(uniquenessKey))
                            {
                                continue;
                            }

                            signatures.Add(
                                new TypeSignature(
                                    type.Name,
                                    type.FullName ?? string.Empty,
                                    assemblyQualifiedName
                                )
                            );
                        }
                    }

                    _typeSignatures = signatures.ToArray();
                }
            }
        }

        /// <summary>
        /// Provides quick insight into a regex ignore pattern.
        /// </summary>
        internal readonly struct PatternStats
        {
            /// <summary>
            /// Captures the validation results for a regex ignore pattern.
            /// </summary>
            /// <param name="pattern">The pattern that was evaluated.</param>
            /// <param name="isValid">Whether the pattern compiled without errors.</param>
            /// <param name="matchCount">How many types matched the pattern.</param>
            /// <param name="errorMessage">Compilation error message, if any.</param>
            /// <example>
            /// <code><![CDATA[
            /// PatternStats stats = new PatternStats(".*Tests$", true, 12, string.Empty);
            /// Debug.Log(stats.MatchCount);
            /// ]]></code>
            /// </example>
            public PatternStats(string pattern, bool isValid, int matchCount, string errorMessage)
            {
                Pattern = pattern ?? string.Empty;
                IsValid = isValid;
                MatchCount = matchCount;
                ErrorMessage = errorMessage;
            }

            public string Pattern { get; }

            public bool IsValid { get; }

            public int MatchCount { get; }

            public string ErrorMessage { get; }
        }

        /// <summary>
        /// Descriptor for inspector presentation.
        /// </summary>
        public readonly struct SerializableTypeDescriptor
        {
            /// <summary>
            /// Initializes a descriptor that feeds the inspector with display and lookup data.
            /// </summary>
            /// <param name="type">Actual runtime type (optional).</param>
            /// <param name="assemblyQualifiedName">Fully qualified name used for serialization.</param>
            /// <param name="displayName">Friendly label displayed in the inspector.</param>
            /// <param name="tooltip">Optional tooltip (typically assembly-qualified name).</param>
            /// <example>
            /// <code><![CDATA[
            /// SerializableTypeDescriptor descriptor = new SerializableTypeDescriptor(
            ///     typeof(AudioClip),
            ///     typeof(AudioClip).AssemblyQualifiedName,
            ///     "UnityEngine.AudioClip"
            /// );
            /// ]]></code>
            /// </example>
            public SerializableTypeDescriptor(
                Type type,
                string assemblyQualifiedName,
                string displayName,
                string tooltip
            )
            {
                Type = type;
                AssemblyQualifiedName = assemblyQualifiedName ?? string.Empty;
                DisplayName = displayName ?? string.Empty;
                Tooltip = tooltip ?? string.Empty;
            }

            public Type Type { get; }

            public string AssemblyQualifiedName { get; }

            public string DisplayName { get; }

            public string Tooltip { get; }

            internal bool Matches(string search)
            {
                if (string.IsNullOrEmpty(search))
                {
                    return true;
                }

                if (
                    DisplayName.StartsWith(search, StringComparison.OrdinalIgnoreCase)
                    || AssemblyQualifiedName.StartsWith(search, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }

                if (Type == null)
                {
                    return false;
                }

                if (
                    !string.IsNullOrEmpty(Type.FullName)
                    && Type.FullName.StartsWith(search, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }

                if (
                    !string.IsNullOrEmpty(Type.Name)
                    && Type.Name.StartsWith(search, StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }

                return false;
            }
        }

        internal static bool ShouldSkipType(Type type)
        {
            if (type == null)
            {
                return true;
            }

            if (
                ReflectionHelpers.HasAttributeSafe<CompilerGeneratedAttribute>(type, inherit: false)
            )
            {
                return true;
            }

            return MatchesConfiguredIgnorePattern(type);
        }

        private readonly struct TypeSignature
        {
            /// <summary>
            /// Snapshot of type metadata used for filtering results.
            /// </summary>
            /// <param name="name">Short type name.</param>
            /// <param name="fullName">Fully qualified name.</param>
            /// <param name="assemblyQualifiedName">String emitted during serialization.</param>
            public TypeSignature(string name, string fullName, string assemblyQualifiedName)
            {
                Name = name ?? string.Empty;
                FullName = fullName ?? string.Empty;
                AssemblyQualifiedName = assemblyQualifiedName ?? string.Empty;
            }

            public string Name { get; }

            public string FullName { get; }

            public string AssemblyQualifiedName { get; }
        }
    }

    internal sealed class SerializableTypeJsonConverter : JsonConverter<SerializableType>
    {
        /// <summary>
        /// Reads a <see cref="SerializableType"/> from JSON by capturing the assembly-qualified string.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableType type = JsonSerializer.Deserialize<SerializableType>("\"UnityEngine.GameObject, UnityEngine\"");
        /// ]]></code>
        /// </example>
        public override SerializableType Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return default;
            }

            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("SerializableType expects a JSON string.");
            }

            string serialized = reader.GetString();
            return SerializableType.FromSerializedName(serialized);
        }

        /// <summary>
        /// Writes the assembly-qualified type string, or <c>null</c> when the value is empty.
        /// </summary>
        /// <example>
        /// <code><![CDATA[
        /// SerializableType type = SerializableType.FromType(typeof(GameObject));
        /// string json = JsonSerializer.Serialize(type);
        /// ]]></code>
        /// </example>
        public override void Write(
            Utf8JsonWriter writer,
            SerializableType value,
            JsonSerializerOptions options
        )
        {
            if (value.IsEmpty)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value.AssemblyQualifiedName);
        }
    }
}
