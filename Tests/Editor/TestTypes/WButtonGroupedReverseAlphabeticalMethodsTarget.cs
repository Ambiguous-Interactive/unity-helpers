// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target with grouped buttons where methods within each group are declared
    /// in reverse alphabetical order. Verifies declaration order is preserved within groups.
    /// </summary>
    public sealed class WButtonGroupedReverseAlphabeticalMethodsTarget : ScriptableObject
    {
        [WButton("Z in Group A", drawOrder: 0, groupName: "Group A")]
        public void GroupAZMethod() { }

        [WButton("M in Group A", drawOrder: 0, groupName: "Group A")]
        public void GroupAMMethod() { }

        [WButton("A in Group A", drawOrder: 0, groupName: "Group A")]
        public void GroupAAMethod() { }

        [WButton("Third in Group B", drawOrder: 0, groupName: "Group B")]
        public void GroupBThird() { }

        [WButton("Second in Group B", drawOrder: 0, groupName: "Group B")]
        public void GroupBSecond() { }

        [WButton("First in Group B", drawOrder: 0, groupName: "Group B")]
        public void GroupBFirst() { }
    }
}
