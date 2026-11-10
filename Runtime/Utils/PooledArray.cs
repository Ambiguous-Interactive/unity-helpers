// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A struct that wraps a pooled array and automatically returns it to the pool when disposed.
    /// This struct provides a unified return type for all array pools (<see cref="SystemArrayPool{T}"/>,
    /// <see cref="WallstopArrayPool{T}"/>, and <see cref="WallstopFastArrayPool{T}"/>).
    /// </summary>
    /// <typeparam name="T">The element type for the array.</typeparam>
    /// <remarks>
    /// <para>
    /// <strong>Important:</strong> The underlying <see cref="array"/> may be larger than the
    /// requested size (especially when using <see cref="SystemArrayPool{T}"/>). Always use
    /// <see cref="length"/> to determine the valid portion of the array.
    /// </para>
    /// <para>
    /// This struct implements <see cref="IDisposable"/> to enable automatic resource return via
    /// 'using' statements. The array is returned to the pool when <see cref="Dispose"/> is called.
    /// </para>
    /// <para>
    /// <strong>Warning:</strong> Do NOT use <c>foreach</c> on pooled arrays since
    /// <see cref="array"/>.Length may exceed <see cref="length"/>. Use indexed iteration instead.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Using with SystemArrayPool (array may be larger than requested)
    /// using PooledArray&lt;int&gt; pooled = SystemArrayPool&lt;int&gt;.Get(100, out int[] array);
    /// for (int i = 0; i &lt; pooled.Length; i++) // Use pooled.Length, not array.Length
    /// {
    ///     array[i] = i * 2;
    /// }
    ///
    /// // Using with WallstopArrayPool (array is exact size)
    /// using PooledArray&lt;int&gt; pooled2 = WallstopArrayPool&lt;int&gt;.Get(100, out int[] buffer);
    /// for (int i = 0; i &lt; pooled2.Length; i++)
    /// {
    ///     buffer[i] = i * 3;
    /// }
    /// </code>
    /// </example>
    public readonly struct PooledArray<T> : IDisposable, global::System.IEquatable<PooledArray<T>>
    {
        /// <summary>
        /// The underlying pooled array. May be larger than <see cref="length"/> when using
        /// <see cref="SystemArrayPool{T}"/> (due to power-of-2 bucketing), or exactly equal
        /// to <see cref="length"/> when using <see cref="WallstopArrayPool{T}"/> or
        /// <see cref="WallstopFastArrayPool{T}"/>.
        /// </summary>
        public readonly T[] array;

        /// <summary>
        /// The originally requested length. Use this instead of <see cref="array"/>.Length
        /// to determine the valid portion of the array.
        /// </summary>
        public readonly int length;

        private readonly Action<T[]> _onDispose;
        private readonly DisposalLease _lease;

        /// <summary>
        /// Creates a new <see cref="PooledArray{T}"/> wrapping the specified array with the given logical length.
        /// Uses the default disposal action that returns the array to <see cref="System.Buffers.ArrayPool{T}.Shared"/>.
        /// </summary>
        /// <param name="array">The pooled array.</param>
        /// <param name="length">The logical length (originally requested size).</param>
        internal PooledArray(T[] array, int length)
            : this(array, length, null) { }

        /// <summary>
        /// Creates a new <see cref="PooledArray{T}"/> wrapping the specified array with the given logical length
        /// and custom disposal action.
        /// </summary>
        /// <param name="array">The pooled array.</param>
        /// <param name="length">The logical length (originally requested size).</param>
        /// <param name="onDispose">The action to invoke when disposing. If null, uses the default
        /// <see cref="System.Buffers.ArrayPool{T}.Shared"/> return logic.</param>
        internal PooledArray(T[] array, int length, Action<T[]> onDispose)
        {
            this.array = array;
            this.length = length;
            _onDispose = onDispose;
            // Zero-length rents share Array.Empty and need no return slot.
            _lease = array == null || array.Length == 0 ? default : DisposalLeases.Acquire();
        }

        /// <summary>
        /// Returns the array to the pool. The clearing behavior depends on which pool the array came from:
        /// <list type="bullet">
        /// <item><see cref="SystemArrayPool{T}"/>: cleared when <typeparamref name="T"/> is, or
        /// contains, a managed reference; not cleared for blittable element types</item>
        /// <item><see cref="WallstopArrayPool{T}"/>: Array IS cleared on return</item>
        /// <item><see cref="WallstopFastArrayPool{T}"/>: Array is NOT cleared (for performance)</item>
        /// </list>
        /// </summary>
        /// <remarks>
        /// After disposal, the array should not be used as it may be reused by another caller. A
        /// returned array never roots the objects it held, but it is not otherwise zeroed: rent
        /// through <see cref="SystemArrayPool{T}.Get(int, bool, out T[])"/> with clearArray=true
        /// whenever a slot is read before it is written, because
        /// <see cref="System.Buffers.ArrayPool{T}.Shared"/> is process-wide and hands out arrays
        /// that code outside this package returned.
        /// </remarks>
        public void Dispose()
        {
            if (!_lease.TryClaim())
            {
                return;
            }

            if (_onDispose != null)
            {
                _onDispose(array);
            }
            else
            {
                System.Buffers.ArrayPool<T>.Shared.Return(
                    array,
                    PooledArrayClearPolicy<T>.ClearOnReturn
                );
            }
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(PooledArray<T> other)
        {
            return EqualityComparer<T[]>.Default.Equals(array, other.array)
                && length.Equals(other.length)
                && EqualityComparer<Action<T[]>>.Default.Equals(_onDispose, other._onDispose)
                && EqualityComparer<DisposalLease>.Default.Equals(_lease, other._lease);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is PooledArray<T> other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                array,
                length,
                _onDispose,
                _lease
            );
        }
    }
}
