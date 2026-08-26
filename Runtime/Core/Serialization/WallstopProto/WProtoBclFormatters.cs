// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using System.Buffers.Binary;
    using System.Runtime.InteropServices;
    using System.Text;

    /// <summary>
    /// Shared pieces of the wire encoding protobuf-net gives the base-class-library value types,
    /// reproduced field-for-field so saves written by either serializer read as the other wrote
    /// them.
    /// </summary>
    /// <remarks>
    /// Measured against protobuf-net 2.4.9 and 3.2.56: both majors emit identical bytes for every
    /// <see cref="DateTime"/>, <see cref="TimeSpan"/>, <see cref="Guid"/>, <see cref="decimal"/>,
    /// <see cref="char"/> and single-valued <see cref="Uri"/> probed, so one implementation serves
    /// both. The remaining base-class-library shapes stay refused deliberately rather than "yet":
    /// a <see cref="DateTimeOffset"/> has no oracle encoding in either major, the two pointer
    /// types are refused by 2.x outright while their serialized value outlives nothing it points
    /// at, and a serialized <see cref="Type"/> is a runtime-bound assembly-qualified name that one
    /// machine cannot read back on another.
    /// </remarks>
    internal static class WProtoBcl
    {
        private static readonly IWProtoFormatter<DateTime> DateTimeRoot =
            new WProtoBclRootFormatter<DateTime>();

        private static readonly IWProtoFormatter<TimeSpan> TimeSpanRoot =
            new WProtoBclRootFormatter<TimeSpan>();

        private static readonly IWProtoFormatter<Guid> GuidRoot =
            new WProtoBclRootFormatter<Guid>();

        private static readonly IWProtoFormatter<decimal> DecimalRoot =
            new WProtoBclRootFormatter<decimal>();

        private static readonly IWProtoFormatter<Uri> UriRoot = new WProtoBclRootFormatter<Uri>();

        private static readonly IWProtoFormatter<char> CharRoot = new WProtoCharRootFormatter();

        /// <summary>The scale identifiers protobuf-net's <c>.bcl.TimeSpan</c> message carries.</summary>
        internal const int ScaleDays = 0;
        internal const int ScaleHours = 1;
        internal const int ScaleMinutes = 2;
        internal const int ScaleSeconds = 3;
        internal const int ScaleMilliseconds = 4;
        internal const int ScaleTicks = 5;

        /// <summary>
        /// The sentinel scale for the duration extremes, which travel as ±1 rather than as a tick
        /// count.
        /// </summary>
        internal const int ScaleMinMax = 15;

        /// <summary>1970-01-01, the instant every DateTime delta is measured from.</summary>
        internal const long EpochTicks = 621355968000000000L;

        private const long TicksPerDay = 864000000000L;
        private const long TicksPerHour = 36000000000L;
        private const long TicksPerMinute = 600000000L;
        private const long TicksPerSecond = 10000000L;
        private const long TicksPerMillisecond = 10000L;

        /// <summary>
        /// Splits a tick count into the scaled magnitude and unit identifier the wire carries.
        /// </summary>
        /// <param name="ticks">The duration in ticks.</param>
        /// <param name="value">The scaled magnitude, ZigZag-encoded on the wire.</param>
        /// <param name="scale">The unit identifier; omitted from the wire when it is days.</param>
        /// <remarks>
        /// The largest whole unit wins -- three and a quarter hours travels as 195 minutes, not
        /// 11,700 seconds -- which is the ladder protobuf-net walks and the one place a deviation
        /// turns into different bytes for an ordinary duration.
        /// </remarks>
        internal static ScaledTicks SplitTicks(long ticks)
        {
            if (ticks % TicksPerDay == 0)
            {
                return new ScaledTicks(ticks / TicksPerDay, ScaleDays);
            }

            if (ticks % TicksPerHour == 0)
            {
                return new ScaledTicks(ticks / TicksPerHour, ScaleHours);
            }

            if (ticks % TicksPerMinute == 0)
            {
                return new ScaledTicks(ticks / TicksPerMinute, ScaleMinutes);
            }

            if (ticks % TicksPerSecond == 0)
            {
                return new ScaledTicks(ticks / TicksPerSecond, ScaleSeconds);
            }

            if (ticks % TicksPerMillisecond == 0)
            {
                return new ScaledTicks(ticks / TicksPerMillisecond, ScaleMilliseconds);
            }

            return new ScaledTicks(ticks, ScaleTicks);
        }

        /// <summary>
        /// Rebuilds a tick count from a scaled magnitude and unit identifier off the wire.
        /// </summary>
        /// <param name="value">The scaled magnitude.</param>
        /// <param name="scale">The unit identifier.</param>
        /// <param name="ticks">The rebuilt duration in ticks.</param>
        /// <returns><c>false</c> when the scale is unknown, the payload is a MinMax sentinel that
        /// is not ±1, or the multiplication overflows.</returns>
        internal static bool TryJoinTicks(long value, int scale, out long ticks)
        {
            switch (scale)
            {
                case ScaleDays:
                {
                    return CheckedMultiply(value, TicksPerDay, out ticks);
                }
                case ScaleHours:
                {
                    return CheckedMultiply(value, TicksPerHour, out ticks);
                }
                case ScaleMinutes:
                {
                    return CheckedMultiply(value, TicksPerMinute, out ticks);
                }
                case ScaleSeconds:
                {
                    return CheckedMultiply(value, TicksPerSecond, out ticks);
                }
                case ScaleMilliseconds:
                {
                    return CheckedMultiply(value, TicksPerMillisecond, out ticks);
                }
                case ScaleTicks:
                {
                    ticks = value;
                    return true;
                }
                case ScaleMinMax:
                {
                    switch (value)
                    {
                        case 1:
                        {
                            ticks = long.MaxValue;
                            return true;
                        }
                        case -1:
                        {
                            ticks = long.MinValue;
                            return true;
                        }
                        default:
                        {
                            ticks = 0;
                            return false;
                        }
                    }
                }
                default:
                {
                    ticks = 0;
                    return false;
                }
            }
        }

        private static bool CheckedMultiply(long value, long unit, out long ticks)
        {
            if (
                value == 0L
                || (0L < value ? value <= long.MaxValue / unit : long.MinValue / unit <= value)
            )
            {
                ticks = value * unit;
                return true;
            }

            ticks = 0;
            return false;
        }

        /// <summary>The byte size of the optional scaled-value and scale fields.</summary>
        internal static int FieldsSize(long value, int scale)
        {
            int size = 0;
            if (value != 0L)
            {
                size += WProtoSizes.TagSize(1) + WProtoSizes.ZigZag64Size(value);
            }

            if (scale != ScaleDays)
            {
                size += WProtoSizes.TagSize(2) + WProtoSizes.Varint32Size((uint)scale);
            }

            return size;
        }

        /// <summary>Writes the optional scaled-value and scale fields, in wire order.</summary>
        internal static bool TryWriteFields(ref WProtoWriter writer, long value, int scale)
        {
            if (
                value != 0L
                && (
                    !writer.TryWriteTag(1, WProtoWireType.Varint) || !writer.TryWriteZigZag64(value)
                )
            )
            {
                return false;
            }

            return scale == ScaleDays
                || (
                    writer.TryWriteTag(2, WProtoWireType.Varint)
                    && writer.TryWriteVarint32((uint)scale)
                );
        }

        /// <summary>
        /// Reports whether a closed generic type uses the BCL scalar-with-nested-payload semantics.
        /// </summary>
        /// <summary>
        /// Reports whether a closed generic type uses one of the base-class-library encodings that
        /// is not a plain scalar.
        /// </summary>
        /// <remarks>
        /// The pointer types and <see cref="char"/> are deliberately absent: protobuf-net treats
        /// them as ordinary varint scalars, the same treatment the generated contract code gives
        /// them directly, so routing them here would length-delimit a value the oracle writes raw.
        /// </remarks>
        internal static bool IsBclType<T>()
        {
            Type type = typeof(T);
            return type == typeof(DateTime)
                || type == typeof(TimeSpan)
                || type == typeof(Guid)
                || type == typeof(decimal)
                || type == typeof(Uri);
        }

        /// <summary>Reports whether a BCL member is omitted at its declared default.</summary>
        internal static bool OmitsMember<T>(in T value)
        {
            return typeof(T) != typeof(DateTime)
                && global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(
                    value,
                    default(T)
                );
        }

        /// <summary>Registers the nested and root encodings for every supported BCL value type.</summary>
        internal static void RegisterAll()
        {
            WProtoFormatterProvider.Register(WProtoDateTimeFormatter.Instance);
            WProtoFormatterProvider.Register(WProtoTimeSpanFormatter.Instance);
            WProtoFormatterProvider.Register(WProtoGuidFormatter.Instance);
            WProtoFormatterProvider.Register(WProtoDecimalFormatter.Instance);
            WProtoFormatterProvider.Register(WProtoUriFormatter.Instance);

            WProtoRootMarshalProvider.Register(DateTimeRoot);
            WProtoRootMarshalProvider.Register(TimeSpanRoot);
            WProtoRootMarshalProvider.Register(GuidRoot);
            WProtoRootMarshalProvider.Register(DecimalRoot);
            WProtoRootMarshalProvider.Register(UriRoot);
            WProtoRootMarshalProvider.Register(CharRoot);
        }

        /// <summary>A scaled duration ready for the shared wire-field helpers.</summary>
        internal readonly struct ScaledTicks
        {
            internal readonly long Value;
            internal readonly int Scale;

            internal ScaledTicks(long value, int scale)
            {
                Value = value;
                Scale = scale;
            }
        }

        /// <summary>The three fields decoded from protobuf-net's duration message.</summary>
        internal readonly struct ScaledFields
        {
            internal readonly long Value;
            internal readonly int Scale;
            internal readonly uint Kind;

            internal ScaledFields(long value, int scale, uint kind)
            {
                Value = value;
                Scale = scale;
                Kind = kind;
            }
        }
    }

    /// <summary>
    /// Wraps a base-class-library payload in the field-one message protobuf-net uses at a root.
    /// </summary>
    internal sealed class WProtoBclRootFormatter<T>
        : IWProtoFormatter<T>,
            IWProtoConditionalFormatter
    {
        /// <inheritdoc />
        public bool CanServe()
        {
            return WProtoFormatterProvider.TryGet(out IWProtoFormatter<T> nested)
                && !(nested is IWProtoConditionalFormatter conditional && !conditional.CanServe());
        }

        /// <inheritdoc />
        public int Measure(in T value)
        {
            return WProtoSizes.TagSize(1)
                + WProtoSizes.MessageSize(WProtoFormatterProvider.Get<T>(), value);
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in T value)
        {
            return writer.TryWriteMessage(1, WProtoFormatterProvider.Get<T>(), value);
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out T value)
        {
            T candidate = default(T);
            while (reader.TryReadTag(out int fieldNumber, out int wireType))
            {
                if (fieldNumber == 1 && wireType == WProtoWireType.LengthDelimited)
                {
                    if (!reader.TryReadMessage(WProtoFormatterProvider.Get<T>(), out T occurrence))
                    {
                        value = default(T);
                        return false;
                    }

                    candidate = occurrence;
                }
                else if (!reader.TrySkipField(fieldNumber, wireType))
                {
                    value = default(T);
                    return false;
                }
            }

            if (reader.Malformed)
            {
                value = default(T);
                return false;
            }

            value = candidate;
            return true;
        }
    }

    /// <summary>
    /// Reads and writes <see cref="DateTime"/> exactly as protobuf-net's default model does: a
    /// <c>.bcl.TimeSpan</c> sub-message of ticks since 1970-01-01, with kind left off when writing.
    /// </summary>
    /// <remarks>
    /// MinValue and MaxValue travel as ±1 under the MinMax scale rather than as deltas, which is why
    /// <c>default(DateTime)</c> still produces six bytes instead of nothing. The default wire form
    /// reads as an unspecified-kind value, while a kind field produced under protobuf-net's
    /// IncludeDateTimeKind option is preserved and validated.
    /// </remarks>
    public sealed class WProtoDateTimeFormatter : IWProtoFormatter<DateTime>
    {
        /// <summary>The shared instance; the formatter holds no state.</summary>
        public static readonly WProtoDateTimeFormatter Instance = new WProtoDateTimeFormatter();

        private WProtoDateTimeFormatter() { }

        /// <inheritdoc />
        public int Measure(in DateTime value)
        {
            if (value == DateTime.MinValue)
            {
                return WProtoBcl.FieldsSize(-1L, WProtoBcl.ScaleMinMax);
            }

            if (value == DateTime.MaxValue)
            {
                return WProtoBcl.FieldsSize(1L, WProtoBcl.ScaleMinMax);
            }

            WProtoBcl.ScaledTicks scaled = WProtoBcl.SplitTicks(value.Ticks - WProtoBcl.EpochTicks);
            return WProtoBcl.FieldsSize(scaled.Value, scaled.Scale);
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in DateTime value)
        {
            if (value == DateTime.MinValue)
            {
                return WProtoBcl.TryWriteFields(ref writer, -1L, WProtoBcl.ScaleMinMax);
            }

            if (value == DateTime.MaxValue)
            {
                return WProtoBcl.TryWriteFields(ref writer, 1L, WProtoBcl.ScaleMinMax);
            }

            WProtoBcl.ScaledTicks scaled = WProtoBcl.SplitTicks(value.Ticks - WProtoBcl.EpochTicks);
            return WProtoBcl.TryWriteFields(ref writer, scaled.Value, scaled.Scale);
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out DateTime value)
        {
            if (!TryReadScaled(ref reader, out WProtoBcl.ScaledFields fields))
            {
                value = default(DateTime);
                return false;
            }

            if (2U < fields.Kind)
            {
                value = default(DateTime);
                return false;
            }

            DateTimeKind kind = (DateTimeKind)fields.Kind;

            if (fields.Scale == WProtoBcl.ScaleMinMax)
            {
                value =
                    fields.Value == 1L ? DateTime.MaxValue
                    : fields.Value == -1L ? DateTime.MinValue
                    : default(DateTime);
                return fields.Value == 1L || fields.Value == -1L;
            }

            if (!WProtoBcl.TryJoinTicks(fields.Value, fields.Scale, out long delta))
            {
                value = default(DateTime);
                return false;
            }

            try
            {
                value = new DateTime(WProtoBcl.EpochTicks + delta, kind);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                value = default(DateTime);
                return false;
            }
        }

        internal static bool TryReadScaled(
            ref WProtoReader reader,
            out WProtoBcl.ScaledFields fields
        )
        {
            long value = 0L;
            int scale = WProtoBcl.ScaleDays;
            uint kind = (uint)DateTimeKind.Unspecified;

            while (reader.TryReadTag(out int fieldNumber, out int wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == WProtoWireType.Varint:
                    {
                        if (!reader.TryReadZigZag64(out value))
                        {
                            return RefuseScaled(out fields);
                        }

                        break;
                    }
                    case 2 when wireType == WProtoWireType.Varint:
                    {
                        if (!reader.TryReadVarint32(out uint encoded))
                        {
                            return RefuseScaled(out fields);
                        }

                        scale = (int)encoded;
                        break;
                    }
                    case 3 when wireType == WProtoWireType.Varint:
                    {
                        // The kind an oracle carrying IncludeDateTimeKind writes. Every origin
                        // shares one instant, so it changes no bytes this side produces -- but an
                        // unknown value refuses in both the DateTime and TimeSpan readers.
                        if (!reader.TryReadVarint32(out kind) || 2U < kind)
                        {
                            return RefuseScaled(out fields);
                        }

                        break;
                    }
                    default:
                    {
                        if (!reader.TrySkipField(fieldNumber, wireType))
                        {
                            return RefuseScaled(out fields);
                        }

                        break;
                    }
                }
            }

            if (reader.Malformed)
            {
                return RefuseScaled(out fields);
            }

            fields = new WProtoBcl.ScaledFields(value, scale, kind);
            return true;
        }

        private static bool RefuseScaled(out WProtoBcl.ScaledFields fields)
        {
            fields = default(WProtoBcl.ScaledFields);
            return false;
        }
    }

    /// <summary>
    /// Reads and writes <see cref="TimeSpan"/> exactly as protobuf-net's default model does: a
    /// <c>.bcl.TimeSpan</c> sub-message whose count rides the largest whole unit, with the extremes
    /// travelling as ±1 sentinels.
    /// </summary>
    public sealed class WProtoTimeSpanFormatter : IWProtoFormatter<TimeSpan>
    {
        /// <summary>The shared instance; the formatter holds no state.</summary>
        public static readonly WProtoTimeSpanFormatter Instance = new WProtoTimeSpanFormatter();

        private WProtoTimeSpanFormatter() { }

        /// <inheritdoc />
        public int Measure(in TimeSpan value)
        {
            long ticks = value.Ticks;
            if (ticks == long.MaxValue || ticks == long.MinValue)
            {
                return WProtoBcl.FieldsSize(
                    ticks == long.MaxValue ? 1L : -1L,
                    WProtoBcl.ScaleMinMax
                );
            }

            WProtoBcl.ScaledTicks scaled = WProtoBcl.SplitTicks(ticks);
            return WProtoBcl.FieldsSize(scaled.Value, scaled.Scale);
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in TimeSpan value)
        {
            long ticks = value.Ticks;
            if (ticks == long.MaxValue || ticks == long.MinValue)
            {
                return WProtoBcl.TryWriteFields(
                    ref writer,
                    ticks == long.MaxValue ? 1L : -1L,
                    WProtoBcl.ScaleMinMax
                );
            }

            WProtoBcl.ScaledTicks scaled = WProtoBcl.SplitTicks(ticks);
            return WProtoBcl.TryWriteFields(ref writer, scaled.Value, scaled.Scale);
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out TimeSpan value)
        {
            if (
                !WProtoDateTimeFormatter.TryReadScaled(
                    ref reader,
                    out WProtoBcl.ScaledFields fields
                )
            )
            {
                value = default(TimeSpan);
                return false;
            }

            bool joined = WProtoBcl.TryJoinTicks(fields.Value, fields.Scale, out long ticks);
            value = joined ? new TimeSpan(ticks) : default(TimeSpan);
            return joined;
        }
    }

    /// <summary>
    /// Reads and writes <see cref="Guid"/> exactly as protobuf-net does: two fixed-64 halves in
    /// <see cref="Guid.ToByteArray"/> order, with an all-zero GUID written as an empty message.
    /// </summary>
    public sealed class WProtoGuidFormatter : IWProtoFormatter<Guid>
    {
        /// <summary>The shared instance; the formatter holds no state.</summary>
        public static readonly WProtoGuidFormatter Instance = new WProtoGuidFormatter();

        private const int ByteCount = 16;

        private WProtoGuidFormatter() { }

        /// <inheritdoc />
        public int Measure(in Guid value)
        {
            return value == Guid.Empty ? 0 : 2 * WProtoSizes.TagSize(1) + 2 * sizeof(ulong);
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in Guid value)
        {
            if (value == Guid.Empty)
            {
                return true;
            }

            Span<byte> bytes = stackalloc byte[ByteCount];
            if (!value.TryWriteBytes(bytes))
            {
                return false;
            }

            return writer.TryWriteTag(1, WProtoWireType.Fixed64)
                && writer.TryWriteFixed64(BinaryPrimitives.ReadUInt64LittleEndian(bytes))
                && writer.TryWriteTag(2, WProtoWireType.Fixed64)
                && writer.TryWriteFixed64(BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(8)));
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out Guid value)
        {
            ulong low = 0;
            ulong high = 0;

            while (reader.TryReadTag(out int fieldNumber, out int wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == WProtoWireType.Fixed64:
                    {
                        if (!reader.TryReadFixed64(out low))
                        {
                            value = Guid.Empty;
                            return false;
                        }

                        break;
                    }
                    case 2 when wireType == WProtoWireType.Fixed64:
                    {
                        if (!reader.TryReadFixed64(out high))
                        {
                            value = Guid.Empty;
                            return false;
                        }

                        break;
                    }
                    default:
                    {
                        if (!reader.TrySkipField(fieldNumber, wireType))
                        {
                            value = Guid.Empty;
                            return false;
                        }

                        break;
                    }
                }
            }

            if (reader.Malformed)
            {
                value = Guid.Empty;
                return false;
            }

            if (low == 0UL && high == 0UL)
            {
                value = Guid.Empty;
                return true;
            }

            Span<byte> bytes = stackalloc byte[ByteCount];
            BinaryPrimitives.WriteUInt64LittleEndian(bytes, low);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.Slice(8), high);
            value = new Guid(bytes);
            return true;
        }
    }

    /// <summary>
    /// Reads and writes <see cref="decimal"/> exactly as protobuf-net does: the mantissa as varint
    /// fields plus a combined sign-and-scale field, each field omitted when zero.
    /// </summary>
    /// <remarks>
    /// The allocation-free fast path uses protobuf-net's verified explicit layout. A runtime whose
    /// layout differs falls back to <see cref="decimal.GetBits(decimal)"/>, allocating one four-item
    /// array per decomposition so portability does not depend on an undocumented runtime layout.
    /// Negative zero keeps its sign flag through a root or collection-element round trip; like
    /// protobuf-net, an optional member at decimal zero is omitted regardless of its sign bit.
    /// </remarks>
    public sealed class WProtoDecimalFormatter : IWProtoFormatter<decimal>
    {
        /// <summary>The shared instance; the formatter holds no state.</summary>
        public static readonly WProtoDecimalFormatter Instance = new WProtoDecimalFormatter();

        private static readonly bool DecimalLayoutOptimized = VerifyDecimalLayout();

        private WProtoDecimalFormatter() { }

        /// <inheritdoc />
        public int Measure(in decimal value)
        {
            Decompose(value, out ulong low, out uint high, out uint signScale);

            int size = 0;
            if (low != 0UL)
            {
                size += WProtoSizes.TagSize(1) + WProtoSizes.Varint64Size(low);
            }

            if (high != 0U)
            {
                size += WProtoSizes.TagSize(2) + WProtoSizes.Varint32Size(high);
            }

            if (signScale != 0U)
            {
                size += WProtoSizes.TagSize(3) + WProtoSizes.Varint32Size(signScale);
            }

            return size;
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in decimal value)
        {
            Decompose(value, out ulong low, out uint high, out uint signScale);
            if (
                low != 0UL
                && (!writer.TryWriteTag(1, WProtoWireType.Varint) || !writer.TryWriteVarint64(low))
            )
            {
                return false;
            }

            if (
                high != 0U
                && (!writer.TryWriteTag(2, WProtoWireType.Varint) || !writer.TryWriteVarint32(high))
            )
            {
                return false;
            }

            return signScale == 0U
                || (
                    writer.TryWriteTag(3, WProtoWireType.Varint)
                    && writer.TryWriteVarint32(signScale)
                );
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out decimal value)
        {
            ulong low = 0;
            uint high = 0;
            uint signScale = 0;

            while (reader.TryReadTag(out int fieldNumber, out int wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == WProtoWireType.Varint:
                    {
                        if (!reader.TryReadVarint64(out low))
                        {
                            value = default(decimal);
                            return false;
                        }

                        break;
                    }
                    case 2 when wireType == WProtoWireType.Varint:
                    {
                        if (!reader.TryReadVarint32(out high))
                        {
                            value = default(decimal);
                            return false;
                        }

                        break;
                    }
                    case 3 when wireType == WProtoWireType.Varint:
                    {
                        if (!reader.TryReadVarint32(out signScale))
                        {
                            value = default(decimal);
                            return false;
                        }

                        break;
                    }
                    default:
                    {
                        if (!reader.TrySkipField(fieldNumber, wireType))
                        {
                            value = default(decimal);
                            return false;
                        }

                        break;
                    }
                }
            }

            if (reader.Malformed)
            {
                value = default(decimal);
                return false;
            }

            uint scale = (signScale & 0x1FEU) >> 1;
            if (28U < scale)
            {
                value = default(decimal);
                return false;
            }

            value = new decimal(
                (int)(low & 0xFFFFFFFFUL),
                (int)((low >> 32) & 0xFFFFFFFFUL),
                unchecked((int)high),
                (signScale & 0x1U) != 0U,
                (byte)scale
            );
            return true;
        }

        private static void Decompose(
            decimal value,
            out ulong low,
            out uint high,
            out uint signScale
        )
        {
            int lo;
            int mid;
            int hi;
            int flags;
            if (DecimalLayoutOptimized)
            {
                DecimalBits bits = new DecimalBits(value);
                lo = bits.Lo;
                mid = bits.Mid;
                hi = bits.Hi;
                flags = bits.Flags;
            }
            else
            {
                int[] bits = decimal.GetBits(value);
                lo = bits[0];
                mid = bits[1];
                hi = bits[2];
                flags = bits[3];
            }

            low = ((ulong)(uint)mid << 32) | (uint)lo;
            high = unchecked((uint)hi);
            signScale = (uint)(((flags >> 15) & 0x01FE) | ((flags >> 31) & 0x0001));
        }

        private static bool VerifyDecimalLayout()
        {
            try
            {
                decimal value = 1.0000000000000000000000000000m;
                DecimalBits layout = new DecimalBits(value);
                int[] bits = decimal.GetBits(value);
                return bits.Length == 4
                    && layout.Lo == bits[0]
                    && layout.Mid == bits[1]
                    && layout.Hi == bits[2]
                    && layout.Flags == bits[3];
            }
            catch (Exception)
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private readonly struct DecimalBits
        {
            [FieldOffset(0)]
            public readonly int Flags;

            [FieldOffset(4)]
            public readonly int Hi;

            [FieldOffset(8)]
            public readonly int Lo;

            [FieldOffset(12)]
            public readonly int Mid;

            [FieldOffset(0)]
            public readonly decimal Value;

            public DecimalBits(decimal value)
            {
                this = default(DecimalBits);
                Value = value;
            }
        }
    }

    /// <summary>
    /// Reads and writes <see cref="char"/> exactly as protobuf-net does: the UTF-16 code unit as a
    /// plain varint, omitted at <c>'\0'</c> in a member position but always written under the root
    /// key -- including the zero that a member would have dropped.
    /// </summary>
    public sealed class WProtoCharFormatter : IWProtoFormatter<char>, IWProtoConditionalFormatter
    {
        /// <summary>The shared instance; the formatter holds no state.</summary>
        public static readonly WProtoCharFormatter Instance = new WProtoCharFormatter();

        private WProtoCharFormatter() { }

        /// <inheritdoc />
        public bool CanServe()
        {
            return true;
        }

        /// <inheritdoc />
        public int Measure(in char value)
        {
            return WProtoSizes.Varint32Size(value);
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in char value)
        {
            return writer.TryWriteVarint32(value);
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out char value)
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

    /// <summary>
    /// Serves <see cref="char"/> where it is registered directly against
    /// <see cref="WProtoRootMarshalProvider"/>: the root writes the code unit even when it is zero.
    /// </summary>
    /// <remarks>
    /// Nothing here needs the nested provider lookup of <see cref="WProtoBclRootFormatter{T}"/>
    /// because the payload is not length-delimited -- it is one varint under the field-one key,
    /// which is exactly the shape the root and a member share.
    /// </remarks>
    internal sealed class WProtoCharRootFormatter : IWProtoFormatter<char>
    {
        public int Measure(in char value)
        {
            return WProtoSizes.TagSize(1) + WProtoSizes.Varint32Size(value);
        }

        public bool Write(ref WProtoWriter writer, in char value)
        {
            return writer.TryWriteTag(1, WProtoWireType.Varint) && writer.TryWriteVarint32(value);
        }

        public bool TryRead(ref WProtoReader reader, out char value)
        {
            uint raw = 0U;
            while (reader.TryReadTag(out int fieldNumber, out int wireType))
            {
                if (fieldNumber == 1 && wireType == WProtoWireType.Varint)
                {
                    if (!reader.TryReadVarint32(out raw))
                    {
                        value = default(char);
                        return false;
                    }
                }
                else if (!reader.TrySkipField(fieldNumber, wireType))
                {
                    value = default(char);
                    return false;
                }
            }

            if (reader.Malformed)
            {
                value = default(char);
                return false;
            }

            value = (char)raw;
            return true;
        }
    }

    /// <summary>
    /// Reads and writes <see cref="Uri"/> exactly as protobuf-net does: the UTF-8 bytes of
    /// <see cref="Uri.OriginalString"/>, with no inner field keys. The measured originals keep
    /// escapes and letter case exactly as constructed, which is what makes them byte-portable
    /// across runtimes unlike a type name.
    /// </summary>
    /// <remarks>
    /// The measure deliberately excludes every prefix: callers wrap this payload in a length or a
    /// message envelope themselves, which is also how the oracle reaches an identical member and
    /// root form. An empty region refuses rather than manufacturing a value no constructor could
    /// have produced.
    /// </remarks>
    public sealed class WProtoUriFormatter : IWProtoFormatter<Uri>
    {
        /// <summary>The shared instance; the formatter holds no state.</summary>
        public static readonly WProtoUriFormatter Instance = new WProtoUriFormatter();

        // The BCL's Encoding.UTF8 replaces an invalid byte with U+FFFD instead of reporting it,
        // which would turn corrupt bytes into a silently different Uri. A strict decoder is what
        // makes a malformed region refuse instead of decode.
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private WProtoUriFormatter() { }

        /// <inheritdoc />
        public int Measure(in Uri value)
        {
            return StrictUtf8.GetByteCount(value.OriginalString);
        }

        /// <inheritdoc />
        public bool Write(ref WProtoWriter writer, in Uri value)
        {
            byte[] encoded = StrictUtf8.GetBytes(value.OriginalString);
            return writer.TryWriteRaw(encoded);
        }

        /// <inheritdoc />
        public bool TryRead(ref WProtoReader reader, out Uri value)
        {
            if (!reader.TryReadRemaining(out ReadOnlySpan<byte> payload))
            {
                value = null;
                return false;
            }

            string text;
            try
            {
                text = StrictUtf8.GetString(payload);
            }
            catch (ArgumentException)
            {
                value = null;
                return false;
            }

            try
            {
                value = new Uri(text, UriKind.RelativeOrAbsolute);
                return true;
            }
            catch (InvalidOperationException)
            {
                value = null;
                return false;
            }
            catch (FormatException)
            {
                value = null;
                return false;
            }
        }
    }
}
