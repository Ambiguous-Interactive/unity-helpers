// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;

    [ProtoContract(Surrogate = typeof(NullableMapMessageSurrogate))]
    public struct NullableMapMessageSurrogateValue
    {
        public int Number;

        public static implicit operator NullableMapMessageSurrogate(
            NullableMapMessageSurrogateValue value
        ) => new NullableMapMessageSurrogate { Number = value.Number };

        public static implicit operator NullableMapMessageSurrogateValue(
            NullableMapMessageSurrogate value
        ) => new NullableMapMessageSurrogateValue { Number = value.Number };
    }
}
