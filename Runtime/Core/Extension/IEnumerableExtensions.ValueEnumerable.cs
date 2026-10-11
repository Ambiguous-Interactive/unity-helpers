// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    public static partial class IEnumerableExtensions
    {
        /// <summary>Creates a lazy view that avoids enumerator boxing for arrays, exact lists, and exact hash sets.</summary>
        /// <remarks>Null is empty. Custom sequences and derived collections keep their original enumerator, including cleanup and mutation behavior.</remarks>
        public static ValueEnumerable<T> AsValueEnumerable<T>(this IEnumerable<T> source)
        {
            return new ValueEnumerable<T>(source);
        }

        internal static ValueEnumerable<T> AsValueEnumerableRequired<T>(this IEnumerable<T> source)
        {
            return new ValueEnumerable<T>(source, required: true);
        }
    }
}
