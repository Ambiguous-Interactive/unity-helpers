// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Editor.Persistence
{
    using System;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Visuals.UIToolkit;

    /// <summary>
    /// Manages automatic cleanup of scoped persistence for MultiFileSelectorElement.
    /// Startup behavior is controlled via EditorPrefs settings exposed in the window.
    /// </summary>
    [InitializeOnLoad]
    public static class MultiFileSelectorPersistenceManager
    {
        private const string PrefKeyAuto = "WallstopStudios.MultiFileSelector.cleanup.autoEnabled";
        private const string PrefKeyDays = "WallstopStudios.MultiFileSelector.cleanup.maxAgeDays";

        static MultiFileSelectorPersistenceManager()
        {
            // Defer EditorPrefs beyond initialization, with a fallback when an idle editor does not pump delayCall.
            EditorStartupCallback.RunOnce(() =>
            {
                if (IsAutoCleanupEnabled())
                {
                    RunCleanupNow();
                }
            });
        }

        public static bool IsAutoCleanupEnabled()
        {
            return EditorPrefs.GetBool(PrefKeyAuto, true);
        }

        public static void SetAutoCleanupEnabled(bool value)
        {
            EditorPrefs.SetBool(PrefKeyAuto, value);
        }

        public static int GetMaxAgeDays()
        {
            int days = EditorPrefs.GetInt(PrefKeyDays, 30);
            return days <= 0 ? 30 : days;
        }

        public static void SetMaxAgeDays(int days)
        {
            if (days <= 0)
            {
                days = 30;
            }
            EditorPrefs.SetInt(PrefKeyDays, days);
        }

        /// <summary>
        /// Runs cleanup using current settings.
        /// </summary>
        public static void RunCleanupNow()
        {
            int days = GetMaxAgeDays();
            MultiFileSelectorElement.CleanupStalePersistenceEntries(TimeSpan.FromDays(days));
        }
    }
}
#endif
