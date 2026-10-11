// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>Enumerates exact dictionaries without boxing and preserves other source protocols.</summary>
    /// <remarks>Current is meaningful after MoveNext succeeds. Copies of a fallback enumerator share its lifetime.</remarks>
    public struct ValueDictionaryEnumerator<TKey, TValue> : IDisposable
    {
        private const int EmptyKind = 0;
        private const int DictionaryKind = 1;
        private const int FallbackKind = 2;

        /// <summary>Gets the current key and value.</summary>
        public KeyValuePair<TKey, TValue> Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>
                _kind == DictionaryKind ? _dictionary.Current
                : _kind == FallbackKind ? _fallback.Current
                : default;
        }

        private readonly int _kind;
        private Dictionary<TKey, TValue>.Enumerator _dictionary;
        private IEnumerator<KeyValuePair<TKey, TValue>> _fallback;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValueDictionaryEnumerator(
            IEnumerable<KeyValuePair<TKey, TValue>> source,
            bool required
        )
        {
            _dictionary = default;
            _fallback = null;
            if (source == null)
            {
                _kind = required ? FallbackKind : EmptyKind;
                if (required)
                {
                    _fallback = source.GetEnumerator();
                }
            }
            else if (source.GetType() == typeof(Dictionary<TKey, TValue>))
            {
                _kind = DictionaryKind;
                _dictionary = ((Dictionary<TKey, TValue>)source).GetEnumerator();
            }
            else
            {
                _kind = FallbackKind;
                _fallback = source.GetEnumerator();
            }
        }

        /// <summary>Advances using the source's mutation policy.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            return _kind == DictionaryKind
                ? _dictionary.MoveNext()
                : _kind == FallbackKind && _fallback.MoveNext();
        }

        /// <summary>Releases the source enumerator when cleanup is required.</summary>
        public void Dispose()
        {
            if (_kind == DictionaryKind)
            {
                _dictionary.Dispose();
            }
            else if (_kind == FallbackKind && _fallback != null)
            {
                _fallback.Dispose();
            }
        }
    }
}
