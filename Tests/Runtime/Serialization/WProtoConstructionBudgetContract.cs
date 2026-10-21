// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    [WProtoContract]
    public sealed partial class WProtoConstructionBudgetContract
    {
        [WProtoMember(1)]
        public int Value
        {
            get => _value;
            set
            {
                ++WProtoConstructionBudgetEvents.Assignments;
                _value = value;
            }
        }

        [WProtoMember(2)]
        public WProtoConstructionBudgetContract Child;

        [WProtoMember(3)]
        public List<WProtoConstructionBudgetContract> Children;

        private int _value = WProtoConstructionBudgetEvents.Initialize();

        public WProtoConstructionBudgetContract()
        {
            ++WProtoConstructionBudgetEvents.Constructors;
            if (WProtoConstructionBudgetEvents.ThrowInConstructor)
            {
                throw new InvalidOperationException("Requested constructor failure.");
            }
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
