// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace NestedOnlyConsumer
{
    using ProtoBuf;
#pragma warning disable WPROTO030 // This consumer deliberately uses the legacy runtime backend.
    [ProtoContract]
    public sealed class OwnedMessage
    {
        [ProtoMember(1)]
        public int Number;

        [ProtoMember(3)]
        public string Text;

        [ProtoMember(5)]
        public int? Checkpoint;
    }
#pragma warning restore WPROTO030
}
