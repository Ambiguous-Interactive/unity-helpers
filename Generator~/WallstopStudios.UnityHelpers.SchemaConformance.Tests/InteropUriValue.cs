// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SchemaConformance.Tests
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    internal sealed partial class InteropUriValue
    {
        [WProtoMember(1)]
        public Uri Source;
    }
}
