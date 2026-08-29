// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Capture.Targets
{
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Documentation target for the draw-order example in the inspector button guide: buttons
    /// sorted by <c>drawOrder</c> within their placement section.
    /// </summary>
    public sealed class ButtonPositioning : MonoBehaviour
    {
        public int someField = 10;

        [WButton("Top Button", drawOrder: -1)]
        private void TopButton() { }

        [WButton("Middle Button", drawOrder: 0)]
        private void MiddleButton() { }

        [WButton("Bottom Button", drawOrder: 1)]
        private void BottomButton() { }
    }
}
