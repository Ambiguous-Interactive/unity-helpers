// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;

    [ProtoContract]
    public struct NullableMapCallbackValue
    {
        [ProtoMember(1)]
        public int Number { get; set; }

        [ProtoBeforeSerialization]
        public void BeforeSerialize()
        {
            if (NullableMapCallbackState.BeforeFailure != null)
            {
                throw NullableMapCallbackState.BeforeFailure;
            }
        }

        [ProtoAfterSerialization]
        public void AfterSerialize()
        {
            if (NullableMapCallbackState.AfterFailure != null)
            {
                throw NullableMapCallbackState.AfterFailure;
            }
        }
    }
}
