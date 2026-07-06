// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.InLineEditor;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.InLineEditor;

    /// <summary>
    /// Tests for WInLineEditorOdinDrawer ensuring WInLineEditor attribute
    /// works correctly with Odin Inspector for SerializedMonoBehaviour
    /// and SerializedScriptableObject types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WInLineEditorOdinDrawerTests : CommonTestBase
    {
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            WInLineEditorOdinDrawer.ClearCachedStateForTesting();
        }

        [TearDown]
        public override void TearDown()
        {
            WInLineEditorOdinDrawer.ClearCachedStateForTesting();
            base.TearDown();
        }

        [Test]
        public void DrawerRegistrationForScriptableObjectIsCorrect()
        {
            OdinInlineEditorScriptableObjectTarget target =
                CreateScriptableObject<OdinInlineEditorScriptableObjectTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for target");
        }

        [Test]
        public void DrawerRegistrationForMonoBehaviourIsCorrect()
        {
            OdinInlineEditorMonoBehaviourTarget target = NewGameObject("InlineMB")
                .AddComponent<OdinInlineEditorMonoBehaviourTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for MonoBehaviour target");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForScriptableObject()
        {
            OdinInlineEditorScriptableObjectTarget target =
                CreateScriptableObject<OdinInlineEditorScriptableObjectTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
            OdinInlineEditorMonoBehaviourTarget target = NewGameObject("InlineMB")
                .AddComponent<OdinInlineEditorMonoBehaviourTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
        public IEnumerator NullReferenceShowsNoInlineEditor()
        {
            OdinInlineEditorScriptableObjectTarget target =
                CreateScriptableObject<OdinInlineEditorScriptableObjectTarget>();
            target.referencedObject = null;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw when referenced object is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ValidReferenceShowsInlineEditor()
        {
            OdinInlineEditorScriptableObjectTarget target =
                CreateScriptableObject<OdinInlineEditorScriptableObjectTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.referencedObject = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw when referenced object is valid. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void ResolveModeReturnsAlwaysExpandedForAlwaysExpandedAttribute()
        {
            WInLineEditorAttribute attribute = new(WInLineEditorMode.AlwaysExpanded);

            WInLineEditorMode resolvedMode = InLineEditorShared.ResolveMode(attribute);

            Assert.That(
                resolvedMode,
                Is.EqualTo(WInLineEditorMode.AlwaysExpanded),
                "Mode should resolve to AlwaysExpanded"
            );
        }

        [Test]
        public void ResolveModeReturnsFoldoutExpandedForFoldoutExpandedAttribute()
        {
            WInLineEditorAttribute attribute = new(WInLineEditorMode.FoldoutExpanded);

            WInLineEditorMode resolvedMode = InLineEditorShared.ResolveMode(attribute);

            Assert.That(
                resolvedMode,
                Is.EqualTo(WInLineEditorMode.FoldoutExpanded),
                "Mode should resolve to FoldoutExpanded"
            );
        }

        [Test]
        public void ResolveModeReturnsFoldoutCollapsedForFoldoutCollapsedAttribute()
        {
            WInLineEditorAttribute attribute = new(WInLineEditorMode.FoldoutCollapsed);

            WInLineEditorMode resolvedMode = InLineEditorShared.ResolveMode(attribute);

            Assert.That(
                resolvedMode,
                Is.EqualTo(WInLineEditorMode.FoldoutCollapsed),
                "Mode should resolve to FoldoutCollapsed"
            );
        }

        [Test]
        public void GetFoldoutStateReturnsTrueForAlwaysExpanded()
        {
            string foldoutKey = "test::alwaysExpanded";

            bool foldoutState = InLineEditorShared.GetFoldoutState(
                foldoutKey,
                WInLineEditorMode.AlwaysExpanded
            );

            Assert.That(
                foldoutState,
                Is.True,
                "Foldout state should be true for AlwaysExpanded mode"
            );
        }

        [Test]
        public void GetFoldoutStateReturnsTrueForFoldoutExpanded()
        {
            string foldoutKey = "test::foldoutExpanded";

            bool foldoutState = InLineEditorShared.GetFoldoutState(
                foldoutKey,
                WInLineEditorMode.FoldoutExpanded
            );

            Assert.That(
                foldoutState,
                Is.True,
                "Initial foldout state should be true for FoldoutExpanded mode"
            );
        }

        [Test]
        public void GetFoldoutStateReturnsFalseForFoldoutCollapsed()
        {
            string foldoutKey = "test::foldoutCollapsed";

            bool foldoutState = InLineEditorShared.GetFoldoutState(
                foldoutKey,
                WInLineEditorMode.FoldoutCollapsed
            );

            Assert.That(
                foldoutState,
                Is.False,
                "Initial foldout state should be false for FoldoutCollapsed mode"
            );
        }

        [Test]
        public void FoldoutStatePersistsAcrossCalls()
        {
            string foldoutKey = "test::persistenceTest";

            WInLineEditorOdinDrawer.SetFoldoutStateForTesting(foldoutKey, true);
            bool state1 = WInLineEditorOdinDrawer.GetFoldoutStateForTesting(foldoutKey);

            WInLineEditorOdinDrawer.SetFoldoutStateForTesting(foldoutKey, false);
            bool state2 = WInLineEditorOdinDrawer.GetFoldoutStateForTesting(foldoutKey);

            Assert.That(state1, Is.True, "First state should be true");
            Assert.That(state2, Is.False, "Second state should be false after modification");
        }

        [UnityTest]
        public IEnumerator AlwaysExpandedModeDoesNotShowFoldoutToggle()
        {
            OdinInlineEditorAlwaysExpandedTarget target =
                CreateScriptableObject<OdinInlineEditorAlwaysExpandedTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.alwaysExpandedReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw for AlwaysExpanded mode. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator FoldoutExpandedModeStartsExpanded()
        {
            OdinInlineEditorFoldoutExpandedTarget target =
                CreateScriptableObject<OdinInlineEditorFoldoutExpandedTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.foldoutExpandedReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw for FoldoutExpanded mode. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator FoldoutCollapsedModeStartsCollapsed()
        {
            OdinInlineEditorFoldoutCollapsedTarget target =
                CreateScriptableObject<OdinInlineEditorFoldoutCollapsedTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.foldoutCollapsedReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw for FoldoutCollapsed mode. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void UseSettingsModeResolvesCorrectly()
        {
            WInLineEditorAttribute attribute = new(WInLineEditorMode.UseSettings);

            WInLineEditorMode resolvedMode = InLineEditorShared.ResolveMode(attribute);

            Assert.That(
                resolvedMode,
                Is.Not.EqualTo(WInLineEditorMode.UseSettings),
                "UseSettings mode should resolve to a concrete mode based on settings"
            );
        }

        [Test]
        public void EditorCachingWorks()
        {
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();

            UnityEditor.Editor editor1 = InLineEditorShared.GetOrCreateEditor(referencedObject);
            UnityEditor.Editor editor2 = InLineEditorShared.GetOrCreateEditor(referencedObject);

            try
            {
                Assert.That(editor1, Is.Not.Null, "First editor should not be null");
                Assert.That(editor2, Is.Not.Null, "Second editor should not be null");
                Assert.That(
                    editor1,
                    Is.SameAs(editor2),
                    "Cached editor should return the same instance"
                );
            }
            finally
            {
                WInLineEditorOdinDrawer.ClearCachedStateForTesting();
            }
        }

        [Test]
        public void EditorCachingReturnsNullForNullObject()
        {
            UnityEditor.Editor editor = InLineEditorShared.GetOrCreateEditor(null);

            Assert.That(editor, Is.Null, "Editor should be null for null object");
        }

        [UnityTest]
        public IEnumerator PreviewRenderingOptionDoesNotThrow()
        {
            OdinInlineEditorWithPreviewTarget target =
                CreateScriptableObject<OdinInlineEditorWithPreviewTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.previewReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with preview enabled. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ScrollingEnabledOptionDoesNotThrow()
        {
            OdinInlineEditorWithScrollingTarget target =
                CreateScriptableObject<OdinInlineEditorWithScrollingTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.scrollingReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with scrolling enabled. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ScrollingDisabledOptionDoesNotThrow()
        {
            OdinInlineEditorNoScrollingTarget target =
                CreateScriptableObject<OdinInlineEditorNoScrollingTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.noScrollingReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with scrolling disabled. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawObjectFieldFalseDoesNotThrow()
        {
            OdinInlineEditorNoObjectFieldTarget target =
                CreateScriptableObject<OdinInlineEditorNoObjectFieldTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.noObjectFieldReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with drawObjectField=false. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawHeaderFalseDoesNotThrow()
        {
            OdinInlineEditorNoHeaderTarget target =
                CreateScriptableObject<OdinInlineEditorNoHeaderTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.noHeaderReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with drawHeader=false. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator RepeatedOnInspectorGuiCallsDoNotThrow()
        {
            OdinInlineEditorScriptableObjectTarget target =
                CreateScriptableObject<OdinInlineEditorScriptableObjectTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.referencedObject = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw on repeated calls. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator MultipleEditorsCanBeCreatedForSameTargetType()
        {
            OdinInlineEditorScriptableObjectTarget target1 =
                CreateScriptableObject<OdinInlineEditorScriptableObjectTarget>();
            OdinInlineEditorScriptableObjectTarget target2 =
                CreateScriptableObject<OdinInlineEditorScriptableObjectTarget>();

            OdinReferencedScriptableObject referencedObject1 =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            OdinReferencedScriptableObject referencedObject2 =
                CreateScriptableObject<OdinReferencedScriptableObject>();

            target1.referencedObject = referencedObject1;
            target2.referencedObject = referencedObject2;

            UnityEditor.Editor editor1 = Track(UnityEditor.Editor.CreateEditor(target1));
            UnityEditor.Editor editor2 = Track(UnityEditor.Editor.CreateEditor(target2));

            bool testCompleted = false;
            Exception caughtException = null;

            Assert.That(editor1, Is.Not.Null, "First editor should not be null");
            Assert.That(editor2, Is.Not.Null, "Second editor should not be null");
            Assert.That(editor1, Is.Not.SameAs(editor2), "Editors should be different instances");

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

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for multiple editors. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator InspectorHandlesDestroyedTargetGracefully()
        {
            GameObject go = NewGameObject("InlineMBTest");
            OdinInlineEditorMonoBehaviourTarget target =
                go.AddComponent<OdinInlineEditorMonoBehaviourTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw before target is destroyed. Exception: {caughtException}"
            );

            UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies behavior after target destroyed
            _trackedObjects.Remove(target);
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

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw after target is destroyed. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator InspectorHandlesDestroyedReferencedObjectGracefully()
        {
            OdinInlineEditorScriptableObjectTarget target =
                CreateScriptableObject<OdinInlineEditorScriptableObjectTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.referencedObject = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw before referenced object is destroyed. Exception: {caughtException}"
            );

            UnityEngine.Object.DestroyImmediate(referencedObject); // UNH-SUPPRESS: Test verifies behavior after referenced object destroyed
            _trackedObjects.Remove(referencedObject);
            target.referencedObject = null;
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

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw after referenced object is destroyed. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void ClearCachedStateForTestingClearsFoldoutStates()
        {
            string foldoutKey = "test::clearTest";
            WInLineEditorOdinDrawer.SetFoldoutStateForTesting(foldoutKey, true);

            bool stateBefore = WInLineEditorOdinDrawer.GetFoldoutStateForTesting(foldoutKey);

            WInLineEditorOdinDrawer.ClearCachedStateForTesting();

            bool stateAfter = WInLineEditorOdinDrawer.GetFoldoutStateForTesting(foldoutKey);

            Assert.That(stateBefore, Is.True, "State should be true before clearing");
            Assert.That(
                stateAfter,
                Is.False,
                "State should be false after clearing (key not found)"
            );
        }

        [Test]
        public void DifferentFoldoutKeysAreIndependent()
        {
            string key1 = "test::independent1";
            string key2 = "test::independent2";

            WInLineEditorOdinDrawer.SetFoldoutStateForTesting(key1, true);
            WInLineEditorOdinDrawer.SetFoldoutStateForTesting(key2, false);

            bool state1 = WInLineEditorOdinDrawer.GetFoldoutStateForTesting(key1);
            bool state2 = WInLineEditorOdinDrawer.GetFoldoutStateForTesting(key2);

            Assert.That(state1, Is.True, "Key1 should be true");
            Assert.That(state2, Is.False, "Key2 should be false");
        }

        [UnityTest]
        public IEnumerator InlineEditorWithCustomHeightDoesNotThrow()
        {
            OdinInlineEditorCustomHeightTarget target =
                CreateScriptableObject<OdinInlineEditorCustomHeightTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.customHeightReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with custom inspector height. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator InlineEditorWithCustomPreviewHeightDoesNotThrow()
        {
            OdinInlineEditorCustomPreviewHeightTarget target =
                CreateScriptableObject<OdinInlineEditorCustomPreviewHeightTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.customPreviewHeightReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with custom preview height. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void AllFoldoutModesCanBeSetAndRetrieved()
        {
            WInLineEditorMode[] modes = new[]
            {
                WInLineEditorMode.AlwaysExpanded,
                WInLineEditorMode.FoldoutExpanded,
                WInLineEditorMode.FoldoutCollapsed,
            };

            foreach (WInLineEditorMode mode in modes)
            {
                string foldoutKey = $"test::mode::{mode}";
                bool expectedState = mode != WInLineEditorMode.FoldoutCollapsed;

                bool state = InLineEditorShared.GetFoldoutState(foldoutKey, mode);

                Assert.That(
                    state,
                    Is.EqualTo(expectedState),
                    $"Mode {mode} should have initial state {expectedState}"
                );
            }
        }

        [UnityTest]
        public IEnumerator MonoBehaviourWithNullReferenceDoesNotThrow()
        {
            OdinInlineEditorMonoBehaviourTarget target = NewGameObject("InlineMBNullRef")
                .AddComponent<OdinInlineEditorMonoBehaviourTarget>();
            target.referencedMaterial = null;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw when MonoBehaviour reference is null. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator NestedInlineEditorsDoNotThrow()
        {
            OdinInlineEditorNestedTarget target =
                CreateScriptableObject<OdinInlineEditorNestedTarget>();
            OdinInlineEditorScriptableObjectTarget nestedTarget =
                CreateScriptableObject<OdinInlineEditorScriptableObjectTarget>();
            OdinReferencedScriptableObject innerReferencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();

            nestedTarget.referencedObject = innerReferencedObject;
            target.nestedReference = nestedTarget;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with nested inline editors. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator AllAttributeOptionsCanBeCombined()
        {
            OdinInlineEditorAllOptionsTarget target =
                CreateScriptableObject<OdinInlineEditorAllOptionsTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.allOptionsReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with all attribute options combined. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator MinInspectorWidthOptionDoesNotThrow()
        {
            OdinInlineEditorMinWidthTarget target =
                CreateScriptableObject<OdinInlineEditorMinWidthTarget>();
            OdinReferencedScriptableObject referencedObject =
                CreateScriptableObject<OdinReferencedScriptableObject>();
            target.minWidthReference = referencedObject;

            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

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
                $"OnInspectorGUI should not throw with minInspectorWidth option. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }
    }
#endif
}
