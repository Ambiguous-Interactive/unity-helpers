// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Migrates a runtime-only protobuf-net model to explicit generated field numbers.
    /// </summary>
    [WProtoContract]
    public sealed partial class RuntimeModelMigrationContract
    {
        /// <summary>The identifier, including zero, at its original field number.</summary>
        [WProtoMember(5, IsRequired = true)]
        public int Identifier;

        /// <summary>The name at its original field number.</summary>
        [WProtoMember(11)]
        public string Name;
    }
}
