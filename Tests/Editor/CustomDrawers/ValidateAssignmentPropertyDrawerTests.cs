// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
#if UNITY_EDITOR
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.UIElements;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ValidateAssignmentPropertyDrawerTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            ValidateAssignmentPropertyDrawer.ClearHeightCache();
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForNullObjectReference()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredGameObject property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsFalseForAssignedObjectReference()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            asset.requiredGameObject = NewGameObject("TestObject");
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredGameObject property.");
            Assert.IsFalse(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForNullTransformReference()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredTransform)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredTransform property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsFalseForAssignedTransformReference()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            asset.requiredTransform = NewGameObject("TestTransform").transform;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredTransform)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredTransform property.");
            Assert.IsFalse(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForEmptyString()
        {
            ValidateAssignmentStringTestAsset asset =
                CreateScriptableObject<ValidateAssignmentStringTestAsset>();
            asset.requiredString = string.Empty;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentStringTestAsset.requiredString)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredString property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForWhitespaceString()
        {
            ValidateAssignmentStringTestAsset asset =
                CreateScriptableObject<ValidateAssignmentStringTestAsset>();
            asset.requiredString = "   ";
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentStringTestAsset.requiredString)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredString property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForNullString()
        {
            ValidateAssignmentStringTestAsset asset =
                CreateScriptableObject<ValidateAssignmentStringTestAsset>();
            asset.requiredString = null;
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentStringTestAsset.requiredString)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredString property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsFalseForNonEmptyString()
        {
            ValidateAssignmentStringTestAsset asset =
                CreateScriptableObject<ValidateAssignmentStringTestAsset>();
            asset.requiredString = "ValidValue";
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentStringTestAsset.requiredString)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredString property.");
            Assert.IsFalse(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForEmptyList()
        {
            ValidateAssignmentCollectionTestAsset asset =
                CreateScriptableObject<ValidateAssignmentCollectionTestAsset>();
            asset.requiredList.Clear();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentCollectionTestAsset.requiredList)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredList property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsFalseForNonEmptyList()
        {
            ValidateAssignmentCollectionTestAsset asset =
                CreateScriptableObject<ValidateAssignmentCollectionTestAsset>();
            asset.requiredList.Add(42);
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentCollectionTestAsset.requiredList)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredList property.");
            Assert.IsFalse(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForEmptyArray()
        {
            ValidateAssignmentCollectionTestAsset asset =
                CreateScriptableObject<ValidateAssignmentCollectionTestAsset>();
            asset.requiredArray = new int[0];
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentCollectionTestAsset.requiredArray)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredArray property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsFalseForNonEmptyArray()
        {
            ValidateAssignmentCollectionTestAsset asset =
                CreateScriptableObject<ValidateAssignmentCollectionTestAsset>();
            asset.requiredArray = new[] { 1, 2, 3 };
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentCollectionTestAsset.requiredArray)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredArray property.");
            Assert.IsFalse(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void CreatePropertyGUIReturnsContainerWithHelpBoxForInvalidField()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredGameObject property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            VisualElement element = drawer.CreatePropertyGUI(property);

            Assert.IsTrue(element != null, "CreatePropertyGUI should return a non-null element.");
            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void CreatePropertyGUIHidesHelpBoxForValidField()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            asset.requiredGameObject = NewGameObject("TestObject");
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredGameObject property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            VisualElement element = drawer.CreatePropertyGUI(property);

            Assert.IsTrue(element != null, "CreatePropertyGUI should return a non-null element.");
            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void CreatePropertyGUIIncludesPropertyField()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredGameObject property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            VisualElement element = drawer.CreatePropertyGUI(property);

            PropertyField propertyField = element.Q<PropertyField>();
            Assert.IsTrue(propertyField != null, "Container should include a PropertyField.");
        }

        [Test]
        public void CreatePropertyGUIUsesWarningMessageTypeByDefault()
        {
            ValidateAssignmentMessageTypeTestAsset asset =
                CreateScriptableObject<ValidateAssignmentMessageTypeTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentMessageTypeTestAsset.defaultField)
            );
            Assert.IsTrue(property != null, "Failed to locate defaultField property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            VisualElement element = drawer.CreatePropertyGUI(property);

            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.messageType, Is.EqualTo(HelpBoxMessageType.Warning));
        }

        [Test]
        public void CreatePropertyGUIUsesErrorMessageTypeWhenSpecified()
        {
            ValidateAssignmentMessageTypeTestAsset asset =
                CreateScriptableObject<ValidateAssignmentMessageTypeTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentMessageTypeTestAsset.errorField)
            );
            Assert.IsTrue(property != null, "Failed to locate errorField property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(
                drawer,
                new ValidateAssignmentAttribute(ValidateAssignmentMessageType.Error)
            );
            VisualElement element = drawer.CreatePropertyGUI(property);

            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.messageType, Is.EqualTo(HelpBoxMessageType.Error));
        }

        [Test]
        public void CreatePropertyGUIUsesWarningMessageTypeWhenSpecified()
        {
            ValidateAssignmentMessageTypeTestAsset asset =
                CreateScriptableObject<ValidateAssignmentMessageTypeTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentMessageTypeTestAsset.warningField)
            );
            Assert.IsTrue(property != null, "Failed to locate warningField property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(
                drawer,
                new ValidateAssignmentAttribute(ValidateAssignmentMessageType.Warning)
            );
            VisualElement element = drawer.CreatePropertyGUI(property);

            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.messageType, Is.EqualTo(HelpBoxMessageType.Warning));
        }

        [Test]
        public void CreatePropertyGUIUsesCustomMessageWhenProvided()
        {
            ValidateAssignmentCustomMessageTestAsset asset =
                CreateScriptableObject<ValidateAssignmentCustomMessageTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentCustomMessageTestAsset.playerPrefab)
            );
            Assert.IsTrue(property != null, "Failed to locate playerPrefab property.");

            string customMessage = "Player prefab is required for spawning";
            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(
                drawer,
                new ValidateAssignmentAttribute(customMessage)
            );
            VisualElement element = drawer.CreatePropertyGUI(property);

            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.text, Is.EqualTo(customMessage));
        }

        [Test]
        public void CreatePropertyGUIUsesCustomMessageWithErrorType()
        {
            ValidateAssignmentCustomMessageTestAsset asset =
                CreateScriptableObject<ValidateAssignmentCustomMessageTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentCustomMessageTestAsset.audioSource)
            );
            Assert.IsTrue(property != null, "Failed to locate audioSource property.");

            string customMessage = "Audio source must be assigned for sound effects";
            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(
                drawer,
                new ValidateAssignmentAttribute(ValidateAssignmentMessageType.Error, customMessage)
            );
            VisualElement element = drawer.CreatePropertyGUI(property);

            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.text, Is.EqualTo(customMessage));
            Assert.That(helpBox.messageType, Is.EqualTo(HelpBoxMessageType.Error));
        }

        [Test]
        public void CreatePropertyGUIGeneratesDefaultMessageForFieldName()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredGameObject property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            VisualElement element = drawer.CreatePropertyGUI(property);

            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.text, Does.Contain("Required Game Object"));
        }

        [Test]
        [Description(
            "Verifies property height is larger for invalid fields to accommodate help box"
        )]
        public void GetPropertyHeightIsGreaterWhenFieldIsInvalid()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty invalidProperty = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(invalidProperty != null, "Failed to locate requiredGameObject property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            GUIContent label = new("Test");

            float invalidHeight = 0f;
            Assert.DoesNotThrow(
                () => invalidHeight = drawer.GetPropertyHeight(invalidProperty, label),
                "GetPropertyHeight for invalid field should not throw"
            );

            asset.requiredGameObject = NewGameObject("TestObject");
            serializedObject.Update();

            SerializedProperty validProperty = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );

            float validHeight = 0f;
            Assert.DoesNotThrow(
                () => validHeight = drawer.GetPropertyHeight(validProperty, label),
                "GetPropertyHeight for valid field should not throw"
            );

            Assert.That(
                invalidHeight,
                Is.GreaterThan(validHeight),
                $"Height should be greater when field is invalid to accommodate help box. "
                    + $"Invalid height: {invalidHeight}, Valid height: {validHeight}"
            );
        }

        [UnityTest]
        public IEnumerator OnGUIRendersHelpBoxForInvalidField()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredGameObject property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            GUIContent label = new("Required Game Object");
            float height = drawer.GetPropertyHeight(property, label);
            Rect position = new(0f, 0f, 400f, height);

            yield return TestIMGUIExecutor.Run(() =>
            {
                drawer.OnGUI(position, property, label);
            });
        }

        [UnityTest]
        public IEnumerator OnGUIRendersPropertyFieldForValidField()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            asset.requiredGameObject = NewGameObject("TestObject");
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredGameObject property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            GUIContent label = new("Required Game Object");
            float height = drawer.GetPropertyHeight(property, label);
            Rect position = new(0f, 0f, 400f, height);

            yield return TestIMGUIExecutor.Run(() =>
            {
                drawer.OnGUI(position, property, label);
            });
        }

        [Test]
        public void AttributeDefaultConstructorSetsWarningMessageType()
        {
            ValidateAssignmentAttribute attribute = new();
            Assert.That(attribute.MessageType, Is.EqualTo(ValidateAssignmentMessageType.Warning));
            Assert.That(attribute.CustomMessage, Is.Null);
        }

        [Test]
        public void AttributeMessageTypeConstructorSetsCorrectType()
        {
            ValidateAssignmentAttribute warningAttribute = new(
                ValidateAssignmentMessageType.Warning
            );
            Assert.That(
                warningAttribute.MessageType,
                Is.EqualTo(ValidateAssignmentMessageType.Warning)
            );

            ValidateAssignmentAttribute errorAttribute = new(ValidateAssignmentMessageType.Error);
            Assert.That(
                errorAttribute.MessageType,
                Is.EqualTo(ValidateAssignmentMessageType.Error)
            );
        }

        [Test]
        public void AttributeCustomMessageConstructorSetsMessage()
        {
            string customMessage = "Custom validation message";
            ValidateAssignmentAttribute attribute = new(customMessage);
            Assert.That(attribute.CustomMessage, Is.EqualTo(customMessage));
            Assert.That(attribute.MessageType, Is.EqualTo(ValidateAssignmentMessageType.Warning));
        }

        [Test]
        public void AttributeFullConstructorSetsBothProperties()
        {
            string customMessage = "Error validation message";
            ValidateAssignmentAttribute attribute = new(
                ValidateAssignmentMessageType.Error,
                customMessage
            );
            Assert.That(attribute.CustomMessage, Is.EqualTo(customMessage));
            Assert.That(attribute.MessageType, Is.EqualTo(ValidateAssignmentMessageType.Error));
        }

        [Test]
        public void IsPropertyInvalidReturnsFalseForIntProperty()
        {
            ValidateAssignmentMixedFieldsTestAsset asset =
                CreateScriptableObject<ValidateAssignmentMixedFieldsTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentMixedFieldsTestAsset.nonDecoratedIntField)
            );
            Assert.IsTrue(property != null, "Failed to locate nonDecoratedIntField property.");
            Assert.IsFalse(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForNullSprite()
        {
            ValidateAssignmentMixedFieldsTestAsset asset =
                CreateScriptableObject<ValidateAssignmentMixedFieldsTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentMixedFieldsTestAsset.nullableSprite)
            );
            Assert.IsTrue(property != null, "Failed to locate nullableSprite property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForNullScriptableObject()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredScriptableObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredScriptableObject property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsFalseForAssignedScriptableObject()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            asset.requiredScriptableObject = CreateScriptableObject<ScriptableObject>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredScriptableObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredScriptableObject property.");
            Assert.IsFalse(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        public void IsPropertyInvalidReturnsTrueForNullMaterial()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredMaterial)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredMaterial property.");
            Assert.IsTrue(ValidateAssignmentPropertyDrawer.IsPropertyInvalid(property));
        }

        [Test]
        [Description(
            "Verifies GetPropertyHeight does not throw when called outside OnGUI context (fixed production bug)"
        )]
        public void GetPropertyHeightHandlesNonGuiContext()
        {
            // EditorGUIUtility.currentViewWidth throws outside an OnGUI pass, so the drawer falls back.
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(
                property != null,
                "Failed to locate requiredGameObject property for non-GUI context test."
            );

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            GUIContent label = new("Test");

            float height = 0f;
            Assert.DoesNotThrow(
                () => height = drawer.GetPropertyHeight(property, label),
                "GetPropertyHeight should not throw when called outside OnGUI context"
            );
            Assert.That(
                height,
                Is.GreaterThan(0f),
                $"Height should be positive even outside OnGUI context, but was {height}"
            );
        }

        [Test]
        [Description("Verifies ClearHeightCache works and subsequent calls still succeed")]
        public void ClearHeightCacheClearsCache()
        {
            ValidateAssignmentObjectReferenceTestAsset asset =
                CreateScriptableObject<ValidateAssignmentObjectReferenceTestAsset>();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentObjectReferenceTestAsset.requiredGameObject)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredGameObject property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            GUIContent label = new("Test");

            float height1 = 0f;
            Assert.DoesNotThrow(
                () => height1 = drawer.GetPropertyHeight(property, label),
                "First GetPropertyHeight call should not throw"
            );

            ValidateAssignmentPropertyDrawer.ClearHeightCache();

            float height2 = 0f;
            Assert.DoesNotThrow(
                () => height2 = drawer.GetPropertyHeight(property, label),
                "GetPropertyHeight after ClearHeightCache should not throw"
            );

            Assert.That(
                height1,
                Is.GreaterThan(0f),
                $"First height should be positive, but was {height1}"
            );
            Assert.That(
                height2,
                Is.GreaterThan(0f),
                $"Second height should be positive, but was {height2}"
            );
        }

        [Test]
        public void CreatePropertyGUIShowsHelpBoxForEmptyList()
        {
            ValidateAssignmentCollectionTestAsset asset =
                CreateScriptableObject<ValidateAssignmentCollectionTestAsset>();
            asset.requiredList.Clear();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentCollectionTestAsset.requiredList)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredList property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            VisualElement element = drawer.CreatePropertyGUI(property);

            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void CreatePropertyGUIHidesHelpBoxForNonEmptyList()
        {
            ValidateAssignmentCollectionTestAsset asset =
                CreateScriptableObject<ValidateAssignmentCollectionTestAsset>();
            asset.requiredList.Add(42);
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentCollectionTestAsset.requiredList)
            );
            Assert.IsTrue(property != null, "Failed to locate requiredList property.");

            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, new ValidateAssignmentAttribute());
            VisualElement element = drawer.CreatePropertyGUI(property);

            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void CreatePropertyGUIUsesErrorMessageTypeForCollectionWithCustomMessage()
        {
            ValidateAssignmentCollectionTestAsset asset =
                CreateScriptableObject<ValidateAssignmentCollectionTestAsset>();
            asset.spawnPoints.Clear();
            using SerializedObject serializedObject = new(asset);
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(ValidateAssignmentCollectionTestAsset.spawnPoints)
            );
            Assert.IsTrue(property != null, "Failed to locate spawnPoints property.");

            string customMessage = "Spawn points cannot be empty";
            ValidateAssignmentPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(
                drawer,
                new ValidateAssignmentAttribute(ValidateAssignmentMessageType.Error, customMessage)
            );
            VisualElement element = drawer.CreatePropertyGUI(property);

            HelpBox helpBox = element.Q<HelpBox>();
            Assert.IsTrue(helpBox != null, "Container should contain a HelpBox.");
            Assert.That(helpBox.text, Is.EqualTo(customMessage));
            Assert.That(helpBox.messageType, Is.EqualTo(HelpBoxMessageType.Error));
        }
    }
#endif
}
