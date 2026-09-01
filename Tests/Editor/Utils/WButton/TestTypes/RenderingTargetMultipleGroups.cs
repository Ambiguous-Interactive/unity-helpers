// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class RenderingTargetMultipleGroups : ScriptableObject
    {
        [WButton(groupName: "Group1")]
        public void Group1Button() { }

        [WButton(groupName: "Group2")]
        public void Group2Button() { }

        [WButton(groupName: "Group3")]
        public void Group3Button() { }
    }
}
#endif
