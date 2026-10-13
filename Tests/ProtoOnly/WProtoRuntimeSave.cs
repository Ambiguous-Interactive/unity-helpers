// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public sealed partial class WProtoRuntimeSave : IWProtoRuntimeSave
    {
        [WProtoMember(1)]
        public int Health;

        [WProtoMember(2)]
        public string Station;

        public string GetFileName()
        {
            return nameof(WProtoRuntimeSave);
        }
    }
}
