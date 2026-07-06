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
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.NotNull;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.NotNull;

    /// <summary>
    /// Tests for WNotNullOdinDrawer ensuring WNotNull attributes work correctly
    /// with Odin Inspector for SerializedMonoBehaviour and SerializedScriptableObject types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WNotNullOdinDrawerTests : CommonTestBase
    {
        [Test]
        public void DrawerTypeExistsAndIsPublic()
        {
            Type drawerType = typeof(WNotNullOdinDrawer);

            Assert.That(drawerType, Is.Not.Null, "WNotNullOdinDrawer type should exist");
            Assert.That(
                drawerType.IsPublic,
                Is.True,
                "WNotNullOdinDrawer should be public for Odin to discover it"
            );
        }

        [Test]
        public void DrawerInheritsFromOdinAttributeDrawer()
        {
            Type drawerType = typeof(WNotNullOdinDrawer);
            Type expectedBaseType = typeof(OdinAttributeDrawer<WNotNullAttribute>);

            Assert.That(
                drawerType.BaseType,
                Is.EqualTo(expectedBaseType),
                "WNotNullOdinDrawer should inherit from OdinAttributeDrawer<WNotNullAttribute>"
            );
        }

        [Test]
        public void DrawerIsSealed()
        {
            Type drawerType = typeof(WNotNullOdinDrawer);

            Assert.That(
                drawerType.IsSealed,
                Is.True,
                "WNotNullOdinDrawer should be sealed for performance"
            );
        }

        [Test]
        public void DrawerRegistrationForScriptableObjectIsCorrect()
        {
            OdinNotNullScriptableObjectTarget target =
                CreateScriptableObject<OdinNotNullScriptableObjectTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for target");
        }

        [Test]
        public void DrawerRegistrationForMonoBehaviourIsCorrect()
        {
            OdinNotNullMonoBehaviourTarget target = NewGameObject("NotNullMB")
                .AddComponent<OdinNotNullMonoBehaviourTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for MonoBehaviour target");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForScriptableObject()
        {
            OdinNotNullScriptableObjectTarget target =
                CreateScriptableObject<OdinNotNullScriptableObjectTarget>();
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
            OdinNotNullMonoBehaviourTarget target = NewGameObject("NotNullMB")
                .AddComponent<OdinNotNullMonoBehaviourTarget>();
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
        public IEnumerator NullObjectReferenceDoesNotThrowOnInspectorGui()
        {
            OdinNotNullObjectReferenceTarget target =
                CreateScriptableObject<OdinNotNullObjectReferenceTarget>();
            target.notNullObject = null;
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
                $"OnInspectorGUI should not throw when object reference is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonNullObjectReferenceDoesNotThrowOnInspectorGui()
        {
            OdinNotNullObjectReferenceTarget target =
                CreateScriptableObject<OdinNotNullObjectReferenceTarget>();
            target.notNullObject = CreateScriptableObject<OdinNotNullReferencedObject>();
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
                $"OnInspectorGUI should not throw when object reference is not null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NullGameObjectReferenceDoesNotThrowOnInspectorGui()
        {
            OdinNotNullGameObjectTarget target =
                CreateScriptableObject<OdinNotNullGameObjectTarget>();
            target.notNullGameObject = null;
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
                $"OnInspectorGUI should not throw when GameObject reference is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonNullGameObjectReferenceDoesNotThrowOnInspectorGui()
        {
            OdinNotNullGameObjectTarget target =
                CreateScriptableObject<OdinNotNullGameObjectTarget>();
            target.notNullGameObject = NewGameObject("TestGameObject");
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
                $"OnInspectorGUI should not throw when GameObject reference is not null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NullTransformReferenceDoesNotThrowOnInspectorGui()
        {
            OdinNotNullTransformTarget target =
                CreateScriptableObject<OdinNotNullTransformTarget>();
            target.notNullTransform = null;
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
                $"OnInspectorGUI should not throw when Transform reference is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonNullTransformReferenceDoesNotThrowOnInspectorGui()
        {
            OdinNotNullTransformTarget target =
                CreateScriptableObject<OdinNotNullTransformTarget>();
            target.notNullTransform = NewGameObject("TestTransform").transform;
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
                $"OnInspectorGUI should not throw when Transform reference is not null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator CustomMessageDoesNotThrowOnInspectorGui()
        {
            OdinNotNullCustomMessageTarget target =
                CreateScriptableObject<OdinNotNullCustomMessageTarget>();
            target.notNullWithCustomMessage = null;
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
        public IEnumerator WarningMessageTypeDoesNotThrowOnInspectorGui()
        {
            OdinNotNullWarningTypeTarget target =
                CreateScriptableObject<OdinNotNullWarningTypeTarget>();
            target.notNullWarning = null;
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
        public IEnumerator ErrorMessageTypeDoesNotThrowOnInspectorGui()
        {
            OdinNotNullErrorTypeTarget target =
                CreateScriptableObject<OdinNotNullErrorTypeTarget>();
            target.notNullError = null;
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
        public IEnumerator ErrorMessageTypeWithCustomMessageDoesNotThrowOnInspectorGui()
        {
            OdinNotNullErrorWithCustomMessageTarget target =
                CreateScriptableObject<OdinNotNullErrorWithCustomMessageTarget>();
            target.notNullErrorCustom = null;
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
        public IEnumerator MultipleNotNullFieldsDoNotThrowOnInspectorGui()
        {
            OdinNotNullMultipleFieldsTarget target =
                CreateScriptableObject<OdinNotNullMultipleFieldsTarget>();
            target.notNullObject1 = null;
            target.notNullObject2 = null;
            target.notNullObject3 = null;
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
                $"OnInspectorGUI should not throw for multiple null fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator MixedNullAndNonNullFieldsDoNotThrowOnInspectorGui()
        {
            OdinNotNullMultipleFieldsTarget target =
                CreateScriptableObject<OdinNotNullMultipleFieldsTarget>();
            target.notNullObject1 = CreateScriptableObject<OdinNotNullReferencedObject>();
            target.notNullObject2 = null;
            target.notNullObject3 = CreateScriptableObject<OdinNotNullReferencedObject>();
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
                $"OnInspectorGUI should not throw for mixed null and non-null fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator UnityObjectNullCheckWorksForDestroyedObject()
        {
            OdinNotNullObjectReferenceTarget target =
                CreateScriptableObject<OdinNotNullObjectReferenceTarget>();
            OdinNotNullReferencedObject referencedObject =
                CreateScriptableObject<OdinNotNullReferencedObject>();
            target.notNullObject = referencedObject;
            UnityEngine.Object.DestroyImmediate(referencedObject); // UNH-SUPPRESS: Test verifies behavior when referenced object is destroyed
            _trackedObjects.Remove(referencedObject);
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
        public IEnumerator NotNullWithOtherAttributesDoesNotThrow()
        {
            OdinNotNullWithOtherAttributesTarget target =
                CreateScriptableObject<OdinNotNullWithOtherAttributesTarget>();
            target.notNullWithTooltip = null;
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
                $"OnInspectorGUI should not throw for WNotNull combined with other attributes. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NotNullOnMonoBehaviourComponentDoesNotThrow()
        {
            OdinNotNullComponentTarget target = NewGameObject("ComponentMB")
                .AddComponent<OdinNotNullComponentTarget>();
            target.notNullComponent = null;
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
                $"OnInspectorGUI should not throw for null Component reference on MonoBehaviour. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NotNullArrayElementDoesNotThrow()
        {
            OdinNotNullArrayTarget target = CreateScriptableObject<OdinNotNullArrayTarget>();
            target.notNullArray = new GameObject[] { null, NewGameObject("Test"), null };
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
                $"OnInspectorGUI should not throw for array with WNotNull. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NotNullListDoesNotThrow()
        {
            OdinNotNullListTarget target = CreateScriptableObject<OdinNotNullListTarget>();
            target.notNullList = new List<Transform> { null, NewGameObject("Test").transform };
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
                $"OnInspectorGUI should not throw for List with WNotNull. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EmptyCustomMessageUsesDefaultMessage()
        {
            OdinNotNullEmptyCustomMessageTarget target =
                CreateScriptableObject<OdinNotNullEmptyCustomMessageTarget>();
            target.notNullEmptyMessage = null;
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

        [UnityTest]
        public IEnumerator NullScriptableObjectFieldDoesNotThrow()
        {
            OdinNotNullScriptableObjectFieldTarget target =
                CreateScriptableObject<OdinNotNullScriptableObjectFieldTarget>();
            target.notNullScriptableObject = null;
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
                $"OnInspectorGUI should not throw when ScriptableObject field is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NonNullScriptableObjectFieldDoesNotThrow()
        {
            OdinNotNullScriptableObjectFieldTarget target =
                CreateScriptableObject<OdinNotNullScriptableObjectFieldTarget>();
            target.notNullScriptableObject =
                CreateScriptableObject<OdinNotNullReferencedScriptableObject>();
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
                $"OnInspectorGUI should not throw when ScriptableObject field is not null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void WNotNullAttributeHasCorrectDefaultMessageType()
        {
            WNotNullAttribute attribute = new WNotNullAttribute();

            Assert.That(
                attribute.MessageType,
                Is.EqualTo(WNotNullMessageType.Warning),
                "Default message type should be Warning"
            );
        }

        [Test]
        public void WNotNullAttributeHasCorrectMessageTypeWhenSpecified()
        {
            WNotNullAttribute attribute = new WNotNullAttribute(WNotNullMessageType.Error);

            Assert.That(
                attribute.MessageType,
                Is.EqualTo(WNotNullMessageType.Error),
                "Message type should be Error when specified"
            );
        }

        [Test]
        public void WNotNullAttributeHasCorrectCustomMessage()
        {
            string customMessage = "This field is required!";
            WNotNullAttribute attribute = new WNotNullAttribute(customMessage);

            Assert.That(
                attribute.CustomMessage,
                Is.EqualTo(customMessage),
                "Custom message should match"
            );
            Assert.That(
                attribute.MessageType,
                Is.EqualTo(WNotNullMessageType.Warning),
                "Message type should default to Warning"
            );
        }

        [Test]
        public void WNotNullAttributeHasCorrectMessageTypeAndCustomMessage()
        {
            string customMessage = "Critical field missing!";
            WNotNullAttribute attribute = new WNotNullAttribute(
                WNotNullMessageType.Error,
                customMessage
            );

            Assert.That(
                attribute.MessageType,
                Is.EqualTo(WNotNullMessageType.Error),
                "Message type should be Error"
            );
            Assert.That(
                attribute.CustomMessage,
                Is.EqualTo(customMessage),
                "Custom message should match"
            );
        }

        [Test]
        public void WNotNullAttributeWithNullCustomMessageDoesNotThrow()
        {
            WNotNullAttribute attribute = new WNotNullAttribute(WNotNullMessageType.Warning, null);

            Assert.That(attribute.CustomMessage, Is.Null, "Custom message should be null");
        }
    }
#endif
}
