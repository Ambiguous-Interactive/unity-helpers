// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization.ConsumerMigration
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>A closed consumer save formerly configured only through a runtime model.</summary>
    /// <typeparam name="TIdentifier">The build-selected identifier type.</typeparam>
    [WProtoContract]
    public sealed partial class RuntimeConfiguredSave<TIdentifier>
    {
        /// <summary>The identifier, including a present zero value.</summary>
        [WProtoMember(5, IsRequired = true)]
        public TIdentifier Identifier;

        /// <summary>A scalar whose runtime model explicitly omits zero.</summary>
        [WProtoMember(11)]
        public int Version;

        /// <summary>A nullable scalar whose zero value remains present.</summary>
        [WProtoMember(17)]
        public int? Checkpoint;

        /// <summary>The polymorphic save entries.</summary>
        [WProtoMember(23)]
        public List<RuntimeConfiguredEntry> Entries;

        /// <summary>A foreign value carried through a static surrogate registration.</summary>
        [WProtoMember(29)]
        public ForeignVector3 Position;
    }
}
