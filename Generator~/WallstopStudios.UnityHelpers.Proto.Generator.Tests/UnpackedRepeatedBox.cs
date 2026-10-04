// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>A generic repeated contract with unpacked and default write policies.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class UnpackedRepeatedBox<T>
    {
        /// <summary>The Array run.</summary>
        [ProtoMember(1, IsPacked = false)]
        [WProtoMember(1, IsPacked = false)]
        public T[] Array;

        /// <summary>The List run.</summary>
        [ProtoMember(2, IsPacked = false)]
        [WProtoMember(2, IsPacked = false)]
        public List<T> List;

        /// <summary>The DefaultPacked run.</summary>
        [ProtoMember(3, IsPacked = true)]
        [WProtoMember(3)]
        public T[] DefaultPacked;
    }
}
