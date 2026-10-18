// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [ProtoContract]
    [WProtoContract]
    internal sealed partial class ProtoBenchmarkLeaf : ProtoBenchmarkNode
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public string Label;
    }
}
