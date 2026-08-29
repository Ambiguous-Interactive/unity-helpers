// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the grouping example: two named groups, Combat and Persistence.
    /// </summary>
    public sealed class ButtonGrouping : MonoBehaviour
    {
        [WButton("Spawn Enemy", groupName: "Combat")]
        private void SpawnEnemy() { }

        [WButton("Clear Enemies", groupName: "Combat")]
        private void ClearEnemies() { }

        [WButton("Save Game", groupName: "Persistence")]
        private void SaveGame() { }

        [WButton("Load Game", groupName: "Persistence")]
        private void LoadGame() { }
    }
}
