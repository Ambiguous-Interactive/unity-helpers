// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class IntegrationTargetNestedGroups : ScriptableObject
    {
        [WGroup("Outer")]
        public int outerValue;

        [WButton(groupName: "Outer")]
        public void OuterButton() { }

        [WButton(groupName: "Inner")]
        public void InnerButton() { }
    }
}
#endif
