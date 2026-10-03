// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Pins default map keys across the supported scalar shapes.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class ZeroKeyMapContract
    {
        /// <summary>A int key map.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public Dictionary<int, double> Signed32;

        /// <summary>A long key map.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public Dictionary<long, double> Signed64;

        /// <summary>A uint key map.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public Dictionary<uint, double> Unsigned32;

        /// <summary>A ulong key map.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public Dictionary<ulong, double> Unsigned64;

        /// <summary>A bool key map.</summary>
        [ProtoMember(5)]
        [WProtoMember(5)]
        public Dictionary<bool, double> Boolean;

        /// <summary>A ZeroMapKeyKind key map.</summary>
        [ProtoMember(6)]
        [WProtoMember(6)]
        public Dictionary<ZeroMapKeyKind, double> Enumeration;

        /// <summary>A float key map.</summary>
        [ProtoMember(7)]
        [WProtoMember(7)]
        public Dictionary<float, double> Single;

        /// <summary>A double key map.</summary>
        [ProtoMember(8)]
        [WProtoMember(8)]
        public Dictionary<double, double> Double;

        /// <summary>A string key map.</summary>
        [ProtoMember(9)]
        [WProtoMember(9)]
        public Dictionary<string, double> Text;
    }
}
