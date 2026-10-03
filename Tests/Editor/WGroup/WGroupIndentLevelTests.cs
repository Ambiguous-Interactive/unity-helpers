// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.WGroup
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Tests for verifying indent level management and compensation in WGroups.
    /// Focuses on EditorGUI.indentLevel behavior during group rendering.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WGroupIndentLevelTests : CommonTestBase
    {
        private UnityHelpersSettings.WGroupAutoIncludeConfiguration _previousConfiguration;
        private int _originalIndentLevel;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            WGroupLayoutBuilder.ClearCache();
            GroupGUIWidthUtilityTestAccess.Reset();
            _originalIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            _previousConfiguration = UnityHelpersSettings.GetWGroupAutoIncludeConfiguration();
            UnityHelpersSettingsTestAccess.SetWGroupAutoIncludeConfiguration(
                UnityHelpersSettings.WGroupAutoIncludeMode.None,
                0
            );
        }

        [TearDown]
        public override void TearDown()
        {
            WGroupLayoutBuilder.ClearCache();
            GroupGUIWidthUtilityTestAccess.Reset();
            EditorGUI.indentLevel = _originalIndentLevel;
            UnityHelpersSettingsTestAccess.SetWGroupAutoIncludeConfiguration(
                _previousConfiguration.Mode,
                _previousConfiguration.RowCount
            );
            base.TearDown();
        }

        [Test]
        public void IndentCompensationDecreasesIndentByOne()
        {
            EditorGUI.indentLevel = 3;
            int observedIndent = -1;

            GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
            {
                observedIndent = EditorGUI.indentLevel;
            });

            Assert.That(
                observedIndent,
                Is.EqualTo(2),
                "Indent should be reduced by 1 inside compensation block."
            );
            Assert.That(
                EditorGUI.indentLevel,
                Is.EqualTo(3),
                "Indent should be restored after compensation."
            );
        }

        [Test]
        public void IndentCompensationDoesNotGoBelowZero()
        {
            EditorGUI.indentLevel = 0;
            int observedIndent = -1;

            GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
            {
                observedIndent = EditorGUI.indentLevel;
            });

            Assert.That(observedIndent, Is.EqualTo(0), "Indent should not go below zero.");
        }

        [Test]
        public void IndentCompensationRestoresAfterException()
        {
            EditorGUI.indentLevel = 4;

            try
            {
                GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
                {
                    Assert.That(EditorGUI.indentLevel, Is.EqualTo(3));
                    throw new InvalidOperationException("Test exception");
                });
            }
            catch (InvalidOperationException) { }

            Assert.That(
                EditorGUI.indentLevel,
                Is.EqualTo(4),
                "Indent should be restored after exception."
            );
        }

        [Test]
        public void NestedIndentCompensationWorksCorrectly()
        {
            EditorGUI.indentLevel = 5;
            int outerObserved = -1;
            int innerObserved = -1;

            GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
            {
                outerObserved = EditorGUI.indentLevel;
                GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
                {
                    innerObserved = EditorGUI.indentLevel;
                });
                Assert.That(
                    EditorGUI.indentLevel,
                    Is.EqualTo(4),
                    "Inner indent should be restored."
                );
            });

            Assert.That(outerObserved, Is.EqualTo(4), "Outer should be reduced by 1.");
            Assert.That(innerObserved, Is.EqualTo(3), "Inner should be reduced by 1 again.");
            Assert.That(EditorGUI.indentLevel, Is.EqualTo(5), "Original should be fully restored.");
        }

        [Test]
        public void IndentCompensationAtVariousLevels(
            [Values(0, 1, 2, 3, 5, 10)] int startingIndent
        )
        {
            EditorGUI.indentLevel = startingIndent;
            int observedIndent = -1;

            GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
            {
                observedIndent = EditorGUI.indentLevel;
            });

            int expectedIndent = Math.Max(0, startingIndent - 1);
            Assert.That(
                observedIndent,
                Is.EqualTo(expectedIndent),
                $"Starting at {startingIndent}, observed should be {expectedIndent}."
            );
            Assert.That(
                EditorGUI.indentLevel,
                Is.EqualTo(startingIndent),
                "Indent should be restored to original value."
            );
        }

        [Test]
        public void ScopeDepthIncreasesWithNonZeroPadding()
        {
            GroupGUIWidthUtilityTestAccess.Reset();
            int originalDepth = GroupGUIWidthUtility.CurrentScopeDepth;

            using (GroupGUIWidthUtility.PushContentPadding(10f, 5f, 5f))
            {
                Assert.That(
                    GroupGUIWidthUtility.CurrentScopeDepth,
                    Is.EqualTo(originalDepth + 1),
                    "Scope depth should increase by 1."
                );
            }

            Assert.That(
                GroupGUIWidthUtility.CurrentScopeDepth,
                Is.EqualTo(originalDepth),
                "Scope depth should be restored."
            );
        }

        [Test]
        public void ScopeDepthDoesNotIncreaseWithZeroPadding()
        {
            GroupGUIWidthUtilityTestAccess.Reset();
            int originalDepth = GroupGUIWidthUtility.CurrentScopeDepth;

            using (GroupGUIWidthUtility.PushContentPadding(0f, 0f, 0f))
            {
                Assert.That(
                    GroupGUIWidthUtility.CurrentScopeDepth,
                    Is.EqualTo(originalDepth),
                    "Zero padding should not increase scope depth."
                );
            }
        }

        [Test]
        public void NestedPaddingScopesAccumulateDepth()
        {
            GroupGUIWidthUtilityTestAccess.Reset();
            int originalDepth = GroupGUIWidthUtility.CurrentScopeDepth;

            using (GroupGUIWidthUtility.PushContentPadding(10f, 5f, 5f))
            {
                Assert.That(GroupGUIWidthUtility.CurrentScopeDepth, Is.EqualTo(originalDepth + 1));

                using (GroupGUIWidthUtility.PushContentPadding(8f, 4f, 4f))
                {
                    Assert.That(
                        GroupGUIWidthUtility.CurrentScopeDepth,
                        Is.EqualTo(originalDepth + 2)
                    );

                    using (GroupGUIWidthUtility.PushContentPadding(6f, 3f, 3f))
                    {
                        Assert.That(
                            GroupGUIWidthUtility.CurrentScopeDepth,
                            Is.EqualTo(originalDepth + 3)
                        );
                    }

                    Assert.That(
                        GroupGUIWidthUtility.CurrentScopeDepth,
                        Is.EqualTo(originalDepth + 2)
                    );
                }

                Assert.That(GroupGUIWidthUtility.CurrentScopeDepth, Is.EqualTo(originalDepth + 1));
            }

            Assert.That(GroupGUIWidthUtility.CurrentScopeDepth, Is.EqualTo(originalDepth));
        }

        [Test]
        public void LeftAndRightPaddingAccumulateSeparately()
        {
            GroupGUIWidthUtilityTestAccess.Reset();

            using (GroupGUIWidthUtility.PushContentPadding(12f, 8f, 4f))
            {
                Assert.That(
                    GroupGUIWidthUtility.CurrentLeftPadding,
                    Is.EqualTo(8f).Within(0.0001f)
                );
                Assert.That(
                    GroupGUIWidthUtility.CurrentRightPadding,
                    Is.EqualTo(4f).Within(0.0001f)
                );

                using (GroupGUIWidthUtility.PushContentPadding(10f, 6f, 4f))
                {
                    Assert.That(
                        GroupGUIWidthUtility.CurrentLeftPadding,
                        Is.EqualTo(14f).Within(0.0001f),
                        "Left padding should accumulate."
                    );
                    Assert.That(
                        GroupGUIWidthUtility.CurrentRightPadding,
                        Is.EqualTo(8f).Within(0.0001f),
                        "Right padding should accumulate."
                    );
                }

                Assert.That(
                    GroupGUIWidthUtility.CurrentLeftPadding,
                    Is.EqualTo(8f).Within(0.0001f),
                    "Left padding should be restored."
                );
                Assert.That(
                    GroupGUIWidthUtility.CurrentRightPadding,
                    Is.EqualTo(4f).Within(0.0001f),
                    "Right padding should be restored."
                );
            }

            Assert.That(GroupGUIWidthUtility.CurrentLeftPadding, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(GroupGUIWidthUtility.CurrentRightPadding, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void ApplyCurrentPaddingAdjustsRectCorrectly()
        {
            GroupGUIWidthUtilityTestAccess.Reset();
            Rect original = new(0f, 0f, 400f, 100f);

            using (GroupGUIWidthUtility.PushContentPadding(30f, 20f, 10f))
            {
                Rect adjusted = GroupGUIWidthUtility.ApplyCurrentPadding(original);

                Assert.That(adjusted.xMin, Is.EqualTo(20f).Within(0.0001f));
                Assert.That(adjusted.xMax, Is.EqualTo(390f).Within(0.0001f));
                Assert.That(adjusted.width, Is.EqualTo(370f).Within(0.0001f));
            }
        }

        [Test]
        public void ApplyCurrentPaddingWithNoPaddingReturnsOriginal()
        {
            GroupGUIWidthUtilityTestAccess.Reset();
            Rect original = new(50f, 25f, 300f, 50f);

            Rect adjusted = GroupGUIWidthUtility.ApplyCurrentPadding(original);

            Assert.That(adjusted, Is.EqualTo(original));
        }

        [Test]
        public void MixedZeroAndNonZeroPaddingScopesTrackCorrectly()
        {
            GroupGUIWidthUtilityTestAccess.Reset();
            int originalDepth = GroupGUIWidthUtility.CurrentScopeDepth;

            using (GroupGUIWidthUtility.PushContentPadding(10f, 5f, 5f))
            {
                Assert.That(GroupGUIWidthUtility.CurrentScopeDepth, Is.EqualTo(originalDepth + 1));

                using (GroupGUIWidthUtility.PushContentPadding(0f, 0f, 0f))
                {
                    Assert.That(
                        GroupGUIWidthUtility.CurrentScopeDepth,
                        Is.EqualTo(originalDepth + 1),
                        "Zero padding should not change depth."
                    );

                    using (GroupGUIWidthUtility.PushContentPadding(5f, 2f, 3f))
                    {
                        Assert.That(
                            GroupGUIWidthUtility.CurrentScopeDepth,
                            Is.EqualTo(originalDepth + 2)
                        );
                    }
                }

                Assert.That(GroupGUIWidthUtility.CurrentScopeDepth, Is.EqualTo(originalDepth + 1));
            }

            Assert.That(GroupGUIWidthUtility.CurrentScopeDepth, Is.EqualTo(originalDepth));
        }

        [Test]
        public void HorizontalPaddingEqualsLeftPlusRight()
        {
            GroupGUIWidthUtilityTestAccess.Reset();

            float leftPadding = 12f;
            float rightPadding = 8f;
            float total = leftPadding + rightPadding;

            using (GroupGUIWidthUtility.PushContentPadding(total, leftPadding, rightPadding))
            {
                Assert.That(
                    GroupGUIWidthUtility.CurrentHorizontalPadding,
                    Is.EqualTo(total).Within(0.0001f),
                    "Horizontal padding should equal left + right."
                );
            }
        }

        [Test]
        public void CalculateHorizontalPaddingFromNullStyleReturnsZero()
        {
            float total = GroupGUIWidthUtility.CalculateHorizontalPadding(
                null,
                out float left,
                out float right
            );

            Assert.That(total, Is.EqualTo(0f));
            Assert.That(left, Is.EqualTo(0f));
            Assert.That(right, Is.EqualTo(0f));
        }

        [Test]
        public void CalculateHorizontalPaddingFromHelpBoxStyleReturnsNonNegative()
        {
            GUIStyle helpBox = EditorStyles.helpBox;

            float total = GroupGUIWidthUtility.CalculateHorizontalPadding(
                helpBox,
                out float left,
                out float right
            );

            Assert.That(left, Is.GreaterThanOrEqualTo(0f));
            Assert.That(right, Is.GreaterThanOrEqualTo(0f));
            Assert.That(total, Is.EqualTo(left + right).Within(0.0001f));
        }

        [Test]
        public void ListPropertyAtDifferentNestingDepthsHasConsistentBehavior()
        {
            ThreeLevelGroupsTarget target = CreateScriptableObject<ThreeLevelGroupsTarget>();
            target.level3List = new List<int> { 1, 2, 3 };
            using SerializedObject serializedObject = new(target);

            SerializedProperty level1Property = serializedObject.FindProperty(
                nameof(ThreeLevelGroupsTarget.level1Field)
            );
            SerializedProperty level2Property = serializedObject.FindProperty(
                nameof(ThreeLevelGroupsTarget.level2Field)
            );
            SerializedProperty level3ListProperty = serializedObject.FindProperty(
                nameof(ThreeLevelGroupsTarget.level3List)
            );

            Assert.That(
                level1Property.hasVisibleChildren,
                Is.False,
                "Simple int at level 1 should not have visible children."
            );
            Assert.That(
                level2Property.hasVisibleChildren,
                Is.False,
                "Simple int at level 2 should not have visible children."
            );
            Assert.That(
                level3ListProperty.hasVisibleChildren,
                Is.True,
                "List at level 3 should have visible children."
            );
        }

        [Test]
        public void ArrayPropertyInNestedGroupHasCorrectChildStatus()
        {
            ComplexCombinedTarget target = CreateScriptableObject<ComplexCombinedTarget>();
            target.deepestArray = new[] { 1, 2, 3 };
            using SerializedObject serializedObject = new(target);

            SerializedProperty deepestArray = serializedObject.FindProperty(
                nameof(ComplexCombinedTarget.deepestArray)
            );

            Assert.That(
                deepestArray.hasVisibleChildren,
                Is.True,
                "Array in deeply nested group should have visible children."
            );
        }

        [Test]
        public void NestedSerializableClassListHasCorrectChildStatus()
        {
            ComplexCombinedTarget target = CreateScriptableObject<ComplexCombinedTarget>();
            target.middleNestedList = new List<NestedData>
            {
                new NestedData { value = 1, name = "Test" },
            };
            using SerializedObject serializedObject = new(target);

            SerializedProperty middleNestedList = serializedObject.FindProperty(
                nameof(ComplexCombinedTarget.middleNestedList)
            );

            Assert.That(
                middleNestedList.hasVisibleChildren,
                Is.True,
                "List of serializable classes should have visible children."
            );
        }

        [Test]
        public void ConsecutiveIndentCompensationCallsRestoreCorrectly()
        {
            EditorGUI.indentLevel = 6;

            GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
            {
                Assert.That(EditorGUI.indentLevel, Is.EqualTo(5));
            });
            Assert.That(EditorGUI.indentLevel, Is.EqualTo(6));

            GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
            {
                Assert.That(EditorGUI.indentLevel, Is.EqualTo(5));
            });
            Assert.That(EditorGUI.indentLevel, Is.EqualTo(6));

            GroupGUIIndentUtility.ExecuteWithIndentCompensation(() =>
            {
                Assert.That(EditorGUI.indentLevel, Is.EqualTo(5));
            });
            Assert.That(EditorGUI.indentLevel, Is.EqualTo(6));
        }

        [Test]
        public void PaddingScopeDisposalOrderMatters()
        {
            GroupGUIWidthUtilityTestAccess.Reset();

            IDisposable outer = GroupGUIWidthUtility.PushContentPadding(10f, 5f, 5f);
            IDisposable inner = GroupGUIWidthUtility.PushContentPadding(8f, 4f, 4f);

            Assert.That(GroupGUIWidthUtility.CurrentLeftPadding, Is.EqualTo(9f).Within(0.0001f));

            inner.Dispose();

            Assert.That(
                GroupGUIWidthUtility.CurrentLeftPadding,
                Is.EqualTo(5f).Within(0.0001f),
                "After inner disposal, should have outer padding only."
            );

            outer.Dispose();

            Assert.That(
                GroupGUIWidthUtility.CurrentLeftPadding,
                Is.EqualTo(0f).Within(0.0001f),
                "After outer disposal, should have zero padding."
            );
        }
    }
}
#endif
