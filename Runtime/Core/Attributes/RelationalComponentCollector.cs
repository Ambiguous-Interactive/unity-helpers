// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using UnityEngine;
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

            if (probe == null)
            {
                return null;
            }

            RelationalComponentCollector created = Create(elementType, probe);
            Collectors[elementType] = created;
            return created;
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
                // An AOT runtime that never generated this instantiation refuses it either when the
                // generic is closed or when the closed method is first called, so both happen here
                // and a refusal is cached once per element type rather than retried per call.
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
            // Detached while in use so a re-entrant call gets its own buffer, matching the family's
            // other scratch buffers. Assignment is main-thread-only; [ThreadStatic] keeps it correct
            // if that ever stops being true.
            [ThreadStatic]
            private static List<TElement> Scratch;

            internal override int CollectChildrenInto(
                Component source,
                bool includeInactive,
                List<Component> destination
            )
            {
                List<TElement> buffer = Rent();
                try
                {
                    source.GetComponentsInChildren(includeInactive, buffer);
                    return Drain(buffer, destination);
                }
                finally
                {
                    Return(buffer);
                }
            }

            internal override int CollectParentsInto(
                Component source,
                bool includeInactive,
                List<Component> destination
            )
            {
                List<TElement> buffer = Rent();
                try
                {
                    source.GetComponentsInParent(includeInactive, buffer);
                    return Drain(buffer, destination);
                }
                finally
                {
                    Return(buffer);
                }
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

            private static List<TElement> Rent()
            {
                List<TElement> results = Scratch;
                if (results == null)
                {
                    results = new List<TElement>();
                }
                else
                {
                    Scratch = null;
                    results.Clear();
                }

                return results;
            }

            private static void Return(List<TElement> results)
            {
                if (results == null || Scratch != null)
                {
                    return;
                }

                results.Clear();
                if (MaximumRetainedScratchCapacity < results.Capacity)
                {
                    results.Capacity = 0;
                }

                Scratch = results;
            }
        }
    }
}
