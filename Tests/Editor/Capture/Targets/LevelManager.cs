// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the complete example: every WButton parameter working together
    /// across a top section, the inspector properties, and a bottom section.
    /// </summary>
    public sealed class LevelManager : MonoBehaviour
    {
        public int currentLevel = 1;
        public bool debugMode;

        [WButton(
            "Initialize Level",
            groupName: "Setup",
            groupPriority: 0,
            groupPlacement: WButtonGroupPlacement.Top
        )]
        private void Initialize() { }

        [WButton("Validate Configuration", groupName: "Setup")]
        private void ValidateConfig() { }

        [WButton(
            "Roll Dice",
            historyCapacity: 10,
            groupName: "Debug",
            groupPriority: 1,
            groupPlacement: WButtonGroupPlacement.Top
        )]
        private int RollDice()
        {
            return 4;
        }

        [WButton("Spawn Test Enemy", colorKey: "Warning", groupName: "Debug")]
        private void SpawnTestEnemy() { }

        [WButton(
            "Start Level",
            colorKey: "Success",
            groupName: "Actions",
            groupPriority: 0,
            groupPlacement: WButtonGroupPlacement.Bottom
        )]
        private void StartLevel() { }

        [WButton("Pause Game", groupName: "Actions")]
        private void PauseGame() { }

        [WButton("Restart Level", colorKey: "Danger", groupName: "Actions")]
        private void RestartLevel() { }

        [WButton(
            "Clear Cache",
            historyCapacity: 1,
            groupName: "Maintenance",
            groupPriority: 10,
            groupPlacement: WButtonGroupPlacement.Bottom
        )]
        private string ClearCache()
        {
            return "Cache cleared";
        }
    }
}
