// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System.Collections;
    using System.Collections.Generic;
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
    /// Tests for ensuring correct indentation behavior of SerializableDictionary and
    /// SerializableSet property drawers in various contexts (normal inspector vs SettingsProvider).
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableDictionaryIndentationTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
            SerializableDictionaryPropertyDrawer.ClearMainFoldoutAnimCacheForTests();
        }

        [Test]
        public void ResolveContentRectNormalContextAppliesIndentation()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;

                // Test ResolveContentRect directly with skipIndentation=false (normal context)
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
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
        public void ResolveContentRectSettingsContextSkipsExtraIndentation()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;

                // Test ResolveContentRect with skipIndentation=true (settings context)
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: true
                );

                Assert.AreEqual(
                    controlRect.x,
                    resolvedRect.x,
                    0.01f,
                    "ResolvedPosition.x should match controlRect.x when targeting settings context (skip extra indentation)."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that at indentLevel 0 outside a WGroup, the UnityListAlignmentOffset (-1.25f)
        /// is applied, but clamped to 0 when the rect starts at x=0.
        /// </summary>
        [Test]
        public void ResolveContentRectNormalContextZeroIndentAppliesAlignmentOffset()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                // Test ResolveContentRect directly with skipIndentation=false (normal context)
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // Outside a WGroup the -1.25f offset would go negative, so production clamps xMin to 0.
                float expectedX = 0f;
                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.01f,
                    "ResolvedPosition.x should be clamped to 0 when alignment offset would make it negative."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void ResolveContentRectSettingsContextZeroIndentNoMinimumIndent()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                // Test ResolveContentRect with skipIndentation=true (settings context)
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: true
                );

                Assert.AreEqual(
                    controlRect.x,
                    resolvedRect.x,
                    0.01f,
                    "ResolvedPosition.x should match controlRect.x when targeting settings context even at indentLevel 0."
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
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            host.dictionary[1] = "value1";
            host.dictionary[2] = "value2";

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Dictionary");

            Rect capturedRect = default;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                EditorGUI.indentLevel = 2;
                try
                {
                    serializedObject.UpdateIfRequiredOrScript();
                    drawer.OnGUI(controlRect, dictionaryProperty, label);
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

        [UnityTest]
        public IEnumerator OnGUISettingsContextSkipsExtraIndentation()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            SerializedProperty paletteProp = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            Assert.IsTrue(
                paletteProp != null,
                "Settings should have the WButtonCustomColors dictionary property."
            );

            if (paletteProp == null)
            {
                yield break;
            }

            paletteProp.isExpanded = true;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Palette");

            Rect capturedRect = default;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                EditorGUI.indentLevel = 2;
                try
                {
                    serializedSettings.UpdateIfRequiredOrScript();
                    drawer.OnGUI(controlRect, paletteProp, label);
                    capturedRect = drawer.LastResolvedPosition;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.AreEqual(
                controlRect.x,
                capturedRect.x,
                0.01f,
                "OnGUI should skip extra indentation when targeting settings context."
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
                Rect rectAtLevel1 = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                EditorGUI.indentLevel = 3;
                Rect rectAtLevel3 = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
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
        public void SettingsContextIgnoresAllIndentLevels()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                Rect rectAtLevel0 = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: true
                );

                EditorGUI.indentLevel = 1;
                Rect rectAtLevel1 = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: true
                );

                EditorGUI.indentLevel = 3;
                Rect rectAtLevel3 = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: true
                );

                Assert.AreEqual(
                    rectAtLevel0.x,
                    rectAtLevel1.x,
                    0.01f,
                    "Settings context should have same x position regardless of indentLevel."
                );
                Assert.AreEqual(
                    rectAtLevel0.x,
                    rectAtLevel3.x,
                    0.01f,
                    "Settings context should have same x position regardless of indentLevel."
                );
                Assert.AreEqual(
                    rectAtLevel0.width,
                    rectAtLevel1.width,
                    0.01f,
                    "Settings context should have same width regardless of indentLevel."
                );
                Assert.AreEqual(
                    rectAtLevel0.width,
                    rectAtLevel3.width,
                    0.01f,
                    "Settings context should have same width regardless of indentLevel."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void WidthIsPreservedInSettingsContext()
        {
            Rect controlRect = new(0f, 0f, 500f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 5;

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: true
                );

                Assert.AreEqual(
                    controlRect.width,
                    resolvedRect.width,
                    0.01f,
                    "Width should be preserved in settings context (no indent reduction)."
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

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
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

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        GroupLeftPadding,
                        GroupRightPadding
                    )
                )
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    float expectedX = controlRect.x + GroupLeftPadding;
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

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        GroupLeftPadding,
                        GroupRightPadding
                    )
                )
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
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
                Rect noGroupRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                    Rect withGroupRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    float expectedNoGroupX = controlRect.x;
                    float expectedWithGroupX = controlRect.x + GroupLeftPadding;

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
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
            TestDictionaryHost host = CreateScriptableObject<TestDictionaryHost>();
            host.dictionary[1] = "value1";
            host.dictionary[2] = "value2";

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(TestDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Dictionary");

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
                        drawer.OnGUI(controlRect, dictionaryProperty, label);
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
        public void SettingsContextAppliesWGroupPaddingButIgnoresIndentation()
        {
            Rect controlRect = new(10f, 20f, 400f, 300f);

            const float GroupLeftPadding = 15f;
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
                    // Unity's layout does not apply our WGroup padding, so it still applies under skipIndentation.
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: true
                        );

                    // WGroup padding should be applied
                    Assert.AreEqual(
                        controlRect.x + GroupLeftPadding,
                        resolvedRect.x,
                        0.01f,
                        "Settings context should apply WGroup left padding."
                    );

                    Assert.AreEqual(
                        controlRect.width - horizontalPadding,
                        resolvedRect.width,
                        0.01f,
                        "Settings context should apply WGroup horizontal padding to width."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [UnityTest]
        public IEnumerator OnGUISettingsContextAppliesWGroupPaddingButIgnoresIndentation()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            SerializedProperty paletteProp = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            Assert.IsTrue(
                paletteProp != null,
                "Settings should have the WButtonCustomColors dictionary property."
            );

            if (paletteProp == null)
            {
                yield break;
            }

            paletteProp.isExpanded = true;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(5f, 10f, 500f, 300f);
            GUIContent label = new("Palette");

            const float GroupLeftPadding = 20f;
            const float GroupRightPadding = 15f;
            float horizontalPadding = GroupLeftPadding + GroupRightPadding;

            Rect capturedRect = default;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                EditorGUI.indentLevel = 3;
                try
                {
                    serializedSettings.UpdateIfRequiredOrScript();
                    GroupGUIWidthUtility.ResetForTests();
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            GroupLeftPadding,
                            GroupRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, paletteProp, label);
                        capturedRect = drawer.LastResolvedPosition;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            // WGroup padding should be applied even in settings context
            Assert.AreEqual(
                controlRect.x + GroupLeftPadding,
                capturedRect.x,
                0.01f,
                "OnGUI in settings context should apply WGroup left padding."
            );

            Assert.AreEqual(
                controlRect.width - horizontalPadding,
                capturedRect.width,
                0.01f,
                "OnGUI in settings context should apply WGroup horizontal padding to width."
            );
        }

        [Test]
        public void SettingsContextPreservesNonZeroRectPositionWithoutWGroup()
        {
            // Test settings context WITHOUT WGroup padding - should preserve position unchanged
            Rect controlRect = new(25f, 50f, 350f, 200f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 1;

                GroupGUIWidthUtility.ResetForTests();
                // No WGroup padding pushed - should preserve rect unchanged
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: true
                );

                Assert.AreEqual(
                    controlRect.x,
                    resolvedRect.x,
                    0.01f,
                    "Settings context without WGroup should preserve original rect x position."
                );

                Assert.AreEqual(
                    controlRect.y,
                    resolvedRect.y,
                    0.01f,
                    "Settings context should preserve original rect y position."
                );

                Assert.AreEqual(
                    controlRect.width,
                    resolvedRect.width,
                    0.01f,
                    "Settings context should preserve original rect width."
                );

                Assert.AreEqual(
                    controlRect.height,
                    resolvedRect.height,
                    0.01f,
                    "Settings context should preserve original rect height."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SettingsContextWithWGroupAppliesPadding()
        {
            Rect controlRect = new(0f, 0f, 450f, 280f);

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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: true
                        );

                    // WGroup padding should be applied
                    Assert.AreEqual(
                        controlRect.x + GroupLeftPadding,
                        resolvedRect.x,
                        0.001f,
                        "Settings context with WGroup should apply left padding."
                    );

                    Assert.AreEqual(
                        0f,
                        resolvedRect.y,
                        0.001f,
                        "Settings context should preserve y=0 exactly."
                    );

                    Assert.AreEqual(
                        controlRect.width - horizontalPadding,
                        resolvedRect.width,
                        0.001f,
                        "Settings context should apply WGroup horizontal padding to width."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SettingsContextWithNestedPaddingScopesAppliesAccumulatedPadding()
        {
            Rect controlRect = new(0f, 0f, 600f, 400f);

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
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: true
                            );

                        float totalLeftPadding = OuterLeftPadding + InnerLeftPadding;
                        float totalRightPadding = OuterRightPadding + InnerRightPadding;
                        float totalHorizontalPadding = totalLeftPadding + totalRightPadding;

                        Assert.AreEqual(
                            controlRect.x + totalLeftPadding,
                            resolvedRect.x,
                            0.01f,
                            "Settings context should apply accumulated WGroup left padding."
                        );

                        Assert.AreEqual(
                            controlRect.width - totalHorizontalPadding,
                            resolvedRect.width,
                            0.01f,
                            "Settings context should apply accumulated WGroup horizontal padding to width."
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
        /// Test data for normal context indentation with various indent levels.
        /// </summary>
        private static IEnumerable<TestCaseData> NormalContextIndentationTestCases()
        {
            // Test case: (indentLevel, inputX, inputWidth, shouldIncreaseX, shouldDecreaseWidth)
            yield return new TestCaseData(0, 0f, 400f).SetName("IndentLevel0.ZeroStart");
            yield return new TestCaseData(1, 0f, 400f).SetName("IndentLevel1.ZeroStart");
            yield return new TestCaseData(2, 0f, 400f).SetName("IndentLevel2.ZeroStart");
            yield return new TestCaseData(3, 10f, 500f).SetName("IndentLevel3.NonZeroStart");
            yield return new TestCaseData(0, 50f, 300f).SetName("IndentLevel0.LargeOffset");
        }

        [TestCaseSource(nameof(NormalContextIndentationTestCases))]
        public void ResolveContentRectDataDriven(int indentLevel, float inputX, float inputWidth)
        {
            Rect controlRect = new(inputX, 0f, inputWidth, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[DataDriven] indentLevel={indentLevel}, input=({inputX}, {inputWidth}), "
                        + $"resolved=({resolvedRect.x:F3}, {resolvedRect.width:F3})"
                );

                // At indentLevel 0 with no WGroup, UnityListAlignmentOffset (-1.25f) is applied
                if (indentLevel == 0)
                {
                    const float UnityListAlignmentOffset = -1.25f;
                    // When starting at x=0, the offset would make x negative, so it's clamped to 0
                    float expectedX = inputX + UnityListAlignmentOffset;
                    if (expectedX < 0f)
                    {
                        expectedX = 0f;
                    }
                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.1f,
                        "At indentLevel 0, x should have UnityListAlignmentOffset applied (clamped if needed)"
                    );

                    // Width increases by the alignment offset (or less if clamped)
                    float widthIncrease =
                        inputX + UnityListAlignmentOffset < 0f
                            ? inputX + UnityListAlignmentOffset + (-UnityListAlignmentOffset)
                            : -UnityListAlignmentOffset;
                    float expectedWidth = inputWidth + widthIncrease;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.1f,
                        "Width should account for UnityListAlignmentOffset at indent level 0 without WGroup"
                    );
                }
                else
                {
                    // The exact indent depends on Unity's internals, so only the direction is asserted.
                    Assert.Greater(
                        resolvedRect.x,
                        inputX,
                        "At indentLevel > 0, x should be greater than input"
                    );

                    // Width should be reduced from original
                    Assert.Less(
                        resolvedRect.width,
                        inputWidth,
                        "Width should be reduced at indentLevel > 0"
                    );
                }

                // Width should always be non-negative
                Assert.GreaterOrEqual(resolvedRect.width, 0f, "Width should never be negative");

                // At indentLevel 0 the alignment offset can widen the rect, so only the indented case is bounded.
                if (0 < indentLevel)
                {
                    Assert.LessOrEqual(
                        resolvedRect.width,
                        inputWidth,
                        "Width should never exceed original width when indentLevel > 0"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Test data for settings context (skipIndentation=true) with WGroup padding.
        /// </summary>
        private static IEnumerable<TestCaseData> SettingsContextWithPaddingTestCases()
        {
            // Test case: (leftPadding, rightPadding, inputX, inputWidth)
            yield return new TestCaseData(10f, 5f, 0f, 400f).SetName("SmallPadding.ZeroStart");
            yield return new TestCaseData(20f, 15f, 5f, 500f).SetName("LargePadding.SmallOffset");
            yield return new TestCaseData(0f, 0f, 10f, 300f).SetName("NoPadding.NonZeroStart");
            yield return new TestCaseData(12f, 8f, 0f, 450f).SetName("MixedPadding.ZeroStart");
        }

        [TestCaseSource(nameof(SettingsContextWithPaddingTestCases))]
        public void ResolveContentRectSettingsContextDataDriven(
            float leftPadding,
            float rightPadding,
            float inputX,
            float inputWidth
        )
        {
            Rect controlRect = new(inputX, 0f, inputWidth, 300f);
            float horizontalPadding = leftPadding + rightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2; // Should be ignored in settings context

                GroupGUIWidthUtility.ResetForTests();

                if (0f < leftPadding || 0f < rightPadding)
                {
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            leftPadding,
                            rightPadding
                        )
                    )
                    {
                        Rect resolvedRect =
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: true
                            );

                        TestContext.WriteLine(
                            $"[SettingsDataDriven] padding=({leftPadding}, {rightPadding}), "
                                + $"input=({inputX}, {inputWidth}), "
                                + $"resolved=({resolvedRect.x:F3}, {resolvedRect.width:F3})"
                        );

                        Assert.AreEqual(
                            inputX + leftPadding,
                            resolvedRect.x,
                            0.01f,
                            "Settings context should apply WGroup left padding"
                        );

                        Assert.AreEqual(
                            inputWidth - horizontalPadding,
                            resolvedRect.width,
                            0.01f,
                            "Settings context should apply WGroup horizontal padding to width"
                        );
                    }
                }
                else
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: true
                        );

                    TestContext.WriteLine(
                        $"[SettingsDataDriven] no padding, input=({inputX}, {inputWidth}), "
                            + $"resolved=({resolvedRect.x:F3}, {resolvedRect.width:F3})"
                    );

                    Assert.AreEqual(
                        inputX,
                        resolvedRect.x,
                        0.01f,
                        "Settings context without WGroup should preserve x"
                    );

                    Assert.AreEqual(
                        inputWidth,
                        resolvedRect.width,
                        0.01f,
                        "Settings context without WGroup should preserve width"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void ResolveContentRectWithZeroWidthPreservesMinimumBounds()
        {
            Rect controlRect = new(0f, 0f, 0f, 300f);

            GroupGUIWidthUtility.ResetForTests();
            Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                controlRect,
                skipIndentation: false
            );

            // Width should not go negative
            Assert.GreaterOrEqual(
                resolvedRect.width,
                0f,
                "Width should never be negative, even with zero input width"
            );
        }

        [Test]
        public void ResolveContentRectWithNegativeInputHandledGracefully()
        {
            Rect controlRect = new(-10f, 0f, 100f, 300f);

            GroupGUIWidthUtility.ResetForTests();
            Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                controlRect,
                skipIndentation: false
            );

            // Should handle negative x gracefully
            Assert.IsTrue(
                !float.IsNaN(resolvedRect.x) && !float.IsInfinity(resolvedRect.x),
                "X should be a valid number even with negative input"
            );
        }

        [Test]
        public void ResolveContentRectWithLargePaddingClampedToZeroWidth()
        {
            Rect controlRect = new(0f, 0f, 50f, 300f);
            const float LargeLeftPadding = 100f;
            const float LargeRightPadding = 100f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        LargeLeftPadding + LargeRightPadding,
                        LargeLeftPadding,
                        LargeRightPadding
                    )
                )
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: true
                        );

                    // When padding exceeds width, width should be clamped to 0
                    Assert.AreEqual(
                        0f,
                        resolvedRect.width,
                        0.001f,
                        "Width should be clamped to 0 when padding exceeds available width"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void ResolveContentRectWithMaxIndentLevelDoesNotCrash()
        {
            Rect controlRect = new(0f, 0f, 1000f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 100; // Very high indent level

                GroupGUIWidthUtility.ResetForTests();
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // Should not crash and return valid values
                Assert.IsTrue(
                    !float.IsNaN(resolvedRect.x) && !float.IsInfinity(resolvedRect.x),
                    "X should be a valid number even with very high indent level"
                );
                Assert.GreaterOrEqual(
                    resolvedRect.width,
                    0f,
                    "Width should be non-negative even with very high indent level"
                );
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );

                        TestContext.WriteLine(
                            $"[WGroupPropertyContextIgnoresPaddingValues] "
                                + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                                + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3}), "
                                + $"padding=({GroupLeftPadding:F3}, {GroupRightPadding:F3})"
                        );

                        // Unity's layout already applied the padding, so only the -4f alignment offset remains.
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
        /// Tests zero padding and zero indent in WGroup property context.
        /// WGroupPropertyContext applies a -4f alignment offset to align with other WGroup content.
        /// </summary>
        [Test]
        public void WGroupPropertyContextZeroPaddingZeroIndent()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            // In WGroup context there is no clamp, so starting at x=0 gives x=-4f.
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    TestContext.WriteLine(
                        $"[WGroupPropertyContextZeroPaddingZeroIndent] "
                            + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                            + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3})"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        "WGroupPropertyContext should apply -4f alignment offset even at zero padding and indent."
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        "WGroupPropertyContext should increase width by 4f even at zero padding and indent."
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
        /// Only the -4f alignment offset is applied, padding is ignored.
        /// </summary>
        [Test]
        public void WGroupPropertyContextWithMaxPaddingValues()
        {
            Rect controlRect = new(100f, 50f, 600f, 400f);

            const float MaxLeftPadding = 100f;
            const float MaxRightPadding = 100f;
            float horizontalPadding = MaxLeftPadding + MaxRightPadding;

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

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
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );

                        TestContext.WriteLine(
                            $"[WGroupPropertyContextWithMaxPaddingValues] "
                                + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                                + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3}), "
                                + $"maxPadding=({MaxLeftPadding:F3}, {MaxRightPadding:F3})"
                        );

                        Assert.AreEqual(
                            expectedX,
                            resolvedRect.x,
                            0.001f,
                            "WGroupPropertyContext should apply -4f alignment offset even with max padding."
                        );
                        Assert.AreEqual(
                            expectedWidth,
                            resolvedRect.width,
                            0.001f,
                            "WGroupPropertyContext should increase width by 4f even with max padding."
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
        /// Tests WGroupPropertyContext with very high indent level.
        /// Only the -4f alignment offset is applied, indent is ignored.
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
        /// Tests nested WGroupPropertyContext scopes.
        /// The -4f alignment offset is applied (once, not doubled for nesting).
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
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float WGroupAlignmentOffset = -4f;
            float expectedXWithContext = controlRect.x + WGroupAlignmentOffset;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 2;
                GroupGUIWidthUtility.ResetForTests();

                // First, resolve without WGroupPropertyContext
                Rect rectWithoutContext =
                    SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                // Then with WGroupPropertyContext
                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect rectWithContext =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    Assert.AreEqual(
                        expectedXWithContext,
                        rectWithContext.x,
                        0.001f,
                        "Inside WGroupPropertyContext, x should have -4f alignment offset."
                    );
                }

                // After disposal, should be back to normal behavior
                Rect rectAfterContext =
                    SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
        /// Tests WGroupPropertyContext with skipIndentation=true parameter.
        /// Even in settings context, WGroupPropertyContext applies -4f alignment offset.
        /// </summary>
        [Test]
        public void WGroupPropertyContextWithSkipIndentationTrue()
        {
            Rect controlRect = new(10f, 20f, 400f, 300f);

            const float LeftPadding = 15f;
            const float RightPadding = 10f;
            float horizontalPadding = LeftPadding + RightPadding;

            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

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
                    using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                    {
                        Rect resolvedRect =
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: true
                            );

                        TestContext.WriteLine(
                            $"[WGroupPropertyContextWithSkipIndentationTrue] "
                                + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                                + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3})"
                        );

                        Assert.AreEqual(
                            expectedX,
                            resolvedRect.x,
                            0.001f,
                            "WGroupPropertyContext with skipIndentation=true should apply -4f alignment offset."
                        );
                        Assert.AreEqual(
                            expectedWidth,
                            resolvedRect.width,
                            0.001f,
                            "WGroupPropertyContext with skipIndentation=true should increase width by 4f."
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
        /// Tests contrast between WGroupPropertyContext (applies -4f alignment offset) vs
        /// PushContentPadding only (applies padding manually).
        /// </summary>
        [Test]
        public void ContrastWGroupPropertyContextVsPushContentPaddingOnly()
        {
            Rect controlRect = new(10f, 20f, 400f, 300f);

            const float LeftPadding = 15f;
            const float RightPadding = 10f;
            float horizontalPadding = LeftPadding + RightPadding;

            const float WGroupAlignmentOffset = -4f;
            float expectedXWithWGroup = controlRect.x + WGroupAlignmentOffset;
            float expectedWidthWithWGroup = controlRect.width - WGroupAlignmentOffset;

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
                    rectWithPaddingOnly =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );
                    }
                }

                TestContext.WriteLine(
                    $"[ContrastWGroupPropertyContextVsPushContentPaddingOnly] "
                        + $"paddingOnly=({rectWithPaddingOnly.x:F3}, {rectWithPaddingOnly.width:F3}), "
                        + $"withWGroupContext=({rectWithWGroupContext.x:F3}, {rectWithWGroupContext.width:F3})"
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

                // WGroupPropertyContext should apply -4f alignment offset
                Assert.AreEqual(
                    expectedXWithWGroup,
                    rectWithWGroupContext.x,
                    0.001f,
                    "WGroupPropertyContext should apply -4f alignment offset to x."
                );
                Assert.AreEqual(
                    expectedWidthWithWGroup,
                    rectWithWGroupContext.width,
                    0.001f,
                    "WGroupPropertyContext should increase width by 4f."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests PushContentPadding with skipIndentation=true (settings context).
        /// Without WGroupPropertyContext, padding should still be applied.
        /// </summary>
        [Test]
        public void PushContentPaddingWithSkipIndentationTrue()
        {
            Rect controlRect = new(5f, 10f, 450f, 300f);

            const float LeftPadding = 12f;
            const float RightPadding = 8f;
            float horizontalPadding = LeftPadding + RightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 3; // Should be ignored with skipIndentation=true
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        LeftPadding,
                        RightPadding
                    )
                )
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: true
                        );

                    TestContext.WriteLine(
                        $"[PushContentPaddingWithSkipIndentationTrue] "
                            + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                            + $"resolvedRect=({resolvedRect.x:F3}, {resolvedRect.width:F3})"
                    );

                    // With skipIndentation=true but no WGroupPropertyContext, padding is still applied
                    Assert.AreEqual(
                        controlRect.x + LeftPadding,
                        resolvedRect.x,
                        0.01f,
                        "skipIndentation=true without WGroupPropertyContext should still apply left padding."
                    );
                    Assert.AreEqual(
                        controlRect.width - horizontalPadding,
                        resolvedRect.width,
                        0.01f,
                        "skipIndentation=true without WGroupPropertyContext should still apply horizontal padding."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests WGroupPropertyContext with a rect at origin (0,0).
        /// The -4f alignment offset results in x=-4f.
        /// </summary>
        [Test]
        public void WGroupPropertyContextWithRectAtOrigin()
        {
            Rect controlRect = new(0f, 0f, 300f, 200f);

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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                        "WGroupPropertyContext should apply -4f alignment offset even at origin."
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
        /// Tests WGroupPropertyContext with a very narrow rect.
        /// The -4f alignment offset still applies, increasing width by 4f.
        /// </summary>
        [Test]
        public void WGroupPropertyContextWithNarrowRect()
        {
            Rect controlRect = new(10f, 10f, 50f, 300f);

            const float WGroupAlignmentOffset = -4f;
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
                            SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                                controlRect,
                                skipIndentation: false
                            );

                        TestContext.WriteLine(
                            $"[WGroupPropertyContextWithNarrowRect] "
                                + $"controlRect.width={controlRect.width:F3}, "
                                + $"resolvedRect.width={resolvedRect.width:F3}"
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
                                SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
        /// Tests WGroupPropertyContext combined with Settings context (UnityHelpersSettings).
        /// </summary>
        [UnityTest]
        public IEnumerator WGroupPropertyContextInSettingsContext()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            SerializedProperty paletteProp = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            Assert.IsTrue(
                paletteProp != null,
                "Settings should have the WButtonCustomColors dictionary property."
            );

            if (paletteProp == null)
            {
                yield break;
            }

            paletteProp.isExpanded = true;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(15f, 25f, 500f, 300f);
            GUIContent label = new("Palette");

            const float GroupLeftPadding = 20f;
            const float GroupRightPadding = 15f;
            float horizontalPadding = GroupLeftPadding + GroupRightPadding;

            Rect capturedRect = default;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                EditorGUI.indentLevel = 2;
                try
                {
                    serializedSettings.UpdateIfRequiredOrScript();
                    GroupGUIWidthUtility.ResetForTests();
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
                            drawer.OnGUI(controlRect, paletteProp, label);
                            capturedRect = drawer.LastResolvedPosition;
                        }
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            TestContext.WriteLine(
                $"[WGroupPropertyContextInSettingsContext] "
                    + $"controlRect=({controlRect.x:F3}, {controlRect.width:F3}), "
                    + $"capturedRect=({capturedRect.x:F3}, {capturedRect.width:F3})"
            );

            // WGroupPropertyContext applies -4f alignment offset, even in settings context
            const float WGroupAlignmentOffset = -4f;
            float expectedX = controlRect.x + WGroupAlignmentOffset;
            float expectedWidth = controlRect.width - WGroupAlignmentOffset;

            Assert.AreEqual(
                expectedX,
                capturedRect.x,
                0.01f,
                "WGroupPropertyContext in settings context should apply -4f alignment offset to x."
            );
            Assert.AreEqual(
                expectedWidth,
                capturedRect.width,
                0.01f,
                "WGroupPropertyContext in settings context should increase width by 4f."
            );
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
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

        /// <summary>
        /// Documents the Unity Rect xMin/xMax/width interdependency behavior.
        /// When xMin is modified, xMax stays constant and width adjusts automatically.
        /// This behavior is critical to understand for correct offset calculations.
        /// </summary>
        [Test]
        public void UnityRectBehaviorXMinModificationAdjustsWidthAutomatically()
        {
            Rect original = new(10f, 0f, 100f, 50f);
            float originalXMax = original.xMax; // 110

            TestContext.WriteLine(
                $"[Original] x={original.x}, width={original.width}, xMax={originalXMax}"
            );

            // Modify xMin - this should keep xMax constant and adjust width
            Rect modified = original;
            modified.xMin -= 5f; // Shift left by 5

            TestContext.WriteLine(
                $"[After xMin -= 5] x={modified.x}, width={modified.width}, xMax={modified.xMax}"
            );

            Assert.AreEqual(5f, modified.x, 0.001f, "x (xMin) should be reduced by 5");
            Assert.AreEqual(
                originalXMax,
                modified.xMax,
                0.001f,
                "xMax should stay constant when xMin is modified"
            );
            Assert.AreEqual(
                105f,
                modified.width,
                0.001f,
                "Width should increase by 5 when xMin decreases by 5 (since xMax stays constant)"
            );
        }

        /// <summary>
        /// Demonstrates that explicit width modification AFTER xMin modification
        /// would cause a double adjustment - the bug that was fixed.
        /// </summary>
        [Test]
        public void UnityRectBehaviorDoubleAdjustmentDemonstration()
        {
            Rect rect = new(10f, 0f, 100f, 50f);
            float offset = -4f;

            TestContext.WriteLine($"[Original] x={rect.x}, width={rect.width}, xMax={rect.xMax}");

            // CORRECT: Only modify xMin, width adjusts automatically
            Rect correct = rect;
            correct.xMin += offset; // x becomes 6, width becomes 104

            TestContext.WriteLine(
                $"[Correct - xMin only] x={correct.x}, width={correct.width}, xMax={correct.xMax}"
            );

            // INCORRECT: Modify both xMin and width explicitly (the old buggy approach)
            Rect incorrect = rect;
            incorrect.xMin += offset; // x becomes 6, width becomes 104
            incorrect.width -= offset; // width becomes 108 (WRONG - double adjustment!)

            TestContext.WriteLine(
                $"[Incorrect - xMin AND width] x={incorrect.x}, width={incorrect.width}, xMax={incorrect.xMax}"
            );

            Assert.AreEqual(
                6f,
                correct.x,
                0.001f,
                "Correct approach: x should be original + offset"
            );
            Assert.AreEqual(
                104f,
                correct.width,
                0.001f,
                "Correct approach: width should increase by |offset|"
            );

            Assert.AreEqual(6f, incorrect.x, 0.001f, "Incorrect approach: x is same");
            Assert.AreEqual(
                108f,
                incorrect.width,
                0.001f,
                "Incorrect approach: width is doubled (the bug)"
            );

            // The key assertion - correct width should be 4 less than incorrect
            Assert.AreEqual(
                correct.width + 4f,
                incorrect.width,
                0.001f,
                "Double adjustment causes width to be 4 more than intended"
            );
        }

        private static IEnumerable<TestCaseData> WGroupPropertyContextEdgeCases()
        {
            // Standard cases
            yield return new TestCaseData(0f, 400f, 0).SetName("X0.Width400.Indent0");
            yield return new TestCaseData(4f, 400f, 0).SetName("X4.Width400.Indent0.XBecomesZero");
            yield return new TestCaseData(2f, 400f, 0).SetName(
                "X2.Width400.Indent0.XBecomesNegative"
            );

            // Various widths
            yield return new TestCaseData(10f, 100f, 0).SetName("X10.Width100.Indent0");
            yield return new TestCaseData(10f, 200f, 0).SetName("X10.Width200.Indent0");
            yield return new TestCaseData(10f, 500f, 0).SetName("X10.Width500.Indent0");

            // Different indent levels (indent level shouldn't matter in WGroupPropertyContext)
            yield return new TestCaseData(10f, 400f, 1).SetName("X10.Width400.Indent1");
            yield return new TestCaseData(10f, 400f, 2).SetName("X10.Width400.Indent2");

            // Edge case: very small rect
            yield return new TestCaseData(4f, 10f, 0).SetName("SmallRect.X4.Width10");

            // Edge case: large x offset
            yield return new TestCaseData(100f, 400f, 0).SetName("LargeX.100.Width400");
        }

        [TestCaseSource(nameof(WGroupPropertyContextEdgeCases))]
        public void WGroupPropertyContextWidthAdjustmentEdgeCases(
            float inputX,
            float inputWidth,
            int indentLevel
        )
        {
            Rect controlRect = new(inputX, 0f, inputWidth, 300f);
            const float WGroupAlignmentOffset = -4f;

            // Expected: xMin shifts left by 4, xMax stays constant, so width increases by 4
            float expectedX = inputX + WGroupAlignmentOffset;
            float expectedWidth = inputWidth - WGroupAlignmentOffset; // width + 4

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    TestContext.WriteLine(
                        $"[EdgeCase] input=({inputX}, {inputWidth}), indent={indentLevel}, "
                            + $"resolved=({resolvedRect.x:F3}, {resolvedRect.width:F3}), "
                            + $"expected=({expectedX:F3}, {expectedWidth:F3})"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.001f,
                        $"x should be input.x + WGroupAlignmentOffset ({inputX} + {WGroupAlignmentOffset} = {expectedX})"
                    );
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.001f,
                        $"Width should increase by |WGroupAlignmentOffset| ({inputWidth} + 4 = {expectedWidth})"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        private static IEnumerable<TestCaseData> UnityListAlignmentOffsetEdgeCases()
        {
            const float UnityListAlignmentOffset = -1.25f;

            // Case: x starts at 0, gets clamped
            yield return new TestCaseData(0f, 400f, 0f, 400f).SetName("X0.ClampedToZero");

            // Case: x starts at 0.5, would go negative, gets clamped
            yield return new TestCaseData(0.5f, 400f, 0f, 400.5f).SetName("X0Point5.ClampedToZero");

            // Case: x starts at 1.25, goes exactly to 0
            yield return new TestCaseData(1.25f, 400f, 0f, 401.25f).SetName(
                "X1Point25.ExactlyZero"
            );

            // Case: x starts above 1.25, normal shift
            yield return new TestCaseData(
                50f,
                300f,
                50f + UnityListAlignmentOffset,
                300f - UnityListAlignmentOffset
            ).SetName("X50.NormalShift");

            // Case: large x, normal shift
            yield return new TestCaseData(
                100f,
                500f,
                100f + UnityListAlignmentOffset,
                500f - UnityListAlignmentOffset
            ).SetName("X100.LargeRect");
        }

        [TestCaseSource(nameof(UnityListAlignmentOffsetEdgeCases))]
        public void UnityListAlignmentOffsetDataDriven(
            float inputX,
            float inputWidth,
            float expectedX,
            float expectedWidth
        )
        {
            Rect controlRect = new(inputX, 0f, inputWidth, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                // Normal context (no WGroup), indentLevel 0
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[UnityListOffset] input=({inputX}, {inputWidth}), "
                        + $"resolved=({resolvedRect.x:F3}, {resolvedRect.width:F3}), "
                        + $"expected=({expectedX:F3}, {expectedWidth:F3})"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.01f,
                    $"x should be {expectedX} after UnityListAlignmentOffset with clamping"
                );
                Assert.AreEqual(
                    expectedWidth,
                    resolvedRect.width,
                    0.01f,
                    $"Width should be {expectedWidth}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Documents the behavior of EditorGUI.IndentedRect at indentLevel=0.
        /// In some Unity versions, IndentedRect unexpectedly modifies the rect width
        /// even at indentLevel=0 (shifts xMax left by approximately 1.25).
        /// In other Unity versions, IndentedRect returns the rect unchanged at level 0.
        /// The production fix skips calling IndentedRect when indentLevel=0 to ensure
        /// consistent behavior across all Unity versions.
        /// </summary>
        [Test]
        public void IndentedRectAtZeroIndentBehaviorDocumentation()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                Rect indentedRect = EditorGUI.IndentedRect(controlRect);

                float xMaxShift = controlRect.xMax - indentedRect.xMax;
                float widthDifference = controlRect.width - indentedRect.width;
                bool behaviorModifiesRect = 0.001f < widthDifference;

                TestContext.WriteLine(
                    $"[IndentedRect at indentLevel=0] Unity Version Behavior Documentation:"
                );
                TestContext.WriteLine(
                    $"  original=({controlRect.x:F3}, {controlRect.width:F3}, xMax={controlRect.xMax:F3})"
                );
                TestContext.WriteLine(
                    $"  indented=({indentedRect.x:F3}, {indentedRect.width:F3}, xMax={indentedRect.xMax:F3})"
                );
                TestContext.WriteLine(
                    $"  xMaxShift={xMaxShift:F4}, widthDifference={widthDifference:F4}"
                );
                TestContext.WriteLine(
                    $"  Unity behavior in this version: {(behaviorModifiesRect ? "MODIFIES rect at level 0" : "Returns rect UNCHANGED at level 0")}"
                );
                TestContext.WriteLine(
                    $"  Production code skips IndentedRect at level 0 to ensure consistent behavior."
                );

                // xMin (x) should remain unchanged at indentLevel=0 in all Unity versions
                Assert.AreEqual(
                    controlRect.x,
                    indentedRect.x,
                    0.001f,
                    "IndentedRect at indentLevel=0 should not modify xMin/x in any Unity version"
                );

                Assert.IsTrue(
                    !float.IsNaN(indentedRect.width) && !float.IsInfinity(indentedRect.width),
                    "IndentedRect should return valid width values"
                );
                Assert.GreaterOrEqual(
                    indentedRect.width,
                    0f,
                    "IndentedRect should return non-negative width"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Documents the xMax shift magnitude at indentLevel=0 across Unity versions.
        /// In some versions: shift is approximately 1.25 (Unity modifies the rect).
        /// In other versions: shift is 0 (Unity returns rect unchanged).
        /// This test documents the observed behavior without asserting a specific value.
        /// </summary>
        [Test]
        public void IndentedRectAtZeroIndentXMaxShiftDocumentation()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                Rect indentedRect = EditorGUI.IndentedRect(controlRect);
                float xMaxShift = controlRect.xMax - indentedRect.xMax;

                TestContext.WriteLine(
                    $"[xMax shift at indentLevel=0] Unity Version Behavior Documentation:"
                );
                TestContext.WriteLine(
                    $"  shift={xMaxShift:F4} (original.xMax={controlRect.xMax:F3}, indented.xMax={indentedRect.xMax:F3})"
                );
                TestContext.WriteLine($"  Known Unity behaviors:");
                TestContext.WriteLine(
                    $"    - Some versions: shift ~1.25 (unexpectedly modifies rect)"
                );
                TestContext.WriteLine($"    - Other versions: shift 0 (returns rect unchanged)");
                TestContext.WriteLine($"  Current Unity version shift: {xMaxShift:F4}");

                // The shift is 0 on newer Unity and about 1.25 on older, so the range accepts both.
                Assert.GreaterOrEqual(xMaxShift, 0f, "xMax shift should be non-negative");
                Assert.LessOrEqual(
                    xMaxShift,
                    2f,
                    "xMax shift should not exceed 2 (typical range is 0 to 1.25)"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Verifies that skipping IndentedRect at indentLevel=0 preserves the original rect.
        /// This is the behavior our fix implements.
        /// </summary>
        [Test]
        public void SkippingIndentedRectAtZeroIndentPreservesRect()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                // Our fix: skip IndentedRect at level 0
                Rect result =
                    0 < EditorGUI.indentLevel ? EditorGUI.IndentedRect(controlRect) : controlRect;

                Assert.AreEqual(
                    controlRect.x,
                    result.x,
                    0.001f,
                    "Skipping IndentedRect at level 0 preserves x"
                );
                Assert.AreEqual(
                    controlRect.width,
                    result.width,
                    0.001f,
                    "Skipping IndentedRect at level 0 preserves width"
                );
                Assert.AreEqual(
                    controlRect.xMax,
                    result.xMax,
                    0.001f,
                    "Skipping IndentedRect at level 0 preserves xMax"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests IndentedRect behavior at indentLevel=1 for comparison with level 0.
        /// At level 1, the x position should shift right and width should decrease.
        /// </summary>
        [Test]
        public void IndentedRectAtLevel1ShiftsXAndReducesWidth()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 1;

                Rect indentedRect = EditorGUI.IndentedRect(controlRect);

                TestContext.WriteLine(
                    $"[IndentedRect at indentLevel=1] "
                        + $"original=({controlRect.x:F3}, {controlRect.width:F3}), "
                        + $"indented=({indentedRect.x:F3}, {indentedRect.width:F3})"
                );

                // At level 1, x should shift right (indentation applied)
                Assert.Greater(
                    indentedRect.x,
                    controlRect.x,
                    "IndentedRect at indentLevel=1 should shift x right"
                );

                // Width should decrease
                Assert.Less(
                    indentedRect.width,
                    controlRect.width,
                    "IndentedRect at indentLevel=1 should reduce width"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Compares IndentedRect behavior between level 0 and level 1 to show the difference.
        /// </summary>
        [Test]
        public void IndentedRectLevel0VsLevel1Comparison()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                Rect rectAtLevel0 = EditorGUI.IndentedRect(controlRect);

                EditorGUI.indentLevel = 1;
                Rect rectAtLevel1 = EditorGUI.IndentedRect(controlRect);

                TestContext.WriteLine(
                    $"[Level comparison] "
                        + $"Level0=({rectAtLevel0.x:F3}, {rectAtLevel0.width:F3}), "
                        + $"Level1=({rectAtLevel1.x:F3}, {rectAtLevel1.width:F3})"
                );

                // Level 1 should have larger x offset than level 0
                Assert.Greater(
                    rectAtLevel1.x,
                    rectAtLevel0.x,
                    "IndentedRect at level 1 should have larger x than level 0"
                );

                // Level 1 should have smaller width than level 0
                Assert.Less(
                    rectAtLevel1.width,
                    rectAtLevel0.width,
                    "IndentedRect at level 1 should have smaller width than level 0"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Data-driven test for IndentedRect behavior at various indent levels.
        /// Verifies that the production code's approach of skipping IndentedRect at level 0
        /// produces consistent results regardless of Unity version.
        /// </summary>
        private static IEnumerable<TestCaseData> IndentedRectBehaviorTestCases()
        {
            // Indent level 0: production code skips IndentedRect entirely
            yield return new TestCaseData(0, 0f, 400f).SetName(
                "IndentLevel0.ZeroStart.ProductionSkipsCall"
            );
            yield return new TestCaseData(0, 50f, 400f).SetName(
                "IndentLevel0.NonZeroStart.ProductionSkipsCall"
            );
            yield return new TestCaseData(0, 0f, 200f).SetName(
                "IndentLevel0.SmallWidth.ProductionSkipsCall"
            );

            // Higher indent levels: IndentedRect is called normally
            yield return new TestCaseData(1, 0f, 400f).SetName("IndentLevel1.NormalIndentation");
            yield return new TestCaseData(2, 0f, 400f).SetName("IndentLevel2.NormalIndentation");
            yield return new TestCaseData(3, 50f, 300f).SetName("IndentLevel3.OffsetStart");
        }

        [TestCaseSource(nameof(IndentedRectBehaviorTestCases))]
        public void IndentedRectBehaviorDataDriven(int indentLevel, float inputX, float inputWidth)
        {
            Rect controlRect = new(inputX, 0f, inputWidth, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;

                Rect directIndentedRect = EditorGUI.IndentedRect(controlRect);

                // Production approach: skip IndentedRect at level 0
                Rect productionApproach =
                    0 < indentLevel ? EditorGUI.IndentedRect(controlRect) : controlRect;

                TestContext.WriteLine(
                    $"[IndentedRect DataDriven] indentLevel={indentLevel}, "
                        + $"input=({inputX}, {inputWidth})"
                );
                TestContext.WriteLine(
                    $"  Direct IndentedRect: ({directIndentedRect.x:F3}, {directIndentedRect.width:F3})"
                );
                TestContext.WriteLine(
                    $"  Production approach: ({productionApproach.x:F3}, {productionApproach.width:F3})"
                );

                if (indentLevel == 0)
                {
                    // At level 0, production code preserves the original rect
                    Assert.AreEqual(
                        inputX,
                        productionApproach.x,
                        0.001f,
                        "Production approach at level 0 should preserve original x"
                    );
                    Assert.AreEqual(
                        inputWidth,
                        productionApproach.width,
                        0.001f,
                        "Production approach at level 0 should preserve original width"
                    );
                }
                else
                {
                    // At higher levels, production code uses IndentedRect
                    Assert.AreEqual(
                        directIndentedRect.x,
                        productionApproach.x,
                        0.001f,
                        $"Production approach at level {indentLevel} should match direct IndentedRect x"
                    );
                    Assert.AreEqual(
                        directIndentedRect.width,
                        productionApproach.width,
                        0.001f,
                        $"Production approach at level {indentLevel} should match direct IndentedRect width"
                    );
                    // Higher levels should shift x right
                    Assert.Greater(
                        productionApproach.x,
                        inputX,
                        $"At indent level {indentLevel}, x should be shifted right"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Verifies that the production code's ResolveContentRect handles the
        /// Unity version-specific IndentedRect behavior correctly at indent level 0.
        /// </summary>
        [Test]
        public void ResolveContentRectHandlesIndentLevel0Correctly()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // Whatever IndentedRect does at level 0, production applies -1.25f outside a WGroup and clamps at 0.
                Assert.GreaterOrEqual(
                    resolvedRect.x,
                    0f,
                    "Production code should clamp x to non-negative at indent level 0"
                );
                Assert.GreaterOrEqual(
                    resolvedRect.width,
                    0f,
                    "Production code should maintain non-negative width"
                );

                TestContext.WriteLine(
                    $"[ResolveContentRect at indentLevel=0] "
                        + $"input=({controlRect.x}, {controlRect.width}), "
                        + $"resolved=({resolvedRect.x:F3}, {resolvedRect.width:F3})"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests the IndentedRect behavior across multiple indent levels
        /// to verify the progressive indentation pattern.
        /// </summary>
        [Test]
        public void IndentedRectProgressiveIndentation()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);
            int previousIndentLevel = EditorGUI.indentLevel;

            List<(int level, float x, float width)> results = new();

            try
            {
                for (int level = 0; level <= 5; level++)
                {
                    EditorGUI.indentLevel = level;
                    Rect indented = EditorGUI.IndentedRect(controlRect);
                    results.Add((level, indented.x, indented.width));
                }

                TestContext.WriteLine("[Progressive Indentation]");
                foreach ((int level, float x, float width) in results)
                {
                    TestContext.WriteLine($"  Level {level}: x={x:F3}, width={width:F3}");
                }

                // Verify that higher levels have larger x offsets (starting from level 1)
                for (int i = 2; i < results.Count; i++)
                {
                    Assert.GreaterOrEqual(
                        results[i].x,
                        results[i - 1].x,
                        $"Level {results[i].level} x should be >= level {results[i - 1].level} x"
                    );
                }

                // Verify that higher levels have smaller widths (starting from level 1)
                for (int i = 2; i < results.Count; i++)
                {
                    Assert.LessOrEqual(
                        results[i].width,
                        results[i - 1].width,
                        $"Level {results[i].level} width should be <= level {results[i - 1].level} width"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests ResolveContentRect with zero width input.
        /// </summary>
        [Test]
        public void ResolveContentRectZeroWidthInput()
        {
            Rect controlRect = new(10f, 0f, 0f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[ZeroWidth] input.width={controlRect.width:F3}, resolved.width={resolvedRect.width:F3}"
                );

                Assert.GreaterOrEqual(
                    resolvedRect.width,
                    0f,
                    "Width should remain non-negative with zero input width"
                );
                Assert.IsFalse(
                    float.IsNaN(resolvedRect.width),
                    "Width should not be NaN with zero input"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests ResolveContentRect with negative width input.
        /// </summary>
        [Test]
        public void ResolveContentRectNegativeWidthInput()
        {
            Rect controlRect = new(10f, 0f, -50f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[NegativeWidth] input.width={controlRect.width:F3}, resolved.width={resolvedRect.width:F3}"
                );

                // Implementation may clamp or pass through; verify it doesn't crash
                Assert.IsFalse(
                    float.IsNaN(resolvedRect.width),
                    "Width should not be NaN with negative input"
                );
                Assert.IsFalse(
                    float.IsInfinity(resolvedRect.width),
                    "Width should not be infinite with negative input"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests ResolveContentRect with very large width input.
        /// </summary>
        [TestCase(10000f, TestName = "VeryLargeWidth.10000")]
        [TestCase(100000f, TestName = "VeryLargeWidth.100000")]
        [TestCase(1000000f, TestName = "VeryLargeWidth.1000000")]
        public void ResolveContentRectVeryLargeWidthInput(float largeWidth)
        {
            Rect controlRect = new(10f, 0f, largeWidth, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[LargeWidth] input.width={largeWidth:F0}, resolved.width={resolvedRect.width:F3}"
                );

                Assert.IsFalse(
                    float.IsNaN(resolvedRect.width),
                    "Width should not be NaN with large input"
                );
                Assert.IsFalse(
                    float.IsInfinity(resolvedRect.width),
                    "Width should not be infinite with large input"
                );
                Assert.GreaterOrEqual(
                    resolvedRect.width,
                    0f,
                    "Width should be non-negative with large input"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests ResolveContentRect with negative X input at indentLevel=0.
        /// </summary>
        [Test]
        public void ResolveContentRectNegativeXAtIndentLevel0()
        {
            Rect controlRect = new(-10f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[NegativeX] input.x={controlRect.x:F3}, resolved.x={resolvedRect.x:F3}"
                );

                Assert.IsFalse(
                    float.IsNaN(resolvedRect.x),
                    "X should not be NaN with negative input"
                );
                Assert.IsFalse(
                    float.IsInfinity(resolvedRect.x),
                    "X should not be infinite with negative input"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests ResolveContentRect with X exactly equal to the UnityListAlignmentOffset (1.25).
        /// At this value, the offset should result in x=0 exactly.
        /// </summary>
        [Test]
        public void ResolveContentRectXEqualsUnityListAlignmentOffset()
        {
            const float UnityListAlignmentOffset = -1.25f;
            float inputX = -UnityListAlignmentOffset; // 1.25
            Rect controlRect = new(inputX, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[XEquals1.25] input.x={inputX:F3}, resolved.x={resolvedRect.x:F3}"
                );

                // When x=1.25 and offset=-1.25, result should be x=0
                Assert.AreEqual(
                    0f,
                    resolvedRect.x,
                    0.01f,
                    "When input.x equals |UnityListAlignmentOffset|, result.x should be 0"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests ResolveContentRect with fractional X values close to the alignment threshold.
        /// </summary>
        [TestCase(0.5f, TestName = "FractionalX.0.5")]
        [TestCase(1.0f, TestName = "FractionalX.1.0")]
        [TestCase(1.24f, TestName = "FractionalX.1.24.JustBelowThreshold")]
        [TestCase(1.26f, TestName = "FractionalX.1.26.JustAboveThreshold")]
        [TestCase(2.5f, TestName = "FractionalX.2.5")]
        public void ResolveContentRectFractionalXValues(float inputX)
        {
            const float UnityListAlignmentOffset = -1.25f;
            Rect controlRect = new(inputX, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                float expectedX = inputX + UnityListAlignmentOffset;
                if (expectedX < 0f)
                {
                    expectedX = 0f;
                }

                TestContext.WriteLine(
                    $"[FractionalX] input.x={inputX:F3}, resolved.x={resolvedRect.x:F3}, expected={expectedX:F3}"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.01f,
                    $"For input.x={inputX}, result.x should be {expectedX} (clamped if needed)"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that UnityListAlignmentOffset is only applied at indentLevel=0 outside WGroup.
        /// At indentLevel > 0, IndentedRect handles positioning and no extra offset is applied.
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void UnityListAlignmentOffsetNotAppliedAtHigherIndentLevels(int indentLevel)
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                Rect indentedOnly = EditorGUI.IndentedRect(controlRect);

                TestContext.WriteLine(
                    $"[IndentLevel{indentLevel}] resolved.x={resolvedRect.x:F3}, indentedOnly.x={indentedOnly.x:F3}"
                );

                // At indentLevel > 0, the x position comes from IndentedRect, not alignment offset
                Assert.Greater(
                    resolvedRect.x,
                    0f,
                    $"At indentLevel={indentLevel}, x should be positive (from IndentedRect)"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that at indentLevel=0 with positive X, UnityListAlignmentOffset shifts X left.
        /// </summary>
        [Test]
        public void UnityListAlignmentOffsetShiftsXLeftAtIndentLevel0()
        {
            const float UnityListAlignmentOffset = -1.25f;
            Rect controlRect = new(50f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                float expectedX = controlRect.x + UnityListAlignmentOffset;

                TestContext.WriteLine(
                    $"[AlignmentOffset] input.x={controlRect.x:F3}, resolved.x={resolvedRect.x:F3}, expected={expectedX:F3}"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.01f,
                    "At indentLevel=0 with positive X, alignment offset should shift X left"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that width increases by |UnityListAlignmentOffset| at indentLevel=0
        /// because xMax stays constant while xMin shifts left.
        /// </summary>
        [Test]
        public void UnityListAlignmentOffsetIncreasesWidthAtIndentLevel0()
        {
            const float UnityListAlignmentOffset = -1.25f;
            Rect controlRect = new(50f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // Width increases because xMin shifts left by 1.25 while xMax stays constant
                float expectedWidth = controlRect.width - UnityListAlignmentOffset;

                TestContext.WriteLine(
                    $"[WidthIncrease] input.width={controlRect.width:F3}, resolved.width={resolvedRect.width:F3}, expected={expectedWidth:F3}"
                );

                Assert.AreEqual(
                    expectedWidth,
                    resolvedRect.width,
                    0.01f,
                    "Width should increase by |alignment offset| at indentLevel=0"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests the clamping behavior when X is small and alignment offset would make it negative.
        /// </summary>
        [TestCase(0f, 0f, TestName = "XStartsAt0.ClampedTo0")]
        [TestCase(0.5f, 0f, TestName = "XStartsAt0.5.ClampedTo0")]
        [TestCase(1.0f, 0f, TestName = "XStartsAt1.0.ClampedTo0")]
        [TestCase(1.25f, 0f, TestName = "XStartsAt1.25.BecomesExactly0")]
        [TestCase(2.0f, 0.75f, TestName = "XStartsAt2.0.Becomes0.75")]
        public void UnityListAlignmentOffsetClampingBehavior(float inputX, float expectedX)
        {
            Rect controlRect = new(inputX, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[Clamping] input.x={inputX:F3}, resolved.x={resolvedRect.x:F3}, expected={expectedX:F3}"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.01f,
                    $"For input.x={inputX}, result.x should be clamped to {expectedX}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Tests that UnityListAlignmentOffset is NOT applied inside WGroup (scopeDepth > 0).
        /// </summary>
        [Test]
        public void UnityListAlignmentOffsetNotAppliedInsideWGroup()
        {
            const float LeftPadding = 12f;
            const float RightPadding = 8f;
            float horizontalPadding = LeftPadding + RightPadding;
            Rect controlRect = new(0f, 0f, 400f, 300f);

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
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    // Inside a WGroup no UnityListAlignmentOffset is applied.
                    float expectedX = controlRect.x + LeftPadding;

                    TestContext.WriteLine(
                        $"[InsideWGroup] resolved.x={resolvedRect.x:F3}, expected={expectedX:F3}"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.01f,
                        "Inside WGroup, UnityListAlignmentOffset should not be applied"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Verifies that SerializableSetPropertyDrawer has the same WGroupPropertyContext behavior
        /// as SerializableDictionaryPropertyDrawer.
        /// </summary>
        [Test]
        public void SetDrawerWGroupPropertyContextMatchesDictionaryDrawer()
        {
            Rect controlRect = new(10f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushWGroupPropertyContext())
                {
                    Rect dictRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    Rect setRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    TestContext.WriteLine(
                        $"[Consistency] Dictionary=({dictRect.x:F3}, {dictRect.width:F3}), "
                            + $"Set=({setRect.x:F3}, {setRect.width:F3})"
                    );

                    Assert.AreEqual(
                        dictRect.x,
                        setRect.x,
                        0.001f,
                        "Set drawer x should match Dictionary drawer x in WGroupPropertyContext"
                    );
                    Assert.AreEqual(
                        dictRect.width,
                        setRect.width,
                        0.001f,
                        "Set drawer width should match Dictionary drawer width in WGroupPropertyContext"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Verifies that SerializableSetPropertyDrawer has the same UnityListAlignmentOffset behavior
        /// as SerializableDictionaryPropertyDrawer outside WGroup context.
        /// </summary>
        [Test]
        public void SetDrawerUnityListAlignmentOffsetMatchesDictionaryDrawer()
        {
            Rect controlRect = new(50f, 0f, 300f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                // No WGroup context
                Rect dictRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                Rect setRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                TestContext.WriteLine(
                    $"[Consistency] Dictionary=({dictRect.x:F3}, {dictRect.width:F3}), "
                        + $"Set=({setRect.x:F3}, {setRect.width:F3})"
                );

                Assert.AreEqual(
                    dictRect.x,
                    setRect.x,
                    0.001f,
                    "Set drawer x should match Dictionary drawer x outside WGroup"
                );
                Assert.AreEqual(
                    dictRect.width,
                    setRect.width,
                    0.001f,
                    "Set drawer width should match Dictionary drawer width outside WGroup"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }
    }
}
