// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    internal interface IWProtoRuntimeWriter
    {
        byte[] Serialize(object value);
        WProtoWriteResult Serialize(object value, ref byte[] buffer);
    }
}
