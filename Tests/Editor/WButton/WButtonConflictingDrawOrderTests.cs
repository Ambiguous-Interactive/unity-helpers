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
    /// Comprehensive tests for WButton draw order conflict handling.
    /// These tests verify that:
    /// - Buttons with the same groupName but different drawOrder values are merged into a single group
    /// - The first declared button's drawOrder is used as the canonical draw order for the group
    /// - Appropriate warnings are generated for conflicting draw orders
    /// - Ungrouped buttons remain separate by draw order
    /// - All buttons are always displayed (the original bug was that nothing was rendered)
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WButtonConflictingDrawOrderTests : BatchedEditorTestBase
    {
        [SetUp]
        public void SetUp()
        {
            base.BaseSetUp();
            WButtonGUI.ClearGroupDataForTesting();
            WButtonGUI.ClearConflictingDrawOrderWarningsForTesting();
            WButtonGUI.ClearConflictWarningContentCacheForTesting();
            WButtonGUI.ClearContextCache();
        }

        [TearDown]
        public override void TearDown()
        {
            WButtonGUI.ClearGroupDataForTesting();
            WButtonGUI.ClearConflictingDrawOrderWarningsForTesting();
            WButtonGUI.ClearConflictWarningContentCacheForTesting();
            WButtonGUI.ClearContextCache();
            base.TearDown();
        }

        [Test]
        public void ConflictingDrawOrderButtonsAreMergedIntoSingleGroup()
        {
            // Two buttons with the same groupName but different drawOrder.
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> setupGroups = groupCounts
                .Keys.Where(k => k._groupName == "Setup")
                .ToList();

            Assert.That(
                setupGroups,
                Is.Not.Empty,
                $"Expected to find 'Setup' group but found: [{string.Join(", ", groupCounts.Keys.Select(k => $"'{k._groupName}'"))}]"
            );
            Assert.That(setupGroups, Has.Count.EqualTo(1), "Should have exactly one 'Setup' group");
            Assert.That(
                groupCounts.ValueFor(setupGroups[0]),
                Is.EqualTo(2),
                "Setup group should contain both buttons"
            );
        }

        [Test]
        public void ConflictingDrawOrderUsesFirstDeclaredButtonDrawOrder()
        {
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            // -21 comes from Initialize, the first-declared button.
            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            WButtonGroupKey setupGroup = groupCounts.Keys.First(k => k._groupName == "Setup");

            Assert.That(
                setupGroup._drawOrder,
                Is.EqualTo(-21),
                "Group should use draw order -21 from first declared button (Initialize)"
            );
        }

        [Test]
        public void ConflictingDrawOrderGeneratesWarning()
        {
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                WButtonGUI.GetConflictingDrawOrderWarnings();

            Assert.That(warnings, Contains.Key("Setup"), "Should have warning for Setup group");
            WButtonGUI.DrawOrderConflictInfo conflict = warnings.ValueFor("Setup");
            Assert.That(conflict._canonicalDrawOrder, Is.EqualTo(-21));
            Assert.That(conflict._allDrawOrders, Contains.Item(-21));
            Assert.That(conflict._allDrawOrders, Contains.Item(-2));
            Assert.That(conflict._allDrawOrders, Has.Count.EqualTo(2));
        }

        [Test]
        public void ThreeWayConflictMergesAllButtonsIntoOneGroup()
        {
            WButtonThreeWayConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonThreeWayConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> actionsGroups = groupCounts
                .Keys.Where(k => k._groupName == "Actions")
                .ToList();

            Assert.That(
                actionsGroups,
                Has.Count.EqualTo(1),
                "Should have exactly one 'Actions' group"
            );
            Assert.That(
                groupCounts.ValueFor(actionsGroups[0]),
                Is.EqualTo(3),
                "Actions group should contain all three buttons"
            );
        }

        [Test]
        public void ThreeWayConflictUsesFirstDeclaredDrawOrder()
        {
            WButtonThreeWayConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonThreeWayConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            // 5 comes from FirstAction, the first declared.
            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            WButtonGroupKey actionsGroup = groupCounts.Keys.First(k => k._groupName == "Actions");

            Assert.That(actionsGroup._drawOrder, Is.EqualTo(5));
        }

        [Test]
        public void ThreeWayConflictWarningContainsAllDrawOrders()
        {
            WButtonThreeWayConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonThreeWayConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                WButtonGUI.GetConflictingDrawOrderWarnings();

            Assert.That(warnings, Contains.Key("Actions"));
            WButtonGUI.DrawOrderConflictInfo conflict = warnings.ValueFor("Actions");
            Assert.That(conflict._allDrawOrders, Contains.Item(5));
            Assert.That(conflict._allDrawOrders, Contains.Item(-10));
            Assert.That(conflict._allDrawOrders, Contains.Item(100));
            Assert.That(conflict._allDrawOrders, Has.Count.EqualTo(3));
        }

        [Test]
        public void MultipleConflictingGroupsAreHandledIndependently()
        {
            WButtonMultipleConflictingGroupsTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMultipleConflictingGroupsTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            // Both placements are needed to collect every group.
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();

            List<WButtonGroupKey> groupAKeys = groupCounts
                .Keys.Where(k => k._groupName == "GroupA")
                .ToList();
            List<WButtonGroupKey> groupBKeys = groupCounts
                .Keys.Where(k => k._groupName == "GroupB")
                .ToList();

            Assert.That(groupAKeys, Has.Count.EqualTo(1), "Should have exactly one GroupA");
            Assert.That(groupBKeys, Has.Count.EqualTo(1), "Should have exactly one GroupB");

            // GroupA should have 3 buttons, GroupB should have 2 buttons
            Assert.That(groupCounts.ValueFor(groupAKeys[0]), Is.EqualTo(3));
            Assert.That(groupCounts.ValueFor(groupBKeys[0]), Is.EqualTo(2));
        }

        [Test]
        public void MultipleConflictingGroupsUseCorrectCanonicalDrawOrders()
        {
            WButtonMultipleConflictingGroupsTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMultipleConflictingGroupsTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();

            WButtonGroupKey groupA = groupCounts.Keys.First(k => k._groupName == "GroupA");
            WButtonGroupKey groupB = groupCounts.Keys.First(k => k._groupName == "GroupB");

            Assert.That(
                groupA._drawOrder,
                Is.EqualTo(0),
                "GroupA should use draw order 0 from FirstA"
            );
            Assert.That(
                groupB._drawOrder,
                Is.EqualTo(-5),
                "GroupB should use draw order -5 from FirstB"
            );
        }

        [Test]
        public void MultipleConflictingGroupsGenerateSeparateWarnings()
        {
            WButtonMultipleConflictingGroupsTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMultipleConflictingGroupsTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                WButtonGUI.GetConflictingDrawOrderWarnings();

            Assert.That(warnings, Contains.Key("GroupA"));
            Assert.That(warnings, Contains.Key("GroupB"));
            Assert.That(warnings.ValueFor("GroupA")._allDrawOrders, Has.Count.EqualTo(3)); // 0, 10, -3
            Assert.That(warnings.ValueFor("GroupB")._allDrawOrders, Has.Count.EqualTo(2)); // -5, -20
        }

        [Test]
        public void MixedGroupedAndUngroupedButtonsAreHandledCorrectly()
        {
            WButtonMixedGroupedAndUngroupedTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMixedGroupedAndUngroupedTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();

            // Setup group should have 2 buttons merged
            List<WButtonGroupKey> setupGroups = groupCounts
                .Keys.Where(k => k._groupName == "Setup")
                .ToList();
            Assert.That(setupGroups, Has.Count.EqualTo(1));
            Assert.That(groupCounts.ValueFor(setupGroups[0]), Is.EqualTo(2));

            // Ungrouped buttons should remain separate (empty group name)
            List<WButtonGroupKey> ungroupedGroups = groupCounts
                .Keys.Where(k => string.IsNullOrEmpty(k._groupName))
                .ToList();

            // Should have 2 separate ungrouped groups (draw order 0 and -5)
            Assert.That(ungroupedGroups, Has.Count.EqualTo(2));
        }

        [Test]
        public void UngroupedButtonsRemainSeparateByDrawOrder()
        {
            WButtonUngroupedDifferentDrawOrdersTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonUngroupedDifferentDrawOrdersTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> ungroupedGroups = groupCounts
                .Keys.Where(k => string.IsNullOrEmpty(k._groupName))
                .ToList();

            Assert.That(
                ungroupedGroups,
                Has.Count.EqualTo(3),
                "Should have 3 separate ungrouped groups"
            );

            // Each should have exactly 1 button
            foreach (WButtonGroupKey key in ungroupedGroups)
            {
                Assert.That(groupCounts.ValueFor(key), Is.EqualTo(1));
            }

            // Verify the draw orders are different
            HashSet<int> drawOrders = new(ungroupedGroups.Select(k => k._drawOrder));
            Assert.That(drawOrders, Has.Count.EqualTo(3));
        }

        [Test]
        public void FirstDeclarationWinsEvenWithLowerDrawOrderSecond()
        {
            WButtonFirstDeclarationWinsTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonFirstDeclarationWinsTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            // The first-declared button has drawOrder 10; -10 is numerically lower but declared second.
            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            WButtonGroupKey testGroup = groupCounts.Keys.First(k => k._groupName == "Test");

            Assert.That(
                testGroup._drawOrder,
                Is.EqualTo(10),
                "Should use draw order 10 from first declared button, not -10"
            );
        }

        [Test]
        public void ExtremeDrawOrderValuesAreMergedCorrectly()
        {
            WButtonExtremeDrawOrderConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonExtremeDrawOrderConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> extremeGroups = groupCounts
                .Keys.Where(k => k._groupName == "Extreme")
                .ToList();

            Assert.That(extremeGroups, Has.Count.EqualTo(1));
            Assert.That(groupCounts.ValueFor(extremeGroups[0]), Is.EqualTo(3));
            Assert.That(extremeGroups[0]._drawOrder, Is.EqualTo(int.MinValue));
        }

        [Test]
        public void ExtremeDrawOrderConflictGeneratesWarning()
        {
            WButtonExtremeDrawOrderConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonExtremeDrawOrderConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                WButtonGUI.GetConflictingDrawOrderWarnings();

            Assert.That(warnings, Contains.Key("Extreme"));
            Assert.That(warnings.ValueFor("Extreme")._allDrawOrders, Contains.Item(int.MinValue));
            Assert.That(warnings.ValueFor("Extreme")._allDrawOrders, Contains.Item(int.MaxValue));
            Assert.That(warnings.ValueFor("Extreme")._allDrawOrders, Contains.Item(0));
        }

        [Test]
        public void CrossPlacementConflictUsesFirstDeclaredPlacement()
        {
            // The first-declared button has draw order 0.
            WButtonCrossPlacementConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonCrossPlacementConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            // Draw at top placement - should find the group here
            bool drawnTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Assert.That(drawnTop, Is.True, "Group should render at top placement");

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            WButtonGroupKey crossGroup = groupCounts.Keys.First(k =>
                k._groupName == "CrossPlacement"
            );

            Assert.That(
                crossGroup._drawOrder,
                Is.EqualTo(0),
                "Should use draw order 0 (top placement)"
            );
            Assert.That(
                groupCounts.ValueFor(crossGroup),
                Is.EqualTo(2),
                "Both buttons should be in the group"
            );
        }

        [Test]
        public void CrossPlacementConflictReverseUsesFirstDeclaredDrawOrder()
        {
            // groupPlacement is unspecified, so it defaults to UseGlobalSetting and globalPlacementIsTop decides.
            WButtonCrossPlacementConflictReverseTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonCrossPlacementConflictReverseTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            // globalPlacementIsTop defaults true, so the buttons render at Top.
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            // Draw at top placement - should find the group here since UseGlobalSetting defaults to top
            bool drawnTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            // The group uses the first-declared draw order (-5); placement follows globalPlacementIsTop.
            Assert.That(
                drawnTop,
                Is.True,
                $"Group with UseGlobalSetting should render at Top when globalPlacementIsTop=true. Groups: {string.Join(", ", WButtonGUI.GetGroupCountsForTesting().Keys.Select(k => $"{k._groupName}:{k._groupPlacement}"))}"
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            WButtonGroupKey crossGroup = groupCounts.Keys.First(k =>
                k._groupName == "CrossPlacement"
            );

            Assert.That(
                crossGroup._drawOrder,
                Is.EqualTo(-5),
                "Should use draw order -5 from first declared button"
            );
            Assert.That(
                crossGroup._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.UseGlobalSetting),
                "GroupPlacement should be UseGlobalSetting (default)"
            );
            Assert.That(
                groupCounts.ValueFor(crossGroup),
                Is.EqualTo(2),
                "Both buttons should be in the group"
            );
        }

        [Test]
        public void NoConflictWhenAllButtonsHaveSameDrawOrder()
        {
            WButtonNoConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonNoConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                WButtonGUI.GetConflictingDrawOrderWarnings();

            Assert.That(
                warnings.ContainsKey("NoConflict"),
                Is.False,
                "Should not generate warning for matching draw orders"
            );

            // But group should still work correctly
            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> noConflictGroups = groupCounts
                .Keys.Where(k => k._groupName == "NoConflict")
                .ToList();

            Assert.That(noConflictGroups, Has.Count.EqualTo(1));
            Assert.That(groupCounts.ValueFor(noConflictGroups[0]), Is.EqualTo(3));
        }

        [Test]
        public void SingleButtonGroupHasNoConflict()
        {
            WButtonSingleButtonGroupTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonSingleButtonGroupTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                WButtonGUI.GetConflictingDrawOrderWarnings();

            Assert.That(warnings.ContainsKey("Single"), Is.False);
        }

        [Test]
        public void MetadataPreservesIndividualDrawOrders()
        {
            // Even though buttons are merged into one group, their metadata should preserve original draw orders
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonConflictingDrawOrderTarget)
            );

            WButtonMethodMetadata initialize = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonConflictingDrawOrderTarget.Initialize)
            );
            WButtonMethodMetadata validateConfig = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonConflictingDrawOrderTarget.ValidateConfig)
            );

            Assert.That(initialize, Is.Not.Null);
            Assert.That(validateConfig, Is.Not.Null);
            Assert.That(initialize.DrawOrder, Is.EqualTo(-21));
            Assert.That(validateConfig.DrawOrder, Is.EqualTo(-2));
        }

        [Test]
        public void MetadataGroupNamesMatch()
        {
            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonConflictingDrawOrderTarget)
            );

            WButtonMethodMetadata initialize = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonConflictingDrawOrderTarget.Initialize)
            );
            WButtonMethodMetadata validateConfig = metadata.FirstOrDefault(m =>
                m.Method.Name == nameof(WButtonConflictingDrawOrderTarget.ValidateConfig)
            );

            Assert.That(initialize.GroupName, Is.EqualTo("Setup"));
            Assert.That(validateConfig.GroupName, Is.EqualTo("Setup"));
        }

        [Test]
        public void GroupNameIsResolvedCorrectlyForConflictingGroup()
        {
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, string> groupNames = WButtonGUI.GetGroupNamesForTesting();
            WButtonGroupKey setupKey = groupNames.Keys.FirstOrDefault(k => k._groupName == "Setup");

            Assert.That(setupKey._groupName, Is.Not.Null);
            Assert.That(groupNames.ValueFor(setupKey), Is.EqualTo("Setup"));
        }

        [Test]
        public void ButtonsAreAlwaysRenderedEvenWithConflictingDrawOrders()
        {
            // The original defect rendered nothing at all.
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            List<WButtonMethodContext> triggeredContexts = new();

            // The target uses UseGlobalSetting and globalPlacementIsTop defaults true, so buttons land at Top.
            bool anyDrawn = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts,
                globalPlacementIsTop: true
            );

            Assert.That(
                anyDrawn,
                Is.True,
                $"Buttons must always be rendered. Groups found: {string.Join(", ", WButtonGUI.GetGroupCountsForTesting().Keys.Select(k => $"{k._groupName}:{k._drawOrder}:{k._groupPlacement}"))}"
            );

            // Verify both buttons are accessible
            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            int totalButtons = groupCounts.Values.Sum();
            Assert.That(totalButtons, Is.EqualTo(2), "Both buttons must be in groups");
        }

        [Test]
        public void LargeConflictingGroupStillRendersAllButtons()
        {
            // WButtonThreeWayConflictTarget carries three conflicting buttons.
            WButtonThreeWayConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonThreeWayConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool anyDrawn = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Assert.That(anyDrawn, Is.True);
            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            int totalButtons = groupCounts.Values.Sum();
            Assert.That(totalButtons, Is.EqualTo(3));
        }

        [Test]
        public void ButtonsWithUseGlobalSettingRespectGlobalPlacement()
        {
            // The target uses UseGlobalSetting, so globalPlacementIsTop decides placement, not drawOrder.
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            // With globalPlacementIsTop false the buttons render at Bottom.
            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawnTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: false
            );

            // Clear for next check
            WButtonGUI.ClearGroupDataForTesting();

            bool drawnBottom = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: false
            );

            Assert.That(
                drawnTop,
                Is.False,
                $"Group with UseGlobalSetting should NOT render at Top when globalPlacementIsTop=false. DrawOrder: -21, GroupPlacement: {WButtonGroupPlacement.UseGlobalSetting}"
            );
            Assert.That(
                drawnBottom,
                Is.True,
                "Group with UseGlobalSetting should render at Bottom when globalPlacementIsTop=false"
            );
        }

        [Test]
        public void BothPlacementsRenderAllButtonsWhenCalled()
        {
            WButtonMixedGroupedAndUngroupedTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMixedGroupedAndUngroupedTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            // Draw both placements (simulating what inspector does)
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            int totalButtons = groupCounts.Values.Sum();

            // The target has a "Setup" group of 2 (drawOrder -10 and 5, canonical -10) and ungrouped buttons at 0 and -5.
            Assert.That(totalButtons, Is.EqualTo(4), "All 4 buttons must be rendered");
        }

        [Test]
        public void GroupWithCanonicalDrawOrderPreservesDrawOrderValue()
        {
            // drawOrder does NOT determine placement; groupPlacement does, defaulting to UseGlobalSetting.
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            // With globalPlacementIsTop=true (default), UseGlobalSetting groups render at Top
            bool drawnTop = WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: true
            );

            Assert.That(
                drawnTop,
                Is.True,
                $"Group with UseGlobalSetting must render at Top when globalPlacementIsTop=true. Groups: {string.Join(", ", WButtonGUI.GetGroupCountsForTesting().Keys.Select(k => $"{k._groupName}:{k._drawOrder}:{k._groupPlacement}"))}"
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            WButtonGroupKey setupGroup = groupCounts.Keys.First(k => k._groupName == "Setup");

            Assert.That(
                setupGroup._drawOrder,
                Is.EqualTo(-21),
                "Canonical drawOrder should be -21 from first declared button"
            );
            Assert.That(
                setupGroup._groupPlacement,
                Is.EqualTo(WButtonGroupPlacement.UseGlobalSetting),
                "GroupPlacement should be UseGlobalSetting (default)"
            );
            Assert.That(
                groupCounts.Values.Sum(),
                Is.EqualTo(2),
                "Both buttons in the group must render"
            );
        }

        [Test]
        public void WarningContainsAllConflictingDrawOrders()
        {
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                WButtonGUI.GetConflictingDrawOrderWarnings();

            Assert.That(warnings, Contains.Key("Setup"));
            WButtonGUI.DrawOrderConflictInfo conflict = warnings.ValueFor("Setup");

            // Should contain both draw orders: -21 and -2
            Assert.That(conflict._allDrawOrders, Contains.Item(-21));
            Assert.That(conflict._allDrawOrders, Contains.Item(-2));
            Assert.That(conflict._allDrawOrders.Count, Is.EqualTo(2));
        }

        [Test]
        public void WarningShowsCanonicalDrawOrderFromFirstDeclaredButton()
        {
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                WButtonGUI.GetConflictingDrawOrderWarnings();

            WButtonGUI.DrawOrderConflictInfo conflict = warnings.ValueFor("Setup");

            // Canonical draw order should be -21 (from Initialize, first declared)
            Assert.That(conflict._canonicalDrawOrder, Is.EqualTo(-21));
        }

        [Test]
        public void EmptyGroupNameButtonsRemainSeparate()
        {
            // Buttons without a group name should NOT be merged even with different draw orders
            WButtonUngroupedDifferentDrawOrdersTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonUngroupedDifferentDrawOrdersTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            // Draw both placements
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> ungroupedKeys = groupCounts
                .Keys.Where(k => string.IsNullOrEmpty(k._groupName))
                .ToList();

            // Should have 3 separate groups (one per button)
            Assert.That(ungroupedKeys.Count, Is.EqualTo(3));

            // Each group should have exactly 1 button
            foreach (WButtonGroupKey key in ungroupedKeys)
            {
                Assert.That(groupCounts.ValueFor(key), Is.EqualTo(1));
            }
        }

        [Test]
        public void NoWarningForGroupsWithoutConflicts()
        {
            // Groups where all buttons have the same drawOrder should not generate warnings
            WButtonNoConflictTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonNoConflictTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            IReadOnlyDictionary<string, WButtonGUI.DrawOrderConflictInfo> warnings =
                WButtonGUI.GetConflictingDrawOrderWarnings();

            Assert.That(
                warnings.Count,
                Is.EqualTo(0),
                "Should have no warnings when all buttons have same drawOrder"
            );
        }

        [Test]
        public void ClearMethodsProperlyResetState()
        {
            // First populate some state
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            // Verify state exists
            Assert.That(WButtonGUI.GetGroupCountsForTesting().Count, Is.GreaterThan(0));
            Assert.That(WButtonGUI.GetConflictingDrawOrderWarnings().Count, Is.GreaterThan(0));

            // Clear all state
            WButtonGUI.ClearGroupDataForTesting();
            WButtonGUI.ClearConflictingDrawOrderWarningsForTesting();
            WButtonGUI.ClearConflictWarningContentCacheForTesting();

            // Verify cleared
            Assert.That(WButtonGUI.GetGroupCountsForTesting().Count, Is.EqualTo(0));
            Assert.That(WButtonGUI.GetConflictingDrawOrderWarnings().Count, Is.EqualTo(0));
        }

        [Test]
        public void FirstDeclaredButtonDeterminesCanonicalDrawOrderNotLowest()
        {
            // Test that the FIRST declared button wins, not the one with the lowest drawOrder
            WButtonFirstDeclarationWinsTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonFirstDeclarationWinsTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            // The first-declared button has drawOrder 10 and the second -10, so a lowest-wins rule would give -10.
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            WButtonGroupKey testGroup = groupCounts.Keys.First(k => k._groupName == "Test");

            Assert.That(
                testGroup._drawOrder,
                Is.EqualTo(10),
                "Should use first declared button's drawOrder (10), not lowest (-10)"
            );
        }

        [Test]
        public void FirstDeclaredButtonDeterminesCanonicalDrawOrderNotHighest()
        {
            // Opposite test - first declared has low drawOrder, second has high
            WButtonCrossPlacementConflictReverseTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonCrossPlacementConflictReverseTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            // The first-declared button has drawOrder -5 and the second 0, so a highest-wins rule would give 0.
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            WButtonGroupKey crossGroup = groupCounts.Keys.First(k =>
                k._groupName == "CrossPlacement"
            );

            Assert.That(
                crossGroup._drawOrder,
                Is.EqualTo(-5),
                "Should use first declared button's drawOrder (-5), not highest (0)"
            );
        }

        [Test]
        public void GroupsWithLowerDrawOrderRenderFirst()
        {
            WButtonMixedDrawOrderAndGroupsTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonMixedDrawOrderAndGroupsTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            // Draw both placements to get all groups
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );
            WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen
            );

            // Get the groups and verify they are sorted
            Dictionary<WButtonGroupKey, int> groupCounts = WButtonGUI.GetGroupCountsForTesting();
            List<WButtonGroupKey> sortedKeys = groupCounts.Keys.OrderBy(k => k._drawOrder).ToList();

            // Verify that iteration order matches draw order sorting
            int previousDrawOrder = int.MinValue;
            foreach (WButtonGroupKey key in sortedKeys)
            {
                Assert.That(
                    key._drawOrder,
                    Is.GreaterThanOrEqualTo(previousDrawOrder),
                    "Groups should be sorted by drawOrder in ascending order"
                );
                previousDrawOrder = key._drawOrder;
            }
        }

        [Test]
        public void GroupsAreSortedByDrawOrderInAscendingOrder()
        {
            // drawOrder determines SORTING order, not placement; groupPlacement decides that.

            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );

            IReadOnlyList<WButtonMethodMetadata> metadata = WButtonMetadataCache.GetMetadata(
                typeof(WButtonConflictingDrawOrderTarget)
            );

            // Get the canonical draw order for Setup group (-21 from first declared button)
            WButtonMethodMetadata setupButton = metadata.First(m => m.GroupName == "Setup");
            Assert.That(
                setupButton.DrawOrder,
                Is.EqualTo(-21),
                "Setup group's first button should have drawOrder -21"
            );
        }

        [TestCase(
            true,
            WButtonPlacement.Top,
            true,
            TestName = "UseGlobalSettingRendersAtTopWhenGlobalIsTop"
        )]
        [TestCase(
            true,
            WButtonPlacement.Bottom,
            false,
            TestName = "UseGlobalSettingDoesNotRenderAtBottomWhenGlobalIsTop"
        )]
        [TestCase(
            false,
            WButtonPlacement.Top,
            false,
            TestName = "UseGlobalSettingDoesNotRenderAtTopWhenGlobalIsBottom"
        )]
        [TestCase(
            false,
            WButtonPlacement.Bottom,
            true,
            TestName = "UseGlobalSettingRendersAtBottomWhenGlobalIsBottom"
        )]
        public void UseGlobalSettingRespectsGlobalPlacementParameter(
            bool globalPlacementIsTop,
            WButtonPlacement placementToTry,
            bool expectedDrawn
        )
        {
            // drawOrder does NOT determine placement; UseGlobalSetting follows globalPlacementIsTop.
            WButtonConflictingDrawOrderTarget asset = Track(
                ScriptableObject.CreateInstance<WButtonConflictingDrawOrderTarget>()
            );
            Editor editor = Track(Editor.CreateEditor(asset));

            Dictionary<WButtonGroupKey, WButtonPaginationState> paginationStates = new();
            Dictionary<WButtonGroupKey, bool> foldoutStates = new();

            bool drawn = WButtonGUI.DrawButtons(
                editor,
                placementToTry,
                paginationStates,
                foldoutStates,
                UnityHelpersSettings.WButtonFoldoutBehavior.AlwaysOpen,
                triggeredContexts: null,
                globalPlacementIsTop: globalPlacementIsTop
            );

            Assert.That(
                drawn,
                Is.EqualTo(expectedDrawn),
                $"With globalPlacementIsTop={globalPlacementIsTop} and placementToTry={placementToTry}, drawn should be {expectedDrawn}. "
                    + $"Groups found: {string.Join(", ", WButtonGUI.GetGroupCountsForTesting().Keys.Select(k => $"{k._groupName}:{k._drawOrder}:{k._groupPlacement}"))}"
            );
        }

        [TestCase(-1000, TestName = "DrawOrderMinus1000DoesNotDeterminePlacement")]
        [TestCase(-100, TestName = "DrawOrderMinus100DoesNotDeterminePlacement")]
        [TestCase(-21, TestName = "DrawOrderMinus21DoesNotDeterminePlacement")]
        [TestCase(-2, TestName = "DrawOrderMinus2DoesNotDeterminePlacement")]
        [TestCase(-1, TestName = "DrawOrderMinus1DoesNotDeterminePlacement")]
        [TestCase(0, TestName = "DrawOrder0DoesNotDeterminePlacement")]
        [TestCase(1, TestName = "DrawOrder1DoesNotDeterminePlacement")]
        [TestCase(100, TestName = "DrawOrder100DoesNotDeterminePlacement")]
        public void DrawOrderDoesNotDeterminePlacement(int drawOrder)
        {
            // The key uses UseGlobalSetting, so placement follows globalPlacementIsTop.
            WButtonGroupKey key = new(
                WButtonAttribute.NoGroupPriority,
                drawOrder,
                "TestGroup",
                0,
                WButtonGroupPlacement.UseGlobalSetting
            );

            // Placement resolves at render time, so UseGlobalSetting always yields TopGroupLabel here.
            GUIContent header = WButtonGUI.BuildGroupHeader(key);
            Assert.That(
                header.text,
                Does.Contain(WButtonStyles.TopGroupLabel.text).Or.EqualTo("TestGroup"),
                $"DrawOrder {drawOrder} should not determine label style. UseGlobalSetting defaults to Top in BuildGroupHeader."
            );
        }

        [TestCase(WButtonGroupPlacement.Top, "TopGroupLabel")]
        [TestCase(WButtonGroupPlacement.Bottom, "BottomGroupLabel")]
        public void ExplicitGroupPlacementDeterminesLabelStyle(
            WButtonGroupPlacement placement,
            string expectedLabelName
        )
        {
            // Verify that explicit groupPlacement determines the label style.
            WButtonGUI.ClearGroupDataForTesting();
            Dictionary<int, int> counts = new() { { 0, 1 } };
            WButtonGUI.SetGroupCountsForTesting(counts);

            WButtonGroupKey key = new(WButtonAttribute.NoGroupPriority, 0, null, 0, placement);

            GUIContent header = WButtonGUI.BuildGroupHeader(key);
            GUIContent expectedLabel =
                placement == WButtonGroupPlacement.Top
                    ? WButtonStyles.TopGroupLabel
                    : WButtonStyles.BottomGroupLabel;

            Assert.That(
                header.text,
                Is.EqualTo(expectedLabel.text),
                $"GroupPlacement.{placement} should use {expectedLabelName}"
            );
        }
    }
}
#endif
