// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Runs an action once when a using scope ends.
    /// </summary>
    /// <remarks>
    /// Copies share one disposal lease. A cached, noncapturing action and a warmed lease slot make
    /// creation and disposal allocation-free. A default scope or null action does nothing.
    /// </remarks>
    public readonly struct DisposableScope : IDisposable, global::System.IEquatable<DisposableScope>
    {
        /// <summary>
        /// True while this scope still owns its cleanup.
        /// </summary>
        public bool IsHeld => _lease.IsHeld;

        private readonly Action _cleanup;
        private readonly DisposalLease _lease;

        private DisposableScope(Action cleanup)
        {
            _cleanup = cleanup;
            _lease = cleanup == null ? default : DisposalLeases.Acquire();
        }

        /// <summary>
        /// Creates a scope that runs the given cleanup on disposal.
        /// </summary>
        /// <param name="cleanup">The action to run once.</param>
        /// <returns>A scope, or a harmless default scope when cleanup is null.</returns>
        public static DisposableScope Create(Action cleanup)
        {
            return new DisposableScope(cleanup);
        }

        /// <summary>
        /// Adds a cleanup that runs before this scope's cleanup.
        /// </summary>
        /// <param name="cleanup">The action to run first.</param>
        /// <returns>A scope containing both cleanups.</returns>
        public DisposableScope<DisposableScope> WithCleanup(Action cleanup)
        {
            return new DisposableScope<DisposableScope>(this, cleanup);
        }

        /// <summary>
        /// Runs the cleanup at most once and never throws.
        /// </summary>
        public void Dispose()
        {
            if (!_lease.TryClaim())
            {
                return;
            }

            try
            {
                _cleanup();
            }
            catch (Exception)
            {
                // A cleanup failure must not replace an exception already in flight.
            }
        }

        /// <summary>Compares the stored fields with another value.</summary>
        public bool Equals(DisposableScope other)
        {
            return EqualityComparer<Action>.Default.Equals(_cleanup, other._cleanup)
                && EqualityComparer<DisposalLease>.Default.Equals(_lease, other._lease);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is DisposableScope other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return global::WallstopStudios.UnityHelpers.Core.Helper.Objects.HashCode(
                _cleanup,
                _lease
            );
        }
    }
}
