// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Settings
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Serialization;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;
    using WallstopStudios.UnityHelpers.Settings;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.Settings.UnityHelpersSettings;
    using static WallstopStudios.UnityHelpers.Editor.Settings.UnityHelpersSettings.SerializedPropertyNames;

    /// <summary>Provides test-side setup and inspection for UnityHelpersSettings.</summary>
    internal static class UnityHelpersSettingsTestAccess
    {
        internal static void SetWGroupAutoIncludeConfiguration(
            WGroupAutoIncludeMode mode,
            int rowCount
        )
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings._wgroupAutoIncludeMode = mode;
            settings._wgroupAutoIncludeRowCount = Mathf.Clamp(
                rowCount,
                MinWGroupAutoIncludeRowCount,
                MaxWGroupAutoIncludeRowCount
            );
            settings.SaveSettings();
        }

        /// <summary>
        /// Clears the cached SerializedObject for testing purposes.
        /// </summary>
        internal static void ClearCachedSerializedObject()
        {
            _cachedSettingsSerializedObject?.Dispose();
            _cachedSettingsSerializedObject = null;
        }

        /// <summary>
        /// Gets the serialized property name value for the given constant name.
        /// Resolves fixture data names to the actual serialized property constants.
        /// </summary>
        internal static string GetPropertyNameValue(string constantName)
        {
            return constantName switch
            {
                nameof(SerializableTypeIgnorePatterns) => SerializableTypeIgnorePatterns,
                nameof(SerializableTypePatternsInitialized) => SerializableTypePatternsInitialized,
                nameof(SerializableTypePattern) => SerializableTypePattern,
                nameof(LegacyWButtonPriorityColors) => LegacyWButtonPriorityColors,
                nameof(WButtonCustomColors) => WButtonCustomColors,
                nameof(WGroupFoldoutsStartCollapsed) => WGroupFoldoutsStartCollapsed,
                nameof(WGroupFoldoutTweenEnabled) => WGroupFoldoutTweenEnabled,
                nameof(WGroupFoldoutSpeed) => WGroupFoldoutSpeed,
                nameof(WEnumToggleButtonsCustomColors) => WEnumToggleButtonsCustomColors,
                nameof(UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutBehavior) =>
                    UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutBehavior,
                nameof(InlineEditorFoldoutTweenEnabled) => InlineEditorFoldoutTweenEnabled,
                nameof(InlineEditorFoldoutSpeed) => InlineEditorFoldoutSpeed,
                nameof(WButtonFoldoutTweenEnabled) => WButtonFoldoutTweenEnabled,
                nameof(SerializableDictionaryFoldoutTweenEnabled) =>
                    SerializableDictionaryFoldoutTweenEnabled,
                nameof(SerializableSortedDictionaryFoldoutTweenEnabled) =>
                    SerializableSortedDictionaryFoldoutTweenEnabled,
                nameof(SerializableSetFoldoutTweenEnabled) => SerializableSetFoldoutTweenEnabled,
                nameof(SerializableSortedSetFoldoutTweenEnabled) =>
                    SerializableSortedSetFoldoutTweenEnabled,
                nameof(FoldoutTweenSettingsInitialized) => FoldoutTweenSettingsInitialized,
                nameof(SerializableDictionaryFoldoutSpeed) => SerializableDictionaryFoldoutSpeed,
                nameof(SerializableSortedDictionaryFoldoutSpeed) =>
                    SerializableSortedDictionaryFoldoutSpeed,
                nameof(SerializableSetFoldoutSpeed) => SerializableSetFoldoutSpeed,
                nameof(SerializableSortedSetFoldoutSpeed) => SerializableSortedSetFoldoutSpeed,
                nameof(DetectAssetChangeLoopWindowSeconds) => DetectAssetChangeLoopWindowSeconds,
                nameof(DeferAssetPostprocessorCallbacks) => DeferAssetPostprocessorCallbacks,
                nameof(WButtonPriority) => WButtonPriority,
                nameof(WButtonCustomColorButton) => WButtonCustomColorButton,
                nameof(WButtonCustomColorText) => WButtonCustomColorText,
                nameof(WButtonCustomColorHasText) => WButtonCustomColorHasText,
                nameof(WEnumToggleButtonsSelectedBackground) =>
                    WEnumToggleButtonsSelectedBackground,
                nameof(WEnumToggleButtonsSelectedText) => WEnumToggleButtonsSelectedText,
                nameof(WEnumToggleButtonsInactiveBackground) =>
                    WEnumToggleButtonsInactiveBackground,
                nameof(WEnumToggleButtonsInactiveText) => WEnumToggleButtonsInactiveText,
                nameof(WEnumToggleButtonsHasSelectedText) => WEnumToggleButtonsHasSelectedText,
                nameof(WEnumToggleButtonsHasInactiveText) => WEnumToggleButtonsHasInactiveText,
                nameof(FailedTestsOutputDirectory) => FailedTestsOutputDirectory,
                _ => null,
            };
        }
    }
#endif
}
