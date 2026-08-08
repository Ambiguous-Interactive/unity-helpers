// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [CustomPropertyDrawer(typeof(WGuid))]
    public sealed class WGuidPropertyDrawer : PropertyDrawer
    {
        internal sealed class DrawerState
        {
            public string displayText = string.Empty;
            public string serializedText = string.Empty;
            public bool hasPendingInvalid;
            public string warningMessage = string.Empty;
            public SerializedProperty cachedLowProperty;
            public SerializedProperty cachedHighProperty;
            public SerializedObject cachedSerializedObject;
            public int lastCacheFrame = -1;
            public readonly GUIContent warningContent = new();

            public void InvalidateCache()
            {
                cachedLowProperty = null;
                cachedHighProperty = null;
                cachedSerializedObject = null;
                lastCacheFrame = -1;
            }
        }

        private const float ButtonWidth = 24f;
        private const string ClearUndoLabel = nameof(WGuid) + ".Clear";
        private const string SetUndoLabel = nameof(WGuid) + ".Set";
        private const string GenerateUndoLabel = nameof(WGuid) + ".Generate";
        private const string VersionFourWarning = "WGuid expects a version 4 Guid.";
        private const string InvalidGuidWarning = "Enter a valid Guid string.";

        private static readonly Dictionary<string, DrawerState> States = new();
        private static readonly GUIContent GenerateContent = CreateGenerateContent();

        private static void GetCachedProperties(
            SerializedProperty property,
            DrawerState state,
            out SerializedProperty lowProperty,
            out SerializedProperty highProperty
        )
        {
            int currentFrame = Time.frameCount;
            SerializedObject serializedObject = property.serializedObject;

            // Validate cached properties are still valid:
            // 1. Same frame as last cache
            // 2. Same SerializedObject instance (not a different object with same property path)
            // 3. Cached SerializedObject hasn't been disposed
            // 4. Both cached properties exist
            bool cacheValid =
                state.lastCacheFrame == currentFrame
                && state.cachedSerializedObject != null
                && ReferenceEquals(state.cachedSerializedObject, serializedObject)
                && state.cachedLowProperty != null
                && state.cachedHighProperty != null
                && IsSerializedObjectValid(serializedObject);

            if (cacheValid)
            {
                lowProperty = state.cachedLowProperty;
                highProperty = state.cachedHighProperty;
                return;
            }

            // Cache miss or invalid - refresh the cache
            lowProperty = property.FindPropertyRelative(WGuid.LowFieldName);
            highProperty = property.FindPropertyRelative(WGuid.HighFieldName);
            state.cachedLowProperty = lowProperty;
            state.cachedHighProperty = highProperty;
            state.cachedSerializedObject = serializedObject;
            state.lastCacheFrame = currentFrame;
        }

        private static bool IsSerializedObjectValid(SerializedObject serializedObject)
        {
            if (serializedObject == null)
            {
                return false;
            }

            try
            {
                // Accessing targetObject will throw if the SerializedObject is disposed
                _ = serializedObject.targetObject;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            DrawerState state = GetState(property);
            float lineHeight = EditorGUIUtility.singleLineHeight;
            if (!state.hasPendingInvalid || string.IsNullOrEmpty(state.warningMessage))
            {
                return lineHeight;
            }

            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float warningWidth = GetWarningWidth();
            state.warningContent.text = state.warningMessage;
            float warningHeight = EditorStyles.helpBox.CalcHeight(
                state.warningContent,
                warningWidth
            );
            return lineHeight + spacing + warningHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            DrawerState state = GetState(property);

            // Early validation: check if the SerializedObject is still valid
            if (!IsSerializedObjectValid(property.serializedObject))
            {
                state.InvalidateCache();
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            GetCachedProperties(
                property,
                state,
                out SerializedProperty lowProperty,
                out SerializedProperty highProperty
            );
            if (lowProperty == null || highProperty == null)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            // Safely read property values with disposal protection
            long lowValue;
            long highValue;
            try
            {
                lowValue = lowProperty.longValue;
                highValue = highProperty.longValue;
            }
            catch (Exception)
            {
                // SerializedObject was disposed between validation and access
                state.InvalidateCache();
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            string serializedText = ConvertToString(lowValue, highValue);
            if (!state.hasPendingInvalid || string.IsNullOrEmpty(state.displayText))
            {
                if (!string.Equals(state.displayText, serializedText, StringComparison.Ordinal))
                {
                    state.displayText = serializedText;
                }

                state.serializedText = serializedText;
                state.hasPendingInvalid = false;
                state.warningMessage = string.Empty;
            }
            else if (!string.Equals(state.serializedText, serializedText, StringComparison.Ordinal))
            {
                state.serializedText = serializedText;
                state.displayText = serializedText;
                state.hasPendingInvalid = false;
                state.warningMessage = string.Empty;
            }

            if (!state.hasPendingInvalid && !WGuid.HasVersionFourLayout(lowValue, highValue))
            {
                state.hasPendingInvalid = true;
                state.warningMessage = VersionFourWarning;
            }

            EditorGUI.BeginProperty(position, label, property);
            using IndentLevelScope indentScope = IndentLevelScope.AtLevel(0);

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            Rect contentRect = EditorGUI.PrefixLabel(position, label);
            float buttonX = contentRect.x + Mathf.Max(0f, contentRect.width - ButtonWidth);
            Rect buttonRect = new(buttonX, contentRect.y, ButtonWidth, lineHeight);
            float textWidth = buttonRect.x - contentRect.x - spacing;
            if (textWidth < 0f)
            {
                textWidth = 0f;
            }

            Rect textRect = new(contentRect.x, contentRect.y, textWidth, lineHeight);

            EditorGUI.BeginChangeCheck();
            string incoming = EditorGUI.DelayedTextField(
                textRect,
                state.displayText ?? string.Empty
            );
            if (EditorGUI.EndChangeCheck())
            {
                HandleTextChange(property, lowProperty, highProperty, state, incoming);
            }

            if (GUI.Button(buttonRect, GenerateContent, EditorStyles.miniButton))
            {
                GenerateNewGuid(property, lowProperty, highProperty, state);
            }

            if (state.hasPendingInvalid && !string.IsNullOrEmpty(state.warningMessage))
            {
                state.warningContent.text = state.warningMessage;
                Rect helpRect = new(
                    position.x,
                    position.y + lineHeight + spacing,
                    position.width,
                    EditorStyles.helpBox.CalcHeight(state.warningContent, position.width)
                );
                EditorGUI.HelpBox(helpRect, state.warningMessage, MessageType.Warning);
            }

            EditorGUI.EndProperty();
        }

        internal static void HandleTextChange(
            SerializedProperty property,
            SerializedProperty lowProperty,
            SerializedProperty highProperty,
            DrawerState state,
            string incoming
        )
        {
            string normalizedInput = incoming ?? string.Empty;
            string trimmed = normalizedInput.Trim();
            state.displayText = normalizedInput;

            if (string.IsNullOrEmpty(trimmed))
            {
                WGuid empty = WGuid.EmptyGuid;
                UpdateGuidValue(property, lowProperty, highProperty, empty, ClearUndoLabel);
                string serializedText = ConvertToString(
                    lowProperty.longValue,
                    highProperty.longValue
                );
                state.displayText = serializedText;
                state.serializedText = serializedText;
                state.hasPendingInvalid = false;
                state.warningMessage = string.Empty;
                return;
            }

            if (!Guid.TryParse(trimmed, out _))
            {
                state.hasPendingInvalid = true;
                state.warningMessage = InvalidGuidWarning;
                return;
            }

            try
            {
                if (!WGuid.TryParse(trimmed, out WGuid parsed))
                {
                    state.hasPendingInvalid = true;
                    state.warningMessage = VersionFourWarning;
                    return;
                }

                UpdateGuidValue(property, lowProperty, highProperty, parsed, SetUndoLabel);
                string normalized = parsed.ToString();
                state.displayText = normalized;
                state.serializedText = normalized;
                state.hasPendingInvalid = false;
                state.warningMessage = string.Empty;
            }
            catch (FormatException e)
            {
                state.hasPendingInvalid = true;
                state.warningMessage = e.Message;
            }
        }

        internal static void GenerateNewGuid(
            SerializedProperty property,
            SerializedProperty lowProperty,
            SerializedProperty highProperty,
            DrawerState state
        )
        {
            WGuid generated = WGuid.NewGuid();
            UpdateGuidValue(property, lowProperty, highProperty, generated, GenerateUndoLabel);
            string normalized = generated.ToString();
            state.displayText = normalized;
            state.serializedText = normalized;
            state.hasPendingInvalid = false;
            state.warningMessage = string.Empty;
        }

        private static void UpdateGuidValue(
            SerializedProperty property,
            SerializedProperty lowProperty,
            SerializedProperty highProperty,
            WGuid value,
            string undoLabel
        )
        {
            SerializedObject serializedObject = property.serializedObject;
            UnityEngine.Object[] targets = serializedObject.targetObjects;
            if (targets is { Length: > 0 })
            {
                Undo.RecordObjects(targets, undoLabel);
            }

            Span<byte> buffer = stackalloc byte[16];
            bool success = value.TryWriteBytes(buffer);
            if (!success)
            {
                throw new InvalidOperationException($"Failed to write {nameof(WGuid)} bytes.");
            }

            ulong low = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(0, 8));
            ulong high = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8, 8));
            lowProperty.longValue = unchecked((long)low);
            highProperty.longValue = unchecked((long)high);

            serializedObject.ApplyModifiedProperties();
            serializedObject.UpdateIfRequiredOrScript();
        }

        internal static string ConvertToString(long low, long high)
        {
            Span<byte> buffer = stackalloc byte[16];
            ulong lowUnsigned = unchecked((ulong)low);
            ulong highUnsigned = unchecked((ulong)high);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(0, 8), lowUnsigned);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(8, 8), highUnsigned);
            Guid guid = new(buffer);
            return guid.ToString();
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

        private static GUIContent CreateGenerateContent()
        {
            string tooltip = $"Generate a new {nameof(WGuid)} (v4).";
            GUIContent content = EditorGUIUtility.IconContent("Refresh", tooltip);
            if (content == null)
            {
                return new GUIContent("New", tooltip);
            }

            if (content.image == null && string.IsNullOrEmpty(content.text))
            {
                content.text = "New";
            }

            if (string.IsNullOrEmpty(content.tooltip))
            {
                content.tooltip = tooltip;
            }

            return content;
        }

        private static float GetWarningWidth()
        {
            try
            {
                return EditorGUIUtility.currentViewWidth;
            }
            catch (ArgumentException)
            {
                return 400f;
            }
        }
    }
}
#endif
