// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SchemaConformance.Tests
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    internal sealed partial class InteropCollections
    {
        [WProtoMember(1)]
        public List<int> Packed;

        [WProtoMember(2, IsPacked = false)]
        public int[] Unpacked;

        [WProtoMember(3)]
        public int[][] Rows;

        [WProtoMember(4)]
        public int[,] Grid;

        [WProtoMember(5)]
        public Dictionary<string, int> Counts;

        [WProtoMember(6)]
        public Dictionary<Guid, int> ByGuid;

        [WProtoMember(7)]
        public byte[][] Blobs;
    }
}
