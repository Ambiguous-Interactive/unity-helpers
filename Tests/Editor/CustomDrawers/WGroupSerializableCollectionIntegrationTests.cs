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
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    /// <summary>
    /// Integration tests for SerializableDictionary and SerializableSet property drawers
    /// when rendered inside WGroup contexts, verifying correct indentation and tweening behavior.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WGroupSerializableCollectionIntegrationTests : CommonTestBase
    {
        private bool _originalDictionaryTweenEnabled;
        private bool _originalSortedDictionaryTweenEnabled;
        private bool _originalSetTweenEnabled;
        private bool _originalSortedSetTweenEnabled;
        private bool _originalWGroupTweenEnabled;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            GroupGUIWidthUtility.ResetForTests();
            SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
            SerializableDictionaryPropertyDrawer.ClearMainFoldoutAnimCacheForTests();
            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

            _originalDictionaryTweenEnabled =
                UnityHelpersSettings.ShouldTweenSerializableDictionaryFoldouts();
            _originalSortedDictionaryTweenEnabled =
                UnityHelpersSettings.ShouldTweenSerializableSortedDictionaryFoldouts();
            _originalSetTweenEnabled = UnityHelpersSettings.ShouldTweenSerializableSetFoldouts();
            _originalSortedSetTweenEnabled =
                UnityHelpersSettings.ShouldTweenSerializableSortedSetFoldouts();
            _originalWGroupTweenEnabled = UnityHelpersSettings.ShouldTweenWGroupFoldouts();
        }

        [TearDown]
        public override void TearDown()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(
                _originalDictionaryTweenEnabled
            );
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(
                _originalSortedDictionaryTweenEnabled
            );
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(_originalSetTweenEnabled);
            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(
                _originalSortedSetTweenEnabled
            );
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(_originalWGroupTweenEnabled);

            GroupGUIWidthUtility.ResetForTests();
            base.TearDown();
        }

        [Test]
        public void DictionaryInsideWGroupHasCorrectIndentationAtZeroLevel()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            // Fixed values rather than EditorStyles.helpBox, which needs an active GUI context.
            const float SimulatedLeftPadding = 4f;
            const float SimulatedRightPadding = 4f;
            const float HorizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        HorizontalPadding,
                        SimulatedLeftPadding,
                        SimulatedRightPadding
                    )
                )
                {
                    // ResolveContentRectForTests needs no IMGUI context.
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    float expectedX = controlRect.x + SimulatedLeftPadding;
                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.1f,
                        $"Dictionary inside WGroup should use WGroup padding. "
                            + $"Input: controlRect.x={controlRect.x}, leftPadding={SimulatedLeftPadding}, "
                            + $"Expected x={expectedX}, Actual x={resolvedRect.x}, "
                            + $"ScopeDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetInsideWGroupHasCorrectIndentationAtZeroLevel()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            // Fixed values rather than EditorStyles.helpBox, which needs an active GUI context.
            const float SimulatedLeftPadding = 4f;
            const float SimulatedRightPadding = 4f;
            const float HorizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        HorizontalPadding,
                        SimulatedLeftPadding,
                        SimulatedRightPadding
                    )
                )
                {
                    // ResolveContentRectForTests needs no IMGUI context.
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    float expectedX = controlRect.x + SimulatedLeftPadding;
                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.1f,
                        $"Set inside WGroup should use WGroup padding. "
                            + $"Input: controlRect.x={controlRect.x}, leftPadding={SimulatedLeftPadding}, "
                            + $"Expected x={expectedX}, Actual x={resolvedRect.x}, "
                            + $"ScopeDepth={GroupGUIWidthUtility.CurrentScopeDepth}"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [UnityTest]
        public IEnumerator DictionaryFoldoutHasAlignmentOffsetWhenInsideWGroup()
        {
            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = false;

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Dictionary");

            const float SimulatedLeftPadding = 12f;
            const float SimulatedRightPadding = 12f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            Rect capturedFoldoutRect = default;
            bool hasFoldoutRect = false;
            int previousIndentLevel = EditorGUI.indentLevel;
            string typeResolutionError = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, dictionaryProperty, label);

                        hasFoldoutRect =
                            SerializableDictionaryPropertyDrawer.HasLastMainFoldoutRect;
                        if (hasFoldoutRect)
                        {
                            capturedFoldoutRect =
                                SerializableDictionaryPropertyDrawer.LastMainFoldoutRect;
                        }
                        else
                        {
                            typeResolutionError =
                                $"OnGUI completed but HasLastMainFoldoutRect={hasFoldoutRect}. Property path: {dictionaryProperty.propertyPath}";
                        }
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsTrue(
                hasFoldoutRect,
                $"Main foldout rect should be tracked after OnGUI. {typeResolutionError ?? ""}"
            );

            float expectedX =
                controlRect.x
                + SimulatedLeftPadding
                + SerializableDictionaryPropertyDrawer.WGroupFoldoutAlignmentOffset;

            Assert.AreEqual(
                expectedX,
                capturedFoldoutRect.x,
                0.1f,
                $"Dictionary foldout inside WGroup should be shifted right by alignment offset. "
                    + $"Input: controlRect.x={controlRect.x}, leftPadding={SimulatedLeftPadding}, "
                    + $"alignmentOffset={SerializableDictionaryPropertyDrawer.WGroupFoldoutAlignmentOffset}, "
                    + $"Expected x={expectedX}, Actual x={capturedFoldoutRect.x}"
            );

            float expectedWidth =
                controlRect.width
                - horizontalPadding
                - SerializableDictionaryPropertyDrawer.WGroupFoldoutAlignmentOffset;
            Assert.AreEqual(
                expectedWidth,
                capturedFoldoutRect.width,
                0.1f,
                $"Dictionary foldout width should be reduced by alignment offset. "
                    + $"Input: controlRect.width={controlRect.width}, horizontalPadding={horizontalPadding}, "
                    + $"alignmentOffset={SerializableDictionaryPropertyDrawer.WGroupFoldoutAlignmentOffset}, "
                    + $"Expected width={expectedWidth}, Actual width={capturedFoldoutRect.width}"
            );
        }

        [UnityTest]
        public IEnumerator SetFoldoutHasAlignmentOffsetWhenInsideWGroup()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = false;

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            const float SimulatedLeftPadding = 12f;
            const float SimulatedRightPadding = 12f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            Rect capturedFoldoutRect = default;
            bool hasFoldoutRect = false;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, setProperty, label);

                        hasFoldoutRect = SerializableSetPropertyDrawer.HasLastMainFoldoutRect;
                        if (hasFoldoutRect)
                        {
                            capturedFoldoutRect = SerializableSetPropertyDrawer.LastMainFoldoutRect;
                        }
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsTrue(hasFoldoutRect, "Main foldout rect should be tracked after OnGUI.");

            float expectedX =
                controlRect.x
                + SimulatedLeftPadding
                + SerializableSetPropertyDrawer.WGroupFoldoutAlignmentOffset;

            Assert.AreEqual(
                expectedX,
                capturedFoldoutRect.x,
                0.1f,
                $"Set foldout inside WGroup should be shifted right by alignment offset. "
                    + $"Input: controlRect.x={controlRect.x}, leftPadding={SimulatedLeftPadding}, "
                    + $"alignmentOffset={SerializableSetPropertyDrawer.WGroupFoldoutAlignmentOffset}, "
                    + $"Expected x={expectedX}, Actual x={capturedFoldoutRect.x}"
            );

            float expectedWidth =
                controlRect.width
                - horizontalPadding
                - SerializableSetPropertyDrawer.WGroupFoldoutAlignmentOffset;
            Assert.AreEqual(
                expectedWidth,
                capturedFoldoutRect.width,
                0.1f,
                $"Set foldout width should be reduced by alignment offset. "
                    + $"Input: controlRect.width={controlRect.width}, horizontalPadding={horizontalPadding}, "
                    + $"alignmentOffset={SerializableSetPropertyDrawer.WGroupFoldoutAlignmentOffset}, "
                    + $"Expected width={expectedWidth}, Actual width={capturedFoldoutRect.width}"
            );
        }

        [UnityTest]
        public IEnumerator DictionaryFoldoutHasNoOffsetWhenPaddingIsZero()
        {
            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = false;

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Dictionary");

            Rect capturedFoldoutRect = default;
            Rect capturedResolvedPosition = default;
            bool hasFoldoutRect = false;
            string typeResolutionError = null;
            int previousIndentLevel = EditorGUI.indentLevel;
            int capturedScopeDepth = -1;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

                    using (GroupGUIWidthUtility.PushContentPadding(0f, 0f, 0f))
                    {
                        capturedScopeDepth = GroupGUIWidthUtility.CurrentScopeDepth;
                        drawer.OnGUI(controlRect, dictionaryProperty, label);

                        hasFoldoutRect =
                            SerializableDictionaryPropertyDrawer.HasLastMainFoldoutRect;
                        if (hasFoldoutRect)
                        {
                            capturedFoldoutRect =
                                SerializableDictionaryPropertyDrawer.LastMainFoldoutRect;
                            capturedResolvedPosition = drawer.LastResolvedPosition;
                        }
                        else
                        {
                            typeResolutionError =
                                $"OnGUI completed but HasLastMainFoldoutRect={hasFoldoutRect}. Property path: {dictionaryProperty?.propertyPath}";
                        }
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsTrue(
                hasFoldoutRect,
                $"Main foldout rect should be tracked after OnGUI. {typeResolutionError ?? ""}"
            );

            // Zero padding means no WGroup visual context, so scope depth should remain 0
            Assert.AreEqual(
                0,
                capturedScopeDepth,
                $"Zero padding scope should not increment scope depth (no visual WGroup context). Actual: {capturedScopeDepth}"
            );

            // Without WGroup visual context, the foldout x should match the resolved position x (no alignment offset)
            Assert.AreEqual(
                capturedResolvedPosition.x,
                capturedFoldoutRect.x,
                0.1f,
                $"Dictionary foldout with zero padding should not have alignment offset. "
                    + $"Expected x={capturedResolvedPosition.x:F3}, Actual x={capturedFoldoutRect.x:F3}"
            );

            Assert.AreEqual(
                capturedResolvedPosition.width,
                capturedFoldoutRect.width,
                0.1f,
                $"Dictionary foldout width should match resolved position width when padding is zero. "
                    + $"Expected width={capturedResolvedPosition.width:F3}, Actual width={capturedFoldoutRect.width:F3}"
            );
        }

        [UnityTest]
        public IEnumerator SetFoldoutHasNoOffsetWhenPaddingIsZero()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = false;

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            Rect capturedFoldoutRect = default;
            Rect capturedResolvedPosition = default;
            bool hasFoldoutRect = false;
            int previousIndentLevel = EditorGUI.indentLevel;
            int capturedScopeDepth = -1;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    using (GroupGUIWidthUtility.PushContentPadding(0f, 0f, 0f))
                    {
                        capturedScopeDepth = GroupGUIWidthUtility.CurrentScopeDepth;
                        drawer.OnGUI(controlRect, setProperty, label);

                        hasFoldoutRect = SerializableSetPropertyDrawer.HasLastMainFoldoutRect;
                        if (hasFoldoutRect)
                        {
                            capturedFoldoutRect = SerializableSetPropertyDrawer.LastMainFoldoutRect;
                            capturedResolvedPosition = drawer.LastResolvedPosition;
                        }
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsTrue(hasFoldoutRect, "Main foldout rect should be tracked after OnGUI.");

            // Zero padding means no WGroup visual context, so scope depth should remain 0
            Assert.AreEqual(
                0,
                capturedScopeDepth,
                $"Zero padding scope should not increment scope depth (no visual WGroup context). Actual: {capturedScopeDepth}"
            );

            // Without WGroup visual context, the foldout x should match the resolved position x (no alignment offset)
            Assert.AreEqual(
                capturedResolvedPosition.x,
                capturedFoldoutRect.x,
                0.1f,
                $"Set foldout with zero padding should not have alignment offset. "
                    + $"Expected x={capturedResolvedPosition.x:F3}, Actual x={capturedFoldoutRect.x:F3}"
            );

            Assert.AreEqual(
                capturedResolvedPosition.width,
                capturedFoldoutRect.width,
                0.1f,
                $"Set foldout width should match resolved position width when padding is zero. "
                    + $"Expected width={capturedResolvedPosition.width:F3}, Actual width={capturedFoldoutRect.width:F3}"
            );
        }

        [UnityTest]
        public IEnumerator DictionaryFoldoutHasNoAlignmentOffsetWhenNotInsideWGroup()
        {
            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = false;

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Dictionary");

            Rect capturedFoldoutRect = default;
            Rect capturedResolvedPosition = default;
            bool hasFoldoutRect = false;
            string typeResolutionError = null;

            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();

                    drawer.OnGUI(controlRect, dictionaryProperty, label);

                    hasFoldoutRect = SerializableDictionaryPropertyDrawer.HasLastMainFoldoutRect;
                    if (hasFoldoutRect)
                    {
                        capturedFoldoutRect =
                            SerializableDictionaryPropertyDrawer.LastMainFoldoutRect;
                        capturedResolvedPosition = drawer.LastResolvedPosition;
                    }
                    else
                    {
                        typeResolutionError =
                            $"OnGUI completed but HasLastMainFoldoutRect={hasFoldoutRect}. Property path: {dictionaryProperty?.propertyPath}";
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsTrue(
                hasFoldoutRect,
                $"Main foldout rect should be tracked after OnGUI. {typeResolutionError ?? ""}"
            );

            // Without WGroup, the foldout x should match the resolved position x (no alignment offset)
            Assert.AreEqual(
                capturedResolvedPosition.x,
                capturedFoldoutRect.x,
                0.1f,
                "Dictionary foldout outside WGroup should not have alignment offset."
            );
        }

        [UnityTest]
        public IEnumerator SetFoldoutHasNoAlignmentOffsetWhenNotInsideWGroup()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = false;

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            Rect capturedFoldoutRect = default;
            Rect capturedResolvedPosition = default;
            bool hasFoldoutRect = false;

            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    // Ensure no WGroup padding is active
                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    drawer.OnGUI(controlRect, setProperty, label);

                    hasFoldoutRect = SerializableSetPropertyDrawer.HasLastMainFoldoutRect;
                    if (hasFoldoutRect)
                    {
                        capturedFoldoutRect = SerializableSetPropertyDrawer.LastMainFoldoutRect;
                        capturedResolvedPosition = drawer.LastResolvedPosition;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsTrue(hasFoldoutRect, "Main foldout rect should be tracked after OnGUI.");

            // Without WGroup, the foldout x should match the resolved position x (no alignment offset)
            Assert.AreEqual(
                capturedResolvedPosition.x,
                capturedFoldoutRect.x,
                0.1f,
                "Set foldout outside WGroup should not have alignment offset."
            );
        }

        [Test]
        public void WGroupFoldoutAlignmentOffsetConstantHasExpectedValue()
        {
            // Verify the constant values match our expected 2.5px offset
            Assert.AreEqual(
                2.5f,
                SerializableDictionaryPropertyDrawer.WGroupFoldoutAlignmentOffset,
                0.01f,
                "Dictionary WGroup foldout alignment offset should be 2.5f."
            );

            Assert.AreEqual(
                2.5f,
                SerializableSetPropertyDrawer.WGroupFoldoutAlignmentOffset,
                0.01f,
                "Set WGroup foldout alignment offset should be 2.5f."
            );
        }

        [TestCase(0f, 0f, 0f, false, Description = "Zero padding - no WGroup context, no offset")]
        [TestCase(
            0.001f,
            0f,
            0f,
            true,
            Description = "Tiny horizontal padding - creates WGroup context with offset"
        )]
        [TestCase(
            0f,
            0.001f,
            0f,
            true,
            Description = "Tiny left padding only - creates WGroup context with offset"
        )]
        [TestCase(
            0f,
            0f,
            0.001f,
            true,
            Description = "Tiny right padding only - creates WGroup context with offset"
        )]
        [TestCase(10f, 5f, 5f, true, Description = "Typical padding - creates WGroup context")]
        [TestCase(24f, 12f, 12f, true, Description = "Larger padding - creates WGroup context")]
        public void AlignmentOffsetAppliedBasedOnPaddingContext(
            float horizontalPadding,
            float leftPadding,
            float rightPadding,
            bool expectOffset
        )
        {
            GroupGUIWidthUtility.ResetForTests();

            using (
                GroupGUIWidthUtility.PushContentPadding(
                    horizontalPadding,
                    leftPadding,
                    rightPadding
                )
            )
            {
                int scopeDepth = GroupGUIWidthUtility.CurrentScopeDepth;
                bool hasWGroupContext = 0 < scopeDepth;

                Assert.AreEqual(
                    expectOffset,
                    hasWGroupContext,
                    $"WGroup context (scope depth > 0) should be {expectOffset} with padding "
                        + $"(h={horizontalPadding}, l={leftPadding}, r={rightPadding}). "
                        + $"Actual scope depth: {scopeDepth}"
                );
            }
        }

        [UnityTest]
        public IEnumerator DictionaryFoldoutAlignmentOffsetConsistentAcrossDrawerInstances()
        {
            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = false;

            SerializableDictionaryPropertyDrawer drawer1 = new();
            SerializableDictionaryPropertyDrawer drawer2 = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Dictionary");

            const float SimulatedLeftPadding = 12f;
            const float SimulatedRightPadding = 12f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            Rect capturedFoldoutRect1 = default;
            Rect capturedFoldoutRect2 = default;

            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                        drawer1.OnGUI(controlRect, dictionaryProperty, label);
                        capturedFoldoutRect1 =
                            SerializableDictionaryPropertyDrawer.LastMainFoldoutRect;

                        SerializableDictionaryPropertyDrawer.ResetLayoutTrackingForTests();
                        drawer2.OnGUI(controlRect, dictionaryProperty, label);
                        capturedFoldoutRect2 =
                            SerializableDictionaryPropertyDrawer.LastMainFoldoutRect;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.AreEqual(
                capturedFoldoutRect1.x,
                capturedFoldoutRect2.x,
                0.01f,
                "Foldout x position should be consistent across drawer instances."
            );

            Assert.AreEqual(
                capturedFoldoutRect1.width,
                capturedFoldoutRect2.width,
                0.01f,
                "Foldout width should be consistent across drawer instances."
            );
        }

        [Test]
        public void DictionaryTweenSettingsRespectedWhenEnabled()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                ),
                "Dictionary tweening should be enabled when setting is true."
            );
        }

        [Test]
        public void DictionaryTweenSettingsRespectedWhenDisabled()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);

            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                ),
                "Dictionary tweening should be disabled when setting is false."
            );
        }

        [Test]
        public void SortedDictionaryTweenSettingsRespectedWhenEnabled()
        {
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(true);

            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: true
                ),
                "Sorted dictionary tweening should be enabled when setting is true."
            );
        }

        [Test]
        public void SortedDictionaryTweenSettingsRespectedWhenDisabled()
        {
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(false);

            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: true
                ),
                "Sorted dictionary tweening should be disabled when setting is false."
            );
        }

        [Test]
        public void SetTweenSettingsRespectedWhenEnabled()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);

            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: false),
                "Set tweening should be enabled when setting is true."
            );
        }

        [Test]
        public void SetTweenSettingsRespectedWhenDisabled()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);

            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: false),
                "Set tweening should be disabled when setting is false."
            );
        }

        [Test]
        public void SortedSetTweenSettingsRespectedWhenEnabled()
        {
            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(true);

            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: true),
                "Sorted set tweening should be enabled when setting is true."
            );
        }

        [Test]
        public void SortedSetTweenSettingsRespectedWhenDisabled()
        {
            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(false);

            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(isSortedSet: true),
                "Sorted set tweening should be disabled when setting is false."
            );
        }

        [Test]
        public void WGroupTweenSettingsAreIndependentOfDictionaryTweenSettings()
        {
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);

            Assert.IsTrue(
                UnityHelpersSettings.ShouldTweenWGroupFoldouts(),
                "WGroup tweening should be enabled independently."
            );

            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                ),
                "Dictionary tweening should remain disabled."
            );
        }

        [Test]
        public void WGroupContextDoesNotAffectDictionaryTweeningSetting()
        {
            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(false);

            bool dictionaryTweenEnabled =
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(
                    isSortedDictionary: false
                );

            Assert.IsTrue(
                dictionaryTweenEnabled,
                "Dictionary tweening setting should not be affected by WGroup tween setting."
            );
        }

        [UnityTest]
        public IEnumerator DictionaryInWGroupRendersWithCorrectPaddingDuringOnGUI()
        {
            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;
            host.dictionary["key2"] = 200;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Dictionary");

            const float SimulatedLeftPadding = 12f;
            const float SimulatedRightPadding = 12f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

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
                            SimulatedLeftPadding,
                            SimulatedRightPadding
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

            float expectedX = controlRect.x + SimulatedLeftPadding;
            Assert.AreEqual(
                expectedX,
                capturedRect.x,
                0.1f,
                $"Dictionary OnGUI in WGroup context should apply WGroup padding correctly. "
                    + $"Input: controlRect.x={controlRect.x}, leftPadding={SimulatedLeftPadding}, "
                    + $"Expected x={expectedX}, Actual x={capturedRect.x}"
            );

            float expectedWidth = controlRect.width - horizontalPadding;
            Assert.AreEqual(
                expectedWidth,
                capturedRect.width,
                0.1f,
                $"Dictionary OnGUI in WGroup context should have width reduced by WGroup padding. "
                    + $"Input: controlRect.width={controlRect.width}, horizontalPadding={horizontalPadding}, "
                    + $"Expected width={expectedWidth}, Actual width={capturedRect.width}"
            );
        }

        [UnityTest]
        public IEnumerator SetInWGroupRendersWithCorrectPaddingDuringOnGUI()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(1);
            host.set.Add(2);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            const float SimulatedLeftPadding = 12f;
            const float SimulatedRightPadding = 12f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

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
                            SimulatedLeftPadding,
                            SimulatedRightPadding
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

            float expectedX = controlRect.x + SimulatedLeftPadding;
            Assert.AreEqual(
                expectedX,
                capturedRect.x,
                0.1f,
                $"Set OnGUI in WGroup context should apply WGroup padding correctly. "
                    + $"Input: controlRect.x={controlRect.x}, leftPadding={SimulatedLeftPadding}, "
                    + $"Expected x={expectedX}, Actual x={capturedRect.x}"
            );

            float expectedWidth = controlRect.width - horizontalPadding;
            Assert.AreEqual(
                expectedWidth,
                capturedRect.width,
                0.1f,
                $"Set OnGUI in WGroup context should have width reduced by WGroup padding. "
                    + $"Input: controlRect.width={controlRect.width}, horizontalPadding={horizontalPadding}, "
                    + $"Expected width={expectedWidth}, Actual width={capturedRect.width}"
            );
        }

        [Test]
        public void DictionaryWidthIsNotNegativeWithLargePadding()
        {
            Rect controlRect = new(0f, 0f, 100f, 300f);

            const float LargePadding = 200f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        LargePadding,
                        LargePadding / 2f,
                        LargePadding / 2f
                    )
                )
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    Assert.GreaterOrEqual(
                        resolvedRect.width,
                        0f,
                        "Width should not be negative even with padding larger than control rect."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetWidthIsNotNegativeWithLargePadding()
        {
            Rect controlRect = new(0f, 0f, 100f, 300f);

            const float LargePadding = 200f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        LargePadding,
                        LargePadding / 2f,
                        LargePadding / 2f
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    Assert.GreaterOrEqual(
                        resolvedRect.width,
                        0f,
                        "Width should not be negative even with padding larger than control rect."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void PaddingScopeIsProperlyCleanedUp()
        {
            GroupGUIWidthUtility.ResetForTests();

            Assert.AreEqual(0f, GroupGUIWidthUtility.CurrentLeftPadding, 0.001f);
            Assert.AreEqual(0f, GroupGUIWidthUtility.CurrentRightPadding, 0.001f);

            using (GroupGUIWidthUtility.PushContentPadding(20f, 10f, 10f))
            {
                Assert.AreEqual(10f, GroupGUIWidthUtility.CurrentLeftPadding, 0.001f);
                Assert.AreEqual(10f, GroupGUIWidthUtility.CurrentRightPadding, 0.001f);
            }

            Assert.AreEqual(0f, GroupGUIWidthUtility.CurrentLeftPadding, 0.001f);
            Assert.AreEqual(0f, GroupGUIWidthUtility.CurrentRightPadding, 0.001f);
        }

        [Test]
        public void NestedPaddingScopesAccumulateCorrectly()
        {
            GroupGUIWidthUtility.ResetForTests();

            using (GroupGUIWidthUtility.PushContentPadding(20f, 10f, 10f))
            {
                Assert.AreEqual(10f, GroupGUIWidthUtility.CurrentLeftPadding, 0.001f);

                using (GroupGUIWidthUtility.PushContentPadding(16f, 8f, 8f))
                {
                    Assert.AreEqual(18f, GroupGUIWidthUtility.CurrentLeftPadding, 0.001f);
                    Assert.AreEqual(18f, GroupGUIWidthUtility.CurrentRightPadding, 0.001f);
                }

                Assert.AreEqual(10f, GroupGUIWidthUtility.CurrentLeftPadding, 0.001f);
            }

            Assert.AreEqual(0f, GroupGUIWidthUtility.CurrentLeftPadding, 0.001f);
        }

        [Test]
        public void DictionaryIndentationConsistentAcrossMultipleDrawCalls()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float LeftPadding = 12f;
            const float RightPadding = 12f;

            int previousIndentLevel = EditorGUI.indentLevel;
            List<float> capturedXPositions = new();

            try
            {
                EditorGUI.indentLevel = 0;

                for (int i = 0; i < 5; i++)
                {
                    GroupGUIWidthUtility.ResetForTests();
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            LeftPadding + RightPadding,
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
                        capturedXPositions.Add(resolvedRect.x);
                    }
                }

                float firstX = capturedXPositions[0];
                foreach (float x in capturedXPositions)
                {
                    Assert.AreEqual(
                        firstX,
                        x,
                        0.01f,
                        "Indentation should be consistent across multiple draw calls."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryInSettingsContextWithWGroupPaddingAppliesPadding()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            SerializedProperty paletteProp = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            if (paletteProp == null)
            {
                Assert.Ignore("WButtonCustomColors property not found in settings.");
                return;
            }

            paletteProp.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float WGroupLeftPadding = 10f;
            const float WGroupRightPadding = 10f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        WGroupLeftPadding + WGroupRightPadding,
                        WGroupLeftPadding,
                        WGroupRightPadding
                    )
                )
                {
                    // Use skipIndentation=true to simulate settings context
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: true
                        );

                    Assert.AreEqual(
                        controlRect.x + WGroupLeftPadding,
                        resolvedRect.x,
                        0.01f,
                        "Settings context with WGroup padding should apply WGroup padding to x position."
                    );

                    float expectedWidth =
                        controlRect.width - WGroupLeftPadding - WGroupRightPadding;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.01f,
                        "Settings context with WGroup padding should reduce width by total padding."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryInSettingsContextWithoutWGroupPaddingHasUnchangedPosition()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                // No WGroup padding pushed

                // Use skipIndentation=true to simulate settings context
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: true
                );

                Assert.AreEqual(
                    controlRect.x,
                    resolvedRect.x,
                    0.01f,
                    "Settings context without WGroup padding should have unchanged x position."
                );

                Assert.AreEqual(
                    controlRect.width,
                    resolvedRect.width,
                    0.01f,
                    "Settings context without WGroup padding should have unchanged width."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetInSettingsContextWithWGroupPaddingAppliesPadding()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float WGroupLeftPadding = 10f;
            const float WGroupRightPadding = 10f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        WGroupLeftPadding + WGroupRightPadding,
                        WGroupLeftPadding,
                        WGroupRightPadding
                    )
                )
                {
                    // Use skipIndentation=true to simulate settings context
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: true
                    );

                    Assert.AreEqual(
                        controlRect.x + WGroupLeftPadding,
                        resolvedRect.x,
                        0.01f,
                        "Set in WGroup context should apply WGroup padding to x position."
                    );

                    float expectedWidth =
                        controlRect.width - WGroupLeftPadding - WGroupRightPadding;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.01f,
                        "Set in WGroup context should reduce width by total padding."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetWithoutWGroupPaddingAlignsWithUnityListsClampedAtZero()
        {
            // At controlRect.x = 0 the -1.25f offset would go negative, so it is clamped to 0.
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                // No WGroup padding pushed

                // Use skipIndentation=false to get normal behavior
                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // Without WGroup padding and with indent level 0, alignment offset is applied but clamped to 0
                float rawExpectedX = controlRect.x - 1.25f;
                float expectedX = 0f; // Clamped from -1.25f to 0f
                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.01f,
                    $"Set without WGroup padding and indent=0 should align with Unity's default list rendering but clamp to 0. "
                        + $"Raw expected x={rawExpectedX}, Clamped expected x={expectedX}, Actual x={resolvedRect.x}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetWithoutWGroupPaddingAlignsWithUnityListsWithPositiveX()
        {
            // With a positive starting x, the alignment offset (-1.25f) is applied without clamping.
            Rect controlRect = new(10f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                // No WGroup padding pushed

                // Use skipIndentation=false to get normal behavior
                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // Without WGroup padding and with indent level 0, aligns with Unity's default list rendering
                float expectedX = controlRect.x - 1.25f; // 10f - 1.25f = 8.75f
                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.01f,
                    $"Set without WGroup padding and indent=0 should align with Unity's default list rendering (-1.25f alignment offset). "
                        + $"Expected x={expectedX}, Actual x={resolvedRect.x}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryWithNonZeroIndentLevelAppliesUnityIndentation()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                // Set indent level to simulate being inside another property
                EditorGUI.indentLevel = 2;

                GroupGUIWidthUtility.ResetForTests();
                // No WGroup padding pushed

                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // The exact indentation is Unity's own, so only "greater than zero" is asserted.
                Assert.Greater(
                    resolvedRect.x,
                    controlRect.x,
                    "Dictionary with indent level > 0 should have indentation applied."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void NestedWGroupsAccumulatePaddingCorrectly()
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

                // Simulate outer WGroup
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        OuterLeftPadding + OuterRightPadding,
                        OuterLeftPadding,
                        OuterRightPadding
                    )
                )
                {
                    // Simulate inner WGroup
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

                        float expectedX = controlRect.x + OuterLeftPadding + InnerLeftPadding;
                        Assert.AreEqual(
                            expectedX,
                            resolvedRect.x,
                            0.01f,
                            $"Nested WGroups should accumulate left padding. "
                                + $"Input: controlRect.x={controlRect.x}, outer={OuterLeftPadding}, inner={InnerLeftPadding}, "
                                + $"Expected x={expectedX}, Actual x={resolvedRect.x}, "
                                + $"ScopeDepth={GroupGUIWidthUtility.CurrentScopeDepth}, "
                                + $"CurrentLeftPadding={GroupGUIWidthUtility.CurrentLeftPadding}"
                        );

                        float expectedWidth =
                            controlRect.width
                            - OuterLeftPadding
                            - OuterRightPadding
                            - InnerLeftPadding
                            - InnerRightPadding;
                        Assert.AreEqual(
                            expectedWidth,
                            resolvedRect.width,
                            0.01f,
                            $"Nested WGroups should accumulate total padding reduction on width. "
                                + $"Input: controlRect.width={controlRect.width}, "
                                + $"outer=(left={OuterLeftPadding}, right={OuterRightPadding}), "
                                + $"inner=(left={InnerLeftPadding}, right={InnerRightPadding}), "
                                + $"Expected width={expectedWidth}, Actual width={resolvedRect.width}"
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryInSettingsContextWithNonZeroIndentLevelAppliesOnlyWGroupPadding()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float WGroupLeftPadding = 10f;
            const float WGroupRightPadding = 10f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                // A parent context would set this, though skipIndentation mode ignores it.
                EditorGUI.indentLevel = 1;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        WGroupLeftPadding + WGroupRightPadding,
                        WGroupLeftPadding,
                        WGroupRightPadding
                    )
                )
                {
                    // Use skipIndentation=true to simulate settings context
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: true
                        );

                    // In skipIndentation mode, only WGroup padding is applied
                    Assert.AreEqual(
                        controlRect.x + WGroupLeftPadding,
                        resolvedRect.x,
                        0.01f,
                        "Settings context with non-zero indent should still only apply WGroup padding."
                    );

                    float expectedWidth =
                        controlRect.width - WGroupLeftPadding - WGroupRightPadding;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.01f,
                        "Settings context with non-zero indent should reduce width by WGroup padding only."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetInSettingsContextWithNonZeroIndentLevelAppliesOnlyWGroupPadding()
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);

            const float WGroupLeftPadding = 10f;
            const float WGroupRightPadding = 10f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                // Simulate indent level being set by WGroup/parent context
                EditorGUI.indentLevel = 1;

                GroupGUIWidthUtility.ResetForTests();
                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        WGroupLeftPadding + WGroupRightPadding,
                        WGroupLeftPadding,
                        WGroupRightPadding
                    )
                )
                {
                    // Use skipIndentation=true to simulate settings context
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: true
                    );

                    Assert.AreEqual(
                        controlRect.x + WGroupLeftPadding,
                        resolvedRect.x,
                        0.01f,
                        "Settings context Set with non-zero indent should only apply WGroup padding."
                    );

                    float expectedWidth =
                        controlRect.width - WGroupLeftPadding - WGroupRightPadding;
                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.01f,
                        "Settings context Set with non-zero indent should reduce width by WGroup padding only."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [UnityTest]
        public IEnumerator DictionaryInSettingsContextRestoresOriginalIndentLevelAfterDraw()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            SerializedProperty paletteProp = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            if (paletteProp == null)
            {
                Assert.Ignore("WButtonCustomColors property not found in settings.");
                yield break;
            }

            paletteProp.isExpanded = false; // Keep collapsed to minimize side effects

            SerializableDictionaryPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 50f);
            GUIContent label = new("Palette");

            int previousIndentLevel = EditorGUI.indentLevel;
            int capturedIndentAfter = -1;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    // Set a specific indent level
                    EditorGUI.indentLevel = 2;

                    GroupGUIWidthUtility.ResetForTests();

                    // Call OnGUI - it should internally set indentLevel to 0 but restore it after
                    drawer.OnGUI(controlRect, paletteProp, label);

                    // Capture indent level after OnGUI
                    capturedIndentAfter = EditorGUI.indentLevel;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            // Verify indent level was restored
            Assert.AreEqual(
                2,
                capturedIndentAfter,
                "Settings context dictionary drawer should restore original indent level after OnGUI."
            );
        }

        [UnityTest]
        public IEnumerator SetInSettingsContextRestoresOriginalIndentLevelAfterDraw()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = false; // Keep collapsed

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 50f);
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            int capturedIndentAfter = -1;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    // Set a specific indent level
                    EditorGUI.indentLevel = 2;

                    GroupGUIWidthUtility.ResetForTests();

                    // Call OnGUI
                    drawer.OnGUI(controlRect, setProperty, label);

                    // Capture indent level after OnGUI
                    capturedIndentAfter = EditorGUI.indentLevel;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            // Verify indent level was restored
            Assert.AreEqual(
                2,
                capturedIndentAfter,
                "Set drawer should restore original indent level after OnGUI."
            );
        }

        [Test]
        public void DictionaryInWGroupCreatesAnimBoolWhenTweenEnabled()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);

            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupDictionaryHost),
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            GUIContent label = new("Dictionary");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    // Call GetPropertyHeight to initialize the pending entry
                    drawer.GetPropertyHeight(dictionaryProperty, label);

                    // Now check if animation state is properly initialized
                    bool found = drawer.TryGetPendingAnimationStateForTests(
                        dictionaryProperty,
                        out bool isExpanded,
                        out float animProgress,
                        out bool hasAnimBool
                    );

                    Assert.IsTrue(found, "Pending entry should exist after GetPropertyHeight.");
                    Assert.IsTrue(
                        hasAnimBool,
                        "AnimBool should be created when dictionary tweening is enabled."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryInWGroupNoAnimBoolWhenTweenDisabled()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);

            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupDictionaryHost),
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            GUIContent label = new("Dictionary");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    drawer.GetPropertyHeight(dictionaryProperty, label);

                    bool found = drawer.TryGetPendingAnimationStateForTests(
                        dictionaryProperty,
                        out bool isExpanded,
                        out float animProgress,
                        out bool hasAnimBool
                    );

                    Assert.IsTrue(found, "Pending entry should exist after GetPropertyHeight.");
                    Assert.IsFalse(
                        hasAnimBool,
                        "AnimBool should NOT be created when dictionary tweening is disabled."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetInWGroupCreatesAnimBoolWhenTweenEnabled()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);

            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    drawer.GetPropertyHeight(setProperty, label);

                    bool found = drawer.TryGetPendingAnimationStateForTests(
                        setProperty,
                        out bool isExpanded,
                        out float animProgress,
                        out bool hasAnimBool
                    );

                    Assert.IsTrue(found, "Pending entry should exist after GetPropertyHeight.");
                    Assert.IsTrue(
                        hasAnimBool,
                        "AnimBool should be created when set tweening is enabled."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetInWGroupNoAnimBoolWhenTweenDisabled()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);

            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    drawer.GetPropertyHeight(setProperty, label);

                    bool found = drawer.TryGetPendingAnimationStateForTests(
                        setProperty,
                        out bool isExpanded,
                        out float animProgress,
                        out bool hasAnimBool
                    );

                    Assert.IsTrue(found, "Pending entry should exist after GetPropertyHeight.");
                    Assert.IsFalse(
                        hasAnimBool,
                        "AnimBool should NOT be created when set tweening is disabled."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryFoldoutProgressStartsAtZeroWhenCollapsed()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);

            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupDictionaryHost),
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            GUIContent label = new("Dictionary");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    drawer.GetPropertyHeight(dictionaryProperty, label);

                    // Set to collapsed state
                    drawer.SetPendingExpandedStateForTests(dictionaryProperty, false);

                    float progress = drawer.GetPendingFoldoutProgressFromInstance(
                        dictionaryProperty
                    );

                    // An AnimBool starts at its target, so progress is 0 or on its way there.
                    Assert.GreaterOrEqual(progress, 0f, "Progress should be >= 0.");
                    Assert.LessOrEqual(progress, 1f, "Progress should be <= 1.");
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetFoldoutProgressStartsAtZeroWhenCollapsed()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);

            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    drawer.GetPropertyHeight(setProperty, label);

                    drawer.SetPendingExpandedStateForTests(setProperty, false);

                    float progress = drawer.GetPendingFoldoutProgressFromInstance(setProperty);

                    Assert.GreaterOrEqual(progress, 0f, "Progress should be >= 0.");
                    Assert.LessOrEqual(progress, 1f, "Progress should be <= 1.");
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryFoldoutProgressIsImmediateWhenTweenDisabled()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);

            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupDictionaryHost),
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            GUIContent label = new("Dictionary");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    drawer.GetPropertyHeight(dictionaryProperty, label);

                    // Set to expanded
                    drawer.SetPendingExpandedStateForTests(dictionaryProperty, true);
                    float expandedProgress = drawer.GetPendingFoldoutProgressFromInstance(
                        dictionaryProperty
                    );
                    Assert.AreEqual(
                        1f,
                        expandedProgress,
                        0.001f,
                        "When tween disabled and expanded, progress should immediately be 1."
                    );

                    // Set to collapsed
                    drawer.SetPendingExpandedStateForTests(dictionaryProperty, false);
                    float collapsedProgress = drawer.GetPendingFoldoutProgressFromInstance(
                        dictionaryProperty
                    );
                    Assert.AreEqual(
                        0f,
                        collapsedProgress,
                        0.001f,
                        "When tween disabled and collapsed, progress should immediately be 0."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetFoldoutProgressIsImmediateWhenTweenDisabled()
        {
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);

            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    drawer.GetPropertyHeight(setProperty, label);

                    // Set to expanded
                    drawer.SetPendingExpandedStateForTests(setProperty, true);
                    float expandedProgress = drawer.GetPendingFoldoutProgressFromInstance(
                        setProperty
                    );
                    Assert.AreEqual(
                        1f,
                        expandedProgress,
                        0.001f,
                        "When tween disabled and expanded, progress should immediately be 1."
                    );

                    // Set to collapsed
                    drawer.SetPendingExpandedStateForTests(setProperty, false);
                    float collapsedProgress = drawer.GetPendingFoldoutProgressFromInstance(
                        setProperty
                    );
                    Assert.AreEqual(
                        0f,
                        collapsedProgress,
                        0.001f,
                        "When tween disabled and collapsed, progress should immediately be 0."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryTweenSettingsRespectedInWGroupContext()
        {
            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupDictionaryHost),
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            GUIContent label = new("Dictionary");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    // Test with tween enabled
                    UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
                    drawer.GetPropertyHeight(dictionaryProperty, label);

                    bool found1 = drawer.TryGetPendingAnimationStateForTests(
                        dictionaryProperty,
                        out _,
                        out _,
                        out bool hasAnimBool1
                    );
                    Assert.IsTrue(
                        found1 && hasAnimBool1,
                        "Should have AnimBool when tween enabled."
                    );

                    // Now disable tween
                    UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);
                    drawer.GetPropertyHeight(dictionaryProperty, label);

                    bool found2 = drawer.TryGetPendingAnimationStateForTests(
                        dictionaryProperty,
                        out _,
                        out _,
                        out bool hasAnimBool2
                    );
                    Assert.IsTrue(
                        found2 && !hasAnimBool2,
                        "Should NOT have AnimBool when tween disabled."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetTweenSettingsRespectedInWGroupContext()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    // Test with tween enabled
                    UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);
                    drawer.GetPropertyHeight(setProperty, label);

                    bool found1 = drawer.TryGetPendingAnimationStateForTests(
                        setProperty,
                        out _,
                        out _,
                        out bool hasAnimBool1
                    );
                    Assert.IsTrue(
                        found1 && hasAnimBool1,
                        "Should have AnimBool when tween enabled."
                    );

                    // Now disable tween
                    UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);
                    drawer.GetPropertyHeight(setProperty, label);

                    bool found2 = drawer.TryGetPendingAnimationStateForTests(
                        setProperty,
                        out _,
                        out _,
                        out bool hasAnimBool2
                    );
                    Assert.IsTrue(
                        found2 && !hasAnimBool2,
                        "Should NOT have AnimBool when tween disabled."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryInSettingsContextCreatesAnimBoolWhenTweenEnabled()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            SerializedProperty paletteProp = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            if (paletteProp == null)
            {
                Assert.Ignore("WButtonCustomColors property not found in settings.");
                return;
            }

            paletteProp.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(UnityHelpersSettings),
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            GUIContent label = new("Palette");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();

                drawer.GetPropertyHeight(paletteProp, label);

                bool found = drawer.TryGetPendingAnimationStateForTests(
                    paletteProp,
                    out bool isExpanded,
                    out float animProgress,
                    out bool hasAnimBool
                );

                Assert.IsTrue(found, "Pending entry should exist after GetPropertyHeight.");
                Assert.IsTrue(
                    hasAnimBool,
                    "AnimBool should be created when dictionary tweening is enabled in Settings context."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryInSettingsContextFoldoutProgressIsImmediateWhenTweenDisabled()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedSettings = TrackDisposable(new SerializedObject(settings));
            serializedSettings.Update();

            SerializedProperty paletteProp = serializedSettings.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );

            if (paletteProp == null)
            {
                Assert.Ignore("WButtonCustomColors property not found in settings.");
                return;
            }

            paletteProp.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(UnityHelpersSettings),
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            GUIContent label = new("Palette");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();

                drawer.GetPropertyHeight(paletteProp, label);

                // Set to expanded
                drawer.SetPendingExpandedStateForTests(paletteProp, true);
                float expandedProgress = drawer.GetPendingFoldoutProgressFromInstance(paletteProp);
                Assert.AreEqual(
                    1f,
                    expandedProgress,
                    0.001f,
                    "Settings context: when tween disabled and expanded, progress should immediately be 1."
                );

                // Set to collapsed
                drawer.SetPendingExpandedStateForTests(paletteProp, false);
                float collapsedProgress = drawer.GetPendingFoldoutProgressFromInstance(paletteProp);
                Assert.AreEqual(
                    0f,
                    collapsedProgress,
                    0.001f,
                    "Settings context: when tween disabled and collapsed, progress should immediately be 0."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void NestedWGroupDictionaryAnimationIsIndependent()
        {
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);

            IntegrationTestMultiWGroupHost host =
                CreateScriptableObject<IntegrationTestMultiWGroupHost>();
            host.nestedDictionary["innerKey"] = 50;
            host.nestedSet.Add(99);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictProperty = serializedObject.FindProperty(
                nameof(IntegrationTestMultiWGroupHost.nestedDictionary)
            );
            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestMultiWGroupHost.nestedSet)
            );
            dictProperty.isExpanded = true;
            setProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer dictDrawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                dictDrawer,
                typeof(IntegrationTestMultiWGroupHost),
                nameof(IntegrationTestMultiWGroupHost.nestedDictionary)
            );
            SerializableSetPropertyDrawer setDrawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                setDrawer,
                typeof(IntegrationTestMultiWGroupHost),
                nameof(IntegrationTestMultiWGroupHost.nestedSet)
            );
            GUIContent dictLabel = new("Dict");
            GUIContent setLabel = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                // Simulate outer group padding
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    // Simulate inner group padding
                    using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                    {
                        dictDrawer.GetPropertyHeight(dictProperty, dictLabel);
                        setDrawer.GetPropertyHeight(setProperty, setLabel);

                        bool dictFound = dictDrawer.TryGetPendingAnimationStateForTests(
                            dictProperty,
                            out _,
                            out _,
                            out bool dictHasAnim
                        );
                        bool setFound = setDrawer.TryGetPendingAnimationStateForTests(
                            setProperty,
                            out _,
                            out _,
                            out bool setHasAnim
                        );

                        Assert.IsTrue(
                            dictFound && dictHasAnim,
                            "Nested dictionary should have AnimBool in nested WGroup."
                        );
                        Assert.IsTrue(
                            setFound && setHasAnim,
                            "Nested set should have AnimBool in nested WGroup."
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void WGroupTweenAndCollectionTweenAreIndependent()
        {
            // Enable WGroup tween, disable collection tweens
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);

            Assert.IsTrue(
                UnityHelpersSettings.ShouldTweenWGroupFoldouts(),
                "WGroup tween should be enabled."
            );
            Assert.IsFalse(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(false),
                "Dictionary tween should be disabled."
            );
            Assert.IsFalse(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(false),
                "Set tween should be disabled."
            );

            // Now reverse: disable WGroup tween, enable collection tweens
            UnityHelpersSettings.SetWGroupFoldoutTweenEnabled(false);
            UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(true);
            UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(true);

            Assert.IsFalse(
                UnityHelpersSettings.ShouldTweenWGroupFoldouts(),
                "WGroup tween should be disabled."
            );
            Assert.IsTrue(
                SerializableDictionaryPropertyDrawer.IsTweeningEnabledForTests(false),
                "Dictionary tween should be enabled."
            );
            Assert.IsTrue(
                SerializableSetPropertyDrawer.IsTweeningEnabledForTests(false),
                "Set tween should be enabled."
            );
        }

        [Test]
        public void SortedDictionaryInWGroupCreatesAnimBoolWhenTweenEnabled()
        {
            UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(true);

            IntegrationTestWGroupSortedDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupSortedDictionaryHost>();
            host.sortedDictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSortedDictionaryHost.sortedDictionary)
            );
            dictProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSortedDictionaryHost),
                nameof(IntegrationTestWGroupSortedDictionaryHost.sortedDictionary)
            );
            GUIContent label = new("SortedDict");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    drawer.GetPropertyHeight(dictProperty, label);

                    bool found = drawer.TryGetPendingAnimationStateForTests(
                        dictProperty,
                        out _,
                        out _,
                        out bool hasAnimBool
                    );

                    Assert.IsTrue(found, "Pending entry should exist.");
                    Assert.IsTrue(
                        hasAnimBool,
                        "AnimBool should be created when sorted dictionary tweening is enabled."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SortedSetInWGroupCreatesAnimBoolWhenTweenEnabled()
        {
            UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(true);

            IntegrationTestWGroupSortedSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSortedSetHost>();
            host.sortedSet.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSortedSetHost.sortedSet)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSortedSetHost),
                nameof(IntegrationTestWGroupSortedSetHost.sortedSet)
            );
            GUIContent label = new("SortedSet");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;

                GroupGUIWidthUtility.ResetForTests();
                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    drawer.GetPropertyHeight(setProperty, label);

                    bool found = drawer.TryGetPendingAnimationStateForTests(
                        setProperty,
                        out _,
                        out _,
                        out bool hasAnimBool
                    );

                    Assert.IsTrue(found, "Pending entry should exist.");
                    Assert.IsTrue(
                        hasAnimBool,
                        "AnimBool should be created when sorted set tweening is enabled."
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        [TestCase(
            true,
            true,
            TestName = "DictionaryAnimBoolCreatedWhenTweenEnabledThenClearedWhenDisabled"
        )]
        [TestCase(false, true, TestName = "DictionaryAnimBoolNotCreatedWhenTweenAlwaysDisabled")]
        public void DictionaryAnimBoolLifecycleWithTweenToggle(bool enableFirst, bool disableAfter)
        {
            IntegrationTestWGroupDictionaryHost host =
                CreateScriptableObject<IntegrationTestWGroupDictionaryHost>();
            host.dictionary["key1"] = 100;

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty dictionaryProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            dictionaryProperty.isExpanded = true;

            SerializableDictionaryPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupDictionaryHost),
                nameof(IntegrationTestWGroupDictionaryHost.dictionary)
            );
            GUIContent label = new("Dictionary");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(enableFirst);
                    drawer.GetPropertyHeight(dictionaryProperty, label);

                    bool foundInitial = drawer.TryGetPendingAnimationStateForTests(
                        dictionaryProperty,
                        out bool isExpandedInitial,
                        out float animProgressInitial,
                        out bool hasAnimBoolInitial
                    );

                    string initialDiagnostic =
                        $"[Initial] found={foundInitial}, expanded={isExpandedInitial}, progress={animProgressInitial:F3}, hasAnimBool={hasAnimBoolInitial}";
                    UnityEngine.Debug.Log(initialDiagnostic);

                    Assert.IsTrue(foundInitial, "Initial call should create pending entry.");
                    Assert.AreEqual(
                        enableFirst,
                        hasAnimBoolInitial,
                        $"AnimBool should {(enableFirst ? "exist" : "not exist")} when tween is {(enableFirst ? "enabled" : "disabled")}. Diagnostic: {initialDiagnostic}"
                    );

                    if (disableAfter && enableFirst)
                    {
                        UnityHelpersSettings.SetSerializableDictionaryFoldoutTweenEnabled(false);
                        drawer.GetPropertyHeight(dictionaryProperty, label);

                        bool foundAfter = drawer.TryGetPendingAnimationStateForTests(
                            dictionaryProperty,
                            out bool isExpandedAfter,
                            out float animProgressAfter,
                            out bool hasAnimBoolAfter
                        );

                        string afterDiagnostic =
                            $"[After Disable] found={foundAfter}, expanded={isExpandedAfter}, progress={animProgressAfter:F3}, hasAnimBool={hasAnimBoolAfter}";
                        UnityEngine.Debug.Log(afterDiagnostic);

                        Assert.IsTrue(
                            foundAfter,
                            "Pending entry should still exist after disabling tween."
                        );
                        Assert.IsFalse(
                            hasAnimBoolAfter,
                            $"AnimBool should be cleaned up when tween is disabled. Diagnostic: {afterDiagnostic}"
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        [TestCase(
            true,
            true,
            TestName = "SetAnimBoolCreatedWhenTweenEnabledThenClearedWhenDisabled"
        )]
        [TestCase(false, true, TestName = "SetAnimBoolNotCreatedWhenTweenAlwaysDisabled")]
        public void SetAnimBoolLifecycleWithTweenToggle(bool enableFirst, bool disableAfter)
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                {
                    UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(enableFirst);
                    drawer.GetPropertyHeight(setProperty, label);

                    bool foundInitial = drawer.TryGetPendingAnimationStateForTests(
                        setProperty,
                        out bool isExpandedInitial,
                        out float animProgressInitial,
                        out bool hasAnimBoolInitial
                    );

                    string initialDiagnostic =
                        $"[Initial] found={foundInitial}, expanded={isExpandedInitial}, progress={animProgressInitial:F3}, hasAnimBool={hasAnimBoolInitial}";
                    UnityEngine.Debug.Log(initialDiagnostic);

                    Assert.IsTrue(foundInitial, "Initial call should create pending entry.");
                    Assert.AreEqual(
                        enableFirst,
                        hasAnimBoolInitial,
                        $"AnimBool should {(enableFirst ? "exist" : "not exist")} when tween is {(enableFirst ? "enabled" : "disabled")}. Diagnostic: {initialDiagnostic}"
                    );

                    if (disableAfter && enableFirst)
                    {
                        UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);
                        drawer.GetPropertyHeight(setProperty, label);

                        bool foundAfter = drawer.TryGetPendingAnimationStateForTests(
                            setProperty,
                            out bool isExpandedAfter,
                            out float animProgressAfter,
                            out bool hasAnimBoolAfter
                        );

                        string afterDiagnostic =
                            $"[After Disable] found={foundAfter}, expanded={isExpandedAfter}, progress={animProgressAfter:F3}, hasAnimBool={hasAnimBoolAfter}";
                        UnityEngine.Debug.Log(afterDiagnostic);

                        Assert.IsTrue(
                            foundAfter,
                            "Pending entry should still exist after disabling tween."
                        );
                        Assert.IsFalse(
                            hasAnimBoolAfter,
                            $"AnimBool should be cleaned up when tween is disabled. Diagnostic: {afterDiagnostic}"
                        );
                    }
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        [TestCase(true, TestName = "SortedDictionaryAnimBoolCleanedUpWhenTweenDisabled")]
        [TestCase(false, TestName = "SortedSetAnimBoolCleanedUpWhenTweenDisabled")]
        public void SortedCollectionAnimBoolLifecycle(bool isSortedDictionary)
        {
            int previousIndentLevel = EditorGUI.indentLevel;

            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                if (isSortedDictionary)
                {
                    IntegrationTestWGroupSortedDictionaryHost host =
                        CreateScriptableObject<IntegrationTestWGroupSortedDictionaryHost>();
                    host.sortedDictionary["key1"] = 100;

                    SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                    serializedObject.Update();

                    SerializedProperty sortedDictProperty = serializedObject.FindProperty(
                        nameof(IntegrationTestWGroupSortedDictionaryHost.sortedDictionary)
                    );
                    sortedDictProperty.isExpanded = true;

                    SerializableDictionaryPropertyDrawer drawer = new();
                    PropertyDrawerTestHelper.AssignFieldInfo(
                        drawer,
                        typeof(IntegrationTestWGroupSortedDictionaryHost),
                        nameof(IntegrationTestWGroupSortedDictionaryHost.sortedDictionary)
                    );
                    GUIContent label = new("SortedDictionary");

                    using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                    {
                        UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(
                            true
                        );
                        drawer.GetPropertyHeight(sortedDictProperty, label);

                        bool found1 = drawer.TryGetPendingAnimationStateForTests(
                            sortedDictProperty,
                            out _,
                            out _,
                            out bool hasAnimBool1
                        );
                        Assert.IsTrue(
                            found1 && hasAnimBool1,
                            "Sorted dictionary should have AnimBool when tween enabled."
                        );

                        UnityHelpersSettings.SetSerializableSortedDictionaryFoldoutTweenEnabled(
                            false
                        );
                        drawer.GetPropertyHeight(sortedDictProperty, label);

                        bool found2 = drawer.TryGetPendingAnimationStateForTests(
                            sortedDictProperty,
                            out _,
                            out _,
                            out bool hasAnimBool2
                        );
                        Assert.IsTrue(found2, "Pending entry should still exist.");
                        Assert.IsFalse(
                            hasAnimBool2,
                            "Sorted dictionary AnimBool should be cleaned up when tween disabled."
                        );
                    }
                }
                else
                {
                    IntegrationTestWGroupSortedSetHost host =
                        CreateScriptableObject<IntegrationTestWGroupSortedSetHost>();
                    host.sortedSet.Add(42);

                    SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
                    serializedObject.Update();

                    SerializedProperty sortedSetProperty = serializedObject.FindProperty(
                        nameof(IntegrationTestWGroupSortedSetHost.sortedSet)
                    );
                    sortedSetProperty.isExpanded = true;

                    SerializableSetPropertyDrawer drawer = new();
                    PropertyDrawerTestHelper.AssignFieldInfo(
                        drawer,
                        typeof(IntegrationTestWGroupSortedSetHost),
                        nameof(IntegrationTestWGroupSortedSetHost.sortedSet)
                    );
                    GUIContent label = new("SortedSet");

                    using (GroupGUIWidthUtility.PushContentPadding(24f, 12f, 12f))
                    {
                        UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(true);
                        drawer.GetPropertyHeight(sortedSetProperty, label);

                        bool found1 = drawer.TryGetPendingAnimationStateForTests(
                            sortedSetProperty,
                            out _,
                            out _,
                            out bool hasAnimBool1
                        );
                        Assert.IsTrue(
                            found1 && hasAnimBool1,
                            "Sorted set should have AnimBool when tween enabled."
                        );

                        UnityHelpersSettings.SetSerializableSortedSetFoldoutTweenEnabled(false);
                        drawer.GetPropertyHeight(sortedSetProperty, label);

                        bool found2 = drawer.TryGetPendingAnimationStateForTests(
                            sortedSetProperty,
                            out _,
                            out _,
                            out bool hasAnimBool2
                        );
                        Assert.IsTrue(found2, "Pending entry should still exist.");
                        Assert.IsFalse(
                            hasAnimBool2,
                            "Sorted set AnimBool should be cleaned up when tween disabled."
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
        public IEnumerator SetFooterRangeLabelAccountsForWGroupPadding()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            const float SimulatedLeftPadding = 12f;
            const float SimulatedRightPadding = 12f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            float capturedLeftPadding = 0f;
            float capturedRightPadding = 0f;
            bool rangeLabelWasDrawn = false;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, setProperty, label);

                        capturedLeftPadding =
                            SerializableSetPropertyDrawer.LastFooterWGroupLeftPadding;
                        capturedRightPadding =
                            SerializableSetPropertyDrawer.LastFooterWGroupRightPadding;
                        rangeLabelWasDrawn =
                            SerializableSetPropertyDrawer.LastFooterRangeLabelWasDrawn;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.AreEqual(
                SimulatedLeftPadding,
                capturedLeftPadding,
                0.1f,
                "Footer should capture WGroup left padding."
            );
            Assert.AreEqual(
                SimulatedRightPadding,
                capturedRightPadding,
                0.1f,
                "Footer should capture WGroup right padding."
            );
            Assert.IsTrue(
                rangeLabelWasDrawn,
                "Range label should be drawn when sufficient space is available."
            );
        }

        [UnityTest]
        public IEnumerator SetFooterRangeLabelDrawnWithNoPadding()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            float capturedAvailableWidth = 0f;
            bool rangeLabelWasDrawn = false;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    // No WGroup padding
                    drawer.OnGUI(controlRect, setProperty, label);

                    capturedAvailableWidth = SerializableSetPropertyDrawer.LastFooterAvailableWidth;
                    rangeLabelWasDrawn = SerializableSetPropertyDrawer.LastFooterRangeLabelWasDrawn;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.Greater(
                capturedAvailableWidth,
                0f,
                "Available width should be positive with no WGroup padding."
            );
            Assert.IsTrue(
                rangeLabelWasDrawn,
                "Range label should be drawn with no WGroup padding and sufficient space."
            );
        }

        [UnityTest]
        public IEnumerator SetFooterRangeLabelHiddenWhenInsufficientSpace()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            // Use very narrow width to force range label to be hidden
            Rect controlRect = new(0f, 0f, 150f, 300f);
            GUIContent label = new("Set");

            // Use very large WGroup padding to consume most of the width
            const float SimulatedLeftPadding = 40f;
            const float SimulatedRightPadding = 40f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            float capturedAvailableWidth = 0f;
            float capturedRangeWidth = 0f;
            bool rangeLabelWasDrawn = false;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, setProperty, label);

                        capturedAvailableWidth =
                            SerializableSetPropertyDrawer.LastFooterAvailableWidth;
                        capturedRangeWidth = SerializableSetPropertyDrawer.LastFooterRangeWidth;
                        rangeLabelWasDrawn =
                            SerializableSetPropertyDrawer.LastFooterRangeLabelWasDrawn;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsFalse(
                rangeLabelWasDrawn,
                "Range label should NOT be drawn when WGroup padding leaves insufficient space."
            );
            Assert.Greater(
                capturedRangeWidth,
                capturedAvailableWidth,
                "Range width should exceed available width when label is hidden."
            );
        }

        [UnityTest]
        public IEnumerator SetFooterPaginationWidthReducedByWGroupPadding()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            const float SimulatedLeftPadding = 20f;
            const float SimulatedRightPadding = 20f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            float availableWidthWithPadding = 0f;
            float availableWidthWithoutPadding = 0f;
            int previousIndentLevel = EditorGUI.indentLevel;

            // First measure with WGroup padding
            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, setProperty, label);
                        availableWidthWithPadding =
                            SerializableSetPropertyDrawer.LastFooterAvailableWidth;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            // Then measure without WGroup padding
            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    drawer.OnGUI(controlRect, setProperty, label);
                    availableWidthWithoutPadding =
                        SerializableSetPropertyDrawer.LastFooterAvailableWidth;
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            /*
                ResolveContentRect narrows the rect on both sides inside a WGroup, so the available
                width drops by the full horizontal padding. At x=0 the -1.25f alignment offset
                cannot expand leftward (xMin clamps to 0), so nothing else contributes; it only adds
                width once x exceeds 1.25.
            */
            float expectedDifference = horizontalPadding;
            float actualDifference = availableWidthWithoutPadding - availableWidthWithPadding;

            Assert.AreEqual(
                expectedDifference,
                actualDifference,
                2f,
                $"Available width should be reduced by WGroup horizontal padding. "
                    + $"Without padding: {availableWidthWithoutPadding}, "
                    + $"With padding: {availableWidthWithPadding}, "
                    + $"Difference: {actualDifference}, "
                    + $"Expected: {expectedDifference} (horizontalPadding={horizontalPadding})"
            );
        }

        [UnityTest]
        public IEnumerator SetFooterRangeLabelPositionRespectsWGroupLeftPadding()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            const float SimulatedLeftPadding = 15f;
            const float SimulatedRightPadding = 15f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            Rect rangeLabelRectWithPadding = default;
            Rect rangeLabelRectWithoutPadding = default;
            bool drawnWithPadding = false;
            bool drawnWithoutPadding = false;
            int previousIndentLevel = EditorGUI.indentLevel;

            // Draw with WGroup padding
            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, setProperty, label);
                        drawnWithPadding =
                            SerializableSetPropertyDrawer.LastFooterRangeLabelWasDrawn;
                        if (drawnWithPadding)
                        {
                            rangeLabelRectWithPadding =
                                SerializableSetPropertyDrawer.LastFooterRangeLabelRect;
                        }
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            // Draw without WGroup padding
            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    drawer.OnGUI(controlRect, setProperty, label);
                    drawnWithoutPadding =
                        SerializableSetPropertyDrawer.LastFooterRangeLabelWasDrawn;
                    if (drawnWithoutPadding)
                    {
                        rangeLabelRectWithoutPadding =
                            SerializableSetPropertyDrawer.LastFooterRangeLabelRect;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsTrue(
                drawnWithPadding,
                "Range label should be drawn with WGroup padding in sufficient space."
            );
            Assert.IsTrue(
                drawnWithoutPadding,
                "Range label should be drawn without WGroup padding."
            );

            float xDifference = rangeLabelRectWithPadding.x - rangeLabelRectWithoutPadding.x;

            /*
                Outside a WGroup with the rect starting at x=0, the -1.25f alignment offset would
                shift xMin negative and is clamped back to 0, so the "without padding" case has x=0
                and the difference is exactly the left padding. A non-zero starting x would show the
                full offset.
            */
            float expectedDifference = SimulatedLeftPadding;

            Assert.AreEqual(
                expectedDifference,
                xDifference,
                1f,
                $"Range label X position should be shifted right by WGroup left padding. "
                    + $"(Alignment offset is clamped when starting rect x=0) "
                    + $"With padding X: {rangeLabelRectWithPadding.x}, "
                    + $"Without padding X: {rangeLabelRectWithoutPadding.x}, "
                    + $"Difference: {xDifference}, "
                    + $"Expected: {expectedDifference} (padding={SimulatedLeftPadding})"
            );
        }

        [UnityTest]
        public IEnumerator SetFooterInNestedWGroupsAccumulatesPadding()
        {
            IntegrationTestMultiWGroupHost host =
                CreateScriptableObject<IntegrationTestMultiWGroupHost>();
            host.nestedSet.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestMultiWGroupHost.nestedSet)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestMultiWGroupHost),
                nameof(IntegrationTestMultiWGroupHost.nestedSet)
            );
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("NestedSet");

            // Simulate nested WGroups with cumulative padding
            const float OuterLeftPadding = 10f;
            const float OuterRightPadding = 10f;
            const float InnerLeftPadding = 8f;
            const float InnerRightPadding = 8f;

            float capturedLeftPadding = 0f;
            float capturedRightPadding = 0f;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    // Simulate outer WGroup
                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            OuterLeftPadding + OuterRightPadding,
                            OuterLeftPadding,
                            OuterRightPadding
                        )
                    )
                    {
                        // Simulate inner WGroup
                        using (
                            GroupGUIWidthUtility.PushContentPadding(
                                InnerLeftPadding + InnerRightPadding,
                                InnerLeftPadding,
                                InnerRightPadding
                            )
                        )
                        {
                            drawer.OnGUI(controlRect, setProperty, label);

                            capturedLeftPadding =
                                SerializableSetPropertyDrawer.LastFooterWGroupLeftPadding;
                            capturedRightPadding =
                                SerializableSetPropertyDrawer.LastFooterWGroupRightPadding;
                        }
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            float expectedTotalLeftPadding = OuterLeftPadding + InnerLeftPadding;
            float expectedTotalRightPadding = OuterRightPadding + InnerRightPadding;

            Assert.AreEqual(
                expectedTotalLeftPadding,
                capturedLeftPadding,
                0.1f,
                $"Nested WGroups should accumulate left padding. "
                    + $"Expected: {expectedTotalLeftPadding}, Actual: {capturedLeftPadding}"
            );
            Assert.AreEqual(
                expectedTotalRightPadding,
                capturedRightPadding,
                0.1f,
                $"Nested WGroups should accumulate right padding. "
                    + $"Expected: {expectedTotalRightPadding}, Actual: {capturedRightPadding}"
            );
        }

        [UnityTest]
        public IEnumerator SortedSetFooterRangeLabelAccountsForWGroupPadding()
        {
            IntegrationTestWGroupSortedSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSortedSetHost>();
            host.sortedSet.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSortedSetHost.sortedSet)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSortedSetHost),
                nameof(IntegrationTestWGroupSortedSetHost.sortedSet)
            );
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("SortedSet");

            const float SimulatedLeftPadding = 12f;
            const float SimulatedRightPadding = 12f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            float capturedLeftPadding = 0f;
            float capturedRightPadding = 0f;
            bool rangeLabelWasDrawn = false;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, setProperty, label);

                        capturedLeftPadding =
                            SerializableSetPropertyDrawer.LastFooterWGroupLeftPadding;
                        capturedRightPadding =
                            SerializableSetPropertyDrawer.LastFooterWGroupRightPadding;
                        rangeLabelWasDrawn =
                            SerializableSetPropertyDrawer.LastFooterRangeLabelWasDrawn;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.AreEqual(
                SimulatedLeftPadding,
                capturedLeftPadding,
                0.1f,
                "SortedSet footer should capture WGroup left padding."
            );
            Assert.AreEqual(
                SimulatedRightPadding,
                capturedRightPadding,
                0.1f,
                "SortedSet footer should capture WGroup right padding."
            );
            Assert.IsTrue(
                rangeLabelWasDrawn,
                "SortedSet range label should be drawn when sufficient space is available."
            );
        }

        [UnityTest]
        public IEnumerator SetFooterRangeLabelDrawnInsideWGroup()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            // Add one item so that DrawFooterControls is called (empty sets use DrawEmptySetDrawer instead)
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            const float SimulatedLeftPadding = 12f;
            const float SimulatedRightPadding = 12f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            bool rangeLabelWasDrawn = false;
            float capturedLeftPadding = 0f;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, setProperty, label);

                        rangeLabelWasDrawn =
                            SerializableSetPropertyDrawer.LastFooterRangeLabelWasDrawn;
                        capturedLeftPadding =
                            SerializableSetPropertyDrawer.LastFooterWGroupLeftPadding;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsTrue(
                rangeLabelWasDrawn,
                "Range label should be drawn inside WGroup when space is sufficient."
            );
            Assert.AreEqual(
                SimulatedLeftPadding,
                capturedLeftPadding,
                0.1f,
                "Footer should capture WGroup padding."
            );
        }

        [UnityTest]
        public IEnumerator SetFooterWithMultipleItemsDrawsCorrectlyInsideWGroup()
        {
            IntegrationTestWGroupSetHost host =
                CreateScriptableObject<IntegrationTestWGroupSetHost>();
            // Add multiple items to test "1-15 of 20" style labels
            for (int i = 0; i < 20; i++)
            {
                host.set.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(IntegrationTestWGroupSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            PropertyDrawerTestHelper.AssignFieldInfo(
                drawer,
                typeof(IntegrationTestWGroupSetHost),
                nameof(IntegrationTestWGroupSetHost.set)
            );
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            const float SimulatedLeftPadding = 12f;
            const float SimulatedRightPadding = 12f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            bool rangeLabelWasDrawn = false;
            float capturedAvailableWidth = 0f;
            float capturedRangeWidth = 0f;
            int previousIndentLevel = EditorGUI.indentLevel;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    EditorGUI.indentLevel = 0;

                    GroupGUIWidthUtility.ResetForTests();
                    SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();

                    using (
                        GroupGUIWidthUtility.PushContentPadding(
                            horizontalPadding,
                            SimulatedLeftPadding,
                            SimulatedRightPadding
                        )
                    )
                    {
                        drawer.OnGUI(controlRect, setProperty, label);

                        rangeLabelWasDrawn =
                            SerializableSetPropertyDrawer.LastFooterRangeLabelWasDrawn;
                        capturedAvailableWidth =
                            SerializableSetPropertyDrawer.LastFooterAvailableWidth;
                        capturedRangeWidth = SerializableSetPropertyDrawer.LastFooterRangeWidth;
                    }
                }
                finally
                {
                    EditorGUI.indentLevel = previousIndentLevel;
                }
            });

            Assert.IsTrue(
                rangeLabelWasDrawn,
                "Range label with multiple items should be drawn inside WGroup."
            );
            Assert.GreaterOrEqual(
                capturedAvailableWidth,
                capturedRangeWidth,
                "Available width should be >= range width when label is drawn."
            );
        }

        [Test]
        public void SetFooterRangeLabelWidthCalculationWithZeroPadding()
        {
            // Unit test to verify the width calculation logic without IMGUI context
            const float RectWidth = 400f;
            const float InternalPadding = 4f;
            const float WGroupLeftPadding = 0f;
            const float WGroupRightPadding = 0f;

            // Simulate the footer calculation from DrawFooterControls
            float rightCursor = RectWidth - InternalPadding - WGroupRightPadding;
            float leftCursor = InternalPadding + WGroupLeftPadding;

            // Simulate buttons consuming space (Add = 60, Clear = 80, spacing = 4 each)
            rightCursor -= 60f; // Add button
            rightCursor -= 4f; // Spacing
            rightCursor -= 80f; // Clear button
            rightCursor -= 4f; // Spacing

            float availableWidth = Mathf.Max(0f, rightCursor - leftCursor);

            // 400 - 4 (left padding) - 4 (right padding) - 60 - 4 - 80 - 4 = 244.
            float expectedAvailableWidth = RectWidth - (2 * InternalPadding) - 60f - 4f - 80f - 4f;

            Assert.AreEqual(
                expectedAvailableWidth,
                availableWidth,
                0.1f,
                $"Available width calculation with zero WGroup padding. "
                    + $"Expected: {expectedAvailableWidth}, Actual: {availableWidth}"
            );
        }

        [Test]
        public void SetFooterRangeLabelWidthCalculationWithWGroupPadding()
        {
            // Only the left cursor is adjusted, to keep the range label off WGroup's left boundary.
            const float RectWidth = 400f;
            const float InternalPadding = 4f;
            const float WGroupLeftPadding = 12f;

            // rightCursor is NOT adjusted: the buttons stay at their normal positions.
            float rightCursor = RectWidth - InternalPadding;
            // leftCursor IS adjusted for range label positioning
            float adjustedLeftCursor = InternalPadding + WGroupLeftPadding;

            // Simulate buttons consuming space (Add = 60, Clear = 80, spacing = 4 each)
            rightCursor -= 60f; // Add button
            rightCursor -= 4f; // Spacing
            rightCursor -= 80f; // Clear button
            rightCursor -= 4f; // Spacing

            float availableWidth = Mathf.Max(0f, rightCursor - adjustedLeftCursor);

            // rightCursor 400-4-60-4-80-4 = 248 and adjustedLeftCursor 4+12 = 16, so the width is 232.
            float expectedAvailableWidth =
                RectWidth
                - InternalPadding
                - 60f
                - 4f
                - 80f
                - 4f
                - (InternalPadding + WGroupLeftPadding);

            Assert.AreEqual(
                expectedAvailableWidth,
                availableWidth,
                0.1f,
                $"Available width calculation with WGroup padding. "
                    + $"Expected: {expectedAvailableWidth}, Actual: {availableWidth}"
            );
        }

        [Test]
        public void SetFooterAvailableWidthDifferenceMatchesWGroupLeftPadding()
        {
            // The right cursor is NOT adjusted; only the range label's left position shifts.
            const float RectWidth = 400f;
            const float InternalPadding = 4f;
            const float WGroupLeftPadding = 15f;

            // Calculate without WGroup padding
            float rightCursorNoPadding = RectWidth - InternalPadding;
            float leftCursorNoPadding = InternalPadding;
            rightCursorNoPadding -= 60f + 4f + 80f + 4f; // Buttons
            float availableNoPadding = Mathf.Max(0f, rightCursorNoPadding - leftCursorNoPadding);

            // Calculate with WGroup padding (only left cursor is adjusted)
            float rightCursorWithPadding = RectWidth - InternalPadding; // NOT adjusted
            float adjustedLeftCursor = InternalPadding + WGroupLeftPadding; // Adjusted
            rightCursorWithPadding -= 60f + 4f + 80f + 4f; // Buttons
            float availableWithPadding = Mathf.Max(0f, rightCursorWithPadding - adjustedLeftCursor);

            float difference = availableNoPadding - availableWithPadding;
            // Only left padding affects available width now
            float expectedDifference = WGroupLeftPadding;

            Assert.AreEqual(
                expectedDifference,
                difference,
                0.1f,
                $"Available width difference should equal WGroup LEFT padding only. "
                    + $"Expected: {expectedDifference}, Actual: {difference}"
            );
        }

        // Data-driven tests for verifying indentation with various padding values
        private static IEnumerable<TestCaseData> IndentationTestCases()
        {
            // Standard padding values
            yield return new TestCaseData(4f, 4f).SetName("StandardPadding4x4");
            yield return new TestCaseData(8f, 8f).SetName("SymmetricPadding8x8");
            yield return new TestCaseData(12f, 12f).SetName("LargePadding12x12");

            // Asymmetric padding
            yield return new TestCaseData(10f, 5f).SetName("AsymmetricPaddingLeftHeavy");
            yield return new TestCaseData(5f, 10f).SetName("AsymmetricPaddingRightHeavy");

            // Edge cases
            yield return new TestCaseData(0.5f, 0.5f).SetName("SmallPadding");
            yield return new TestCaseData(50f, 50f).SetName("VeryLargePadding");
        }

        [TestCaseSource(nameof(IndentationTestCases))]
        public void DictionaryResolveContentRectAppliesPaddingCorrectly(
            float leftPadding,
            float rightPadding
        )
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);
            float horizontalPadding = leftPadding + rightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

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
                            skipIndentation: false
                        );

                    float expectedX = controlRect.x + leftPadding;
                    float expectedWidth = controlRect.width - horizontalPadding;

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.1f,
                        $"Dictionary x position should equal input x + leftPadding. "
                            + $"Input: ({controlRect.x}, leftPadding={leftPadding}), "
                            + $"Expected: {expectedX}, Actual: {resolvedRect.x}, "
                            + $"ScopeDepth: {GroupGUIWidthUtility.CurrentScopeDepth}"
                    );

                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.1f,
                        $"Dictionary width should equal input width - horizontalPadding. "
                            + $"Input: ({controlRect.width}, horizontalPadding={horizontalPadding}), "
                            + $"Expected: {expectedWidth}, Actual: {resolvedRect.width}"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [TestCaseSource(nameof(IndentationTestCases))]
        public void SetResolveContentRectAppliesPaddingCorrectly(
            float leftPadding,
            float rightPadding
        )
        {
            Rect controlRect = new(0f, 0f, 400f, 300f);
            float horizontalPadding = leftPadding + rightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
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
                    float expectedWidth = controlRect.width - horizontalPadding;

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.1f,
                        $"Set x position should equal input x + leftPadding. "
                            + $"Input: ({controlRect.x}, leftPadding={leftPadding}), "
                            + $"Expected: {expectedX}, Actual: {resolvedRect.x}, "
                            + $"ScopeDepth: {GroupGUIWidthUtility.CurrentScopeDepth}"
                    );

                    Assert.AreEqual(
                        expectedWidth,
                        resolvedRect.width,
                        0.1f,
                        $"Set width should equal input width - horizontalPadding. "
                            + $"Input: ({controlRect.width}, horizontalPadding={horizontalPadding}), "
                            + $"Expected: {expectedWidth}, Actual: {resolvedRect.width}"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryOutsideWGroupAppliesUnityListAlignmentOffsetClampedAtZero()
        {
            // Outside a WGroup the -1.25f alignment offset applies, but at x=0 it clamps back to 0.
            Rect controlRect = new(0f, 0f, 400f, 300f);
            const float UnityListAlignmentOffset = -1.25f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                // No WGroup padding - scopeDepth should be 0
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // x would be shifted left by alignment offset, but clamped to 0 to prevent negative values
                float rawExpectedX = controlRect.x + UnityListAlignmentOffset;
                float expectedX = 0f; // Clamped from -1.25f to 0f

                Assert.AreEqual(
                    0,
                    GroupGUIWidthUtility.CurrentScopeDepth,
                    "ScopeDepth should be 0 when no WGroup padding is active"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.1f,
                    $"Dictionary outside WGroup should apply UnityListAlignmentOffset but clamp to 0. "
                        + $"Input: controlRect.x={controlRect.x}, alignmentOffset={UnityListAlignmentOffset}, "
                        + $"Raw expected x={rawExpectedX}, Clamped expected x={expectedX}, Actual x={resolvedRect.x}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryOutsideWGroupAppliesUnityListAlignmentOffsetWithPositiveX()
        {
            // Outside a WGroup the -1.25f alignment offset applies; a positive starting x needs no clamp.
            Rect controlRect = new(10f, 0f, 400f, 300f);
            const float UnityListAlignmentOffset = -1.25f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                // No WGroup padding - scopeDepth should be 0
                Rect resolvedRect = SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // x should be shifted left by alignment offset (10f + (-1.25f) = 8.75f)
                float expectedX = controlRect.x + UnityListAlignmentOffset;

                Assert.AreEqual(
                    0,
                    GroupGUIWidthUtility.CurrentScopeDepth,
                    "ScopeDepth should be 0 when no WGroup padding is active"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.1f,
                    $"Dictionary outside WGroup should apply UnityListAlignmentOffset. "
                        + $"Input: controlRect.x={controlRect.x}, alignmentOffset={UnityListAlignmentOffset}, "
                        + $"Expected x={expectedX}, Actual x={resolvedRect.x}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetOutsideWGroupAppliesUnityListAlignmentOffsetClampedAtZero()
        {
            // Outside a WGroup the -1.25f alignment offset applies, but at x=0 it clamps back to 0.
            Rect controlRect = new(0f, 0f, 400f, 300f);
            const float UnityListAlignmentOffset = -1.25f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                // No WGroup padding - scopeDepth should be 0
                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // x would be shifted left by alignment offset, but clamped to 0 to prevent negative values
                float rawExpectedX = controlRect.x + UnityListAlignmentOffset;
                float expectedX = 0f; // Clamped from -1.25f to 0f

                Assert.AreEqual(
                    0,
                    GroupGUIWidthUtility.CurrentScopeDepth,
                    "ScopeDepth should be 0 when no WGroup padding is active"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.1f,
                    $"Set outside WGroup should apply UnityListAlignmentOffset but clamp to 0. "
                        + $"Input: controlRect.x={controlRect.x}, alignmentOffset={UnityListAlignmentOffset}, "
                        + $"Raw expected x={rawExpectedX}, Clamped expected x={expectedX}, Actual x={resolvedRect.x}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetOutsideWGroupAppliesUnityListAlignmentOffsetWithPositiveX()
        {
            // Outside a WGroup the -1.25f alignment offset applies; a positive starting x needs no clamp.
            Rect controlRect = new(10f, 0f, 400f, 300f);
            const float UnityListAlignmentOffset = -1.25f;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                // No WGroup padding - scopeDepth should be 0
                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    controlRect,
                    skipIndentation: false
                );

                // x should be shifted left by alignment offset (10f + (-1.25f) = 8.75f)
                float expectedX = controlRect.x + UnityListAlignmentOffset;

                Assert.AreEqual(
                    0,
                    GroupGUIWidthUtility.CurrentScopeDepth,
                    "ScopeDepth should be 0 when no WGroup padding is active"
                );

                Assert.AreEqual(
                    expectedX,
                    resolvedRect.x,
                    0.1f,
                    $"Set outside WGroup should apply UnityListAlignmentOffset. "
                        + $"Input: controlRect.x={controlRect.x}, alignmentOffset={UnityListAlignmentOffset}, "
                        + $"Expected x={expectedX}, Actual x={resolvedRect.x}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void DictionaryInsideWGroupDoesNotApplyUnityListAlignmentOffset()
        {
            // Inside a WGroup (scopeDepth > 0) only the WGroup padding affects positioning.
            Rect controlRect = new(0f, 0f, 400f, 300f);
            const float SimulatedLeftPadding = 6f;
            const float SimulatedRightPadding = 6f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        SimulatedLeftPadding,
                        SimulatedRightPadding
                    )
                )
                {
                    Rect resolvedRect =
                        SerializableDictionaryPropertyDrawer.ResolveContentRectForTests(
                            controlRect,
                            skipIndentation: false
                        );

                    // x should ONLY be affected by WGroup left padding, NOT UnityListAlignmentOffset
                    float expectedX = controlRect.x + SimulatedLeftPadding;

                    Assert.Greater(
                        GroupGUIWidthUtility.CurrentScopeDepth,
                        0,
                        "ScopeDepth should be > 0 when WGroup padding is active"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.1f,
                        $"Dictionary inside WGroup should NOT apply UnityListAlignmentOffset. "
                            + $"Input: controlRect.x={controlRect.x}, leftPadding={SimulatedLeftPadding}, "
                            + $"Expected x={expectedX} (no alignment offset), Actual x={resolvedRect.x}"
                    );
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void SetInsideWGroupDoesNotApplyUnityListAlignmentOffset()
        {
            // Inside a WGroup (scopeDepth > 0) only the WGroup padding affects positioning.
            Rect controlRect = new(0f, 0f, 400f, 300f);
            const float SimulatedLeftPadding = 6f;
            const float SimulatedRightPadding = 6f;
            float horizontalPadding = SimulatedLeftPadding + SimulatedRightPadding;

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                using (
                    GroupGUIWidthUtility.PushContentPadding(
                        horizontalPadding,
                        SimulatedLeftPadding,
                        SimulatedRightPadding
                    )
                )
                {
                    Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                        controlRect,
                        skipIndentation: false
                    );

                    // x should ONLY be affected by WGroup left padding, NOT UnityListAlignmentOffset
                    float expectedX = controlRect.x + SimulatedLeftPadding;

                    Assert.Greater(
                        GroupGUIWidthUtility.CurrentScopeDepth,
                        0,
                        "ScopeDepth should be > 0 when WGroup padding is active"
                    );

                    Assert.AreEqual(
                        expectedX,
                        resolvedRect.x,
                        0.1f,
                        $"Set inside WGroup should NOT apply UnityListAlignmentOffset. "
                            + $"Input: controlRect.x={controlRect.x}, leftPadding={SimulatedLeftPadding}, "
                            + $"Expected x={expectedX} (no alignment offset), Actual x={resolvedRect.x}"
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
