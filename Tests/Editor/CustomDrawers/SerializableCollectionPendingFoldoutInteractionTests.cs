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
                drawer.TryGetPendingAnimationState(
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
                $"Unexpected dictionary pending foldout state {phase}."
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
                drawer.TryGetPendingAnimationState(
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

        private static string DescribeMouseEvent(Event mouseDown)
        {
            return $"type={mouseDown.type}, rawType={mouseDown.rawType}, button={mouseDown.button}, "
                + $"position={mouseDown.mousePosition}, localContains={LocalLabelRect.Contains(mouseDown.mousePosition)}, "
                + $"absoluteContains={AbsoluteLabelRect.Contains(mouseDown.mousePosition)}";
        }

        [TestCaseSource(nameof(DictionaryPendingFoldoutClickCases))]
        public void DictionaryPendingFoldoutLabelClickToggles(Vector2 mousePosition)
        {
            bool expanded = false;
            Event mouseDown = CreateMouseDown(mousePosition);
            string eventSnapshot = DescribeMouseEvent(mouseDown);

            bool toggled = SerializableDictionaryPropertyDrawer.TryTogglePendingFoldoutLabel(
                mouseDown,
                LocalLabelRect,
                AbsoluteLabelRect,
                ref expanded
            );

            Assert.IsTrue(
                toggled,
                BuildToggleFailureMessage(mousePosition) + " Event before call: " + eventSnapshot
            );
            Assert.IsTrue(
                expanded,
                BuildExpandedFailureMessage(mousePosition) + " Event before call: " + eventSnapshot
            );
        }

        [TestCaseSource(nameof(SetPendingFoldoutClickCases))]
        public void SetPendingFoldoutLabelClickToggles(Vector2 mousePosition)
        {
            bool expanded = false;
            Event mouseDown = CreateMouseDown(mousePosition);
            string eventSnapshot = DescribeMouseEvent(mouseDown);

            bool toggled = SerializableSetPropertyDrawer.TryToggleManualEntryFoldoutLabel(
                mouseDown,
                LocalLabelRect,
                ref expanded
            );

            Assert.IsTrue(
                toggled,
                BuildToggleFailureMessage(mousePosition) + " Event before call: " + eventSnapshot
            );
            Assert.IsTrue(
                expanded,
                BuildExpandedFailureMessage(mousePosition) + " Event before call: " + eventSnapshot
            );
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

            bool toggled = SerializableDictionaryPropertyDrawer.TryTogglePendingFoldoutLabel(
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

            bool toggled = SerializableSetPropertyDrawer.TryToggleManualEntryFoldoutLabel(
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
                SerializableDictionaryPropertyDrawer.GetEffectiveMouseEventType(
                    eventType,
                    rawEventType
                )
            );
            Assert.AreEqual(
                expectedEventType,
                SerializableSetPropertyDrawer.GetEffectiveMouseEventType(eventType, rawEventType)
            );
        }

        [UnityTest]
        public IEnumerator SetPendingFoldoutDrawerLabelClickTogglesProductionState()
        {
            GroupGUIWidthUtilityTestAccess.Reset();
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
            Rect position = new(35f, 55f, 480f, 220f);
            Action draw = () =>
            {
                serializedObject.Update();
                drawer.OnGUI(position, property, GUIContent.none);
                serializedObject.ApplyModifiedProperties();
            };
            yield return TestIMGUIExecutor.Run(draw);
            AssertSetPendingState(drawer, property, false, "before label click");
            Rect content = SerializableSetPropertyDrawer.ResolveContentRect(position);
            Vector2 mouse = new(
                content.center.x,
                content.y
                    + EditorGUIUtility.singleLineHeight
                    + SerializableSetPropertyDrawer.SectionSpacing
                    + SerializableSetPropertyDrawer.ResolveManualEntrySectionPadding(property)
                    + EditorGUIUtility.singleLineHeight * 0.5f
            );
            yield return TestIMGUIExecutor.RunMouseDown(draw, mouse);
            AssertSetPendingState(drawer, property, true, "after label click");
        }
    }
}
