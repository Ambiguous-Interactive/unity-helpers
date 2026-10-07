// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.DataStructure;

    [ProtoContract]
    public sealed class CollectionScratchParent
    {
        [ProtoMember(1)]
        public Deque<CollectionScratchItem> Deque { get; set; }

        [ProtoMember(2)]
        public CyclicBuffer<CollectionScratchItem> Cyclic { get; set; }

        [ProtoMember(3)]
        public byte[] Payload { get; set; }

        [ProtoIgnore]
        public CollectionScratchCallbackState State;

        [ProtoBeforeSerialization]
        public void BeforeSerialize()
        {
            if (State == null)
            {
                return;
            }
            ++State.ParentBeforeCalls;
            if (State.FailParentBefore)
            {
                throw State.Failure;
            }
        }

        [ProtoAfterSerialization]
        public void AfterSerialize()
        {
            if (State == null)
            {
                return;
            }
            ++State.ParentAfterCalls;
            if (State.FailParentAfter)
            {
                throw State.Failure;
            }
        }
    }
}
