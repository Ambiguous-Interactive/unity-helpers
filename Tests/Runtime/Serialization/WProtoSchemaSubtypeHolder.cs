// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public sealed partial class WProtoSchemaSubtypeHolder
    {
        [WProtoMember(1)]
        public SchemaIncludeBase Base;

        [WProtoMember(2)]
        public SchemaDerived Concrete;

        [WProtoMember(3)]
        public SchemaChainDerived Middle;

        [WProtoMember(4)]
        public SchemaChainSubDerived Leaf;

        [WProtoMember(5)]
        public List<SchemaDerived> Repeated;

        [WProtoMember(6)]
        public Dictionary<string, SchemaDerived> ByName;
    }
}
