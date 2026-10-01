// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;

    /// <summary>
    /// Provides high-performance array conversion methods using Buffer.BlockCopy for efficient memory operations.
    /// </summary>
    /// <remarks>
    /// Thread Safety: All methods are thread-safe.
    /// Performance: Uses Buffer.BlockCopy for optimal performance - significantly faster than element-by-element copying.
    /// </remarks>
    public static class ArrayConverter
    {
        /// <summary>
        /// Converts an integer array to a byte array using Buffer.BlockCopy for high performance.
        /// </summary>
        /// <param name="ints">The integer array to convert.</param>
        /// <returns>A byte array containing the binary representation of the integers (4 bytes per int).</returns>
        /// <exception cref="ArgumentNullException">Thrown when ints is null.</exception>
        /// <remarks>
        /// Null handling: Throws ArgumentNullException if ints is null.
        /// Thread-safe: Yes.
        /// Performance: O(n) where n is array length. Uses native memory copy.
        /// Allocations: Allocates new byte array of size (ints.Length * 4).
        /// Edge cases: Empty array returns empty byte array. Endianness depends on system architecture.
        /// </remarks>
        public static byte[] IntArrayToByteArrayBlockCopy(int[] ints)
        {
            if (ints == null)
            {
                throw new ArgumentNullException(nameof(ints));
            }

            byte[] bytes = new byte[ints.Length * sizeof(int)];
            Buffer.BlockCopy(ints, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>
        /// Converts a byte array to an integer array using Buffer.BlockCopy for high performance.
        /// </summary>
        /// <param name="bytes">The byte array to convert. Must have length divisible by 4.</param>
        /// <returns>An integer array reconstructed from the byte data (1 int per 4 bytes).</returns>
        /// <exception cref="ArgumentNullException">Thrown when bytes is null.</exception>
        /// <exception cref="ArgumentException">Thrown when byte array length is not a multiple of sizeof(int) (4).</exception>
        /// <remarks>
        /// Null handling: Throws ArgumentNullException if bytes is null.
        /// Thread-safe: Yes.
        /// Performance: O(n) where n is array length. Uses native memory copy.
        /// Allocations: Allocates new integer array of size (bytes.Length / 4).
        /// Edge cases: Empty array returns empty int array. Requires byte length to be multiple of 4. Endianness depends on system architecture.
        /// </remarks>
        public static int[] ByteArrayToIntArrayBlockCopy(byte[] bytes)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes), "Byte array cannot be null.");
            }

            int bytesLength = bytes.Length;
            if (bytesLength % sizeof(int) != 0)
            {
                throw new ArgumentException(
                    $"Byte array length must be a multiple of {sizeof(int)}.",
                    nameof(bytes)
                );
            }

            int[] ints = new int[bytesLength / sizeof(int)];
            Buffer.BlockCopy(bytes, 0, ints, 0, bytesLength);
            return ints;
        }
    }
}
