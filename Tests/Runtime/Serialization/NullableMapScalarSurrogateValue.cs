// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;

    [ProtoContract(Surrogate = typeof(int))]
    public struct NullableMapScalarSurrogateValue
    {
        public int Number;

        public static implicit operator int(NullableMapScalarSurrogateValue value) => value.Number;

        public static implicit operator NullableMapScalarSurrogateValue(int value) =>
            new NullableMapScalarSurrogateValue { Number = value };
    }
}
