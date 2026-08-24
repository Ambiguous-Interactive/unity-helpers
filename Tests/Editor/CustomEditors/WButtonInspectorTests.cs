// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes;
    using Object = UnityEngine.Object;

    [TestFixture]
    [Category("Slow")]
    [Category("Integration")]
    public sealed class WButtonInspectorTests : BatchedEditorTestBase
    {
        [Test]
        public void WButtonInspectorCanBeInstantiatedForScriptableObject()
        {
            WButtonSingleButtonTarget target = CreateScriptableObject<WButtonSingleButtonTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.IsTrue(editor != null, "Editor should not be null");
            Assert.IsTrue(
                editor is WButtonInspector,
                $"Expected WButtonInspector but got {editor.GetType().Name}"
            );
        }

        [Test]
        public void WButtonInspectorCanBeInstantiatedForMonoBehaviour()
        {
            GameObject go = NewGameObject("TestObject");
            WButtonMonoBehaviourTestTarget target =
                go.AddComponent<WButtonMonoBehaviourTestTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.IsTrue(editor != null, "Editor should not be null");
            Assert.IsTrue(
                editor is WButtonInspector,
                $"Expected WButtonInspector but got {editor.GetType().Name}"
            );
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForScriptableObject()
        {
            WButtonSingleButtonTarget target = CreateScriptableObject<WButtonSingleButtonTarget>();
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

            Assert.IsTrue(
                caughtException == null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForMonoBehaviour()
        {
            GameObject go = NewGameObject("TestObject");
            WButtonMonoBehaviourTestTarget target =
                go.AddComponent<WButtonMonoBehaviourTestTarget>();
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

            Assert.IsTrue(
                caughtException == null,
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiHandlesNullTargetGracefully()
        {
            WButtonSingleButtonTarget target = CreateScriptableObject<WButtonSingleButtonTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"First OnInspectorGUI call should not throw. Exception: {caughtException}"
            );

            Object.DestroyImmediate(target); // UNH-SUPPRESS: Intentional to test null handling

            caughtException = null;
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

            Assert.IsTrue(
                caughtException == null,
                $"OnInspectorGUI should handle destroyed target gracefully. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator RepeatedOnInspectorGuiCallsDoNotThrow()
        {
            WButtonSingleButtonTarget target = CreateScriptableObject<WButtonSingleButtonTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        editor.OnInspectorGUI();
                    }
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"Repeated OnInspectorGUI calls should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiWithMultipleButtonsDoesNotThrow()
        {
            WButtonComplexGroupingTarget target =
                CreateScriptableObject<WButtonComplexGroupingTarget>();
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

            Assert.IsTrue(
                caughtException == null,
                $"OnInspectorGUI with multiple buttons should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiWithGroupedButtonsDoesNotThrow()
        {
            WButtonGroupPlacementTopTarget target =
                CreateScriptableObject<WButtonGroupPlacementTopTarget>();
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

            Assert.IsTrue(
                caughtException == null,
                $"OnInspectorGUI with grouped buttons should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiWithMixedPlacementDoesNotThrow()
        {
            WButtonMixedPlacementGroupsTarget target =
                CreateScriptableObject<WButtonMixedPlacementGroupsTarget>();
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

            Assert.IsTrue(
                caughtException == null,
                $"OnInspectorGUI with mixed placement should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator CanEditMultipleObjectsDoesNotThrow()
        {
            WButtonSingleButtonTarget target1 = CreateScriptableObject<WButtonSingleButtonTarget>();
            WButtonSingleButtonTarget target2 = CreateScriptableObject<WButtonSingleButtonTarget>();
            Object[] targets = { target1, target2 };
            Editor editor = Track(Editor.CreateEditor(targets));
            bool testCompleted = false;
            Exception caughtException = null;

            Assert.IsTrue(editor != null, "Editor should be created for multiple targets");

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

            Assert.IsTrue(
                caughtException == null,
                $"Multi-object editing should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [Test]
        public void WButtonMetadataCacheFindsMethodsOnScriptableObject()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonSingleButtonTarget)
            );

            Assert.IsTrue(metadata != null, "Metadata should not be null");
            Assert.AreEqual(1, metadata.Count, "Should find exactly 1 WButton method");
            Assert.AreEqual(
                nameof(WButtonSingleButtonTarget.OnlyMethod),
                metadata[0].Method.Name,
                "Should find OnlyMethod"
            );
        }

        [Test]
        public void WButtonMetadataCacheFindsGroupedMethods()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonGroupPlacementTopTarget)
            );

            Assert.IsTrue(metadata != null, "Metadata should not be null");
            Assert.IsTrue(0 < metadata.Count, "Should find WButton methods");

            bool foundGroupedMethod = false;
            for (int i = 0; i < metadata.Count; i++)
            {
                if (!string.IsNullOrEmpty(metadata[i].GroupName))
                {
                    foundGroupedMethod = true;
                    break;
                }
            }
            Assert.IsTrue(foundGroupedMethod, "Should find at least one grouped method");
        }

        [Test]
        public void WButtonMetadataCacheReturnsEmptyForTypeWithNoButtons()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RegularScriptableObject)
            );

            Assert.IsTrue(metadata != null, "Metadata should not be null");
            Assert.AreEqual(0, metadata.Count, "Should find no WButton methods");
        }

        [Test]
        public void WButtonMetadataCacheRespectsDrawOrder()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonDrawOrderTestTarget)
            );

            Assert.IsTrue(metadata != null, "Metadata should not be null");
            Assert.IsTrue(2 <= metadata.Count, "Should find multiple WButton methods");

            bool hasExplicitDrawOrder = false;
            for (int i = 0; i < metadata.Count; i++)
            {
                if (metadata[i].DrawOrder != 0)
                {
                    hasExplicitDrawOrder = true;
                    break;
                }
            }
            Assert.IsTrue(hasExplicitDrawOrder, "Should find method with explicit draw order");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiWithSpecialCharacterGroupNamesDoesNotThrow()
        {
            WButtonSpecialCharactersTarget target =
                CreateScriptableObject<WButtonSpecialCharactersTarget>();
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

            Assert.IsTrue(
                caughtException == null,
                $"OnInspectorGUI with special character group names should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiWithUnicodeGroupNamesDoesNotThrow()
        {
            WButtonUnicodeGroupNameTarget target =
                CreateScriptableObject<WButtonUnicodeGroupNameTarget>();
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

            Assert.IsTrue(
                caughtException == null,
                $"OnInspectorGUI with unicode group names should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiWithEmptyDisplayNameDoesNotThrow()
        {
            WButtonEmptyDisplayNameTarget target =
                CreateScriptableObject<WButtonEmptyDisplayNameTarget>();
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

            Assert.IsTrue(
                caughtException == null,
                $"OnInspectorGUI with empty display name should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator MultipleEditorsForSameTargetTypeDoNotInterfere()
        {
            WButtonSingleButtonTarget target1 = CreateScriptableObject<WButtonSingleButtonTarget>();
            WButtonSingleButtonTarget target2 = CreateScriptableObject<WButtonSingleButtonTarget>();

            Editor editor1 = Track(Editor.CreateEditor(target1));
            Editor editor2 = Track(Editor.CreateEditor(target2));
            bool testCompleted = false;
            Exception caughtException = null;

            Assert.IsTrue(editor1 != null, "Editor1 should be created");
            Assert.IsTrue(editor2 != null, "Editor2 should be created");
            Assert.IsFalse(
                ReferenceEquals(editor1, editor2),
                "Editors should be different instances"
            );

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor1.OnInspectorGUI();
                    editor2.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"Multiple editors should not interfere. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [Test]
        public void WButtonInspectorIsCanEditMultipleObjectsAttributeApplied()
        {
            object[] attrs = typeof(WButtonInspector).GetCustomAttributes(
                typeof(CanEditMultipleObjects),
                true
            );

            Assert.IsTrue(
                0 < attrs.Length,
                "WButtonInspector should have CanEditMultipleObjects attribute"
            );
        }

        [Test]
        public void WButtonInspectorHasCustomEditorAttribute()
        {
            object[] attrs = typeof(WButtonInspector).GetCustomAttributes(
                typeof(CustomEditor),
                true
            );

            Assert.IsTrue(0 < attrs.Length, "WButtonInspector should have CustomEditor attribute");
        }
    }
#endif
}
