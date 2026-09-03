// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Tests for danger warning functionality in SerializableSetPropertyDrawer.
    /// Validates detection and UI warnings for potentially problematic values like
    /// null Unity objects, empty strings, and whitespace-only strings.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableSetDangerWarningTests : CommonTestBase
    {
        [Test]
        public void ValueIsValidReturnsFalseForNullString()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(string), null);
            Assert.IsFalse(result, "Null string should not be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsFalseForEmptyString()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(string), string.Empty);
            Assert.IsFalse(result, "Empty string should not be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForNonEmptyString()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(string), "hello");
            Assert.IsTrue(result, "Non-empty string should be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForWhitespaceOnlyString()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(string), "   ");
            Assert.IsTrue(
                result,
                "Whitespace-only string should be considered valid (not null or empty). "
                    + "Danger warning is handled separately by IsBlankStringValue."
            );
        }

        [Test]
        public void ValueIsValidReturnsTrueForSingleCharacterString()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(string), "x");
            Assert.IsTrue(result, "Single character string should be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForStringWithLeadingWhitespace()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(string), "  hello");
            Assert.IsTrue(result, "String with leading whitespace should be valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForStringWithTrailingWhitespace()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(string), "hello  ");
            Assert.IsTrue(result, "String with trailing whitespace should be valid.");
        }

        [Test]
        public void WhitespaceOnlyStringIsValidButIsBlankReturnsTrue()
        {
            string whitespaceOnlyString = "   ";

            bool isValid = SerializableSetPropertyDrawer.ValueIsValid(
                typeof(string),
                whitespaceOnlyString
            );
            bool isBlank = SerializableSetPropertyDrawer.IsBlankStringValue(
                typeof(string),
                whitespaceOnlyString
            );

            Assert.IsTrue(isValid, "Whitespace-only string should be valid (not null/empty).");
            Assert.IsTrue(isBlank, "Whitespace-only string should be blank (for danger warning).");
        }

        [TestCase(null, false, true, TestName = "NullString")]
        [TestCase("", false, true, TestName = "EmptyString")]
        [TestCase("   ", true, true, TestName = "WhitespaceOnly")]
        [TestCase("\t\n", true, true, TestName = "TabAndNewline")]
        [TestCase("hello", true, false, TestName = "NonEmptyString")]
        [TestCase("  hello  ", true, false, TestName = "StringWithWhitespacePadding")]
        [TestCase("x", true, false, TestName = "SingleCharacter")]
        public void ValueIsValidAndIsBlankStringValueHaveExpectedRelationship(
            string testValue,
            bool expectedIsValid,
            bool expectedIsBlank
        )
        {
            bool actualIsValid = SerializableSetPropertyDrawer.ValueIsValid(
                typeof(string),
                testValue
            );
            bool actualIsBlank = SerializableSetPropertyDrawer.IsBlankStringValue(
                typeof(string),
                testValue
            );

            Assert.AreEqual(
                expectedIsValid,
                actualIsValid,
                $"ValueIsValid for '{testValue ?? "(null)"}' should be {expectedIsValid}."
            );
            Assert.AreEqual(
                expectedIsBlank,
                actualIsBlank,
                $"IsBlankStringValue for '{testValue ?? "(null)"}' should be {expectedIsBlank}."
            );
        }

        [Test]
        public void ValueIsValidReturnsFalseForNullGameObject()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(GameObject), null);
            Assert.IsFalse(result, "Null GameObject should not be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForValidGameObject()
        {
            GameObject go = Track(new GameObject("TestObject"));
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(GameObject), go);
            Assert.IsTrue(result, "Valid GameObject should be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsFalseForDestroyedGameObject()
        {
            GameObject go = Track(new GameObject("ToBeDestroyed"));
            Object.DestroyImmediate(go); // UNH-SUPPRESS: Testing destroyed object validation
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(GameObject), go);
            Assert.IsFalse(result, "Destroyed GameObject should not be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsFalseForNullScriptableObject()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(TestData), null);
            Assert.IsFalse(result, "Null ScriptableObject should not be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForValidScriptableObject()
        {
            TestData data = CreateScriptableObject<TestData>();
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(TestData), data);
            Assert.IsTrue(result, "Valid ScriptableObject should be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsFalseForDestroyedScriptableObject()
        {
            TestData data = CreateScriptableObject<TestData>();
            Object.DestroyImmediate(data); // UNH-SUPPRESS: Testing destroyed object validation
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(TestData), data);
            Assert.IsFalse(result, "Destroyed ScriptableObject should not be considered valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForIntZero()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(int), 0);
            Assert.IsTrue(result, "Zero int should be valid (value types are always valid).");
        }

        [Test]
        public void ValueIsValidReturnsTrueForIntPositive()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(int), 42);
            Assert.IsTrue(result, "Positive int should be valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForIntNegative()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(int), -42);
            Assert.IsTrue(result, "Negative int should be valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForFloatZero()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(float), 0f);
            Assert.IsTrue(result, "Zero float should be valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForBoolFalse()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(bool), false);
            Assert.IsTrue(result, "False bool should be valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForBoolTrue()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(bool), true);
            Assert.IsTrue(result, "True bool should be valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForDefaultStruct()
        {
            Vector3 defaultVector = default;
            bool result = SerializableSetPropertyDrawer.ValueIsValid(
                typeof(Vector3),
                defaultVector
            );
            Assert.IsTrue(result, "Default struct value should be valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForNonDefaultStruct()
        {
            Vector3 vector = new(1f, 2f, 3f);
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(Vector3), vector);
            Assert.IsTrue(result, "Non-default struct value should be valid.");
        }

        [Test]
        public void ValueIsValidReturnsFalseForNullReferenceType()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(object), null);
            Assert.IsFalse(result, "Null reference type should not be valid.");
        }

        [Test]
        public void ValueIsValidReturnsTrueForNonNullReferenceType()
        {
            object obj = new();
            bool result = SerializableSetPropertyDrawer.ValueIsValid(typeof(object), obj);
            Assert.IsTrue(result, "Non-null reference type should be valid.");
        }

        [Test]
        public void IsBlankStringValueReturnsTrueForNullString()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(typeof(string), null);
            Assert.IsTrue(result, "Null string should be considered blank.");
        }

        [Test]
        public void IsBlankStringValueReturnsTrueForEmptyString()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(
                typeof(string),
                string.Empty
            );
            Assert.IsTrue(result, "Empty string should be considered blank.");
        }

        [Test]
        public void IsBlankStringValueReturnsTrueForWhitespaceOnlyString()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(typeof(string), "   ");
            Assert.IsTrue(result, "Whitespace-only string should be considered blank.");
        }

        [Test]
        public void IsBlankStringValueReturnsTrueForTabsAndSpaces()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(
                typeof(string),
                "\t  \t"
            );
            Assert.IsTrue(result, "String with only tabs and spaces should be considered blank.");
        }

        [Test]
        public void IsBlankStringValueReturnsTrueForNewlineOnly()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(typeof(string), "\n");
            Assert.IsTrue(result, "String with only newline should be considered blank.");
        }

        [Test]
        public void IsBlankStringValueReturnsFalseForNonBlankString()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(typeof(string), "hello");
            Assert.IsFalse(result, "Non-blank string should not be considered blank.");
        }

        [Test]
        public void IsBlankStringValueReturnsFalseForStringWithContent()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(
                typeof(string),
                "  hello  "
            );
            Assert.IsFalse(
                result,
                "String with content (even with whitespace) should not be blank."
            );
        }

        [Test]
        public void IsBlankStringValueReturnsFalseForNonStringType()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(typeof(int), 0);
            Assert.IsFalse(result, "Non-string type should never be considered blank string.");
        }

        [Test]
        public void IsBlankStringValueReturnsFalseForGameObjectType()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(
                typeof(GameObject),
                null
            );
            Assert.IsFalse(result, "GameObject type should never be considered blank string.");
        }

        [Test]
        public void IsNullUnityObjectValueReturnsTrueForNullGameObject()
        {
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(
                typeof(GameObject),
                null
            );
            Assert.IsTrue(result, "Null GameObject should be detected as null Unity object.");
        }

        [Test]
        public void IsNullUnityObjectValueReturnsTrueForDestroyedGameObject()
        {
            GameObject go = Track(new GameObject("ToDestroy"));
            Object.DestroyImmediate(go); // UNH-SUPPRESS: Testing destroyed object null detection
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(
                typeof(GameObject),
                go
            );
            Assert.IsTrue(result, "Destroyed GameObject should be detected as null Unity object.");
        }

        [Test]
        public void IsNullUnityObjectValueReturnsFalseForValidGameObject()
        {
            GameObject go = Track(new GameObject("Valid"));
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(
                typeof(GameObject),
                go
            );
            Assert.IsFalse(result, "Valid GameObject should not be null.");
        }

        [Test]
        public void IsNullUnityObjectValueReturnsTrueForNullScriptableObject()
        {
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(
                typeof(TestData),
                null
            );
            Assert.IsTrue(result, "Null ScriptableObject should be detected as null Unity object.");
        }

        [Test]
        public void IsNullUnityObjectValueReturnsTrueForDestroyedScriptableObject()
        {
            TestData data = CreateScriptableObject<TestData>();
            Object.DestroyImmediate(data); // UNH-SUPPRESS: Testing destroyed object null detection
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(
                typeof(TestData),
                data
            );
            Assert.IsTrue(
                result,
                "Destroyed ScriptableObject should be detected as null Unity object."
            );
        }

        [Test]
        public void IsNullUnityObjectValueReturnsFalseForValidScriptableObject()
        {
            TestData data = CreateScriptableObject<TestData>();
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(
                typeof(TestData),
                data
            );
            Assert.IsFalse(result, "Valid ScriptableObject should not be null.");
        }

        [Test]
        public void IsNullUnityObjectValueReturnsFalseForNonUnityObjectType()
        {
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(
                typeof(string),
                null
            );
            Assert.IsFalse(result, "String type should not be detected as Unity object.");
        }

        [Test]
        public void IsNullUnityObjectValueReturnsFalseForIntType()
        {
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(typeof(int), 0);
            Assert.IsFalse(result, "Int type should not be detected as Unity object.");
        }

        [Test]
        public void IsNullUnityObjectValueReturnsFalseForObjectType()
        {
            // System.Object is not a UnityEngine.Object
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(
                typeof(object),
                null
            );
            Assert.IsFalse(result, "System.Object type should not be detected as Unity object.");
        }

        [Test]
        public void PendingEntryCommitsEmptyStringWhenAllowed()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = string.Empty;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(
                committed,
                "Empty string should be committable (danger value but allowed)."
            );
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual(string.Empty, itemsProperty.GetArrayElementAtIndex(0).stringValue);
        }

        [Test]
        public void PendingEntryCommitsWhitespaceOnlyStringWhenAllowed()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "   ";

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(
                committed,
                "Whitespace-only string should be committable (danger value but allowed)."
            );
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual("   ", itemsProperty.GetArrayElementAtIndex(0).stringValue);
        }

        [Test]
        public void PendingEntryCommitsNormalStringSuccessfully()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = "ValidString";

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Normal string should commit successfully.");
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual("ValidString", itemsProperty.GetArrayElementAtIndex(0).stringValue);
        }

        [Test]
        public void PendingEntryCommitsValidGameObject()
        {
            GameObjectSetHost host = CreateScriptableObject<GameObjectSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(GameObjectSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(GameObject),
                isSortedSet: false
            );
            GameObject go = Track(new GameObject("TestGO"));
            pending.value = go;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Valid GameObject should commit successfully.");
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
        }

        [Test]
        public void PendingEntryCommitsNullGameObjectWhenAllowed()
        {
            GameObjectSetHost host = CreateScriptableObject<GameObjectSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(GameObjectSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(GameObject),
                isSortedSet: false
            );
            pending.value = null;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(
                committed,
                "Null GameObject should be committable (danger value but allowed for Unity objects)."
            );
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
        }

        [Test]
        public void PendingEntryCommitsValidScriptableObject()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(TestData),
                isSortedSet: false
            );
            TestData data = CreateScriptableObject<TestData>();
            pending.value = data;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Valid ScriptableObject should commit successfully.");
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
        }

        [Test]
        public void PendingEntryCommitsNullScriptableObjectWhenAllowed()
        {
            ObjectSetHost host = CreateScriptableObject<ObjectSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(ObjectSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(TestData),
                isSortedSet: false
            );
            pending.value = null;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(
                committed,
                "Null ScriptableObject should be committable (danger value but allowed for Unity objects)."
            );
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
        }

        [Test]
        public void PendingEntryCommitsZeroIntSuccessfully()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                isSortedSet: false
            );
            pending.value = 0;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(
                committed,
                "Zero int should commit successfully (value types always valid)."
            );
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual(0, itemsProperty.GetArrayElementAtIndex(0).intValue);
        }

        [Test]
        public void PendingEntryCommitsNegativeIntSuccessfully()
        {
            HashSetHost host = CreateScriptableObject<HashSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(nameof(HashSetHost.set));
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(int),
                isSortedSet: false
            );
            pending.value = -999;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsTrue(committed, "Negative int should commit successfully.");
            serializedObject.Update();
            itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );
            Assert.AreEqual(1, itemsProperty.arraySize);
            Assert.AreEqual(-999, itemsProperty.GetArrayElementAtIndex(0).intValue);
        }

        [Test]
        public void ValueIsValidHandlesNullType()
        {
            bool result = SerializableSetPropertyDrawer.ValueIsValid(null, "test");
            Assert.IsFalse(result, "Null type should result in invalid.");
        }

        [Test]
        public void IsBlankStringValueHandlesNullType()
        {
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(null, "test");
            Assert.IsFalse(result, "Null type should not be detected as blank string.");
        }

        [Test]
        public void IsNullUnityObjectValueHandlesNullType()
        {
            bool result = SerializableSetPropertyDrawer.IsNullUnityObjectValue(null, null);
            Assert.IsFalse(result, "Null type should not be detected as null Unity object.");
        }

        [Test]
        public void DangerValidationConsistencyForStringType()
        {
            // Empty string should be: not valid, is blank string, not null Unity object
            Assert.IsFalse(
                SerializableSetPropertyDrawer.ValueIsValid(typeof(string), string.Empty)
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsBlankStringValue(typeof(string), string.Empty)
            );
            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsNullUnityObjectValue(typeof(string), string.Empty)
            );
        }

        [Test]
        public void DangerValidationConsistencyForGameObjectType()
        {
            // Null GameObject should be: not valid, not blank string, is null Unity object
            Assert.IsFalse(SerializableSetPropertyDrawer.ValueIsValid(typeof(GameObject), null));
            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsBlankStringValue(typeof(GameObject), null)
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsNullUnityObjectValue(typeof(GameObject), null)
            );
        }

        [Test]
        public void DangerValidationConsistencyForIntType()
        {
            // Zero int should be: valid, not blank string, not null Unity object
            Assert.IsTrue(SerializableSetPropertyDrawer.ValueIsValid(typeof(int), 0));
            Assert.IsFalse(SerializableSetPropertyDrawer.IsBlankStringValue(typeof(int), 0));
            Assert.IsFalse(SerializableSetPropertyDrawer.IsNullUnityObjectValue(typeof(int), 0));
        }

        [Test]
        public void WhitespaceVariantsAreAllConsideredBlank()
        {
            string[] whitespaceVariants = { " ", "  ", "\t", "\n", "\r", "\r\n", " \t\n\r " };
            foreach (string variant in whitespaceVariants)
            {
                Assert.IsTrue(
                    SerializableSetPropertyDrawer.IsBlankStringValue(typeof(string), variant),
                    $"Whitespace variant '{variant.Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r")}' should be blank."
                );
            }
        }

        [Test]
        public void StringWithNonBreakingSpaceIsConsideredBlank()
        {
            string nonBreakingSpace = "\u00A0"; // Non-breaking space
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(
                typeof(string),
                nonBreakingSpace
            );
            Assert.IsTrue(result, "Non-breaking space should be considered blank.");
        }

        [Test]
        public void StringWithZeroWidthSpaceIsNotConsideredBlank()
        {
            // Zero-width space is not whitespace per string.IsNullOrWhiteSpace
            string zeroWidthSpace = "\u200B";
            bool result = SerializableSetPropertyDrawer.IsBlankStringValue(
                typeof(string),
                zeroWidthSpace
            );
            Assert.IsFalse(
                result,
                "Zero-width space character is not considered whitespace by .NET."
            );
        }

        [Test]
        public void DuplicateEmptyStringIsRejected()
        {
            StringSetHost host = CreateScriptableObject<StringSetHost>();
            host.set.Add(string.Empty);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(StringSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(string),
                isSortedSet: false
            );
            pending.value = string.Empty;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsFalse(committed, "Duplicate empty string should be rejected.");
        }

        [Test]
        public void DuplicateNullGameObjectIsRejected()
        {
            GameObjectSetHost host = CreateScriptableObject<GameObjectSetHost>();
            host.set.Add(null);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(GameObjectSetHost.set)
            );
            SerializedProperty itemsProperty = setProperty.FindPropertyRelative(
                SerializableHashSetSerializedPropertyNames.Items
            );

            SerializableSetPropertyDrawer drawer = new();
            SerializableSetPropertyDrawer.PaginationState pagination =
                drawer.GetOrCreatePaginationState(setProperty);
            SerializableSetPropertyDrawer.PendingEntry pending = drawer.GetOrCreatePendingEntry(
                setProperty,
                setProperty.propertyPath,
                typeof(GameObject),
                isSortedSet: false
            );
            pending.value = null;

            ISerializableSetInspector inspector = host.set;
            bool committed = drawer.TryCommitPendingEntry(
                pending,
                setProperty,
                setProperty.propertyPath,
                ref itemsProperty,
                pagination,
                inspector
            );

            Assert.IsFalse(committed, "Duplicate null GameObject should be rejected.");
        }
    }
}
