// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the <c>groupName</c> parameter section: a Debug Tools group at
    /// the top and a Save System group at the bottom.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public int currentLevel = 1;
        public bool debugMode;

        [WButton("Log State", groupName: "Debug Tools", groupPlacement: WButtonGroupPlacement.Top)]
        private void LogState() { }

        [WButton("Clear Console", groupName: "Debug Tools")]
        private void ClearConsole() { }

        [WButton(
            "Save Game",
            groupName: "Save System",
            groupPlacement: WButtonGroupPlacement.Bottom
        )]
        private void SaveGame() { }

        [WButton("Load Game", groupName: "Save System")]
        private void LoadGame() { }

        [WButton("Delete Save", groupName: "Save System")]
        private void DeleteSave() { }
    }
}
