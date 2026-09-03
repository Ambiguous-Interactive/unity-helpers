// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using UnityEngine;

    /// <summary>
    /// A single piece of global state that can be borrowed for the life of a <c>using</c> block and
    /// handed back to this owner exactly once, however many copies of the scope exist.
    /// </summary>
    /// <typeparam name="T">The type of the value the global holds.</typeparam>
    /// <remarks>
    /// <para>
    /// <b>Why this exists.</b> The obvious scope captures the previous value in one of its own
    /// fields and restores from that field:
    /// <code><![CDATA[
    /// public readonly struct ActiveTextureScope : IDisposable
    /// {
    ///     private readonly RenderTexture _previous;
    ///     public void Dispose() => RenderTexture.active = _previous;
    /// }
    /// ]]></code>
    /// Making it <c>readonly</c> fixes the half of the problem that a mutable "have I been disposed"
    /// flag gets wrong -- but it does not fix this half. Every copy agrees about <i>what</i> to put
    /// back and none of them agree about <i>whether it already has</i>, so a second
    /// <see cref="IDisposable.Dispose"/> re-imposes a value the world has moved past, which reads
    /// as "something else changed it back".
    /// </para>
    /// <para>
    /// <b>Giving a claim back is a call to whoever issued it, not an assignment.</b> A
    /// <see cref="Scope"/> holds only an identifier, and the state every copy has to agree on lives
    /// here, in the owner. Identifiers are never reused, so a stale copy's release is a no-op rather
    /// than a re-imposition.
    /// </para>
    /// <para>
    /// <b>Nesting and out-of-order disposal.</b> Nesting is the ordinary case -- a batch that sets a
    /// flag, containing a step that sets it again -- so the rule is stated for any depth and any
    /// order:
    /// <list type="bullet">
    /// <item><description>
    /// The global always holds the value the newest live borrow asked for. Releasing an OLDER borrow
    /// therefore writes nothing: the newer borrow is still live and is still entitled to the value
    /// it asked for.
    /// </description></item>
    /// <item><description>
    /// The released borrow's restore value is inherited by the borrow directly above it, so nothing
    /// is lost. When the last borrow is released the global returns to the value it held before the
    /// outermost borrow was taken, whatever order the releases happened in.
    /// </description></item>
    /// <item><description>
    /// The inheritance is conditional: the borrow above only takes over the restore value when what
    /// it captured is still the value the released borrow applied. Where something else has written
    /// to the global in between, that write is what the newer borrow captured and it is what gets
    /// restored.
    /// </description></item>
    /// </list>
    /// The alternative -- having an out-of-order release unwind every newer borrow with it -- was
    /// rejected because it takes a value away from a scope that is still running, which is the same
    /// class of surprise this type exists to remove.
    /// </para>
    /// <para>
    /// <b>Nothing here throws.</b> Disposing a <c>default</c> scope, disposing twice, disposing
    /// after <see cref="ReleaseAll"/>, and a getter or setter that throws are all handled: the
    /// failure is logged and the bookkeeping stays consistent. <see cref="Scope.Dispose"/> in
    /// particular runs from a <c>finally</c>, so a throw there would replace whatever exception the
    /// caller was already unwinding with.
    /// </para>
    /// <para>
    /// <b>Cost.</b> A borrow allocates nothing once the slot table has grown to the deepest nesting
    /// ever reached: <see cref="Scope"/> is a <c>readonly struct</c> and <c>using</c> over it calls
    /// <see cref="Scope.Dispose"/> directly rather than through a boxed interface. Construction
    /// allocates the owner and its table. The delegates are supplied once, at construction, rather
    /// than per borrow.
    /// </para>
    /// <para>
    /// <b>Threading.</b> Every public member is guarded by a per-instance monitor, so concurrent
    /// borrows and releases cannot corrupt the table. That guarantee is about this type's own
    /// bookkeeping, not about the global: a process-wide cell has one value, so two threads
    /// borrowing at once still last-writer-wins on the thing itself, and most globals worth
    /// borrowing (Unity's among them) are main-thread only regardless. The getter and setter are
    /// invoked while the monitor is held, which makes a borrow atomic against another thread's; a
    /// monitor is reentrant, so a delegate that borrows again on the same thread is safe. Under
    /// <c>SINGLE_THREADED</c> the monitor is compiled out entirely.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// private static readonly RestorableGlobal<RenderTexture> ActiveTexture =
    ///     new RestorableGlobal<RenderTexture>(
    ///         () => RenderTexture.active,
    ///         value => RenderTexture.active = value
    ///     );
    ///
    /// public static void BlitInto(RenderTexture target)
    /// {
    ///     using (ActiveTexture.Borrow(target))
    ///     {
    ///         GL.Clear(true, true, Color.clear);
    ///     }
    /// }
    /// ]]></code>
    /// </example>
    public sealed class RestorableGlobal<T>
    {
        private const int InitialCapacity = 4;
        private const int NoSlot = -1;

        private readonly Func<T> _read;
        private readonly Action<T> _write;
        private readonly IEqualityComparer<T> _comparer;

#if !SINGLE_THREADED
        private readonly object _gate = new object();
#endif

        private Entry[] _entries;
        private long _nextIdentifier;
        private int _slotsCreated;
        private int _freeHead = NoSlot;
        private int _newest = NoSlot;

        /// <summary>
        /// Creates an owner over the global that <paramref name="read"/> and
        /// <paramref name="write"/> address.
        /// </summary>
        /// <param name="read">Reads the global's current value.</param>
        /// <param name="write">Writes the global's value.</param>
        public RestorableGlobal(Func<T> read, Action<T> write)
            : this(read, write, null) { }

        /// <summary>
        /// Creates an owner that decides with <paramref name="comparer"/> whether a released
        /// borrow's restore value is still the one an outer borrow should inherit.
        /// </summary>
        /// <param name="read">Reads the global's current value.</param>
        /// <param name="write">Writes the global's value.</param>
        /// <param name="comparer">
        /// Compares two values of the global. Null selects <see cref="EqualityComparer{T}.Default"/>.
        /// </param>
        public RestorableGlobal(Func<T> read, Action<T> write, IEqualityComparer<T> comparer)
        {
            _read = read;
            _write = write;
            _comparer = comparer ?? EqualityComparer<T>.Default;
            _entries = new Entry[InitialCapacity];
            for (int index = 0; index < InitialCapacity; index++)
            {
                ref Entry entry = ref _entries[index];
                entry.freeNext = NoSlot;
                entry.older = NoSlot;
                entry.newer = NoSlot;
            }

            if (read == null || write == null)
            {
                Debug.LogWarning(
                    $"[{nameof(RestorableGlobal<T>)}] Constructed without a "
                        + $"{(read == null ? nameof(read) : nameof(write))} delegate, so every borrow "
                        + "will be a no-op that changes nothing."
                );
            }
        }

        /// <summary>
        /// How many borrows are currently live.
        /// </summary>
        public int Depth
        {
            get
            {
#if SINGLE_THREADED
                return DepthCore();
#else
                lock (_gate)
                {
                    return DepthCore();
                }
#endif
            }
        }

        /// <summary>
        /// Applies <paramref name="value"/> to the global until the returned scope is disposed.
        /// </summary>
        /// <param name="value">The value the global holds for the life of the scope.</param>
        /// <returns>A scope that gives the borrow back exactly once.</returns>
        public Scope Borrow(T value)
        {
            Scope scope;
#if SINGLE_THREADED
            BorrowCore(value, out scope);
            return scope;
#else
            lock (_gate)
            {
                BorrowCore(value, out scope);
            }

            return scope;
#endif
        }

        /// <summary>
        /// Applies <paramref name="value"/> to the global, reporting whether the global could
        /// actually be read and written.
        /// </summary>
        /// <param name="value">The value the global holds for the life of the scope.</param>
        /// <param name="scope">
        /// The scope to dispose. Always safe to dispose, including when this returns false.
        /// </param>
        /// <returns>True when the global was read and written without a delegate failing.</returns>
        public bool TryBorrow(T value, out Scope scope)
        {
#if SINGLE_THREADED
            return BorrowCore(value, out scope);
#else
            lock (_gate)
            {
                return BorrowCore(value, out scope);
            }
#endif
        }

        /// <summary>
        /// Releases every live borrow, leaving the global at the value it held before the oldest of
        /// them was taken.
        /// </summary>
        /// <remarks>
        /// Every scope already handed out becomes stale, and disposing one afterwards is a no-op
        /// rather than a re-imposition, because identifiers are never reused.
        /// </remarks>
        public void ReleaseAll()
        {
#if SINGLE_THREADED
            ReleaseAllCore();
#else
            lock (_gate)
            {
                ReleaseAllCore();
            }
#endif
        }

        private int DepthCore()
        {
            int depth = 0;
            for (int slot = _newest; 0 <= slot; slot = _entries[slot].older)
            {
                depth++;
            }

            return depth;
        }

        private bool BorrowCore(T value, out Scope scope)
        {
            if (_read == null || _write == null)
            {
                scope = default;
                return false;
            }

            T restore;
            try
            {
                restore = _read();
            }
            catch (Exception readFailure)
            {
                Debug.LogWarning(
                    $"[{nameof(RestorableGlobal<T>)}] The getter threw, so nothing was borrowed and "
                        + $"the global was left alone: {readFailure}"
                );
                scope = default;
                return false;
            }

            bool applied = TryWrite(value, "The setter threw while taking a borrow");

            int slot = TakeSlot();
            long identifier = ++_nextIdentifier;
            /*
                Indexed once, and only after TakeSlot has returned: TakeSlot is where the array
                grows, so a reference taken before it would address the array the resize replaced.
            */
            ref Entry entry = ref _entries[slot];
            entry.applied = value;
            entry.restore = restore;
            entry.identifier = identifier;
            entry.live = true;
            entry.older = _newest;
            entry.newer = NoSlot;
            if (0 <= _newest)
            {
                _entries[_newest].newer = slot;
            }

            _newest = slot;
            scope = new Scope(this, slot, identifier);
            return applied;
        }

        private void ReleaseAllCore()
        {
            while (0 <= _newest)
            {
                ReleaseCore(_newest, _entries[_newest].identifier);
            }
        }

        private bool Holds(int slot, long identifier)
        {
#if SINGLE_THREADED
            return HoldsCore(slot, identifier);
#else
            lock (_gate)
            {
                return HoldsCore(slot, identifier);
            }
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HoldsCore(int slot, long identifier)
        {
            return 0 <= slot
                && slot < _slotsCreated
                && _entries[slot].live
                && _entries[slot].identifier == identifier;
        }

        private void Release(int slot, long identifier)
        {
#if SINGLE_THREADED
            ReleaseCore(slot, identifier);
#else
            lock (_gate)
            {
                ReleaseCore(slot, identifier);
            }
#endif
        }

        private void ReleaseCore(int slot, long identifier)
        {
            if (!HoldsCore(slot, identifier))
            {
                return;
            }

            int older = _entries[slot].older;
            int newer = _entries[slot].newer;
            if (0 <= older)
            {
                _entries[older].newer = newer;
            }

            if (0 <= newer)
            {
                _entries[newer].older = older;
                if (_comparer.Equals(_entries[newer].restore, _entries[slot].applied))
                {
                    _entries[newer].restore = _entries[slot].restore;
                }
            }

            bool wasNewest = _newest == slot;
            if (wasNewest)
            {
                _newest = older;
            }

            /*
                Indexed once, and only here: the comparer above is the caller's, so a reference
                taken before it would address the array a re-entrant borrow could have resized.
            */
            ref Entry released = ref _entries[slot];
            T restore = released.restore;
            released = default;
            released.older = NoSlot;
            released.newer = NoSlot;
            released.freeNext = _freeHead;
            _freeHead = slot;

            if (wasNewest)
            {
                TryWrite(restore, "The setter threw while giving a borrow back");
            }
        }

        private bool TryWrite(T value, string what)
        {
            try
            {
                _write(value);
                return true;
            }
            catch (Exception writeFailure)
            {
                Debug.LogWarning($"[{nameof(RestorableGlobal<T>)}] {what}: {writeFailure}");
                return false;
            }
        }

        private int TakeSlot()
        {
            if (0 <= _freeHead)
            {
                int recycled = _freeHead;
                _freeHead = _entries[recycled].freeNext;
                return recycled;
            }

            int created = _slotsCreated;
            _slotsCreated++;
            if (_entries.Length <= created)
            {
                int previousCapacity = _entries.Length;
                int capacity = previousCapacity * 2;
                Array.Resize(ref _entries, capacity);
                for (int index = previousCapacity; index < capacity; index++)
                {
                    ref Entry entry = ref _entries[index];
                    entry.freeNext = NoSlot;
                    entry.older = NoSlot;
                    entry.newer = NoSlot;
                }
            }

            return created;
        }

        /// <summary>
        /// One live borrow, held by the owner rather than by the scope so every copy of the scope
        /// reads the same answer.
        /// </summary>
        private struct Entry
        {
            public T applied;
            public T restore;
            public long identifier;
            public int older;
            public int newer;
            public int freeNext;
            public bool live;
        }

        /// <summary>
        /// A live borrow of the global, given back to its owner when disposed.
        /// </summary>
        /// <remarks>
        /// Copy it, pass it by value, capture it: every copy names the same borrow, exactly one
        /// release happens, and a copy disposed after that release does nothing at all.
        /// </remarks>
        public readonly struct Scope : IDisposable
        {
            private readonly RestorableGlobal<T> _owner;
            private readonly int _slot;
            private readonly long _identifier;

            internal Scope(RestorableGlobal<T> owner, int slot, long identifier)
            {
                _owner = owner;
                _slot = slot;
                _identifier = identifier;
            }

            /// <summary>
            /// True while this borrow is still live, on every copy of the scope at once.
            /// </summary>
            public bool IsHeld => _owner != null && _owner.Holds(_slot, _identifier);

            /// <summary>
            /// Gives the borrow back. Safe to call any number of times, on any number of copies.
            /// </summary>
            public void Dispose()
            {
                if (_owner == null)
                {
                    return;
                }

                _owner.Release(_slot, _identifier);
            }
        }
    }
}
