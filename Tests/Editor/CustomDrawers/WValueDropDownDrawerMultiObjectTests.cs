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
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;
    using PropertyAttribute = UnityEngine.PropertyAttribute;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WValueDropDownDrawerMultiObjectTests : CommonTestBase
    {
        private static void AssignAttribute(PropertyDrawer drawer, PropertyAttribute attribute)
        {
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);
        }

        [Test]
        public void MultiObjectSameValueDoesNotShowMixedIndicator()
        {
            MultiObjectWValueDropDownTarget first =
                CreateScriptableObject<MultiObjectWValueDropDownTarget>();
            MultiObjectWValueDropDownTarget second =
                CreateScriptableObject<MultiObjectWValueDropDownTarget>();
            first.selection = 20;
            second.selection = 20;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectWValueDropDownTarget.selection)
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
            MultiObjectWValueDropDownTarget first =
                CreateScriptableObject<MultiObjectWValueDropDownTarget>();
            MultiObjectWValueDropDownTarget second =
                CreateScriptableObject<MultiObjectWValueDropDownTarget>();
            first.selection = 10;
            second.selection = 30;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectWValueDropDownTarget.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate selection property.");

            Assert.IsTrue(
                property.hasMultipleDifferentValues,
                "Multiple objects with different values SHOULD have hasMultipleDifferentValues set."
            );
        }

        [UnityTest]
        public IEnumerator MultiObjectSelectionChangePropagatesToAllObjects()
        {
            MultiObjectWValueDropDownTarget first =
                CreateScriptableObject<MultiObjectWValueDropDownTarget>();
            MultiObjectWValueDropDownTarget second =
                CreateScriptableObject<MultiObjectWValueDropDownTarget>();
            first.selection = 10;
            second.selection = 30;

            using SerializedObject serializedObject = new(new Object[] { first, second });
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(MultiObjectWValueDropDownTarget.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate selection property.");
            Assert.IsTrue(
                property.hasMultipleDifferentValues,
                "Precondition: objects should start with different values."
            );

            WValueDropDownDrawer.ApplyOption(property, 40);
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
