// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using ProtoBuf;
    using ProtoBuf.Serializers;

    public class NullableMapDualService
        : ISerializer<NullableMapValue>,
            ISerializer<NullableMapValue?>
    {
        [ThreadStatic]
        public static int UnderlyingWrites;

        [ThreadStatic]
        public static int NullableWrites;

        SerializerFeatures ISerializer<NullableMapValue>.Features =>
            SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeVarint;

        SerializerFeatures ISerializer<NullableMapValue?>.Features =>
            SerializerFeatures.CategoryScalar | SerializerFeatures.WireTypeVarint;

        NullableMapValue ISerializer<NullableMapValue>.Read(
            ref ProtoReader.State state,
            NullableMapValue value
        )
        {
            return new NullableMapValue { Number = state.ReadInt32() - 100 };
        }

        NullableMapValue? ISerializer<NullableMapValue?>.Read(
            ref ProtoReader.State state,
            NullableMapValue? value
        )
        {
            return new NullableMapValue { Number = state.ReadInt32() - 200 };
        }

        void ISerializer<NullableMapValue>.Write(
            ref ProtoWriter.State state,
            NullableMapValue value
        )
        {
            ++UnderlyingWrites;
            state.WriteInt32(value.Number + 100);
        }

        void ISerializer<NullableMapValue?>.Write(
            ref ProtoWriter.State state,
            NullableMapValue? value
        )
        {
            ++NullableWrites;
            state.WriteInt32(value.GetValueOrDefault().Number + 200);
        }
    }
}
