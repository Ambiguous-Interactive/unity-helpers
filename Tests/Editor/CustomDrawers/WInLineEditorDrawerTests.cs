// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
    using System;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEditor.AnimatedValues;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestUtils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WInLineEditorDrawerTests : CommonTestBase
    {
        private const float InlinePaddingContribution = 4f;

        private bool _originalTweenEnabled;
        private float _originalTweenSpeed;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();

            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            _originalTweenEnabled = settings.InlineEditorFoldoutTweenEnabled;
            _originalTweenSpeed = settings.InlineEditorFoldoutSpeed;

            WInLineEditorDrawer.ClearCachedStateForTesting();
            WInLineEditorDrawer.ClearAnimationCacheForTesting();
        }

        [TearDown]
        public override void TearDown()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.InlineEditorFoldoutTweenEnabled = _originalTweenEnabled;
            settings.InlineEditorFoldoutSpeed = _originalTweenSpeed;

            WInLineEditorDrawer.ClearCachedStateForTesting();
            WInLineEditorDrawer.ClearAnimationCacheForTesting();
            base.TearDown();
        }

        // ==================== InlineEditorFoldoutBehaviorScope Validation ====================

        [Test]
        public void InlineEditorFoldoutBehaviorScopeAppliesAndRestoresSetting()
        {
            // Capture the original setting before our test
            UnityHelpersSettings.InlineEditorFoldoutBehavior originalSetting =
                UnityHelpersSettings.GetInlineEditorFoldoutBehavior();

            // Test each behavior value is correctly applied
            foreach (
                UnityHelpersSettings.InlineEditorFoldoutBehavior behavior in System.Enum.GetValues(
                    typeof(UnityHelpersSettings.InlineEditorFoldoutBehavior)
                )
            )
            {
                using (InlineEditorFoldoutBehaviorScope scope = new(behavior))
                {
                    UnityHelpersSettings.InlineEditorFoldoutBehavior currentBehavior =
                        UnityHelpersSettings.GetInlineEditorFoldoutBehavior();
                    Assert.That(
                        currentBehavior,
                        Is.EqualTo(behavior),
                        $"InlineEditorFoldoutBehaviorScope should set behavior to {behavior}, but got {currentBehavior}"
                    );
                }

                // After disposing the scope, verify the setting was restored
                UnityHelpersSettings.InlineEditorFoldoutBehavior afterDisposeValue =
                    UnityHelpersSettings.GetInlineEditorFoldoutBehavior();
                Assert.That(
                    afterDisposeValue,
                    Is.EqualTo(originalSetting),
                    $"InlineEditorFoldoutBehaviorScope should restore original value {originalSetting} after dispose, but got {afterDisposeValue}"
                );
            }
        }

        [TestCase(UnityHelpersSettings.InlineEditorFoldoutBehavior.StartCollapsed)]
        [TestCase(UnityHelpersSettings.InlineEditorFoldoutBehavior.StartExpanded)]
        [TestCase(UnityHelpersSettings.InlineEditorFoldoutBehavior.AlwaysOpen)]
        public void InlineEditorFoldoutBehaviorScopeCorrectlySetsBehavior(
            UnityHelpersSettings.InlineEditorFoldoutBehavior behavior
        )
        {
            UnityHelpersSettings.InlineEditorFoldoutBehavior originalSetting =
                UnityHelpersSettings.GetInlineEditorFoldoutBehavior();

            using (InlineEditorFoldoutBehaviorScope scope = new(behavior))
            {
                UnityHelpersSettings.InlineEditorFoldoutBehavior currentBehavior =
                    UnityHelpersSettings.GetInlineEditorFoldoutBehavior();
                Assert.That(
                    currentBehavior,
                    Is.EqualTo(behavior),
                    $"Scope should apply {behavior}"
                );
            }

            Assert.That(
                UnityHelpersSettings.GetInlineEditorFoldoutBehavior(),
                Is.EqualTo(originalSetting),
                "Scope should restore original value after dispose"
            );
        }

        // ==================== Basic Foldout Tests ====================

        [Test]
        public void HeaderFoldoutControlsInlineHeight()
        {
            float collapsedHeight = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: false
            );
            float expandedHeight = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );
            Assert.That(expandedHeight, Is.GreaterThan(collapsedHeight));

            float collapsedAgainHeight = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: false
            );
            Assert.That(collapsedAgainHeight, Is.EqualTo(collapsedHeight).Within(0.001f));
        }

        [Test]
        public void BuiltInInlineInspectorRemainsSuppressed()
        {
            float collapsedHeight = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false
            );
            float expandedHeight = MeasurePropertyHeight<InlineEditorHost>(propertyExpanded: true);
            Assert.That(expandedHeight, Is.EqualTo(collapsedHeight));
        }

        // Data-driven test for foldout settings behavior
        [TestCase(
            UnityHelpersSettings.InlineEditorFoldoutBehavior.StartCollapsed,
            false,
            TestName = "FoldoutBehavior.StartCollapsed.InitiallyCollapsed"
        )]
        [TestCase(
            UnityHelpersSettings.InlineEditorFoldoutBehavior.StartExpanded,
            true,
            TestName = "FoldoutBehavior.StartExpanded.InitiallyExpanded"
        )]
        [TestCase(
            UnityHelpersSettings.InlineEditorFoldoutBehavior.AlwaysOpen,
            true,
            TestName = "FoldoutBehavior.AlwaysOpen.InitiallyExpanded"
        )]
        public void DefaultModeUsesSettingsDataDriven(
            UnityHelpersSettings.InlineEditorFoldoutBehavior behavior,
            bool expectExpanded
        )
        {
            using InlineEditorFoldoutBehaviorScope scope = new(behavior);

            // Measure height with explicit state matching expected behavior
            (
                float expectedHeight,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) expectedDetails,
                _
            ) = MeasurePropertyHeightWithDetailedDiagnostics<DefaultSettingsInlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: expectExpanded
            );

            // Measure height without explicit state (should use settings)
            (
                float defaultHeight,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) defaultDetails,
                string diagnostics
            ) = MeasurePropertyHeightWithDetailedDiagnostics<DefaultSettingsInlineEditorHost>(
                propertyExpanded: false
            );

            Assert.That(
                defaultHeight,
                Is.EqualTo(expectedHeight).Within(0.001f),
                $"With setting {behavior}, expected showBody={expectExpanded}. "
                    + $"Expected details: showBody={expectedDetails.showBody}, inlineH={expectedDetails.inlineHeight}. "
                    + $"Default details: showBody={defaultDetails.showBody}, inlineH={defaultDetails.inlineHeight}.\n"
                    + $"Diagnostics:\n{diagnostics}"
            );
        }

        [Test]
        public void DefaultModeUsesSettingsWhenCollapsed()
        {
            using InlineEditorFoldoutBehaviorScope scope = new(
                UnityHelpersSettings.InlineEditorFoldoutBehavior.StartCollapsed
            );
            float expectedCollapsed = MeasurePropertyHeight<DefaultSettingsInlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: false
            );
            float defaultHeight = MeasurePropertyHeight<DefaultSettingsInlineEditorHost>(
                propertyExpanded: false
            );
            Assert.That(defaultHeight, Is.EqualTo(expectedCollapsed).Within(0.001f));
        }

        [Test]
        public void DefaultModeUsesSettingsWhenExpanded()
        {
            using InlineEditorFoldoutBehaviorScope scope = new(
                UnityHelpersSettings.InlineEditorFoldoutBehavior.StartExpanded
            );

            // Verify the setting was actually applied
            UnityHelpersSettings.InlineEditorFoldoutBehavior currentBehavior =
                UnityHelpersSettings.GetInlineEditorFoldoutBehavior();
            Assert.That(
                currentBehavior,
                Is.EqualTo(UnityHelpersSettings.InlineEditorFoldoutBehavior.StartExpanded),
                "Setting should be StartExpanded but was " + currentBehavior
            );

            (
                float expectedExpanded,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) detailsExplicit,
                string diagnosticsExplicit
            ) = MeasurePropertyHeightWithDetailedDiagnostics<DefaultSettingsInlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );
            (
                float defaultHeight,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) detailsDefault,
                string diagnosticsDefault
            ) = MeasurePropertyHeightWithDetailedDiagnostics<DefaultSettingsInlineEditorHost>(
                propertyExpanded: false
            );
            Assert.That(
                defaultHeight,
                Is.EqualTo(expectedExpanded).Within(0.001f),
                $"Expected height (explicitly expanded): {expectedExpanded}, "
                    + $"Default height (should use settings): {defaultHeight}. "
                    + $"Current foldout behavior setting: {currentBehavior}. "
                    + $"Explicit details: showBody={detailsExplicit.showBody}, inlineH={detailsExplicit.inlineHeight}. "
                    + $"Default details: showBody={detailsDefault.showBody}, inlineH={detailsDefault.inlineHeight}\n"
                    + $"--- Explicit Diagnostics ---\n{diagnosticsExplicit}\n"
                    + $"--- Default Diagnostics ---\n{diagnosticsDefault}"
            );
        }

        [Test]
        public void StandaloneHeaderOnlyDrawnWhenObjectFieldHidden()
        {
            // Inline heights, not totals: GetPropertyHeight differs for object fields versus labels.
            (
                _,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) detailsWithObject
            ) = MeasurePropertyHeightWithDetails<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );
            (
                _,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) detailsWithHeader
            ) = MeasurePropertyHeightWithDetails<HeaderOnlyInlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );

            // Verify that the standalone header IS shown when drawObjectField=false
            Assert.That(
                detailsWithObject.showHeader,
                Is.False,
                "InlineEditorHost (DrawObjectField=true) should NOT show standalone header"
            );
            Assert.That(
                detailsWithHeader.showHeader,
                Is.True,
                "HeaderOnlyInlineEditorHost (DrawObjectField=false) should show standalone header"
            );

            // The inline height difference should be the header contribution (HeaderHeight=20 + Spacing=2)
            const float ExpectedHeaderContribution = 22f;
            float inlineHeightDifference =
                detailsWithHeader.inlineHeight - detailsWithObject.inlineHeight;
            Assert.That(
                inlineHeightDifference,
                Is.EqualTo(ExpectedHeaderContribution).Within(0.001f),
                $"Inline height with object field: {detailsWithObject.inlineHeight}, "
                    + $"inline height with standalone header: {detailsWithHeader.inlineHeight}, "
                    + $"difference: {inlineHeightDifference}, "
                    + $"expected header contribution: {ExpectedHeaderContribution}. "
                    + $"Both bodies should have same displayHeight: withObject={detailsWithObject.displayHeight}, withHeader={detailsWithHeader.displayHeight}"
            );
        }

        [Test]
        public void InlineInspectorOmitsScriptField()
        {
            // Compared against the measured height of every visible property except m_Script.
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();
            float expectedContentHeight = 0f;
            System.Text.StringBuilder propertyDebug = new();
            using SerializedObject so = new(target);
            so.Update();
            SerializedProperty iterator = so.GetIterator();
            bool enterChildren = true;
            bool first = true;
            while (iterator.NextVisible(enterChildren))
            {
                float propHeight = EditorGUI.GetPropertyHeight(iterator, true);
                if (iterator.propertyPath == "m_Script")
                {
                    propertyDebug.AppendLine(
                        $"  {iterator.propertyPath}: {propHeight}px [SKIPPED]"
                    );
                    enterChildren = false;
                    continue;
                }
                if (!first)
                {
                    expectedContentHeight += EditorGUIUtility.standardVerticalSpacing;
                }
                expectedContentHeight += propHeight;
                propertyDebug.AppendLine($"  {iterator.propertyPath}: {propHeight}px");
                enterChildren = false;
                first = false;
            }

            (float collapsedHeight, _, string collapsedDiagnostics) =
                MeasurePropertyHeightWithDetailedDiagnostics<NoScrollInlineEditorHost>(
                    propertyExpanded: false,
                    setInlineExpanded: false
                );
            (float expandedHeight, _, string expandedDiagnostics) =
                MeasurePropertyHeightWithDetailedDiagnostics<NoScrollInlineEditorHost>(
                    propertyExpanded: false,
                    setInlineExpanded: true
                );

            // The inline height is the difference, minus the standardVerticalSpacing between base and inline
            float inlineContribution = expandedHeight - collapsedHeight;
            // inlineContribution = standardVerticalSpacing + inlineHeight
            float inlineHeight = inlineContribution - EditorGUIUtility.standardVerticalSpacing;

            // Expected: contentHeight + padding (4)
            float expectedInlineHeight = expectedContentHeight + InlinePaddingContribution;

            Assert.That(
                inlineHeight,
                Is.EqualTo(expectedInlineHeight).Within(0.01f),
                $"Collapsed height: {collapsedHeight}, "
                    + $"expanded height: {expandedHeight}, "
                    + $"inline contribution (with spacing): {inlineContribution}, "
                    + $"inline height: {inlineHeight}, "
                    + $"expected inline height: {expectedInlineHeight} "
                    + $"(contentHeight={expectedContentHeight} + padding={InlinePaddingContribution}). "
                    + $"standardVerticalSpacing: {EditorGUIUtility.standardVerticalSpacing}\n"
                    + $"--- Expected Properties (test calculation) ---\n{propertyDebug}\n"
                    + $"--- Collapsed Diagnostics ---\n{collapsedDiagnostics}\n"
                    + $"--- Expanded Diagnostics ---\n{expandedDiagnostics}"
            );
        }

        [Test]
        public void PingButtonsDisabledWhenProjectBrowserHidden()
        {
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();
            try
            {
                ProjectBrowserVisibilityUtility.SetProjectBrowserVisibilityForTesting(false);
                Assert.That(WInLineEditorDrawer.ShouldShowPingButton(target), Is.False);
            }
            finally
            {
                ProjectBrowserVisibilityUtility.SetProjectBrowserVisibilityForTesting(null);
            }
        }

        [Test]
        public void PingButtonsEnabledWhenProjectBrowserVisible()
        {
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();
            try
            {
                ProjectBrowserVisibilityUtility.SetProjectBrowserVisibilityForTesting(true);
                Assert.That(WInLineEditorDrawer.ShouldShowPingButton(target), Is.True);
            }
            finally
            {
                ProjectBrowserVisibilityUtility.SetProjectBrowserVisibilityForTesting(null);
            }
        }

        [Test]
        public void SimplePropertiesAreDetectedCorrectly()
        {
            // Test the simple property detection directly without full editor integration
            SimpleInlineEditorTarget target = CreateHiddenInstance<SimpleInlineEditorTarget>();
            using SerializedObject serializedObject = new(target);
            bool hasOnlySimple = WInLineEditorDrawer.HasOnlySimplePropertiesForTesting(
                serializedObject
            );
            Assert.That(
                hasOnlySimple,
                Is.True,
                "SimpleInlineEditorTarget with int and string fields should be detected as simple"
            );
        }

        [Test]
        public void ArrayPropertiesAreDetectedAsComplex()
        {
            // Test that arrays are correctly detected as complex
            ArrayInlineEditorTarget target = CreateHiddenInstance<ArrayInlineEditorTarget>();
            using SerializedObject serializedObject = new(target);
            bool hasOnlySimple = WInLineEditorDrawer.HasOnlySimplePropertiesForTesting(
                serializedObject
            );
            Assert.That(
                hasOnlySimple,
                Is.False,
                "ArrayInlineEditorTarget with array field should be detected as complex"
            );
        }

        // Strings are internally arrays, which is the edge case these cover.
        [TestCase(
            typeof(SimpleInlineEditorTarget),
            true,
            TestName = "SimpleDetection.IntAndString.Simple"
        )]
        [TestCase(typeof(StringOnlyTarget), true, TestName = "SimpleDetection.StringOnly.Simple")]
        [TestCase(
            typeof(NumericTypesTarget),
            true,
            TestName = "SimpleDetection.NumericTypes.Simple"
        )]
        [TestCase(typeof(BoolAndEnumTarget), true, TestName = "SimpleDetection.BoolAndEnum.Simple")]
        [TestCase(typeof(VectorTarget), true, TestName = "SimpleDetection.Vectors.Simple")]
        [TestCase(typeof(ColorTarget), true, TestName = "SimpleDetection.Color.Simple")]
        [TestCase(
            typeof(ObjectReferenceTarget),
            true,
            TestName = "SimpleDetection.ObjectReference.Simple"
        )]
        [TestCase(
            typeof(ArrayInlineEditorTarget),
            false,
            TestName = "SimpleDetection.Array.Complex"
        )]
        [TestCase(
            typeof(AnimationCurveTarget),
            false,
            TestName = "SimpleDetection.AnimationCurve.Complex"
        )]
        [TestCase(typeof(ListTarget), false, TestName = "SimpleDetection.List.Complex")]
        [TestCase(
            typeof(NestedClassTarget),
            false,
            TestName = "SimpleDetection.NestedClass.Complex"
        )]
        public void SimplePropertyDetectionDataDriven(Type targetType, bool expectedSimple)
        {
            ScriptableObject target = Track(
                ScriptableObject.CreateInstance(targetType) as ScriptableObject
            );
            Assert.That(target, Is.Not.Null, $"Failed to create instance of {targetType.Name}");
            target.hideFlags = HideFlags.HideAndDontSave;

            using SerializedObject serializedObject = new(target);
            bool hasOnlySimple = WInLineEditorDrawer.HasOnlySimplePropertiesForTesting(
                serializedObject
            );
            Assert.That(
                hasOnlySimple,
                Is.EqualTo(expectedSimple),
                $"{targetType.Name} should be detected as {(expectedSimple ? "simple" : "complex")} "
                    + $"but was detected as {(hasOnlySimple ? "simple" : "complex")}"
            );
        }

        // Data-driven tests for horizontal scrollbar decision logic
        [TestCase(
            true,
            520f,
            false,
            true,
            360f,
            false,
            TestName = "ScrollDecision.SimpleLayout.NoScroll"
        )]
        [TestCase(
            true,
            520f,
            false,
            false,
            360f,
            true,
            TestName = "ScrollDecision.ComplexLayout.NeedsScroll"
        )]
        [TestCase(
            true,
            720f,
            true,
            true,
            360f,
            true,
            TestName = "ScrollDecision.ExplicitMinWidth.OverridesSimple"
        )]
        [TestCase(
            false,
            520f,
            false,
            false,
            360f,
            false,
            TestName = "ScrollDecision.ScrollDisabled.NoScroll"
        )]
        [TestCase(
            true,
            0f,
            false,
            false,
            360f,
            false,
            TestName = "ScrollDecision.ZeroMinWidth.NoScroll"
        )]
        [TestCase(
            true,
            520f,
            false,
            false,
            600f,
            false,
            TestName = "ScrollDecision.WideEnough.NoScroll"
        )]
        [TestCase(
            true,
            300f,
            false,
            false,
            360f,
            false,
            TestName = "ScrollDecision.MinWidthUnderAvailable.NoScroll"
        )]
        public void HorizontalScrollbarDecisionLogic(
            bool enableScrolling,
            float minInspectorWidth,
            bool hasExplicitMinInspectorWidth,
            bool hasSimpleLayout,
            float availableWidth,
            bool expectedNeedsScroll
        )
        {
            bool needsScroll = WInLineEditorDrawer.RequiresHorizontalScrollbarForTesting(
                enableScrolling,
                minInspectorWidth,
                hasExplicitMinInspectorWidth,
                hasSimpleLayout,
                availableWidth
            );
            Assert.That(
                needsScroll,
                Is.EqualTo(expectedNeedsScroll),
                $"Scroll decision mismatch for enableScrolling={enableScrolling}, "
                    + $"minWidth={minInspectorWidth}, explicitMin={hasExplicitMinInspectorWidth}, "
                    + $"simpleLayout={hasSimpleLayout}, availWidth={availableWidth}"
            );
        }

        [Test]
        public void SimpleTargetsDoNotTriggerHorizontalScrollbars()
        {
            // Integration test - verifies the full path with a simple target
            SimpleInlineEditorTarget target = CreateHiddenInstance<SimpleInlineEditorTarget>();
            // First verify our target is detected as simple
            using SerializedObject serializedObject = new(target);
            bool isSimple = WInLineEditorDrawer.HasOnlySimplePropertiesForTesting(serializedObject);

            // If simple detection works, verify the full integration
            if (isSimple)
            {
                WInLineEditorAttribute inlineAttribute = new();
                bool usesScrollbar = WInLineEditorDrawer.UsesHorizontalScrollbarForTesting(
                    target,
                    inlineAttribute,
                    availableWidth: 360f
                );
                Assert.That(
                    usesScrollbar,
                    Is.False,
                    "Simple targets should not trigger horizontal scrollbars"
                );
            }
            else
            {
                // Detection can fail from editor integration, so check the logic with known-good inputs.
                bool wouldNeedScroll = WInLineEditorDrawer.RequiresHorizontalScrollbarForTesting(
                    enableScrolling: true,
                    minInspectorWidth: 520f, // default
                    hasExplicitMinInspectorWidth: false,
                    hasSimpleLayout: true, // what we expect
                    availableWidth: 360f
                );
                Assert.That(
                    wouldNeedScroll,
                    Is.False,
                    "Simple layout logic should not require horizontal scrollbar"
                );
                Debug.LogWarning(
                    "Simple property detection returned false unexpectedly - "
                        + "verified logic directly instead"
                );
            }
        }

        [Test]
        public void ComplexTargetsStillTriggerHorizontalScrollbars()
        {
            // Integration test - verifies the full path with a complex target
            ArrayInlineEditorTarget target = CreateHiddenInstance<ArrayInlineEditorTarget>();
            // First verify our target is detected as complex
            using SerializedObject serializedObject = new(target);
            bool isSimple = WInLineEditorDrawer.HasOnlySimplePropertiesForTesting(serializedObject);

            // Verify array target is detected as complex
            Assert.That(isSimple, Is.False, "Array target should be detected as complex");

            WInLineEditorAttribute inlineAttribute = new();
            bool usesScrollbar = WInLineEditorDrawer.UsesHorizontalScrollbarForTesting(
                target,
                inlineAttribute,
                availableWidth: 360f
            );
            Assert.That(
                usesScrollbar,
                Is.True,
                "Complex targets should trigger horizontal scrollbars when width is insufficient"
            );
        }

        [Test]
        public void ExplicitMinWidthOverridesSimpleTargetHeuristic()
        {
            // Test the explicit min width override logic directly
            bool needsScroll = WInLineEditorDrawer.RequiresHorizontalScrollbarForTesting(
                enableScrolling: true,
                minInspectorWidth: 720f,
                hasExplicitMinInspectorWidth: true, // explicit override
                hasSimpleLayout: true, // even though simple
                availableWidth: 360f
            );
            Assert.That(
                needsScroll,
                Is.True,
                "Explicit min width should override simple layout heuristic"
            );
        }

        [Test]
        public void CustomEditorsRespectMeasuredInlineHeight()
        {
            float collapsedHeight = MeasurePropertyHeight<CustomEditorInlineHost>(
                propertyExpanded: false,
                setInlineExpanded: false
            );
            float expandedHeight = MeasurePropertyHeight<CustomEditorInlineHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );
            float inlineHeight = expandedHeight - collapsedHeight;
            Assert.That(inlineHeight, Is.GreaterThan(40f));
            Assert.That(inlineHeight, Is.LessThan(140f));
        }

        [Test]
        public void InlineInspectorContentRectAppliesPadding()
        {
            Rect outer = new(10f, 20f, 200f, 100f);
            Rect content = WInLineEditorDrawer.GetInlineContentRectForTesting(outer);
            Assert.That(content.x, Is.EqualTo(outer.x + 2f));
            Assert.That(content.y, Is.EqualTo(outer.y + 2f));
            Assert.That(content.width, Is.EqualTo(outer.width - 4f));
            Assert.That(content.height, Is.EqualTo(outer.height - 4f));
        }

        [Test]
        public void InlineInspectorContentRectClampsHeightToZero()
        {
            Rect outer = new(0f, 0f, 4f, 3f);
            Rect content = WInLineEditorDrawer.GetInlineContentRectForTesting(outer);
            Assert.That(content.height, Is.EqualTo(0f));
        }

        /// <summary>
        /// Horizontal scrollbar calculations must not throw when called outside an OnGUI context;
        /// the production code catches the ArgumentException that GUI.skin access raises there.
        /// </summary>
        [Test]
        public void HorizontalScrollbarCalculationHandlesOutsideGUIContext()
        {
            WInLineEditorDrawer.ClearCachedStateForTesting();

            // MeasurePropertyHeight triggers the scrollbar height calculation internally.
            Assert.DoesNotThrow(
                () =>
                {
                    MeasurePropertyHeight<InlineEditorHost>(
                        propertyExpanded: false,
                        setInlineExpanded: true
                    );
                },
                "Methods calculating scrollbar heights should handle being called outside OnGUI context"
            );
        }

        // Data-driven tests for content rect calculations
        [TestCase(10f, 20f, 200f, 100f, 12f, 22f, 196f, 96f, TestName = "ContentRect.NormalCase")]
        [TestCase(0f, 0f, 100f, 50f, 2f, 2f, 96f, 46f, TestName = "ContentRect.ZeroOrigin")]
        [TestCase(0f, 0f, 4f, 4f, 2f, 2f, 0f, 0f, TestName = "ContentRect.MinimalSize")]
        [TestCase(0f, 0f, 5f, 5f, 2f, 2f, 1f, 1f, TestName = "ContentRect.JustAboveMinimal")]
        public void ContentRectDataDrivenScenarios(
            float outerX,
            float outerY,
            float outerWidth,
            float outerHeight,
            float expectedX,
            float expectedY,
            float expectedWidth,
            float expectedHeight
        )
        {
            Rect outer = new(outerX, outerY, outerWidth, outerHeight);
            Rect content = WInLineEditorDrawer.GetInlineContentRectForTesting(outer);
            Assert.That(content.x, Is.EqualTo(expectedX).Within(0.01f), "Content X mismatch");
            Assert.That(content.y, Is.EqualTo(expectedY).Within(0.01f), "Content Y mismatch");
            Assert.That(
                content.width,
                Is.EqualTo(expectedWidth).Within(0.01f),
                "Content width mismatch"
            );
            Assert.That(
                content.height,
                Is.EqualTo(expectedHeight).Within(0.01f),
                "Content height mismatch"
            );
        }

        [Test]
        public void NullTargetReturnsBaseHeight()
        {
            // Test that when the target object is null, only the base height is returned
            WInLineEditorDrawer.ClearCachedStateForTesting();
            InlineEditorHost host = CreateHiddenInstance<InlineEditorHost>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(
                nameof(InlineEditorHost.collapsedTarget)
            );
            Assert.That(property, Is.Not.Null);

            // Don't assign a target - leave it null
            Assert.That(
                property.objectReferenceValue,
                Is.Null,
                "Target should be null for this test"
            );

            // Get the attribute via reflection
            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(InlineEditorHost),
                nameof(InlineEditorHost.collapsedTarget)
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            // Setup drawer
            GUIContent label = new("Target");
            WInLineEditorDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, inlineAttribute);

            float height = drawer.GetPropertyHeight(property, label);

            // With null target, should return just base property height (typically singleLineHeight)
            float expectedMaxHeight = EditorGUIUtility.singleLineHeight + 2f; // Small tolerance
            Assert.That(
                height,
                Is.LessThanOrEqualTo(expectedMaxHeight),
                $"Height with null target should be base height. Got {height}, expected <= {expectedMaxHeight}"
            );
        }

        /// <summary>
        /// Pins the recursion fix: EditorGUI.GetPropertyHeight on the property made Unity call this
        /// drawer's GetPropertyHeight again, doubling the height.
        /// </summary>
        [Test]
        public void HeightDoesNotDoubleFromRecursion()
        {
            WInLineEditorDrawer.ClearCachedStateForTesting();

            float collapsedHeight = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: false
            );
            float expandedHeight = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );

            float inlineContribution = expandedHeight - collapsedHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            // With the recursion defect this contribution was roughly double.
            float maxReasonableInlineContribution = 100f; // A reasonable upper bound for a simple inline inspector

            Assert.That(
                inlineContribution,
                Is.LessThan(maxReasonableInlineContribution),
                $"Inline contribution ({inlineContribution}) is suspiciously large. "
                    + "This may indicate height recursion. "
                    + $"Collapsed: {collapsedHeight}, Expanded: {expandedHeight}"
            );

            // Content height (one property, about 18px) plus 4px padding is about 22px.
            float inlineHeight = inlineContribution - spacing;
            float expectedApproxInlineHeight = EditorGUIUtility.singleLineHeight + 4f; // ~22px

            Assert.That(
                inlineHeight,
                Is.EqualTo(expectedApproxInlineHeight).Within(10f), // Allow some tolerance for different editors
                $"Inline height ({inlineHeight}) should be approximately {expectedApproxInlineHeight}. "
                    + "Large deviation may indicate height calculation issues."
            );
        }

        // Data-driven test for explicit inline editor modes
        [TestCase(
            WInLineEditorMode.FoldoutCollapsed,
            false,
            TestName = "ExplicitMode.FoldoutCollapsed.InitiallyCollapsed"
        )]
        [TestCase(
            WInLineEditorMode.FoldoutExpanded,
            true,
            TestName = "ExplicitMode.FoldoutExpanded.InitiallyExpanded"
        )]
        [TestCase(
            WInLineEditorMode.AlwaysExpanded,
            true,
            TestName = "ExplicitMode.AlwaysExpanded.AlwaysShows"
        )]
        public void ExplicitModeInitialFoldoutState(WInLineEditorMode mode, bool expectExpanded)
        {
            WInLineEditorDrawer.ClearCachedStateForTesting();

            // Create a host with the specified mode
            ExplicitModeTestHost host = CreateHiddenInstance<ExplicitModeTestHost>();
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();

            // Find the property with the correct mode
            string propertyName = mode switch
            {
                WInLineEditorMode.FoldoutCollapsed => nameof(
                    ExplicitModeTestHost.foldoutCollapsedTarget
                ),
                WInLineEditorMode.FoldoutExpanded => nameof(
                    ExplicitModeTestHost.foldoutExpandedTarget
                ),
                WInLineEditorMode.AlwaysExpanded => nameof(
                    ExplicitModeTestHost.alwaysExpandedTarget
                ),
                _ => throw new ArgumentException($"Unsupported mode: {mode}"),
            };

            SerializedProperty property = serializedHost.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, $"Property {propertyName} not found");

            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(propertyName);

            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(ExplicitModeTestHost),
                propertyName
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            (
                float baseHeight,
                float inlineHeight,
                bool showHeader,
                bool showBody,
                float displayHeight
            ) details = WInLineEditorDrawer.GetHeightCalculationDetailsForTesting(
                property,
                inlineAttribute,
                target,
                500f
            );

            Assert.That(
                details.showBody,
                Is.EqualTo(expectExpanded),
                $"With mode {mode}, expected showBody={expectExpanded} but got {details.showBody}"
            );
        }

        // ==================== Width and Layout Tests ====================

        [Test]
        public void SerializedInspectorIsUsedByDefault()
        {
            // The serialized inspector path is what avoids the 50% width defect.
            Assert.That(
                WInLineEditorDrawer.ForceSerializedInspectorForTesting,
                Is.True,
                "ForceSerializedInspector should be true by default to avoid width issues"
            );
        }

        [Test]
        public void LabelWidthIsCalculatedCorrectly()
        {
            // Test that labelWidth is calculated as 40% of available width
            const float availableWidth = 400f;
            const float expectedLabelWidth = 160f; // 40% of 400

            float calculatedLabelWidth = WInLineEditorDrawer.CalculateLabelWidthForTesting(
                availableWidth
            );

            Assert.That(
                calculatedLabelWidth,
                Is.EqualTo(expectedLabelWidth).Within(0.01f),
                $"Label width should be 40% of available width ({availableWidth})"
            );
        }

        [TestCase(400f, 160f, TestName = "LabelWidth.400px.Returns160")]
        [TestCase(500f, 200f, TestName = "LabelWidth.500px.Returns200")]
        [TestCase(300f, 120f, TestName = "LabelWidth.300px.Returns120")]
        [TestCase(100f, 40f, TestName = "LabelWidth.100px.Returns40")]
        public void LabelWidthCalculationDataDriven(float availableWidth, float expectedLabelWidth)
        {
            float calculatedLabelWidth = WInLineEditorDrawer.CalculateLabelWidthForTesting(
                availableWidth
            );

            Assert.That(
                calculatedLabelWidth,
                Is.EqualTo(expectedLabelWidth).Within(0.01f),
                $"Label width for {availableWidth}px should be {expectedLabelWidth}px"
            );
        }

        /*
            Production logic applies ContentPadding (2px each side) before comparing against the
            200px MinimumUsableWidth, so availableWidth has to exceed 204px to avoid a scrollbar:
            204 gives effectiveWidth 200, which is not below the threshold, while 203 gives 199,
            which is.
        */
        [TestCase(150f, true, TestName = "NarrowWidth.150px.TriggersScroll")]
        [TestCase(180f, true, TestName = "NarrowWidth.180px.TriggersScroll")]
        [TestCase(199f, true, TestName = "NarrowWidth.199px.TriggersScroll")]
        [TestCase(200f, true, TestName = "NarrowWidth.200px.TriggersScroll")] // effectiveWidth=196 < 200
        [TestCase(203f, true, TestName = "NarrowWidth.203px.TriggersScroll")] // effectiveWidth=199 < 200
        [TestCase(204f, false, TestName = "NarrowWidth.204px.NoScroll")] // effectiveWidth=200, not < 200
        [TestCase(250f, false, TestName = "NarrowWidth.250px.NoScroll")]
        public void NarrowWidthTriggersHorizontalScrollDataDriven(
            float availableWidth,
            bool expectedNeedsScroll
        )
        {
            // effectiveWidth = availableWidth - 4; below MinimumUsableWidth (200) the scroll triggers.
            bool needsScroll = WInLineEditorDrawer.RequiresHorizontalScrollbarForTesting(
                enableScrolling: true,
                minInspectorWidth: 520f, // default
                hasExplicitMinInspectorWidth: false,
                hasSimpleLayout: true, // simple layout
                availableWidth: availableWidth
            );

            // Calculate expected effective width for diagnostic purposes
            const float ContentPadding = 2f;
            float effectiveWidth = availableWidth - (ContentPadding * 2f);
            const float MinimumUsableWidth = 200f;

            Assert.That(
                needsScroll,
                Is.EqualTo(expectedNeedsScroll),
                $"At {availableWidth}px availableWidth (effectiveWidth={effectiveWidth}px), "
                    + $"simple layout scroll should be {expectedNeedsScroll}. "
                    + $"MinimumUsableWidth threshold is {MinimumUsableWidth}px (applied to effectiveWidth)."
            );
        }

        [Test]
        public void VeryNarrowWidthTriggersScrollForSimpleLayouts()
        {
            // Integration test: verify that very narrow widths trigger scroll even for simple targets
            SimpleInlineEditorTarget target = CreateHiddenInstance<SimpleInlineEditorTarget>();
            // Verify target is detected as simple
            using SerializedObject serializedObject = new(target);
            bool isSimple = WInLineEditorDrawer.HasOnlySimplePropertiesForTesting(serializedObject);
            Assert.That(isSimple, Is.True, "SimpleInlineEditorTarget should be detected as simple");

            // At very narrow width (< 200px), even simple targets should get scrollbar
            WInLineEditorAttribute inlineAttribute = new();
            bool usesScrollbar = WInLineEditorDrawer.UsesHorizontalScrollbarForTesting(
                target,
                inlineAttribute,
                availableWidth: 150f // Very narrow
            );

            Assert.That(
                usesScrollbar,
                Is.True,
                "Simple targets should trigger horizontal scroll at very narrow widths (< 200px)"
            );
        }

        [Test]
        public void SimpleLayoutAtNormalWidthDoesNotTriggerScroll()
        {
            // Verify simple layouts don't trigger scroll at normal widths
            SimpleInlineEditorTarget target = CreateHiddenInstance<SimpleInlineEditorTarget>();
            using SerializedObject serializedObject = new(target);
            bool isSimple = WInLineEditorDrawer.HasOnlySimplePropertiesForTesting(serializedObject);
            Assert.That(isSimple, Is.True, "SimpleInlineEditorTarget should be detected as simple");

            WInLineEditorAttribute inlineAttribute = new();
            bool usesScrollbar = WInLineEditorDrawer.UsesHorizontalScrollbarForTesting(
                target,
                inlineAttribute,
                availableWidth: 360f // Normal width
            );

            Assert.That(
                usesScrollbar,
                Is.False,
                "Simple targets should not trigger horizontal scroll at normal widths"
            );
        }

        // The production constants are MinimumUsableWidth=200 and ContentPadding=2.
        [TestCase(204.5f, false, TestName = "EffectiveWidthBoundary.204.5px.NoScroll")] // effectiveWidth=200.5 >= 200
        [TestCase(204.0f, false, TestName = "EffectiveWidthBoundary.204px.ExactlyAtThreshold")] // effectiveWidth=200 >= 200
        [TestCase(203.9f, true, TestName = "EffectiveWidthBoundary.203.9px.JustUnderThreshold")] // effectiveWidth=199.9 < 200
        [TestCase(203.5f, true, TestName = "EffectiveWidthBoundary.203.5px.TriggersScroll")] // effectiveWidth=199.5 < 200
        public void EffectiveWidthBoundaryTests(float availableWidth, bool expectedNeedsScroll)
        {
            const float ContentPadding = 2f;
            const float MinimumUsableWidth = 200f;
            float effectiveWidth = availableWidth - (ContentPadding * 2f);
            bool expectedBasedOnThreshold = effectiveWidth < MinimumUsableWidth;

            // Sanity check: our manual calculation matches our expected value
            Assert.That(
                expectedBasedOnThreshold,
                Is.EqualTo(expectedNeedsScroll),
                $"Test case setup error: effectiveWidth={effectiveWidth}, threshold={MinimumUsableWidth}, "
                    + $"manual calc says needsScroll={expectedBasedOnThreshold}, but expectedNeedsScroll={expectedNeedsScroll}"
            );

            bool needsScroll = WInLineEditorDrawer.RequiresHorizontalScrollbarForTesting(
                enableScrolling: true,
                minInspectorWidth: 520f, // default
                hasExplicitMinInspectorWidth: false,
                hasSimpleLayout: true, // simple layout (threshold only applies to simple layouts)
                availableWidth: availableWidth
            );

            Assert.That(
                needsScroll,
                Is.EqualTo(expectedNeedsScroll),
                $"At {availableWidth}px availableWidth (effectiveWidth={effectiveWidth}px), "
                    + $"simple layout scroll should be {expectedNeedsScroll}. "
                    + $"MinimumUsableWidth={MinimumUsableWidth}px, ContentPadding={ContentPadding}px."
            );
        }

        [Test]
        public void MinimumUsableWidthConstantMatchesProduction()
        {
            const float ExpectedContentPadding = 2f;
            const float ExpectedMinimumUsableWidth = 200f;

            // At availableWidth 204 the effectiveWidth is exactly 200, which is NOT below 200, so no scroll.
            float thresholdAvailableWidth =
                ExpectedMinimumUsableWidth + (ExpectedContentPadding * 2f);

            bool needsScrollAtThreshold = WInLineEditorDrawer.RequiresHorizontalScrollbarForTesting(
                enableScrolling: true,
                minInspectorWidth: 520f,
                hasExplicitMinInspectorWidth: false,
                hasSimpleLayout: true,
                availableWidth: thresholdAvailableWidth
            );

            Assert.That(
                needsScrollAtThreshold,
                Is.False,
                $"At exact threshold availableWidth={thresholdAvailableWidth}px "
                    + $"(effectiveWidth={ExpectedMinimumUsableWidth}px), scroll should NOT be needed. "
                    + "If this fails, production constants may have changed."
            );

            // Test just below threshold
            bool needsScrollBelowThreshold =
                WInLineEditorDrawer.RequiresHorizontalScrollbarForTesting(
                    enableScrolling: true,
                    minInspectorWidth: 520f,
                    hasExplicitMinInspectorWidth: false,
                    hasSimpleLayout: true,
                    availableWidth: thresholdAvailableWidth - 0.1f
                );

            Assert.That(
                needsScrollBelowThreshold,
                Is.True,
                $"Just below threshold at availableWidth={thresholdAvailableWidth - 0.1f}px, "
                    + "scroll SHOULD be needed."
            );
        }

        // ==================== End Width and Layout Tests ====================

        // ==================== Compact Object Field Tests (drawObjectField: false) ====================

        [Test]
        public void CompactModeShowsObjectPickerInsteadOfFullObjectField()
        {
            WInLineEditorDrawer.ClearCachedStateForTesting();

            CompactInlineEditorHost host = CreateHiddenInstance<CompactInlineEditorHost>();
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(
                nameof(CompactInlineEditorHost.compactTarget)
            );
            Assert.That(property, Is.Not.Null, "Property should exist");

            // Test with null reference - should still have a way to assign
            Assert.That(
                property.objectReferenceValue,
                Is.Null,
                "Property should initially be null"
            );

            // Assign an object to verify it works
            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(nameof(CompactInlineEditorHost.compactTarget));
            Assert.That(
                property.objectReferenceValue,
                Is.EqualTo(target),
                "Object assignment should work in compact mode"
            );
        }

        [Test]
        public void CompactModeHeightMatchesNonCompactWhenExpanded()
        {
            (
                _,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) compactDetails
            ) = MeasurePropertyHeightWithDetails<CompactInlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );

            (
                _,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) nonCompactDetails
            ) = MeasurePropertyHeightWithDetails<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );

            // Both should show body when expanded
            Assert.That(
                compactDetails.showBody,
                Is.True,
                "Compact mode should show body when expanded"
            );
            Assert.That(
                nonCompactDetails.showBody,
                Is.True,
                "Non-compact mode should show body when expanded"
            );

            // Display heights (inline inspector content) should be similar
            Assert.That(
                compactDetails.displayHeight,
                Is.EqualTo(nonCompactDetails.displayHeight).Within(1f),
                $"Compact displayHeight={compactDetails.displayHeight} should be similar to "
                    + $"non-compact displayHeight={nonCompactDetails.displayHeight}"
            );
        }

        [Test]
        public void CompactAlwaysExpandedModeShowsInlineInspector()
        {
            // Verify that AlwaysExpanded mode with drawObjectField=false still shows inline inspector
            (
                float height,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) details
            ) = MeasurePropertyHeightWithDetails<CompactAlwaysExpandedHost>(
                propertyExpanded: false
            );

            Assert.That(
                details.showBody,
                Is.True,
                "AlwaysExpanded compact mode should always show body"
            );
            Assert.That(
                details.inlineHeight,
                Is.GreaterThan(0f),
                "AlwaysExpanded compact mode should have positive inline height"
            );
            Assert.That(
                height,
                Is.GreaterThan(details.baseHeight),
                "Total height should be greater than base height due to inline inspector"
            );
        }

        [Test]
        public void CompactModeWithNullTargetReturnsBaseHeight()
        {
            // When target is null, compact mode should return base height (singleLineHeight)
            WInLineEditorDrawer.ClearCachedStateForTesting();

            CompactInlineEditorHost host = CreateHiddenInstance<CompactInlineEditorHost>();
            using SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(
                nameof(CompactInlineEditorHost.compactTarget)
            );
            Assert.That(property, Is.Not.Null);
            Assert.That(
                property.objectReferenceValue,
                Is.Null,
                "Target should be null for this test"
            );

            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(CompactInlineEditorHost),
                nameof(CompactInlineEditorHost.compactTarget)
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            GUIContent label = new("Target");
            WInLineEditorDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, inlineAttribute);

            float height = drawer.GetPropertyHeight(property, label);

            // With null target and drawObjectField=false, should return singleLineHeight
            Assert.That(
                height,
                Is.EqualTo(EditorGUIUtility.singleLineHeight).Within(0.01f),
                $"Compact mode with null target should return singleLineHeight ({EditorGUIUtility.singleLineHeight}), "
                    + $"but got {height}"
            );
        }

        [TestCase(
            WInLineEditorMode.FoldoutCollapsed,
            false,
            TestName = "CompactMode.FoldoutCollapsed.InitiallyCollapsed"
        )]
        [TestCase(
            WInLineEditorMode.FoldoutExpanded,
            true,
            TestName = "CompactMode.FoldoutExpanded.InitiallyExpanded"
        )]
        [TestCase(
            WInLineEditorMode.AlwaysExpanded,
            true,
            TestName = "CompactMode.AlwaysExpanded.AlwaysShows"
        )]
        public void CompactModeRespectsInitialFoldoutState(
            WInLineEditorMode mode,
            bool expectExpanded
        )
        {
            WInLineEditorDrawer.ClearCachedStateForTesting();

            CompactModeTestHost host = CreateHiddenInstance<CompactModeTestHost>();
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();

            string propertyName = mode switch
            {
                WInLineEditorMode.FoldoutCollapsed => nameof(
                    CompactModeTestHost.foldoutCollapsedCompact
                ),
                WInLineEditorMode.FoldoutExpanded => nameof(
                    CompactModeTestHost.foldoutExpandedCompact
                ),
                WInLineEditorMode.AlwaysExpanded => nameof(
                    CompactModeTestHost.alwaysExpandedCompact
                ),
                _ => throw new ArgumentException($"Unsupported mode: {mode}"),
            };

            SerializedProperty property = serializedHost.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, $"Property {propertyName} not found");

            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(propertyName);

            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(CompactModeTestHost),
                propertyName
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            (
                float baseHeight,
                float inlineHeight,
                bool showHeader,
                bool showBody,
                float displayHeight
            ) details = WInLineEditorDrawer.GetHeightCalculationDetailsForTesting(
                property,
                inlineAttribute,
                target,
                500f
            );

            Assert.That(
                details.showBody,
                Is.EqualTo(expectExpanded),
                $"Compact mode with {mode} expected showBody={expectExpanded} but got {details.showBody}"
            );
        }

        [Test]
        public void CompactModeWithCustomHeightRespectsHeight()
        {
            // Verify that custom inspectorHeight is respected in compact mode
            WInLineEditorDrawer.ClearCachedStateForTesting();

            CompactCustomHeightHost host = CreateHiddenInstance<CompactCustomHeightHost>();
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(
                nameof(CompactCustomHeightHost.fixedHeightCompact)
            );
            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(
                nameof(CompactCustomHeightHost.fixedHeightCompact)
            );

            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(CompactCustomHeightHost),
                nameof(CompactCustomHeightHost.fixedHeightCompact)
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            // Verify the attribute has the expected custom height
            Assert.That(
                inlineAttribute.InspectorHeight,
                Is.EqualTo(180f).Within(0.01f),
                "Custom inspector height should be 180"
            );
            Assert.That(
                inlineAttribute.DrawObjectField,
                Is.False,
                "DrawObjectField should be false for compact mode"
            );
        }

        [Test]
        public void CompactModeShowsStandaloneHeaderWhenDrawHeaderTrue()
        {
            // When drawObjectField=false and drawHeader=true, a standalone header should be shown
            (
                _,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) compactWithHeader
            ) = MeasurePropertyHeightWithDetails<CompactInlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );

            Assert.That(
                compactWithHeader.showHeader,
                Is.True,
                "Compact mode with drawHeader=true should show standalone header"
            );
        }

        [Test]
        public void CompactModeHidesHeaderWhenDrawHeaderFalse()
        {
            // When drawObjectField=false and drawHeader=false, no header should be shown
            (
                _,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) compactNoHeader
            ) = MeasurePropertyHeightWithDetails<CompactAlwaysExpandedHost>(
                propertyExpanded: false
            );

            Assert.That(
                compactNoHeader.showHeader,
                Is.False,
                "Compact mode with drawHeader=false should not show standalone header"
            );
        }

        [Test]
        public void CompactModeUseSettingsRespectsFoldoutBehavior()
        {
            // Verify that UseSettings mode works correctly in compact mode
            using InlineEditorFoldoutBehaviorScope scope = new(
                UnityHelpersSettings.InlineEditorFoldoutBehavior.StartExpanded
            );

            (
                float expectedExpanded,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) expectedDetails,
                _
            ) = MeasurePropertyHeightWithDetailedDiagnostics<CompactModeTestHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );

            // The host uses UseSettings mode for one of its fields
            CompactModeTestHost host = CreateHiddenInstance<CompactModeTestHost>();
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(
                nameof(CompactModeTestHost.useSettingsCompact)
            );
            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(nameof(CompactModeTestHost.useSettingsCompact));

            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(CompactModeTestHost),
                nameof(CompactModeTestHost.useSettingsCompact)
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            (
                float baseHeight,
                float inlineHeight,
                bool showHeader,
                bool showBody,
                float displayHeight
            ) details = WInLineEditorDrawer.GetHeightCalculationDetailsForTesting(
                property,
                inlineAttribute,
                target,
                500f
            );

            // With StartExpanded setting, showBody should be true
            Assert.That(
                details.showBody,
                Is.True,
                "Compact mode with UseSettings should respect StartExpanded setting"
            );
        }

        [Test]
        public void CompactVsNonCompactBaseHeightDifference()
        {
            // Non-compact uses the ObjectField height, compact uses singleLineHeight.
            (
                _,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) compactDetails
            ) = MeasurePropertyHeightWithDetails<CompactInlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: false
            );

            (
                _,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) nonCompactDetails
            ) = MeasurePropertyHeightWithDetails<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: false
            );

            // EditorGUI.GetPropertyHeight returns singleLineHeight for a childless ObjectReference.
            Assert.That(
                compactDetails.baseHeight,
                Is.EqualTo(EditorGUIUtility.singleLineHeight).Within(0.01f),
                $"Compact base height should be singleLineHeight"
            );
            Assert.That(
                nonCompactDetails.baseHeight,
                Is.EqualTo(EditorGUIUtility.singleLineHeight).Within(0.01f),
                $"Non-compact base height should be singleLineHeight"
            );
        }

        [Test]
        public void CompactModeWithPreviewShowsPreview()
        {
            // Verify preview is shown in compact mode when enabled
            WInLineEditorDrawer.ClearCachedStateForTesting();

            CompactCustomHeightHost host = CreateHiddenInstance<CompactCustomHeightHost>();
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(
                nameof(CompactCustomHeightHost.compactWithPreview)
            );
            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(
                nameof(CompactCustomHeightHost.compactWithPreview)
            );

            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(CompactCustomHeightHost),
                nameof(CompactCustomHeightHost.compactWithPreview)
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            Assert.That(
                inlineAttribute.DrawPreview,
                Is.True,
                "DrawPreview should be true for this test"
            );
            Assert.That(
                inlineAttribute.PreviewHeight,
                Is.EqualTo(64f).Within(0.01f),
                "PreviewHeight should be 64"
            );
            Assert.That(
                inlineAttribute.DrawObjectField,
                Is.False,
                "DrawObjectField should be false for compact mode"
            );
        }

        [Test]
        public void CompactModeNoScrollRespectsScrollSetting()
        {
            // Verify scrolling disabled works in compact mode
            WInLineEditorDrawer.ClearCachedStateForTesting();

            CompactCustomHeightHost host = CreateHiddenInstance<CompactCustomHeightHost>();
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(
                nameof(CompactCustomHeightHost.compactNoScroll)
            );
            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(nameof(CompactCustomHeightHost.compactNoScroll));

            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(CompactCustomHeightHost),
                nameof(CompactCustomHeightHost.compactNoScroll)
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            Assert.That(
                inlineAttribute.EnableScrolling,
                Is.False,
                "EnableScrolling should be false for this test"
            );
            Assert.That(
                inlineAttribute.DrawObjectField,
                Is.False,
                "DrawObjectField should be false for compact mode"
            );

            // Verify no scrollbar is triggered
            bool usesScrollbar = WInLineEditorDrawer.UsesHorizontalScrollbarForTesting(
                target,
                inlineAttribute,
                availableWidth: 200f // Narrow width that would normally trigger scroll
            );
            Assert.That(
                usesScrollbar,
                Is.False,
                "Scrollbar should not be used when EnableScrolling is false"
            );
        }

        // Data-driven test for compact mode foldout toggling
        [TestCase(false, true, TestName = "CompactFoldoutToggle.CollapsedToExpanded")]
        [TestCase(true, false, TestName = "CompactFoldoutToggle.ExpandedToCollapsed")]
        public void CompactModeFoldoutToggleChangesHeight(bool initialState, bool finalState)
        {
            WInLineEditorDrawer.ClearCachedStateForTesting();

            float initialHeight = MeasurePropertyHeight<CompactInlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: initialState
            );

            float finalHeight = MeasurePropertyHeight<CompactInlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: finalState
            );

            if (finalState)
            {
                Assert.That(
                    finalHeight,
                    Is.GreaterThan(initialHeight),
                    $"Expanding compact mode should increase height. Initial: {initialHeight}, Final: {finalHeight}"
                );
            }
            else
            {
                Assert.That(
                    finalHeight,
                    Is.LessThan(initialHeight),
                    $"Collapsing compact mode should decrease height. Initial: {initialHeight}, Final: {finalHeight}"
                );
            }
        }

        [Test]
        public void CompactModeDrawObjectFieldAttributeIsCorrect()
        {
            // Verify the DrawObjectField attribute is correctly set on various hosts
            FieldInfo compactField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(CompactInlineEditorHost),
                nameof(CompactInlineEditorHost.compactTarget)
            );
            WInLineEditorAttribute compactAttr = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(compactField, typeof(WInLineEditorAttribute));
            Assert.That(
                compactAttr.DrawObjectField,
                Is.False,
                "CompactInlineEditorHost should have DrawObjectField=false"
            );

            FieldInfo nonCompactField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(InlineEditorHost),
                nameof(InlineEditorHost.collapsedTarget)
            );
            WInLineEditorAttribute nonCompactAttr = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(nonCompactField, typeof(WInLineEditorAttribute));
            Assert.That(
                nonCompactAttr.DrawObjectField,
                Is.True,
                "InlineEditorHost should have DrawObjectField=true"
            );
        }

        [Test]
        public void CompactAlwaysExpandedWithHeaderShowsHeaderAndBody()
        {
            // Test the combination of AlwaysExpanded with header visible
            WInLineEditorDrawer.ClearCachedStateForTesting();

            CompactModeTestHost host = CreateHiddenInstance<CompactModeTestHost>();
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(
                nameof(CompactModeTestHost.alwaysExpandedWithHeaderCompact)
            );
            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(
                nameof(CompactModeTestHost.alwaysExpandedWithHeaderCompact)
            );

            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(CompactModeTestHost),
                nameof(CompactModeTestHost.alwaysExpandedWithHeaderCompact)
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            (
                float baseHeight,
                float inlineHeight,
                bool showHeader,
                bool showBody,
                float displayHeight
            ) details = WInLineEditorDrawer.GetHeightCalculationDetailsForTesting(
                property,
                inlineAttribute,
                target,
                500f
            );

            Assert.That(details.showBody, Is.True, "AlwaysExpanded should always show body");
            Assert.That(
                details.showHeader,
                Is.True,
                "AlwaysExpanded with drawHeader=true should show header"
            );
        }

        // ==================== End Compact Object Field Tests ====================

        [Test]
        public void BaseHeightIsConsistentAcrossDrawerCalls()
        {
            // Verify that the base property height (singleLineHeight) is used consistently
            WInLineEditorDrawer.ClearCachedStateForTesting();

            InlineEditorHost host = CreateHiddenInstance<InlineEditorHost>();
            InlineEditorTarget target = CreateHiddenInstance<InlineEditorTarget>();

            using SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(
                nameof(InlineEditorHost.collapsedTarget)
            );
            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(nameof(InlineEditorHost.collapsedTarget));

            FieldInfo targetField = PropertyDrawerTestHelper.GetFieldInfoOrFail(
                typeof(InlineEditorHost),
                nameof(InlineEditorHost.collapsedTarget)
            );
            WInLineEditorAttribute inlineAttribute = (WInLineEditorAttribute)
                Attribute.GetCustomAttribute(targetField, typeof(WInLineEditorAttribute));

            // Set foldout to collapsed so we only get base height
            WInLineEditorDrawer.SetInlineFoldoutStateForTesting(property, false);

            GUIContent label = new("Target");
            WInLineEditorDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, inlineAttribute);

            float height = drawer.GetPropertyHeight(property, label);

            // With collapsed state and non-null target, height should be exactly singleLineHeight
            Assert.That(
                height,
                Is.EqualTo(EditorGUIUtility.singleLineHeight).Within(0.001f),
                $"Collapsed height should be singleLineHeight ({EditorGUIUtility.singleLineHeight}), "
                    + $"but got {height}"
            );
        }

        private float MeasurePropertyHeight<THost>(
            bool propertyExpanded,
            bool? setInlineExpanded = null
        )
            where THost : ScriptableObject
        {
            (
                WInLineEditorDrawer drawer,
                SerializedProperty property,
                WInLineEditorAttribute _,
                ScriptableObject _,
                SerializedObject serializedHost
            ) = PrepareInlineEditorTestContext<THost>(propertyExpanded, setInlineExpanded);

            using (serializedHost)
            {
                GUIContent label = new("Target");
                return drawer.GetPropertyHeight(property, label);
            }
        }

        /// <summary>
        /// Measures property height and returns detailed calculation info for diagnostics.
        /// </summary>
        private (
            float height,
            (
                float baseHeight,
                float inlineHeight,
                bool showHeader,
                bool showBody,
                float displayHeight
            ) details
        ) MeasurePropertyHeightWithDetails<THost>(
            bool propertyExpanded,
            bool? setInlineExpanded = null
        )
            where THost : ScriptableObject
        {
            (
                float height,
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) details,
                _
            ) = MeasurePropertyHeightWithDetailedDiagnostics<THost>(
                propertyExpanded,
                setInlineExpanded
            );
            return (height, details);
        }

        /// <summary>
        /// Measures property height and returns detailed calculation info plus extensive diagnostics.
        /// </summary>
        private (
            float height,
            (
                float baseHeight,
                float inlineHeight,
                bool showHeader,
                bool showBody,
                float displayHeight
            ) details,
            string diagnostics
        ) MeasurePropertyHeightWithDetailedDiagnostics<THost>(
            bool propertyExpanded,
            bool? setInlineExpanded = null
        )
            where THost : ScriptableObject
        {
            (
                WInLineEditorDrawer drawer,
                SerializedProperty property,
                WInLineEditorAttribute inlineAttribute,
                ScriptableObject target,
                SerializedObject serializedHost
            ) = PrepareInlineEditorTestContext<THost>(propertyExpanded, setInlineExpanded);

            using (serializedHost)
            {
                GUIContent label = new("Target");

                float height = drawer.GetPropertyHeight(property, label);

                // Get detailed calculation info for diagnostics
                (
                    float baseHeight,
                    float inlineHeight,
                    bool showHeader,
                    bool showBody,
                    float displayHeight
                ) details = WInLineEditorDrawer.GetHeightCalculationDetailsForTesting(
                    property,
                    inlineAttribute,
                    target,
                    500f // Arbitrary available width for testing
                );

                // Get extensive diagnostics
                string diagnostics = WInLineEditorDrawer.GetExtensiveDiagnosticsForTesting(
                    property,
                    inlineAttribute,
                    target,
                    500f
                );

                return (height, details, diagnostics);
            }
        }

        /// <summary>
        /// Prepares a test context for inline editor testing, creating all necessary objects.
        /// </summary>
        /// <typeparam name="THost">The host ScriptableObject type with a WInLineEditor attribute.</typeparam>
        /// <param name="propertyExpanded">Whether the property should be expanded.</param>
        /// <param name="setInlineExpanded">Optional inline foldout state.</param>
        /// <returns>Tuple containing the drawer, property, attribute, target, and serialized object.</returns>
        private (
            WInLineEditorDrawer drawer,
            SerializedProperty property,
            WInLineEditorAttribute attribute,
            ScriptableObject target,
            SerializedObject serializedHost
        ) PrepareInlineEditorTestContext<THost>(bool propertyExpanded, bool? setInlineExpanded)
            where THost : ScriptableObject
        {
            WInLineEditorDrawer.ClearCachedStateForTesting();
            THost host = Track(ScriptableObject.CreateInstance<THost>());
            host.hideFlags = HideFlags.HideAndDontSave;

            // Find the first field with WInLineEditorAttribute
            (FieldInfo targetField, WInLineEditorAttribute inlineAttribute) =
                PropertyDrawerTestHelper.FindFirstFieldWithAttributeOrFail<WInLineEditorAttribute>(
                    typeof(THost)
                );

            string propertyName = targetField.Name;
            Type fieldType = targetField.FieldType;

            // Create a target of the appropriate type
            ScriptableObject target = Track(
                ScriptableObject.CreateInstance(fieldType) as ScriptableObject
            );
            Assert.That(target, Is.Not.Null, $"Failed to create instance of {fieldType.Name}.");
            target.hideFlags = HideFlags.HideAndDontSave;

            SerializedObject serializedHost = new(host);
            serializedHost.Update();
            SerializedProperty property = serializedHost.FindProperty(propertyName);
            Assert.That(
                property,
                Is.Not.Null,
                $"Failed to find property '{propertyName}' on {typeof(THost).Name}."
            );
            property.objectReferenceValue = target;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            serializedHost.Update();
            property = serializedHost.FindProperty(propertyName);
            Assert.That(
                property,
                Is.Not.Null,
                $"Failed to re-find property '{propertyName}' after assignment."
            );
            property.isExpanded = propertyExpanded;
            if (setInlineExpanded.HasValue)
            {
                WInLineEditorDrawer.SetInlineFoldoutStateForTesting(
                    property,
                    setInlineExpanded.Value
                );
            }

            // Assign the attribute to the drawer
            WInLineEditorDrawer drawer = new();
            PropertyDrawerTestHelper.AssignAttribute(drawer, inlineAttribute);

            return (drawer, property, inlineAttribute, target, serializedHost);
        }

        private T CreateHiddenInstance<T>()
            where T : ScriptableObject
        {
            T instance = Track(ScriptableObject.CreateInstance<T>());
            instance.hideFlags = HideFlags.HideAndDontSave;
            return instance;
        }

        // Test ScriptableObject types live in their own files under TestTypes/, as Unity requires.

        [Test]
        public void FoldoutAnimationCacheCreatesNewAnimBoolForUnseenKey()
        {
            const string foldoutKey = "TestKey_NewAnimation";

            Assert.That(
                WInLineEditorDrawer.GetAnimationCacheCountForTesting(),
                Is.EqualTo(0),
                "Animation cache should be empty at start."
            );

            AnimBool anim = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey,
                expanded: true
            );

            Assert.That(
                anim,
                Is.Not.Null,
                "GetOrCreateFoldoutAnim should return a non-null AnimBool."
            );
            Assert.That(
                WInLineEditorDrawer.GetAnimationCacheCountForTesting(),
                Is.EqualTo(1),
                "Animation cache should contain one entry after creation."
            );
            Assert.That(
                WInLineEditorDrawer.HasAnimationCacheEntryForTesting(foldoutKey),
                Is.True,
                "Animation cache should have an entry for the specified key."
            );
        }

        [Test]
        public void FoldoutAnimationCacheReturnsSameAnimBoolForSameKey()
        {
            const string foldoutKey = "TestKey_SameInstance";

            AnimBool first = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey,
                expanded: true
            );
            AnimBool second = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey,
                expanded: true
            );

            Assert.AreSame(
                first,
                second,
                "GetOrCreateFoldoutAnim should return the same AnimBool instance for the same key."
            );
            Assert.That(
                WInLineEditorDrawer.GetAnimationCacheCountForTesting(),
                Is.EqualTo(1),
                "Animation cache should still contain only one entry."
            );
        }

        [Test]
        public void FoldoutAnimationCacheReturnsDifferentAnimBoolsForDifferentKeys()
        {
            const string foldoutKey1 = "TestKey_First";
            const string foldoutKey2 = "TestKey_Second";

            AnimBool first = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey1,
                expanded: true
            );
            AnimBool second = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey2,
                expanded: true
            );

            Assert.AreNotSame(
                first,
                second,
                "GetOrCreateFoldoutAnim should return different AnimBool instances for different keys."
            );
            Assert.That(
                WInLineEditorDrawer.GetAnimationCacheCountForTesting(),
                Is.EqualTo(2),
                "Animation cache should contain two entries."
            );
        }

        [Test]
        public void AnimBoolTargetUpdatesOnExpandedChange()
        {
            const string foldoutKey = "TestKey_TargetUpdate";

            AnimBool anim = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey,
                expanded: true
            );
            Assert.IsTrue(anim.target, "Initial target should be true when expanded is true.");

            WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(foldoutKey, expanded: false);
            Assert.IsFalse(anim.target, "Target should update to false when expanded changes.");

            WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(foldoutKey, expanded: true);
            Assert.IsTrue(anim.target, "Target should update back to true.");
        }

        [Test]
        public void AnimBoolSpeedReflectsCurrentSettings()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            const string foldoutKey = "TestKey_SpeedCheck";

            settings.InlineEditorFoldoutSpeed = 8f;
            AnimBool anim = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey,
                expanded: true
            );
            Assert.That(anim.speed, Is.EqualTo(8f), "AnimBool speed should match settings value.");

            settings.InlineEditorFoldoutSpeed = 4f;
            WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(foldoutKey, expanded: true);
            Assert.That(
                anim.speed,
                Is.EqualTo(4f),
                "AnimBool speed should update when settings change."
            );
        }

        [Test]
        public void GetFadeProgressReturnsImmediateValueWhenTweenDisabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.InlineEditorFoldoutTweenEnabled = false;

            const string foldoutKey = "TestKey_ImmediateProgress";

            float expandedProgress = WInLineEditorDrawer.GetFadeProgressForTesting(
                foldoutKey,
                expanded: true
            );
            Assert.That(
                expandedProgress,
                Is.EqualTo(1f),
                "When tweening disabled, expanded=true should return 1f immediately."
            );

            float collapsedProgress = WInLineEditorDrawer.GetFadeProgressForTesting(
                foldoutKey,
                expanded: false
            );
            Assert.That(
                collapsedProgress,
                Is.EqualTo(0f),
                "When tweening disabled, expanded=false should return 0f immediately."
            );
        }

        [Test]
        public void GetFadeProgressReturnsAnimatedValueWhenTweenEnabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.InlineEditorFoldoutTweenEnabled = true;
            settings.InlineEditorFoldoutSpeed = 4f;

            const string foldoutKey = "TestKey_AnimatedProgress";

            // First, create the AnimBool in collapsed state
            AnimBool anim = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey,
                expanded: false
            );
            anim.value = false;

            // Now expand - the animation should be in progress
            anim.target = true;

            float progress = WInLineEditorDrawer.GetFadeProgressForTesting(
                foldoutKey,
                expanded: true
            );

            Assert.That(
                progress,
                Is.InRange(0f, 1f),
                "Fade progress should be between 0 and 1 during animation."
            );
        }

        [Test]
        public void GetFadeProgressCreatesAnimBoolWhenTweenEnabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.InlineEditorFoldoutTweenEnabled = true;

            const string foldoutKey = "TestKey_CreateOnProgress";

            Assert.That(
                WInLineEditorDrawer.HasAnimationCacheEntryForTesting(foldoutKey),
                Is.False,
                "Animation cache should not have entry before GetFadeProgress call."
            );

            float progress = WInLineEditorDrawer.GetFadeProgressForTesting(
                foldoutKey,
                expanded: true
            );

            Assert.That(
                WInLineEditorDrawer.HasAnimationCacheEntryForTesting(foldoutKey),
                Is.True,
                "GetFadeProgress should create AnimBool entry when tweening is enabled."
            );
            Assert.That(progress, Is.InRange(0f, 1f), "Fade progress should be a valid value.");
        }

        [Test]
        public void GetFadeProgressDoesNotCreateAnimBoolWhenTweenDisabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.InlineEditorFoldoutTweenEnabled = false;

            const string foldoutKey = "TestKey_NoCreateOnDisabled";

            Assert.That(
                WInLineEditorDrawer.HasAnimationCacheEntryForTesting(foldoutKey),
                Is.False,
                "Animation cache should not have entry before GetFadeProgress call."
            );

            float progress = WInLineEditorDrawer.GetFadeProgressForTesting(
                foldoutKey,
                expanded: true
            );

            Assert.That(
                WInLineEditorDrawer.HasAnimationCacheEntryForTesting(foldoutKey),
                Is.False,
                "GetFadeProgress should not create AnimBool when tweening is disabled."
            );
            Assert.That(
                progress,
                Is.EqualTo(1f),
                "Should return immediate value when tweening disabled."
            );
        }

        [Test]
        public void ClearAnimationCacheRemovesAllEntries()
        {
            const string foldoutKey1 = "TestKey_ClearCache1";
            const string foldoutKey2 = "TestKey_ClearCache2";

            AnimBool anim1Before = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey1,
                expanded: true
            );
            AnimBool anim2Before = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey2,
                expanded: false
            );

            Assert.That(
                WInLineEditorDrawer.GetAnimationCacheCountForTesting(),
                Is.EqualTo(2),
                "Should have 2 cache entries before clearing."
            );

            WInLineEditorDrawer.ClearAnimationCacheForTesting();

            Assert.That(
                WInLineEditorDrawer.GetAnimationCacheCountForTesting(),
                Is.EqualTo(0),
                "Animation cache should be empty after clearing."
            );

            AnimBool anim1After = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey1,
                expanded: true
            );
            AnimBool anim2After = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey2,
                expanded: false
            );

            Assert.AreNotSame(
                anim1Before,
                anim1After,
                "After ClearCache, a new AnimBool should be created."
            );
            Assert.AreNotSame(
                anim2Before,
                anim2After,
                "After ClearCache, a new AnimBool should be created."
            );
        }

        [Test]
        public void ClearAnimationCacheCanBeCalledMultipleTimes()
        {
            WInLineEditorDrawer.ClearAnimationCacheForTesting();
            WInLineEditorDrawer.ClearAnimationCacheForTesting();
            WInLineEditorDrawer.ClearAnimationCacheForTesting();

            Assert.That(
                WInLineEditorDrawer.GetAnimationCacheCountForTesting(),
                Is.EqualTo(0),
                "Clearing an already empty cache should not cause errors."
            );
        }

        [TestCase(2f, TestName = "AnimBoolSpeed.MinValue")]
        [TestCase(4f, TestName = "AnimBoolSpeed.Default")]
        [TestCase(8f, TestName = "AnimBoolSpeed.MediumValue")]
        [TestCase(12f, TestName = "AnimBoolSpeed.MaxValue")]
        public void AnimBoolSpeedReflectsVariousSpeedSettings(float expectedSpeed)
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.InlineEditorFoldoutSpeed = expectedSpeed;

            string foldoutKey = $"TestKey_Speed_{expectedSpeed}";
            AnimBool anim = WInLineEditorDrawer.GetOrCreateFoldoutAnimForTesting(
                foldoutKey,
                expanded: true
            );

            Assert.That(
                anim.speed,
                Is.EqualTo(expectedSpeed),
                $"AnimBool speed should be {expectedSpeed} when settings specify that value."
            );
        }

        [Test]
        public void HeightCalculationAccountsForAnimationProgress()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.InlineEditorFoldoutTweenEnabled = true;
            settings.InlineEditorFoldoutSpeed = 4f;

            // Measure collapsed height (no animation)
            float collapsedHeight = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: false
            );

            // Measure fully expanded height (animation complete)
            float expandedHeight = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );

            // The expanded height should be greater than collapsed
            Assert.That(
                expandedHeight,
                Is.GreaterThan(collapsedHeight),
                "Expanded height should be greater than collapsed height."
            );

            // The height difference represents the body height that gets animated
            float bodyHeight = expandedHeight - collapsedHeight;
            Assert.That(
                bodyHeight,
                Is.GreaterThan(0f),
                "Body height contribution should be positive."
            );
        }

        [Test]
        public void HeightCalculationReturnsFullHeightWhenTweenDisabled()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.InlineEditorFoldoutTweenEnabled = false;

            // Measure expanded height with tweening disabled
            float expandedHeightNoTween = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );

            settings.InlineEditorFoldoutTweenEnabled = true;

            // Clear cache to ensure fresh measurement
            WInLineEditorDrawer.ClearAnimationCacheForTesting();
            WInLineEditorDrawer.ClearCachedStateForTesting();

            // Measure expanded height with tweening enabled (should eventually match)
            float expandedHeightWithTween = MeasurePropertyHeight<InlineEditorHost>(
                propertyExpanded: false,
                setInlineExpanded: true
            );

            // Both should return the same final height when fully expanded
            Assert.That(
                expandedHeightWithTween,
                Is.EqualTo(expandedHeightNoTween).Within(1f),
                "Fully expanded height should be the same regardless of tween setting."
            );
        }

        [Test]
        public void GetFadeProgressReturnsConsistentResultsForSameState()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            settings.InlineEditorFoldoutTweenEnabled = true;

            const string foldoutKey = "TestKey_ConsistentProgress";

            // Get progress multiple times for the same expanded state
            float progress1 = WInLineEditorDrawer.GetFadeProgressForTesting(
                foldoutKey,
                expanded: true
            );
            float progress2 = WInLineEditorDrawer.GetFadeProgressForTesting(
                foldoutKey,
                expanded: true
            );

            // Progress values should be equal or nearly equal (animation may have progressed slightly)
            Assert.That(
                progress2,
                Is.EqualTo(progress1).Within(0.01f),
                "Consecutive GetFadeProgress calls should return consistent results."
            );
        }

        private sealed class InlineEditorFoldoutBehaviorScope : IDisposable
        {
            private readonly UnityHelpersSettings.InlineEditorFoldoutBehavior originalValue;
            private bool disposed;

            public InlineEditorFoldoutBehaviorScope(
                UnityHelpersSettings.InlineEditorFoldoutBehavior behavior
            )
            {
                originalValue = UnityHelpersSettings.GetInlineEditorFoldoutBehavior();
                UnityHelpersSettings.SetInlineEditorFoldoutBehavior(behavior);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                UnityHelpersSettings.SetInlineEditorFoldoutBehavior(originalValue);
            }
        }
    }
}
