// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;

    /// <summary>
    /// Tests for ensuring correct indentation behavior of SerializableSet property drawer
    /// in various contexts (normal inspector vs SettingsProvider).
    /// Note: UnityHelpersSettings does not have SerializableHashSet properties, so settings
    /// context behavior is tested using mock scenarios and dictionary tests serve as the
    /// canonical reference for settings provider behavior.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableSetIndentationTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            GroupGUIWidthUtility.ResetForTests();
            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
        }

        [Test]
        public void ResolveContentRectNormalContextAppliesIndentation()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[ResolveContentRectNormalContextAppliesIndentation] "
                        + $"controlRect.x={controlRect.x:F3}, resolvedRect.x={resolvedRect.x:F3}, "
                        + $"indentLevel={EditorGUI.indentLevel}"
                );

                Assert.Greater(
                    resolvedRect.x,
                    controlRect.x,
                    "ResolvedPosition.x should be greater than controlRect.x when indentLevel > 0 in normal context."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void ResolveContentRectNormalContextZeroIndentAlignsWithUnityListsClampedAtZero()
        {
            // When controlRect.x starts at 0, the alignment offset (-1.25f) would produce a negative x,
            // which is clamped to 0 to prevent off-screen rendering.
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                float rawExpectedX = controlRect.x - 1.25f;
                float expectedX = 0f; // Clamped from -1.25f to 0f
                TestContext.WriteLine(
                    $"[ResolveContentRectNormalContextZeroIndentAlignsWithUnityListsClampedAtZero] "
                        + $"controlRect.x={controlRect.x:F3}, resolvedRect.x={resolvedRect.x:F3}, "
                        + $"rawExpected={rawExpectedX:F3}, clampedExpected={expectedX:F3}"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.01f,
                    $"ResolvedPosition.x should align with Unity's default list rendering but clamp to 0. "
                        + $"Raw expected x={rawExpectedX}, Clamped expected x={expectedX}, Actual x={resolvedRect.x}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void ResolveContentRectNormalContextZeroIndentAlignsWithUnityListsWithPositiveX()
        {
            // With a positive starting x, the alignment offset (-1.25f) is applied without clamping.
            Rect controlRect = new(10f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                float expectedX = controlRect.x - 1.25f; // 10f - 1.25f = 8.75f
                TestContext.WriteLine(
                    $"[ResolveContentRectNormalContextZeroIndentAlignsWithUnityListsWithPositiveX] "
                        + $"controlRect.x={controlRect.x:F3}, resolvedRect.x={resolvedRect.x:F3}, "
                        + $"expected={expectedX:F3}"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.01f,
                    $"ResolvedPosition.x should align with Unity's default list rendering (-1.25f alignment offset). "
                        + $"Expected x={expectedX}, Actual x={resolvedRect.x}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [UnityTest]
        public IEnumerator OnGUINormalContextRespectsIndentLevel()
        {
            IndentationTestSetHost host = CreateScriptableObject<IndentationTestSetHost>();
            host.set.Add(1);
            host.set.Add(2);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IndentationTestSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            Rect capturedRect = default;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                EditorGUI.indentLevel = 2;
                try
                {
                    serializedObject.UpdateIfRequiredOrScript();
                    drawer.OnGUI(controlRect, setProperty, label);
                    capturedRect = drawer.LastResolvedPosition;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.Greater(
                capturedRect.x,
                controlRect.x,
                "OnGUI should respect indentLevel in normal context and indent the content."
            );
        }

        [Test]
        public void NormalContextWithNestedIndentLevelStaysConsistent()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 1;
                GroupGUIWidthUtility.ResetForTests();
                Rect rectAtLevel1 = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                EditorGUI.indentLevel = 3;
                Rect rectAtLevel3 = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[NormalContextWithNestedIndentLevelStaysConsistent] "
                        + $"rectAtLevel1.x={rectAtLevel1.x:F3}, rectAtLevel3.x={rectAtLevel3.x:F3}, "
                        + $"rectAtLevel1.width={rectAtLevel1.width:F3}, rectAtLevel3.width={rectAtLevel3.width:F3}"
                );

                Assert.Greater(
                    rectAtLevel3.x,
                    rectAtLevel1.x,
                    "Higher indentLevel should result in larger x offset."
                );
                Assert.Less(
                    rectAtLevel3.width,
                    rectAtLevel1.width,
                    "Higher indentLevel should result in smaller width."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void NormalContextReducesWidthWithIndentation()
        {
            Rect controlRect = new(0f, 0f, 500f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 3;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[NormalContextReducesWidthWithIndentation] "
                        + $"controlRect.width={controlRect.width:F3}, resolvedRect.width={resolvedRect.width:F3}"
                );

                Assert.Less(
                    resolvedRect.width,
                    controlRect.width,
                    "Width should be reduced in normal context due to indentation."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void TargetsUnityHelpersSettingsReturnsFalseForRegularScriptableObject()
        {
            IndentationTestSetHost host = CreateScriptableObject<IndentationTestSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));

            bool targetsSettings =
                serializedObject.targetObject is UnityHelpersSettings
                || Array.Exists(serializedObject.targetObjects, t => t is UnityHelpersSettings);

            Assert.IsFalse(
                targetsSettings,
                "Regular ScriptableObject should not be detected as UnityHelpersSettings."
            );
        }

        [Test]
        public void TargetsUnityHelpersSettingsReturnsTrueForSettings()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));

            bool targetsSettings =
                serializedSettings.targetObject is UnityHelpersSettings
                || Array.Exists(serializedSettings.targetObjects, t => t is UnityHelpersSettings);

            Assert.IsTrue(targetsSettings, "UnityHelpersSettings should be correctly detected.");
        }

        [Test]
        public void DrawerDoesNotThrowWhenSetIsEmpty()
        {
            IndentationTestSetHost host = CreateScriptableObject<IndentationTestSetHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IndentationTestSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            Assert.DoesNotThrow(
                () => drawer.GetPropertyHeight(setProperty, label),
                "GetPropertyHeight should not throw for empty set."
            );

            GroupGUIWidthUtility.ResetForTests();
            Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                controlRect,
                skipIndentation: false
            );

            TestContext.WriteLine(
                $"[DrawerDoesNotThrowWhenSetIsEmpty] "
                    + $"controlRect.width={controlRect.width:F3}, resolvedRect.width={resolvedRect.width:F3}"
            );

            Assert.Greater(
                resolvedRect.width,
                0f,
                "Resolved position should have valid width for empty set."
            );
        }

        [Test]
        public void DrawerHandlesVeryLargeIndentLevel()
        {
            IndentationTestSetHost host = CreateScriptableObject<IndentationTestSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IndentationTestSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 600f, 300f);
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 20;
                GroupGUIWidthUtility.ResetForTests();

                Assert.DoesNotThrow(
                    () => drawer.GetPropertyHeight(setProperty, label),
                    "GetPropertyHeight should not throw for very large indentLevel."
                );

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[DrawerHandlesVeryLargeIndentLevel] "
                        + $"controlRect.width={controlRect.width:F3}, resolvedRect.width={resolvedRect.width:F3}, "
                        + $"indentLevel={EditorGUI.indentLevel}"
                );

                Assert.GreaterOrEqual(
                    resolvedRect.width,
                    0f,
                    "Width should not be negative even with very large indentLevel."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void WGroupContextZeroIndentSkipsMinimumIndentWhenPaddingApplied()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float GroupLeftPadding = 12f;
            const float GroupRightPadding = 8f;
            float horizontalPadding = GroupLeftPadding + GroupRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        GroupLeftPadding,
                        GroupRightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    float expectedX = controlRect.x + GroupLeftPadding;

                    TestContext.WriteLine(
                        $"[WGroupContextZeroIndentSkipsMinimumIndentWhenPaddingApplied] "
                            + $"controlRect.x={controlRect.x:F3}, resolvedRect.x={resolvedRect.x:F3}, "
                            + $"expectedX={expectedX:F3}, GroupLeftPadding={GroupLeftPadding:F3}"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.01f,
                        "When inside WGroup (padding applied) at indentLevel 0, should use WGroup left padding."
                    );

                    float expectedWidth = controlRect.width - horizontalPadding;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.01f,
                        "Width should account for WGroup padding."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void WGroupContextWithIndentLevelAppliesIndentWithoutDoubleOffset()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float GroupLeftPadding = 10f;
            const float GroupRightPadding = 10f;
            float horizontalPadding = GroupLeftPadding + GroupRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        GroupLeftPadding,
                        GroupRightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupContextWithIndentLevelAppliesIndentWithoutDoubleOffset] "
                            + $"controlRect.x={controlRect.x:F3}, resolvedRect.x={resolvedRect.x:F3}, "
                            + $"controlRect.width={controlRect.width:F3}, resolvedRect.width={resolvedRect.width:F3}, "
                            + $"GroupLeftPadding={GroupLeftPadding:F3}, indentLevel={EditorGUI.indentLevel}"
                    );

                    Assert.Greater(
                        resolvedRect.x,
                        controlRect.x + GroupLeftPadding,
                        "With indentLevel > 0 inside WGroup, position should account for both WGroup padding and indent."
                    );

                    Assert.Less(
                        resolvedRect.width,
                        controlRect.width - horizontalPadding,
                        "Width should be reduced by both WGroup padding and indentation."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void WGroupContextVsNoGroupContextIndentationDifference()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                Rect noGroupRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                const float GroupLeftPadding = 8f;
                const float GroupRightPadding = 8f;
                float horizontalPadding = GroupLeftPadding + GroupRightPadding;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        GroupLeftPadding,
                        GroupRightPadding
                    )
                )
                {
                    Rect withGroupRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    float expectedNoGroupX = controlRect.x;
                    float expectedWithGroupX = controlRect.x + GroupLeftPadding;

                    TestContext.WriteLine(
                        $"[WGroupContextVsNoGroupContextIndentationDifference] "
                            + $"noGroupRect.x={noGroupRect.x:F3} (expected={expectedNoGroupX:F3}), "
                            + $"withGroupRect.x={withGroupRect.x:F3} (expected={expectedWithGroupX:F3})"
                    );

                    Assert.AreEqual(
                        expectedNoGroupX,
                        noGroupRect.x,
                        0.01f,
                        "Without WGroup at indentLevel 0, should align with Unity's default list rendering (no offset)."
                    );

                    Assert.AreEqual(
                        expectedWithGroupX,
                        withGroupRect.x,
                        0.01f,
                        "With WGroup at indentLevel 0, WGroup padding should be applied."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void NestedWGroupContextAccumulatesPaddingCorrectly()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float OuterLeftPadding = 8f;
            const float OuterRightPadding = 8f;
            const float InnerLeftPadding = 6f;
            const float InnerRightPadding = 6f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        OuterLeftPadding + OuterRightPadding,
                        OuterLeftPadding,
                        OuterRightPadding
                    )
                )
                {
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            InnerLeftPadding + InnerRightPadding,
                            InnerLeftPadding,
                            InnerRightPadding
                        )
                    )
                    {
                        Rect resolvedRect =
                            SerializableSetPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );

                        float expectedTotalLeftPadding = OuterLeftPadding + InnerLeftPadding;
                        float expectedTotalRightPadding = OuterRightPadding + InnerRightPadding;
                        float expectedX = controlRect.x + expectedTotalLeftPadding;
                        float expectedWidth =
                            controlRect.width
                            - expectedTotalLeftPadding
                            - expectedTotalRightPadding;

                        TestContext.WriteLine(
                            $"[NestedWGroupContextAccumulatesPaddingCorrectly] "
                                + $"resolvedRect.x={resolvedRect.x:F3} (expected={expectedX:F3}), "
                                + $"resolvedRect.width={resolvedRect.width:F3} (expected={expectedWidth:F3})"
                        );

                        Assert.AreEqual(
                            expectedX,
                            resolvedRect.x,
                            0.01f,
                            "Nested WGroups should accumulate left padding correctly."
                        );

                        Assert.AreEqual(
                            expectedWidth,
                            resolvedRect.width,
                            0.01f,
                            "Nested WGroups should accumulate total horizontal padding correctly."
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [UnityTest]
        public IEnumerator OnGUIInWGroupContextSkipsMinimumIndentAtZeroLevel()
        {
            IndentationTestSetHost host = CreateScriptableObject<IndentationTestSetHost>();
            host.set.Add(1);
            host.set.Add(2);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IndentationTestSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            const float GroupLeftPadding = 15f;
            const float GroupRightPadding = 10f;
            float horizontalPadding = GroupLeftPadding + GroupRightPadding;

            Rect capturedRect = default;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                EditorGUI.indentLevel = 0;
                try
                {
                    serializedObject.UpdateIfRequiredOrScript();
                    GroupGUIWidthUtility.ResetForTests();
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            GroupLeftPadding,
                            GroupRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, setProperty, label);
                        capturedRect = drawer.LastResolvedPosition;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            float expectedX = controlRect.x + GroupLeftPadding;
            Assert.AreEqual(
                expectedX,
                capturedRect.x,
                0.01f,
                "OnGUI in WGroup context at indentLevel 0 should use WGroup padding."
            );
        }

        [Test]
        public void SettingsContextSkipIndentationScenarioPreservesRectUnchanged()
        {
            Rect controlRect = new(15f, 25f, 450f, 280f);

            const float GroupLeftPadding = 12f;
            const float GroupRightPadding = 8f;
            float horizontalPadding = GroupLeftPadding + GroupRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        GroupLeftPadding,
                        GroupRightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[SettingsContextSkipIndentationScenarioPreservesRectUnchanged] "
                            + $"controlRect.x={controlRect.x:F3}, resolvedRect.x={resolvedRect.x:F3}, "
                            + $"controlRect.width={controlRect.width:F3}, resolvedRect.width={resolvedRect.width:F3}"
                    );

                    Assert.Greater(
                        resolvedRect.x,
                        controlRect.x,
                        "Normal context should modify x position based on padding and indent."
                    );

                    Assert.Less(
                        resolvedRect.width,
                        controlRect.width,
                        "Normal context should reduce width based on padding."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void NormalContextWithGroupPaddingAppliesPaddingAndIndent()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float GroupLeftPadding = 10f;
            const float GroupRightPadding = 10f;
            float horizontalPadding = GroupLeftPadding + GroupRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 1;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        GroupLeftPadding,
                        GroupRightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[NormalContextWithGroupPaddingAppliesPaddingAndIndent] "
                            + $"resolvedRect.x={resolvedRect.x:F3}, expected > {controlRect.x + GroupLeftPadding:F3}, "
                            + $"resolvedRect.width={resolvedRect.width:F3}, expected < {controlRect.width - horizontalPadding:F3}"
                    );

                    Assert.Greater(
                        resolvedRect.x,
                        controlRect.x + GroupLeftPadding,
                        "Normal context should apply both WGroup padding and indent offset."
                    );

                    Assert.Less(
                        resolvedRect.width,
                        controlRect.width - horizontalPadding,
                        "Normal context should reduce width by both padding and indent."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void NormalContextZeroIndentWithGroupPaddingSkipsMinimumIndent()
        {
            Rect controlRect = new(0f, 0f, 350f, 250f);

            const float GroupLeftPadding = 8f;
            const float GroupRightPadding = 8f;
            float horizontalPadding = GroupLeftPadding + GroupRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        GroupLeftPadding,
                        GroupRightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    float expectedX = controlRect.x + GroupLeftPadding;
                    float expectedWidth = controlRect.width - horizontalPadding;

                    TestContext.WriteLine(
                        $"[NormalContextZeroIndentWithGroupPaddingSkipsMinimumIndent] "
                            + $"resolvedRect.x={resolvedRect.x:F3} (expected={expectedX:F3}), "
                            + $"resolvedRect.width={resolvedRect.width:F3} (expected={expectedWidth:F3})"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.01f,
                        "With WGroup padding at indentLevel 0, should use WGroup padding."
                    );

                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.01f,
                        "Width should account for WGroup padding only at indentLevel 0."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [TestCase(0, 0f, 0f, 0f, TestName = "NoGroupNoIndentAlignsWithUnityLists")]
        [TestCase(0, 10f, 5f, 10f, TestName = "WithGroupNoIndentUsesGroupPadding")]
        // Note: Unity's EditorGUI.IndentedRect returns ~13.75f at indent level 1; exact value may vary by Unity version.
        [TestCase(1, 0f, 0f, 13f, TestName = "NoGroupWithIndentAppliesUnityIndent")]
        [TestCase(2, 8f, 4f, 8f, TestName = "WithGroupWithIndentCombinesBoth")]
        public void ResolveContentRectDataDrivenLeftPadding(
            int indentLevel,
            float groupLeftPadding,
            float groupRightPadding,
            float minimumExpectedXShift
        )
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);
            float horizontalPadding = groupLeftPadding + groupRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect;
                if (0f < horizontalPadding)
                {
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            groupLeftPadding,
                            groupRightPadding
                        )
                    )
                    {
                        resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );
                    }
                }
                else
                {
                    resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );
                }

                TestContext.WriteLine(
                    $"[ResolveContentRectDataDrivenLeftPadding] "
                        + $"indentLevel={indentLevel}, groupLeftPadding={groupLeftPadding:F3}, "
                        + $"resolvedRect.x={resolvedRect.x:F3}, minimumExpectedXShift={minimumExpectedXShift:F3}"
                );

                Assert.GreaterOrEqual(
                    resolvedRect.x,
                    controlRect.x + minimumExpectedXShift - 0.01f,
                    $"X position should be at least {minimumExpectedXShift} pixels right of control origin."
                );

                if (indentLevel == 0 && horizontalPadding <= 0f)
                {
                    // When controlRect.x starts at 0, the alignment offset (-1.25f) would produce a negative x,
                    // which is clamped to 0 to prevent off-screen rendering.
                    float rawExpectedX = controlRect.x - 1.25f;
                    float expectedX = rawExpectedX < 0f ? 0f : rawExpectedX;
                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.01f,
                        $"At indentLevel 0 with no WGroup, should align with Unity's default list rendering (clamped to 0 if negative). "
                            + $"Raw expected x={rawExpectedX}, Clamped expected x={expectedX}, Actual x={resolvedRect.x}"
                    );
                }
                else if (indentLevel == 0 && 0f < horizontalPadding)
                {
                    Assert.AreEqual(
                        controlRect.x + groupLeftPadding,
                        resolvedRect.x,
                        0.01f,
                        "At indentLevel 0 with WGroup, WGroup padding should apply."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [TestCase(
            400f,
            0f,
            0f,
            0,
            ExpectedResult = true,
            TestName = "NoGroupNoIndentPreservesWidth"
        )]
        [TestCase(400f, 20f, 10f, 0, ExpectedResult = true, TestName = "WithGroupReducesWidth")]
        [TestCase(400f, 0f, 0f, 3, ExpectedResult = true, TestName = "WithIndentReducesWidth")]
        [TestCase(
            400f,
            15f,
            5f,
            2,
            ExpectedResult = true,
            TestName = "GroupAndIndentBothReduceWidth"
        )]
        public bool ResolveContentRectDataDrivenWidthReduction(
            float controlWidth,
            float groupLeftPadding,
            float groupRightPadding,
            int indentLevel
        )
        {
            Rect controlRect = new(0f, 0f, controlWidth, 300f);
            float horizontalPadding = groupLeftPadding + groupRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect;
                if (0f < horizontalPadding)
                {
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            groupLeftPadding,
                            groupRightPadding
                        )
                    )
                    {
                        resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );
                    }
                }
                else
                {
                    resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );
                }

                TestContext.WriteLine(
                    $"[ResolveContentRectDataDrivenWidthReduction] "
                        + $"controlWidth={controlWidth:F3}, horizontalPadding={horizontalPadding:F3}, "
                        + $"indentLevel={indentLevel}, resolvedRect.width={resolvedRect.width:F3}"
                );

                bool widthReduced =
                    (0f < horizontalPadding || 0 < indentLevel)
                    && resolvedRect.width < controlWidth;
                bool widthPreserved =
                    horizontalPadding <= 0f
                    && indentLevel == 0
                    && resolvedRect.width <= controlWidth;

                return widthReduced || widthPreserved;
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that when IsInsideWGroupPropertyDraw is true, ResolveContentRect applies
        /// a -4f alignment offset to x and increases width by 4f for visual alignment.
        /// </summary>
        [Test]
        public void WGroupPropertyContextAppliesAlignmentOffset()
        {
            Rect controlRect = new(25f, 50f, 400f, 300f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextAppliesAlignmentOffset] "
                            + $"controlRect=({controlRect.x:F3}, {controlRect.y:F3}, {controlRect.width:F3}, {controlRect.height:F3}), "
                            + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.y:F3}, {resolvedRect.width:F3}, {resolvedRect.height:F3})"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        "WGroupPropertyContext should apply -4f alignment offset to x."
                    );
                    Assert.AreEqual(
                        controlRect.y,
                        resolvedRect.y,
                        0.001f,
                        "WGroupPropertyContext should return position.y unchanged."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        "WGroupPropertyContext should increase width by 4f."
                    );
                    Assert.AreEqual(
                        controlRect.height,
                        resolvedRect.height,
                        0.001f,
                        "WGroupPropertyContext should return position.height unchanged."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with various indent levels - only -4f alignment offset is applied, indent is ignored.
        /// </summary>
        [TestCase(0, TestName = "WGroupPropertyContextWithIndentLevel0")]
        [TestCase(1, TestName = "WGroupPropertyContextWithIndentLevel1")]
        [TestCase(2, TestName = "WGroupPropertyContextWithIndentLevel2")]
        [TestCase(5, TestName = "WGroupPropertyContextWithIndentLevel5")]
        [TestCase(10, TestName = "WGroupPropertyContextWithIndentLevel10")]
        public void WGroupPropertyContextIgnoresIndentLevel(int indentLevel)
        {
            Rect controlRect = new(15f, 30f, 450f, 250f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextIgnoresIndentLevel] "
                            + $"indentLevel={indentLevel}, "
                            + $"controlRect.x={controlRect.x:F3}, resolvedRect.x={resolvedRect.x:F3}"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        $"WGroupPropertyContext should ignore indentLevel {indentLevel} and apply only -4f alignment offset."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        $"WGroupPropertyContext should ignore indentLevel {indentLevel} and increase width by 4f."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that even with padding values set, only the -4f alignment offset is applied in WGroup property context.
        /// This is the key distinction: PushWGroupPropertyContext means Unity's layout already applied padding.
        /// </summary>
        [Test]
        public void WGroupPropertyContextIgnoresPaddingValues()
        {
            Rect controlRect = new(20f, 40f, 500f, 300f);

            const float GroupLeftPadding = 15f;
            const float GroupRightPadding = 10f;
            float horizontalPadding = GroupLeftPadding + GroupRightPadding;

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 1;
                GroupGUIWidthUtility.ResetForTests();

                // Push padding, then push WGroupPropertyContext
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        GroupLeftPadding,
                        GroupRightPadding
                    )
                )
                {
                    using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                    {
                        Rect resolvedRect =
                            SerializableSetPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );

                        TestContext.WriteLine(
                            $"[WGroupPropertyContextIgnoresPaddingValues] "
                                + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                                + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3}), "
                                + $"padding=({GroupLeftPadding:F3}, {GroupRightPadding:F3})"
                        );

                        // Despite padding being set, only the -4f alignment offset is applied because
                        // Unity's layout system already applied the padding
                        Assert.AreEqual(
                            expectedX,
                            resolvedRect.x,
                            0.001f,
                            "WGroupPropertyContext should apply -4f alignment offset even with padding set."
                        );
                        Assert.AreEqual(
                            expectedWidth,
                            resolvedRect.width,
                            0.001f,
                            "WGroupPropertyContext should increase width by 4f even with padding set."
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroup property context with zero padding and zero indent.
        /// WGroupPropertyContext applies a -4f alignment offset to align with other WGroup content.
        /// </summary>
        [Test]
        public void WGroupPropertyContextZeroPaddingZeroIndent()
        {
            // Use a positive starting x to demonstrate the offset clearly
            // (In practice, WGroupPropertyContext is used when Unity's layout has already positioned the rect,
            // so negative x values after offset wouldn't occur in real usage)
            Rect controlRect = new(20f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    // WGroupPropertyContext applies WGroupAlignmentOffset (-4f) to align with other WGroup content.
                    // This shifts xMin left by 4f and increases width by 4f.
                    const float WGroupAlignmentOffset = -4f;
                    float expectedX = controlRect.x + WGroupAlignmentOffset;
                    float expectedWidth = controlRect.width - WGroupAlignmentOffset;

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextZeroPaddingZeroIndent] "
                            + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                            + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3}), "
                            + $"expectedX={expectedX:F3}, expectedWidth={expectedWidth:F3}"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        "WGroupPropertyContext should apply -4f alignment offset to x."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        "WGroupPropertyContext should increase width by 4f to compensate for x offset."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with maximum padding values.
        /// </summary>
        [Test]
        public void WGroupPropertyContextWithMaxPaddingValues()
        {
            Rect controlRect = new(100f, 50f, 600f, 400f);

            const float MaxLeftPadding = 100f;
            const float MaxRightPadding = 100f;
            float horizontalPadding = MaxLeftPadding + MaxRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 5;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        MaxLeftPadding,
                        MaxRightPadding
                    )
                )
                {
                    using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                    {
                        Rect resolvedRect =
                            SerializableSetPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );

                        // WGroupPropertyContext applies WGroupAlignmentOffset (-4f) to align with other WGroup content.
                        // This shifts xMin left by 4f and increases width by 4f.
                        const float WGroupAlignmentOffset = -4f;
                        float expectedX = controlRect.x + WGroupAlignmentOffset;
                        float expectedWidth = controlRect.width - WGroupAlignmentOffset;

                        TestContext.WriteLine(
                            $"[WGroupPropertyContextWithMaxPaddingValues] "
                                + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                                + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3}), "
                                + $"expectedX={expectedX:F3}, expectedWidth={expectedWidth:F3}, "
                                + $"maxPadding=({MaxLeftPadding:F3}, {MaxRightPadding:F3})"
                        );

                        Assert.AreEqual(
                            expectedX,
                            resolvedRect.x,
                            0.001f,
                            "WGroupPropertyContext should apply -4f alignment offset to x."
                        );
                        Assert.AreEqual(
                            expectedWidth,
                            resolvedRect.width,
                            0.001f,
                            "WGroupPropertyContext should increase width by 4f (subtract negative offset)."
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with very high indent level - only -4f alignment offset is applied.
        /// </summary>
        [Test]
        public void WGroupPropertyContextWithVeryHighIndentLevel()
        {
            Rect controlRect = new(50f, 25f, 800f, 500f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 50;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextWithVeryHighIndentLevel] "
                            + $"indentLevel=50, "
                            + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                            + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3})"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        "WGroupPropertyContext should apply -4f alignment offset even with very high indent."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        "WGroupPropertyContext should increase width by 4f even with very high indent."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests nested WGroupPropertyContext scopes - only -4f alignment offset is applied.
        /// </summary>
        [Test]
        public void WGroupPropertyContextNestedScopesApplyAlignmentOffset()
        {
            Rect controlRect = new(30f, 60f, 350f, 200f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 3;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                    {
                        Rect resolvedRect =
                            SerializableSetPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );

                        TestContext.WriteLine(
                            $"[WGroupPropertyContextNestedScopesApplyAlignmentOffset] "
                                + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                                + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3})"
                        );

                        Assert.AreEqual(
                            expectedX,
                            resolvedRect.x,
                            0.001f,
                            "Nested WGroupPropertyContext should apply -4f alignment offset."
                        );
                        Assert.AreEqual(
                            expectedWidth,
                            resolvedRect.width,
                            0.001f,
                            "Nested WGroupPropertyContext should increase width by 4f."
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that WGroupPropertyContext correctly restores state after disposal.
        /// </summary>
        [Test]
        public void WGroupPropertyContextRestoredAfterDisposal()
        {
            Rect controlRect = new(10f, 0f, 400f, 300f);

            const float WGroupAlignmentOffset = -4f;
            float expectedXWithContext = controlRect.x + WGroupAlignmentOffset;
            float expectedWidthWithContext = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;
                GroupGUIWidthUtility.ResetForTests();

                // First, resolve without WGroupPropertyContext
                Rect rectWithoutContext = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // Then with WGroupPropertyContext
                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect rectWithContext = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    Assert.AreEqual(
                        expectedXWithContext,
                        rectWithContext.x,
                        0.001f,
                        "Inside WGroupPropertyContext, x should have -4f alignment offset applied."
                    );
                    Assert.AreEqual(
                        expectedWidthWithContext,
                        rectWithContext.width,
                        0.001f,
                        "Inside WGroupPropertyContext, width should be increased by 4f."
                    );
                }

                // After disposal, should be back to normal behavior
                Rect rectAfterContext = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[WGroupPropertyContextRestoredAfterDisposal] "
                        + $"rectWithoutContext.x={rectWithoutContext.x:F3}, "
                        + $"rectAfterContext.x={rectAfterContext.x:F3}"
                );

                Assert.AreEqual(
                    rectWithoutContext.x,
                    rectAfterContext.x,
                    0.001f,
                    "After WGroupPropertyContext disposal, behavior should be restored."
                );
                Assert.AreEqual(
                    rectWithoutContext.width,
                    rectAfterContext.width,
                    0.001f,
                    "After WGroupPropertyContext disposal, width behavior should be restored."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that padding is correctly applied when not in WGroup property context.
        /// This is the case where CurrentScopeDepth > 0 but IsInsideWGroupPropertyDraw = false.
        /// </summary>
        [Test]
        public void PushContentPaddingOnlyAppliesPadding()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float LeftPadding = 12f;
            const float RightPadding = 8f;
            float horizontalPadding = LeftPadding + RightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                // Push padding but NOT WGroupPropertyContext
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[PushContentPaddingOnlyAppliesPadding] "
                            + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                            + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3}), "
                            + $"expectedX={controlRect.x + LeftPadding:F3}"
                    );

                    // Without WGroupPropertyContext, padding should be manually applied
                    Assert.AreEqual(
                        controlRect.x + LeftPadding,
                        resolvedRect.x,
                        0.01f,
                        "Without WGroupPropertyContext, left padding should be manually applied."
                    );
                    Assert.AreEqual(
                        controlRect.width - horizontalPadding,
                        resolvedRect.width,
                        0.01f,
                        "Without WGroupPropertyContext, horizontal padding should reduce width."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests nested padding accumulation without WGroupPropertyContext.
        /// </summary>
        [Test]
        public void PushContentPaddingNestedAccumulation()
        {
            Rect controlRect = new(0f, 0f, 500f, 300f);

            const float OuterLeftPadding = 10f;
            const float OuterRightPadding = 10f;
            const float InnerLeftPadding = 8f;
            const float InnerRightPadding = 8f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        OuterLeftPadding + OuterRightPadding,
                        OuterLeftPadding,
                        OuterRightPadding
                    )
                )
                {
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            InnerLeftPadding + InnerRightPadding,
                            InnerLeftPadding,
                            InnerRightPadding
                        )
                    )
                    {
                        Rect resolvedRect =
                            SerializableSetPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );

                        float totalLeftPadding = OuterLeftPadding + InnerLeftPadding;
                        float totalRightPadding = OuterRightPadding + InnerRightPadding;
                        float totalHorizontalPadding = totalLeftPadding + totalRightPadding;

                        TestContext.WriteLine(
                            $"[PushContentPaddingNestedAccumulation] "
                                + $"resolvedRect.x={resolvedRect.x:F3} (expected={totalLeftPadding:F3}), "
                                + $"resolvedRect.width={resolvedRect.width:F3} (expected={controlRect.width - totalHorizontalPadding:F3})"
                        );

                        Assert.AreEqual(
                            controlRect.x + totalLeftPadding,
                            resolvedRect.x,
                            0.01f,
                            "Nested padding should accumulate left padding correctly."
                        );
                        Assert.AreEqual(
                            controlRect.width - totalHorizontalPadding,
                            resolvedRect.width,
                            0.01f,
                            "Nested padding should accumulate total horizontal padding correctly."
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests combined padding and indent levels without WGroupPropertyContext.
        /// </summary>
        [Test]
        public void PushContentPaddingCombinedWithIndentLevel()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float LeftPadding = 10f;
            const float RightPadding = 10f;
            float horizontalPadding = LeftPadding + RightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[PushContentPaddingCombinedWithIndentLevel] "
                            + $"resolvedRect.x={resolvedRect.x:F3}, "
                            + $"resolvedRect.width={resolvedRect.width:F3}, "
                            + $"minExpectedX={controlRect.x + LeftPadding:F3}"
                    );

                    // Should apply both padding AND indent
                    Assert.Greater(
                        resolvedRect.x,
                        controlRect.x + LeftPadding,
                        "Should apply both padding and indent offset."
                    );
                    Assert.Less(
                        resolvedRect.width,
                        controlRect.width - horizontalPadding,
                        "Should reduce width by both padding and indent."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that zero padding does not increase scope depth.
        /// </summary>
        [Test]
        public void PushContentPaddingZeroPaddingDoesNotIncreaseScopeDepth()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                int initialScopeDepth = GroupGUIWidthUtility.CurrentScopeDepth;

                // Push zero padding
                using (GroupGUIWidthUtility.PushContentPadding(0f, 0f, 0f))
                {
                    int scopeDepthWithZeroPadding = GroupGUIWidthUtility.CurrentScopeDepth;

                    TestContext.WriteLine(
                        $"[PushContentPaddingZeroPaddingDoesNotIncreaseScopeDepth] "
                            + $"initialScopeDepth={initialScopeDepth}, "
                            + $"scopeDepthWithZeroPadding={scopeDepthWithZeroPadding}"
                    );

                    Assert.AreEqual(
                        initialScopeDepth,
                        scopeDepthWithZeroPadding,
                        "Zero padding should not increase scope depth."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests contrast between WGroupPropertyContext (returns unchanged) vs
        /// PushContentPadding only (applies padding manually).
        /// </summary>
        [Test]
        public void ContrastWGroupPropertyContextVsPushContentPaddingOnly()
        {
            Rect controlRect = new(10f, 20f, 400f, 300f);

            const float LeftPadding = 15f;
            const float RightPadding = 10f;
            float horizontalPadding = LeftPadding + RightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 1;
                GroupGUIWidthUtility.ResetForTests();

                // Case 1: PushContentPadding only (no WGroupPropertyContext)
                Rect rectWithPaddingOnly;
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    rectWithPaddingOnly = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );
                }

                GroupGUIWidthUtility.ResetForTests();

                // Case 2: PushContentPadding with WGroupPropertyContext
                Rect rectWithWGroupContext;
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                    {
                        rectWithWGroupContext =
                            SerializableSetPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );
                    }
                }

                // WGroupPropertyContext applies WGroupAlignmentOffset (-4f) to align with other WGroup content.
                // This shifts xMin left by 4f and increases width by 4f.
                const float WGroupAlignmentOffset = -4f;
                float expectedWGroupX = controlRect.x + WGroupAlignmentOffset;
                float expectedWGroupWidth = controlRect.width - WGroupAlignmentOffset;

                TestContext.WriteLine(
                    $"[ContrastWGroupPropertyContextVsPushContentPaddingOnly] "
                        + $"paddingOnly=({rectWithPaddingOnly.x:F3}, {rectWithPaddingOnly.width:F3}), "
                        + $"withWGroupContext=({rectWithWGroupContext.x:F3}, {rectWithWGroupContext.width:F3}), "
                        + $"expectedWGroupX={expectedWGroupX:F3}, expectedWGroupWidth={expectedWGroupWidth:F3}"
                );

                // PushContentPadding only should apply padding
                Assert.Greater(
                    rectWithPaddingOnly.x,
                    controlRect.x,
                    "PushContentPadding only should shift x position."
                );
                Assert.Less(
                    rectWithPaddingOnly.width,
                    controlRect.width,
                    "PushContentPadding only should reduce width."
                );

                // WGroupPropertyContext applies -4f alignment offset to align with other WGroup content
                Assert.AreEqual(
                    expectedWGroupX,
                    rectWithWGroupContext.x,
                    0.001f,
                    "WGroupPropertyContext should apply -4f alignment offset to x."
                );
                Assert.AreEqual(
                    expectedWGroupWidth,
                    rectWithWGroupContext.width,
                    0.001f,
                    "WGroupPropertyContext should increase width by 4f (subtract negative offset)."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with a rect starting at x=4 results in x=0 after -4f offset.
        /// </summary>
        [Test]
        public void WGroupPropertyContextWithRectAtOrigin()
        {
            // Start at x=4 so after -4f offset we get x=0
            Rect controlRect = new(4f, 0f, 300f, 200f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset; // 4 + (-4) = 0
            float expectedWidth = controlRect.width - WGroupAlignmentOffset; // 300 - (-4) = 304

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextWithRectAtOrigin] "
                            + $"controlRect=({controlRect.x:F3}, {controlRect.y:F3}), "
                            + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.y:F3})"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        "WGroupPropertyContext should apply -4f offset resulting in x=0."
                    );
                    Assert.AreEqual(
                        0f,
                        resolvedRect.y,
                        0.001f,
                        "WGroupPropertyContext should preserve y=0."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        "WGroupPropertyContext should increase width by 4f."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with a very narrow rect - still applies -4f alignment offset.
        /// </summary>
        [Test]
        public void WGroupPropertyContextWithNarrowRect()
        {
            Rect controlRect = new(10f, 10f, 50f, 300f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 3;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushContentPadding(40f, 20f, 20f))
                {
                    using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                    {
                        Rect resolvedRect =
                            SerializableSetPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );

                        TestContext.WriteLine(
                            $"[WGroupPropertyContextWithNarrowRect] "
                                + $"controlRect.width={controlRect.width:F3}, "
                                + $"resolvedRect.width={resolvedRect.width:F3}"
                        );

                        Assert.AreEqual(
                            expectedX,
                            resolvedRect.x,
                            0.001f,
                            "WGroupPropertyContext should apply -4f alignment offset even for narrow rect."
                        );
                        Assert.AreEqual(
                            expectedWidth,
                            resolvedRect.width,
                            0.001f,
                            "WGroupPropertyContext should increase width by 4f even for narrow rect."
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests triple-nested padding scopes without WGroupPropertyContext.
        /// </summary>
        [Test]
        public void TripleNestedPaddingScopesWithoutWGroupContext()
        {
            Rect controlRect = new(0f, 0f, 600f, 300f);

            const float Padding1Left = 10f;
            const float Padding1Right = 10f;
            const float Padding2Left = 8f;
            const float Padding2Right = 8f;
            const float Padding3Left = 6f;
            const float Padding3Right = 6f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        Padding1Left + Padding1Right,
                        Padding1Left,
                        Padding1Right
                    )
                )
                {
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            Padding2Left + Padding2Right,
                            Padding2Left,
                            Padding2Right
                        )
                    )
                    {
                        using (
                            GroupGUIWidthUtility.PushContentPadding(
                                Padding3Left + Padding3Right,
                                Padding3Left,
                                Padding3Right
                            )
                        )
                        {
                            Rect resolvedRect =
                                SerializableSetPropertyDrawer.ResolveContentRectForTests(
                                    controlRect,
                                    skipIndentation: false
                                );

                            float totalLeft = Padding1Left + Padding2Left + Padding3Left;
                            float totalRight = Padding1Right + Padding2Right + Padding3Right;
                            float totalHorizontal = totalLeft + totalRight;

                            TestContext.WriteLine(
                                $"[TripleNestedPaddingScopesWithoutWGroupContext] "
                                    + $"totalLeft={totalLeft:F3}, totalRight={totalRight:F3}, "
                                    + $"resolvedRect.x={resolvedRect.x:F3}, resolvedRect.width={resolvedRect.width:F3}"
                            );

                            Assert.AreEqual(
                                controlRect.x + totalLeft,
                                resolvedRect.x,
                                0.01f,
                                "Triple-nested padding should accumulate left padding."
                            );
                            Assert.AreEqual(
                                controlRect.width - totalHorizontal,
                                resolvedRect.width,
                                0.01f,
                                "Triple-nested padding should accumulate horizontal padding."
                            );
                        }
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests asymmetric padding (different left and right values) without WGroupPropertyContext.
        /// </summary>
        [Test]
        public void AsymmetricPaddingWithoutWGroupContext()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float LeftPadding = 25f;
            const float RightPadding = 5f;
            float horizontalPadding = LeftPadding + RightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[AsymmetricPaddingWithoutWGroupContext] "
                            + $"leftPadding={LeftPadding:F3}, rightPadding={RightPadding:F3}, "
                            + $"resolvedRect.x={resolvedRect.x:F3}, resolvedRect.width={resolvedRect.width:F3}"
                    );

                    Assert.AreEqual(
                        controlRect.x + LeftPadding,
                        resolvedRect.x,
                        0.01f,
                        "Asymmetric padding should apply left padding correctly."
                    );
                    Assert.AreEqual(
                        controlRect.width - horizontalPadding,
                        resolvedRect.width,
                        0.01f,
                        "Asymmetric padding should apply total horizontal padding to width."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Data-driven test for WGroupPropertyContext alignment offset with various starting x values.
        /// Tests that the -4f offset is consistently applied regardless of starting x position.
        /// </summary>
        [TestCase(
            4f,
            400f,
            0,
            TestName = "WGroupPropertyContextAlignmentOffset.X4.Width400.Indent0"
        )]
        [TestCase(
            8f,
            400f,
            0,
            TestName = "WGroupPropertyContextAlignmentOffset.X8.Width400.Indent0"
        )]
        [TestCase(
            12f,
            400f,
            0,
            TestName = "WGroupPropertyContextAlignmentOffset.X12.Width400.Indent0"
        )]
        [TestCase(
            20f,
            400f,
            0,
            TestName = "WGroupPropertyContextAlignmentOffset.X20.Width400.Indent0"
        )]
        [TestCase(
            50f,
            400f,
            0,
            TestName = "WGroupPropertyContextAlignmentOffset.X50.Width400.Indent0"
        )]
        [TestCase(
            0f,
            400f,
            0,
            TestName = "WGroupPropertyContextAlignmentOffset.X0.Width400.Indent0"
        )]
        [TestCase(
            100f,
            400f,
            0,
            TestName = "WGroupPropertyContextAlignmentOffset.X100.Width400.Indent0"
        )]
        [TestCase(
            4f,
            400f,
            1,
            TestName = "WGroupPropertyContextAlignmentOffset.X4.Width400.Indent1"
        )]
        [TestCase(
            4f,
            400f,
            2,
            TestName = "WGroupPropertyContextAlignmentOffset.X4.Width400.Indent2"
        )]
        [TestCase(
            4f,
            400f,
            5,
            TestName = "WGroupPropertyContextAlignmentOffset.X4.Width400.Indent5"
        )]
        [TestCase(
            20f,
            200f,
            3,
            TestName = "WGroupPropertyContextAlignmentOffset.X20.Width200.Indent3"
        )]
        [TestCase(
            50f,
            600f,
            4,
            TestName = "WGroupPropertyContextAlignmentOffset.X50.Width600.Indent4"
        )]
        public void WGroupPropertyContextAlignmentOffsetDataDriven(
            float startX,
            float width,
            int indentLevel
        )
        {
            Rect controlRect = new(startX, 0f, width, 300f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextAlignmentOffsetDataDriven] "
                            + $"startX={startX:F3}, width={width:F3}, indentLevel={indentLevel}, "
                            + $"expectedX={expectedX:F3}, resolvedRect.x={resolvedRect.x:F3}, "
                            + $"expectedWidth={expectedWidth:F3}, resolvedRect.width={resolvedRect.width:F3}"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        $"WGroupPropertyContext should apply -4f offset: x={startX} should become {expectedX}."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        $"WGroupPropertyContext should increase width by 4f: {width} should become {expectedWidth}."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with very small widths to ensure width increases by 4f without issues.
        /// </summary>
        [TestCase(1f, TestName = "WGroupPropertyContextSmallWidth.1")]
        [TestCase(2f, TestName = "WGroupPropertyContextSmallWidth.2")]
        [TestCase(5f, TestName = "WGroupPropertyContextSmallWidth.5")]
        [TestCase(10f, TestName = "WGroupPropertyContextSmallWidth.10")]
        public void WGroupPropertyContextSmallWidthHandling(float smallWidth)
        {
            Rect controlRect = new(20f, 0f, smallWidth, 300f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextSmallWidthHandling] "
                            + $"smallWidth={smallWidth:F3}, expectedWidth={expectedWidth:F3}, "
                            + $"resolvedRect.width={resolvedRect.width:F3}"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        $"WGroupPropertyContext should apply -4f offset even with small width {smallWidth}."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        $"WGroupPropertyContext should increase small width {smallWidth} to {expectedWidth}."
                    );
                    Assert.IsFalse(
                        float.IsNaN(resolvedRect.width),
                        "Resolved width should not be NaN."
                    );
                    Assert.IsFalse(
                        float.IsInfinity(resolvedRect.width),
                        "Resolved width should not be infinite."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with very large rects to ensure no overflow issues.
        /// </summary>
        [TestCase(1000f, TestName = "WGroupPropertyContextLargeWidth.1000")]
        [TestCase(2000f, TestName = "WGroupPropertyContextLargeWidth.2000")]
        [TestCase(5000f, TestName = "WGroupPropertyContextLargeWidth.5000")]
        [TestCase(10000f, TestName = "WGroupPropertyContextLargeWidth.10000")]
        public void WGroupPropertyContextLargeWidthHandling(float largeWidth)
        {
            Rect controlRect = new(100f, 0f, largeWidth, 300f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextLargeWidthHandling] "
                            + $"largeWidth={largeWidth:F3}, expectedWidth={expectedWidth:F3}, "
                            + $"resolvedRect.width={resolvedRect.width:F3}"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        $"WGroupPropertyContext should apply -4f offset with large width {largeWidth}."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        $"WGroupPropertyContext should correctly handle large width {largeWidth}."
                    );
                    Assert.IsFalse(
                        float.IsNaN(resolvedRect.width),
                        "Resolved width should not be NaN."
                    );
                    Assert.IsFalse(
                        float.IsInfinity(resolvedRect.width),
                        "Resolved width should not be infinite."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with negative x boundary after offset is applied.
        /// When x=0 and offset is -4f, result is x=-4f (no clamping in WGroupPropertyContext).
        /// </summary>
        [Test]
        public void WGroupPropertyContextNegativeXBoundary()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset; // 0 + (-4) = -4
            float expectedWidth = controlRect.width - WGroupAlignmentOffset; // 400 - (-4) = 404

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextNegativeXBoundary] "
                            + $"controlRect.x={controlRect.x:F3}, resolvedRect.x={resolvedRect.x:F3}, "
                            + $"expectedX={expectedX:F3} (negative x is allowed in WGroupPropertyContext)"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        "WGroupPropertyContext should allow negative x values after -4f offset."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        "WGroupPropertyContext should increase width by 4f."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with x=4 specifically to verify offset results in x=0.
        /// </summary>
        [Test]
        public void WGroupPropertyContextXEqualsOffset()
        {
            Rect controlRect = new(4f, 0f, 400f, 300f);

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset; // 4 + (-4) = 0
            float expectedWidth = controlRect.width - WGroupAlignmentOffset; // 400 - (-4) = 404

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextXEqualsOffset] "
                            + $"controlRect.x={controlRect.x:F3}, resolvedRect.x={resolvedRect.x:F3}, "
                            + $"expectedX={expectedX:F3} (x=4 - 4 = 0)"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        "WGroupPropertyContext with x=4 should result in x=0 after -4f offset."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        "WGroupPropertyContext should increase width by 4f."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }
    }
}
