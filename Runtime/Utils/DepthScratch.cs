// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>Keeps one grow-only scratch array per recursion depth.</summary>
    /// <remarks>
    /// One instance belongs to one search or thread. Growth preserves existing contents, and a
    /// smaller request exposes the stale prefix, so write every slot before reading it.
    /// </remarks>
    public sealed class DepthScratch<T>
    {
        private const int InitialDepth = 4;
        private T[][] _buffers = Array.Empty<T[]>();

        /// <summary>Returns the scratch span kept for a recursion depth.</summary>
        /// <param name="depth">Nonnegative recursion depth.</param>
        /// <param name="length">Requested logical length; zero returns an empty span.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when depth or length is negative.</exception>
        public Span<T> Get(int depth, int length)
        {
            if (depth < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(depth),
                    depth,
                    "Must be non-negative."
                );
            }

            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length),
                    length,
                    "Must be non-negative."
                );
            }

            if (length == 0)
            {
                return Span<T>.Empty;
            }

            int knownDepth = _buffers.Length;
            if (knownDepth <= depth)
            {
                int capacity = Math.Max(InitialDepth, knownDepth * 2);
                Array.Resize(ref _buffers, Math.Max(depth + 1, capacity));
            }

            T[] buffer = _buffers[depth];
            GrowOnlyBuffers.EnsureCapacity(ref buffer, length);
            _buffers[depth] = buffer;
            return new Span<T>(buffer, 0, length);
        }
    }
}
