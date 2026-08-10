// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

// Assembly level because the real types are not ours to annotate, and because assembly
// attributes are the one thing a source generator can enumerate cheaply across every
// referenced assembly -- which is what lets a CONSUMER build find the surrogates this
// package ships. Field numbers come from the protobuf-net surrogates beside them, so the
// two produce identical bytes; FastVector2Int/FastVector3Int are deliberately absent,
// because those types keep hand-written formatters that recompute their cached hash
// instead of trusting it from the wire.
[assembly: WProtoSurrogate(typeof(UnityEngine.Vector2), typeof(Vector2Surrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.Vector3), typeof(Vector3Surrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.Quaternion), typeof(QuaternionSurrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.Color), typeof(ColorSurrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.Color32), typeof(Color32Surrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.Rect), typeof(RectSurrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.RectInt), typeof(RectIntSurrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.Bounds), typeof(BoundsSurrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.BoundsInt), typeof(BoundsIntSurrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.Vector2Int), typeof(Vector2IntSurrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.Vector3Int), typeof(Vector3IntSurrogate))]
[assembly: WProtoSurrogate(typeof(UnityEngine.Resolution), typeof(ResolutionSurrogate))]
[assembly: WProtoSurrogate(
    typeof(WallstopStudios.UnityHelpers.Core.Math.Parabola),
    typeof(ParabolaSurrogate)
)]
[assembly: WProtoSurrogate(
    typeof(WallstopStudios.UnityHelpers.Core.DataStructure.ImmutableBitSet),
    typeof(ImmutableBitSetSurrogate)
)]
