// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Sirenix.OdinInspector.Editor;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;

    /// <summary>
    /// Odin Inspector attribute drawer for <see cref="IntDropDownAttribute"/>.
    /// Renders integer fields decorated with IntDropDown as dropdown selectors.
    /// </summary>
    /// <remarks>
    /// This drawer ensures IntDropDown works correctly when Odin Inspector is installed
    /// and classes derive from SerializedMonoBehaviour or SerializedScriptableObject,
    /// where Unity's standard PropertyDrawer system is bypassed.
    /// </remarks>
    public sealed class IntDropDownOdinDrawer : OdinAttributeDrawer<IntDropDownAttribute>
    {
        private static readonly Dictionary<int, string[]> DisplayOptionsCache = new();

        /// <summary>
        /// Clears all cached state. Called during domain reload via
        /// <see cref="Internal.EditorCacheManager.ClearAllCaches"/>.
        /// </summary>
        internal static void ClearCache()
        {
            DisplayOptionsCache.Clear();
        }

        /// <summary>
        /// Draws the property as a dropdown selector with the integer options provided by the attribute.
        /// </summary>
        /// <param name="label">The label to display for the property.</param>
        protected override void DrawPropertyLayout(GUIContent label)
        {
            IntDropDownAttribute intDropDown = Attribute;
            if (intDropDown == null)
            {
                CallNextDrawer(label);
                return;
            }

            Type valueType = Property.ValueEntry?.TypeOfValue;
            if (valueType != typeof(int))
            {
                EditorGUILayout.HelpBox(
                    $"[IntDropDown] Type mismatch: field is {valueType?.Name ?? "unknown"}, but IntDropDown requires int.",
                    MessageType.Error
                );
                return;
            }

            object parentValue = Property.Parent?.ValueEntry?.WeakSmartValue;
            int[] options = intDropDown.GetOptions(parentValue) ?? Array.Empty<int>();

            if (options.Length == 0)
            {
                EditorGUILayout.HelpBox("No options available for IntDropDown.", MessageType.Info);
                return;
            }

            // Check for mixed values BEFORE any calculations
            bool hasMultipleDifferentValues = false;
            if (Property.ValueEntry.ValueCount > 1)
            {
                object firstValue = Property.ValueEntry.WeakValues[0];
                for (int i = 1; i < Property.ValueEntry.ValueCount; i++)
                {
                    if (!Equals(firstValue, Property.ValueEntry.WeakValues[i]))
                    {
                        hasMultipleDifferentValues = true;
                        break;
                    }
                }
            }

            // Set showMixedValue FIRST, before any index calculations
            bool previousMixed = EditorGUI.showMixedValue;
            EditorGUI.showMixedValue = hasMultipleDifferentValues;

            object currentValue = Property.ValueEntry?.WeakSmartValue;
            int currentInt = currentValue is int intValue ? intValue : 0;
            int currentIndex = Array.IndexOf(options, currentInt);

            string[] displayOptions = GetOrCreateDisplayOptions(options);

            Rect controlRect = EditorGUILayout.GetControlRect(
                true,
                EditorGUIUtility.singleLineHeight
            );

            try
            {
                DrawPopupDropDown(
                    controlRect,
                    label,
                    options,
                    displayOptions,
                    currentIndex,
                    currentInt,
                    hasMultipleDifferentValues
                );
            }
            finally
            {
                EditorGUI.showMixedValue = previousMixed;
            }
        }

        private void DrawPopupDropDown(
            Rect position,
            GUIContent label,
            int[] options,
            string[] displayOptions,
            int currentIndex,
            int currentInt,
            bool hasMultipleDifferentValues
        )
        {
            Rect labelRect = new(
                position.x,
                position.y,
                EditorGUIUtility.labelWidth,
                position.height
            );

            Rect fieldRect = new(
                position.x + EditorGUIUtility.labelWidth + 2f,
                position.y,
                position.width - EditorGUIUtility.labelWidth - 2f,
                position.height
            );

            if (label != null && label != GUIContent.none)
            {
                EditorGUI.LabelField(labelRect, label);
            }

            // Determine display value without modifying property
            string displayValue;
            if (hasMultipleDifferentValues)
            {
                displayValue = "\u2014"; // Em dash for mixed values
            }
            else if (currentIndex >= 0 && currentIndex < displayOptions.Length)
            {
                displayValue = displayOptions[currentIndex];
            }
            else
            {
                // Invalid value - show it but don't clamp
                displayValue = DropDownShared.GetCachedIntString(currentInt) + " (Invalid)";
            }

            if (GUI.Button(fieldRect, displayValue, EditorStyles.popup))
            {
                ShowPopupMenu(
                    fieldRect,
                    options,
                    displayOptions,
                    currentIndex,
                    hasMultipleDifferentValues
                );
            }
        }

        private void ShowPopupMenu(
            Rect buttonRect,
            int[] options,
            string[] displayOptions,
            int currentIndex,
            bool hasMultipleDifferentValues
        )
        {
            GenericMenu menu = new();
            for (int i = 0; i < options.Length; i++)
            {
                int capturedIndex = i;
                bool isSelected = i == currentIndex && !hasMultipleDifferentValues;
                menu.AddItem(
                    new GUIContent(displayOptions[i]),
                    isSelected,
                    () => ApplySelection(options[capturedIndex])
                );
            }
            menu.DropDown(buttonRect);
        }

        private void ApplySelection(int value)
        {
            // Record Undo for ALL selected objects, filtering out non-Unity targets
            IList weakTargets = Property.Tree.WeakTargets;
            List<UnityEngine.Object> validTargets = new(weakTargets.Count);
            for (int i = 0; i < weakTargets.Count; i++)
            {
                if (weakTargets[i] is UnityEngine.Object unityObject && unityObject != null)
                {
                    validTargets.Add(unityObject);
                }
            }

            if (validTargets.Count > 0)
            {
                Undo.RecordObjects(validTargets.ToArray(), "Change IntDropDown Selection");
            }

            Property.ValueEntry.WeakSmartValue = value;
        }

        internal static string[] GetOrCreateDisplayOptions(int[] options)
        {
            if (options == null || options.Length == 0)
            {
                return Array.Empty<string>();
            }

            int hashCode = DropDownShared.ComputeOptionsHash(options);
            if (DisplayOptionsCache.TryGetValue(hashCode, out string[] cached))
            {
                if (cached.Length == options.Length)
                {
                    bool match = true;
                    for (int i = 0; i < options.Length && match; i++)
                    {
                        if (
                            !string.Equals(
                                cached[i],
                                DropDownShared.GetCachedIntString(options[i]),
                                StringComparison.Ordinal
                            )
                        )
                        {
                            match = false;
                        }
                    }
                    if (match)
                    {
                        return cached;
                    }
                }
            }

            string[] displayOptions = new string[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                displayOptions[i] = DropDownShared.GetCachedIntString(options[i]);
            }
            DisplayOptionsCache[hashCode] = displayOptions;
            return displayOptions;
        }
    }
#endif
}
