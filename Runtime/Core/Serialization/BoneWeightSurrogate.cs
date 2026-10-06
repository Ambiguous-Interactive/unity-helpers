// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
#if !WALLSTOP_PROTO_ONLY
    using ProtoBuf;
#endif

#if UNITY_5_3_OR_NEWER
    using UnityEngine;
#endif

    /// <summary>Carries the protobuf wire shape of BoneWeight.</summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct BoneWeightSurrogate
    {
        /// <summary>The weight0 component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float weight0;

        /// <summary>The weight1 component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float weight1;

        /// <summary>The weight2 component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public float weight2;

        /// <summary>The weight3 component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public float weight3;

        /// <summary>The boneIndex0 component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(5)]
#endif
        [WProtoMember(5)]
        public int boneIndex0;

        /// <summary>The boneIndex1 component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(6)]
#endif
        [WProtoMember(6)]
        public int boneIndex1;

        /// <summary>The boneIndex2 component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(7)]
#endif
        [WProtoMember(7)]
        public int boneIndex2;

        /// <summary>The boneIndex3 component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(8)]
#endif
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
