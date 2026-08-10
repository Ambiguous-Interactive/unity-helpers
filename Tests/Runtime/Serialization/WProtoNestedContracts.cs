// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The bottom of a nested graph: lifecycle hooks, and a payload wide enough to move its own
    /// length prefix past one byte.
    /// </summary>
    /// <remarks>
    /// The hook counters are static because the question they answer is how many times a hook ran
    /// for one serialization of an enclosing graph, and the instance carrying them is two levels
    /// down from the value anybody hands to a formatter.
    /// </remarks>
    [WProtoContract]
    public sealed partial class WProtoNestedLeafContract
    {
        /// <summary>How many times the before-serialization hook has run, across every instance.</summary>
        public static int BeforeSerializationRuns;

        /// <summary>How many times the after-serialization hook has run, across every instance.</summary>
        public static int AfterSerializationRuns;

        /// <summary>A scalar, so an empty leaf and a populated one differ on the wire.</summary>
        [WProtoMember(1)]
        public int Value;

        /// <summary>Bulk bytes, to drive the enclosing length prefixes across their width boundary.</summary>
        [WProtoMember(2)]
        public byte[] Bulk;

        [WProtoBeforeSerialization]
        private void OnBeforeSerialization()
        {
            BeforeSerializationRuns++;
        }

        [WProtoAfterSerialization]
        private void OnAfterSerialization()
        {
            AfterSerializationRuns++;
        }
    }

    /// <summary>The middle of the graph, so the leaf sits under two length prefixes.</summary>
    [WProtoContract]
    public sealed partial class WProtoNestedMidContract
    {
        /// <summary>A scalar written before the sub-message, in ascending field order.</summary>
        [WProtoMember(1)]
        public int Id;

        /// <summary>The nested contract.</summary>
        [WProtoMember(2)]
        public WProtoNestedLeafContract Child;
    }

    /// <summary>The top of the graph.</summary>
    [WProtoContract]
    public sealed partial class WProtoNestedRootContract
    {
        /// <summary>A scalar written before the sub-message, in ascending field order.</summary>
        [WProtoMember(1)]
        public int Id;

        /// <summary>The nested contract.</summary>
        [WProtoMember(2)]
        public WProtoNestedMidContract Child;

        /// <summary>A scalar written after the sub-message, so a wrong prefix width corrupts it.</summary>
        [WProtoMember(3)]
        public int Trailer;
    }

    /// <summary>A contract with no members, so a present one is a key and a zero length.</summary>
    [WProtoContract]
    public sealed partial class WProtoEmptyContract { }

    /// <summary>
    /// Every shape that can carry <c>IsRequired</c>, so what it forces can be pinned.
    /// </summary>
    /// <remarks>
    /// The reference members are the interesting ones. <c>IsRequired</c> forces a value that equals
    /// its default onto the wire; it does not invent one for a <c>null</c>. Reading it as "always
    /// present" writes an empty string where protobuf-net wrote nothing, and hands the nested
    /// formatter's <c>Measure</c> a null to dereference -- an exception from the first save in a
    /// shipped player, which is the failure mode this whole serializer exists to remove.
    /// </remarks>
    [WProtoContract]
    public sealed partial class WProtoRequiredContract
    {
        /// <summary>A value type, written even at its default.</summary>
        [WProtoMember(1, IsRequired = true)]
        public int Number;

        /// <summary>A reference sub-message, absent when null.</summary>
        [WProtoMember(2, IsRequired = true)]
        public WProtoEmptyContract Message;

        /// <summary>A string, absent when null and present when empty.</summary>
        [WProtoMember(3, IsRequired = true)]
        public string Text;

        /// <summary>Bytes, absent when null and present when empty.</summary>
        [WProtoMember(4, IsRequired = true)]
        public byte[] Bytes;
    }

    /// <summary>
    /// A contract that refers to itself: a legal schema whose measurement is unbounded unless
    /// something bounds it.
    /// </summary>
    [WProtoContract]
    public sealed partial class WProtoNestedChainContract
    {
        /// <summary>This link's position in the chain.</summary>
        [WProtoMember(1)]
        public int Id;

        /// <summary>The next link, or <c>null</c> at the end.</summary>
        [WProtoMember(2)]
        public WProtoNestedChainContract Next;
    }
}
