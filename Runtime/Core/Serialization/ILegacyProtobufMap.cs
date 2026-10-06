// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
#if !WALLSTOP_PROTO_ONLY
    using System.IO;

    internal interface ILegacyProtobufMap
    {
        bool TrySerialize(Stream destination);
    }
#endif
}
