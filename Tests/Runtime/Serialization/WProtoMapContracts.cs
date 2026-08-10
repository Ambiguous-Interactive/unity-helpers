// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

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
}
