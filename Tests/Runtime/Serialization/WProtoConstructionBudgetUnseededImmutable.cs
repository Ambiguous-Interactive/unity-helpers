// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract(SkipConstructor = true)]
    public sealed partial class WProtoConstructionBudgetUnseededImmutable
    {
        [WProtoMember(1)]
        public readonly int Value = WProtoConstructionBudgetEvents.Initialize();

        public WProtoConstructionBudgetUnseededImmutable()
        {
            ++WProtoConstructionBudgetEvents.Constructors;
        }
    }
}
