// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>A generic contract whose repeated members explicitly use unpacked writes.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class UnpackedRepeatedValues<T>
    {
        /// <summary>The Array run.</summary>
        [ProtoMember(1, IsPacked = false)]
        [WProtoMember(1, IsPacked = false)]
        public T[] Array;

        /// <summary>The List run.</summary>
        [ProtoMember(2, IsPacked = false)]
        [WProtoMember(2, IsPacked = false)]
        public List<T> List;
    }
}
