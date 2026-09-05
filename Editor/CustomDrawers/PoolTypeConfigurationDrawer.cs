// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Custom property drawer for <see cref="PoolTypeConfiguration"/> that validates type names
    /// and displays the resolved type information.
    /// </summary>
    [CustomPropertyDrawer(typeof(PoolTypeConfiguration))]
    public sealed class PoolTypeConfigurationDrawer : PropertyDrawer
    {
        private const string TypeNameFieldName = nameof(PoolTypeConfiguration._typeName);
        private const string EnabledFieldName = nameof(PoolTypeConfiguration._enabled);
        private const string IdleTimeoutFieldName = nameof(
            PoolTypeConfiguration._idleTimeoutSeconds
        );
        private const string MinRetainCountFieldName = nameof(
            PoolTypeConfiguration._minRetainCount
        );
        private const string MaxPoolSizeFieldName = nameof(PoolTypeConfiguration._maxPoolSize);
        private const string BufferMultiplierFieldName = nameof(
            PoolTypeConfiguration._bufferMultiplier
        );
        private const string RollingWindowFieldName = nameof(
            PoolTypeConfiguration._rollingWindowSeconds
        );
        private const string HysteresisFieldName = nameof(PoolTypeConfiguration._hysteresisSeconds);
        private const string SpikeThresholdFieldName = nameof(
            PoolTypeConfiguration._spikeThresholdMultiplier
        );

        private const float StatusBoxMargin = 4f;
        private const float MinStatusBoxWidth = 200f;

        private static readonly Dictionary<string, DrawerState> States = new();

        static PoolTypeConfigurationDrawer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ClearCachedStates;
        }

        private static readonly GUIContent TypeNameLabel = new(
            "Type Name",
            "Type name in any supported format:\n"
                + "- List<int> (simplified closed generic)\n"
                + "- List<> (simplified open generic)\n"
                + "- Dictionary<string, int> (multiple args)\n"
                + "- Dictionary<,> (open with multiple args)\n"
                + "- List<List<int>> (nested generics)\n"
                + "- System.Collections.Generic.List`1 (CLR syntax)"
        );

        private static readonly GUIContent EnabledLabel = new(
            "Enabled",
            "Whether intelligent pool purging is enabled for this type."
        );

        private static readonly GUIContent IdleTimeoutLabel = new(
            "Idle Timeout (s)",
            "Idle timeout in seconds before items become eligible for purging."
        );

        private static readonly GUIContent MinRetainCountLabel = new(
            "Min Retain",
            "Minimum number of items to always retain during purge operations."
        );

        private static readonly GUIContent MaxPoolSizeLabel = new(
            "Max Size",
            "Maximum pool size (0 = unbounded)."
        );

        private static readonly GUIContent BufferMultiplierLabel = new(
            "Buffer",
            "Buffer multiplier for comfortable pool size calculation."
        );

        private static readonly GUIContent RollingWindowLabel = new(
            "Window (s)",
            "Rolling window duration in seconds for high water mark tracking."
        );

        private static readonly GUIContent HysteresisLabel = new(
            "Hysteresis (s)",
            "Hysteresis duration in seconds. Purging is suppressed after a usage spike."
        );

        private static readonly GUIContent SpikeThresholdLabel = new(
            "Spike",
            "Spike threshold multiplier."
        );

        private static readonly GUIContent ReusableHeaderLabel = new();

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            // Header + Type Name + Status box + Enabled + 7 numeric fields
            int lineCount = 10;
            float totalHeight = lineHeight + (lineCount * (lineHeight + spacing));

            DrawerState state = GetState(property);
            if (!state.isValid && !string.IsNullOrEmpty(state.statusMessage))
            {
                state.statusContent.text = state.statusMessage;
                float statusWidth = GetStatusBoxWidth();
                float statusHeight = EditorStyles.helpBox.CalcHeight(
                    state.statusContent,
                    statusWidth
                );
                totalHeight += statusHeight + spacing;
            }
            else if (state.isValid && !string.IsNullOrEmpty(state.statusMessage))
            {
                state.statusContent.text = state.statusMessage;
                float statusWidth = GetStatusBoxWidth();
                float statusHeight = EditorStyles.helpBox.CalcHeight(
                    state.statusContent,
                    statusWidth
                );
                totalHeight += statusHeight + spacing;
            }

            return totalHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            DrawerState state = GetState(property);

            SerializedProperty typeNameProp = property.FindPropertyRelative(TypeNameFieldName);
            SerializedProperty enabledProp = property.FindPropertyRelative(EnabledFieldName);
            SerializedProperty idleTimeoutProp = property.FindPropertyRelative(
                IdleTimeoutFieldName
            );
            SerializedProperty minRetainProp = property.FindPropertyRelative(
                MinRetainCountFieldName
            );
            SerializedProperty maxPoolSizeProp = property.FindPropertyRelative(
                MaxPoolSizeFieldName
            );
            SerializedProperty bufferProp = property.FindPropertyRelative(
                BufferMultiplierFieldName
            );
            SerializedProperty rollingWindowProp = property.FindPropertyRelative(
                RollingWindowFieldName
            );
            SerializedProperty hysteresisProp = property.FindPropertyRelative(HysteresisFieldName);
            SerializedProperty spikeThresholdProp = property.FindPropertyRelative(
                SpikeThresholdFieldName
            );

            if (typeNameProp == null)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            string currentTypeName = typeNameProp.stringValue;
            if (!string.Equals(state.lastTypeName, currentTypeName, StringComparison.Ordinal))
            {
                state.lastTypeName = currentTypeName;
                ValidateTypeName(state, currentTypeName);
            }

            EditorGUI.BeginProperty(position, label, property);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float currentY = position.y;

            Rect foldoutRect = new(position.x, currentY, position.width, lineHeight);

            if (string.IsNullOrWhiteSpace(currentTypeName))
            {
                ReusableHeaderLabel.text = label.text + " (empty)";
                ReusableHeaderLabel.tooltip = label.tooltip;
            }
            else if (state.isValid)
            {
                string displayName = PoolTypeResolver.GetDisplayName(state.resolvedType);
                string prefix = state.resolvedType.IsGenericTypeDefinition ? "[Open] " : "";
                ReusableHeaderLabel.text = $"{label.text}: {prefix}{displayName}";
                ReusableHeaderLabel.tooltip = label.tooltip;
            }
            else
            {
                ReusableHeaderLabel.text = $"{label.text}: {currentTypeName} (invalid)";
                ReusableHeaderLabel.tooltip = label.tooltip;
            }

            property.isExpanded = EditorGUI.Foldout(
                foldoutRect,
                property.isExpanded,
                ReusableHeaderLabel,
                true
            );
            currentY += lineHeight + spacing;

            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            using IndentLevelScope indentScope = IndentLevelScope.Indent();

            Rect typeNameRect = new(position.x, currentY, position.width, lineHeight);
            EditorGUI.PropertyField(typeNameRect, typeNameProp, TypeNameLabel);
            currentY += lineHeight + spacing;

            if (!string.IsNullOrEmpty(state.statusMessage))
            {
                state.statusContent.text = state.statusMessage;
                float statusHeight = EditorStyles.helpBox.CalcHeight(
                    state.statusContent,
                    position.width - StatusBoxMargin * 2
                );
                Rect statusRect = new(
                    position.x + StatusBoxMargin,
                    currentY,
                    position.width - StatusBoxMargin * 2,
                    statusHeight
                );
                EditorGUI.HelpBox(statusRect, state.statusMessage, state.messageType);
                currentY += statusHeight + spacing;
            }

            Rect enabledRect = new(position.x, currentY, position.width, lineHeight);
            if (enabledProp != null)
            {
                EditorGUI.PropertyField(enabledRect, enabledProp, EnabledLabel);
            }
            currentY += lineHeight + spacing;

            float halfWidth = (position.width - spacing) / 2f;
            float indent = EditorGUI.indentLevel * 15f;

            DrawTwoColumnRow(
                position,
                ref currentY,
                lineHeight,
                spacing,
                halfWidth,
                indent,
                idleTimeoutProp,
                IdleTimeoutLabel,
                minRetainProp,
                MinRetainCountLabel
            );

            DrawTwoColumnRow(
                position,
                ref currentY,
                lineHeight,
                spacing,
                halfWidth,
                indent,
                maxPoolSizeProp,
                MaxPoolSizeLabel,
                bufferProp,
                BufferMultiplierLabel
            );

            DrawTwoColumnRow(
                position,
                ref currentY,
                lineHeight,
                spacing,
                halfWidth,
                indent,
                rollingWindowProp,
                RollingWindowLabel,
                hysteresisProp,
                HysteresisLabel
            );

            Rect spikeRect = new(position.x, currentY, position.width, lineHeight);
            if (spikeThresholdProp != null)
            {
                EditorGUI.PropertyField(spikeRect, spikeThresholdProp, SpikeThresholdLabel);
            }

            EditorGUI.EndProperty();
        }

        private static void DrawTwoColumnRow(
            Rect position,
            ref float currentY,
            float lineHeight,
            float spacing,
            float halfWidth,
            float indent,
            SerializedProperty leftProp,
            GUIContent leftLabel,
            SerializedProperty rightProp,
            GUIContent rightLabel
        )
        {
            Rect leftRect = new(position.x, currentY, halfWidth, lineHeight);
            Rect rightRect = new(position.x + halfWidth + spacing, currentY, halfWidth, lineHeight);

            if (leftProp != null)
            {
                EditorGUI.PropertyField(leftRect, leftProp, leftLabel);
            }

            using (IndentLevelScope.AtLevel(0))
            {
                Rect adjustedRightRect = new(
                    rightRect.x + indent,
                    rightRect.y,
                    rightRect.width - indent,
                    rightRect.height
                );
                if (rightProp != null)
                {
                    EditorGUI.PropertyField(adjustedRightRect, rightProp, rightLabel);
                }
            }

            currentY += lineHeight + spacing;
        }

        private static void ValidateTypeName(DrawerState state, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                state.resolvedType = null;
                state.isValid = false;
                state.statusMessage = string.Empty;
                state.messageType = MessageType.None;
                return;
            }

            Type resolved = PoolTypeResolver.ResolveType(typeName);
            if (resolved == null)
            {
                state.resolvedType = null;
                state.isValid = false;
                state.statusMessage = $"Unable to resolve type: {typeName}";
                state.messageType = MessageType.Warning;
                return;
            }

            state.resolvedType = resolved;
            state.isValid = true;

            if (resolved.IsGenericTypeDefinition)
            {
                string displayName = PoolTypeResolver.GetDisplayName(resolved);
                state.statusMessage = $"Open generic pattern: matches all {displayName} types";
                state.messageType = MessageType.Info;
            }
            else if (resolved.IsGenericType)
            {
                string displayName = PoolTypeResolver.GetDisplayName(resolved);
                state.statusMessage = $"Resolved: {displayName}";
                state.messageType = MessageType.Info;
            }
            else
            {
                state.statusMessage = $"Resolved: {resolved.FullName}";
                state.messageType = MessageType.Info;
            }
        }

        internal static DrawerState GetState(SerializedProperty property)
        {
            string key = property.propertyPath;
            return States.GetOrAdd(key);
        }

        internal static void ClearCachedStates()
        {
            States.Clear();
        }

        private static float GetStatusBoxWidth()
        {
            try
            {
                return Mathf.Max(
                    MinStatusBoxWidth,
                    EditorGUIUtility.currentViewWidth - StatusBoxMargin * 2
                );
            }
            catch (ArgumentException)
            {
                return MinStatusBoxWidth;
            }
        }

        internal sealed class DrawerState
        {
            public string lastTypeName = string.Empty;
            public Type resolvedType;
            public bool isValid;
            public string statusMessage = string.Empty;
            public MessageType messageType = MessageType.None;
            public readonly GUIContent statusContent = new();
        }
    }
}
#endif
