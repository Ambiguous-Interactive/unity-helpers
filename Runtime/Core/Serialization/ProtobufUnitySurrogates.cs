// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
#if !WALLSTOP_PROTO_ONLY
    using ProtoBuf;
#endif
#if !WALLSTOP_PROTO_ONLY
    using ProtoBuf.Meta;
#endif

#if UNITY_5_3_OR_NEWER
    using UnityEngine;
#endif

#if UNITY_5_3_OR_NEWER
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct Vector2Surrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float x;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float y;

        public static implicit operator Vector2Surrogate(Vector2 v) => new() { x = v.x, y = v.y };

        public static implicit operator Vector2(Vector2Surrogate s) => new(s.x, s.y);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct Vector3Surrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float x;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float y;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public float z;

        public static implicit operator Vector3Surrogate(Vector3 v) =>
            new()
            {
                x = v.x,
                y = v.y,
                z = v.z,
            };

        public static implicit operator Vector3(Vector3Surrogate s) => new(s.x, s.y, s.z);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct QuaternionSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float x;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float y;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public float z;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public float w;

        public static implicit operator QuaternionSurrogate(Quaternion q) =>
            new()
            {
                x = q.x,
                y = q.y,
                z = q.z,
                w = q.w,
            };

        public static implicit operator Quaternion(QuaternionSurrogate s) =>
            new(s.x, s.y, s.z, s.w);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct ColorSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float r;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float g;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public float b;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public float a;

        public static implicit operator ColorSurrogate(Color c) =>
            new()
            {
                r = c.r,
                g = c.g,
                b = c.b,
                a = c.a,
            };

        public static implicit operator Color(ColorSurrogate s) => new(s.r, s.g, s.b, s.a);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct Color32Surrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public byte r;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public byte g;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public byte b;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public byte a;

        public static implicit operator Color32Surrogate(Color32 c) =>
            new()
            {
                r = c.r,
                g = c.g,
                b = c.b,
                a = c.a,
            };

        public static implicit operator Color32(Color32Surrogate s) => new(s.r, s.g, s.b, s.a);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct RectSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float x;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float y;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public float width;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public float height;

        public static implicit operator RectSurrogate(Rect r) =>
            new()
            {
                x = r.x,
                y = r.y,
                width = r.width,
                height = r.height,
            };

        public static implicit operator Rect(RectSurrogate s) => new(s.x, s.y, s.width, s.height);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct RectIntSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public int x;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int y;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public int width;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public int height;

        public static implicit operator RectIntSurrogate(RectInt r) =>
            new()
            {
                x = r.x,
                y = r.y,
                width = r.width,
                height = r.height,
            };

        public static implicit operator RectInt(RectIntSurrogate s) =>
            new(s.x, s.y, s.width, s.height);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct BoundsSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float cx;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float cy;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public float cz;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public float sx;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(5)]
#endif
        [WProtoMember(5)]
        public float sy;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(6)]
#endif
        [WProtoMember(6)]
        public float sz;

        public static implicit operator BoundsSurrogate(Bounds b) =>
            new()
            {
                cx = b.center.x,
                cy = b.center.y,
                cz = b.center.z,
                sx = b.size.x,
                sy = b.size.y,
                sz = b.size.z,
            };

        public static implicit operator Bounds(BoundsSurrogate s) =>
            new(new Vector3(s.cx, s.cy, s.cz), new Vector3(s.sx, s.sy, s.sz));
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct BoundsIntSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public int px;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int py;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public int pz;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public int sx;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(5)]
#endif
        [WProtoMember(5)]
        public int sy;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(6)]
#endif
        [WProtoMember(6)]
        public int sz;

        public static implicit operator BoundsIntSurrogate(BoundsInt b) =>
            new()
            {
                px = b.position.x,
                py = b.position.y,
                pz = b.position.z,
                sx = b.size.x,
                sy = b.size.y,
                sz = b.size.z,
            };

        public static implicit operator BoundsInt(BoundsIntSurrogate s) =>
            new(new Vector3Int(s.px, s.py, s.pz), new Vector3Int(s.sx, s.sy, s.sz));
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct Vector2IntSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public int x;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int y;

        public static implicit operator Vector2IntSurrogate(Vector2Int v) =>
            new() { x = v.x, y = v.y };

        public static implicit operator Vector2Int(Vector2IntSurrogate s) => new(s.x, s.y);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct Vector3IntSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public int x;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int y;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public int z;

        public static implicit operator Vector3IntSurrogate(Vector3Int v) =>
            new()
            {
                x = v.x,
                y = v.y,
                z = v.z,
            };

        public static implicit operator Vector3Int(Vector3IntSurrogate s) => new(s.x, s.y, s.z);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct ResolutionSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public int width;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int height;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public int refreshRate;

        [Obsolete("Obsolete")]
        public static implicit operator ResolutionSurrogate(Resolution r) =>
            new()
            {
                width = r.width,
                height = r.height,
                refreshRate = r.refreshRate,
            };

        public static implicit operator Resolution(ResolutionSurrogate s)
        {
            Resolution r = new() { width = s.width, height = s.height };
#if !UNITY_2022_2_OR_NEWER
            r.refreshRate = s.refreshRate;
#endif
            return r;
        }
    }
