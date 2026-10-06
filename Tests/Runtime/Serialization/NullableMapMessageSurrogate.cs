// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;

    [ProtoContract]
    public sealed class NullableMapMessageSurrogate
    {
        [ProtoMember(1)]
        public int Number { get; set; }
    }
}
