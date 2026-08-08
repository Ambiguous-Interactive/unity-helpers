// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomEditors
{
#if UNITY_EDITOR
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using PlatformPropertyNames = WallstopStudios.UnityHelpers.Editor.Sprites.TextureSettingsApplierWindow.PlatformOverrideEntry.SerializedPropertyNames;

    [CustomPropertyDrawer(typeof(TextureSettingsApplierWindow.PlatformOverrideEntry))]
    public sealed class TexturePlatformOverrideEntryDrawer : PropertyDrawer
    {
        private static string[] _cachedChoices;
        private static string[] _lastKnownRef;
        private const string CustomOptionLabel = "Custom";
        private const string MixedValueIndicator = "\u2014";

        private static string[] GetChoices()
        {
            string[] known = TexturePlatformNameHelper.GetKnownPlatformNames();
            if (ReferenceEquals(known, _lastKnownRef) && _cachedChoices != null)
            {
                return _cachedChoices;
            }

            string[] arr = new string[known.Length + 1];
            for (int i = 0; i < known.Length; i++)
            {
                arr[i] = known[i];
            }
            arr[arr.Length - 1] = CustomOptionLabel;
            _lastKnownRef = known;
            _cachedChoices = arr;
            return _cachedChoices;
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            // Layout: 1 line for platform + potential custom name, then each checkbox possibly adds a line
            float h = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            // custom name line (only shown when not mixed and platform resolves to Custom)
            SerializedProperty nameProp = property.FindPropertyRelative(
                PlatformPropertyNames.PlatformName
            );
            string[] choices = GetChoices();
            if (
                !nameProp.hasMultipleDifferentValues
                && GetSelectedIndex(nameProp.stringValue, choices) == choices.Length - 1
            )
            {
                h += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }
            // For each apply field, add one line; if true, add an extra line for its value
            h += LineCount(property, PlatformPropertyNames.ApplyResizeAlgorithm, true);
            h += LineCount(property, PlatformPropertyNames.ApplyMaxTextureSize, true);
            h += LineCount(property, PlatformPropertyNames.ApplyFormat, true);
            h += LineCount(property, PlatformPropertyNames.ApplyCompression, true);
            h += LineCount(property, PlatformPropertyNames.ApplyCrunchCompression, true);
            return h;
        }

        private static float LineCount(
            SerializedProperty property,
            string applyName,
            bool includeValue
        )
        {
            float h = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            SerializedProperty apply = property.FindPropertyRelative(applyName);
            if (apply.boolValue && includeValue)
            {
                h += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            }
            return h;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            Rect r = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            SerializedProperty nameProp = property.FindPropertyRelative(
                PlatformPropertyNames.PlatformName
            );
            string[] choices = GetChoices();
            bool isMixed = nameProp.hasMultipleDifferentValues;
            int idx = GetSelectedIndex(nameProp.stringValue, choices);
            string currentDisplay =
                isMixed ? MixedValueIndicator
                : idx >= 0 && idx < choices.Length ? choices[idx]
                : string.Empty;

            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = isMixed;

            Rect labelRect = new(r.x, r.y, EditorGUIUtility.labelWidth, r.height);
            Rect fieldRect = new(
                r.x + EditorGUIUtility.labelWidth + 2f,
                r.y,
                r.width - EditorGUIUtility.labelWidth - 2f,
                r.height
            );
            EditorGUI.LabelField(labelRect, "Platform");
            if (GUI.Button(fieldRect, currentDisplay, EditorStyles.popup))
            {
                SerializedObject serializedObject = property.serializedObject;
                string propertyPath = nameProp.propertyPath;
                int currentIndex = idx;
                bool wasMixed = isMixed;

                GenericMenu menu = new();
                for (int i = 0; i < choices.Length; i++)
                {
                    int capturedIndex = i;
                    bool isSelected = !wasMixed && i == currentIndex;
                    menu.AddItem(
                        new GUIContent(choices[i]),
                        isSelected,
                        () =>
                        {
                            Undo.RecordObjects(
                                serializedObject.targetObjects,
                                "Change Platform Override"
                            );
                            serializedObject.Update();
                            SerializedProperty prop = serializedObject.FindProperty(propertyPath);
                            if (prop == null)
                            {
                                return;
                            }

                            string value = choices[capturedIndex];
                            if (value == CustomOptionLabel)
                            {
                                prop.stringValue = string.Empty;
                            }
                            else
                            {
                                prop.stringValue = value;
                            }
                            serializedObject.ApplyModifiedProperties();
                        }
                    );
                }
                menu.DropDown(fieldRect);
            }

            EditorGUI.showMixedValue = previousMixed;

            if (!isMixed && idx == choices.Length - 1)
            {
                r.y += r.height + EditorGUIUtility.standardVerticalSpacing;
                EditorGUI.BeginChangeCheck();
                string newCustomName = EditorGUI.TextField(r, "Custom Name", nameProp.stringValue);
                if (EditorGUI.EndChangeCheck())
                {
                    nameProp.stringValue = newCustomName;
                }
            }

            DrawToggleWithValue(
                property,
                ref r,
                PlatformPropertyNames.ApplyResizeAlgorithm,
                PlatformPropertyNames.ResizeAlgorithm,
                "Resize Algorithm"
            );
            DrawToggleWithValue(
                property,
                ref r,
                PlatformPropertyNames.ApplyMaxTextureSize,
                PlatformPropertyNames.MaxTextureSize,
                "Max Texture Size"
            );
            DrawToggleWithValue(
                property,
                ref r,
                PlatformPropertyNames.ApplyFormat,
                PlatformPropertyNames.Format,
                "Format"
            );
            DrawToggleWithValue(
                property,
                ref r,
                PlatformPropertyNames.ApplyCompression,
                PlatformPropertyNames.Compression,
                "Compression"
            );
            DrawToggleWithValue(
                property,
                ref r,
                PlatformPropertyNames.ApplyCrunchCompression,
                PlatformPropertyNames.UseCrunchCompression,
                "Use Crunch Compression"
            );

            EditorGUI.EndProperty();
        }

        private static void DrawToggleWithValue(
            SerializedProperty property,
            ref Rect r,
            string applyName,
            string valueName,
            string label
        )
        {
            SerializedProperty apply = property.FindPropertyRelative(applyName);
            SerializedProperty val = property.FindPropertyRelative(valueName);

            r.y += r.height + EditorGUIUtility.standardVerticalSpacing;
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = apply.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            bool newApplyValue = EditorGUI.ToggleLeft(r, label, apply.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                apply.boolValue = newApplyValue;
            }
            EditorGUI.showMixedValue = previousMixed;
            if (apply.boolValue)
            {
                r.y += r.height + EditorGUIUtility.standardVerticalSpacing;
                using (IndentLevelScope.Indent())
                {
                    EditorGUI.PropertyField(r, val, GUIContent.none);
                }
            }
        }

        private static int GetSelectedIndex(string name, string[] choices)
        {
            if (name == null)
            {
                // Note: Unreachable through SerializedProperty.stringValue (which converts
                // null to ""), but kept as defensive code in case the method is made internal.
                return 0;
            }

            if (name.Length == 0)
            {
                return choices.Length - 1; // Empty string means Custom (user explicitly selected Custom)
            }

            for (int i = 0; i < choices.Length - 1; i++)
            {
                if (choices[i] == name)
                {
                    return i;
                }
            }
            return choices.Length - 1; // Unknown platform treated as Custom
        }
    }
#endif
}
