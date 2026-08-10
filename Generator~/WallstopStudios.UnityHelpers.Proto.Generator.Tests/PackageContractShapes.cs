// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections;
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    // One type per contract the package annotates for WallstopProto, with the same member numbers,
    // the same declared types and the same flags. The real contracts cannot be compiled here --
    // Unity's reference assemblies do not load under CoreCLR -- so these stand in for them, and
    // PackageContractShapeTests drives each through both serializers.
    //
    // ContractMirrorTests requires one of these per mirrored contract, so a contract cannot be
    // annotated without also stating what its bytes are.

    /// <summary>Stands in for <c>None</c>: a contract with no members at all.</summary>
    [ProtoContract]
    [WProtoContract]
    public readonly partial struct NoneShape { }

    /// <summary>
    /// Stands in for <c>Line2D</c> and <c>Line3D</c>: two readonly members of a type the package
    /// does not own, reached through a surrogate.
    /// </summary>
    /// <remarks>
    /// Readonly plus surrogate is the combination worth pinning: every member is assigned through
    /// the generated constructor, and each one is converted on the way in.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public readonly partial struct LineShape
    {
        /// <summary>The starting point.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public readonly ForeignVector3 from;

        /// <summary>The ending point.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public readonly ForeignVector3 to;

        /// <summary>Constructs a segment.</summary>
        /// <param name="from">The starting point.</param>
        /// <param name="to">The ending point.</param>
        public LineShape(ForeignVector3 from, ForeignVector3 to)
        {
            this.from = from;
            this.to = to;
        }
    }

    /// <summary>
    /// Stands in for <c>Range&lt;T&gt;</c>: a generic contract whose bounds are its own type
    /// parameter and whose inclusivity flags are not.
    /// </summary>
    /// <typeparam name="T">The bound type.</typeparam>
    [ProtoContract]
    [WProtoContract]
    public partial struct RangeShape<T>
    {
        /// <summary>The lower bound.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public T min;

        /// <summary>The upper bound.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public T max;

        /// <summary>Whether the lower bound is part of the range.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public bool startInclusive;

        /// <summary>Whether the upper bound is part of the range.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public bool endInclusive;
    }

    /// <summary>
    /// Stands in for <c>SerializableNullable&lt;T&gt;</c>: a presence flag beside a value that is
    /// explicitly not required.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    [ProtoContract]
    [WProtoContract]
    public partial struct NullableShape<T>
        where T : struct
    {
        /// <summary>Whether the value is present.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public bool hasValue;

        /// <summary>The value, written only when it differs from its default.</summary>
        [ProtoMember(2, IsRequired = false)]
        [WProtoMember(2, IsRequired = false)]
        public T value;
    }

    /// <summary>
    /// Stands in for <c>SerializableType</c>: one string member beside fields neither serializer
    /// writes.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public partial struct TypeNameShape
    {
        /// <summary>The assembly-qualified name.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public string name;

        /// <summary>Resolution state, rebuilt rather than carried.</summary>
        [ProtoIgnore]
        [WProtoIgnore]
        public int cached;
    }

    /// <summary>
    /// Stands in for <c>SerializableList&lt;T&gt;</c>: a generic contract that is itself a list,
    /// backed by one repeated member at field 1.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <remarks>
    /// protobuf-net applies list handling to this shape rather than message handling, so it writes
    /// the elements of the enclosing type. Because the only member sits at field 1 and holds exactly
    /// those elements, the two readings coincide -- which is what makes this contract portable at
    /// all, and is measured here rather than assumed.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ListShape<T> : IList<T>, IReadOnlyList<T>
    {
        [ProtoMember(1, OverwriteList = true)]
        [WProtoMember(1, OverwriteList = true)]
        private List<T> _items = new List<T>();

        /// <summary>Gets or sets the element at <paramref name="index"/>.</summary>
        /// <param name="index">The position.</param>
        public T this[int index]
        {
            get => _items[index];
            set => _items[index] = value;
        }

        /// <summary>Gets the element count.</summary>
        public int Count => _items.Count;

        /// <summary>Gets whether the list rejects mutation.</summary>
        public bool IsReadOnly => false;

        /// <summary>Appends an element.</summary>
        /// <param name="item">The element.</param>
        public void Add(T item) => _items.Add(item);

        /// <summary>Removes every element.</summary>
        public void Clear() => _items.Clear();

        /// <summary>Reports whether an element is present.</summary>
        /// <param name="item">The element.</param>
        public bool Contains(T item) => _items.Contains(item);

        /// <summary>Copies the elements into an array.</summary>
        /// <param name="array">The destination.</param>
        /// <param name="arrayIndex">The first index written.</param>
        public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

        /// <summary>Enumerates the elements.</summary>
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        /// <summary>Reports the position of an element.</summary>
        /// <param name="item">The element.</param>
        public int IndexOf(T item) => _items.IndexOf(item);

        /// <summary>Inserts an element.</summary>
        /// <param name="index">The position.</param>
        /// <param name="item">The element.</param>
        public void Insert(int index, T item) => _items.Insert(index, item);

        /// <summary>Removes the first matching element.</summary>
        /// <param name="item">The element.</param>
        public bool Remove(T item) => _items.Remove(item);

        /// <summary>Removes the element at a position.</summary>
        /// <param name="index">The position.</param>
        public void RemoveAt(int index) => _items.RemoveAt(index);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Stands in for <c>DisjointSet</c>: two arrays and a counter behind a private parameterless
    /// constructor.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class DisjointShape
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        private int[] _parent;

        [ProtoMember(2)]
        [WProtoMember(2)]
        private int[] _rank;

        [ProtoMember(3)]
        [WProtoMember(3)]
        private int _setCount;

        private DisjointShape() { }

        /// <summary>Builds a populated instance.</summary>
        /// <param name="parent">Parent indices.</param>
        /// <param name="rank">Union ranks.</param>
        /// <param name="setCount">The number of distinct sets.</param>
        public DisjointShape(int[] parent, int[] rank, int setCount)
        {
            _parent = parent;
            _rank = rank;
            _setCount = setCount;
        }
    }

    /// <summary>
    /// Stands in for <c>BitSet</c>: a message-shaped contract that also presents as a read-only
    /// list.
    /// </summary>
    /// <remarks>
    /// <c>IReadOnlyList&lt;bool&gt;</c> is not <c>ICollection&lt;T&gt;</c>, so neither serializer
    /// would take the list reading here even without the flag; the flag is carried because the real
    /// contract carries it, and dropping it on one side only is exactly what the mirror gate exists
    /// to catch.
    /// </remarks>
    [ProtoContract(IgnoreListHandling = true)]
    [WProtoContract(IgnoreListHandling = true)]
    public sealed partial class BitShape : IReadOnlyList<bool>
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        private ulong[] _bits;

        [ProtoMember(2)]
        [WProtoMember(2)]
        private int _capacity;

        /// <summary>Builds an empty instance.</summary>
        public BitShape() { }

        /// <summary>Builds a populated instance.</summary>
        /// <param name="bits">The packed bits.</param>
        /// <param name="capacity">The addressable bit count.</param>
        public BitShape(ulong[] bits, int capacity)
        {
            _bits = bits;
            _capacity = capacity;
        }

        /// <summary>Gets the bit at <paramref name="index"/>.</summary>
        /// <param name="index">The bit position.</param>
        public bool this[int index] => (_bits[index >> 6] & (1UL << (index & 63))) != 0;

        /// <summary>Gets the addressable bit count.</summary>
        public int Count => _capacity;

        /// <summary>Enumerates the bits.</summary>
        public IEnumerator<bool> GetEnumerator()
        {
            for (int index = 0; index < _capacity; index++)
            {
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>The action an <see cref="ModificationShape"/> performs.</summary>
    public enum ModificationActionShape
    {
        /// <summary>Unset.</summary>
        None = 0,

        /// <summary>Adds the value.</summary>
        Addition = 1,

        /// <summary>Multiplies by the value.</summary>
        Multiplication = 2,
    }

    /// <summary>
    /// Stands in for <c>AttributeModification</c>: a string, an enum and a float in one struct.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public partial struct ModificationShape
    {
        /// <summary>The attribute name.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public string attribute;

        /// <summary>The operation.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public ModificationActionShape action;

        /// <summary>The operand.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public float value;
    }

    /// <summary>
    /// Stands in for <c>PeriodicEffectDefinition</c>: scalars beside a repeated member whose
    /// element is itself a contract.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class PeriodicShape
    {
        /// <summary>An optional label.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public string name;

        /// <summary>Seconds before the first tick.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public float initialDelay;

        /// <summary>Seconds between ticks.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public float interval = 1f;

        /// <summary>The tick ceiling, or zero for unlimited.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public int maxTicks;

        /// <summary>What each tick applies.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public List<ModificationShape> modifications = new List<ModificationShape>();
    }

    /// <summary>
    /// Stands in for <c>SerializableDictionary</c>, whose only role here is to be the non-generic
    /// type a generic contract is nested inside.
    /// </summary>
    public static partial class CacheHolderShape
    {
        /// <summary>
        /// Stands in for <c>SerializableDictionary.Cache&lt;T&gt;</c>: a generic contract nested in
        /// a non-generic type, with one member typed as its own parameter.
        /// </summary>
        /// <typeparam name="T">The cached type.</typeparam>
        [ProtoContract]
        [WProtoContract]
        public partial class CacheShape<T>
        {
            /// <summary>The cached value.</summary>
            [ProtoMember(1)]
            [WProtoMember(1)]
            public T Data;
        }
    }
}
