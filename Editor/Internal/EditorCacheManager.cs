// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Internal
{
#if UNITY_EDITOR
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Editor.Extensions;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;

    [InitializeOnLoad]
    internal static class EditorCacheManager
    {
        static EditorCacheManager()
        {
            // Defer beyond initialization, with a fallback because an idle editor may not pump delayCall.
            EditorStartupCallback.RunOnce(ClearAllCaches);
            AssemblyReloadEvents.beforeAssemblyReload += ClearAllCaches;
            EditorApplication.quitting += ClearAllCaches;
        }

        /// <summary>Releases editor caches before domain reload and quit, and after startup.</summary>
        internal static void ClearAllCaches()
        {
            SolidButtonStyles.ClearCache();
            EditorCacheHelper.ClearAllCaches();
            WGroupLayoutBuilder.ClearCache();
            WButtonGUI.ClearContextCache();
            SerializedPropertyExtensions.ClearCache();
            WEnumToggleButtonsDrawer.ClearCache();
            WShowIfPropertyDrawer.ClearCache();
            InLineEditorShared.ClearCache();
#if WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
            WEnumToggleButtonsOdinDrawer.ClearCache();
            WShowIfOdinDrawer.ClearCache();
            IntDropDownOdinDrawer.ClearCache();
#endif
        }
    }
#endif
}
