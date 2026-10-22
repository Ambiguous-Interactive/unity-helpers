// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public sealed partial class WProtoConstructionBudgetImmutable
    {
        [WProtoMember(1)]
        public readonly int Value = WProtoConstructionBudgetEvents.Initialize();

        public WProtoConstructionBudgetImmutable()
        {
            ++WProtoConstructionBudgetEvents.Constructors;
        }

        [WProtoBeforeDeserialization]
        private void BeforeRead()
        {
            ++WProtoConstructionBudgetEvents.BeforeReads;
        }

        [WProtoAfterDeserialization]
        private void AfterRead()
        {
            ++WProtoConstructionBudgetEvents.AfterReads;
        }
    }
}
