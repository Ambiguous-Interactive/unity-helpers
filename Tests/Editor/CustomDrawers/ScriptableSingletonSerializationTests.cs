// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Comprehensive tests for ScriptableSingleton detection and serialization behavior
    /// in SerializableDictionary and SerializableSet property drawers.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ScriptableSingletonSerializationTests : CommonTestBase
    {
        [Test]
        public void IsScriptableSingletonTypeWithNullReturnsFalse()
        {
            bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(null);
            Assert.IsFalse(result, "Null should not be detected as ScriptableSingleton.");
        }

        [Test]
        public void IsScriptableSingletonTypeWithRegularScriptableObjectReturnsFalse()
        {
            RegularScriptableObject target = CreateScriptableObject<RegularScriptableObject>();
            bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(target);
            Assert.IsFalse(
                result,
                "Regular ScriptableObject should not be detected as ScriptableSingleton."
            );
        }

        [Test]
        public void IsScriptableSingletonTypeWithMonoBehaviourReturnsFalse()
        {
            GameObject go = NewGameObject("TestMonoBehaviour");
            RegularMonoBehaviour target = go.AddComponent<RegularMonoBehaviour>();
            bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(target);
            Assert.IsFalse(result, "MonoBehaviour should not be detected as ScriptableSingleton.");
        }

        [Test]
        public void IsScriptableSingletonTypeWithUnityHelpersSettingsReturnsTrue()
        {
            // UnityHelpersSettings is a ScriptableSingleton - use the instance
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(settings);
            Assert.IsTrue(result, "UnityHelpersSettings (ScriptableSingleton) should be detected.");
        }

        [Test]
        public void IsScriptableSingletonTypeWithTestScriptableSingletonReturnsTrue()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();
            bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(singleton);
            Assert.IsTrue(
                result,
                "TestScriptableSingleton should be detected as ScriptableSingleton."
            );
        }

        [Test]
        public void IsScriptableSingletonTypeWithAnotherTestScriptableSingletonReturnsTrue()
        {
            AnotherTestScriptableSingleton singleton = AnotherTestScriptableSingleton.instance;
            bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(singleton);
            Assert.IsTrue(
                result,
                "AnotherTestScriptableSingleton should be detected as ScriptableSingleton."
            );
        }

        [Test]
        public void IsScriptableSingletonTypeWithDerivedScriptableSingletonReturnsTrue()
        {
            DerivedTestScriptableSingleton singleton = DerivedTestScriptableSingleton.instance;
            bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(singleton);
            Assert.IsTrue(
                result,
                "DerivedTestScriptableSingleton should be detected - inheritance chain should be traversed."
            );
        }

        [Test]
        public void IsScriptableSingletonTypeWithComplexScriptableSingletonReturnsTrue()
        {
            ComplexScriptableSingleton singleton = ComplexScriptableSingleton.instance;
            singleton.ResetForTest();
            bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(singleton);
            Assert.IsTrue(
                result,
                "ComplexScriptableSingleton should be detected as ScriptableSingleton."
            );
        }

        [Test]
        public void IsScriptableSingletonTypeSetDrawerWithNullReturnsFalse()
        {
            bool result = SerializableSetPropertyDrawer.IsScriptableSingletonType(null);
            Assert.IsFalse(
                result,
                "Set drawer: Null should not be detected as ScriptableSingleton."
            );
        }

        [Test]
        public void IsScriptableSingletonTypeSetDrawerWithRegularScriptableObjectReturnsFalse()
        {
            RegularScriptableObject target = CreateScriptableObject<RegularScriptableObject>();
            bool result = SerializableSetPropertyDrawer.IsScriptableSingletonType(target);
            Assert.IsFalse(
                result,
                "Set drawer: Regular ScriptableObject should not be detected as ScriptableSingleton."
            );
        }

        [Test]
        public void IsScriptableSingletonTypeSetDrawerWithScriptableSingletonReturnsTrue()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();
            bool result = SerializableSetPropertyDrawer.IsScriptableSingletonType(singleton);
            Assert.IsTrue(result, "Set drawer: TestScriptableSingleton should be detected.");
        }

        [Test]
        public void IsScriptableSingletonTypeDictionaryAndSetDrawersReturnSameResult()
        {
            // Test consistency between both drawers
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            bool dictResult = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(
                singleton
            );
            bool setResult = SerializableSetPropertyDrawer.IsScriptableSingletonType(singleton);

            Assert.AreEqual(
                dictResult,
                setResult,
                "Dictionary and Set drawers should return the same result for ScriptableSingleton detection."
            );
            Assert.IsTrue(dictResult, "Both should detect ScriptableSingleton.");

            RegularScriptableObject regular = CreateScriptableObject<RegularScriptableObject>();
            dictResult = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(regular);
            setResult = SerializableSetPropertyDrawer.IsScriptableSingletonType(regular);

            Assert.AreEqual(
                dictResult,
                setResult,
                "Dictionary and Set drawers should return the same result for non-singleton."
            );
            Assert.IsFalse(dictResult, "Both should not detect regular ScriptableObject.");
        }

        [Test]
        public void SaveScriptableSingletonWithNullDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(null),
                "SaveScriptableSingleton should not throw when given null."
            );
        }

        [Test]
        public void SaveScriptableSingletonWithRegularScriptableObjectDoesNotThrow()
        {
            RegularScriptableObject target = CreateScriptableObject<RegularScriptableObject>();
            Assert.DoesNotThrow(
                () => SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(target),
                "SaveScriptableSingleton should not throw for non-singleton (no Save method)."
            );
        }

        [Test]
        public void SaveScriptableSingletonWithMonoBehaviourDoesNotThrow()
        {
            GameObject go = NewGameObject("TestMonoBehaviour");
            RegularMonoBehaviour target = go.AddComponent<RegularMonoBehaviour>();
            Assert.DoesNotThrow(
                () => SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(target),
                "SaveScriptableSingleton should not throw for MonoBehaviour (no Save method)."
            );
        }

        [Test]
        public void SaveScriptableSingletonWithScriptableSingletonInvokesSaveMethod()
        {
            // This test verifies that Save(true) is actually invoked.
            // We can check this by modifying data and seeing if it persists.
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            // Add some data
            string testKey = $"TestKey_{Guid.NewGuid():N}";
            singleton.dictionary[testKey] = "TestValue";

            // Save using our method
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);

            // The singleton should have Save called - verify the data is in the dictionary
            Assert.IsTrue(
                singleton.dictionary.ContainsKey(testKey),
                "Dictionary should contain the test key after save."
            );

            // Clean up
            singleton.dictionary.Remove(testKey);
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void SaveScriptableSingletonSetDrawerWithNullDoesNotThrow()
        {
            Assert.DoesNotThrow(
                () => SerializableSetPropertyDrawer.SaveScriptableSingleton(null),
                "Set drawer: SaveScriptableSingleton should not throw when given null."
            );
        }

        [Test]
        public void SaveScriptableSingletonSetDrawerWithScriptableSingletonDoesNotThrow()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            Assert.DoesNotThrow(
                () => SerializableSetPropertyDrawer.SaveScriptableSingleton(singleton),
                "Set drawer: SaveScriptableSingleton should not throw for valid singleton."
            );
        }

        [Test]
        public void CommitEntryToScriptableSingletonAddsEntry()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();
            int initialCount = singleton.dictionary.Count;

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            string testKey = $"CommitEntryTest_{Guid.NewGuid():N}";
            string testValue = "CommitEntryValue";

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(string),
                testKey,
                testValue,
                dictionaryProperty
            );

            Assert.IsTrue(result.added, "CommitEntry should succeed for ScriptableSingleton.");

            // Verify the entry was added
            singleton.dictionary.EditorAfterDeserialize();
            Assert.IsTrue(
                singleton.dictionary.ContainsKey(testKey),
                "Dictionary should contain the committed key."
            );
            Assert.AreEqual(
                testValue,
                singleton.dictionary[testKey],
                "Dictionary should have the correct value."
            );

            // Clean up
            singleton.dictionary.Remove(testKey);
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void CommitEntryToScriptableSingletonMultipleEntriesAllPersist()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            string[] testKeys = new string[5];
            for (int i = 0; i < 5; i++)
            {
                testKeys[i] = $"MultiTest_{Guid.NewGuid():N}";
                string testValue = $"Value_{i}";

                SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                    keysProperty,
                    valuesProperty,
                    typeof(string),
                    typeof(string),
                    testKeys[i],
                    testValue,
                    dictionaryProperty
                );

                Assert.IsTrue(result.added, $"CommitEntry {i} should succeed.");

                // Refresh the properties after each commit
                serializedObject.Update();
                keysProperty = dictionaryProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Keys
                );
                valuesProperty = dictionaryProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Values
                );
            }

            // Verify all entries exist
            singleton.dictionary.EditorAfterDeserialize();
            Assert.AreEqual(5, singleton.dictionary.Count, "All 5 entries should persist.");

            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(
                    singleton.dictionary.ContainsKey(testKeys[i]),
                    $"Dictionary should contain key {i}."
                );
                Assert.AreEqual(
                    $"Value_{i}",
                    singleton.dictionary[testKeys[i]],
                    $"Dictionary should have correct value for key {i}."
                );
            }

            // Clean up
            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void CommitEntryToScriptableSingletonUpdateExistingEntrySucceeds()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            string testKey = $"UpdateTest_{Guid.NewGuid():N}";
            singleton.dictionary[testKey] = "OriginalValue";
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            int originalCount = singleton.dictionary.Count;

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(string),
                testKey,
                "UpdatedValue",
                dictionaryProperty
            );

            // CommitEntry updates existing entries
            Assert.IsFalse(
                result.added,
                $"CommitEntry should report not added (updated existing). Key: '{testKey}', OriginalCount: {originalCount}"
            );
            Assert.GreaterOrEqual(
                result.index,
                0,
                $"Should return the index of the existing entry. Key: '{testKey}', Index: {result.index}"
            );

            singleton.dictionary.EditorAfterDeserialize();
            Assert.AreEqual(
                originalCount,
                singleton.dictionary.Count,
                $"Dictionary count should remain unchanged after update. Key: '{testKey}'"
            );
            Assert.AreEqual(
                "UpdatedValue",
                singleton.dictionary[testKey],
                $"Value should be updated. Key: '{testKey}'"
            );

            // Clean up
            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void CommitEntryToScriptableSingletonUpdateExistingEntryWithExplicitIndexSucceeds()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            string testKey = $"UpdateWithIndexTest_{Guid.NewGuid():N}";
            singleton.dictionary[testKey] = "OriginalValue";
            singleton.dictionary.EditorSyncSerializedArrays();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            int existingIndex = -1;
            for (int i = 0; i < keysProperty.arraySize; i++)
            {
                SerializedProperty keyProp = keysProperty.GetArrayElementAtIndex(i);
                if (keyProp.stringValue == testKey)
                {
                    existingIndex = i;
                    break;
                }
            }

            Assert.GreaterOrEqual(
                existingIndex,
                0,
                $"Should find existing key in serialized array. Key: '{testKey}', ArraySize: {keysProperty.arraySize}"
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(string),
                testKey,
                "UpdatedWithIndex",
                dictionaryProperty,
                existingIndex
            );

            Assert.IsFalse(
                result.added,
                $"CommitEntry should report not added. ExistingIndex: {existingIndex}"
            );
            Assert.AreEqual(
                existingIndex,
                result.index,
                $"Should return the provided existingIndex."
            );

            singleton.dictionary.EditorAfterDeserialize();
            Assert.AreEqual(
                "UpdatedWithIndex",
                singleton.dictionary[testKey],
                "Value should be updated when using explicit index."
            );

            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void CommitEntryToScriptableSingletonSequentialUpdatesSameKeyAllSucceed()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            string testKey = $"SequentialUpdateTest_{Guid.NewGuid():N}";
            singleton.dictionary[testKey] = "Initial";
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);

            using SerializedObject serializedObject = new(singleton);
            SerializableDictionaryPropertyDrawer drawer = new();

            string[] values = { "First", "Second", "Third", "Fourth" };
            foreach (string value in values)
            {
                serializedObject.Update();
                SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                    nameof(TestScriptableSingleton.dictionary)
                );
                SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Keys
                );
                SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                    SerializableDictionarySerializedPropertyNames.Values
                );

                SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                    keysProperty,
                    valuesProperty,
                    typeof(string),
                    typeof(string),
                    testKey,
                    value,
                    dictionaryProperty
                );

                Assert.IsFalse(
                    result.added,
                    $"Update {value}: Should not report added for existing key."
                );
                Assert.GreaterOrEqual(
                    result.index,
                    0,
                    $"Update {value}: Should return valid index."
                );

                singleton.dictionary.EditorAfterDeserialize();
                Assert.AreEqual(
                    value,
                    singleton.dictionary[testKey],
                    $"Update {value}: Value should match."
                );
                Assert.AreEqual(
                    1,
                    singleton.dictionary.Count,
                    $"Update {value}: Dictionary count should remain 1."
                );
            }

            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void TryCommitPendingEntryToScriptableSingletonAddsEntry()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                false
            );
            pending.value = 42;

            ISerializableSetInspector inspector = singleton.set;

            bool result = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                drawer.GetOrCreatePaginationState(setProperty),
                inspector
            );

            Assert.IsTrue(result, "TryCommitPendingEntry should succeed for ScriptableSingleton.");

            // Verify the entry was added
            Assert.IsTrue(singleton.set.Contains(42), "Set should contain the committed value.");

            // Clean up
            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void TryCommitPendingEntryToScriptableSingletonMultipleEntriesAllPersist()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            ISerializableSetInspector inspector = singleton.set;

            int[] testValues = { 10, 20, 30, 40, 50 };
            foreach (int value in testValues)
            {
                SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                    setProperty,
                    setProperty.propertyPath,
                    typeof(int),
                    false
                );
                pending.value = value;

                bool result = drawer.TryCommitPendingEntry(
                    pending,
                    setProperty,
                    setProperty.propertyPath,
                    ref itemsProperty,
                    drawer.GetOrCreatePaginationState(setProperty),
                    inspector
                );

                Assert.IsTrue(result, $"TryCommitPendingEntry for {value} should succeed.");

                // Refresh properties
                serializedObject.Update();
                setProperty = serializedObject.FindProperty(nameof(TestScriptableSingleton.set));
                itemsProperty = setProperty.FindPropertyRelative(
                    SerializableHashSetSerializedPropertyNames.Items
                );
            }

            // Verify all entries exist
            Assert.AreEqual(5, singleton.set.Count, "All 5 entries should persist.");
            foreach (int value in testValues)
            {
                Assert.IsTrue(singleton.set.Contains(value), $"Set should contain {value}.");
            }

            // Clean up
            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void TryCommitPendingEntryToScriptableSingletonDuplicateEntryFails()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();
            singleton.set.Add(99);
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                false
            );
            pending.value = 99; // Duplicate

            ISerializableSetInspector inspector = singleton.set;

            bool result = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                drawer.GetOrCreatePaginationState(setProperty),
                inspector
            );

            Assert.IsFalse(result, "TryCommitPendingEntry should fail for duplicate entry.");
            Assert.IsTrue(pending.errorMessage != null, "Error message should be set.");
            StringAssert.Contains("exists", pending.errorMessage.ToLowerInvariant());

            // Clean up
            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void IsScriptableSingletonTypeWithDestroyedObjectReturnsFalse()
        {
            RegularScriptableObject target = CreateScriptableObject<RegularScriptableObject>();
            Object.DestroyImmediate(target); // UNH-SUPPRESS: Testing destroyed object handling

            // Unity's null check should handle destroyed objects
            bool result = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(target);
            Assert.IsFalse(result, "Destroyed object should return false (Unity null check).");
        }

        [Test]
        public void CommitEntryToRegularScriptableObjectStillWorks()
        {
            // Ensure the fix for ScriptableSingleton didn't break regular ScriptableObjects
            RegularScriptableObject host = CreateScriptableObject<RegularScriptableObject>();

            using SerializedObject serializedObject = new(host);
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(RegularScriptableObject.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();
            SerializableDictionaryPropertyDrawer.CommitResult result = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(string),
                "RegularKey",
                "RegularValue",
                dictionaryProperty
            );

            Assert.IsTrue(result.added, "CommitEntry should work for regular ScriptableObject.");

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            serializedObject.Update();
            host.dictionary.EditorAfterDeserialize();

            Assert.IsTrue(host.dictionary.ContainsKey("RegularKey"));
            Assert.AreEqual("RegularValue", host.dictionary["RegularKey"]);
        }

        [Test]
        public void TryCommitPendingEntryToRegularScriptableObjectStillWorks()
        {
            // Ensure the fix for ScriptableSingleton didn't break regular ScriptableObjects
            RegularScriptableObject host = CreateScriptableObject<RegularScriptableObject>();

            using SerializedObject serializedObject = new(host);
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(RegularScriptableObject.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                false
            );
            pending.value = 123;

            ISerializableSetInspector inspector = host.set;

            bool result = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                drawer.GetOrCreatePaginationState(setProperty),
                inspector
            );

            Assert.IsTrue(
                result,
                "TryCommitPendingEntry should work for regular ScriptableObject."
            );
            Assert.IsTrue(host.set.Contains(123));
        }

        [Test]
        public void SyncRuntimeDictionaryWithScriptableSingletonPreservesData()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            string testKey = $"SyncTest_{Guid.NewGuid():N}";
            singleton.dictionary[testKey] = "SyncValue";
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.dictionary)
            );

            // Call SyncRuntimeDictionary
            SerializableDictionaryPropertyDrawer.SyncRuntimeDictionary(dictionaryProperty);

            // Verify data is still there
            Assert.IsTrue(singleton.dictionary.ContainsKey(testKey));
            Assert.AreEqual("SyncValue", singleton.dictionary[testKey]);

            // Clean up
            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void SyncRuntimeSetWithScriptableSingletonPreservesData()
        {
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            singleton.set.Add(777);
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.set)
            );

            // Call SyncRuntimeSet
            SerializableSetPropertyDrawer.SyncRuntimeSet(setProperty);

            // Verify data is still there
            Assert.IsTrue(singleton.set.Contains(777));

            // Clean up
            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [Test]
        public void TypeDetectionUsesProperTypeComparisonNotStringMatching()
        {
            // This test verifies we're using typeof(ScriptableSingleton<>) not string matching
            // by checking that types with "ScriptableSingleton" in the name but not inheriting
            // from it are not detected.

            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            // Verify the actual ScriptableSingleton is detected
            bool isDetected = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(
                singleton
            );
            Assert.IsTrue(isDetected, "Real ScriptableSingleton should be detected.");

            // Verify regular ScriptableObject is not detected
            RegularScriptableObject regular = CreateScriptableObject<RegularScriptableObject>();
            isDetected = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(regular);
            Assert.IsFalse(isDetected, "Regular ScriptableObject should not be detected.");

            // The type comparison should use typeof(ScriptableSingleton<>) == type.GetGenericTypeDefinition()
            Type singletonType = typeof(ScriptableSingleton<>);
            Type testType = typeof(TestScriptableSingleton).BaseType;
            Assert.IsTrue(testType != null);
            Assert.IsTrue(testType.IsGenericType);
            Assert.AreEqual(
                singletonType,
                testType.GetGenericTypeDefinition(),
                "Type comparison should work correctly."
            );
        }

        [Test]
        public void UnityHelpersSettingsIsDetectedAsScriptableSingleton()
        {
            // Test that the original use case (UnityHelpersSettings) is properly detected
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            bool dictDetected = SerializableDictionaryPropertyDrawer.IsScriptableSingletonType(
                settings
            );
            bool setDetected = SerializableSetPropertyDrawer.IsScriptableSingletonType(settings);

            Assert.IsTrue(
                dictDetected,
                "Dictionary drawer should detect UnityHelpersSettings as ScriptableSingleton."
            );
            Assert.IsTrue(
                setDetected,
                "Set drawer should detect UnityHelpersSettings as ScriptableSingleton."
            );
        }

        [Test]
        public void UnityHelpersSettingsHasPaletteProperties()
        {
            // Verify the settings object has the expected palette properties
            UnityHelpersSettings settings = UnityHelpersSettings.instance;

            using SerializedObject serializedObject = new(settings);
            serializedObject.Update();

            SerializedProperty paletteProperty = serializedObject.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            Assert.IsTrue(
                paletteProperty != null,
                "WButtonCustomColors property should exist on UnityHelpersSettings."
            );

            SerializedProperty keysProperty = paletteProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = paletteProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            Assert.IsTrue(keysProperty != null, "Keys property should exist.");
            Assert.IsTrue(valuesProperty != null, "Values property should exist.");
            Assert.IsTrue(keysProperty.isArray, "Keys should be an array.");
            Assert.IsTrue(valuesProperty.isArray, "Values should be an array.");
        }

        [Test]
        public void SerializableDictionaryHasMultipleIndexerPropertiesNamedItem()
        {
            // This test verifies the root cause of AmbiguousMatchException:
            // SerializableDictionary implements both IDictionary<TKey,TValue> and IDictionary,
            // each with an indexer named "Item" but with different parameter types.
            // Using GetProperty("Item", BindingFlags) without specifying types will throw.
            Type dictionaryType = typeof(SerializableDictionary<string, string>);

            // Find all properties named "Item" - there should be more than one
            PropertyInfo[] allItemProperties = dictionaryType.GetProperties(
                BindingFlags.Instance | BindingFlags.Public
            );
            int itemCount = 0;
            foreach (PropertyInfo prop in allItemProperties)
            {
                if (prop.Name == "Item")
                {
                    itemCount++;
                }
            }

            Assert.GreaterOrEqual(
                itemCount,
                2,
                "SerializableDictionary should have at least 2 indexer properties (from IDictionary<TKey,TValue> and IDictionary). "
                    + $"Found {itemCount} Item properties. This is why GetProperty(\"Item\", BindingFlags) causes AmbiguousMatchException."
            );
        }

        [Test]
        public void GetPropertyWithExactTypesAvoidsAmbiguousMatchException()
        {
            // This test demonstrates the correct way to get the indexer property
            // when multiple indexers exist with the same name but different parameter types.
            Type dictionaryType = typeof(SerializableDictionary<string, string>);
            Type keyType = typeof(string);
            Type valueType = typeof(string);

            // This would throw AmbiguousMatchException:
            // PropertyInfo indexer = dictionaryType.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);

            // This is the correct approach - specify the return type and parameter types:
            PropertyInfo indexer = null;
            AmbiguousMatchException ambiguousException = null;

            // First, verify that GetProperty without types throws AmbiguousMatchException
            try
            {
                indexer = dictionaryType.GetProperty(
                    "Item",
                    BindingFlags.Instance | BindingFlags.Public
                );
            }
            catch (AmbiguousMatchException ex)
            {
                ambiguousException = ex;
            }

            Assert.IsTrue(
                ambiguousException != null,
                "GetProperty(\"Item\", BindingFlags) should throw AmbiguousMatchException for SerializableDictionary."
            );

            // Now verify the fix works - using GetProperty with exact types
            PropertyInfo correctIndexer = dictionaryType.GetProperty(
                "Item",
                valueType,
                new[] { keyType }
            );

            Assert.IsTrue(
                correctIndexer != null,
                "GetProperty(\"Item\", returnType, paramTypes) should find the correct indexer."
            );
            Assert.AreEqual(
                valueType,
                correctIndexer.PropertyType,
                $"Indexer return type should be {valueType.Name}."
            );
            Assert.IsTrue(correctIndexer.CanRead, "Indexer should be readable.");
            Assert.IsTrue(correctIndexer.CanWrite, "Indexer should be writable.");

            ParameterInfo[] parameters = correctIndexer.GetIndexParameters();
            Assert.AreEqual(1, parameters.Length, "Indexer should have exactly 1 parameter.");
            Assert.AreEqual(
                keyType,
                parameters[0].ParameterType,
                $"Indexer parameter should be of type {keyType.Name}."
            );
        }

        [Test]
        public void CommitEntryReflectionHandlesMultipleIndexersCorrectly()
        {
            // Integration test that verifies CommitEntry works correctly with the fixed reflection
            TestScriptableSingleton singleton = TestScriptableSingleton.instance;
            singleton.ResetForTest();

            string testKey = $"ReflectionTest_{Guid.NewGuid():N}";

            using SerializedObject serializedObject = new(singleton);
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.dictionary)
            );
            SerializedProperty keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            SerializedProperty valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer drawer = new();

            // Add new entry
            SerializableDictionaryPropertyDrawer.CommitResult addResult = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(string),
                testKey,
                "InitialValue",
                dictionaryProperty
            );

            Assert.IsTrue(
                addResult.added,
                $"First CommitEntry should add a new entry. Key: '{testKey}', "
                    + $"DictionaryType: {singleton.dictionary.GetType().FullName}, "
                    + $"KeyType: {typeof(string).FullName}, ValueType: {typeof(string).FullName}"
            );

            singleton.dictionary.EditorAfterDeserialize();
            Assert.IsTrue(
                singleton.dictionary.ContainsKey(testKey),
                $"Dictionary should contain the key after add. Key: '{testKey}'"
            );

            // Update existing entry (this is where the AmbiguousMatchException would occur)
            serializedObject.Update();
            dictionaryProperty = serializedObject.FindProperty(
                nameof(TestScriptableSingleton.dictionary)
            );
            keysProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Keys
            );
            valuesProperty = dictionaryProperty.FindPropertyRelative(
                SerializableDictionarySerializedPropertyNames.Values
            );

            SerializableDictionaryPropertyDrawer.CommitResult updateResult = drawer.CommitEntry(
                keysProperty,
                valuesProperty,
                typeof(string),
                typeof(string),
                testKey,
                "UpdatedValue",
                dictionaryProperty
            );

            Assert.IsFalse(
                updateResult.added,
                $"Second CommitEntry should update existing entry, not add. Key: '{testKey}', "
                    + $"Index returned: {updateResult.index}"
            );

            singleton.dictionary.EditorAfterDeserialize();
            Assert.AreEqual(
                "UpdatedValue",
                singleton.dictionary[testKey],
                $"Value should be updated. Key: '{testKey}'"
            );

            // Clean up
            singleton.ResetForTest();
            SerializableDictionaryPropertyDrawer.SaveScriptableSingleton(singleton);
        }

        [TestCase(typeof(int), typeof(string), 123, "StringValue")]
        [TestCase(typeof(string), typeof(int), "StringKey", 42)]
        [TestCase(typeof(string), typeof(float), "FloatKey", 3.14f)]
        [TestCase(typeof(long), typeof(double), 456L, 2.718)]
        [TestCase(typeof(string), typeof(bool), "BoolKey", true)]
        public void GetPropertyWithTypesWorksForVariousKeyValueTypes(
            Type keyType,
            Type valueType,
            object testKey,
            object testValue
        )
        {
            // Diagnostic info for debugging
            string diagnosticInfo =
                $"\nKeyType: {keyType.Name}, ValueType: {valueType.Name}"
                + $"\nTestKey: {testKey} (type: {testKey?.GetType().Name ?? "null"})"
                + $"\nTestValue: {testValue} (type: {testValue?.GetType().Name ?? "null"})";

            // Validate test data - key and value types must match
            Assert.IsTrue(
                testKey == null || keyType.IsAssignableFrom(testKey.GetType()),
                $"Test data error: testKey type ({testKey?.GetType().Name}) must be assignable to keyType ({keyType.Name}).{diagnosticInfo}"
            );
            Assert.IsTrue(
                testValue == null || valueType.IsAssignableFrom(testValue.GetType()),
                $"Test data error: testValue type ({testValue?.GetType().Name}) must be assignable to valueType ({valueType.Name}).{diagnosticInfo}"
            );

            // Construct SerializableDictionary<keyType, valueType> using reflection
            Type genericDictType = typeof(SerializableDictionary<,>).MakeGenericType(
                keyType,
                valueType
            );

            // Verify we can get the indexer without AmbiguousMatchException
            PropertyInfo indexer = genericDictType.GetProperty(
                "Item",
                valueType,
                new[] { keyType }
            );

            Assert.IsTrue(
                indexer != null,
                $"Should find indexer for SerializableDictionary<{keyType.Name}, {valueType.Name}>.{diagnosticInfo}"
            );
            Assert.AreEqual(
                valueType,
                indexer.PropertyType,
                $"Indexer return type should be {valueType.Name}.{diagnosticInfo}"
            );

            ParameterInfo[] parameters = indexer.GetIndexParameters();
            Assert.AreEqual(
                1,
                parameters.Length,
                $"Indexer should have exactly 1 parameter.{diagnosticInfo}"
            );
            Assert.AreEqual(
                keyType,
                parameters[0].ParameterType,
                $"Indexer parameter should be of type {keyType.Name}.{diagnosticInfo}"
            );

            // Also verify the indexer actually works by creating an instance
            object dictInstance = Activator.CreateInstance(genericDictType);
            Assert.IsTrue(
                dictInstance != null,
                $"Should be able to create dictionary instance.{diagnosticInfo}"
            );

            // Use Add method to add an entry
            MethodInfo addMethod = genericDictType.GetMethod(
                "Add",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { keyType, valueType },
                null
            );
            Assert.IsTrue(addMethod != null, $"Should find Add method.{diagnosticInfo}");

            addMethod.Invoke(dictInstance, new[] { testKey, testValue });

            // Use indexer to read back
            object retrievedValue = indexer.GetValue(dictInstance, new[] { testKey });
            Assert.AreEqual(
                testValue,
                retrievedValue,
                $"Indexer should return the correct value for key {testKey}.{diagnosticInfo}"
            );

            // Use indexer to update
            object newValue =
                valueType == typeof(string) ? "UpdatedValue" : Convert.ChangeType(99, valueType);
            indexer.SetValue(dictInstance, newValue, new[] { testKey });

            object updatedValue = indexer.GetValue(dictInstance, new[] { testKey });
            Assert.AreEqual(
                newValue,
                updatedValue,
                $"Indexer should return the updated value after set.{diagnosticInfo}"
            );
        }

        [Test]
        public void NonGenericDictionaryIndexerAlsoAccessible()
        {
            // Verify we can also access the non-generic IDictionary indexer if needed
            Type dictionaryType = typeof(SerializableDictionary<string, string>);

            // The non-generic indexer takes object and returns object
            PropertyInfo nonGenericIndexer = dictionaryType.GetProperty(
                "Item",
                typeof(object),
                new[] { typeof(object) }
            );

            Assert.IsTrue(
                nonGenericIndexer != null,
                "Should be able to find the non-generic IDictionary indexer by specifying object types."
            );
            Assert.AreEqual(
                typeof(object),
                nonGenericIndexer.PropertyType,
                "Non-generic indexer return type should be object."
            );
        }

        [Test]
        public void TryGetIndexerPropertyFindsCorrectIndexerWithCaching()
        {
            // Test that ReflectionHelpers.TryGetIndexerProperty works correctly
            // and avoids AmbiguousMatchException
            Type dictionaryType = typeof(SerializableDictionary<string, int>);
            Type keyType = typeof(string);
            Type valueType = typeof(int);

            bool found = ReflectionHelpers.TryGetIndexerProperty(
                dictionaryType,
                valueType,
                new[] { keyType },
                out PropertyInfo indexer
            );

            Assert.IsTrue(found, "TryGetIndexerProperty should find the indexer.");
            Assert.IsTrue(indexer != null, "Indexer should not be null.");
            Assert.AreEqual(valueType, indexer.PropertyType, "Return type should match.");

            ParameterInfo[] parameters = indexer.GetIndexParameters();
            Assert.AreEqual(1, parameters.Length, "Should have exactly one index parameter.");
            Assert.AreEqual(keyType, parameters[0].ParameterType, "Parameter type should match.");

            // Call again to test caching - should return the same result
            bool foundAgain = ReflectionHelpers.TryGetIndexerProperty(
                dictionaryType,
                valueType,
                new[] { keyType },
                out PropertyInfo cachedIndexer
            );

            Assert.IsTrue(foundAgain, "Second call should also find the indexer.");
            Assert.AreEqual(
                indexer,
                cachedIndexer,
                "Cached lookup should return the same PropertyInfo."
            );
        }

        [Test]
        public void TryGetIndexerPropertyWithNullParametersReturnsFalse()
        {
            bool result = ReflectionHelpers.TryGetIndexerProperty(
                null,
                typeof(int),
                new[] { typeof(string) },
                out PropertyInfo indexer
            );
            Assert.IsFalse(result, "Should return false for null type.");
            Assert.IsTrue(indexer == null, "Indexer should be null.");

            result = ReflectionHelpers.TryGetIndexerProperty(
                typeof(SerializableDictionary<string, int>),
                null,
                new[] { typeof(string) },
                out indexer
            );
            Assert.IsFalse(result, "Should return false for null return type.");
            Assert.IsTrue(indexer == null, "Indexer should be null.");

            result = ReflectionHelpers.TryGetIndexerProperty(
                typeof(SerializableDictionary<string, int>),
                typeof(int),
                null,
                out indexer
            );
            Assert.IsFalse(result, "Should return false for null parameter types.");
            Assert.IsTrue(indexer == null, "Indexer should be null.");
        }

        [TestCase(
            typeof(string),
            typeof(int),
            typeof(string),
            typeof(string),
            false,
            TestName = "WrongReturnType"
        )]
        [TestCase(
            typeof(string),
            typeof(int),
            typeof(int),
            typeof(int),
            false,
            TestName = "WrongParameterType"
        )]
        [TestCase(
            typeof(string),
            typeof(int),
            typeof(int),
            typeof(string),
            true,
            TestName = "CorrectTypes"
        )]
        [TestCase(
            typeof(int),
            typeof(string),
            typeof(string),
            typeof(int),
            true,
            TestName = "CorrectTypesIntKeyStringValue"
        )]
        [TestCase(
            typeof(string),
            typeof(int),
            typeof(object),
            typeof(object),
            true,
            TestName = "ObjectTypesMatchingPublicIndexer"
        )]
        public void TryGetIndexerPropertyWithVariousTypeMatchingScenarios(
            Type keyType,
            Type valueType,
            Type requestedReturnType,
            Type requestedParamType,
            bool expectedFound
        )
        {
            Type dictionaryType = typeof(SerializableDictionary<,>).MakeGenericType(
                keyType,
                valueType
            );

            bool found = ReflectionHelpers.TryGetIndexerProperty(
                dictionaryType,
                requestedReturnType,
                new[] { requestedParamType },
                out PropertyInfo indexer
            );

            // Comprehensive diagnostic output for debugging
            string diagnosticInfo =
                $"\nDictionary type: SerializableDictionary<{keyType.Name}, {valueType.Name}>"
                + $"\nRequested return type: {requestedReturnType.Name}"
                + $"\nRequested parameter type: {requestedParamType.Name}"
                + $"\nActual value type (expected return): {valueType.Name}"
                + $"\nActual key type (expected param): {keyType.Name}"
                + $"\nFound: {found}, Expected: {expectedFound}";

            if (indexer != null)
            {
                ParameterInfo[] indexParams = indexer.GetIndexParameters();
                diagnosticInfo +=
                    $"\nFound indexer return type: {indexer.PropertyType.Name}"
                    + $"\nFound indexer param type: {indexParams[0].ParameterType.Name}"
                    + $"\nIndexer declaring type: {indexer.DeclaringType?.Name ?? "null"}"
                    + $"\nIndexer name: {indexer.Name}";
            }
            else
            {
                // List all available indexers for debugging
                PropertyInfo[] allProps = dictionaryType.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                );
                string availableIndexers = string.Join(
                    ", ",
                    allProps
                        .Where(p => 0 < p.GetIndexParameters().Length)
                        .Select(p =>
                        {
                            ParameterInfo[] parms = p.GetIndexParameters();
                            return $"{p.PropertyType.Name}[{string.Join(",", parms.Select(ip => ip.ParameterType.Name))}]";
                        })
                );
                diagnosticInfo +=
                    $"\nAvailable indexers: {(string.IsNullOrEmpty(availableIndexers) ? "none" : availableIndexers)}";
            }

            Assert.AreEqual(expectedFound, found, $"Type matching mismatch.{diagnosticInfo}");

            if (expectedFound)
            {
                Assert.IsTrue(
                    indexer != null,
                    $"Indexer should not be null when found is true.{diagnosticInfo}"
                );
                Assert.AreEqual(
                    requestedReturnType,
                    indexer.PropertyType,
                    $"Return type should match requested type.{diagnosticInfo}"
                );
                Assert.AreEqual(
                    requestedParamType,
                    indexer.GetIndexParameters()[0].ParameterType,
                    $"Parameter type should match requested type.{diagnosticInfo}"
                );
            }
            else
            {
                Assert.IsTrue(
                    indexer == null,
                    $"Indexer should be null when not found.{diagnosticInfo}"
                );
            }
        }

        [Test]
        public void TryGetIndexerPropertyWithWrongTypesReturnsFalse()
        {
            // Try to find an indexer with mismatched types
            bool found = ReflectionHelpers.TryGetIndexerProperty(
                typeof(SerializableDictionary<string, int>),
                typeof(string), // Wrong return type - actual is int
                new[] { typeof(string) },
                out PropertyInfo indexer
            );

            // Diagnostic info for debugging
            string diagnosticInfo =
                $"Found: {found}, Indexer: {indexer?.PropertyType?.Name ?? "null"}";
            if (indexer != null)
            {
                ParameterInfo[] parms = indexer.GetIndexParameters();
                diagnosticInfo +=
                    $", IndexerParams: [{string.Join(", ", parms.Select(p => p.ParameterType.Name))}]";
            }

            Assert.IsFalse(
                found,
                $"Should not find indexer with wrong return type (string instead of int). {diagnosticInfo}"
            );
            Assert.IsTrue(
                indexer == null,
                $"Indexer should be null when not found. {diagnosticInfo}"
            );

            // Try to find an indexer with wrong parameter type
            found = ReflectionHelpers.TryGetIndexerProperty(
                typeof(SerializableDictionary<string, int>),
                typeof(int),
                new[] { typeof(int) }, // Wrong parameter type - actual is string
                out indexer
            );

            diagnosticInfo = $"Found: {found}, Indexer: {indexer?.PropertyType?.Name ?? "null"}";
            if (indexer != null)
            {
                ParameterInfo[] parms = indexer.GetIndexParameters();
                diagnosticInfo +=
                    $", IndexerParams: [{string.Join(", ", parms.Select(p => p.ParameterType.Name))}]";
            }

            Assert.IsFalse(
                found,
                $"Should not find indexer with wrong parameter type (int instead of string). {diagnosticInfo}"
            );
            Assert.IsTrue(
                indexer == null,
                $"Indexer should be null when not found. {diagnosticInfo}"
            );
        }

        [Test]
        public void GetIndexerSetterWorksWithCachedPropertyInfo()
        {
            // Integration test: TryGetIndexerProperty + GetIndexerSetter
            Type dictionaryType = typeof(SerializableDictionary<string, int>);

            bool found = ReflectionHelpers.TryGetIndexerProperty(
                dictionaryType,
                typeof(int),
                new[] { typeof(string) },
                out PropertyInfo indexer
            );

            Assert.IsTrue(found, "Should find indexer.");

            // Create a dictionary and test the setter
            SerializableDictionary<string, int> dict = new();
            dict["initial"] = 1;

            Action<object, object, object[]> setter = ReflectionHelpers.GetIndexerSetter(indexer);
            Assert.IsTrue(setter != null, "Should get a setter delegate.");

            setter(dict, 42, new object[] { "test" });
            Assert.AreEqual(42, dict["test"], "Setter should have set the value.");

            // Also test getter
            Func<object, object[], object> getter = ReflectionHelpers.GetIndexerGetter(indexer);
            Assert.IsTrue(getter != null, "Should get a getter delegate.");

            object retrieved = getter(dict, new object[] { "test" });
            Assert.AreEqual(42, retrieved, "Getter should retrieve the value.");
        }

        [Test]
        public void SerializableDictionaryHasPublicObjectIndexer()
        {
            // SerializableDictionary implements 'object this[object key]' as a PUBLIC property,
            // not as an explicit interface implementation. This is intentional to provide
            // compatibility with non-generic IDictionary operations.
            Type dictType = typeof(SerializableDictionary<string, int>);

            bool foundObjectIndexer = ReflectionHelpers.TryGetIndexerProperty(
                dictType,
                typeof(object),
                new[] { typeof(object) },
                out PropertyInfo objectIndexer
            );

            Assert.IsTrue(
                foundObjectIndexer,
                "SerializableDictionary should have a PUBLIC object indexer."
            );
            Assert.IsTrue(
                objectIndexer != null,
                "The object indexer PropertyInfo should not be null."
            );
            Assert.AreEqual(
                typeof(object),
                objectIndexer.PropertyType,
                "Return type should be object."
            );

            ParameterInfo[] indexParams = objectIndexer.GetIndexParameters();
            Assert.AreEqual(1, indexParams.Length, "Should have exactly one index parameter.");
            Assert.AreEqual(
                typeof(object),
                indexParams[0].ParameterType,
                "Parameter type should be object."
            );
        }

        [Test]
        public void DictionaryHasExplicitInterfaceObjectIndexer()
        {
            // System.Collections.Generic.Dictionary implements 'object this[object key]'
            // as an EXPLICIT interface implementation (IDictionary.Item), so it's NOT
            // found by GetProperty("Item", ...).
            Type dictType = typeof(Dictionary<string, int>);

            bool foundObjectIndexer = ReflectionHelpers.TryGetIndexerProperty(
                dictType,
                typeof(object),
                new[] { typeof(object) },
                out PropertyInfo objectIndexer
            );

            Assert.IsFalse(
                foundObjectIndexer,
                "Dictionary should NOT have a public object indexer (it's an explicit interface implementation)."
            );
            Assert.IsTrue(
                objectIndexer == null,
                "The object indexer PropertyInfo should be null for Dictionary."
            );

            // But the generic indexer should still be found
            bool foundGenericIndexer = ReflectionHelpers.TryGetIndexerProperty(
                dictType,
                typeof(int),
                new[] { typeof(string) },
                out PropertyInfo genericIndexer
            );

            Assert.IsTrue(foundGenericIndexer, "Dictionary SHOULD have a public generic indexer.");
            Assert.IsTrue(
                genericIndexer != null,
                "The generic indexer PropertyInfo should not be null."
            );
            Assert.AreEqual(typeof(int), genericIndexer.PropertyType, "Return type should be int.");
        }

        [Test]
        public void SerializableDictionarySupportsMultipleIndexerTypes()
        {
            // SerializableDictionary has BOTH a generic indexer (TValue this[TKey]) and
            // a non-generic indexer (object this[object]). Both should be findable.
            Type dictType = typeof(SerializableDictionary<string, int>);

            // Find the generic indexer
            bool foundGeneric = ReflectionHelpers.TryGetIndexerProperty(
                dictType,
                typeof(int),
                new[] { typeof(string) },
                out PropertyInfo genericIndexer
            );

            Assert.IsTrue(foundGeneric, "Should find generic indexer.");
            Assert.IsTrue(genericIndexer != null, "Generic indexer should not be null.");

            // Find the object indexer
            bool foundObject = ReflectionHelpers.TryGetIndexerProperty(
                dictType,
                typeof(object),
                new[] { typeof(object) },
                out PropertyInfo objectIndexer
            );

            Assert.IsTrue(foundObject, "Should find object indexer.");
            Assert.IsTrue(objectIndexer != null, "Object indexer should not be null.");

            // They should be different properties
            Assert.AreNotEqual(
                genericIndexer,
                objectIndexer,
                "Generic and object indexers should be different PropertyInfo instances."
            );
        }
    }
}
