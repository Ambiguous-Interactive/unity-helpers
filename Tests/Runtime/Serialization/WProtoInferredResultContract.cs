// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Serialization
{
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>A contract closed only through factory return inference.</summary>
    /// <typeparam name="T">The inferred member type.</typeparam>
    [ProtoContract]
    [WProtoContract]
    public partial struct WProtoInferredResultContract<T>
    {
        /// <summary>The inferred payload.</summary>
        [ProtoMember(1)]
        [WProtoMember(1)]
        public T Value;
    }
}
