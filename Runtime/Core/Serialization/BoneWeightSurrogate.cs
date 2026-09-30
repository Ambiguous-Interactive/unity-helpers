// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
#if UNITY_5_3_OR_NEWER
    using UnityEngine;
#endif

    /// <summary>Carries the protobuf wire shape of BoneWeight.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial struct BoneWeightSurrogate
    {
        /// <summary>The weight0 component.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public float weight0;

        /// <summary>The weight1 component.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public float weight1;

        /// <summary>The weight2 component.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public float weight2;

        /// <summary>The weight3 component.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public float weight3;

        /// <summary>The boneIndex0 component.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public int boneIndex0;

        /// <summary>The boneIndex1 component.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        public int boneIndex1;

        /// <summary>The boneIndex2 component.</summary>
        [ProtoMember(7)]
        [WProtoMember(7)]
        public int boneIndex2;

        /// <summary>The boneIndex3 component.</summary>
        [ProtoMember(8)]
        [WProtoMember(8)]
        public int boneIndex3;

#if UNITY_5_3_OR_NEWER
        /// <summary>Copies the Unity value into its protobuf shape.</summary>
        /// <param name="source">The Unity value.</param>
        /// <returns>The matching protobuf shape.</returns>
        public static implicit operator BoneWeightSurrogate(BoneWeight source)
        {
            return new BoneWeightSurrogate
            {
                weight0 = source.weight0,
                weight1 = source.weight1,
                weight2 = source.weight2,
                weight3 = source.weight3,
                boneIndex0 = source.boneIndex0,
                boneIndex1 = source.boneIndex1,
                boneIndex2 = source.boneIndex2,
                boneIndex3 = source.boneIndex3,
            };
        }

        /// <summary>Restores the Unity value from its protobuf shape.</summary>
        /// <param name="source">The protobuf shape.</param>
        /// <returns>The matching Unity value.</returns>
        public static implicit operator BoneWeight(BoneWeightSurrogate source)
        {
            return new BoneWeight
            {
                weight0 = source.weight0,
                weight1 = source.weight1,
                weight2 = source.weight2,
                weight3 = source.weight3,
                boneIndex0 = source.boneIndex0,
                boneIndex1 = source.boneIndex1,
                boneIndex2 = source.boneIndex2,
                boneIndex3 = source.boneIndex3,
            };
        }
#endif
    }
}
