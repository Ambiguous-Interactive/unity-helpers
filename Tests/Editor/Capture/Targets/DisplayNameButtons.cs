// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Right half of the display-name comparison: the same methods with explicit display names.
    /// </summary>
    public sealed class DisplayNameButtons : MonoBehaviour
    {
        [WButton("Spawn Enemy")]
        private void SpawnEnemy() { }

        [WButton("Reset Player to Checkpoint")]
        private void ResetPlayer() { }

        [WButton("Clear All Save Data")]
        private void ClearSaveData() { }
    }
}
