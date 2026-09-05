// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

using WallstopStudios.UnityHelpers.Core.Serialization;
using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

// Assembly attributes expose Unity surrogate mappings to consumer generators without annotating Unity types.

// FastVector formatters recompute cached hashes instead of trusting serialized values.

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

// Root marshals also need registration: member surrogate mappings alone cannot serve root values.

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
