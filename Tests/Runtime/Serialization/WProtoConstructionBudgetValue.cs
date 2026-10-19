// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public partial struct WProtoConstructionBudgetValue : IEquatable<WProtoConstructionBudgetValue>
    {
        [WProtoMember(1)]
        public int Value;

        public bool Equals(WProtoConstructionBudgetValue other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object other)
        {
            return other is WProtoConstructionBudgetValue value && Equals(value);
        }

        public override int GetHashCode()
        {
            return Value;
        }
    }
}
