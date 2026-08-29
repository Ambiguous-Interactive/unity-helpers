// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the <c>drawOrder</c> parameter section: three buttons whose
    /// declaration order differs from their render order.
    /// </summary>
    public sealed class PlayerController : MonoBehaviour
    {
        public int health = 100;
        public float speed = 5f;

        [WButton("Initialize", drawOrder: -1)]
        private void Initialize() { }

        [WButton("Validate", drawOrder: 0)]
        private void Validate() { }

        [WButton("Debug Info", drawOrder: 1)]
        private void ShowDebugInfo() { }
    }
}
