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
        /// <returns>An array containing 'count' randomly selected items from the collection, with uniform probability.</returns>
        /// <exception cref="ArgumentNullException">Thrown if items is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if count is negative.</exception>
        /// <exception cref="ArgumentException">Thrown if count exceeds the number of items.</exception>
        /// <remarks>
        /// Null Handling: Throws ArgumentNullException if items is null. Will throw NullReferenceException if random is null.
        /// Thread Safety: Thread-safe if random is thread-safe and items is not modified during execution.
        /// Performance: O(n) where n is items.Count - uses reservoir sampling algorithm. Materializes non-list collections.
        /// Allocations: Uses pooled array for result (returned to pool when disposed). Materializes IEnumerable to array/list.
        /// Edge Cases: count=0 returns empty without reading items. Uses Algorithm R (reservoir sampling) for uniform selection probability.
        /// The returned array is pooled and will be returned to the pool - caller should not hold reference long-term.
        /// A source that is not an <see cref="IReadOnlyList{T}"/> is copied once into an array this
        /// method owns, because the sampling itself is deferred and a pooled staging buffer would
        /// be back in the pool before the first element is read.
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

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(items);

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
