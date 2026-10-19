// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract(Name = "NamedBase")]
    [WProtoInclude(7, typeof(WProtoNamedSchemaDerived))]
    [WProtoReserved("namedNode2")]
    public partial class WProtoNamedSchemaBase
    {
        [WProtoMember(1)]
        public int RootId;

        [WProtoMember(3, Name = "namedNode")]
        public int PayloadNumber;
    }
}
