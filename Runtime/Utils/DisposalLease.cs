// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Threading;

    /// <summary>
    /// The right to dispose something exactly once, carried by value so a struct can hold it without
    /// allocating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A struct that implements <see cref="IDisposable"/> and tracks disposal in one of its own
    /// fields is not safe to copy. Passing it to a method by value, assigning it, or capturing it
    /// produces a copy carrying its own copy of that flag, which cannot see that the original was
    /// already disposed, so disposing both runs the disposal twice — returning one pooled buffer to
    /// two live callers, decrementing a scope counter twice for one scope, and so on.
    /// </para>
    /// <para>
    /// A lease is a <c>(slot, generation)</c> handle. The generation lives in
    /// <see cref="DisposalLeases"/>, outside the struct, so every copy reads the same one; it
    /// advances on each acquire, and <see cref="TryClaim"/> succeeds only for the copy still holding
    /// the current value. This is the generational-handle (slotmap) pattern — the same shape as
    /// Unity DOTS's <c>Entity{Index, Version}</c>, Rust's <c>slotmap</c>, and Vulkan object handles —
    /// chosen because it is the standard way to detect a stale handle without allocating one.
    /// </para>
    /// <para>
    /// It catches all three shapes: the copy disposed immediately, the same lease disposed twice,
    /// and — the case a "is it already back in the pool?" check cannot see — a stale copy disposed
    /// after the slot has been acquired again, which is the one that corrupts a live renter.
    /// </para>
    /// <para>
    /// A <c>default</c> lease is not held, so <see cref="TryClaim"/> returns false and disposing a
    /// <c>default</c> struct does nothing.
    /// </para>
    /// </remarks>
    internal readonly struct DisposalLease
    {
        // Slot 0 is never handed out, so `default` reads as "not held" without a second field.
        private readonly int _slot;
        private readonly long _generation;

        internal DisposalLease(int slot, long generation)
        {
            _slot = slot;
            _generation = generation;
        }

        /// <summary>
        /// True while this lease is still the current holder of its slot: not <c>default</c>, not
        /// yet claimed, and not superseded by a copy that claimed first.
        /// </summary>
        /// <remarks>
        /// Unlike a per-copy flag, this answers for every copy at once — a copy of a lease that
        /// another copy already disposed reports false, which is the honest answer.
        /// </remarks>
        internal bool IsHeld
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _slot != 0 && DisposalLeases.CurrentGeneration(_slot) == _generation;
        }

        /// <summary>
        /// The slot behind this lease, so a test can assert on ownership. Not part of the disposal
        /// contract.
        /// </summary>
        internal int SlotForTests
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _slot;
        }

        /// <summary>
        /// Claims without recycling the slot, so a test can control exactly when it becomes
        /// reusable and observe who holds it in between.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryClaimWithoutRelease()
        {
            return _slot != 0 && DisposalLeases.TryClaim(_slot, _generation);
        }

        /// <summary>
        /// Claims the right to dispose and hands the slot back for reuse, for the one copy still
        /// holding the current generation.
        /// </summary>
        /// <remarks>
        /// Recycling here rather than in the caller is deliberate: the slot must be released exactly
        /// once, and the only place that is known to have happened exactly once is the winning
        /// claim.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryClaim()
        {
            if (_slot == 0 || !DisposalLeases.TryClaim(_slot, _generation))
            {
                return false;
            }

            DisposalLeases.Release(_slot);
            return true;
        }
    }

    /// <summary>
    /// The generations behind every <see cref="DisposalLease"/>, stored so that acquiring, claiming
    /// and recycling a lease all allocate nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generations live in fixed-size blocks that are never reallocated. Growth appends a block, so
    /// the address an <see cref="Interlocked"/> operation targets stays valid for the life of the
    /// process. A single growable array would move a live counter out from under a concurrent claim.
    /// </para>
    /// <para>
    /// Recycling is per-thread, through an intrusive free list threaded on
    /// <c>_freeNext</c>. A thread that acquires and releases in balance — which is what a
    /// <c>using</c> block does — reuses its own slots with no atomics and no contention at all, so
    /// there is no process-wide free-list head for many threads to fight over. A thread holding
    /// slots it never reuses costs 12 bytes each, and a thread that only ever acquires simply
    /// creates fresh slots.
    /// </para>
    /// <para>
    /// Reusing a slot for a new owner is safe on its own terms: a generation only ever advances, so
    /// a stale lease from that slot's previous owner can never match again. That is what removes the
    /// recycling hazard a plain free list would otherwise have.
    /// </para>
    /// <para>
    /// A lease that is never disposed keeps its slot. That costs 12 bytes, cannot grow with reuse,
    /// and the same accident already loses whatever the lease was guarding. Tracking live owners in
    /// a set instead would allocate on every acquire, which is what this design exists to avoid.
    /// </para>
    /// </remarks>
    internal static class DisposalLeases
    {
        private const int BlockShift = 10;
        private const int BlockSize = 1 << BlockShift;
        private const int BlockMask = BlockSize - 1;

        // Slot 0 is burned so it can mean "not held", which is why _nextNewSlot starts at 1.
        private static long[][] _generations = { new long[BlockSize] };
        private static int[][] _freeNext = { new int[BlockSize] };
        private static int _blockCount = 1;
        private static int _nextNewSlot = 1;

        private static readonly object GrowthGate = new();

#if SINGLE_THREADED
        private static int _freeHead;
#else
        [ThreadStatic]
        private static int _freeHead;
#endif

        /// <summary>
        /// How many slots have ever been created, for diagnostics and tests.
        /// </summary>
        internal static int SlotsCreated => Volatile.Read(ref _nextNewSlot) - 1;

        /// <summary>
        /// Takes a slot and leases it. The lease is surrendered by a winning
        /// <see cref="DisposalLease.TryClaim"/>, which also recycles the slot.
        /// </summary>
        /// <remarks>
        /// The generation is advanced with a store-release rather than an interlocked increment,
        /// which is what keeps an acquire down to roughly the cost of a plain write. That is sound
        /// because the caller is the slot's exclusive owner at this moment, and no outstanding lease
        /// can match it:
        /// <list type="bullet">
        /// <item><description>
        /// A recycled slot reached the free list only through a winning <see cref="TryClaim"/>,
        /// which already advanced the generation past every lease that was outstanding on it.
        /// </description></item>
        /// <item><description>
        /// A slot is pushed onto the free list of the thread that claimed it, so the last write to
        /// its generation was made by this same thread and is already visible to it.
        /// </description></item>
        /// <item><description>
        /// A brand-new slot has never been leased at all.
        /// </description></item>
        /// </list>
        /// The store-release pairs with the interlocked read-modify-write in
        /// <see cref="TryClaim"/>, so a lease handed to another thread sees the new generation.
        /// <para>
        /// The one thing to check when changing this: no outstanding lease may hold the generation
        /// being written. It cannot, because the winning claim that freed this slot set the
        /// generation to <c>g + 1</c> while every lease outstanding on it held at most <c>g</c>, and
        /// nothing has since handed out a lease at <c>g + 1</c> — this call is the first. A stale
        /// claim therefore fails its compare-and-swap rather than racing this write.
        /// </para>
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static DisposalLease Acquire()
        {
            int slot = _freeHead;
            if (slot != 0)
            {
                _freeHead = FreeNextOf(slot);
            }
            else
            {
#if SINGLE_THREADED
                slot = _nextNewSlot++;
#else
                slot = Interlocked.Increment(ref _nextNewSlot) - 1;
#endif
                EnsureBlock(slot >> BlockShift);
            }

            ref long generation = ref GenerationRef(slot);
            long acquired = generation + 1;
#if SINGLE_THREADED
            generation = acquired;
#else
            Volatile.Write(ref generation, acquired);
#endif
            return new DisposalLease(slot, acquired);
        }

        /// <summary>
        /// The generation a slot is currently on, for <see cref="DisposalLease.IsHeld"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long CurrentGeneration(int slot)
        {
#if SINGLE_THREADED
            return GenerationRef(slot);
#else
            return Interlocked.Read(ref GenerationRef(slot));
#endif
        }

        /// <summary>
        /// Advances the generation past <paramref name="generation"/>, for the one claimer still
        /// holding it.
        /// </summary>
        /// <remarks>
        /// This is the only place two threads can contend, and it is the one place that must not be
        /// resolved by anything weaker than a compare-and-swap: copies of a lease are exactly the
        /// callers racing here, and electing more than one winner is the defect being prevented.
        /// Under <c>SINGLE_THREADED</c> there is no second thread to race, and the package strips
        /// synchronization there as it does in its pools.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryClaim(int slot, long generation)
        {
#if SINGLE_THREADED
            ref long current = ref GenerationRef(slot);
            if (current != generation)
            {
                return false;
            }

            current = generation + 1;
            return true;
#else
            return Interlocked.CompareExchange(ref GenerationRef(slot), generation + 1, generation)
                == generation;
#endif
        }

        /// <summary>
        /// Puts a slot back on the calling thread's free list.
        /// </summary>
        /// <remarks>
        /// Deliberately unsynchronized, and it does not need to be. <c>_freeHead</c> is
        /// <see cref="ThreadStaticAttribute"/>, so this touches only the calling thread's own list
        /// and no other thread can observe it. The <c>_freeNext</c> links live in a shared array,
        /// but two threads can never write the same entry: a slot reaches here only through a
        /// winning <see cref="TryClaim"/>, and the compare-and-swap elects exactly one winner, so
        /// at most one thread owns any given slot at any moment. Distinct <c>int</c> elements of an
        /// array are independent and aligned, so there is nothing left to tear.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Release(int slot)
        {
            SetFreeNext(slot, _freeHead);
            _freeHead = slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ref long GenerationRef(int slot)
        {
            return ref Volatile.Read(ref _generations)[slot >> BlockShift][slot & BlockMask];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int FreeNextOf(int slot)
        {
            return Volatile.Read(ref _freeNext)[slot >> BlockShift][slot & BlockMask];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetFreeNext(int slot, int next)
        {
            Volatile.Read(ref _freeNext)[slot >> BlockShift][slot & BlockMask] = next;
        }

        private static void EnsureBlock(int block)
        {
            if (block < Volatile.Read(ref _blockCount))
            {
                return;
            }

            lock (GrowthGate)
            {
                int required = block + 1;
                if (required <= _blockCount)
                {
                    return;
                }

                if (_generations.Length < required)
                {
                    int capacity = _generations.Length * 2;
                    while (capacity < required)
                    {
                        capacity *= 2;
                    }

                    long[][] grownGenerations = new long[capacity][];
                    int[][] grownFree = new int[capacity][];
                    Array.Copy(_generations, grownGenerations, _blockCount);
                    Array.Copy(_freeNext, grownFree, _blockCount);
                    for (int i = _blockCount; i < required; ++i)
                    {
                        grownGenerations[i] = new long[BlockSize];
                        grownFree[i] = new int[BlockSize];
                    }

                    /*
                        Only the outer arrays are replaced, and they are published already populated.
                        Every existing block keeps its identity, so a concurrent Interlocked on a live
                        generation still targets the same memory.
                    */
                    Volatile.Write(ref _generations, grownGenerations);
                    Volatile.Write(ref _freeNext, grownFree);
                }
                else
                {
                    for (int i = _blockCount; i < required; ++i)
                    {
                        _generations[i] = new long[BlockSize];
                        _freeNext[i] = new int[BlockSize];
                    }
                }

                Volatile.Write(ref _blockCount, required);
            }
        }
    }
}
