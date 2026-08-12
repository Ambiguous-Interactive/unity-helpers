// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text.Json.Serialization;
    using Helper;
    using ProtoBuf;
    using UnityEngine;

    /// <summary>
    /// Lightweight alternative to Unity's <see cref="Vector3Int"/> that caches its hash to accelerate dictionary and set lookups.
    /// Converts seamlessly to and from Unity vectors so you can drop it into serialization-friendly containers without refactors.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// FastVector3Int voxel = new FastVector3Int(4, 2, 6);
    /// Dictionary<FastVector3Int, float> heatMap = new Dictionary<FastVector3Int, float>();
    /// heatMap[voxel] = 0.75f;
    /// bool isTracked = heatMap.ContainsKey(new FastVector3Int(4, 2, 6));
    /// Vector3Int unityVector = voxel;
    /// ]]></code>
    /// </example>
    [Serializable]
#pragma warning disable WPROTO030 // Served by a hand-written formatter and protobuf-net surrogate.
    [ProtoContract]
#pragma warning restore WPROTO030
    public readonly partial struct FastVector3Int
        : IEquatable<FastVector3Int>,
            IEquatable<FastVector2Int>,
            IEquatable<Vector3Int>,
            IEquatable<Vector2Int>,
            IComparable<FastVector3Int>,
            IComparable<FastVector2Int>,
            IComparable<Vector3Int>,
            IComparable<Vector2Int>,
            IComparable
    {
        /// <summary>
        /// Represents the origin vector <c>(0, 0, 0)</c>, useful as a default without allocating new instances.
        /// </summary>
        /// <example>
        /// <code>
        /// FastVector3Int origin = FastVector3Int.zero;
        /// </code>
        /// </example>
        public static readonly FastVector3Int zero = new(0, 0, 0);

        [ProtoMember(1)]
        [JsonIgnore]
        public readonly int x;

        [ProtoMember(2)]
        [JsonIgnore]
        public readonly int y;

        [ProtoMember(4)]
        [JsonIgnore]
        public readonly int z;

        // Out of order proto is expected
        [ProtoMember(3)]
        private readonly int _hash;

        /// <summary>
        /// Initializes a fast vector with explicit components and a cached hash.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <param name="z">The Z component.</param>
        /// <example>
        /// <code>
        /// FastVector3Int gridPosition = new FastVector3Int(12, -3, 5);
        /// </code>
        /// </example>
        [JsonConstructor]
        public FastVector3Int(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            _hash = Objects.HashCode(x, y, z);
        }

        /// <summary>
        /// Initializes a fast vector from a Unity <see cref="Vector3Int"/> while caching its hash.
        /// </summary>
        /// <param name="vector">The Unity vector to convert.</param>
        /// <example>
        /// <code>
        /// FastVector3Int cached = new FastVector3Int(new Vector3Int(2, 7, 1));
        /// </code>
        /// </example>
        public FastVector3Int(Vector3Int vector)
            : this(vector.x, vector.y, vector.z) { }

        /// <summary>
        /// Initializes a fast vector with a zero Z component.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <example>
        /// <code>
        /// FastVector3Int planar = new FastVector3Int(3, 4);
        /// </code>
        /// </example>
        public FastVector3Int(int x, int y)
            : this(x, y, 0) { }

        /// <summary>
        /// Gets the stored X component.
        /// </summary>
        /// <example>
        /// <code>
        /// int column = voxel.X;
        /// </code>
        /// </example>
        [JsonPropertyName("x")]
        public int X => x;

        /// <summary>
        /// Gets the stored Y component.
        /// </summary>
        /// <example>
        /// <code>
        /// int row = voxel.Y;
        /// </code>
        /// </example>
        [JsonPropertyName("y")]
        public int Y => y;

        /// <summary>
        /// Gets the stored Z component.
        /// </summary>
        /// <example>
        /// <code>
        /// int level = voxel.Z;
        /// </code>
        /// </example>
        [JsonPropertyName("z")]
        public int Z => z;

        /// <summary>
        /// Determines whether two fast vectors have identical components.
        /// </summary>
        /// <param name="lhs">The left-hand vector.</param>
        /// <param name="rhs">The right-hand vector.</param>
        /// <returns><c>true</c> when both vectors match.</returns>
        /// <example>
        /// <code>
        /// bool matches = FastVector3Int.zero == new FastVector3Int(0, 0, 0);
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FastVector3Int lhs, FastVector3Int rhs)
        {
            return lhs.Equals(rhs);
        }

        /// <summary>
        /// Determines whether two fast vectors differ.
        /// </summary>
        /// <param name="lhs">The left-hand vector.</param>
        /// <param name="rhs">The right-hand vector.</param>
        /// <returns><c>true</c> when the vectors are not equal.</returns>
        /// <example>
        /// <code>
        /// bool changed = current != previous;
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FastVector3Int lhs, FastVector3Int rhs)
        {
            return !lhs.Equals(rhs);
        }

        /// <summary>
        /// Adds two fast vectors component-wise.
        /// </summary>
        /// <param name="lhs">The first summand.</param>
        /// <param name="rhs">The second summand.</param>
        /// <returns>The component-wise sum.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int destination = origin + new FastVector3Int(1, 0, 0);
        /// </code>
        /// </example>
        public static FastVector3Int operator +(FastVector3Int lhs, FastVector3Int rhs)
        {
            return new FastVector3Int(lhs.x + rhs.x, lhs.y + rhs.y, lhs.z + rhs.z);
        }

        /// <summary>
        /// Adds a Unity <see cref="Vector3Int"/> to a fast vector component-wise.
        /// </summary>
        /// <param name="lhs">The fast vector.</param>
        /// <param name="rhs">The Unity vector.</param>
        /// <returns>The component-wise sum.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int snapped = current + new Vector3Int(0, 1, 0);
        /// </code>
        /// </example>
        public static FastVector3Int operator +(FastVector3Int lhs, Vector3Int rhs)
        {
            return new FastVector3Int(lhs.x + rhs.x, lhs.y + rhs.y, lhs.z + rhs.z);
        }

        /// <summary>
        /// Adds a Unity <see cref="Vector2Int"/> to the planar components of a fast vector.
        /// </summary>
        /// <param name="lhs">The fast vector.</param>
        /// <param name="rhs">The two-dimensional Unity vector.</param>
        /// <returns>The component-wise sum with Z preserved.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int moved = current + new Vector2Int(2, -1);
        /// </code>
        /// </example>
        public static FastVector3Int operator +(FastVector3Int lhs, Vector2Int rhs)
        {
            return new FastVector3Int(lhs.x + rhs.x, lhs.y + rhs.y, lhs.z);
        }

        /// <summary>
        /// Adds a <see cref="FastVector2Int"/> to the planar components of a fast vector.
        /// </summary>
        /// <param name="lhs">The fast vector.</param>
        /// <param name="rhs">The two-dimensional fast vector.</param>
        /// <returns>The component-wise sum with Z preserved.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int offset = current + new FastVector2Int(0, 3);
        /// </code>
        /// </example>
        public static FastVector3Int operator +(FastVector3Int lhs, FastVector2Int rhs)
        {
            return new FastVector3Int(lhs.x + rhs.x, lhs.y + rhs.y, lhs.z);
        }

        /// <summary>
        /// Subtracts one fast vector from another component-wise.
        /// </summary>
        /// <param name="lhs">The minuend.</param>
        /// <param name="rhs">The subtrahend.</param>
        /// <returns>The component-wise difference.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int delta = target - origin;
        /// </code>
        /// </example>
        public static FastVector3Int operator -(FastVector3Int lhs, FastVector3Int rhs)
        {
            return new FastVector3Int(lhs.x - rhs.x, lhs.y - rhs.y, lhs.z - rhs.z);
        }

        /// <summary>
        /// Subtracts a Unity <see cref="Vector3Int"/> from a fast vector component-wise.
        /// </summary>
        /// <param name="lhs">The fast vector.</param>
        /// <param name="rhs">The Unity vector.</param>
        /// <returns>The component-wise difference.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int offset = current - new Vector3Int(0, 1, 0);
        /// </code>
        /// </example>
        public static FastVector3Int operator -(FastVector3Int lhs, Vector3Int rhs)
        {
            return new FastVector3Int(lhs.x - rhs.x, lhs.y - rhs.y, lhs.z - rhs.z);
        }

        /// <summary>
        /// Subtracts a <see cref="FastVector2Int"/> from the planar components of a fast vector.
        /// </summary>
        /// <param name="lhs">The fast vector.</param>
        /// <param name="rhs">The two-dimensional fast vector.</param>
        /// <returns>The component-wise difference with Z preserved.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int planarDelta = current - new FastVector2Int(5, 1);
        /// </code>
        /// </example>
        public static FastVector3Int operator -(FastVector3Int lhs, FastVector2Int rhs)
        {
            return new FastVector3Int(lhs.x - rhs.x, lhs.y - rhs.y, lhs.z);
        }

        /// <summary>
        /// Subtracts a Unity <see cref="Vector2Int"/> from the planar components of a fast vector.
        /// </summary>
        /// <param name="lhs">The fast vector.</param>
        /// <param name="rhs">The two-dimensional Unity vector.</param>
        /// <returns>The component-wise difference with Z preserved.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int adjustment = current - new Vector2Int(3, 0);
        /// </code>
        /// </example>
        public static FastVector3Int operator -(FastVector3Int lhs, Vector2Int rhs)
        {
            return new FastVector3Int(lhs.x - rhs.x, lhs.y - rhs.y, lhs.z);
        }

        /// <summary>
        /// Converts a fast vector into a Unity <see cref="Vector3Int"/>.
        /// </summary>
        /// <param name="vector">The fast vector.</param>
        /// <returns>A Unity vector with identical components.</returns>
        /// <example>
        /// <code>
        /// Vector3Int unityVector = FastVector3Int.zero;
        /// </code>
        /// </example>
        public static implicit operator Vector3Int(FastVector3Int vector)
        {
            return new Vector3Int(vector.x, vector.y, vector.z);
        }

        /// <summary>
        /// Projects this fast 3D vector onto the XY plane as a Unity <see cref="Vector2Int"/> by discarding the Z component.
        /// </summary>
        /// <param name="vector">The fast vector to project.</param>
        /// <returns>A <see cref="Vector2Int"/> containing the X and Y components.</returns>
        /// <example>
        /// <code>
        /// Vector2Int tile = (Vector2Int)new FastVector3Int(5, 9, -2);
        /// </code>
        /// </example>
        public static implicit operator Vector2Int(FastVector3Int vector)
        {
            return new Vector2Int(vector.x, vector.y);
        }

        /// <summary>
        /// Converts a Unity <see cref="Vector3Int"/> to a fast vector while caching its hash.
        /// </summary>
        /// <param name="vector">The Unity vector.</param>
        /// <returns>A fast vector with identical components.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int cached = new Vector3Int(6, 1, 9);
        /// </code>
        /// </example>
        public static implicit operator FastVector3Int(Vector3Int vector)
        {
            return new FastVector3Int(vector.x, vector.y, vector.z);
        }

        /// <summary>
        /// Converts a fast vector into Unity's <see cref="Vector3"/>.
        /// </summary>
        /// <param name="vector">The fast vector.</param>
        /// <returns>A <see cref="Vector3"/> with identical components.</returns>
        /// <example>
        /// <code>
        /// Vector3 worldPoint = FastVector3Int.zero;
        /// </code>
        /// </example>
        public static implicit operator Vector3(FastVector3Int vector)
        {
            return new Vector3(vector.x, vector.y, vector.z);
        }

        /// <summary>
        /// Converts a Unity <see cref="Vector2Int"/> to a fast vector with a zero Z component.
        /// </summary>
        /// <param name="vector">The Unity vector.</param>
        /// <returns>A fast vector containing the planar components.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int elevated = new Vector2Int(1, 2);
        /// </code>
        /// </example>
        public static implicit operator FastVector3Int(Vector2Int vector)
        {
            return new FastVector3Int(vector.x, vector.y, 0);
        }

        /// <summary>
        /// Converts a fast vector into Unity's <see cref="Vector2"/>.
        /// </summary>
        /// <param name="vector">The fast vector.</param>
        /// <returns>A <see cref="Vector2"/> with the X and Y components.</returns>
        /// <example>
        /// <code>
        /// Vector2 planar = FastVector3Int.zero;
        /// </code>
        /// </example>
        public static implicit operator Vector2(FastVector3Int vector)
        {
            return new Vector2(vector.x, vector.y);
        }

        /// <summary>
        /// Returns the cached hash code for the vector so it can participate in hash-based collections without recomputing component hashes.
        /// </summary>
        /// <returns>A deterministic hash based on X, Y, and Z.</returns>
        /// <example>
        /// <code><![CDATA[
        /// HashSet<FastVector3Int> occupied = new HashSet<FastVector3Int>();
        /// FastVector3Int anchor = new FastVector3Int(1, 2, 3);
        /// occupied.Add(anchor);
        /// int hash = anchor.GetHashCode();
        /// bool contains = occupied.Contains(new FastVector3Int(1, 2, 3));
        /// ]]></code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return _hash;
        }

        /// <summary>
        /// Determines equality between this fast vector and a Unity <see cref="Vector2Int"/> by comparing planar components.
        /// </summary>
        /// <param name="other">The Unity vector.</param>
        /// <returns><c>true</c> when X and Y match.</returns>
        /// <example>
        /// <code>
        /// bool shareCell = current.Equals(new Vector2Int(7, 1));
        /// </code>
        /// </example>
        public bool Equals(Vector2Int other)
        {
            return x == other.x && y == other.y;
        }

        /// <summary>
        /// Compares this fast vector to a Unity <see cref="Vector2Int"/> using lexicographical ordering.
        /// </summary>
        /// <param name="other">The Unity vector.</param>
        /// <returns>A signed integer describing the ordering.</returns>
        /// <example>
        /// <code>
        /// bool comesAfter = current.CompareTo(new Vector2Int(5, 0)) &gt; 0;
        /// </code>
        /// </example>
        public int CompareTo(Vector2Int other)
        {
            int comparison = x.CompareTo(other.x);
            if (comparison != 0)
            {
                return comparison;
            }

            return y.CompareTo(other.y);
        }

        /// <summary>
        /// Compares this vector to a Unity <see cref="Vector3Int"/> using lexicographical ordering on X, then Y, then Z.
        /// </summary>
        /// <param name="other">The Unity vector to compare to.</param>
        /// <returns>A signed integer describing the ordering relationship.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int platform = new FastVector3Int(0, 4, 2);
        /// bool isAbove = platform.CompareTo(new Vector3Int(0, 3, 10)) > 0;
        /// </code>
        /// </example>
        public int CompareTo(Vector3Int other)
        {
            int comparison = x.CompareTo(other.x);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = y.CompareTo(other.y);
            if (comparison != 0)
            {
                return comparison;
            }

            return z.CompareTo(other.z);
        }

        /// <summary>
        /// Determines equality against any supported vector representation.
        /// </summary>
        /// <param name="obj">The candidate vector.</param>
        /// <returns><c>true</c> when <paramref name="obj"/> represents the same coordinates.</returns>
        /// <example>
        /// <code>
        /// object candidate = new Vector3Int(4, 2, 6);
        /// bool matches = current.Equals(candidate);
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj)
        {
            return obj switch
            {
                FastVector3Int vector => Equals(vector),
                Vector3Int vector => Equals(vector),
                FastVector2Int vector => Equals(vector),
                Vector2Int vector => Equals(vector),
                _ => false,
            };
        }

        /// <summary>
        /// Determines equality with a Unity <see cref="Vector3Int"/> instance.
        /// </summary>
        /// <param name="other">The Unity vector.</param>
        /// <returns><c>true</c> when all components match.</returns>
        /// <example>
        /// <code>
        /// bool identical = current.Equals(new Vector3Int(1, 2, 3));
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Vector3Int other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        /// <summary>
        /// Determines equality with another fast vector.
        /// </summary>
        /// <param name="other">The other fast vector.</param>
        /// <returns><c>true</c> when all components match.</returns>
        /// <example>
        /// <code>
        /// bool isOrigin = current.Equals(FastVector3Int.zero);
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FastVector3Int other)
        {
            return GetHashCode() == other.GetHashCode()
                && x == other.x
                && y == other.y
                && z == other.z;
        }

        /// <summary>
        /// Determines equality with a planar <see cref="FastVector2Int"/> by comparing X and Y.
        /// </summary>
        /// <param name="other">The two-dimensional fast vector.</param>
        /// <returns><c>true</c> when the planar components match.</returns>
        /// <example>
        /// <code>
        /// bool overlaps = current.Equals(new FastVector2Int(5, 3));
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FastVector2Int other)
        {
            return x == other.x && y == other.y;
        }

        /// <summary>
        /// Compares this fast vector to another fast vector using lexicographical ordering by X, then Y, then Z.
        /// </summary>
        /// <param name="other">The other fast vector.</param>
        /// <returns>A signed integer describing the ordering.</returns>
        /// <example>
        /// <code>
        /// bool isBefore = current.CompareTo(target) &lt; 0;
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(FastVector3Int other)
        {
            int xComparison = x.CompareTo(other.x);
            if (xComparison != 0)
            {
                return xComparison;
            }

            int yComparison = y.CompareTo(other.y);
            if (yComparison != 0)
            {
                return yComparison;
            }

            return z.CompareTo(other.z);
        }

        /// <summary>
        /// Compares this fast vector to a <see cref="FastVector2Int"/> by X then Y.
        /// </summary>
        /// <param name="other">The two-dimensional fast vector.</param>
        /// <returns>A signed integer describing the ordering.</returns>
        /// <example>
        /// <code>
        /// bool precedes = current.CompareTo(new FastVector2Int(1, 4)) &lt; 0;
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(FastVector2Int other)
        {
            int comparison = x.CompareTo(other.x);
            if (comparison != 0)
            {
                return comparison;
            }

            return y.CompareTo(other.y);
        }

        /// <summary>
        /// Compares this fast vector to any supported vector representation.
        /// </summary>
        /// <param name="other">The candidate vector.</param>
        /// <returns>A signed integer describing the ordering, or <c>-1</c> when unsupported.</returns>
        /// <example>
        /// <code>
        /// int ordering = current.CompareTo((object)new Vector3Int(8, 0, 2));
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(object other)
        {
            return other switch
            {
                FastVector3Int vector => CompareTo(vector),
                Vector3Int vector => CompareTo(vector),
                FastVector2Int vector => CompareTo(vector),
                Vector2Int vector => CompareTo(vector),
                _ => -1,
            };
        }

        /// <summary>
        /// Formats the vector as a string tuple containing X, Y, and Z.
        /// </summary>
        /// <returns>A string formatted as <c>(x, y, z)</c>.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int checkpoint = new FastVector3Int(-2, 8, 15);
        /// string label = checkpoint.ToString();
        /// </code>
        /// </example>
        public override string ToString()
        {
            return $"({x}, {y}, {z})";
        }

        /// <summary>
        /// Converts this vector to a <see cref="FastVector2Int"/> by discarding the Z component.
        /// </summary>
        /// <returns>The planar fast vector.</returns>
        /// <example>
        /// <code>
        /// FastVector2Int planar = current.FastVector2Int();
        /// </code>
        /// </example>
        public FastVector2Int FastVector2Int()
        {
            return new FastVector2Int(x, y);
        }

        /// <summary>
        /// Converts this fast vector to a Unity <see cref="Vector2"/>.
        /// </summary>
        /// <returns>The planar <see cref="Vector2"/>.</returns>
        /// <example>
        /// <code>
        /// Vector2 uiPosition = current.AsVector2();
        /// </code>
        /// </example>
        public Vector2 AsVector2()
        {
            return new Vector2(x, y);
        }

        /// <summary>
        /// Converts this fast vector to a Unity <see cref="Vector3"/>.
        /// </summary>
        /// <returns>The <see cref="Vector3"/> with identical components.</returns>
        /// <example>
        /// <code>
        /// Vector3 worldPoint = current.AsVector3();
        /// </code>
        /// </example>
        public Vector3 AsVector3()
        {
            return new Vector3(x, y, z);
        }
    }
}
