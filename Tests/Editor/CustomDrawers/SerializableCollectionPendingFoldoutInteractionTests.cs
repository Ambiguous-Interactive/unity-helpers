// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableCollectionPendingFoldoutInteractionTests : CommonTestBase
    {
        private const float TestLeftPadding = 12f;
        private const float TestRightPadding = 12f;
        private const float TestHorizontalPadding = 24f;

        [SetUp]
        public void TolerateHeadlessWindowErrorsSetUp()
        {
            // These tests drive a real EditorWindow (ShowUtility). Under -batchmode
            // -nographics, initializing its view logs the benign "No graphic device is
            // available to initialize the view." error (Unity 6+), which the Unity Test
            // Framework treats as a failure. Tolerate it in headless CI only; on a
            // machine with a graphics device this is inert. The framework resets
            // ignoreFailingMessages after each test, so no teardown restore is needed
            // (and restoring it would re-catch the same error if the window closes
            // during teardown).
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                LogAssert.ignoreFailingMessages = true;
            }
        }

        private sealed class PropertyDrawerClickWindow : EditorWindow
        {
            internal Action OnGUIDraw;

            internal EventType LastEventType { get; private set; }

            private void OnGUI()
            {
                if (OnGUIDraw == null)
                {
                    return;
                }

                OnGUIDraw.Invoke();
                LastEventType = Event.current.type;
            }
        }

        [UnityTest]
        public IEnumerator DictionaryPendingFoldoutLabelClickHonorsGroupOffset()
        {
            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            FoldoutInteractionDictionaryHost host =
                CreateScriptableObject<FoldoutInteractionDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(FoldoutInteractionDictionaryHost.dictionary)
            );
            property.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(FoldoutInteractionDictionaryHost),
                nameof(FoldoutInteractionDictionaryHost.dictionary)
            );

            Rect controlRect = new(40f, 60f, 500f, 240f);
            PropertyDrawerClickWindow window = Track(
                ScriptableObject.CreateInstance<PropertyDrawerClickWindow>()
            );
            window.OnGUIDraw = () =>
            {
                serializedObject.Update();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        TestHorizontalPadding,
                        TestLeftPadding,
                        TestRightPadding
                    )
                )
                {
                    drawer.OnGUI(controlRect, property, GUIContent.none);
                }

                serializedObject.ApplyModifiedProperties();
            };

            window.ShowUtility();
            window.Repaint();

            int guard = 0;
            while (!SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect && guard < 50)
            {
                window.Repaint();
                yield return null;
                guard++;
            }

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect,
                "Pending header rect should be recorded after initial draw."
            );

            Rect headerRect = SerializableDictionaryPropertyDrawer.LastPendingHeaderRect;
            Rect toggleRect = SerializableDictionaryPropertyDrawer.LastPendingFoldoutToggleRect;
            float labelWidth = Mathf.Max(0f, headerRect.xMax - toggleRect.xMax);
            Rect labelHitRect = new(toggleRect.xMax, headerRect.y, labelWidth, headerRect.height);

            Event mouseDown = new()
            {
                type = EventType.MouseDown,
                mousePosition = labelHitRect.center,
                button = 0,
            };
            window.SendEvent(mouseDown);

            window.Repaint();
            yield return null;
            window.Repaint();
            yield return null;

            bool found = drawer.TryGetPendingAnimationStateForTests(
                property,
                out bool isExpanded,
                out float animProgress,
                out bool hasAnimBool
            );

            Assert.IsTrue(found, "Pending animation state should be retrievable after click.");
            Assert.IsTrue(isExpanded, "Pending foldout should expand after label click.");
            Assert.IsTrue(hasAnimBool, "Tween AnimBool should exist after expansion.");
            Assert.Greater(animProgress, 0f, "Animation progress should advance after expansion.");

            window.Close();
        }

        [UnityTest]
        public IEnumerator SetPendingFoldoutLabelClickHonorsGroupOffset()
        {
            GroupGUIWidthUtility.ResetForTests();
            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

            FoldoutInteractionSetHost host = CreateScriptableObject<FoldoutInteractionSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(FoldoutInteractionSetHost.set)
            );
            property.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(FoldoutInteractionSetHost),
                nameof(FoldoutInteractionSetHost.set)
            );

            Rect controlRect = new(35f, 55f, 480f, 220f);
            PropertyDrawerClickWindow window = Track(
                ScriptableObject.CreateInstance<PropertyDrawerClickWindow>()
            );
            window.OnGUIDraw = () =>
            {
                serializedObject.Update();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        TestHorizontalPadding,
                        TestLeftPadding,
                        TestRightPadding
                    )
                )
                {
                    drawer.OnGUI(controlRect, property, GUIContent.none);
                }

                serializedObject.ApplyModifiedProperties();
            };

            window.ShowUtility();
            window.Repaint();

            int guard = 0;
            while (!SerializableSetPropertyDrawer.HasLastManualEntryHeaderRect && guard < 50)
            {
                window.Repaint();
                yield return null;
                guard++;
            }

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastManualEntryHeaderRect,
                "Manual entry header rect should be recorded after initial draw."
            );

            Rect headerRect = SerializableSetPropertyDrawer.LastManualEntryHeaderRect;
            Rect toggleRect = SerializableSetPropertyDrawer.LastManualEntryToggleRect;
            float labelWidth = Mathf.Max(0f, headerRect.xMax - toggleRect.xMax);
            Rect labelHitRect = new(toggleRect.xMax, headerRect.y, labelWidth, headerRect.height);

            Event mouseDown = new()
            {
                type = EventType.MouseDown,
                mousePosition = labelHitRect.center,
                button = 0,
            };
            window.SendEvent(mouseDown);

            window.Repaint();
            yield return null;
            window.Repaint();
            yield return null;

            bool found = drawer.TryGetPendingAnimationStateForTests(
                property,
                out bool isExpanded,
                out float animProgress,
                out bool hasAnimBool
            );

            Assert.IsTrue(found, "Pending animation state should be retrievable after click.");
            Assert.IsTrue(isExpanded, "Manual entry foldout should expand after label click.");
            Assert.IsTrue(hasAnimBool, "Tween AnimBool should exist after expansion.");
            Assert.Greater(animProgress, 0f, "Animation progress should advance after expansion.");

            window.Close();
        }

        [UnityTest]
        public IEnumerator DictionaryPendingFoldoutExpandsWhenRawEventTypeIsMouseDown()
        {
            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            FoldoutInteractionDictionaryHost host =
                CreateScriptableObject<FoldoutInteractionDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(FoldoutInteractionDictionaryHost.dictionary)
            );
            property.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(FoldoutInteractionDictionaryHost),
                nameof(FoldoutInteractionDictionaryHost.dictionary)
            );

            Rect controlRect = new(40f, 60f, 500f, 240f);
            PropertyDrawerClickWindow window = Track(
                ScriptableObject.CreateInstance<PropertyDrawerClickWindow>()
            );
            window.OnGUIDraw = () =>
            {
                serializedObject.Update();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        TestHorizontalPadding,
                        TestLeftPadding,
                        TestRightPadding
                    )
                )
                {
                    drawer.OnGUI(controlRect, property, GUIContent.none);
                }

                serializedObject.ApplyModifiedProperties();
            };

            window.ShowUtility();
            window.Repaint();

            int guard = 0;
            while (!SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect && guard < 50)
            {
                window.Repaint();
                yield return null;
                guard++;
            }

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect,
                "Pending header rect should be recorded after initial draw."
            );

            Rect headerRect = SerializableDictionaryPropertyDrawer.LastPendingHeaderRect;
            Rect toggleRect = SerializableDictionaryPropertyDrawer.LastPendingFoldoutToggleRect;
            float labelWidth = Mathf.Max(0f, headerRect.xMax - toggleRect.xMax);
            Rect labelHitRect = new(toggleRect.xMax, headerRect.y, labelWidth, headerRect.height);

            Event mouseDown = new()
            {
                type = EventType.MouseDown,
                mousePosition = labelHitRect.center,
                button = 0,
            };
            window.SendEvent(mouseDown);

            window.Repaint();
            yield return null;
            window.Repaint();
            yield return null;

            bool found = drawer.TryGetPendingAnimationStateForTests(
                property,
                out bool isExpanded,
                out float animProgress,
                out bool hasAnimBool
            );

            Assert.IsTrue(found, "Pending animation state should be retrievable after click.");
            Assert.IsTrue(isExpanded, "Pending foldout should expand after label click.");
            Assert.IsTrue(hasAnimBool, "Tween AnimBool should exist after expansion.");
            Assert.Greater(animProgress, 0f, "Animation progress should advance after expansion.");

            window.Close();
        }

        [UnityTest]
        public IEnumerator SetPendingFoldoutExpandsWhenRawEventTypeIsMouseDown()
        {
            GroupGUIWidthUtility.ResetForTests();
            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

            FoldoutInteractionSetHost host = CreateScriptableObject<FoldoutInteractionSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty property = serializedObject.FindProperty(
                nameof(FoldoutInteractionSetHost.set)
            );
            property.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(FoldoutInteractionSetHost),
                nameof(FoldoutInteractionSetHost.set)
            );

            Rect controlRect = new(35f, 55f, 480f, 220f);
            PropertyDrawerClickWindow window = Track(
                ScriptableObject.CreateInstance<PropertyDrawerClickWindow>()
            );
            window.OnGUIDraw = () =>
            {
                serializedObject.Update();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        TestHorizontalPadding,
                        TestLeftPadding,
                        TestRightPadding
                    )
                )
                {
                    drawer.OnGUI(controlRect, property, GUIContent.none);
                }

                serializedObject.ApplyModifiedProperties();
            };

            window.ShowUtility();
            window.Repaint();

            int guard = 0;
            while (!SerializableSetPropertyDrawer.HasLastManualEntryHeaderRect && guard < 50)
            {
                window.Repaint();
                yield return null;
                guard++;
            }

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastManualEntryHeaderRect,
                "Manual entry header rect should be recorded after initial draw."
            );

            Rect headerRect = SerializableSetPropertyDrawer.LastManualEntryHeaderRect;
            Rect toggleRect = SerializableSetPropertyDrawer.LastManualEntryToggleRect;
            float labelWidth = Mathf.Max(0f, headerRect.xMax - toggleRect.xMax);
            Rect labelHitRect = new(toggleRect.xMax, headerRect.y, labelWidth, headerRect.height);

            Event mouseDown = new()
            {
                type = EventType.MouseDown,
                mousePosition = labelHitRect.center,
                button = 0,
            };
            window.SendEvent(mouseDown);

            window.Repaint();
            yield return null;
            window.Repaint();
            yield return null;

            bool found = drawer.TryGetPendingAnimationStateForTests(
                property,
                out bool isExpanded,
                out float animProgress,
                out bool hasAnimBool
            );

            Assert.IsTrue(found, "Pending animation state should be retrievable after click.");
            Assert.IsTrue(isExpanded, "Manual entry foldout should expand after label click.");
            Assert.IsTrue(hasAnimBool, "Tween AnimBool should exist after expansion.");
            Assert.Greater(animProgress, 0f, "Animation progress should advance after expansion.");

            window.Close();
        }
    }
}
