// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;

    /// <summary>
    /// Deterministic integer sequences for retries, restarts and backoff schedules.
    /// </summary>
    public static class Sequences
    {
        /// <summary>
        /// Returns the 1-based Luby restart multiplier at an index.
        /// </summary>
        /// <param name="index">The 1-based position in the sequence.</param>
        /// <returns>The multiplier, a power of two. The sequence is 1, 1, 2, 1, 1, 2, 4, 1, ...</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="index"/> is not positive.</exception>
        public static long Luby(int index)
        {
            if (index <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    "The Luby sequence is 1-based, so the index must be positive."
                );
            }

            long remaining = index;
            while (true)
            {
                int run = 1;
                while ((1L << run) - 1L < remaining)
                {
                    ++run;
                }

                long runEnd = (1L << run) - 1L;
                if (remaining == runEnd)
                {
                    return 1L << (run - 1);
                }

                remaining = remaining - (1L << (run - 1)) + 1L;
            }
        }

        /// <summary>
        /// Tries to read the 1-based Luby restart multiplier at an index.
        /// </summary>
        /// <param name="index">The 1-based position in the sequence.</param>
        /// <param name="value">The multiplier, or 0 when <paramref name="index"/> is not positive.</param>
        /// <returns>True when <paramref name="index"/> is positive; otherwise, false.</returns>
        public static bool TryLuby(int index, out long value)
        {
            if (index <= 0)
            {
                value = 0;
                return false;
            }

            value = Luby(index);
            return true;
        }

        /// <summary>
        /// Fills a span with the 1-based Luby restart multipliers.
        /// </summary>
        /// <param name="destination">The span to fill, starting at index 1 of the sequence.</param>
        public static void Luby(Span<long> destination)
        {
            int length = destination.Length;
            for (int i = 0; i < length; ++i)
            {
                destination[i] = Luby(i + 1);
            }
        }
    }
}
