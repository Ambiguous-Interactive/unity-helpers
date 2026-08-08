// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;

    /// <summary>
    /// A pooled instance, how it is released, and the counter that decides which disposal actually
    /// releases it.
    /// </summary>
    /// <typeparam name="T">The pooled type.</typeparam>
    /// <remarks>
    /// <para>
    /// Pooled leases (<see cref="PooledResource{T}"/>, <see cref="PooledArray{T}"/>) are structs, so
    /// a <b>copy</b> — passing one to a method by value, assigning it, capturing it — carries its own
    /// copy of any "already disposed" field and cannot see that the original already returned.
    /// Disposing both returns the instance twice, and a pool holding one instance twice hands it to
    /// two live callers: from then on every write through one is visible through the other, and the
    /// release callback of the second return wipes whatever the current renter had put there.
    /// </para>
    /// <para>
    /// The counter is what makes that detectable. It advances on every rent, each lease remembers the
    /// value it was rented at, and <see cref="TryRelease"/> only succeeds for a disposal still
    /// holding the current one. That catches the copy disposed immediately <b>and</b> the copy
    /// disposed after the instance has already been rented out again — the dangerous case, which a
    /// "is it already in the free list" check cannot see, because at that moment it is not.
    /// </para>
    /// <para>
    /// One of these exists per pooled instance, not per rent, and it travels with its instance from
    /// the pool to the renter and back. An instance whose lease is abandoned loses both together, so
    /// nothing accumulates. Tracking rented instances in a set instead would turn a forgotten
    /// <c>using</c> from "the garbage collector takes it" into a permanent leak, which is worse than
    /// the defect being fixed.
    /// </para>
    /// </remarks>
    internal sealed class PoolLease<T>
    {
        /// <summary>
        /// The pooled instance this lease belongs to, for the life of that instance.
        /// </summary>
        internal readonly T value;

        // The pool needs its own lease back to park it in the free list, which a plain Action<T>
        // cannot carry. Both live here rather than in the lease struct so the struct stays three
        // fields wide -- it is copied on every rent and every `using`.
        private readonly Action<T, PoolLease<T>> _poolRelease;
        private readonly Action<T> _release;

        // Touched with Interlocked because an instance is rented on one thread and can be disposed
        // on another; `long` so the counter cannot realistically wrap onto a live generation.
        private long _generation;

        internal PoolLease(T value, Action<T, PoolLease<T>> poolRelease)
        {
            this.value = value;
            _poolRelease = poolRelease;
            _release = null;
        }

        internal PoolLease(T value, Action<T> release)
        {
            this.value = value;
            _poolRelease = null;
            _release = release;
        }

        /// <summary>
        /// The generation the next release must present, for tests and diagnostics.
        /// </summary>
        internal long CurrentGeneration => Interlocked.Read(ref _generation);

        /// <summary>
        /// Marks the instance as rented and returns the generation the renter must present back.
        /// </summary>
        /// <remarks>
        /// Called while the pool holds the lock that also keeps two renters from taking the same
        /// instance, so the increment needs no further synchronization. A lease minted for a
        /// brand-new instance is not reachable by anyone else yet.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long Rent()
        {
            return ++_generation;
        }

        /// <summary>
        /// Claims the right to release the instance.
        /// </summary>
        /// <param name="generation">The generation the lease was rented at.</param>
        /// <returns>
        /// True for the one disposal that still holds the current generation. False for a copy of a
        /// lease that was already disposed, for a second disposal of the same lease, and for a lease
        /// whose instance has since been rented out again.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryRelease(long generation)
        {
            return Interlocked.CompareExchange(ref _generation, generation + 1, generation)
                == generation;
        }

        /// <summary>
        /// Runs the release the lease was built with. Only ever reached by a winning
        /// <see cref="TryRelease"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Release()
        {
            if (_poolRelease != null)
            {
                _poolRelease(value, this);
                return;
            }

            _release?.Invoke(value);
        }
    }

    /// <summary>
    /// One instance sitting in a pool's free list, with the time it was returned.
    /// </summary>
    /// <typeparam name="T">The pooled type.</typeparam>
    internal struct PooledEntry<T>
    {
        /// <summary>
        /// The pooled instance. Kept alongside <see cref="Lease"/> so purge paths read it directly.
        /// </summary>
        public T Value;

        /// <summary>
        /// When the instance was returned, for idle-timeout purging.
        /// </summary>
        public float ReturnTime;

        /// <summary>
        /// The instance's lease, handed back out on the next rent.
        /// </summary>
        public PoolLease<T> Lease;
    }
}
