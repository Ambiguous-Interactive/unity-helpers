// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Serialization
{
    using System;
    using System.ComponentModel;
    using System.Globalization;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Scalar defaults shared by the generated formatter and both oracle versions.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class WProtoExplicitDefaultContract
    {
        /// <summary>The SignedByte value.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        [DefaultValue((sbyte)-5)]
        public sbyte SignedByte = -5;

        /// <summary>The Byte value.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        [DefaultValue((byte)5)]
        public byte Byte = 5;

        /// <summary>The Int16 value.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        [DefaultValue((short)-25)]
        public short Int16 = -25;

        /// <summary>The UInt16 value.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        [DefaultValue((ushort)25)]
        public ushort UInt16 = 25;

        /// <summary>The Int32 value.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        [DefaultValue(typeof(int), "-123")]
        public int Int32 = -123;

        /// <summary>The UInt32 value.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        [DefaultValue(123U)]
        public uint UInt32 = 123U;

        /// <summary>The Int64 value.</summary>
        [ProtoMember(7)]
        [WProtoMember(7)]
        [DefaultValue(-123456789L)]
        public long Int64 = -123456789L;

        /// <summary>The UInt64 value.</summary>
        [ProtoMember(8)]
        [WProtoMember(8)]
        [DefaultValue(123456789UL)]
        public ulong UInt64 = 123456789UL;

        /// <summary>The Flag value.</summary>
        [ProtoMember(9)]
        [WProtoMember(9)]
        [DefaultValue(true)]
        public bool Flag = true;

        /// <summary>The Character value.</summary>
        [ProtoMember(10)]
        [WProtoMember(10)]
        [DefaultValue('Ω')]
        public char Character = 'Ω';

        /// <summary>The Single value.</summary>
        [ProtoMember(11)]
        [WProtoMember(11)]
        [DefaultValue(1.25F)]
        public float Single = 1.25F;

        /// <summary>The Double value.</summary>
        [ProtoMember(12)]
        [WProtoMember(12)]
        [DefaultValue(-2.5D)]
        public double Double = -2.5D;

        /// <summary>The Text value.</summary>
        [ProtoMember(13)]
        [WProtoMember(13)]
        [DefaultValue("café")]
        public string Text = "café";

        /// <summary>The Enum value.</summary>
        [ProtoMember(14)]
        [WProtoMember(14)]
        [DefaultValue(typeof(DayOfWeek), "Friday")]
        public DayOfWeek Enum = DayOfWeek.Friday;

        /// <summary>The Nullable value.</summary>
        [ProtoMember(15)]
        [WProtoMember(15)]
        [DefaultValue(5)]
        public int? Nullable = 5;

        /// <summary>The NaN value.</summary>
        [ProtoMember(16)]
        [WProtoMember(16)]
        public float NaN = float.NaN;

        /// <summary>The Infinity value.</summary>
        [ProtoMember(17)]
        [WProtoMember(17)]
        [DefaultValue(double.PositiveInfinity)]
        public double Infinity = double.PositiveInfinity;

        /// <summary>The Decimal value.</summary>
        [ProtoMember(18)]
        [WProtoMember(18)]
        [DefaultValue(typeof(decimal), "1.25")]
        public decimal Decimal = 1.25M;

        /// <summary>The Timestamp value.</summary>
        [ProtoMember(19)]
        [WProtoMember(19)]
        [DefaultValue(typeof(DateTime), "2000-01-01")]
        public DateTime Timestamp = new DateTime(2000, 1, 1);

        /// <summary>The Duration value.</summary>
        [ProtoMember(20)]
        [WProtoMember(20)]
        [DefaultValue(typeof(TimeSpan), "00:00:05")]
        public TimeSpan Duration = TimeSpan.FromSeconds(5);

        /// <summary>The Guid value.</summary>
        [ProtoMember(21)]
        [WProtoMember(21)]
        [DefaultValue(typeof(Guid), "00112233-4455-6677-8899-aabbccddeeff")]
        public Guid Guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");

        /// <summary>The Plain value.</summary>
        [ProtoMember(22)]
        [WProtoMember(22)]
        [DefaultValue(5)]
        public int Plain;

        /// <summary>A null explicit default leaves nullable presence unchanged.</summary>
        [ProtoMember(24)]
        [WProtoMember(24)]
        [DefaultValue(null)]
        public int? NullNullable;

        /// <summary>A null string explicit default preserves the empty string.</summary>
        [ProtoMember(25)]
        [WProtoMember(25)]
        [DefaultValue(null)]
        public string NullText;

        /// <summary>A required value that overrides its explicit omission default.</summary>
        [ProtoMember(23, IsRequired = true)]
        [WProtoMember(23, IsRequired = true)]
        [DefaultValue(5)]
        public int Required = 5;

        /// <summary>A required NaN default never participates in omission.</summary>
        [ProtoMember(26, IsRequired = true)]
        [WProtoMember(26, IsRequired = true)]
        [DefaultValue(float.NaN)]
        public float RequiredNaN;

        /// <summary>A direct timestamp string whose default follows the player's local time zone.</summary>
        [ProtoMember(27)]
        [WProtoMember(27)]
        [DefaultValue("2000-01-01T00:00:00.1234567Z")]
        public DateTime DirectZonedTimestamp = DateTime.Parse(
            "2000-01-01T00:00:00.1234567Z",
            CultureInfo.InvariantCulture
        );

        /// <summary>A typed offset timestamp default retaining fractional ticks.</summary>
        [ProtoMember(28)]
        [WProtoMember(28)]
        [DefaultValue(typeof(DateTime), "2001-02-03T00:00:00.7654321+05:30")]
        public DateTime TypedZonedTimestamp = DateTime.Parse(
            "2001-02-03T00:00:00.7654321+05:30",
            CultureInfo.InvariantCulture
        );

        /// <summary>A date converted to a string after the player selects its local time zone.</summary>
        [ProtoMember(29)]
        [WProtoMember(29)]
        [DefaultValue(typeof(DateTime), "2000-01-01T00:00:00Z")]
        public string ZonedTimestampText = DateTime
            .Parse("2000-01-01T00:00:00Z", CultureInfo.InvariantCulture)
            .ToString(CultureInfo.InvariantCulture);
    }
}