#endif

    // Mutable surrogates avoid protobuf-net reflection paths that cannot construct readonly structs on AOT.

    // Keep legacy int32 tags readable and emit sint32 on new tags; reusing a tag would silently reinterpret old coordinates.
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    internal partial struct FastVector2IntSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(5, DataFormat = DataFormat.ZigZag)]
#endif
        [WProtoMember(5, DataFormat = WProtoDataFormat.ZigZag)]
        public int x;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(6, DataFormat = DataFormat.ZigZag)]
#endif
        [WProtoMember(6, DataFormat = WProtoDataFormat.ZigZag)]
        public int y;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public int legacyX;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int legacyY;

        public static implicit operator FastVector2IntSurrogate(FastVector2Int v) =>
            new() { x = v.x, y = v.y };

        // Prefer a present zigzag field, otherwise read its legacy encoding.
        public static implicit operator FastVector2Int(FastVector2IntSurrogate s) =>
            new(s.x != 0 ? s.x : s.legacyX, s.y != 0 ? s.y : s.legacyY);
    }

    // Legacy z stays at tag 4 because tag 3 previously stored the hash.
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    internal partial struct FastVector3IntSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(5, DataFormat = DataFormat.ZigZag)]
#endif
        [WProtoMember(5, DataFormat = WProtoDataFormat.ZigZag)]
        public int x;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(6, DataFormat = DataFormat.ZigZag)]
#endif
        [WProtoMember(6, DataFormat = WProtoDataFormat.ZigZag)]
        public int y;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(7, DataFormat = DataFormat.ZigZag)]
#endif
        [WProtoMember(7, DataFormat = WProtoDataFormat.ZigZag)]
        public int z;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public int legacyX;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int legacyY;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public int legacyZ;

        public static implicit operator FastVector3IntSurrogate(FastVector3Int v) =>
            new()
            {
                x = v.x,
                y = v.y,
                z = v.z,
            };

        public static implicit operator FastVector3Int(FastVector3IntSurrogate s) =>
            new(s.x != 0 ? s.x : s.legacyX, s.y != 0 ? s.y : s.legacyY, s.z != 0 ? s.z : s.legacyZ);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct ParabolaSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public float length;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public float a;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(3)]
#endif
        [WProtoMember(3)]
        public float b;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(4)]
#endif
        [WProtoMember(4)]
        public float maxHeight;

        public static implicit operator ParabolaSurrogate(Parabola p) =>
            new()
            {
                length = p.Length,
                a = p.A,
                b = p.B,
                maxHeight = p.MaxHeight,
            };

        // Restore stored coefficients verbatim, including default and legacy underflowed values.
        public static implicit operator Parabola(ParabolaSurrogate s) =>
            new(s.maxHeight, s.length, s.a, s.b);
    }

#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    public partial struct ImmutableBitSetSurrogate
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1)]
#endif
        [WProtoMember(1)]
        public ulong[] bits;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int capacity;

        public static implicit operator ImmutableBitSetSurrogate(ImmutableBitSet b) =>
            new() { bits = b.GetBitsArrayCopy(), capacity = b.Capacity };

        public static implicit operator ImmutableBitSet(ImmutableBitSetSurrogate s) =>
            new(s.bits, s.capacity);
    }

    // Wrappers omit IEnumerable so protobuf-net cannot reinterpret them as repeated fields.

    // See: https://github.com/protobuf-net/protobuf-net/issues/1185

    /// <summary>
    /// Protobuf wrapper for SerializableHashSet that avoids IEnumerable collection detection.
    /// </summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    internal sealed partial class SerializableHashSetProtoWrapper<T>
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1, OverwriteList = true)]
#endif
        [WProtoMember(1, OverwriteList = true)]
        public T[] Items;
    }

    /// <summary>
    /// Protobuf wrapper for SerializableSortedSet that avoids IEnumerable collection detection.
    /// </summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    internal sealed partial class SerializableSortedSetProtoWrapper<T>
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1, OverwriteList = true)]
#endif
        [WProtoMember(1, OverwriteList = true)]
        public T[] Items;
    }

    /// <summary>
    /// Protobuf wrapper for SerializableDictionary that avoids IEnumerable collection detection.
    /// </summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    internal sealed partial class SerializableDictionaryProtoWrapper<TKey, TValue>
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1, OverwriteList = true)]
#endif
        [WProtoMember(1, OverwriteList = true)]
        public TKey[] Keys;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2, OverwriteList = true)]
