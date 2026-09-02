// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Random;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Extension methods for IEnumerable providing collection operations, conversions, shuffling, and iteration utilities.
    /// </summary>
    /// <remarks>
    /// Thread Safety: Most methods are not thread-safe unless noted otherwise. Concurrent enumeration requires external synchronization.
    /// Performance: Methods are optimized for common collection types (IList, IReadOnlyList, HashSet) with pattern matching.
    /// Allocations: Documented per method. Many methods use pooled buffers to minimize allocations.
    /// </remarks>
    public static class IEnumerableExtensions
    {
        /// <summary>
        /// Converts an enumerable collection to a LinkedList.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="source">The source enumerable to convert.</param>
        /// <returns>A new LinkedList containing all elements from the source.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if source is null (thrown by LinkedList constructor).</para>
        /// <para>Thread safety: Not thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements in source.</para>
        /// <para>Allocations: Allocates a new LinkedList and a node for each element.</para>
        /// <para>Edge cases: Empty source results in an empty LinkedList.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when source is null.</exception>
        public static LinkedList<T> ToLinkedList<T>(this IEnumerable<T> source)
        {
            return new LinkedList<T>(source);
        }

        /// <summary>
        /// Converts an enumerable to an IList, avoiding allocation if the enumerable is already an IList.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="enumeration">The enumerable to convert.</param>
        /// <returns>The original enumeration if it's already an IList, otherwise a new List containing all elements.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if enumeration is null when calling ToList().</para>
        /// <para>Thread safety: Not thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(1) if enumeration is already IList, O(n) otherwise where n is the number of elements.</para>
        /// <para>Allocations: No allocation if enumeration is already an IList. Otherwise allocates a new List.</para>
        /// <para>Edge cases: Returns the original IList reference if enumeration is already an IList, enabling type-checking optimizations.</para>
        /// </remarks>
        public static IList<T> AsList<T>(this IEnumerable<T> enumeration)
        {
            if (enumeration is IList<T> list)
            {
                return list;
            }

            return enumeration.ToList();
        }

        /// <summary>
        /// Sorts the elements of a sequence in ascending order using a comparison function.
        /// </summary>
        /// <typeparam name="T">The type of the elements of enumeration.</typeparam>
        /// <param name="enumeration">A sequence of values to order.</param>
        /// <param name="comparer">A comparison function that returns negative if first is less than second, 0 if equal, positive if greater.</param>
        /// <returns>A new List containing the sorted elements.</returns>
        /// <remarks>
        /// <para>Null handling: Comparer can be invoked with null elements depending on enumeration content.</para>
        /// <para>Thread safety: Not thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(n log n) where n is the number of elements (uses List.Sort).</para>
        /// <para>Allocations: Allocates a result List. Uses pooled buffer for intermediate work.</para>
        /// <para>Edge cases: Empty or single element collections return without sorting.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when comparer is null (propagated from FuncBasedComparer).</exception>
        public static List<T> OrderBy<T>(this IEnumerable<T> enumeration, Func<T, T, int> comparer)
        {
            FuncBasedComparer<T> typedComparer = new(comparer);
            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> buffer);

            buffer.AddRange(enumeration);

            if (buffer.Count <= 1)
            {
                return new List<T>(buffer);
            }

            buffer.Sort(typedComparer);
            return new List<T>(buffer);
        }

        /// <summary>
        /// Sorts the elements of a sequence in ascending order using their natural IComparable ordering.
        /// </summary>
        /// <typeparam name="T">The type of the elements of enumerable. Must implement IComparable.</typeparam>
        /// <param name="enumerable">A sequence of values to order.</param>
        /// <returns>A new List containing the sorted elements in ascending order.</returns>
        /// <remarks>
        /// <para>Null handling: Behavior with null elements depends on T's CompareTo implementation.</para>
        /// <para>Thread safety: Not thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(n log n) where n is the number of elements (uses List.Sort).</para>
        /// <para>Allocations: Allocates a result List. Uses pooled buffer for intermediate work.</para>
        /// <para>Edge cases: Empty or single element collections return without sorting.</para>
        /// </remarks>
        public static List<T> Ordered<T>(this IEnumerable<T> enumerable)
            where T : IComparable
        {
            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> buffer);

            buffer.AddRange(enumerable);

            if (buffer.Count <= 1)
            {
                return new List<T>(buffer);
            }

            buffer.Sort();
            return new List<T>(buffer);
        }

        /// <summary>
        /// Returns a randomly shuffled version of the enumerable.
        /// </summary>
        /// <typeparam name="T">The type of the elements of enumerable.</typeparam>
        /// <param name="enumerable">A sequence of values to shuffle.</param>
        /// <param name="random">The random number generator to use. If null, uses PRNG.Instance.</param>
        /// <returns>A new List containing the same elements in random order.</returns>
        /// <remarks>
        /// <para>Null handling: If enumerable is null, will throw when enumerated. If random is null, uses PRNG.Instance.</para>
        /// <para>Thread safety: Not thread-safe if random is shared. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements (uses Fisher-Yates shuffle via IListExtensions.Shuffle).</para>
        /// <para>Allocations: Allocates a result List. Uses pooled buffer for intermediate work.</para>
        /// <para>Edge cases: Empty or single element collections return unchanged. Shuffle quality depends on random implementation.</para>
        /// </remarks>
        public static List<T> Shuffled<T>(this IEnumerable<T> enumerable, IRandom random = null)
        {
            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> buffer);
            buffer.AddRange(enumerable);
            buffer.Shuffle(random);
            return new List<T>(buffer);
        }

        /// <summary>
        /// Creates an infinite repeating sequence from the given enumerable.
        /// </summary>
        /// <typeparam name="T">The type of the elements of enumerable.</typeparam>
        /// <param name="enumerable">A sequence of values to repeat infinitely.</param>
        /// <returns>An infinite IEnumerable that cycles through the source elements repeatedly.</returns>
        /// <remarks>
        /// <para>Null handling: If enumerable is null, behavior is undefined. Empty enumerables immediately yield break.</para>
        /// <para>Thread safety: Not thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(1) amortized per element. Optimized for common collection types (IList, HashSet, etc). Unknown collection types use buffering on first iteration.</para>
        /// <para>Allocations: No allocations for known collection types. Unknown types allocate a pooled buffer during first enumeration.</para>
        /// <para>Edge cases: Empty collections immediately stop enumeration. Unknown collection types buffer all elements on first pass. The iterator will loop forever for non-empty collections.</para>
        /// </remarks>
        public static IEnumerable<T> Infinite<T>(this IEnumerable<T> enumerable)
        {
            switch (enumerable)
            {
                case IReadOnlyList<T> { Count: 0 }:
                {
                    yield break;
                }
                case IReadOnlyList<T> readonlyList:
                {
                    while (true)
                    {
                        for (int i = 0; i < readonlyList.Count; ++i)
                        {
                            yield return readonlyList[i];
                        }
                    }
                }
                case IList<T> { Count: 0 }:
                {
                    yield break;
                }
                case IList<T> list:
                {
                    while (true)
                    {
                        for (int i = 0; i < list.Count; ++i)
                        {
                            yield return list[i];
                        }
                    }
                }
                case HashSet<T> { Count: 0 }:
                {
                    yield break;
                }
                case HashSet<T> hashSet:
                {
                    while (true)
                    {
                        foreach (T element in hashSet)
                        {
                            yield return element;
                        }
                    }
                }
                case Queue<T> { Count: 0 }:
                {
                    yield break;
                }
                case Queue<T> queue:
                {
                    while (true)
                    {
                        foreach (T element in queue)
                        {
                            yield return element;
                        }
                    }
                }
                case Stack<T> { Count: 0 }:
                {
                    yield break;
                }
                case Stack<T> stack:
                {
                    while (true)
                    {
                        foreach (T element in stack)
                        {
                            yield return element;
                        }
                    }
                }
                case SortedSet<T> { Count: 0 }:
                {
                    yield break;
                }
                case SortedSet<T> sortedSet:
                {
                    while (true)
                    {
                        foreach (T element in sortedSet)
                        {
                            yield return element;
                        }
                    }
                }
                case LinkedList<T> { Count: 0 }:
                {
                    yield break;
                }
                case LinkedList<T> linkedList:
                {
                    while (true)
                    {
                        foreach (T element in linkedList)
                        {
                            yield return element;
                        }
                    }
                }
            }

            using PooledResource<List<T>> buffer = Buffers<T>.List.Get(out List<T> bufferList);
            foreach (T element in enumerable)
            {
                bufferList.Add(element);
                yield return element;
            }

            if (bufferList.Count == 0)
            {
                yield break;
            }

            while (true)
            {
                foreach (T element in bufferList)
                {
                    yield return element;
                }
            }
        }

        /// <summary>
        /// Splits an enumerable into partitions of the specified size.
        /// </summary>
        /// <typeparam name="T">The type of the elements of items.</typeparam>
        /// <param name="items">The sequence to partition.</param>
        /// <param name="size">The maximum number of elements per partition.</param>
        /// <returns>An IEnumerable of IEnumerable where each inner enumerable contains up to 'size' elements.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if items is null when GetEnumerator is called.</para>
        /// <para>Thread safety: Not thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the total number of elements. Lazy evaluation - partitions are created as enumerated.</para>
        /// <para>Allocations: Uses a pooled List buffer for each partition, reducing allocations. The buffer is reused across partitions.</para>
        /// <para>Edge cases: Last partition may have fewer than 'size' elements. Size less than 1 will cause infinite loop or no partitions. Empty items yields no partitions.</para>
        /// </remarks>
        public static IEnumerable<IEnumerable<T>> Partition<T>(this IEnumerable<T> items, int size)
        {
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be positive.");
            }
            using IEnumerator<T> enumerator = items.GetEnumerator();

            while (enumerator.MoveNext())
            {
                /*
                    Allocate one independent list per partition to ensure the yielded value
                    is not mutated by subsequent iterations.
                */
                List<T> partition = new(size);

                int count = 0;
                do
                {
                    partition.Add(enumerator.Current);
                } while (++count < size && enumerator.MoveNext());

                yield return partition;
            }
        }

        /// <summary>
        /// Splits an enumerable into partitions of the specified size, returning pooled lists.
        /// Each yielded value must be disposed by the consumer to return the list to the pool.
        /// </summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="items">Sequence to partition.</param>
        /// <param name="size">Maximum elements per partition.</param>
        /// <returns>Sequence of pooled list resources; dispose each to return to pool.</returns>
        public static IEnumerable<PooledResource<List<T>>> PartitionPooled<T>(
            this IEnumerable<T> items,
            int size
        )
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }
            if (size <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be positive.");
            }

            return new PartitionPooledEnumerable<T>(items, size);
        }

        private sealed class PartitionPooledEnumerable<T> : IEnumerable<PooledResource<List<T>>>
        {
            private readonly IEnumerable<T> _items;
            private readonly int _size;

            public PartitionPooledEnumerable(IEnumerable<T> items, int size)
            {
                _items = items;
                _size = size;
            }

            public IEnumerator<PooledResource<List<T>>> GetEnumerator()
            {
                return new PartitionPooledEnumerator<T>(_items.GetEnumerator(), _size);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private sealed class PartitionPooledEnumerator<T> : IEnumerator<PooledResource<List<T>>>
        {
            private readonly IEnumerator<T> _source;
            private readonly int _size;
            private readonly Dictionary<List<T>, PooledResource<List<T>>> _leases = new();
            private readonly Action<List<T>> _returnLeaseAction;
            private bool _disposed;

            public PartitionPooledEnumerator(IEnumerator<T> source, int size)
            {
                _source = source;
                _size = size;
                _returnLeaseAction = ReturnLease;
            }

            public PooledResource<List<T>> Current { get; private set; }

            object IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (_disposed)
                {
                    return false;
                }

                if (!_source.MoveNext())
                {
                    Current = default;
                    return false;
                }

                PooledResource<List<T>> pooled = Buffers<T>.GetList(_size, out List<T> partition);
                partition.Add(_source.Current);
                int count = 1;
                while (count < _size && _source.MoveNext())
                {
                    partition.Add(_source.Current);
                    count++;
                }

                _leases[partition] = pooled;
                Current = new PooledResource<List<T>>(partition, _returnLeaseAction);
                return true;
            }

            public void Reset()
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _source.Dispose();

                foreach (KeyValuePair<List<T>, PooledResource<List<T>>> lease in _leases)
                {
                    lease.Value.Dispose();
                }

                _leases.Clear();
            }

            private void ReturnLease(List<T> partition)
            {
                if (_leases.Remove(partition, out PooledResource<List<T>> pooled))
                {
                    pooled.Dispose();
                }
            }
        }
    }
}
