// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Comprehensive tests for WGroup indentation behavior across different field types.
    /// These tests verify that indentation is properly applied and restored for:
    /// - Simple primitive fields
    /// - Lists and arrays
    /// - Nested serializable classes
    /// - Nested WGroups
    /// - Various nesting depths
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WGroupIndentBehaviorTests : CommonTestBase
    {
        private UnityHelpersSettings.WGroupAutoIncludeConfiguration _previousConfiguration;
        private int _originalIndentLevel;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            WGroupLayoutBuilder.ClearCache();
            GroupGUIWidthUtility.ResetForTests();
            _originalIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
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
            GroupGUIWidthUtility.ResetForTests();
            EditorGUI.indentLevel = _originalIndentLevel;
            UnityHelpersSettings.SetWGroupAutoIncludeConfigurationForTests(
                _previousConfiguration.Mode,
                _previousConfiguration.RowCount
            );
            base.TearDown();
        }

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
                lines.Add($"  Group '{group.Name}':");
                lines.Add($"    Properties: [{string.Join(", ", group.PropertyPaths)}]");
                lines.Add($"    ParentGroup: {group.ParentGroupName ?? "(none)"}");
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
        public void SimpleFieldsWithinGroupHaveCorrectLayout()
        {
            SimpleFieldsTarget target = CreateScriptableObject<SimpleFieldsTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Primitives", out WGroupDefinition group),
                Is.True,
                () => $"Primitives group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(4),
                () => $"Should have 4 properties in group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(group.PropertyPaths, Contains.Item(nameof(SimpleFieldsTarget.intField)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(SimpleFieldsTarget.floatField)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(SimpleFieldsTarget.stringField)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(SimpleFieldsTarget.boolField)));
        }

        [Test]
        public void ListFieldsWithinGroupHaveCorrectLayout()
        {
            ListFieldsTarget target = CreateScriptableObject<ListFieldsTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Lists", out WGroupDefinition group),
                Is.True,
                () => $"Lists group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(3),
                () => $"Should have 3 list properties in group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(group.PropertyPaths, Contains.Item(nameof(ListFieldsTarget.intList)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(ListFieldsTarget.stringList)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(ListFieldsTarget.floatList)));
        }

        [Test]
        public void ArrayFieldsWithinGroupHaveCorrectLayout()
        {
            ArrayFieldsTarget target = CreateScriptableObject<ArrayFieldsTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Arrays", out WGroupDefinition group),
                Is.True,
                () => $"Arrays group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(3),
                () => $"Should have 3 array properties in group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(group.PropertyPaths, Contains.Item(nameof(ArrayFieldsTarget.intArray)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(ArrayFieldsTarget.stringArray)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(ArrayFieldsTarget.floatArray)));
        }

        [Test]
        public void SerializableClassFieldsWithinGroupHaveCorrectLayout()
        {
            SerializableClassTarget target = CreateScriptableObject<SerializableClassTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Nested", out WGroupDefinition group),
                Is.True,
                () => $"Nested group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(2),
                () =>
                    $"Should have 2 nested properties in group.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                group.PropertyPaths,
                Contains.Item(nameof(SerializableClassTarget.nestedData))
            );
            Assert.That(
                group.PropertyPaths,
                Contains.Item(nameof(SerializableClassTarget.anotherNestedData))
            );
        }

        [Test]
        public void ListPropertyHasVisibleChildren()
        {
            ListFieldsTarget target = CreateScriptableObject<ListFieldsTarget>();
            target.intList = new List<int> { 1, 2, 3 };
            using SerializedObject serializedObject = new(target);

            SerializedProperty listProperty = serializedObject.FindProperty(
                nameof(ListFieldsTarget.intList)
            );

            Assert.That(
                listProperty.hasVisibleChildren,
                Is.True,
                "List property should have visible children for proper indent handling."
            );
        }

        [Test]
        public void ArrayPropertyHasVisibleChildren()
        {
            ArrayFieldsTarget target = CreateScriptableObject<ArrayFieldsTarget>();
            target.intArray = new[] { 1, 2, 3 };
            using SerializedObject serializedObject = new(target);

            SerializedProperty arrayProperty = serializedObject.FindProperty(
                nameof(ArrayFieldsTarget.intArray)
            );

            Assert.That(
                arrayProperty.hasVisibleChildren,
                Is.True,
                "Array property should have visible children for proper indent handling."
            );
        }

        [Test]
        public void EmptyListPropertyHasVisibleChildren()
        {
            EmptyCollectionsTarget target = CreateScriptableObject<EmptyCollectionsTarget>();
            using SerializedObject serializedObject = new(target);

            SerializedProperty emptyListProperty = serializedObject.FindProperty(
                nameof(EmptyCollectionsTarget.emptyList)
            );

            Assert.That(
                emptyListProperty.hasVisibleChildren,
                Is.True,
                "Empty list property should still have visible children (for size field)."
            );
        }

        [Test]
        public void EmptyArrayPropertyHasVisibleChildren()
        {
            EmptyCollectionsTarget target = CreateScriptableObject<EmptyCollectionsTarget>();
            using SerializedObject serializedObject = new(target);

            SerializedProperty emptyArrayProperty = serializedObject.FindProperty(
                nameof(EmptyCollectionsTarget.emptyArray)
            );

            Assert.That(
                emptyArrayProperty.hasVisibleChildren,
                Is.True,
                "Empty array property should still have visible children (for size field)."
            );
        }

        [Test]
        public void SerializableClassPropertyHasVisibleChildren()
        {
            SerializableClassTarget target = CreateScriptableObject<SerializableClassTarget>();
            using SerializedObject serializedObject = new(target);

            SerializedProperty nestedProperty = serializedObject.FindProperty(
                nameof(SerializableClassTarget.nestedData)
            );

            Assert.That(
                nestedProperty.hasVisibleChildren,
                Is.True,
                "Serializable class property should have visible children."
            );
        }

        [Test]
        public void SimpleIntPropertyDoesNotHaveVisibleChildren()
        {
            SimpleFieldsTarget target = CreateScriptableObject<SimpleFieldsTarget>();
            using SerializedObject serializedObject = new(target);

            SerializedProperty intProperty = serializedObject.FindProperty(
                nameof(SimpleFieldsTarget.intField)
            );

            Assert.That(
                intProperty.hasVisibleChildren,
                Is.False,
                "Simple int property should not have visible children."
            );
        }

        [Test]
        public void SimpleStringPropertyDoesNotHaveVisibleChildren()
        {
            SimpleFieldsTarget target = CreateScriptableObject<SimpleFieldsTarget>();
            using SerializedObject serializedObject = new(target);

            SerializedProperty stringProperty = serializedObject.FindProperty(
                nameof(SimpleFieldsTarget.stringField)
            );

            Assert.That(
                stringProperty.hasVisibleChildren,
                Is.False,
                "Simple string property should not have visible children."
            );
        }

        [Test]
        public void NestedGroupsHaveCorrectParentChildRelationship()
        {
            NestedGroupsTarget target = CreateScriptableObject<NestedGroupsTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Outer", out WGroupDefinition outer),
                Is.True,
                () => $"Outer group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("Inner", out WGroupDefinition inner),
                Is.True,
                () => $"Inner group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                inner.ParentGroupName,
                Is.EqualTo("Outer").IgnoreCase,
                "Inner group should have Outer as parent."
            );
            Assert.That(
                outer.ChildGroups,
                Has.Count.EqualTo(1),
                "Outer should have 1 child group."
            );
            Assert.That(
                outer.ChildGroups[0].Name,
                Is.EqualTo("Inner").IgnoreCase,
                "Outer's child should be Inner."
            );
        }

        [Test]
        public void ThreeLevelNestedGroupsHaveCorrectHierarchy()
        {
            ThreeLevelGroupsTarget target = CreateScriptableObject<ThreeLevelGroupsTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Level1", out WGroupDefinition level1),
                Is.True,
                () => $"Level1 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("Level2", out WGroupDefinition level2),
                Is.True,
                () => $"Level2 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("Level3", out WGroupDefinition level3),
                Is.True,
                () => $"Level3 group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(level1.HasParent, Is.False, "Level1 should not have a parent.");
            Assert.That(level2.ParentGroupName, Is.EqualTo("Level1").IgnoreCase);
            Assert.That(level3.ParentGroupName, Is.EqualTo("Level2").IgnoreCase);
        }

        [Test]
        public void NestedListInNestedGroupHasCorrectLayout()
        {
            NestedGroupsTarget target = CreateScriptableObject<NestedGroupsTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Inner", out WGroupDefinition inner),
                Is.True,
                () => $"Inner group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                inner.PropertyPaths,
                Contains.Item(nameof(NestedGroupsTarget.innerList)),
                "Inner group should contain innerList."
            );
        }

        [Test]
        public void ListAtDeepNestingLevelHasVisibleChildren()
        {
            ThreeLevelGroupsTarget target = CreateScriptableObject<ThreeLevelGroupsTarget>();
            target.level3List = new List<int> { 1, 2, 3 };
            using SerializedObject serializedObject = new(target);

            SerializedProperty level3ListProperty = serializedObject.FindProperty(
                nameof(ThreeLevelGroupsTarget.level3List)
            );

            Assert.That(
                level3ListProperty.hasVisibleChildren,
                Is.True,
                "List at deep nesting level should have visible children."
            );
        }

        [Test]
        public void MixedFieldsGroupContainsAllExpectedProperties()
        {
            MixedFieldsTarget target = CreateScriptableObject<MixedFieldsTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Mixed", out WGroupDefinition group),
                Is.True,
                () => $"Mixed group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(5),
                () => $"Should have 5 mixed properties.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(group.PropertyPaths, Contains.Item(nameof(MixedFieldsTarget.simpleInt)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(MixedFieldsTarget.listField)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(MixedFieldsTarget.simpleString)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(MixedFieldsTarget.nestedField)));
            Assert.That(group.PropertyPaths, Contains.Item(nameof(MixedFieldsTarget.simpleFloat)));
        }

        [Test]
        public void SingleElementListHasVisibleChildren()
        {
            SingleElementTarget target = CreateScriptableObject<SingleElementTarget>();
            using SerializedObject serializedObject = new(target);

            SerializedProperty singleItemList = serializedObject.FindProperty(
                nameof(SingleElementTarget.singleItemList)
            );

            Assert.That(
                singleItemList.hasVisibleChildren,
                Is.True,
                "Single element list should have visible children."
            );
        }

        [Test]
        public void SingleElementArrayHasVisibleChildren()
        {
            SingleElementTarget target = CreateScriptableObject<SingleElementTarget>();
            using SerializedObject serializedObject = new(target);

            SerializedProperty singleItemArray = serializedObject.FindProperty(
                nameof(SingleElementTarget.singleItemArray)
            );

            Assert.That(
                singleItemArray.hasVisibleChildren,
                Is.True,
                "Single element array should have visible children."
            );
        }

        [Test]
        public void WrappedNestedListsHaveVisibleChildren()
        {
            WrappedNestedListTarget target = CreateScriptableObject<WrappedNestedListTarget>();
            target.wrappedLists = new List<IntListWrapper>
            {
                new IntListWrapper
                {
                    values = new List<int> { 1, 2, 3 },
                },
            };
            using SerializedObject serializedObject = new(target);

            SerializedProperty wrappedListProperty = serializedObject.FindProperty(
                nameof(WrappedNestedListTarget.wrappedLists)
            );

            Assert.That(
                wrappedListProperty.hasVisibleChildren,
                Is.True,
                "Wrapped nested lists should have visible children."
            );
        }

        [Test]
        public void DeepNestedDataHasVisibleChildren()
        {
            DeepNestingTarget target = CreateScriptableObject<DeepNestingTarget>();
            using SerializedObject serializedObject = new(target);

            SerializedProperty deepDataProperty = serializedObject.FindProperty(
                nameof(DeepNestingTarget.deepData)
            );

            Assert.That(
                deepDataProperty.hasVisibleChildren,
                Is.True,
                "Deep nested data should have visible children."
            );
        }

        [Test]
        public void SiblingGroupsWithDifferentTypesHaveCorrectLayouts()
        {
            SiblingGroupsWithDifferentTypesTarget target =
                CreateScriptableObject<SiblingGroupsWithDifferentTypesTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("GroupA", out WGroupDefinition groupA),
                Is.True,
                () => $"GroupA should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("GroupB", out WGroupDefinition groupB),
                Is.True,
                () => $"GroupB should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("GroupC", out WGroupDefinition groupC),
                Is.True,
                () => $"GroupC should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                groupA.PropertyPaths,
                Contains.Item(nameof(SiblingGroupsWithDifferentTypesTarget.intA))
            );
            Assert.That(
                groupA.PropertyPaths,
                Contains.Item(nameof(SiblingGroupsWithDifferentTypesTarget.floatA))
            );
            Assert.That(
                groupB.PropertyPaths,
                Contains.Item(nameof(SiblingGroupsWithDifferentTypesTarget.listB))
            );
            Assert.That(
                groupB.PropertyPaths,
                Contains.Item(nameof(SiblingGroupsWithDifferentTypesTarget.arrayB))
            );
            Assert.That(
                groupC.PropertyPaths,
                Contains.Item(nameof(SiblingGroupsWithDifferentTypesTarget.nestedC))
            );
        }

        [Test]
        public void IndentRestorationTargetHasUngroupedFieldsOutsideGroup()
        {
            IndentRestorationTarget target = CreateScriptableObject<IndentRestorationTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.GroupedPaths,
                Does.Not.Contain(nameof(IndentRestorationTarget.beforeGroup)),
                "beforeGroup should not be in any group."
            );
            Assert.That(
                layout.GroupedPaths,
                Does.Not.Contain(nameof(IndentRestorationTarget.afterGroup)),
                "afterGroup should not be in any group."
            );
            Assert.That(
                layout.GroupedPaths,
                Contains.Item(nameof(IndentRestorationTarget.middleList)),
                "middleList should be in the group."
            );
            Assert.That(
                layout.GroupedPaths,
                Contains.Item(nameof(IndentRestorationTarget.middleNested)),
                "middleNested should be in the group."
            );
        }

        [Test]
        public void ComplexCombinedTargetHasCorrectNestedHierarchy()
        {
            ComplexCombinedTarget target = CreateScriptableObject<ComplexCombinedTarget>();
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup("Outer", out WGroupDefinition outer),
                Is.True,
                () => $"Outer group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("MiddleNested", out WGroupDefinition middle),
                Is.True,
                () => $"MiddleNested group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );
            Assert.That(
                layout.TryGetGroup("Deepest", out WGroupDefinition deepest),
                Is.True,
                () => $"Deepest group should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(outer.HasParent, Is.False);
            Assert.That(middle.ParentGroupName, Is.EqualTo("Outer").IgnoreCase);
            Assert.That(deepest.ParentGroupName, Is.EqualTo("MiddleNested").IgnoreCase);

            Assert.That(
                deepest.PropertyPaths,
                Contains.Item(nameof(ComplexCombinedTarget.deepestArray))
            );
        }

        /// <summary>
        /// Data-driven test cases validating that groups contain their expected properties.
        /// These test cases validate the correct behavior of explicit [WGroup] + InfiniteAutoInclude
        /// combined with [WGroup] + [WGroupEnd] patterns.
        /// </summary>
        private static System.Collections.IEnumerable GroupPropertyCountTestCases()
        {
            yield return new TestCaseData(
                typeof(SimpleFieldsTarget),
                "Primitives",
                4,
                new[]
                {
                    nameof(SimpleFieldsTarget.intField),
                    nameof(SimpleFieldsTarget.floatField),
                    nameof(SimpleFieldsTarget.stringField),
                    nameof(SimpleFieldsTarget.boolField),
                }
            ).SetName("SimpleFields.HasAllFourPrimitives");

            yield return new TestCaseData(
                typeof(ListFieldsTarget),
                "Lists",
                3,
                new[]
                {
                    nameof(ListFieldsTarget.intList),
                    nameof(ListFieldsTarget.stringList),
                    nameof(ListFieldsTarget.floatList),
                }
            ).SetName("ListFields.HasAllThreeLists");

            yield return new TestCaseData(
                typeof(ArrayFieldsTarget),
                "Arrays",
                3,
                new[]
                {
                    nameof(ArrayFieldsTarget.intArray),
                    nameof(ArrayFieldsTarget.stringArray),
                    nameof(ArrayFieldsTarget.floatArray),
                }
            ).SetName("ArrayFields.HasAllThreeArrays");

            yield return new TestCaseData(
                typeof(SerializableClassTarget),
                "Nested",
                2,
                new[]
                {
                    nameof(SerializableClassTarget.nestedData),
                    nameof(SerializableClassTarget.anotherNestedData),
                }
            ).SetName("SerializableClass.HasBothNestedFields");

            yield return new TestCaseData(
                typeof(MixedFieldsTarget),
                "Mixed",
                5,
                new[]
                {
                    nameof(MixedFieldsTarget.simpleInt),
                    nameof(MixedFieldsTarget.listField),
                    nameof(MixedFieldsTarget.simpleString),
                    nameof(MixedFieldsTarget.nestedField),
                    nameof(MixedFieldsTarget.simpleFloat),
                }
            ).SetName("MixedFields.HasAllFiveMixedFields");
        }

        [Test]
        [TestCaseSource(nameof(GroupPropertyCountTestCases))]
        public void GroupContainsExpectedProperties(
            System.Type targetType,
            string groupName,
            int expectedCount,
            string[] expectedProperties
        )
        {
            UnityEngine.Object target = CreateScriptableObject(targetType);
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup(groupName, out WGroupDefinition group),
                Is.True,
                () => $"Group '{groupName}' should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Has.Count.EqualTo(expectedCount),
                () =>
                    $"Group '{groupName}' should have {expectedCount} properties but has {group.PropertyPaths.Count}: [{string.Join(", ", group.PropertyPaths)}].\n{FormatLayoutDiagnostics(layout)}"
            );

            foreach (string expectedProperty in expectedProperties)
            {
                Assert.That(
                    group.PropertyPaths,
                    Contains.Item(expectedProperty),
                    () =>
                        $"Group '{groupName}' should contain '{expectedProperty}'.\n{FormatLayoutDiagnostics(layout)}"
                );
            }
        }

        /// <summary>
        /// Test cases validating behavior of WGroupEnd without [WGroup] (should NOT include field).
        /// </summary>
        private static System.Collections.IEnumerable WGroupEndOnlyDoesNotIncludeFieldTestCases()
        {
            yield return new TestCaseData(
                typeof(SiblingGroupsWithDifferentTypesTarget),
                "GroupC",
                nameof(SiblingGroupsWithDifferentTypesTarget.afterAllGroups)
            ).SetName("WGroupEndOnly.DoesNotIncludeTerminatingField");
        }

        [Test]
        [TestCaseSource(nameof(WGroupEndOnlyDoesNotIncludeFieldTestCases))]
        public void WGroupEndOnlyDoesNotIncludeField(
            System.Type targetType,
            string groupName,
            string fieldWithEndOnly
        )
        {
            UnityEngine.Object target = CreateScriptableObject(targetType);
            using SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.That(
                layout.TryGetGroup(groupName, out WGroupDefinition group),
                Is.True,
                () => $"Group '{groupName}' should exist.\n{FormatLayoutDiagnostics(layout)}"
            );

            Assert.That(
                group.PropertyPaths,
                Does.Not.Contain(fieldWithEndOnly),
                () =>
                    $"Field '{fieldWithEndOnly}' with only [WGroupEnd] should NOT be in group '{groupName}'.\n{FormatLayoutDiagnostics(layout)}"
            );
        }
    }
}
#endif
