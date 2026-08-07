// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Threading
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A held <see cref="SemaphoreSlim"/> permit that is returned when the lease is disposed, so a
    /// critical section reads as a <c>using</c> block instead of a <c>try</c>/<c>finally</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A struct, so an uncontended acquire allocates nothing: <c>using</c> over a struct calls
    /// <see cref="Dispose"/> directly rather than through a boxed interface.
    /// </para>
    /// <para>
    /// <b>Disposal is tracked and idempotent.</b> The lease drops its reference to the semaphore as
    /// it releases, so disposing twice returns one permit, not two. That matters more than it
    /// sounds: an extra <see cref="SemaphoreSlim.Release()"/> raises the permit count above what the
    /// semaphore was constructed with and quietly lets two callers into a section built for one.
    /// <see cref="IsHeld"/> reports whether this lease still owns a permit.
    /// </para>
    /// <para>
    /// <b>Do not copy a lease.</b> Assigning one to another variable, capturing it, or passing it by
    /// value produces a second lease pointing at the same permit. Disposing both is harmless — the
    /// second release is swallowed rather than allowed to corrupt the count — but the section is
    /// unguarded from the first disposal onward. Acquire it directly into a <c>using</c> and let it
    /// die there.
    /// </para>
    /// <para>
    /// <b><see cref="Dispose"/> never throws</b>, including when the semaphore itself has already
    /// been disposed. Disposal runs from a <c>finally</c>, so a throw there would replace the
    /// exception the caller was already unwinding with.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
    ///
    /// public void Append(string line)
    /// {
    ///     using (_gate.Acquire())
    ///     {
    ///         _lines.Add(line);
    ///     }
    /// }
    ///
    /// public async Task AppendAsync(string line, CancellationToken cancellationToken)
    /// {
    ///     using (await _gate.AcquireAsync(cancellationToken))
    ///     {
    ///         _lines.Add(line);
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    public struct SemaphoreLease : IDisposable
    {
        private SemaphoreSlim _semaphore;

        internal SemaphoreLease(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        /// <summary>
        /// True while this lease still owns a permit. False for a default lease, for a failed
        /// <see cref="SemaphoreSlimExtensions.TryAcquire(SemaphoreSlim, TimeSpan, out SemaphoreLease)"/>,
        /// and after <see cref="Dispose"/>.
        /// </summary>
        public bool IsHeld => _semaphore != null;

        /// <summary>
        /// Returns the permit. Safe to call more than once; only the first call releases. Never
        /// throws, including when the semaphore has already been disposed.
        /// </summary>
        public void Dispose()
        {
            SemaphoreSlim semaphore = _semaphore;
            if (semaphore == null)
            {
                return;
            }

            _semaphore = null;
            try
            {
                semaphore.Release();
            }
            catch
            {
                // Dispose runs from a `finally`, so throwing here would REPLACE whatever exception
                // the using block was already unwinding with -- the caller loses the failure they
                // care about and gets a confusing one about a semaphore instead. Neither reachable
                // exception is actionable: ObjectDisposedException means the semaphore is already
                // gone, so the permit is moot, and SemaphoreFullException means the count is
                // already back at its maximum. Both leave the permit accounted for.
            }
        }
    }

    /// <summary>
    /// Turns <see cref="SemaphoreSlim"/> waits into <see cref="SemaphoreLease"/> values so callers
    /// write <c>using</c> instead of pairing every wait with a <c>finally</c>.
    /// </summary>
    public static class SemaphoreSlimExtensions
    {
        /// <summary>
        /// Waits for a permit and returns a lease that releases it on disposal.
        /// </summary>
        /// <param name="semaphore">The semaphore to take a permit from.</param>
        /// <returns>A held lease.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="semaphore"/> is null. This is the one place the package prefers throwing
        /// to returning: a lease that silently is not held would let a second caller into a section
        /// built for one, and that corruption surfaces far from its cause. Use
        /// <see cref="TryAcquire(SemaphoreSlim, TimeSpan, out SemaphoreLease)"/> where failure is a
        /// state the caller wants to handle.
        /// </exception>
        public static SemaphoreLease Acquire(this SemaphoreSlim semaphore)
        {
            if (semaphore == null)
            {
                throw new ArgumentNullException(nameof(semaphore));
            }

            semaphore.Wait();
            return new SemaphoreLease(semaphore);
        }

        /// <summary>
        /// Waits up to <paramref name="timeout"/> for a permit, reporting failure rather than
        /// throwing.
        /// </summary>
        /// <param name="semaphore">The semaphore to take a permit from.</param>
        /// <param name="timeout">How long to wait. <see cref="TimeSpan.Zero"/> polls.</param>
        /// <param name="lease">A held lease when this returns true; a lease that is not held otherwise.</param>
        /// <returns>True when a permit was taken.</returns>
        public static bool TryAcquire(
            this SemaphoreSlim semaphore,
            TimeSpan timeout,
            out SemaphoreLease lease
        )
        {
            lease = default;
            if (semaphore == null)
            {
                return false;
            }

            try
            {
                if (!semaphore.Wait(timeout))
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            lease = new SemaphoreLease(semaphore);
            return true;
        }

        /// <summary>
        /// Takes a permit only if one is free right now.
        /// </summary>
        /// <param name="semaphore">The semaphore to take a permit from.</param>
        /// <param name="lease">A held lease when this returns true; a lease that is not held otherwise.</param>
        /// <returns>True when a permit was taken.</returns>
        public static bool TryAcquire(this SemaphoreSlim semaphore, out SemaphoreLease lease)
        {
            return TryAcquire(semaphore, TimeSpan.Zero, out lease);
        }

        /// <summary>
        /// Asynchronously waits for a permit and returns a lease that releases it on disposal.
        /// </summary>
        /// <param name="semaphore">The semaphore to take a permit from.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>A held lease.</returns>
        /// <remarks>
        /// A cancelled wait throws before the lease exists, so <c>using (await
        /// semaphore.AcquireAsync(token))</c> never enters the block and never releases a permit it
        /// did not take — the <c>using</c> operand is evaluated before the scope is established.
        /// Callers that must report cancellation rather than propagate it should await outside the
        /// <c>using</c> and convert there.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="semaphore"/> is null.</exception>
        /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired.</exception>
        public static async ValueTask<SemaphoreLease> AcquireAsync(
            this SemaphoreSlim semaphore,
            CancellationToken cancellationToken = default
        )
        {
            if (semaphore == null)
            {
                throw new ArgumentNullException(nameof(semaphore));
            }

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new SemaphoreLease(semaphore);
        }
    }
}
