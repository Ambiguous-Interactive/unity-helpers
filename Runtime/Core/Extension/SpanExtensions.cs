// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Extension
{
    using System;
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
    /// <c>yield</c>, so nothing here takes a delegate.
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
    }
}
