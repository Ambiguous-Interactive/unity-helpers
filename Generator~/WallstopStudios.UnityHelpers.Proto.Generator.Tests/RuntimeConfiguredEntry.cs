// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>The base shape of a runtime-configured consumer hierarchy.</summary>
    [WProtoContract]
    public partial class RuntimeConfiguredEntry
    {
        /// <summary>The entry name.</summary>
        [WProtoMember(7)]
        public string Name;
    }
}
