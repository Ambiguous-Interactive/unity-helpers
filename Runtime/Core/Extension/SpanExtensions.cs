// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
    using Helper;
    using Random;

    /// <summary>
    /// The contiguous-buffer half of <see cref="IListExtensions"/>, for a caller holding a
    /// <c>stackalloc</c> buffer, an array slice, or any other span that no <see cref="System.Collections.Generic.IList{T}"/>
    /// describes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>These consume the random source draw for draw exactly as their <c>IList</c> siblings do</b>,
    /// because they are the same body: <see cref="IListExtensions.Shuffle{T}(System.Collections.Generic.IList{T}, IRandom)"/>
    /// reaches <see cref="Shuffle{T}(Span{T}, IRandom)"/> for its array fast path and for its pooled
    /// write-back path alike. A caller with seeded, reproducible generation can therefore move a
    /// shuffle onto a stack buffer and get byte-identical output, which is the property that decides
    /// whether the move is possible at all.
    /// </para>
    /// <para>
    /// A <see cref="Span{T}"/> cannot be captured by a lambda or held across an <c>await</c> or a
    /// <c>yield</c>. A predicate passed as an argument is fine -- it is the span that cannot be
    /// captured, not the delegate -- but a predicate that closes over caller state allocates, so
    /// every predicate-taking method here also has a <c>TState</c> overload that lets the caller
    /// keep a <c>static</c> lambda.
    /// </para>
    /// <para>
    /// <see cref="Span{T}"/> already carries <c>Fill(T)</c>, <c>Reverse()</c>, <c>Clear()</c>,
    /// <c>CopyTo</c> and <c>MemoryExtensions.IndexOf(T)</c>, so this type deliberately does not
    /// shadow them. What is here is what the framework does not have.
    /// </para>
    /// <para>
    /// A <see cref="ReadOnlySpan{T}"/> receiver needs an explicit cast at this language version --
    /// <c>((ReadOnlySpan&lt;int&gt;)table).IndexOf(...)</c> -- because an extension receiver takes no
    /// user-defined conversion.
    /// </para>
    /// </remarks>
    public static class SpanExtensions
    {
        /// <summary>
        /// Randomly shuffles a span in place using the Fisher-Yates algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="span">The span to shuffle.</param>
        /// <param name="random">The random number generator to use. If null, uses PRNG.Instance.</param>
        /// <remarks>
        /// <para>Null handling: If random is null, uses PRNG.Instance.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the span in place. If random is shared, may not be thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(n). Nothing is copied and nothing is written back.</para>
        /// <para>Allocations: None.</para>
        /// <para>Random draws: exactly <c>span.Length - 1</c> calls to <c>random.Next(i, span.Length)</c>,
        /// ascending in <c>i</c> -- the same sequence <see cref="IListExtensions.Shuffle{T}(System.Collections.Generic.IList{T}, IRandom)"/>
        /// makes for a collection of the same length, because it is the same code.</para>
        /// <para>Edge cases: Spans of 0 or 1 elements are not modified and draw nothing.</para>
        /// </remarks>
        public static void Shuffle<T>(this Span<T> span, IRandom random = null)
        {
            int count = span.Length;
            if (count <= 1)
            {
                return;
            }

            random ??= PRNG.Instance;
            for (int i = 0; i < count - 1; ++i)
            {
                int nextIndex = random.Next(i, count);
                if (nextIndex == i)
                {
                    continue;
                }

                (span[i], span[nextIndex]) = (span[nextIndex], span[i]);
            }
        }

        /// <summary>
        /// Copies <paramref name="source"/> into <paramref name="destination"/> and shuffles the
        /// copy, leaving <paramref name="source"/> untouched.
        /// </summary>
        /// <typeparam name="T">The type of elements to copy.</typeparam>
        /// <param name="source">The sequence to copy. Not modified.</param>
        /// <param name="destination">Caller-provided storage, which must be at least as long as <paramref name="source"/>.</param>
        /// <param name="random">The random number generator to use. If null, uses PRNG.Instance.</param>
        /// <returns>
        /// True when <paramref name="destination"/> was long enough and now holds a shuffled copy of
        /// <paramref name="source"/>; false when it was too short, in which case nothing was written
        /// and nothing was drawn.
        /// </returns>
        /// <remarks>
        /// <para>This is the shape that removes the allocation: a shared <c>static readonly T[]</c>
        /// table cannot be shuffled in place, so it is copied first, and the copy is the allocation.
        /// Copying into a <c>stackalloc</c> buffer instead costs nothing.</para>
        /// <para>Thread safety: Not thread-safe. If random is shared, may not be thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(n). Allocations: None.</para>
        /// <para>Only the first <c>source.Length</c> elements of <paramref name="destination"/> are
        /// written or shuffled; a longer destination keeps its tail.</para>
        /// </remarks>
        public static bool TryCopyShuffled<T>(
            this ReadOnlySpan<T> source,
            Span<T> destination,
            IRandom random = null
        )
        {
            if (destination.Length < source.Length)
            {
                return false;
            }

            Span<T> written = destination.Slice(0, source.Length);
            source.CopyTo(written);
            Shuffle(written, random);
            return true;
        }

        /// <summary>
        /// Selects one element of a span at random.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="span">The span to select from.</param>
        /// <param name="element">The selected element, or <c>default</c> when the span is empty.</param>
        /// <param name="random">The random number generator to use. If null, uses PRNG.Instance.</param>
        /// <returns>True when an element was selected; false when the span is empty.</returns>
        /// <remarks>
        /// <para>The <c>IList</c> sibling throws on an empty list. This one reports instead, which is
        /// the package's rule for a new public API, and it is why there is no throwing span overload
        /// to pair with it.</para>
        /// <para>Thread safety: Thread-safe for read-only access. If random is shared, may not be thread-safe. No Unity main thread requirement.</para>
        /// <para>Performance: O(1). Allocations: None.</para>
        /// </remarks>
        public static bool TryGetRandomElement<T>(
            this ReadOnlySpan<T> span,
            out T element,
            IRandom random = null
        )
        {
            if (span.Length <= 0)
            {
                element = default;
                return false;
            }

            random ??= PRNG.Instance;
            element = span[random.Next(0, span.Length)];
            return true;
        }

        /// <summary>
        /// Exchanges the elements at two indices.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="span">The span to modify.</param>
        /// <param name="indexA">The index of the first element.</param>
        /// <param name="indexB">The index of the second element.</param>
        /// <returns>
        /// True when both indices were inside the span; false when either was not, in which case
        /// nothing was written.
        /// </returns>
        /// <remarks>
        /// <para>The <c>IList</c> sibling throws <see cref="ArgumentOutOfRangeException"/>. This one
        /// reports instead, which is the package's rule for a new public API.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the span in place.</para>
        /// <para>Performance: O(1). Allocations: None.</para>
        /// <para>Edge cases: swapping an index with itself succeeds and writes nothing.</para>
        /// </remarks>
        /// <seealso cref="IListExtensions.Swap{T}(System.Collections.Generic.IList{T}, int, int)"/>
        public static bool TrySwap<T>(this Span<T> span, int indexA, int indexB)
        {
            if (indexA < 0 || span.Length <= indexA)
            {
                return false;
            }

            if (indexB < 0 || span.Length <= indexB)
            {
                return false;
            }

            if (indexA == indexB)
            {
                return true;
            }

            (span[indexA], span[indexB]) = (span[indexB], span[indexA]);
            return true;
        }

        /// <summary>
        /// Assigns every element from a factory that receives its index.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="span">The span to fill.</param>
        /// <param name="factory">A function mapping an index to the value for that position.</param>
        /// <remarks>
        /// <para>Null handling: a null factory writes nothing. The <c>IList</c> sibling throws.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the span in place.</para>
        /// <para>Performance: O(n) plus the factory. Allocations: whatever the factory allocates.</para>
        /// <para><see cref="Span{T}.Fill(T)"/> already covers the constant case, so there is no
        /// value-taking overload here.</para>
        /// </remarks>
        /// <seealso cref="IListExtensions.Fill{T}(System.Collections.Generic.IList{T}, Func{int, T})"/>
        public static void Fill<T>(this Span<T> span, Func<int, T> factory)
        {
            if (factory == null)
            {
                return;
            }

            for (int i = 0; i < span.Length; ++i)
            {
                span[i] = factory(i);
            }
        }

        /// <summary>
        /// Rotates a span in place by the given number of positions.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="span">The span to rotate.</param>
        /// <param name="amount">Positions to shift. Positive shifts right, negative shifts left.</param>
        /// <remarks>
        /// <para>This is the body <see cref="IListExtensions.Shift{T}(System.Collections.Generic.IList{T}, int)"/>
        /// uses for its array fast path, so moving a rotation onto a span cannot change its result.</para>
        /// <para>Thread safety: Not thread-safe. Modifies the span in place.</para>
        /// <para>Performance: O(n), by three reversals. Allocations: None.</para>
        /// <para>Edge cases: spans of 0 or 1 elements are not modified. The amount is normalized with
        /// <see cref="WallMath.PositiveMod(int, int)"/>, so a negative or oversized amount is
        /// well-defined and a multiple of the length is a no-op.</para>
        /// </remarks>
        public static void Shift<T>(this Span<T> span, int amount)
        {
            int count = span.Length;
            if (count <= 1)
            {
                return;
            }

            amount = amount.PositiveMod(count);
            if (amount == 0)
            {
                return;
            }

            span.Reverse();
            span.Slice(0, amount).Reverse();
            span.Slice(amount, count - amount).Reverse();
        }

        /// <summary>
        /// Rotates a span left by the given number of positions.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="span">The span to rotate.</param>
        /// <param name="positions">Positions to rotate left. Defaults to 1.</param>
        /// <remarks>Delegates to <see cref="Shift{T}(Span{T}, int)"/>; see it for edge cases.</remarks>
        public static void RotateLeft<T>(this Span<T> span, int positions = 1)
        {
            Shift(span, -positions);
        }

        /// <summary>
        /// Rotates a span right by the given number of positions.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="span">The span to rotate.</param>
        /// <param name="positions">Positions to rotate right. Defaults to 1.</param>
        /// <remarks>Delegates to <see cref="Shift{T}(Span{T}, int)"/>; see it for edge cases.</remarks>
        public static void RotateRight<T>(this Span<T> span, int positions = 1)
        {
            Shift(span, positions);
        }

        /// <summary>
        /// Finds the index of the first element matching a predicate.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="span">The span to search.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns>The index of the first match, or -1 when there is none.</returns>
        /// <remarks>
        /// <para>Null handling: a null predicate returns -1. The <c>IList</c> sibling throws.</para>
        /// <para>Performance: O(n), short-circuiting on the first match. Allocations: none of its
        /// own; a predicate that closes over caller state allocates, which the
        /// <see cref="IndexOf{T, TState}(ReadOnlySpan{T}, TState, Func{T, TState, bool})"/> overload
        /// exists to avoid.</para>
        /// </remarks>
        /// <seealso cref="IListExtensions.IndexOf{T}(System.Collections.Generic.IList{T}, Func{T, bool})"/>
        public static int IndexOf<T>(this ReadOnlySpan<T> span, Func<T, bool> predicate)
        {
            if (predicate == null)
            {
                return -1;
            }

            for (int i = 0; i < span.Length; ++i)
            {
                if (predicate(span[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Finds the index of the first element matching a predicate, passing caller state through.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <typeparam name="TState">The type of the caller state.</typeparam>
        /// <param name="span">The span to search.</param>
        /// <param name="state">Caller state handed to every predicate call.</param>
        /// <param name="predicate">A function to test each element against the state.</param>
        /// <returns>The index of the first match, or -1 when there is none.</returns>
        /// <remarks>
        /// <para>The state parameter is what lets the predicate be a <c>static</c> lambda, so the
        /// search allocates nothing at all.</para>
        /// <para>Null handling: a null predicate returns -1.</para>
        /// <para>Performance: O(n), short-circuiting on the first match. Allocations: None.</para>
        /// </remarks>
        public static int IndexOf<T, TState>(
            this ReadOnlySpan<T> span,
            TState state,
            Func<T, TState, bool> predicate
        )
        {
            if (predicate == null)
            {
                return -1;
            }

            for (int i = 0; i < span.Length; ++i)
            {
                if (predicate(span[i], state))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Finds the index of the last element matching a predicate.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <param name="span">The span to search.</param>
        /// <param name="predicate">A function to test each element.</param>
        /// <returns>The index of the last match, or -1 when there is none.</returns>
        /// <remarks>
        /// <para>Null handling: a null predicate returns -1. The <c>IList</c> sibling throws.</para>
        /// <para>Performance: O(n) from the end, short-circuiting on the first match.</para>
        /// </remarks>
        /// <seealso cref="IListExtensions.LastIndexOf{T}(System.Collections.Generic.IList{T}, Func{T, bool})"/>
        public static int LastIndexOf<T>(this ReadOnlySpan<T> span, Func<T, bool> predicate)
        {
            if (predicate == null)
            {
                return -1;
            }

            for (int i = span.Length - 1; 0 <= i; --i)
            {
                if (predicate(span[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Finds the index of the last element matching a predicate, passing caller state through.
        /// </summary>
        /// <typeparam name="T">The type of elements in the span.</typeparam>
        /// <typeparam name="TState">The type of the caller state.</typeparam>
        /// <param name="span">The span to search.</param>
        /// <param name="state">Caller state handed to every predicate call.</param>
        /// <param name="predicate">A function to test each element against the state.</param>
        /// <returns>The index of the last match, or -1 when there is none.</returns>
        /// <remarks>
        /// <para>Null handling: a null predicate returns -1.</para>
        /// <para>Performance: O(n) from the end. Allocations: None.</para>
        /// </remarks>
        public static int LastIndexOf<T, TState>(
            this ReadOnlySpan<T> span,
            TState state,
            Func<T, TState, bool> predicate
        )
        {
            if (predicate == null)
            {
                return -1;
            }

            for (int i = span.Length - 1; 0 <= i; --i)
            {
                if (predicate(span[i], state))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Copies every element matching a predicate into caller-provided storage.
        /// </summary>
        /// <typeparam name="T">The type of elements to test and copy.</typeparam>
        /// <typeparam name="TState">The type of the caller state.</typeparam>
        /// <param name="source">The sequence to search. Not modified.</param>
        /// <param name="destination">Caller-provided storage for the matches.</param>
        /// <param name="state">Caller state handed to every predicate call.</param>
        /// <param name="predicate">A function to test each element against the state.</param>
        /// <param name="written">The number of matches written to <paramref name="destination"/>.</param>
        /// <returns>
        /// True when every match fit; false when <paramref name="destination"/> filled up first, or
        /// when <paramref name="predicate"/> is null.
        /// </returns>
        /// <remarks>
        /// <para>This is the allocating <see cref="IListExtensions.FindAll{T}(System.Collections.Generic.IList{T}, Func{T, bool})"/>
        /// made usable in a hot loop: the <c>IList</c> sibling returns a new <c>List</c> every call.</para>
        /// <para>Unlike <see cref="TryCopyShuffled{T}(ReadOnlySpan{T}, Span{T}, IRandom)"/>, a refusal
        /// here leaves what already fit in place and reports it in <paramref name="written"/>, so the
        /// caller can grow and retry. Counting first to keep the destination pristine would run the
        /// predicate twice per element, which a predicate with a cost or a side effect cannot afford.</para>
        /// <para>Performance: O(n), one predicate call per element. Allocations: None.</para>
        /// </remarks>
        public static bool TryFindAll<T, TState>(
            this ReadOnlySpan<T> source,
            Span<T> destination,
            TState state,
            Func<T, TState, bool> predicate,
            out int written
        )
        {
            if (predicate == null)
            {
                written = 0;
                return false;
            }

            int matches = 0;
            for (int i = 0; i < source.Length; ++i)
            {
                T element = source[i];
                if (!predicate(element, state))
                {
                    continue;
                }

                if (destination.Length <= matches)
                {
                    written = matches;
                    return false;
                }

                destination[matches] = element;
                ++matches;
            }

            written = matches;
            return true;
        }

        /// <summary>
        /// Splits a sequence into the elements matching a predicate and the elements that do not.
        /// </summary>
        /// <typeparam name="T">The type of elements to test and copy.</typeparam>
        /// <typeparam name="TState">The type of the caller state.</typeparam>
        /// <param name="source">The sequence to split. Not modified.</param>
        /// <param name="matching">Storage for the matches; must be at least as long as <paramref name="source"/>.</param>
        /// <param name="notMatching">Storage for the rest; must be at least as long as <paramref name="source"/>.</param>
        /// <param name="state">Caller state handed to every predicate call.</param>
        /// <param name="predicate">A function to test each element against the state.</param>
        /// <param name="matchedCount">The number of elements written to <paramref name="matching"/>.</param>
        /// <param name="unmatchedCount">The number of elements written to <paramref name="notMatching"/>.</param>
        /// <returns>
        /// True when the split completed; false when either destination is shorter than
        /// <paramref name="source"/> or <paramref name="predicate"/> is null, in which case nothing
        /// was written.
        /// </returns>
        /// <remarks>
        /// <para>Either side can take every element, so demanding both be source-length is the only
        /// bound that cannot overflow without counting the matches first -- and that keeps the
        /// refusal clean, with nothing written, which a two-destination partial stop could not be.
        /// The <c>IList</c> sibling allocates two lists per call instead.</para>
        /// <para>Performance: O(n), one predicate call per element. Allocations: None.</para>
        /// </remarks>
        /// <seealso cref="IListExtensions.Partition{T}(System.Collections.Generic.IList{T}, Func{T, bool})"/>
        public static bool TryPartition<T, TState>(
            this ReadOnlySpan<T> source,
            Span<T> matching,
            Span<T> notMatching,
            TState state,
            Func<T, TState, bool> predicate,
            out int matchedCount,
            out int unmatchedCount
        )
        {
            if (predicate == null)
            {
                matchedCount = 0;
                unmatchedCount = 0;
                return false;
            }

            if (matching.Length < source.Length || notMatching.Length < source.Length)
            {
                matchedCount = 0;
                unmatchedCount = 0;
                return false;
            }

            int matched = 0;
            int unmatched = 0;
            for (int i = 0; i < source.Length; ++i)
            {
                T element = source[i];
                if (predicate(element, state))
                {
                    matching[matched] = element;
                    ++matched;
                }
                else
                {
                    notMatching[unmatched] = element;
                    ++unmatched;
                }
            }

            matchedCount = matched;
            unmatchedCount = unmatched;
            return true;
        }
    }
}
