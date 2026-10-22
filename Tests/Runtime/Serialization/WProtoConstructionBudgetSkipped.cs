// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract(SkipConstructor = true)]
    public sealed partial class WProtoConstructionBudgetSkipped
    {
        [WProtoMember(1)]
        public int Value = WProtoConstructionBudgetEvents.Initialize();

        public WProtoConstructionBudgetSkipped()
        {
            ++WProtoConstructionBudgetEvents.Constructors;
        }

        [WProtoAfterDeserialization]
        private void AfterRead()
        {
            ++WProtoConstructionBudgetEvents.AfterReads;
        }
    }
}
