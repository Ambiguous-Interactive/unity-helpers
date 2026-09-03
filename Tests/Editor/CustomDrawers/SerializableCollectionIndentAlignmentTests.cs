// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
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
    /// Tests for precise pixel-level indent alignment of SerializableDictionary and SerializableSet
    /// when rendered in various contexts (WGroup, SettingsProvider, nested groups, etc.).
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableCollectionIndentAlignmentTests : CommonTestBase
    {
        private const float PixelTolerance = 0.01f;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
        }

        [TearDown]
        public override void TearDown()
        {
            GroupGUIWidthUtility.ResetForTests();
            base.TearDown();
        }

        [Test]
        public void DictionaryAtIndentZeroWithoutWGroupAlignsWithUnityLists()
        {
            // x=18 is realistic Inspector positioning; at x=0 the -1.25f offset hits the xMin >= 0 clamp.
            Rect controlRect = new(18f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                float expectedX = controlRect.x - 1.25f;
                TestContext.WriteLine(
                    $"[DictionaryAtIndentZeroWithoutWGroupAlignsWithUnityLists] "
                        + $"input=({controlRect.x}, {controlRect.width}), "
                        + $"resolved=({resolvedRect.x}, {resolvedRect.width})"
                );
                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    PixelTolerance,
                    "Dictionary at indent=0 without WGroup should align with Unity's default list rendering (-1px alignment offset)."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetAtIndentZeroWithoutWGroupAlignsWithUnityLists()
        {
            // x=18 is realistic Inspector positioning; at x=0 the -1.25f offset hits the xMin >= 0 clamp.
            Rect controlRect = new(18f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                float expectedX = controlRect.x - 1.25f;
                TestContext.WriteLine(
                    $"[SetAtIndentZeroWithoutWGroupAlignsWithUnityLists] "
                        + $"input=({controlRect.x}, {controlRect.width}), "
                        + $"resolved=({resolvedRect.x}, {resolvedRect.width})"
                );
                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    PixelTolerance,
                    "Set at indent=0 without WGroup should align with Unity's default list rendering (-1px alignment offset)."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryInWGroupUsesOnlyWGroupPadding()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            float helpBoxPadding = GroupGUIWidthUtility.CalculateHorizontalPadding(
                EditorStyles.helpBox,
                out float leftPadding,
                out float rightPadding
            );

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        helpBoxPadding,
                        leftPadding,
                        rightPadding
                    )
                )
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    float expectedX = controlRect.x + leftPadding;
                    TestContext.WriteLine(
                        $"[DictionaryInWGroupUsesOnlyWGroupPadding] "
                            + $"leftPadding={leftPadding}, rightPadding={rightPadding}, "
                            + $"resolved=({resolvedRect.x}, {resolvedRect.width})"
                    );
                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        PixelTolerance,
                        "Dictionary in WGroup should use WGroup padding."
                    );

                    float expectedWidth = controlRect.width - leftPadding - rightPadding;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        PixelTolerance,
                        "Dictionary in WGroup should have width reduced by WGroup padding."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetInWGroupUsesOnlyWGroupPadding()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            float helpBoxPadding = GroupGUIWidthUtility.CalculateHorizontalPadding(
                EditorStyles.helpBox,
                out float leftPadding,
                out float rightPadding
            );

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        helpBoxPadding,
                        leftPadding,
                        rightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    float expectedX = controlRect.x + leftPadding;
                    TestContext.WriteLine(
                        $"[SetInWGroupUsesOnlyWGroupPadding] "
                            + $"leftPadding={leftPadding}, rightPadding={rightPadding}, "
                            + $"resolved=({resolvedRect.x}, {resolvedRect.width})"
                    );
                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        PixelTolerance,
                        "Set in WGroup should use WGroup padding."
                    );

                    float expectedWidth = controlRect.width - leftPadding - rightPadding;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        PixelTolerance,
                        "Set in WGroup should have width reduced by WGroup padding."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryNestedInMultipleWGroupsAccumulatesPadding()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

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
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        InnerLeftPadding + InnerRightPadding,
                        InnerLeftPadding,
                        InnerRightPadding
                    )
                )
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    float totalLeftPadding = OuterLeftPadding + InnerLeftPadding;
                    float totalRightPadding = OuterRightPadding + InnerRightPadding;

                    float expectedX = controlRect.x + totalLeftPadding;
                    TestContext.WriteLine(
                        $"[DictionaryNestedInMultipleWGroupsAccumulatesPadding] "
                            + $"totalLeft={totalLeftPadding}, totalRight={totalRightPadding}, "
                            + $"resolved=({resolvedRect.x}, {resolvedRect.width})"
                    );
                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        PixelTolerance,
                        "Dictionary in nested WGroups should accumulate left padding."
                    );

                    float expectedWidth = controlRect.width - totalLeftPadding - totalRightPadding;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        PixelTolerance,
                        "Dictionary in nested WGroups should accumulate total padding."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetNestedInMultipleWGroupsAccumulatesPadding()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

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
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        InnerLeftPadding + InnerRightPadding,
                        InnerLeftPadding,
                        InnerRightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    float totalLeftPadding = OuterLeftPadding + InnerLeftPadding;
                    float totalRightPadding = OuterRightPadding + InnerRightPadding;

                    float expectedX = controlRect.x + totalLeftPadding;
                    TestContext.WriteLine(
                        $"[SetNestedInMultipleWGroupsAccumulatesPadding] "
                            + $"totalLeft={totalLeftPadding}, totalRight={totalRightPadding}, "
                            + $"resolved=({resolvedRect.x}, {resolvedRect.width})"
                    );
                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        PixelTolerance,
                        "Set in nested WGroups should accumulate left padding."
                    );

                    float expectedWidth = controlRect.width - totalLeftPadding - totalRightPadding;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        PixelTolerance,
                        "Set in nested WGroups should accumulate total padding."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryWithNonZeroIndentAndNoWGroupUsesUnityIndent()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[DictionaryWithNonZeroIndentAndNoWGroupUsesUnityIndent] "
                        + $"indentLevel=2, resolved=({resolvedRect.x}, {resolvedRect.width})"
                );

                // With indent level > 0 and no WGroup, Unity's IndentedRect applies indentation
                Assert.Greater(
                    resolvedRect.x,
                    controlRect.x,
                    "Dictionary with indent > 0 should have positive x offset from IndentedRect."
                );

                // Indentation should be within reasonable bounds
                Assert.Less(
                    resolvedRect.x,
                    controlRect.x + 50f,
                    "Indentation should be reasonable (not excessive)."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetWithNonZeroIndentAndNoWGroupUsesUnityIndent()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[SetWithNonZeroIndentAndNoWGroupUsesUnityIndent] "
                        + $"indentLevel=2, resolved=({resolvedRect.x}, {resolvedRect.width})"
                );

                // With indent level > 0 and no WGroup, Unity's IndentedRect applies indentation
                Assert.Greater(
                    resolvedRect.x,
                    controlRect.x,
                    "Set with indent > 0 should have positive x offset from IndentedRect."
                );

                // Indentation should be within reasonable bounds
                Assert.Less(
                    resolvedRect.x,
                    controlRect.x + 50f,
                    "Indentation should be reasonable (not excessive)."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryWidthIsNeverNegativeWithExcessivePadding()
        {
            Rect controlRect = new(0f, 0f, 50f, 300f); // Narrow rect

            const float ExcessivePadding = 100f; // Larger than control rect width

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        ExcessivePadding * 2f,
                        ExcessivePadding,
                        ExcessivePadding
                    )
                )
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    TestContext.WriteLine(
                        $"[DictionaryWidthIsNeverNegativeWithExcessivePadding] "
                            + $"resolved=({resolvedRect.x}, {resolvedRect.width})"
                    );

                    Assert.GreaterOrEqual(
                        resolvedRect.width,
                        0f,
                        "Dictionary width should never be negative even with excessive padding."
                    );
                    Assert.IsFalse(
                        float.IsNaN(resolvedRect.width),
                        "Dictionary width should not be NaN."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetWidthIsNeverNegativeWithExcessivePadding()
        {
            Rect controlRect = new(0f, 0f, 50f, 300f); // Narrow rect

            const float ExcessivePadding = 100f; // Larger than control rect width

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        ExcessivePadding * 2f,
                        ExcessivePadding,
                        ExcessivePadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[SetWidthIsNeverNegativeWithExcessivePadding] "
                            + $"resolved=({resolvedRect.x}, {resolvedRect.width})"
                    );

                    Assert.GreaterOrEqual(
                        resolvedRect.width,
                        0f,
                        "Set width should never be negative even with excessive padding."
                    );
                    Assert.IsFalse(float.IsNaN(resolvedRect.width), "Set width should not be NaN.");
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [UnityTest]
        public IEnumerator DictionarySettingsContextAppliesOnlyWGroupPadding()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            // Find a SerializableDictionary property in settings
            SerializedProperty paletteProp = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            if (paletteProp == null)
            {
                Assert.Ignore("WButtonCustomColors property not found in settings.");
                yield break;
            }

            paletteProp.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Palette");

            const float TestLeftPadding = 15f;
            const float TestRightPadding = 15f;

            int previousIndentLevel = EditorGUI.indentLevel;
            Rect resolvedRect = default;

            yield return TestIMGUIExecutor.Run(() =>
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        TestLeftPadding + TestRightPadding,
                        TestLeftPadding,
                        TestRightPadding
                    )
                )
                {
                    try
                    {
                        // Settings context uses skipIndentation=true
                        drawer.OnGUI(controlRect, paletteProp, label);
                        resolvedRect = drawer.LastResolvedPosition;
                    }
                    finally
                    {
                        EditorGUI.indentLevel = previousIndentLevel;
                    }
                }
            });

            float expectedX = controlRect.x + TestLeftPadding;
            Assert.AreEqual(
                expectedX,
                resolvedRect.x,
                PixelTolerance,
                "Dictionary in settings context should apply only WGroup padding."
            );

            float expectedWidth = controlRect.width - TestLeftPadding - TestRightPadding;
            Assert.AreEqual(
                expectedWidth,
                resolvedRect.width,
                PixelTolerance,
                "Dictionary in settings context should reduce width by WGroup padding."
            );
        }

        [Test]
        public void GroupGUIWidthUtilityScopeDepthTracksCorrectly()
        {
            GroupGUIWidthUtility.ResetForTests();

            Assert.AreEqual(
                0,
                GroupGUIWidthUtility.CurrentScopeDepth,
                "Initial scope depth should be 0."
            );

            using (GroupGUIWidthUtility.PushContentPadding(10f, 5f, 5f))
            {
                Assert.AreEqual(
                    1,
                    GroupGUIWidthUtility.CurrentScopeDepth,
                    "Scope depth should be 1 after first push."
                );

                using (GroupGUIWidthUtility.PushContentPadding(8f, 4f, 4f))
                {
                    Assert.AreEqual(
                        2,
                        GroupGUIWidthUtility.CurrentScopeDepth,
                        "Scope depth should be 2 after second push."
                    );
                }

                Assert.AreEqual(
                    1,
                    GroupGUIWidthUtility.CurrentScopeDepth,
                    "Scope depth should be 1 after inner pop."
                );
            }

            Assert.AreEqual(
                0,
                GroupGUIWidthUtility.CurrentScopeDepth,
                "Scope depth should be 0 after outer pop."
            );
        }

        [Test]
        public void GroupGUIWidthUtilityPaddingAccumulatesCorrectly()
        {
            GroupGUIWidthUtility.ResetForTests();

            Assert.AreEqual(
                0f,
                GroupGUIWidthUtility.CurrentLeftPadding,
                PixelTolerance,
                "Initial left padding should be 0."
            );
            Assert.AreEqual(
                0f,
                GroupGUIWidthUtility.CurrentRightPadding,
                PixelTolerance,
                "Initial right padding should be 0."
            );

            const float Left1 = 5f;
            const float Right1 = 7f;
            const float Left2 = 3f;
            const float Right2 = 4f;

            using (GroupGUIWidthUtility.PushContentPadding(Left1 + Right1, Left1, Right1))
            {
                Assert.AreEqual(
                    Left1,
                    GroupGUIWidthUtility.CurrentLeftPadding,
                    PixelTolerance,
                    "Left padding after first push."
                );
                Assert.AreEqual(
                    Right1,
                    GroupGUIWidthUtility.CurrentRightPadding,
                    PixelTolerance,
                    "Right padding after first push."
                );

                using (GroupGUIWidthUtility.PushContentPadding(Left2 + Right2, Left2, Right2))
                {
                    Assert.AreEqual(
                        Left1 + Left2,
                        GroupGUIWidthUtility.CurrentLeftPadding,
                        PixelTolerance,
                        "Accumulated left padding."
                    );
                    Assert.AreEqual(
                        Right1 + Right2,
                        GroupGUIWidthUtility.CurrentRightPadding,
                        PixelTolerance,
                        "Accumulated right padding."
                    );
                }

                Assert.AreEqual(
                    Left1,
                    GroupGUIWidthUtility.CurrentLeftPadding,
                    PixelTolerance,
                    "Left padding restored after inner pop."
                );
                Assert.AreEqual(
                    Right1,
                    GroupGUIWidthUtility.CurrentRightPadding,
                    PixelTolerance,
                    "Right padding restored after inner pop."
                );
            }

            Assert.AreEqual(
                0f,
                GroupGUIWidthUtility.CurrentLeftPadding,
                PixelTolerance,
                "Left padding reset after outer pop."
            );
            Assert.AreEqual(
                0f,
                GroupGUIWidthUtility.CurrentRightPadding,
                PixelTolerance,
                "Right padding reset after outer pop."
            );
        }

        [Test]
        public void ApplyCurrentPaddingReturnsOriginalRectWhenNoPadding()
        {
            GroupGUIWidthUtility.ResetForTests();

            Rect original = new(10f, 20f, 300f, 100f);
            Rect result = GroupGUIWidthUtility.ApplyCurrentPadding(original);

            Assert.AreEqual(
                original.x,
                result.x,
                PixelTolerance,
                "X should be unchanged when no padding."
            );
            Assert.AreEqual(original.y, result.y, PixelTolerance, "Y should be unchanged.");
            Assert.AreEqual(
                original.width,
                result.width,
                PixelTolerance,
                "Width should be unchanged when no padding."
            );
            Assert.AreEqual(
                original.height,
                result.height,
                PixelTolerance,
                "Height should be unchanged."
            );
        }

        [Test]
        public void ApplyCurrentPaddingAdjustsRectWithPadding()
        {
            GroupGUIWidthUtility.ResetForTests();

            const float LeftPadding = 12f;
            const float RightPadding = 8f;

            Rect original = new(10f, 20f, 300f, 100f);

            using (
                GroupGUIWidthUtility.PushContentPadding(
                    LeftPadding + RightPadding,
                    LeftPadding,
                    RightPadding
                )
            )
            {
                Rect result = GroupGUIWidthUtility.ApplyCurrentPadding(original);

                Assert.AreEqual(
                    original.x + LeftPadding,
                    result.x,
                    PixelTolerance,
                    "X should be offset by left padding."
                );
                Assert.AreEqual(original.y, result.y, PixelTolerance, "Y should be unchanged.");
                Assert.AreEqual(
                    original.width - LeftPadding - RightPadding,
                    result.width,
                    PixelTolerance,
                    "Width should be reduced by total padding."
                );
                Assert.AreEqual(
                    original.height,
                    result.height,
                    PixelTolerance,
                    "Height should be unchanged."
                );
            }
        }

        [UnityTest]
        public IEnumerator DictionaryIndentRestoredAfterOnGUI()
        {
            IndentAlignmentSimpleDictionaryHost host =
                CreateScriptableObject<IndentAlignmentSimpleDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IndentAlignmentSimpleDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Dictionary");

            int previousIndentLevel = EditorGUI.indentLevel;
            int indentAfterOnGUI = -1;

            yield return TestIMGUIExecutor.Run(() =>
            {
                EditorGUI.indentLevel = 3;
                GroupGUIWidthUtility.ResetForTests();

                try
                {
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
                    indentAfterOnGUI = EditorGUI.indentLevel;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.AreEqual(
                3,
                indentAfterOnGUI,
                "Indent level should be restored after OnGUI completes."
            );
        }

        [UnityTest]
        public IEnumerator SetIndentRestoredAfterOnGUI()
        {
            IndentAlignmentSimpleSetHost host =
                CreateScriptableObject<IndentAlignmentSimpleSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IndentAlignmentSimpleSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            int indentAfterOnGUI = -1;

            yield return TestIMGUIExecutor.Run(() =>
            {
                EditorGUI.indentLevel = 3;
                GroupGUIWidthUtility.ResetForTests();

                try
                {
                    drawer.OnGUI(controlRect, setProperty, label);
                    indentAfterOnGUI = EditorGUI.indentLevel;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.AreEqual(
                3,
                indentAfterOnGUI,
                "Indent level should be restored after OnGUI completes."
            );
        }

        [Test]
        public void ZeroPaddingPushDoesNotIncreaseScopeDepth()
        {
            GroupGUIWidthUtility.ResetForTests();

            Assert.AreEqual(
                0,
                GroupGUIWidthUtility.CurrentScopeDepth,
                "Initial scope depth should be 0."
            );

            using (GroupGUIWidthUtility.PushContentPadding(0f, 0f, 0f))
            {
                Assert.AreEqual(
                    0,
                    GroupGUIWidthUtility.CurrentScopeDepth,
                    "Zero padding should not increase scope depth."
                );
            }

            Assert.AreEqual(
                0,
                GroupGUIWidthUtility.CurrentScopeDepth,
                "Scope depth should remain 0 after zero padding pop."
            );
        }

        [Test]
        public void NegativePaddingIsClamped()
        {
            GroupGUIWidthUtility.ResetForTests();

            using (GroupGUIWidthUtility.PushContentPadding(-10f, -5f, -5f))
            {
                Assert.GreaterOrEqual(
                    GroupGUIWidthUtility.CurrentLeftPadding,
                    0f,
                    "Left padding should be clamped to >= 0."
                );
                Assert.GreaterOrEqual(
                    GroupGUIWidthUtility.CurrentRightPadding,
                    0f,
                    "Right padding should be clamped to >= 0."
                );
            }
        }

        /// <summary>
        /// Data-driven test for WGroupPropertyContext alignment offset with various starting x values.
        /// Tests both Dictionary and Set to ensure consistent behavior.
        /// </summary>
        [TestCase(
            4f,
            400f,
            0,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X4.Width400.Indent0"
        )]
        [TestCase(
            8f,
            400f,
            0,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X8.Width400.Indent0"
        )]
        [TestCase(
            12f,
            400f,
            0,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X12.Width400.Indent0"
        )]
        [TestCase(
            20f,
            400f,
            0,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X20.Width400.Indent0"
        )]
        [TestCase(
            50f,
            400f,
            0,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X50.Width400.Indent0"
        )]
        [TestCase(
            0f,
            400f,
            0,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X0.Width400.Indent0"
        )]
        [TestCase(
            100f,
            400f,
            0,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X100.Width400.Indent0"
        )]
        [TestCase(
            4f,
            400f,
            1,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X4.Width400.Indent1"
        )]
        [TestCase(
            4f,
            400f,
            2,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X4.Width400.Indent2"
        )]
        [TestCase(
            4f,
            400f,
            5,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X4.Width400.Indent5"
        )]
        [TestCase(
            20f,
            200f,
            3,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X20.Width200.Indent3"
        )]
        [TestCase(
            50f,
            600f,
            4,
            TestName = "CollectionWGroupPropertyContextAlignmentOffset.X50.Width600.Indent4"
        )]
        public void WGroupPropertyContextAlignmentOffsetBothCollections(
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
                    Rect dictResolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    Rect setResolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextAlignmentOffsetBothCollections] "
                            + $"startX={startX:F3}, width={width:F3}, indentLevel={indentLevel}, "
                            + $"dict.x={dictResolvedRect.x:F3}, set.x={setResolvedRect.x:F3}, "
                            + $"dict.width={dictResolvedRect.width:F3}, set.width={setResolvedRect.width:F3}"
                    );

                    // Dictionary assertions
                    Assert.AreEqual(
                        expectedX,
                        dictResolvedRect.x,
                        PixelTolerance,
                        $"Dictionary: WGroupPropertyContext should apply -4f offset to x={startX}."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        dictResolvedRect.width,
                        PixelTolerance,
                        $"Dictionary: WGroupPropertyContext should increase width by 4f."
                    );

                    // Set assertions
                    Assert.AreEqual(
                        expectedX,
                        setResolvedRect.x,
                        PixelTolerance,
                        $"Set: WGroupPropertyContext should apply -4f offset to x={startX}."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        setResolvedRect.width,
                        PixelTolerance,
                        $"Set: WGroupPropertyContext should increase width by 4f."
                    );

                    // Both should be identical
                    Assert.AreEqual(
                        dictResolvedRect.x,
                        setResolvedRect.x,
                        PixelTolerance,
                        "Dictionary and Set should produce identical x in WGroupPropertyContext."
                    );
                    Assert.AreEqual(
                        dictResolvedRect.width,
                        setResolvedRect.width,
                        PixelTolerance,
                        "Dictionary and Set should produce identical width in WGroupPropertyContext."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with very small widths for both Dictionary and Set.
        /// </summary>
        [TestCase(1f, TestName = "CollectionWGroupPropertyContextSmallWidth.1")]
        [TestCase(2f, TestName = "CollectionWGroupPropertyContextSmallWidth.2")]
        [TestCase(5f, TestName = "CollectionWGroupPropertyContextSmallWidth.5")]
        [TestCase(10f, TestName = "CollectionWGroupPropertyContextSmallWidth.10")]
        public void WGroupPropertyContextSmallWidthBothCollections(float smallWidth)
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
                    Rect dictResolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    Rect setResolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextSmallWidthBothCollections] "
                            + $"smallWidth={smallWidth:F3}, "
                            + $"dict.width={dictResolvedRect.width:F3}, set.width={setResolvedRect.width:F3}"
                    );

                    // Dictionary assertions
                    Assert.AreEqual(
                        expectedWidth,
                        dictResolvedRect.width,
                        PixelTolerance,
                        $"Dictionary: Small width {smallWidth} should become {expectedWidth}."
                    );
                    Assert.IsFalse(
                        float.IsNaN(dictResolvedRect.width),
                        "Dictionary: Resolved width should not be NaN."
                    );

                    // Set assertions
                    Assert.AreEqual(
                        expectedWidth,
                        setResolvedRect.width,
                        PixelTolerance,
                        $"Set: Small width {smallWidth} should become {expectedWidth}."
                    );
                    Assert.IsFalse(
                        float.IsNaN(setResolvedRect.width),
                        "Set: Resolved width should not be NaN."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with very large widths for both Dictionary and Set.
        /// </summary>
        [TestCase(1000f, TestName = "CollectionWGroupPropertyContextLargeWidth.1000")]
        [TestCase(2000f, TestName = "CollectionWGroupPropertyContextLargeWidth.2000")]
        [TestCase(5000f, TestName = "CollectionWGroupPropertyContextLargeWidth.5000")]
        [TestCase(10000f, TestName = "CollectionWGroupPropertyContextLargeWidth.10000")]
        public void WGroupPropertyContextLargeWidthBothCollections(float largeWidth)
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
                    Rect dictResolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    Rect setResolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextLargeWidthBothCollections] "
                            + $"largeWidth={largeWidth:F3}, "
                            + $"dict.width={dictResolvedRect.width:F3}, set.width={setResolvedRect.width:F3}"
                    );

                    // Dictionary assertions
                    Assert.AreEqual(
                        expectedWidth,
                        dictResolvedRect.width,
                        PixelTolerance,
                        $"Dictionary: Large width {largeWidth} should become {expectedWidth}."
                    );
                    Assert.IsFalse(
                        float.IsInfinity(dictResolvedRect.width),
                        "Dictionary: Resolved width should not be infinite."
                    );

                    // Set assertions
                    Assert.AreEqual(
                        expectedWidth,
                        setResolvedRect.width,
                        PixelTolerance,
                        $"Set: Large width {largeWidth} should become {expectedWidth}."
                    );
                    Assert.IsFalse(
                        float.IsInfinity(setResolvedRect.width),
                        "Set: Resolved width should not be infinite."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext negative x boundary for both Dictionary and Set.
        /// </summary>
        [Test]
        public void WGroupPropertyContextNegativeXBoundaryBothCollections()
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
                    Rect dictResolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    Rect setResolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextNegativeXBoundaryBothCollections] "
                            + $"dict.x={dictResolvedRect.x:F3}, set.x={setResolvedRect.x:F3}, "
                            + $"expectedX={expectedX:F3}"
                    );

                    // Both should allow negative x
                    Assert.AreEqual(
                        expectedX,
                        dictResolvedRect.x,
                        PixelTolerance,
                        "Dictionary: Should allow negative x after -4f offset."
                    );
                    Assert.AreEqual(
                        expectedX,
                        setResolvedRect.x,
                        PixelTolerance,
                        "Set: Should allow negative x after -4f offset."
                    );

                    // Width should be increased
                    Assert.AreEqual(
                        expectedWidth,
                        dictResolvedRect.width,
                        PixelTolerance,
                        "Dictionary: Width should be increased by 4f."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        setResolvedRect.width,
                        PixelTolerance,
                        "Set: Width should be increased by 4f."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with x=4 specifically (offset cancels out to zero).
        /// </summary>
        [Test]
        public void WGroupPropertyContextXEqualsOffsetBothCollections()
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
                    Rect dictResolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    Rect setResolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextXEqualsOffsetBothCollections] "
                            + $"dict.x={dictResolvedRect.x:F3}, set.x={setResolvedRect.x:F3}, "
                            + $"expectedX={expectedX:F3} (x=4 - 4 = 0)"
                    );

                    Assert.AreEqual(
                        expectedX,
                        dictResolvedRect.x,
                        PixelTolerance,
                        "Dictionary: x=4 should result in x=0 after -4f offset."
                    );
                    Assert.AreEqual(
                        expectedX,
                        setResolvedRect.x,
                        PixelTolerance,
                        "Set: x=4 should result in x=0 after -4f offset."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        dictResolvedRect.width,
                        PixelTolerance,
                        "Dictionary: Width should be 404f."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        setResolvedRect.width,
                        PixelTolerance,
                        "Set: Width should be 404f."
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
