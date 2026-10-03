// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Mirrors enum and integer map-key compatibility controls in players.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class WProtoZeroKeyMapContract
    {
        /// <summary>A byte-backed enum key with a double value.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public Dictionary<WProtoButtonType, double> ByEnum;

        /// <summary>An integer key with a double value.</summary>
        [ProtoMember(2)]
        [WProtoMember(2)]
        public Dictionary<int, double> ByInteger;

        /// <summary>A floating-point key whose default remains omitted.</summary>
        [ProtoMember(3)]
        [WProtoMember(3)]
        public Dictionary<float, double> BySingle;

        /// <summary>A double key whose default remains omitted.</summary>
        [ProtoMember(4)]
        [WProtoMember(4)]
        public Dictionary<double, double> ByDouble;
    }
}
