// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;

    /// <summary>Traverses the set bits of a 64-bit mask without boxing.</summary>
    public struct SetBitEnumerator : IEquatable<SetBitEnumerator>
    {
        /// <summary>Gets the current bit index, or -1 after traversal ends.</summary>
        public int Current { get; private set; }

        private ulong _remaining;

        internal SetBitEnumerator(ulong mask)
        {
            _remaining = mask;
            Current = -1;
        }

        /// <summary>Returns an independent copy of this traversal.</summary>
        public SetBitEnumerator GetEnumerator()
        {
            return this;
        }

        /// <summary>Advances to the next set bit.</summary>
        public bool MoveNext()
        {
            if (_remaining == 0)
            {
                Current = -1;
                return false;
            }
            Current = BitOps.TrailingZeroCount(_remaining);
            _remaining &= _remaining - 1;
            return true;
        }

        /// <summary>Compares traversal states.</summary>
        public bool Equals(SetBitEnumerator other)
        {
            return _remaining == other._remaining && Current == other.Current;
        }

        /// <summary>Compares traversal states.</summary>
        public override bool Equals(object obj)
        {
            return obj is SetBitEnumerator other && Equals(other);
        }

        /// <summary>Hashes the traversal state.</summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(_remaining, Current);
        }
    }
}
