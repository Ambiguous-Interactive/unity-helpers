// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonMetadataCacheTests : CommonTestBase
    {
        private static int FindDictionaryKeyIndex(SerializedProperty keysProperty, string key)
        {
            for (int index = 0; index < keysProperty.arraySize; index++)
            {
                SerializedProperty keyProperty = keysProperty.GetArrayElementAtIndex(index);
                if (string.Equals(keyProperty.stringValue, key, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        [Test]
        public void MetadataSortedByDrawOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(SampleTarget)
            );
            Assert.That(metadata, Is.Not.Empty);
            int previousOrder = metadata[0].DrawOrder;
            for (int index = 1; index < metadata.Count; index++)
            {
                int currentOrder = metadata[index].DrawOrder;
                Assert.That(previousOrder, Is.LessThanOrEqualTo(currentOrder));
                previousOrder = currentOrder;
            }
        }

        [Test]
        public void MetadataCapturesCancellationTokenIndex()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(SampleTarget)
            );
            WButtonMethodMetadata method = metadata.First(m =>
                m.Method.Name == nameof(SampleTarget.MethodWithCancellation)
            );
            Assert.That(method.CancellationTokenParameterIndex, Is.EqualTo(0));
        }

        [Test]
        public void MetadataCapturesColorKey()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(SampleTarget)
            );
            WButtonMethodMetadata method = metadata.First(m =>
                m.Method.Name == nameof(SampleTarget.PriorityMethod)
            );
            Assert.That(method.ColorKey, Is.EqualTo("Critical"));
#pragma warning disable CS0618
            Assert.That(method.Priority, Is.EqualTo("Critical"));
#pragma warning restore CS0618
        }

        [Test]
        public void MetadataCapturesGroupName()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(SampleTarget)
            );
            WButtonMethodMetadata method = metadata.First(m =>
                m.Method.Name == nameof(SampleTarget.NamedGroupMethod)
            );
            Assert.That(method.GroupName, Is.EqualTo("Utilities"));
        }

        [Test]
        public void AsyncTaskMetadataDetectsResultType()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(SampleTarget)
            );
            WButtonMethodMetadata method = metadata.First(m =>
                m.Method.Name == nameof(SampleTarget.TaskMethodAsync)
            );
            Assert.That(method.ExecutionKind, Is.EqualTo(WButtonExecutionKind.Task));
            Assert.AreEqual(typeof(int), method.AsyncResultType);
        }

        [Test]
        public void EnumeratorMetadataClassifiedCorrectly()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(SampleTarget)
            );
            WButtonMethodMetadata method = metadata.First(m =>
                m.Method.Name == nameof(SampleTarget.EnumeratorMethod)
            );
            Assert.That(method.ExecutionKind, Is.EqualTo(WButtonExecutionKind.Enumerator));
        }

        [Test]
        public void ParameterStatesInitializeWithDefaults()
        {
            SampleTarget asset = Track(ScriptableObject.CreateInstance<SampleTarget>());
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(SampleTarget)
            );
            WButtonMethodMetadata method = metadata.First(m =>
                m.Method.Name == nameof(SampleTarget.MethodWithDefaults)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(method);
            Assert.That(methodState.Parameters.Length, Is.EqualTo(2));
            Assert.That(methodState.Parameters[0].CurrentValue, Is.EqualTo(7));
            Assert.That(methodState.Parameters[1].CurrentValue, Is.EqualTo("hello"));
        }

        [Test]
        public void ResolveCustomColorReturnsDefaultsAndOverrides()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            using SerializedObject serialized = new(settings);
            SerializedProperty palette = serialized.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            SerializedProperty keys = palette.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty values = palette.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            serialized.Update();

            List<(string Key, Color ButtonColor, Color TextColor)> originalEntries = new(
                keys.arraySize
            );
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

            string colorKey = "Critical";
            Color expectedColor = new(0.85f, 0.2f, 0.2f);
            Color expectedTextColor = WButtonColorUtility.GetReadableTextColor(expectedColor);

            try
            {
                int entryIndex = FindDictionaryKeyIndex(keys, colorKey);
                if (entryIndex < 0)
                {
                    int newIndex = keys.arraySize;
                    keys.InsertArrayElementAtIndex(newIndex);
                    values.InsertArrayElementAtIndex(newIndex);
                    entryIndex = newIndex;
                }

                SerializedProperty keyProperty = keys.GetArrayElementAtIndex(entryIndex);
                keyProperty.stringValue = colorKey;

                SerializedProperty valueProperty = values.GetArrayElementAtIndex(entryIndex);
                valueProperty
                    .FindPropertyRelative(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                    )
                    .colorValue = expectedColor;
                valueProperty
                    .FindPropertyRelative(
                        UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                    )
                    .colorValue = expectedTextColor;

                serialized.ApplyModifiedPropertiesWithoutUndo();
                settings.SaveSettings();

                UnityHelpersSettings.WButtonPaletteEntry resolvedCustom =
                    UnityHelpersSettings.ResolveWButtonPalette(colorKey);
                Assert.That(resolvedCustom.ButtonColor, Is.EqualTo(expectedColor));
                Assert.That(resolvedCustom.TextColor, Is.EqualTo(expectedTextColor));

                UnityHelpersSettings.WButtonPaletteEntry resolvedDefault =
                    UnityHelpersSettings.ResolveWButtonPalette(null);
                Assert.That(resolvedDefault.ButtonColor.a, Is.GreaterThan(0.9f));
                Assert.That(resolvedDefault.TextColor.a, Is.GreaterThan(0.9f));
            }
            finally
            {
                serialized.Update();
                keys.arraySize = originalEntries.Count;
                values.arraySize = originalEntries.Count;
                for (int index = 0; index < originalEntries.Count; index++)
                {
                    (string Key, Color ButtonColor, Color TextColor) original = originalEntries[
                        index
                    ];
                    SerializedProperty keyProperty = keys.GetArrayElementAtIndex(index);
                    keyProperty.stringValue = original.Key;
                    SerializedProperty valueProperty = values.GetArrayElementAtIndex(index);
                    valueProperty
                        .FindPropertyRelative(
                            UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorButton
                        )
                        .colorValue = original.ButtonColor;
                    valueProperty
                        .FindPropertyRelative(
                            UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColorText
                        )
                        .colorValue = original.TextColor;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                settings.SaveSettings();
            }
        }
    }
}
#endif
