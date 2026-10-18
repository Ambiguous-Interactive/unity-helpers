// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    internal sealed partial class ImmutableHookTimingContract
    {
        [WProtoMember(1)]
        internal readonly int Value = 7;

        [WProtoMember(2)]
        internal int Marker = 3;

        [WProtoMember(3)]
        internal readonly List<int> Values = new List<int> { 5 };

        internal int BeforeValue;
        internal int BeforeMarker;
        internal List<int> BeforeValues;
        internal int[] BeforeValuesSnapshot;
        internal int AfterMarker;
        internal int BeforeCalls;
        internal int AfterCalls;
        internal int HookOrder;

        [WProtoBeforeDeserialization]
        private void BeforeDeserialization()
        {
            ++BeforeCalls;
            HookOrder = HookOrder * 10 + 1;
            BeforeValue = Value;
            BeforeMarker = Marker;
            BeforeValues = Values;
            BeforeValuesSnapshot = Values.ToArray();
            Marker = 99;
        }

        [WProtoAfterDeserialization]
        private void AfterDeserialization()
        {
            ++AfterCalls;
            HookOrder = HookOrder * 10 + 2;
            AfterMarker = Marker;
        }
    }
}
