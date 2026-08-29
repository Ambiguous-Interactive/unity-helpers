// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the <c>groupPlacement</c> parameter section: a Setup group
    /// pinned above the properties and a Maintenance group pinned below them.
    /// </summary>
    public sealed class MixedPlacementExample : MonoBehaviour
    {
        public int health = 100;
        public float speed = 5f;

        [WButton("Initialize", groupName: "Setup", groupPlacement: WButtonGroupPlacement.Top)]
        private void Initialize() { }

        [WButton("Validate", groupName: "Setup", groupPlacement: WButtonGroupPlacement.Top)]
        private void Validate() { }

        [WButton("Cleanup", groupName: "Maintenance", groupPlacement: WButtonGroupPlacement.Bottom)]
        private void Cleanup() { }

        [WButton(
            "Reset All",
            groupName: "Maintenance",
            groupPlacement: WButtonGroupPlacement.Bottom
        )]
        private void ResetAll() { }
    }
}
