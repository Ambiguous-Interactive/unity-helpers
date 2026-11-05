// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    public static partial class IEnumerableExtensions
    {
        /// <summary>Retains the original static factory for a lazy value view.</summary>
        /// <remarks>Use WAsValueEnumerable for extension syntax; the original name is static-only to avoid third-party collisions.</remarks>
        public static ValueEnumerable<T> AsValueEnumerable<T>(IEnumerable<T> source)
        {
            return source.WAsValueEnumerable();
        }

        /// <summary>Retains the original static factory for a lazy dictionary value view.</summary>
        /// <remarks>Use WAsValueDictionaryEnumerable for extension syntax.</remarks>
        public static ValueDictionaryEnumerable<TKey, TValue> AsValueDictionaryEnumerable<
            TKey,
            TValue
        >(IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            return source.WAsValueDictionaryEnumerable();
        }

        /// <summary>Creates a lazy view that avoids enumerator boxing for arrays, exact lists, and exact hash sets.</summary>
        /// <remarks>Null is empty. Custom sequences and derived collections keep their original enumerator, including cleanup and mutation behavior.</remarks>
        public static ValueEnumerable<T> WAsValueEnumerable<T>(this IEnumerable<T> source)
        {
            return new ValueEnumerable<T>(source);
        }

        /// <summary>Creates a lazy view that avoids boxing exact dictionary enumerators.</summary>
        /// <remarks>Derived dictionaries and other sources keep their original enumeration and cleanup, including allocations. Null is empty.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ValueDictionaryEnumerable<TKey, TValue> WAsValueDictionaryEnumerable<
            TKey,
            TValue
        >(this IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            return new ValueDictionaryEnumerable<TKey, TValue>(source);
        }

        internal static ValueEnumerable<T> AsValueEnumerableRequired<T>(this IEnumerable<T> source)
        {
            return new ValueEnumerable<T>(source, required: true);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ValueDictionaryEnumerable<TKey, TValue> AsValueDictionaryEnumerableRequired<
            TKey,
            TValue
        >(this IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            return new ValueDictionaryEnumerable<TKey, TValue>(source, required: true);
        }
    }
}
