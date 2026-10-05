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

    /// <summary>Carries the protobuf wire shape of Vector4.</summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct Vector4Surrogate
    {
        /// <summary>The x component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float x;

        /// <summary>The y component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float y;

        /// <summary>The z component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public float z;

        /// <summary>The w component.</summary>
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public float w;

#if UNITY_5_3_OR_NEWER
        /// <summary>Copies the Unity value into its protobuf shape.</summary>
        /// <param name="source">The Unity value.</param>
        /// <returns>The matching protobuf shape.</returns>
        public static implicit operator Vector4Surrogate(Vector4 source)
        {
            return new Vector4Surrogate
            {
                x = source.x,
                y = source.y,
                z = source.z,
                w = source.w,
            };
        }

        /// <summary>Restores the Unity value from its protobuf shape.</summary>
        /// <param name="source">The protobuf shape.</param>
        /// <returns>The matching Unity value.</returns>
        public static implicit operator Vector4(Vector4Surrogate source)
        {
            return new Vector4
            {
                x = source.x,
                y = source.y,
                z = source.z,
                w = source.w,
            };
        }
#endif
    }
}
