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
        /// Randomly selects an element from a collection with no exclusions.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <returns>A randomly selected element from values.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(1) for lists/arrays, O(n) for general enumerables.
        /// Allocations: Zero allocation for this overload. Materializes non-list/collection enumerables to pooled list.
        /// Edge Cases: Empty values collection will cause NextOf to fail.
        /// </remarks>
        public static T NextOfExcept<T>(this IRandom random, IEnumerable<T> values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            return random.NextOf(values);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding one specified value.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exception1">The value to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not the excluded value.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(n) where n is collection size.
        /// Allocations: Zero allocation - uses pooled collections internally.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will fail.
        /// </remarks>
        public static T NextOfExcept<T>(this IRandom random, IEnumerable<T> values, T exception1)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exception1);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exception1);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding two specified values.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exception1">The first value to exclude from selection.</param>
        /// <param name="exception2">The second value to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not one of the excluded values.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(n) where n is collection size.
        /// Allocations: Zero allocation - uses pooled collections internally.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will fail.
        /// </remarks>
        public static T NextOfExcept<T>(
            this IRandom random,
            IEnumerable<T> values,
            T exception1,
            T exception2
        )
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exception1, exception2);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exception1, exception2);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding three specified values.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exception1">The first value to exclude from selection.</param>
        /// <param name="exception2">The second value to exclude from selection.</param>
        /// <param name="exception3">The third value to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not one of the excluded values.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(n) where n is collection size.
        /// Allocations: Zero allocation - uses pooled collections internally.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will fail.
        /// </remarks>
        public static T NextOfExcept<T>(
            this IRandom random,
            IEnumerable<T> values,
            T exception1,
            T exception2,
            T exception3
        )
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exception1, exception2, exception3);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exception1, exception2, exception3);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding specified exception values via IEnumerable.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exceptions">An enumerable of values to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not in exceptions.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null. Null exceptions treated as empty.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(n*k) where n is collection size and k is exceptions count.
        /// Allocations: Uses pooled collections internally. Does not allocate params array.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will fail.
        /// </remarks>
        public static T NextOfExcept<T>(
            this IRandom random,
            IEnumerable<T> values,
            IEnumerable<T> exceptions
        )
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exceptions);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exceptions);
        }

        /// <summary>
        /// Randomly selects an element from a collection, excluding specified exception values.
        /// </summary>
        /// <typeparam name="T">The type of elements in the collection.</typeparam>
        /// <param name="random">The random number generator to use.</param>
        /// <param name="values">The collection to select from.</param>
        /// <param name="exceptions">Values to exclude from selection.</param>
        /// <returns>A randomly selected element from values that is not in exceptions.</returns>
        /// <remarks>
        /// Null Handling: Will throw NullReferenceException if random or values is null.
        /// Thread Safety: Thread-safe if random is thread-safe and values is not modified during execution.
        /// Performance: O(k*n) worst case where k is number of exceptions and n is selection attempts.
        /// Allocations: This params overload allocates an array on each call. Prefer the specific 0-3 arg overloads
        /// or the IEnumerable overload for zero-allocation hot paths.
        /// Edge Cases: Throws if all values are excluded. Empty values collection will cause NextOf to fail.
        /// </remarks>
        public static T NextOfExcept<T>(
            this IRandom random,
            IEnumerable<T> values,
            params T[] exceptions
        )
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values is IReadOnlyList<T> source)
            {
                return NextOfExceptCore(random, source, exceptions);
            }

            using PooledResource<List<T>> lease = Buffers<T>.List.Get(out List<T> materializedList);
            materializedList.AddRange(values);

            return NextOfExceptCore(random, materializedList, exceptions);
        }

        private static T NextOfExceptCore<T>(IRandom random, IReadOnlyList<T> source, T exception1)
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (!comparer.Equals(v, exception1))
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exception1));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }

        private static T NextOfExceptCore<T>(
            IRandom random,
            IReadOnlyList<T> source,
            T exception1,
            T exception2
        )
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (!comparer.Equals(v, exception1) && !comparer.Equals(v, exception2))
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exception1));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }

        private static T NextOfExceptCore<T>(
            IRandom random,
            IReadOnlyList<T> source,
            T exception1,
            T exception2,
            T exception3
        )
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (
                    !comparer.Equals(v, exception1)
                    && !comparer.Equals(v, exception2)
                    && !comparer.Equals(v, exception3)
                )
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exception1));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }

        private static T NextOfExceptCore<T>(
            IRandom random,
            IReadOnlyList<T> source,
            IEnumerable<T> exceptions
        )
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            if (exceptions == null)
            {
                return random.NextOf(source);
            }

            using PooledResource<HashSet<T>> excludeLease = Buffers<T>.HashSet.Get(
                out HashSet<T> exclude
            );

            if (exceptions is IReadOnlyList<T> exceptionList)
            {
                for (int i = 0; i < exceptionList.Count; ++i)
                {
                    exclude.Add(exceptionList[i]);
                }
            }
            else
            {
                foreach (T exception in exceptions)
                {
                    exclude.Add(exception);
                }
            }

            if (exclude.Count == 0)
            {
                return random.NextOf(source);
            }

            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (!exclude.Contains(v))
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exceptions));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }

        private static T NextOfExceptCore<T>(
            IRandom random,
            IReadOnlyList<T> source,
            T[] exceptions
        )
        {
            if (source.Count == 0)
            {
                throw new ArgumentException("Collection cannot be empty", nameof(source));
            }

            if (exceptions == null || exceptions.Length == 0)
            {
                return random.NextOf(source);
            }

            using PooledResource<HashSet<T>> excludeLease = Buffers<T>.HashSet.Get(
                out HashSet<T> exclude
            );
            foreach (T exception in exceptions)
            {
                exclude.Add(exception);
            }

            using PooledArray<T> pooled = SystemArrayPool<T>.Get(source.Count, out T[] buffer);
            int n = 0;
            for (int i = 0; i < source.Count; ++i)
            {
                T v = source[i];
                if (!exclude.Contains(v))
                {
                    buffer[n++] = v;
                }
            }

            if (n == 0)
            {
                throw new ArgumentException("All values are excluded", nameof(exceptions));
            }

            return n == 1 ? buffer[0] : buffer[random.Next(n)];
        }
    }
}
