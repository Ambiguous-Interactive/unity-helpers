// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Decides whether an array of <typeparamref name="T"/> must be cleared before it goes back to
    /// <see cref="System.Buffers.ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <typeparam name="T">The element type for the arrays.</typeparam>
    /// <remarks>
    /// Clearing exists to stop a returned array from rooting the objects it still points at, which
    /// only a reference-bearing element can do. A blittable element roots nothing, so clearing it is
    /// pure cost -- and the cost is the whole rented array (the shared pool rounds a rent up to a
    /// power of two), not just the portion the caller asked for.
    /// </remarks>
    internal static class PooledArrayClearPolicy<T>
    {
        /// <summary>
        /// True when an element of <typeparamref name="T"/> is, or transitively contains, a managed
        /// reference. A struct holding a <see cref="string"/> answers true, which
        /// <c>!typeof(T).IsValueType</c> would miss.
        /// </summary>
        internal static readonly bool ClearOnReturn =
            RuntimeHelpers.IsReferenceOrContainsReferences<T>();
    }
}
