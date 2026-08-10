// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Every member shape the generator claims to support, in one contract.</summary>
    [WProtoContract]
    public sealed partial class ScalarContract
    {
        [WProtoMember(1)]
        public int Int32;

        [WProtoMember(2)]
        public long Int64;

        [WProtoMember(3)]
        public uint UInt32;

        [WProtoMember(4)]
        public ulong UInt64;

        [WProtoMember(5)]
        public bool Flag;

        [WProtoMember(6)]
        public float Single;

        [WProtoMember(7)]
        public double Double;

        [WProtoMember(8)]
        public string Text;

        [WProtoMember(9)]
        public byte[] Bytes;

        [WProtoMember(10)]
        public Mode Enum;

        [WProtoMember(11)]
        public double? MaybeDouble;

        [WProtoMember(12)]
        public short Int16;

        // A private field reached only because the formatter is emitted nested inside this type.
        [WProtoMember(13)]
        private int _hidden;

        /// <summary>Exposes the private member so a test can set and read it.</summary>
        public int Hidden
        {
            get => _hidden;
            set => _hidden = value;
        }

        /// <summary>A property, to prove members are not restricted to fields.</summary>
        [WProtoMember(14)]
        public int Counted { get; set; }
    }

    /// <summary>Tags declared out of source order, the way FastVector3Int declares them.</summary>
    [WProtoContract]
    public sealed partial class OutOfOrderContract
    {
        [WProtoMember(1)]
        public int First;

        [WProtoMember(4)]
        public int Fourth;

        [WProtoMember(3)]
        public int Third;
    }

    /// <summary>Carries all four lifecycle hooks, privately.</summary>
    [WProtoContract]
    public sealed partial class HookedContract
    {
        /// <summary>The order the hooks actually ran in, for the test to assert against.</summary>
        public readonly System.Collections.Generic.List<string> Trace = new();

        /// <summary>
        /// How many times the after-deserialization hook has run, across every instance.
        /// </summary>
        /// <remarks>
        /// Static on purpose. An instance-local trace cannot observe a hook that ran on an object
        /// the formatter then threw away, which is exactly the failed-read case -- and a test that
        /// only inspects the returned value passes whether or not the hook fired.
        /// </remarks>
        public static int AfterDeserializationRuns;

        [WProtoMember(1)]
        public int Value;

        [WProtoBeforeSerialization]
        private void OnBeforeSerialization()
        {
            Trace.Add(nameof(OnBeforeSerialization));
        }

        [WProtoAfterSerialization]
        private void OnAfterSerialization()
        {
            Trace.Add(nameof(OnAfterSerialization));
        }

        [WProtoBeforeDeserialization]
        private void OnBeforeDeserialization()
        {
            Trace.Add(nameof(OnBeforeDeserialization));
        }

        [WProtoAfterDeserialization]
        private void OnAfterDeserialization()
        {
            AfterDeserializationRuns++;
            Trace.Add(nameof(OnAfterDeserialization));
        }
    }

    /// <summary>A struct contract, nested inside another type.</summary>
    public static partial class Outer
    {
        /// <summary>Proves the emitter reopens every enclosing type, not just the contract.</summary>
        [ProtoContract]
        [WProtoContract]
        public partial struct Point
        {
            [ProtoMember(1)]
            [WProtoMember(1)]
            public int X;

            [ProtoMember(2)]
            [WProtoMember(2)]
            public int Y;
        }
    }

    /// <summary>
    /// A contract whose members are other contracts -- the shape <c>WPROTO003</c> used to refuse.
    /// </summary>
    [WProtoContract]
    public sealed partial class NestingContract
    {
        [WProtoMember(1)]
        public int Id;

        [WProtoMember(2)]
        public HookedContract Child;

        [WProtoMember(3)]
        public Outer.Point Where;

        [WProtoMember(4)]
        public Outer.Point? MaybeWhere;
    }

    /// <summary>
    /// One more level, so a hooked contract sits two prefixes deep.
    /// </summary>
    /// <remarks>
    /// This is the shape that decides how sub-message lengths are produced. Re-measuring a child to
    /// size its prefix runs the child's before-serialization hook once per enclosing level while its
    /// after-serialization hook still runs once, so anything the before hook rents leaks one rental
    /// per level.
    /// </remarks>
    [WProtoContract]
    public sealed partial class DeepContract
    {
        [WProtoMember(1)]
        public int Id;

        [WProtoMember(2)]
        public NestingContract Child;
    }

    /// <summary>A sub-message big enough to need a multi-byte length prefix.</summary>
    [WProtoContract]
    public sealed partial class BulkContract
    {
        [WProtoMember(1)]
        public byte[] Payload;
    }

    /// <summary>Wraps <see cref="BulkContract"/> so its prefix has to be produced by a parent.</summary>
    [WProtoContract]
    public sealed partial class BulkHolder
    {
        [WProtoMember(1)]
        public BulkContract Child;

        [WProtoMember(2)]
        public int Trailer;
    }

    /// <summary>A contract with no members, so a present one is a key and a zero length.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class EmptyContract { }

    /// <summary>Every shape that can carry <c>IsRequired</c>, so what it forces can be pinned.</summary>
    [WProtoContract]
    public sealed partial class RequiredContract
    {
        [WProtoMember(1, IsRequired = true)]
        public int Number;

        [WProtoMember(2, IsRequired = true)]
        public EmptyContract Message;

        [WProtoMember(3, IsRequired = true)]
        public string Text;

        [WProtoMember(4, IsRequired = true)]
        public byte[] Bytes;

        [WProtoMember(5, IsRequired = true)]
        public Outer.Point Where;

        [WProtoMember(6, IsRequired = true)]
        public double? Ratio;
    }

    /// <summary>
    /// A contract that refers to itself: a legal schema, and one whose measurement is unbounded
    /// unless something bounds it.
    /// </summary>
    [WProtoContract]
    public sealed partial class ChainContract
    {
        [WProtoMember(1)]
        public int Id;

        [WProtoMember(2)]
        public ChainContract Next;
    }

    /// <summary>Every repeated element shape the generator claims to support.</summary>
    /// <remarks>
    /// Annotated for both serializers, with identical field numbers, so
    /// <c>OracleDifferentialTests</c> can hand the same instance to each and compare bytes. Tags
    /// that drifted apart would make that comparison meaningless, which is why the two attributes
    /// are declared beside each other rather than in separate files.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class RepeatedContract
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int[] Ints;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public List<int> IntList;

        [ProtoMember(3)]
        [WProtoMember(3)]
        public string[] Texts;

        [ProtoMember(4)]
        [WProtoMember(4)]
        public double[] Doubles;

        [ProtoMember(5)]
        [WProtoMember(5)]
        public ulong[] Longs;

        [ProtoMember(6)]
        [WProtoMember(6)]
        public bool[] Flags;

        [ProtoMember(7)]
        [WProtoMember(7)]
        public Mode[] Modes;

        [ProtoMember(8)]
        [WProtoMember(8)]
        public Outer.Point[] Points;

        [ProtoMember(9)]
        [WProtoMember(9)]
        public EmptyContract[] Messages;

        [ProtoMember(10)]
        [WProtoMember(10)]
        public byte[][] Blobs;

        [ProtoMember(11)]
        [WProtoMember(11)]
        public List<Outer.Point> PointList;

        [ProtoMember(12)]
        [WProtoMember(12)]
        public short[] Shorts;
    }

    /// <summary>
    /// Collections the constructor has already filled, which is the only way append and overwrite
    /// can be told apart.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SeededRepeatedContract
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public List<int> AppendedList = new List<int> { 7, 8 };

        [ProtoMember(2, OverwriteList = true)]
        [WProtoMember(2, OverwriteList = true)]
        public List<int> OverwrittenList = new List<int> { 7, 8 };

        [ProtoMember(3)]
        [WProtoMember(3)]
        public int[] AppendedArray = { 7, 8 };

        [ProtoMember(4, OverwriteList = true)]
        [WProtoMember(4, OverwriteList = true)]
        public int[] OverwrittenArray = { 7, 8 };

        [ProtoMember(5)]
        [WProtoMember(5)]
        public int Marker;
    }

    /// <summary>A struct contract carrying a repeated member.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial struct RepeatedStructContract
    {
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int[] Ints;

        [ProtoMember(2)]
        [WProtoMember(2)]
        public int Marker;
    }

    /// <summary>
    /// A collection implemented as a <b>struct</b>, which is the shape the emitter must not assume
    /// away.
    /// </summary>
    /// <remarks>
    /// Deliberately minimal, and deliberately lazy about its backing store, so a
    /// <c>default(IntBag)</c> is a legal empty value. That is what makes the copy semantics visible:
    /// the read loop accumulates into a local copy and the formatter has to assign it back, because
    /// every <c>Add</c> in between landed on the copy.
    /// </remarks>
    public struct IntBag : ICollection<int>
    {
        private List<int> _items;

        /// <inheritdoc />
        public int Count => _items == null ? 0 : _items.Count;

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

        private static readonly List<int> Empty = new List<int>();
    }

    /// <summary>
    /// The collection shapes beyond array and <c>List&lt;T&gt;</c>, annotated for both serializers.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class CollectionShapesContract
    {
        /// <summary>A set, which protobuf-net also treats as a repeated field.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public HashSet<int> Set;

        /// <summary>An ordered set of a length-delimited element.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public SortedSet<string> Sorted;

        /// <summary>A collection that is neither a list nor a set.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public System.Collections.ObjectModel.Collection<int> Owned;
    }

    /// <summary>
    /// A contract whose collection is a value type.
    /// </summary>
    /// <remarks>
    /// Not annotated for protobuf-net: it cannot serialize this member at all, which is the whole
    /// reason the shape is worth supporting. Its bytes are compared against the oracle's output for
    /// an <c>int[]</c> at the same field number instead, which is the stronger claim -- a struct
    /// collection is not a new encoding, it is the same repeated field with a different container.
    /// </remarks>
    [WProtoContract]
    public sealed partial class ValueTypeCollectionContract
    {
        /// <summary>The struct collection.</summary>
        [WProtoMember(1)]
        public IntBag Bag;

        /// <summary>The same, replaced rather than appended to on read.</summary>
        [WProtoMember(2, OverwriteList = true)]
        public IntBag Overwritten;

        /// <summary>
        /// A struct collection the constructor has already filled, which is the only way appending
        /// into a copy can be told from replacing it.
        /// </summary>
        [WProtoMember(3)]
        public IntBag Seeded = Filled();

        /// <summary>The same, under <c>OverwriteList</c>.</summary>
        [WProtoMember(4, OverwriteList = true)]
        public IntBag SeededOverwritten = Filled();

        private static IntBag Filled()
        {
            IntBag bag = new IntBag();
            bag.Add(7);
            bag.Add(8);
            return bag;
        }
    }

    /// <summary>The one enum the contracts above use.</summary>
    public enum Mode
    {
        /// <summary>The default, which is omitted from the wire.</summary>
        None = 0,

        /// <summary>A non-default value, which is written.</summary>
        Fast = 1,

        /// <summary>A larger value, to exercise a multi-byte varint.</summary>
        Careful = 300,
    }
}
