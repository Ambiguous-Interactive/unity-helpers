// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Runtime.CompilerServices;
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
        /// <remarks>
        /// Last registration wins. Registration is not thread-safe against concurrent resolution
        /// and is meant to run once during startup, before anything serializes.
        /// </remarks>
        public static void Register<T>(IWProtoScalarFormatter<T> formatter)
        {
            Cache<T>.Formatter = formatter;
            WProtoGeneric<T>.Reset();
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

        /// <summary>Creates the scalar formatter for a concrete enum closure.</summary>
        /// <typeparam name="T">The enum type named by generated code.</typeparam>
        /// <param name="size">The size of the enum's underlying integer, in bytes.</param>
        /// <param name="signed">Whether the underlying integer is signed.</param>
        /// <returns>A reflection-free enum formatter.</returns>
        /// <remarks>
        /// Generated registrars call this with constants obtained from Roslyn. Keeping the numeric
        /// shape in generated source lets generic contracts encode enum arguments without
        /// <c>Enum.GetUnderlyingType</c>, boxing, or a reflective formatter factory under IL2CPP.
        /// </remarks>
        public static IWProtoScalarFormatter<T> Enum<T>(int size, bool signed)
            where T : struct
        {
            if (size != 1 && size != 2 && size != 4 && size != 8)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(size),
                    size,
                    "Expected 1, 2, 4, or 8."
                );
            }

            if (Unsafe.SizeOf<T>() != size)
            {
                throw new ArgumentException(
                    $"The declared enum size {size} does not match {typeof(T).FullName}.",
                    nameof(size)
                );
            }

            return new EnumFormatter<T>(size, signed);
        }

        private static void RegisterBuiltIns()
        {
            WProtoScalarFormatterProvider.Register(new Int32Formatter());
            WProtoScalarFormatterProvider.Register(new Int64Formatter());
            WProtoScalarFormatterProvider.Register(new UInt32Formatter());
            WProtoScalarFormatterProvider.Register(new UInt64Formatter());
            WProtoScalarFormatterProvider.Register(new Int16Formatter());
            WProtoScalarFormatterProvider.Register(new UInt16Formatter());
            WProtoScalarFormatterProvider.Register(new CharFormatter());
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

        private sealed class EnumFormatter<T> : IWProtoScalarFormatter<T>
            where T : struct
        {
            private readonly int _size;
            private readonly bool _signed;

            internal EnumFormatter(int size, bool signed)
            {
                _size = size;
                _signed = signed;
            }

            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in T value) => Numeric(value) == 0;

            public int MeasureValue(in T value) =>
                _size == 8
                    ? WProtoSizes.Int64Size(Numeric(value))
                    : WProtoSizes.Int32Size(unchecked((int)Numeric(value)));

            public bool WriteValue(ref WProtoWriter writer, in T value) =>
                _size == 8
                    ? writer.TryWriteInt64(Numeric(value))
                    : writer.TryWriteInt32(unchecked((int)Numeric(value)));

            public bool TryReadValue(ref WProtoReader reader, out T value)
            {
                long numeric;
                if (_size == 8)
                {
                    if (!reader.TryReadInt64(out numeric))
                    {
                        value = default;
                        return false;
                    }
                }
                else
                {
                    if (!reader.TryReadInt32(out int narrow))
                    {
                        value = default;
                        return false;
                    }

                    numeric = narrow;
                }

                value = FromNumeric(numeric);
                return true;
            }

            private long Numeric(in T value)
            {
                ref T valueRef = ref Unsafe.AsRef(in value);
                switch (_size)
                {
                    case 1:
                        return _signed
                            ? Unsafe.As<T, sbyte>(ref valueRef)
                            : Unsafe.As<T, byte>(ref valueRef);
                    case 2:
                        return _signed
                            ? Unsafe.As<T, short>(ref valueRef)
                            : Unsafe.As<T, ushort>(ref valueRef);
                    case 4:
                        return _signed
                            ? Unsafe.As<T, int>(ref valueRef)
                            : unchecked((int)Unsafe.As<T, uint>(ref valueRef));
                    default:
                        return Unsafe.As<T, long>(ref valueRef);
                }
            }

            private T FromNumeric(long numeric)
            {
                T value = default;
                switch (_size)
                {
                    case 1:
                        Unsafe.As<T, byte>(ref value) = unchecked((byte)numeric);
                        break;
                    case 2:
                        Unsafe.As<T, ushort>(ref value) = unchecked((ushort)numeric);
                        break;
                    case 4:
                        Unsafe.As<T, uint>(ref value) = unchecked((uint)numeric);
                        break;
                    default:
                        Unsafe.As<T, ulong>(ref value) = unchecked((ulong)numeric);
                        break;
                }

                return value;
            }
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

        /// <remarks>
        /// A code unit rides the same plain varint the unsigned 32-bit shape uses, and a member at
        /// its default is omitted exactly like zero -- which is why this lives beside the integer
        /// scalars rather than with the wrapped base-class-library messages. It opts out of packing
        /// through <see cref="IWProtoNeverPacked"/> because the oracle writes every repeated char
        /// under its own field key.
        /// </remarks>
        private sealed class CharFormatter : IWProtoScalarFormatter<char>, IWProtoNeverPacked
        {
            public int WireType => WProtoWireType.Varint;

            public bool IsDefault(in char value) => value == '\0';

            public int MeasureValue(in char value) => WProtoSizes.Varint32Size(value);

            public bool WriteValue(ref WProtoWriter writer, in char value) =>
                writer.TryWriteVarint32(value);

            public bool TryReadValue(ref WProtoReader reader, out char value)
            {
                if (!reader.TryReadVarint32(out uint raw))
                {
                    value = default(char);
                    return false;
                }

                value = (char)raw;
                return true;
            }
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
