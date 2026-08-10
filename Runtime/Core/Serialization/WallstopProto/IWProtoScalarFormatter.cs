// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    /// <summary>
    /// Encodes a value that is <b>not</b> a length-delimited message: a varint, a fixed32, a
    /// fixed64, or a length-delimited value that carries no sub-message (a string, a byte array).
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <remarks>
    /// <para>
    /// This exists because a generic contract cannot know, at generate time, how to encode a member
    /// typed <c>T</c>. Measured against protobuf-net: <c>Box&lt;int&gt;.Value</c> is <c>08 01</c>,
    /// <c>Box&lt;double&gt;</c> is <c>09 …</c>, <c>Box&lt;string&gt;</c> is <c>0A …</c> — the
    /// <b>field key itself</b> changes with <c>T</c>. A single emitted tag constant is wrong for all
    /// but one closure, so the wire type has to be a property of the closed type rather than of the
    /// emitted code.
    /// </para>
    /// <para>
    /// Deliberately <b>separate</b> from <see cref="IWProtoFormatter{T}"/> rather than a member added
    /// to it. Every hand-written formatter in existence describes a message, and a message is
    /// exactly the case this interface does not cover; keeping them apart means nothing already
    /// written has to change, and <c>Measure</c> keeps its single meaning of "payload size,
    /// excluding the key and the length prefix".
    /// </para>
    /// </remarks>
    public interface IWProtoScalarFormatter<T>
    {
        /// <summary>The wire type a field of this shape carries in its key.</summary>
        int WireType { get; }

        /// <summary>
        /// Reports whether <paramref name="value"/> is omitted from the wire.
        /// </summary>
        /// <param name="value">The value to test.</param>
        /// <returns><c>true</c> when the value equals its type's default and is not written.</returns>
        /// <remarks>
        /// protobuf-net omits a member equal to its declared default. That rule is per-type — zero
        /// for the numerics, <c>false</c> for <c>bool</c>, <c>null</c> (but not empty) for
        /// <c>string</c> and <c>byte[]</c> — so it lives here rather than in emitted code.
        /// </remarks>
        bool IsDefault(in T value);

        /// <summary>Returns the encoded size of the value, excluding its field key.</summary>
        /// <param name="value">The value to measure.</param>
        /// <returns>The encoded size in bytes.</returns>
        int MeasureValue(in T value);

        /// <summary>Writes the value, without a field key and without a length prefix.</summary>
        /// <param name="writer">The destination.</param>
        /// <param name="value">The value to write.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        bool WriteValue(ref WProtoWriter writer, in T value);

        /// <summary>Reads a value written by <see cref="WriteValue"/>.</summary>
        /// <param name="reader">The source.</param>
        /// <param name="value">Receives the value, or <c>default</c> on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        bool TryReadValue(ref WProtoReader reader, out T value);
    }
}
