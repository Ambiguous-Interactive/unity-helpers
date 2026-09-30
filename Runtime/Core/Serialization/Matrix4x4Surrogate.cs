// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
#if UNITY_5_3_OR_NEWER
    using UnityEngine;
#endif

    /// <summary>Carries the protobuf wire shape of Matrix4x4.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial struct Matrix4x4Surrogate
    {
        /// <summary>The m00 component.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public float m00;

        /// <summary>The m10 component.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public float m10;

        /// <summary>The m20 component.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public float m20;

        /// <summary>The m30 component.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public float m30;

        /// <summary>The m01 component.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public float m01;

        /// <summary>The m11 component.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        public float m11;

        /// <summary>The m21 component.</summary>
        [ProtoMember(7)]
        [WProtoMember(7)]
        public float m21;

        /// <summary>The m31 component.</summary>
        [ProtoMember(8)]
        [WProtoMember(8)]
        public float m31;

        /// <summary>The m02 component.</summary>
        [ProtoMember(9)]
        [WProtoMember(9)]
        public float m02;

        /// <summary>The m12 component.</summary>
        [ProtoMember(10)]
        [WProtoMember(10)]
        public float m12;

        /// <summary>The m22 component.</summary>
        [ProtoMember(11)]
        [WProtoMember(11)]
        public float m22;

        /// <summary>The m32 component.</summary>
        [ProtoMember(12)]
        [WProtoMember(12)]
        public float m32;

        /// <summary>The m03 component.</summary>
        [ProtoMember(13)]
        [WProtoMember(13)]
        public float m03;

        /// <summary>The m13 component.</summary>
        [ProtoMember(14)]
        [WProtoMember(14)]
        public float m13;

        /// <summary>The m23 component.</summary>
        [ProtoMember(15)]
        [WProtoMember(15)]
        public float m23;

        /// <summary>The m33 component.</summary>
        [ProtoMember(16)]
        [WProtoMember(16)]
        public float m33;

#if UNITY_5_3_OR_NEWER
        /// <summary>Copies the Unity value into its protobuf shape.</summary>
        /// <param name="source">The Unity value.</param>
        /// <returns>The matching protobuf shape.</returns>
        public static implicit operator Matrix4x4Surrogate(Matrix4x4 source)
        {
            return new Matrix4x4Surrogate
            {
                m00 = source.m00,
                m10 = source.m10,
                m20 = source.m20,
                m30 = source.m30,
                m01 = source.m01,
                m11 = source.m11,
                m21 = source.m21,
                m31 = source.m31,
                m02 = source.m02,
                m12 = source.m12,
                m22 = source.m22,
                m32 = source.m32,
                m03 = source.m03,
                m13 = source.m13,
                m23 = source.m23,
                m33 = source.m33,
            };
        }

        /// <summary>Restores the Unity value from its protobuf shape.</summary>
        /// <param name="source">The protobuf shape.</param>
        /// <returns>The matching Unity value.</returns>
        public static implicit operator Matrix4x4(Matrix4x4Surrogate source)
        {
            return new Matrix4x4
            {
                m00 = source.m00,
                m10 = source.m10,
                m20 = source.m20,
                m30 = source.m30,
                m01 = source.m01,
                m11 = source.m11,
                m21 = source.m21,
                m31 = source.m31,
                m02 = source.m02,
                m12 = source.m12,
                m22 = source.m22,
                m32 = source.m32,
                m03 = source.m03,
                m13 = source.m13,
                m23 = source.m23,
                m33 = source.m33,
            };
        }
#endif
    }
}
