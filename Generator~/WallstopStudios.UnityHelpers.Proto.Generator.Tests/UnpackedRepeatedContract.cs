// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>Repeated members with explicit write packing policies.</summary>
    [ProtoContract]
    [WProtoContract]
    public sealed partial class UnpackedRepeatedContract
    {
        /// <summary>The Ints run.</summary>
        [ProtoMember(1, IsPacked = false)]
        [WProtoMember(1, IsPacked = false)]
        public int[] Ints;

        /// <summary>The IntList run.</summary>
        [ProtoMember(2, IsPacked = false)]
        [WProtoMember(2, IsPacked = false)]
        public List<int> IntList;

        /// <summary>The Modes run.</summary>
        [ProtoMember(3, IsPacked = false)]
        [WProtoMember(3, IsPacked = false)]
        public Mode[] Modes;

        /// <summary>The ModeList run.</summary>
        [ProtoMember(4, IsPacked = false)]
        [WProtoMember(4, IsPacked = false)]
        public List<Mode> ModeList;

        /// <summary>The Doubles run.</summary>
        [ProtoMember(5, IsPacked = false)]
        [WProtoMember(5, IsPacked = false)]
        public double[] Doubles;

        /// <summary>The Flags run.</summary>
        [ProtoMember(6, IsPacked = false)]
        [WProtoMember(6, IsPacked = false)]
        public bool[] Flags;

        /// <summary>The Texts run.</summary>
        [ProtoMember(7, IsPacked = false)]
        [WProtoMember(7, IsPacked = false)]
        public string[] Texts;

        /// <summary>The PackedInts run.</summary>
        [ProtoMember(8, IsPacked = true)]
        [WProtoMember(8, IsPacked = true)]
        public int[] PackedInts;

        /// <summary>The Chars run.</summary>
        [ProtoMember(9, IsPacked = false)]
        [WProtoMember(9, IsPacked = false)]
        public char[] Chars;
    }
}
