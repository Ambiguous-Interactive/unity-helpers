// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>
    /// A wrapper around <see cref="System.Buffers.ArrayPool{T}"/> that provides a Wallstop-style
    /// auto-disposal pattern using <see cref="PooledArray{T}"/>.
    /// </summary>
    /// <typeparam name="T">The element type for the arrays.</typeparam>
    /// <remarks>
    /// <para>
    /// <strong>Key Difference from <see cref="WallstopArrayPool{T}"/>:</strong>
    /// This pool uses .NET's <see cref="System.Buffers.ArrayPool{T}.Shared"/> which returns arrays
    /// that may be <em>larger</em> than the requested size (typically rounded up to a power of 2).
    /// Callers MUST use the <see cref="PooledArray{T}.length"/> property (which returns the
    /// originally requested length) instead of accessing the underlying array's Length directly.
    /// </para>
    /// <para>
    /// <strong>When to use this pool:</strong>
    /// Use <see cref="SystemArrayPool{T}"/> when array sizes vary widely or unpredictably (e.g., user input,
    /// collection sizes, dynamically computed sizes). The shared pool handles size bucketing efficiently,
    /// reducing memory fragmentation for variable-size workloads.
    /// </para>
    /// <para>
    /// <strong>When to use <see cref="WallstopArrayPool{T}"/> instead:</strong>
    /// Use <see cref="WallstopArrayPool{T}"/> when array sizes are fixed or highly predictable (e.g., internal
    /// PRNG state buffers, algorithm-constant sizes). This avoids the overhead of size bucketing when you
    /// always request the same size.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Correct usage - use lease.Length, not lease.Array.Length
    /// using PooledArray&lt;int&gt; lease = SystemArrayPool&lt;int&gt;.Get(count, out int[] buffer);
    /// for (int i = 0; i &lt; lease.Length; i++)  // ✓ Use lease.Length
    /// {
    ///     buffer[i] = ProcessItem(i);
    /// }
    ///
    /// // WRONG - buffer.Length may be larger than requested
    /// for (int i = 0; i &lt; buffer.Length; i++)  // ✗ May iterate past valid data
    /// {
    ///     ...
    /// }
    /// </code>
    /// </example>
    public static class SystemArrayPool<T>
    {
        /// <summary>
        /// Gets a pooled array of at least the specified size. When disposed, the array is returned to the pool.
        /// </summary>
        /// <param name="minimumLength">The minimum size of the array to retrieve. Must be non-negative.</param>
        /// <returns>A <see cref="PooledArray{T}"/> wrapping an array of at least the specified size.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when minimumLength is negative.</exception>
        /// <remarks>
        /// The returned array may be larger than <paramref name="minimumLength"/>. Always use
        /// <see cref="PooledArray{T}.length"/> to determine the valid portion of the array.
        /// </remarks>
        public static PooledArray<T> Get(int minimumLength)
        {
            return Get(minimumLength, out _);
        }

        /// <summary>
        /// Gets a pooled array of at least the specified size and outputs the array. When disposed, the array is returned to the pool.
        /// </summary>
        /// <param name="minimumLength">The minimum size of the array to retrieve. Must be non-negative.</param>
        /// <param name="array">The retrieved array. May be larger than <paramref name="minimumLength"/>.</param>
        /// <returns>A <see cref="PooledArray{T}"/> wrapping the array with proper length tracking.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when minimumLength is negative.</exception>
        /// <remarks>
        /// <para>
        /// The returned array may be larger than <paramref name="minimumLength"/>. Always use
        /// <see cref="PooledArray{T}.length"/> (or the <paramref name="minimumLength"/> you passed in)
        /// to determine the valid portion of the array.
        /// </para>
        /// <para>
        /// The array is <strong>not</strong> zeroed. Every element you read must be one you wrote
        /// first; if the algorithm accumulates into slots or tests them as flags, rent through
        /// <see cref="Get(int, bool, out T[])"/> with clearArray=true instead.
        /// </para>
        /// </remarks>
        public static PooledArray<T> Get(int minimumLength, out T[] array)
        {
            if (minimumLength < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumLength),
                    minimumLength,
                    "Must be non-negative."
                );
            }

            if (minimumLength == 0)
            {
                array = Array.Empty<T>();
                return new PooledArray<T>(array, 0);
            }

            array = System.Buffers.ArrayPool<T>.Shared.Rent(minimumLength);
            return new PooledArray<T>(array, minimumLength);
        }

        /// <summary>
        /// Gets a pooled array of at least the specified size with optional clearing. When disposed, the array is returned to the pool.
        /// </summary>
        /// <param name="minimumLength">The minimum size of the array to retrieve. Must be non-negative.</param>
        /// <param name="clearArray">If true, the array is cleared to default values before being returned.</param>
        /// <param name="array">The retrieved array. May be larger than <paramref name="minimumLength"/>.</param>
        /// <returns>A <see cref="PooledArray{T}"/> wrapping the array with proper length tracking.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when minimumLength is negative.</exception>
        /// <remarks>
        /// <para>
        /// When <paramref name="clearArray"/> is true, only the portion of the array up to
        /// <paramref name="minimumLength"/> is guaranteed to be cleared. The remainder of the
        /// array (if any) may contain stale data.
        /// </para>
        /// <para>
        /// Set <paramref name="clearArray"/> to true whenever the algorithm reads a slot before it
        /// writes it -- counters, visited flags, running sums. Returning an array to the pool never
        /// leaves a managed reference rooted, but it does not zero blittable data, and
        /// <see cref="System.Buffers.ArrayPool{T}.Shared"/> is process-wide, so an array that code
        /// outside this package returned dirty can be handed straight back here.
        /// </para>
        /// </remarks>
        public static PooledArray<T> Get(int minimumLength, bool clearArray, out T[] array)
        {
            if (minimumLength < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumLength),
                    minimumLength,
                    "Must be non-negative."
                );
            }

            if (minimumLength == 0)
            {
                array = Array.Empty<T>();
                return new PooledArray<T>(array, 0);
            }

            T[] rented = System.Buffers.ArrayPool<T>.Shared.Rent(minimumLength);
            if (clearArray)
            {
                Array.Clear(rented, 0, minimumLength);
            }
            array = rented;
            return new PooledArray<T>(rented, minimumLength);
        }

        /// <summary>Processes a private rental synchronously without issuing a disposal handle.</summary>
        /// <typeparam name="TState">The explicit caller state type.</typeparam>
        /// <typeparam name="TResult">The independent result type.</typeparam>
        /// <param name="length">The logical buffer length; must be non-negative.</param>
        /// <param name="state">Caller state passed by value.</param>
        /// <param name="callback">A synchronous operation over the logical buffer.</param>
        /// <param name="result">The callback result on success; default on failure.</param>
        /// <param name="error">The original failure, or both operation and cleanup failures in an aggregate.</param>
        /// <param name="clearArray">Whether to initialize reference-free elements before invocation.</param>
        /// <returns>True when the callback and cleanup succeeded; otherwise false.</returns>
        /// <remarks>
        /// Reference-containing elements are always initialized before invocation and cleared on return.
        /// Reference-free elements require writing before reading unless clearArray is true.
        /// Zero length invokes the callback once without renting. Negative length or null callback
        /// reports failure without invoking it. After a successful rent, cleanup attempts return once;
        /// a failed return is not retried. Callback/state side effects persist on failure.
        /// Borrowed storage must not escape through unsafe or low-level APIs. Independently owned
        /// copies and objects stored in elements may escape. Fatal runtime failures are outside
        /// ordinary error recovery guarantees.
        /// </remarks>
        public static bool TryWithBuffer<TState, TResult>(
            int length,
            TState state,
            BufferFunc<T, TState, TResult> callback,
            out TResult result,
            out Exception error,
            bool clearArray = false
        )
        {
            return TryWithBufferCore(
                System.Buffers.ArrayPool<T>.Shared,
                length,
                state,
                callback,
                out result,
                out error,
                clearArray
            );
        }

        /// <summary>Processes a private rental synchronously without returning a value.</summary>
        /// <typeparam name="TState">The explicit caller state type.</typeparam>
        /// <param name="length">The logical buffer length; must be non-negative.</param>
        /// <param name="state">Caller state passed by value.</param>
        /// <param name="callback">A synchronous operation over the logical buffer.</param>
        /// <param name="error">The original failure, or both operation and cleanup failures in an aggregate.</param>
        /// <param name="clearArray">Whether to initialize reference-free elements before invocation.</param>
        /// <returns>True when the callback and cleanup succeeded; otherwise false.</returns>
        /// <remarks>
        /// Reference-containing elements are always initialized before invocation and cleared on return.
        /// Zero length invokes the callback once without renting. Callback/state side effects persist
        /// on failure, and cleanup makes one return attempt without retrying a failed return.
        /// Borrowed storage must not escape through unsafe or low-level APIs. Reference-free elements
        /// require writing before reading unless clearArray is true. Fatal runtime failures are outside
        /// ordinary error recovery guarantees.
        /// </remarks>
        public static bool TryWithBuffer<TState>(
            int length,
            TState state,
            BufferAction<T, TState> callback,
            out Exception error,
            bool clearArray = false
        )
        {
            return TryWithBufferActionCore(
                System.Buffers.ArrayPool<T>.Shared,
                length,
                state,
                callback,
                out error,
                clearArray
            );
        }

        /// <summary>Rents an array whose complete lifetime is managed by a private owner.</summary>
        internal static T[] RentForPrivateOwner(int minimumLength)
        {
            return System.Buffers.ArrayPool<T>.Shared.Rent(minimumLength);
        }

        /// <summary>Returns a privately owned array without issuing a disposal handle.</summary>
        internal static void ReturnForPrivateOwner(T[] array, bool clearArray)
        {
            System.Buffers.ArrayPool<T>.Shared.Return(array, clearArray);
        }

        internal static bool TryWithBufferActionCore<TState>(
            System.Buffers.ArrayPool<T> pool,
            int length,
            TState state,
            BufferAction<T, TState> callback,
            out Exception error,
            bool clearArray = false
        )
        {
            if (length < 0)
            {
                error = new ArgumentOutOfRangeException(nameof(length));
                return false;
            }
            if (callback == null)
            {
                error = new ArgumentNullException(nameof(callback));
                return false;
            }
            return TryWithBufferCore(
                pool,
                length,
                (state, callback),
                static (buffer, pair) =>
                {
                    pair.callback(buffer, pair.state);
                    return true;
                },
                out bool ignored,
                out error,
                clearArray
            );
        }

        internal static bool TryWithBufferCore<TState, TResult>(
            System.Buffers.ArrayPool<T> pool,
            int length,
            TState state,
            BufferFunc<T, TState, TResult> callback,
            out TResult result,
            out Exception error,
            bool clearArray = false
        )
        {
            if (length < 0)
            {
                result = default;
                error = new ArgumentOutOfRangeException(nameof(length));
                return false;
            }
            if (callback == null)
            {
                result = default;
                error = new ArgumentNullException(nameof(callback));
                return false;
            }
            TResult callbackResult = default;
            Exception operationError = null;
            T[] array = null;
            try
            {
                if (length == 0)
                {
                    callbackResult = callback(Span<T>.Empty, state);
                }
                else
                {
                    array = pool.Rent(length);
                    Span<T> logicalBuffer = array.AsSpan(0, length);
                    if (clearArray || PooledArrayClearPolicy<T>.ClearOnReturn)
                    {
                        logicalBuffer.Clear();
                    }
                    callbackResult = callback(logicalBuffer, state);
                }
            }
            catch (Exception failure)
            {
                operationError = failure;
            }
            finally
            {
                if (array != null)
                {
                    try
                    {
                        pool.Return(array, PooledArrayClearPolicy<T>.ClearOnReturn);
                    }
                    catch (Exception returnFailure)
                    {
                        operationError =
                            operationError == null
                                ? returnFailure
                                : new AggregateException(operationError, returnFailure);
                    }
                }
            }
            if (operationError != null)
            {
                result = default;
                error = operationError;
                return false;
            }
            result = callbackResult;
            error = null;
            return true;
        }
    }
}
