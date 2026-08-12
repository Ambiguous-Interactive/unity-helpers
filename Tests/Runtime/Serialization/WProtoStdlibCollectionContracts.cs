// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The standard-library collection shapes added for issue 395, at the field numbers the
    /// differential suite uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The numbers match the contracts under <c>Generator~/</c> so the golden hex in
    /// <c>WProtoStdlibCollectionTests</c> is a transcription of a measured payload rather than this
    /// session's reading of the wire format. That indirection is the point: protobuf-net cannot run
    /// under IL2CPP, so the standalone legs are the only place this generated code is AOT-compiled,
    /// and they have no oracle to compare against.
    /// </para>
    /// <para>
    /// Four of these shapes are ones protobuf-net cannot round-trip at all -- 2.4.9 has no
    /// serializer for <c>Queue&lt;T&gt;</c> or <c>Stack&lt;T&gt;</c>, and neither major reads back
    /// a <c>ReadOnlyCollection&lt;T&gt;</c> or <c>ReadOnlyDictionary&lt;K,V&gt;</c> it wrote itself.
    /// </para>
    /// </remarks>
    [WProtoContract]
    public sealed partial class WProtoStdlibCollectionContract
    {
        /// <summary>Filled through <c>AddLast</c>; its <c>ICollection&lt;T&gt;.Add</c> is explicit.</summary>
        [WProtoMember(1)]
        public LinkedList<int> Linked;

        /// <summary>Filled through <c>Enqueue</c>; not an <c>ICollection&lt;T&gt;</c> at all.</summary>
        [WProtoMember(2)]
        public Queue<int> Queued;

        /// <summary>Written top-first and pushed back in reverse.</summary>
        [WProtoMember(3)]
        public Stack<int> Stacked;

        /// <summary>An interface with a length-delimited element.</summary>
        [WProtoMember(4)]
        public IList<string> Listed;

        /// <summary>An interface with a packable element.</summary>
        [WProtoMember(5)]
        public ICollection<int> Collected;

        /// <summary>The one supported collection with no <c>Count</c> to test for emptiness.</summary>
        [WProtoMember(6)]
        public IEnumerable<int> Enumerated;

        /// <summary>Read-only to the consumer, and still fillable by the formatter.</summary>
        [WProtoMember(7)]
        public IReadOnlyList<int> ReadOnlyListed;

        /// <summary>The set interface, which resolves to <c>HashSet&lt;T&gt;</c>.</summary>
        [WProtoMember(8)]
        public ISet<int> SetOf;

        /// <summary>The dictionary interface, which resolves to <c>Dictionary&lt;K,V&gt;</c>.</summary>
        [WProtoMember(9)]
        public IDictionary<string, int> Mapped;

        /// <summary>The read-only dictionary interface, resolving to the same.</summary>
        [WProtoMember(10)]
        public IReadOnlyDictionary<string, int> ReadOnlyMapped;

        /// <summary>A stack of messages, so the reversal is covered for a non-packable element.</summary>
        [WProtoMember(11)]
        public Stack<WProtoRepeatedPoint> StackedPoints;
    }

    /// <summary>The two collections that can only be built once, never filled.</summary>
    [WProtoContract]
    public sealed partial class WProtoConstructedCollectionContract
    {
        /// <summary>Accumulated into a list and constructed once.</summary>
        [WProtoMember(1)]
        public ReadOnlyCollection<int> Frozen;

        /// <summary>The map analogue of the same problem.</summary>
        [WProtoMember(2)]
        public ReadOnlyDictionary<string, int> FrozenMap;
    }

    /// <summary>
    /// The new shapes with a constructor value behind them, which is the only way append and
    /// overwrite can be told apart.
    /// </summary>
    [WProtoContract]
    public sealed partial class WProtoSeededStdlibContract
    {
        /// <summary>Appends at the end.</summary>
        [WProtoMember(1)]
        public LinkedList<int> Linked = new LinkedList<int>(new[] { 7, 8 });

        /// <summary>Appends at the back.</summary>
        [WProtoMember(2)]
        public Queue<int> Queued = new Queue<int>(new[] { 7, 8 });

        /// <summary>Pushes on top, the first decoded element ending up topmost.</summary>
        [WProtoMember(3)]
        public Stack<int> Stacked = new Stack<int>(new[] { 7, 8 });

        /// <summary>The same, replaced rather than pushed onto.</summary>
        [WProtoMember(4, OverwriteList = true)]
        public Stack<int> OverwrittenStack = new Stack<int>(new[] { 7, 8 });

        /// <summary>An interface member whose current elements are copied forward.</summary>
        [WProtoMember(5)]
        public IList<int> Listed = new List<int> { 7, 8 };

        /// <summary>A dictionary interface, merged into.</summary>
        [WProtoMember(9)]
        public IDictionary<string, int> Mapped = new Dictionary<string, int> { { "seed", 9 } };
    }
}
