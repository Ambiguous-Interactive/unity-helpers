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
    /// Comprehensive tests for WButton group placement functionality.
    /// These tests verify:
    /// - Groups with explicit groupPlacement render in the correct location
    /// - UseGlobalSetting respects the global placement setting
    /// - Conflicting groupPlacement values generate warnings
    /// - groupPriority controls render order within placement sections
    /// - Ungrouped buttons ignore groupPlacement and groupPriority
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonGroupPlacementTests : BatchedEditorTestBase
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
        public void DefaultPlacementGroupRendersWithGlobalSettingTop()
        {
            WButtonGroupPlacementDefaultTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPlacementDefaultTarget>()
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
                "Default group should render at top when global is top"
            );
            Assert.That(
                drawnAtBottom,
                Is.False,
                "Default group should not render at bottom when global is top"
            );
        }

        [Test]
        public void DefaultPlacementGroupRendersWithGlobalSettingBottom()
        {
            WButtonGroupPlacementDefaultTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPlacementDefaultTarget>()
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
                "Default group should not render at top when global is bottom"
            );
            Assert.That(
                drawnAtBottom,
                Is.True,
                "Default group should render at bottom when global is bottom"
            );
        }

        [Test]
        public void ExplicitTopPlacementAlwaysRendersAtTop()
        {
            WButtonGroupPlacementTopTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPlacementTopTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            bool drawnAtBottomWithGlobalTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(
                drawnAtTopWithGlobalTop,
                Is.True,
                "Explicit Top group should render at top when global is top"
            );
            Assert.That(
                drawnAtBottomWithGlobalTop,
                Is.False,
                "Explicit Top group should not render at bottom when global is top"
            );

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
                "Explicit Top group should render at top even when global is bottom"
            );
            Assert.That(
                drawnAtBottomWithGlobalBottom,
                Is.False,
                "Explicit Top group should not render at bottom when global is bottom"
            );
        }

        [Test]
        public void ExplicitBottomPlacementAlwaysRendersAtBottom()
        {
            WButtonGroupPlacementBottomTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPlacementBottomTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
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

            bool drawnAtBottomWithGlobalTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(
                drawnAtTopWithGlobalTop,
                Is.False,
                "Explicit Bottom group should not render at top when global is top"
            );
            Assert.That(
                drawnAtBottomWithGlobalTop,
                Is.True,
                "Explicit Bottom group should render at bottom when global is top"
            );

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
                Is.False,
                "Explicit Bottom group should not render at top when global is bottom"
            );
            Assert.That(
                drawnAtBottomWithGlobalBottom,
                Is.True,
                "Explicit Bottom group should render at bottom even when global is bottom"
            );
        }

        [Test]
        public void ConflictingGroupPlacementUsesFirstDeclaredPlacement()
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

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> conflictGroups = groupCounts
                .Keys.Where(k => k._groupName == "ConflictGroup")
                .ToList();

            Assert.That(
                conflictGroups,
                Has.Count.EqualTo(1),
                "Should have exactly one 'ConflictGroup'"
            );
            Assert.That(
                conflictGroups[0]._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Top),
                "Group should use first declared placement (Top)"
            );
            Assert.That(
                groupCounts.ValueFor(conflictGroups[0]),
                Is.EqualTo(2),
                "Group should contain both buttons"
            );
        }

        [Test]
        public void ConflictingGroupPlacementGeneratesWarning()
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
                "Should have warning for ConflictGroup"
            );
            Assert.That(
                warnings.ValueFor("ConflictGroup")._canonicalGroupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Top),
                "Warning should indicate canonical placement is Top"
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
        }

        [Test]
        public void ConflictingGroupPlacementReverseUsesFirstDeclaredPlacement()
        {
            WButtonGroupPlacementConflictReverseTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPlacementConflictReverseTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
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
                "Should have exactly one 'ConflictGroup'"
            );
            Assert.That(
                conflictGroups[0]._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Bottom),
                "Group should use first declared placement (Bottom)"
            );
        }

        [Test]
        public void MixedPlacementGroupsRenderInCorrectLocations()
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

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();

            List<WButtonGroupKey> topGroups = groupCounts
                .Keys.Where(k => k._groupName == "TopGroup")
                .ToList();
            List<WButtonGroupKey> bottomGroups = groupCounts
                .Keys.Where(k => k._groupName == "BottomGroup")
                .ToList();
            List<WButtonGroupKey> defaultGroups = groupCounts
                .Keys.Where(k => k._groupName == "DefaultGroup")
                .ToList();

            Assert.That(topGroups, Has.Count.EqualTo(1), "Should have TopGroup");
            Assert.That(
                topGroups[0]._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Top),
                "TopGroup should have Top placement"
            );
            Assert.That(
                groupCounts.ValueFor(topGroups[0]),
                Is.EqualTo(2),
                "TopGroup should have 2 buttons"
            );

            Assert.That(bottomGroups, Has.Count.EqualTo(1), "Should have BottomGroup");
            Assert.That(
                bottomGroups[0]._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Bottom),
                "BottomGroup should have Bottom placement"
            );
            Assert.That(
                groupCounts.ValueFor(bottomGroups[0]),
                Is.EqualTo(2),
                "BottomGroup should have 2 buttons"
            );

            Assert.That(defaultGroups, Has.Count.EqualTo(1), "Should have DefaultGroup");
            Assert.That(
                defaultGroups[0]._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.UseGlobalSetting),
                "DefaultGroup should have UseGlobalSetting placement"
            );
        }

        [Test]
        public void GroupPriorityControlsRenderOrder()
        {
            WButtonGroupPriorityTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonGroupPriorityTarget>()
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

            WButtonGroupKey group0 = groupCounts.Keys.First(k => k._groupName == "Group0");
            WButtonGroupKey group5 = groupCounts.Keys.First(k => k._groupName == "Group5");
            WButtonGroupKey group10 = groupCounts.Keys.First(k => k._groupName == "Group10");
            WButtonGroupKey groupNoPriority = groupCounts.Keys.First(k =>
                k._groupName == "GroupNoPriority"
            );

            Assert.That(group0._groupPriority, Is.EqualTo(0), "Group0 should have priority 0");
            Assert.That(group5._groupPriority, Is.EqualTo(5), "Group5 should have priority 5");
            Assert.That(group10._groupPriority, Is.EqualTo(10), "Group10 should have priority 10");
            Assert.That(
                groupNoPriority._groupPriority,
                Is.EqualTo(WButtonAttribute.NoGroupPriority),
                "GroupNoPriority should have NoGroupPriority"
            );

            Assert.That(
                group0.CompareTo(group5),
                Is.LessThan(0),
                "Group0 should sort before Group5"
            );
            Assert.That(
                group5.CompareTo(group10),
                Is.LessThan(0),
                "Group5 should sort before Group10"
            );
            Assert.That(
                group10.CompareTo(groupNoPriority),
                Is.LessThan(0),
                "Group10 should sort before GroupNoPriority"
            );
        }

        [Test]
        public void ConflictingGroupPriorityUsesFirstDeclaredPriority()
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

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> conflictGroups = groupCounts
                .Keys.Where(k => k._groupName == "ConflictGroup")
                .ToList();

            Assert.That(conflictGroups, Has.Count.EqualTo(1), "Should have one ConflictGroup");
            Assert.That(
                conflictGroups[0]._groupPriority,
                Is.EqualTo(0),
                "Group should use first declared priority (0)"
            );
        }

        [Test]
        public void ConflictingGroupPriorityGeneratesWarning()
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
                "Should have warning for ConflictGroup"
            );
            Assert.That(
                warnings.ValueFor("ConflictGroup")._canonicalGroupPriority,
                Is.EqualTo(0),
                "Warning should indicate canonical priority is 0"
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
        }

        [Test]
        public void UngroupedButtonsIgnoreGroupPlacementAndPriority()
        {
            WButtonUngroupedPlacementTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonUngroupedPlacementTarget>()
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
            List<WButtonGroupKey> ungroupedKeys = groupCounts
                .Keys.Where(k => string.IsNullOrEmpty(k._groupName))
                .ToList();

            Assert.That(
                ungroupedKeys.Count,
                Is.GreaterThanOrEqualTo(1),
                "Should have ungrouped buttons"
            );

            foreach (WButtonGroupKey key in ungroupedKeys)
            {
                Assert.That(
                    key._groupPlacement,
                    Is.EqualTo(WButtonGroupPlacement.UseGlobalSetting),
                    "Ungrouped buttons should have UseGlobalSetting placement regardless of attribute value"
                );
                Assert.That(
                    key._groupPriority,
                    Is.EqualTo(WButtonAttribute.NoGroupPriority),
                    "Ungrouped buttons should have NoGroupPriority regardless of attribute value"
                );
            }
        }

        [Test]
        public void ComplexGroupingOrdersCorrectly()
        {
            WButtonComplexGroupingTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonComplexGroupingTarget>()
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

            WButtonGroupKey highPriorityTop = groupCounts.Keys.First(k =>
                k._groupName == "HighPriorityTop"
            );
            WButtonGroupKey lowPriorityTop = groupCounts.Keys.First(k =>
                k._groupName == "LowPriorityTop"
            );
            WButtonGroupKey bottomGroup = groupCounts.Keys.First(k =>
                k._groupName == "BottomGroup"
            );

            Assert.That(
                highPriorityTop._groupPriority,
                Is.EqualTo(0),
                "HighPriorityTop should have priority 0"
            );
            Assert.That(
                lowPriorityTop._groupPriority,
                Is.EqualTo(10),
                "LowPriorityTop should have priority 10"
            );
            Assert.That(
                highPriorityTop.CompareTo(lowPriorityTop),
                Is.LessThan(0),
                "HighPriorityTop should sort before LowPriorityTop"
            );

            Assert.That(
                highPriorityTop._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Top),
                "HighPriorityTop should have Top placement"
            );
            Assert.That(
                bottomGroup._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.Bottom),
                "BottomGroup should have Bottom placement"
            );

            Assert.That(
                groupCounts.ValueFor(highPriorityTop),
                Is.EqualTo(2),
                "HighPriorityTop should have 2 buttons"
            );
            Assert.That(
                groupCounts.ValueFor(lowPriorityTop),
                Is.EqualTo(2),
                "LowPriorityTop should have 2 buttons"
            );
        }

        [Test]
        public void WButtonGroupKeyCompareToHandlesSamePriorityDifferentDrawOrder()
        {
            WButtonGroupKey key1 = new(0, 10, "GroupA", 0, WButtonGroupPlacement.Top);
            WButtonGroupKey key2 = new(0, 20, "GroupB", 0, WButtonGroupPlacement.Top);

            Assert.That(
                key1.CompareTo(key2),
                Is.LessThan(0),
                "Key with lower drawOrder should sort first when priorities are equal"
            );
        }

        [Test]
        public void WButtonGroupKeyCompareToHandlesSamePriorityAndDrawOrderDifferentDeclarationOrder()
        {
            WButtonGroupKey key1 = new(0, 10, "GroupA", 5, WButtonGroupPlacement.Top);
            WButtonGroupKey key2 = new(0, 10, "GroupB", 10, WButtonGroupPlacement.Top);

            Assert.That(
                key1.CompareTo(key2),
                Is.LessThan(0),
                "Key with lower declarationOrder should sort first when priority and drawOrder are equal"
            );
        }

        [Test]
        public void WButtonGroupKeyEqualsHandlesAllFields()
        {
            WButtonGroupKey key1 = new(5, 10, "Group", 0, WButtonGroupPlacement.Top);
            WButtonGroupKey key2 = new(5, 10, "Group", 0, WButtonGroupPlacement.Top);
            WButtonGroupKey key3 = new(5, 10, "Group", 0, WButtonGroupPlacement.Bottom);
            WButtonGroupKey key4 = new(10, 10, "Group", 0, WButtonGroupPlacement.Top);

            Assert.That(key1.Equals(key2), Is.True, "Identical keys should be equal");
            Assert.That(
                key1.Equals(key3),
                Is.False,
                "Keys with different placement should not be equal"
            );
            Assert.That(
                key1.Equals(key4),
                Is.False,
                "Keys with different priority should not be equal"
            );
        }

        [Test]
        public void WButtonGroupKeyGetHashCodeIsConsistent()
        {
            WButtonGroupKey key1 = new(5, 10, "Group", 0, WButtonGroupPlacement.Top);
            WButtonGroupKey key2 = new(5, 10, "Group", 0, WButtonGroupPlacement.Top);

            Assert.That(
                key1.GetHashCode(),
                Is.EqualTo(key2.GetHashCode()),
                "Equal keys should have same hash code"
            );
        }

        [Test]
        public void NoPlacementWarningsForConsistentGroups()
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
                "Should have no placement warnings for consistent groups"
            );
        }
    }
}
#endif
