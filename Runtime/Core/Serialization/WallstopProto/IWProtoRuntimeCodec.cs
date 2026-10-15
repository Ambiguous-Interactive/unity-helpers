// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    internal interface IWProtoRuntimeCodec
    {
        bool TryDeserialize(
            ReadOnlySpan<byte> data,
            WProtoReadLimits limits,
            bool preserveTypedFailures,
            out object value
        );
        bool TrySerialize(object value, out byte[] bytes);
        WProtoWriteResult Serialize(object value, ref byte[] buffer);
    }
}
