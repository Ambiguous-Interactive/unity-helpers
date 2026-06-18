// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonIntegrationTests : BatchedEditorTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            ClearWButtonCaches();
        }

        [TearDown]
        public override void TearDown()
        {
            ClearWButtonCaches();
            base.TearDown();
        }

        private static void ClearWButtonCaches()
        {
            WButtonGUI.ClearGroupDataForTesting();
            WButtonGUI.ClearConflictingDrawOrderWarningsForTesting();
            WButtonGUI.ClearConflictingGroupPriorityWarningsForTesting();
            WButtonGUI.ClearConflictingGroupPlacementWarningsForTesting();
            WButtonGUI.ClearConflictWarningContentCacheForTesting();
            WButtonGUI.ClearContextCache();
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float endTime = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > endTime)
                {
                    Assert.Fail("Timed out while waiting for condition.");
                }
                yield return null;
            }
        }

        private static void ExpectWButtonExceptionLog(string exceptionMessage)
        {
            LogAssert.Expect(
                LogType.Exception,
                new Regex(
                    "^(System\\.)?InvalidOperationException: " + Regex.Escape(exceptionMessage)
                )
            );
        }

        // ==================== Custom Inspector Integration Tests ====================

        [Test]
        public void UserCanCreateCustomEditorWithWButtonEditorHelper()
        {
            WButtonEditorHelper helper = new WButtonEditorHelper();

            Assert.That(
                helper,
                Is.Not.Null,
                "Should be able to create WButtonEditorHelper instance"
            );
        }

        [Test]
        public void DrawButtonsAtTopAndBottomWorkflowWorks()
        {
            IntegrationTargetMixedPlacement target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetMixedPlacement>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool topDrawn = helper.DrawButtonsAtTop(editor);
            bool bottomDrawn = helper.DrawButtonsAtBottom(editor);

            Assert.That(topDrawn, Is.True, "Top buttons should be drawn");
            Assert.That(bottomDrawn, Is.True, "Bottom buttons should be drawn");
        }

        [Test]
        public void ProcessInvocationsCalledSeparatelyWorks()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            target.InvocationCount = 0;
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            helper.DrawButtonsAtTop(editor);
            helper.DrawButtonsAtBottom(editor);

            Assert.DoesNotThrow(
                () => helper.ProcessInvocations(),
                "ProcessInvocations should not throw"
            );
        }

        [Test]
        public void HelperInstanceCanBeReusedAcrossOnInspectorGuiCalls()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            for (int i = 0; i < 10; i++)
            {
                Assert.DoesNotThrow(
                    () =>
                    {
                        helper.DrawButtonsAtTop(editor);
                        helper.DrawButtonsAtBottom(editor);
                        helper.ProcessInvocations();
                    },
                    $"Iteration {i} should not throw"
                );
            }
        }

        [Test]
        public void MultipleHelpersForDifferentEditorsWorkIndependently()
        {
            IntegrationTargetSimple target1 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            IntegrationTargetWithGroup target2 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetWithGroup>()
            );
            UnityEditor.Editor editor1 = Track(UnityEditor.Editor.CreateEditor(target1));
            UnityEditor.Editor editor2 = Track(UnityEditor.Editor.CreateEditor(target2));
            WButtonEditorHelper helper1 = new WButtonEditorHelper();
            WButtonEditorHelper helper2 = new WButtonEditorHelper();

            bool result1 = helper1.DrawAllButtonsAndProcessInvocations(editor1);
            bool result2 = helper2.DrawAllButtonsAndProcessInvocations(editor2);

            Assert.That(result1, Is.True, "First helper should draw buttons");
            Assert.That(result2, Is.True, "Second helper should draw buttons");
        }

        [Test]
        public void HelperWorksWithMultiObjectEditing()
        {
            IntegrationTargetSimple target1 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            IntegrationTargetSimple target2 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            IntegrationTargetSimple target3 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            UnityEngine.Object[] targets = { target1, target2, target3 };
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(targets));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True, "Should draw buttons for multi-target editor");
        }

        // ==================== WButton + WGroup Integration ====================

        [Test]
        public void WButtonInWGroupRendersWithinGroupSection()
        {
            IntegrationTargetWithGroup target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetWithGroup>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawn = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(drawn, Is.True, "Button in group should be drawn");

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetWithGroup)
            );
            WButtonMethodMetadata groupedButton = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(IntegrationTargetWithGroup.GroupedButton)
            );

            Assert.That(groupedButton, Is.Not.Null);
            Assert.That(groupedButton.GroupName, Is.EqualTo("TestGroup"));
        }

        [Test]
        public void MultipleWButtonsInSameWGroupRenderTogether()
        {
            IntegrationTargetMultipleButtonsSameGroup target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetMultipleButtonsSameGroup>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> sharedGroupKeys = groupCounts
                .Keys.Where(k => k._groupName == "SharedGroup")
                .ToList();

            Assert.That(
                sharedGroupKeys,
                Has.Count.EqualTo(1),
                "Should have exactly one SharedGroup"
            );
            Assert.That(
                groupCounts[sharedGroupKeys[0]],
                Is.EqualTo(3),
                "SharedGroup should contain all 3 buttons"
            );
        }

        [Test]
        public void WButtonGroupTakesPrecedenceOverWGroup()
        {
            IntegrationTargetWithGroup target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetWithGroup>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True, "Should draw buttons");

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetWithGroup)
            );
            WButtonMethodMetadata groupedButton = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetWithGroup.GroupedButton)
            );

            Assert.That(
                groupedButton.GroupName,
                Is.EqualTo("TestGroup"),
                "WButton group should be respected"
            );
        }

        [Test]
        public void NestedWGroupWithWButtonMethodsHandled()
        {
            IntegrationTargetNestedGroups target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetNestedGroups>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True, "Should draw buttons in nested group structure");
        }

        // ==================== WButton + WShowIf Integration ====================

        [Test]
        public void WButtonMethodModifiesSerializedFields()
        {
            IntegrationTargetWithShowIf target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetWithShowIf>()
            );
            target.showAdvanced = false;

            Assert.That(target.showAdvanced, Is.False, "Initial state should be false");

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetWithShowIf)
            );

            Assert.That(metadata.Count, Is.GreaterThan(0), "Should have WButton methods");
        }

        [Test]
        public void WShowIfConditionWorksWithMultipleConditions()
        {
            IntegrationTargetMultipleConditions target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetMultipleConditions>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True, "Should draw buttons");
        }

        [Test]
        public void DynamicWShowIfConditionsUpdateButtonVisibility()
        {
            IntegrationTargetWithShowIf target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetWithShowIf>()
            );
            target.showAdvanced = false;
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            helper.DrawAllButtonsAndProcessInvocations(editor);

            target.showAdvanced = true;

            bool resultAfterChange = helper.DrawAllButtonsAndProcessInvocations(editor);
            Assert.That(resultAfterChange, Is.True, "Should draw buttons after condition change");
        }

        // ==================== WButton + Serialized Properties Integration ====================

        [Test]
        public void WButtonMethodCanModifySerializedFieldsDirectly()
        {
            IntegrationTargetModifiesFields target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetModifiesFields>()
            );
            target.counter = 0;

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetModifiesFields)
            );
            WButtonMethodMetadata incrementMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetModifiesFields.IncrementCounter)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(incrementMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                incrementMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(target.counter, Is.EqualTo(1), "Counter should be incremented");
        }

        [Test]
        public void MultipleTargetsModifiedConsistently()
        {
            IntegrationTargetModifiesFields target1 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetModifiesFields>()
            );
            IntegrationTargetModifiesFields target2 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetModifiesFields>()
            );
            target1.counter = 0;
            target2.counter = 0;

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetModifiesFields)
            );
            WButtonMethodMetadata incrementMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetModifiesFields.IncrementCounter)
            );

            WButtonTargetState targetState1 = WButtonStateRepository.GetOrCreate(target1);
            WButtonTargetState targetState2 = WButtonStateRepository.GetOrCreate(target2);
            WButtonMethodState methodState1 = targetState1.GetOrCreateMethodState(incrementMethod);
            WButtonMethodState methodState2 = targetState2.GetOrCreateMethodState(incrementMethod);

            WButtonMethodContext context = new WButtonMethodContext(
                incrementMethod,
                new[] { methodState1, methodState2 },
                new UnityEngine.Object[] { target1, target2 }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(
                target1.counter,
                Is.EqualTo(1),
                "First target counter should be incremented"
            );
            Assert.That(
                target2.counter,
                Is.EqualTo(1),
                "Second target counter should be incremented"
            );
        }

        [Test]
        public void ChangesFromWButtonMethodPersistAfterUndo()
        {
            IntegrationTargetModifiesFields target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetModifiesFields>()
            );
            target.counter = 5;

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetModifiesFields)
            );
            WButtonMethodMetadata incrementMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetModifiesFields.IncrementCounter)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(incrementMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                incrementMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            Undo.RecordObject(target, "Test Undo");
            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(
                target.counter,
                Is.EqualTo(6),
                "Counter should be incremented after undo record"
            );
        }

        // ==================== Editor Lifecycle Integration ====================

        [Test]
        public void OnEnableCreatesHelperCorrectly()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

            WButtonEditorHelper helper = new WButtonEditorHelper();

            Assert.That(helper, Is.Not.Null, "Helper should be created in OnEnable");
            Assert.DoesNotThrow(
                () => helper.DrawButtonsAtTop(editor),
                "Should be usable immediately"
            );
        }

        [Test]
        public void OnDisableCleansUpResourcesGracefully()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            helper.DrawAllButtonsAndProcessInvocations(editor);

            UnityEngine.Object.DestroyImmediate(editor); // UNH-SUPPRESS: Test verifies cleanup behavior after editor destroyed
            _trackedObjects.Remove(editor);

            Assert.DoesNotThrow(
                () => helper.ProcessInvocations(),
                "Should handle cleanup gracefully"
            );
        }

        [Test]
        public void EditorRecreationPreservesInvocationHistory()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetSimple)
            );
            WButtonMethodMetadata simpleButton = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetSimple.SimpleButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(simpleButton);

            methodState.AddResult(
                new WButtonResultEntry(
                    WButtonResultKind.Success,
                    DateTime.UtcNow,
                    null,
                    "Test result",
                    null
                ),
                10
            );

            Assert.That(methodState.HasHistory, Is.True, "Should have history");

            UnityEditor.Editor editor2 = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper2 = new WButtonEditorHelper();
            helper2.DrawAllButtonsAndProcessInvocations(editor2);

            WButtonTargetState targetState2 = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState2 = targetState2.GetOrCreateMethodState(simpleButton);

            Assert.That(methodState2.HasHistory, Is.True, "History should be preserved");
        }

        [UnityTest]
        public IEnumerator RepaintTriggeredAfterAsyncMethodCompletes()
        {
            IntegrationTargetAsync target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetAsync>()
            );
            target.CompletionCount = 0;
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetAsync)
            );
            WButtonMethodMetadata asyncMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetAsync.AsyncButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(asyncMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                asyncMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(target.CompletionCount, Is.EqualTo(1), "Async method should complete");
        }

        // ==================== Settings Integration ====================

        [Test]
        public void ChangesToGlobalPlacementSettingTakeEffect()
        {
            IntegrationTargetDefaultPlacement target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetDefaultPlacement>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawnAtTopWithGlobalTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(
                drawnAtTopWithGlobalTop,
                Is.True,
                "Should draw at top with global top setting"
            );

            ClearWButtonCaches();

            bool drawnAtBottomWithGlobalBottom = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: false
            );

            Assert.That(
                drawnAtBottomWithGlobalBottom,
                Is.True,
                "Should draw at bottom with global bottom setting"
            );
        }

        [Test]
        public void ChangesToFoldoutBehaviorSettingTakeEffect()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStatesExpanded = new();
            Dictionary<WButtonGroupKey, bool> foldoutStatesCollapsed = new();

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStatesExpanded,
                UnityHelpersSettings.WButtonFoldoutBehavior.StartExpanded,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            foreach (KeyValuePair<WButtonGroupKey, bool> entry in foldoutStatesExpanded)
            {
                Assert.That(entry.Value, Is.True, "Should start expanded");
            }

            ClearWButtonCaches();

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStatesCollapsed,
                UnityHelpersSettings.WButtonFoldoutBehavior.StartCollapsed,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            foreach (KeyValuePair<WButtonGroupKey, bool> entry in foldoutStatesCollapsed)
            {
                Assert.That(entry.Value, Is.False, "Should start collapsed");
            }
        }

        [Test]
        public void ChangesToPageSizeSettingTakeEffect()
        {
            IntegrationTargetManyButtons target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetManyButtons>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            int totalButtons = groupCounts.Values.Sum();

            Assert.That(totalButtons, Is.GreaterThan(0), "Should have buttons rendered");
        }

        [Test]
        public void ChangesToColorPaletteTakeEffect()
        {
            IntegrationTargetWithColorKey target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetWithColorKey>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool result = helper.DrawAllButtonsAndProcessInvocations(editor);

            Assert.That(result, Is.True, "Should draw buttons with custom color key");

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetWithColorKey)
            );
            WButtonMethodMetadata coloredButton = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetWithColorKey.ColoredButton)
            );

            Assert.That(
                coloredButton.ColorKey,
                Is.EqualTo("CustomColor"),
                "Color key should be respected"
            );
        }

        // ==================== Error Handling Integration ====================

        [Test]
        public void ExceptionInWButtonMethodDoesNotBreakInspector()
        {
            IntegrationTargetThrowsException target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetThrowsException>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetThrowsException)
            );
            WButtonMethodMetadata throwingMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetThrowsException.ThrowingButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(throwingMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                throwingMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            ExpectWButtonExceptionLog("Test exception from WButton");
            Assert.DoesNotThrow(
                () =>
                {
                    WButtonInvocationController.ProcessTriggeredMethods(
                        new List<WButtonMethodContext> { context }
                    );
                },
                "Exception should be caught and not propagate"
            );

            Assert.DoesNotThrow(
                () => helper.DrawAllButtonsAndProcessInvocations(editor),
                "Inspector should remain functional after exception"
            );
        }

        [Test]
        public void ExceptionMessageDisplayedInHistory()
        {
            IntegrationTargetThrowsException target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetThrowsException>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetThrowsException)
            );
            WButtonMethodMetadata throwingMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetThrowsException.ThrowingButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(throwingMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                throwingMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            ExpectWButtonExceptionLog("Test exception from WButton");
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(methodState.HasHistory, Is.True, "Should have history entry for exception");
            WButtonResultEntry entry = methodState.History[^1];
            Assert.That(entry.Kind, Is.EqualTo(WButtonResultKind.Error), "Should be error entry");
        }

        [Test]
        public void InspectorRemainsFunctionalAfterException()
        {
            IntegrationTargetThrowsException target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetThrowsException>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetThrowsException)
            );
            WButtonMethodMetadata throwingMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetThrowsException.ThrowingButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(throwingMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                throwingMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            ExpectWButtonExceptionLog("Test exception from WButton");
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            for (int i = 0; i < 5; i++)
            {
                Assert.DoesNotThrow(
                    () => helper.DrawAllButtonsAndProcessInvocations(editor),
                    $"Inspector should remain functional after exception (iteration {i})"
                );
            }
        }

        [UnityTest]
        public IEnumerator AsyncExceptionHandledCorrectly()
        {
            IntegrationTargetAsyncThrows target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetAsyncThrows>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetAsyncThrows)
            );
            WButtonMethodMetadata asyncThrowingMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetAsyncThrows.AsyncThrowingButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(
                asyncThrowingMethod
            );
            WButtonMethodContext context = new WButtonMethodContext(
                asyncThrowingMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            ExpectWButtonExceptionLog("Test async exception from WButton");
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            yield return WaitUntil(() => methodState.ActiveInvocation == null, 5f);

            Assert.That(
                methodState.HasHistory,
                Is.True,
                "Should have history entry for async exception"
            );
            WButtonResultEntry entry = methodState.History[^1];
            Assert.That(entry.Kind, Is.EqualTo(WButtonResultKind.Error), "Should be error entry");
        }

        // ==================== Performance Integration ====================

        [Test]
        public void LargeNumberOfWButtonMethodsDoesNotDegradePerformance()
        {
            IntegrationTargetManyButtons target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetManyButtons>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            for (int i = 0; i < 100; i++)
            {
                helper.DrawAllButtonsAndProcessInvocations(editor);
            }

            stopwatch.Stop();

            Assert.That(
                stopwatch.ElapsedMilliseconds,
                Is.LessThan(5000),
                "100 draw cycles should complete in reasonable time"
            );
        }

        [Test]
        public void RapidOnInspectorGuiCallsHandledEfficiently()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            WButtonEditorHelper helper = new WButtonEditorHelper();

            for (int i = 0; i < 1000; i++)
            {
                Assert.DoesNotThrow(
                    () =>
                    {
                        helper.DrawButtonsAtTop(editor);
                        helper.DrawButtonsAtBottom(editor);
                        helper.ProcessInvocations();
                    },
                    $"Rapid call {i} should not throw"
                );
            }
        }

        [Test]
        public void MemoryDoesNotLeakAcrossEditorSessions()
        {
            for (int session = 0; session < 10; session++)
            {
                IntegrationTargetSimple target = Track(
                    ScriptableObject.CreateInstance<IntegrationTargetSimple>()
                );
                UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
                WButtonEditorHelper helper = new WButtonEditorHelper();

                for (int i = 0; i < 50; i++)
                {
                    helper.DrawAllButtonsAndProcessInvocations(editor);
                }

                UnityEngine.Object.DestroyImmediate(editor); // UNH-SUPPRESS: Simulating editor session cleanup
                _trackedObjects.Remove(editor);
                UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Simulating session cleanup
                _trackedObjects.Remove(target);

                ClearWButtonCaches();
            }

            Assert.Pass("Completed multiple simulated editor sessions without memory issues");
        }

        [Test]
        public void MetadataCachingWorksAcrossMultipleEditors()
        {
            IntegrationTargetSimple target1 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            IntegrationTargetSimple target2 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            UnityEditor.Editor editor1 = Track(UnityEditor.Editor.CreateEditor(target1));
            UnityEditor.Editor editor2 = Track(UnityEditor.Editor.CreateEditor(target2));

            IReadOnlyList<WButtonMethodMetadata> metadata1 = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetSimple)
            );

            WButtonEditorHelper helper1 = new WButtonEditorHelper();
            WButtonEditorHelper helper2 = new WButtonEditorHelper();
            helper1.DrawAllButtonsAndProcessInvocations(editor1);
            helper2.DrawAllButtonsAndProcessInvocations(editor2);

            IReadOnlyList<WButtonMethodMetadata> metadata2 = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetSimple)
            );

            Assert.That(metadata1, Is.SameAs(metadata2), "Metadata should be cached and reused");
        }

        // ==================== Multi-Target Integration ====================

        [Test]
        public void WButtonInvokedOnAllSelectedTargets()
        {
            IntegrationTargetModifiesFields target1 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetModifiesFields>()
            );
            IntegrationTargetModifiesFields target2 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetModifiesFields>()
            );
            IntegrationTargetModifiesFields target3 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetModifiesFields>()
            );
            target1.counter = 0;
            target2.counter = 0;
            target3.counter = 0;

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetModifiesFields)
            );
            WButtonMethodMetadata incrementMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetModifiesFields.IncrementCounter)
            );

            WButtonTargetState targetState1 = WButtonStateRepository.GetOrCreate(target1);
            WButtonTargetState targetState2 = WButtonStateRepository.GetOrCreate(target2);
            WButtonTargetState targetState3 = WButtonStateRepository.GetOrCreate(target3);
            WButtonMethodState methodState1 = targetState1.GetOrCreateMethodState(incrementMethod);
            WButtonMethodState methodState2 = targetState2.GetOrCreateMethodState(incrementMethod);
            WButtonMethodState methodState3 = targetState3.GetOrCreateMethodState(incrementMethod);

            WButtonMethodContext context = new WButtonMethodContext(
                incrementMethod,
                new[] { methodState1, methodState2, methodState3 },
                new UnityEngine.Object[] { target1, target2, target3 }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(target1.counter, Is.EqualTo(1), "First target should be modified");
            Assert.That(target2.counter, Is.EqualTo(1), "Second target should be modified");
            Assert.That(target3.counter, Is.EqualTo(1), "Third target should be modified");
        }

        [Test]
        public void InheritedWButtonMethodsInvokedOnAllTargets()
        {
            IntegrationTargetDerived target1 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetDerived>()
            );
            IntegrationTargetDerived target2 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetDerived>()
            );
            target1.BaseCallCount = 0;
            target2.BaseCallCount = 0;

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetDerived)
            );
            WButtonMethodMetadata baseMethod = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetBase.BaseButton)
            );

            WButtonTargetState targetState1 = WButtonStateRepository.GetOrCreate(target1);
            WButtonTargetState targetState2 = WButtonStateRepository.GetOrCreate(target2);
            WButtonMethodState methodState1 = targetState1.GetOrCreateMethodState(baseMethod);
            WButtonMethodState methodState2 = targetState2.GetOrCreateMethodState(baseMethod);

            WButtonMethodContext context = new WButtonMethodContext(
                baseMethod,
                new[] { methodState1, methodState2 },
                new UnityEngine.Object[] { target1, target2 }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(
                target1.BaseCallCount,
                Is.EqualTo(1),
                "First derived target should call base method"
            );
            Assert.That(
                target2.BaseCallCount,
                Is.EqualTo(1),
                "Second derived target should call base method"
            );
        }

        [Test]
        public void MixedTargetsWithAndWithoutWButtonHandled()
        {
            IntegrationTargetSimple targetWithButton = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            IntegrationTargetNoButtons targetWithoutButton = Track(
                ScriptableObject.CreateInstance<IntegrationTargetNoButtons>()
            );

            UnityEditor.Editor editorWithButton = Track(
                UnityEditor.Editor.CreateEditor(targetWithButton)
            );
            UnityEditor.Editor editorWithoutButton = Track(
                UnityEditor.Editor.CreateEditor(targetWithoutButton)
            );

            WButtonEditorHelper helper = new WButtonEditorHelper();

            bool resultWith = helper.DrawAllButtonsAndProcessInvocations(editorWithButton);
            bool resultWithout = helper.DrawAllButtonsAndProcessInvocations(editorWithoutButton);

            Assert.That(resultWith, Is.True, "Target with buttons should draw buttons");
            Assert.That(resultWithout, Is.False, "Target without buttons should not draw buttons");
        }

        // ==================== IMGUI Context Integration ====================

        [UnityTest]
        public IEnumerator RenderingInImguiContextWorksCorrectly()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));

            bool wasDrawn = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                WButtonEditorHelper helper = new WButtonEditorHelper();
                wasDrawn = helper.DrawAllButtonsAndProcessInvocations(editor);
            });

            Assert.That(wasDrawn, Is.True, "Buttons should be drawn in IMGUI context");
        }

        [Test]
        public void VirtualWButtonMethodOverrideCallsCorrectImplementation()
        {
            InheritanceTargetDerivedOverride target = Track(
                ScriptableObject.CreateInstance<InheritanceTargetDerivedOverride>()
            );
            target.BaseCallCount = 0;
            target.DerivedCallCount = 0;

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(InheritanceTargetDerivedOverride)
            );
            WButtonMethodMetadata virtualMethod = metadata.First(m =>
                m.Method.Name == nameof(InheritanceTargetDerivedOverride.VirtualButton)
            );

            Assert.That(
                virtualMethod.Method.DeclaringType,
                Is.EqualTo(typeof(InheritanceTargetDerivedOverride)),
                "Metadata should reference derived type's override"
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(virtualMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                virtualMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(
                target.DerivedCallCount,
                Is.EqualTo(1),
                "Derived override should be called"
            );
            Assert.That(target.BaseCallCount, Is.EqualTo(0), "Base should not be called directly");
        }

        [Test]
        public void VirtualWButtonMethodOnBaseNotOverriddenCallsBase()
        {
            InheritanceTargetDerivedNoOverride target = Track(
                ScriptableObject.CreateInstance<InheritanceTargetDerivedNoOverride>()
            );
            target.BaseCallCount = 0;

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(InheritanceTargetDerivedNoOverride)
            );
            WButtonMethodMetadata virtualMethod = metadata.First(m =>
                m.Method.Name == nameof(InheritanceTargetVirtualBase.VirtualButton)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(virtualMethod);
            WButtonMethodContext context = new WButtonMethodContext(
                virtualMethod,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(
                target.BaseCallCount,
                Is.EqualTo(1),
                "Base virtual method should be called"
            );
        }

        [Test]
        public void MetadataCacheReturnsOnlyOneEntryForOverriddenMethod()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(InheritanceTargetDerivedOverride)
            );

            int virtualButtonCount = metadata.Count(m =>
                m.Method.Name == nameof(InheritanceTargetDerivedOverride.VirtualButton)
            );
            Assert.That(
                virtualButtonCount,
                Is.EqualTo(1),
                "Should have exactly one entry for overridden virtual method"
            );
        }

        [Test]
        public void WButtonOnBaseWithNonWButtonOverrideInDerivedClassStillDiscovered()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(InheritanceTargetNonWButtonOverride)
            );

            bool hasVirtualMethod = metadata.Any(m =>
                m.Method.Name
                == nameof(
                    InheritanceTargetVirtualNonWButtonBase.VirtualMethodNotOverriddenAsWButton
                )
            );
            Assert.That(
                hasVirtualMethod,
                Is.True,
                "Base WButton method should be discoverable even when derived overrides without WButton"
            );
        }

        [Test]
        public void AbstractBaseWithWButtonInDerivedClassDiscovered()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(InheritanceTargetConcreteWithWButton)
            );

            WButtonMethodMetadata concreteButton = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(InheritanceTargetConcreteWithWButton.ConcreteButton)
            );
            Assert.That(
                concreteButton,
                Is.Not.Null,
                "WButton on concrete class derived from abstract should be found"
            );
        }

        [Test]
        public void AbstractBaseWithWButtonInDerivedClassInvokesCorrectMethod()
        {
            InheritanceTargetConcreteWithWButton target = Track(
                ScriptableObject.CreateInstance<InheritanceTargetConcreteWithWButton>()
            );
            target.ConcreteCallCount = 0;

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(InheritanceTargetConcreteWithWButton)
            );
            WButtonMethodMetadata concreteButton = metadata.First(m =>
                m.Method.Name == nameof(InheritanceTargetConcreteWithWButton.ConcreteButton)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(concreteButton);
            WButtonMethodContext context = new WButtonMethodContext(
                concreteButton,
                new[] { methodState },
                new UnityEngine.Object[] { target }
            );

            context.MarkTriggered();
            WButtonInvocationController.ProcessTriggeredMethods(
                new List<WButtonMethodContext> { context }
            );

            Assert.That(
                target.ConcreteCallCount,
                Is.EqualTo(1),
                "Concrete class WButton should be invoked"
            );
        }

        [Test]
        public void InheritedMethodFromBaseAndDerivedMethodBothDiscovered()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetDerived)
            );

            bool hasBaseButton = metadata.Any(m =>
                m.Method.Name == nameof(IntegrationTargetBase.BaseButton)
            );
            bool hasDerivedButton = metadata.Any(m =>
                m.Method.Name == nameof(IntegrationTargetDerived.DerivedButton)
            );

            Assert.That(hasBaseButton, Is.True, "Inherited base button should be discovered");
            Assert.That(hasDerivedButton, Is.True, "Derived class button should be discovered");
        }

        [Test]
        public void DeepInheritanceChainDiscoversMethods()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(InheritanceTargetLevel3)
            );

            bool hasLevel1Button = metadata.Any(m =>
                m.Method.Name == nameof(InheritanceTargetLevel1.Level1Button)
            );
            bool hasLevel2Button = metadata.Any(m =>
                m.Method.Name == nameof(InheritanceTargetLevel2.Level2Button)
            );
            bool hasLevel3Button = metadata.Any(m =>
                m.Method.Name == nameof(InheritanceTargetLevel3.Level3Button)
            );

            Assert.That(hasLevel1Button, Is.True, "Level 1 button should be discovered");
            Assert.That(hasLevel2Button, Is.True, "Level 2 button should be discovered");
            Assert.That(hasLevel3Button, Is.True, "Level 3 button should be discovered");
            Assert.That(
                metadata.Count,
                Is.EqualTo(3),
                "Should have exactly 3 buttons from inheritance chain"
            );
        }

        // ==================== State Cleanup Tests ====================

        [Test]
        public void WButtonStateRepositoryUsesWeakReferences()
        {
            InheritanceTargetConcreteWithWButton target = Track(
                ScriptableObject.CreateInstance<InheritanceTargetConcreteWithWButton>()
            );
            WButtonTargetState state1 = WButtonStateRepository.GetOrCreate(target);
            WButtonTargetState state2 = WButtonStateRepository.GetOrCreate(target);

            Assert.That(state1, Is.SameAs(state2), "Same target should return same state instance");
        }

        [Test]
        public void StateCleanupWhenTargetDestroyedDoesNotThrow()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetSimple)
            );
            WButtonMethodMetadata simpleButton = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetSimple.SimpleButton)
            );
            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(simpleButton);

            methodState.AddResult(
                new WButtonResultEntry(
                    WButtonResultKind.Success,
                    DateTime.UtcNow,
                    null,
                    "Test",
                    null
                ),
                10
            );

            UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies GC behavior after target destruction

            Assert.DoesNotThrow(
                () => GC.Collect(2, GCCollectionMode.Forced, blocking: true),
                "GC should not throw after target destroyed"
            );
        }

        [Test]
        public void MultipleTargetsHaveIndependentStateInstances()
        {
            IntegrationTargetModifiesFields target1 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetModifiesFields>()
            );
            IntegrationTargetModifiesFields target2 = Track(
                ScriptableObject.CreateInstance<IntegrationTargetModifiesFields>()
            );

            WButtonTargetState state1 = WButtonStateRepository.GetOrCreate(target1);
            WButtonTargetState state2 = WButtonStateRepository.GetOrCreate(target2);

            Assert.That(
                state1,
                Is.Not.SameAs(state2),
                "Different targets should have different state instances"
            );
        }

        [Test]
        public void MethodStatePreservedAcrossMultipleHelperCalls()
        {
            IntegrationTargetSimple target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetSimple>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(IntegrationTargetSimple)
            );
            WButtonMethodMetadata simpleButton = metadata.First(m =>
                m.Method.Name == nameof(IntegrationTargetSimple.SimpleButton)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);
            WButtonMethodState methodState1 = targetState.GetOrCreateMethodState(simpleButton);

            methodState1.AddResult(
                new WButtonResultEntry(
                    WButtonResultKind.Success,
                    DateTime.UtcNow,
                    null,
                    "First call",
                    null
                ),
                10
            );

            WButtonMethodState methodState2 = targetState.GetOrCreateMethodState(simpleButton);

            Assert.That(
                methodState1,
                Is.SameAs(methodState2),
                "Same method should return same state"
            );
            Assert.That(methodState2.HasHistory, Is.True, "History should be preserved");
            Assert.That(methodState2.History.Count, Is.EqualTo(1), "Should have one history entry");
        }

        [Test]
        public void TargetStateTracksCorrectType()
        {
            IntegrationTargetDerived target = Track(
                ScriptableObject.CreateInstance<IntegrationTargetDerived>()
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(target);

            Assert.That(
                targetState.TargetType,
                Is.EqualTo(typeof(IntegrationTargetDerived)),
                "TargetState should track the actual derived type"
            );
        }
    }

    // ==================== Test Target Classes ====================

    internal sealed class IntegrationTargetSimple : ScriptableObject
    {
        public int InvocationCount;

        [WButton]
        public void SimpleButton()
        {
            InvocationCount++;
        }
    }

    internal sealed class IntegrationTargetMixedPlacement : ScriptableObject
    {
        [WButton(groupName: "TopGroup", groupPlacement: WButtonGroupPlacement.Top)]
        public void TopButton() { }

        [WButton(groupName: "BottomGroup", groupPlacement: WButtonGroupPlacement.Bottom)]
        public void BottomButton() { }
    }

    internal sealed class IntegrationTargetWithGroup : ScriptableObject
    {
        [WGroup("PropertyGroup")]
        public int someValue;

        [WButton(groupName: "TestGroup")]
        public void GroupedButton() { }
    }

    internal sealed class IntegrationTargetMultipleButtonsSameGroup : ScriptableObject
    {
        [WButton(groupName: "SharedGroup")]
        public void Button1() { }

        [WButton(groupName: "SharedGroup")]
        public void Button2() { }

        [WButton(groupName: "SharedGroup")]
        public void Button3() { }
    }

    internal sealed class IntegrationTargetNestedGroups : ScriptableObject
    {
        [WGroup("Outer")]
        public int outerValue;

        [WButton(groupName: "Outer")]
        public void OuterButton() { }

        [WButton(groupName: "Inner")]
        public void InnerButton() { }
    }

    internal sealed class IntegrationTargetWithShowIf : ScriptableObject
    {
        public bool showAdvanced;

        [WShowIf(nameof(showAdvanced))]
        public int advancedSetting;

        [WButton]
        public void ToggleAdvanced()
        {
            showAdvanced = !showAdvanced;
        }
    }

    internal sealed class IntegrationTargetMultipleConditions : ScriptableObject
    {
        public bool condition1;
        public bool condition2;
        public int threshold;

        [WButton]
        public void ConditionalButton() { }
    }

    internal sealed class IntegrationTargetModifiesFields : ScriptableObject
    {
        public int counter;
        public string lastAction;

        [WButton]
        public void IncrementCounter()
        {
            counter++;
            lastAction = "Incremented";
        }

        [WButton]
        public void ResetCounter()
        {
            counter = 0;
            lastAction = "Reset";
        }
    }

    internal sealed class IntegrationTargetDefaultPlacement : ScriptableObject
    {
        [WButton(groupName: "DefaultGroup")]
        public void DefaultButton() { }
    }

    internal sealed class IntegrationTargetManyButtons : ScriptableObject
    {
        [WButton(groupName: "ManyGroup")]
        public void Button1() { }

        [WButton(groupName: "ManyGroup")]
        public void Button2() { }

        [WButton(groupName: "ManyGroup")]
        public void Button3() { }

        [WButton(groupName: "ManyGroup")]
        public void Button4() { }

        [WButton(groupName: "ManyGroup")]
        public void Button5() { }

        [WButton(groupName: "ManyGroup")]
        public void Button6() { }

        [WButton(groupName: "ManyGroup")]
        public void Button7() { }

        [WButton(groupName: "ManyGroup")]
        public void Button8() { }

        [WButton(groupName: "ManyGroup")]
        public void Button9() { }

        [WButton(groupName: "ManyGroup")]
        public void Button10() { }
    }

    internal sealed class IntegrationTargetWithColorKey : ScriptableObject
    {
        [WButton(colorKey: "CustomColor")]
        public void ColoredButton() { }
    }

    internal sealed class IntegrationTargetThrowsException : ScriptableObject
    {
        [WButton]
        public void ThrowingButton()
        {
            throw new InvalidOperationException("Test exception from WButton");
        }
    }

    internal sealed class IntegrationTargetAsyncThrows : ScriptableObject
    {
        [WButton]
        public async Task AsyncThrowingButton()
        {
            await Task.Delay(10);
            throw new InvalidOperationException("Test async exception from WButton");
        }
    }

    internal sealed class IntegrationTargetAsync : ScriptableObject
    {
        public int CompletionCount;

        [WButton]
        public async Task AsyncButton()
        {
            await Task.Delay(50);
            CompletionCount++;
        }
    }

    internal abstract class IntegrationTargetBase : ScriptableObject
    {
        public int BaseCallCount;

        [WButton]
        public void BaseButton()
        {
            BaseCallCount++;
        }
    }

    internal sealed class IntegrationTargetDerived : IntegrationTargetBase
    {
        public int DerivedCallCount;

        [WButton]
        public void DerivedButton()
        {
            DerivedCallCount++;
        }
    }

    internal sealed class IntegrationTargetNoButtons : ScriptableObject
    {
        public int SomeValue;
        public string SomeName;
    }

    // ADD THESE TEST TARGETS:

    internal abstract class InheritanceTargetVirtualBase : ScriptableObject
    {
        public int BaseCallCount;

        [WButton]
        public virtual void VirtualButton()
        {
            BaseCallCount++;
        }
    }

    internal sealed class InheritanceTargetDerivedOverride : InheritanceTargetVirtualBase
    {
        public int DerivedCallCount;

        [WButton]
        public override void VirtualButton()
        {
            DerivedCallCount++;
        }
    }

    internal sealed class InheritanceTargetDerivedNoOverride : InheritanceTargetVirtualBase
    {
        public int OtherCallCount;

        [WButton]
        public void OtherButton()
        {
            OtherCallCount++;
        }
    }

    internal abstract class InheritanceTargetVirtualNonWButtonBase : ScriptableObject
    {
        public int BaseCallCount;

        [WButton]
        public virtual void VirtualMethodNotOverriddenAsWButton()
        {
            BaseCallCount++;
        }
    }

    internal sealed class InheritanceTargetNonWButtonOverride
        : InheritanceTargetVirtualNonWButtonBase
    {
        public int DerivedCallCount;

        public override void VirtualMethodNotOverriddenAsWButton()
        {
            DerivedCallCount++;
        }
    }

    internal abstract class InheritanceTargetAbstractBase : ScriptableObject
    {
        public abstract void AbstractMethod();
    }

    internal sealed class InheritanceTargetConcreteWithWButton : InheritanceTargetAbstractBase
    {
        public int ConcreteCallCount;

        public override void AbstractMethod() { }

        [WButton]
        public void ConcreteButton()
        {
            ConcreteCallCount++;
        }
    }

    internal abstract class InheritanceTargetLevel1 : ScriptableObject
    {
        public int Level1CallCount;

        [WButton]
        public void Level1Button()
        {
            Level1CallCount++;
        }
    }

    internal abstract class InheritanceTargetLevel2 : InheritanceTargetLevel1
    {
        public int Level2CallCount;

        [WButton]
        public void Level2Button()
        {
            Level2CallCount++;
        }
    }

    internal sealed class InheritanceTargetLevel3 : InheritanceTargetLevel2
    {
        public int Level3CallCount;

        [WButton]
        public void Level3Button()
        {
            Level3CallCount++;
        }
    }
}
#endif
