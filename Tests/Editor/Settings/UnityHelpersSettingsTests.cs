// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Extensions;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Settings;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class UnityHelpersSettingsTests
    {
        [Test]
        public void SaveSettingsPropagatesRegexConfiguration()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            IReadOnlyList<string> originalPatterns = settings.GetSerializableTypeIgnorePatterns();
            bool wasConfigured = !ReferenceEquals(
                SerializableTypeCatalog.GetActiveIgnorePatterns(),
                SerializableTypeCatalog.GetDefaultIgnorePatterns()
            );
            string[] backup = originalPatterns?.ToArray() ?? Array.Empty<string>();

            try
            {
                using SerializedObject serializedSettings = new(settings);
                SerializedProperty patternsProperty = serializedSettings.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.SerializableTypeIgnorePatterns
                );
                patternsProperty.ClearArray();
                patternsProperty.InsertArrayElementAtIndex(0);
                SerializedProperty patternElement = patternsProperty.GetArrayElementAtIndex(0);
                patternElement
                    .FindPropertyRelative(
                        UnityHelpersSettings.SerializedPropertyNames.SerializableTypePattern
                    )
                    .stringValue = "^System\\.Int32$";
                SerializedProperty initializedProperty = serializedSettings.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.SerializableTypePatternsInitialized
                );
                initializedProperty.boolValue = true;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();

                settings.SaveSettings();

                Assert.IsTrue(
                    SerializableTypeCatalog.ShouldSkipType(typeof(int)),
                    "Configured regex should exclude System.Int32 from the catalog."
                );
                Assert.IsFalse(
                    SerializableTypeCatalog.ShouldSkipType(typeof(string)),
                    "Other types should remain available when only System.Int32 is ignored."
                );
            }
            finally
            {
                using SerializedObject restore = new(settings);
                SerializedProperty patternsProperty = restore.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.SerializableTypeIgnorePatterns
                );
                patternsProperty.ClearArray();
                for (int index = 0; index < backup.Length; index++)
                {
                    patternsProperty.InsertArrayElementAtIndex(index);
                    SerializedProperty element = patternsProperty.GetArrayElementAtIndex(index);
                    element
                        .FindPropertyRelative(
                            UnityHelpersSettings.SerializedPropertyNames.SerializableTypePattern
                        )
                        .stringValue = backup[index];
                }

                SerializedProperty initializedProperty = restore.FindProperty(
                    UnityHelpersSettings.SerializedPropertyNames.SerializableTypePatternsInitialized
                );
                initializedProperty.boolValue = true;
                restore.ApplyModifiedPropertiesWithoutUndo();

                settings.SaveSettings();

                SerializableTypeCatalog.ConfigureTypeNameIgnorePatterns(
                    wasConfigured ? backup : null
                );
            }
        }

        [Test]
        public void WButtonSettingsClampToBounds()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalPageSize = settings.WButtonPageSize;
            int originalHistorySize = settings.WButtonHistorySize;
            try
            {
                settings.WButtonPageSize = UnityHelpersSettings.MinPageSize - 100;
                settings.WButtonHistorySize = UnityHelpersSettings.MinWButtonHistorySize - 5;

                Assert.That(
                    UnityHelpersSettings.GetWButtonPageSize(),
                    Is.EqualTo(UnityHelpersSettings.MinPageSize)
                );
                Assert.That(
                    UnityHelpersSettings.GetWButtonHistorySize(),
                    Is.EqualTo(UnityHelpersSettings.MinWButtonHistorySize)
                );

                settings.WButtonPageSize = UnityHelpersSettings.MaxPageSize + 250;
                settings.WButtonHistorySize = UnityHelpersSettings.MaxWButtonHistorySize + 25;

                Assert.That(
                    UnityHelpersSettings.GetWButtonPageSize(),
                    Is.EqualTo(UnityHelpersSettings.MaxPageSize)
                );
                Assert.That(
                    UnityHelpersSettings.GetWButtonHistorySize(),
                    Is.EqualTo(UnityHelpersSettings.MaxWButtonHistorySize)
                );
            }
            finally
            {
                settings.WButtonPageSize = originalPageSize;
                settings.WButtonHistorySize = originalHistorySize;
            }
        }

        [Test]
        public void SerializableDictionaryPageSizeClampsToBounds()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalDictionaryPageSize = settings.SerializableDictionaryPageSize;
            try
            {
                settings.SerializableDictionaryPageSize = UnityHelpersSettings.MinPageSize - 1;
                Assert.That(
                    UnityHelpersSettings.GetSerializableDictionaryPageSize(),
                    Is.EqualTo(UnityHelpersSettings.MinPageSize)
                );

                settings.SerializableDictionaryPageSize =
                    UnityHelpersSettings.MaxSerializableDictionaryPageSize + 25;
                Assert.That(
                    UnityHelpersSettings.GetSerializableDictionaryPageSize(),
                    Is.EqualTo(UnityHelpersSettings.MaxSerializableDictionaryPageSize)
                );
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalDictionaryPageSize;
            }
        }

        [Test]
        public void DictionaryAndSetPaginationSettingsRemainIndependent()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            int originalDictionaryPageSize = settings.SerializableDictionaryPageSize;
            int originalSetPageSize = settings.SerializableSetPageSize;

            try
            {
                int dictionarySize = UnityHelpersSettings.MinPageSize + 6;
                int setSize = UnityHelpersSettings.MaxPageSize - 10;

                settings.SerializableDictionaryPageSize = dictionarySize;
                settings.SerializableSetPageSize = setSize;

                Assert.That(
                    UnityHelpersSettings.GetSerializableDictionaryPageSize(),
                    Is.EqualTo(dictionarySize)
                );
                Assert.That(UnityHelpersSettings.GetSerializableSetPageSize(), Is.EqualTo(setSize));
            }
            finally
            {
                settings.SerializableDictionaryPageSize = originalDictionaryPageSize;
                settings.SerializableSetPageSize = originalSetPageSize;
            }
        }

        [Test]
        public void SerializableTypeIgnorePatternsCanBeAppendedAndReset()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty patterns = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.SerializableTypeIgnorePatterns
            );
            Assert.NotNull(patterns);

            patterns.ClearArray();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Update();

            SerializedProperty element = patterns.AppendArrayElement();
            element
                .FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.SerializableTypePattern
                )
                .stringValue = "^CustomPattern$";
            serialized.ApplyModifiedPropertiesWithoutUndo();

            serialized.Update();
            Assert.That(patterns.arraySize, Is.EqualTo(1));

            patterns.ClearArray();
            IReadOnlyList<string> defaults = SerializableTypeCatalog.GetDefaultIgnorePatterns();
            for (int index = 0; index < defaults.Count; index++)
            {
                SerializedProperty defaultElement = patterns.AppendArrayElement();
                defaultElement
                    .FindPropertyRelative(
                        UnityHelpersSettings.SerializedPropertyNames.SerializableTypePattern
                    )
                    .stringValue = defaults[index];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Update();

            Assert.That(patterns.arraySize, Is.EqualTo(defaults.Count));
        }

        [Test]
        public void SerializableCollectionFoldoutDefaultsAreConfigurable()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalDictionarySetting = settings.SerializableDictionaryStartCollapsed;
            bool originalSetSetting = settings.SerializableSetStartCollapsed;

            try
            {
                settings.SerializableDictionaryStartCollapsed = false;
                settings.SerializableSetStartCollapsed = false;

                Assert.IsFalse(UnityHelpersSettings.ShouldStartSerializableDictionaryCollapsed());
                Assert.IsFalse(UnityHelpersSettings.ShouldStartSerializableSetCollapsed());

                settings.SerializableDictionaryStartCollapsed = true;
                settings.SerializableSetStartCollapsed = true;

                Assert.IsTrue(UnityHelpersSettings.ShouldStartSerializableDictionaryCollapsed());
                Assert.IsTrue(UnityHelpersSettings.ShouldStartSerializableSetCollapsed());
            }
            finally
            {
                settings.SerializableDictionaryStartCollapsed = originalDictionarySetting;
                settings.SerializableSetStartCollapsed = originalSetSetting;
            }
        }

        [Test]
        public void WGroupFoldoutDefaultIsConfigurable()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool originalSetting = settings.WGroupFoldoutsStartCollapsed;

            try
            {
                settings.WGroupFoldoutsStartCollapsed = true;
                Assert.IsTrue(UnityHelpersSettings.ShouldStartWGroupCollapsed());

                settings.WGroupFoldoutsStartCollapsed = false;
                Assert.IsFalse(UnityHelpersSettings.ShouldStartWGroupCollapsed());
            }
            finally
            {
                settings.WGroupFoldoutsStartCollapsed = originalSetting;
            }
        }

        [Test]
        public void ResolveWButtonPaletteFallsBackToDefault()
        {
            UnityHelpersSettings.WButtonPaletteEntry defaultEntry =
                UnityHelpersSettings.ResolveWButtonPalette(
                    UnityHelpersSettings.DefaultWButtonColorKey
                );
            UnityHelpersSettings.WButtonPaletteEntry missingEntry =
                UnityHelpersSettings.ResolveWButtonPalette("NonExistentColorKey");
            UnityHelpersSettings.WButtonPaletteEntry lightThemeEntry =
                UnityHelpersSettings.ResolveWButtonPalette(
                    UnityHelpersSettings.WButtonLightThemeColorKey
                );
            UnityHelpersSettings.WButtonPaletteEntry darkThemeEntry =
                UnityHelpersSettings.ResolveWButtonPalette(
                    UnityHelpersSettings.WButtonDarkThemeColorKey
                );

            Color expectedButton = EditorGUIUtility.isProSkin
                ? new Color(0.35f, 0.35f, 0.35f, 1f)
                : new Color(0.78f, 0.78f, 0.78f, 1f);
            Color expectedText = EditorGUIUtility.isProSkin ? Color.white : Color.black;
            Color expectedLightButton = new(0.78f, 0.78f, 0.78f, 1f);
            Color expectedDarkButton = new(0.35f, 0.35f, 0.35f, 1f);

            AssertColorsApproximately(expectedButton, defaultEntry.ButtonColor);
            AssertColorsApproximately(expectedText, defaultEntry.TextColor);
            AssertColorsApproximately(expectedButton, missingEntry.ButtonColor);
            AssertColorsApproximately(expectedText, missingEntry.TextColor);
            AssertColorsApproximately(expectedLightButton, lightThemeEntry.ButtonColor);
            AssertColorsApproximately(Color.black, lightThemeEntry.TextColor);
            AssertColorsApproximately(expectedDarkButton, darkThemeEntry.ButtonColor);
            AssertColorsApproximately(Color.white, darkThemeEntry.TextColor);
            Assert.IsTrue(
                UnityHelpersSettings.HasWButtonPaletteColorKey(
                    UnityHelpersSettings.WButtonLightThemeColorKey
                )
            );
            Assert.IsTrue(
                UnityHelpersSettings.HasWButtonPaletteColorKey(
                    UnityHelpersSettings.WButtonDarkThemeColorKey
                )
            );
            Assert.IsTrue(
                UnityHelpersSettings.HasWButtonPaletteColorKey(
                    UnityHelpersSettings.WButtonLegacyColorKey
                )
            );
        }

        [Test]
        public void LegacyPaletteMigratesIntoCustomColors()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty legacyPalette = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.LegacyWButtonPriorityColors
            );
            SerializedProperty customPalette = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            SerializedProperty keys = customPalette.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = customPalette.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            List<(string Key, Color Button, Color Text)> originalEntries = new(keys.arraySize);
            for (int index = 0; index < keys.arraySize; index++)
            {
                SerializedProperty keyProperty = keys.GetArrayElementAtIndex(index);
                SerializedProperty valueProperty = values.GetArrayElementAtIndex(index);
                SerializedProperty buttonColorProperty = valueProperty.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                );
                SerializedProperty textColorProperty = valueProperty.FindPropertyRelative(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );
                originalEntries.Add(
                    (
                        keyProperty.stringValue,
                        buttonColorProperty.colorValue,
                        textColorProperty.colorValue
                    )
                );
            }

            List<(string Key, Color Button, Color Text)> legacyEntries = new(
                legacyPalette != null ? legacyPalette.arraySize : 0
            );
            if (legacyPalette != null)
            {
                for (int index = 0; index < legacyPalette.arraySize; index++)
                {
                    SerializedProperty element = legacyPalette.GetArrayElementAtIndex(index);
                    SerializedProperty keyProperty = element.FindPropertyRelative(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonPriority
                    );
                    SerializedProperty buttonColorProperty = element.FindPropertyRelative(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                    );
                    SerializedProperty textColorProperty = element.FindPropertyRelative(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                    );
                    legacyEntries.Add(
                        (
                            keyProperty.stringValue,
                            buttonColorProperty.colorValue,
                            textColorProperty.colorValue
                        )
                    );
                }
            }

            Color legacyButton = new(0.1f, 0.6f, 0.9f, 1f);
            Color expectedText = WButtonColorUtility.GetReadableTextColor(legacyButton);

            try
            {
                if (legacyPalette != null)
                {
                    legacyPalette.arraySize = 1;
                    SerializedProperty element = legacyPalette.GetArrayElementAtIndex(0);
                    element
                        .FindPropertyRelative(
                            UnityHelpersSettings.SerializedPropertyNames.WButtonPriority
                        )
                        .stringValue = "LegacyKey";
                    element
                        .FindPropertyRelative(
                            UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                        )
                        .colorValue = legacyButton;
                    element
                        .FindPropertyRelative(
                            UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                        )
                        .colorValue = Color.clear;
                }

                keys.arraySize = 0;
                values.arraySize = 0;

                serialized.ApplyModifiedPropertiesWithoutUndo();

                settings.OnEnable();

                UnityHelpersSettings.WButtonPaletteEntry migrated =
                    UnityHelpersSettings.ResolveWButtonPalette("LegacyKey");
                Assert.That(migrated.ButtonColor, Is.EqualTo(legacyButton));
                Assert.That(migrated.TextColor, Is.EqualTo(expectedText));
            }
            finally
            {
                serialized.Update();

                if (legacyPalette != null)
                {
                    legacyPalette.arraySize = legacyEntries.Count;
                    for (int index = 0; index < legacyEntries.Count; index++)
                    {
                        SerializedProperty element = legacyPalette.GetArrayElementAtIndex(index);
                        (string Key, Color Button, Color Text) originalLegacy = legacyEntries[
                            index
                        ];
                        element
                            .FindPropertyRelative(
                                UnityHelpersSettings.SerializedPropertyNames.WButtonPriority
                            )
                            .stringValue = originalLegacy.Key;
                        element
                            .FindPropertyRelative(
                                UnityHelpersSettings
                                    .SerializedPropertyNames
                                    .WButtonCustomColorButton
                            )
                            .colorValue = originalLegacy.Button;
                        element
                            .FindPropertyRelative(
                                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                            )
                            .colorValue = originalLegacy.Text;
                    }
                }

                keys.arraySize = originalEntries.Count;
                values.arraySize = originalEntries.Count;
                for (int index = 0; index < originalEntries.Count; index++)
                {
                    (string Key, Color Button, Color Text) original = originalEntries[index];
                    SerializedProperty keyProperty = keys.GetArrayElementAtIndex(index);
                    keyProperty.stringValue = original.Key;
                    SerializedProperty valueProperty = values.GetArrayElementAtIndex(index);
                    valueProperty
                        .FindPropertyRelative(
                            UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                        )
                        .colorValue = original.Button;
                    valueProperty
                        .FindPropertyRelative(
                            UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                        )
                        .colorValue = original.Text;
                }

                serialized.ApplyModifiedPropertiesWithoutUndo();
                settings.SaveSettings();
            }
        }

        [Test]
        public void WButtonPlacementDefaultsToTop()
        {
            Assert.That(
                UnityHelpersSettings.GetWButtonActionsPlacement(),
                Is.EqualTo(UnityHelpersSettings.WButtonActionsPlacement.Top)
            );
        }

        [Test]
        public void WButtonFoldoutBehaviorDefaultsToStartExpanded()
        {
            Assert.That(
                UnityHelpersSettings.GetWButtonFoldoutBehavior(),
                Is.EqualTo(UnityHelpersSettings.WButtonFoldoutBehavior.StartExpanded)
            );
        }

        [Test]
        public void FoldoutSpeedDefaultsMatchConstants()
        {
            Assert.That(
                UnityHelpersSettings.GetWButtonFoldoutSpeed(),
                Is.EqualTo(UnityHelpersSettings.DefaultFoldoutSpeed)
            );
            Assert.That(
                UnityHelpersSettings.GetSerializableDictionaryFoldoutSpeed(),
                Is.EqualTo(UnityHelpersSettings.DefaultFoldoutSpeed)
            );
            Assert.That(
                UnityHelpersSettings.GetSerializableSortedDictionaryFoldoutSpeed(),
                Is.EqualTo(UnityHelpersSettings.DefaultFoldoutSpeed)
            );
            Assert.That(
                UnityHelpersSettings.GetSerializableSetFoldoutSpeed(),
                Is.EqualTo(UnityHelpersSettings.DefaultFoldoutSpeed)
            );
            Assert.That(
                UnityHelpersSettings.GetSerializableSortedSetFoldoutSpeed(),
                Is.EqualTo(UnityHelpersSettings.DefaultFoldoutSpeed)
            );
            Assert.That(
                UnityHelpersSettings.GetWGroupFoldoutSpeed(),
                Is.EqualTo(UnityHelpersSettings.DefaultFoldoutSpeed)
            );
            Assert.That(
                UnityHelpersSettings.GetInlineEditorFoldoutSpeed(),
                Is.EqualTo(UnityHelpersSettings.DefaultFoldoutSpeed)
            );
        }

        [Test]
        public void FoldoutTweensEnabledByDefault()
        {
            Assert.IsTrue(UnityHelpersSettings.ShouldTweenWButtonFoldouts());
            Assert.IsTrue(UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts());
            Assert.IsTrue(UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts());
            Assert.IsTrue(UnityHelpersSettings.ShouldTweenSerializableSetFoldouts());
            Assert.IsTrue(UnityHelpersSettings.ShouldTweenSerializableSortedSetFoldouts());
            Assert.IsTrue(UnityHelpersSettings.ShouldTweenWGroupFoldouts());
            Assert.IsTrue(UnityHelpersSettings.ShouldTweenInlineEditorFoldouts());
        }

        [Test]
        public void FoldoutTweenDefaultsRepairWhenUninitialized()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty wbuttonTweenProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonFoldoutTweenEnabled
            );
            SerializedProperty dictionaryTweenProperty = serialized.FindProperty(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableDictionaryFoldoutTweenEnabled
            );
            SerializedProperty sortedDictionaryTweenProperty = serialized.FindProperty(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedDictionaryFoldoutTweenEnabled
            );
            SerializedProperty setTweenProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.SerializableSetFoldoutTweenEnabled
            );
            SerializedProperty sortedSetTweenProperty = serialized.FindProperty(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedSetFoldoutTweenEnabled
            );
            SerializedProperty initializedProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.FoldoutTweenSettingsInitialized
            );

            bool originalWButton = wbuttonTweenProperty.boolValue;
            bool originalDictionary = dictionaryTweenProperty.boolValue;
            bool originalSorted = sortedDictionaryTweenProperty.boolValue;
            bool originalSet = setTweenProperty.boolValue;
            bool originalSortedSet = sortedSetTweenProperty.boolValue;
            bool originalInitialized = initializedProperty.boolValue;

            try
            {
                wbuttonTweenProperty.boolValue = false;
                dictionaryTweenProperty.boolValue = false;
                sortedDictionaryTweenProperty.boolValue = false;
                setTweenProperty.boolValue = false;
                sortedSetTweenProperty.boolValue = false;
                initializedProperty.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                settings.OnEnable();

                Assert.IsTrue(UnityHelpersSettings.ShouldTweenWButtonFoldouts());
                Assert.IsTrue(UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts());
                Assert.IsTrue(
                    UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts()
                );
                Assert.IsTrue(UnityHelpersSettings.ShouldTweenSerializableSetFoldouts());
                Assert.IsTrue(UnityHelpersSettings.ShouldTweenSerializableSortedSetFoldouts());

                using SerializedObject verification = new(settings);
                verification.Update();
                Assert.IsTrue(
                    verification
                        .FindProperty(
                            UnityHelpersSettings
                                .SerializedPropertyNames
                                .FoldoutTweenSettingsInitialized
                        )
                        .boolValue
                );
            }
            finally
            {
                using SerializedObject restore = new(settings);
                restore
                    .FindProperty(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonFoldoutTweenEnabled
                    )
                    .boolValue = originalWButton;
                restore
                    .FindProperty(
                        UnityHelpersSettings
                            .SerializedPropertyNames
                            .SerializableDictionaryFoldoutTweenEnabled
                    )
                    .boolValue = originalDictionary;
                restore
                    .FindProperty(
                        UnityHelpersSettings
                            .SerializedPropertyNames
                            .SerializableSortedDictionaryFoldoutTweenEnabled
                    )
                    .boolValue = originalSorted;
                restore
                    .FindProperty(
                        UnityHelpersSettings
                            .SerializedPropertyNames
                            .SerializableSetFoldoutTweenEnabled
                    )
                    .boolValue = originalSet;
                restore
                    .FindProperty(
                        UnityHelpersSettings
                            .SerializedPropertyNames
                            .SerializableSortedSetFoldoutTweenEnabled
                    )
                    .boolValue = originalSortedSet;
                restore
                    .FindProperty(
                        UnityHelpersSettings.SerializedPropertyNames.FoldoutTweenSettingsInitialized
                    )
                    .boolValue = originalInitialized;
                restore.ApplyModifiedPropertiesWithoutUndo();
                settings.SaveSettings();
            }
        }

        [Test]
        public void FoldoutTweenTogglesAffectBehavior()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            SerializedProperty wbuttonTweenProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonFoldoutTweenEnabled
            );
            SerializedProperty dictionaryTweenProperty = serialized.FindProperty(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableDictionaryFoldoutTweenEnabled
            );
            SerializedProperty sortedDictionaryTweenProperty = serialized.FindProperty(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedDictionaryFoldoutTweenEnabled
            );
            SerializedProperty setTweenProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.SerializableSetFoldoutTweenEnabled
            );
            SerializedProperty sortedSetTweenProperty = serialized.FindProperty(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedSetFoldoutTweenEnabled
            );
            SerializedProperty wgroupTweenProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WGroupFoldoutTweenEnabled
            );
            SerializedProperty inlineEditorTweenProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutTweenEnabled
            );

            bool originalWButtonValue = wbuttonTweenProperty.boolValue;
            bool originalDictionaryValue = dictionaryTweenProperty.boolValue;
            bool originalSortedValue = sortedDictionaryTweenProperty.boolValue;
            bool originalSetValue = setTweenProperty.boolValue;
            bool originalSortedSetValue = sortedSetTweenProperty.boolValue;
            bool originalWGroupValue = wgroupTweenProperty.boolValue;
            bool originalInlineEditorValue = inlineEditorTweenProperty.boolValue;

            try
            {
                wbuttonTweenProperty.boolValue = false;
                dictionaryTweenProperty.boolValue = false;
                sortedDictionaryTweenProperty.boolValue = false;
                setTweenProperty.boolValue = false;
                sortedSetTweenProperty.boolValue = false;
                wgroupTweenProperty.boolValue = false;
                inlineEditorTweenProperty.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsFalse(UnityHelpersSettings.ShouldTweenWButtonFoldouts());
                Assert.IsFalse(UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts());
                Assert.IsFalse(
                    UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts()
                );
                Assert.IsFalse(UnityHelpersSettings.ShouldTweenSerializableSetFoldouts());
                Assert.IsFalse(UnityHelpersSettings.ShouldTweenSerializableSortedSetFoldouts());
                Assert.IsFalse(UnityHelpersSettings.ShouldTweenWGroupFoldouts());
                Assert.IsFalse(UnityHelpersSettings.ShouldTweenInlineEditorFoldouts());
            }
            finally
            {
                using SerializedObject restore = new(settings);
                restore
                    .FindProperty(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonFoldoutTweenEnabled
                    )
                    .boolValue = originalWButtonValue;
                restore
                    .FindProperty(
                        UnityHelpersSettings
                            .SerializedPropertyNames
                            .SerializableDictionaryFoldoutTweenEnabled
                    )
                    .boolValue = originalDictionaryValue;
                restore
                    .FindProperty(
                        UnityHelpersSettings
                            .SerializedPropertyNames
                            .SerializableSortedDictionaryFoldoutTweenEnabled
                    )
                    .boolValue = originalSortedValue;
                restore
                    .FindProperty(
                        UnityHelpersSettings
                            .SerializedPropertyNames
                            .SerializableSetFoldoutTweenEnabled
                    )
                    .boolValue = originalSetValue;
                restore
                    .FindProperty(
                        UnityHelpersSettings
                            .SerializedPropertyNames
                            .SerializableSortedSetFoldoutTweenEnabled
                    )
                    .boolValue = originalSortedSetValue;
                restore
                    .FindProperty(
                        UnityHelpersSettings.SerializedPropertyNames.WGroupFoldoutTweenEnabled
                    )
                    .boolValue = originalWGroupValue;
                restore
                    .FindProperty(
                        UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutTweenEnabled
                    )
                    .boolValue = originalInlineEditorValue;
                restore.ApplyModifiedPropertiesWithoutUndo();
                settings.SaveSettings();
            }
        }

        [Test]
        public void WGroupFoldoutSpeedClampsToBounds()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            float originalSpeed = settings.WGroupFoldoutSpeed;

            try
            {
                settings.WGroupFoldoutSpeed = UnityHelpersSettings.MinFoldoutSpeed - 1f;
                Assert.That(
                    UnityHelpersSettings.GetWGroupFoldoutSpeed(),
                    Is.EqualTo(UnityHelpersSettings.MinFoldoutSpeed)
                );

                settings.WGroupFoldoutSpeed = UnityHelpersSettings.MaxFoldoutSpeed + 10f;
                Assert.That(
                    UnityHelpersSettings.GetWGroupFoldoutSpeed(),
                    Is.EqualTo(UnityHelpersSettings.MaxFoldoutSpeed)
                );

                float midValue =
                    (UnityHelpersSettings.MinFoldoutSpeed + UnityHelpersSettings.MaxFoldoutSpeed)
                    / 2f;
                settings.WGroupFoldoutSpeed = midValue;
                Assert.That(
                    UnityHelpersSettings.GetWGroupFoldoutSpeed(),
                    Is.EqualTo(midValue).Within(0.001f)
                );
            }
            finally
            {
                settings.WGroupFoldoutSpeed = originalSpeed;
            }
        }

        [Test]
        public void InlineEditorFoldoutSpeedClampsToBounds()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            float originalSpeed = settings.InlineEditorFoldoutSpeed;

            try
            {
                settings.InlineEditorFoldoutSpeed = UnityHelpersSettings.MinFoldoutSpeed - 1f;
                Assert.That(
                    UnityHelpersSettings.GetInlineEditorFoldoutSpeed(),
                    Is.EqualTo(UnityHelpersSettings.MinFoldoutSpeed)
                );

                settings.InlineEditorFoldoutSpeed = UnityHelpersSettings.MaxFoldoutSpeed + 10f;
                Assert.That(
                    UnityHelpersSettings.GetInlineEditorFoldoutSpeed(),
                    Is.EqualTo(UnityHelpersSettings.MaxFoldoutSpeed)
                );

                float midValue =
                    (UnityHelpersSettings.MinFoldoutSpeed + UnityHelpersSettings.MaxFoldoutSpeed)
                    / 2f;
                settings.InlineEditorFoldoutSpeed = midValue;
                Assert.That(
                    UnityHelpersSettings.GetInlineEditorFoldoutSpeed(),
                    Is.EqualTo(midValue).Within(0.001f)
                );
            }
            finally
            {
                settings.InlineEditorFoldoutSpeed = originalSpeed;
            }
        }

        [Test]
        public void WGroupFoldoutTweenEnabledPropertyAccessorWorks()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool original = settings.WGroupFoldoutTweenEnabled;

            try
            {
                settings.WGroupFoldoutTweenEnabled = true;
                Assert.IsTrue(UnityHelpersSettings.ShouldTweenWGroupFoldouts());

                settings.WGroupFoldoutTweenEnabled = false;
                Assert.IsFalse(UnityHelpersSettings.ShouldTweenWGroupFoldouts());
            }
            finally
            {
                settings.WGroupFoldoutTweenEnabled = original;
            }
        }

        [Test]
        public void InlineEditorFoldoutTweenEnabledPropertyAccessorWorks()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool original = settings.InlineEditorFoldoutTweenEnabled;

            try
            {
                settings.InlineEditorFoldoutTweenEnabled = true;
                Assert.IsTrue(UnityHelpersSettings.ShouldTweenInlineEditorFoldouts());

                settings.InlineEditorFoldoutTweenEnabled = false;
                Assert.IsFalse(UnityHelpersSettings.ShouldTweenInlineEditorFoldouts());
            }
            finally
            {
                settings.InlineEditorFoldoutTweenEnabled = original;
            }
        }

        [Test]
        public void WGroupAutoIncludeConfigurationClampsToBounds()
        {
            UnityHelpersSettings.WGroupAutoIncludeConfiguration original =
                UnityHelpersSettings.GetWGroupAutoIncludeConfiguration();

            try
            {
                UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                    UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                    1000
                );

                UnityHelpersSettings.WGroupAutoIncludeConfiguration configuration =
                    UnityHelpersSettings.GetWGroupAutoIncludeConfiguration();

                Assert.AreEqual(
                    UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                    configuration.Mode
                );
                Assert.AreEqual(
                    UnityHelpersSettings.MaxWGroupAutoIncludeRowCount,
                    configuration.RowCount
                );
            }
            finally
            {
                UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                    original.Mode,
                    original.RowCount
                );
            }
        }

        [Test]
        public void InlineEditorFoldoutBehaviorFollowsSettingsValue()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty property = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutBehavior
            );
            int original = property.enumValueIndex;

            try
            {
                property.enumValueIndex = (int)
                    UnityHelpersSettings.InlineEditorFoldoutBehavior.StartCollapsed;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.AreEqual(
                    UnityHelpersSettings.InlineEditorFoldoutBehavior.StartCollapsed,
                    UnityHelpersSettings.GetInlineEditorFoldoutBehavior()
                );
            }
            finally
            {
                property.enumValueIndex = original;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <remarks>
        /// Every reader of a colour key matched without regard to case while the dictionary storing
        /// them was ordinal: two entries differing only in case were one entry to a reader and two on
        /// disk, and each reader carried its own linear scan to paper over it.
        /// </remarks>
        [Test]
        public void PaletteResolutionIgnoresColorKeyCase()
        {
            const string PaletteKey = "SessionCaseKey";
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty paletteProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            PaletteEntrySnapshot snapshot = CapturePaletteEntrySnapshot(
                paletteProperty,
                PaletteKey,
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
            );

            try
            {
                Color button = new(0.2f, 0.6f, 0.9f, 1f);
                Color text = new(0.05f, 0.05f, 0.05f, 1f);
                SetPaletteEntryColors(
                    paletteProperty,
                    PaletteKey,
                    button,
                    text,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );
                serialized.ApplyModifiedPropertiesWithoutUndo();

                foreach (string lookup in new[] { PaletteKey, "sessioncasekey", "SESSIONCASEKEY" })
                {
                    UnityHelpersSettings.WButtonPaletteEntry resolved =
                        UnityHelpersSettings.ResolveWButtonPalette(lookup);
                    AssertColorsApproximately(button, resolved.ButtonColor);
                    Assert.IsTrue(
                        UnityHelpersSettings.HasWButtonPaletteColorKey(lookup),
                        $"{lookup} should resolve to the entry stored as {PaletteKey}."
                    );
                }
            }
            finally
            {
                RestorePaletteEntry(
                    paletteProperty,
                    PaletteKey,
                    snapshot,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void EnsureWButtonCustomColorDefaultsManualEditSkipsAutoSuggestion()
        {
            const string PaletteKey = "EditorManualWButton";
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty paletteProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            PaletteEntrySnapshot snapshot = CapturePaletteEntrySnapshot(
                paletteProperty,
                PaletteKey,
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
            );
            HashSet<string> skipSnapshot = CloneSkipSet(settings.WButtonCustomColorSkipAutoSuggest);

            try
            {
                SetPaletteEntryColors(
                    paletteProperty,
                    PaletteKey,
                    Color.white,
                    Color.black,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );
                serialized.ApplyModifiedPropertiesWithoutUndo();

                UnityHelpersSettings.RegisterPaletteManualEdit(
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors,
                    PaletteKey
                );

                settings.EnsureWButtonCustomColorDefaults();
                serialized.Update();

                (Color button, Color text) = GetPaletteEntryColors(
                    paletteProperty,
                    PaletteKey,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );

                AssertColorsApproximately(Color.white, button);
                AssertColorsApproximately(Color.black, text);
                HashSet<string> skipSet = settings.WButtonCustomColorSkipAutoSuggest;
                Assert.IsTrue(
                    skipSet != null && skipSet.Contains(PaletteKey),
                    "Manual edit should remain in the skip set."
                );
            }
            finally
            {
                RestorePaletteEntry(
                    paletteProperty,
                    PaletteKey,
                    snapshot,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );
                serialized.ApplyModifiedPropertiesWithoutUndo();
                settings.WButtonCustomColorSkipAutoSuggest = CloneSkipSet(skipSnapshot);
            }
        }

        [Test]
        public void EnsureWButtonCustomColorDefaultsSuggestsColorsWithoutManualEdit()
        {
            const string PaletteKey = "EditorAutoWButton";
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty paletteProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            PaletteEntrySnapshot snapshot = CapturePaletteEntrySnapshot(
                paletteProperty,
                PaletteKey,
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
            );
            HashSet<string> skipSnapshot = CloneSkipSet(settings.WButtonCustomColorSkipAutoSuggest);

            try
            {
                SetPaletteEntryColors(
                    paletteProperty,
                    PaletteKey,
                    Color.white,
                    Color.black,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );
                serialized.ApplyModifiedPropertiesWithoutUndo();

                settings.EnsureWButtonCustomColorDefaults();
                serialized.Update();

                (Color button, Color text) = GetPaletteEntryColors(
                    paletteProperty,
                    PaletteKey,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );

                Assert.IsFalse(
                    ColorsApproximatelyEqual(Color.white, button),
                    "Auto-suggestion should replace the placeholder button color."
                );
                Color expectedText = WButtonColorUtility.GetReadableTextColor(button);
                Assert.IsTrue(
                    ColorsApproximatelyEqual(expectedText, text),
                    $"Auto-suggested text color should match readability for {button}. Expected {expectedText}, observed {text}."
                );
            }
            finally
            {
                RestorePaletteEntry(
                    paletteProperty,
                    PaletteKey,
                    snapshot,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );
                serialized.ApplyModifiedPropertiesWithoutUndo();
                settings.WButtonCustomColorSkipAutoSuggest = CloneSkipSet(skipSnapshot);
            }
        }

        [Test]
        public void SerializableDictionaryDrawerCommitRegistersManualEditForPalette()
        {
            const string PaletteKey = "EditorDrawerPaletteKey";
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty paletteProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            PaletteEntrySnapshot snapshot = CapturePaletteEntrySnapshot(
                paletteProperty,
                PaletteKey,
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
            );
            HashSet<string> skipSnapshot = CloneSkipSet(settings.WButtonCustomColorSkipAutoSuggest);

            try
            {
                SerializableDictionaryPropertyDrawer drawer = new();
                Type paletteValueType = typeof(UnityHelpersSettings.WButtonCustomColor);
                UnityHelpersSettings.WButtonCustomColor paletteValue = new()
                {
                    ButtonColor = Color.cyan,
                    TextColor = Color.black,
                };

                SerializedProperty keysProperty = paletteProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Keys
                );
                SerializedProperty valuesProperty = paletteProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Values
                );

                drawer.CommitEntry(
                    keysProperty,
                    valuesProperty,
                    typeof(string),
                    paletteValueType,
                    PaletteKey,
                    paletteValue,
                    dictionaryProperty: paletteProperty,
                    existingIndex: -1
                );

                serialized.Update();
                HashSet<string> skipSet = settings.WButtonCustomColorSkipAutoSuggest;
                Assert.IsTrue(
                    skipSet != null && skipSet.Contains(PaletteKey),
                    "Drawer commit should register palette manual edits."
                );
            }
            finally
            {
                RestorePaletteEntry(
                    paletteProperty,
                    PaletteKey,
                    snapshot,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton,
                    UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                );
                serialized.ApplyModifiedPropertiesWithoutUndo();
                settings.WButtonCustomColorSkipAutoSuggest = CloneSkipSet(skipSnapshot);
            }
        }

        private static void AssertColorsApproximately(
            Color expected,
            Color actual,
            float tolerance = 0.01f
        )
        {
            Assert.That(Mathf.Abs(expected.r - actual.r), Is.LessThanOrEqualTo(tolerance));
            Assert.That(Mathf.Abs(expected.g - actual.g), Is.LessThanOrEqualTo(tolerance));
            Assert.That(Mathf.Abs(expected.b - actual.b), Is.LessThanOrEqualTo(tolerance));
            Assert.That(Mathf.Abs(expected.a - actual.a), Is.LessThanOrEqualTo(tolerance));
        }

        private readonly struct PaletteEntrySnapshot
        {
            public PaletteEntrySnapshot(bool exists, Color button, Color text)
            {
                Exists = exists;
                Button = button;
                Text = text;
            }

            public bool Exists { get; }
            public Color Button { get; }
            public Color Text { get; }
        }

        private static PaletteEntrySnapshot CapturePaletteEntrySnapshot(
            SerializedProperty dictionaryProperty,
            string key,
            string buttonField,
            string textField
        )
        {
            (SerializedProperty keys, SerializedProperty values) = GetDictionaryArrays(
                dictionaryProperty
            );
            int index = FindDictionaryIndex(keys, key);
            if (index < 0)
            {
                return new PaletteEntrySnapshot(false, default, default);
            }

            SerializedProperty valueProperty = values.GetArrayElementAtIndex(index);
            Color button =
                valueProperty.FindPropertyRelative(buttonField)?.colorValue ?? Color.clear;
            Color text = valueProperty.FindPropertyRelative(textField)?.colorValue ?? Color.clear;
            return new PaletteEntrySnapshot(true, button, text);
        }

        private sealed class PaletteDictionarySnapshot
        {
            public PaletteDictionarySnapshot(IReadOnlyList<PaletteDictionaryEntrySnapshot> entries)
            {
                Entries = entries ?? Array.Empty<PaletteDictionaryEntrySnapshot>();
            }

            public IReadOnlyList<PaletteDictionaryEntrySnapshot> Entries { get; }
        }

        private readonly struct PaletteDictionaryEntrySnapshot
        {
            public PaletteDictionaryEntrySnapshot(string key, Color button, Color text)
            {
                Key = key;
                Button = button;
                Text = text;
            }

            public string Key { get; }
            public Color Button { get; }
            public Color Text { get; }
        }

        private static PaletteDictionarySnapshot CapturePaletteDictionarySnapshot(
            SerializedProperty dictionaryProperty,
            string buttonField,
            string textField
        )
        {
            (SerializedProperty keysProperty, SerializedProperty valuesProperty) =
                GetDictionaryArrays(dictionaryProperty);
            if (keysProperty == null || valuesProperty == null)
            {
                return new PaletteDictionarySnapshot(Array.Empty<PaletteDictionaryEntrySnapshot>());
            }

            List<PaletteDictionaryEntrySnapshot> entries = new(keysProperty.arraySize);
            for (int index = 0; index < keysProperty.arraySize; index++)
            {
                SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(index);
                SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(index);
                string key = keyProperty.stringValue;
                Color button =
                    valueProperty.FindPropertyRelative(buttonField)?.colorValue ?? Color.clear;
                Color text =
                    valueProperty.FindPropertyRelative(textField)?.colorValue ?? Color.clear;
                entries.Add(new PaletteDictionaryEntrySnapshot(key, button, text));
            }

            return new PaletteDictionarySnapshot(entries);
        }

        private static void RestorePaletteEntry(
            SerializedProperty dictionaryProperty,
            string key,
            PaletteEntrySnapshot snapshot,
            string buttonField,
            string textField
        )
        {
            if (snapshot.Exists)
            {
                SetPaletteEntryColors(
                    dictionaryProperty,
                    key,
                    snapshot.Button,
                    snapshot.Text,
                    buttonField,
                    textField
                );
            }
            else
            {
                RemovePaletteEntry(dictionaryProperty, key);
            }
        }

        private static void RestorePaletteDictionary(
            SerializedProperty dictionaryProperty,
            PaletteDictionarySnapshot snapshot,
            string buttonField,
            string textField
        )
        {
            IReadOnlyList<PaletteDictionaryEntrySnapshot> entries =
                snapshot?.Entries ?? Array.Empty<PaletteDictionaryEntrySnapshot>();
            OverwritePaletteDictionary(dictionaryProperty, entries, buttonField, textField);
        }

        private static void OverwritePaletteDictionary(
            SerializedProperty dictionaryProperty,
            IReadOnlyList<PaletteDictionaryEntrySnapshot> entries,
            string buttonField,
            string textField
        )
        {
            (SerializedProperty keysProperty, SerializedProperty valuesProperty) =
                GetDictionaryArrays(dictionaryProperty);
            if (keysProperty == null || valuesProperty == null)
            {
                return;
            }

            keysProperty.ClearArray();
            valuesProperty.ClearArray();

            if (entries == null || entries.Count == 0)
            {
                return;
            }

            keysProperty.arraySize = entries.Count;
            valuesProperty.arraySize = entries.Count;

            for (int index = 0; index < entries.Count; index++)
            {
                PaletteDictionaryEntrySnapshot entry = entries[index];
                SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(index);
                keyProperty.stringValue = entry.Key ?? string.Empty;

                SerializedProperty valueProperty = valuesProperty.GetArrayElementAtIndex(index);
                SerializedProperty backgroundProperty = valueProperty.FindPropertyRelative(
                    buttonField
                );
                if (backgroundProperty != null)
                {
                    backgroundProperty.colorValue = entry.Button;
                }

                SerializedProperty textProperty = valueProperty.FindPropertyRelative(textField);
                if (textProperty != null)
                {
                    textProperty.colorValue = entry.Text;
                }
            }
        }

        private static void SetPaletteEntryColors(
            SerializedProperty dictionaryProperty,
            string key,
            Color buttonColor,
            Color textColor,
            string buttonField,
            string textField
        )
        {
            (SerializedProperty keys, SerializedProperty values) = GetDictionaryArrays(
                dictionaryProperty
            );
            int index = FindDictionaryIndex(keys, key);
            if (index < 0)
            {
                index = keys.arraySize;
                keys.InsertArrayElementAtIndex(index);
                values.InsertArrayElementAtIndex(index);
                keys.GetArrayElementAtIndex(index).stringValue = key;
            }

            SerializedProperty valueProperty = values.GetArrayElementAtIndex(index);
            valueProperty.FindPropertyRelative(buttonField).colorValue = buttonColor;
            valueProperty.FindPropertyRelative(textField).colorValue = textColor;
        }

        private static string[] ExtractDictionaryKeys(SerializedProperty keysProperty)
        {
            if (keysProperty == null)
            {
                return Array.Empty<string>();
            }

            int count = Mathf.Max(0, keysProperty.arraySize);
            string[] keys = new string[count];
            for (int index = 0; index < count; index++)
            {
                keys[index] = keysProperty.GetArrayElementAtIndex(index).stringValue;
            }

            return keys;
        }

        private static (Color Button, Color Text) GetPaletteEntryColors(
            SerializedProperty dictionaryProperty,
            string key,
            string buttonField,
            string textField
        )
        {
            (SerializedProperty keys, SerializedProperty values) = GetDictionaryArrays(
                dictionaryProperty
            );
            int index = FindDictionaryIndex(keys, key);
            Assert.GreaterOrEqual(index, 0, $"Palette entry '{key}' was not found.");

            SerializedProperty valueProperty = values.GetArrayElementAtIndex(index);
            return (
                valueProperty.FindPropertyRelative(buttonField).colorValue,
                valueProperty.FindPropertyRelative(textField).colorValue
            );
        }

        private static void RemovePaletteEntry(SerializedProperty dictionaryProperty, string key)
        {
            (SerializedProperty keys, SerializedProperty values) = GetDictionaryArrays(
                dictionaryProperty
            );
            int index = FindDictionaryIndex(keys, key);
            if (index < 0)
            {
                return;
            }

            keys.DeleteArrayElementAtIndex(index);
            values.DeleteArrayElementAtIndex(index);
        }

        private static (SerializedProperty Keys, SerializedProperty Values) GetDictionaryArrays(
            SerializedProperty dictionaryProperty
        )
        {
            SerializedProperty keys = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );
            return (keys, values);
        }

        private static int FindDictionaryIndex(SerializedProperty keysProperty, string targetKey)
        {
            for (int index = 0; index < keysProperty.arraySize; index++)
            {
                SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(index);
                if (
                    string.Equals(
                        keyProperty.stringValue,
                        targetKey,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return index;
                }
            }

            return -1;
        }

        private static HashSet<string> CloneSkipSet(HashSet<string> source)
        {
            return source != null
                ? new HashSet<string>(source, StringComparer.OrdinalIgnoreCase)
                : null;
        }

        private static bool ColorsApproximatelyEqual(Color a, Color b, float tolerance = 0.01f)
        {
            return Mathf.Abs(a.r - b.r) <= tolerance
                && Mathf.Abs(a.g - b.g) <= tolerance
                && Mathf.Abs(a.b - b.b) <= tolerance
                && Mathf.Abs(a.a - b.a) <= tolerance;
        }

        [Test]
        public void CustomColorDrawerLayoutMinColorFieldWidthIsReasonable()
        {
            float minColorFieldWidth = UnityHelpersSettings
                .CustomColorDrawerLayout
                .MinColorFieldWidth;
            Assert.That(
                minColorFieldWidth,
                Is.GreaterThan(20f),
                "Min color field width should be large enough to be usable."
            );
            Assert.That(
                minColorFieldWidth,
                Is.LessThan(100f),
                "Min color field width should not be excessively large."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutLabelWidthRatioIsReasonable()
        {
            float labelWidthRatio = UnityHelpersSettings.CustomColorDrawerLayout.LabelWidthRatio;
            Assert.That(
                labelWidthRatio,
                Is.GreaterThan(0.2f),
                "Label width ratio should be large enough for labels to be readable."
            );
            Assert.That(
                labelWidthRatio,
                Is.LessThan(0.6f),
                "Label width ratio should leave enough space for color fields."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutMinLabelWidthIsReasonable()
        {
            float minLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MinLabelWidth;
            Assert.That(
                minLabelWidth,
                Is.GreaterThan(10f),
                "Min label width should be large enough for short labels."
            );
            Assert.That(
                minLabelWidth,
                Is.LessThan(50f),
                "Min label width should not consume too much space."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutMaxLabelWidthIsReasonable()
        {
            float maxLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MaxLabelWidth;
            float minLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MinLabelWidth;
            Assert.That(
                maxLabelWidth,
                Is.GreaterThan(minLabelWidth),
                "Max label width should be greater than min."
            );
            Assert.That(
                maxLabelWidth,
                Is.LessThan(200f),
                "Max label width should not consume too much horizontal space."
            );
        }

        [TestCase(100f, TestName = "CalculateLabelWidth.100px.WithinBounds")]
        [TestCase(150f, TestName = "CalculateLabelWidth.150px.WithinBounds")]
        [TestCase(200f, TestName = "CalculateLabelWidth.200px.WithinBounds")]
        [TestCase(300f, TestName = "CalculateLabelWidth.300px.WithinBounds")]
        [TestCase(50f, TestName = "CalculateLabelWidth.50px.AtMinBound")]
        public void CustomColorDrawerLayoutCalculateLabelWidthIsWithinBounds(float columnWidth)
        {
            float labelWidth = UnityHelpersSettings.CustomColorDrawerLayout.CalculateLabelWidth(
                columnWidth
            );
            float minLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MinLabelWidth;
            float maxLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MaxLabelWidth;
            Assert.That(
                labelWidth,
                Is.GreaterThanOrEqualTo(minLabelWidth),
                $"Label width should be at least {minLabelWidth}px."
            );
            Assert.That(
                labelWidth,
                Is.LessThanOrEqualTo(maxLabelWidth),
                $"Label width should not exceed {maxLabelWidth}px."
            );
        }

        [TestCase(200f, true, TestName = "ShouldShowLabels.200px.ShowsLabels")]
        [TestCase(150f, true, TestName = "ShouldShowLabels.150px.ShowsLabels")]
        [TestCase(120f, true, TestName = "ShouldShowLabels.120px.ShowsLabels")]
        [TestCase(100f, true, TestName = "ShouldShowLabels.100px.ShowsLabels")]
        public void CustomColorDrawerLayoutShouldShowLabelsForNormalWidths(
            float columnWidth,
            bool expectedShowLabels
        )
        {
            bool shouldShowLabels = UnityHelpersSettings.CustomColorDrawerLayout.ShouldShowLabels(
                columnWidth
            );
            Assert.That(
                shouldShowLabels,
                Is.EqualTo(expectedShowLabels),
                $"For column width {columnWidth}px, labels should {(expectedShowLabels ? "" : "not ")}be shown."
            );
        }

        [TestCase(50f, false, TestName = "ShouldShowLabels.50px.HidesLabels")]
        [TestCase(30f, false, TestName = "ShouldShowLabels.30px.HidesLabels")]
        [TestCase(20f, false, TestName = "ShouldShowLabels.20px.HidesLabels")]
        public void CustomColorDrawerLayoutShouldHideLabelsForNarrowWidths(
            float columnWidth,
            bool expectedShowLabels
        )
        {
            bool shouldShowLabels = UnityHelpersSettings.CustomColorDrawerLayout.ShouldShowLabels(
                columnWidth
            );
            Assert.That(
                shouldShowLabels,
                Is.EqualTo(expectedShowLabels),
                $"For narrow column width {columnWidth}px, labels should be hidden."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutLabelsHiddenWhenWidthBelowThreshold()
        {
            float minColorFieldWidth = UnityHelpersSettings
                .CustomColorDrawerLayout
                .MinColorFieldWidth;
            float minLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MinLabelWidth;
            float threshold = minColorFieldWidth + minLabelWidth;
            bool justAboveThreshold = UnityHelpersSettings.CustomColorDrawerLayout.ShouldShowLabels(
                threshold + 1f
            );
            bool justBelowThreshold = UnityHelpersSettings.CustomColorDrawerLayout.ShouldShowLabels(
                threshold - 1f
            );
            Assert.IsTrue(
                justAboveThreshold,
                $"Labels should be shown when column width ({threshold + 1f}px) is above threshold ({threshold}px)."
            );
            Assert.IsFalse(
                justBelowThreshold,
                $"Labels should be hidden when column width ({threshold - 1f}px) is below threshold ({threshold}px)."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutCalculateLabelWidthMatchesRatioForMidRange()
        {
            float labelWidthRatio = UnityHelpersSettings.CustomColorDrawerLayout.LabelWidthRatio;
            float maxLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MaxLabelWidth;
            float columnWidth = maxLabelWidth / labelWidthRatio * 0.8f;
            float expectedLabelWidth = columnWidth * labelWidthRatio;
            float actualLabelWidth =
                UnityHelpersSettings.CustomColorDrawerLayout.CalculateLabelWidth(columnWidth);
            Assert.That(
                actualLabelWidth,
                Is.EqualTo(expectedLabelWidth).Within(0.1f),
                $"For column width {columnWidth}px in the mid-range, label width should match ratio calculation."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutLabelWidthClampsAtMinimum()
        {
            float minLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MinLabelWidth;
            float actualLabelWidth =
                UnityHelpersSettings.CustomColorDrawerLayout.CalculateLabelWidth(10f);
            Assert.That(
                actualLabelWidth,
                Is.EqualTo(minLabelWidth),
                "Label width should be clamped to minimum for very narrow columns."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutLabelWidthClampsAtMaximum()
        {
            float maxLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MaxLabelWidth;
            float actualLabelWidth =
                UnityHelpersSettings.CustomColorDrawerLayout.CalculateLabelWidth(500f);
            Assert.That(
                actualLabelWidth,
                Is.EqualTo(maxLabelWidth),
                "Label width should be clamped to maximum for very wide columns."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutMinFieldWidthAllowsUsableColorPicker()
        {
            float minColorFieldWidth = UnityHelpersSettings
                .CustomColorDrawerLayout
                .MinColorFieldWidth;
            float minLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MinLabelWidth;
            float combinedMinWidth = minColorFieldWidth + minLabelWidth;
            Assert.That(
                combinedMinWidth,
                Is.GreaterThanOrEqualTo(50f),
                "Combined minimum width should allow for a usable color picker with a label."
            );
            Assert.That(
                combinedMinWidth,
                Is.LessThan(150f),
                "Combined minimum width should not be excessively large."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutTwoColumnLayoutFitsInNarrowSettings()
        {
            const float narrowSettingsPanelWidth = 300f;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float availableWidth = narrowSettingsPanelWidth - spacing;
            float halfWidth = availableWidth * 0.5f;
            bool labelsVisible = UnityHelpersSettings.CustomColorDrawerLayout.ShouldShowLabels(
                halfWidth
            );
            float labelWidth = UnityHelpersSettings.CustomColorDrawerLayout.CalculateLabelWidth(
                halfWidth
            );
            float minColorFieldWidth = UnityHelpersSettings
                .CustomColorDrawerLayout
                .MinColorFieldWidth;
            float remainingWidthForColorField = halfWidth - labelWidth;
            Assert.That(
                labelsVisible,
                Is.True,
                $"Labels should be visible for a {narrowSettingsPanelWidth}px panel width."
            );
            Assert.That(
                remainingWidthForColorField,
                Is.GreaterThanOrEqualTo(minColorFieldWidth),
                $"At {narrowSettingsPanelWidth}px panel width, color fields should have adequate space."
            );
        }

        [Test]
        public void CustomColorDrawerLayoutTwoColumnLayoutGracefullyDegradesBelowMinimum()
        {
            const float veryNarrowPanelWidth = 120f;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            float availableWidth = veryNarrowPanelWidth - spacing;
            float halfWidth = availableWidth * 0.5f;
            bool labelsVisible = UnityHelpersSettings.CustomColorDrawerLayout.ShouldShowLabels(
                halfWidth
            );
            float minColorFieldWidth = UnityHelpersSettings
                .CustomColorDrawerLayout
                .MinColorFieldWidth;
            float minLabelWidth = UnityHelpersSettings.CustomColorDrawerLayout.MinLabelWidth;
            float threshold = minColorFieldWidth + minLabelWidth;
            bool expectedLabelsHidden = halfWidth < threshold;
            Assert.That(
                labelsVisible,
                Is.EqualTo(!expectedLabelsHidden),
                $"For very narrow panel ({veryNarrowPanelWidth}px), labels should be hidden when halfWidth ({halfWidth}px) < threshold ({threshold}px)."
            );
        }

        /// <summary>
        /// Data-driven test that validates each SerializedPropertyNames constant maps to an actual field.
        /// This prevents regressions where field names are changed but constants are not updated.
        /// </summary>
        [Test]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.SerializableTypeIgnorePatterns),
            "SerializableTypeIgnorePatterns"
        )]
        [TestCase(
            nameof(
                UnityHelpersSettings.SerializedPropertyNames.SerializableTypePatternsInitialized
            ),
            "SerializableTypePatternsInitialized"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.LegacyWButtonPriorityColors),
            "LegacyWButtonPriorityColors"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors),
            "WButtonCustomColors"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.WGroupFoldoutsStartCollapsed),
            "WGroupFoldoutsStartCollapsed"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.WGroupFoldoutTweenEnabled),
            "WGroupFoldoutTweenEnabled"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.WGroupFoldoutSpeed),
            "WGroupFoldoutSpeed"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors),
            "WEnumToggleButtonsCustomColors"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutBehavior),
            "InlineEditorFoldoutBehavior"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutTweenEnabled),
            "InlineEditorFoldoutTweenEnabled"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutSpeed),
            "InlineEditorFoldoutSpeed"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.WButtonFoldoutTweenEnabled),
            "WButtonFoldoutTweenEnabled"
        )]
        [TestCase(
            nameof(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableDictionaryFoldoutTweenEnabled
            ),
            "SerializableDictionaryFoldoutTweenEnabled"
        )]
        [TestCase(
            nameof(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedDictionaryFoldoutTweenEnabled
            ),
            "SerializableSortedDictionaryFoldoutTweenEnabled"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.SerializableSetFoldoutTweenEnabled),
            "SerializableSetFoldoutTweenEnabled"
        )]
        [TestCase(
            nameof(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedSetFoldoutTweenEnabled
            ),
            "SerializableSortedSetFoldoutTweenEnabled"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.FoldoutTweenSettingsInitialized),
            "FoldoutTweenSettingsInitialized"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.DetectAssetChangeLoopWindowSeconds),
            "DetectAssetChangeLoopWindowSeconds"
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.FailedTestsOutputDirectory),
            "FailedTestsOutputDirectory"
        )]
        public void SerializedPropertyNamesMapsToActualField(
            string constantName,
            string expectedFieldDescription
        )
        {
            string actualFieldName =
                UnityHelpersSettings.SerializedPropertyNames.GetPropertyNameValue(constantName);
            Assert.IsTrue(
                actualFieldName != null,
                $"SerializedPropertyNames.{constantName} should have a non-null value."
            );

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty property = serialized.FindProperty(actualFieldName);
            Assert.IsTrue(
                property != null,
                $"SerializedPropertyNames.{constantName} = \"{actualFieldName}\" should reference an actual "
                    + $"serialized field on {nameof(UnityHelpersSettings)}. "
                    + $"Expected field description: {expectedFieldDescription}. "
                    + $"This typically indicates a field was renamed without updating the constant."
            );
        }

        /// <summary>
        /// Tests that SerializedPropertyNames constants can be used to find SerializedProperty via SerializedObject.
        /// This validates the constants work correctly in the Unity serialization context.
        /// </summary>
        [Test]
        [TestCase(nameof(UnityHelpersSettings.SerializedPropertyNames.WButtonFoldoutTweenEnabled))]
        [TestCase(
            nameof(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableDictionaryFoldoutTweenEnabled
            )
        )]
        [TestCase(
            nameof(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedDictionaryFoldoutTweenEnabled
            )
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.SerializableSetFoldoutTweenEnabled)
        )]
        [TestCase(
            nameof(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedSetFoldoutTweenEnabled
            )
        )]
        [TestCase(nameof(UnityHelpersSettings.SerializedPropertyNames.WGroupFoldoutTweenEnabled))]
        [TestCase(nameof(UnityHelpersSettings.SerializedPropertyNames.WGroupFoldoutSpeed))]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutTweenEnabled)
        )]
        [TestCase(nameof(UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutSpeed))]
        public void SerializedPropertyNamesResolvesToSerializedProperty(string constantName)
        {
            string fieldName = UnityHelpersSettings.SerializedPropertyNames.GetPropertyNameValue(
                constantName
            );

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty property = serialized.FindProperty(fieldName);

            Assert.IsTrue(
                property != null,
                $"SerializedPropertyNames.{constantName} = \"{fieldName}\" should resolve to a valid SerializedProperty. "
                    + $"This could indicate the field is not serializable, has incorrect attributes, or was renamed."
            );
        }

        /// <summary>
        /// Validates that tween settings can be found via SerializedObject with correct property type.
        /// This validates the pattern used by TweenDisabledScope helper classes in tests.
        /// </summary>
        [Test]
        [TestCase(
            nameof(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableDictionaryFoldoutTweenEnabled
            ),
            typeof(bool)
        )]
        [TestCase(
            nameof(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedDictionaryFoldoutTweenEnabled
            ),
            typeof(bool)
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.SerializableSetFoldoutTweenEnabled),
            typeof(bool)
        )]
        [TestCase(
            nameof(
                UnityHelpersSettings
                    .SerializedPropertyNames
                    .SerializableSortedSetFoldoutTweenEnabled
            ),
            typeof(bool)
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.WGroupFoldoutTweenEnabled),
            typeof(bool)
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.WGroupFoldoutSpeed),
            typeof(float)
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutTweenEnabled),
            typeof(bool)
        )]
        [TestCase(
            nameof(UnityHelpersSettings.SerializedPropertyNames.InlineEditorFoldoutSpeed),
            typeof(float)
        )]
        public void TweenFieldsHaveCorrectSerializedPropertyType(
            string constantName,
            Type expectedFieldType
        )
        {
            string fieldName = UnityHelpersSettings.SerializedPropertyNames.GetPropertyNameValue(
                constantName
            );

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty property = serialized.FindProperty(fieldName);
            Assert.IsTrue(
                property != null,
                $"Field '{fieldName}' from SerializedPropertyNames.{constantName} should be accessible via SerializedObject."
            );

            SerializedPropertyType expectedPropertyType = GetExpectedPropertyType(
                expectedFieldType
            );
            Assert.AreEqual(
                expectedPropertyType,
                property.propertyType,
                $"Field '{fieldName}' should be of type {expectedFieldType.Name} (SerializedPropertyType.{expectedPropertyType})."
            );
        }

        private static SerializedPropertyType GetExpectedPropertyType(Type type)
        {
            if (type == typeof(bool))
            {
                return SerializedPropertyType.Boolean;
            }
            if (type == typeof(float))
            {
                return SerializedPropertyType.Float;
            }
            if (type == typeof(int))
            {
                return SerializedPropertyType.Integer;
            }
            if (type == typeof(string))
            {
                return SerializedPropertyType.String;
            }
            return SerializedPropertyType.Generic;
        }

        /// <summary>
        /// Validates that WButtonCustomColor SerializedPropertyNames resolve to actual properties.
        /// This catches bugs where field names are changed but constants are not updated,
        /// which would cause PropertyDrawers to fail to find their child properties.
        /// </summary>
        [Test]
        public void WButtonCustomColorPropertiesResolveCorrectly()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty paletteProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(paletteProperty != null, "Should find WButtonCustomColors property.");

            SerializedProperty keysProperty = paletteProperty.FindPropertyRelative(
                nameof(SerializableDictionary<object, object>._keys)
            );
            Assert.IsTrue(keysProperty != null, "Should find keys property in dictionary.");

            if (keysProperty.arraySize == 0)
            {
                Assert.Inconclusive(
                    "No WButtonCustomColor entries exist to validate. Consider adding a test entry."
                );
                return;
            }

            SerializedProperty valuesProperty = paletteProperty.FindPropertyRelative(
                nameof(SerializableDictionary<object, object>._values)
            );
            Assert.IsTrue(valuesProperty != null, "Should find values property in dictionary.");
            Assert.Greater(valuesProperty.arraySize, 0, "Should have at least one value entry.");

            SerializedProperty firstValue = valuesProperty.GetArrayElementAtIndex(0);
            Assert.IsTrue(firstValue != null, "Should be able to get first value element.");

            string buttonFieldName = UnityHelpersSettings
                .SerializedPropertyNames
                .WButtonCustomColorButton;
            SerializedProperty buttonColor = firstValue.FindPropertyRelative(buttonFieldName);
            Assert.IsTrue(
                buttonColor != null,
                $"WButtonCustomColor should have property '{buttonFieldName}'. "
                    + $"This typically indicates the field was renamed. "
                    + $"Available properties: {GetAvailableRelativeProperties(firstValue)}"
            );

            string textFieldName = UnityHelpersSettings
                .SerializedPropertyNames
                .WButtonCustomColorText;
            SerializedProperty textColor = firstValue.FindPropertyRelative(textFieldName);
            Assert.IsTrue(
                textColor != null,
                $"WButtonCustomColor should have property '{textFieldName}'. "
                    + $"This typically indicates the field was renamed. "
                    + $"Available properties: {GetAvailableRelativeProperties(firstValue)}"
            );
        }

        /// <summary>
        /// Validates that WEnumToggleButtonsCustomColor SerializedPropertyNames resolve to actual properties.
        /// This catches bugs where field names are changed but constants are not updated.
        /// </summary>
        [Test]
        public void WEnumToggleButtonsCustomColorPropertiesResolveCorrectly()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            serialized.Update();

            SerializedProperty paletteProperty = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WEnumToggleButtonsCustomColors
            );
            Assert.IsTrue(
                paletteProperty != null,
                "Should find WEnumToggleButtonsCustomColors property."
            );

            SerializedProperty keysProperty = paletteProperty.FindPropertyRelative(
                nameof(SerializableDictionary<object, object>._keys)
            );
            Assert.IsTrue(keysProperty != null, "Should find keys property in dictionary.");

            if (keysProperty.arraySize == 0)
            {
                Assert.Inconclusive(
                    "No WEnumToggleButtonsCustomColor entries exist to validate. Consider adding a test entry."
                );
                return;
            }

            SerializedProperty valuesProperty = paletteProperty.FindPropertyRelative(
                nameof(SerializableDictionary<object, object>._values)
            );
            Assert.IsTrue(valuesProperty != null, "Should find values property in dictionary.");
            Assert.Greater(valuesProperty.arraySize, 0, "Should have at least one value entry.");

            SerializedProperty firstValue = valuesProperty.GetArrayElementAtIndex(0);
            Assert.IsTrue(firstValue != null, "Should be able to get first value element.");

            string selectedBackgroundFieldName = UnityHelpersSettings
                .SerializedPropertyNames
                .WEnumToggleButtonsSelectedBackground;
            SerializedProperty selectedBackground = firstValue.FindPropertyRelative(
                selectedBackgroundFieldName
            );
            Assert.IsTrue(
                selectedBackground != null,
                $"WEnumToggleButtonsCustomColor should have property '{selectedBackgroundFieldName}'. "
                    + $"This typically indicates the field was renamed. "
                    + $"Available properties: {GetAvailableRelativeProperties(firstValue)}"
            );

            string selectedTextFieldName = UnityHelpersSettings
                .SerializedPropertyNames
                .WEnumToggleButtonsSelectedText;
            SerializedProperty selectedText = firstValue.FindPropertyRelative(
                selectedTextFieldName
            );
            Assert.IsTrue(
                selectedText != null,
                $"WEnumToggleButtonsCustomColor should have property '{selectedTextFieldName}'. "
                    + $"This typically indicates the field was renamed. "
                    + $"Available properties: {GetAvailableRelativeProperties(firstValue)}"
            );

            string inactiveBackgroundFieldName = UnityHelpersSettings
                .SerializedPropertyNames
                .WEnumToggleButtonsInactiveBackground;
            SerializedProperty inactiveBackground = firstValue.FindPropertyRelative(
                inactiveBackgroundFieldName
            );
            Assert.IsTrue(
                inactiveBackground != null,
                $"WEnumToggleButtonsCustomColor should have property '{inactiveBackgroundFieldName}'. "
                    + $"This typically indicates the field was renamed. "
                    + $"Available properties: {GetAvailableRelativeProperties(firstValue)}"
            );

            string inactiveTextFieldName = UnityHelpersSettings
                .SerializedPropertyNames
                .WEnumToggleButtonsInactiveText;
            SerializedProperty inactiveText = firstValue.FindPropertyRelative(
                inactiveTextFieldName
            );
            Assert.IsTrue(
                inactiveText != null,
                $"WEnumToggleButtonsCustomColor should have property '{inactiveTextFieldName}'. "
                    + $"This typically indicates the field was renamed. "
                    + $"Available properties: {GetAvailableRelativeProperties(firstValue)}"
            );
        }

        /// <summary>
        /// Validates that applying buffer settings to the asset persists the values correctly.
        /// This test ensures that the "Apply To Buffers" operation saves the asset with the configured values.
        /// </summary>
        [Test]
        public void ApplyBufferSettingsSavesAssetWithCorrectValues()
        {
            UnityHelpersBufferSettingsAsset asset = Resources.Load<UnityHelpersBufferSettingsAsset>(
                UnityHelpersBufferSettingsAsset.ResourcePath
            );
            if (asset == null)
            {
                Assert.Inconclusive(
                    "UnityHelpersBufferSettingsAsset not found in Resources. "
                        + "Run the settings UI to create the asset first."
                );
                return;
            }

            float originalQuantization = asset.QuantizationStepSeconds;
            int originalMaxEntries = asset.MaxDistinctEntries;
            bool originalUseLru = asset.UseLruEviction;
            bool originalApplyOnLoad = asset.ApplyOnLoad;

            try
            {
                float testQuantization = 0.25f;
                int testMaxEntries = 128;
                bool testUseLru = !originalUseLru;
                bool testApplyOnLoad = !originalApplyOnLoad;

                using (SerializedObject assetSerialized = new(asset))
                {
                    SerializedProperty quantizationProperty = assetSerialized.FindProperty(
                        UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                    );
                    SerializedProperty maxEntriesProperty = assetSerialized.FindProperty(
                        UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName
                    );
                    SerializedProperty useLruProperty = assetSerialized.FindProperty(
                        UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName
                    );
                    SerializedProperty applyOnLoadProperty = assetSerialized.FindProperty(
                        UnityHelpersBufferSettingsAsset.ApplyOnLoadPropertyName
                    );

                    Assert.IsTrue(
                        quantizationProperty != null,
                        "Should find quantization property."
                    );
                    Assert.IsTrue(maxEntriesProperty != null, "Should find max entries property.");
                    Assert.IsTrue(useLruProperty != null, "Should find use LRU property.");
                    Assert.IsTrue(
                        applyOnLoadProperty != null,
                        "Should find apply on load property."
                    );

                    quantizationProperty.floatValue = testQuantization;
                    maxEntriesProperty.intValue = testMaxEntries;
                    useLruProperty.boolValue = testUseLru;
                    applyOnLoadProperty.boolValue = testApplyOnLoad;
                    assetSerialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
                }

                UnityHelpersBufferSettingsAsset reloadedAsset =
                    AssetDatabase.LoadAssetAtPath<UnityHelpersBufferSettingsAsset>(
                        UnityHelpersBufferSettingsAsset.AssetPath
                    );
                Assert.IsTrue(
                    reloadedAsset != null,
                    "Should be able to reload the asset from disk."
                );
                Assert.That(
                    reloadedAsset.QuantizationStepSeconds,
                    Is.EqualTo(testQuantization),
                    "Quantization step should be persisted."
                );
                Assert.That(
                    reloadedAsset.MaxDistinctEntries,
                    Is.EqualTo(testMaxEntries),
                    "Max distinct entries should be persisted."
                );
                Assert.That(
                    reloadedAsset.UseLruEviction,
                    Is.EqualTo(testUseLru),
                    "Use LRU eviction should be persisted."
                );
                Assert.That(
                    reloadedAsset.ApplyOnLoad,
                    Is.EqualTo(testApplyOnLoad),
                    "Apply on load should be persisted."
                );
            }
            finally
            {
                using SerializedObject restoreSerialized = new(asset);
                SerializedProperty quantizationProperty = restoreSerialized.FindProperty(
                    UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                );
                SerializedProperty maxEntriesProperty = restoreSerialized.FindProperty(
                    UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName
                );
                SerializedProperty useLruProperty = restoreSerialized.FindProperty(
                    UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName
                );
                SerializedProperty applyOnLoadProperty = restoreSerialized.FindProperty(
                    UnityHelpersBufferSettingsAsset.ApplyOnLoadPropertyName
                );
                quantizationProperty.floatValue = originalQuantization;
                maxEntriesProperty.intValue = originalMaxEntries;
                useLruProperty.boolValue = originalUseLru;
                applyOnLoadProperty.boolValue = originalApplyOnLoad;
                restoreSerialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>
        /// Validates that the buffer settings asset correctly applies values to the Buffers runtime class.
        /// </summary>
        [Test]
        public void BufferSettingsAssetAppliesValuesToRuntime()
        {
            UnityHelpersBufferSettingsAsset asset = Resources.Load<UnityHelpersBufferSettingsAsset>(
                UnityHelpersBufferSettingsAsset.ResourcePath
            );
            if (asset == null)
            {
                Assert.Inconclusive(
                    "UnityHelpersBufferSettingsAsset not found in Resources. "
                        + "Run the settings UI to create the asset first."
                );
                return;
            }

            float originalRuntimeQuantization = Buffers.WaitInstructionQuantizationStepSeconds;
            int originalRuntimeMaxEntries = Buffers.WaitInstructionMaxDistinctEntries;
            bool originalRuntimeUseLru = Buffers.WaitInstructionUseLruEviction;

            float originalAssetQuantization = asset.QuantizationStepSeconds;
            int originalAssetMaxEntries = asset.MaxDistinctEntries;
            bool originalAssetUseLru = asset.UseLruEviction;

            try
            {
                float testQuantization = 0.5f;
                int testMaxEntries = 256;
                bool testUseLru = true;

                using (SerializedObject assetSerialized = new(asset))
                {
                    assetSerialized
                        .FindProperty(
                            UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                        )
                        .floatValue = testQuantization;
                    assetSerialized
                        .FindProperty(
                            UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName
                        )
                        .intValue = testMaxEntries;
                    assetSerialized
                        .FindProperty(UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName)
                        .boolValue = testUseLru;
                    assetSerialized.ApplyModifiedPropertiesWithoutUndo();
                }

                asset.ApplyToBuffers();

                Assert.That(
                    Buffers.WaitInstructionQuantizationStepSeconds,
                    Is.EqualTo(testQuantization),
                    "Runtime quantization should match asset value after ApplyToBuffers."
                );
                Assert.That(
                    Buffers.WaitInstructionMaxDistinctEntries,
                    Is.EqualTo(testMaxEntries),
                    "Runtime max entries should match asset value after ApplyToBuffers."
                );
                Assert.That(
                    Buffers.WaitInstructionUseLruEviction,
                    Is.EqualTo(testUseLru),
                    "Runtime use LRU should match asset value after ApplyToBuffers."
                );
            }
            finally
            {
                Buffers.WaitInstructionQuantizationStepSeconds = originalRuntimeQuantization;
                Buffers.WaitInstructionMaxDistinctEntries = originalRuntimeMaxEntries;
                Buffers.WaitInstructionUseLruEviction = originalRuntimeUseLru;

                using SerializedObject restoreSerialized = new(asset);
                restoreSerialized
                    .FindProperty(
                        UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                    )
                    .floatValue = originalAssetQuantization;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName)
                    .intValue = originalAssetMaxEntries;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName)
                    .boolValue = originalAssetUseLru;
                restoreSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Validates that the buffer settings asset correctly syncs values from the Buffers runtime class.
        /// </summary>
        [Test]
        public void BufferSettingsAssetSyncsFromRuntime()
        {
            UnityHelpersBufferSettingsAsset asset = Resources.Load<UnityHelpersBufferSettingsAsset>(
                UnityHelpersBufferSettingsAsset.ResourcePath
            );
            if (asset == null)
            {
                Assert.Inconclusive(
                    "UnityHelpersBufferSettingsAsset not found in Resources. "
                        + "Run the settings UI to create the asset first."
                );
                return;
            }

            float originalRuntimeQuantization = Buffers.WaitInstructionQuantizationStepSeconds;
            int originalRuntimeMaxEntries = Buffers.WaitInstructionMaxDistinctEntries;
            bool originalRuntimeUseLru = Buffers.WaitInstructionUseLruEviction;

            float originalAssetQuantization = asset.QuantizationStepSeconds;
            int originalAssetMaxEntries = asset.MaxDistinctEntries;
            bool originalAssetUseLru = asset.UseLruEviction;

            try
            {
                float testQuantization = 0.75f;
                int testMaxEntries = 1024;
                bool testUseLru = true;

                Buffers.WaitInstructionQuantizationStepSeconds = testQuantization;
                Buffers.WaitInstructionMaxDistinctEntries = testMaxEntries;
                Buffers.WaitInstructionUseLruEviction = testUseLru;

                asset.SyncFromRuntime();

                Assert.That(
                    asset.QuantizationStepSeconds,
                    Is.EqualTo(testQuantization),
                    "Asset quantization should match runtime value after SyncFromRuntime."
                );
                Assert.That(
                    asset.MaxDistinctEntries,
                    Is.EqualTo(testMaxEntries),
                    "Asset max entries should match runtime value after SyncFromRuntime."
                );
                Assert.That(
                    asset.UseLruEviction,
                    Is.EqualTo(testUseLru),
                    "Asset use LRU should match runtime value after SyncFromRuntime."
                );
            }
            finally
            {
                Buffers.WaitInstructionQuantizationStepSeconds = originalRuntimeQuantization;
                Buffers.WaitInstructionMaxDistinctEntries = originalRuntimeMaxEntries;
                Buffers.WaitInstructionUseLruEviction = originalRuntimeUseLru;

                using SerializedObject restoreSerialized = new(asset);
                restoreSerialized
                    .FindProperty(
                        UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                    )
                    .floatValue = originalAssetQuantization;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName)
                    .intValue = originalAssetMaxEntries;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName)
                    .boolValue = originalAssetUseLru;
                restoreSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Validates that buffer settings quantization sanitization handles edge cases correctly.
        /// </summary>
        [TestCase(0f, 0f, Description = "Zero should remain zero (disabled).")]
        [TestCase(-1f, 0f, Description = "Negative values should be sanitized to zero.")]
        [TestCase(float.NaN, 0f, Description = "NaN should be sanitized to zero.")]
        [TestCase(
            float.PositiveInfinity,
            0f,
            Description = "Positive infinity should be sanitized to zero."
        )]
        [TestCase(
            float.NegativeInfinity,
            0f,
            Description = "Negative infinity should be sanitized to zero."
        )]
        [TestCase(0.001f, 0.001f, Description = "Small positive values should be preserved.")]
        [TestCase(1.5f, 1.5f, Description = "Normal positive values should be preserved.")]
        public void BufferSettingsQuantizationSanitizesEdgeCases(float input, float expectedOutput)
        {
            UnityHelpersBufferSettingsAsset asset = Resources.Load<UnityHelpersBufferSettingsAsset>(
                UnityHelpersBufferSettingsAsset.ResourcePath
            );
            if (asset == null)
            {
                Assert.Inconclusive(
                    "UnityHelpersBufferSettingsAsset not found in Resources. "
                        + "Run the settings UI to create the asset first."
                );
                return;
            }

            float originalQuantization = asset.QuantizationStepSeconds;

            try
            {
                using SerializedObject assetSerialized = new(asset);
                assetSerialized
                    .FindProperty(
                        UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                    )
                    .floatValue = input;
                assetSerialized.ApplyModifiedPropertiesWithoutUndo();

                float sanitizedValue = asset.QuantizationStepSeconds;

                Assert.That(
                    sanitizedValue,
                    Is.EqualTo(expectedOutput),
                    $"Input {input} should be sanitized to {expectedOutput}."
                );
            }
            finally
            {
                using SerializedObject restoreSerialized = new(asset);
                restoreSerialized
                    .FindProperty(
                        UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                    )
                    .floatValue = originalQuantization;
                restoreSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Validates that buffer settings max distinct entries sanitization handles edge cases correctly.
        /// </summary>
        [TestCase(0, 0, Description = "Zero should remain zero (unbounded).")]
        [TestCase(-1, 0, Description = "Negative values should be sanitized to zero.")]
        [TestCase(-100, 0, Description = "Large negative values should be sanitized to zero.")]
        [TestCase(1, 1, Description = "Minimum positive values should be preserved.")]
        [TestCase(512, 512, Description = "Default value should be preserved.")]
        [TestCase(10000, 10000, Description = "Large positive values should be preserved.")]
        public void BufferSettingsMaxDistinctEntriesSanitizesEdgeCases(
            int input,
            int expectedOutput
        )
        {
            UnityHelpersBufferSettingsAsset asset = Resources.Load<UnityHelpersBufferSettingsAsset>(
                UnityHelpersBufferSettingsAsset.ResourcePath
            );
            if (asset == null)
            {
                Assert.Inconclusive(
                    "UnityHelpersBufferSettingsAsset not found in Resources. "
                        + "Run the settings UI to create the asset first."
                );
                return;
            }

            int originalMaxEntries = asset.MaxDistinctEntries;

            try
            {
                using SerializedObject assetSerialized = new(asset);
                assetSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName)
                    .intValue = input;
                assetSerialized.ApplyModifiedPropertiesWithoutUndo();

                int sanitizedValue = asset.MaxDistinctEntries;

                Assert.That(
                    sanitizedValue,
                    Is.EqualTo(expectedOutput),
                    $"Input {input} should be sanitized to {expectedOutput}."
                );
            }
            finally
            {
                using SerializedObject restoreSerialized = new(asset);
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName)
                    .intValue = originalMaxEntries;
                restoreSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Validates that the bootstrap initialization applies settings when ApplyOnLoad is true.
        /// This test simulates the bootstrap behavior without actually triggering domain reload.
        /// </summary>
        [Test]
        public void BufferSettingsBootstrapAppliesSettingsWhenApplyOnLoadIsTrue()
        {
            UnityHelpersBufferSettingsAsset asset = Resources.Load<UnityHelpersBufferSettingsAsset>(
                UnityHelpersBufferSettingsAsset.ResourcePath
            );
            if (asset == null)
            {
                Assert.Inconclusive(
                    "UnityHelpersBufferSettingsAsset not found in Resources. "
                        + "Run the settings UI to create the asset first."
                );
                return;
            }

            float originalRuntimeQuantization = Buffers.WaitInstructionQuantizationStepSeconds;
            int originalRuntimeMaxEntries = Buffers.WaitInstructionMaxDistinctEntries;
            bool originalRuntimeUseLru = Buffers.WaitInstructionUseLruEviction;

            float originalAssetQuantization = asset.QuantizationStepSeconds;
            int originalAssetMaxEntries = asset.MaxDistinctEntries;
            bool originalAssetUseLru = asset.UseLruEviction;
            bool originalApplyOnLoad = asset.ApplyOnLoad;

            try
            {
                float testQuantization = 0.33f;
                int testMaxEntries = 64;
                bool testUseLru = true;

                using (SerializedObject assetSerialized = new(asset))
                {
                    assetSerialized
                        .FindProperty(
                            UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                        )
                        .floatValue = testQuantization;
                    assetSerialized
                        .FindProperty(
                            UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName
                        )
                        .intValue = testMaxEntries;
                    assetSerialized
                        .FindProperty(UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName)
                        .boolValue = testUseLru;
                    assetSerialized
                        .FindProperty(UnityHelpersBufferSettingsAsset.ApplyOnLoadPropertyName)
                        .boolValue = true;
                    assetSerialized.ApplyModifiedPropertiesWithoutUndo();
                }

                Buffers.WaitInstructionQuantizationStepSeconds = 0f;
                Buffers.WaitInstructionMaxDistinctEntries = 999;
                Buffers.WaitInstructionUseLruEviction = false;

                UnityHelpersBufferSettingsAsset loadedAsset =
                    Resources.Load<UnityHelpersBufferSettingsAsset>(
                        UnityHelpersBufferSettingsAsset.ResourcePath
                    );
                Assert.IsTrue(loadedAsset != null, "Should be able to load asset from Resources.");

                if (loadedAsset.ApplyOnLoad)
                {
                    loadedAsset.ApplyToBuffers();
                }

                Assert.That(
                    Buffers.WaitInstructionQuantizationStepSeconds,
                    Is.EqualTo(testQuantization),
                    "Runtime quantization should be applied from asset when ApplyOnLoad is true."
                );
                Assert.That(
                    Buffers.WaitInstructionMaxDistinctEntries,
                    Is.EqualTo(testMaxEntries),
                    "Runtime max entries should be applied from asset when ApplyOnLoad is true."
                );
                Assert.That(
                    Buffers.WaitInstructionUseLruEviction,
                    Is.EqualTo(testUseLru),
                    "Runtime use LRU should be applied from asset when ApplyOnLoad is true."
                );
            }
            finally
            {
                Buffers.WaitInstructionQuantizationStepSeconds = originalRuntimeQuantization;
                Buffers.WaitInstructionMaxDistinctEntries = originalRuntimeMaxEntries;
                Buffers.WaitInstructionUseLruEviction = originalRuntimeUseLru;

                using SerializedObject restoreSerialized = new(asset);
                restoreSerialized
                    .FindProperty(
                        UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                    )
                    .floatValue = originalAssetQuantization;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName)
                    .intValue = originalAssetMaxEntries;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName)
                    .boolValue = originalAssetUseLru;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.ApplyOnLoadPropertyName)
                    .boolValue = originalApplyOnLoad;
                restoreSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Validates that the bootstrap initialization does NOT apply settings when ApplyOnLoad is false.
        /// </summary>
        [Test]
        public void BufferSettingsBootstrapSkipsApplicationWhenApplyOnLoadIsFalse()
        {
            UnityHelpersBufferSettingsAsset asset = Resources.Load<UnityHelpersBufferSettingsAsset>(
                UnityHelpersBufferSettingsAsset.ResourcePath
            );
            if (asset == null)
            {
                Assert.Inconclusive(
                    "UnityHelpersBufferSettingsAsset not found in Resources. "
                        + "Run the settings UI to create the asset first."
                );
                return;
            }

            float originalRuntimeQuantization = Buffers.WaitInstructionQuantizationStepSeconds;
            int originalRuntimeMaxEntries = Buffers.WaitInstructionMaxDistinctEntries;
            bool originalRuntimeUseLru = Buffers.WaitInstructionUseLruEviction;

            float originalAssetQuantization = asset.QuantizationStepSeconds;
            int originalAssetMaxEntries = asset.MaxDistinctEntries;
            bool originalAssetUseLru = asset.UseLruEviction;
            bool originalApplyOnLoad = asset.ApplyOnLoad;

            try
            {
                float assetQuantization = 0.99f;
                int assetMaxEntries = 77;
                bool assetUseLru = true;

                float runtimeQuantization = 0.11f;
                int runtimeMaxEntries = 222;
                bool runtimeUseLru = false;

                using (SerializedObject assetSerialized = new(asset))
                {
                    assetSerialized
                        .FindProperty(
                            UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                        )
                        .floatValue = assetQuantization;
                    assetSerialized
                        .FindProperty(
                            UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName
                        )
                        .intValue = assetMaxEntries;
                    assetSerialized
                        .FindProperty(UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName)
                        .boolValue = assetUseLru;
                    assetSerialized
                        .FindProperty(UnityHelpersBufferSettingsAsset.ApplyOnLoadPropertyName)
                        .boolValue = false;
                    assetSerialized.ApplyModifiedPropertiesWithoutUndo();
                }

                Buffers.WaitInstructionQuantizationStepSeconds = runtimeQuantization;
                Buffers.WaitInstructionMaxDistinctEntries = runtimeMaxEntries;
                Buffers.WaitInstructionUseLruEviction = runtimeUseLru;

                UnityHelpersBufferSettingsAsset loadedAsset =
                    Resources.Load<UnityHelpersBufferSettingsAsset>(
                        UnityHelpersBufferSettingsAsset.ResourcePath
                    );
                Assert.IsTrue(loadedAsset != null, "Should be able to load asset from Resources.");

                if (loadedAsset.ApplyOnLoad)
                {
                    loadedAsset.ApplyToBuffers();
                }

                Assert.That(
                    Buffers.WaitInstructionQuantizationStepSeconds,
                    Is.EqualTo(runtimeQuantization),
                    "Runtime quantization should remain unchanged when ApplyOnLoad is false."
                );
                Assert.That(
                    Buffers.WaitInstructionMaxDistinctEntries,
                    Is.EqualTo(runtimeMaxEntries),
                    "Runtime max entries should remain unchanged when ApplyOnLoad is false."
                );
                Assert.That(
                    Buffers.WaitInstructionUseLruEviction,
                    Is.EqualTo(runtimeUseLru),
                    "Runtime use LRU should remain unchanged when ApplyOnLoad is false."
                );
            }
            finally
            {
                Buffers.WaitInstructionQuantizationStepSeconds = originalRuntimeQuantization;
                Buffers.WaitInstructionMaxDistinctEntries = originalRuntimeMaxEntries;
                Buffers.WaitInstructionUseLruEviction = originalRuntimeUseLru;

                using SerializedObject restoreSerialized = new(asset);
                restoreSerialized
                    .FindProperty(
                        UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
                    )
                    .floatValue = originalAssetQuantization;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName)
                    .intValue = originalAssetMaxEntries;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName)
                    .boolValue = originalAssetUseLru;
                restoreSerialized
                    .FindProperty(UnityHelpersBufferSettingsAsset.ApplyOnLoadPropertyName)
                    .boolValue = originalApplyOnLoad;
                restoreSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>
        /// Validates that the buffer settings asset resource path constant matches expected structure.
        /// </summary>
        [Test]
        public void BufferSettingsAssetResourcePathIsValid()
        {
            Assert.IsFalse(
                string.IsNullOrEmpty(UnityHelpersBufferSettingsAsset.ResourcePath),
                "Resource path should not be null or empty."
            );
            Assert.IsFalse(
                UnityHelpersBufferSettingsAsset.ResourcePath.EndsWith(".asset"),
                "Resource path should not include the .asset extension for Resources.Load."
            );
            Assert.IsTrue(
                UnityHelpersBufferSettingsAsset.ResourcePath.Contains("Wallstop Studios"),
                "Resource path should be organized under Wallstop Studios folder."
            );
        }

        /// <summary>
        /// Validates that the buffer settings asset asset path constant is properly formed for AssetDatabase.
        /// </summary>
        [Test]
        public void BufferSettingsAssetAssetPathIsValid()
        {
            Assert.IsFalse(
                string.IsNullOrEmpty(UnityHelpersBufferSettingsAsset.AssetPath),
                "Asset path should not be null or empty."
            );
            Assert.IsTrue(
                UnityHelpersBufferSettingsAsset.AssetPath.StartsWith("Assets/"),
                "Asset path should start with Assets/ for AssetDatabase operations."
            );
            Assert.IsTrue(
                UnityHelpersBufferSettingsAsset.AssetPath.EndsWith(".asset"),
                "Asset path should end with .asset extension."
            );
            Assert.IsTrue(
                UnityHelpersBufferSettingsAsset.AssetPath.Contains("Resources/"),
                "Asset path should be under Resources folder for runtime loading."
            );
        }

        /// <summary>
        /// Validates that the buffer settings property name constants match actual field names.
        /// </summary>
        [Test]
        public void BufferSettingsPropertyNameConstantsMatchFields()
        {
            UnityHelpersBufferSettingsAsset asset = Resources.Load<UnityHelpersBufferSettingsAsset>(
                UnityHelpersBufferSettingsAsset.ResourcePath
            );
            if (asset == null)
            {
                Assert.Inconclusive(
                    "UnityHelpersBufferSettingsAsset not found in Resources. "
                        + "Run the settings UI to create the asset first."
                );
                return;
            }

            using SerializedObject serialized = new(asset);

            SerializedProperty applyOnLoadProperty = serialized.FindProperty(
                UnityHelpersBufferSettingsAsset.ApplyOnLoadPropertyName
            );
            Assert.IsTrue(
                applyOnLoadProperty != null,
                $"Property '{UnityHelpersBufferSettingsAsset.ApplyOnLoadPropertyName}' should exist on the asset."
            );

            SerializedProperty quantizationProperty = serialized.FindProperty(
                UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName
            );
            Assert.IsTrue(
                quantizationProperty != null,
                $"Property '{UnityHelpersBufferSettingsAsset.QuantizationStepSecondsPropertyName}' should exist on the asset."
            );

            SerializedProperty maxEntriesProperty = serialized.FindProperty(
                UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName
            );
            Assert.IsTrue(
                maxEntriesProperty != null,
                $"Property '{UnityHelpersBufferSettingsAsset.MaxDistinctEntriesPropertyName}' should exist on the asset."
            );

            SerializedProperty useLruProperty = serialized.FindProperty(
                UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName
            );
            Assert.IsTrue(
                useLruProperty != null,
                $"Property '{UnityHelpersBufferSettingsAsset.UseLruEvictionPropertyName}' should exist on the asset."
            );
        }

        [Test]
        public void SerializedPropertyNamesFailedTestsOutputDirectoryMatchesFieldName()
        {
            Assert.AreEqual(
                "_failedTestsOutputDirectory",
                UnityHelpersSettings.SerializedPropertyNames.FailedTestsOutputDirectory
            );
        }

        /// <summary>
        /// Helper method to get available relative properties for diagnostic messages.
        /// </summary>
        private static string GetAvailableRelativeProperties(SerializedProperty property)
        {
            List<string> propertyNames = new();
            SerializedProperty iterator = property.Copy();
            SerializedProperty endProperty = property.GetEndProperty();

            if (iterator.NextVisible(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(iterator, endProperty))
                    {
                        break;
                    }

                    propertyNames.Add(iterator.name);
                } while (iterator.NextVisible(false));
            }

            return propertyNames.Count > 0
                ? string.Join(", ", propertyNames)
                : "(no visible properties found)";
        }
    }
}
#endif
