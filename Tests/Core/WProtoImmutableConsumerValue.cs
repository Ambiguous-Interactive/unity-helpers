// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>A readonly consumer contract whose closed formatter is discovered by its caller.</summary>
    [WProtoContract]
    public readonly partial struct WProtoImmutableConsumerValue<TIdentifier>
    {
        /// <summary>The original identifier.</summary>
        [WProtoMember(5)]
        public readonly TIdentifier Identifier;

        /// <summary>The exact saved sequence.</summary>
        [WProtoMember(11)]
        public readonly long Sequence;

        /// <summary>Creates a consumer value from its persisted fields.</summary>
        public WProtoImmutableConsumerValue(TIdentifier identifier, long sequence)
        {
            Identifier = identifier;
            Sequence = sequence;
        }
    }
}
