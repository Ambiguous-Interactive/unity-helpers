// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Repeated members with explicit write packing policies.</summary>
    [WProtoContract]
    public sealed partial class WProtoUnpackedRepeatedContract
    {
        /// <summary>The Ints run.</summary>
        [WProtoMember(1, IsPacked = false)]
        public int[] Ints;

        /// <summary>The IntList run.</summary>
        [WProtoMember(2, IsPacked = false)]
        public List<int> IntList;

        /// <summary>The Modes run.</summary>
        [WProtoMember(3, IsPacked = false)]
        public WProtoRepeatedMode[] Modes;

        /// <summary>The ModeList run.</summary>
        [WProtoMember(4, IsPacked = false)]
        public List<WProtoRepeatedMode> ModeList;

        /// <summary>The Doubles run.</summary>
        [WProtoMember(5, IsPacked = false)]
        public double[] Doubles;

        /// <summary>The Flags run.</summary>
        [WProtoMember(6, IsPacked = false)]
        public bool[] Flags;

        /// <summary>The Texts run.</summary>
        [WProtoMember(7, IsPacked = false)]
        public string[] Texts;

        /// <summary>The PackedInts run.</summary>
        [WProtoMember(8, IsPacked = true)]
        public int[] PackedInts;

        /// <summary>The Chars run.</summary>
        [WProtoMember(9, IsPacked = false)]
        public char[] Chars;
    }
}
