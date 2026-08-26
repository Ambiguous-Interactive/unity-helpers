// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
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
    /// The standard-library collection shapes both protobuf-net majors round-trip (#395).
    /// </summary>
    /// <remarks>
    /// Annotated for both serializers, at identical field numbers, so the differential can hand the
    /// same instance to each. The membership of this contract is a measurement rather than a
    /// grouping: these are exactly the new shapes protobuf-net <b>2.4.9 and 3.2.56 both</b> write
    /// and read. Everything else #395 asked for lives on <see cref="V3CollectionContract"/> or
    /// <see cref="ConstructedCollectionContract"/>, because a member v2 cannot serve makes its model
    /// build throw for the whole contract.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class StdlibCollectionContract
    {
        /// <summary>Fills through <c>AddLast</c>; its <c>ICollection&lt;T&gt;.Add</c> is explicit.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public LinkedList<int> Linked;

        /// <summary>An interface with a length-delimited element.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public IList<string> Listed;

        /// <summary>An interface with a packable element.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public ICollection<int> Collected;

        /// <summary>The one supported collection with no <c>Count</c>.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        public IEnumerable<int> Enumerated;

        /// <summary>Read-only to the consumer, and still fillable by the formatter.</summary>
        [ProtoMember(7)]
        [WProtoMember(7)]
        public IReadOnlyList<int> ReadOnlyListed;

        /// <summary>The other read-only sequence interface.</summary>
        [ProtoMember(8)]
        [WProtoMember(8)]
        public IReadOnlyCollection<int> ReadOnlyCollected;

        /// <summary>The dictionary interface, which resolves to <c>Dictionary&lt;K,V&gt;</c>.</summary>
        [ProtoMember(9)]
        [WProtoMember(9)]
        public IDictionary<string, int> Mapped;
    }

    /// <summary>
    /// The collection shapes only protobuf-net 3.2.56 can serve.
    /// </summary>
    /// <remarks>
    /// Measured against both vendored oracles rather than assumed. 2.4.9 has <b>no serializer at
    /// all</b> for <c>Queue&lt;T&gt;</c> and <c>Stack&lt;T&gt;</c> -- its model build throws, which
    /// is why these cannot share <see cref="StdlibCollectionContract"/> -- and it writes
    /// <c>ISet&lt;T&gt;</c> and <c>IReadOnlyDictionary&lt;K,V&gt;</c> and then throws
    /// <see cref="System.NullReferenceException"/> reading either back. WallstopProto serves all
    /// four on both, so the differential over this contract is gated to the v3 process and the
    /// round trip through WallstopProto is asserted in both.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class V3CollectionContract
    {
        /// <summary>Fills through <c>Enqueue</c>; not an <c>ICollection&lt;T&gt;</c> at all.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public Queue<int> Queued;

        /// <summary>
        /// Written top-first and pushed back in reverse, which is what makes the round trip
        /// faithful.
        /// </summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public Stack<int> Stacked;

        /// <summary>The set interface, which resolves to <c>HashSet&lt;T&gt;</c>.</summary>
        [ProtoMember(8)]
        [WProtoMember(8)]
        public ISet<int> SetOf;

        /// <summary>The read-only dictionary interface, resolving to <c>Dictionary&lt;K,V&gt;</c>.</summary>
        [ProtoMember(10)]
        [WProtoMember(10)]
        public IReadOnlyDictionary<string, int> ReadOnlyMapped;

        /// <summary>A stack of messages, so the reversal is proven for a non-packable element.</summary>
        [ProtoMember(11)]
        [WProtoMember(11)]
        public Stack<Outer.Point> StackedPoints;
    }

    /// <summary>
    /// The two collections that can only be built once, never filled.
    /// </summary>
    /// <remarks>
    /// Neither protobuf-net major reads either back: 3.2.56 refuses both with "No parameterless
    /// constructor found", and 2.4.9 throws a <see cref="System.NullReferenceException"/> on
    /// <c>ReadOnlyCollection&lt;T&gt;</c> and cannot even build a model containing
    /// <c>ReadOnlyDictionary&lt;K,V&gt;</c>. The protobuf-net annotations therefore pin the
    /// <b>write</b> only, and reading these is strictly more than either oracle does with bytes it
    /// produced itself.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ConstructedCollectionContract
    {
        /// <summary>Accumulated into a list and constructed once.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public System.Collections.ObjectModel.ReadOnlyCollection<int> Frozen;

        /// <summary>The map analogue of the same problem.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public System.Collections.ObjectModel.ReadOnlyDictionary<string, int> FrozenMap;
    }

    /// <summary>
    /// The new collection shapes with a constructor value behind them, which is the only way append
    /// and overwrite can be told apart.
    /// </summary>
    /// <remarks>
    /// WallstopProto-only, deliberately. The shapes worth seeding include the two v2 cannot model at
    /// all, and splitting them across two seeded contracts would buy an oracle comparison the
    /// unseeded differentials already make. The append and overwrite answers below are the ones
    /// measured from protobuf-net 3.2.56 before the emitter was written.
    /// </remarks>
    [WProtoContract]
    public sealed partial class SeededStdlibContract
    {
        /// <summary>Appends at the end.</summary>
        [WProtoMember(1)]
        public LinkedList<int> Linked = new LinkedList<int>(new[] { 7, 8 });

        /// <summary>Appends at the back.</summary>
        [WProtoMember(2)]
        public Queue<int> Queued = new Queue<int>(new[] { 7, 8 });

        /// <summary>Pushes on top, first decoded element ending up topmost.</summary>
        [WProtoMember(3)]
        public Stack<int> Stacked = new Stack<int>(new[] { 7, 8 });

        /// <summary>The same, replaced rather than pushed onto.</summary>
        [WProtoMember(4, OverwriteList = true)]
        public Stack<int> OverwrittenStack = new Stack<int>(new[] { 7, 8 });

        /// <summary>An interface member whose current elements are copied forward.</summary>
        [WProtoMember(5)]
        public IList<int> Listed = new List<int> { 7, 8 };

        /// <summary>The same, replaced.</summary>
        [WProtoMember(6, OverwriteList = true)]
        public IList<int> OverwrittenList = new List<int> { 7, 8 };

        /// <summary>A set interface, merged into.</summary>
        [WProtoMember(7)]
        public ISet<int> SetOf = new HashSet<int> { 7, 8 };

        /// <summary>A dictionary interface, merged into.</summary>
        [WProtoMember(8)]
        public IDictionary<string, int> Mapped = new Dictionary<string, int> { { "seed", 9 } };
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
    /// A dictionary implemented as a <b>struct</b>, the map half of the same assumption.
    /// </summary>
    /// <remarks>
    /// Lazy about its backing store for the same reason <see cref="IntBag"/> is: a
    /// <c>default(IntPairs)</c> has to be a legal empty value, or the copy semantics the emitter
    /// depends on are never exercised.
    /// </remarks>
    public struct IntPairs : IDictionary<int, int>
    {
        private Dictionary<int, int> _items;

        private Dictionary<int, int> Items => _items ??= new Dictionary<int, int>();

        /// <inheritdoc />
        public int this[int key]
        {
            get => Items[key];
            set => Items[key] = value;
        }

        /// <inheritdoc />
        public ICollection<int> Keys => Items.Keys;

        /// <inheritdoc />
        public ICollection<int> Values => Items.Values;

        /// <inheritdoc />
        public int Count => _items == null ? 0 : _items.Count;

        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public void Add(int key, int value)
        {
            Items.Add(key, value);
        }

        /// <inheritdoc />
        public void Add(KeyValuePair<int, int> item)
        {
            Items.Add(item.Key, item.Value);
        }

        /// <inheritdoc />
        public void Clear()
        {
            _items = null;
        }

        /// <inheritdoc />
        public bool Contains(KeyValuePair<int, int> item)
        {
            return _items != null
                && _items.TryGetValue(item.Key, out int held)
                && held == item.Value;
        }

        /// <inheritdoc />
        public bool ContainsKey(int key)
        {
            return _items != null && _items.ContainsKey(key);
        }

        /// <inheritdoc />
        public void CopyTo(KeyValuePair<int, int>[] array, int arrayIndex) { }

        /// <inheritdoc />
        public bool Remove(int key)
        {
            return _items != null && _items.Remove(key);
        }

        /// <inheritdoc />
        public bool Remove(KeyValuePair<int, int> item)
        {
            return Remove(item.Key);
        }

        /// <inheritdoc />
        public bool TryGetValue(int key, out int value)
        {
            value = 0;
            return _items != null && _items.TryGetValue(key, out value);
        }

        /// <summary>
        /// Returns a non-boxing enumerator, which is what <c>foreach</c> in generated code binds to.
        /// </summary>
        /// <returns>The enumerator.</returns>
        public Dictionary<int, int>.Enumerator GetEnumerator()
        {
            return (_items ?? Empty).GetEnumerator();
        }

        IEnumerator<KeyValuePair<int, int>> IEnumerable<KeyValuePair<int, int>>.GetEnumerator()
        {
            return GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private static readonly Dictionary<int, int> Empty = new Dictionary<int, int>();
    }

    /// <summary>
    /// A contract whose collection and map members are value types.
    /// </summary>
    /// <remarks>
    /// Not annotated for protobuf-net: it cannot serialize these members at all, which is the whole
    /// reason the shape is worth supporting. Its bytes are compared against the oracle's output for
    /// an <c>int[]</c> or a <c>Dictionary&lt;int,int&gt;</c> at the same field number instead, which
    /// is the stronger claim -- a struct container is not a new encoding, it is the same repeated
    /// field or map with a different container.
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

        /// <summary>The struct dictionary.</summary>
        [WProtoMember(5)]
        public IntPairs Pairs;

        /// <summary>
        /// A struct dictionary the constructor has already filled, which is the only way merging
        /// into a copy can be told from replacing it.
        /// </summary>
        [WProtoMember(6)]
        public IntPairs SeededPairs = FilledPairs();

        /// <summary>The same, replaced rather than merged into on read.</summary>
        [WProtoMember(7, OverwriteList = true)]
        public IntPairs SeededOverwrittenPairs = FilledPairs();

        private static IntBag Filled()
        {
            IntBag bag = new IntBag();
            bag.Add(7);
            bag.Add(8);
            return bag;
        }

        private static IntPairs FilledPairs()
        {
            IntPairs pairs = new IntPairs();
            pairs.Add(7, 70);
            return pairs;
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

    /// <summary>
    /// The interface a value in this chain is usually held as.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>IRandom</c>: an interface with no members, implemented by the chain's base and
    /// also by a contract outside the chain, which is the pair of cases a declared root has to tell
    /// apart.
    /// </remarks>
    public interface IIncludeThing { }

    /// <summary>
    /// A polymorphic base, annotated for both serializers so the include encoding can be compared.
    /// </summary>
    /// <remarks>
    /// The shape mirrors <c>AbstractRandom</c>, which carries 17 includes at contiguous tags
    /// 100-116 and is the reason this feature exists.
    /// </remarks>
    [ProtoContract]
    [ProtoInclude(100, typeof(IncludeAlpha))]
    [ProtoInclude(101, typeof(IncludeBeta))]
    [WProtoContract]
    [WProtoInclude(100, typeof(IncludeAlpha))]
    [WProtoInclude(101, typeof(IncludeBeta))]
    public partial class IncludeBase : IIncludeThing
    {
        /// <summary>A base member, written after the include.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id;

        /// <summary>A length-delimited base member.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string Label;
    }

    /// <summary>A leaf subtype.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class IncludeAlpha : IncludeBase
    {
        /// <summary>The subtype's own member, in its own tag space.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int AlphaOnly;

        /// <summary>A second one, to prove the sub-message carries more than a marker.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public string AlphaText;
    }

    /// <summary>A subtype that is itself a base, so the nesting recurses.</summary>
    [ProtoContract]
    [ProtoInclude(200, typeof(IncludeGamma))]
    [WProtoContract]
    [WProtoInclude(200, typeof(IncludeGamma))]
    public partial class IncludeBeta : IncludeBase
    {
        /// <summary>A fixed64 member at the middle level.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public double BetaOnly;
    }

    /// <summary>The third level.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class IncludeGamma : IncludeBeta
    {
        /// <summary>The deepest member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public bool GammaOnly;
    }

    /// <summary>Holds a polymorphic value, so the include chain sits under a length prefix.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class IncludeHolder
    {
        /// <summary>The polymorphic member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public IncludeBase Value;

        /// <summary>A scalar after it.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public int Trailer;
    }

    /// <summary>
    /// An include whose tag is <b>lower</b> than a base member's, which is what proves the include
    /// is not merely sorted into field-number order.
    /// </summary>
    [ProtoContract]
    [ProtoInclude(3, typeof(LowTagSub))]
    [WProtoContract]
    [WProtoInclude(3, typeof(LowTagSub))]
    public partial class LowTagBase
    {
        /// <summary>A member numbered below the include.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int First;

        /// <summary>A member numbered above the include.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public int Fifth;
    }

    /// <summary>The subtype for <see cref="LowTagBase"/>.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial class LowTagSub : LowTagBase
    {
        /// <summary>The subtype's own member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int SubOnly;
    }

    /// <summary>
    /// An abstract base with an include, which is the shape <c>AbstractRandom</c> has.
    /// </summary>
    /// <remarks>
    /// Not annotated for protobuf-net: what it pins is that reading a payload with no include tag
    /// fails rather than producing an instance of a type that cannot exist.
    /// </remarks>
    [WProtoContract]
    [WProtoInclude(100, typeof(ConcreteShape))]
    public abstract partial class AbstractShape
    {
        /// <summary>A base member.</summary>
        [WProtoMember(1)]
        public int Sides;
    }

    /// <summary>The only concrete shape.</summary>
    [WProtoContract]
    public partial class ConcreteShape : AbstractShape
    {
        /// <summary>The subtype's own member.</summary>
        [WProtoMember(1)]
        public int Edge;
    }

    /// <summary>
    /// A subtype nothing declares, so writing it must be refused rather than downgraded.
    /// </summary>
    /// <remarks>
    /// <c>value is IncludeAlpha</c> is true for this, so a formatter without the guard would write
    /// it under Alpha's include tag and read it back as an <c>IncludeAlpha</c> — a level of type
    /// identity lost from saved data with nothing to report it.
    /// </remarks>
    public sealed class UndeclaredAlpha : IncludeAlpha { }

    /// <summary>
    /// A contract that implements the declared interface without joining its root's chain.
    /// </summary>
    /// <remarks>
    /// This is a consumer's own implementation of an interface the package declares a root for. It
    /// has a perfectly good formatter of its own, and the declared root must still refuse it: its
    /// payload is not <see cref="IncludeBase"/>'s chain, so decoding one as the other hands back
    /// the wrong type from bytes something else wrote.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ForeignThing : IIncludeThing
    {
        /// <summary>The only member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Value;
    }

    /// <summary>
    /// An abstract polymorphic base whose members include a <b>collection</b>.
    /// </summary>
    /// <remarks>
    /// The shape that crashed. An abstract base has no instance until an include tag arrives, so a
    /// collection element read before that tag has no member to seed from -- the generated seed
    /// dereferenced a null <c>read</c>. Nothing covered it because the first abstract fixture held
    /// only scalars.
    /// </remarks>
    [WProtoContract]
    [WProtoInclude(100, typeof(PolyListSub))]
    public abstract partial class PolyListBase
    {
        /// <summary>A collection on an abstract base.</summary>
        [WProtoMember(1)]
        public List<int> Items = new List<int> { 7, 8 };

        /// <summary>An array too, since the two take different epilogue paths.</summary>
        [WProtoMember(2)]
        public int[] Extras;
    }

    /// <summary>
    /// The subtype, whose constructor seeds a <b>different</b> collection from its base's.
    /// </summary>
    /// <remarks>
    /// Deliberately different. With identical seeds, appending onto the provisional base instance
    /// and appending onto the final subtype produce the same answer, and a test cannot tell a
    /// correct implementation from one that seeds too early.
    /// </remarks>
    [WProtoContract]
    public partial class PolyListSub : PolyListBase
    {
        /// <summary>Replaces the base constructor's seed.</summary>
        public PolyListSub()
        {
            Items = new List<int> { 5 };
        }

        /// <summary>The subtype's own member.</summary>
        [WProtoMember(1)]
        public int SubOnly;
    }

    /// <summary>
    /// A polymorphic base carrying the collection shapes whose commit is not an assignment.
    /// </summary>
    /// <remarks>
    /// A contract with an include reads every member aside and commits it once the include has
    /// settled which instance it belongs to. That path builds the accumulator from a different
    /// place than the ordinary one, so a form whose commit consults the member -- a stack -- or
    /// constructs a new value -- a read-only collection -- has a second code path nothing else
    /// exercises.
    /// </remarks>
    [WProtoContract]
    [WProtoInclude(100, typeof(PolyStackSub))]
    public abstract partial class PolyStackBase
    {
        /// <summary>A stack, whose commit pushes the decoded run back in reverse.</summary>
        [WProtoMember(1)]
        public Stack<int> Stacked = new Stack<int>(new[] { 7, 8 });

        /// <summary>A read-only collection, whose commit constructs rather than assigns.</summary>
        [WProtoMember(2)]
        public System.Collections.ObjectModel.ReadOnlyCollection<int> Frozen;

        /// <summary>An interface, whose accumulator is seeded by copy.</summary>
        [WProtoMember(3)]
        public IList<int> Listed = new List<int> { 7, 8 };
    }

    /// <summary>The subtype, whose constructor seeds different collections from its base's.</summary>
    [WProtoContract]
    public partial class PolyStackSub : PolyStackBase
    {
        /// <summary>Replaces the base constructor's seeds, so seeding too early is visible.</summary>
        public PolyStackSub()
        {
            Stacked = new Stack<int>(new[] { 5 });
            Listed = new List<int> { 5 };
        }

        /// <summary>The subtype's own member.</summary>
        [WProtoMember(1)]
        public int SubOnly;
    }

    /// <summary>
    /// An immutable contract whose members are the shapes that cannot simply be assigned.
    /// </summary>
    /// <remarks>
    /// A contract built by a constructor has no instance to seed from, so every collection starts
    /// empty and the whole value is produced once the last member is read -- a third path through
    /// the same commit code, and the one that dereferences a null instance if a seed reaches for
    /// the member.
    /// </remarks>
    [WProtoContract]
    public sealed partial class ImmutableCollectionRecord
    {
        /// <summary>A readonly stack, whose commit constructs its own target.</summary>
        [WProtoMember(1)]
        public readonly Stack<int> Stacked;

        /// <summary>A readonly read-only collection: constructed twice over.</summary>
        [WProtoMember(2)]
        public readonly System.Collections.ObjectModel.ReadOnlyCollection<int> Frozen;

        /// <summary>A get-only interface member.</summary>
        [WProtoMember(3)]
        public IList<int> Listed { get; }

        /// <summary>A readonly dictionary interface.</summary>
        [WProtoMember(4)]
        public readonly IDictionary<string, int> Mapped;
    }

    /// <summary>
    /// The reference encoding for the struct dictionary on <see cref="ValueTypeCollectionContract"/>.
    /// </summary>
    /// <remarks>
    /// protobuf-net cannot serialize a struct dictionary at all, so the claim is made the same way
    /// the struct collection's is: the oracle is asked for an ordinary <c>Dictionary</c> at the same
    /// field number, and the bytes must agree. The field numbers therefore have to match
    /// <see cref="ValueTypeCollectionContract"/>'s.
    /// </remarks>
    [ProtoContract]
    public sealed class IntKeyedMapContract
    {
        /// <summary>Mirrors <c>ValueTypeCollectionContract.Pairs</c>.</summary>
        [ProtoMember(5)]
        public Dictionary<int, int> Pairs;
    }

    /// <summary>Map-shaped members, annotated for both serializers.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class MapContract
    {
        /// <summary>A length-delimited key with a varint value.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public Dictionary<string, int> ByName;

        /// <summary>A varint key with a sub-message value.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public Dictionary<int, Outer.Point> ById;

        /// <summary>An ordered dictionary, so enumeration order is deterministic.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public SortedDictionary<string, string> Sorted;

        /// <summary>Replaced on read rather than merged into.</summary>
        [ProtoMember(4, OverwriteList = true)]
        [WProtoMember(4, OverwriteList = true)]
        public Dictionary<string, int> Overwritten = new Dictionary<string, int> { { "seed", 9 } };

        /// <summary>Merged into on read.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public Dictionary<string, int> Merged = new Dictionary<string, int> { { "seed", 9 } };
    }

    /// <summary>
    /// The map shape common to both protobuf-net oracle majors.
    /// </summary>
    /// <remarks>
    /// protobuf-net 2.4.9 cannot compile <see cref="MapContract"/> for reading because that contract
    /// also contains a map whose value is a struct sub-message. Keeping this minimal contract
    /// separate lets the dual-oracle suite prove string-map migration in both directions without
    /// mistaking that v2 compiler limitation for a WallstopProto failure.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class V2CompatibleMapContract
    {
        /// <summary>A string-keyed map with a scalar value.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public Dictionary<string, int> Values;

        /// <summary>An ordered dictionary, so enumeration order is deterministic.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public SortedDictionary<string, string> Sorted;

        /// <summary>Replaced on read rather than merged into.</summary>
        [ProtoMember(4, OverwriteList = true)]
        [WProtoMember(4, OverwriteList = true)]
        public Dictionary<string, int> Overwritten = new Dictionary<string, int> { { "seed", 9 } };

        /// <summary>Merged into on read.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public Dictionary<string, int> Merged = new Dictionary<string, int> { { "seed", 9 } };
    }

    /// <summary>An enum, so an enum-keyed map has something to name.</summary>
    public enum MapKeyKind
    {
        /// <summary>The default, which is also a map key worth pinning.</summary>
        None = 0,

        /// <summary>A non-default value.</summary>
        Other = 7,
    }

    /// <summary>Map keys the protobuf SPEC does not allow but protobuf-net encodes anyway.</summary>
    /// <remarks>
    /// The map emitter's comment used to claim keys were restricted to "the integral types, bool and
    /// string" and that anything else was refused. Measured against protobuf-net 3.2.56, all four of
    /// these encode without complaint, so refusing them would have broken parity with the oracle
    /// rather than protected anyone. This fixture is what keeps the permissiveness deliberate.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ExoticKeyContract
    {
        /// <summary>A fixed32 key.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public Dictionary<float, int> ByFloat;

        /// <summary>A fixed64 key.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public Dictionary<double, int> ByDouble;

        /// <summary>An enum key, which travels as a varint.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public Dictionary<MapKeyKind, int> ByEnum;

        /// <summary>A bool key, which the spec does allow.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public Dictionary<bool, int> ByBool;
    }

    /// <summary>A map whose VALUE is a contract carrying lifecycle hooks.</summary>
    /// <remarks>
    /// The case the map emitter's own comment assumed away: it justified measuring an entry twice on
    /// the grounds that "an entry is two scalars with no lifecycle hooks". A map value may be any
    /// contract, and this is the fixture that says so.
    /// </remarks>
    [WProtoContract]
    public sealed partial class HookedMapContract
    {
        /// <summary>A varint key with a hooked sub-message value.</summary>
        [WProtoMember(1)]
        public Dictionary<int, HookedContract> ById;
    }

    /// <summary>
    /// A type this assembly does not get to annotate, standing in for <c>UnityEngine.Vector3</c>.
    /// </summary>
    public struct ForeignVector3
    {
        /// <summary>The first component.</summary>
        public float x;

        /// <summary>The second component.</summary>
        public float y;

        /// <summary>The third component.</summary>
        public float z;
    }

    /// <summary>The contract that gives <see cref="ForeignVector3"/> a wire shape.</summary>
    [ProtoContract]
    [WProtoContract]
    public partial struct ForeignVector3Surrogate
    {
        /// <summary>The first component.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public float x;

        /// <summary>The second component.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public float y;

        /// <summary>The third component.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public float z;

        /// <summary>Converts to the real type.</summary>
        /// <param name="value">The surrogate.</param>
        public static implicit operator ForeignVector3(ForeignVector3Surrogate value)
        {
            return new ForeignVector3
            {
                x = value.x,
                y = value.y,
                z = value.z,
            };
        }

        /// <summary>Converts from the real type.</summary>
        /// <param name="value">The real value.</param>
        public static implicit operator ForeignVector3Surrogate(ForeignVector3 value)
        {
            return new ForeignVector3Surrogate
            {
                x = value.x,
                y = value.y,
                z = value.z,
            };
        }
    }

    /// <summary>Holds surrogated values in each position a member can occupy.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class SurrogateHolder
    {
        /// <summary>A plain surrogated member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public ForeignVector3 Position;

        /// <summary>A repeated surrogated element.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public ForeignVector3[] Path;

        /// <summary>A scalar after them, to pin ordering.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public int Trailer;

        /// <summary>A surrogated map value, so the substitution reaches every shape.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public System.Collections.Generic.Dictionary<string, ForeignVector3> Named;
    }

    /// <summary>
    /// The surrogate shapes protobuf-net 2.4.9 can compile as an oracle.
    /// </summary>
    /// <remarks>
    /// The v2 runtime compiler cannot represent a surrogated struct as a map value's default. This
    /// contract deliberately omits that one v3-only shape while retaining scalar and repeated
    /// positions for cross-major byte and read compatibility.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class V2CompatibleSurrogateHolder
    {
        /// <summary>A plain surrogated member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public ForeignVector3 Position;

        /// <summary>A repeated surrogated element.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public ForeignVector3[] Path;

        /// <summary>A scalar after them, to pin ordering.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public int Trailer;
    }

    /// <summary>
    /// A generic contract, whose members' encodings are only decided by the closure.
    /// </summary>
    /// <remarks>
    /// Annotated for both serializers so the closures can be compared byte for byte. The field key
    /// itself changes with <c>T</c> -- <c>08</c> for an int, <c>09</c> for a double, <c>0A</c> for a
    /// string -- which is why the emitted code asks <c>WProtoGeneric&lt;T&gt;</c> instead of
    /// carrying a constant.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public partial class Box<T>
    {
        /// <summary>A member typed as the parameter.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public T Value;

        /// <summary>A repeated member of the parameter.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public T[] Many;

        /// <summary>A scalar, to pin ordering against the generic members.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public int Trailer;
    }

    /// <summary>
    /// A generic contract whose collection members are the shapes #395 added.
    /// </summary>
    /// <remarks>
    /// WallstopProto-only: protobuf-net 2.4.9 has no serializer for <c>Queue&lt;T&gt;</c> or
    /// <c>Stack&lt;T&gt;</c> at any closure. The point of the fixture is the intersection of two
    /// runtime decisions -- whether the element packs is decided by the closure, and how the
    /// collection is filled is decided by the declared type -- which is where a per-type fill method
    /// and a per-closure packed branch could disagree.
    /// </remarks>
    [WProtoContract]
    public partial class CollectionBox<T>
    {
        /// <summary>Filled through <c>Enqueue</c> whatever the closure.</summary>
        [WProtoMember(1)]
        public Queue<T> Queued;

        /// <summary>Pushed back in reverse whatever the closure.</summary>
        [WProtoMember(2)]
        public Stack<T> Stacked;

        /// <summary>Constructed as a <c>List&lt;T&gt;</c> whatever the closure.</summary>
        [WProtoMember(3)]
        public IList<T> Listed;

        /// <summary>The one supported shape with no <c>Count</c> to test for emptiness.</summary>
        [WProtoMember(4)]
        public IEnumerable<T> Enumerated;

        /// <summary>A scalar, to pin ordering against the collection members.</summary>
        [WProtoMember(5)]
        public int Trailer;
    }

    /// <summary>Names the closures of <see cref="CollectionBox{T}"/> this assembly uses.</summary>
    public static class CollectionBoxClosures
    {
        /// <summary>A packable closure, whose runs are written packed.</summary>
        public static CollectionBox<int> Ints;

        /// <summary>A length-delimited closure, which cannot pack.</summary>
        public static CollectionBox<string> Texts;
    }

    /// <summary>A generic contract whose member is required.</summary>
    /// <remarks>
    /// IsRequired is decided at generate time for a scalar and cannot be for a type parameter: what
    /// "required" does to a default depends on whether the closure is a value type, which is exactly
    /// what is unknown until it closes.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public partial class RequiredBox<T>
    {
        /// <summary>A required member typed as the parameter.</summary>
        [ProtoMember(1, IsRequired = true)]
        [WProtoMember(1, IsRequired = true)]
        public T Value;
    }

    /// <summary>Closures of <see cref="RequiredBox{T}"/>, one per omission rule.</summary>
    public static class RequiredBoxClosures
    {
        /// <summary>A value-type closure, whose default is still written.</summary>
        public static RequiredBox<int> Ints;

        /// <summary>A reference closure, whose null stays absent even when required.</summary>
        public static RequiredBox<string> Texts;
    }

    /// <summary>Names the closures this assembly uses, so the generator registers them.</summary>
    /// <remarks>
    /// A registrar cannot register an open generic, and constructing one at runtime needs
    /// <c>MakeGenericType</c> -- the exact call IL2CPP cannot compile. The generator therefore
    /// registers the constructions it can see in source, and this is where these ones become
    /// visible.
    /// </remarks>
    public static class BoxClosures
    {
        /// <summary>An integer closure.</summary>
        public static Box<int> Ints;

        /// <summary>A floating-point closure, whose wire type differs from the integer one.</summary>
        public static Box<double> Doubles;

        /// <summary>A length-delimited closure.</summary>
        public static Box<string> Texts;

        /// <summary>A message closure, so the sub-message path is covered too.</summary>
        public static Box<Outer.Point> Points;

        /// <summary>A base-class-library message closure.</summary>
        public static Box<DateTime> DateTimes;

        /// <summary>A duration message closure.</summary>
        public static Box<TimeSpan> TimeSpans;

        /// <summary>An identifier message closure.</summary>
        public static Box<Guid> Guids;

        /// <summary>A decimal message closure, including signed zero.</summary>
        public static Box<decimal> Decimals;
    }

    /// <summary>
    /// An immutable contract: every serialized member is <c>readonly</c>.
    /// </summary>
    /// <remarks>
    /// The shape thirty of this package's serialized fields have -- <c>FastVector2Int.x</c>,
    /// <c>Line2D.from</c>, <c>ImmutableBitSet._bits</c> and the rest. protobuf-net assigns them by
    /// reflection; this generator cannot, so it emits a private constructor into the contract's
    /// partial declaration instead. The type keeps its immutability and gains no public surface.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public readonly partial struct ImmutablePoint
    {
        /// <summary>A readonly scalar.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public readonly int X;

        /// <summary>A second one, so the constructor takes more than one argument.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public readonly int Y;

        /// <summary>A readonly length-delimited member.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public readonly string Label;

        /// <summary>A readonly collection, which takes the repeated path.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public readonly int[] Marks;

        /// <summary>The author's own constructor, which the generated one must not collide with.</summary>
        /// <param name="x">The first component.</param>
        /// <param name="y">The second component.</param>
        public ImmutablePoint(int x, int y)
        {
            X = x;
            Y = y;
            Label = null;
            Marks = null;
        }
    }

    /// <summary>An immutable reference contract, so the class path is covered too.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ImmutableRecord
    {
        /// <summary>A get-only property, which is equally unassignable.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public int Id { get; }

        /// <summary>A readonly field.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public readonly string Name;

        /// <summary>
        /// A readonly collection on a REFERENCE contract, which is the case that crashes if the
        /// seed reaches for an instance that does not exist yet: `read` is null until the
        /// constructor runs, so seeding from `read.Tags` dereferences it.
        /// </summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public readonly int[] Tags;

        /// <summary>protobuf-net needs one; the generated constructor is separate.</summary>
        public ImmutableRecord() { }
    }

    /// <summary>
    /// The base-class-library value types both protobuf-net majors encode identically (#399).
    /// </summary>
    /// <remarks>
    /// Annotated for both serializers at identical field numbers, so the differential can hand the
    /// same instance to each. The membership is a measurement: DateTime, TimeSpan, Guid and decimal
    /// produce identical bytes on 2.4.9 and 3.2.56, while DateTimeOffset has no encoding in either
    /// major and stays a generator refusal.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class BclScalarContract
    {
        /// <summary>Travels as ticks since 1970 under the largest whole unit.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public DateTime When;

        /// <summary>Travels as a scaled tick count.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public TimeSpan Duration;

        /// <summary>Travels as two fixed-64 halves.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public Guid Identifier;

        /// <summary>Travels as mantissa varints plus a sign-and-scale field.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public decimal Amount;

        /// <summary>Nullable of a message-encoded value: omitted when null, sub-message when not.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public DateTime? NullableWhen;

        /// <summary>A repeated message-encoded element, which can never be packed.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        public List<DateTime> Timeline;

        /// <summary>A map whose value is message-encoded.</summary>
        [ProtoMember(7)]
        [WProtoMember(7)]
        public Dictionary<string, TimeSpan> DurationsByName;
    }

    /// <summary>BCL value types in the map-key position protobuf-net accepts.</summary>
    /// <remarks>
    /// These are outside proto3's schema grammar but have stable protobuf-net encodings in both
    /// oracle majors. Keeping them separate from <see cref="BclScalarContract"/> makes the expanded
    /// key surface explicit without multiplying every corpus case.
    /// </remarks>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class BclKeyContract
    {
        /// <summary>A DateTime key.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public Dictionary<DateTime, int> ByDate;

        /// <summary>A TimeSpan key.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public Dictionary<TimeSpan, int> ByDuration;

        /// <summary>A Guid key.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public Dictionary<Guid, int> ByIdentifier;

        /// <summary>A decimal key.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public Dictionary<decimal, int> ByAmount;
    }
}
