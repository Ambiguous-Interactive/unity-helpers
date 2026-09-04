// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WButton
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WButton;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    /// <summary>
    /// Comprehensive tests for WButton conflict detection logic.
    /// These tests verify that:
    /// - Conflicts are only detected when there are multiple EXPLICIT conflicting values
    /// - UseGlobalSetting (placement) and NoGroupPriority (priority) are treated as "no explicit value"
    /// - A group with one explicit value and defaults does NOT generate a conflict
    /// - A group with multiple different explicit values DOES generate a conflict
    /// - A group with all defaults does NOT generate a conflict
    /// - A group with all identical explicit values does NOT generate a conflict
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonConflictDetectionTests : BatchedEditorTestBase
    {
        [SetUp]
        public void SetUp()
        {
            base.BaseSetUp();
            WButtonGUI.ClearGroupDataForTesting();
            WButtonGUI.ClearConflictingDrawOrderWarningsForTesting();
            WButtonGUI.ClearConflictingGroupPriorityWarningsForTesting();
            WButtonGUI.ClearConflictingGroupPlacementWarningsForTesting();
            WButtonGUI.ClearConflictWarningContentCacheForTesting();
            WButtonGUI.ClearContextCache();
        }

        [TearDown]
        public override void TearDown()
        {
            WButtonGUI.ClearGroupDataForTesting();
            WButtonGUI.ClearConflictingDrawOrderWarningsForTesting();
            WButtonGUI.ClearConflictingGroupPriorityWarningsForTesting();
            WButtonGUI.ClearConflictingGroupPlacementWarningsForTesting();
            WButtonGUI.ClearConflictWarningContentCacheForTesting();
            WButtonGUI.ClearContextCache();
            base.TearDown();
        }

        [Test]
        public void MixedExplicitAndDefaultPlacementDoesNotGenerateWarning()
        {
            WButtonMixedExplicitAndDefaultPlacementTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMixedExplicitAndDefaultPlacementTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPlacementWarnings();

            Assert.That(
                warnings.ContainsKey("Debug Tools"),
                Is.False,
                "Debug Tools group should not have placement warning (one explicit Top, one default)"
            );
            Assert.That(
                warnings.ContainsKey("Save System"),
                Is.False,
                "Save System group should not have placement warning (one explicit Bottom, two defaults)"
            );
        }

        [Test]
        public void MixedExplicitAndDefaultPlacementUsesExplicitPlacement()
        {
            WButtonMixedExplicitAndDefaultPlacementTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMixedExplicitAndDefaultPlacementTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            List<WButtonGroupKey> debugToolsGroups = groupCounts
                .Keys.Where(k => k._groupName == "Debug Tools")
                .ToList();
            List<WButtonGroupKey> saveSystemGroups = groupCounts
                .Keys.Where(k => k._groupName == "Save System")
                .ToList();

            Assert.That(
                debugToolsGroups,
                Has.Count.EqualTo(1),
                "Should have exactly one Debug Tools group"
            );
            Assert.That(
                debugToolsGroups[0]._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Top),
                "Debug Tools should use explicit Top placement"
            );
            Assert.That(
                groupCounts.ValueFor(debugToolsGroups[0]),
                Is.EqualTo(2),
                "Debug Tools should have 2 buttons"
            );

            Assert.That(
                saveSystemGroups,
                Has.Count.EqualTo(1),
                "Should have exactly one Save System group"
            );
            Assert.That(
                saveSystemGroups[0]._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Bottom),
                "Save System should use explicit Bottom placement"
            );
            Assert.That(
                groupCounts.ValueFor(saveSystemGroups[0]),
                Is.EqualTo(3),
                "Save System should have 3 buttons"
            );
        }

        [Test]
        public void AllDefaultPlacementDoesNotGenerateWarning()
        {
            WButtonAllDefaultPlacementTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonAllDefaultPlacementTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPlacementWarnings();

            Assert.That(
                warnings,
                Is.Empty,
                "All default placements should not generate any warnings"
            );
        }

        [Test]
        public void AllSameExplicitPlacementDoesNotGenerateWarning()
        {
            WButtonAllSameExplicitPlacementTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonAllSameExplicitPlacementTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPlacementWarnings();

            Assert.That(
                warnings,
                Is.Empty,
                "All same explicit placements should not generate any warnings"
            );
        }

        [Test]
        public void MultipleExplicitPlacementConflictGeneratesWarning()
        {
            WButtonMultipleExplicitPlacementConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMultipleExplicitPlacementConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPlacementWarnings();

            Assert.That(
                warnings.ContainsKey("ConflictGroup"),
                Is.True,
                "Should have warning for ConflictGroup with multiple explicit placements"
            );
            Assert.That(
                warnings.ValueFor("ConflictGroup")._allGroupPlacements,
                Contains.Item(WButtonGroupPlacement.Top),
                "Warning should include Top placement"
            );
            Assert.That(
                warnings.ValueFor("ConflictGroup")._allGroupPlacements,
                Contains.Item(WButtonGroupPlacement.Bottom),
                "Warning should include Bottom placement"
            );
            Assert.That(
                warnings
                    .ValueFor("ConflictGroup")
                    ._allGroupPlacements.Contains(WButtonGroupPlacement.UseGlobalSetting),
                Is.False,
                "Warning should NOT include UseGlobalSetting (it's filtered out)"
            );
        }

        [Test]
        public void MultipleExplicitPlacementConflictUsesFirstDeclaredPlacement()
        {
            WButtonMultipleExplicitPlacementConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMultipleExplicitPlacementConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            List<WButtonGroupKey> conflictGroups = groupCounts
                .Keys.Where(k => k._groupName == "ConflictGroup")
                .ToList();

            Assert.That(
                conflictGroups,
                Has.Count.EqualTo(1),
                "Should have exactly one ConflictGroup"
            );
            Assert.That(
                conflictGroups[0]._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Top),
                "Group should use first declared placement (Top)"
            );
        }

        [Test]
        public void ExistingPlacementConflictTargetStillGeneratesWarning()
        {
            WButtonGroupPlacementConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPlacementConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPlacementWarnings();

            Assert.That(
                warnings.ContainsKey("ConflictGroup"),
                Is.True,
                "Existing test with two explicit placements should still generate warning"
            );
        }

        [Test]
        public void MixedExplicitAndDefaultPriorityDoesNotGenerateWarning()
        {
            WButtonMixedExplicitAndDefaultPriorityTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMixedExplicitAndDefaultPriorityTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPriorityWarnings();

            Assert.That(
                warnings.ContainsKey("Setup"),
                Is.False,
                "Setup group should not have priority warning (one explicit 0, one default)"
            );
            Assert.That(
                warnings.ContainsKey("Cleanup"),
                Is.False,
                "Cleanup group should not have priority warning (one explicit 10, two defaults)"
            );
        }

        [Test]
        public void MixedExplicitAndDefaultPriorityUsesExplicitPriority()
        {
            WButtonMixedExplicitAndDefaultPriorityTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMixedExplicitAndDefaultPriorityTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            List<WButtonGroupKey> setupGroups = groupCounts
                .Keys.Where(k => k._groupName == "Setup")
                .ToList();
            List<WButtonGroupKey> cleanupGroups = groupCounts
                .Keys.Where(k => k._groupName == "Cleanup")
                .ToList();

            Assert.That(setupGroups, Has.Count.EqualTo(1), "Should have exactly one Setup group");
            Assert.That(
                setupGroups[0]._groupPriority,
                Is.EqualTo(0),
                "Setup should use explicit priority 0"
            );
            Assert.That(
                groupCounts.ValueFor(setupGroups[0]),
                Is.EqualTo(2),
                "Setup should have 2 buttons"
            );

            Assert.That(
                cleanupGroups,
                Has.Count.EqualTo(1),
                "Should have exactly one Cleanup group"
            );
            Assert.That(
                cleanupGroups[0]._groupPriority,
                Is.EqualTo(10),
                "Cleanup should use explicit priority 10"
            );
            Assert.That(
                groupCounts.ValueFor(cleanupGroups[0]),
                Is.EqualTo(3),
                "Cleanup should have 3 buttons"
            );
        }

        [Test]
        public void AllDefaultPriorityDoesNotGenerateWarning()
        {
            WButtonAllDefaultPriorityTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonAllDefaultPriorityTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPriorityWarnings();

            Assert.That(
                warnings,
                Is.Empty,
                "All default priorities should not generate any warnings"
            );
        }

        [Test]
        public void AllSameExplicitPriorityDoesNotGenerateWarning()
        {
            WButtonAllSameExplicitPriorityTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonAllSameExplicitPriorityTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPriorityWarnings();

            Assert.That(
                warnings,
                Is.Empty,
                "All same explicit priorities should not generate any warnings"
            );
        }

        [Test]
        public void MultipleExplicitPriorityConflictGeneratesWarning()
        {
            WButtonMultipleExplicitPriorityConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMultipleExplicitPriorityConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPriorityWarnings();

            Assert.That(
                warnings.ContainsKey("ConflictGroup"),
                Is.True,
                "Should have warning for ConflictGroup with multiple explicit priorities"
            );
            Assert.That(
                warnings.ValueFor("ConflictGroup")._allGroupPriorities,
                Contains.Item(0),
                "Warning should include priority 0"
            );
            Assert.That(
                warnings.ValueFor("ConflictGroup")._allGroupPriorities,
                Contains.Item(10),
                "Warning should include priority 10"
            );
            Assert.That(
                warnings
                    .ValueFor("ConflictGroup")
                    ._allGroupPriorities.Contains(WButtonAttribute.NoGroupPriority),
                Is.False,
                "Warning should NOT include NoGroupPriority (it's filtered out)"
            );
        }

        [Test]
        public void MultipleExplicitPriorityConflictUsesFirstDeclaredPriority()
        {
            WButtonMultipleExplicitPriorityConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMultipleExplicitPriorityConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            List<WButtonGroupKey> conflictGroups = groupCounts
                .Keys.Where(k => k._groupName == "ConflictGroup")
                .ToList();

            Assert.That(
                conflictGroups,
                Has.Count.EqualTo(1),
                "Should have exactly one ConflictGroup"
            );
            Assert.That(
                conflictGroups[0]._groupPriority,
                Is.EqualTo(0),
                "Group should use first declared priority (0)"
            );
        }

        [Test]
        public void ExistingPriorityConflictTargetStillGeneratesWarning()
        {
            WButtonGroupPriorityConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPriorityConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPriorityWarnings();

            Assert.That(
                warnings.ContainsKey("ConflictGroup"),
                Is.True,
                "Existing test with two explicit priorities should still generate warning"
            );
        }

        [Test]
        public void MixedExplicitAndDefaultPlacementRendersCorrectly()
        {
            WButtonMixedExplicitAndDefaultPlacementTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMixedExplicitAndDefaultPlacementTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            Assert.That(drawnAtTop, Is.True, "Debug Tools (Top) should render at top");
            Assert.That(drawnAtBottom, Is.True, "Save System (Bottom) should render at bottom");
        }

        [Test]
        public void NoWarningsForMixedPlacementGroupsWithSingleExplicitValue()
        {
            WButtonMixedPlacementGroupsTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMixedPlacementGroupsTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> placementWarnings =
                WButtonGUI.GetConflictingGroupPlacementWarnings();
            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> priorityWarnings =
                WButtonGUI.GetConflictingGroupPriorityWarnings();

            Assert.That(
                placementWarnings,
                Is.Empty,
                "No placement warnings expected for groups with consistent placements"
            );
            Assert.That(
                priorityWarnings,
                Is.Empty,
                "No priority warnings expected for groups with consistent priorities"
            );
        }

        [Test]
        public void SingleButtonGroupWithExplicitPlacementDoesNotGenerateWarning()
        {
            WButtonGroupPlacementTopTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPlacementTopTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPlacementWarnings();

            Assert.That(
                warnings,
                Is.Empty,
                "Single button group with explicit placement should not generate warning"
            );
        }

        [Test]
        public void SingleButtonGroupWithDefaultPlacementDoesNotGenerateWarning()
        {
            WButtonGroupPlacementDefaultTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPlacementDefaultTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPlacementWarnings();

            Assert.That(
                warnings,
                Is.Empty,
                "Single button group with default placement should not generate warning"
            );
        }

        [Test]
        public void UseGlobalSettingWithGlobalTopRendersAtTop()
        {
            WButtonUseGlobalSettingTopTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonUseGlobalSettingTopTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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
                "UseGlobalSetting should render at top when global is top"
            );
            Assert.That(
                drawnAtBottom,
                Is.False,
                "UseGlobalSetting should not render at bottom when global is top"
            );
        }

        [Test]
        public void UseGlobalSettingWithGlobalBottomRendersAtBottom()
        {
            WButtonUseGlobalSettingTopTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonUseGlobalSettingTopTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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
                "UseGlobalSetting should not render at top when global is bottom"
            );
            Assert.That(
                drawnAtBottom,
                Is.True,
                "UseGlobalSetting should render at bottom when global is bottom"
            );
        }

        [Test]
        public void PlacementConflictWarningOnlyContainsExplicitValues()
        {
            WButtonGroupPlacementConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPlacementConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPlacementConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPlacementWarnings();

            Assert.That(warnings.ContainsKey("ConflictGroup"), Is.True);
            Assert.That(
                warnings.ValueFor("ConflictGroup")._allGroupPlacements.Count,
                Is.EqualTo(2),
                "Should only have 2 explicit placements (Top and Bottom)"
            );
            Assert.That(
                warnings
                    .ValueFor("ConflictGroup")
                    ._allGroupPlacements.Contains(WButtonGroupPlacement.UseGlobalSetting),
                Is.False,
                "UseGlobalSetting should never appear in conflict warnings"
            );
        }

        [Test]
        public void PriorityConflictWarningOnlyContainsExplicitValues()
        {
            WButtonGroupPriorityConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPriorityConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            IReadOnlyDictionary<string, WButtonGUI.GroupPriorityConflictInfo> warnings =
                WButtonGUI.GetConflictingGroupPriorityWarnings();

            Assert.That(warnings.ContainsKey("ConflictGroup"), Is.True);
            Assert.That(
                warnings.ValueFor("ConflictGroup")._allGroupPriorities.Count,
                Is.EqualTo(2),
                "Should only have 2 explicit priorities (0 and 10)"
            );
            Assert.That(
                warnings
                    .ValueFor("ConflictGroup")
                    ._allGroupPriorities.Contains(WButtonAttribute.NoGroupPriority),
                Is.False,
                "NoGroupPriority should never appear in conflict warnings"
            );
        }
    }
}
#endif
