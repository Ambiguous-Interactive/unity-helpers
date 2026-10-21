// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public sealed partial class WProtoSchemaWideEnumContract
    {
        [WProtoMember(1)]
        public WProtoSchemaUnsigned32 Unsigned;

        [WProtoMember(2)]
        public WProtoSchemaSigned64 Signed;

        [WProtoMember(3)]
        public WProtoSchemaUnsigned64 WideUnsigned;

        [WProtoMember(4)]
        public WProtoSchemaUnsigned32? Optional;

        [WProtoMember(5)]
        public WProtoSchemaSigned64[] Packed;

        [WProtoMember(6, IsPacked = false)]
        public WProtoSchemaUnsigned32[] Unpacked;

        [WProtoMember(7)]
        public Dictionary<WProtoSchemaUnsigned64, WProtoSchemaSigned64> ByNumber;

        [WProtoMember(8)]
        public WProtoSchemaUnsigned64[][] Rows;

        [WProtoMember(9)]
        public WProtoSchemaUnsigned32[,] Grid;

        [WProtoMember(10)]
        public Dictionary<string, WProtoSchemaUnsigned64> ByName;
    }
}
