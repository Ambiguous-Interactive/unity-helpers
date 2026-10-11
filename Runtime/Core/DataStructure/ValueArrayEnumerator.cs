// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;

    internal struct ValueArrayEnumerator : IDisposable
    {
        /// <summary>Gets the current element after a successful advance.</summary>
        public object Current
        {
            get
            {
                if (_fallback != null)
                {
                    return _fallback.Current;
                }
                if (_vector != null)
                {
                    return _vector[_index];
                }
                if (_rank == 1)
                {
                    return _source.GetValue(_lower0 + _index);
                }
                if (_rank == 2)
                {
                    return _source.GetValue(
                        _lower0 + _index / _length1,
                        _lower1 + _index % _length1
                    );
                }
                return _source.GetValue(
                    _lower0 + _index / _length2 / _length1,
                    _lower1 + _index / _length2 % _length1,
                    _lower2 + _index % _length2
                );
            }
        }

        private readonly Array _source;
        private readonly object[] _vector;
        private readonly IEnumerator _fallback;
        private readonly int _rank;
        private readonly int _length;
        private readonly int _length1;
        private readonly int _length2;
        private readonly int _lower0;
        private readonly int _lower1;
        private readonly int _lower2;
        private int _index;

        /// <summary>Creates a row-major enumerator that preserves array lower bounds.</summary>
        public ValueArrayEnumerator(Array source)
        {
            _source = source;
            _vector = source as object[];
            _fallback = null;
            _rank = source == null ? 0 : source.Rank;
            _length = source == null ? 0 : source.Length;
            _length1 = 1;
            _length2 = 1;
            _lower0 = 0;
            _lower1 = 0;
            _lower2 = 0;
            _index = -1;
            if (_rank > 3)
            {
                _fallback = source.GetEnumerator();
            }
            else if (_rank > 0)
            {
                _lower0 = source.GetLowerBound(0);
                if (_rank > 1)
                {
                    _lower1 = source.GetLowerBound(1);
                    _length1 = source.GetLength(1);
                }
                if (_rank > 2)
                {
                    _lower2 = source.GetLowerBound(2);
                    _length2 = source.GetLength(2);
                }
            }
        }

        /// <summary>Advances to the next element.</summary>
        public bool MoveNext()
        {
            if (_fallback != null)
            {
                return _fallback.MoveNext();
            }
            if (_index < _length)
            {
                ++_index;
            }
            return _index < _length;
        }

        /// <summary>Disposes a fallback enumerator when necessary.</summary>
        public void Dispose()
        {
            if (_fallback is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
