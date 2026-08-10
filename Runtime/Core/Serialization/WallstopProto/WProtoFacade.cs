// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// The seam <see cref="Serializer"/> uses to serve a type through WallstopProto instead of
    /// protobuf-net.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the facade swap of the design item, and it is deliberately <b>opt-in per type</b>
    /// rather than all-or-nothing. Each method answers "is there a generated formatter for exactly
    /// this type", and returns <c>false</c> when there is not, so a contract that has been annotated
    /// travels the reflection-free path while one that has not keeps working exactly as before.
    /// Porting the remaining contracts is therefore incremental and individually verifiable, instead
    /// of one change that moves every type at once and can only be tested in aggregate.
    /// </para>
    /// <para>
    /// <see cref="Serializer"/> only calls this when <c>WALLSTOP_PROTO</c> is defined. The methods
    /// themselves compile unconditionally so they can be tested without a second compilation.
    /// </para>
    /// <para>
    /// The type test is <b>exact</b>, not assignable. A subtype served by its base's formatter would
    /// silently lose everything the subtype declares -- the same failure the generator refuses at
    /// build time -- so a runtime type that is not the declared one falls back rather than guessing.
    /// </para>
    /// </remarks>
    public static class WProtoFacade
    {
        /// <summary>
        /// Serializes <paramref name="value"/> into <paramref name="buffer"/>, growing it only when
        /// what is already there is too small.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <param name="buffer">
        /// The destination, reused in place when it is large enough and replaced with a larger array
        /// when it is not. May be <c>null</c>. Left untouched when the request is not served.
        /// </param>
        /// <returns>
        /// A <see cref="WProtoWriteResult"/> carrying the byte count -- which may be less than
        /// <c>buffer.Length</c> -- and whether the buffer had to be replaced to fit it.
        /// </returns>
        /// <remarks>
        /// <para>
        /// The allocation-free entry point, and the one a caller serializing every frame should use.
        /// <see cref="TrySerialize{T}"/> hands back an array sized exactly to the payload, which
        /// means a fresh allocation per call; this one lets a caller keep a single scratch buffer for
        /// the lifetime of the program.
        /// </para>
        /// <para>
        /// The length is the RETURN VALUE rather than the buffer's length, because a reused buffer is
        /// almost never exactly the right size. Callers must slice to it -- writing
        /// <c>buffer.Length</c> bytes to a file or socket would append whatever the previous, larger
        /// message left behind.
        /// </para>
        /// <para>
        /// The result is a struct rather than an <c>int</c> with a sentinel. <c>0</c> is a legitimate
        /// length -- an empty contract and a null root both encode to nothing, measured against
        /// protobuf-net -- so "not served" needed a representation that is not a number at all, and a
        /// magic <c>-1</c> reads as a length everywhere it is passed on.
        /// </para>
        /// </remarks>
        public static WProtoWriteResult Serialize<T>(T value, ref byte[] buffer)
        {
            if (
                !CanServe(value)
                || !WProtoFormatterProvider.TryGet(out IWProtoFormatter<T> formatter)
            )
            {
                return new WProtoWriteResult(null, false);
            }

            if (!typeof(T).IsValueType && value == null)
            {
                return new WProtoWriteResult(0, false);
            }

            int size = formatter.Measure(value);
            bool resized = buffer == null || buffer.Length < size;
            if (resized)
            {
                buffer = new byte[size];
            }

            // Sliced to `size`, not handed the whole buffer: a reused buffer is longer than this
            // message, and the writer's bounds checks are what stop a formatter from running past
            // its own payload into the previous one's bytes.
            WProtoWriter writer = new WProtoWriter(new Span<byte>(buffer, 0, size));
            if (!formatter.Write(ref writer, value))
            {
                // Thrown, not reported as "not served" -- the serialize-side mirror of the refused
                // READ that already throws below. A registered formatter that fails is a defect in
                // this package (Measure and Write disagreeing, or a latched fault), and returning
                // the not-served answer would send the value on to protobuf-net, silently producing
                // bytes from the reflection path this whole serializer exists to avoid. The buffer
                // may also be half-written by now, so there is nothing coherent to hand back.
                throw new InvalidOperationException(
                    $"WallstopProto failed to write a '{typeof(T).FullName}' into {size} measured "
                        + "byte(s). Measure and Write disagree, or the writer latched a fault. This "
                        + "type has a generated formatter, so it is not retried with protobuf-net."
                );
            }

            return new WProtoWriteResult(size, resized);
        }

        /// <summary>
        /// Serializes <paramref name="value"/> when a formatter is registered for
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <param name="bytes">Receives the payload, or <c>null</c> when unhandled.</param>
        /// <returns><c>true</c> when WallstopProto served the request.</returns>
        /// <remarks>
        /// Allocates an array sized exactly to the payload. A caller that serializes repeatedly
        /// should prefer <see cref="Serialize{T}"/>, which reuses a buffer it is given.
        /// </remarks>
        public static bool TrySerialize<T>(T value, out byte[] bytes)
        {
            // Starting from null is what makes the delegation exact: Serialize allocates precisely
            // `size` when it has nothing to reuse, so the array handed back is never oversized and
            // the caller does not need the written count to interpret it.
            byte[] buffer = null;
            WProtoWriteResult result = Serialize(value, ref buffer);

            if (!result.Served)
            {
                bytes = null;
                return false;
            }

            // A null root and an empty contract both encode to nothing, and neither allocates.
            bytes = result.Length == 0 ? Array.Empty<byte>() : buffer;
            return true;
        }

        /// <summary>
        /// Deserializes <paramref name="data"/> when a formatter is registered for
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="data">The payload.</param>
        /// <param name="value">Receives the value, or <c>default</c> when unhandled.</param>
        /// <returns><c>true</c> when WallstopProto served the request.</returns>
        public static bool TryDeserialize<T>(ReadOnlySpan<byte> data, out T value)
        {
            if (!WProtoFormatterProvider.TryGet(out IWProtoFormatter<T> formatter))
            {
                value = default;
                return false;
            }

            WProtoReader reader = new WProtoReader(data);
            if (formatter.TryRead(ref reader, out value))
            {
                return true;
            }

            // "No formatter for this type" and "this type's formatter rejected the payload" are
            // different answers, and only the first one means "not ours". Reporting both as false
            // sent a payload WallstopProto had already refused on to protobuf-net for a second,
            // differently-implemented decode -- which under IL2CPP is the path that cannot run at
            // all, so the real error would surface as a reflection failure somewhere else entirely.
            throw new InvalidOperationException(
                $"WallstopProto could not read a '{typeof(T).FullName}' from {data.Length} byte(s): "
                    + "the payload is truncated, malformed, or was written by a different contract. "
                    + "This type has a generated formatter, so it is not retried with protobuf-net."
            );
        }

        /// <summary>
        /// Reports whether <typeparamref name="T"/> can currently be served, formatter aside.
        /// </summary>
        private static bool CanServe<T>(T value)
        {
            // A null reference has no runtime type to compare, and its encoding is the empty payload
            // either way, so it is served.
            if (typeof(T).IsValueType || value == null)
            {
                return true;
            }

            // Exact match only. protobuf-net resolves a subtype through its base's model; this
            // package's formatters are per-declared-type, and serving a subtype through the base
            // would drop everything the subtype declares.
            return value.GetType() == typeof(T);
        }
    }
}
