// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Threading;

    /// <summary>
    /// Encodes one field whose type is only known when a generic contract is closed.
    /// </summary>
    /// <typeparam name="T">The closed type argument.</typeparam>
    /// <remarks>
    /// <para>
    /// A generic contract cannot be emitted with a constant wire type: measured against
    /// protobuf-net, <c>Box&lt;int&gt;.Value</c> is <c>08 01</c>, <c>Box&lt;double&gt;</c> is
    /// <c>09 …</c> and <c>Box&lt;string&gt;</c> is <c>0A …</c> — the field key changes with
    /// <c>T</c>. So the whole per-field decision lives here, in one place, and the generator emits a
    /// call rather than a shape.
    /// </para>
    /// <para>
    /// This is a closed generic type, so IL2CPP compiles a copy per <c>T</c> ahead of time exactly
    /// as it does for <c>WProtoFormatterProvider.Get&lt;T&gt;()</c>. There is no reflection, no
    /// <c>MakeGenericType</c>, and the resolution is a static field read after the first use.
    /// </para>
    /// </remarks>
    public static class WProtoGeneric<T>
    {
        private static IWProtoScalarFormatter<T> _scalar;
        private static IWProtoFormatter<T> _message;
        private static bool _resolved;

        /// <summary>The wire type a field of this type carries in its key.</summary>
        public static int WireType
        {
            get
            {
                Resolve();
                return _scalar != null ? _scalar.WireType : WProtoWireType.LengthDelimited;
            }
        }

        /// <summary>
        /// Reports whether a field of this type accepts <paramref name="wireType"/> on read.
        /// </summary>
        /// <param name="wireType">The wire type from the field's key.</param>
        /// <returns><c>true</c> when the value can be decoded from that wire type.</returns>
        public static bool Accepts(int wireType)
        {
            return wireType == WireType;
        }

        /// <summary>
        /// Reports whether a repeated field of this type can arrive as a packed run.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only the fixed-width and varint scalars pack. The distinction matters because a packed run
        /// is length-delimited, so for a <c>T</c> that is <b>already</b> length-delimited -- a string,
        /// a byte array, a message -- a length-delimited field is one ELEMENT and not a run of them.
        /// Reading it as a run would decode a string's characters as separate values.
        /// </para>
        /// <para>
        /// This has to be a property of the closed type rather than of emitted code, for the same
        /// reason <see cref="WireType"/> does: at generate time <c>T</c> is not yet known.
        /// </para>
        /// </remarks>
        public static bool Packable
        {
            get
            {
                Resolve();
                return _scalar != null && _scalar.WireType != WProtoWireType.LengthDelimited;
            }
        }

        /// <summary>
        /// Returns the encoded size of the field, key included, or 0 when it is omitted.
        /// </summary>
        /// <param name="tag">The field number.</param>
        /// <param name="value">The value.</param>
        /// <returns>The encoded size in bytes.</returns>
        public static int MeasureField(int tag, in T value)
        {
            return MeasureField(tag, value, false);
        }

        /// <summary>
        /// Returns the encoded size of the field, key included, or 0 when it is omitted.
        /// </summary>
        /// <param name="tag">The field number.</param>
        /// <param name="value">The value.</param>
        /// <param name="required">Whether the member declared <c>IsRequired</c>.</param>
        /// <returns>The encoded size in bytes.</returns>
        /// <remarks>
        /// What <c>IsRequired</c> does is decided by the closure, which is why it is a parameter here
        /// rather than a constant in emitted code. Measured against protobuf-net 3.2.56: a required
        /// <c>int</c> at 0 is written, while a required <c>null</c> string is still absent. Required
        /// forces a VALUE onto the wire; it does not invent one where there is no value at all.
        /// </remarks>
        public static int MeasureField(int tag, in T value, bool required)
        {
            Resolve();

            if (_scalar != null)
            {
                return Omit(value, required)
                    ? 0
                    : WProtoSizes.TagSize(tag) + _scalar.MeasureValue(value);
            }

            // A message: a null reference is omitted, a struct is always written. Both measured.
            if (!typeof(T).IsValueType && value == null)
            {
                return 0;
            }

            return WProtoSizes.TagSize(tag) + WProtoSizes.MessageSize(Message(), value);
        }

        /// <summary>Writes the field, key included, or nothing when it is omitted.</summary>
        /// <param name="writer">The destination.</param>
        /// <param name="tag">The field number.</param>
        /// <param name="value">The value.</param>
        /// <returns><c>true</c> when the field was written or deliberately skipped.</returns>
        public static bool WriteField(ref WProtoWriter writer, int tag, in T value)
        {
            return WriteField(ref writer, tag, value, false);
        }

        /// <summary>Writes the field, key included, or nothing when it is omitted.</summary>
        /// <param name="writer">The destination.</param>
        /// <param name="tag">The field number.</param>
        /// <param name="value">The value.</param>
        /// <param name="required">Whether the member declared <c>IsRequired</c>.</param>
        /// <returns><c>true</c> when the field was written or deliberately skipped.</returns>
        public static bool WriteField(ref WProtoWriter writer, int tag, in T value, bool required)
        {
            Resolve();

            if (_scalar != null)
            {
                if (Omit(value, required))
                {
                    return true;
                }

                return writer.TryWriteTag(tag, _scalar.WireType)
                    && _scalar.WriteValue(ref writer, value);
            }

            if (!typeof(T).IsValueType && value == null)
            {
                return true;
            }

            return writer.TryWriteMessage(tag, Message(), value);
        }

        /// <summary>
        /// Returns the encoded size of the value as a repeated element, key included.
        /// </summary>
        /// <param name="tag">The field number.</param>
        /// <param name="value">The element.</param>
        /// <returns>The encoded size in bytes.</returns>
        /// <remarks>
        /// Unlike a member, every element is written -- including one equal to its type's default.
        /// A <c>null</c> element has no encoding at all and is refused rather than invented.
        /// </remarks>
        public static int MeasureElement(int tag, in T value)
        {
            Resolve();

            if (_scalar != null)
            {
                if (!typeof(T).IsValueType && value == null)
                {
                    throw WProtoRepeated.NullElement(typeof(T).Name, "element", typeof(T).FullName);
                }

                return WProtoSizes.TagSize(tag) + _scalar.MeasureValue(value);
            }

            if (!typeof(T).IsValueType && value == null)
            {
                throw WProtoRepeated.NullElement(typeof(T).Name, "element", typeof(T).FullName);
            }

            return WProtoSizes.TagSize(tag) + WProtoSizes.MessageSize(Message(), value);
        }

        /// <summary>Writes the value as a repeated element, key included.</summary>
        /// <param name="writer">The destination.</param>
        /// <param name="tag">The field number.</param>
        /// <param name="value">The element.</param>
        /// <returns><c>true</c> when the element was written.</returns>
        public static bool WriteElement(ref WProtoWriter writer, int tag, in T value)
        {
            Resolve();

            if (!typeof(T).IsValueType && value == null)
            {
                throw WProtoRepeated.NullElement(typeof(T).Name, "element", typeof(T).FullName);
            }

            if (_scalar != null)
            {
                return writer.TryWriteTag(tag, _scalar.WireType)
                    && _scalar.WriteValue(ref writer, value);
            }

            return writer.TryWriteMessage(tag, Message(), value);
        }

        /// <summary>
        /// Returns the encoded size of the value with <b>no field key</b>, for a packed run.
        /// </summary>
        /// <param name="value">The element.</param>
        /// <returns>The encoded size in bytes.</returns>
        /// <remarks>
        /// Only meaningful when <see cref="Packable"/>; a caller that asks for it otherwise is
        /// building a run that cannot be parsed, so this refuses rather than inventing a size.
        /// </remarks>
        public static int MeasureValue(in T value)
        {
            Resolve();

            if (_scalar == null)
            {
                throw new InvalidOperationException(
                    $"'{typeof(T).FullName}' is not a packable scalar, so it has no keyless size. "
                        + "Check WProtoGeneric<T>.Packable before building a packed run."
                );
            }

            return _scalar.MeasureValue(value);
        }

        /// <summary>Writes the value with <b>no field key</b>, as an element of a packed run.</summary>
        /// <param name="writer">The destination.</param>
        /// <param name="value">The element.</param>
        /// <returns><c>true</c> when the value was written.</returns>
        public static bool WriteValue(ref WProtoWriter writer, in T value)
        {
            Resolve();

            if (_scalar == null)
            {
                throw new InvalidOperationException(
                    $"'{typeof(T).FullName}' is not a packable scalar, so it cannot be written into "
                        + "a packed run. Check WProtoGeneric<T>.Packable first."
                );
            }

            return _scalar.WriteValue(ref writer, value);
        }

        /// <summary>Reads a value written by <see cref="WriteField"/> or <see cref="WriteElement"/>.</summary>
        /// <param name="reader">The source.</param>
        /// <param name="value">Receives the value, or <c>default</c> on failure.</param>
        /// <returns><c>true</c> when a value was read.</returns>
        public static bool TryReadValue(ref WProtoReader reader, out T value)
        {
            Resolve();

            if (_scalar != null)
            {
                return _scalar.TryReadValue(ref reader, out value);
            }

            return reader.TryReadMessage(Message(), out value);
        }

        /// <summary>
        /// Reports whether a scalar member is left off the wire.
        /// </summary>
        /// <remarks>
        /// The two rules differ only for a value type. For a reference scalar -- a string, a byte
        /// array -- <c>IsDefault</c> already means "null", so a required one omits exactly when an
        /// optional one does, and an EMPTY string is written either way.
        /// </remarks>
        private static bool Omit(in T value, bool required)
        {
            if (!required)
            {
                return _scalar.IsDefault(value);
            }

            return !typeof(T).IsValueType && value == null;
        }

        private static IWProtoFormatter<T> Message()
        {
            return _message ?? WProtoFormatterProvider.Get<T>();
        }

        /// <summary>
        /// Caches which of the two formatter kinds serves <typeparamref name="T"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>The flag is published last, and that ordering is the whole point.</b> Setting
        /// <c>_resolved</c> first left a window where a second thread saw "already resolved" and read
        /// <c>_scalar</c> and <c>_message</c> while both were still <c>null</c> -- so a scalar
        /// <c>T</c> took the message path and produced a length-delimited field where the oracle
        /// writes a varint. Silently wrong bytes, not a crash.
        /// </para>
        /// <para>
        /// Deliberately lock-free rather than double-checked locking. Resolution is pure and
        /// deterministic, so two threads racing here compute the <b>same</b> answer and the worst
        /// case is doing it twice, once, at startup. <see cref="Volatile"/> is what makes it correct:
        /// the write releases the field stores that precede it, and the read acquires them, so a
        /// thread that sees the flag set cannot see stale fields. A lock would add contention to a
        /// path taken for every field of every message.
        /// </para>
        /// </remarks>
        private static void Resolve()
        {
            if (Volatile.Read(ref _resolved))
            {
                return;
            }

            IWProtoScalarFormatter<T> scalar;
            IWProtoFormatter<T> message = null;
            if (!WProtoScalarFormatterProvider.TryGet(out scalar))
            {
                WProtoFormatterProvider.TryGet(out message);
            }

            _scalar = scalar;
            _message = message;
            Volatile.Write(ref _resolved, true);
        }

        /// <summary>
        /// Forgets a cached resolution, so a formatter registered later is picked up.
        /// </summary>
        /// <remarks>
        /// Registration is meant to happen once during startup, before anything serializes, and the
        /// cache exists so a per-element resolution is a field read. A test that registers a
        /// formatter after this type has already resolved would otherwise see the stale answer.
        /// </remarks>
        public static void Reset()
        {
            // Cleared in the MIRROR of the publish order: the flag goes down first, so no thread can
            // observe "resolved" alongside the nulls this is about to write. Nulling the fields first
            // would open exactly the window Resolve was fixed to close.
            Volatile.Write(ref _resolved, false);
            _scalar = null;
            _message = null;
        }
    }
}
