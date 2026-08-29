// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Left half of the display-name comparison: buttons that fall back to their method names.
    /// </summary>
    public sealed class MethodNameButtons : MonoBehaviour
    {
        [WButton]
        private void SpawnEnemy() { }

        [WButton]
        private void ResetPlayer() { }

        [WButton]
        private void ClearSaveData() { }
    }
}
