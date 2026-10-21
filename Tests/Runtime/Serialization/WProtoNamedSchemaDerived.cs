// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract(Name = "NamedNode")]
    public sealed partial class WProtoNamedSchemaDerived : WProtoNamedSchemaBase
    {
        [WProtoMember(2)]
        public int Extra;
    }
}
