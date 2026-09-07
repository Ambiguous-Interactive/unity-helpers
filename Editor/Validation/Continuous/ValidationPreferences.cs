// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    /// <summary>Controls whether Sentinel runs and displays UI on this workstation.</summary>
    public static class ValidationPreferences
    {
        /// <summary>The per-user preference controlling Sentinel editor integration.</summary>
        public const string EnabledPreferenceKey =
            "WallstopStudios.UnityHelpers.Validation.Enabled";

        internal const string SettingsPath = "Preferences/Unity Helpers/Sentinel";
        internal static event Action Changed;
        private static bool _enabled = EditorPrefs.GetBool(EnabledPreferenceKey, true);

        /// <summary>Enables Sentinel editor UI and automatic work; explicit headless validation remains available.</summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value)
                    return;
                _enabled = value;
                EditorPrefs.SetBool(EnabledPreferenceKey, value);
                if (!value)
                {
                    ValidationAutoRun.ClearPending();
                    ValidationScheduler.Stop();
                }
                Changed?.Invoke();
            }
        }

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.User)
            {
                label = "Sentinel",
                keywords = new HashSet<string> { "Sentinel", "Validation", "Enable", "Disable" },
                guiHandler = _ =>
                {
                    Enabled = EditorGUILayout.Toggle("Enable Sentinel", Enabled);
                    EditorGUILayout.HelpBox(
                        "Disabling closes Sentinel and hides its status UI, cancels scans, and stops automatic validation and build hooks. Explicit command-line validation remains available. Profiles and results are preserved.",
                        MessageType.Info
                    );
                },
            };
        }
    }
#endif
}
