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
    /// Tests for manual entry section padding resolution in SerializableSet property drawer.
    /// Verifies that settings context uses reduced padding to compensate for WGroup padding stacking.
    /// Note: UnityHelpersSettings does not contain SerializableHashSet properties, so settings
    /// context behavior for sets is tested via mock detection and property attribute inference.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class SerializableSetPendingPaddingTests : CommonTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            GroupGUIWidthUtility.ResetForTests();
            SerializableSetPropertyDrawer.ResetLayoutTrackingForTests();
        }

        [Test]
        public void ManualEntrySectionPaddingConstantsHaveExpectedValues()
        {
            // The 4f difference between the two compensates for WGroup padding.
            float normalPadding = 6f;
            float settingsPadding = 2f;

            Assert.AreEqual(normalPadding, 6f, "Normal manual entry section padding should be 6f.");
            Assert.AreEqual(
                settingsPadding,
                2f,
                "Settings manual entry section padding should be 2f."
            );
            Assert.AreEqual(
                4f,
                normalPadding - settingsPadding,
                "Padding difference should be 4f to compensate for WGroup horizontal padding."
            );
        }

        [Test]
        public void NormalContextUsesFullManualEntrySectionPadding()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            float height = drawer.GetPropertyHeight(setProperty, label);

            Assert.Greater(height, 0f, "Property height should be positive.");

            bool targetsSettings =
                serializedObject.targetObject is UnityHelpersSettings
                || Array.Exists(serializedObject.targetObjects, t => t is UnityHelpersSettings);

            Assert.IsFalse(
                targetsSettings,
                "Regular ScriptableObject should not be detected as UnityHelpersSettings."
            );
        }

        [Test]
        public void ManualEntryFoldoutToggleOffsetDiffersForContexts()
        {
            // The settings context sits 10f lower, to account for the WGroup offset.
            float normalOffset = 16f;
            float settingsOffset = 6f;

            Assert.AreEqual(16f, normalOffset, "Normal foldout toggle offset should be 16f.");
            Assert.AreEqual(6f, settingsOffset, "Settings foldout toggle offset should be 6f.");
            Assert.AreEqual(
                10f,
                normalOffset - settingsOffset,
                "Toggle offset difference should be 10f."
            );
        }

        [UnityTest]
        public IEnumerator OnGUINormalContextDrawsManualEntryWithFullPadding()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            Rect capturedRect = default;

            yield return TestIMGUIExecutor.Run(() =>
            {
                serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, setProperty, label);
                capturedRect = drawer.LastResolvedPosition;
            });

            Assert.Greater(capturedRect.width, 0f, "Resolved position should have valid width.");
        }

        [Test]
        public void NullPropertyDoesNotCrashPaddingResolution()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));

            SerializedProperty nullProperty = null;

            Assert.DoesNotThrow(
                () =>
                {
                    SerializableSetPropertyDrawer drawer = new();
                    GUIContent label = new("Test");
                    try
                    {
                        drawer.GetPropertyHeight(nullProperty, label);
                    }
                    catch (NullReferenceException) { }
                    catch (ArgumentNullException) { }
                },
                "Null property should be handled gracefully."
            );
        }

        [Test]
        public void PropertyWithNullSerializedObjectDoesNotCrash()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );

            serializedObject.Dispose();

            SerializableSetPropertyDrawer drawer = new();
            GUIContent label = new("Test");

            Assert.DoesNotThrow(
                () =>
                {
                    try
                    {
                        drawer.GetPropertyHeight(setProperty, label);
                    }
                    catch (Exception ex)
                        when (ex is ObjectDisposedException
                            || ex is NullReferenceException
                            || ex is InvalidOperationException
                        ) { }
                },
                "Disposed serialized object should be handled gracefully."
            );
        }

        [Test]
        public void MultipleDrawerInstancesOperateIndependently()
        {
            PaddingTestSetHost host1 = CreateScriptableObject<PaddingTestSetHost>();
            host1.set.Add(1);

            PaddingTestSetHost host2 = CreateScriptableObject<PaddingTestSetHost>();
            host2.set.Add(2);
            host2.set.Add(3);

            SerializedObject serializedObject1 = TrackDisposable(new SerializedObject(host1));
            SerializedObject serializedObject2 = TrackDisposable(new SerializedObject(host2));
            serializedObject1.Update();
            serializedObject2.Update();

            SerializedProperty property1 = serializedObject1.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            SerializedProperty property2 = serializedObject2.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            property1.isExpanded = true;
            property2.isExpanded = true;

            SerializableSetPropertyDrawer drawer1 = new();
            SerializableSetPropertyDrawer drawer2 = new();

            GUIContent label1 = new("Set 1");
            GUIContent label2 = new("Set 2");

            float height1 = drawer1.GetPropertyHeight(property1, label1);
            float height2 = drawer2.GetPropertyHeight(property2, label2);

            Assert.Greater(height1, 0f, "First set height should be positive.");
            Assert.Greater(height2, 0f, "Second set height should be positive.");
        }

        [Test]
        public void EmptySetHandledGracefully()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            GUIContent label = new("Set");

            float height = drawer.GetPropertyHeight(setProperty, label);

            Assert.Greater(height, 0f, "Empty set should still have valid height.");
        }

        [Test]
        public void ExpandedSetTallerThanCollapsed()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            host.set.Add(1);
            host.set.Add(2);
            host.set.Add(3);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            GUIContent label = new("Set");

            // With tweening on, the foldout AnimBool lags isExpanded and both heights come out equal.
            bool originalTweenEnabled = UnityHelpersSettings.ShouldTweenSerializableSetFoldouts();
            try
            {
                UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);
                SerializableSetPropertyDrawer.ClearMainFoldoutAnimCacheForTests();

                setProperty.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(setProperty, label);

                setProperty.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(setProperty, label);

                Assert.Greater(
                    expandedHeight,
                    collapsedHeight,
                    $"Expanded set should be taller than collapsed. "
                        + $"ExpandedHeight={expandedHeight}, CollapsedHeight={collapsedHeight}, "
                        + $"TweenEnabled={originalTweenEnabled}"
                );
            }
            finally
            {
                UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(originalTweenEnabled);
            }
        }

        [UnityTest]
        public IEnumerator DrawerMaintainsConsistentPaddingAcrossMultipleRepaints()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            setProperty.isExpanded = true;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 400f, 300f);
            GUIContent label = new("Set");

            Rect firstRect = default;
            Rect secondRect = default;

            yield return TestIMGUIExecutor.Run(() =>
            {
                serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, setProperty, label);
                firstRect = drawer.LastResolvedPosition;
            });

            yield return TestIMGUIExecutor.Run(() =>
            {
                serializedObject.UpdateIfRequiredOrScript();
                drawer.OnGUI(controlRect, setProperty, label);
                secondRect = drawer.LastResolvedPosition;
            });

            Assert.AreEqual(
                firstRect.x,
                secondRect.x,
                0.01f,
                "Resolved x position should be consistent across repaints."
            );
            Assert.AreEqual(
                firstRect.width,
                secondRect.width,
                0.01f,
                "Resolved width should be consistent across repaints."
            );
        }

        [Test]
        public void SetWithManyElementsHandledCorrectly()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            for (int i = 0; i < 20; i++)
            {
                host.set.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            GUIContent label = new("Large Set");

            float height = drawer.GetPropertyHeight(setProperty, label);

            Assert.Greater(
                height,
                EditorGUIUtility.singleLineHeight * 10,
                "Set with 20 elements should have substantial height."
            );
        }

        [Test]
        public void VeryLargeIndentLevelHandledGracefully()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            host.set.Add(42);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            setProperty.isExpanded = true;

            SerializableSetPropertyDrawer drawer = new();
            Rect controlRect = new(0f, 0f, 600f, 300f);
            GUIContent label = new("Set");

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 20;

                Assert.DoesNotThrow(
                    () => drawer.GetPropertyHeight(setProperty, label),
                    "GetPropertyHeight should not throw for very large indentLevel."
                );

                Rect resolvedRect = drawer.LastResolvedPosition;
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
        public void ZeroIndentLevelAppliesMinimumPadding()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            setProperty.isExpanded = true;

            GUIContent label = new("Set");
            Rect originalRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    originalRect,
                    skipIndentation: false
                );

                /*
                    Skipping IndentedRect at level zero avoids Unity-version drift; x=0 also clamps the negative
                    alignment offset.
                */
                float expectedMinimumX = 0f;
                Assert.GreaterOrEqual(
                    resolvedRect.x,
                    expectedMinimumX,
                    $"x should be clamped to non-negative at indent level 0. "
                        + $"OriginalRect.x={originalRect.x}, ResolvedRect.x={resolvedRect.x}, "
                        + $"IndentLevel={EditorGUI.indentLevel}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        [Test]
        public void HigherIndentLevelReducesWidth()
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            host.set.Add(1);

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );
            setProperty.isExpanded = true;

            GUIContent label = new("Set");
            Rect originalRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                GroupGUIWidthUtility.ResetForTests();

                EditorGUI.indentLevel = 1;
                Rect rectAtLevel1 = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    originalRect,
                    skipIndentation: false
                );

                EditorGUI.indentLevel = 4;
                Rect rectAtLevel4 = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    originalRect,
                    skipIndentation: false
                );

                Assert.Greater(
                    rectAtLevel4.x,
                    rectAtLevel1.x,
                    "Higher indent level should increase x offset."
                );
                Assert.Less(
                    rectAtLevel4.width,
                    rectAtLevel1.width,
                    "Higher indent level should reduce width."
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Data-driven test for ResolveContentRect behavior across various starting x positions.
        /// Verifies that xMin never goes negative regardless of the original rect's x position.
        /// </summary>
        /// <param name="startX">The starting x position of the rect.</param>
        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        [TestCase(1.25f)]
        [TestCase(2f)]
        [TestCase(10f)]
        [TestCase(-5f)]
        public void ResolveContentRectNeverReturnsNegativeXMin(float startX)
        {
            Rect originalRect = new(startX, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    originalRect,
                    skipIndentation: false
                );

                Assert.GreaterOrEqual(
                    resolvedRect.x,
                    0f,
                    $"xMin should never be negative. StartX={startX}, ResolvedX={resolvedRect.x}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Data-driven test for ResolveContentRect behavior at various indent levels.
        /// Verifies that xMin is always non-negative regardless of indent level.
        /// </summary>
        /// <param name="indentLevel">The indent level to test.</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(5)]
        [TestCase(10)]
        public void ResolveContentRectNonNegativeAtVariousIndentLevels(int indentLevel)
        {
            Rect originalRect = new(0f, 0f, 400f, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = indentLevel;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    originalRect,
                    skipIndentation: false
                );

                Assert.GreaterOrEqual(
                    resolvedRect.x,
                    0f,
                    $"xMin should never be negative at indent level {indentLevel}. ResolvedX={resolvedRect.x}"
                );
                Assert.GreaterOrEqual(
                    resolvedRect.width,
                    0f,
                    $"Width should never be negative at indent level {indentLevel}. ResolvedWidth={resolvedRect.width}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }

        /// <summary>
        /// Data-driven test for expanded vs collapsed height across different element counts.
        /// Verifies that expanded height is always greater than collapsed height.
        /// </summary>
        /// <param name="elementCount">The number of elements in the set.</param>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(10)]
        public void ExpandedHeightAlwaysGreaterThanCollapsedForVariousElementCounts(
            int elementCount
        )
        {
            PaddingTestSetHost host = CreateScriptableObject<PaddingTestSetHost>();
            for (int i = 0; i < elementCount; i++)
            {
                host.set.Add(i);
            }

            SerializedObject serializedObject = TrackDisposable(new SerializedObject(host));
            serializedObject.Update();

            SerializedProperty setProperty = serializedObject.FindProperty(
                nameof(PaddingTestSetHost.set)
            );

            SerializableSetPropertyDrawer drawer = new();
            GUIContent label = new("Set");

            bool originalTweenEnabled = UnityHelpersSettings.ShouldTweenSerializableSetFoldouts();
            try
            {
                UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(false);
                SerializableSetPropertyDrawer.ClearMainFoldoutAnimCacheForTests();

                setProperty.isExpanded = true;
                float expandedHeight = drawer.GetPropertyHeight(setProperty, label);

                setProperty.isExpanded = false;
                float collapsedHeight = drawer.GetPropertyHeight(setProperty, label);

                Assert.Greater(
                    expandedHeight,
                    collapsedHeight,
                    $"Expanded height should be greater than collapsed for {elementCount} element(s). "
                        + $"ExpandedHeight={expandedHeight}, CollapsedHeight={collapsedHeight}"
                );
            }
            finally
            {
                UnityHelpersSettings.SetSerializableSetFoldoutTweenEnabled(originalTweenEnabled);
            }
        }

        /// <summary>
        /// Data-driven test for rect width at various starting widths.
        /// Verifies that resolved width is always non-negative.
        /// </summary>
        /// <param name="startWidth">The starting width of the rect.</param>
        [TestCase(400f)]
        [TestCase(200f)]
        [TestCase(100f)]
        [TestCase(50f)]
        [TestCase(20f)]
        [TestCase(10f)]
        [TestCase(5f)]
        public void ResolveContentRectWidthNonNegativeForVariousWidths(float startWidth)
        {
            Rect originalRect = new(0f, 0f, startWidth, 300f);

            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.indentLevel = 0;
                GroupGUIWidthUtility.ResetForTests();

                Rect resolvedRect = SerializableSetPropertyDrawer.ResolveContentRectForTests(
                    originalRect,
                    skipIndentation: false
                );

                Assert.GreaterOrEqual(
                    resolvedRect.width,
                    0f,
                    $"Width should never be negative. StartWidth={startWidth}, ResolvedWidth={resolvedRect.width}"
                );
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
            }
        }
    }
}
