// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The values a repeated element can take, so the enum path is exercised too.
    /// </summary>
    public enum WProtoRepeatedMode
    {
        /// <summary>The default. Omitted from a scalar member, but written as an element.</summary>
        None = 0,

        /// <summary>A single-byte varint.</summary>
        Fast = 1,

        /// <summary>A two-byte varint, so a wide element is covered.</summary>
        Careful = 300,
    }

    /// <summary>A struct sub-message, used as a repeated element.</summary>
    [WProtoContract]
    public partial struct WProtoRepeatedPoint
    {
        /// <summary>The first component.</summary>
        [WProtoMember(1)]
        public int X;

        /// <summary>The second component.</summary>
        [WProtoMember(2)]
        public int Y;
    }

    /// <summary>A message with no members, so each element is a key and a zero length.</summary>
    [WProtoContract]
    public sealed partial class WProtoRepeatedEmpty { }

    /// <summary>Every repeated element shape the generator emits.</summary>
    /// <remarks>
    /// The field numbers match the contract the differential suite under <c>Generator~/</c> compares
    /// against protobuf-net, so the hex in <c>WProtoRepeatedContractTests</c> is the oracle's own
    /// output rather than this session's reading of the specification. That indirection is the
    /// point: protobuf-net cannot run under IL2CPP, and the standalone legs are the only place the
    /// generated repeated code is AOT-compiled at all.
    /// </remarks>
    [WProtoContract]
    public sealed partial class WProtoRepeatedContract
    {
        /// <summary>A varint element.</summary>
        [WProtoMember(1)]
        public int[] Ints;

        /// <summary>The same element through a list rather than an array.</summary>
        [WProtoMember(2)]
        public List<int> IntList;

        /// <summary>A length-delimited element that can be empty without being null.</summary>
        [WProtoMember(3)]
        public string[] Texts;

        /// <summary>A fixed64 element.</summary>
        [WProtoMember(4)]
        public double[] Doubles;

        /// <summary>An unsigned varint element.</summary>
        [WProtoMember(5)]
        public ulong[] Longs;

        /// <summary>A one-byte varint element.</summary>
        [WProtoMember(6)]
        public bool[] Flags;

        /// <summary>An enum element, which encodes as its underlying varint.</summary>
        [WProtoMember(7)]
        public WProtoRepeatedMode[] Modes;

        /// <summary>A struct sub-message element, written even when every member is default.</summary>
        [WProtoMember(8)]
        public WProtoRepeatedPoint[] Points;

        /// <summary>A reference sub-message element.</summary>
        [WProtoMember(9)]
        public WProtoRepeatedEmpty[] Messages;

        /// <summary>
        /// The one array that is a scalar, repeated. It looks jagged and is not: a <c>byte[]</c> is
        /// a single length-delimited value, so this is an ordinary repeated member and gets none of
        /// the wrapper-message encoding a real nested collection does -- which
        /// <c>WProtoNestedCollectionTests</c> pins by asserting these exact bytes.
        /// </summary>
        [WProtoMember(10)]
        public byte[][] Blobs;

        /// <summary>A sub-message element through a list rather than an array.</summary>
        [WProtoMember(11)]
        public List<WProtoRepeatedPoint> PointList;

        /// <summary>A narrow signed element, which widens to a ten-byte varint when negative.</summary>
        [WProtoMember(12)]
        public short[] Shorts;
    }

    /// <summary>
    /// Collections the constructor has already filled, which is the only way append and overwrite
    /// can be told apart.
    /// </summary>
    [WProtoContract]
    public sealed partial class WProtoSeededRepeatedContract
    {
        /// <summary>Reading appends to this one.</summary>
        [WProtoMember(1)]
        public List<int> AppendedList = new List<int> { 7, 8 };

        /// <summary>Reading replaces this one.</summary>
        [WProtoMember(2, OverwriteList = true)]
        public List<int> OverwrittenList = new List<int> { 7, 8 };

        /// <summary>The same, through an array.</summary>
        [WProtoMember(3)]
        public int[] AppendedArray = { 7, 8 };

        /// <summary>The same, through an array that is replaced.</summary>
        [WProtoMember(4, OverwriteList = true)]
        public int[] OverwrittenArray = { 7, 8 };

        /// <summary>A scalar, so a payload can be non-empty without touching a collection.</summary>
        [WProtoMember(5)]
        public int Marker;
    }

    /// <summary>
    /// A collection implemented as a <b>struct</b>, which is a shape the generator must not assume
    /// away.
    /// </summary>
    /// <remarks>
    /// Nothing about <see cref="ICollection{T}"/> requires a class, and an inline or pooled buffer
    /// is a natural struct -- but a formatter that null-checks every collection does not merely emit
    /// redundant code for one, it emits code that will not compile. The backing store is created
    /// lazily so <c>default(WProtoIntBag)</c> is a legal empty value, which is what makes the copy
    /// semantics observable: the read loop accumulates into a copy and has to assign it back.
    /// </remarks>
    public struct WProtoIntBag : ICollection<int>
    {
        private static readonly List<int> Empty = new();

        private List<int> _items;

        /// <inheritdoc />
        public int Count => _items?.Count ?? 0;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public void Add(int item)
        {
            _items ??= new List<int>();
            _items.Add(item);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _items = null;
        }

        /// <inheritdoc />
        public bool Contains(int item)
        {
            return _items != null && _items.Contains(item);
        }

        /// <inheritdoc />
        public void CopyTo(int[] array, int arrayIndex)
        {
            _items?.CopyTo(array, arrayIndex);
        }

        /// <inheritdoc />
        public bool Remove(int item)
        {
            return _items != null && _items.Remove(item);
        }

        /// <summary>
        /// Returns a non-boxing enumerator, which is what <c>foreach</c> in generated code binds to.
        /// </summary>
        /// <returns>The enumerator.</returns>
        public List<int>.Enumerator GetEnumerator()
        {
            return (_items ?? Empty).GetEnumerator();
        }

        IEnumerator<int> IEnumerable<int>.GetEnumerator()
        {
            return GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }

    /// <summary>The collection shapes beyond array and <c>List&lt;T&gt;</c>.</summary>
    [WProtoContract]
    public sealed partial class WProtoCollectionShapesContract
    {
        /// <summary>A set.</summary>
        [WProtoMember(1)]
        public HashSet<int> Set;

        /// <summary>An ordered set of a length-delimited element.</summary>
        [WProtoMember(2)]
        public SortedSet<string> Sorted;

        /// <summary>A collection that is neither a list nor a set.</summary>
        [WProtoMember(3)]
        public System.Collections.ObjectModel.Collection<int> Owned;

        /// <summary>A collection that is a value type.</summary>
        [WProtoMember(4)]
        public WProtoIntBag Bag;
    }

    /// <summary>A struct contract carrying a repeated member.</summary>
    [WProtoContract]
    public partial struct WProtoRepeatedStructContract
    {
        /// <summary>The repeated member.</summary>
        [WProtoMember(1)]
        public int[] Ints;

        /// <summary>A scalar after it, to pin ascending field order.</summary>
        [WProtoMember(2)]
        public int Marker;
    }
}
