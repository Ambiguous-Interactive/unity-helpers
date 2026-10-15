// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System.Collections.Generic;

    /// <summary>Provides value enumeration of common collections through an enumerable interface.</summary>
    /// <remarks>Arrays, exact lists, and exact hash sets avoid enumerator boxing. Other sources retain their original enumerator. The constructor treats null as empty; RequireSource retains null-source failures. Enumeration remains lazy and does not snapshot the collection.</remarks>
    public readonly struct ValueEnumerable<T> : global::System.IEquatable<ValueEnumerable<T>>
    {
        private readonly IEnumerable<T> _source;
        private readonly bool _required;

        /// <summary>Creates a view of a sequence without acquiring its enumerator.</summary>
        public ValueEnumerable(IEnumerable<T> source)
        {
            _source = source;
            _required = false;
        }

        internal ValueEnumerable(IEnumerable<T> source, bool required)
        {
            _source = source;
            _required = required;
        }

        /// <summary>Creates a lazy view that retains null-source enumeration failures.</summary>
        public static ValueEnumerable<T> RequireSource(IEnumerable<T> source)
        {
            return new ValueEnumerable<T>(source, true);
        }

        /// <summary>Acquires the source enumerator for this view.</summary>
        public ValueEnumerator<T> GetEnumerator()
        {
            return new ValueEnumerator<T>(_source, _required);
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(ValueEnumerable<T> other)
        {
            return EqualityComparer<IEnumerable<T>>.Default.Equals(_source, other._source)
                && _required.Equals(other._required);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is ValueEnumerable<T> other && Equals(other);
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
