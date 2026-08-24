// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Settings
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Provides centralized notification when WButton or WEnumToggleButtons custom color entries
    /// are modified in Unity Helpers settings. Inspectors using specific color keys can subscribe
    /// to receive targeted repaint notifications.
    /// </summary>
    internal static class ColorKeyChangeNotifier
    {
        /// <summary>
        /// Fired when one or more WButton custom color entries change.
        /// The HashSet contains the normalized color keys that were modified.
        /// </summary>
        internal static event Action<HashSet<string>> OnWButtonColorKeysChanged;

        /// <summary>
        /// Fired when one or more WEnumToggleButtons custom color entries change.
        /// The HashSet contains the normalized color keys that were modified.
        /// </summary>
        internal static event Action<HashSet<string>> OnWEnumToggleButtonsColorKeysChanged;

        private static readonly Dictionary<string, Color> PreviousWButtonButtonColors = new(
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly Dictionary<string, Color> PreviousWButtonTextColors = new(
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly Dictionary<string, Color> PreviousWEnumSelectedBackgrounds = new(
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly Dictionary<string, Color> PreviousWEnumSelectedTexts = new(
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly Dictionary<string, Color> PreviousWEnumInactiveBackgrounds = new(
            StringComparer.OrdinalIgnoreCase
        );

        private static readonly Dictionary<string, Color> PreviousWEnumInactiveTexts = new(
            StringComparer.OrdinalIgnoreCase
        );

        private static HashSet<string> _changedWButtonKeys;
        private static HashSet<string> _changedWEnumKeys;

        /// <summary>
        /// Captures the current state of custom color dictionaries so we can detect changes later.
        /// Call this before the settings UI is drawn.
        /// </summary>
        internal static void CaptureCurrentState(SerializedObject settingsObject)
        {
            if (settingsObject == null)
            {
                return;
            }

            CaptureWButtonColors(settingsObject);
            CaptureWEnumToggleButtonsColors(settingsObject);
        }

        /// <summary>
        /// Compares current state against the captured snapshot and fires events for any changed keys.
        /// Call this after settings have been applied.
        /// </summary>
        internal static void DetectAndNotifyChanges(SerializedObject settingsObject)
        {
            if (settingsObject == null)
            {
                return;
            }

            _changedWButtonKeys = DetectWButtonChanges(settingsObject);
            _changedWEnumKeys = DetectWEnumToggleButtonsChanges(settingsObject);

            if (_changedWButtonKeys != null && 0 < _changedWButtonKeys.Count)
            {
                OnWButtonColorKeysChanged?.Invoke(_changedWButtonKeys);
            }

            if (_changedWEnumKeys != null && 0 < _changedWEnumKeys.Count)
            {
                OnWEnumToggleButtonsColorKeysChanged?.Invoke(_changedWEnumKeys);
            }

            // Recapture state for next comparison
            CaptureWButtonColors(settingsObject);
            CaptureWEnumToggleButtonsColors(settingsObject);
        }

        /// <summary>
        /// Requests a repaint of all inspector windows that may be affected by color key changes.
        /// </summary>
        internal static void RepaintAffectedInspectors()
        {
            // Use InspectorWindow.RepaintAllInspectors via reflection or simply repaint all
            // EditorWindow.GetWindow can't be used for InspectorWindow directly in all cases
            // Instead, use the tracker approach to get all active editors
            EditorWindow[] allWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            for (int index = 0; index < allWindows.Length; index++)
            {
                EditorWindow window = allWindows[index];
                if (window == null)
                {
                    continue;
                }

                string typeName = window.GetType().Name;
                if (
                    string.Equals(typeName, "InspectorWindow", StringComparison.Ordinal)
                    || string.Equals(typeName, "PropertyEditor", StringComparison.Ordinal)
                )
                {
                    window.Repaint();
                }
            }
        }

        /// <summary>
        /// Clears all cached state. Useful for tests or domain reload scenarios.
        /// </summary>
        internal static void ClearCache()
        {
            PreviousWButtonButtonColors.Clear();
            PreviousWButtonTextColors.Clear();
            PreviousWEnumSelectedBackgrounds.Clear();
            PreviousWEnumSelectedTexts.Clear();
            PreviousWEnumInactiveBackgrounds.Clear();
            PreviousWEnumInactiveTexts.Clear();
            _changedWButtonKeys = null;
            _changedWEnumKeys = null;
        }

        private static void CaptureWButtonColors(SerializedObject settingsObject)
        {
            PreviousWButtonButtonColors.Clear();
            PreviousWButtonTextColors.Clear();

            SerializedProperty customColors = settingsObject.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            if (customColors == null)
            {
                return;
            }

            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys == null || values == null)
            {
                return;
            }

            int count = Mathf.Min(keys.arraySize, values.arraySize);
            for (int index = 0; index < count; index++)
            {
                SerializedProperty keyProp = keys.GetArrayElementAtIndex(index);
                SerializedProperty valueProp = values.GetArrayElementAtIndex(index);

                if (keyProp == null || valueProp == null)
                {
                    continue;
                }

                string key = keyProp.stringValue;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                SerializedProperty buttonColor = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                );
                SerializedProperty textColor = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );

                if (buttonColor != null)
                {
                    PreviousWButtonButtonColors[key] = buttonColor.colorValue;
                }

                if (textColor != null)
                {
                    PreviousWButtonTextColors[key] = textColor.colorValue;
                }
            }
        }

        private static void CaptureWEnumToggleButtonsColors(SerializedObject settingsObject)
        {
            PreviousWEnumSelectedBackgrounds.Clear();
            PreviousWEnumSelectedTexts.Clear();
            PreviousWEnumInactiveBackgrounds.Clear();
            PreviousWEnumInactiveTexts.Clear();

            SerializedProperty customColors = settingsObject.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );

            if (customColors == null)
            {
                return;
            }

            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys == null || values == null)
            {
                return;
            }

            int count = Mathf.Min(keys.arraySize, values.arraySize);
            for (int index = 0; index < count; index++)
            {
                SerializedProperty keyProp = keys.GetArrayElementAtIndex(index);
                SerializedProperty valueProp = values.GetArrayElementAtIndex(index);

                if (keyProp == null || valueProp == null)
                {
                    continue;
                }

                string key = keyProp.stringValue;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                SerializedProperty selectedBg = valueProp.FindPropertyRelative(
                    UnityHelpersSettings
                        .SerializedPropertyNames
                        .WEnumToggleButtonsSelectedBackground
                );
                SerializedProperty selectedText = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsSelectedText
                );
                SerializedProperty inactiveBg = valueProp.FindPropertyRelative(
                    UnityHelpersSettings
                        .SerializedPropertyNames
                        .WEnumToggleButtonsInactiveBackground
                );
                SerializedProperty inactiveText = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsInactiveText
                );

                if (selectedBg != null)
                {
                    PreviousWEnumSelectedBackgrounds[key] = selectedBg.colorValue;
                }

                if (selectedText != null)
                {
                    PreviousWEnumSelectedTexts[key] = selectedText.colorValue;
                }

                if (inactiveBg != null)
                {
                    PreviousWEnumInactiveBackgrounds[key] = inactiveBg.colorValue;
                }

                if (inactiveText != null)
                {
                    PreviousWEnumInactiveTexts[key] = inactiveText.colorValue;
                }
            }
        }

        private static HashSet<string> DetectWButtonChanges(SerializedObject settingsObject)
        {
            HashSet<string> changedKeys = null;

            SerializedProperty customColors = settingsObject.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            if (customColors == null)
            {
                return changedKeys;
            }

            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys == null || values == null)
            {
                return changedKeys;
            }

            HashSet<string> currentKeys = new(StringComparer.OrdinalIgnoreCase);

            int count = Mathf.Min(keys.arraySize, values.arraySize);
            for (int index = 0; index < count; index++)
            {
                SerializedProperty keyProp = keys.GetArrayElementAtIndex(index);
                SerializedProperty valueProp = values.GetArrayElementAtIndex(index);

                if (keyProp == null || valueProp == null)
                {
                    continue;
                }

                string key = keyProp.stringValue;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                currentKeys.Add(key);

                SerializedProperty buttonColor = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                );
                SerializedProperty textColor = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );

                bool buttonChanged = false;
                bool textChanged = false;

                if (buttonColor != null)
                {
                    if (
                        !PreviousWButtonButtonColors.TryGetValue(key, out Color previousButton)
                        || !ColorsEqual(previousButton, buttonColor.colorValue)
                    )
                    {
                        buttonChanged = true;
                    }
                }

                if (textColor != null)
                {
                    if (
                        !PreviousWButtonTextColors.TryGetValue(key, out Color previousText)
                        || !ColorsEqual(previousText, textColor.colorValue)
                    )
                    {
                        textChanged = true;
                    }
                }

                if (buttonChanged || textChanged)
                {
                    changedKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    changedKeys.Add(key);
                }
            }

            // Check for removed keys, across every snapshot. A key whose button colour property was
            // absent at capture time is only in the text map, and sweeping one map missed its removal.
            CollectRemovedKeys(PreviousWButtonButtonColors, currentKeys, ref changedKeys);
            CollectRemovedKeys(PreviousWButtonTextColors, currentKeys, ref changedKeys);

            return changedKeys;
        }

        private static void CollectRemovedKeys(
            Dictionary<string, Color> previous,
            HashSet<string> currentKeys,
            ref HashSet<string> changedKeys
        )
        {
            foreach (string previousKey in previous.Keys)
            {
                if (currentKeys.Contains(previousKey))
                {
                    continue;
                }

                changedKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                changedKeys.Add(previousKey);
            }
        }

        private static HashSet<string> DetectWEnumToggleButtonsChanges(
            SerializedObject settingsObject
        )
        {
            HashSet<string> changedKeys = null;

            SerializedProperty customColors = settingsObject.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );

            if (customColors == null)
            {
                return changedKeys;
            }

            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys == null || values == null)
            {
                return changedKeys;
            }

            HashSet<string> currentKeys = new(StringComparer.OrdinalIgnoreCase);

            int count = Mathf.Min(keys.arraySize, values.arraySize);
            for (int index = 0; index < count; index++)
            {
                SerializedProperty keyProp = keys.GetArrayElementAtIndex(index);
                SerializedProperty valueProp = values.GetArrayElementAtIndex(index);

                if (keyProp == null || valueProp == null)
                {
                    continue;
                }

                string key = keyProp.stringValue;
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                currentKeys.Add(key);

                SerializedProperty selectedBg = valueProp.FindPropertyRelative(
                    UnityHelpersSettings
                        .SerializedPropertyNames
                        .WEnumToggleButtonsSelectedBackground
                );
                SerializedProperty selectedText = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsSelectedText
                );
                SerializedProperty inactiveBg = valueProp.FindPropertyRelative(
                    UnityHelpersSettings
                        .SerializedPropertyNames
                        .WEnumToggleButtonsInactiveBackground
                );
                SerializedProperty inactiveText = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsInactiveText
                );

                bool anyChanged = false;

                if (selectedBg != null)
                {
                    if (
                        !PreviousWEnumSelectedBackgrounds.TryGetValue(key, out Color previous)
                        || !ColorsEqual(previous, selectedBg.colorValue)
                    )
                    {
                        anyChanged = true;
                    }
                }

                if (!anyChanged && selectedText != null)
                {
                    if (
                        !PreviousWEnumSelectedTexts.TryGetValue(key, out Color previous)
                        || !ColorsEqual(previous, selectedText.colorValue)
                    )
                    {
                        anyChanged = true;
                    }
                }

                if (!anyChanged && inactiveBg != null)
                {
                    if (
                        !PreviousWEnumInactiveBackgrounds.TryGetValue(key, out Color previous)
                        || !ColorsEqual(previous, inactiveBg.colorValue)
                    )
                    {
                        anyChanged = true;
                    }
                }

                if (!anyChanged && inactiveText != null)
                {
                    if (
                        !PreviousWEnumInactiveTexts.TryGetValue(key, out Color previous)
                        || !ColorsEqual(previous, inactiveText.colorValue)
                    )
                    {
                        anyChanged = true;
                    }
                }

                if (anyChanged)
                {
                    changedKeys ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    changedKeys.Add(key);
                }
            }

            // Check for removed keys, across every snapshot rather than only the first.
            CollectRemovedKeys(PreviousWEnumSelectedBackgrounds, currentKeys, ref changedKeys);
            CollectRemovedKeys(PreviousWEnumSelectedTexts, currentKeys, ref changedKeys);
            CollectRemovedKeys(PreviousWEnumInactiveBackgrounds, currentKeys, ref changedKeys);
            CollectRemovedKeys(PreviousWEnumInactiveTexts, currentKeys, ref changedKeys);

            return changedKeys;
        }

        /// <remarks>
        /// One definition of "the same colour", shared with the settings file and the style caches.
        /// The absolute tolerance this used to apply answered <c>false</c> for a NaN channel, because
        /// every comparison against NaN is false - so a colour holding one was reported as changed on
        /// every check and the notifier never settled.
        /// </remarks>
        private static bool ColorsEqual(Color a, Color b)
        {
            return ColorQuantization.AreSameColor(a, b);
        }
    }
#endif
}
