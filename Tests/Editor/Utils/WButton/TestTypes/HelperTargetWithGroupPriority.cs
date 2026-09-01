// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    internal sealed class HelperTargetWithGroupPriority : ScriptableObject
    {
        [WButton(groupName: "HighPriority", groupPriority: 0)]
        public void HighPriorityButton() { }

        [WButton(groupName: "LowPriority", groupPriority: 100)]
        public void LowPriorityButton() { }
    }
}
