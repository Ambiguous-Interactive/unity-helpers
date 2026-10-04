// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>A generic repeated contract with unpacked and default write policies.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    [WProtoContract]
    public sealed partial class WProtoUnpackedRepeatedBox<T>
    {
        /// <summary>The Array run.</summary>
        [WProtoMember(1, IsPacked = false)]
        public T[] Array;

        /// <summary>The List run.</summary>
        [WProtoMember(2, IsPacked = false)]
        public List<T> List;

        /// <summary>The DefaultPacked run.</summary>
        [WProtoMember(3)]
        public T[] DefaultPacked;
    }
}
