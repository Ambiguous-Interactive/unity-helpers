// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Threading;

    /// <summary>Reuses one fixed array length in a separate stack for each calling thread.</summary>
    /// <remarks>
    /// Return each rental once and stop using it afterward. Arrays are cleared before reuse. Plain
    /// returns and lease disposal transfer storage to the returning thread. Dispose this owner only
    /// after all users have stopped. Use SystemArrayPool for varying lengths.
    /// </remarks>
    public sealed class ThreadLocalArrayPool<T> : IDisposable
    {
        /// <summary>Gets the exact length of every rental.</summary>
        public int Length { get; }

        /// <summary>Gets whether this owner has been disposed.</summary>
        public bool IsDisposed => _disposed;

        private readonly ThreadLocal<LocalPool<T[]>> _pools;
        private readonly int _maxRetainedPerThread;
        private readonly Action<T[]> _returnAction;
        private bool _disposed;

        /// <summary>Creates a fixed-length pool with bounded idle storage per thread.</summary>
        /// <param name="length">Exact array length; nonpositive values use the shared empty array.</param>
        /// <param name="maxRetainedPerThread">Maximum idle arrays per thread; nonpositive values disable retention.</param>
        public ThreadLocalArrayPool(int length, int maxRetainedPerThread = 64)
        {
            Length = Math.Max(0, length);
            _maxRetainedPerThread = Math.Max(0, maxRetainedPerThread);
            _pools = new ThreadLocal<LocalPool<T[]>>(CreatePool);
            _returnAction = Return;
        }

        private static void ClearArray(T[] array)
        {
            Array.Clear(array, 0, array.Length);
        }

        /// <summary>Rents an exact-length array, or null after disposal.</summary>
        public T[] Rent()
        {
            if (_disposed)
            {
                return null;
            }
            return Length == 0 ? Array.Empty<T>() : _pools.Value.Rent();
        }

        /// <summary>Rents an array with the shared, copy-safe disposal lease.</summary>
        /// <remarks>The lease adds disposal tracking costs. Do not also return its array manually.</remarks>
        public PooledResource<T[]> Get(out T[] array)
        {
            T[] rented = Rent();
            PooledResource<T[]> lease =
                rented == null
                    ? default
                    : new PooledResource<T[]>(rented, Length == 0 ? null : _returnAction);
            array = rented;
            return lease;
        }

        /// <summary>Clears an array and returns it to the calling thread's stack.</summary>
        /// <remarks>Null, wrong-length and post-disposal returns are ignored. Duplicate and foreign returns are caller errors.</remarks>
        public void Return(T[] array)
        {
            if (_disposed || array == null || array.Length != Length || Length == 0)
            {
                return;
            }
            _pools.Value.Return(array);
        }

        /// <summary>Drops the calling thread's idle arrays without creating a stack.</summary>
        public void ClearCurrentThread()
        {
            if (!_disposed && _pools.IsValueCreated)
            {
                _pools.Value.Clear();
            }
        }

        /// <summary>Releases this owner's per-thread storage after its users have stopped.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _pools.Dispose();
        }

        private LocalPool<T[]> CreatePool()
        {
            return new LocalPool<T[]>(CreateArray, ClearArray, _maxRetainedPerThread);
        }

        private T[] CreateArray()
        {
            return new T[Length];
        }
    }
}
