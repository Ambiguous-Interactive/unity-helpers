// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.DataStructure.Adapters
{
    using System;
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Unity-serializable stand-in for <see cref="ValueTuple{T1, T2}"/>.
    /// </summary>
    /// <remarks>
    /// Unity's serializer declines every type out of the framework assemblies, whatever they are
    /// marked -- and it declines silently, producing no <c>SerializedProperty</c> at all rather than
    /// an empty one. So a <c>(int, float)</c> in a <see cref="SerializableDictionary{TKey, TValue}"/>
    /// loses its authored contents with nothing to report it. This type is the same two fields under
    /// a name Unity will serialize, with implicit conversions so <c>(T1, T2)</c> stays the spelling
    /// everywhere else.
    ///
    /// The field names and numbers are <see cref="ValueTuple{T1, T2}"/>'s, so protobuf and JSON
    /// payloads are interchangeable with it in both directions.
    /// </remarks>
    /// <typeparam name="T1">The first component's type.</typeparam>
    /// <typeparam name="T2">The second component's type.</typeparam>
    /// <example>
    /// <code><![CDATA[
    /// [SerializeField]
    /// private SerializableDictionary<string, SerializableValueTuple<int, float>> _loot = new();
    ///
    /// public void Grant(string id)
    /// {
    ///     (int count, float weight) = _loot[id];
    /// }
    /// ]]></code>
    /// </example>
    [Serializable]
    [ProtoContract]
    [WProtoContract]
    public partial struct SerializableValueTuple<T1, T2>
        : IEquatable<SerializableValueTuple<T1, T2>>,
            IEquatable<ValueTuple<T1, T2>>
    {
        /// <summary>The first component.</summary>
        [ProtoMember(1, IsRequired = true)]
        [WProtoMember(1, IsRequired = true)]
        public T1 Item1;

        /// <summary>The second component.</summary>
        [ProtoMember(2, IsRequired = true)]
        [WProtoMember(2, IsRequired = true)]
        public T2 Item2;

        /// <summary>Initializes a new instance holding both components.</summary>
        /// <param name="item1">The first component.</param>
        /// <param name="item2">The second component.</param>
        public SerializableValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        /// <summary>Converts from the framework tuple.</summary>
        /// <param name="value">The tuple to copy.</param>
        public static implicit operator SerializableValueTuple<T1, T2>(ValueTuple<T1, T2> value)
        {
            return new SerializableValueTuple<T1, T2>(value.Item1, value.Item2);
        }

        /// <summary>Converts to the framework tuple.</summary>
        /// <param name="value">The value to copy.</param>
        public static implicit operator ValueTuple<T1, T2>(SerializableValueTuple<T1, T2> value)
        {
            return new ValueTuple<T1, T2>(value.Item1, value.Item2);
        }

        /// <summary>Reports whether both components are equal.</summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        public static bool operator ==(
            SerializableValueTuple<T1, T2> left,
            SerializableValueTuple<T1, T2> right
        )
        {
            return left.Equals(right);
        }

        /// <summary>Reports whether either component differs.</summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        public static bool operator !=(
            SerializableValueTuple<T1, T2> left,
            SerializableValueTuple<T1, T2> right
        )
        {
            return !left.Equals(right);
        }

        /// <summary>Copies both components out.</summary>
        /// <param name="item1">Receives the first component.</param>
        /// <param name="item2">Receives the second component.</param>
        public void Deconstruct(out T1 item1, out T2 item2)
        {
            item1 = Item1;
            item2 = Item2;
        }

        /// <inheritdoc/>
        public bool Equals(SerializableValueTuple<T1, T2> other)
        {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                && EqualityComparer<T2>.Default.Equals(Item2, other.Item2);
        }

        /// <inheritdoc/>
        public bool Equals(ValueTuple<T1, T2> other)
        {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                && EqualityComparer<T2>.Default.Equals(Item2, other.Item2);
        }

        /// <summary>
        /// Determines whether this tuple equals another boxed
        /// <see cref="SerializableValueTuple{T1, T2}"/>. A boxed <see cref="ValueTuple{T1, T2}"/> is
        /// not accepted: it answers <c>false</c> for this type in return and hashes differently.
        /// Compare against one through <see cref="Equals(ValueTuple{T1, T2})"/>.
        /// </summary>
        /// <param name="obj">The candidate value.</param>
        /// <returns><c>true</c> when <paramref name="obj"/> is a serializable tuple with equal components.</returns>
        public override bool Equals(object obj)
        {
            return obj is SerializableValueTuple<T1, T2> serializable && Equals(serializable);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Objects.HashCode(Item1, Item2);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return "(" + Item1 + ", " + Item2 + ")";
        }
    }

    /// <summary>
    /// Unity-serializable stand-in for <see cref="ValueTuple{T1, T2, T3}"/>.
    /// </summary>
    /// <remarks>
    /// The three-component form of <see cref="SerializableValueTuple{T1, T2}"/>, with the same
    /// field names and numbers as <see cref="ValueTuple{T1, T2, T3}"/>.
    /// </remarks>
    /// <typeparam name="T1">The first component's type.</typeparam>
    /// <typeparam name="T2">The second component's type.</typeparam>
    /// <typeparam name="T3">The third component's type.</typeparam>
    [Serializable]
    [ProtoContract]
    [WProtoContract]
    public partial struct SerializableValueTuple<T1, T2, T3>
        : IEquatable<SerializableValueTuple<T1, T2, T3>>,
            IEquatable<ValueTuple<T1, T2, T3>>
    {
        /// <summary>The first component.</summary>
        [ProtoMember(1, IsRequired = true)]
        [WProtoMember(1, IsRequired = true)]
        public T1 Item1;

        /// <summary>The second component.</summary>
        [ProtoMember(2, IsRequired = true)]
        [WProtoMember(2, IsRequired = true)]
        public T2 Item2;

        /// <summary>The third component.</summary>
        [ProtoMember(3, IsRequired = true)]
        [WProtoMember(3, IsRequired = true)]
        public T3 Item3;

        /// <summary>Initializes a new instance holding all three components.</summary>
        /// <param name="item1">The first component.</param>
        /// <param name="item2">The second component.</param>
        /// <param name="item3">The third component.</param>
        public SerializableValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }

        /// <summary>Converts from the framework tuple.</summary>
        /// <param name="value">The tuple to copy.</param>
        public static implicit operator SerializableValueTuple<T1, T2, T3>(
            ValueTuple<T1, T2, T3> value
        )
        {
            return new SerializableValueTuple<T1, T2, T3>(value.Item1, value.Item2, value.Item3);
        }

        /// <summary>Converts to the framework tuple.</summary>
        /// <param name="value">The value to copy.</param>
        public static implicit operator ValueTuple<T1, T2, T3>(
            SerializableValueTuple<T1, T2, T3> value
        )
        {
            return new ValueTuple<T1, T2, T3>(value.Item1, value.Item2, value.Item3);
        }

        /// <summary>Reports whether every component is equal.</summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        public static bool operator ==(
            SerializableValueTuple<T1, T2, T3> left,
            SerializableValueTuple<T1, T2, T3> right
        )
        {
            return left.Equals(right);
        }

        /// <summary>Reports whether any component differs.</summary>
        /// <param name="left">The first value.</param>
        /// <param name="right">The second value.</param>
        public static bool operator !=(
            SerializableValueTuple<T1, T2, T3> left,
            SerializableValueTuple<T1, T2, T3> right
        )
        {
            return !left.Equals(right);
        }

        /// <summary>Copies every component out.</summary>
        /// <param name="item1">Receives the first component.</param>
        /// <param name="item2">Receives the second component.</param>
        /// <param name="item3">Receives the third component.</param>
        public void Deconstruct(out T1 item1, out T2 item2, out T3 item3)
        {
            item1 = Item1;
            item2 = Item2;
            item3 = Item3;
        }

        /// <inheritdoc/>
        public bool Equals(SerializableValueTuple<T1, T2, T3> other)
        {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                && EqualityComparer<T2>.Default.Equals(Item2, other.Item2)
                && EqualityComparer<T3>.Default.Equals(Item3, other.Item3);
        }

        /// <inheritdoc/>
        public bool Equals(ValueTuple<T1, T2, T3> other)
        {
            return EqualityComparer<T1>.Default.Equals(Item1, other.Item1)
                && EqualityComparer<T2>.Default.Equals(Item2, other.Item2)
                && EqualityComparer<T3>.Default.Equals(Item3, other.Item3);
        }

        /// <summary>
        /// Determines whether this tuple equals another boxed
        /// <see cref="SerializableValueTuple{T1, T2, T3}"/>. A boxed
        /// <see cref="ValueTuple{T1, T2, T3}"/> is not accepted: it answers <c>false</c> for this
        /// type in return and hashes differently. Compare against one through
        /// <see cref="Equals(ValueTuple{T1, T2, T3})"/>.
        /// </summary>
        /// <param name="obj">The candidate value.</param>
        /// <returns><c>true</c> when <paramref name="obj"/> is a serializable tuple with equal components.</returns>
        public override bool Equals(object obj)
        {
            return obj is SerializableValueTuple<T1, T2, T3> serializable && Equals(serializable);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return Objects.HashCode(Item1, Item2, Item3);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return "(" + Item1 + ", " + Item2 + ", " + Item3 + ")";
        }
    }
}
