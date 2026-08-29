// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the <c>groupPriority</c> parameter section: Primary renders
    /// first, Debug second, and the group without an explicit priority last.
    /// </summary>
    public sealed class ActionPanel : MonoBehaviour
    {
        [WButton("Quick Save", groupName: "Primary", groupPriority: 0)]
        private void QuickSave() { }

        [WButton("Quick Load", groupName: "Primary", groupPriority: 0)]
        private void QuickLoad() { }

        [WButton("Debug Info", groupName: "Debug", groupPriority: 10)]
        private void ShowDebugInfo() { }

        [WButton("Reset", groupName: "Misc")]
        private void ResetPanel() { }
    }
}
