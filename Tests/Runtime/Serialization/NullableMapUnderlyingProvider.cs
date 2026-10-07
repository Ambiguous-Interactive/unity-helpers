// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using ProtoBuf;
    using ProtoBuf.Serializers;

    public sealed class NullableMapUnderlyingProvider : ISerializer<NullableMapValue>
    {
        [ThreadStatic]
        public static int Writes;

        public SerializerFeatures Features =>
            SerializerFeatures.CategoryMessage | SerializerFeatures.WireTypeString;

        public NullableMapValue Read(ref ProtoReader.State state, NullableMapValue value)
        {
            int field;
            while (0 < (field = state.ReadFieldHeader()))
            {
                if (field == 1)
                {
                    value.Number = state.ReadInt32();
                }
                else
                {
                    state.SkipField();
                }
            }
            return value;
        }

        public void Write(ref ProtoWriter.State state, NullableMapValue value)
        {
            ++Writes;
            if (value.Number != 0)
            {
                state.WriteFieldHeader(1, WireType.Varint);
                state.WriteInt32(value.Number);
            }
        }
    }
}
