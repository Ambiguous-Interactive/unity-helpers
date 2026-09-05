// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Comprehensive tests for WGroup layout functionality.
    /// These tests use AutoIncludeMode.None to ensure only explicitly marked
    /// fields are included in groups, making assertions predictable.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WGroupLayoutTests : CommonTestBase
    {
        private UnityHelpersSettings.WGroupAutoIncludeConfiguration _previousConfiguration;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            WGroupLayoutBuilder.ClearCache();

            _previousConfiguration = UnityHelpersSettings.GetWGroupAutoIncludeConfiguration();
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0
            );
        }

        [TearDown]
        public override void TearDown()
        {
            WGroupLayoutBuilder.ClearCache();

            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                _previousConfiguration.Mode,
                _previousConfiguration.RowCount
            );
            base.TearDown();
        }

        /// <summary>
        /// Formats layout information for diagnostic output when tests fail.
        /// </summary>
        private static string FormatLayoutDiagnostics(WGroupLayout layout)
        {
            List<string> lines = new()
            {
                "=== Layout Diagnostics ===",
                $"Total Groups: {layout.Groups.Count}",
                $"Total Operations: {layout.Operations.Count}",
                $"Grouped Paths: [{string.Join(", ", layout.GroupedPaths)}]",
                "\n--- Groups ---",
            };

            for (int i = 0; i < layout.Groups.Count; i++)
            {
                WGroupDefinition group = layout.Groups[i];
                lines.Add(
                    $"  Group '{group.Name}' (DisplayName='{group.DisplayName}', DeclarationOrder={group.DeclarationOrder}):"
                );
                lines.Add($"    Anchor: {group.AnchorPropertyPath}");
                lines.Add($"    Properties: [{string.Join(", ", group.PropertyPaths)}]");
            }

            lines.Add("\n--- Operations ---");
            for (int i = 0; i < layout.Operations.Count; i++)
            {
                WGroupDrawOperation op = layout.Operations[i];
                if (op.Type == WGroupDrawOperationType.Group)
                {
                    lines.Add($"  [{i}] Group: {op.Group?.Name ?? "(null)"}");
                }
                else
                {
                    string hiddenMarker = op.IsHiddenInInspector ? " [HIDDEN]" : "";
                    lines.Add($"  [{i}] Property: {op.PropertyPath}{hiddenMarker}");
                }
            }

            lines.Add("\n--- Hidden Property Paths ---");
            lines.Add($"  [{string.Join(", ", layout.HiddenPropertyPaths)}]");

            return string.Join("\n", lines);
        }

        [Test]
        public void LayoutBuildsCorrectNumberOfGroups()
        {
            WGroupLayoutTestTarget target = CreateScriptableObject<WGroupLayoutTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.Groups,
                Has.Count.EqualTo(3),
                () =>
                    $"Expected 3 groups but found {layout.Groups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void GroupsPreserveDeclarationOrder()
        {
            WGroupDeclarationOrderTestTarget target =
                CreateScriptableObject<WGroupDeclarationOrderTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(layout.Groups, Has.Count.EqualTo(3));

            List<WGroupDefinition> sortedGroups = layout
                .Groups.OrderBy(g => g.DeclarationOrder)
                .ToList();
            Assert.That(sortedGroups[0].Name, Is.EqualTo("First"));
            Assert.That(sortedGroups[1].Name, Is.EqualTo("Second"));
            Assert.That(sortedGroups[2].Name, Is.EqualTo("Third"));
        }

        [Test]
        public void GroupsContainCorrectProperties()
        {
            WGroupLayoutTestTarget target = CreateScriptableObject<WGroupLayoutTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Group A", out WGroupDefinition groupA),
                Is.True,
                () => $"Group A should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupA.PropertyPaths,
                Has.Count.EqualTo(2),
                () =>
                    $"Group A should have 2 properties but has {groupA.PropertyPaths.Count}: [{string.Join(", ", groupA.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupA.PropertyPaths,
                Contains.Item(nameof(WGroupLayoutTestTarget.fieldA1))
            );
            Assert.That(
                groupA.PropertyPaths,
                Contains.Item(nameof(WGroupLayoutTestTarget.fieldA2))
            );

            Assert.That(
                layout.TryGetGroup("Group B", out WGroupDefinition groupB),
                Is.True,
                () => $"Group B should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupB.PropertyPaths,
                Has.Count.EqualTo(2),
                () =>
                    $"Group B should have 2 properties but has {groupB.PropertyPaths.Count}: [{string.Join(", ", groupB.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupB.PropertyPaths,
                Contains.Item(nameof(WGroupLayoutTestTarget.fieldB1))
            );
            Assert.That(
                groupB.PropertyPaths,
                Contains.Item(nameof(WGroupLayoutTestTarget.fieldB2))
            );

            Assert.That(
                layout.TryGetGroup("Group C", out WGroupDefinition groupC),
                Is.True,
                () => $"Group C should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupC.PropertyPaths,
                Has.Count.EqualTo(1),
                () =>
                    $"Group C should have 1 property but has {groupC.PropertyPaths.Count}: [{string.Join(", ", groupC.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupC.PropertyPaths,
                Contains.Item(nameof(WGroupLayoutTestTarget.fieldC1))
            );
        }

        [Test]
        public void DisplayNameIsResolvedCorrectly()
        {
            WGroupLayoutTestTarget target = CreateScriptableObject<WGroupLayoutTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Group A", out WGroupDefinition groupA),
                Is.True,
                () => $"Group A should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupA.DisplayName,
                Is.EqualTo("Alpha Group"),
                () =>
                    $"Group A display name expected 'Alpha Group' but was '{groupA.DisplayName}'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.TryGetGroup("Group B", out WGroupDefinition groupB),
                Is.True,
                () => $"Group B should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupB.DisplayName,
                Is.EqualTo("Beta Group"),
                () =>
                    $"Group B display name expected 'Beta Group' but was '{groupB.DisplayName}'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.TryGetGroup("Group C", out WGroupDefinition groupC),
                Is.True,
                () => $"Group C should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupC.DisplayName,
                Is.EqualTo("Group C"),
                () =>
                    $"Group C display name expected 'Group C' but was '{groupC.DisplayName}'.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Test cases for display name resolution behavior.
        /// Tests various scenarios where display names are set on different fields.
        /// </summary>
        private static IEnumerable<TestCaseData> DisplayNameResolutionTestCases()
        {
            yield return new TestCaseData("GroupA", "Custom Display A", 3).SetName(
                "DisplayName.FirstFieldHasCustomName.Preserved"
            );

            yield return new TestCaseData("GroupB", "Custom Display B", 3).SetName(
                "DisplayName.SecondFieldHasCustomName.Wins"
            );

            yield return new TestCaseData("GroupC", "Custom Display C", 3).SetName(
                "DisplayName.LastFieldHasCustomName.Wins"
            );

            yield return new TestCaseData("GroupD", "GroupD", 2).SetName(
                "DisplayName.NoExplicitName.UsesGroupName"
            );

            yield return new TestCaseData("GroupE", "Second Display E", 2).SetName(
                "DisplayName.ConflictingNames.LastExplicitWins"
            );
        }

        [Test]
        [TestCaseSource(nameof(DisplayNameResolutionTestCases))]
        public void DisplayNameResolutionFollowsExpectedBehavior(
            string groupName,
            string expectedDisplayName,
            int expectedPropertyCount
        )
        {
            WGroupDisplayNameTestTarget target =
                CreateScriptableObject<WGroupDisplayNameTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup(groupName, out WGroupDefinition group),
                Is.True,
                () => $"{groupName} should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.DisplayName,
                Is.EqualTo(expectedDisplayName),
                () =>
                    $"{groupName} display name expected '{expectedDisplayName}' but was '{group.DisplayName}'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(expectedPropertyCount),
                () =>
                    $"{groupName} expected {expectedPropertyCount} properties but has {group.PropertyPaths.Count}: [{string.Join(", ", group.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void GroupNameLookupIsCaseInsensitive()
        {
            WGroupLayoutTestTarget target = CreateScriptableObject<WGroupLayoutTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(layout.TryGetGroup("group a", out WGroupDefinition lower), Is.True);
            Assert.That(layout.TryGetGroup("GROUP A", out WGroupDefinition upper), Is.True);
            Assert.That(layout.TryGetGroup("Group A", out WGroupDefinition mixed), Is.True);

            Assert.That(lower.Name, Is.EqualTo(upper.Name));
            Assert.That(upper.Name, Is.EqualTo(mixed.Name));
        }

        [Test]
        public void UngroupedFieldNotInAnyGroup()
        {
            WGroupLayoutTestTarget target = CreateScriptableObject<WGroupLayoutTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.GroupedPaths,
                Does.Not.Contain(nameof(WGroupLayoutTestTarget.ungroupedField)),
                () =>
                    $"ungroupedField should not be in any group but was found in GroupedPaths.\n{FormatLayoutDiagnostics(layout)}"
            );

            bool foundInOperations = layout.Operations.Any(op =>
                op.Type == WGroupDrawOperationType.Property
                && op.PropertyPath == nameof(WGroupLayoutTestTarget.ungroupedField)
            );
            Assert.That(
                foundInOperations,
                Is.True,
                () =>
                    $"Ungrouped field should appear in operations as a Property type.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void AnchorPropertyPathIsFirstPropertyInGroup()
        {
            WGroupDeclarationOrderTestTarget target =
                CreateScriptableObject<WGroupDeclarationOrderTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(layout.TryGetGroup("First", out WGroupDefinition first), Is.True);
            Assert.That(
                first.AnchorPropertyPath,
                Is.EqualTo(nameof(WGroupDeclarationOrderTestTarget.first1))
            );

            Assert.That(layout.TryGetGroup("Second", out WGroupDefinition second), Is.True);
            Assert.That(
                second.AnchorPropertyPath,
                Is.EqualTo(nameof(WGroupDeclarationOrderTestTarget.second1))
            );
        }

        [Test]
        public void LayoutCachingWorks()
        {
            WGroupLayoutTestTarget target = CreateScriptableObject<WGroupLayoutTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout1 = WGroupLayoutBuilder.Build(serializedObject, "m_Script");
            WGroupLayout layout2 = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(layout1, Is.SameAs(layout2));
        }

        [Test]
        public void ClearCacheInvalidatesCache()
        {
            WGroupLayoutTestTarget target = CreateScriptableObject<WGroupLayoutTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout1 = WGroupLayoutBuilder.Build(serializedObject, "m_Script");
            WGroupLayoutBuilder.ClearCache();
            WGroupLayout layout2 = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(layout1, Is.Not.SameAs(layout2));
        }

        [Test]
        public void OperationsInCorrectOrder()
        {
            WGroupLayoutTestTarget target = CreateScriptableObject<WGroupLayoutTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.Operations,
                Is.Not.Empty,
                () => $"Operations should not be empty.\n{FormatLayoutDiagnostics(layout)}"
            );

            int groupOperations = layout.Operations.Count(op =>
                op.Type == WGroupDrawOperationType.Group
            );
            Assert.That(
                groupOperations,
                Is.EqualTo(3),
                () =>
                    $"Should have 3 group operations but found {groupOperations}.\n{FormatLayoutDiagnostics(layout)}"
            );

            int propertyOperations = layout.Operations.Count(op =>
                op.Type == WGroupDrawOperationType.Property
            );
            Assert.That(
                propertyOperations,
                Is.GreaterThanOrEqualTo(1),
                () =>
                    $"Should have at least 1 property operation but found {propertyOperations}.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void MultiplePropertiesWithSameGroupNameAreMerged()
        {
            WGroupDeclarationOrderTestTarget target =
                CreateScriptableObject<WGroupDeclarationOrderTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("First", out WGroupDefinition first),
                Is.True,
                () => $"First group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                first.PropertyPaths,
                Has.Count.EqualTo(2),
                () =>
                    $"First group should have 2 properties but has {first.PropertyPaths.Count}: [{string.Join(", ", first.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                first.PropertyPaths,
                Contains.Item(nameof(WGroupDeclarationOrderTestTarget.first1))
            );
            Assert.That(
                first.PropertyPaths,
                Contains.Item(nameof(WGroupDeclarationOrderTestTarget.first2))
            );

            Assert.That(
                layout.TryGetGroup("Second", out WGroupDefinition second),
                Is.True,
                () => $"Second group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                second.PropertyPaths,
                Has.Count.EqualTo(2),
                () =>
                    $"Second group should have 2 properties but has {second.PropertyPaths.Count}: [{string.Join(", ", second.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                second.PropertyPaths,
                Contains.Item(nameof(WGroupDeclarationOrderTestTarget.second1))
            );
            Assert.That(
                second.PropertyPaths,
                Contains.Item(nameof(WGroupDeclarationOrderTestTarget.second2))
            );
        }

        /// <summary>
        /// Test cases for auto-include mode behavior using WGroupAutoIncludeTestTarget.
        /// The target has: [WGroup("Auto Group")] autoGroupFirst (uses UseGlobalAutoInclude),
        /// then autoIncluded1, autoIncluded2, notAutoIncluded (no attributes).
        ///
        /// IMPORTANT: The attribute uses default autoIncludeCount (UseGlobalAutoInclude = -2)
        /// which means the global WGroupAutoIncludeConfiguration controls how many
        /// subsequent fields are captured. If an attribute explicitly specifies
        /// autoIncludeCount (e.g., autoIncludeCount: 2), that value would override the
        /// global setting entirely.
        ///
        /// Note: WGroupLayoutTestTarget is NOT suitable for these tests because it has
        /// explicit [WGroup] attributes on most fields. Auto-include only captures
        /// fields that don't have explicit group assignments.
        ///
        /// Each case specifies: mode, row count, expected property count for "Auto Group",
        /// and whether notAutoIncluded should be in any group.
        /// </summary>
        private static IEnumerable<TestCaseData> AutoIncludeModeTestCases()
        {
            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0,
                1,
                false
            ).SetName("AutoInclude.None.OnlyExplicitFields");

            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.Infinite,
                0,
                4,
                true
            ).SetName("AutoInclude.Infinite.CapturesAllSubsequent");

            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                1,
                2,
                false
            ).SetName("AutoInclude.Finite1.CapturesOneExtra");

            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                2,
                3,
                false
            ).SetName("AutoInclude.Finite2.CapturesTwoExtra");

            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                3,
                4,
                true
            ).SetName("AutoInclude.Finite3.CapturesThreeExtra");
        }

        [Test]
        [TestCaseSource(nameof(AutoIncludeModeTestCases))]
        public void AutoIncludeModeAffectsGroupCapture(
            UnityHelpersSettings.WGroupAutoIncludeMode mode,
            int rowCount,
            int expectedGroupPropertyCount,
            bool expectNotAutoIncludedInGroup
        )
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(mode, rowCount);
            WGroupLayoutBuilder.ClearCache();

            WGroupAutoIncludeTestTarget target =
                CreateScriptableObject<WGroupAutoIncludeTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Auto Group", out WGroupDefinition autoGroup),
                Is.True,
                () => $"Auto Group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                autoGroup.PropertyPaths,
                Has.Count.EqualTo(expectedGroupPropertyCount),
                () =>
                    $"Mode={mode}, RowCount={rowCount}: Auto Group expected {expectedGroupPropertyCount} properties but has {autoGroup.PropertyPaths.Count}: [{string.Join(", ", autoGroup.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );

            bool notAutoIncludedInGroup = layout.GroupedPaths.Contains(
                nameof(WGroupAutoIncludeTestTarget.notAutoIncluded)
            );
            Assert.That(
                notAutoIncludedInGroup,
                Is.EqualTo(expectNotAutoIncludedInGroup),
                () =>
                    $"Mode={mode}, RowCount={rowCount}: notAutoIncluded expected in groups: {expectNotAutoIncludedInGroup}, actual: {notAutoIncludedInGroup}.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Test cases for auto-include behavior using WGroupLayoutTestTarget.
        /// This target has multiple groups with explicit [WGroup] attributes.
        /// Auto-include only affects the single unattributed field: ungroupedField.
        ///
        /// Field layout:
        /// - fieldA1, fieldA2: explicit Group A
        /// - fieldB1, fieldB2: explicit Group B
        /// - ungroupedField: NO attribute (can be auto-included)
        /// - fieldC1: explicit Group C
        ///
        /// In infinite mode, the last active group (Group B) should capture ungroupedField.
        /// </summary>
        private static IEnumerable<TestCaseData> MultiGroupAutoIncludeTestCases()
        {
            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0,
                false,
                2,
                "Group B"
            ).SetName("MultiGroup.None.UngroupedStaysUngrouped");

            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.Infinite,
                0,
                true,
                3,
                "Group B"
            ).SetName("MultiGroup.Infinite.UngroupedCapturedByLastActiveGroup");

            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                1,
                true,
                3,
                "Group B"
            ).SetName("MultiGroup.Finite1.UngroupedCapturedByGroupB");
        }

        [Test]
        [TestCaseSource(nameof(MultiGroupAutoIncludeTestCases))]
        public void AutoIncludeModeWithMultipleGroups(
            UnityHelpersSettings.WGroupAutoIncludeMode mode,
            int rowCount,
            bool expectUngroupedInAnyGroup,
            int expectedGroupBPropertyCount,
            string expectedCapturingGroup
        )
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(mode, rowCount);
            WGroupLayoutBuilder.ClearCache();

            WGroupLayoutTestTarget target = CreateScriptableObject<WGroupLayoutTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            bool ungroupedFieldInGroups = layout.GroupedPaths.Contains(
                nameof(WGroupLayoutTestTarget.ungroupedField)
            );
            Assert.That(
                ungroupedFieldInGroups,
                Is.EqualTo(expectUngroupedInAnyGroup),
                () =>
                    $"Mode={mode}, RowCount={rowCount}: ungroupedField expected in groups: {expectUngroupedInAnyGroup}, actual: {ungroupedFieldInGroups}.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.TryGetGroup(expectedCapturingGroup, out WGroupDefinition group),
                Is.True,
                () => $"{expectedCapturingGroup} should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(expectedGroupBPropertyCount),
                () =>
                    $"Mode={mode}, RowCount={rowCount}: {expectedCapturingGroup} expected {expectedGroupBPropertyCount} properties but has {group.PropertyPaths.Count}: [{string.Join(", ", group.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );

            if (expectUngroupedInAnyGroup)
            {
                Assert.That(
                    group.PropertyPaths,
                    Contains.Item(nameof(WGroupLayoutTestTarget.ungroupedField)),
                    () =>
                        $"Mode={mode}: ungroupedField expected in {expectedCapturingGroup}.\n{FormatLayoutDiagnostics(layout)}"
                );
            }
        }

        /// <summary>
        /// Test cases for explicit autoIncludeCount on the attribute.
        /// When an attribute specifies an explicit count, it should override global settings entirely.
        ///
        /// Each case specifies: global mode, global row count, expected property count for the group.
        /// The explicit count is always 2, so regardless of global settings, exactly 2 additional
        /// fields should be captured.
        /// </summary>
        private static IEnumerable<TestCaseData> ExplicitAutoIncludeCountTestCases()
        {
            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0,
                3,
                new[]
                {
                    nameof(WGroupExplicitAutoIncludeTestTarget.explicitGroupFirst),
                    nameof(WGroupExplicitAutoIncludeTestTarget.captured1),
                    nameof(WGroupExplicitAutoIncludeTestTarget.captured2),
                }
            ).SetName("ExplicitCount.OverridesGlobalNone");

            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.Infinite,
                0,
                3,
                new[]
                {
                    nameof(WGroupExplicitAutoIncludeTestTarget.explicitGroupFirst),
                    nameof(WGroupExplicitAutoIncludeTestTarget.captured1),
                    nameof(WGroupExplicitAutoIncludeTestTarget.captured2),
                }
            ).SetName("ExplicitCount.OverridesGlobalInfinite");

            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                5,
                3,
                new[]
                {
                    nameof(WGroupExplicitAutoIncludeTestTarget.explicitGroupFirst),
                    nameof(WGroupExplicitAutoIncludeTestTarget.captured1),
                    nameof(WGroupExplicitAutoIncludeTestTarget.captured2),
                }
            ).SetName("ExplicitCount.OverridesGlobalFiniteHigher");

            yield return new TestCaseData(
                UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                1,
                3,
                new[]
                {
                    nameof(WGroupExplicitAutoIncludeTestTarget.explicitGroupFirst),
                    nameof(WGroupExplicitAutoIncludeTestTarget.captured1),
                    nameof(WGroupExplicitAutoIncludeTestTarget.captured2),
                }
            ).SetName("ExplicitCount.OverridesGlobalFiniteLower");
        }

        [Test]
        [TestCaseSource(nameof(ExplicitAutoIncludeCountTestCases))]
        public void ExplicitAutoIncludeCountOverridesGlobalSettings(
            UnityHelpersSettings.WGroupAutoIncludeMode mode,
            int rowCount,
            int expectedGroupPropertyCount,
            string[] expectedProperties
        )
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(mode, rowCount);
            WGroupLayoutBuilder.ClearCache();

            WGroupExplicitAutoIncludeTestTarget target =
                CreateScriptableObject<WGroupExplicitAutoIncludeTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Explicit Group", out WGroupDefinition group),
                Is.True,
                () => $"Explicit Group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(expectedGroupPropertyCount),
                () =>
                    $"Mode={mode}, RowCount={rowCount}: Explicit Group expected {expectedGroupPropertyCount} properties but has {group.PropertyPaths.Count}: [{string.Join(", ", group.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );

            foreach (string expectedProperty in expectedProperties)
            {
                Assert.That(
                    group.PropertyPaths,
                    Contains.Item(expectedProperty),
                    () =>
                        $"Explicit Group should contain '{expectedProperty}'.\n{FormatLayoutDiagnostics(layout)}"
                );
            }

            Assert.That(
                layout.GroupedPaths.Contains(
                    nameof(WGroupExplicitAutoIncludeTestTarget.notCaptured)
                ),
                Is.False,
                () =>
                    $"notCaptured should NOT be in any group with explicit count=2.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Tests that explicit InfiniteAutoInclude (-1) on an attribute captures all subsequent fields.
        /// </summary>
        [Test]
        public void ExplicitInfiniteAutoIncludeCapturesAllSubsequent()
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0
            );
            WGroupLayoutBuilder.ClearCache();

            WGroupInfiniteAutoIncludeTestTarget target =
                CreateScriptableObject<WGroupInfiniteAutoIncludeTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Infinite Group", out WGroupDefinition group),
                Is.True,
                () => $"Infinite Group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(4),
                () =>
                    $"Infinite Group should have all 4 fields but has {group.PropertyPaths.Count}: [{string.Join(", ", group.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );

            string[] expectedProperties =
            {
                nameof(WGroupInfiniteAutoIncludeTestTarget.infiniteGroupFirst),
                nameof(WGroupInfiniteAutoIncludeTestTarget.capturedA),
                nameof(WGroupInfiniteAutoIncludeTestTarget.capturedB),
                nameof(WGroupInfiniteAutoIncludeTestTarget.capturedC),
            };
            foreach (string expectedProperty in expectedProperties)
            {
                Assert.That(
                    group.PropertyPaths,
                    Contains.Item(expectedProperty),
                    () =>
                        $"Infinite Group should contain '{expectedProperty}'.\n{FormatLayoutDiagnostics(layout)}"
                );
            }
        }

        /// <summary>
        /// Tests that explicit autoIncludeCount: 0 captures no subsequent fields.
        /// </summary>
        [Test]
        public void ExplicitZeroAutoIncludeCapturesNoSubsequent()
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.Infinite,
                0
            );
            WGroupLayoutBuilder.ClearCache();

            WGroupZeroAutoIncludeTestTarget target =
                CreateScriptableObject<WGroupZeroAutoIncludeTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Zero Group", out WGroupDefinition group),
                Is.True,
                () => $"Zero Group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(1),
                () =>
                    $"Zero Group should have only 1 field but has {group.PropertyPaths.Count}: [{string.Join(", ", group.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Contains.Item(nameof(WGroupZeroAutoIncludeTestTarget.zeroGroupFirst)),
                () =>
                    $"Zero Group should contain 'zeroGroupFirst'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.GroupedPaths.Contains(nameof(WGroupZeroAutoIncludeTestTarget.notCaptured1)),
                Is.False,
                () => $"notCaptured1 should NOT be in any group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.GroupedPaths.Contains(nameof(WGroupZeroAutoIncludeTestTarget.notCaptured2)),
                Is.False,
                () => $"notCaptured2 should NOT be in any group.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Tests that Finite mode with rowCount 0 behaves like None mode.
        /// </summary>
        [Test]
        public void FiniteModeWithZeroRowCountBehavesLikeNone()
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                0
            );
            WGroupLayoutBuilder.ClearCache();

            WGroupAutoIncludeTestTarget target =
                CreateScriptableObject<WGroupAutoIncludeTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Auto Group", out WGroupDefinition group),
                Is.True,
                () => $"Auto Group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(1),
                () =>
                    $"Finite(0) should behave like None - only 1 field expected but has {group.PropertyPaths.Count}: [{string.Join(", ", group.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Tests that [HideInInspector] fields are excluded from auto-include processing.
        /// Hidden fields should not be automatically added to groups.
        /// </summary>
        [Test]
        public void HideInInspectorFieldsExcludedFromAutoInclude()
        {
            // Finite mode with enough budget to capture every field, were HideInInspector not respected.
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.Finite,
                6
            );
            WGroupLayoutBuilder.ClearCache();

            WGroupHideInInspectorTestTarget target =
                CreateScriptableObject<WGroupHideInInspectorTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Test Group", out WGroupDefinition group),
                Is.True,
                () => $"Test Group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths.Contains(nameof(WGroupHideInInspectorTestTarget._hiddenField1)),
                Is.False,
                () =>
                    $"{nameof(WGroupHideInInspectorTestTarget._hiddenField1)} should NOT be auto-included.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                group.PropertyPaths.Contains(nameof(WGroupHideInInspectorTestTarget._hiddenField2)),
                Is.False,
                () =>
                    $"{nameof(WGroupHideInInspectorTestTarget._hiddenField2)} should NOT be auto-included.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths.Contains(nameof(WGroupHideInInspectorTestTarget.groupAnchor)),
                Is.True,
                () => $"groupAnchor should be in the group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                group.PropertyPaths.Contains(nameof(WGroupHideInInspectorTestTarget.visibleField1)),
                Is.True,
                () => $"visibleField1 should be auto-included.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                group.PropertyPaths.Contains(nameof(WGroupHideInInspectorTestTarget.visibleField2)),
                Is.True,
                () => $"visibleField2 should be auto-included.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                group.PropertyPaths.Contains(nameof(WGroupHideInInspectorTestTarget.visibleField3)),
                Is.True,
                () => $"visibleField3 should be auto-included.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                group.PropertyPaths.Contains(nameof(WGroupHideInInspectorTestTarget.visibleField4)),
                Is.True,
                () => $"visibleField4 should be auto-included.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Tests that explicitly grouped [HideInInspector] fields are still included.
        /// The [WGroup] attribute should override the auto-include exclusion.
        /// </summary>
        [Test]
        public void ExplicitlyGroupedHiddenFieldsAreIncluded()
        {
            // None mode, so auto-include cannot interfere.
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0
            );
            WGroupLayoutBuilder.ClearCache();

            WGroupExplicitHiddenFieldTestTarget target =
                CreateScriptableObject<WGroupExplicitHiddenFieldTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Explicit Group", out WGroupDefinition group),
                Is.True,
                () => $"Explicit Group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths.Contains(
                    nameof(WGroupExplicitHiddenFieldTestTarget._explicitlyGroupedHiddenField)
                ),
                Is.True,
                () =>
                    $"{nameof(WGroupExplicitHiddenFieldTestTarget._explicitlyGroupedHiddenField)} should be explicitly included.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths.Contains(
                    nameof(WGroupExplicitHiddenFieldTestTarget.groupAnchor)
                ),
                Is.True,
                () => $"groupAnchor should be in the group.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths.Contains(
                    nameof(WGroupExplicitHiddenFieldTestTarget.visibleField)
                ),
                Is.False,
                () =>
                    $"visibleField should NOT be in the group (no auto-include).\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Tests that [HideInInspector] fields are excluded from infinite auto-include mode.
        /// </summary>
        [Test]
        public void HideInInspectorExcludedInInfiniteMode()
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.Infinite,
                0
            );
            WGroupLayoutBuilder.ClearCache();

            WGroupHideInInspectorInfiniteTestTarget target =
                CreateScriptableObject<WGroupHideInInspectorInfiniteTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Infinite Group", out WGroupDefinition group),
                Is.True,
                () => $"Infinite Group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths.Contains(
                    nameof(WGroupHideInInspectorInfiniteTestTarget._hiddenField)
                ),
                Is.False,
                () =>
                    $"{nameof(WGroupHideInInspectorInfiniteTestTarget._hiddenField)} should NOT be auto-included even in infinite mode.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths.Contains(
                    nameof(WGroupHideInInspectorInfiniteTestTarget.groupAnchor)
                ),
                Is.True,
                () => $"groupAnchor should be in the group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                group.PropertyPaths.Contains(
                    nameof(WGroupHideInInspectorInfiniteTestTarget.visibleField1)
                ),
                Is.True,
                () => $"visibleField1 should be auto-included.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                group.PropertyPaths.Contains(
                    nameof(WGroupHideInInspectorInfiniteTestTarget.visibleField2)
                ),
                Is.True,
                () => $"visibleField2 should be auto-included.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Tests that ungrouped [HideInInspector] fields are tracked in HiddenPropertyPaths.
        /// </summary>
        [Test]
        public void UngroupedHiddenFieldsInHiddenPropertyPaths()
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0
            );
            WGroupLayoutBuilder.ClearCache();

            WGroupUngroupedHiddenFieldTestTarget target =
                CreateScriptableObject<WGroupUngroupedHiddenFieldTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.HiddenPropertyPaths.Contains("_ungroupedHiddenField1"),
                Is.True,
                () =>
                    $"_ungroupedHiddenField1 should be in HiddenPropertyPaths.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.HiddenPropertyPaths.Contains("_ungroupedHiddenField2"),
                Is.True,
                () =>
                    $"_ungroupedHiddenField2 should be in HiddenPropertyPaths.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.HiddenPropertyPaths.Contains(
                    nameof(WGroupUngroupedHiddenFieldTestTarget.visibleField1)
                ),
                Is.False,
                () =>
                    $"visibleField1 should NOT be in HiddenPropertyPaths.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.HiddenPropertyPaths.Contains(
                    nameof(WGroupUngroupedHiddenFieldTestTarget.visibleField2)
                ),
                Is.False,
                () =>
                    $"visibleField2 should NOT be in HiddenPropertyPaths.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Tests that WGroupDrawOperation.IsHiddenInInspector is set correctly for property operations.
        /// </summary>
        [Test]
        public void PropertyOperationIsHiddenInInspectorFlagSetCorrectly()
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0
            );
            WGroupLayoutBuilder.ClearCache();

            WGroupUngroupedHiddenFieldTestTarget target =
                CreateScriptableObject<WGroupUngroupedHiddenFieldTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            bool foundHiddenField1 = false;
            bool foundHiddenField2 = false;
            bool foundVisibleField1 = false;
            bool foundVisibleField2 = false;

            for (int i = 0; i < layout.Operations.Count; i++)
            {
                WGroupDrawOperation op = layout.Operations[i];
                if (op.Type != WGroupDrawOperationType.Property)
                {
                    continue;
                }

                if (string.Equals(op.PropertyPath, "_ungroupedHiddenField1"))
                {
                    foundHiddenField1 = true;
                    Assert.That(
                        op.IsHiddenInInspector,
                        Is.True,
                        () =>
                            $"_ungroupedHiddenField1 operation should have IsHiddenInInspector=true.\n{FormatLayoutDiagnostics(layout)}"
                    );
                }
                else if (string.Equals(op.PropertyPath, "_ungroupedHiddenField2"))
                {
                    foundHiddenField2 = true;
                    Assert.That(
                        op.IsHiddenInInspector,
                        Is.True,
                        () =>
                            $"_ungroupedHiddenField2 operation should have IsHiddenInInspector=true.\n{FormatLayoutDiagnostics(layout)}"
                    );
                }
                else if (
                    string.Equals(
                        op.PropertyPath,
                        nameof(WGroupUngroupedHiddenFieldTestTarget.visibleField1)
                    )
                )
                {
                    foundVisibleField1 = true;
                    Assert.That(
                        op.IsHiddenInInspector,
                        Is.False,
                        () =>
                            $"visibleField1 operation should have IsHiddenInInspector=false.\n{FormatLayoutDiagnostics(layout)}"
                    );
                }
                else if (
                    string.Equals(
                        op.PropertyPath,
                        nameof(WGroupUngroupedHiddenFieldTestTarget.visibleField2)
                    )
                )
                {
                    foundVisibleField2 = true;
                    Assert.That(
                        op.IsHiddenInInspector,
                        Is.False,
                        () =>
                            $"visibleField2 operation should have IsHiddenInInspector=false.\n{FormatLayoutDiagnostics(layout)}"
                    );
                }
            }

            Assert.That(
                foundHiddenField1,
                Is.True,
                () =>
                    $"Should have found _ungroupedHiddenField1 operation.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                foundHiddenField2,
                Is.True,
                () =>
                    $"Should have found _ungroupedHiddenField2 operation.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                foundVisibleField1,
                Is.True,
                () =>
                    $"Should have found visibleField1 operation.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                foundVisibleField2,
                Is.True,
                () =>
                    $"Should have found visibleField2 operation.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        /// <summary>
        /// Tests that group operations always have IsHiddenInInspector=false.
        /// </summary>
        [Test]
        public void GroupOperationIsHiddenInInspectorAlwaysFalse()
        {
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0
            );
            WGroupLayoutBuilder.ClearCache();

            WGroupUngroupedHiddenFieldTestTarget target =
                CreateScriptableObject<WGroupUngroupedHiddenFieldTestTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            for (int i = 0; i < layout.Operations.Count; i++)
            {
                WGroupDrawOperation op = layout.Operations[i];
                if (op.Type == WGroupDrawOperationType.Group)
                {
                    Assert.That(
                        op.IsHiddenInInspector,
                        Is.False,
                        () =>
                            $"Group operation '{op.Group?.Name}' should have IsHiddenInInspector=false.\n{FormatLayoutDiagnostics(layout)}"
                    );
                }
            }
        }

        /// <summary>
        /// Membership cases for reading-order auto-include (#455). A null expected group name
        /// means the member must not belong to any group.
        /// </summary>
        private static IEnumerable<TestCaseData> ReadingOrderMembershipTestCases()
        {
            yield return new TestCaseData(
                typeof(WGroupReopenedGroupTestTarget),
                nameof(WGroupReopenedGroupTestTarget.alphaAuto),
                "Alpha"
            ).SetName("ReadingOrder.FirstGroupCapturesBeforeSecondOpens");

            yield return new TestCaseData(
                typeof(WGroupReopenedGroupTestTarget),
                nameof(WGroupReopenedGroupTestTarget.betaAuto),
                "Beta"
            ).SetName("ReadingOrder.SecondGroupCaptures");

            yield return new TestCaseData(
                typeof(WGroupReopenedGroupTestTarget),
                nameof(WGroupReopenedGroupTestTarget.alphaAfterReopen),
                "Alpha"
            ).SetName("ReadingOrder.ReopenedGroupRetargetsAutoInclude");

            yield return new TestCaseData(
                typeof(WGroupBareEndTestTarget),
                nameof(WGroupBareEndTestTarget.alphaClosing),
                "Alpha"
            ).SetName("BareEnd.TerminatingMemberJoinsItsOwnGroup");

            yield return new TestCaseData(
                typeof(WGroupBareEndTestTarget),
                nameof(WGroupBareEndTestTarget.ungrouped),
                null
            ).SetName("BareEnd.ClosesEveryActiveGroup");
        }

        [Test]
        [TestCaseSource(nameof(ReadingOrderMembershipTestCases))]
        public void AutoIncludeFollowsReadingOrder(
            Type targetType,
            string propertyPath,
            string expectedGroupName
        )
        {
            ScriptableObject target = CreateScriptableObject(targetType);
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            List<string> owners = layout
                .Groups.Where(group => group.PropertyPaths.Contains(propertyPath))
                .Select(group => group.Name)
                .ToList();

            if (string.IsNullOrEmpty(expectedGroupName))
            {
                Assert.That(
                    owners,
                    Is.Empty,
                    () =>
                        $"'{propertyPath}' should belong to no group but belongs to [{string.Join(", ", owners)}].\n{FormatLayoutDiagnostics(layout)}"
                );
                return;
            }

            Assert.That(
                owners,
                Is.EqualTo(new[] { expectedGroupName }),
                () =>
                    $"'{propertyPath}' should belong only to '{expectedGroupName}' but belongs to [{string.Join(", ", owners)}].\n{FormatLayoutDiagnostics(layout)}"
            );
        }
    }
}
#endif
