// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Threading
{
    using System;
    using System.Threading;

    /// <summary>Holds one scoped value per thread and closed generic type.</summary>
    /// <typeparam name="T">The ambient context value type.</typeparam>
    /// <remarks>
    /// Scopes restore in reverse acquisition order on their originating thread. Early or cross-thread
    /// disposal is ignored and may be retried. Values do not flow across async continuations.
    /// Push and disposal use no locks or per-scope managed allocations. Each thread receives a unique identity
    /// through atomic operations on its first push. All callers using the same T share that thread's context.
    /// </remarks>
    public static class ThreadAmbient<T>
    {
        /// <summary>Gets this thread's current value, or default outside a scope.</summary>
        public static T Current => _current;

        private static long _lastThreadIdentity;

        [ThreadStatic]
        private static long _threadIdentity;

        [ThreadStatic]
        private static long _lastScopeIdentity;

        [ThreadStatic]
        private static long _scopeIdentity;

        [ThreadStatic]
        private static T _current;

        /// <summary>Pushes a value and returns a scope that restores the previous value once.</summary>
        /// <remarks>Returns an inactive scope if the finite identity space has been exhausted.</remarks>
        public static Scope Push(T value)
        {
            if (_threadIdentity == 0)
            {
                _threadIdentity = CreateThreadIdentity();
            }
            if (_threadIdentity == 0 || _lastScopeIdentity == long.MaxValue)
            {
                return default;
            }
            long identity = ++_lastScopeIdentity;
            Scope scope = new(_threadIdentity, identity, _scopeIdentity, _current);
            _scopeIdentity = identity;
            _current = value;
            return scope;
        }

        private static long CreateThreadIdentity()
        {
            while (true)
            {
                long previous = Interlocked.Read(ref _lastThreadIdentity);
                if (previous == long.MaxValue)
                {
                    return 0;
                }
                long next = previous + 1;
                if (
                    Interlocked.CompareExchange(ref _lastThreadIdentity, next, previous) == previous
                )
                {
                    return next;
                }
            }
        }

        private static void Release(long thread, long identity, long parent, T previous)
        {
            if (identity == 0 || _threadIdentity != thread || _scopeIdentity != identity)
            {
                return;
            }
            _current = previous;
            _scopeIdentity = parent;
        }

        /// <summary>A copyable scope whose valid disposal restores its thread's previous value once.</summary>
        public readonly struct Scope : IDisposable, IEquatable<Scope>
        {
            /// <summary>Gets whether this scope is the current top scope on this thread.</summary>
            public bool IsCurrent =>
                _identity != 0
                && ThreadAmbient<T>._threadIdentity == _thread
                && ThreadAmbient<T>._scopeIdentity == _identity;

            private readonly long _thread;
            private readonly long _identity;
            private readonly long _parent;
            private readonly T _previous;

            internal Scope(long thread, long identity, long parent, T previous)
            {
                _thread = thread;
                _identity = identity;
                _parent = parent;
                _previous = previous;
            }

            /// <summary>Restores the saved value when this is the top scope on its originating thread.</summary>
            public void Dispose()
            {
                Release(_thread, _identity, _parent, _previous);
            }

            /// <inheritdoc />
            public bool Equals(Scope other)
            {
                return _thread == other._thread && _identity == other._identity;
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return obj is Scope other && Equals(other);
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return HashCode.Combine(_thread, _identity);
            }
        }
    }
}
