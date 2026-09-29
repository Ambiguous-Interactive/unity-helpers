// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;

    internal static class EnumValuesCache<T>
        where T : struct
    {
        internal static ReadOnlySpan<T> Values => CachedValues;

        private static readonly T[] CachedValues = (T[])Enum.GetValues(typeof(T));
    }
}
