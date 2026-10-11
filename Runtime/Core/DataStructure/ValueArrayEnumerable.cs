// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;

    internal readonly struct ValueArrayEnumerable
    {
        private readonly Array _source;

        /// <summary>Creates an array view without acquiring an enumerator.</summary>
        public ValueArrayEnumerable(Array source)
        {
            _source = source;
        }

        /// <summary>Acquires a value enumerator for ranks up to three.</summary>
        public ValueArrayEnumerator GetEnumerator()
        {
            return new ValueArrayEnumerator(_source);
        }
    }
}
