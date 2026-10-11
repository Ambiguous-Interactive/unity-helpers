// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>Enumerates arrays, exact lists, and exact hash sets without boxing their enumerators.</summary>
    /// <remarks>Other sources retain their enumeration and disposal behavior. Current is meaningful only after a successful MoveNext. Copies of a fallback enumerator share its lifetime.</remarks>
    public struct ValueEnumerator<T> : IDisposable
    {
        private const int ArrayKind = 0;
        private const int ListKind = 1;
        private const int HashSetKind = 2;
        private const int FallbackKind = 3;

        /// <summary>Gets the current element.</summary>
        public T Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                switch (_kind)
                {
                    case ListKind:
                        return _list.Current;
                    case HashSetKind:
                        return _hashSet.Current;
                    case FallbackKind:
                        return _fallback.Current;
                    default:
                        return _array != null && 0 <= _index && _index < _array.Length
                            ? _array[_index]
                            : default;
                }
            }
        }

        private readonly int _kind;
        private readonly T[] _array;
        private List<T>.Enumerator _list;
        private HashSet<T>.Enumerator _hashSet;
        private IEnumerator<T> _fallback;
        private int _index;

        internal ValueEnumerator(IEnumerable<T> source, bool required)
        {
            _array = null;
            _list = default;
            _hashSet = default;
            _fallback = null;
            _index = -1;
            if (source == null)
            {
                _kind = required ? FallbackKind : ArrayKind;
                if (required)
                {
                    _fallback = source.GetEnumerator();
                }
            }
            else if (source is T[] array)
            {
                _kind = ArrayKind;
                _array = array;
            }
            else if (source.GetType() == typeof(List<T>))
            {
                _kind = ListKind;
                _list = ((List<T>)source).GetEnumerator();
            }
            else if (source.GetType() == typeof(HashSet<T>))
            {
                _kind = HashSetKind;
                _hashSet = ((HashSet<T>)source).GetEnumerator();
            }
            else
            {
                _kind = FallbackKind;
                _fallback = source.GetEnumerator();
            }
        }

        /// <summary>Advances to the next element using the source's mutation policy.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            switch (_kind)
            {
                case ListKind:
                    return _list.MoveNext();
                case HashSetKind:
                    return _hashSet.MoveNext();
                case FallbackKind:
                    return _fallback.MoveNext();
                default:
                    if (_array != null && _index < _array.Length)
                    {
                        ++_index;
                        return _index < _array.Length;
                    }
                    return false;
            }
        }

        /// <summary>Releases the source enumerator when it requires cleanup.</summary>
        public void Dispose()
        {
            switch (_kind)
            {
                case ListKind:
                    _list.Dispose();
                    break;
                case HashSetKind:
                    _hashSet.Dispose();
                    break;
                case FallbackKind:
                    IEnumerator<T> fallback = _fallback;
                    if (fallback != null)
                    {
                        fallback.Dispose();
                    }
                    break;
            }
        }
    }
}
