// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Helper;

    public sealed partial class BitSet
    {
        /// <summary>Traverses live set indices in ascending order without allocating an iterator object.</summary>
        /// <remarks>Observes changes ahead of its cursor. Changes to already visited indices are ignored.</remarks>
        public SetIndexEnumerator SetIndices => new(this);

        /// <summary>Traverses live set indices and tolerates resizing between moves.</summary>
        public struct SetIndexEnumerator : IEnumerator<int>, IEquatable<SetIndexEnumerator>
        {
            /// <summary>Gets the current index, or -1 when no index is selected.</summary>
            public int Current { get; private set; }

            object IEnumerator.Current => Current;

            private readonly BitSet _owner;
            private int _cursor;
            private bool _finished;

            internal SetIndexEnumerator(BitSet owner)
            {
                _owner = owner;
                _cursor = 0;
                _finished = false;
                Current = -1;
            }

            /// <summary>Returns an independent copy of the traversal state.</summary>
            public SetIndexEnumerator GetEnumerator()
            {
                return this;
            }

            /// <summary>Advances to the next live set index.</summary>
            public bool MoveNext()
            {
                if (_owner != null && !_finished)
                {
                    while (_cursor < _owner._capacity)
                    {
                        int word = _cursor >> BitsPerLongShift;
                        ulong remaining =
                            _owner._bits[word] & (ulong.MaxValue << (_cursor & BitsPerLongMask));
                        if (remaining == 0)
                        {
                            long nextWord = ((long)word + 1) << BitsPerLongShift;
                            if (_owner._capacity <= nextWord)
                            {
                                break;
                            }
                            _cursor = (int)nextWord;
                            continue;
                        }
                        int index =
                            (word << BitsPerLongShift) + BitOps.TrailingZeroCount(remaining);
                        if (_owner._capacity <= index)
                        {
                            break;
                        }
                        _cursor = index + 1;
                        Current = index;
                        return true;
                    }
                }
                _finished = true;
                Current = -1;
                return false;
            }

            /// <summary>Restarts traversal against the owner's current contents.</summary>
            public void Reset()
            {
                _cursor = 0;
                _finished = false;
                Current = -1;
            }

            /// <summary>Completes traversal without owning the bit storage.</summary>
            public void Dispose() { }

            /// <summary>Compares traversal states.</summary>
            public bool Equals(SetIndexEnumerator other)
            {
                return ReferenceEquals(_owner, other._owner)
                    && _cursor == other._cursor
                    && _finished == other._finished
                    && Current == other.Current;
            }

            /// <summary>Compares traversal states.</summary>
            public override bool Equals(object obj)
            {
                return obj is SetIndexEnumerator other && Equals(other);
            }

            /// <summary>Hashes the traversal state.</summary>
            public override int GetHashCode()
            {
                return HashCode.Combine(_owner, _cursor, _finished, Current);
            }
        }
    }
}
