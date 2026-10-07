// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using ProtoBuf;

    [ProtoContract]
    public sealed class CollectionScratchItem
    {
        [ProtoMember(1)]
        public int Value { get; set; }

        [ProtoMember(2)]
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
            ++State.BeforeCalls;
            Action action = State.BeforeAction;
            State.BeforeAction = null;
            action?.Invoke();
            if (Value == State.FailingValue && State.FailBefore)
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
            ++State.AfterCalls;
            if (Value == State.FailingValue && State.FailAfter)
            {
                throw State.Failure;
            }
        }
    }
}
