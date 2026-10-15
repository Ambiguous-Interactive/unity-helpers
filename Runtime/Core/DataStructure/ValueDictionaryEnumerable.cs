// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>Provides lazy dictionary enumeration without boxing an exact dictionary's enumerator.</summary>
    /// <remarks>Other sources preserve their enumeration protocol. The constructor treats null as empty; RequireSource retains null-source failures. The view does not snapshot its source.</remarks>
    public readonly struct ValueDictionaryEnumerable<TKey, TValue>
        : global::System.IEquatable<ValueDictionaryEnumerable<TKey, TValue>>
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

        /// <summary>Creates a lazy view that retains null-source enumeration failures.</summary>
        public static ValueDictionaryEnumerable<TKey, TValue> RequireSource(
            IEnumerable<KeyValuePair<TKey, TValue>> source
        )
        {
            return new ValueDictionaryEnumerable<TKey, TValue>(source, true);
        }

        /// <summary>Acquires the source enumerator for this view.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValueDictionaryEnumerator<TKey, TValue> GetEnumerator()
        {
            return new ValueDictionaryEnumerator<TKey, TValue>(_source, _required);
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(ValueDictionaryEnumerable<TKey, TValue> other)
        {
            return EqualityComparer<IEnumerable<KeyValuePair<TKey, TValue>>>.Default.Equals(
                    _source,
                    other._source
                ) && _required.Equals(other._required);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ValueDictionaryEnumerable<TKey, TValue> other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                _source,
                _required
            );
        }
    }
}
