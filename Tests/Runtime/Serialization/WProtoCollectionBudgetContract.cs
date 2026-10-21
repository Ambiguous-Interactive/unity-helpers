// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public sealed partial class WProtoCollectionBudgetContract
    {
        [WProtoMember(1)]
        public List<int> Numbers;

        [WProtoMember(2)]
        public List<string> Texts;

        [WProtoMember(3)]
        public Dictionary<string, WProtoCollectionBudgetContract> Values;

        [WProtoMember(4)]
        public List<WProtoCollectionBudgetContract> Children;

        [WProtoMember(5)]
        public int[,] Grid;

        [WProtoMember(6)]
        public List<List<int>> Groups;

        [WProtoMember(7)]
        public HashSet<int> Unique;

        [WProtoMember(8)]
        public byte[] Data;

        [WProtoMember(9)]
        public string Text;

        [WProtoMember(10)]
        public WProtoCollectionBudgetContract Child;
    }
}
