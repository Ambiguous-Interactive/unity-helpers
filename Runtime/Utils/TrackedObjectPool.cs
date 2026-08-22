// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;

    /// <summary>
    /// A pool for <see cref="UnityEngine.Object"/> instances whose lifetime ends in a callback rather
    /// than at the end of a scope, and whose teardown therefore has to reach what is still checked out.
    /// </summary>
    /// <typeparam name="T">The pooled type.</typeparam>
    /// <remarks>
    /// <para>
    /// <b>The problem it solves.</b> A pool that only knows what is <i>in</i> it cannot clean up what is
    /// <i>out</i> of it. Dispose such a pool while a pooled effect is still playing and the effect stays
    /// in the scene forever, and its ending calls back into a pool that is gone. The item is owned by
    /// nothing: the pool no longer holds it and the caller handed ownership to a tween.
    /// </para>
    /// <para>
    /// <b>Why not <see cref="WallstopGenericPool{T}"/>.</b> That pool hands out a
    /// <see cref="PooledResource{T}"/>, whose disposal returns the item — a lexical scope, which is
    /// exactly right for the scratch buffers it serves and cannot strand anything. It also cannot
    /// <i>refuse</i> a return, and refusing one is the whole point here: a destroyed Unity object put
    /// back in a pool is the next thing handed out.
    /// </para>
    /// <para>
    /// <b>The distinction this type is built on.</b> For a <see cref="UnityEngine.Object"/>,
    /// <c>ReferenceEquals(item, null)</c> asks "was anything handed in" and <c>item == null</c> asks
    /// "has it been destroyed". They are different questions and this type needs both: an item
    /// destroyed out from under its flight is still the entry sitting in the tracking list, so removal
    /// is unconditional while returning it to the pool is not. Guarding the removal instead leaks one
    /// dead reference per use, which is the unbounded growth the list exists to prevent.
    /// </para>
    /// <para>
    /// <typeparamref name="T"/> is constrained to <see cref="UnityEngine.Object"/>, so it is always a
    /// reference type and there is no value-type case to reason about — and that constraint is what
    /// makes the two questions above expressible at all. A pool for plain objects, whose lifetime is a
    /// scope, is <see cref="WallstopGenericPool{T}"/>.
    /// </para>
    /// <para>
    /// Not thread-safe, because <see cref="UnityEngine.Object"/> is a main-thread type.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Utils;
    ///
    /// TrackedObjectPool&lt;GameObject&gt; puffs = new(
    ///     producer: () =&gt; Instantiate(_puffPrefab),
    ///     onTake: puff =&gt; puff.SetActive(true),
    ///     onRelease: puff =&gt; puff.SetActive(false),
    ///     onDestroy: puff =&gt; Destroy(puff));
    ///
    /// if (puffs.TryTake(out GameObject puff))
    /// {
    ///     puff.transform.position = point;
    ///     // The ending is a callback, not a scope: this pool is what makes that safe.
    ///     tween.OnComplete(() =&gt; puffs.Release(puff));
    /// }
    ///
    /// // On teardown, the puff still in flight is destroyed too rather than left in the scene.
    /// puffs.Dispose();
    /// </code>
    /// </example>
    public sealed class TrackedObjectPool<T> : IDisposable
        where T : UnityEngine.Object
    {
        /// <summary>
        /// How many items are checked out, and would therefore be destroyed by
        /// <see cref="Dispose"/> right now.
        /// </summary>
        public int InFlightCount => _inFlight.Count;

        /// <summary>How many items are pooled and ready to be handed out.</summary>
        public int IdleCount => _idle.Count;

        /// <summary>Whether <see cref="Dispose"/> has run.</summary>
        public bool IsDisposed => _disposed;

        private readonly Func<T> _producer;
        private readonly Action<T> _onTake;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly int _maxIdleCount;
        private readonly List<T> _idle = new();
        private readonly List<T> _inFlight = new();

        private bool _disposed;

        /// <summary>
        /// Creates a pool.
        /// </summary>
        /// <param name="producer">Builds an instance when none is pooled. A pool with no producer
        /// hands out only what is returned to it.</param>
        /// <param name="onTake">Applied to an item as it is handed out.</param>
        /// <param name="onRelease">Applied to a live item as it comes back.</param>
        /// <param name="onDestroy">Applied to an item this pool is finished with — one evicted by
        /// <paramref name="maxIdleCount"/>, and every item <see cref="Dispose"/> reaches. Pass
        /// <c>null</c> only when something else owns destruction; this pool never calls
        /// <c>Object.Destroy</c> on its own initiative.</param>
        /// <param name="maxIdleCount">The most items to keep pooled. Zero or less is unbounded.</param>
        public TrackedObjectPool(
            Func<T> producer,
            Action<T> onTake = null,
            Action<T> onRelease = null,
            Action<T> onDestroy = null,
            int maxIdleCount = 0
        )
        {
            _producer = producer;
            _onTake = onTake;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            _maxIdleCount = maxIdleCount;
        }

        /// <summary>
        /// Checks out an item, creating one when none is pooled.
        /// </summary>
        /// <param name="taken">Receives the item, or <c>null</c> when none could be produced.</param>
        /// <returns><c>true</c> when an item was checked out.</returns>
        /// <remarks>
        /// An item that was destroyed while pooled is discarded rather than handed out. That happens
        /// whenever a scene unload takes the objects but not the pool holding them.
        /// </remarks>
        public bool TryTake(out T taken)
        {
            if (_disposed)
            {
                taken = null;
                return false;
            }

            T candidate = null;
            while (_idle.Count > 0)
            {
                // The last entry, so this removal is already the cheap one.
                int last = _idle.Count - 1;
                T pooled = _idle[last];
                _idle.RemoveAt(last);
                if (IsGone(pooled))
                {
                    continue;
                }

                candidate = pooled;
                break;
            }

            if (!WasHandedIn(candidate))
            {
                if (_producer == null)
                {
                    taken = null;
                    return false;
                }

                candidate = _producer();

                // Covers both a producer that answered null and one that answered something already
                // destroyed; neither is usable and neither should enter the tracking list.
                if (IsGone(candidate))
                {
                    taken = null;
                    return false;
                }
            }

            _inFlight.Add(candidate);
            _onTake?.Invoke(candidate);
            taken = candidate;
            return true;
        }

        /// <summary>
        /// Checks an item back in.
        /// </summary>
        /// <param name="taken">The item this pool handed out.</param>
        /// <returns><c>true</c> when the item was checked out by this pool, whether or not it
        /// survived to be pooled again.</returns>
        /// <remarks>
        /// Releasing something this pool did not hand out, or releasing twice, answers <c>false</c>
        /// rather than throwing — the caller is usually a tween's completion callback, where a throw
        /// surfaces a level away from its cause and often nowhere at all.
        /// </remarks>
        public bool Release(T taken)
        {
            if (!WasHandedIn(taken))
            {
                return false;
            }

            int index = IndexOfInFlight(taken);
            if (index < 0)
            {
                return false;
            }

            // Unconditional, and this is the line that is easy to get wrong: a destroyed item is
            // still the entry in this list, and skipping the removal leaks it forever. Swap-back
            // because nothing reads this list in order -- Dispose drains all of it and IndexOfInFlight
            // scans all of it -- so paying to shift the tail would buy nothing.
            _inFlight.RemoveAtSwapBack(index);

            // Reached only for something that WAS handed in, so this is asking whether it has been
            // destroyed since -- a different question from the one above, and the reason this type
            // exists. It is out of the pool's hands either way; it just must not be pooled.
            if (IsGone(taken))
            {
                return true;
            }

            _onRelease?.Invoke(taken);

            if (_maxIdleCount > 0 && _idle.Count >= _maxIdleCount)
            {
                _onDestroy?.Invoke(taken);
                return true;
            }

            _idle.Add(taken);
            return true;
        }

        /// <summary>
        /// Destroys everything this pool is responsible for, in flight included.
        /// </summary>
        /// <remarks>
        /// The in-flight list is drained into scratch storage <b>before</b> anything is destroyed, so
        /// that a <see cref="Release"/> arriving from a destroyed item's own ending finds nothing to
        /// hand back and answers <c>false</c> instead of being counted twice.
        /// </remarks>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            using (Buffers<T>.List.Get(out List<T> pending))
            {
                pending.AddRange(_inFlight);
                _inFlight.Clear();
                pending.AddRange(_idle);
                _idle.Clear();

                for (int i = 0; i < pending.Count; ++i)
                {
                    T current = pending[i];
                    if (IsGone(current))
                    {
                        continue;
                    }

                    _onDestroy?.Invoke(current);
                }
            }
        }

        // The two questions this type is built on, named, because Unity's == reads as an ordinary
        // null check and is not one: it answers true for a destroyed object as well as for a null
        // reference. Every call site here means exactly one of these, and which one is never obvious
        // from the operator alone.
        private static bool WasHandedIn(T candidate)
        {
            return !ReferenceEquals(candidate, null);
        }

        private static bool IsGone(T candidate)
        {
            return candidate == null;
        }

        private int IndexOfInFlight(T taken)
        {
            for (int i = 0; i < _inFlight.Count; ++i)
            {
                // ReferenceEquals rather than ==, so a destroyed entry is still findable: Unity's
                // operator answers true for two different destroyed objects.
                if (ReferenceEquals(_inFlight[i], taken))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
