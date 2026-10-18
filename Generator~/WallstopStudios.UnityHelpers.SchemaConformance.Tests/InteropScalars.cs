// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SchemaConformance.Tests
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    [WProtoReserved(50)]
    [WProtoReserved("OldValue")]
    internal sealed partial class InteropScalars
    {
        [WProtoMember(1)]
        public int Signed;

        [WProtoMember(2)]
        public long WideSigned;

        [WProtoMember(3)]
        public uint Unsigned;

        [WProtoMember(4)]
        public ulong WideUnsigned;

        [WProtoMember(5, DataFormat = WProtoDataFormat.ZigZag)]
        public long ZigZag;

        [WProtoMember(6)]
        public float Single;

        [WProtoMember(7)]
        public double Double;

        [WProtoMember(8)]
        public bool Enabled;

        [WProtoMember(9)]
        public char Character;

        [WProtoMember(10)]
        public string Text;

        [WProtoMember(11)]
        public byte[] Bytes;

        [WProtoMember(12)]
        public int? Optional;
    }
}
