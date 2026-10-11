// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>Provides lazy dictionary enumeration without boxing an exact dictionary's enumerator.</summary>
    /// <remarks>Other sources preserve their enumeration protocol. Null sources are empty. The view does not snapshot its source.</remarks>
    public readonly struct ValueDictionaryEnumerable<TKey, TValue>
    {
        private readonly IEnumerable<KeyValuePair<TKey, TValue>> _source;
        private readonly bool _required;

        /// <summary>Creates a view without acquiring the source enumerator.</summary>
        public ValueDictionaryEnumerable(IEnumerable<KeyValuePair<TKey, TValue>> source)
        {
            _source = source;
            _required = false;
        }

        internal ValueDictionaryEnumerable(
            IEnumerable<KeyValuePair<TKey, TValue>> source,
            bool required
        )
        {
            _source = source;
            _required = required;
        }

        /// <summary>Acquires the source enumerator for this view.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueDictionaryEnumerator<TKey, TValue> GetEnumerator()
        {
            return new ValueDictionaryEnumerator<TKey, TValue>(_source, _required);
        }
    }
}
