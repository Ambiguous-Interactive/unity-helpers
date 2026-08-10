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
        /// Serializes <paramref name="value"/> when a formatter is registered for
        /// <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The declared type.</typeparam>
        /// <param name="value">The value to serialize.</param>
        /// <param name="bytes">Receives the payload, or <c>null</c> when unhandled.</param>
        /// <returns><c>true</c> when WallstopProto served the request.</returns>
        public static bool TrySerialize<T>(T value, out byte[] bytes)
        {
            bytes = null;

            if (
                !CanServe(value)
                || !WProtoFormatterProvider.TryGet(out IWProtoFormatter<T> formatter)
            )
            {
                return false;
            }

            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            if (!formatter.Write(ref writer, value))
            {
                return false;
            }

            bytes = buffer;
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
            value = default;

            if (!WProtoFormatterProvider.TryGet(out IWProtoFormatter<T> formatter))
            {
                return false;
            }

            WProtoReader reader = new WProtoReader(data);
            return formatter.TryRead(ref reader, out value);
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
