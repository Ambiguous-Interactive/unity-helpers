// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR && UNITY_6000_3_OR_NEWER
    using UnityEditor.Toolbars;

    internal static class ValidationMainToolbar
    {
        [MainToolbarElement(
            "Sentinel/Validation",
            defaultDockPosition = MainToolbarDockPosition.Right
        )]
        private static MainToolbarElement CreateBadge()
        {
            return new MainToolbarButton(
                new MainToolbarContent(ValidationStatusSurfaces.Badge, "Open validation issues"),
                ValidationWindow.Open
            );
        }
    }
#endif
}
