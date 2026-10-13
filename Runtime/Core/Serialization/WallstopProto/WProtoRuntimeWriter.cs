// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    internal sealed class WProtoRuntimeWriter<T> : IWProtoRuntimeWriter
    {
        internal static readonly WProtoRuntimeWriter<T> Instance = new WProtoRuntimeWriter<T>();

        private WProtoRuntimeWriter() { }

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
