// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Utils
{
    using System;

    /// <summary>Reuses caller-owned scratch arrays across changing logical lengths.</summary>
    /// <remarks>
    /// Keep the logical count separately from array capacity. Growth discards prior contents;
    /// use exact-size arrays for Unity APIs that require an exact pixel or element count.
    /// </remarks>
    public static class GrowOnlyBuffers
    {
        /// <summary>Allocates a scratch array only when the requested count exceeds its capacity.</summary>
        /// <returns>Whether a new nonempty array replaced the buffer.</returns>
        /// <remarks>Nonpositive counts keep existing storage or initialize a null buffer to an empty array.</remarks>
        public static bool EnsureCapacity<T>(ref T[] buffer, int count)
        {
            if (count <= 0)
            {
                buffer ??= Array.Empty<T>();
                return false;
            }

            if (buffer != null && count <= buffer.Length)
            {
                return false;
            }

            buffer = new T[count];
            return true;
        }

        /// <summary>Clears the bounded logical prefix while preserving unused storage.</summary>
        public static void Clear<T>(T[] buffer, int count)
        {
            if (buffer == null || count <= 0)
            {
                return;
            }

            Array.Clear(buffer, 0, Math.Min(count, buffer.Length));
        }

        /// <summary>Returns the bounded logical prefix without exposing unused storage.</summary>
        public static ReadOnlySpan<T> AsSpan<T>(T[] buffer, int count)
        {
            if (buffer == null || count <= 0)
            {
                return ReadOnlySpan<T>.Empty;
            }

            return new ReadOnlySpan<T>(buffer, 0, Math.Min(count, buffer.Length));
        }
    }
}
