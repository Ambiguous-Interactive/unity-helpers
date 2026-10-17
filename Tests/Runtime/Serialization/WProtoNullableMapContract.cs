// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Serialization
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Checks nullable map entries in native players without a legacy backend.</summary>
    [WProtoContract]
    public sealed partial class WProtoNullableMapContract
    {
        /// <summary>Nullable scalar values.</summary>
        [WProtoMember(1)]
        public Dictionary<int, int?> Values;

        /// <summary>Nullable scalar keys and values.</summary>
        [WProtoMember(2)]
        public Dictionary<int?, int?> Keys;

        /// <summary>Nullable message values.</summary>
        [WProtoMember(3)]
        public Dictionary<int, DateTime?> Dates;
    }
}
