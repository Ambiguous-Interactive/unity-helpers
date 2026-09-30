// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
#if UNITY_5_3_OR_NEWER
    using UnityEngine;

    /// <summary>Serializes a BoneWeight root through its surrogate.</summary>
    internal sealed class BoneWeightMarshalFormatter
        : SurrogateMarshalFormatter<BoneWeight, BoneWeightSurrogate>
    {
        /// <summary>Initializes a new instance of the <see cref="BoneWeightMarshalFormatter"/> class.</summary>
        public BoneWeightMarshalFormatter()
            : base(BoneWeightSurrogate.WProtoFormatter.Instance) { }

        /// <inheritdoc />
        protected override BoneWeightSurrogate ToSurrogate(in BoneWeight value)
        {
            return value;
        }

        /// <inheritdoc />
        protected override BoneWeight FromSurrogate(in BoneWeightSurrogate surrogate)
        {
            return surrogate;
        }
    }
#endif
}
