// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Settings
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    /// Tests for verifying that <see cref="ColorKeyChangeNotifier"/> correctly detects
    /// and notifies when WButton and WEnumToggleButtons custom color entries are modified.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ColorKeyChangeNotifierTests
    {
        private UnityHelpersSettings _settings;
        private SerializedObject _serializedSettings;
        private HashSet<string> _receivedWButtonKeys;
        private HashSet<string> _receivedWEnumKeys;
        private int _wbuttonEventCount;
        private int _wenumEventCount;

        [SetUp]
        public void SetUp()
        {
            _settings = UnityHelpersSettings.instance;
            _serializedSettings = new SerializedObject(_settings);
            _serializedSettings.UpdateIfRequiredOrScript();
            _receivedWButtonKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _receivedWEnumKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _wbuttonEventCount = 0;
            _wenumEventCount = 0;

            ColorKeyChangeNotifier.ClearCache();
            ColorKeyChangeNotifier.OnWButtonColorKeysChanged += OnWButtonColorKeysChanged;
            ColorKeyChangeNotifier.OnWEnumToggleButtonsColorKeysChanged +=
                OnWEnumToggleButtonsColorKeysChanged;
        }

        [TearDown]
        public void TearDown()
        {
            ColorKeyChangeNotifier.OnWButtonColorKeysChanged -= OnWButtonColorKeysChanged;
            ColorKeyChangeNotifier.OnWEnumToggleButtonsColorKeysChanged -=
                OnWEnumToggleButtonsColorKeysChanged;
            ColorKeyChangeNotifier.ClearCache();

            _serializedSettings?.Dispose();
            _serializedSettings = null;
        }

        private void OnWButtonColorKeysChanged(HashSet<string> changedKeys)
        {
            _wbuttonEventCount++;
            if (changedKeys != null)
            {
                foreach (string key in changedKeys)
                {
                    _receivedWButtonKeys.Add(key);
                }
            }
        }

        private void OnWEnumToggleButtonsColorKeysChanged(HashSet<string> changedKeys)
        {
            _wenumEventCount++;
            if (changedKeys != null)
            {
                foreach (string key in changedKeys)
                {
                    _receivedWEnumKeys.Add(key);
                }
            }
        }

        [Test]
        public void CaptureCurrentStateCapturesExistingWButtonColors()
        {
            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(customColors != null, "WButtonCustomColors property should exist.");

            Assert.DoesNotThrow(() =>
                ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings)
            );
        }

        [Test]
        public void CaptureCurrentStateCapturesExistingWEnumToggleButtonsColors()
        {
            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );
            Assert.IsTrue(
                customColors != null,
                "WEnumToggleButtonsCustomColors property should exist."
            );

            Assert.DoesNotThrow(() =>
                ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings)
            );
        }

        [Test]
        public void NoChangeDoesNotTriggerWButtonEvent()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.AreEqual(
                0,
                _wbuttonEventCount,
                "No WButton event should fire when no colors changed."
            );
        }

        [Test]
        public void NoChangeDoesNotTriggerWEnumEvent()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.AreEqual(
                0,
                _wenumEventCount,
                "No WEnumToggleButtons event should fire when no colors changed."
            );
        }

        [Test]
        public void WButtonColorModificationTriggersEventWithCorrectKey()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            Assert.IsTrue(keys != null, "Keys property should exist.");
            Assert.IsTrue(values != null, "Values property should exist.");

            if (keys.arraySize == 0)
            {
                Assert.Inconclusive(
                    "No WButton custom colors exist to test modification. "
                        + "Ensure Default key exists in settings."
                );
                return;
            }

            SerializedProperty firstKey = keys.GetArrayElementAtIndex(0);
            string colorKey = firstKey.stringValue;
            Assert.IsFalse(string.IsNullOrEmpty(colorKey), "Color key should not be empty.");

            SerializedProperty firstValue = values.GetArrayElementAtIndex(0);
            SerializedProperty buttonColorProp = firstValue.FindPropertyRelative(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
            );
            Assert.IsTrue(buttonColorProp != null, "Button color property should exist.");

            Color originalColor = buttonColorProp.colorValue;
            Color newColor = new(
                1f - originalColor.r,
                1f - originalColor.g,
                1f - originalColor.b,
                originalColor.a
            );
            buttonColorProp.colorValue = newColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.AreEqual(1, _wbuttonEventCount, "WButton event should fire once.");
            Assert.IsTrue(
                _receivedWButtonKeys.Contains(colorKey),
                $"Changed key '{colorKey}' should be in the received keys."
            );

            buttonColorProp.colorValue = originalColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void WEnumToggleButtonsColorModificationTriggersEventWithCorrectKey()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );
            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            Assert.IsTrue(keys != null, "Keys property should exist.");
            Assert.IsTrue(values != null, "Values property should exist.");

            if (keys.arraySize == 0)
            {
                Assert.Inconclusive(
                    "No WEnumToggleButtons custom colors exist to test modification."
                );
                return;
            }

            SerializedProperty firstKey = keys.GetArrayElementAtIndex(0);
            string colorKey = firstKey.stringValue;
            Assert.IsFalse(string.IsNullOrEmpty(colorKey), "Color key should not be empty.");

            SerializedProperty firstValue = values.GetArrayElementAtIndex(0);
            SerializedProperty selectedBgProp = firstValue.FindPropertyRelative(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsSelectedBackground
            );
            Assert.IsTrue(selectedBgProp != null, "Selected background property should exist.");

            Color originalColor = selectedBgProp.colorValue;
            Color newColor = new(
                1f - originalColor.r,
                1f - originalColor.g,
                1f - originalColor.b,
                originalColor.a
            );
            selectedBgProp.colorValue = newColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.AreEqual(1, _wenumEventCount, "WEnumToggleButtons event should fire once.");
            Assert.IsTrue(
                _receivedWEnumKeys.Contains(colorKey),
                $"Changed key '{colorKey}' should be in the received keys."
            );

            selectedBgProp.colorValue = originalColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void ClearCacheResetsState()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);
            ColorKeyChangeNotifier.ClearCache();

            Assert.DoesNotThrow(() =>
                ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings)
            );
        }

        [Test]
        public void NullSerializedObjectDoesNotThrow()
        {
            Assert.DoesNotThrow(() => ColorKeyChangeNotifier.CaptureCurrentState(null));
            Assert.DoesNotThrow(() => ColorKeyChangeNotifier.DetectAndNotifyChanges(null));
        }

        [Test]
        public void RepaintAffectedInspectorsDoesNotThrow()
        {
            Assert.DoesNotThrow(() => ColorKeyChangeNotifier.RepaintAffectedInspectors());
        }

        [Test]
        public void MultipleChangesReportAllChangedKeys()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys.arraySize < 2)
            {
                Assert.Inconclusive(
                    "Need at least 2 WButton custom color entries to test multiple changes."
                );
                return;
            }

            List<Color> originalColors = new();
            List<string> changedKeyNames = new();

            for (int index = 0; index < Math.Min(2, keys.arraySize); index++)
            {
                SerializedProperty keyProp = keys.GetArrayElementAtIndex(index);
                SerializedProperty valueProp = values.GetArrayElementAtIndex(index);
                SerializedProperty buttonColorProp = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                );

                string keyName = keyProp.stringValue;
                Color originalColor = buttonColorProp.colorValue;

                originalColors.Add(originalColor);
                changedKeyNames.Add(keyName);

                Color newColor = new(
                    1f - originalColor.r,
                    1f - originalColor.g,
                    1f - originalColor.b,
                    originalColor.a
                );
                buttonColorProp.colorValue = newColor;
            }

            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.AreEqual(1, _wbuttonEventCount, "WButton event should fire once.");
            foreach (string keyName in changedKeyNames)
            {
                Assert.IsTrue(
                    _receivedWButtonKeys.Contains(keyName),
                    $"Changed key '{keyName}' should be in the received keys."
                );
            }

            for (int index = 0; index < changedKeyNames.Count; index++)
            {
                SerializedProperty valueProp = values.GetArrayElementAtIndex(index);
                SerializedProperty buttonColorProp = valueProp.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                );
                buttonColorProp.colorValue = originalColors[index];
            }

            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void WButtonTextColorChangeTriggersEvent()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys.arraySize == 0)
            {
                Assert.Inconclusive("No WButton custom colors exist to test.");
                return;
            }

            SerializedProperty firstKey = keys.GetArrayElementAtIndex(0);
            string colorKey = firstKey.stringValue;

            SerializedProperty firstValue = values.GetArrayElementAtIndex(0);
            SerializedProperty textColorProp = firstValue.FindPropertyRelative(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
            );
            Assert.IsTrue(textColorProp != null, "Text color property should exist.");

            Color originalColor = textColorProp.colorValue;
            Color newColor = new(
                1f - originalColor.r,
                1f - originalColor.g,
                1f - originalColor.b,
                originalColor.a
            );
            textColorProp.colorValue = newColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.AreEqual(1, _wbuttonEventCount, "WButton event should fire once.");
            Assert.IsTrue(
                _receivedWButtonKeys.Contains(colorKey),
                $"Changed key '{colorKey}' should be in the received keys."
            );

            textColorProp.colorValue = originalColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void WEnumToggleButtonsSelectedTextColorChangeTriggersEvent()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );
            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys.arraySize == 0)
            {
                Assert.Inconclusive("No WEnumToggleButtons custom colors exist to test.");
                return;
            }

            SerializedProperty firstKey = keys.GetArrayElementAtIndex(0);
            string colorKey = firstKey.stringValue;

            SerializedProperty firstValue = values.GetArrayElementAtIndex(0);
            SerializedProperty selectedTextProp = firstValue.FindPropertyRelative(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsSelectedText
            );
            Assert.IsTrue(selectedTextProp != null, "Selected text property should exist.");

            Color originalColor = selectedTextProp.colorValue;
            Color newColor = new(
                1f - originalColor.r,
                1f - originalColor.g,
                1f - originalColor.b,
                originalColor.a
            );
            selectedTextProp.colorValue = newColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.AreEqual(1, _wenumEventCount, "WEnumToggleButtons event should fire once.");
            Assert.IsTrue(
                _receivedWEnumKeys.Contains(colorKey),
                $"Changed key '{colorKey}' should be in the received keys."
            );

            selectedTextProp.colorValue = originalColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void WEnumToggleButtonsInactiveBackgroundColorChangeTriggersEvent()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );
            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys.arraySize == 0)
            {
                Assert.Inconclusive("No WEnumToggleButtons custom colors exist to test.");
                return;
            }

            SerializedProperty firstKey = keys.GetArrayElementAtIndex(0);
            string colorKey = firstKey.stringValue;

            SerializedProperty firstValue = values.GetArrayElementAtIndex(0);
            SerializedProperty inactiveBgProp = firstValue.FindPropertyRelative(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsInactiveBackground
            );
            Assert.IsTrue(inactiveBgProp != null, "Inactive background property should exist.");

            Color originalColor = inactiveBgProp.colorValue;
            Color newColor = new(
                1f - originalColor.r,
                1f - originalColor.g,
                1f - originalColor.b,
                originalColor.a
            );
            inactiveBgProp.colorValue = newColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.AreEqual(1, _wenumEventCount, "WEnumToggleButtons event should fire once.");
            Assert.IsTrue(
                _receivedWEnumKeys.Contains(colorKey),
                $"Changed key '{colorKey}' should be in the received keys."
            );

            inactiveBgProp.colorValue = originalColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void WEnumToggleButtonsInactiveTextColorChangeTriggersEvent()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );
            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys.arraySize == 0)
            {
                Assert.Inconclusive("No WEnumToggleButtons custom colors exist to test.");
                return;
            }

            SerializedProperty firstKey = keys.GetArrayElementAtIndex(0);
            string colorKey = firstKey.stringValue;

            SerializedProperty firstValue = values.GetArrayElementAtIndex(0);
            SerializedProperty inactiveTextProp = firstValue.FindPropertyRelative(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsInactiveText
            );
            Assert.IsTrue(inactiveTextProp != null, "Inactive text property should exist.");

            Color originalColor = inactiveTextProp.colorValue;
            Color newColor = new(
                1f - originalColor.r,
                1f - originalColor.g,
                1f - originalColor.b,
                originalColor.a
            );
            inactiveTextProp.colorValue = newColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.AreEqual(1, _wenumEventCount, "WEnumToggleButtons event should fire once.");
            Assert.IsTrue(
                _receivedWEnumKeys.Contains(colorKey),
                $"Changed key '{colorKey}' should be in the received keys."
            );

            inactiveTextProp.colorValue = originalColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }

        [Test]
        public void SequentialDetectCallsDoNotAccumulateChanges()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);
            Assert.AreEqual(0, _wbuttonEventCount, "No event should fire without changes.");

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);
            Assert.AreEqual(
                0,
                _wbuttonEventCount,
                "No event should fire on second call without changes."
            );
        }

        [Test]
        public void ChangedKeysAreCaseInsensitive()
        {
            ColorKeyChangeNotifier.CaptureCurrentState(_serializedSettings);

            SerializedProperty customColors = _serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            SerializedProperty keys = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customColors.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            if (keys.arraySize == 0)
            {
                Assert.Inconclusive("No WButton custom colors exist to test.");
                return;
            }

            SerializedProperty firstKey = keys.GetArrayElementAtIndex(0);
            string colorKey = firstKey.stringValue;

            SerializedProperty firstValue = values.GetArrayElementAtIndex(0);
            SerializedProperty buttonColorProp = firstValue.FindPropertyRelative(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
            );

            Color originalColor = buttonColorProp.colorValue;
            buttonColorProp.colorValue = new Color(
                1f - originalColor.r,
                1f - originalColor.g,
                1f - originalColor.b,
                originalColor.a
            );
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            ColorKeyChangeNotifier.DetectAndNotifyChanges(_serializedSettings);

            Assert.IsTrue(
                _receivedWButtonKeys.Contains(colorKey.ToUpperInvariant()),
                "Case-insensitive lookup should find the changed key."
            );
            Assert.IsTrue(
                _receivedWButtonKeys.Contains(colorKey.ToLowerInvariant()),
                "Case-insensitive lookup should find the changed key."
            );

            buttonColorProp.colorValue = originalColor;
            _serializedSettings.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
