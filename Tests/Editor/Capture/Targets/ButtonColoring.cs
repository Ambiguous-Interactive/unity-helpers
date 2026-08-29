// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the color theming example: two buttons using the built-in
    /// dark and light color keys.
    /// </summary>
    public sealed class ButtonColoring : MonoBehaviour
    {
        [WButton("Dangerous Action", colorKey: "Default-Dark")]
        private void DangerousAction() { }

        [WButton("Safe Action", colorKey: "Default-Light")]
        private void SafeAction() { }
    }
}
