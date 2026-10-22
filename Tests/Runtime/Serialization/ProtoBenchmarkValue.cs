// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [ProtoContract]
    [WProtoContract]
    internal sealed partial class ProtoBenchmarkValue<T>
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public T Value;

        [ProtoMember(100)]
        [WProtoMember(100)]
        public int CorpusMarker = 1;
    }
}
