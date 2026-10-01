// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.ComponentModel;
    using System.IO;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Defaults whose attribute conversion precedes conversion to the member type.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ExplicitDefaultConversionContract
    {
        /// <summary>A single precision default converted to an integer.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        [DefaultValue(typeof(float), "16777217")]
        public int Rounded = 16777216;

        /// <summary>An integer parsed through its invariant type converter.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        [DefaultValue(typeof(int), "0x10")]
        public int Hexadecimal = 16;

        /// <summary>A numeric enum string.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        [DefaultValue(typeof(DayOfWeek), "5")]
        public DayOfWeek Numeric = DayOfWeek.Friday;

        /// <summary>A Boolean converted to an enum value.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        [DefaultValue(true)]
        public DayOfWeek Boolean = DayOfWeek.Monday;

        /// <summary>A character converted to an enum value.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        [DefaultValue('\u0005')]
        public DayOfWeek Character = DayOfWeek.Friday;

        /// <summary>A combined flags enum default.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        [DefaultValue(typeof(FileAttributes), "Hidden, Archive")]
        public FileAttributes Flags = FileAttributes.Hidden | FileAttributes.Archive;

        /// <summary>An integer attribute value converted to a string member.</summary>
        [ProtoMember(7)]
        [WProtoMember(7)]
        [DefaultValue(typeof(int), "5")]
        public string Text = "5";

        /// <summary>A fractional default rounded when converted to an integer.</summary>
        [ProtoMember(8)]
        [WProtoMember(8)]
        [DefaultValue(typeof(double), "2.5")]
        public int Fractional = 2;

        /// <summary>An enum attribute converted to its named string value.</summary>
        [ProtoMember(9)]
        [WProtoMember(9)]
        [DefaultValue(DayOfWeek.Friday)]
        public string EnumText = "Friday";

        /// <summary>A typed enum string converted to its named string value.</summary>
        [ProtoMember(10)]
        [WProtoMember(10)]
        [DefaultValue(typeof(DayOfWeek), "friday")]
        public string TypedEnumText = "Friday";
    }
}
