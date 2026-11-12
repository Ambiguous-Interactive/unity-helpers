// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>Counts independent holds and reports transitions between held and released.</summary>
    /// <remarks>
    /// Use on the Unity main thread. Each acquisition is independent, including repeated owners.
    /// Call SweepDestroyedOwners to release destroyed Unity owners; ordinary owners require disposal.
    /// Callbacks observe the committed state and may reenter. Callback failures are logged.
    /// </remarks>
    public sealed class HoldSet : IDisposable
    {
        /// <summary>Gets the number of outstanding acquisitions, including owners not yet swept.</summary>
        public int Count => _owners.Count;

        /// <summary>Gets whether at least one acquisition is outstanding.</summary>
        public bool IsHeld => _owners.Count != 0;

        /// <summary>Gets whether this set has permanently stopped accepting holds.</summary>
        public bool IsDisposed => _disposed;

        private readonly Dictionary<long, object> _owners = new();
        private readonly List<long> _destroyed = new();
        private readonly Action _onFirstAcquired;
        private readonly Action _onLastReleased;
        private long _nextIdentity;
        private bool _disposed;

        /// <summary>Creates a set with optional callbacks for its two occupancy transitions.</summary>
        public HoldSet(Action onFirstAcquired = null, Action onLastReleased = null)
        {
            _onFirstAcquired = onFirstAcquired;
            _onLastReleased = onLastReleased;
        }

        private static bool InvokeCallback(Action callback)
        {
            if (callback == null)
            {
                return true;
            }
            try
            {
                callback();
                return true;
            }
            catch (Exception exception)
            {
                try
                {
                    Debug.LogException(exception);
                }
                catch (Exception)
                {
                    // Cleanup must not depend on a custom log handler succeeding.
                }
                return false;
            }
        }

        /// <summary>Acquires an independent hold, or returns an inactive handle for an unavailable owner or set.</summary>
        /// <remarks>
        /// Null and destroyed Unity owners are refused. Copies of a handle release the same hold once.
        /// If the first-acquired callback fails, this acquisition is released and an inactive handle is returned.
        /// </remarks>
        public Handle Acquire(object owner)
        {
            if (
                _disposed
                || owner == null
                || (owner is UnityEngine.Object unityOwner && unityOwner == null)
                || _nextIdentity == long.MaxValue
            )
            {
                return default;
            }

            long identity = ++_nextIdentity;
            bool wasEmpty = _owners.Count == 0;
            _owners.Add(identity, owner);
            Handle handle = new(this, identity);
            if (wasEmpty && !InvokeCallback(_onFirstAcquired))
            {
                Release(identity);
                return default;
            }
            return handle;
        }

        /// <summary>Releases outstanding holds whose Unity owners have been destroyed and returns their count.</summary>
        public int SweepDestroyedOwners()
        {
            _destroyed.Clear();
            foreach (KeyValuePair<long, object> entry in _owners)
            {
                if (entry.Value is UnityEngine.Object owner && owner == null)
                {
                    _destroyed.Add(entry.Key);
                }
            }
            int count = _destroyed.Count;
            foreach (long identity in _destroyed)
            {
                _owners.Remove(identity);
            }
            _destroyed.Clear();
            if (0 < count && _owners.Count == 0)
            {
                InvokeCallback(_onLastReleased);
            }
            return count;
        }

        /// <summary>Releases all current holds while allowing later acquisitions.</summary>
        public void Clear()
        {
            bool wasHeld = _owners.Count != 0;
            _owners.Clear();
            if (wasHeld)
            {
                InvokeCallback(_onLastReleased);
            }
        }

        /// <summary>Permanently refuses new holds and releases all outstanding acquisitions once.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            Clear();
        }

        private void Release(long identity)
        {
            if (_owners.Remove(identity) && _owners.Count == 0)
            {
                InvokeCallback(_onLastReleased);
            }
        }

        /// <summary>A copyable handle whose disposal is safe to repeat.</summary>
        public readonly struct Handle : IDisposable, IEquatable<Handle>
        {
            /// <summary>Gets whether this acquisition is still outstanding.</summary>
            public bool IsHeld => _set != null && _set._owners.ContainsKey(_identity);

            private readonly HoldSet _set;
            private readonly long _identity;

            internal Handle(HoldSet set, long identity)
            {
                _set = set;
                _identity = identity;
            }

            /// <summary>Releases this acquisition once, including across copies of this handle.</summary>
            public void Dispose()
            {
                _set?.Release(_identity);
            }

            /// <inheritdoc />
            public bool Equals(Handle other)
            {
                return ReferenceEquals(_set, other._set) && _identity == other._identity;
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is Handle other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return HashCode.Combine(_set, _identity);
            }
        }
    }
}
