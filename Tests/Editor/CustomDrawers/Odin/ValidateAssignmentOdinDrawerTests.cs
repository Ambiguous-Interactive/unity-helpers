// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using Sirenix.OdinInspector.Editor;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValidateAssignment;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.ValidateAssignment;

    /// <summary>
    /// Tests for ValidateAssignmentOdinDrawer ensuring ValidateAssignment attributes work correctly
    /// with Odin Inspector for SerializedMonoBehaviour and SerializedScriptableObject types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ValidateAssignmentOdinDrawerTests : CommonTestBase
    {
        [Test]
        public void DrawerTypeExistsAndIsPublic()
        {
            Type drawerType = typeof(ValidateAssignmentOdinDrawer);

            Assert.That(drawerType, Is.Not.Null, "ValidateAssignmentOdinDrawer type should exist");
            Assert.That(
                drawerType.IsPublic,
                Is.True,
                "ValidateAssignmentOdinDrawer should be public for Odin to discover it"
            );
        }

        [Test]
        public void DrawerInheritsFromOdinAttributeDrawer()
        {
            Type drawerType = typeof(ValidateAssignmentOdinDrawer);
            Type expectedBaseType = typeof(OdinAttributeDrawer<ValidateAssignmentAttribute>);

            Assert.That(
                drawerType.BaseType,
                Is.EqualTo(expectedBaseType),
                "ValidateAssignmentOdinDrawer should inherit from OdinAttributeDrawer<ValidateAssignmentAttribute>"
            );
        }

        [Test]
        public void DrawerIsSealed()
        {
            Type drawerType = typeof(ValidateAssignmentOdinDrawer);

            Assert.That(
                drawerType.IsSealed,
                Is.True,
                "ValidateAssignmentOdinDrawer should be sealed for performance"
            );
        }

        [Test]
        public void DrawerRegistrationForScriptableObjectIsCorrect()
        {
            OdinValidateAssignmentScriptableObjectTarget target =
                CreateScriptableObject<OdinValidateAssignmentScriptableObjectTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for target");
        }

        [Test]
        public void DrawerRegistrationForMonoBehaviourIsCorrect()
        {
            OdinValidateAssignmentMonoBehaviourTarget target = NewGameObject("ValidateAssignmentMB")
                .AddComponent<OdinValidateAssignmentMonoBehaviourTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for MonoBehaviour target");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForScriptableObject()
        {
            OdinValidateAssignmentScriptableObjectTarget target =
                CreateScriptableObject<OdinValidateAssignmentScriptableObjectTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for ScriptableObject. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForMonoBehaviour()
        {
            OdinValidateAssignmentMonoBehaviourTarget target = NewGameObject("ValidateAssignmentMB")
                .AddComponent<OdinValidateAssignmentMonoBehaviourTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for MonoBehaviour. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NullObjectShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentNullObjectTarget target =
                CreateScriptableObject<OdinValidateAssignmentNullObjectTarget>();
            target.validateObject = null;
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when object is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonNullObjectDoesNotThrow()
        {
            OdinValidateAssignmentNullObjectTarget target =
                CreateScriptableObject<OdinValidateAssignmentNullObjectTarget>();
            target.validateObject = NewGameObject("ValidObject");
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when object is not null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EmptyStringShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentStringTarget target =
                CreateScriptableObject<OdinValidateAssignmentStringTarget>();
            target.validateString = "";
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when string is empty. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator WhitespaceStringShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentStringTarget target =
                CreateScriptableObject<OdinValidateAssignmentStringTarget>();
            target.validateString = "   ";
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when string is whitespace. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NullStringShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentStringTarget target =
                CreateScriptableObject<OdinValidateAssignmentStringTarget>();
            target.validateString = null;
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when string is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonEmptyStringDoesNotThrow()
        {
            OdinValidateAssignmentStringTarget target =
                CreateScriptableObject<OdinValidateAssignmentStringTarget>();
            target.validateString = "Valid string content";
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when string has content. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EmptyListShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentListTarget target =
                CreateScriptableObject<OdinValidateAssignmentListTarget>();
            target.validateList = new List<string>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when list is empty. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NullListShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentListTarget target =
                CreateScriptableObject<OdinValidateAssignmentListTarget>();
            target.validateList = null;
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when list is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonEmptyListDoesNotThrow()
        {
            OdinValidateAssignmentListTarget target =
                CreateScriptableObject<OdinValidateAssignmentListTarget>();
            target.validateList = new List<string> { "item1", "item2" };
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when list has elements. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EmptyArrayShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentArrayTarget target =
                CreateScriptableObject<OdinValidateAssignmentArrayTarget>();
            target.validateArray = new int[0];
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when array is empty. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NullArrayShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentArrayTarget target =
                CreateScriptableObject<OdinValidateAssignmentArrayTarget>();
            target.validateArray = null;
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when array is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonEmptyArrayDoesNotThrow()
        {
            OdinValidateAssignmentArrayTarget target =
                CreateScriptableObject<OdinValidateAssignmentArrayTarget>();
            target.validateArray = new int[] { 1, 2, 3 };
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when array has elements. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EmptyDictionaryShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentDictionaryTarget target =
                CreateScriptableObject<OdinValidateAssignmentDictionaryTarget>();
            target.validateDictionary = new Dictionary<string, int>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when dictionary is empty. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonEmptyDictionaryDoesNotThrow()
        {
            OdinValidateAssignmentDictionaryTarget target =
                CreateScriptableObject<OdinValidateAssignmentDictionaryTarget>();
            target.validateDictionary = new Dictionary<string, int>
            {
                { "key1", 1 },
                { "key2", 2 },
            };
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when dictionary has elements. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EmptyHashSetShowsValidationDoesNotThrow()
        {
            OdinValidateAssignmentHashSetTarget target =
                CreateScriptableObject<OdinValidateAssignmentHashSetTarget>();
            target.validateHashSet = new HashSet<int>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when HashSet is empty. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonEmptyHashSetDoesNotThrow()
        {
            OdinValidateAssignmentHashSetTarget target =
                CreateScriptableObject<OdinValidateAssignmentHashSetTarget>();
            target.validateHashSet = new HashSet<int> { 1, 2, 3 };
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when HashSet has elements. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator CustomMessageDoesNotThrow()
        {
            OdinValidateAssignmentCustomMessageTarget target =
                CreateScriptableObject<OdinValidateAssignmentCustomMessageTarget>();
            target.validateWithCustomMessage = null;
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when custom message is specified. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator WarningMessageTypeDoesNotThrow()
        {
            OdinValidateAssignmentWarningTypeTarget target =
                CreateScriptableObject<OdinValidateAssignmentWarningTypeTarget>();
            target.validateWarning = "";
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for Warning message type. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ErrorMessageTypeDoesNotThrow()
        {
            OdinValidateAssignmentErrorTypeTarget target =
                CreateScriptableObject<OdinValidateAssignmentErrorTypeTarget>();
            target.validateError = "";
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for Error message type. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ErrorMessageTypeWithCustomMessageDoesNotThrow()
        {
            OdinValidateAssignmentErrorWithCustomMessageTarget target =
                CreateScriptableObject<OdinValidateAssignmentErrorWithCustomMessageTarget>();
            target.validateErrorCustom = new List<Transform>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for Error type with custom message. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator MultipleValidateAssignmentFieldsDoNotThrow()
        {
            OdinValidateAssignmentMultipleFieldsTarget target =
                CreateScriptableObject<OdinValidateAssignmentMultipleFieldsTarget>();
            target.validateObject = null;
            target.validateString = "";
            target.validateList = new List<int>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for multiple invalid fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator MixedValidAndInvalidFieldsDoNotThrow()
        {
            OdinValidateAssignmentMultipleFieldsTarget target =
                CreateScriptableObject<OdinValidateAssignmentMultipleFieldsTarget>();
            target.validateObject = NewGameObject("ValidObject");
            target.validateString = "";
            target.validateList = new List<int> { 1, 2, 3 };
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for mixed valid and invalid fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator UnityObjectNullCheckWorksForDestroyedObject()
        {
            OdinValidateAssignmentNullObjectTarget target =
                CreateScriptableObject<OdinValidateAssignmentNullObjectTarget>();
            GameObject validObject = NewGameObject("DestroyedObject");
            target.validateObject = validObject;
            UnityEngine.Object.DestroyImmediate(validObject); // UNH-SUPPRESS: Test verifies behavior when referenced object is destroyed
            _trackedObjects.Remove(validObject);
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when referenced object is destroyed. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ValidateAssignmentWithOtherAttributesDoesNotThrow()
        {
            OdinValidateAssignmentWithOtherAttributesTarget target =
                CreateScriptableObject<OdinValidateAssignmentWithOtherAttributesTarget>();
            target.validateWithTooltip = "";
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for ValidateAssignment combined with other attributes. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ValidateAssignmentOnMonoBehaviourFieldsDoNotThrow()
        {
            OdinValidateAssignmentMonoBehaviourFieldsTarget target = NewGameObject("MBFields")
                .AddComponent<OdinValidateAssignmentMonoBehaviourFieldsTarget>();
            target.validateTransform = null;
            target.validateName = "";
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for MonoBehaviour with invalid fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EmptyCustomMessageUsesDefaultMessage()
        {
            OdinValidateAssignmentEmptyCustomMessageTarget target =
                CreateScriptableObject<OdinValidateAssignmentEmptyCustomMessageTarget>();
            target.validateEmptyMessage = null;
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when custom message is empty. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void ValidateAssignmentAttributeHasCorrectDefaultMessageType()
        {
            ValidateAssignmentAttribute attribute = new ValidateAssignmentAttribute();

            Assert.That(
                attribute.MessageType,
                Is.EqualTo(ValidateAssignmentMessageType.Warning),
                "Default message type should be Warning"
            );
        }

        [Test]
        public void ValidateAssignmentAttributeHasCorrectMessageTypeWhenSpecified()
        {
            ValidateAssignmentAttribute attribute = new ValidateAssignmentAttribute(
                ValidateAssignmentMessageType.Error
            );

            Assert.That(
                attribute.MessageType,
                Is.EqualTo(ValidateAssignmentMessageType.Error),
                "Message type should be Error when specified"
            );
        }

        [Test]
        public void ValidateAssignmentAttributeHasCorrectCustomMessage()
        {
            string customMessage = "This field must be assigned!";
            ValidateAssignmentAttribute attribute = new ValidateAssignmentAttribute(customMessage);

            Assert.That(
                attribute.CustomMessage,
                Is.EqualTo(customMessage),
                "Custom message should match"
            );
            Assert.That(
                attribute.MessageType,
                Is.EqualTo(ValidateAssignmentMessageType.Warning),
                "Message type should default to Warning"
            );
        }

        [Test]
        public void ValidateAssignmentAttributeHasCorrectMessageTypeAndCustomMessage()
        {
            string customMessage = "Critical: spawn points required!";
            ValidateAssignmentAttribute attribute = new ValidateAssignmentAttribute(
                ValidateAssignmentMessageType.Error,
                customMessage
            );

            Assert.That(
                attribute.MessageType,
                Is.EqualTo(ValidateAssignmentMessageType.Error),
                "Message type should be Error"
            );
            Assert.That(
                attribute.CustomMessage,
                Is.EqualTo(customMessage),
                "Custom message should match"
            );
        }

        [Test]
        public void ValidateAssignmentAttributeWithNullCustomMessageDoesNotThrow()
        {
            ValidateAssignmentAttribute attribute = new ValidateAssignmentAttribute(
                ValidateAssignmentMessageType.Warning,
                null
            );

            Assert.That(attribute.CustomMessage, Is.Null, "Custom message should be null");
        }

        [UnityTest]
        public IEnumerator ValidateAssignmentQueueEmptyDoesNotThrow()
        {
            OdinValidateAssignmentQueueTarget target =
                CreateScriptableObject<OdinValidateAssignmentQueueTarget>();
            target.validateQueue = new Queue<string>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when Queue is empty. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ValidateAssignmentQueueWithElementsDoesNotThrow()
        {
            OdinValidateAssignmentQueueTarget target =
                CreateScriptableObject<OdinValidateAssignmentQueueTarget>();
            target.validateQueue = new Queue<string>(new[] { "a", "b", "c" });
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when Queue has elements. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ValidateAssignmentStackEmptyDoesNotThrow()
        {
            OdinValidateAssignmentStackTarget target =
                CreateScriptableObject<OdinValidateAssignmentStackTarget>();
            target.validateStack = new Stack<int>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when Stack is empty. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ValidateAssignmentStackWithElementsDoesNotThrow()
        {
            OdinValidateAssignmentStackTarget target =
                CreateScriptableObject<OdinValidateAssignmentStackTarget>();
            target.validateStack = new Stack<int>(new[] { 1, 2, 3 });
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw when Stack has elements. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ValidValueTypesDoNotShowValidation()
        {
            OdinValidateAssignmentValidValuesTarget target =
                CreateScriptableObject<OdinValidateAssignmentValidValuesTarget>();
            target.validObject = NewGameObject("ValidObject");
            target.validString = "Valid String";
            target.validList = new List<int> { 1, 2, 3 };
            target.validArray = new float[] { 1.0f, 2.0f };
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for all valid values. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }
    }
#endif
}
