// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public sealed partial class WProtoDecodedPayloadContract
    {
        [WProtoMember(1)]
        public string Text;

        [WProtoMember(2)]
        public byte[] Data;

        [WProtoMember(3)]
        public Uri Address;

        [WProtoMember(4)]
        public WProtoDecodedPayloadContract Child;

        [WProtoMember(5)]
        public List<string> Texts;

        [WProtoMember(6)]
        public Dictionary<string, byte[]> Values;

        [WProtoMember(7)]
        public List<Uri> Addresses;
    }
}
