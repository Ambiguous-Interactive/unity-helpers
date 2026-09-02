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
    /// Lightweight alternative to Unity's <see cref="Vector2Int"/> that caches its hash for efficient dictionary and set usage.
    /// Provides implicit conversions to and from Unity's struct so gameplay code can continue to use the familiar API while
    /// obtaining stable, allocation-free dictionary keys.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// FastVector2Int gridCell = new FastVector2Int(3, 5);
    /// Dictionary<FastVector2Int, string> labels = new Dictionary<FastVector2Int, string>();
    /// labels[gridCell] = "SpawnPoint";
    /// bool hasSpawn = labels.ContainsKey(new FastVector2Int(3, 5));
    /// Vector2Int unityVector = gridCell;
    /// ]]></code>
    /// </example>
    [Serializable]
#pragma warning disable WPROTO030 // Served by a hand-written formatter and protobuf-net surrogate.
    [ProtoContract]
#pragma warning restore WPROTO030
    public readonly partial struct FastVector2Int
        : IEquatable<FastVector2Int>,
            IEquatable<FastVector3Int>,
            IEquatable<Vector2Int>,
            IEquatable<Vector3Int>,
            IComparable<FastVector2Int>,
            IComparable<FastVector3Int>,
            IComparable<Vector2Int>,
            IComparable<Vector3Int>,
            IComparable,
            IUnderlyingValueProvider
    {
        /// <summary>
        /// Represents the origin vector <c>(0, 0)</c>, useful as a default value without reallocation.
        /// </summary>
        /// <example>
        /// <code>
        /// FastVector2Int startCell = FastVector2Int.zero;
        /// </code>
        /// </example>
        public static readonly FastVector2Int zero = new(0, 0);

        [ProtoMember(1)]
        [JsonIgnore]
        public readonly int x;

        [ProtoMember(2)]
        [JsonIgnore]
        public readonly int y;

        /*
            Derived from x and y, so it is not serialized. Field 3 used to carry it and, being a
            well-distributed 32-bit value, spent six of the ten bytes an ordinary cell encodes to.
        */
        private readonly int _hash;

        /*
            A zero-initialized instance -- default(FastVector2Int), an array element, a struct a
            deserializer allocated without running a constructor -- carries _hash == 0 while its
            components are already the origin's. Mapping 0 onto the origin's hash is what makes such
            an instance indistinguishable from new FastVector2Int(0, 0), which is what its components
            claim it is. A non-origin cell whose hash happens to be 0 merely collides with the origin,
            and Equals settles that the same way it settles every other collision.

            The obvious alternative -- store the hash XOR'd with this constant, so 0 already means the
            origin and no branch is needed -- is no longer blocked by the wire, but is still rejected:
            static field initializers run in declaration order, so zero above would be built while
            OriginHash is still 0, and would then be read back XOR'd against the real value. One
            compare needs no such ordering to stay correct.
        */
        private static readonly int OriginHash = Objects.HashCode(0, 0);

        /// <summary>
        /// Initializes a new fast vector with integer components and a cached hash.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        /// <example>
        /// <code>
        /// FastVector2Int waypoint = new FastVector2Int(12, -3);
        /// </code>
        /// </example>
        [JsonConstructor]
        public FastVector2Int(int x, int y)
        {
            this.x = x;
            this.y = y;
            _hash = Objects.HashCode(x, y);
        }

        /// <summary>
        /// Gets the stored X component.
        /// </summary>
        /// <example>
        /// <code>
        /// int column = waypoint.X;
        /// </code>
        /// </example>
        [JsonPropertyName("x")]
        public int X => x;

        /// <summary>
        /// Gets the stored Y component.
        /// </summary>
        /// <example>
        /// <code>
        /// int row = waypoint.Y;
        /// </code>
        /// </example>
        [JsonPropertyName("y")]
        public int Y => y;

        /// <summary>
        /// Determines whether two fast vectors are equal by comparing their components.
        /// </summary>
        /// <param name="lhs">The left-hand vector.</param>
        /// <param name="rhs">The right-hand vector.</param>
        /// <returns><c>true</c> when both vectors have matching components.</returns>
        /// <example>
        /// <code>
        /// bool sameCell = FastVector2Int.zero == new FastVector2Int(0, 0);
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FastVector2Int lhs, FastVector2Int rhs)
        {
            return lhs.Equals(rhs);
        }

        /// <summary>
        /// Determines whether two fast vectors differ in any component.
        /// </summary>
        /// <param name="lhs">The left-hand vector.</param>
        /// <param name="rhs">The right-hand vector.</param>
        /// <returns><c>true</c> when the vectors are not equal.</returns>
        /// <example>
        /// <code>
        /// bool hasMoved = currentCell != previousCell;
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FastVector2Int lhs, FastVector2Int rhs)
        {
            return !lhs.Equals(rhs);
        }

        /// <summary>
        /// Converts a fast vector into Unity's <see cref="Vector2Int"/>.
        /// </summary>
        /// <param name="vector">The fast vector to convert.</param>
        /// <returns>A Unity vector with identical components.</returns>
        /// <example>
        /// <code>
        /// Vector2Int unityVector = FastVector2Int.zero;
        /// </code>
        /// </example>
        public static implicit operator Vector2Int(FastVector2Int vector)
        {
            return new Vector2Int(vector.x, vector.y);
        }

        /// <summary>
        /// Converts a <see cref="FastVector3Int"/> into a <see cref="FastVector2Int"/> by discarding the Z component.
        /// </summary>
        /// <param name="vector">The three-dimensional fast vector.</param>
        /// <returns>A two-dimensional fast vector containing the X and Y components.</returns>
        /// <example>
        /// <code>
        /// FastVector2Int planar = new FastVector3Int(2, 7, 4);
        /// </code>
        /// </example>
        public static implicit operator FastVector2Int(FastVector3Int vector)
        {
            return new FastVector2Int(vector.x, vector.y);
        }

        /// <summary>
        /// Converts a Unity <see cref="Vector2Int"/> to a fast vector while caching its hash code.
        /// </summary>
        /// <param name="vector">The Unity vector to convert.</param>
        /// <returns>A new fast vector with identical components.</returns>
        /// <example>
        /// <code>
        /// FastVector2Int cached = new Vector2Int(5, 9);
        /// </code>
        /// </example>
        public static implicit operator FastVector2Int(Vector2Int vector)
        {
            return new FastVector2Int(vector.x, vector.y);
        }

        /// <summary>
        /// Adds two fast vectors component-wise.
        /// </summary>
        /// <param name="lhs">The first summand.</param>
        /// <param name="rhs">The second summand.</param>
        /// <returns>The component-wise sum.</returns>
        /// <example>
        /// <code>
        /// FastVector2Int destination = origin + new FastVector2Int(1, 0);
        /// </code>
        /// </example>
        public static FastVector2Int operator +(FastVector2Int lhs, FastVector2Int rhs)
        {
            return new FastVector2Int(lhs.x + rhs.x, lhs.y + rhs.y);
        }

        /// <summary>
        /// Subtracts one fast vector from another component-wise.
        /// </summary>
        /// <param name="lhs">The minuend.</param>
        /// <param name="rhs">The subtrahend.</param>
        /// <returns>The component-wise difference.</returns>
        /// <example>
        /// <code>
        /// FastVector2Int offset = target - origin;
        /// </code>
        /// </example>
        public static FastVector2Int operator -(FastVector2Int lhs, FastVector2Int rhs)
        {
            return new FastVector2Int(lhs.x - rhs.x, lhs.y - rhs.y);
        }

        /// <summary>
        /// Determines whether this fast vector equals another fast vector instance.
        /// </summary>
        /// <param name="other">The other fast vector.</param>
        /// <returns><c>true</c> when both vectors have identical components.</returns>
        /// <example>
        /// <code>
        /// bool isOrigin = position.Equals(FastVector2Int.zero);
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FastVector2Int other)
        {
            return x == other.x && y == other.y;
        }

        /// <summary>
        /// Determines whether this fast vector equals a Unity <see cref="Vector2Int"/>.
        /// </summary>
        /// <param name="other">The Unity vector.</param>
        /// <returns><c>true</c> when the X and Y components match.</returns>
        /// <example>
        /// <code>
        /// bool matches = position.Equals(new Vector2Int(8, 3));
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(Vector2Int other)
        {
            return x == other.x && y == other.y;
        }

        /// <summary>
        /// Compares this fast vector to another fast vector using lexicographical ordering.
        /// </summary>
        /// <param name="other">The other fast vector.</param>
        /// <returns>A signed integer describing the ordering.</returns>
        /// <example>
        /// <code>
        /// bool isBefore = current.CompareTo(target) &lt; 0;
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(FastVector2Int other)
        {
            int comparison = x.CompareTo(other.x);
            return comparison != 0 ? comparison : y.CompareTo(other.y);
        }

        /// <summary>
        /// Compares this fast vector to a Unity <see cref="Vector2Int"/> using lexicographical ordering.
        /// </summary>
        /// <param name="other">The Unity vector.</param>
        /// <returns>A signed integer describing the ordering.</returns>
        /// <example>
        /// <code>
        /// bool comesAfter = current.CompareTo(new Vector2Int(1, 2)) &gt; 0;
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(Vector2Int other)
        {
            int comparison = x.CompareTo(other.x);
            return comparison != 0 ? comparison : y.CompareTo(other.y);
        }

        /// <summary>
        /// Determines whether this fast vector equals a <see cref="FastVector3Int"/>, ignoring the Z component.
        /// </summary>
        /// <param name="other">The other fast vector.</param>
        /// <returns><c>true</c> when the X and Y components match.</returns>
        /// <example>
        /// <code>
        /// bool overlaps = position.HasSameXY(new FastVector3Int(4, 2, 9));
        /// </code>
        /// </example>
        [Obsolete(
            "Equals across dimensions ignores Z, which breaks transitivity and equal-implies-same-hash. Use HasSameXY(FastVector3Int) instead. This overload is removed in 4.0."
        )]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(FastVector3Int other)
        {
            return HasSameXY(other);
        }

        /// <summary>
        /// Determines whether this fast vector shares the planar coordinates of a
        /// <see cref="FastVector3Int"/>. The Z component is deliberately ignored, which is why this
        /// is not spelled <c>Equals</c>.
        /// </summary>
        /// <param name="other">The other fast vector.</param>
        /// <returns><c>true</c> when the X and Y components match.</returns>
        /// <example>
        /// <code>
        /// bool overlaps = position.HasSameXY(new FastVector3Int(4, 2, 9));
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSameXY(FastVector3Int other)
        {
            return x == other.x && y == other.y;
        }

        /// <summary>
        /// Compares this fast vector to a <see cref="FastVector3Int"/> by X then Y, ignoring Z.
        /// </summary>
        /// <param name="other">The three-dimensional fast vector.</param>
        /// <returns>A signed integer describing the ordering.</returns>
        /// <example>
        /// <code>
        /// bool precedes = current.CompareTo(other.FastVector2Int()) &lt; 0;
        /// </code>
        /// </example>
        [Obsolete(
            "Comparing across dimensions ignores Z, so it answers 0 for a pair Equals(object) refuses. Compare the planar coordinates explicitly with CompareTo(other.FastVector2Int()) instead. This overload is removed in 4.0."
        )]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(FastVector3Int other)
        {
            int comparison = x.CompareTo(other.x);
            return comparison != 0 ? comparison : y.CompareTo(other.y);
        }

        /// <summary>
        /// Determines whether this fast vector equals a Unity <see cref="Vector3Int"/> without considering its Z component.
        /// </summary>
        /// <param name="other">The Unity vector.</param>
        /// <returns><c>true</c> when the planar components match.</returns>
        /// <example>
        /// <code>
        /// bool sharesCell = position.HasSameXY(new Vector3Int(4, 2, 6));
        /// </code>
        /// </example>
        [Obsolete(
            "Equals across dimensions ignores Z, which breaks transitivity and equal-implies-same-hash. Use HasSameXY(Vector3Int) instead. This overload is removed in 4.0."
        )]
        public bool Equals(Vector3Int other)
        {
            return HasSameXY(other);
        }

        /// <summary>
        /// Determines whether this fast vector shares the planar coordinates of a Unity
        /// <see cref="Vector3Int"/>. The Z component is deliberately ignored, which is why this is
        /// not spelled <c>Equals</c>.
        /// </summary>
        /// <param name="other">The Unity vector.</param>
        /// <returns><c>true</c> when the planar components match.</returns>
        /// <example>
        /// <code>
        /// bool sharesCell = position.HasSameXY(new Vector3Int(4, 2, 6));
        /// </code>
        /// </example>
        public bool HasSameXY(Vector3Int other)
        {
            return x == other.x && y == other.y;
        }

        /// <summary>
        /// Compares this fast vector to a Unity <see cref="Vector3Int"/> by X then Y components.
        /// </summary>
        /// <param name="other">The Unity vector.</param>
        /// <returns>A signed integer describing the ordering.</returns>
        /// <example>
        /// <code>
        /// bool isLower = current.CompareTo(new Vector2Int(2, 3)) &lt; 0;
        /// </code>
        /// </example>
        [Obsolete(
            "Comparing across dimensions ignores Z, so it answers 0 for a pair Equals(object) refuses. Compare the planar coordinates explicitly with CompareTo(new Vector2Int(other.x, other.y)) instead. This overload is removed in 4.0."
        )]
        public int CompareTo(Vector3Int other)
        {
            int comparison = x.CompareTo(other.x);
            if (comparison != 0)
            {
                return comparison;
            }
            return y.CompareTo(other.y);
        }

        /// <summary>
        /// Determines equality against another boxed <see cref="FastVector2Int"/>. No other type is
        /// accepted: none of them answers <c>true</c> for a boxed fast vector in return, and a
        /// three-dimensional vector matched on X and Y alone would hash differently.
        /// Compare against Unity's vector through <see cref="Equals(Vector2Int)"/>, and against a
        /// three-dimensional vector through <see cref="HasSameXY(Vector3Int)"/>.
        /// </summary>
        /// <param name="obj">The candidate vector.</param>
        /// <returns><c>true</c> when <paramref name="obj"/> is a fast vector with the same coordinates.</returns>
        /// <example>
        /// <code>
        /// object candidate = new FastVector2Int(2, 1);
        /// bool matches = position.Equals(candidate);
        /// </code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj)
        {
            return obj is FastVector2Int vector && Equals(vector);
        }

        bool IUnderlyingValueProvider.TryGetUnderlyingValue(out object value)
        {
            value = (Vector2Int)this;
            return true;
        }

        /// <summary>
        /// Returns the cached hash code so this vector can be used as a deterministic key in dictionaries and sets.
        /// </summary>
        /// <returns>An integer hash that combines the X and Y components.</returns>
        /// <example>
        /// <code><![CDATA[
        /// HashSet<FastVector2Int> visitedCells = new HashSet<FastVector2Int>();
        /// FastVector2Int cell = new FastVector2Int(2, 4);
        /// visitedCells.Add(cell);
        /// int hash = cell.GetHashCode();
        /// bool contains = visitedCells.Contains(new FastVector2Int(2, 4));
        /// ]]></code>
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return _hash != 0 ? _hash : OriginHash;
        }

        /// <summary>
        /// Formats the vector as a readable string containing its X and Y components.
        /// </summary>
        /// <returns>A string in the form <c>(x, y)</c>.</returns>
        /// <example>
        /// <code><![CDATA[
        /// FastVector2Int cursor = new FastVector2Int(7, -1);
        /// string label = cursor.ToString();
        /// Debug.Log(label);
        /// ]]></code>
        /// </example>
        public override string ToString()
        {
            return $"({x}, {y})";
        }

        /// <summary>
        /// Compares this fast vector to another boxed <see cref="FastVector2Int"/>. No other type is
        /// accepted, for the same reason <see cref="Equals(object)"/> accepts none: a
        /// <c>CompareTo</c> that answers <c>0</c> where <c>Equals</c> answers <c>false</c> breaks
        /// the ordering <c>Array.Sort(object[])</c> and every sorted collection assume. Compare
        /// against Unity's vector through <see cref="CompareTo(Vector2Int)"/>.
        /// </summary>
        /// <param name="obj">The candidate vector.</param>
        /// <returns>A signed integer describing the ordering, <c>1</c> for <c>null</c>, or <c>-1</c> when the type is unsupported.</returns>
        /// <example>
        /// <code>
        /// int ordering = position.CompareTo((object)new FastVector2Int(4, 2));
        /// </code>
        /// </example>
        public int CompareTo(object obj)
        {
            return obj switch
            {
                FastVector2Int vector => CompareTo(vector),
                null => 1,
                _ => -1,
            };
        }

        /// <summary>
        /// Creates a <see cref="FastVector3Int"/> from this vector with a zero Z component.
        /// </summary>
        /// <returns>The extended fast vector.</returns>
        /// <example>
        /// <code>
        /// FastVector3Int elevated = position.AsFastVector3Int();
        /// </code>
        /// </example>
        public FastVector3Int AsFastVector3Int()
        {
            return new FastVector3Int(x, y);
        }

        /// <summary>
        /// Converts this fast vector to a Unity <see cref="Vector2"/>.
        /// </summary>
        /// <returns>A <see cref="Vector2"/> with the same components.</returns>
        /// <example>
        /// <code>
        /// Vector2 uiPosition = position.AsVector2();
        /// </code>
        /// </example>
        public Vector2 AsVector2()
        {
            return new Vector2(x, y);
        }

        /// <summary>
        /// Converts this fast vector to a Unity <see cref="Vector3"/> with a zero Z component.
        /// </summary>
        /// <returns>A <see cref="Vector3"/> that represents the same planar location.</returns>
        /// <example>
        /// <code>
        /// Vector3 worldPoint = position.AsVector3();
        /// </code>
        /// </example>
        public Vector3 AsVector3()
        {
            return new Vector3(x, y);
        }
    }
}
