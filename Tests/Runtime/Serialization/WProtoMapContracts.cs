// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>A byte-backed input kind matching tuple keys in surveyed consumer contracts.</summary>
    public enum WProtoButtonType : byte
    {
        /// <summary>No button.</summary>
        None = 0,

        /// <summary>The primary button.</summary>
        Primary = 1,
    }

    /// <summary>Byte-backed directional flags matching tuple keys in surveyed consumer contracts.</summary>
    [Flags]
    public enum WProtoButtonDirection : byte
    {
        /// <summary>No direction.</summary>
        None = 0,

        /// <summary>Left.</summary>
        Left = 1,

        /// <summary>Right.</summary>
        Right = 2,
    }

    /// <summary>Map-shaped members, whose entries IL2CPP has to AOT-compile like any sub-message.</summary>
    /// <remarks>
    /// The field numbers match the contract the differential suite under <c>Generator~/</c> compares
    /// against protobuf-net, so the hex below is the oracle's own output.
    /// </remarks>
    [WProtoContract]
    public sealed partial class WProtoMapContract
    {
        /// <summary>A length-delimited key with a varint value.</summary>
        [WProtoMember(1)]
        public Dictionary<string, int> ByName;

        /// <summary>A varint key with a sub-message value.</summary>
        [WProtoMember(2)]
        public Dictionary<int, WProtoRepeatedPoint> ById;

        /// <summary>An ordered dictionary, so enumeration order is deterministic.</summary>
        [WProtoMember(3)]
        public SortedDictionary<string, string> Sorted;

        /// <summary>Replaced on read rather than merged into.</summary>
        [WProtoMember(4, OverwriteList = true)]
        public Dictionary<string, int> Overwritten;

        /// <summary>Merged into on read.</summary>
        [WProtoMember(5)]
        public Dictionary<string, int> Merged;
    }

    /// <summary>
    /// Tuple-shaped members found in consumer protobuf contracts, including a tuple map key.
    /// </summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class WProtoTupleMapContract
    {
        /// <summary>An ordinary two-component tuple member.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public ValueTuple<int, string> Pair;

        /// <summary>An ordinary three-component tuple member.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public ValueTuple<int, string, double> Triple;

        /// <summary>A tuple key, matching the consumer contract shape that prompted this fixture.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public Dictionary<(WProtoButtonType, WProtoButtonDirection), double> Values;
    }
}
