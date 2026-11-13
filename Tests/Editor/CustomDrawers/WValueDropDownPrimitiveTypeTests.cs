// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Base;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    /// <summary>
    /// Comprehensive tests for WValueDropDown bool and char primitive type support.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WValueDropDownPrimitiveTypeTests : SynchronousEditorTestBase
    {
        [Test]
        public void ApplyOptionUpdatesBoolSerializedProperty()
        {
            WValueDropDownBoolAsset asset = CreateScriptableObject<WValueDropDownBoolAsset>();
            asset.selection = true;

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownBoolAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate bool selection property.");

            WValueDropDownDrawer.ApplyOption(property, false);
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selection, Is.False);
        }

        [Test]
        public void ApplyOptionUpdatesCharSerializedProperty()
        {
            WValueDropDownCharAsset asset = CreateScriptableObject<WValueDropDownCharAsset>();
            asset.selection = 'A';

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate char selection property.");

            WValueDropDownDrawer.ApplyOption(property, 'C');
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selection, Is.EqualTo('C'));
        }

        [Test]
        public void BoolPropertyTypeIsSupported()
        {
            WValueDropDownBoolAsset asset = CreateScriptableObject<WValueDropDownBoolAsset>();

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownBoolAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate bool selection property.");
            Assert.That(property.propertyType, Is.EqualTo(SerializedPropertyType.Boolean));
        }

        [Test]
        public void CharPropertyTypeIsSupported()
        {
            WValueDropDownCharAsset asset = CreateScriptableObject<WValueDropDownCharAsset>();

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate char selection property.");
            Assert.That(property.propertyType, Is.EqualTo(SerializedPropertyType.Character));
        }

        [Test]
        public void SelectorUpdatesBoolSerializedProperty()
        {
            WValueDropDownBoolAsset asset = CreateScriptableObject<WValueDropDownBoolAsset>();
            asset.selection = true;

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownBoolAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate bool selection property.");

            WValueDropDownDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(
                drawer,
                new WValueDropDownAttribute(true, false)
            );
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<BaseField<string>>(element);

            BaseField<string> selector = (BaseField<string>)element;
            DropdownField dropdown = selector.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "Dropdown field was not created.");
            Assert.That(dropdown.value, Is.EqualTo("True"));

            WDropDownSelectorBase<string> dropDownSelector =
                selector as WDropDownSelectorBase<string>;
            Assert.IsTrue(
                dropDownSelector != null,
                "Expected selector to derive from WDropDownSelectorBase<string>."
            );
            dropDownSelector.ApplySelection(1);
            serializedObject.Update();
            Assert.That(asset.selection, Is.False);
        }

        [Test]
        public void SelectorUpdatesCharSerializedProperty()
        {
            WValueDropDownCharAsset asset = CreateScriptableObject<WValueDropDownCharAsset>();
            asset.selection = 'A';

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate char selection property.");

            WValueDropDownDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(
                drawer,
                new WValueDropDownAttribute('A', 'B', 'C')
            );
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<BaseField<string>>(element);

            BaseField<string> selector = (BaseField<string>)element;
            DropdownField dropdown = selector.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "Dropdown field was not created.");
            Assert.That(dropdown.value, Is.EqualTo("A"));

            WDropDownSelectorBase<string> dropDownSelector =
                selector as WDropDownSelectorBase<string>;
            Assert.IsTrue(
                dropDownSelector != null,
                "Expected selector to derive from WDropDownSelectorBase<string>."
            );
            dropDownSelector.ApplySelection(2);
            serializedObject.Update();
            Assert.That(asset.selection, Is.EqualTo('C'));
        }

        [Test]
        public void BoolDropdownDisplaysTrueAndFalse()
        {
            WValueDropDownBoolAsset asset = CreateScriptableObject<WValueDropDownBoolAsset>();
            asset.selection = false;

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownBoolAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate bool selection property.");

            WValueDropDownDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(
                drawer,
                new WValueDropDownAttribute(true, false)
            );
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<BaseField<string>>(element);

            BaseField<string> selector = (BaseField<string>)element;
            DropdownField dropdown = selector.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "Dropdown field was not created.");
            Assert.That(dropdown.value, Is.EqualTo("False"));
        }

        [Test]
        public void CharDropdownDisplaysCharacters()
        {
            WValueDropDownCharAsset asset = CreateScriptableObject<WValueDropDownCharAsset>();
            asset.selection = 'B';

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate char selection property.");

            WValueDropDownDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(
                drawer,
                new WValueDropDownAttribute('A', 'B', 'C')
            );
            VisualElement element = drawer.CreatePropertyGUI(property);
            Assert.IsInstanceOf<BaseField<string>>(element);

            BaseField<string> selector = (BaseField<string>)element;
            DropdownField dropdown = selector.Q<DropdownField>();
            Assert.IsTrue(dropdown != null, "Dropdown field was not created.");
            Assert.That(dropdown.value, Is.EqualTo("B"));
        }

        [Test]
        public void ChangingBoolSelectionUpdatesProperty()
        {
            WValueDropDownBoolAsset asset = CreateScriptableObject<WValueDropDownBoolAsset>();
            asset.selection = true;

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownBoolAsset.selection)
            );

            Assert.That(asset.selection, Is.True);

            WValueDropDownDrawer.ApplyOption(property, false);
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.False);

            WValueDropDownDrawer.ApplyOption(property, true);
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.True);
        }

        [Test]
        public void ChangingCharSelectionUpdatesProperty()
        {
            WValueDropDownCharAsset asset = CreateScriptableObject<WValueDropDownCharAsset>();
            asset.selection = 'A';

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.selection)
            );

            Assert.That(asset.selection, Is.EqualTo('A'));

            WValueDropDownDrawer.ApplyOption(property, 'B');
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.EqualTo('B'));

            WValueDropDownDrawer.ApplyOption(property, 'C');
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.EqualTo('C'));

            WValueDropDownDrawer.ApplyOption(property, 'A');
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.EqualTo('A'));
        }

        [Test]
        public void TypedBoolConstructorSetsValueType()
        {
            WValueDropDownAttribute boolAttr = new(true, false);
            Assert.That(boolAttr.ValueType, Is.EqualTo(typeof(bool)));
        }

        [Test]
        public void TypedCharConstructorSetsValueType()
        {
            WValueDropDownAttribute charAttr = new('a', 'b', 'c');
            Assert.That(charAttr.ValueType, Is.EqualTo(typeof(char)));
        }

        [Test]
        public void BoolAttributeReturnsCorrectOptions()
        {
            WValueDropDownAttribute attr = new(true, false);
            object[] options = attr.GetOptions(null);

            Assert.That(options.Length, Is.EqualTo(2));
            Assert.That(options[0], Is.EqualTo(true));
            Assert.That(options[1], Is.EqualTo(false));
        }

        [Test]
        public void CharAttributeReturnsCorrectOptions()
        {
            WValueDropDownAttribute attr = new('X', 'Y', 'Z');
            object[] options = attr.GetOptions(null);

            Assert.That(options.Length, Is.EqualTo(3));
            Assert.That(options[0], Is.EqualTo('X'));
            Assert.That(options[1], Is.EqualTo('Y'));
            Assert.That(options[2], Is.EqualTo('Z'));
        }

        [Test]
        public void ApplyBoolIgnoresNonBoolValue()
        {
            WValueDropDownBoolAsset asset = CreateScriptableObject<WValueDropDownBoolAsset>();
            asset.selection = true;

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownBoolAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate bool selection property.");

            WValueDropDownDrawer.ApplyOption(property, "not a bool");
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selection, Is.True, "Should not change when applying non-bool value");
        }

        [Test]
        public void ApplyCharIgnoresNonCharValue()
        {
            WValueDropDownCharAsset asset = CreateScriptableObject<WValueDropDownCharAsset>();
            asset.selection = 'A';

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.selection)
            );
            Assert.IsTrue(property != null, "Failed to locate char selection property.");

            WValueDropDownDrawer.ApplyOption(property, "not a char");
            serializedObject.ApplyModifiedProperties();

            Assert.That(
                asset.selection,
                Is.EqualTo('A'),
                "Should not change when applying non-char value"
            );
        }

        [Test]
        public void MultipleBoolFieldsWorkIndependently()
        {
            WValueDropDownBoolAsset asset = CreateScriptableObject<WValueDropDownBoolAsset>();
            asset.selection = true;
            asset.alternateSelection = false;

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty selectionProperty = serializedObject.FindProperty(
                nameof(WValueDropDownBoolAsset.selection)
            );
            SerializedProperty alternateProperty = serializedObject.FindProperty(
                nameof(WValueDropDownBoolAsset.alternateSelection)
            );

            WValueDropDownDrawer.ApplyOption(selectionProperty, false);
            WValueDropDownDrawer.ApplyOption(alternateProperty, true);
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selection, Is.False);
            Assert.That(asset.alternateSelection, Is.True);
        }

        [Test]
        public void MultipleCharFieldsWorkIndependently()
        {
            WValueDropDownCharAsset asset = CreateScriptableObject<WValueDropDownCharAsset>();
            asset.selection = 'A';
            asset.alternateSelection = 'X';

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty selectionProperty = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.selection)
            );
            SerializedProperty alternateProperty = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.alternateSelection)
            );

            WValueDropDownDrawer.ApplyOption(selectionProperty, 'C');
            WValueDropDownDrawer.ApplyOption(alternateProperty, 'Z');
            serializedObject.ApplyModifiedProperties();

            Assert.That(asset.selection, Is.EqualTo('C'));
            Assert.That(asset.alternateSelection, Is.EqualTo('Z'));
        }

        [Test]
        public void CharWithSpecialCharactersWorks()
        {
            WValueDropDownCharAsset asset = CreateScriptableObject<WValueDropDownCharAsset>();

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.selection)
            );

            WValueDropDownDrawer.ApplyOption(property, ' ');
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.EqualTo(' '), "Space character should work");

            WValueDropDownDrawer.ApplyOption(property, '\t');
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.EqualTo('\t'), "Tab character should work");

            WValueDropDownDrawer.ApplyOption(property, '0');
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.EqualTo('0'), "Digit character should work");
        }

        [Test]
        public void CharWithUnicodeCharactersWorks()
        {
            WValueDropDownCharAsset asset = CreateScriptableObject<WValueDropDownCharAsset>();

            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(WValueDropDownCharAsset.selection)
            );

            WValueDropDownDrawer.ApplyOption(property, '\u00E9');
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.EqualTo('é'), "Accented character should work");

            WValueDropDownDrawer.ApplyOption(property, '\u4E2D');
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.EqualTo('中'), "Chinese character should work");

            WValueDropDownDrawer.ApplyOption(property, '\u263A');
            serializedObject.ApplyModifiedProperties();
            Assert.That(asset.selection, Is.EqualTo('☺'), "Symbol character should work");
        }
    }
#endif
}
