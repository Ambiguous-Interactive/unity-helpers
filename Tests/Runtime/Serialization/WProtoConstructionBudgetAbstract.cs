// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    [WProtoInclude(50, typeof(WProtoConstructionBudgetConcrete))]
    public abstract partial class WProtoConstructionBudgetAbstract
    {
        [WProtoMember(1)]
        public int Value;
    }
}
