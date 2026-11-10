// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;

    /// <summary>Provides a cached, read-only view of an enum's declared values.</summary>
    /// <typeparam name="T">The enum type.</typeparam>
    public static class EnumValues<T>
        where T : struct, Enum
    {
        /// <summary>Gets all declared values in unsigned numeric order, retaining aliases.</summary>
        public static ReadOnlySpan<T> Values => EnumValuesCache<T>.Values;

        /// <summary>Checks whether the value is declared, without boxing or accepting undeclared flags combinations.</summary>
        public static bool IsDefined(T value)
        {
            return EnumMembershipCache<T>.IsDefined(value);
        }
    }
}
