// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>
    /// Holds a cleanup followed by an earlier disposable scope.
    /// </summary>
    /// <typeparam name="TPrevious">The earlier value-type scope.</typeparam>
    /// <remarks>
    /// Dispose the outermost scope to run cleanups in last-in-first-out order. Copies of each
    /// layer share a disposal lease, so a cleanup runs at most once. Concurrent disposal of copies
    /// preserves once-only execution, but cleanup order across threads is undefined.
    /// </remarks>
    public readonly struct DisposableScope<TPrevious> : IDisposable
        where TPrevious : struct, IDisposable
    {
        private readonly TPrevious _previous;
        private readonly Action _cleanup;
        private readonly DisposalLease _lease;

        internal DisposableScope(TPrevious previous, Action cleanup)
        {
            _previous = previous;
            _cleanup = cleanup;
            _lease = cleanup == null ? default : DisposalLeases.Acquire();
        }

        /// <summary>
        /// Adds a cleanup that runs before the earlier cleanups.
        /// </summary>
        /// <param name="cleanup">The action to run first.</param>
        /// <returns>A scope containing every cleanup.</returns>
        public DisposableScope<DisposableScope<TPrevious>> WithCleanup(Action cleanup)
        {
            return new DisposableScope<DisposableScope<TPrevious>>(this, cleanup);
        }

        /// <summary>
        /// Runs this cleanup before the earlier cleanups and never throws.
        /// </summary>
        public void Dispose()
        {
            if (_lease.TryClaim())
            {
                try
                {
                    _cleanup();
                }
                catch (Exception)
                {
                    // Continue to earlier cleanups even if this one fails.
                }
            }

            try
            {
                _previous.Dispose();
            }
            catch (Exception)
            {
                // A previous scope may be a caller-defined disposable.
            }
        }
    }
}
