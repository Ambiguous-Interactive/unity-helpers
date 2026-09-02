// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Utils;
    using static RelationalComponentProcessor;
#if !SINGLE_THREADED
    using System.Collections.Concurrent;
#endif

    /// <summary>
    /// Runs a Unity component query directly into a buffer of the element type, for an element type
    /// known only at run time.
    /// </summary>
    /// <remarks>
    /// This exists for one reason: <c>GetComponentsInChildren(Type, bool)</c> and
    /// <c>GetComponentsInParent(Type, bool)</c> have no caller-buffer overload, so every child or
    /// parent collection assignment allocates a <c>Component[]</c> that is copied out and thrown
    /// away. The generic overloads take a caller buffer and allocate nothing.
    ///
    /// It is deliberately NOT used for the sibling query, which already has a caller-buffer
    /// overload. Routing that one through here as well was measured and dropped: it removes no
    /// allocation and its time is 0.998x, so it was pure surface. The 1.15x-1.41x recorded on #534
    /// was measured on the query pipeline in isolation and does not survive at the assignment call
    /// site, where per-field overhead dominates -- the whole-call A/B is 1.030x for children and
    /// 1.007x for parents, best of three in one domain. The allocation is the win here; the clock
    /// is not.
    ///
    /// The scratch buffer is a <see cref="Buffers{T}"/> lease rather than the <c>[ThreadStatic]</c>
    /// list the sibling, child and parent fast invokers each hand-roll. That looks inconsistent with
    /// them and is measured: here the lease is 0.976-0.983x of the hand-rolled buffer across three
    /// orderings (pooled first, scratch first, interleaved), so specializing bought nothing. Session
    /// 215's opposite finding -- a lease costing 7% -- was on the sibling path, a ~1 us call where
    /// the same fixed lease cost is a far larger fraction than it is in this ~4.7 us one. Neither
    /// result generalizes to the other call site; measure before copying either.
    ///
    /// This construct is the one the relational fast path reverted once, so it is fail-soft in both
    /// places it can fail. Construction is guarded, and construction also <em>invokes</em> every
    /// entry point once against a real component, because an AOT runtime that never generated an
    /// instantiation refuses it when it is called rather than when the generic over it is closed. A
    /// refusal caches null and the non-generic path serves that element type for the rest of the
    /// process. Note that <see cref="FieldAccessor"/> in this same family already closes a generic
    /// over the field type at run time on every relational assignment, unguarded, and ships through
    /// the IL2CPP standalone legs.
    /// </remarks>
    internal abstract class RelationalComponentCollector
    {
#if SINGLE_THREADED
        private static readonly Dictionary<Type, RelationalComponentCollector> Collectors = new();
#else
        private static readonly ConcurrentDictionary<
            Type,
            RelationalComponentCollector
        > Collectors = new();
#endif

        private static readonly MethodInfo CreateGenericMethod =
            typeof(RelationalComponentCollector).GetMethod(
                nameof(CreateGeneric),
                BindingFlags.NonPublic | BindingFlags.Static
            );

        /// <summary>
        /// Forces every caller onto the non-generic fallback, as an AOT runtime that refuses the
        /// closed generic would. Exists so the two paths can be asserted equal on the same
        /// hierarchies rather than only the fast one being tested.
        /// </summary>
        internal static bool FallbackOnly;

        /// <summary>
        /// Gets the collector for <paramref name="elementType"/>, building and proving it on first
        /// use.
        /// </summary>
        /// <param name="elementType">Component type the query must return.</param>
        /// <param name="probe">
        /// A live component the proving invocations run against. A destroyed or null probe returns
        /// null without caching, so the next caller tries again.
        /// </param>
        /// <returns>The collector, or null when this runtime refuses it.</returns>
        internal static RelationalComponentCollector For(Type elementType, Component probe)
        {
            if (elementType == null || FallbackOnly)
            {
                return null;
            }

            if (Collectors.TryGetValue(elementType, out RelationalComponentCollector cached))
            {
                return cached;
            }

            /*
                A miss needs a live probe, and GetOrAdd cannot express that: handed a null one its
                factory would cache a refusal permanently for a reason that has nothing to do with
                this runtime. So the miss is filtered here and the store below is still atomic.
            */
            if (probe == null)
            {
                return null;
            }

#if SINGLE_THREADED
            RelationalComponentCollector created = Create(elementType, probe);
            Collectors[elementType] = created;
            return created;
#else
            /*
                GetOrAdd rather than an indexer store after the miss: the indexer is last-write-wins,
                so two threads racing a first use could each build and probe a collector and each
                return a different instance than the one that ends up cached. The state-taking
                overload keeps the lambda static, so no closure is allocated for `probe`.
            */
            return Collectors.GetOrAdd(
                elementType,
                static (type, live) => Create(type, live),
                probe
            );
#endif
        }

        /// <summary>
        /// Appends every matching component on <paramref name="source"/> and its descendants to
        /// <paramref name="destination"/>.
        /// </summary>
        internal abstract int CollectChildrenInto(
            Component source,
            bool includeInactive,
            List<Component> destination
        );

        /// <summary>
        /// Appends every matching component on <paramref name="source"/> and its ancestors to
        /// <paramref name="destination"/>.
        /// </summary>
        internal abstract int CollectParentsInto(
            Component source,
            bool includeInactive,
            List<Component> destination
        );

        private static RelationalComponentCollector Create(Type elementType, Component probe)
        {
            if (
                CreateGenericMethod == null
                || elementType.IsInterface
                || !typeof(Component).IsAssignableFrom(elementType)
            )
            {
                return null;
            }

            try
            {
                RelationalComponentCollector collector = (RelationalComponentCollector)
                    CreateGenericMethod.MakeGenericMethod(elementType).Invoke(null, null);
                if (collector == null)
                {
                    return null;
                }

                List<Component> sink = new();
                _ = collector.CollectChildrenInto(probe, true, sink);
                _ = collector.CollectParentsInto(probe, true, sink);
                return collector;
            }
            catch (Exception)
            {
                /*
                    An AOT runtime that never generated this instantiation refuses it either when the
                    generic is closed or when the closed method is first called, so both happen here
                    and a refusal is cached once per element type rather than retried per call.
                */
                return null;
            }
        }

        private static RelationalComponentCollector CreateGeneric<TElement>()
            where TElement : Component
        {
            return new TypedCollector<TElement>();
        }

        private sealed class TypedCollector<TElement> : RelationalComponentCollector
            where TElement : Component
        {
            internal override int CollectChildrenInto(
                Component source,
                bool includeInactive,
                List<Component> destination
            )
            {
                using PooledResource<List<TElement>> lease = Buffers<TElement>.List.Get(
                    out List<TElement> buffer
                );
                source.GetComponentsInChildren(includeInactive, buffer);
                return Drain(buffer, destination);
            }

            internal override int CollectParentsInto(
                Component source,
                bool includeInactive,
                List<Component> destination
            )
            {
                using PooledResource<List<TElement>> lease = Buffers<TElement>.List.Get(
                    out List<TElement> buffer
                );
                source.GetComponentsInParent(includeInactive, buffer);
                return Drain(buffer, destination);
            }

            private static int Drain(List<TElement> buffer, List<Component> destination)
            {
                int count = buffer.Count;
                for (int i = 0; i < count; ++i)
                {
                    destination.Add(buffer[i]);
                }

                return count;
            }
        }
    }
}
