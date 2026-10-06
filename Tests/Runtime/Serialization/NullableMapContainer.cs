// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;

    [ProtoContract]
    public sealed class NullableMapContainer<TMap>
        where TMap : new()
    {
        [ProtoMember(7)]
        public TMap Entries { get; set; } = new TMap();
    }
}
