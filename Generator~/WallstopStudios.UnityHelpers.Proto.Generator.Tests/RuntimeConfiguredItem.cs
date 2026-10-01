// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>A subtype declared independently of its base contract.</summary>
    [WProtoContract]
    [WProtoSubtype(typeof(RuntimeConfiguredEntry), 101)]
    public sealed partial class RuntimeConfiguredItem : RuntimeConfiguredEntry
    {
        /// <summary>The quantity, including a present zero value.</summary>
        [WProtoMember(13, IsRequired = true)]
        public int Quantity;
    }
}
