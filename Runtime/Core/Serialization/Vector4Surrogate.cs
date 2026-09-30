// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
#if UNITY_5_3_OR_NEWER
    using UnityEngine;
#endif

    /// <summary>Carries the protobuf wire shape of Vector4.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial struct Vector4Surrogate
    {
        /// <summary>The x component.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public float x;

        /// <summary>The y component.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public float y;

        /// <summary>The z component.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public float z;

        /// <summary>The w component.</summary>
        [ProtoMember(4)]
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
