// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System.Collections.Generic;

    /// <summary>Provides value enumeration of common collections through an enumerable interface.</summary>
    /// <remarks>Arrays, exact lists, and exact hash sets avoid enumerator boxing. Other sources retain their original enumerator. Null sources are empty. Enumeration remains lazy and does not snapshot the collection.</remarks>
    public readonly struct ValueEnumerable<T>
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

        /// <summary>Acquires the source enumerator for this view.</summary>
        public ValueEnumerator<T> GetEnumerator()
        {
            return new ValueEnumerator<T>(_source, _required);
        }
    }
}
