// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public sealed partial class WProtoSmallIntegerContract
    {
        [WProtoMember(1)]
        public byte Byte;

        [WProtoMember(2)]
        public sbyte SignedByte;

        [WProtoMember(3)]
        public short Short;

        [WProtoMember(4)]
        public ushort UnsignedShort;

        [WProtoMember(5)]
        public char Character;

        [WProtoMember(6)]
        public short? NullableShort;

        [WProtoMember(7)]
        public List<short> Shorts;

        [WProtoMember(8)]
        public List<byte> Bytes;

        [WProtoMember(9)]
        public Dictionary<short, byte> Values;

        [WProtoMember(10, DataFormat = WProtoDataFormat.ZigZag)]
        public short ZigZagShort;

        [WProtoMember(11, DataFormat = WProtoDataFormat.ZigZag)]
        public sbyte ZigZagSignedByte;

        [WProtoMember(12, DataFormat = WProtoDataFormat.ZigZag)]
        public short? NullableZigZagShort;

        [WProtoMember(13)]
        public List<sbyte> SignedBytes;

        [WProtoMember(14)]
        public List<ushort> UnsignedShorts;

        [WProtoMember(15)]
        public List<char> Characters;

        [WProtoMember(16)]
        public Dictionary<ushort, char> CharactersByNumber;

        [WProtoMember(17)]
        public Dictionary<sbyte, short> ShortsBySignedByte;
    }
}
