// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Extension methods for IReadOnlyList providing search, slice, and utility operations.
    /// </summary>
    /// <remarks>
    /// Thread Safety: All methods are thread-safe for read-only access when the underlying collection is not modified.
    /// Performance: Methods are optimized for common list types (T[], List&lt;T&gt;) with specialized implementations.
    /// Allocations: Most methods avoid allocations; slice operations create new arrays.
    /// </remarks>
    public static class IReadonlyListExtensions
    {
        /// <summary>
        /// Searches for the specified element and returns the zero-based index of the first occurrence.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <returns>The zero-based index of the first occurrence of element, or -1 if not found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the list count. Optimized for T[] and List&lt;T&gt; using built-in methods.</para>
        /// <para>Allocations: No allocations if EqualityComparer&lt;T&gt;.Default is used (delegates to overload).</para>
        /// <para>Edge cases: Returns -1 if element not found. Empty lists return -1.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static int IndexOf<T>(this IReadOnlyList<T> readonlyList, T element)
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            return readonlyList.IndexOf(element, 0, readonlyList.Count, null);
        }

        /// <summary>
        /// Searches for the specified element and returns the zero-based index of the first occurrence starting at the specified index.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <param name="startIndex">The zero-based starting index of the search.</param>
        /// <returns>The zero-based index of the first occurrence of element at or after startIndex, or -1 if not found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements from startIndex to end. Optimized for T[] and List&lt;T&gt;.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns -1 if element not found. StartIndex at or beyond list length returns -1.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static int IndexOf<T>(this IReadOnlyList<T> readonlyList, T element, int startIndex)
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            int length = readonlyList.Count;
            return readonlyList.IndexOf(element, startIndex, length - startIndex, null);
        }

        /// <summary>
        /// Searches for the specified element and returns the zero-based index of the first occurrence within the range of elements.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <param name="startIndex">The zero-based starting index of the search.</param>
        /// <param name="count">The number of elements to search.</param>
        /// <returns>The zero-based index of the first occurrence of element within the range, or -1 if not found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(count). Optimized for T[] and List&lt;T&gt;.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns -1 if element not found. Delegates to overload with null comparer.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when startIndex or count are out of valid range.</exception>
        public static int IndexOf<T>(
            this IReadOnlyList<T> readonlyList,
            T element,
            int startIndex,
            int count
        )
        {
            return readonlyList.IndexOf(element, startIndex, count, null);
        }

        /// <summary>
        /// Searches for the specified element using a custom comparer and returns the zero-based index of the first occurrence within the range.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <param name="startIndex">The zero-based starting index of the search.</param>
        /// <param name="count">The number of elements to search.</param>
        /// <param name="comparer">The equality comparer to use. If null, uses EqualityComparer&lt;T&gt;.Default.</param>
        /// <returns>The zero-based index of the first occurrence of element within the range, or -1 if not found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null. Comparer defaults to EqualityComparer&lt;T&gt;.Default if null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(count). Optimized for T[] and List&lt;T&gt; when using default comparer.</para>
        /// <para>Allocations: No allocations if using default comparer, otherwise minimal allocations.</para>
        /// <para>Edge cases: Returns -1 if element not found. Validates segment bounds before searching.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when startIndex or count are out of valid range.</exception>
        public static int IndexOf<T>(
            this IReadOnlyList<T> readonlyList,
            T element,
            int startIndex,
            int count,
            IEqualityComparer<T> comparer
        )
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            int length = readonlyList.Count;
            ValidateForwardSegment(length, startIndex, count);

            IEqualityComparer<T> defaultComparer = EqualityComparer<T>.Default;
            comparer ??= defaultComparer;
            bool useDefaultComparer = ReferenceEquals(comparer, defaultComparer);

            switch (readonlyList)
            {
                case T[] array when useDefaultComparer:
                {
                    return Array.IndexOf(array, element, startIndex, count);
                }

                case List<T> list when useDefaultComparer:
                {
                    return list.IndexOf(element, startIndex, count);
                }
            }

            int endExclusive = startIndex + count;
            for (int i = startIndex; i < endExclusive; ++i)
            {
                if (comparer.Equals(readonlyList[i], element))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Searches for the specified element and returns the zero-based index of the last occurrence.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <returns>The zero-based index of the last occurrence of element, or -1 if not found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. Optimized for T[] and List&lt;T&gt;.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns -1 if element not found or list is empty.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static int LastIndexOf<T>(this IReadOnlyList<T> readonlyList, T element)
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            int length = readonlyList.Count;
            if (length == 0)
            {
                return -1;
            }

            return readonlyList.LastIndexOf(element, length - 1, length, null);
        }

        /// <summary>
        /// Searches backward for the specified element starting at the specified index.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <param name="startIndex">The zero-based starting index of the backward search.</param>
        /// <returns>The zero-based index of the last occurrence of element at or before startIndex, or -1 if not found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is startIndex + 1. Searches backward from startIndex.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns -1 if element not found. StartIndex is inclusive.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static int LastIndexOf<T>(
            this IReadOnlyList<T> readonlyList,
            T element,
            int startIndex
        )
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            return readonlyList.LastIndexOf(element, startIndex, startIndex + 1, null);
        }

        /// <summary>
        /// Searches backward for the specified element within a range.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <param name="startIndex">The zero-based starting index of the backward search.</param>
        /// <param name="count">The number of elements to search backward.</param>
        /// <returns>The zero-based index of the last occurrence of element within the range, or -1 if not found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(count). Searches backward from startIndex for count elements.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns -1 if element not found. Delegates to overload with null comparer.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when startIndex or count are out of valid range.</exception>
        public static int LastIndexOf<T>(
            this IReadOnlyList<T> readonlyList,
            T element,
            int startIndex,
            int count
        )
        {
            return readonlyList.LastIndexOf(element, startIndex, count, null);
        }

        /// <summary>
        /// Searches backward for the specified element using a custom comparer within a range.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <param name="startIndex">The zero-based starting index of the backward search.</param>
        /// <param name="count">The number of elements to search backward.</param>
        /// <param name="comparer">The equality comparer to use. If null, uses EqualityComparer&lt;T&gt;.Default.</param>
        /// <returns>The zero-based index of the last occurrence of element within the range, or -1 if not found.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null. Comparer defaults to EqualityComparer&lt;T&gt;.Default if null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(count). Optimized for T[] and List&lt;T&gt; when using default comparer.</para>
        /// <para>Allocations: No allocations if using default comparer.</para>
        /// <para>Edge cases: Returns -1 if element not found or count is 0. Validates segment bounds before searching.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when startIndex or count are out of valid range.</exception>
        public static int LastIndexOf<T>(
            this IReadOnlyList<T> readonlyList,
            T element,
            int startIndex,
            int count,
            IEqualityComparer<T> comparer
        )
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            int length = readonlyList.Count;
            ValidateBackwardSegment(length, startIndex, count);
            if (count == 0)
            {
                return -1;
            }

            IEqualityComparer<T> defaultComparer = EqualityComparer<T>.Default;
            comparer ??= defaultComparer;
            bool useDefaultComparer = ReferenceEquals(comparer, defaultComparer);

            switch (readonlyList)
            {
                case T[] array when useDefaultComparer:
                {
                    return Array.LastIndexOf(array, element, startIndex, count);
                }

                case List<T> list when useDefaultComparer:
                {
                    return list.LastIndexOf(element, startIndex, count);
                }
            }

            int segmentStart = startIndex - count + 1;
            for (int i = startIndex; segmentStart <= i; --i)
            {
                if (comparer.Equals(readonlyList[i], element))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Determines whether the list contains the specified element.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <returns>True if the element is found; otherwise, false.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null (delegated to overload).</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. Delegates to IndexOf.</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns false for empty lists or if element is not found.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static bool Contains<T>(this IReadOnlyList<T> readonlyList, T element)
        {
            return readonlyList.Contains(element, null);
        }

        /// <summary>
        /// Determines whether the list contains the specified element using a custom comparer.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to search.</param>
        /// <param name="element">The element to locate.</param>
        /// <param name="comparer">The equality comparer to use. If null, uses EqualityComparer&lt;T&gt;.Default.</param>
        /// <returns>True if the element is found; otherwise, false.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(n) where n is the number of elements. Delegates to IndexOf.</para>
        /// <para>Allocations: No allocations if using default comparer.</para>
        /// <para>Edge cases: Returns false for empty lists or if element is not found.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static bool Contains<T>(
            this IReadOnlyList<T> readonlyList,
            T element,
            IEqualityComparer<T> comparer
        )
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            return 0 <= readonlyList.IndexOf(element, 0, readonlyList.Count, comparer);
        }

        /// <summary>
        /// Attempts to get the element at the specified index.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to access.</param>
        /// <param name="index">The zero-based index of the element to get.</param>
        /// <param name="value">When this method returns, contains the element at the specified index if successful; otherwise, the default value for type T.</param>
        /// <returns>True if the index is within bounds; otherwise, false.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(1).</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns false and sets value to default(T) if index is out of bounds. Uses unsigned cast for bounds check optimization.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static bool TryGetElementAt<T>(
            this IReadOnlyList<T> readonlyList,
            int index,
            out T value
        )
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            if ((uint)readonlyList.Count <= (uint)index)
            {
                value = default;
                return false;
            }

            value = readonlyList[index];
            return true;
        }

        /// <summary>
        /// Attempts to get the first element of the list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to access.</param>
        /// <param name="value">When this method returns, contains the first element if the list is not empty; otherwise, the default value for type T.</param>
        /// <returns>True if the list is not empty; otherwise, false.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(1).</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns false and sets value to default(T) if list is empty.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static bool TryGetFirst<T>(this IReadOnlyList<T> readonlyList, out T value)
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            if (readonlyList.Count == 0)
            {
                value = default;
                return false;
            }

            value = readonlyList[0];
            return true;
        }

        /// <summary>
        /// Attempts to get the last element of the list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to access.</param>
        /// <param name="value">When this method returns, contains the last element if the list is not empty; otherwise, the default value for type T.</param>
        /// <returns>True if the list is not empty; otherwise, false.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(1).</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Returns false and sets value to default(T) if list is empty.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static bool TryGetLast<T>(this IReadOnlyList<T> readonlyList, out T value)
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            int length = readonlyList.Count;
            if (length == 0)
            {
                value = default;
                return false;
            }

            value = readonlyList[length - 1];
            return true;
        }

        /// <summary>
        /// Determines whether the readonly list is null or empty.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The list to check.</param>
        /// <returns>True if the list is null or has a count of zero; otherwise, false.</returns>
        /// <remarks>
        /// <para>Null handling: Returns true if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. No Unity main thread requirement.</para>
        /// <para>Performance: O(1).</para>
        /// <para>Allocations: No allocations.</para>
        /// <para>Edge cases: Null lists are considered empty.</para>
        /// </remarks>
        public static bool IsNullOrEmpty<T>(this IReadOnlyList<T> readonlyList)
        {
            return readonlyList == null || readonlyList.Count == 0;
        }

        /// <summary>
        /// Searches a sorted readonly list for a value using binary search.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The sorted list to search.</param>
        /// <param name="value">The value to search for.</param>
        /// <returns>The index of the value if found; otherwise, the bitwise complement of the index where it should be inserted.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(log n) where n is the number of elements. List must be pre-sorted.</para>
        /// <para>Allocations: No allocations if using default comparer (delegates to overload).</para>
        /// <para>Edge cases: List must be sorted. Returns bitwise complement of insertion point if not found. Use ~result to get insertion index.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static int BinarySearch<T>(this IReadOnlyList<T> readonlyList, T value)
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            return readonlyList.BinarySearch(0, readonlyList.Count, value, null);
        }

        /// <summary>
        /// Searches a sorted readonly list for a value using binary search with a custom comparer.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The sorted list to search.</param>
        /// <param name="value">The value to search for.</param>
        /// <param name="comparer">The comparer to use. If null, uses Comparer&lt;T&gt;.Default.</param>
        /// <returns>The index of the value if found; otherwise, the bitwise complement of the index where it should be inserted.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null. Comparer defaults to Comparer&lt;T&gt;.Default if null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(log n) where n is the number of elements. List must be pre-sorted according to comparer.</para>
        /// <para>Allocations: No allocations if comparer is provided, otherwise allocates default comparer.</para>
        /// <para>Edge cases: List must be sorted according to comparer. Returns bitwise complement of insertion point if not found.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        public static int BinarySearch<T>(
            this IReadOnlyList<T> readonlyList,
            T value,
            IComparer<T> comparer
        )
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            return readonlyList.BinarySearch(0, readonlyList.Count, value, comparer);
        }

        /// <summary>
        /// Searches a range of a sorted readonly list for a value using binary search.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The sorted list to search.</param>
        /// <param name="index">The starting index of the range to search.</param>
        /// <param name="count">The number of elements to search.</param>
        /// <param name="value">The value to search for.</param>
        /// <returns>The index of the value if found within the range; otherwise, the bitwise complement of the index where it should be inserted.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(log count). Range must be pre-sorted.</para>
        /// <para>Allocations: No allocations (delegates to overload with null comparer).</para>
        /// <para>Edge cases: Range must be sorted. Validates range bounds before searching.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when index or count are out of valid range.</exception>
        public static int BinarySearch<T>(
            this IReadOnlyList<T> readonlyList,
            int index,
            int count,
            T value
        )
        {
            return readonlyList.BinarySearch(index, count, value, null);
        }

        /// <summary>
        /// Searches a range of a sorted readonly list for a value using binary search with a custom comparer.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="readonlyList">The sorted list to search.</param>
        /// <param name="index">The starting index of the range to search.</param>
        /// <param name="count">The number of elements to search.</param>
        /// <param name="value">The value to search for.</param>
        /// <param name="comparer">The comparer to use. If null, uses Comparer&lt;T&gt;.Default.</param>
        /// <returns>The index of the value if found within the range; otherwise, the bitwise complement of the index where it should be inserted.</returns>
        /// <remarks>
        /// <para>Null handling: Throws ArgumentNullException if readonlyList is null. Comparer defaults to Comparer&lt;T&gt;.Default if null.</para>
        /// <para>Thread safety: Thread-safe for read-only access. Not thread-safe if list is modified during execution. No Unity main thread requirement.</para>
        /// <para>Performance: O(log count). Optimized for T[] and List&lt;T&gt; when using default comparer.</para>
        /// <para>Allocations: No allocations if comparer is provided, otherwise allocates default comparer.</para>
        /// <para>Edge cases: Range must be sorted according to comparer. Validates range bounds before searching.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Thrown when readonlyList is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when index or count are out of valid range.</exception>
        public static int BinarySearch<T>(
            this IReadOnlyList<T> readonlyList,
            int index,
            int count,
            T value,
            IComparer<T> comparer
        )
        {
            if (readonlyList == null)
            {
                throw new ArgumentNullException(nameof(readonlyList));
            }

            int length = readonlyList.Count;
            ValidateForwardSegment(length, index, count);

            comparer ??= Comparer<T>.Default;

            switch (readonlyList)
            {
                case T[] array:
                {
                    return Array.BinarySearch(array, index, count, value, comparer);
                }

                case List<T> list:
                {
                    return list.BinarySearch(index, count, value, comparer);
                }
            }

            int low = index;
            int high = index + count - 1;

            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                int comparison = comparer.Compare(readonlyList[mid], value);

                if (comparison == 0)
                {
                    return mid;
                }

                if (comparison < 0)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return ~low;
        }

        private static void ValidateForwardSegment(int length, int startIndex, int count)
        {
            if ((uint)length < (uint)startIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            if (count < 0 || length - count < startIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }

        private static void ValidateBackwardSegment(int length, int startIndex, int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (length == 0)
            {
                if (0 <= startIndex || count != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(startIndex));
                }

                return;
            }

            if ((uint)length <= (uint)startIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            if (startIndex + 1 < count)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
        }
    }
}
