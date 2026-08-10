// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// What a buffer-reusing serialize did: how many bytes it wrote, and whether it had to replace
    /// the caller's array to fit them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returned instead of an <c>int</c> with a sentinel. A magic <c>-1</c> for "not served" reads
    /// as a length everywhere it is passed on, and the compiler cannot tell the two apart -- so the
    /// one caller who forgets to check it writes a negative count into a stream API and gets a
    /// failure nowhere near the cause. <see cref="BytesWritten"/> being <c>null</c> cannot be used
    /// as a length by accident.
    /// </para>
    /// <para>
    /// <see cref="Resized"/> exists because <c>ref byte[]</c> is a trap for a caller holding a
    /// SECOND reference to the same array -- a pooled buffer's owner, a struct field, a captured
    /// local. When the payload does not fit, the reference the caller passed is pointed at a new
    /// array and every other reference still names the old, now-stale one. Reporting the swap lets
    /// that caller re-read the buffer rather than silently keep writing into the previous array.
    /// </para>
    /// </remarks>
    public readonly struct WProtoWriteResult
    {
        /// <summary>
        /// The number of bytes written, or <c>null</c> when WallstopProto does not serve the type.
        /// </summary>
        /// <remarks>
        /// Zero is a legitimate value and means the payload is genuinely empty: an empty contract
        /// and a null root both encode to nothing, measured against protobuf-net. That is exactly
        /// why "not served" needed a representation that is not a number.
        /// </remarks>
        public readonly int? BytesWritten;

        /// <summary>
        /// Whether the caller's array was replaced with a larger one.
        /// </summary>
        public readonly bool Resized;

        /// <summary>Creates a result.</summary>
        /// <param name="bytesWritten">The payload length, or <c>null</c> when unserved.</param>
        /// <param name="resized">Whether the buffer reference was pointed at a new array.</param>
        public WProtoWriteResult(int? bytesWritten, bool resized)
        {
            BytesWritten = bytesWritten;
            Resized = resized;
        }

        /// <summary>Whether WallstopProto handled the request.</summary>
        public bool Served => BytesWritten.HasValue;

        /// <summary>The payload length, or 0 when the request was not served.</summary>
        /// <remarks>
        /// For a caller that has already checked <see cref="Served"/> and wants the number without
        /// unwrapping it a second time.
        /// </remarks>
        public int Length => BytesWritten ?? 0;
    }
}
