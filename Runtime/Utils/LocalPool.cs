// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEngine;

    /// <summary>Reuses managed objects under one caller's exclusive ownership.</summary>
    /// <remarks>
    /// Use from one thread at a time. Return each rental exactly once, to this pool, and stop using it
    /// after return. Ownership is not tracked. A producer must return a fresh, independently owned
    /// object. Use TrackedObjectPool for Unity object lifecycle and checked returns.
    /// </remarks>
    public sealed class LocalPool<T>
        where T : class
    {
        /// <summary>Gets the number of objects available for reuse.</summary>
        public int CountInactive => _items.Count;

        /// <summary>Gets the maximum number of idle objects retained by this pool.</summary>
        public int MaxRetained => _maxRetained;

        private readonly Stack<T> _items = new();
        private readonly Func<T> _producer;
        private readonly Action<T> _onReturn;
        private readonly Action<T> _returnAction;
        private readonly int _maxRetained;
        private bool _invokingCallback;

        /// <summary>Creates a pool with optional return cleanup and bounded idle storage.</summary>
        /// <param name="producer">Creates an object when no idle object is available; null disables production.</param>
        /// <param name="onReturn">Resets an object before reuse, including objects discarded when full.</param>
        /// <param name="maxRetained">Maximum idle objects; nonpositive values disable retention.</param>
        public LocalPool(Func<T> producer, Action<T> onReturn = null, int maxRetained = 64)
        {
            _producer = producer;
            _onReturn = onReturn;
            _maxRetained = Math.Max(0, maxRetained);
            _returnAction = Return;
        }

        /// <summary>Rents an idle or newly produced object, or null when production fails.</summary>
        /// <remarks>Recursive callback operations are refused. Callback exceptions are logged and the affected object is discarded.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Rent()
        {
            if (_invokingCallback)
            {
                return null;
            }
            if (_items.TryPop(out T item))
            {
                return item;
            }
            return Produce();
        }

        /// <summary>Rents an object with the shared, copy-safe disposal lease.</summary>
        /// <remarks>The lease adds disposal tracking costs. Do not also return its object manually.</remarks>
        public PooledResource<T> Get(out T item)
        {
            T rented = Rent();
            PooledResource<T> lease =
                rented == null ? default : new PooledResource<T>(rented, _returnAction);
            item = rented;
            return lease;
        }

        /// <summary>Resets and returns an object, discarding it when idle storage is full.</summary>
        /// <remarks>Null and recursive callback returns are ignored; foreign and duplicate returns are caller errors.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return(T item)
        {
            if (item == null || _invokingCallback)
            {
                return;
            }
            if (_onReturn != null && !Reset(item))
            {
                return;
            }
            if (_items.Count < _maxRetained)
            {
                _items.Push(item);
            }
        }

        /// <summary>Drops idle references without affecting outstanding rentals.</summary>
        public void Clear()
        {
            if (!_invokingCallback)
            {
                _items.Clear();
            }
        }

        private T Produce()
        {
            if (_producer == null)
            {
                return null;
            }
            _invokingCallback = true;
            try
            {
                return _producer();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return null;
            }
            finally
            {
                _invokingCallback = false;
            }
        }

        private bool Reset(T item)
        {
            _invokingCallback = true;
            try
            {
                _onReturn(item);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return false;
            }
            finally
            {
                _invokingCallback = false;
            }
        }
    }
}
