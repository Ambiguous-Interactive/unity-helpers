// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

[assembly: WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSurrogate(
    typeof(WallstopStudios.UnityHelpers.Tests.Serialization.ConsumerMigration.ForeignVector3),
    typeof(WallstopStudios.UnityHelpers.Tests.Serialization.ConsumerMigration.ForeignVector3Surrogate)
)]

namespace WallstopStudios.UnityHelpers.Tests.Serialization.ConsumerMigration
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>The contract that gives <see cref="ForeignVector3"/> a wire shape.</summary>
    [WProtoContract]
    public partial struct ForeignVector3Surrogate
    {
        /// <summary>The first component.</summary>
        [WProtoMember(1)]
        public float x;

        /// <summary>The second component.</summary>
        [WProtoMember(2)]
        public float y;

        /// <summary>The third component.</summary>
        [WProtoMember(3)]
        public float z;

        /// <summary>Converts to the real type.</summary>
        /// <param name="value">The surrogate.</param>
        public static implicit operator ForeignVector3(ForeignVector3Surrogate value)
        {
            return new ForeignVector3
            {
                x = value.x,
                y = value.y,
                z = value.z,
            };
        }

        /// <summary>Converts from the real type.</summary>
        /// <param name="value">The real value.</param>
        public static implicit operator ForeignVector3Surrogate(ForeignVector3 value)
        {
            return new ForeignVector3Surrogate
            {
                x = value.x,
                y = value.y,
                z = value.z,
            };
        }
    }
}
