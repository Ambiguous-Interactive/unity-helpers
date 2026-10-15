// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    internal sealed class WProtoRuntimeCodec<T> : IWProtoRuntimeCodec
    {
        internal static readonly WProtoRuntimeCodec<T> Instance = new WProtoRuntimeCodec<T>();

        private WProtoRuntimeCodec() { }

        public bool TryDeserialize(
            ReadOnlySpan<byte> data,
            WProtoReadLimits limits,
            bool preserveTypedFailures,
            out object value
        )
        {
            bool served = WProtoFacade.ReadAs(
                data,
                typeof(T),
                limits,
                preserveTypedFailures,
                out T decoded
            );
            value = served ? decoded : null;
            return served;
        }

        public bool TrySerialize(object value, out byte[] bytes)
        {
            return WProtoFacade.TrySerialize((T)value, out bytes);
        }

        public WProtoWriteResult Serialize(object value, ref byte[] buffer)
        {
            return WProtoFacade.Serialize((T)value, ref buffer);
        }
    }
}
