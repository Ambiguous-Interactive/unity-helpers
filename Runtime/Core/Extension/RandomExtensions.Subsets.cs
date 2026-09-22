// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using Random;
    using WallstopStudios.UnityHelpers.Utils;

    public static partial class RandomExtensions
    {
        /// <summary>
        /// Selects a random subset of items from a collection using reservoir sampling for uniform distribution.
        /// </summary>
        /// <typeparam name="T">The type of items in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="items">The collection to select from.</param>
        /// <param name="count">The number of items to select.</param>
        /// <returns>A deferred sequence of uniformly selected items.</returns>
        /// <exception cref="ArgumentNullException">Thrown if items is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if count is negative.</exception>
        /// <exception cref="ArgumentException">Thrown if count exceeds the number of items.</exception>
        /// <remarks>
        /// Null Handling: Throws ArgumentNullException if items is null. Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe and items is not modified during execution.
        /// Performance: O(n) in the number of source items. Materializes non-list collections.
        /// Allocations: Uses a pooled reservoir for the duration of enumeration.
        /// Edge Cases: count=0 returns empty without reading items. Uses Algorithm R (reservoir sampling) for uniform selection probability.
        /// A source that is not an <see cref="IReadOnlyList{T}"/> is copied into an owned array
        /// before sampling, because sampling is deferred. Queues, stacks, hash sets, and linked lists copy directly; other
        /// sources first grow a pooled list from the items they deliver.
        /// Disposing the enumerator returns the reservoir to the pool. A later enumeration draws
        /// a new sample.
        /// </remarks>
        public static IEnumerable<T> NextSubset<T>(
            this IRandom random,
            IEnumerable<T> items,
            int count
        )
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative");
            }

            if (count == 0)
            {
                return Array.Empty<T>();
            }

            if (items is IReadOnlyList<T> itemsList)
            {
                if (itemsList.Count < count)
                {
                    throw new ArgumentException(
                        "Count cannot exceed the number of items",
                        nameof(count)
                    );
                }

                return NextSubsetIterator(random, itemsList, count);
            }

            if (items is Queue<T> queue)
            {
                if (queue.Count < count)
                {
                    throw new ArgumentException(
                        "Count cannot exceed the number of items",
                        nameof(count)
                    );
                }

                T[] snapshot = new T[queue.Count];
                queue.CopyTo(snapshot, 0);
                return NextSubsetIterator(random, snapshot, count);
            }

            if (items is Stack<T> stack)
            {
                if (stack.Count < count)
                {
                    throw new ArgumentException(
                        "Count cannot exceed the number of items",
                        nameof(count)
                    );
                }

                T[] snapshot = new T[stack.Count];
                stack.CopyTo(snapshot, 0);
                return NextSubsetIterator(random, snapshot, count);
            }

            if (items is HashSet<T> set)
            {
                if (set.Count < count)
                {
                    throw new ArgumentException(
                        "Count cannot exceed the number of items",
                        nameof(count)
                    );
                }

                T[] snapshot = new T[set.Count];
                set.CopyTo(snapshot, 0);
                return NextSubsetIterator(random, snapshot, count);
            }

            if (items is LinkedList<T> linkedList)
            {
                if (linkedList.Count < count)
                {
                    throw new ArgumentException(
                        "Count cannot exceed the number of items",
                        nameof(count)
                    );
                }

                T[] snapshot = new T[linkedList.Count];
                linkedList.CopyTo(snapshot, 0);
                return NextSubsetIterator(random, snapshot, count);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            foreach (T item in items)
            {
                materializedList.Add(item);
            }

            if (materializedList.Count < count)
            {
                throw new ArgumentException(
                    "Count cannot exceed the number of items",
                    nameof(count)
                );
            }

            // The deferred iterator outlives this pool lease; give it an owned copy.
            return NextSubsetIterator(random, materializedList.ToArray(), count);
        }

        private static IEnumerable<T> NextSubsetIterator<T>(
            IRandom random,
            IReadOnlyList<T> items,
            int count
        )
        {
            using PooledArray<T> arrayBuffer = SystemArrayPool<T>.Get(count, out T[] result);

            for (int i = 0; i < count; ++i)
            {
                result[i] = items[i];
            }

            for (int i = count; i < items.Count; ++i)
            {
                int j = random.Next(0, i + 1);
                if (j < count)
                {
                    result[j] = items[i];
                }
            }

            for (int i = 0; i < count; ++i)
            {
                yield return result[i];
            }
        }
    }
}
