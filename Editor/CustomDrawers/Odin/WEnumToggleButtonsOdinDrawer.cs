// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Sirenix.OdinInspector.Editor;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;
    using EnumShared = WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils.EnumToggleButtonsShared;
    using CacheHelper = WallstopStudios.UnityHelpers.Editor.Core.Helper.EditorCacheHelper;

    /// <summary>
    /// Odin Inspector attribute drawer for <see cref="WEnumToggleButtonsAttribute"/>.
    /// Renders enum fields as horizontal toggle buttons instead of a dropdown.
    /// </summary>
    /// <remarks>
    /// This drawer ensures WEnumToggleButtons works correctly when Odin Inspector is installed
    /// and classes derive from SerializedMonoBehaviour or SerializedScriptableObject,
    /// where Unity's standard PropertyDrawer system is bypassed.
    /// </remarks>
    public sealed class WEnumToggleButtonsOdinDrawer
        : OdinAttributeDrawer<WEnumToggleButtonsAttribute>
    {
        private const float VerticalPadding = 5f;

        private static readonly GUIContent SelectAllContent = new("All");
        private static readonly GUIContent SelectNoneContent = new("None");
        private static readonly GUIContent OutOfViewContent = new();

        private static readonly Dictionary<Type, EnumShared.ToggleOption[]> EnumOptionsCache =
            new();
        private static readonly Dictionary<string, EnumShared.PaginationState> PaginationStates =
            new(StringComparer.Ordinal);

        /// <summary>
        /// Clears all cached state. Called during domain reload to prevent stale references.
        /// </summary>
        /// <remarks>
        /// This method is called by <see cref="Internal.EditorCacheManager.ClearAllCaches"/>
        /// when the Unity domain reloads (after script compilation, entering/exiting play mode, etc.).
        /// </remarks>
        internal static void ClearCache()
        {
            EnumOptionsCache.Clear();
            PaginationStates.Clear();
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            WEnumToggleButtonsAttribute toggleAttribute = Attribute;
            if (toggleAttribute == null)
            {
                CallNextDrawer(label);
                return;
            }

            if (Property == null || Property.ValueEntry == null)
            {
                CallNextDrawer(label);
                return;
            }

            Type valueType = Property.ValueEntry.TypeOfValue;
            if (valueType == null || !valueType.IsEnum)
            {
                CallNextDrawer(label);
                return;
            }

            EnumShared.ToggleOption[] options = GetCachedEnumOptions(valueType);
            if (options == null || options.Length == 0)
            {
                CallNextDrawer(label);
                return;
            }

            bool isFlags = ReflectionHelpers.HasAttributeSafe<FlagsAttribute>(
                valueType,
                inherit: true
            );

            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry palette =
                UnityHelpersSettings.ResolveWEnumToggleButtonsPalette(toggleAttribute.ColorKey);

            bool usePagination = ShouldPaginate(toggleAttribute, options.Length, out int pageSize);
            int startIndex = 0;
            int visibleCount = options.Length;
            EnumShared.PaginationState paginationState = null;

            if (usePagination)
            {
                string stateKey = BuildPaginationKey();
                paginationState = GetOrCreatePaginationState(stateKey, options.Length, pageSize);
                startIndex = paginationState.StartIndex;
                visibleCount = paginationState.VisibleCount;
            }

            if (visibleCount <= 0)
            {
                CallNextDrawer(label);
                return;
            }

            object currentValue = Property.ValueEntry.WeakSmartValue;
            ulong currentMask = EnumShared.ConvertToUInt64(currentValue);

            EnumShared.SelectionSummary summary = BuildSelectionSummary(
                options,
                currentMask,
                isFlags,
                startIndex,
                visibleCount,
                usePagination
            );

            bool showToolbarControls =
                isFlags && (toggleAttribute.ShowSelectAll || toggleAttribute.ShowSelectNone);

            Rect totalRect = EditorGUILayout.GetControlRect(
                true,
                CalculateTotalHeight(
                    visibleCount,
                    toggleAttribute.ButtonsPerRow,
                    showToolbarControls,
                    usePagination,
                    summary.HasSummary,
                    EditorGUIUtility.currentViewWidth - EditorGUIUtility.labelWidth - 20f
                )
            );

            Rect labelRect = new(
                totalRect.x,
                totalRect.y,
                EditorGUIUtility.labelWidth,
                EditorGUIUtility.singleLineHeight
            );
            if (label != null && label != GUIContent.none)
            {
                EditorGUI.LabelField(labelRect, label);
            }

            Rect contentRect = new(
                totalRect.x + EditorGUIUtility.labelWidth,
                totalRect.y,
                totalRect.width - EditorGUIUtility.labelWidth,
                totalRect.height
            );

            float currentY = contentRect.y + VerticalPadding;

            if (showToolbarControls)
            {
                Rect toolbarRect = new(
                    contentRect.x,
                    currentY,
                    contentRect.width,
                    EditorGUIUtility.singleLineHeight
                );
                DrawToolbar(toolbarRect, options, currentMask, toggleAttribute, palette, isFlags);
                currentY += toolbarRect.height + EnumShared.ToolbarSpacing;
            }

            if (usePagination && paginationState != null)
            {
                Rect paginationRect = new(
                    contentRect.x,
                    currentY,
                    contentRect.width,
                    EditorGUIUtility.singleLineHeight
                );
                DrawPagination(paginationRect, paginationState);
                currentY += paginationRect.height + EnumShared.ToolbarSpacing;
            }

            if (summary.HasSummary)
            {
                float summaryHeight = EnumShared.SummaryStyle.CalcHeight(
                    summary.Content,
                    contentRect.width
                );
                Rect summaryRect = new(contentRect.x, currentY, contentRect.width, summaryHeight);
                EditorGUI.LabelField(summaryRect, summary.Content, EnumShared.SummaryStyle);
                currentY += summaryHeight + EnumShared.SummarySpacing;
            }

            EnumShared.LayoutMetrics metrics = EnumShared.CalculateLayout(
                toggleAttribute.ButtonsPerRow,
                visibleCount,
                contentRect.width,
                EditorGUIUtility.singleLineHeight,
                EnumShared.ToolbarSpacing,
                EnumShared.MinButtonWidth
            );

            Rect buttonsRect = new(contentRect.x, currentY, contentRect.width, metrics.TotalHeight);

            for (int index = 0; index < visibleCount; index += 1)
            {
                EnumShared.ToggleOption option = options[startIndex + index];
                Rect buttonRect = metrics.GetItemRect(buttonsRect, index);
                DrawToggle(
                    buttonRect,
                    option,
                    currentMask,
                    isFlags,
                    metrics,
                    index,
                    visibleCount,
                    palette
                );
            }
        }

        private float CalculateTotalHeight(
            int visibleCount,
            int buttonsPerRow,
            bool showToolbarControls,
            bool usePagination,
            bool hasSummary,
            float availableWidth
        )
        {
            float extraHeight = VerticalPadding * 2f;

            if (showToolbarControls)
            {
                extraHeight += EditorGUIUtility.singleLineHeight + EnumShared.ToolbarSpacing;
            }

            if (usePagination)
            {
                extraHeight += EditorGUIUtility.singleLineHeight + EnumShared.ToolbarSpacing;
            }

            if (hasSummary)
            {
                float summaryHeight = EnumShared.SummaryStyle.CalcHeight(
                    OutOfViewContent,
                    availableWidth
                );
                extraHeight += summaryHeight + EnumShared.SummarySpacing;
            }

            EnumShared.LayoutMetrics metrics = EnumShared.CalculateLayout(
                buttonsPerRow,
                visibleCount,
                availableWidth,
                EditorGUIUtility.singleLineHeight,
                EnumShared.ToolbarSpacing,
                EnumShared.MinButtonWidth
            );

            return extraHeight + metrics.TotalHeight;
        }

        private void DrawToolbar(
            Rect rect,
            EnumShared.ToggleOption[] options,
            ulong currentMask,
            WEnumToggleButtonsAttribute toggleAttribute,
            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry palette,
            bool isFlags
        )
        {
            if (!isFlags)
            {
                return;
            }

            bool drawSelectAll = toggleAttribute.ShowSelectAll;
            bool drawSelectNone = toggleAttribute.ShowSelectNone;

            if (!drawSelectAll && !drawSelectNone)
            {
                return;
            }

            ulong allFlagsMask = CalculateAllFlagsMask(options);
            bool allActive = allFlagsMask != 0UL && (currentMask & allFlagsMask) == allFlagsMask;
            bool noneActive = currentMask == 0UL;

            bool alignedPair = drawSelectAll && drawSelectNone;

            if (alignedPair)
            {
                float availableWidth = rect.width - EnumShared.ToolbarButtonGap;
                float buttonWidth = Mathf.Max(
                    EnumShared.ToolbarButtonMinWidth,
                    Mathf.Floor(availableWidth * EnumShared.EqualSplitRatio)
                );

                Rect selectAllRect = new(rect.x, rect.y, buttonWidth, rect.height);
                GUIStyle allStyle = EnumShared.GetButtonStyle(
                    EnumShared.ButtonSegment.Single,
                    allActive,
                    palette
                );
                bool selectAllPressed = GUI.Toggle(
                    selectAllRect,
                    allActive,
                    SelectAllContent,
                    allStyle
                );

                if (selectAllPressed && !allActive)
                {
                    ApplyEnumValue(allFlagsMask);
                }

                Rect selectNoneRect = new(
                    selectAllRect.xMax + EnumShared.ToolbarButtonGap,
                    rect.y,
                    rect.width - buttonWidth - EnumShared.ToolbarButtonGap,
                    rect.height
                );
                GUIStyle noneStyle = EnumShared.GetButtonStyle(
                    EnumShared.ButtonSegment.Single,
                    noneActive,
                    palette
                );
                bool selectNonePressed = GUI.Toggle(
                    selectNoneRect,
                    noneActive,
                    SelectNoneContent,
                    noneStyle
                );

                if (selectNonePressed && !noneActive)
                {
                    ApplyEnumValue(0UL);
                }
            }
            else if (drawSelectAll)
            {
                GUIStyle style = EnumShared.GetButtonStyle(
                    EnumShared.ButtonSegment.Single,
                    allActive,
                    palette
                );
                bool selectAllPressed = GUI.Toggle(rect, allActive, SelectAllContent, style);

                if (selectAllPressed && !allActive)
                {
                    ApplyEnumValue(allFlagsMask);
                }
            }
            else if (drawSelectNone)
            {
                GUIStyle style = EnumShared.GetButtonStyle(
                    EnumShared.ButtonSegment.Single,
                    noneActive,
                    palette
                );
                bool selectNonePressed = GUI.Toggle(rect, noneActive, SelectNoneContent, style);

                if (selectNonePressed && !noneActive)
                {
                    ApplyEnumValue(0UL);
                }
            }
        }

        private static void DrawPagination(Rect rect, EnumShared.PaginationState state)
        {
            if (state.TotalPages <= 1)
            {
                return;
            }

            float spacing = EnumShared.ToolbarSpacing;
            float buttonWidth = Mathf.Min(
                EnumShared.PaginationButtonWidth,
                rect.width * EnumShared.MaxPaginationButtonWidthRatio
            );
            float labelWidth = Mathf.Max(
                EnumShared.PaginationLabelMinWidth,
                rect.width - (buttonWidth * 4f) - spacing * 4f
            );

            Rect firstRect = new(rect.x, rect.y, buttonWidth, rect.height);
            Rect prevRect = new(firstRect.xMax + spacing, rect.y, buttonWidth, rect.height);
            Rect labelRect = new(prevRect.xMax + spacing, rect.y, labelWidth, rect.height);
            Rect nextRect = new(labelRect.xMax + spacing, rect.y, buttonWidth, rect.height);
            Rect lastRect = new(nextRect.xMax + spacing, rect.y, buttonWidth, rect.height);

            if (lastRect.xMax > rect.xMax)
            {
                float overflow = lastRect.xMax - rect.xMax;
                firstRect.x -= overflow * EnumShared.OverflowCenteringRatio;
                prevRect.x -= overflow * EnumShared.OverflowCenteringRatio;
                labelRect.x -= overflow * EnumShared.OverflowCenteringRatio;
                nextRect.x -= overflow * EnumShared.OverflowCenteringRatio;
                lastRect.x -= overflow * EnumShared.OverflowCenteringRatio;
            }

            bool originalEnabled = GUI.enabled;
            bool canNavigateBackward = state.PageIndex > 0;
            bool canNavigateForward = state.PageIndex < state.TotalPages - 1;

            GUI.enabled = originalEnabled && canNavigateBackward;
            if (GUI.Button(firstRect, EnumShared.FirstPageContent, EditorStyles.miniButtonLeft))
            {
                state.PageIndex = 0;
            }

            if (GUI.Button(prevRect, EnumShared.PrevPageContent, EditorStyles.miniButtonMid))
            {
                state.PageIndex = Mathf.Max(0, state.PageIndex - 1);
            }
            GUI.enabled = originalEnabled;

            GUI.Label(
                labelRect,
                CacheHelper.GetPaginationLabel(state.PageIndex + 1, state.TotalPages),
                EditorStyles.miniLabel
            );

            GUI.enabled = originalEnabled && canNavigateForward;
            if (GUI.Button(nextRect, EnumShared.NextPageContent, EditorStyles.miniButtonMid))
            {
                state.PageIndex = Mathf.Min(state.TotalPages - 1, state.PageIndex + 1);
            }

            if (GUI.Button(lastRect, EnumShared.LastPageContent, EditorStyles.miniButtonRight))
            {
                state.PageIndex = state.TotalPages - 1;
            }

            GUI.enabled = originalEnabled;
        }

        private void DrawToggle(
            Rect rect,
            EnumShared.ToggleOption option,
            ulong currentMask,
            bool isFlags,
            EnumShared.LayoutMetrics metrics,
            int visibleIndex,
            int visibleCount,
            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry palette
        )
        {
            bool isActive = IsOptionActive(option, currentMask, isFlags);
            EnumShared.ButtonSegment segment = EnumShared.ResolveButtonSegment(
                visibleIndex,
                visibleCount,
                metrics.Columns
            );
            GUIStyle style = EnumShared.GetButtonStyle(segment, isActive, palette);
            bool newState = GUI.Toggle(rect, isActive, option.Label, style);

            if (newState == isActive)
            {
                return;
            }

            ApplyToggleChange(option, currentMask, isFlags, newState);
        }

        private static bool IsOptionActive(
            EnumShared.ToggleOption option,
            ulong currentMask,
            bool isFlags
        )
        {
            if (isFlags)
            {
                if (option.IsZeroFlag)
                {
                    return currentMask == 0UL;
                }
                return (currentMask & option.FlagValue) == option.FlagValue;
            }

            return currentMask == option.FlagValue;
        }

        private void ApplyToggleChange(
            EnumShared.ToggleOption option,
            ulong currentMask,
            bool isFlags,
            bool desiredState
        )
        {
            if (isFlags)
            {
                if (option.IsZeroFlag)
                {
                    if (desiredState && currentMask != 0UL)
                    {
                        ApplyEnumValue(0UL);
                    }
                    return;
                }

                ulong mask = option.FlagValue;
                ulong newMask;
                if (desiredState)
                {
                    newMask = currentMask | mask;
                }
                else
                {
                    newMask = currentMask & ~mask;
                }
                ApplyEnumValue(newMask);
            }
            else
            {
                ApplyEnumValue(option.FlagValue);
            }
        }

        private void ApplyEnumValue(ulong value)
        {
            Type enumType = Property.ValueEntry?.TypeOfValue;
            if (enumType == null || !enumType.IsEnum)
            {
                return;
            }

            object enumValue = Enum.ToObject(enumType, unchecked((long)value));
            Property.ValueEntry.WeakSmartValue = enumValue;
        }

        private string BuildPaginationKey()
        {
            if (Property == null)
            {
                return string.Empty;
            }

            object parent = Property.Parent?.ValueEntry?.WeakSmartValue;
            if (parent == null)
            {
                return Property.Path ?? string.Empty;
            }

            int instanceId = parent.GetHashCode();
            string instancePart = instanceId.ToString("X8", CultureInfo.InvariantCulture);
            string pathPart = Property.Path ?? string.Empty;
            return instancePart + ":" + pathPart;
        }

        private static EnumShared.PaginationState GetOrCreatePaginationState(
            string key,
            int totalItems,
            int pageSize
        )
        {
            if (!PaginationStates.TryGetValue(key, out EnumShared.PaginationState state))
            {
                state = new EnumShared.PaginationState();
                PaginationStates[key] = state;
            }

            state.PageSize = Mathf.Max(1, pageSize);
            state.TotalItems = Mathf.Max(0, totalItems);

            int totalPages = state.TotalPages;
            if (state.PageIndex >= totalPages)
            {
                state.PageIndex = totalPages - 1;
            }

            if (state.PageIndex < 0)
            {
                state.PageIndex = 0;
            }

            return state;
        }

        private static EnumShared.SelectionSummary BuildSelectionSummary(
            EnumShared.ToggleOption[] options,
            ulong currentMask,
            bool isFlags,
            int startIndex,
            int visibleCount,
            bool usePagination
        )
        {
            if (!usePagination || options == null || options.Length == 0)
            {
                return EnumShared.SelectionSummary.None;
            }

            int endIndex = startIndex + visibleCount;
            using PooledResource<List<string>> lease = Buffers<string>.GetList(
                4,
                out List<string> outOfView
            );

            for (int index = 0; index < options.Length; index += 1)
            {
                EnumShared.ToggleOption option = options[index];
                if (!IsOptionActive(option, currentMask, isFlags))
                {
                    continue;
                }

                if (index >= startIndex && index < endIndex)
                {
                    continue;
                }

                outOfView.Add(option.Label);
            }

            if (outOfView.Count == 0)
            {
                return EnumShared.SelectionSummary.None;
            }

            string joined = string.Join(", ", outOfView);
            string text = "Current (out of view): " + joined;
            OutOfViewContent.text = text;
            return new EnumShared.SelectionSummary(true, OutOfViewContent);
        }

        internal static ulong CalculateAllFlagsMask(EnumShared.ToggleOption[] options)
        {
            ulong mask = 0UL;
            for (int index = 0; index < options.Length; index += 1)
            {
                EnumShared.ToggleOption option = options[index];
                if (option.FlagValue != 0UL)
                {
                    mask |= option.FlagValue;
                }
            }
            return mask;
        }

        internal static EnumShared.ToggleOption[] GetCachedEnumOptions(Type enumType)
        {
            if (enumType == null || !enumType.IsEnum)
            {
                return null;
            }

            if (EnumOptionsCache.TryGetValue(enumType, out EnumShared.ToggleOption[] cached))
            {
                return cached;
            }

            bool isFlags = ReflectionHelpers.HasAttributeSafe<FlagsAttribute>(
                enumType,
                inherit: true
            );
            EnumShared.ToggleOption[] options = BuildEnumOptions(enumType, isFlags);
            EnumOptionsCache[enumType] = options;
            return options;
        }

        internal static EnumShared.ToggleOption[] BuildEnumOptions(Type enumType, bool isFlags)
        {
            Array values = Enum.GetValues(enumType);
            using PooledResource<List<EnumShared.ToggleOption>> optionsLease =
                Buffers<EnumShared.ToggleOption>.GetList(
                    values.Length,
                    out List<EnumShared.ToggleOption> options
                );

            for (int index = 0; index < values.Length; index += 1)
            {
                object value = values.GetValue(index);
                if (value == null)
                {
                    continue;
                }

                string name = Enum.GetName(enumType, value);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                ulong numericValue = EnumShared.ConvertToUInt64(value);
                if (isFlags && numericValue != 0UL && !EnumShared.IsPowerOfTwo(numericValue))
                {
                    Debug.LogWarning(
                        $"[{nameof(WEnumToggleButtonsOdinDrawer)}] Skipping composite flag value {name} "
                            + $"in {enumType.Name} (value: {numericValue})"
                    );
                    continue;
                }

                string label = ObjectNames.NicifyVariableName(name);
                EnumShared.ToggleOption option = new(
                    label,
                    value,
                    numericValue,
                    numericValue == 0UL
                );
                options.Add(option);
            }

            if (options.Count == 0)
            {
                return Array.Empty<EnumShared.ToggleOption>();
            }

            return options.ToArray();
        }

        internal static bool ShouldPaginate(
            WEnumToggleButtonsAttribute attribute,
            int optionCount,
            out int pageSize
        )
        {
            pageSize = ResolvePageSize(attribute);
            if (attribute is { EnablePagination: false })
            {
                return false;
            }

            return optionCount > pageSize;
        }

        internal static int ResolvePageSize(WEnumToggleButtonsAttribute attribute)
        {
            int overrideSize = attribute?.PageSize ?? 0;
            if (overrideSize > 0)
            {
                return Mathf.Clamp(
                    overrideSize,
                    UnityHelpersSettings.MinPageSize,
                    UnityHelpersSettings.MaxPageSize
                );
            }

            return Mathf.Clamp(
                UnityHelpersSettings.GetEnumToggleButtonsPageSize(),
                UnityHelpersSettings.MinPageSize,
                UnityHelpersSettings.MaxPageSize
            );
        }
    }
#endif
}
