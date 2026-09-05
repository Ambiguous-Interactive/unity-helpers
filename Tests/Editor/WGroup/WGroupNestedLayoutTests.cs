// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Comprehensive tests for nested WGroup layout functionality.
    /// Tests parent-child relationships, sibling ordering, circular references,
    /// orphan children, and proper operation ordering.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WGroupNestedLayoutTests : CommonTestBase
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
        /// Helper method to build a layout from a serialized object.
        /// </summary>
        private static WGroupLayout BuildLayout(SerializedObject serializedObject)
        {
            return WGroupLayoutBuilder.Build(serializedObject, "m_Script");
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
                lines.Add($"    ParentGroupName: {group.ParentGroupName ?? "(none)"}");
                lines.Add($"    HasParent: {group.HasParent}");
                lines.Add(
                    $"    ChildGroups: [{string.Join(", ", group.ChildGroups.Select(c => c.Name))}]"
                );
                lines.Add($"    PropertyPaths: [{string.Join(", ", group.PropertyPaths)}]");
                lines.Add(
                    $"    DirectPropertyPaths: [{string.Join(", ", group.DirectPropertyPaths)}]"
                );
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
                    lines.Add($"  [{i}] Property: {op.PropertyPath}");
                }
            }

            return string.Join("\n", lines);
        }

        [Test]
        public void TwoLevelNestedGroupHasCorrectParentChild()
        {
            TwoLevelNestedTarget target = CreateScriptableObject<TwoLevelNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("inner", out WGroupDefinition innerGroup),
                Is.True,
                () => $"inner group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                innerGroup.HasParent,
                Is.True,
                () => $"inner group should have a parent.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                innerGroup.ParentGroupName,
                Is.EqualTo("outer").IgnoreCase,
                () => $"inner group parent should be 'outer'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.TryGetGroup("outer", out WGroupDefinition outerGroup),
                Is.True,
                () => $"outer group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                outerGroup.ChildGroups,
                Has.Count.EqualTo(1),
                () =>
                    $"outer group should have 1 child but has {outerGroup.ChildGroups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                outerGroup.ChildGroups[0].Name,
                Is.EqualTo("inner").IgnoreCase,
                () => $"outer group's child should be 'inner'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                outerGroup.HasParent,
                Is.False,
                () => $"outer group should not have a parent.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void ThreeLevelNestedGroupBuildsCorrectHierarchy()
        {
            ThreeLevelNestedTarget target = CreateScriptableObject<ThreeLevelNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("level1", out WGroupDefinition level1),
                Is.True,
                () => $"level1 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("level2", out WGroupDefinition level2),
                Is.True,
                () => $"level2 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("level3", out WGroupDefinition level3),
                Is.True,
                () => $"level3 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                level3.HasParent,
                Is.True,
                () => $"level3 should have a parent.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                level3.ParentGroupName,
                Is.EqualTo("level2").IgnoreCase,
                () => $"level3 parent should be 'level2'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                level2.HasParent,
                Is.True,
                () => $"level2 should have a parent.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                level2.ParentGroupName,
                Is.EqualTo("level1").IgnoreCase,
                () => $"level2 parent should be 'level1'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                level1.HasParent,
                Is.False,
                () => $"level1 should not have a parent.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                level1.ChildGroups,
                Has.Count.EqualTo(1),
                () =>
                    $"level1 should have 1 child but has {level1.ChildGroups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                level1.ChildGroups[0].Name,
                Is.EqualTo("level2").IgnoreCase,
                () => $"level1's child should be 'level2'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                level2.ChildGroups,
                Has.Count.EqualTo(1),
                () =>
                    $"level2 should have 1 child but has {level2.ChildGroups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                level2.ChildGroups[0].Name,
                Is.EqualTo("level3").IgnoreCase,
                () => $"level2's child should be 'level3'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                level3.ChildGroups,
                Is.Empty,
                () =>
                    $"level3 should have no children but has {level3.ChildGroups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void SiblingChildrenAreOrderedByDeclaration()
        {
            SiblingNestedTarget target = CreateScriptableObject<SiblingNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("parent", out WGroupDefinition parentGroup),
                Is.True,
                () => $"parent group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                parentGroup.ChildGroups,
                Has.Count.EqualTo(2),
                () =>
                    $"parent should have 2 children but has {parentGroup.ChildGroups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                parentGroup.ChildGroups[0].Name,
                Is.EqualTo("child1").IgnoreCase,
                () =>
                    $"First child should be 'child1' but was '{parentGroup.ChildGroups[0].Name}'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                parentGroup.ChildGroups[1].Name,
                Is.EqualTo("child2").IgnoreCase,
                () =>
                    $"Second child should be 'child2' but was '{parentGroup.ChildGroups[1].Name}'.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void CircularReferenceGroupsTreatedAsTopLevel()
        {
            CircularReferenceTarget target = CreateScriptableObject<CircularReferenceTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("groupA", out WGroupDefinition groupA),
                Is.True,
                () => $"groupA should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("groupB", out WGroupDefinition groupB),
                Is.True,
                () => $"groupB should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                groupA.ChildGroups,
                Is.Empty,
                () =>
                    $"groupA should have no children due to circular ref but has {groupA.ChildGroups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupB.ChildGroups,
                Is.Empty,
                () =>
                    $"groupB should have no children due to circular ref but has {groupB.ChildGroups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.Groups,
                Has.Count.EqualTo(2),
                () => $"Should have exactly 2 groups in layout.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void OrphanChildTreatedAsTopLevel()
        {
            OrphanChildTarget target = CreateScriptableObject<OrphanChildTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("child", out WGroupDefinition childGroup),
                Is.True,
                () => $"child group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                childGroup.HasParent,
                Is.True,
                () =>
                    $"child should have HasParent=true because ParentGroupName is set.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                childGroup.ParentGroupName,
                Is.EqualTo("nonExistent").IgnoreCase,
                () =>
                    $"child ParentGroupName should be 'nonExistent'.\n{FormatLayoutDiagnostics(layout)}"
            );

            int groupOperationsCount = layout.Operations.Count(op =>
                op.Type == WGroupDrawOperationType.Group
            );
            Assert.That(
                groupOperationsCount,
                Is.EqualTo(1),
                () =>
                    $"Should have exactly 1 group operation (the orphan child).\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void NestedGroupExcludedFromTopLevelOperations()
        {
            TwoLevelNestedTarget target = CreateScriptableObject<TwoLevelNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            List<WGroupDrawOperation> groupOperations = layout
                .Operations.Where(op => op.Type == WGroupDrawOperationType.Group)
                .ToList();

            Assert.That(
                groupOperations,
                Has.Count.EqualTo(1),
                () =>
                    $"Should have exactly 1 top-level group operation but found {groupOperations.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                groupOperations[0].Group.Name,
                Is.EqualTo("outer").IgnoreCase,
                () =>
                    $"The top-level group operation should be 'outer' but was '{groupOperations[0].Group.Name}'.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void DirectPropertyPathsExcludeChildAnchors()
        {
            TwoLevelNestedTarget target = CreateScriptableObject<TwoLevelNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("outer", out WGroupDefinition outerGroup),
                Is.True,
                () => $"outer group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.TryGetGroup("inner", out WGroupDefinition innerGroup),
                Is.True,
                () => $"inner group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            string innerAnchor = innerGroup.AnchorPropertyPath;

            Assert.That(
                outerGroup.DirectPropertyPaths,
                Contains.Item("characterName"),
                () =>
                    $"DirectPropertyPaths should contain 'characterName'.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                outerGroup.DirectPropertyPaths,
                Contains.Item("faction"),
                () =>
                    $"DirectPropertyPaths should contain 'faction'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                outerGroup.DirectPropertyPaths,
                Does.Not.Contain(innerAnchor),
                () =>
                    $"DirectPropertyPaths should NOT contain child anchor '{innerAnchor}'.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void MixedNestingPreservesCorrectStructure()
        {
            MixedNestingTarget target = CreateScriptableObject<MixedNestingTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("standalone", out WGroupDefinition standaloneGroup),
                Is.True,
                () => $"standalone group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("parent", out WGroupDefinition parentGroup),
                Is.True,
                () => $"parent group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("nested", out WGroupDefinition nestedGroup),
                Is.True,
                () => $"nested group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                standaloneGroup.HasParent,
                Is.False,
                () => $"standalone should not have a parent.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                standaloneGroup.ChildGroups,
                Is.Empty,
                () =>
                    $"standalone should have no children but has {standaloneGroup.ChildGroups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                parentGroup.HasParent,
                Is.False,
                () => $"parent should not have a parent.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                parentGroup.ChildGroups,
                Has.Count.EqualTo(1),
                () =>
                    $"parent should have 1 child but has {parentGroup.ChildGroups.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                parentGroup.ChildGroups[0].Name,
                Is.EqualTo("nested").IgnoreCase,
                () => $"parent's child should be 'nested'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                nestedGroup.HasParent,
                Is.True,
                () => $"nested should have a parent.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                nestedGroup.ParentGroupName,
                Is.EqualTo("parent").IgnoreCase,
                () => $"nested parent should be 'parent'.\n{FormatLayoutDiagnostics(layout)}"
            );

            List<WGroupDrawOperation> groupOperations = layout
                .Operations.Where(op => op.Type == WGroupDrawOperationType.Group)
                .ToList();
            Assert.That(
                groupOperations,
                Has.Count.EqualTo(2),
                () =>
                    $"Should have exactly 2 top-level group operations but found {groupOperations.Count}.\n{FormatLayoutDiagnostics(layout)}"
            );

            List<string> operationNames = groupOperations
                .Select(op => op.Group.Name.ToLowerInvariant())
                .ToList();
            Assert.That(
                operationNames,
                Contains.Item("standalone"),
                () =>
                    $"Top-level operations should include 'standalone'.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                operationNames,
                Contains.Item("parent"),
                () =>
                    $"Top-level operations should include 'parent'.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                operationNames,
                Does.Not.Contain("nested"),
                () =>
                    $"Top-level operations should NOT include 'nested'.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void NestedGroupPropertyPathsAreStillGrouped()
        {
            TwoLevelNestedTarget target = CreateScriptableObject<TwoLevelNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.GroupedPaths,
                Contains.Item("level"),
                () =>
                    $"GroupedPaths should contain 'level' from inner group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.GroupedPaths,
                Contains.Item("experience"),
                () =>
                    $"GroupedPaths should contain 'experience' from inner group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.GroupedPaths,
                Contains.Item("characterName"),
                () =>
                    $"GroupedPaths should contain 'characterName' from outer group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.GroupedPaths,
                Contains.Item("faction"),
                () =>
                    $"GroupedPaths should contain 'faction' from outer group.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void ChildGroupDeclarationOrderPreserved()
        {
            SiblingNestedTarget target = CreateScriptableObject<SiblingNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("parent", out WGroupDefinition parentGroup),
                Is.True,
                () => $"parent group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.TryGetGroup("child1", out WGroupDefinition child1),
                Is.True,
                () => $"child1 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("child2", out WGroupDefinition child2),
                Is.True,
                () => $"child2 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                child1.DeclarationOrder,
                Is.LessThan(child2.DeclarationOrder),
                () =>
                    $"child1 declaration order ({child1.DeclarationOrder}) should be less than child2 ({child2.DeclarationOrder}).\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                parentGroup.ChildGroups[0].DeclarationOrder,
                Is.LessThanOrEqualTo(parentGroup.ChildGroups[1].DeclarationOrder),
                () =>
                    $"ChildGroups should be sorted by declaration order.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void ThreeLevelNestedGroupHasCorrectDirectPropertyPaths()
        {
            ThreeLevelNestedTarget target = CreateScriptableObject<ThreeLevelNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("level1", out WGroupDefinition level1),
                Is.True,
                () => $"level1 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("level2", out WGroupDefinition level2),
                Is.True,
                () => $"level2 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("level3", out WGroupDefinition level3),
                Is.True,
                () => $"level3 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                level3.DirectPropertyPaths.Count,
                Is.GreaterThan(0),
                () =>
                    $"level3 should have direct property paths.\n{FormatLayoutDiagnostics(layout)}"
            );

            string level3Anchor = level3.AnchorPropertyPath;
            Assert.That(
                level2.DirectPropertyPaths,
                Does.Not.Contain(level3Anchor),
                () =>
                    $"level2 DirectPropertyPaths should NOT contain level3 anchor '{level3Anchor}'.\n{FormatLayoutDiagnostics(layout)}"
            );

            string level2Anchor = level2.AnchorPropertyPath;
            Assert.That(
                level1.DirectPropertyPaths,
                Does.Not.Contain(level2Anchor),
                () =>
                    $"level1 DirectPropertyPaths should NOT contain level2 anchor '{level2Anchor}'.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void LayoutGroupsContainsAllGroupsIncludingNested()
        {
            ThreeLevelNestedTarget target = CreateScriptableObject<ThreeLevelNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.Groups,
                Has.Count.EqualTo(3),
                () => $"Should have 3 groups in layout.Groups.\n{FormatLayoutDiagnostics(layout)}"
            );

            List<string> groupNames = layout.Groups.Select(g => g.Name.ToLowerInvariant()).ToList();
            Assert.That(
                groupNames,
                Contains.Item("level1"),
                () => $"layout.Groups should contain 'level1'.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupNames,
                Contains.Item("level2"),
                () => $"layout.Groups should contain 'level2'.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                groupNames,
                Contains.Item("level3"),
                () => $"layout.Groups should contain 'level3'.\n{FormatLayoutDiagnostics(layout)}"
            );
        }

        [Test]
        public void TryGetGroupWorksForNestedGroups()
        {
            ThreeLevelNestedTarget target = CreateScriptableObject<ThreeLevelNestedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = BuildLayout(serializedObject);

            Assert.That(
                layout.TryGetGroup("level1", out WGroupDefinition _),
                Is.True,
                () => $"TryGetGroup should find 'level1'.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("level2", out WGroupDefinition _),
                Is.True,
                () => $"TryGetGroup should find 'level2'.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("level3", out WGroupDefinition _),
                Is.True,
                () => $"TryGetGroup should find 'level3'.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                layout.TryGetGroup("LEVEL1", out WGroupDefinition _),
                Is.True,
                () => $"TryGetGroup should be case-insensitive.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("LeVeL2", out WGroupDefinition _),
                Is.True,
                () => $"TryGetGroup should be case-insensitive.\n{FormatLayoutDiagnostics(layout)}"
            );
        }
    }
}
#endif
