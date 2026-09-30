// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
#if UNITY_5_3_OR_NEWER
    using UnityEngine;
#endif

    /// <summary>Carries the protobuf wire shape of Keyframe.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial struct KeyframeSurrogate
    {
        /// <summary>The time component.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public float time;

        /// <summary>The value component.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public float value;

        /// <summary>The inTangent component.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public float inTangent;

        /// <summary>The outTangent component.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public float outTangent;

        /// <summary>The inWeight component.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public float inWeight;

        /// <summary>The outWeight component.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        public float outWeight;

        /// <summary>The weightedMode component.</summary>
        [ProtoMember(7)]
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
