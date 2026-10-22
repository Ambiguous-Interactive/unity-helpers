// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SchemaConformance.Tests
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    internal sealed partial class InteropUriValues
    {
        [WProtoMember(1)]
        public Uri Source;

        [WProtoMember(2)]
        public List<Uri> Mirrors;

        [WProtoMember(3)]
        public Dictionary<string, Uri> ByName;
    }
}
