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
    public partial class IncludeBase
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
}
