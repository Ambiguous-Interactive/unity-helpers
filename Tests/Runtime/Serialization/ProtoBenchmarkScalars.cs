// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [ProtoContract]
    [WProtoContract]
    internal sealed partial class ProtoBenchmarkScalars
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public ulong First;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public ulong Second;

        [ProtoMember(3)]
        [WProtoMember(3)]
        public ulong Third;

        [ProtoMember(4)]
        [WProtoMember(4)]
        public ulong Fourth;

        [ProtoMember(5)]
        [WProtoMember(5)]
        public ulong Fifth;

        [ProtoMember(6)]
        [WProtoMember(6)]
        public ulong Sixth;

        [ProtoMember(7)]
        [WProtoMember(7)]
        public ulong Seventh;

        [ProtoMember(8)]
        [WProtoMember(8)]
        public ulong Eighth;

        [ProtoMember(9)]
        [WProtoMember(9)]
        public ulong Ninth;

        [ProtoMember(10)]
        [WProtoMember(10)]
        public ulong Tenth;

        [ProtoMember(100)]
        [WProtoMember(100)]
        public int CorpusMarker = 1;
    }
}
