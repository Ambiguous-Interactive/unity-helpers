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

// The same fourteen types again, this time as ROOT marshals. A surrogate registration substitutes
// the surrogate for a MEMBER and stops there, so a root Vector2 had no WallstopProto formatter at
// all and Serializer fell through to protobuf-net -- which builds
// ProtoBuf.Internal.StructValueChecker<Vector2>, a closed generic no source names, so an IL2CPP
// player either throws ExecutionEngineException (2021.3) or hands back a default value (6000.5).
// The marshals write the surrogate formatter's bytes, which is what the member path already writes.
// FastVector2Int and FastVector3Int are absent because WProtoBuiltInFormatters already serves them
// at the root; WProtoUnitySurrogateMarshalTests fails if a future surrogate arrives without either.
[assembly: WProtoRootMarshal(typeof(UnityEngine.Vector2), typeof(Vector2MarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.Vector3), typeof(Vector3MarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.Quaternion), typeof(QuaternionMarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.Color), typeof(ColorMarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.Color32), typeof(Color32MarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.Rect), typeof(RectMarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.RectInt), typeof(RectIntMarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.Bounds), typeof(BoundsMarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.BoundsInt), typeof(BoundsIntMarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.Vector2Int), typeof(Vector2IntMarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.Vector3Int), typeof(Vector3IntMarshalFormatter))]
[assembly: WProtoRootMarshal(typeof(UnityEngine.Resolution), typeof(ResolutionMarshalFormatter))]
[assembly: WProtoRootMarshal(
    typeof(WallstopStudios.UnityHelpers.Core.Math.Parabola),
    typeof(ParabolaMarshalFormatter)
)]
[assembly: WProtoRootMarshal(
    typeof(WallstopStudios.UnityHelpers.Core.DataStructure.ImmutableBitSet),
    typeof(ImmutableBitSetMarshalFormatter)
)]
