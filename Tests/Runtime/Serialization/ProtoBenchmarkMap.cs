// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [ProtoContract]
    [WProtoContract]
    internal sealed partial class ProtoBenchmarkMap
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public Dictionary<string, int> Value;

        [ProtoMember(100)]
        [WProtoMember(100)]
        public int CorpusMarker = 1;
    }
}