#endif
        [WProtoMember(2, OverwriteList = true)]
        public TValue[] Values;
    }

    /// <summary>
    /// Protobuf wrapper for SerializableSortedDictionary that avoids IEnumerable collection detection.
    /// </summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    internal sealed partial class SerializableSortedDictionaryProtoWrapper<TKey, TValue>
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1, OverwriteList = true)]
#endif
        [WProtoMember(1, OverwriteList = true)]
        public TKey[] Keys;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2, OverwriteList = true)]
#endif
        [WProtoMember(2, OverwriteList = true)]
        public TValue[] Values;
    }

    // Plain wrappers avoid building the original collection models through unsupported AOT reflection.

    /// <summary>
    /// Protobuf wrapper for <see cref="Deque{T}"/>: ordered items (front to back) plus capacity.
    /// </summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    internal sealed partial class DequeProtoWrapper<T>
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1, OverwriteList = true)]
#endif
        [WProtoMember(1, OverwriteList = true)]
        public T[] Items;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int Capacity;
    }

    /// <summary>
    /// Protobuf wrapper for <see cref="CyclicBuffer{T}"/>: ordered items (oldest to newest) plus capacity.
    /// </summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    internal sealed partial class CyclicBufferProtoWrapper<T>
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1, OverwriteList = true)]
#endif
        [WProtoMember(1, OverwriteList = true)]
        public T[] Items;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int Capacity;
    }

    /// <summary>
    /// Protobuf wrapper for <see cref="SparseSet"/>: dense elements plus universe size (capacity).
    /// </summary>
#if !WALLSTOP_PROTO_ONLY
    [ProtoContract]
#endif
    [WProtoContract]
    internal sealed partial class SparseSetProtoWrapper
    {
#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(1, OverwriteList = true)]
#endif
        [WProtoMember(1, OverwriteList = true)]
        public int[] Elements;

#if !WALLSTOP_PROTO_ONLY
        [ProtoMember(2)]
#endif
        [WProtoMember(2)]
        public int Capacity;

        internal bool TryRestore(out SparseSet value)
        {
            int[] elements = Elements ?? Array.Empty<int>();
            bool inferCapacity = Capacity <= 0;
            int capacity = inferCapacity ? 1 : Capacity;
            foreach (int element in elements)
            {
                if (element < 0 || int.MaxValue <= element)
                {
                    value = default;
                    return false;
                }

                if (inferCapacity)
                {
                    int requiredCapacity = element + 1;
                    if (capacity < requiredCapacity)
                    {
                        capacity = requiredCapacity;
                    }
                }
                else if (capacity <= element)
                {
                    value = default;
                    return false;
                }
            }

            if (!SerializationCapacityLimits.TryAccept(capacity, elements.Length, out _))
            {
                value = default;
                return false;
            }

            SparseSet restored = new SparseSet(capacity);
            foreach (int element in elements)
            {
                restored.TryAdd(element);
            }
            value = restored;
            return true;
        }
    }

