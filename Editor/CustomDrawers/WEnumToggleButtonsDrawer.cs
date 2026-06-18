// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Extensions;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Utils;
    using CacheHelper = WallstopStudios.UnityHelpers.Editor.Core.Helper.EditorCacheHelper;
    using EnumShared = WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils.EnumToggleButtonsShared;

    [CustomPropertyDrawer(typeof(WEnumToggleButtonsAttribute))]
    public sealed class WEnumToggleButtonsDrawer : PropertyDrawer
    {
        private const float ContentWidthPadding = 24f;
        private const float VerticalPadding = 5f;

        private static readonly GUIContent OutOfViewContent = new();

        internal static void ClearCache()
        {
            WEnumToggleButtonsUtility.ClearCache();
        }

        private static float EstimateContentWidth()
        {
            float viewWidth = 600f;
            try
            {
                viewWidth = Mathf.Max(0f, EditorGUIUtility.currentViewWidth);
            }
            catch
            {
                viewWidth = 600f;
            }
            Rect dummyRect = new Rect(0f, 0f, viewWidth, EditorGUIUtility.singleLineHeight);
            Rect indentedRect = EditorGUI.IndentedRect(dummyRect);

            float widthAfterPadding =
                indentedRect.width - GroupGUIWidthUtility.CurrentHorizontalPadding;
            if (widthAfterPadding < 0f || float.IsNaN(widthAfterPadding))
            {
                widthAfterPadding = 0f;
            }
            float estimatedWidth =
                widthAfterPadding - EditorGUIUtility.labelWidth - ContentWidthPadding;

            if (estimatedWidth <= 0f || float.IsNaN(estimatedWidth))
            {
                estimatedWidth = EnumShared.MinButtonWidth;
            }

            return estimatedWidth;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            WEnumToggleButtonsAttribute toggleAttribute = attribute as WEnumToggleButtonsAttribute;
            if (toggleAttribute == null)
            {
                return EditorGUI.GetPropertyHeight(property, label, true);
            }

            ToggleSet toggleSet = WEnumToggleButtonsUtility.CreateToggleSet(property, fieldInfo);
            if (toggleSet.IsEmpty)
            {
                return EditorGUI.GetPropertyHeight(property, label, true);
            }

            bool showToolbarControls =
                toggleSet.SupportsMultipleSelection
                && (toggleAttribute.ShowSelectAll || toggleAttribute.ShowSelectNone);

            float extraHeight = 0f;
            if (showToolbarControls)
            {
                extraHeight = EditorGUIUtility.singleLineHeight + EnumShared.ToolbarSpacing;
            }

            bool usePagination = WEnumToggleButtonsUtility.ShouldPaginate(
                toggleAttribute,
                toggleSet.Options.Count,
                out int pageSize
            );
            int startIndex = 0;
            int visibleCount = toggleSet.Options.Count;
            if (usePagination)
            {
                WEnumToggleButtonsPagination.PaginationState state =
                    WEnumToggleButtonsPagination.GetState(
                        property,
                        toggleSet.Options.Count,
                        pageSize
                    );
                startIndex = state.StartIndex;
                visibleCount = state.VisibleCount;
                extraHeight += EditorGUIUtility.singleLineHeight + EnumShared.ToolbarSpacing;
            }

            if (visibleCount <= 0)
            {
                return EditorGUI.GetPropertyHeight(property, label, true);
            }

            EnumShared.SelectionSummary summary = BuildSelectionSummary(
                toggleSet,
                property,
                startIndex,
                visibleCount,
                usePagination
            );

            float estimatedWidth = EstimateContentWidth();

            LayoutSignature signature = WEnumToggleButtonsLayoutCache.CreateSignature(
                toggleSet.Options.Count,
                visibleCount,
                toggleAttribute.ButtonsPerRow,
                toggleSet.SupportsMultipleSelection,
                toggleAttribute.ShowSelectAll,
                toggleAttribute.ShowSelectNone,
                usePagination,
                summary.HasSummary,
                estimatedWidth
            );

            if (
                WEnumToggleButtonsLayoutCache.TryGetHeight(
                    property,
                    signature,
                    out float cachedHeight
                )
            )
            {
                return cachedHeight;
            }

            LayoutMetrics metrics = WEnumToggleButtonsUtility.CalculateLayout(
                toggleAttribute.ButtonsPerRow,
                visibleCount,
                estimatedWidth,
                EditorGUIUtility.singleLineHeight,
                EnumShared.ToolbarSpacing,
                EnumShared.MinButtonWidth
            );

            if (summary.HasSummary)
            {
                float summaryHeight = EnumShared.SummaryStyle.CalcHeight(
                    summary.Content,
                    estimatedWidth
                );
                extraHeight += summaryHeight + EnumShared.SummarySpacing;
            }

            return extraHeight + metrics.TotalHeight + VerticalPadding * 2f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            WEnumToggleButtonsAttribute toggleAttribute = attribute as WEnumToggleButtonsAttribute;
            if (toggleAttribute == null)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            ToggleSet toggleSet = WEnumToggleButtonsUtility.CreateToggleSet(property, fieldInfo);
            if (toggleSet.IsEmpty)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry palette =
                UnityHelpersSettings.ResolveWEnumToggleButtonsPalette(toggleAttribute.ColorKey);

            bool usePagination = WEnumToggleButtonsUtility.ShouldPaginate(
                toggleAttribute,
                toggleSet.Options.Count,
                out int pageSize
            );
            int startIndex = 0;
            int visibleCount = toggleSet.Options.Count;
            WEnumToggleButtonsPagination.PaginationState paginationState = null;
            if (usePagination)
            {
                paginationState = WEnumToggleButtonsPagination.GetState(
                    property,
                    toggleSet.Options.Count,
                    pageSize
                );
                startIndex = paginationState.StartIndex;
                visibleCount = paginationState.VisibleCount;
            }
            EnumShared.SelectionSummary summary = BuildSelectionSummary(
                toggleSet,
                property,
                startIndex,
                visibleCount,
                usePagination
            );

            EditorGUI.BeginProperty(position, label, property);

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            bool labelTemporarilyEnabled = usePagination && !GUI.enabled;
            bool previousLabelState = GUI.enabled;
            if (labelTemporarilyEnabled)
            {
                GUI.enabled = true;
            }
            Rect contentRect = EditorGUI.PrefixLabel(position, controlId, label);
            if (labelTemporarilyEnabled)
            {
                GUI.enabled = previousLabelState;
            }

            if (contentRect.width <= 0f || visibleCount <= 0)
            {
                EditorGUI.PropertyField(position, property, label, true);
                EditorGUI.EndProperty();
                return;
            }

            LayoutSignature signature = WEnumToggleButtonsLayoutCache.CreateSignature(
                toggleSet.Options.Count,
                visibleCount,
                toggleAttribute.ButtonsPerRow,
                toggleSet.SupportsMultipleSelection,
                toggleAttribute.ShowSelectAll,
                toggleAttribute.ShowSelectNone,
                usePagination,
                summary.HasSummary,
                contentRect.width
            );

            bool showToolbarControls =
                toggleSet.SupportsMultipleSelection
                && (toggleAttribute.ShowSelectAll || toggleAttribute.ShowSelectNone);

            float currentY = contentRect.y + VerticalPadding;
            if (showToolbarControls)
            {
                Rect toolbarRect = new(
                    contentRect.x,
                    currentY,
                    contentRect.width,
                    EditorGUIUtility.singleLineHeight
                );
                DrawToolbar(toolbarRect, toggleSet, property, toggleAttribute, palette);
                currentY += toolbarRect.height + EnumShared.ToolbarSpacing;
            }

            if (usePagination)
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

            LayoutMetrics metrics = WEnumToggleButtonsUtility.CalculateLayout(
                toggleAttribute.ButtonsPerRow,
                visibleCount,
                contentRect.width,
                EditorGUIUtility.singleLineHeight,
                EnumShared.ToolbarSpacing,
                EnumShared.MinButtonWidth
            );

            Rect buttonsRect = new(
                contentRect.x,
                currentY,
                contentRect.width,
                metrics.TotalHeight + VerticalPadding * 2
            );

            for (int index = 0; index < visibleCount; index += 1)
            {
                ToggleOption option = toggleSet.Options[startIndex + index];
                Rect buttonRect = metrics.GetItemRect(buttonsRect, index);
                DrawToggle(
                    buttonRect,
                    toggleSet,
                    property,
                    metrics,
                    option,
                    index,
                    visibleCount,
                    palette
                );
            }

            float totalHeight = (currentY - contentRect.y) + metrics.TotalHeight + VerticalPadding;
            WEnumToggleButtonsLayoutCache.Store(
                property,
                signature,
                contentRect.width,
                totalHeight
            );

            EditorGUI.EndProperty();
        }

        private static void DrawToolbar(
            Rect rect,
            ToggleSet toggleSet,
            SerializedProperty property,
            WEnumToggleButtonsAttribute toggleAttribute,
            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry palette
        )
        {
            bool drawSelectAll = toggleAttribute.ShowSelectAll;
            bool drawSelectNone = toggleAttribute.ShowSelectNone;

            if (!drawSelectAll && !drawSelectNone)
            {
                return;
            }

            bool alignedPair = drawSelectAll && drawSelectNone;

            if (alignedPair)
            {
                // Calculate widths: each button gets half minus half the gap
                float availableWidth = rect.width - EnumShared.ToolbarButtonGap;
                float buttonWidth = Mathf.Max(
                    EnumShared.ToolbarButtonMinWidth,
                    Mathf.Floor(availableWidth * EnumShared.EqualSplitRatio)
                );

                // Draw "All" button as standalone (not joined)
                Rect selectAllRect = new(rect.x, rect.y, buttonWidth, rect.height);
                bool allActive = WEnumToggleButtonsUtility.AreAllFlagsSelected(property, toggleSet);
                GUIStyle allStyle = EnumShared.GetButtonStyle(
                    EnumShared.ButtonSegment.Single,
                    allActive,
                    palette
                );
                bool selectAllPressed = GUI.Toggle(
                    selectAllRect,
                    allActive,
                    EnumShared.AllContent,
                    allStyle
                );

                if (selectAllPressed && !allActive)
                {
                    WEnumToggleButtonsUtility.ApplySelectAll(property, toggleSet);
                    property.serializedObject.ApplyModifiedProperties();
                }

                // Draw "None" button as standalone (not joined) with gap
                Rect selectNoneRect = new(
                    selectAllRect.xMax + EnumShared.ToolbarButtonGap,
                    rect.y,
                    rect.width - buttonWidth - EnumShared.ToolbarButtonGap,
                    rect.height
                );
                bool noneActive = WEnumToggleButtonsUtility.AreNoFlagsSelected(property);
                GUIStyle noneStyle = EnumShared.GetButtonStyle(
                    EnumShared.ButtonSegment.Single,
                    noneActive,
                    palette
                );
                bool selectNonePressed = GUI.Toggle(
                    selectNoneRect,
                    noneActive,
                    EnumShared.NoneContent,
                    noneStyle
                );

                if (selectNonePressed && !noneActive)
                {
                    WEnumToggleButtonsUtility.ApplySelectNone(property);
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
            else if (drawSelectAll)
            {
                bool allActive = WEnumToggleButtonsUtility.AreAllFlagsSelected(property, toggleSet);
                GUIStyle style = EnumShared.GetButtonStyle(
                    EnumShared.ButtonSegment.Single,
                    allActive,
                    palette
                );
                bool selectAllPressed = GUI.Toggle(rect, allActive, EnumShared.AllContent, style);

                if (selectAllPressed && !allActive)
                {
                    WEnumToggleButtonsUtility.ApplySelectAll(property, toggleSet);
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
            else if (drawSelectNone)
            {
                bool noneActive = WEnumToggleButtonsUtility.AreNoFlagsSelected(property);
                GUIStyle style = EnumShared.GetButtonStyle(
                    EnumShared.ButtonSegment.Single,
                    noneActive,
                    palette
                );
                bool selectNonePressed = GUI.Toggle(
                    rect,
                    noneActive,
                    EnumShared.NoneContent,
                    style
                );

                if (selectNonePressed && !noneActive)
                {
                    WEnumToggleButtonsUtility.ApplySelectNone(property);
                    property.serializedObject.ApplyModifiedProperties();
                }
            }
        }

        internal static void DrawPagination(
            Rect rect,
            WEnumToggleButtonsPagination.PaginationState state
        )
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

        private static void DrawToggle(
            Rect rect,
            ToggleSet toggleSet,
            SerializedProperty property,
            LayoutMetrics metrics,
            ToggleOption option,
            int visibleIndex,
            int visibleCount,
            UnityHelpersSettings.WEnumToggleButtonsPaletteEntry palette
        )
        {
            bool isActive = WEnumToggleButtonsUtility.IsOptionActive(property, toggleSet, option);
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

            WEnumToggleButtonsUtility.ApplyOption(property, toggleSet, option, newState);
            property.serializedObject.ApplyModifiedProperties();
        }

        internal static EnumShared.SelectionSummary BuildSelectionSummary(
            ToggleSet toggleSet,
            SerializedProperty property,
            int startIndex,
            int visibleCount,
            bool usePagination
        )
        {
            if (!usePagination || toggleSet.IsEmpty || property == null)
            {
                return EnumShared.SelectionSummary.None;
            }

            int endIndex = startIndex + visibleCount;
            PooledResource<List<string>> outOfViewLease = default;
            try
            {
                List<string> outOfView = null;
                IReadOnlyList<ToggleOption> options = toggleSet.Options;
                for (int index = 0; index < options.Count; index += 1)
                {
                    ToggleOption option = options[index];
                    if (!WEnumToggleButtonsUtility.IsOptionActive(property, toggleSet, option))
                    {
                        continue;
                    }

                    if (index >= startIndex && index < endIndex)
                    {
                        continue;
                    }

                    if (outOfView == null)
                    {
                        outOfViewLease = Buffers<string>.List.Get(out outOfView);
                    }

                    outOfView.Add(option.Label);
                }

                if (outOfView == null || outOfView.Count == 0)
                {
                    return EnumShared.SelectionSummary.None;
                }

                string joined = string.Join(", ", outOfView);
                string text = $"Current (out of view): {joined}";
                OutOfViewContent.text = text;
                return new EnumShared.SelectionSummary(true, OutOfViewContent);
            }
            finally
            {
                outOfViewLease.Dispose();
            }
        }
    }

    internal static class WEnumToggleButtonsUtility
    {
        private static readonly Dictionary<Type, ToggleOption[]> EnumOptionsCache = new();
        private static readonly Dictionary<Type, ToggleSet> ToggleSetCache = new();

        internal static void ClearCache()
        {
            EnumOptionsCache.Clear();
            ToggleSetCache.Clear();
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

        internal static ToggleSet CreateToggleSet(SerializedProperty property, FieldInfo fieldInfo)
        {
            if (property == null)
            {
                return ToggleSet.Empty;
            }

            FieldInfo resolvedFieldInfo = fieldInfo;
            if (resolvedFieldInfo == null)
            {
                property.GetEnclosingObject(out resolvedFieldInfo);
            }

            if (property.propertyType == SerializedPropertyType.Enum)
            {
                Type enumType = ResolveEnumType(resolvedFieldInfo);
                if (enumType == null)
                {
                    return ToggleSet.Empty;
                }

                if (ToggleSetCache.TryGetValue(enumType, out ToggleSet cachedToggleSet))
                {
                    return cachedToggleSet;
                }

                bool isFlags = ReflectionHelpers.HasAttributeSafe<FlagsAttribute>(
                    enumType,
                    inherit: true
                );
                ToggleOption[] enumOptions = GetCachedEnumOptions(enumType, isFlags);
                if (enumOptions.Length == 0)
                {
                    ToggleSetCache[enumType] = ToggleSet.Empty;
                    return ToggleSet.Empty;
                }

                ToggleSource source = isFlags ? ToggleSource.FlaggedEnum : ToggleSource.Enum;
                ToggleSet toggleSet = new(enumOptions, isFlags, source, enumType);
                ToggleSetCache[enumType] = toggleSet;
                return toggleSet;
            }

            ToggleOption[] dropdownOptions = BuildDropDownOptions(property, resolvedFieldInfo);
            if (dropdownOptions.Length == 0)
            {
                return ToggleSet.Empty;
            }

            Type valueType = resolvedFieldInfo != null ? resolvedFieldInfo.FieldType : null;
            return new ToggleSet(dropdownOptions, false, ToggleSource.Dropdown, valueType);
        }

        private static ToggleOption[] GetCachedEnumOptions(Type enumType, bool isFlags)
        {
            if (EnumOptionsCache.TryGetValue(enumType, out ToggleOption[] cached))
            {
                return cached;
            }

            ToggleOption[] options = BuildEnumOptions(enumType, isFlags);
            EnumOptionsCache[enumType] = options;
            return options;
        }

        internal static LayoutMetrics CalculateLayout(
            int requestedPerRow,
            int optionCount,
            float availableWidth,
            float lineHeight,
            float spacing,
            float minWidth
        )
        {
            if (optionCount <= 0)
            {
                return new LayoutMetrics(
                    1,
                    1,
                    Mathf.Max(minWidth, availableWidth),
                    lineHeight,
                    spacing,
                    lineHeight
                );
            }

            int columns =
                requestedPerRow > 0
                    ? requestedPerRow
                    : DetermineAutoColumns(availableWidth, spacing, minWidth);
            columns = Mathf.Clamp(columns, 1, optionCount);

            int rows = Mathf.CeilToInt(optionCount / (float)columns);
            float workingWidth = availableWidth;
            if (workingWidth <= 0f)
            {
                workingWidth = columns * minWidth + (columns - 1) * spacing;
            }

            float buttonWidth = (workingWidth - (columns - 1) * spacing) / columns;
            if (buttonWidth < minWidth)
            {
                buttonWidth = minWidth;
            }

            float totalHeight = rows * lineHeight + Mathf.Max(0, rows - 1) * spacing;
            return new LayoutMetrics(columns, rows, buttonWidth, lineHeight, spacing, totalHeight);
        }

        internal static bool IsOptionActive(
            SerializedProperty property,
            ToggleSet toggleSet,
            ToggleOption option
        )
        {
            switch (toggleSet.Source)
            {
                case ToggleSource.FlaggedEnum:
                    ulong mask = ReadEnumValue(property);
                    if (option.IsZeroFlag)
                    {
                        return mask == 0UL;
                    }
                    return (mask & option.FlagValue) == option.FlagValue;
                case ToggleSource.Enum:
                    if (toggleSet.ValueType == null)
                    {
                        return false;
                    }
                    object enumValue = Enum.ToObject(toggleSet.ValueType, ReadEnumValue(property));
                    return Equals(enumValue, option.Value);
                case ToggleSource.Dropdown:
                    return DropDownValueEquals(property, option.Value);
                default:
                    return false;
            }
        }

        internal static void ApplyOption(
            SerializedProperty property,
            ToggleSet toggleSet,
            ToggleOption option,
            bool desiredState
        )
        {
            switch (toggleSet.Source)
            {
                case ToggleSource.FlaggedEnum:
                    ApplyFlagOption(property, option, desiredState);
                    break;
                case ToggleSource.Enum:
                    ApplyEnumOption(property, option);
                    break;
                case ToggleSource.Dropdown:
                    ApplyDropDownOption(property, option);
                    break;
            }
        }

        internal static bool AreAllFlagsSelected(SerializedProperty property, ToggleSet toggleSet)
        {
            if (!toggleSet.SupportsMultipleSelection || toggleSet.Options.Count == 0)
            {
                return false;
            }

            ulong targetMask = 0UL;
            for (int index = 0; index < toggleSet.Options.Count; index += 1)
            {
                ToggleOption option = toggleSet.Options[index];
                if (option.FlagValue == 0UL)
                {
                    continue;
                }
                targetMask |= option.FlagValue;
            }

            if (targetMask == 0UL)
            {
                return false;
            }

            ulong current = ReadEnumValue(property);
            return (current & targetMask) == targetMask;
        }

        internal static bool AreNoFlagsSelected(SerializedProperty property)
        {
            return ReadEnumValue(property) == 0UL;
        }

        internal static void ApplySelectAll(SerializedProperty property, ToggleSet toggleSet)
        {
            if (!toggleSet.SupportsMultipleSelection)
            {
                return;
            }

            ulong combined = 0UL;
            for (int index = 0; index < toggleSet.Options.Count; index += 1)
            {
                ToggleOption option = toggleSet.Options[index];
                if (option.FlagValue == 0UL)
                {
                    continue;
                }
                combined |= option.FlagValue;
            }

            SetEnumValue(property, combined);
        }

        internal static void ApplySelectNone(SerializedProperty property)
        {
            SetEnumValue(property, 0UL);
        }

        private static Type ResolveEnumType(FieldInfo fieldInfo)
        {
            if (fieldInfo == null)
            {
                return null;
            }

            Type enumType = fieldInfo.FieldType;
            if (enumType.IsArray)
            {
                enumType = enumType.GetElementType();
            }

            if (enumType != null)
            {
                Type nullableUnderlying = Nullable.GetUnderlyingType(enumType);
                if (nullableUnderlying != null)
                {
                    enumType = nullableUnderlying;
                }
            }

            if (enumType is not { IsEnum: true })
            {
                return null;
            }

            return enumType;
        }

        private static ToggleOption[] BuildEnumOptions(Type enumType, bool isFlags)
        {
            Array values = Enum.GetValues(enumType);
            using PooledResource<List<ToggleOption>> optionsLease = Buffers<ToggleOption>.GetList(
                values.Length,
                out List<ToggleOption> options
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

                ulong numericValue = ConvertToUInt64(value);
                if (isFlags && numericValue != 0UL && !IsPowerOfTwo(numericValue))
                {
                    Debug.LogWarning(
                        $"[{nameof(WEnumToggleButtonsUtility)}] Skipping composite flag value {name} "
                            + $"in {enumType.Name} (value: {numericValue})"
                    );
                    continue;
                }

                string label = ObjectNames.NicifyVariableName(name);
                ToggleOption option = new(label, value, numericValue, numericValue == 0UL);
                options.Add(option);
            }

            if (options.Count == 0)
            {
                return Array.Empty<ToggleOption>();
            }

            return options.ToArray();
        }

        private static ToggleOption[] BuildDropDownOptions(
            SerializedProperty property,
            FieldInfo fieldInfo
        )
        {
            WValueDropDownAttribute wValueDropDownAttribute = GetAttribute<WValueDropDownAttribute>(
                fieldInfo,
                property
            );
            if (wValueDropDownAttribute != null)
            {
                object[] values = wValueDropDownAttribute.Options ?? Array.Empty<object>();
                return BuildGenericOptions(values);
            }

            IntDropDownAttribute intDropDownAttribute = GetAttribute<IntDropDownAttribute>(
                fieldInfo,
                property
            );
            if (intDropDownAttribute != null)
            {
                int[] values = intDropDownAttribute.Options ?? Array.Empty<int>();
                ToggleOption[] options = new ToggleOption[values.Length];
                for (int index = 0; index < values.Length; index += 1)
                {
                    int value = values[index];
                    string label = FormatOption(value);
                    ToggleOption option = new(label, value, 0UL, value == 0);
                    options[index] = option;
                }
                return options;
            }

            StringInListAttribute stringInListAttribute = GetAttribute<StringInListAttribute>(
                fieldInfo,
                property
            );
            if (stringInListAttribute != null)
            {
                UnityEngine.Object context = property.serializedObject?.targetObject;
                string[] values =
                    stringInListAttribute.GetOptions(context) ?? Array.Empty<string>();
                ToggleOption[] options = new ToggleOption[values.Length];
                for (int index = 0; index < values.Length; index += 1)
                {
                    string value = values[index] ?? string.Empty;
                    string label = FormatOption(value);
                    ToggleOption option = new(label, value, 0UL, false);
                    options[index] = option;
                }
                return options;
            }

            return Array.Empty<ToggleOption>();
        }

        private static ToggleOption[] BuildGenericOptions(object[] values)
        {
            if (values == null || values.Length == 0)
            {
                return Array.Empty<ToggleOption>();
            }

            ToggleOption[] options = new ToggleOption[values.Length];
            for (int index = 0; index < values.Length; index += 1)
            {
                object value = values[index];
                string label = FormatOption(value);
                bool isZero = IsZeroEquivalent(value);
                ToggleOption option = new(label, value, 0UL, isZero);
                options[index] = option;
            }

            return options;
        }

        private static int DetermineAutoColumns(float availableWidth, float spacing, float minWidth)
        {
            if (availableWidth <= 0f)
            {
                return 1;
            }

            float effectiveWidth = minWidth + spacing;
            int columns = Mathf.FloorToInt((availableWidth + spacing) / effectiveWidth);
            if (columns < 1)
            {
                columns = 1;
            }

            return columns;
        }

        private static bool DropDownValueEquals(SerializedProperty property, object candidate)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    return CompareLong(property.longValue, candidate);
                case SerializedPropertyType.Float:
                    double numericValue = IsDoubleProperty(property)
                        ? property.doubleValue
                        : property.floatValue;
                    return CompareDouble(numericValue, candidate);
                case SerializedPropertyType.String:
                    return CompareString(property.stringValue, candidate);
                case SerializedPropertyType.ObjectReference:
                    return Equals(property.objectReferenceValue, candidate as UnityEngine.Object);
                case SerializedPropertyType.Enum:
                    return CompareLong(property.longValue, candidate);
                default:
                    return false;
            }
        }

        private static void ApplyFlagOption(
            SerializedProperty property,
            ToggleOption option,
            bool desiredState
        )
        {
            ulong current = ReadEnumValue(property);
            if (option.IsZeroFlag)
            {
                if (desiredState && current != 0UL)
                {
                    SetEnumValue(property, 0UL);
                }
                return;
            }

            ulong mask = option.FlagValue;
            if (desiredState)
            {
                ulong combined = current | mask;
                SetEnumValue(property, combined);
                return;
            }

            ulong cleared = current & ~mask;
            SetEnumValue(property, cleared);
        }

        private static void ApplyEnumOption(SerializedProperty property, ToggleOption option)
        {
            if (option.Value == null)
            {
                return;
            }

            if (option.Value is Enum enumValue)
            {
                long numeric = Convert.ToInt64(enumValue, CultureInfo.InvariantCulture);
                property.longValue = numeric;
                return;
            }

            if (TryConvertToInt64(option.Value, out long longValue))
            {
                property.longValue = longValue;
            }
        }

        private static void ApplyDropDownOption(SerializedProperty property, ToggleOption option)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    if (TryConvertToInt64(option.Value, out long longValue))
                    {
                        property.longValue = longValue;
                    }
                    break;
                case SerializedPropertyType.Float:
                    if (TryConvertToDouble(option.Value, out double doubleValue))
                    {
                        if (IsDoubleProperty(property))
                        {
                            property.doubleValue = doubleValue;
                        }
                        else
                        {
                            property.floatValue = (float)doubleValue;
                        }
                    }
                    break;
                case SerializedPropertyType.String:
                    property.stringValue =
                        option.Value == null
                            ? string.Empty
                            : Convert.ToString(option.Value, CultureInfo.InvariantCulture)
                                ?? string.Empty;
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = option.Value as UnityEngine.Object;
                    break;
                case SerializedPropertyType.Enum:
                    if (TryConvertToInt64(option.Value, out long enumLong))
                    {
                        property.longValue = enumLong;
                    }
                    break;
            }
        }

        private static bool CompareLong(long current, object candidate)
        {
            if (!TryConvertToInt64(candidate, out long candidateValue))
            {
                return false;
            }

            return current == candidateValue;
        }

        private static bool CompareDouble(double current, object candidate)
        {
            if (!TryConvertToDouble(candidate, out double candidateValue))
            {
                return false;
            }

            return Math.Abs(current - candidateValue) <= 0.000001d;
        }

        private static bool IsDoubleProperty(SerializedProperty property)
        {
            if (property == null)
            {
                return false;
            }

            return string.Equals(property.type, "double", StringComparison.Ordinal);
        }

        private static bool CompareString(string current, object candidate)
        {
            string candidateString =
                candidate == null
                    ? string.Empty
                    : Convert.ToString(candidate, CultureInfo.InvariantCulture) ?? string.Empty;
            return string.Equals(
                current ?? string.Empty,
                candidateString,
                StringComparison.Ordinal
            );
        }

        private static bool TryConvertToInt64(object value, out long converted)
        {
            converted = 0L;
            if (value == null)
            {
                return false;
            }

            try
            {
                converted = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryConvertToDouble(object value, out double converted)
        {
            converted = 0d;
            if (value == null)
            {
                return false;
            }

            try
            {
                converted = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool IsZeroEquivalent(object value)
        {
            if (value == null)
            {
                return true;
            }

            if (value is int intValue)
            {
                return intValue == 0;
            }

            if (value is long longValue)
            {
                return longValue == 0L;
            }

            if (value is short shortValue)
            {
                return shortValue == 0;
            }

            if (value is byte byteValue)
            {
                return byteValue == 0;
            }

            return false;
        }

        private static string FormatOption(object value)
        {
            if (value == null)
            {
                return "(null)";
            }

            if (value is string stringValue)
            {
                return stringValue;
            }

            if (value is Enum enumValue)
            {
                string name = enumValue.ToString();
                return ObjectNames.NicifyVariableName(name);
            }

            if (value is IFormattable formattable)
            {
                return formattable.ToString(null, CultureInfo.InvariantCulture);
            }

            string fallback = value.ToString();
            return string.IsNullOrEmpty(fallback) ? "(empty)" : fallback;
        }

        private static ulong ReadEnumValue(SerializedProperty property)
        {
            return unchecked((ulong)property.longValue);
        }

        private static void SetEnumValue(SerializedProperty property, ulong value)
        {
            property.longValue = unchecked((long)value);
        }

        private static ulong ConvertToUInt64(object value)
        {
            return Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        }

        private static bool IsPowerOfTwo(ulong value)
        {
            return value != 0UL && (value & (value - 1UL)) == 0UL;
        }

        private static TAttribute GetAttribute<TAttribute>(
            FieldInfo fieldInfo,
            SerializedProperty property
        )
            where TAttribute : Attribute
        {
            if (
                ReflectionHelpers.TryGetAttributeSafe(
                    fieldInfo,
                    out TAttribute attribute,
                    inherit: true
                )
                || property == null
            )
            {
                return attribute;
            }

            property.GetEnclosingObject(out FieldInfo inferredFieldInfo);
            if (
                inferredFieldInfo != null
                && inferredFieldInfo != fieldInfo
                && ReflectionHelpers.TryGetAttributeSafe(
                    inferredFieldInfo,
                    out attribute,
                    inherit: true
                )
            )
            {
                return attribute;
            }

            return null;
        }
    }

    internal enum ToggleSource
    {
        None,
        FlaggedEnum,
        Enum,
        Dropdown,
    }

    internal readonly struct ToggleOption
    {
        internal ToggleOption(string label, object value, ulong flagValue, bool isZeroFlag)
        {
            Label = string.IsNullOrEmpty(label) ? "(Unnamed)" : label;
            Value = value;
            FlagValue = flagValue;
            IsZeroFlag = isZeroFlag;
        }

        internal string Label { get; }

        internal object Value { get; }

        internal ulong FlagValue { get; }

        internal bool IsZeroFlag { get; }
    }

    internal readonly struct ToggleSet
    {
        private readonly ToggleOption[] _options;

        internal ToggleSet(
            ToggleOption[] options,
            bool supportsMultipleSelection,
            ToggleSource source,
            Type valueType
        )
        {
            _options = options ?? Array.Empty<ToggleOption>();
            SupportsMultipleSelection = supportsMultipleSelection;
            Source = source;
            ValueType = valueType;
        }

        internal static ToggleSet Empty { get; } =
            new(Array.Empty<ToggleOption>(), false, ToggleSource.None, null);

        internal IReadOnlyList<ToggleOption> Options => _options;

        internal bool SupportsMultipleSelection { get; }

        internal ToggleSource Source { get; }

        internal Type ValueType { get; }

        internal bool IsEmpty => _options == null || _options.Length == 0;
    }

    internal readonly struct LayoutSignature : IEquatable<LayoutSignature>
    {
        internal LayoutSignature(
            int optionCount,
            int visibleCount,
            int buttonsPerRow,
            bool supportsMultipleSelection,
            bool showSelectAll,
            bool showSelectNone,
            bool usePagination,
            bool hasSummary,
            int widthBucket
        )
        {
            OptionCount = optionCount;
            VisibleCount = visibleCount;
            ButtonsPerRow = buttonsPerRow;
            SupportsMultipleSelection = supportsMultipleSelection;
            ShowSelectAll = showSelectAll;
            ShowSelectNone = showSelectNone;
            UsePagination = usePagination;
            HasSummary = hasSummary;
            WidthBucket = widthBucket;
        }

        internal int OptionCount { get; }

        internal int VisibleCount { get; }

        internal int ButtonsPerRow { get; }

        internal bool SupportsMultipleSelection { get; }

        internal bool ShowSelectAll { get; }

        internal bool ShowSelectNone { get; }

        internal bool UsePagination { get; }

        internal bool HasSummary { get; }

        internal int WidthBucket { get; }

        public bool Equals(LayoutSignature other)
        {
            return OptionCount == other.OptionCount
                && VisibleCount == other.VisibleCount
                && ButtonsPerRow == other.ButtonsPerRow
                && SupportsMultipleSelection == other.SupportsMultipleSelection
                && ShowSelectAll == other.ShowSelectAll
                && ShowSelectNone == other.ShowSelectNone
                && UsePagination == other.UsePagination
                && HasSummary == other.HasSummary
                && WidthBucket == other.WidthBucket;
        }

        public override bool Equals(object obj)
        {
            return obj is LayoutSignature other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(
                OptionCount,
                VisibleCount,
                ButtonsPerRow,
                SupportsMultipleSelection,
                ShowSelectAll,
                ShowSelectNone,
                UsePagination,
                HasSummary,
                WidthBucket
            );
        }
    }

    internal static class WEnumToggleButtonsLayoutCache
    {
        private sealed class Entry
        {
            internal Entry(LayoutSignature signature, float width, float height)
            {
                Signature = signature;
                Width = width;
                Height = height;
            }

            internal LayoutSignature Signature { get; }

            internal float Width { get; }

            internal float Height { get; }
        }

        private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);

        internal static LayoutSignature CreateSignature(
            int optionCount,
            int visibleCount,
            int buttonsPerRow,
            bool supportsMultipleSelection,
            bool showSelectAll,
            bool showSelectNone,
            bool usePagination,
            bool hasSummary,
            float widthHint
        )
        {
            int widthBucket = Mathf.RoundToInt(Mathf.Max(0f, widthHint) * 100f);
            return new LayoutSignature(
                optionCount,
                visibleCount,
                buttonsPerRow,
                supportsMultipleSelection,
                showSelectAll,
                showSelectNone,
                usePagination,
                hasSummary,
                widthBucket
            );
        }

        internal static bool TryGetHeight(
            SerializedProperty property,
            LayoutSignature signature,
            out float height
        )
        {
            if (property == null)
            {
                height = 0f;
                return false;
            }

            string key = BuildKey(property);
            if (Entries.TryGetValue(key, out Entry entry) && entry.Signature.Equals(signature))
            {
                height = entry.Height;
                return true;
            }

            height = 0f;
            return false;
        }

        internal static void Store(
            SerializedProperty property,
            LayoutSignature signature,
            float width,
            float height
        )
        {
            if (property == null)
            {
                return;
            }

            string key = BuildKey(property);
            Entries[key] = new Entry(signature, width, height);
        }

        internal static void Reset()
        {
            Entries.Clear();
        }

        private static string BuildKey(SerializedProperty property)
        {
            if (property == null)
            {
                return string.Empty;
            }

            SerializedObject serializedObject = property.serializedObject;
            UnityEngine.Object target = serializedObject?.targetObject;
            if (target == null)
            {
                return property.propertyPath ?? string.Empty;
            }

            string instancePart = target
                .GetUnityObjectId()
                .ToString("X8", CultureInfo.InvariantCulture);
            string pathPart = property.propertyPath ?? string.Empty;
            return instancePart + ":" + pathPart;
        }
    }

    internal readonly struct LayoutMetrics
    {
        internal LayoutMetrics(
            int columns,
            int rows,
            float buttonWidth,
            float buttonHeight,
            float spacing,
            float totalHeight
        )
        {
            Columns = columns;
            Rows = rows;
            ButtonWidth = buttonWidth;
            ButtonHeight = buttonHeight;
            Spacing = spacing;
            TotalHeight = totalHeight;
        }

        internal int Columns { get; }

        internal int Rows { get; }

        internal float ButtonWidth { get; }

        internal float ButtonHeight { get; }

        internal float Spacing { get; }

        internal float TotalHeight { get; }

        internal Rect GetItemRect(Rect bounds, int index)
        {
            int row = index / Columns;
            int column = index % Columns;
            float x = bounds.x + column * (ButtonWidth + Spacing);
            float y = bounds.y + row * (ButtonHeight + Spacing);
            return new Rect(x, y, ButtonWidth, ButtonHeight);
        }
    }

    internal static class WEnumToggleButtonsPagination
    {
        internal sealed class PaginationState
        {
            private int _pageIndex;

            internal int PageSize { get; set; }

            internal int TotalItems { get; set; }

            internal int PageIndex
            {
                get => _pageIndex;
                set => _pageIndex = value;
            }

            internal int TotalPages
            {
                get
                {
                    if (PageSize <= 0)
                    {
                        return 1;
                    }

                    return Mathf.Max(1, Mathf.CeilToInt(TotalItems / (float)PageSize));
                }
            }

            internal int StartIndex
            {
                get
                {
                    if (TotalItems <= 0 || PageSize <= 0)
                    {
                        return 0;
                    }

                    int clampedIndex = Mathf.Clamp(PageIndex, 0, TotalPages - 1);
                    return clampedIndex * PageSize;
                }
            }

            internal int VisibleCount
            {
                get
                {
                    if (TotalItems <= 0 || PageSize <= 0)
                    {
                        return 0;
                    }

                    int clampedIndex = Mathf.Clamp(PageIndex, 0, TotalPages - 1);
                    int start = clampedIndex * PageSize;
                    return Mathf.Clamp(TotalItems - start, 0, PageSize);
                }
            }
        }

        private static readonly Dictionary<string, PaginationState> States = new(
            StringComparer.Ordinal
        );

        internal static PaginationState GetState(
            SerializedProperty property,
            int totalItems,
            int pageSize
        )
        {
            string key = BuildKey(property);
            PaginationState state = States.GetOrAdd(key);

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

        internal static void Reset()
        {
            States.Clear();
            WEnumToggleButtonsLayoutCache.Reset();
        }

        private static string BuildKey(SerializedProperty property)
        {
            if (property == null)
            {
                return string.Empty;
            }

            SerializedObject serializedObject = property.serializedObject;
            UnityEngine.Object target = serializedObject?.targetObject;
            if (target == null)
            {
                return property.propertyPath ?? string.Empty;
            }

            string instancePart = target
                .GetUnityObjectId()
                .ToString("X8", CultureInfo.InvariantCulture);
            string pathPart = property.propertyPath ?? string.Empty;
            return instancePart + ":" + pathPart;
        }
    }
}
