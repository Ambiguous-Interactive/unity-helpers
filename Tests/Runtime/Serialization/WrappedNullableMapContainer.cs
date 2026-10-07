// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using ProtoBuf;

    [ProtoContract]
    public sealed class WrappedNullableMapContainer<TValue>
    {
        [ProtoMember(7)]
        [NullWrappedValue]
        public Dictionary<int, TValue> Entries { get; set; }
    }
}
