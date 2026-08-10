// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Threading;

    /// <summary>
    /// Resolves the <see cref="IWProtoScalarFormatter{T}"/> for a non-message type.
    /// </summary>
    /// <remarks>
    /// A second registry beside <see cref="WProtoFormatterProvider"/> rather than an entry in it,
    /// because the two answer different questions: that one says "how is this message's payload
    /// encoded", this one says "what wire type does this value carry and how is it written raw".
    /// A type has at most one of the two.
    /// </remarks>
    public static class WProtoScalarFormatterProvider
    {
        /// <summary>
        /// Registers <paramref name="formatter"/> for <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="formatter">The formatter, or <c>null</c> to clear the registration.</param>
        public static void Register<T>(IWProtoScalarFormatter<T> formatter)
        {
            Cache<T>.Formatter = formatter;
        }

        /// <summary>
        /// Gets the scalar formatter for <typeparamref name="T"/> without throwing.
        /// </summary>
        /// <typeparam name="T">The value type.</typeparam>
        /// <param name="formatter">Receives the formatter, or <c>null</c>.</param>
        /// <returns><c>true</c> when one is registered.</returns>
        public static bool TryGet<T>(out IWProtoScalarFormatter<T> formatter)
        {
            formatter = Cache<T>.Formatter;
            return formatter != null;
        }

        private static class Cache<T>
        {
            internal static IWProtoScalarFormatter<T> Formatter;
        }
    }

    /// <summary>
    /// The scalar formatters this package ships, one per shape the wire core already understands.
    /// </summary>
    public static class WProtoScalarFormatters
    {
        private static readonly object RegistrationGate = new object();

        private static bool _registered;

        /// <summary>
        /// Registers every built-in scalar formatter. Idempotent.
        /// </summary>
        /// <remarks>
        /// Called from <c>WProtoBootstrap</c> inside Unity. Outside a Unity runtime -- a plain
        /// <c>dotnet test</c> harness, say -- call it directly.
        /// </remarks>
        public static void RegisterAll()
        {
            if (Volatile.Read(ref _registered))
            {
                return;
            }

            // Locked, unlike WProtoGeneric's resolution, and for a reason worth stating: this is not
            // an idempotent computation of one value but thirteen writes to thirteen different
            // caches, so a second thread that raced past the flag could serialize against a
            // HALF-REGISTERED provider -- finding no formatter for `int` and encoding it as a message.
            // The flag used to be set before the registrations, which made that window certain rather
            // than merely possible. It runs once at startup, so the lock costs nothing measurable.
            lock (RegistrationGate)
            {
                if (_registered)
                {
                    return;
                }

                RegisterBuiltIns();

                // Last, and volatile: a reader that sees this cannot see an unregistered provider.
                Volatile.Write(ref _registered, true);
            }
        }

        private static void RegisterBuiltIns()
        {
            WProtoScalarFormatterProvider.Register(new Int32Formatter());
            WProtoScalarFormatterProvider.Register(new Int64Formatter());
            WProtoScalarFormatterProvider.Register(new UInt32Formatter());
            WProtoScalarFormatterProvider.Register(new UInt64Formatter());
            WProtoScalarFormatterProvider.Register(new Int16Formatter());
            WProtoScalarFormatterProvider.Register(new UInt16Formatter());
            WProtoScalarFormatterProvider.Register(new SByteFormatter());
            WProtoScalarFormatterProvider.Register(new ByteFormatter());
            WProtoScalarFormatterProvider.Register(new BooleanFormatter());
            WProtoScalarFormatterProvider.Register(new SingleFormatter());
            WProtoScalarFormatterProvider.Register(new DoubleFormatter());
            WProtoScalarFormatterProvider.Register(new StringFormatter());
            WProtoScalarFormatterProvider.Register(new BytesFormatter());
        }

        private sealed class Int32Formatter : IWProtoScalarFormatter<int>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in int value) => value == 0;

            public int MeasureValue(in int value) => WProtoSizes.Int32Size(value);

            public bool WriteValue(ref WProtoWriter writer, in int value) =>
                writer.TryWriteInt32(value);

            public bool TryReadValue(ref WProtoReader reader, out int value) =>
                reader.TryReadInt32(out value);
        }

        private sealed class Int64Formatter : IWProtoScalarFormatter<long>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in long value) => value == 0;

            public int MeasureValue(in long value) => WProtoSizes.Int64Size(value);

            public bool WriteValue(ref WProtoWriter writer, in long value) =>
                writer.TryWriteInt64(value);

            public bool TryReadValue(ref WProtoReader reader, out long value) =>
                reader.TryReadInt64(out value);
        }

        private sealed class UInt32Formatter : IWProtoScalarFormatter<uint>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in uint value) => value == 0;

            public int MeasureValue(in uint value) => WProtoSizes.Varint32Size(value);

            public bool WriteValue(ref WProtoWriter writer, in uint value) =>
                writer.TryWriteVarint32(value);

            public bool TryReadValue(ref WProtoReader reader, out uint value) =>
                reader.TryReadVarint32(out value);
        }

        private sealed class UInt64Formatter : IWProtoScalarFormatter<ulong>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in ulong value) => value == 0;

            public int MeasureValue(in ulong value) => WProtoSizes.Varint64Size(value);

            public bool WriteValue(ref WProtoWriter writer, in ulong value) =>
                writer.TryWriteVarint64(value);

            public bool TryReadValue(ref WProtoReader reader, out ulong value) =>
                reader.TryReadVarint64(out value);
        }

        private sealed class Int16Formatter : IWProtoScalarFormatter<short>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in short value) => value == 0;

            public int MeasureValue(in short value) => WProtoSizes.Int32Size(value);

            public bool WriteValue(ref WProtoWriter writer, in short value) =>
                writer.TryWriteInt32(value);

            public bool TryReadValue(ref WProtoReader reader, out short value)
            {
                if (!reader.TryReadInt32(out int decoded))
                {
                    value = 0;
                    return false;
                }

                value = (short)decoded;
                return true;
            }
        }

        private sealed class UInt16Formatter : IWProtoScalarFormatter<ushort>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in ushort value) => value == 0;

            public int MeasureValue(in ushort value) => WProtoSizes.Varint32Size(value);

            public bool WriteValue(ref WProtoWriter writer, in ushort value) =>
                writer.TryWriteVarint32(value);

            public bool TryReadValue(ref WProtoReader reader, out ushort value)
            {
                if (!reader.TryReadVarint32(out uint decoded))
                {
                    value = 0;
                    return false;
                }

                value = (ushort)decoded;
                return true;
            }
        }

        private sealed class SByteFormatter : IWProtoScalarFormatter<sbyte>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in sbyte value) => value == 0;

            public int MeasureValue(in sbyte value) => WProtoSizes.Int32Size(value);

            public bool WriteValue(ref WProtoWriter writer, in sbyte value) =>
                writer.TryWriteInt32(value);

            public bool TryReadValue(ref WProtoReader reader, out sbyte value)
            {
                if (!reader.TryReadInt32(out int decoded))
                {
                    value = 0;
                    return false;
                }

                value = (sbyte)decoded;
                return true;
            }
        }

        private sealed class ByteFormatter : IWProtoScalarFormatter<byte>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in byte value) => value == 0;

            public int MeasureValue(in byte value) => WProtoSizes.Varint32Size(value);

            public bool WriteValue(ref WProtoWriter writer, in byte value) =>
                writer.TryWriteVarint32(value);

            public bool TryReadValue(ref WProtoReader reader, out byte value)
            {
                if (!reader.TryReadVarint32(out uint decoded))
                {
                    value = 0;
                    return false;
                }

                value = (byte)decoded;
                return true;
            }
        }

        private sealed class BooleanFormatter : IWProtoScalarFormatter<bool>
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in bool value) => !value;

            public int MeasureValue(in bool value) => 1;

            public bool WriteValue(ref WProtoWriter writer, in bool value) =>
                writer.TryWriteBool(value);

            public bool TryReadValue(ref WProtoReader reader, out bool value) =>
                reader.TryReadBool(out value);
        }

        private sealed class SingleFormatter : IWProtoScalarFormatter<float>
        {
            public int WireType => WProtoWireType.Fixed32;

            public bool IsDefault(in float value) => value == 0f;

            public int MeasureValue(in float value) => 4;

            public bool WriteValue(ref WProtoWriter writer, in float value) =>
                writer.TryWriteSingle(value);

            public bool TryReadValue(ref WProtoReader reader, out float value) =>
                reader.TryReadSingle(out value);
        }

        private sealed class DoubleFormatter : IWProtoScalarFormatter<double>
        {
            public int WireType => WProtoWireType.Fixed64;

            public bool IsDefault(in double value) => value == 0d;

            public int MeasureValue(in double value) => 8;

            public bool WriteValue(ref WProtoWriter writer, in double value) =>
                writer.TryWriteDouble(value);

            public bool TryReadValue(ref WProtoReader reader, out double value) =>
                reader.TryReadDouble(out value);
        }

        private sealed class StringFormatter : IWProtoScalarFormatter<string>
        {
            public int WireType => WProtoWireType.LengthDelimited;

            // Only null is absent. An empty string is written as a tag and a zero length -- measured,
            // and the distinction the omission rule cannot otherwise express.
            public bool IsDefault(in string value) => value == null;

            public int MeasureValue(in string value) => WProtoSizes.StringSize(value);

            public bool WriteValue(ref WProtoWriter writer, in string value) =>
                writer.TryWriteString(value);

            public bool TryReadValue(ref WProtoReader reader, out string value) =>
                reader.TryReadString(out value);
        }

        private sealed class BytesFormatter : IWProtoScalarFormatter<byte[]>
        {
            public int WireType => WProtoWireType.LengthDelimited;

            public bool IsDefault(in byte[] value) => value == null;

            public int MeasureValue(in byte[] value) =>
                WProtoSizes.LengthDelimitedSize(value.Length);

            public bool WriteValue(ref WProtoWriter writer, in byte[] value) =>
                writer.TryWriteBytes(value);

            public bool TryReadValue(ref WProtoReader reader, out byte[] value)
            {
                if (!reader.TryReadBytes(out ReadOnlySpan<byte> decoded))
                {
                    value = null;
                    return false;
                }

                value = decoded.ToArray();
                return true;
            }
        }
    }
}
