// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    internal interface IWProtoRuntimeWriter
    {
        bool TrySerialize(object value, out byte[] bytes);
        WProtoWriteResult Serialize(object value, ref byte[] buffer);
    }
}
