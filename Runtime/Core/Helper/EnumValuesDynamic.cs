// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Concurrent;

    /// <summary>Provides cached declared values for an enum known by its runtime type.</summary>
    public static class EnumValues
    {
        private static readonly ConcurrentDictionary<Type, Array> Values = new();
        private static readonly Func<Type, Array> CreateValues = Enum.GetValues;

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
        /// <remarks>Never modify the returned array. Memory exhaustion is not converted to a false result.</remarks>
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
                values = null;
                return false;
            }
            values = null;
            return false;
        }
    }
}
