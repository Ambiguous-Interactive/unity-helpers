// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Extension;

    /// <summary>Orders enum values by their cached names using ordinal comparison.</summary>
    /// <typeparam name="T">The enum type.</typeparam>
    /// <remarks>Names not retained by the bounded cache may allocate when formatted.</remarks>
    public sealed class EnumNameComparer<T> : IComparer<T>
        where T : unmanaged, Enum
    {
        /// <summary>The shared, stateless comparer instance.</summary>
        public static readonly EnumNameComparer<T> Instance = new();

        private EnumNameComparer() { }

        /// <summary>Compares two cached enum names using ordinal comparison.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(T x, T y)
        {
            return string.Compare(x.ToCachedName(), y.ToCachedName(), StringComparison.Ordinal);
        }
    }
}
