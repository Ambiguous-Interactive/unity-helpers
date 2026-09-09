// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
#if UNITY_EDITOR
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using PropertyAttribute = UnityEngine.PropertyAttribute;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class IntDropDownDrawerMultiObjectTests : CommonTestBase
    {
        private static void AssignAttribute(PropertyDrawer drawer, PropertyAttribute attribute)
        {
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);
        }

        [Test]
        public void MultiObjectSameValueDoesNotShowMixedIndicator()
        {
            MultiObjectIntDropDownTarget first =
                CreateScriptableObject<MultiObjectIntDropDownTarget>();
            MultiObjectIntDropDownTarget second =
                CreateScriptableObject<MultiObjectIntDropDownTarget>();
            first.selection = 20;
            second.selection = 20;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectIntDropDownTarget.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate selection property.");

            Assert.IsFalse(
                property.hasMultipleDifferentValues,
                "Multiple objects with the same value should NOT have hasMultipleDifferentValues set."
            );
        }

        [Test]
        public void MultiObjectDifferentValueShowsMixedIndicator()
        {
            MultiObjectIntDropDownTarget first =
                CreateScriptableObject<MultiObjectIntDropDownTarget>();
            MultiObjectIntDropDownTarget second =
                CreateScriptableObject<MultiObjectIntDropDownTarget>();
            first.selection = 10;
            second.selection = 30;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectIntDropDownTarget.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate selection property.");

            Assert.IsTrue(
                property.hasMultipleDifferentValues,
                "Multiple objects with different values SHOULD have hasMultipleDifferentValues set."
            );
        }

        [UnityTest]
        public IEnumerator MultiObjectSameInvalidValueDoesNotShowMixedIndicator()
        {
            MultiObjectIntDropDownTarget first =
                CreateScriptableObject<MultiObjectIntDropDownTarget>();
            MultiObjectIntDropDownTarget second =
                CreateScriptableObject<MultiObjectIntDropDownTarget>();
            first.selection = 999;
            second.selection = 999;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectIntDropDownTarget.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate selection property.");

            IntDropDownDrawer drawer = new();
            AssignAttribute(drawer, new IntDropDownAttribute(10, 20, 30, 40, 50));

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsTrue(element != null, "CreatePropertyGUI should return a VisualElement.");

            Rect position = new(0f, 0f, 240f, EditorGUIUtility.singleLineHeight);
            GUIContent label = new("Selection");

            yield return TestIMGUIExecutor.Run(() =>
            {
                drawer.OnGUI(position, property, label);
            });

            Assert.IsFalse(
                property.hasMultipleDifferentValues,
                "Both objects have the same (invalid) value, so hasMultipleDifferentValues should be false."
            );
        }

        [UnityTest]
        public IEnumerator MultiObjectSelectionChangesAllObjects()
        {
            MultiObjectIntDropDownTarget first =
                CreateScriptableObject<MultiObjectIntDropDownTarget>();
            MultiObjectIntDropDownTarget second =
                CreateScriptableObject<MultiObjectIntDropDownTarget>();
            first.selection = 10;
            second.selection = 30;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectIntDropDownTarget.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate selection property.");
            Assert.IsTrue(
                property.hasMultipleDifferentValues,
                "Precondition: objects should start with different values."
            );

            property.intValue = 40;
            serializedObject.ApplyModifiedProperties();

            Assert.That(
                first.selection,
                Is.EqualTo(40),
                "First object should have been updated to the new value."
            );
            Assert.That(
                second.selection,
                Is.EqualTo(40),
                "Second object should have been updated to the new value."
            );

            serializedObject.Update();
            Assert.IsFalse(
                property.hasMultipleDifferentValues,
                "After applying the same value to all targets, hasMultipleDifferentValues should be false."
            );

            yield return null;
        }
    }
#endif
}