#if !WALLSTOP_PROTO_ONLY
    internal static class ProtobufUnityModel
    {
        /// <summary>
        /// The types whose surrogate was refused, as a view a caller cannot mutate.
        /// </summary>
        internal static IReadOnlyList<string> Refused => RefusedView;

        /// <summary>
        /// Every type this model routes through a surrogate, in registration order.
        /// </summary>
        internal static IReadOnlyList<Type> Surrogated => SurrogatedView;

        /// <summary>
        /// The types whose surrogate could not be registered, in the order they were refused.
        /// </summary>
        /// <remarks>
        /// A list rather than a flag because the useful question is <b>which</b> type is now writing
        /// the wrong bytes, and because a test needs to be able to assert that the answer is none.
        /// </remarks>
        internal static readonly List<string> RegistrationFailures = new List<string>();

        // The read-only view must observe registrations appended by the static constructor.
        private static readonly ReadOnlyCollection<string> RefusedView =
            new ReadOnlyCollection<string>(RegistrationFailures);

        private static readonly List<Type> SurrogatedList = new List<Type>();

        // Record refused registrations too: WallstopProto still needs the complete surrogate roster.
        private static readonly ReadOnlyCollection<Type> SurrogatedView =
            new ReadOnlyCollection<Type>(SurrogatedList);

        static ProtobufUnityModel()
        {
            RuntimeTypeModel model;
            try
            {
                model = RuntimeTypeModel.Default;
            }
            catch
            {
                // Keep recording surrogate pairs even when protobuf-net is unavailable; JSON-only operation remains valid.
                model = null;
            }

#if UNITY_5_3_OR_NEWER
            Register<Vector2, Vector2Surrogate>(model);
            Register<Vector3, Vector3Surrogate>(model);
            Register<Vector4, Vector4Surrogate>(model);
            Register<Matrix4x4, Matrix4x4Surrogate>(model);
            Register<Keyframe, KeyframeSurrogate>(model);
            Register<BoneWeight, BoneWeightSurrogate>(model);
            Register<Quaternion, QuaternionSurrogate>(model);
            Register<Color, ColorSurrogate>(model);
            Register<Color32, Color32Surrogate>(model);
            Register<Rect, RectSurrogate>(model);
            Register<RectInt, RectIntSurrogate>(model);
            Register<Bounds, BoundsSurrogate>(model);
            Register<BoundsInt, BoundsIntSurrogate>(model);
            Register<Vector2Int, Vector2IntSurrogate>(model);
            Register<Vector3Int, Vector3IntSurrogate>(model);
            Register<Resolution, ResolutionSurrogate>(model);
#endif

            // Disable direct contract inference so readonly structs use mutable surrogates.
            Register<FastVector2Int, FastVector2IntSurrogate>(model);
            Register<FastVector3Int, FastVector3IntSurrogate>(model);
            Register<Parabola, ParabolaSurrogate>(model);
            Register<ImmutableBitSet, ImmutableBitSetSurrogate>(model);

            // Collection wrappers bypass protobuf-net IgnoreListHandling failures; see https://github.com/protobuf-net/protobuf-net/issues/1185.
        }

        internal static void EnsureInitialized() { }

        /// <summary>
        /// Points protobuf-net at <typeparamref name="TSurrogate"/> for <typeparamref name="TReal"/>.
        /// </summary>
        /// <typeparam name="TReal">The type being given a wire shape.</typeparam>
        /// <typeparam name="TSurrogate">The shape it is given.</typeparam>
        /// <remarks>
        /// <para>
        /// Guarded one registration at a time, which is the whole point of the method existing.
        /// <c>RuntimeTypeModel.Default</c> is process-global and freezes a type the first time it
        /// serializes one, so anything that reaches protobuf-net before this constructor runs --
        /// another package, or a consumer calling <c>ProtoBuf.Serializer</c> directly -- makes
        /// <c>Add</c> throw for that type. Under one shared <c>try</c> that single throw skipped
        /// every registration after it, and the package then silently encoded <see cref="Vector3"/>,
        /// <see cref="Color"/> and the rest through whatever protobuf-net inferred: different bytes,
        /// no exception, saves that do not load.
        /// </para>
        /// <para>
        /// The failure is recorded rather than thrown, because a static constructor that throws
        /// takes the whole type down with a <c>TypeInitializationException</c> on every later use,
        /// which is worse than one type having the wrong shape.
        /// </para>
        /// </remarks>
        private static void Register<TReal, TSurrogate>(RuntimeTypeModel model)
        {
            SurrogatedList.Add(typeof(TReal));
            if (model == null)
            {
                return;
            }

            try
            {
                // WallstopProto declares these shapes separately in WProtoUnitySurrogateRegistrations.
#pragma warning disable WPROTO048
                model
                    .Add(typeof(TReal), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(TSurrogate));
#pragma warning restore WPROTO048
            }
            catch (Exception error)
            {
                RegistrationFailures.Add(typeof(TReal).Name);
#if ENABLE_IL2CPP || !UNITY_5_3_OR_NEWER
                _ = error;
#else
                // AOT always uses WallstopProto; expected protobuf-net refusals must not log startup errors.
                Debug.LogError(
                    $"[UnityHelpers] protobuf-net already bound {typeof(TReal).Name}, so its "
                        + $"{typeof(TSurrogate).Name} could not be registered and the type will be "
                        + $"encoded with different bytes than this package documents. Something "
                        + $"serialized it before UnityHelpers' Serializer was first touched. {error}"
                );
#endif
            }
        }
    }
#endif
}
