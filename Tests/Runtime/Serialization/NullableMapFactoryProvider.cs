// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf.Serializers;

    public sealed class NullableMapFactoryProvider : ISerializerProxy<NullableMapValue>
    {
        public ISerializer<NullableMapValue> Serializer { get; } = new NullableMapDualService();
    }
}
