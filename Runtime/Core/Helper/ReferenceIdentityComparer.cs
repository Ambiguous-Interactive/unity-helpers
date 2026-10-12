// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>Compares managed references without invoking value or Unity object equality.</summary>
    public sealed class ReferenceIdentityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        /// <summary>Gets the shared comparer for this reference type.</summary>
        public static ReferenceIdentityComparer<T> Instance { get; } = new();

        private ReferenceIdentityComparer() { }

        /// <summary>Returns whether both arguments are the same managed reference, including null.</summary>
        public bool Equals(T left, T right)
        {
            return ReferenceEquals(left, right);
        }

        /// <summary>Returns a stable identity hash, or zero for a managed null reference.</summary>
        public int GetHashCode(T value)
        {
            return ReferenceEquals(value, null) ? 0 : RuntimeHelpers.GetHashCode(value);
        }
    }
}
