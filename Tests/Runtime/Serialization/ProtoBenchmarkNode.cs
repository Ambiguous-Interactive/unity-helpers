// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [ProtoContract]
    [ProtoInclude(10, typeof(ProtoBenchmarkLeaf))]
    [WProtoContract]
    [WProtoInclude(10, typeof(ProtoBenchmarkLeaf))]
    internal partial class ProtoBenchmarkNode
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;
    }
}
