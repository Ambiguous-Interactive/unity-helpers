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

    /// <summary>Carries the protobuf wire shape of Keyframe.</summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct KeyframeSurrogate
    {
        /// <summary>The time component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float time;

        /// <summary>The value component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float value;

        /// <summary>The inTangent component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public float inTangent;

        /// <summary>The outTangent component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public float outTangent;

        /// <summary>The inWeight component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(5)]
#endif
        [WProtoMember(5)]
        public float inWeight;

        /// <summary>The outWeight component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(6)]
#endif
        [WProtoMember(6)]
        public float outWeight;

        /// <summary>The weightedMode component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(7)]
#endif
        [WProtoMember(7)]
        public int weightedMode;

#if UNITY_5_3_OR_NEWER
        /// <summary>Copies the Unity value into its protobuf shape.</summary>
        /// <param name="source">The Unity value.</param>
        /// <returns>The matching protobuf shape.</returns>
        public static implicit operator KeyframeSurrogate(Keyframe source)
        {
            return new KeyframeSurrogate
            {
                time = source.time,
                value = source.value,
                inTangent = source.inTangent,
                outTangent = source.outTangent,
                inWeight = source.inWeight,
                outWeight = source.outWeight,
                weightedMode = (int)source.weightedMode,
            };
        }

        /// <summary>Restores the Unity value from its protobuf shape.</summary>
        /// <param name="source">The protobuf shape.</param>
        /// <returns>The matching Unity value.</returns>
        public static implicit operator Keyframe(KeyframeSurrogate source)
        {
            return new Keyframe
            {
                time = source.time,
                value = source.value,
                inTangent = source.inTangent,
                outTangent = source.outTangent,
                inWeight = source.inWeight,
                outWeight = source.outWeight,
                weightedMode = (WeightedMode)source.weightedMode,
            };
        }
#endif
    }
}
