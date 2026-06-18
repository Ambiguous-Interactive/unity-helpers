// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializableCollectionPendingFoldoutInteractionTests : CommonTestBase
    {
        private static readonly Rect LocalLabelRect = new(12f, 8f, 140f, 18f);
        private static readonly Rect AbsoluteLabelRect = new(52f, 68f, 140f, 18f);

        public static IEnumerable<TestCaseData> DictionaryPendingFoldoutClickCases()
        {
            yield return new TestCaseData(LocalLabelRect.center).SetName(
                "DictionaryPendingFoldoutLabelClickHonorsLocalRect"
            );
            yield return new TestCaseData(AbsoluteLabelRect.center).SetName(
                "DictionaryPendingFoldoutLabelClickHonorsGroupOffsetRect"
            );
        }

        public static IEnumerable<TestCaseData> SetPendingFoldoutClickCases()
        {
            yield return new TestCaseData(LocalLabelRect.center).SetName(
                "SetPendingFoldoutLabelClickHonorsLocalRect"
            );
        }

        [TestCaseSource(nameof(DictionaryPendingFoldoutClickCases))]
        public void DictionaryPendingFoldoutLabelClickToggles(Vector2 mousePosition)
        {
            bool expanded = false;
            Event mouseDown = CreateMouseDown(mousePosition);

            bool toggled =
                SerializableDictionaryPropertyDrawer.TryTogglePendingFoldoutLabelForTests(
                    mouseDown,
                    LocalLabelRect,
                    AbsoluteLabelRect,
                    ref expanded
                );

            Assert.IsTrue(toggled, BuildToggleFailureMessage(mousePosition));
            Assert.IsTrue(expanded, BuildExpandedFailureMessage(mousePosition));
        }

        [TestCaseSource(nameof(SetPendingFoldoutClickCases))]
        public void SetPendingFoldoutLabelClickToggles(Vector2 mousePosition)
        {
            bool expanded = false;
            Event mouseDown = CreateMouseDown(mousePosition);

            bool toggled = SerializableSetPropertyDrawer.TryToggleManualEntryFoldoutLabelForTests(
                mouseDown,
                LocalLabelRect,
                ref expanded
            );

            Assert.IsTrue(toggled, BuildToggleFailureMessage(mousePosition));
            Assert.IsTrue(expanded, BuildExpandedFailureMessage(mousePosition));
        }

        [Test]
        public void PendingFoldoutLabelClickIgnoresNonPrimaryButton()
        {
            bool expanded = false;
            Event mouseDown = new()
            {
                type = EventType.MouseDown,
                mousePosition = LocalLabelRect.center,
                button = 1,
            };

            bool toggled =
                SerializableDictionaryPropertyDrawer.TryTogglePendingFoldoutLabelForTests(
                    mouseDown,
                    LocalLabelRect,
                    AbsoluteLabelRect,
                    ref expanded
                );

            Assert.IsFalse(toggled);
            Assert.IsFalse(expanded);
        }

        [Test]
        public void PendingFoldoutLabelClickIgnoresMiss()
        {
            bool expanded = false;
            Event mouseDown = CreateMouseDown(new Vector2(400f, 400f));

            bool toggled = SerializableSetPropertyDrawer.TryToggleManualEntryFoldoutLabelForTests(
                mouseDown,
                LocalLabelRect,
                ref expanded
            );

            Assert.IsFalse(toggled);
            Assert.IsFalse(expanded);
        }

        [TestCase(EventType.MouseDown, EventType.MouseDown, EventType.MouseDown)]
        [TestCase(EventType.Used, EventType.MouseDown, EventType.MouseDown)]
        [TestCase(EventType.Used, EventType.Used, EventType.Used)]
        public void EffectiveMouseEventTypeHonorsRawMouseDown(
            EventType eventType,
            EventType rawEventType,
            EventType expectedEventType
        )
        {
            Assert.AreEqual(
                expectedEventType,
                SerializableDictionaryPropertyDrawer.GetEffectiveMouseEventTypeForTests(
                    eventType,
                    rawEventType
                )
            );
            Assert.AreEqual(
                expectedEventType,
                SerializableSetPropertyDrawer.GetEffectiveMouseEventTypeForTests(
                    eventType,
                    rawEventType
                )
            );
        }

        [UnityTest]
        public IEnumerator DictionaryPendingFoldoutDrawerLabelClickTogglesProductionState()
        {
            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

            FoldoutInteractionDictionaryHost host =
                CreateScriptableObject<FoldoutInteractionDictionaryHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
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

            Action draw = () =>
            {
                serializedObject.Update();
                drawer.OnGUI(new Rect(40f, 60f, 500f, 240f), property, GUIContent.none);
                serializedObject.ApplyModifiedProperties();
            };

            yield return TestIMGUIExecutor.Run(draw);

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.HasLastPendingHeaderRect,
                "Pending header rect should be recorded by the actual drawer OnGUI path."
            );

            Rect labelRect = SerializableDictionaryPropertyDrawer.LastPendingAbsoluteLabelHitRect;
            Assert.Greater(labelRect.width, 0f, "Dictionary label hit rect should have width.");
            AssertDictionaryPendingState(
                drawer,
                property,
                expectedExpanded: false,
                "before label click"
            );

            yield return TestIMGUIExecutor.RunMouseDown(draw, labelRect.center);

            AssertDictionaryPendingState(
                drawer,
                property,
                expectedExpanded: true,
                "after label click"
            );
        }

        [UnityTest]
        public IEnumerator SetPendingFoldoutDrawerLabelClickTogglesProductionState()
        {
            GroupGUIWidthUtility.ResetForTests();
            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

            FoldoutInteractionSetHost host = CreateScriptableObject<FoldoutInteractionSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
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

            Action draw = () =>
            {
                serializedObject.Update();
                drawer.OnGUI(new Rect(35f, 55f, 480f, 220f), property, GUIContent.none);
                serializedObject.ApplyModifiedProperties();
            };

            yield return TestIMGUIExecutor.Run(draw);

            Assert.IsTrue(
                SerializableSetPropertyDrawer.HasLastManualEntryHeaderRect,
                "Manual entry header rect should be recorded by the actual drawer OnGUI path."
            );

            Rect labelRect = BuildLabelRect(
                SerializableSetPropertyDrawer.LastManualEntryHeaderRect,
                SerializableSetPropertyDrawer.LastManualEntryToggleRect
            );
            AssertSetPendingState(drawer, property, expectedExpanded: false, "before label click");

            yield return TestIMGUIExecutor.RunMouseDown(draw, labelRect.center);

            AssertSetPendingState(drawer, property, expectedExpanded: true, "after label click");
        }

        private static Event CreateMouseDown(Vector2 mousePosition)
        {
            return new Event
            {
                type = EventType.MouseDown,
                mousePosition = mousePosition,
                button = 0,
            };
        }

        private static Rect BuildLabelRect(Rect headerRect, Rect toggleRect)
        {
            float labelWidth = Mathf.Max(0f, headerRect.xMax - toggleRect.xMax);
            return new Rect(toggleRect.xMax, headerRect.y, labelWidth, headerRect.height);
        }

        private static void AssertDictionaryPendingState(
            SerializableDictionaryPropertyDrawer drawer,
            SerializedProperty property,
            bool expectedExpanded,
            string phase
        )
        {
            Assert.IsTrue(
                drawer.TryGetPendingAnimationStateForTests(
                    property,
                    out bool isExpanded,
                    out float animProgress,
                    out bool hasAnimBool
                ),
                $"Expected dictionary pending state to exist {phase}."
            );
            Assert.AreEqual(
                expectedExpanded,
                isExpanded,
                $"Unexpected dictionary pending foldout state {phase}. "
                    + BuildDictionaryLayoutDiagnostics()
            );
            Assert.IsTrue(hasAnimBool, $"Expected dictionary pending AnimBool {phase}.");
            Assert.GreaterOrEqual(
                animProgress,
                0f,
                $"Expected non-negative dictionary animation progress {phase}."
            );
        }

        private static void AssertSetPendingState(
            SerializableSetPropertyDrawer drawer,
            SerializedProperty property,
            bool expectedExpanded,
            string phase
        )
        {
            Assert.IsTrue(
                drawer.TryGetPendingAnimationStateForTests(
                    property,
                    out bool isExpanded,
                    out float animProgress,
                    out bool hasAnimBool
                ),
                $"Expected set pending state to exist {phase}."
            );
            Assert.AreEqual(
                expectedExpanded,
                isExpanded,
                $"Unexpected set pending foldout state {phase}."
            );
            Assert.IsTrue(hasAnimBool, $"Expected set pending AnimBool {phase}.");
            Assert.GreaterOrEqual(
                animProgress,
                0f,
                $"Expected non-negative set animation progress {phase}."
            );
        }

        private static string BuildToggleFailureMessage(Vector2 mousePosition)
        {
            return $"Expected label click to toggle at mouse={mousePosition}. "
                + $"local={LocalLabelRect}, absolute={AbsoluteLabelRect}.";
        }

        private static string BuildExpandedFailureMessage(Vector2 mousePosition)
        {
            return $"Expected pending foldout to expand at mouse={mousePosition}. "
                + $"local={LocalLabelRect}, absolute={AbsoluteLabelRect}.";
        }

        private static string BuildDictionaryLayoutDiagnostics()
        {
            return "Recorded dictionary rects: "
                + $"header={SerializableDictionaryPropertyDrawer.LastPendingHeaderRect}, "
                + $"toggle={SerializableDictionaryPropertyDrawer.LastPendingFoldoutToggleRect}, "
                + $"localLabel={SerializableDictionaryPropertyDrawer.LastPendingLabelHitRect}, "
                + $"absoluteLabel={SerializableDictionaryPropertyDrawer.LastPendingAbsoluteLabelHitRect}.";
        }
    }
}
