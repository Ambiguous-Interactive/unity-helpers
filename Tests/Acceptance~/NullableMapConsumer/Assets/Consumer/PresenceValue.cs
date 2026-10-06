// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace NestedOnlyConsumer
{
    using ProtoBuf;

#pragma warning disable WPROTO030
    [ProtoContract]
    public struct PresenceValue
    {
        [ProtoMember(1)]
        public int Number;
    }
#pragma warning restore WPROTO030
}
