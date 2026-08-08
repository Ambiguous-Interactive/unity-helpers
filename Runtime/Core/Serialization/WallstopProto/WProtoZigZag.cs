// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System.Runtime.CompilerServices;

    /// <summary>
    /// ZigZag transforms, which map signed values onto unsigned ones so that small magnitudes
    /// encode short regardless of sign.
    /// </summary>
    public static class WProtoZigZag
    {
        /// <summary>
        /// Maps a signed 32-bit value onto its ZigZag representation.
        /// </summary>
        /// <param name="value">The signed value.</param>
        /// <returns>The unsigned ZigZag encoding.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Encode32(int value)
        {
            return (uint)((value << 1) ^ (value >> 31));
        }

        /// <summary>
        /// Recovers a signed 32-bit value from its ZigZag representation.
        /// </summary>
        /// <param name="value">The unsigned ZigZag encoding.</param>
        /// <returns>The signed value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Decode32(uint value)
        {
            return (int)(value >> 1) ^ -(int)(value & 1);
        }

        /// <summary>
        /// Maps a signed 64-bit value onto its ZigZag representation.
        /// </summary>
        /// <param name="value">The signed value.</param>
        /// <returns>The unsigned ZigZag encoding.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Encode64(long value)
        {
            return (ulong)((value << 1) ^ (value >> 63));
        }

        /// <summary>
        /// Recovers a signed 64-bit value from its ZigZag representation.
        /// </summary>
        /// <param name="value">The unsigned ZigZag encoding.</param>
        /// <returns>The signed value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long Decode64(ulong value)
        {
            return (long)(value >> 1) ^ -(long)(value & 1);
        }
    }
}
