// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
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
        [WProtoContract]
        public partial struct Point
        {
            [WProtoMember(1)]
            public int X;

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
