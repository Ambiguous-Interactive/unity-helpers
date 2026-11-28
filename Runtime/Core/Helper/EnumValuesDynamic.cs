// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Concurrent;
    using System.Globalization;

    /// <summary>Provides cached declared values for an enum known by its runtime type.</summary>
    public static class EnumValues
    {
        private static readonly ConcurrentDictionary<Type, Array> Values = new();
        private static readonly Func<Type, Array> CreateValues = Enum.GetValues;
        private static readonly ConcurrentDictionary<Type, string[]> Names = new();
        private static readonly Func<Type, string[]> CreateNames = Enum.GetNames;
        private static readonly ConcurrentDictionary<Type, object[]> ParseValues = new();
        private static readonly Func<Type, object[]> CreateParseValues = ReadParseValues;

        /// <summary>Gets the shared values in unsigned numeric order, retaining aliases.</summary>
        /// <param name="enumType">The enum type to read.</param>
        /// <returns>The same cached array for subsequent calls with this type.</returns>
        /// <remarks>Never modify the returned array. Cache publication is thread-safe; failed reads are not cached.</remarks>
        /// <exception cref="ArgumentNullException">The type is null.</exception>
        /// <exception cref="ArgumentException">The type is not an enum.</exception>
        public static Array Of(Type enumType)
        {
            if (enumType == null)
            {
                throw new ArgumentNullException(nameof(enumType));
            }
            if (!enumType.IsEnum)
            {
                throw new ArgumentException("The type must be an enum.", nameof(enumType));
            }
            return Values.GetOrAdd(enumType, CreateValues);
        }

        /// <summary>Tries to read shared declared values without throwing for unsupported enum types.</summary>
        /// <param name="enumType">The enum type to read.</param>
        /// <param name="values">The shared array, or null when the type is unsupported.</param>
        /// <returns>Whether the enum values could be read.</returns>
        /// <remarks>Never modify the returned array. ArgumentException, InvalidOperationException and NotSupportedException produce false; other exceptions propagate.</remarks>
        public static bool TryGetValues(Type enumType, out Array values)
        {
            try
            {
                if (enumType != null && enumType.IsEnum)
                {
                    Array cached = Values.GetOrAdd(enumType, CreateValues);
                    values = cached;
                    return true;
                }
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is NotSupportedException
                )
            {
                // Unsupported metadata uses the common failure result below.
            }
            values = null;
            return false;
        }

        /// <summary>Parses a declared name on a runtime enum type.</summary>
        /// <param name="enumType">The enum type to read.</param>
        /// <param name="name">The declared name to match.</param>
        /// <param name="ignoreCase">Whether ordinal case folding is applied.</param>
        /// <param name="value">The boxed enum value, or null when no unique declared name matches.</param>
        /// <returns>Whether exactly one distinct declared value matched.</returns>
        /// <remarks>Names matching distinct values under the selected comparison are ambiguous and fail. Aliases with one value are accepted.</remarks>
        public static bool TryParse(
            Type enumType,
            ReadOnlySpan<char> name,
            bool ignoreCase,
            out object value
        )
        {
            if (enumType == null || !enumType.IsEnum)
            {
                value = null;
                return false;
            }

            object[] values;
            try
            {
                values = ParseValues.GetOrAdd(enumType, CreateParseValues);
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is NotSupportedException
                )
            {
                value = null;
                return false;
            }

            string[] names = Names.GetOrAdd(enumType, CreateNames);
            StringComparison comparison = ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            bool found = false;
            object candidate = null;
            int nameCount = names.Length;
            for (int index = 0; index < nameCount; ++index)
            {
                if (!name.Equals(names[index].AsSpan(), comparison))
                {
                    continue;
                }

                object current = values[index];
                if (found && !Equals(candidate, current))
                {
                    value = null;
                    return false;
                }

                candidate = current;
                found = true;
            }

            value = found ? candidate : null;
            return found;
        }

        /// <summary>Converts an integer to a runtime enum type when the underlying type can hold it.</summary>
        /// <param name="enumType">The enum type to create.</param>
        /// <param name="value">The numeric value.</param>
        /// <param name="result">The boxed enum value, or null when the value cannot be represented.</param>
        /// <returns>Whether the enum value was created.</returns>
        /// <remarks>The value need not be declared. Values outside the underlying type's range fail.</remarks>
        public static bool TryFromInteger(Type enumType, long value, out object result)
        {
            if (enumType == null || !enumType.IsEnum)
            {
                result = null;
                return false;
            }

            Type underlying = Enum.GetUnderlyingType(enumType);
            if (!IsInRange(underlying, value))
            {
                result = null;
                return false;
            }

            try
            {
                result = Enum.ToObject(enumType, value);
                return true;
            }
            catch (ArgumentException)
            {
                result = null;
                return false;
            }
        }

        private static object[] ReadParseValues(Type enumType)
        {
            Array values = Enum.GetValues(enumType);
            int count = values.Length;
            object[] copy = new object[count];
            for (int index = 0; index < count; ++index)
            {
                copy[index] = values.GetValue(index);
            }
            return copy;
        }

        private static bool IsInRange(Type underlying, long value)
        {
            switch (Type.GetTypeCode(underlying))
            {
                case TypeCode.SByte:
                    return sbyte.MinValue <= value && value <= sbyte.MaxValue;
                case TypeCode.Byte:
                    return 0 <= value && value <= byte.MaxValue;
                case TypeCode.Int16:
                    return short.MinValue <= value && value <= short.MaxValue;
                case TypeCode.UInt16:
                    return 0 <= value && value <= ushort.MaxValue;
                case TypeCode.Int32:
                    return int.MinValue <= value && value <= int.MaxValue;
                case TypeCode.UInt32:
                    return 0 <= value && value <= uint.MaxValue;
                case TypeCode.Int64:
                    return true;
                case TypeCode.UInt64:
                    return 0 <= value;
                default:
                    return false;
            }
        }
    }
}
