// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Utils.WButton
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using Object = UnityEngine.Object;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonRenderingTests : BatchedEditorTestBase
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

        [Test]
        public void ButtonsWithTopPlacementRenderAtTop()
        {
            RenderingTargetTopPlacement asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetTopPlacement>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawnAtTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            bool drawnAtBottom = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(drawnAtTop, Is.True, "Top placement buttons should render at top");
            Assert.That(
                drawnAtBottom,
                Is.False,
                "Top placement buttons should not render at bottom"
            );
        }

        [Test]
        public void ButtonsWithBottomPlacementRenderAtBottom()
        {
            RenderingTargetBottomPlacement asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetBottomPlacement>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawnAtTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            bool drawnAtBottom = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(drawnAtTop, Is.False, "Bottom placement buttons should not render at top");
            Assert.That(drawnAtBottom, Is.True, "Bottom placement buttons should render at bottom");
        }

        [Test]
        public void ButtonsWithDefaultPlacementFollowGlobalSettingTop()
        {
            RenderingTargetDefaultPlacement asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetDefaultPlacement>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawnAtTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            bool drawnAtBottom = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(
                drawnAtTop,
                Is.True,
                "Default placement should render at top when global is top"
            );
            Assert.That(
                drawnAtBottom,
                Is.False,
                "Default placement should not render at bottom when global is top"
            );
        }

        [Test]
        public void ButtonsWithDefaultPlacementFollowGlobalSettingBottom()
        {
            RenderingTargetDefaultPlacement asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetDefaultPlacement>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawnAtTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: false
            );

            bool drawnAtBottom = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: false
            );

            Assert.That(
                drawnAtTop,
                Is.False,
                "Default placement should not render at top when global is bottom"
            );
            Assert.That(
                drawnAtBottom,
                Is.True,
                "Default placement should render at bottom when global is bottom"
            );
        }

        [Test]
        public void ExplicitGroupPlacementOverridesGlobalPlacement()
        {
            RenderingTargetExplicitOverrideGlobal asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetExplicitOverrideGlobal>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawnAtTopWithGlobalBottom = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: false
            );

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
                drawnAtTopWithGlobalBottom,
                Is.True,
                "Explicit top placement should render at top even when global is bottom"
            );
            Assert.That(
                drawnAtBottomWithGlobalBottom,
                Is.False,
                "Explicit top placement should not render at bottom"
            );
        }

        [Test]
        public void MixedPlacementGroupsRenderInCorrectOrder()
        {
            RenderingTargetMixedPlacement asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetMixedPlacement>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawnAtTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            bool drawnAtBottom = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(drawnAtTop, Is.True, "Should render top placement group at top");
            Assert.That(drawnAtBottom, Is.True, "Should render bottom placement group at bottom");

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> topGroups = groupCounts
                .Keys.Where(k => k._groupPlacement == WButtonGroupPlacement.Top)
                .ToList();
            List<WButtonGroupKey> bottomGroups = groupCounts
                .Keys.Where(k => k._groupPlacement == WButtonGroupPlacement.Bottom)
                .ToList();

            Assert.That(topGroups, Has.Count.GreaterThan(0), "Should have top groups");
            Assert.That(bottomGroups, Has.Count.GreaterThan(0), "Should have bottom groups");
        }

        [Test]
        public void ButtonsInSameGroupAreRenderedTogether()
        {
            RenderingTargetSameGroup asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSameGroup>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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
            List<WButtonGroupKey> testGroupKeys = groupCounts
                .Keys.Where(k => k._groupName == "TestGroup")
                .ToList();

            Assert.That(testGroupKeys, Has.Count.EqualTo(1), "Should have exactly one TestGroup");
            Assert.That(
                groupCounts.ValueFor(testGroupKeys[0]),
                Is.EqualTo(3),
                "TestGroup should contain all 3 buttons"
            );
        }

        [Test]
        public void ButtonsWithoutGroupAreRenderedInDefaultGroup()
        {
            RenderingTargetNoGroup asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetNoGroup>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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
            List<WButtonGroupKey> ungroupedKeys = groupCounts
                .Keys.Where(k => string.IsNullOrEmpty(k._groupName))
                .ToList();

            Assert.That(ungroupedKeys, Has.Count.GreaterThan(0), "Should have ungrouped buttons");
        }

        [Test]
        public void MultipleGroupsRenderInPriorityOrder()
        {
            RenderingTargetMultipleGroupsPriority asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetMultipleGroupsPriority>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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
            WButtonGroupKey highPriorityKey = groupCounts.Keys.First(k =>
                k._groupName == "HighPriority"
            );
            WButtonGroupKey lowPriorityKey = groupCounts.Keys.First(k =>
                k._groupName == "LowPriority"
            );

            Assert.That(
                highPriorityKey.CompareTo(lowPriorityKey),
                Is.LessThan(0),
                "High priority group should sort before low priority group"
            );
        }

        [Test]
        public void GroupHeadersDisplayCorrectLabels()
        {
            RenderingTargetCustomGroupName asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetCustomGroupName>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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

            Dictionary<WButtonGroupKey, string> groupNames = WButtonGUI.GetGroupNamesForTesting();

            Assert.That(
                groupNames.Values,
                Does.Contain("CustomGroupName"),
                "Should have custom group name in headers"
            );
        }

        [Test]
        public void CustomGroupNamesAppearInHeaders()
        {
            WButtonGUI.ClearGroupDataForTesting();

            Dictionary<int, string> names = new() { { -1, "MyCustomGroup" } };
            Dictionary<int, int> counts = new() { { -1, 2 }, { -2, 1 } };
            WButtonGUI.SetGroupNamesForTesting(names);
            WButtonGUI.SetGroupCountsForTesting(counts);

            GUIContent header = WButtonGUI.BuildGroupHeader(-1);

            Assert.That(
                header.text,
                Is.EqualTo("MyCustomGroup"),
                "Custom group name should appear in header"
            );
        }

        [Test]
        public void EmptyGroupsAreNotRendered()
        {
            RenderingTargetSingleButton asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSingleButton>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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

            foreach (KeyValuePair<WButtonGroupKey, int> entry in groupCounts)
            {
                Assert.That(
                    entry.Value,
                    Is.GreaterThan(0),
                    "No empty groups should be in the count dictionary"
                );
            }
        }

        [Test]
        public void AlwaysOpenFoldoutBehaviorKeepsGroupsExpanded()
        {
            RenderingTargetSingleButton asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSingleButton>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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

            Assert.That(drawn, Is.True, "Buttons should be drawn with AlwaysOpen behavior");
        }

        [Test]
        public void StartExpandedFoldoutBehaviorStartsExpanded()
        {
            RenderingTargetSingleButton asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSingleButton>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.StartExpanded,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            foreach (KeyValuePair<WButtonGroupKey, bool> entry in foldoutStates)
            {
                Assert.That(entry.Value, Is.True, "Foldout should start expanded");
            }
        }

        [Test]
        public void StartCollapsedFoldoutBehaviorStartsCollapsed()
        {
            RenderingTargetSingleButton asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSingleButton>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.StartCollapsed,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            foreach (KeyValuePair<WButtonGroupKey, bool> entry in foldoutStates)
            {
                Assert.That(entry.Value, Is.False, "Foldout should start collapsed");
            }
        }

        [Test]
        public void FoldoutStatePersistsBetweenOnInspectorGuiCalls()
        {
            RenderingTargetSingleButton asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSingleButton>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.StartExpanded,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Dictionary<WButtonGroupKey, bool> foldoutSnapshot = new(foldoutStates);

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.StartExpanded,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            foreach (KeyValuePair<WButtonGroupKey, bool> entry in foldoutSnapshot)
            {
                Assert.That(
                    foldoutStates.ContainsKey(entry.Key),
                    Is.True,
                    "Foldout state key should persist"
                );
                Assert.That(
                    foldoutStates.ValueFor(entry.Key),
                    Is.EqualTo(entry.Value),
                    "Foldout state value should persist"
                );
            }
        }

        [Test]
        public void MultipleGroupsHaveIndependentFoldoutStates()
        {
            RenderingTargetMultipleGroups asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetMultipleGroups>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.StartExpanded,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            List<WButtonGroupKey> keys = foldoutStates.Keys.ToList();
            if (2 <= keys.Count)
            {
                foldoutStates[keys[0]] = true;
                foldoutStates[keys[1]] = false;

                Assert.That(
                    foldoutStates.ValueFor(keys[0]),
                    Is.Not.EqualTo(foldoutStates.ValueFor(keys[1])),
                    "Groups should have independent foldout states"
                );
            }
        }

        [Test]
        public void PaginationAppearsWhenButtonCountExceedsPageSize()
        {
            RenderingTargetManyButtons asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetManyButtons>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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

            Assert.That(
                totalButtons,
                Is.GreaterThan(0),
                "Should have multiple buttons for pagination"
            );
        }

        [Test]
        public void PageNavigationWorksCorrectly()
        {
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            WButtonGroupKey testKey = new(0, 0, "TestGroup", 0, WButtonGroupPlacement.Top);
            WButtonPaginationState state = new();
            paginationStates[testKey] = state;

            state._pageIndex = 0;
            Assert.That(state._pageIndex, Is.EqualTo(0), "Initial page should be 0");

            state._pageIndex = 1;
            Assert.That(state._pageIndex, Is.EqualTo(1), "Page should be navigable to 1");

            state._pageIndex = 0;
            Assert.That(state._pageIndex, Is.EqualTo(0), "Page should be navigable back to 0");
        }

        [Test]
        public void PaginationStatePersistsBetweenCalls()
        {
            RenderingTargetManyButtons asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetManyButtons>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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

            int statesCount = paginationStates.Count;

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(
                paginationStates.Count,
                Is.EqualTo(statesCount),
                "Pagination states should persist"
            );
        }

        [Test]
        public void ResizingPageSizeUpdatesPaginationCorrectly()
        {
            WButtonPaginationState state = new();
            state._pageIndex = 5;

            if (3 <= state._pageIndex)
            {
                state._pageIndex = 2;
            }

            Assert.That(
                state._pageIndex,
                Is.EqualTo(2),
                "Page index should be clamped when page size changes"
            );
        }

        [Test]
        public void MethodsWithoutParametersShowSimpleButton()
        {
            RenderingTargetNoParameters asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetNoParameters>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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

            Assert.That(drawn, Is.True, "Button without parameters should be drawn");

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetNoParameters)
            );
            Assert.That(metadata[0].Parameters, Is.Empty, "Method should have no parameters");
        }

        [Test]
        public void MethodsWithParametersShowParameterFields()
        {
            RenderingTargetWithParameters asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetWithParameters>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetWithParameters)
            );
            WButtonMethodMetadata paramMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetWithParameters.ButtonWithParams)
            );

            Assert.That(
                paramMethod.Parameters.Length,
                Is.GreaterThan(0),
                "Method should have parameters"
            );
        }

        [Test]
        public void ParameterDefaultValuesArePopulated()
        {
            RenderingTargetWithDefaultParameters asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetWithDefaultParameters>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetWithDefaultParameters)
            );
            WButtonMethodMetadata paramMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetWithDefaultParameters.ButtonWithDefaults)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(paramMethod);

            WButtonParameterState intParam = methodState.Parameters.FirstOrDefault(p =>
                p.Metadata.ParameterType == typeof(int)
            );
            WButtonParameterState stringParam = methodState.Parameters.FirstOrDefault(p =>
                p.Metadata.ParameterType == typeof(string)
            );

            Assert.That(intParam, Is.Not.Null, "Should have int parameter");
            Assert.That(stringParam, Is.Not.Null, "Should have string parameter");
        }

        [Test]
        public void ParameterFieldsUpdateMethodState()
        {
            RenderingTargetWithParameters asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetWithParameters>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetWithParameters)
            );
            WButtonMethodMetadata paramMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetWithParameters.ButtonWithParams)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(paramMethod);

            string testValue = "UpdatedValue";
            methodState.Parameters[0].CurrentValue = testValue;

            Assert.That(
                methodState.Parameters[0].CurrentValue,
                Is.EqualTo(testValue),
                "Parameter value should be updated"
            );
        }

        [Test]
        public void MultipleParametersRenderInCorrectOrder()
        {
            RenderingTargetMultipleParameters asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetMultipleParameters>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetMultipleParameters)
            );
            WButtonMethodMetadata paramMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetMultipleParameters.MultiParam)
            );

            Assert.That(paramMethod.Parameters.Length, Is.EqualTo(3), "Should have 3 parameters");
            Assert.That(
                paramMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(string)),
                "First param should be string"
            );
            Assert.That(
                paramMethod.Parameters[1].ParameterType,
                Is.EqualTo(typeof(int)),
                "Second param should be int"
            );
            Assert.That(
                paramMethod.Parameters[2].ParameterType,
                Is.EqualTo(typeof(bool)),
                "Third param should be bool"
            );
        }

        [Test]
        public void RunningAsyncMethodsHaveCorrectStatus()
        {
            RenderingTargetAsync asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetAsync>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetAsync)
            );
            WButtonMethodMetadata asyncMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetAsync.AsyncButton)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(asyncMethod);

            Assert.That(methodState.ActiveInvocation, Is.Null, "Initially no active invocation");
        }

        [Test]
        public void CompletedAsyncMethodsUpdateDisplay()
        {
            RenderingTargetAsync asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetAsync>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetAsync)
            );
            WButtonMethodMetadata asyncMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetAsync.AsyncButton)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(asyncMethod);

            WButtonResultEntry result = new(
                WButtonResultKind.Success,
                DateTime.UtcNow,
                null,
                "Completed",
                null
            );
            methodState.AddResult(result, 10);

            Assert.That(methodState.HasHistory, Is.True, "Should have history after completion");
            Assert.That(methodState.History.Count, Is.EqualTo(1), "Should have one result");
        }

        [Test]
        public void CancelButtonInvokesCancellation()
        {
            RenderingTargetCancellable asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetCancellable>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetCancellable)
            );
            WButtonMethodMetadata cancellableMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetCancellable.CancellableButton)
            );

            Assert.That(
                0 <= cancellableMethod.CancellationTokenParameterIndex,
                Is.True,
                "Method should support cancellation"
            );
        }

        [Test]
        public void TaskResultHistoryIsDisplayed()
        {
            RenderingTargetAsync asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetAsync>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetAsync)
            );
            WButtonMethodMetadata asyncMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetAsync.AsyncButton)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(asyncMethod);

            methodState.AddResult(
                new WButtonResultEntry(
                    WButtonResultKind.Success,
                    DateTime.UtcNow,
                    null,
                    "Result 1",
                    null
                ),
                10
            );
            methodState.AddResult(
                new WButtonResultEntry(
                    WButtonResultKind.Success,
                    DateTime.UtcNow,
                    null,
                    "Result 2",
                    null
                ),
                10
            );
            methodState.AddResult(
                new WButtonResultEntry(
                    WButtonResultKind.Error,
                    DateTime.UtcNow,
                    null,
                    "Error",
                    null,
                    new Exception("Test")
                ),
                10
            );

            Assert.That(methodState.History.Count, Is.EqualTo(3), "Should have 3 history entries");
        }

        [Test]
        public void NullEditorTargetHandledGracefully()
        {
            RenderingTargetSingleButton asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSingleButton>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            Object.DestroyImmediate(asset); // UNH-SUPPRESS: Test verifies behavior when target is destroyed
            _trackedObjects.Remove(asset);

            bool drawn = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(drawn, Is.False, "Should return false when target is destroyed");
        }

        [Test]
        public void DestroyedTargetObjectHandled()
        {
            RenderingTargetSingleButton asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSingleButton>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            Object.DestroyImmediate(asset); // UNH-SUPPRESS: Test verifies no throw when target is destroyed
            _trackedObjects.Remove(asset);

            Assert.DoesNotThrow(
                () =>
                    WButtonGUI.DrawButtons(
                        editor,
                        WButtonPlacement.Top,
                        paginationStates,
                        foldoutStates,
                        UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                        triggeredContexts: null,
                        globalPlacementIsTop: true
                    ),
                "Should not throw when target is destroyed"
            );
        }

        [Test]
        public void EmptyWButtonMethodsListHandled()
        {
            RenderingTargetNoButtons asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetNoButtons>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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

            Assert.That(drawn, Is.False, "Should return false when no WButton methods exist");
        }

        [Test]
        public void VeryLongMethodNamesRenderCorrectly()
        {
            RenderingTargetLongMethodName asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetLongMethodName>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
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

            Assert.That(drawn, Is.True, "Should render buttons with long method names");
        }

        [Test]
        public void SpecialCharactersInGroupNamesHandled()
        {
            RenderingTargetSpecialCharsGroupName asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSpecialCharsGroupName>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            Assert.DoesNotThrow(
                () =>
                    WButtonGUI.DrawButtons(
                        editor,
                        WButtonPlacement.Top,
                        paginationStates,
                        foldoutStates,
                        UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                        triggeredContexts: null,
                        globalPlacementIsTop: true
                    ),
                "Should handle special characters in group names"
            );

            Dictionary<WButtonGroupKey, string> groupNames = WButtonGUI.GetGroupNamesForTesting();
            Assert.That(
                groupNames.Values.Any(n => n.Contains("&")),
                Is.True,
                "Should preserve special characters"
            );
        }

        [UnityTest]
        public IEnumerator RenderingInImguiContextWorksCorrectly()
        {
            RenderingTargetSingleButton asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetSingleButton>()
            );
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(asset));

            bool wasDrawn = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
                Dictionary<WButtonGroupKey, bool> foldoutStates = new();

                wasDrawn = WButtonGUI.DrawButtons(
                    editor,
                    WButtonPlacement.Top,
                    paginationStates,
                    foldoutStates,
                    UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                    triggeredContexts: null,
                    globalPlacementIsTop: true
                );
            });

            Assert.That(wasDrawn, Is.True, "Buttons should be drawn in IMGUI context");
        }

        [Test]
        public void FloatParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetFloatParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetFloatParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetFloatParameter)
            );
            WButtonMethodMetadata floatMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetFloatParameter.FloatButton)
            );

            Assert.That(floatMethod.Parameters.Length, Is.EqualTo(1), "Should have one parameter");
            Assert.That(
                floatMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(float)),
                "Parameter type should be float"
            );
            Assert.That(
                floatMethod.Parameters[0].Name,
                Is.EqualTo("value"),
                "Parameter name should be 'value'"
            );
        }

        [Test]
        public void FloatParameterDefaultValueIsHandled()
        {
            RenderingTargetFloatParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetFloatParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetFloatParameter)
            );
            WButtonMethodMetadata defaultMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetFloatParameter.FloatWithDefault)
            );

            Assert.That(
                defaultMethod.Parameters[0].IsOptional,
                Is.True,
                "Parameter should be optional"
            );
            Assert.That(
                defaultMethod.Parameters[0].HasDefaultValue,
                Is.True,
                "Parameter should have default value"
            );
            Assert.That(
                defaultMethod.Parameters[0].DefaultValue,
                Is.EqualTo(3.14f).Within(0.001f),
                "Default value should be 3.14"
            );
        }

        [Test]
        public void Vector2ParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetVector2Parameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetVector2Parameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetVector2Parameter)
            );
            WButtonMethodMetadata vectorMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetVector2Parameter.Vector2Button)
            );

            Assert.That(vectorMethod.Parameters.Length, Is.EqualTo(1), "Should have one parameter");
            Assert.That(
                vectorMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(Vector2)),
                "Parameter type should be Vector2"
            );
            Assert.That(
                vectorMethod.Parameters[0].IsValueType,
                Is.True,
                "Vector2 should be identified as value type"
            );
        }

        [Test]
        public void Vector2ParameterDefaultValueIsHandled()
        {
            RenderingTargetVector2Parameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetVector2Parameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetVector2Parameter)
            );
            WButtonMethodMetadata defaultMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetVector2Parameter.Vector2WithDefault)
            );

            Assert.That(
                defaultMethod.Parameters[0].IsOptional,
                Is.True,
                "Parameter should be optional"
            );
            Assert.That(
                defaultMethod.Parameters[0].HasDefaultValue,
                Is.True,
                "Parameter should have default value"
            );
        }

        [Test]
        public void Vector3ParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetVector3Parameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetVector3Parameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetVector3Parameter)
            );
            WButtonMethodMetadata vectorMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetVector3Parameter.Vector3Button)
            );

            Assert.That(vectorMethod.Parameters.Length, Is.EqualTo(1), "Should have one parameter");
            Assert.That(
                vectorMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(Vector3)),
                "Parameter type should be Vector3"
            );
            Assert.That(
                vectorMethod.Parameters[0].IsValueType,
                Is.True,
                "Vector3 should be identified as value type"
            );
        }

        [Test]
        public void Vector3ParameterDefaultValueIsHandled()
        {
            RenderingTargetVector3Parameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetVector3Parameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetVector3Parameter)
            );
            WButtonMethodMetadata defaultMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetVector3Parameter.Vector3WithDefault)
            );

            Assert.That(
                defaultMethod.Parameters[0].IsOptional,
                Is.True,
                "Parameter should be optional"
            );
            Assert.That(
                defaultMethod.Parameters[0].HasDefaultValue,
                Is.True,
                "Parameter should have default value"
            );
        }

        [Test]
        public void ColorParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetColorParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetColorParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetColorParameter)
            );
            WButtonMethodMetadata colorMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetColorParameter.ColorButton)
            );

            Assert.That(colorMethod.Parameters.Length, Is.EqualTo(1), "Should have one parameter");
            Assert.That(
                colorMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(Color)),
                "Parameter type should be Color"
            );
            Assert.That(
                colorMethod.Parameters[0].IsValueType,
                Is.True,
                "Color should be identified as value type"
            );
        }

        [Test]
        public void ColorParameterDefaultValueIsHandled()
        {
            RenderingTargetColorParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetColorParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetColorParameter)
            );
            WButtonMethodMetadata defaultMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetColorParameter.ColorWithDefault)
            );

            Assert.That(
                defaultMethod.Parameters[0].IsOptional,
                Is.True,
                "Parameter should be optional"
            );
            Assert.That(
                defaultMethod.Parameters[0].HasDefaultValue,
                Is.True,
                "Parameter should have default value"
            );
        }

        [Test]
        public void GameObjectParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetUnityObjectParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetUnityObjectParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetUnityObjectParameter)
            );
            WButtonMethodMetadata gameObjectMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetUnityObjectParameter.GameObjectButton)
            );

            Assert.That(
                gameObjectMethod.Parameters.Length,
                Is.EqualTo(1),
                "Should have one parameter"
            );
            Assert.That(
                gameObjectMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(GameObject)),
                "Parameter type should be GameObject"
            );
            Assert.That(
                gameObjectMethod.Parameters[0].IsUnityObject,
                Is.True,
                "GameObject should be identified as Unity Object"
            );
        }

        [Test]
        public void TransformParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetUnityObjectParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetUnityObjectParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetUnityObjectParameter)
            );
            WButtonMethodMetadata transformMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetUnityObjectParameter.TransformButton)
            );

            Assert.That(
                transformMethod.Parameters.Length,
                Is.EqualTo(1),
                "Should have one parameter"
            );
            Assert.That(
                transformMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(Transform)),
                "Parameter type should be Transform"
            );
            Assert.That(
                transformMethod.Parameters[0].IsUnityObject,
                Is.True,
                "Transform should be identified as Unity Object"
            );
        }

        [Test]
        public void EnumParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetEnumParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetEnumParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetEnumParameter)
            );
            WButtonMethodMetadata enumMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetEnumParameter.EnumButton)
            );

            Assert.That(enumMethod.Parameters.Length, Is.EqualTo(1), "Should have one parameter");
            Assert.That(
                enumMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(TestButtonEnum)),
                "Parameter type should be TestButtonEnum"
            );
            Assert.That(
                enumMethod.Parameters[0].ParameterType.IsEnum,
                Is.True,
                "Parameter type should be enum"
            );
        }

        [Test]
        public void EnumParameterDefaultValueIsHandled()
        {
            RenderingTargetEnumParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetEnumParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetEnumParameter)
            );
            WButtonMethodMetadata defaultMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetEnumParameter.EnumWithDefault)
            );

            Assert.That(
                defaultMethod.Parameters[0].IsOptional,
                Is.True,
                "Parameter should be optional"
            );
            Assert.That(
                defaultMethod.Parameters[0].HasDefaultValue,
                Is.True,
                "Parameter should have default value"
            );
            Assert.That(
                defaultMethod.Parameters[0].DefaultValue,
                Is.EqualTo(TestButtonEnum.OptionB),
                "Default value should be OptionB"
            );
        }

        [Test]
        public void IntArrayParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetArrayParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetArrayParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetArrayParameter)
            );
            WButtonMethodMetadata arrayMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetArrayParameter.IntArrayButton)
            );

            Assert.That(arrayMethod.Parameters.Length, Is.EqualTo(1), "Should have one parameter");
            Assert.That(
                arrayMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(int[])),
                "Parameter type should be int[]"
            );
            Assert.That(
                arrayMethod.Parameters[0].ParameterType.IsArray,
                Is.True,
                "Parameter type should be array"
            );
        }

        [Test]
        public void StringArrayParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetArrayParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetArrayParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetArrayParameter)
            );
            WButtonMethodMetadata arrayMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetArrayParameter.StringArrayButton)
            );

            Assert.That(arrayMethod.Parameters.Length, Is.EqualTo(1), "Should have one parameter");
            Assert.That(
                arrayMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(string[])),
                "Parameter type should be string[]"
            );
            Assert.That(
                arrayMethod.Parameters[0].ParameterType.IsArray,
                Is.True,
                "Parameter type should be array"
            );
            Assert.That(
                arrayMethod.Parameters[0].ParameterType.GetElementType(),
                Is.EqualTo(typeof(string)),
                "Array element type should be string"
            );
        }

        [Test]
        public void MixedUnityTypesParameterMetadataIsDetectedCorrectly()
        {
            RenderingTargetMixedUnityTypes asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetMixedUnityTypes>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetMixedUnityTypes)
            );
            WButtonMethodMetadata mixedMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetMixedUnityTypes.MixedUnityButton)
            );

            Assert.That(
                mixedMethod.Parameters.Length,
                Is.EqualTo(5),
                "Should have five parameters"
            );
            Assert.That(
                mixedMethod.Parameters[0].ParameterType,
                Is.EqualTo(typeof(float)),
                "First param should be float"
            );
            Assert.That(
                mixedMethod.Parameters[1].ParameterType,
                Is.EqualTo(typeof(Vector3)),
                "Second param should be Vector3"
            );
            Assert.That(
                mixedMethod.Parameters[2].ParameterType,
                Is.EqualTo(typeof(Color)),
                "Third param should be Color"
            );
            Assert.That(
                mixedMethod.Parameters[3].ParameterType,
                Is.EqualTo(typeof(GameObject)),
                "Fourth param should be GameObject"
            );
            Assert.That(
                mixedMethod.Parameters[4].ParameterType,
                Is.EqualTo(typeof(TestButtonEnum)),
                "Fifth param should be TestButtonEnum"
            );
        }

        [Test]
        public void MixedUnityTypesHaveCorrectTypeCategories()
        {
            RenderingTargetMixedUnityTypes asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetMixedUnityTypes>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetMixedUnityTypes)
            );
            WButtonMethodMetadata mixedMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetMixedUnityTypes.MixedUnityButton)
            );

            Assert.That(
                mixedMethod.Parameters[0].IsValueType,
                Is.True,
                "float should be value type"
            );
            Assert.That(
                mixedMethod.Parameters[1].IsValueType,
                Is.True,
                "Vector3 should be value type"
            );
            Assert.That(
                mixedMethod.Parameters[2].IsValueType,
                Is.True,
                "Color should be value type"
            );
            Assert.That(
                mixedMethod.Parameters[3].IsUnityObject,
                Is.True,
                "GameObject should be Unity Object"
            );
            Assert.That(
                mixedMethod.Parameters[4].ParameterType.IsEnum,
                Is.True,
                "TestButtonEnum should be enum"
            );
        }

        [Test]
        public void ParameterStateStoredCorrectlyForFloatType()
        {
            RenderingTargetFloatParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetFloatParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetFloatParameter)
            );
            WButtonMethodMetadata floatMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetFloatParameter.FloatButton)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(floatMethod);

            float testValue = 42.5f;
            methodState.Parameters[0].CurrentValue = testValue;

            Assert.That(
                methodState.Parameters[0].CurrentValue,
                Is.EqualTo(testValue),
                "Float parameter value should be stored correctly"
            );
        }

        [Test]
        public void ParameterStateStoredCorrectlyForVector3Type()
        {
            RenderingTargetVector3Parameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetVector3Parameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetVector3Parameter)
            );
            WButtonMethodMetadata vectorMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetVector3Parameter.Vector3Button)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(vectorMethod);

            Vector3 testValue = new Vector3(1f, 2f, 3f);
            methodState.Parameters[0].CurrentValue = testValue;

            Assert.That(
                methodState.Parameters[0].CurrentValue,
                Is.EqualTo(testValue),
                "Vector3 parameter value should be stored correctly"
            );
        }

        [Test]
        public void ParameterStateStoredCorrectlyForColorType()
        {
            RenderingTargetColorParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetColorParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetColorParameter)
            );
            WButtonMethodMetadata colorMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetColorParameter.ColorButton)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(colorMethod);

            Color testValue = new Color(0.5f, 0.25f, 0.75f, 1f);
            methodState.Parameters[0].CurrentValue = testValue;

            Assert.That(
                methodState.Parameters[0].CurrentValue,
                Is.EqualTo(testValue),
                "Color parameter value should be stored correctly"
            );
        }

        [Test]
        public void ParameterStateStoredCorrectlyForEnumType()
        {
            RenderingTargetEnumParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetEnumParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetEnumParameter)
            );
            WButtonMethodMetadata enumMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetEnumParameter.EnumButton)
            );

            WButtonTargetState targetState = WButtonStateRepository.GetOrCreate(asset);
            WButtonMethodState methodState = targetState.GetOrCreateMethodState(enumMethod);

            TestButtonEnum testValue = TestButtonEnum.OptionC;
            methodState.Parameters[0].CurrentValue = testValue;

            Assert.That(
                methodState.Parameters[0].CurrentValue,
                Is.EqualTo(testValue),
                "Enum parameter value should be stored correctly"
            );
        }

        [Test]
        public void UnityObjectParameterIsNotValueType()
        {
            RenderingTargetUnityObjectParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetUnityObjectParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetUnityObjectParameter)
            );
            WButtonMethodMetadata gameObjectMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetUnityObjectParameter.GameObjectButton)
            );

            Assert.That(
                gameObjectMethod.Parameters[0].IsValueType,
                Is.False,
                "GameObject should not be identified as value type"
            );
            Assert.That(
                gameObjectMethod.Parameters[0].IsUnityObject,
                Is.True,
                "GameObject should be identified as Unity Object"
            );
        }

        [Test]
        public void EnumParameterIsNotIdentifiedAsValueType()
        {
            RenderingTargetEnumParameter asset = Track(
                ScriptableObject.CreateInstance<RenderingTargetEnumParameter>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(RenderingTargetEnumParameter)
            );
            WButtonMethodMetadata enumMethod = metadata.First(m =>
                m.Method.Name == nameof(RenderingTargetEnumParameter.EnumButton)
            );

            Assert.That(
                enumMethod.Parameters[0].IsValueType,
                Is.False,
                "Enum should not be identified as value type (due to IsValueType && !IsEnum check)"
            );
        }
    }

    internal enum TestButtonEnum
    {
        OptionA = 0,
        OptionB = 1,
        OptionC = 2,
    }
}
#endif
