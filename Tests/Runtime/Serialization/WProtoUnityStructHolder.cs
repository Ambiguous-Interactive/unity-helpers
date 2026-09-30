// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using ProtoBuf;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [ProtoContract]
    [WProtoContract]
    public partial class WProtoUnityStructHolder
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public Vector4 vector;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public Matrix4x4[] matrices;

        [ProtoMember(3)]
        [WProtoMember(3)]
        public Keyframe[] keyframes;

        [ProtoMember(4)]
        [WProtoMember(4)]
        public BoneWeight[] boneWeights;
    }
}
