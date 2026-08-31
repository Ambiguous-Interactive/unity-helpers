// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using DataStructure.Adapters;
    using Helper;
    using Random;
    using Utils;

    /// <summary>
    /// Extension methods for IList providing shuffling, shifting, sorting, searching, and element manipulation.
    /// </summary>
    /// <remarks>
    /// Thread Safety: Methods are not thread-safe and modify lists in-place unless noted otherwise.
    /// Performance: Methods are optimized for performance with minimal allocations.
    /// Most operations work directly on the list without creating copies.
    /// </remarks>
    public static partial class IListExtensions
    {
        /// <summary>
        /// Randomly shuffles the elements of a list in-place using the Fisher-Yates algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to shuffle.</param>
        /// <param name="random">The random number generator to use. If null, uses PRNG.Instance.</param>
        /// <remarks>
        /// <para>Null handling: If list is null, returns immediately. If random is null, uses PRNG.Instance.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. If random is shared, may not be thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. A list that is not already a
        /// <c>T[]</c> is shuffled through a pooled array, which is 1.5x to 2x faster than swapping
        /// through the indexer.</para>
        /// <para>Allocations: the scratch array comes from a pool. A list taking the bulk write-back
        /// path boxes one <see cref="ArraySegment{T}"/> per <c>AddRange</c>, because
        /// <see cref="List{T}"/> declares no <see cref="ICollection{T}"/> overload on this profile.</para>
        /// <para>Edge cases: Lists with 0 or 1 elements are not modified.</para>
        /// <para>Random draws: exactly <c>Count - 1</c> calls to <c>random.Next(i, Count)</c>,
        /// ascending in <c>i</c>. The permutation is the shape of the collection's business, not the
        /// container's: every container of the same length shuffles identically from the same seed.</para>
        /// </remarks>
        /// <seealso cref="SpanExtensions.Shuffle{T}(Span{T}, IRandom)"/>
        public static void Shuffle<T>(this IList<T> list, IRandom random = null)
        {
            if (list is not { Count: > 1 })
            {
                return;
            }

            random ??= PRNG.Instance;

            int count = list.Count;
            // The exact-type test is not redundant: a covariant array is not a Span<T>.
            // string[] used as IList<object> passes `is object[]`, and Span<T>'s array constructor
            // then throws ArrayTypeMismatchException. Falling through rents an exact T[] instead.
            if (list is T[] array && array.GetType() == typeof(T[]))
            {
                SpanExtensions.Shuffle(array.AsSpan(0, count), random);
                return;
            }

            using PooledArray<T> lease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            SpanExtensions.Shuffle(scratch.AsSpan(0, count), random);
            WriteBack(list, scratch, count);
        }

        /// <summary>
        /// Shifts (rotates) the elements of a list by the specified amount.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to shift.</param>
        /// <param name="amount">The number of positions to shift. Positive shifts right, negative shifts left.</param>
        /// <remarks>
        /// <para>Null handling: If list is null, returns immediately.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. A <c>T[]</c> is rotated by
        /// three reversals through <see cref="SpanExtensions.Shift{T}(Span{T}, int)"/>, which is the
        /// same body a span caller reaches directly; anything else is copied into a pooled array
        /// whose two runs are written back in order, so nothing is reversed at all. Measured 2.7x to
        /// 36x faster than three reversals through the indexer.</para>
        /// <para>Allocations: the scratch array comes from a pool. A list taking the bulk write-back
        /// path boxes one <see cref="ArraySegment{T}"/> per <c>AddRange</c>, because
        /// <see cref="List{T}"/> declares no <see cref="ICollection{T}"/> overload on this profile.</para>
        /// <para>Edge cases: Lists with 0 or 1 elements are not modified. Amount is normalized using modulo. Amount of 0 or multiples of count result in no change.</para>
        /// </remarks>
        public static void Shift<T>(this IList<T> list, int amount)
        {
            if (list is not { Count: > 1 })
            {
                return;
            }

            int count = list.Count;
            amount = amount.PositiveMod(count);
            if (amount == 0)
            {
                return;
            }

            /*
                The exact-type test is not redundant, for the reason Shuffle gives: a covariant
                array is not a Span<T>, and Span<T>'s array constructor throws
                ArrayTypeMismatchException on one. Falling through rents an exact T[] instead.
            */
            if (list is T[] array && array.GetType() == typeof(T[]))
            {
                SpanExtensions.Shift(array.AsSpan(0, count), amount);
                return;
            }

            using PooledArray<T> lease = SystemArrayPool<T>.Get(count, out T[] scratch);
            list.CopyTo(scratch, 0);
            WriteBackRotated(list, scratch, count - amount, count);
        }

        /// <summary>
        /// Reverses the elements in a list within the specified range in-place.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to reverse a portion of.</param>
        /// <param name="start">The starting index (inclusive).</param>
        /// <param name="end">The ending index (inclusive).</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements in the range (end - start + 1).
        /// A <c>T[]</c> and a <see cref="List{T}"/> both carry a bulk reverse, which measures 26x to
        /// 37x faster than swapping through the indexer; nothing is copied on any path.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: If start equals end, no change occurs. If start greater than end, no change occurs.</para>
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown when start or end are out of range [0, Count).</exception>
        public static void Reverse<T>(this IList<T> list, int start, int end)
        {
            if (start < 0 || list.Count <= start)
            {
                throw new ArgumentException(nameof(start));
            }
            if (end < 0 || list.Count <= end)
            {
                throw new ArgumentException(nameof(end));
            }

            int length = end - start + 1;
            if (length <= 1)
            {
                return;
            }

            switch (list)
            {
                case T[] array:
                {
                    Array.Reverse(array, start, length);
                    return;
                }
                case List<T> concrete:
                {
                    concrete.Reverse(start, length);
                    return;
                }
                default:
                {
                    while (start < end)
                    {
                        (list[start], list[end]) = (list[end], list[start]);
                        start++;
                        end--;
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// Removes an element at the specified index by swapping it with the last element, then removing the last element.
        /// This is faster than regular RemoveAt but does not preserve order.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to remove an element from.</param>
        /// <param name="index">The index of the element to remove.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(1) regardless of list size or index position.</para>
        /// <para>Allocations: No allocations beyond what RemoveAt might allocate.</para>
        /// <para>Edge cases: If index is the last element, behaves like normal RemoveAt. Does not preserve element order. An out-of-range index leaves the list untouched.</para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is
        /// outside the valid range [0, Count), matching <see cref="Swap{T}"/> and
        /// <see cref="Reverse{T}(IList{T}, int, int)"/>. The list is not modified.</exception>
        public static void RemoveAtSwapBack<T>(this IList<T> list, int index)
        {
            int count = list.Count;
            if (index < 0 || count <= index)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int lastIndex = count - 1;
            if (index == lastIndex)
            {
                list.RemoveAt(index);
                return;
            }

            list[index] = list[lastIndex];
            list.RemoveAt(lastIndex);
        }

        /// <summary>
        /// Swaps two elements in the list at the specified indices.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list containing the elements to swap.</param>
        /// <param name="indexA">The index of the first element.</param>
        /// <param name="indexB">The index of the second element.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(1).</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: If indexA equals indexB, no swap occurs.</para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when indexA or indexB are outside the valid range [0, Count).</exception>
        public static void Swap<T>(this IList<T> list, int indexA, int indexB)
        {
            if (indexA < 0 || list.Count <= indexA)
            {
                throw new ArgumentOutOfRangeException(nameof(indexA));
            }
            if (indexB < 0 || list.Count <= indexB)
            {
                throw new ArgumentOutOfRangeException(nameof(indexB));
            }

            if (indexA == indexB)
            {
                return;
            }

            (list[indexA], list[indexB]) = (list[indexB], list[indexA]);
        }

        /// <summary>
        /// Fills all elements in the list with the specified value.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to fill.</param>
        /// <param name="value">The value to assign to all elements.</param>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if list is null. Value can be null if T is nullable.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. A list that offers bulk
        /// replacement is filled through a pooled array, which measures 3.6x to 38x faster than
        /// assigning through the indexer.</para>
        /// <para>Allocations: the scratch array comes from a pool. A list taking the bulk write-back
        /// path boxes one <see cref="ArraySegment{T}"/> per <c>AddRange</c>, because
        /// <see cref="List{T}"/> declares no <see cref="ICollection{T}"/> overload on this profile.</para>
        /// <para>Edge cases: Empty lists are not modified.</para>
        /// </remarks>
        public static void Fill<T>(this IList<T> list, T value)
        {
            int count = list.Count;
            if (count <= 0)
            {
                return;
            }

            if (list is T[] array)
            {
                Array.Fill(array, value, 0, count);
                return;
            }

            if (list is List<T> or SerializableList<T>)
            {
                using PooledArray<T> lease = SystemArrayPool<T>.Get(count, out T[] scratch);
                Array.Fill(scratch, value, 0, count);
                WriteBack(list, scratch, count);
                return;
            }

            for (int i = 0; i < count; ++i)
            {
                list[i] = value;
            }
        }

        /// <summary>
        /// Fills all elements in the list using a factory function that receives the element index.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to fill.</param>
        /// <param name="factory">A function that takes an index and returns the value for that position.</param>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if factory is null. Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements, plus the cost of factory invocations.</para>
        /// <para>Allocations: Allocations depend on factory function behavior.</para>
        /// <para>Edge cases: Empty lists result in no factory invocations.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when factory is null.</exception>
        public static void Fill<T>(this IList<T> list, Func<int, T> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            // Deliberately re-read: the factory can mutate the list, and a hoisted bound would
            // index past the end of a shorter one rather than stopping at it.
            for (int i = 0; i < list.Count; ++i)
            {
                list[i] = factory(i);
            }
        }

        /// <summary>
        /// Finds the index of the first element that matches the specified predicate.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to search.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns>The index of the first matching element, or -1 if no match is found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if predicate is null. Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. Short-circuits on first match.
        /// A <c>T[]</c> is scanned by direct indexing, which measures 2.5x to 3.1x faster than the
        /// interface indexer. A search that can stop early is never copied into a pooled array: a
        /// match at the first element would then have paid for reading every other one.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns -1 if no matching element is found. Empty lists always return -1.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when predicate is null.</exception>
        public static int IndexOf<T>(this IList<T> list, Func<T, bool> predicate)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            int count = list.Count;
            if (list is T[] array)
            {
                for (int i = 0; i < count; ++i)
                {
                    if (predicate(array[i]))
                    {
                        return i;
                    }
                }

                return -1;
            }

            // Deliberately re-read: the predicate can mutate the list. An array cannot change length
            // under the branch above, so that one hoists.
            for (int i = 0; i < list.Count; ++i)
            {
                if (predicate(list[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Finds the index of the last element that matches the specified predicate.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to search.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns>The index of the last matching element, or -1 if no match is found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if predicate is null. Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. Searches from end to beginning,
        /// short-circuits on first match. A <c>T[]</c> is scanned by direct indexing; a search that
        /// can stop early is never copied into a pooled array.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns -1 if no matching element is found. Empty lists always return -1.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when predicate is null.</exception>
        public static int LastIndexOf<T>(this IList<T> list, Func<T, bool> predicate)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            // Evaluated once on both paths, as it always was: a backwards scan fixes its start
            // index before the first predicate runs, so there is no bound left to re-read.
            int count = list.Count;
            if (list is T[] array)
            {
                for (int i = count - 1; 0 <= i; --i)
                {
                    if (predicate(array[i]))
                    {
                        return i;
                    }
                }

                return -1;
            }

            for (int i = count - 1; 0 <= i; --i)
            {
                if (predicate(list[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Returns all elements in the list that match the specified predicate.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to search.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns>A new List containing all matching elements.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if predicate is null. Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements.</para>
        /// <para>Allocations: Allocates a new List. Size depends on number of matching elements.</para>
        /// <para>Edge cases: Returns empty list if no matches found.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when predicate is null.</exception>
        public static List<T> FindAll<T>(this IList<T> list, Func<T, bool> predicate)
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            List<T> result = new();
            for (int i = 0; i < list.Count; ++i)
            {
                T element = list[i];
                if (predicate(element))
                {
                    result.Add(element);
                }
            }

            return result;
        }

        /// <summary>
        /// Adds the elements of the specified collection to the end of the list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to add items to.</param>
        /// <param name="items">The collection whose elements should be added.</param>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if items is null. Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(m) where m is the number of items to add. Optimized for List&lt;T&gt; using AddRange.</para>
        /// <para>Allocations: May allocate if list needs to grow capacity.</para>
        /// <para>Edge cases: Empty items collection adds nothing to the list.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when items is null.</exception>
        public static void AddRange<T>(this IList<T> list, IEnumerable<T> items)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (list is List<T> concreteList)
            {
                concreteList.AddRange(items);
                return;
            }

            foreach (T item in items)
            {
                list.Add(item);
            }
        }

        /// <summary>
        /// Rotates the list elements to the left by the specified number of positions.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to rotate.</param>
        /// <param name="positions">The number of positions to rotate left. Defaults to 1.</param>
        /// <remarks>
        /// <para>Null handling: If list is null, returns immediately (delegated to Shift).</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. Delegates to Shift.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: See Shift method for edge cases.</para>
        /// </remarks>
        public static void RotateLeft<T>(this IList<T> list, int positions = 1)
        {
            list.Shift(-positions);
        }

        /// <summary>
        /// Rotates the list elements to the right by the specified number of positions.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to rotate.</param>
        /// <param name="positions">The number of positions to rotate right. Defaults to 1.</param>
        /// <remarks>
        /// <para>Null handling: If list is null, returns immediately (delegated to Shift).</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. Delegates to Shift.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: See Shift method for edge cases.</para>
        /// </remarks>
        public static void RotateRight<T>(this IList<T> list, int positions = 1)
        {
            list.Shift(positions);
        }

        /// <summary>
        /// Partitions the list into two lists based on a predicate: elements that match and elements that don't.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to partition.</param>
        /// <param name="predicate">The function to test each element.</param>
        /// <returns>A tuple containing two lists: matching elements and non-matching elements.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if predicate is null. Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements.</para>
        /// <para>Allocations: Allocates two new Lists. Total size equals original list size.</para>
        /// <para>Edge cases: One of the returned lists may be empty if all elements match or none match.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when predicate is null.</exception>
        public static (List<T> matching, List<T> notMatching) Partition<T>(
            this IList<T> list,
            Func<T, bool> predicate
        )
        {
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }

            List<T> matching = new();
            List<T> notMatching = new();

            for (int i = 0; i < list.Count; ++i)
            {
                T element = list[i];
                if (predicate(element))
                {
                    matching.Add(element);
                }
                else
                {
                    notMatching.Add(element);
                }
            }

            return (matching, notMatching);
        }

        /// <summary>
        /// Removes and returns the last element of the list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to pop from.</param>
        /// <returns>The last element of the list.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(1) for most list implementations.</para>
        /// <para>Allocations: No allocations beyond what RemoveAt might allocate.</para>
        /// <para>Edge cases: Throws InvalidOperationException if list is empty.</para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when attempting to pop from an empty list.</exception>
        public static T PopBack<T>(this IList<T> list)
        {
            if (list.Count == 0)
            {
                throw new InvalidOperationException("Cannot pop from empty list");
            }

            int lastIndex = list.Count - 1;
            T item = list[lastIndex];
            list.RemoveAt(lastIndex);
            return item;
        }

        /// <summary>
        /// Removes and returns the first element of the list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to pop from.</param>
        /// <returns>The first element of the list.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if list is null.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the list in place. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) for most list implementations due to element shifting.</para>
        /// <para>Allocations: No allocations beyond what RemoveAt might allocate.</para>
        /// <para>Edge cases: Throws InvalidOperationException if list is empty. Expensive for large lists due to element shifting.</para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when attempting to pop from an empty list.</exception>
        public static T PopFront<T>(this IList<T> list)
        {
            if (list.Count == 0)
            {
                throw new InvalidOperationException("Cannot pop from empty list");
            }

            T item = list[0];
            list.RemoveAt(0);
            return item;
        }

        /// <summary>
        /// Returns a random element from the list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to get a random element from.</param>
        /// <param name="random">The random number generator to use. If null, uses PRNG.Instance.</param>
        /// <returns>A randomly selected element from the list.</returns>
        /// <remarks>
        /// <para>Null handling: Throws NullReferenceException if list is null. If random is null, uses PRNG.Instance.</para>
        /// <para>Thread safety: Thread-safe for read-only access. If random is shared, may not be thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(1).</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Throws InvalidOperationException if list is empty.</para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Thrown when attempting to get random element from an empty list.</exception>
        [Pure]
        public static T GetRandomElement<T>(this IList<T> list, IRandom random = null)
        {
            if (list.Count == 0)
            {
                throw new InvalidOperationException("Cannot get random element from empty list");
            }

            random ??= PRNG.Instance;
            return list[random.Next(0, list.Count)];
        }
    }
}
