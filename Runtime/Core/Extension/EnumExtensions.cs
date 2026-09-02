// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// ReSharper disable StaticMemberInGenericType
namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using Attributes;
    using Helper;

    /// <summary>
    /// Internal cache data structure for storing enum name mappings optimized for fast lookup.
    /// </summary>
    internal sealed class EnumNameCacheData
    {
        public readonly string[] namesArray;
        public readonly ConcurrentDictionary<ulong, string> namesDict;
        public readonly bool useArray;
        public readonly ulong minValue;
        public readonly int arrayLength;

        public EnumNameCacheData(
            string[] namesArray,
            ConcurrentDictionary<ulong, string> namesDict,
            bool useArray,
            ulong minValue,
            int arrayLength
        )
        {
            this.namesArray = namesArray;
            this.namesDict = namesDict;
            this.useArray = useArray;
            this.minValue = minValue;
            this.arrayLength = arrayLength;
        }
    }

    /// <summary>
    /// Provides high-performance cached enum name lookups with zero allocation for frequently accessed enum values.
    /// </summary>
    /// <typeparam name="T">The unmanaged enum type to cache names for.</typeparam>
    /// <remarks>
    /// Uses array-based lookup for enums with small ranges (≤256 values) and dictionary-based lookup for larger enums.
    /// Thread-safe with reader-writer locking for dictionary operations.
    /// Performance: O(1) lookups for both array and dictionary strategies.
    /// </remarks>
    public static class EnumNameCache<T>
        where T : unmanaged, Enum
    {
        // Use instance holder to avoid static field access overhead on Mono
        private static readonly EnumNameCacheData Cache;

        static EnumNameCache()
        {
            Array rawValues = Enum.GetValues(typeof(T));
            T[] values = Unsafe.As<Array, T[]>(ref rawValues);
            string[] names = Enum.GetNames(typeof(T));

            bool useArray = EnumLookupStrategy<T>.TryComputeArrayWindow(
                values,
                out ulong windowMinValue,
                out int windowLength
            );

            string[] namesArray;
            ConcurrentDictionary<ulong, string> namesDict;
            ulong minValue;
            int arrayLength;

            if (useArray)
            {
                minValue = windowMinValue;
                arrayLength = windowLength;
                namesArray = new string[arrayLength];

                for (int i = 0; i < values.Length; i++)
                {
                    T value = values[i];
                    if (EnumNumericHelper<T>.TryConvertToUInt64(value, out ulong key))
                    {
                        /*
                            Unsigned subtraction, matching ToCachedName's lookup exactly, so a
                            window that straddles zero indexes the same slot on both sides.
                        */
                        ulong index = unchecked(key - minValue);
                        if (index < (ulong)arrayLength)
                        {
                            string name = names[i];
                            if (namesArray[index] == null)
                            {
                                namesArray[index] = name;
                            }
                        }
                    }
                }
                namesDict = new ConcurrentDictionary<ulong, string>();
            }
            else
            {
                // Fall back to dictionary
                namesDict = new ConcurrentDictionary<ulong, string>();

                foreach (T value in values)
                {
                    if (!EnumNumericHelper<T>.TryConvertToUInt64(value, out ulong key))
                    {
                        continue;
                    }

                    string name = value.ToString("G");
                    namesDict.TryAdd(key, name);
                }
                namesArray = null;
                minValue = 0;
                arrayLength = 0;
            }

            Cache = new EnumNameCacheData(namesArray, namesDict, useArray, minValue, arrayLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToCachedName(T value)
        {
            if (!EnumNumericHelper<T>.TryConvertToUInt64(value, out ulong key))
            {
                return value.ToString("G");
            }

            EnumNameCacheData cache = Cache;
            if (cache.useArray && cache.namesArray != null)
            {
                ulong index = key - cache.minValue;
                if (index < (ulong)cache.arrayLength)
                {
                    string existing = cache.namesArray[index];
                    if (existing != null)
                    {
                        return existing;
                    }

                    string generated = value.ToString("G");
                    string prior = Interlocked.CompareExchange(
                        ref cache.namesArray[index],
                        generated,
                        null
                    );
                    return prior ?? generated;
                }
            }

            ConcurrentDictionary<ulong, string> namesDict = cache.namesDict;
            if (namesDict != null)
            {
                if (namesDict.TryGetValue(key, out string name))
                {
                    return name;
                }
            }

            /*
                A miss is an undefined value or a composite of flags, and there are up to 2^64 of
                those. Caching one grows a process-lifetime dictionary from a stream the caller
                controls, so the answer is formatted fresh instead. Only declared members are
                cached.
            */

            return value.ToString("G");
        }
    }

    /// <summary>
    /// Internal cache data structure for storing enum display name mappings from EnumDisplayNameAttribute.
    /// </summary>
    internal sealed class EnumDisplayNameCacheData
    {
        public readonly string[] namesArray;
        public readonly ConcurrentDictionary<ulong, string> namesDict;
        public readonly bool useArray;
        public readonly ulong minValue;
        public readonly int arrayLength;

        public EnumDisplayNameCacheData(
            string[] namesArray,
            ConcurrentDictionary<ulong, string> namesDict,
            bool useArray,
            ulong minValue,
            int arrayLength
        )
        {
            this.namesArray = namesArray;
            this.namesDict = namesDict;
            this.useArray = useArray;
            this.minValue = minValue;
            this.arrayLength = arrayLength;
        }
    }

    /// <summary>
    /// Provides high-performance cached enum display name lookups using EnumDisplayNameAttribute values.
    /// </summary>
    /// <typeparam name="T">The unmanaged enum type to cache display names for.</typeparam>
    /// <remarks>
    /// Uses reflection to extract EnumDisplayNameAttribute values at startup, then caches for fast access.
    /// Falls back to field name if attribute is not present.
    /// Uses array-based lookup for enums with small ranges (≤256 values) and dictionary-based lookup for larger enums.
    /// Thread-safe with concurrent dictionary operations.
    /// Performance: O(1) lookups for both array and dictionary strategies.
    /// </remarks>
    public static class EnumDisplayNameCache<T>
        where T : unmanaged, Enum
    {
        // Use instance holder to avoid static field access overhead on Mono
        private static readonly EnumDisplayNameCacheData Cache;

        static EnumDisplayNameCache()
        {
            Type type = typeof(T);
            FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
            T[] fieldValues = new T[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                fieldValues[i] = (T)fields[i].GetValue(null);
            }

            bool useArray = EnumLookupStrategy<T>.TryComputeArrayWindow(
                fieldValues,
                out ulong windowMinValue,
                out int windowLength
            );

            string[] namesArray;
            ConcurrentDictionary<ulong, string> namesDict;
            ulong minValue;
            int arrayLength;

            if (useArray)
            {
                minValue = windowMinValue;
                arrayLength = windowLength;
                namesArray = new string[arrayLength];

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    string name = field.IsAttributeDefined(
                        out EnumDisplayNameAttribute displayName,
                        inherit: false
                    )
                        ? displayName.DisplayName
                        : field.Name;

                    if (EnumNumericHelper<T>.TryConvertToUInt64(fieldValues[i], out ulong key))
                    {
                        /*
                            Unsigned subtraction, matching ToDisplayName's lookup exactly, so a
                            window that straddles zero indexes the same slot on both sides.
                        */
                        ulong index = unchecked(key - minValue);
                        if (index < (ulong)arrayLength)
                        {
                            namesArray[index] = name;
                        }
                    }
                }
                namesDict = new ConcurrentDictionary<ulong, string>(
                    Environment.ProcessorCount,
                    fields.Length
                );
            }
            else
            {
                // Fall back to dictionary
                namesDict = new ConcurrentDictionary<ulong, string>(
                    Environment.ProcessorCount,
                    fields.Length
                );

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    string name = field.IsAttributeDefined(
                        out EnumDisplayNameAttribute displayName,
                        inherit: false
                    )
                        ? displayName.DisplayName
                        : field.Name;

                    if (!EnumNumericHelper<T>.TryConvertToUInt64(fieldValues[i], out ulong key))
                    {
                        continue;
                    }

                    namesDict.TryAdd(key, name);
                }
                namesArray = null;
                minValue = 0;
                arrayLength = 0;
            }

            Cache = new EnumDisplayNameCacheData(
                namesArray,
                namesDict,
                useArray,
                minValue,
                arrayLength
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ToDisplayName(T value)
        {
            if (!EnumNumericHelper<T>.TryConvertToUInt64(value, out ulong key))
            {
                return value.ToString("G");
            }

            EnumDisplayNameCacheData cache = Cache;
            if (cache.useArray && cache.namesArray != null)
            {
                ulong index = key - cache.minValue;
                if (index < (ulong)cache.arrayLength)
                {
                    string existing = cache.namesArray[index];
                    if (existing != null)
                    {
                        return existing;
                    }

                    string generated = value.ToString("G");
                    string prior = Interlocked.CompareExchange(
                        ref cache.namesArray[index],
                        generated,
                        null
                    );
                    return prior ?? generated;
                }
            }

            ConcurrentDictionary<ulong, string> namesDict = cache.namesDict;
            if (namesDict != null)
            {
                if (namesDict.TryGetValue(key, out string name))
                {
                    return name;
                }
            }

            /*
                A miss is an undefined value or a composite of flags, and there are up to 2^64 of
                those. Caching one grows a process-lifetime dictionary from a stream the caller
                controls, so the answer is formatted fresh instead. Only declared members are
                cached.
            */

            return value.ToString("G");
        }
    }

    /// <summary>
    /// Extension methods for enum types providing allocation-free flag checking and cached name conversions.
    /// </summary>
    /// <remarks>
    /// Thread Safety: All methods are thread-safe.
    /// Performance: Methods use caching and aggressive inlining for optimal performance.
    /// </remarks>
    public static class EnumExtensions
    {
        /// <summary>
        /// Checks if an enum value has a specific flag set without boxing allocation.
        /// </summary>
        /// <typeparam name="T">The unmanaged enum type (must be a flags enum for meaningful results).</typeparam>
        /// <param name="value">The enum value to check.</param>
        /// <param name="flag">The flag to check for.</param>
        /// <returns>True if the flag is set, false otherwise.</returns>
        /// <remarks>
        /// Null handling: N/A - operates on value types.
        /// Thread-safe: Yes.
        /// Performance: O(1) - uses bitwise operations on underlying numeric type.
        /// Allocations: Zero allocations (no boxing). Falls back to built-in HasFlag for unsupported enum sizes.
        /// Edge cases: Works with enum sizes 1, 2, 4, or 8 bytes. Larger sizes fall back to HasFlag.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool HasFlagNoAlloc<T>(this T value, T flag)
            where T : unmanaged, Enum
        {
            if (
                !EnumNumericHelper<T>.TryConvertToUInt64(value, out ulong valueUnderlying)
                || !EnumNumericHelper<T>.TryConvertToUInt64(flag, out ulong flagUnderlying)
            )
            {
                // Fallback for unsupported enum sizes
                return value.HasFlag(flag);
            }

            return (valueUnderlying & flagUnderlying) == flagUnderlying;
        }

        /// <summary>
        /// Converts an enum value to its display name using the EnumDisplayNameAttribute if present, otherwise the field name.
        /// </summary>
        /// <typeparam name="T">The unmanaged enum type.</typeparam>
        /// <param name="value">The enum value to convert.</param>
        /// <returns>The display name string from the attribute, or the enum's ToString() if not cached.</returns>
        /// <remarks>
        /// Null handling: N/A - operates on value types.
        /// Thread-safe: Yes.
        /// Performance: O(1) - uses cached lookups via EnumDisplayNameCache.
        /// Allocations: Zero for cached values, one string allocation for uncached values on first access.
        /// Edge cases: Returns ToString("G") for values not in the cache.
        /// </remarks>
        public static string ToDisplayName<T>(this T value)
            where T : unmanaged, Enum
        {
            return EnumDisplayNameCache<T>.ToDisplayName(value);
        }

        /// <summary>
        /// Converts a collection of enum values to their display names.
        /// </summary>
        /// <typeparam name="T">The unmanaged enum type.</typeparam>
        /// <param name="enumerable">The collection of enum values to convert.</param>
        /// <returns>An enumerable of display name strings.</returns>
        /// <remarks>
        /// Null handling: Throws if enumerable is null when enumerated.
        /// Thread-safe: Yes for reads.
        /// Performance: O(n) where n is the number of enum values. Uses cached lookups.
        /// Allocations: Allocates LINQ iterator. Minimal allocations for cached display names.
        /// Edge cases: Empty collection returns empty enumerable.
        /// Laziness: Uses deferred execution - values are transformed only when enumerated.
        /// </remarks>
        public static IEnumerable<string> ToDisplayNames<T>(this IEnumerable<T> enumerable)
            where T : unmanaged, Enum
        {
            return enumerable.Select(value => value.ToDisplayName());
        }

        /// <summary>
        /// Converts an enum value to its name string using a high-performance cache.
        /// </summary>
        /// <typeparam name="T">The unmanaged enum type.</typeparam>
        /// <param name="value">The enum value to convert.</param>
        /// <returns>The cached name string, or ToString("G") if not cached.</returns>
        /// <remarks>
        /// Null handling: N/A - operates on value types.
        /// Thread-safe: Yes with reader-writer locking.
        /// Performance: O(1) - uses cached lookups via EnumNameCache.
        /// Allocations: Zero for cached values, one string allocation for uncached values on first access.
        /// Edge cases: Returns ToString("G") for values not in the cache.
        /// </remarks>
        public static string ToCachedName<T>(this T value)
            where T : unmanaged, Enum
        {
            return EnumNameCache<T>.ToCachedName(value);
        }

        /// <summary>
        /// Converts a collection of enum values to their cached name strings.
        /// </summary>
        /// <typeparam name="T">The unmanaged enum type.</typeparam>
        /// <param name="enumerable">The collection of enum values to convert.</param>
        /// <returns>An enumerable of cached name strings.</returns>
        /// <remarks>
        /// Null handling: Throws if enumerable is null when enumerated.
        /// Thread-safe: Yes for reads.
        /// Performance: O(n) where n is the number of enum values. Uses cached lookups.
        /// Allocations: Allocates LINQ iterator. Minimal allocations for cached names.
        /// Edge cases: Empty collection returns empty enumerable.
        /// Laziness: Uses deferred execution - values are transformed only when enumerated.
        /// </remarks>
        public static IEnumerable<string> ToCachedNames<T>(this IEnumerable<T> enumerable)
            where T : unmanaged, Enum
        {
            return enumerable.Select(value => value.ToCachedName());
        }

        /// <summary>
        /// Converts an enum value to the 64-bit two's-complement pattern of its underlying type,
        /// without boxing.
        /// </summary>
        /// <typeparam name="T">The unmanaged enum type.</typeparam>
        /// <param name="value">The enum value to convert.</param>
        /// <param name="result">The converted bit pattern, or 0 when conversion fails.</param>
        /// <returns>True when <paramref name="value"/> was converted; otherwise, false.</returns>
        /// <remarks>
        /// Null handling: Not applicable; <typeparamref name="T"/> is a value type.
        /// Thread-safe: Yes.
        /// Performance: O(1).
        /// Allocations: None. Prefer this overload wherever the enum type is known at compile time;
        /// the <see cref="Enum"/> overload exists for editor tooling, which only ever holds enum
        /// values as <see cref="object"/> and so cannot supply a type argument.
        /// Edge cases: Signed underlying types are sign-extended, matching the boxed overload
        /// exactly, so the two agree for every member of every shape.
        /// </remarks>
        /// <example>
        /// <code><![CDATA[
        /// if (Direction.Left.TryConvertToUInt64(out ulong pattern))
        /// {
        ///     Debug.Log(pattern);
        /// }
        /// ]]></code>
        /// </example>
        public static bool TryConvertToUInt64<T>(this T value, out ulong result)
            where T : unmanaged, Enum
        {
            return EnumNumericHelper<T>.TryConvertToUInt64(value, out result);
        }

        /// <summary>
        /// Converts an enum value to the signed 64-bit pattern Unity's serialized properties store,
        /// without boxing.
        /// </summary>
        /// <typeparam name="T">The unmanaged enum type.</typeparam>
        /// <param name="value">The enum value to convert.</param>
        /// <param name="result">The converted value, or 0 when conversion fails.</param>
        /// <returns>True when <paramref name="value"/> was converted; otherwise, false.</returns>
        /// <remarks>
        /// Null handling: Not applicable; <typeparamref name="T"/> is a value type.
        /// Thread-safe: Yes.
        /// Performance: O(1).
        /// Allocations: None.
        /// Edge cases: Identical bit pattern to the <see cref="Enum"/> overload.
        /// </remarks>
        /// <example>
        /// <code><![CDATA[
        /// if (Direction.Left.TryConvertToInt64(out long serialized))
        /// {
        ///     property.longValue = serialized;
        /// }
        /// ]]></code>
        /// </example>
        public static bool TryConvertToInt64<T>(this T value, out long result)
            where T : unmanaged, Enum
        {
            if (!EnumNumericHelper<T>.TryConvertToUInt64(value, out ulong unsigned))
            {
                result = 0L;
                return false;
            }

            result = unchecked((long)unsigned);
            return true;
        }

        /// <summary>
        /// Converts a boxed enum value to the 64-bit two's-complement pattern of its underlying type.
        /// </summary>
        /// <param name="value">The enum value to convert.</param>
        /// <param name="result">The converted bit pattern, or 0 when conversion fails.</param>
        /// <returns>True when <paramref name="value"/> was converted; otherwise, false.</returns>
        /// <remarks>
        /// Null handling: A null value returns false.
        /// Thread-safe: Yes.
        /// Performance: O(1).
        /// Allocations: None beyond the caller's existing box.
        /// Edge cases: Signed underlying types are sign-extended, so a negative member converts to
        /// its full 64-bit pattern rather than overflowing; <see cref="ulong"/>-backed members above
        /// <see cref="long.MaxValue"/> convert without overflowing. This is the boxed counterpart of
        /// the generic path used by <see cref="ToCachedName{T}"/> and
        /// <see cref="HasFlagNoAlloc{T}"/>, and exists because editor tooling only ever holds enum
        /// values as <see cref="object"/>.
        /// </remarks>
        /// <example>
        /// <code><![CDATA[
        /// // Editor tooling holds enum members as object, so the type is not known statically.
        /// object member = System.Enum.GetValues(enumType).GetValue(0);
        /// if (member is Enum boxed && boxed.TryConvertToUInt64(out ulong pattern))
        /// {
        ///     Debug.Log(pattern);
        /// }
        /// ]]></code>
        /// </example>
        public static bool TryConvertToUInt64(this Enum value, out ulong result)
        {
            if (value == null)
            {
                result = 0UL;
                return false;
            }

            /*
                Convert.ToUInt64 throws OverflowException on every negative member, and
                Convert.ToInt64 throws on ulong members above long.MaxValue. Dispatching on the
                underlying type is the only conversion that is total over all nine enum shapes.
            */
            IConvertible convertible = value;
            switch (Type.GetTypeCode(value.GetType()))
            {
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    result = unchecked((ulong)convertible.ToInt64(CultureInfo.InvariantCulture));
                    return true;
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    result = convertible.ToUInt64(CultureInfo.InvariantCulture);
                    return true;
                default:
                    result = 0UL;
                    return false;
            }
        }

        /// <summary>
        /// Converts a boxed enum value to the signed 64-bit pattern Unity's serialized properties store.
        /// </summary>
        /// <param name="value">The enum value to convert.</param>
        /// <param name="result">The converted value, or 0 when conversion fails.</param>
        /// <returns>True when <paramref name="value"/> was converted; otherwise, false.</returns>
        /// <remarks>
        /// Null handling: A null value returns false.
        /// Thread-safe: Yes.
        /// Performance: O(1).
        /// Allocations: None beyond the caller's existing box.
        /// Edge cases: Total over every underlying type, including negative members and
        /// <see cref="ulong"/>-backed members above <see cref="long.MaxValue"/>, which round-trip
        /// through Unity's serialized properties as the same bit pattern.
        /// </remarks>
        /// <example>
        /// <code><![CDATA[
        /// if (option.Value is Enum boxed && boxed.TryConvertToInt64(out long serialized))
        /// {
        ///     property.longValue = serialized;
        /// }
        /// ]]></code>
        /// </example>
        public static bool TryConvertToInt64(this Enum value, out long result)
        {
            if (!value.TryConvertToUInt64(out ulong unsigned))
            {
                result = 0L;
                return false;
            }

            result = unchecked((long)unsigned);
            return true;
        }
    }

    /// <summary>
    /// Internal helper class for converting enum values to their underlying numeric representation without boxing.
    /// </summary>
    /// <typeparam name="T">The unmanaged enum type.</typeparam>
    internal static class EnumNumericHelper<T>
        where T : unmanaged, Enum
    {
        /// <summary>
        /// True when the enum's underlying type is signed and can therefore hold negative members.
        /// </summary>
        public static readonly bool IsSigned = ResolveIsSigned();

        private static readonly int Size = Unsafe.SizeOf<T>();

        /*
            Signed underlying types are SIGN-extended to the full 64-bit two's-complement
            pattern, not zero-extended. Zero-extending a negative sbyte/short/int yields a
            key that is numerically large but only 8/16/32 bits wide, while every consumer
            does `key - minValue` in 64-bit modular arithmetic -- so the wrap that should
            land a negative member on a small array index instead lands astronomically far
            from it. Sign extension makes every width behave like the 8-byte case, where
            that arithmetic has always been correct.
        */
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryConvertToUInt64(T value, out ulong result)
        {
            ref T valueRef = ref Unsafe.AsRef(in value);

            switch (Size)
            {
                case 1:
                    result = IsSigned
                        ? unchecked((ulong)(long)Unsafe.As<T, sbyte>(ref valueRef))
                        : Unsafe.As<T, byte>(ref valueRef);
                    return true;
                case 2:
                    result = IsSigned
                        ? unchecked((ulong)(long)Unsafe.As<T, short>(ref valueRef))
                        : Unsafe.As<T, ushort>(ref valueRef);
                    return true;
                case 4:
                    result = IsSigned
                        ? unchecked((ulong)(long)Unsafe.As<T, int>(ref valueRef))
                        : Unsafe.As<T, uint>(ref valueRef);
                    return true;
                case 8:
                    result = Unsafe.As<T, ulong>(ref valueRef);
                    return true;
                default:
                    result = default;
                    return false;
            }
        }

        /// <summary>
        /// Orders two converted keys the way the enum's own members order, so a signed
        /// enum's negative members sort below its positive ones instead of above them.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsLessThan(ulong left, ulong right)
        {
            if (IsSigned)
            {
                return unchecked((long)left) < unchecked((long)right);
            }

            return left < right;
        }

        private static bool ResolveIsSigned()
        {
            switch (Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T))))
            {
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// Internal helper that decides whether an enum's values are dense enough for the
    /// array-indexed lookup strategy the name caches prefer.
    /// </summary>
    /// <typeparam name="T">The unmanaged enum type.</typeparam>
    internal static class EnumLookupStrategy<T>
        where T : unmanaged, Enum
    {
        /// <summary>
        /// Largest number of array slots a name cache will allocate for an enum.
        /// </summary>
        public const int MaximumArrayLength = 256;

        /// <summary>
        /// Computes the array-lookup window for a set of enum values.
        /// </summary>
        /// <param name="values">The enum's declared values.</param>
        /// <param name="minValue">The converted key that maps to array index 0.</param>
        /// <param name="arrayLength">The number of array slots the window spans.</param>
        /// <returns>True when an array lookup is worthwhile, false to use a dictionary.</returns>
        /// <remarks>
        /// Range is measured in the enum's OWN ordering (see <see cref="EnumNumericHelper{T}.IsLessThan"/>),
        /// which is what keeps a small signed enum such as `{ -2, -1, 0, 1 }` on the array
        /// path. Measuring it on unsigned keys made every signed enum with a negative
        /// member look billions of slots wide and silently demoted it to the dictionary.
        /// </remarks>
        public static bool TryComputeArrayWindow(
            T[] values,
            out ulong minValue,
            out int arrayLength
        )
        {
            if (values == null || values.Length == 0)
            {
                minValue = 0;
                arrayLength = 0;
                return false;
            }

            ulong minKey = 0;
            ulong maxKey = 0;
            bool hasAny = false;

            foreach (T value in values)
            {
                if (!EnumNumericHelper<T>.TryConvertToUInt64(value, out ulong key))
                {
                    minValue = 0;
                    arrayLength = 0;
                    return false;
                }

                if (!hasAny)
                {
                    minKey = key;
                    maxKey = key;
                    hasAny = true;
                    continue;
                }

                if (EnumNumericHelper<T>.IsLessThan(key, minKey))
                {
                    minKey = key;
                }

                if (EnumNumericHelper<T>.IsLessThan(maxKey, key))
                {
                    maxKey = key;
                }
            }

            if (!hasAny)
            {
                minValue = 0;
                arrayLength = 0;
                return false;
            }

            /*
                Modular subtraction, so a window that straddles zero (or wraps the unsigned
                domain) still measures its true width. Both operands come from the same
                64-bit key space, so the difference is exact whenever it fits the cap below.
            */
            ulong span = unchecked(maxKey - minKey);
            if ((ulong)MaximumArrayLength <= span)
            {
                minValue = 0;
                arrayLength = 0;
                return false;
            }

            minValue = minKey;
            arrayLength = (int)span + 1;
            return true;
        }
    }
}
