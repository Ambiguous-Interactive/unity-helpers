// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
#if UNITY_EDITOR
    using System;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    /// <summary>
    /// Tests for WValueDropDown support with SerializableType and arbitrary generic types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WValueDropDownSerializableTypeTests : CommonTestBase
    {
        [Test]
        public void SerializableTypePropertyIsRecognizedAsSupported()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedType)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedType property.");
            Assert.That(property.propertyType, Is.EqualTo(SerializedPropertyType.Generic));

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownSerializableTypeSource),
                nameof(WValueDropDownSerializableTypeSource.GetTypeOptions)
            );
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsTrue(element != null, "CreatePropertyGUI should return a non-null element.");
            Assert.IsFalse(
                element is HelpBox,
                "SerializableType should be a supported property type."
            );
        }

        [Test]
        public void ApplyOptionUpdatesSerializableTypeFromTypeOption()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            asset.selectedType = new SerializableType(typeof(int));
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedType)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedType property.");

            WValueDropDownDrawer.ApplyOption(property, typeof(string));
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selectedType.Value, Is.EqualTo(typeof(string)));
        }

        [Test]
        public void ApplyOptionUpdatesSerializableTypeFromSerializableTypeOption()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            asset.selectedSerializableType = new SerializableType(typeof(int));
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedSerializableType)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedSerializableType property.");

            SerializableType newValue = new(typeof(double));
            WValueDropDownDrawer.ApplyOption(property, newValue);
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selectedSerializableType.Value, Is.EqualTo(typeof(double)));
        }

        [Test]
        public void ApplyOptionUpdatesSerializableTypeFromStringOption()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            asset.selectedFromStrings = new SerializableType(typeof(int));
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedFromStrings)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedFromStrings property.");

            string newAssemblyQualifiedName = SerializableType.NormalizeTypeName(typeof(Vector3));
            WValueDropDownDrawer.ApplyOption(property, newAssemblyQualifiedName);
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selectedFromStrings.Value, Is.EqualTo(typeof(Vector3)));
        }

        [Test]
        public void SerializableTypeOptionsFromTypeProviderShowCorrectDisplayNames()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedType)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedType property.");

            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownSerializableTypeSource),
                nameof(WValueDropDownSerializableTypeSource.GetTypeOptions)
            );

            object[] options = attribute.GetOptions(asset);
            Assert.That(
                options.Length,
                Is.GreaterThan(0),
                "Options should contain at least one type."
            );
            Assert.IsTrue(options[0] is Type, "First option should be a Type.");
        }

        [Test]
        public void SerializableTypeOptionsFromSerializableTypeProviderWork()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedSerializableType)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedSerializableType property.");

            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownSerializableTypeSource),
                nameof(WValueDropDownSerializableTypeSource.GetSerializableTypeOptions)
            );

            object[] options = attribute.GetOptions(asset);
            Assert.That(options.Length, Is.EqualTo(4), "Should have 4 SerializableType options.");

            foreach (object option in options)
            {
                Assert.IsTrue(
                    option is SerializableType,
                    $"Option should be SerializableType but was {option?.GetType().Name ?? "null"}."
                );
            }
        }

        [Test]
        public void InstanceMethodProviderReturnsSerializableTypeOptions()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            asset.dynamicTypes.Add(typeof(GameObject));
            asset.dynamicTypes.Add(typeof(Transform));
            asset.dynamicTypes.Add(typeof(Rigidbody));
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.instanceSelectedType)
            );
            Assert.IsTrue(property != null, "Failed to locate instanceSelectedType property.");

            WValueDropDownAttribute attribute =
                PropertyDrawerTestHelper.GetAttributeFromProperty<WValueDropDownAttribute>(
                    property
                );
            Assert.IsTrue(attribute != null, "Failed to retrieve attribute.");

            object[] options = attribute.GetOptions(asset);
            Assert.That(options.Length, Is.EqualTo(3), "Should have 3 instance-provided options.");
        }

        [Test]
        public void CreatePropertyGUIReturnsDropdownSelectorForSerializableType()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedSerializableType)
            );
            Assert.IsTrue(property != null, "Failed to locate property.");

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownSerializableTypeSource),
                nameof(WValueDropDownSerializableTypeSource.GetSerializableTypeOptions)
            );
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsTrue(element != null, "CreatePropertyGUI should return element.");
            Assert.IsInstanceOf<BaseField<string>>(
                element,
                "Should be a string BaseField selector."
            );
        }

        [Test]
        public void ApplyOptionWithNullTypeHandledGracefully()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            asset.selectedType = new SerializableType(typeof(int));
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedType)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedType property.");

            WValueDropDownDrawer.ApplyOption(property, (Type)null);
            serializedObject.ApplyModifiedProperties();

            Assert.IsTrue(
                asset.selectedType.IsEmpty,
                "SerializableType should be empty after applying null."
            );
        }

        [Test]
        public void SerializableTypeMatchesCorrectlyWithTypeOption()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            asset.selectedType = new SerializableType(typeof(float));
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedType)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedType property.");

            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownSerializableTypeSource),
                nameof(WValueDropDownSerializableTypeSource.GetTypeOptions)
            );

            object[] options = attribute.GetOptions(asset);

            WValueDropDownDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsTrue(element != null, "CreatePropertyGUI should return element.");
        }

        [Test]
        public void MultipleSerializableTypeFieldsAreIndependent()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            asset.selectedType = new SerializableType(typeof(int));
            asset.selectedSerializableType = new SerializableType(typeof(string));
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty typeProperty = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedType)
            );
            SerializedProperty serializableTypeProperty = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedSerializableType)
            );

            Assert.IsTrue(typeProperty != null, "Failed to locate selectedType property.");
            Assert.IsTrue(
                serializableTypeProperty != null,
                "Failed to locate selectedSerializableType property."
            );

            WValueDropDownDrawer.ApplyOption(typeProperty, typeof(double));
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selectedType.Value, Is.EqualTo(typeof(double)));
            Assert.That(asset.selectedSerializableType.Value, Is.EqualTo(typeof(string)));
        }

        [Test]
        public void EmptySerializableTypeHandledCorrectly()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            asset.selectedType = default;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedType)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedType property.");

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownSerializableTypeSource),
                nameof(WValueDropDownSerializableTypeSource.GetTypeOptions)
            );
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsTrue(element != null, "Should handle empty SerializableType gracefully.");
        }
    }

    /// <summary>
    /// Tests for WValueDropDown support with custom equatable struct types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WValueDropDownCustomStructTests : CommonTestBase
    {
        [Test]
        public void CustomStructPropertyIsRecognizedAsSupported()
        {
            WValueDropDownCustomStructAsset asset =
                CreateScriptableObject<WValueDropDownCustomStructAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCustomStructAsset.selectedStruct)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedStruct property.");
            Assert.That(property.propertyType, Is.EqualTo(SerializedPropertyType.Generic));

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownCustomStructSource),
                nameof(WValueDropDownCustomStructSource.GetStructOptions)
            );
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsTrue(element != null, "CreatePropertyGUI should return a non-null element.");
            Assert.IsFalse(
                element is HelpBox,
                "Custom struct should be a supported property type."
            );
        }

        [Test]
        public void ApplyOptionUpdatesCustomStruct()
        {
            WValueDropDownCustomStructAsset asset =
                CreateScriptableObject<WValueDropDownCustomStructAsset>();
            asset.selectedStruct = new TestEquatableStruct(1, "First");
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCustomStructAsset.selectedStruct)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedStruct property.");

            TestEquatableStruct newValue = new(3, "Third");
            WValueDropDownDrawer.ApplyOption(property, newValue);
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selectedStruct.Id, Is.EqualTo(3));
            Assert.That(asset.selectedStruct.Name, Is.EqualTo("Third"));
        }

        [Test]
        public void CustomStructOptionsDisplayCorrectly()
        {
            WValueDropDownCustomStructAsset asset =
                CreateScriptableObject<WValueDropDownCustomStructAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownCustomStructSource),
                nameof(WValueDropDownCustomStructSource.GetStructOptions)
            );

            object[] options = attribute.GetOptions(asset);
            Assert.That(options.Length, Is.EqualTo(4), "Should have 4 struct options.");

            foreach (object option in options)
            {
                Assert.IsTrue(
                    option is TestEquatableStruct,
                    "Each option should be TestEquatableStruct."
                );
            }
        }

        [Test]
        public void CustomStructInstanceMethodProviderWorks()
        {
            WValueDropDownCustomStructAsset asset =
                CreateScriptableObject<WValueDropDownCustomStructAsset>();
            asset.dynamicStructs.Add(new TestEquatableStruct(100, "Dynamic1"));
            asset.dynamicStructs.Add(new TestEquatableStruct(200, "Dynamic2"));
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCustomStructAsset.instanceSelectedStruct)
            );
            Assert.IsTrue(property != null, "Failed to locate instanceSelectedStruct property.");

            WValueDropDownAttribute attribute =
                PropertyDrawerTestHelper.GetAttributeFromProperty<WValueDropDownAttribute>(
                    property
                );
            Assert.IsTrue(attribute != null, "Failed to retrieve attribute.");

            object[] options = attribute.GetOptions(asset);
            Assert.That(
                options.Length,
                Is.EqualTo(2),
                "Should have 2 instance-provided struct options."
            );
        }

        [Test]
        public void CreatePropertyGUIReturnsDropdownForCustomStruct()
        {
            WValueDropDownCustomStructAsset asset =
                CreateScriptableObject<WValueDropDownCustomStructAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCustomStructAsset.selectedStruct)
            );
            Assert.IsTrue(property != null, "Failed to locate property.");

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownCustomStructSource),
                nameof(WValueDropDownCustomStructSource.GetStructOptions)
            );
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsTrue(element != null, "CreatePropertyGUI should return element.");
            Assert.IsInstanceOf<BaseField<string>>(
                element,
                "Should be a string BaseField selector."
            );
        }

        [Test]
        public void CustomStructEqualityComparesCorrectly()
        {
            TestEquatableStruct first = new(1, "Test");
            TestEquatableStruct second = new(1, "Test");
            TestEquatableStruct different = new(2, "Test");

            Assert.That(first.Equals(second), Is.True, "Identical structs should be equal.");
            Assert.That(
                first.Equals(different),
                Is.False,
                "Different structs should not be equal."
            );
            Assert.That(first == second, Is.True, "Operator == should work correctly.");
            Assert.That(first != different, Is.True, "Operator != should work correctly.");
        }

        [Test]
        public void DefaultCustomStructHandledCorrectly()
        {
            WValueDropDownCustomStructAsset asset =
                CreateScriptableObject<WValueDropDownCustomStructAsset>();
            asset.selectedStruct = default;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCustomStructAsset.selectedStruct)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedStruct property.");

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownCustomStructSource),
                nameof(WValueDropDownCustomStructSource.GetStructOptions)
            );
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsTrue(element != null, "Should handle default struct gracefully.");
        }

        [Test]
        public void ApplyOptionWithNullDoesNotThrow()
        {
            WValueDropDownCustomStructAsset asset =
                CreateScriptableObject<WValueDropDownCustomStructAsset>();
            asset.selectedStruct = new TestEquatableStruct(1, "First");
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCustomStructAsset.selectedStruct)
            );
            Assert.IsTrue(property != null, "Failed to locate selectedStruct property.");

            Assert.DoesNotThrow(() =>
            {
                WValueDropDownDrawer.ApplyOption(property, null);
                serializedObject.ApplyModifiedProperties();
            });
        }

        [Test]
        public void MultipleCustomStructFieldsAreIndependent()
        {
            WValueDropDownCustomStructAsset asset =
                CreateScriptableObject<WValueDropDownCustomStructAsset>();
            asset.selectedStruct = new TestEquatableStruct(1, "First");
            asset.instanceSelectedStruct = new TestEquatableStruct(2, "Second");
            asset.dynamicStructs.Add(new TestEquatableStruct(10, "Dynamic"));
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty selectedProperty = serializedObject.FindProperty(
                nameof(WValueDropDownCustomStructAsset.selectedStruct)
            );
            SerializedProperty instanceProperty = serializedObject.FindProperty(
                nameof(WValueDropDownCustomStructAsset.instanceSelectedStruct)
            );

            Assert.IsTrue(selectedProperty != null, "Failed to locate selectedStruct property.");
            Assert.IsTrue(
                instanceProperty != null,
                "Failed to locate instanceSelectedStruct property."
            );

            TestEquatableStruct newValue = new(4, "Fourth");
            WValueDropDownDrawer.ApplyOption(selectedProperty, newValue);
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selectedStruct.Id, Is.EqualTo(4));
            Assert.That(asset.instanceSelectedStruct.Id, Is.EqualTo(2));
        }
    }

    /// <summary>
    /// Edge case tests for WValueDropDown with arbitrary types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WValueDropDownArbitraryTypeEdgeCaseTests : CommonTestBase
    {
        [Test]
        public void GenericPropertyWithNoChildrenRejected()
        {
            WValueDropDownTypeMismatchAsset asset =
                CreateScriptableObject<WValueDropDownTypeMismatchAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownTypeMismatchAsset.vector2FieldWithDropDown)
            );
            Assert.IsTrue(property != null, "Failed to locate property.");

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new(1, 2, 3);
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<HelpBox>(element, "Unsupported types should show HelpBox.");
        }

        [Test]
        [Description(
            "Verifies type mismatch between bool property and string options shows HelpBox"
        )]
        public void TypeMismatchShowsHelpBoxForBoolWithStringOptions()
        {
            WValueDropDownTypeMismatchAsset asset =
                CreateScriptableObject<WValueDropDownTypeMismatchAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownTypeMismatchAsset.boolFieldWithDropDown)
            );
            Assert.IsTrue(property != null, "Failed to locate property.");
            Assert.That(
                property.propertyType,
                Is.EqualTo(SerializedPropertyType.Boolean),
                $"Expected Boolean property type but got {property.propertyType}"
            );

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new("A", "B", "C");
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            Assert.That(
                attribute.ValueType,
                Is.EqualTo(typeof(string)),
                $"Attribute ValueType should be string but was {attribute.ValueType}"
            );

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<HelpBox>(
                element,
                $"Type mismatch should show HelpBox. "
                    + $"Property type: {property.propertyType}, Attribute ValueType: {attribute.ValueType}, "
                    + $"Actual element type: {element?.GetType().Name ?? "null"}"
            );
        }

        [Test]
        [Description(
            "Verifies type mismatch between Color property and float options shows HelpBox"
        )]
        public void TypeMismatchShowsHelpBoxForColorWithFloatOptions()
        {
            WValueDropDownTypeMismatchAsset asset =
                CreateScriptableObject<WValueDropDownTypeMismatchAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownTypeMismatchAsset.colorFieldWithDropDown)
            );
            Assert.IsTrue(property != null, "Failed to locate property.");
            Assert.That(
                property.propertyType,
                Is.EqualTo(SerializedPropertyType.Color),
                $"Expected Color property type but got {property.propertyType}"
            );

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new(1.5f, 2.5f, 3.5f);
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            Assert.That(
                attribute.ValueType,
                Is.EqualTo(typeof(float)),
                $"Attribute ValueType should be float but was {attribute.ValueType}"
            );

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<HelpBox>(
                element,
                $"Type mismatch should show HelpBox. "
                    + $"Property type: {property.propertyType}, Attribute ValueType: {attribute.ValueType}, "
                    + $"Actual element type: {element?.GetType().Name ?? "null"}"
            );
        }

        [Test]
        public void EmptyOptionsReturnsHelpBox()
        {
            WValueDropDownSerializableTypeAsset asset =
                CreateScriptableObject<WValueDropDownSerializableTypeAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownSerializableTypeAsset.selectedType)
            );
            Assert.IsTrue(property != null, "Failed to locate property.");

            WValueDropDownDrawer drawer = new();
            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownEmptySource),
                nameof(WValueDropDownEmptySource.GetEmptyOptions)
            );
            PropertyDrawerTestHelper.AssignAttribute(drawer, attribute);

            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<HelpBox>(element, "Empty options should show HelpBox.");
        }

        [Test]
        public void NullContextForInstanceMethodReturnsEmptyOptions()
        {
            WValueDropDownAttribute attribute = new(
                nameof(WValueDropDownSerializableTypeAsset.GetInstanceTypeOptions),
                typeof(SerializableType)
            );

            object[] options = attribute.GetOptions(null);
            Assert.That(options.Length, Is.EqualTo(0), "Null context should return empty options.");
        }

        [Test]
        public void ProviderMethodReturningNullHandledGracefully()
        {
            WValueDropDownAttribute attribute = new(
                typeof(WValueDropDownEmptySource),
                nameof(WValueDropDownEmptySource.GetEmptyOptions)
            );

            object[] options = attribute.GetOptions(null);
            Assert.IsTrue(options != null, "Options should never be null.");
            Assert.That(options.Length, Is.EqualTo(0));
        }
    }
#endif
}
