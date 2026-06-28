// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization
{
    using System;
    using ProtoBuf;
    using ProtoBuf.Meta;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Math;

    // Surrogates allow protobuf-net to serialize Unity structs we cannot annotate directly.
    [ProtoContract]
    internal struct Vector2Surrogate
    {
        [ProtoMember(1)]
        public float x;

        [ProtoMember(2)]
        public float y;

        public static implicit operator Vector2Surrogate(Vector2 v) => new() { x = v.x, y = v.y };

        public static implicit operator Vector2(Vector2Surrogate s) => new(s.x, s.y);
    }

    [ProtoContract]
    internal struct Vector3Surrogate
    {
        [ProtoMember(1)]
        public float x;

        [ProtoMember(2)]
        public float y;

        [ProtoMember(3)]
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

    [ProtoContract]
    internal struct QuaternionSurrogate
    {
        [ProtoMember(1)]
        public float x;

        [ProtoMember(2)]
        public float y;

        [ProtoMember(3)]
        public float z;

        [ProtoMember(4)]
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

    [ProtoContract]
    internal struct ColorSurrogate
    {
        [ProtoMember(1)]
        public float r;

        [ProtoMember(2)]
        public float g;

        [ProtoMember(3)]
        public float b;

        [ProtoMember(4)]
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

    [ProtoContract]
    internal struct Color32Surrogate
    {
        [ProtoMember(1)]
        public byte r;

        [ProtoMember(2)]
        public byte g;

        [ProtoMember(3)]
        public byte b;

        [ProtoMember(4)]
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

    [ProtoContract]
    internal struct RectSurrogate
    {
        [ProtoMember(1)]
        public float x;

        [ProtoMember(2)]
        public float y;

        [ProtoMember(3)]
        public float width;

        [ProtoMember(4)]
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

    [ProtoContract]
    internal struct RectIntSurrogate
    {
        [ProtoMember(1)]
        public int x;

        [ProtoMember(2)]
        public int y;

        [ProtoMember(3)]
        public int width;

        [ProtoMember(4)]
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

    [ProtoContract]
    internal struct BoundsSurrogate
    {
        [ProtoMember(1)]
        public float cx;

        [ProtoMember(2)]
        public float cy;

        [ProtoMember(3)]
        public float cz;

        [ProtoMember(4)]
        public float sx;

        [ProtoMember(5)]
        public float sy;

        [ProtoMember(6)]
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

    [ProtoContract]
    internal struct BoundsIntSurrogate
    {
        [ProtoMember(1)]
        public int px;

        [ProtoMember(2)]
        public int py;

        [ProtoMember(3)]
        public int pz;

        [ProtoMember(4)]
        public int sx;

        [ProtoMember(5)]
        public int sy;

        [ProtoMember(6)]
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

    [ProtoContract]
    internal struct Vector2IntSurrogate
    {
        [ProtoMember(1)]
        public int x;

        [ProtoMember(2)]
        public int y;

        public static implicit operator Vector2IntSurrogate(Vector2Int v) =>
            new() { x = v.x, y = v.y };

        public static implicit operator Vector2Int(Vector2IntSurrogate s) => new(s.x, s.y);
    }

    [ProtoContract]
    internal struct Vector3IntSurrogate
    {
        [ProtoMember(1)]
        public int x;

        [ProtoMember(2)]
        public int y;

        [ProtoMember(3)]
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

    [ProtoContract]
    internal struct ResolutionSurrogate
    {
        [ProtoMember(1)]
        public int width;

        [ProtoMember(2)]
        public int height;

        [ProtoMember(3)]
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

    // Surrogates for our own immutable [ProtoContract] readonly structs. Under IL2CPP/AOT
    // protobuf-net cannot bind a parameterized constructor nor assign readonly fields without
    // Reflection.Emit, so it falls back to ParameterInfo.GetRequiredCustomModifiers which hits the
    // unsupported RuntimeParameterInfo::GetTypeModifiers icall. Routing these types through a
    // mutable surrogate uses protobuf-net's working surrogate path instead. Field numbers mirror
    // the originals exactly so the wire format is byte-identical to the pre-surrogate mono output.

    [ProtoContract]
    internal sealed class FastVector2IntSurrogate
    {
        [ProtoMember(1)]
        public int x;

        [ProtoMember(2)]
        public int y;

        // Mirrors FastVector2Int's serialized cached hash (ProtoMember 3) for wire parity; the value
        // is recomputed by the FastVector2Int constructor on conversion, so it is not trusted on read.
        [ProtoMember(3)]
        public int hash;

        public static implicit operator FastVector2IntSurrogate(FastVector2Int v) =>
            new()
            {
                x = v.x,
                y = v.y,
                hash = v.GetHashCode(),
            };

        public static implicit operator FastVector2Int(FastVector2IntSurrogate s) => new(s.x, s.y);
    }

    [ProtoContract]
    internal sealed class FastVector3IntSurrogate
    {
        [ProtoMember(1)]
        public int x;

        [ProtoMember(2)]
        public int y;

        // FastVector3Int intentionally serializes its cached hash as ProtoMember 3 and z as
        // ProtoMember 4 (out of order). The surrogate preserves that ordering for wire parity.
        [ProtoMember(3)]
        public int hash;

        [ProtoMember(4)]
        public int z;

        public static implicit operator FastVector3IntSurrogate(FastVector3Int v) =>
            new()
            {
                x = v.x,
                y = v.y,
                hash = v.GetHashCode(),
                z = v.z,
            };

        public static implicit operator FastVector3Int(FastVector3IntSurrogate s) =>
            new(s.x, s.y, s.z);
    }

    [ProtoContract]
    internal sealed class ParabolaSurrogate
    {
        [ProtoMember(1)]
        public float length;

        [ProtoMember(2)]
        public float a;

        [ProtoMember(3)]
        public float b;

        [ProtoMember(4)]
        public float maxHeight;

        public static implicit operator ParabolaSurrogate(Parabola p) =>
            new()
            {
                length = p.Length,
                a = p.A,
                b = p.B,
                maxHeight = p.MaxHeight,
            };

        // Uses the internal coefficient constructor so all four fields are restored verbatim and the
        // public constructor's positivity validation (which would throw for default/zero) is bypassed.
        public static implicit operator Parabola(ParabolaSurrogate s) =>
            new(s.maxHeight, s.length, s.a, s.b);
    }

    [ProtoContract]
    internal sealed class ImmutableBitSetSurrogate
    {
        [ProtoMember(1)]
        public ulong[] bits;

        [ProtoMember(2)]
        public int capacity;

        public static implicit operator ImmutableBitSetSurrogate(ImmutableBitSet b) =>
            new() { bits = b.GetBitsArrayCopy(), capacity = b.Capacity };

        public static implicit operator ImmutableBitSet(ImmutableBitSetSurrogate s) =>
            new(s.bits, s.capacity);
    }

    // Protobuf wrapper types for serializable collections.
    // These types do NOT implement IEnumerable, which prevents protobuf-net's
    // collection detection from treating them as repeated fields.
    // See: https://github.com/protobuf-net/protobuf-net/issues/1185

    /// <summary>
    /// Protobuf wrapper for SerializableHashSet that avoids IEnumerable collection detection.
    /// </summary>
    [ProtoContract]
    internal sealed class SerializableHashSetProtoWrapper<T>
    {
        [ProtoMember(1, OverwriteList = true)]
        public T[] Items;
    }

    /// <summary>
    /// Protobuf wrapper for SerializableSortedSet that avoids IEnumerable collection detection.
    /// </summary>
    [ProtoContract]
    internal sealed class SerializableSortedSetProtoWrapper<T>
    {
        [ProtoMember(1, OverwriteList = true)]
        public T[] Items;
    }

    /// <summary>
    /// Protobuf wrapper for SerializableDictionary that avoids IEnumerable collection detection.
    /// </summary>
    [ProtoContract]
    internal sealed class SerializableDictionaryProtoWrapper<TKey, TValue>
    {
        [ProtoMember(1, OverwriteList = true)]
        public TKey[] Keys;

        [ProtoMember(2, OverwriteList = true)]
        public TValue[] Values;
    }

    /// <summary>
    /// Protobuf wrapper for SerializableSortedDictionary that avoids IEnumerable collection detection.
    /// </summary>
    [ProtoContract]
    internal sealed class SerializableSortedDictionaryProtoWrapper<TKey, TValue>
    {
        [ProtoMember(1, OverwriteList = true)]
        public TKey[] Keys;

        [ProtoMember(2, OverwriteList = true)]
        public TValue[] Values;
    }

    // Wrappers for Deque/CyclicBuffer/SparseSet. These [ProtoContract] classes carry parameterized
    // data plus [ProtoAfterDeserialization] reconstruction hooks; building their per-type model under
    // IL2CPP/AOT trips the unsupported RuntimeParameterInfo::GetTypeModifiers icall (Class A). Routing
    // them through these plain array/scalar POCOs (reconstructed in Serializer) bypasses protobuf-net's
    // per-type model build for the originals entirely and also avoids the post-deserialization hook.

    /// <summary>
    /// Protobuf wrapper for <see cref="Deque{T}"/>: ordered items (front to back) plus capacity.
    /// </summary>
    [ProtoContract]
    internal sealed class DequeProtoWrapper<T>
    {
        [ProtoMember(1, OverwriteList = true)]
        public T[] Items;

        [ProtoMember(2)]
        public int Capacity;
    }

    /// <summary>
    /// Protobuf wrapper for <see cref="CyclicBuffer{T}"/>: ordered items (oldest to newest) plus capacity.
    /// </summary>
    [ProtoContract]
    internal sealed class CyclicBufferProtoWrapper<T>
    {
        [ProtoMember(1, OverwriteList = true)]
        public T[] Items;

        [ProtoMember(2)]
        public int Capacity;
    }

    /// <summary>
    /// Protobuf wrapper for <see cref="SparseSet"/>: dense elements plus universe size (capacity).
    /// </summary>
    [ProtoContract]
    internal sealed class SparseSetProtoWrapper
    {
        [ProtoMember(1, OverwriteList = true)]
        public int[] Elements;

        [ProtoMember(2)]
        public int Capacity;
    }

    internal static class ProtobufUnityModel
    {
        static ProtobufUnityModel()
        {
            try
            {
                RuntimeTypeModel model = RuntimeTypeModel.Default;

                // Register surrogates for Unity types we cannot annotate directly.
                model
                    .Add(typeof(Vector2), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(Vector2Surrogate));
                model
                    .Add(typeof(Vector3), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(Vector3Surrogate));
                model
                    .Add(typeof(Quaternion), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(QuaternionSurrogate));
                model
                    .Add(typeof(Color), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(ColorSurrogate));
                model
                    .Add(typeof(Color32), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(Color32Surrogate));
                model
                    .Add(typeof(Rect), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(RectSurrogate));
                model
                    .Add(typeof(RectInt), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(RectIntSurrogate));
                model
                    .Add(typeof(Bounds), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(BoundsSurrogate));
                model
                    .Add(typeof(BoundsInt), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(BoundsIntSurrogate));
                model
                    .Add(typeof(Vector2Int), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(Vector2IntSurrogate));
                model
                    .Add(typeof(Vector3Int), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(Vector3IntSurrogate));
                model
                    .Add(typeof(Resolution), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(ResolutionSurrogate));

                // Immutable readonly [ProtoContract] structs we own. applyDefaultBehaviour: false
                // discards their direct contract so the mutable surrogate path is used instead; this
                // is what keeps them serializable under IL2CPP/AOT (Class B). Wire format is preserved.
                model
                    .Add(typeof(FastVector2Int), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(FastVector2IntSurrogate));
                model
                    .Add(typeof(FastVector3Int), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(FastVector3IntSurrogate));
                model
                    .Add(typeof(Parabola), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(ParabolaSurrogate));
                model
                    .Add(typeof(ImmutableBitSet), applyDefaultBehaviour: false)
                    .SetSurrogate(typeof(ImmutableBitSetSurrogate));

                // NOTE: SerializableHashSet, SerializableSortedSet, SerializableDictionary, and
                // SerializableSortedDictionary are handled via wrapper-based serialization in
                // Serializer.ProtoSerialize/ProtoDeserialize rather than RuntimeTypeModel configuration.
                // This is necessary because protobuf-net's TryGetRepeatedProvider does not respect
                // IgnoreListHandling, causing IEnumerable types to always be treated as collections.
                // See: https://github.com/protobuf-net/protobuf-net/issues/1185
            }
            catch
            {
                // In restricted environments, model mutation may fail; ignore to keep JSON-only scenarios working.
            }
        }

        internal static void EnsureInitialized() { /* triggers static ctor */
        }
    }
}
