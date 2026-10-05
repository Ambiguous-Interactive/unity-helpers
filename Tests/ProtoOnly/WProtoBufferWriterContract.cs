// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public sealed partial class WProtoBufferWriterContract
    {
        [WProtoMember(1)]
        public int Number;

        [WProtoMember(2)]
        public byte[] Data;

        public bool FailSerialization;

        [WProtoBeforeSerialization]
        public void BeforeSerialization()
        {
            if (FailSerialization)
            {
                throw new InvalidOperationException("Serialization callback failed.");
            }
        }
    }
}
