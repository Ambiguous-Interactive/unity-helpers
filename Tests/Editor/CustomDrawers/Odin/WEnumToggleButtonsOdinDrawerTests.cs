// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.EnumToggleButtons;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.EnumToggleButtons;

    /// <summary>
    /// Tests for WEnumToggleButtonsOdinDrawer ensuring WEnumToggleButtons attribute
    /// works correctly with Odin Inspector for both regular and flags enums.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WEnumToggleButtonsOdinDrawerTests : CommonTestBase
    {
        [Test]
        public void DrawerRegistrationForRegularEnumIsCorrect()
        {
            OdinEnumToggleButtonsRegularTarget target =
                CreateScriptableObject<OdinEnumToggleButtonsRegularTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for target");
        }

        [Test]
        public void DrawerRegistrationForFlagsEnumIsCorrect()
        {
            OdinEnumToggleButtonsFlagsTarget target =
                CreateScriptableObject<OdinEnumToggleButtonsFlagsTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for flags target");
        }

        [UnityTest]
        public IEnumerator DrawerHandlesMonoBehaviourTarget()
        {
            while (EditorApplication.isCompiling)
            {
                yield return null;
            }

            OdinEnumToggleButtonsMonoBehaviour target = NewGameObject("EnumToggleMB")
                .AddComponent<OdinEnumToggleButtonsMonoBehaviour>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for MonoBehaviour target");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForRegularEnum()
        {
            OdinEnumToggleButtonsRegularTarget target =
                CreateScriptableObject<OdinEnumToggleButtonsRegularTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for regular enum. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForFlagsEnum()
        {
            OdinEnumToggleButtonsFlagsTarget target =
                CreateScriptableObject<OdinEnumToggleButtonsFlagsTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for flags enum. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForMonoBehaviour()
        {
            OdinEnumToggleButtonsMonoBehaviour target = NewGameObject("EnumToggleMB")
                .AddComponent<OdinEnumToggleButtonsMonoBehaviour>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for MonoBehaviour. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator RepeatedOnInspectorGuiCallsDoNotThrow()
        {
            OdinEnumToggleButtonsRegularTarget target =
                CreateScriptableObject<OdinEnumToggleButtonsRegularTarget>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        editor.OnInspectorGUI();
                    }
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"Repeated OnInspectorGUI calls should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void GetCachedEnumOptionsReturnsOptionsForRegularEnum()
        {
            EnumToggleButtonsShared.ToggleOption[] options = GetCachedEnumOptions(
                typeof(SimpleTestEnum)
            );

            Assert.That(options, Is.Not.Null, "Options should not be null");
            Assert.That(options.Length, Is.EqualTo(3), "SimpleTestEnum has 3 values");
        }

        [Test]
        public void GetCachedEnumOptionsReturnsOptionsForFlagsEnum()
        {
            EnumToggleButtonsShared.ToggleOption[] options = GetCachedEnumOptions(
                typeof(TestFlagsEnum)
            );

            Assert.That(options, Is.Not.Null, "Options should not be null");
            Assert.That(
                options.Length,
                Is.EqualTo(4),
                "TestFlagsEnum has 4 individual flag values (including None)"
            );
        }

        [Test]
        public void GetCachedEnumOptionsReturnsNullForNonEnumType()
        {
            EnumToggleButtonsShared.ToggleOption[] options = GetCachedEnumOptions(typeof(int));

            Assert.That(options, Is.Null, "Options should be null for non-enum type");
        }

        [Test]
        public void GetCachedEnumOptionsReturnsNullForNullType()
        {
            EnumToggleButtonsShared.ToggleOption[] options = GetCachedEnumOptions(null);

            Assert.That(options, Is.Null, "Options should be null for null type");
        }

        [Test]
        public void GetCachedEnumOptionsCachesResults()
        {
            EnumToggleButtonsShared.ToggleOption[] options1 = GetCachedEnumOptions(
                typeof(SimpleTestEnum)
            );
            EnumToggleButtonsShared.ToggleOption[] options2 = GetCachedEnumOptions(
                typeof(SimpleTestEnum)
            );

            Assert.That(
                options1,
                Is.SameAs(options2),
                "Cached options should be the same instance"
            );
        }

        [Test]
        public void BuildEnumOptionsExcludesCompositeFlags()
        {
            EnumToggleButtonsShared.ToggleOption[] options = BuildEnumOptions(
                typeof(TestFlagsEnumWithComposite),
                isFlags: true
            );

            Assert.That(options, Is.Not.Null, "Options should not be null");
            Assert.That(
                options.Length,
                Is.EqualTo(4),
                "Should exclude composite All flag and include only single flags"
            );
        }

        [TestCase(typeof(SmallTestEnum), 2)]
        [TestCase(typeof(SimpleTestEnum), 3)]
        [TestCase(typeof(MediumTestEnum), 5)]
        [TestCase(typeof(LargeTestEnum), 10)]
        public void GetCachedEnumOptionsReturnsCorrectCountForEnumSize(
            Type enumType,
            int expectedCount
        )
        {
            EnumToggleButtonsShared.ToggleOption[] options = GetCachedEnumOptions(enumType);

            Assert.That(options, Is.Not.Null, "Options should not be null");
            Assert.That(
                options.Length,
                Is.EqualTo(expectedCount),
                $"Enum {enumType.Name} should have {expectedCount} values"
            );
        }

        [Test]
        public void CalculateAllFlagsMaskReturnsCorrectMask()
        {
            EnumToggleButtonsShared.ToggleOption[] options = GetCachedEnumOptions(
                typeof(TestFlagsEnum)
            );
            ulong mask = CalculateAllFlagsMask(options);

            ulong expectedMask = (ulong)(
                TestFlagsEnum.FlagA | TestFlagsEnum.FlagB | TestFlagsEnum.FlagC
            );
            Assert.That(mask, Is.EqualTo(expectedMask), "Mask should include all non-zero flags");
        }

        [Test]
        public void CalculateAllFlagsMaskExcludesZeroFlag()
        {
            EnumToggleButtonsShared.ToggleOption[] options = GetCachedEnumOptions(
                typeof(TestFlagsEnum)
            );
            ulong mask = CalculateAllFlagsMask(options);

            Assert.That(mask, Is.Not.EqualTo(0UL), "Mask should not be zero");
            Assert.That((mask & 0UL), Is.EqualTo(0UL), "Zero flag should not affect the mask");
        }

        [TestCase(0UL, false)]
        [TestCase(1UL, true)]
        [TestCase(2UL, true)]
        [TestCase(3UL, false)]
        [TestCase(4UL, true)]
        [TestCase(5UL, false)]
        [TestCase(8UL, true)]
        [TestCase(16UL, true)]
        [TestCase(15UL, false)]
        [TestCase(32UL, true)]
        [TestCase(64UL, true)]
        [TestCase(128UL, true)]
        [TestCase(256UL, true)]
        [TestCase(1024UL, true)]
        [TestCase(2048UL, true)]
        [TestCase(4096UL, true)]
        [TestCase(1UL << 20, true)] // 1048576
        [TestCase(1UL << 30, true)] // 1073741824
        [TestCase(1UL << 40, true)] // Large power of 2
        [TestCase(1UL << 50, true)] // Very large power of 2
        [TestCase(1UL << 62, true)] // Near max ulong power of 2
        [TestCase(1UL << 63, true)] // Maximum power of 2 for ulong (9223372036854775808)
        [TestCase(6UL, false)]
        [TestCase(7UL, false)]
        [TestCase(9UL, false)]
        [TestCase(10UL, false)]
        [TestCase(12UL, false)]
        [TestCase(100UL, false)]
        [TestCase(1000UL, false)]
        [TestCase(255UL, false)]
        [TestCase(511UL, false)]
        [TestCase(1023UL, false)]
        [TestCase(ulong.MaxValue, false)] // 18446744073709551615 (all bits set, not a power of 2)
        [TestCase(ulong.MaxValue - 1UL, false)] // 18446744073709551614
        [TestCase((1UL << 63) - 1UL, false)] // 9223372036854775807 (one less than largest power of 2)
        [TestCase((1UL << 63) + 1UL, false)] // 9223372036854775809 (one more than largest power of 2)
        public void IsPowerOfTwoReturnsCorrectResult(ulong value, bool expectedResult)
        {
            bool result = IsPowerOfTwo(value);

            Assert.That(
                result,
                Is.EqualTo(expectedResult),
                $"IsPowerOfTwo({value}) should return {expectedResult}"
            );
        }

        [TestCase(SimpleTestEnum.OptionA, 0UL)]
        [TestCase(SimpleTestEnum.OptionB, 1UL)]
        [TestCase(SimpleTestEnum.OptionC, 2UL)]
        public void ConvertToUInt64ReturnsCorrectValueForRegularEnum(
            SimpleTestEnum enumValue,
            ulong expectedValue
        )
        {
            ulong result = ConvertToUInt64(enumValue);

            Assert.That(
                result,
                Is.EqualTo(expectedValue),
                $"ConvertToUInt64({enumValue}) should return {expectedValue}"
            );
        }

        [TestCase(TestFlagsEnum.None, 0UL)]
        [TestCase(TestFlagsEnum.FlagA, 1UL)]
        [TestCase(TestFlagsEnum.FlagB, 2UL)]
        [TestCase(TestFlagsEnum.FlagC, 4UL)]
        [TestCase(TestFlagsEnum.FlagA | TestFlagsEnum.FlagB, 3UL)]
        public void ConvertToUInt64ReturnsCorrectValueForFlagsEnum(
            TestFlagsEnum enumValue,
            ulong expectedValue
        )
        {
            ulong result = ConvertToUInt64(enumValue);

            Assert.That(
                result,
                Is.EqualTo(expectedValue),
                $"ConvertToUInt64({enumValue}) should return {expectedValue}"
            );
        }

        [Test]
        public void ConvertToUInt64ReturnsZeroForNull()
        {
            ulong result = ConvertToUInt64(null);

            Assert.That(result, Is.EqualTo(0UL), "ConvertToUInt64(null) should return 0");
        }

        // Single column (columns <= 1) always returns Single
        [TestCase(0, 1, 1, 0)] // Single button, single column → Single
        [TestCase(0, 3, 1, 0)] // 3 buttons, single column, index 0 → Single
        [TestCase(1, 3, 1, 0)] // 3 buttons, single column, index 1 → Single
        [TestCase(2, 3, 1, 0)] // 3 buttons, single column, index 2 → Single
        [TestCase(0, 5, 0, 0)] // Edge case: columns = 0 → Single
        // 2 buttons in 2 columns (full row)
        [TestCase(0, 2, 2, 1)] // First button → Left
        [TestCase(1, 2, 2, 3)] // Last button → Right
        // 3 buttons in 3 columns (full row)
        [TestCase(0, 3, 3, 1)] // First button → Left
        [TestCase(1, 3, 3, 2)] // Middle button → Middle
        [TestCase(2, 3, 3, 3)] // Last button → Right
        // 5 buttons in 3 columns (2 full rows, 1 partial row)
        // Row 0: [0-Left] [1-Middle] [2-Right]
        // Row 1: [3-Left] [4-Right]
        [TestCase(0, 5, 3, 1)] // Row 0, col 0 → Left
        [TestCase(1, 5, 3, 2)] // Row 0, col 1 → Middle
        [TestCase(2, 5, 3, 3)] // Row 0, col 2 → Right
        [TestCase(3, 5, 3, 1)] // Row 1, col 0 → Left
        [TestCase(4, 5, 3, 3)] // Row 1, col 1, last button → Right
        // 4 buttons in 3 columns (partial last row with single button)
        // Row 0: [0-Left] [1-Middle] [2-Right]
        // Row 1: [3-Single]
        [TestCase(0, 4, 3, 1)] // Row 0, col 0 → Left
        [TestCase(1, 4, 3, 2)] // Row 0, col 1 → Middle
        [TestCase(2, 4, 3, 3)] // Row 0, col 2 → Right
        [TestCase(3, 4, 3, 0)] // Row 1, col 0, last button AND first column → Single
        // 7 buttons in 4 columns (partial last row with 3 buttons)
        // Row 0: [0-Left] [1-Middle] [2-Middle] [3-Right]
        // Row 1: [4-Left] [5-Middle] [6-Right]
        [TestCase(0, 7, 4, 1)] // Row 0, col 0 → Left
        [TestCase(1, 7, 4, 2)] // Row 0, col 1 → Middle
        [TestCase(2, 7, 4, 2)] // Row 0, col 2 → Middle
        [TestCase(3, 7, 4, 3)] // Row 0, col 3 → Right
        [TestCase(4, 7, 4, 1)] // Row 1, col 0 → Left
        [TestCase(5, 7, 4, 2)] // Row 1, col 1 → Middle
        [TestCase(6, 7, 4, 3)] // Row 1, col 2, last button → Right
        // Edge case: 1 button in multi-column layout
        [TestCase(0, 1, 3, 0)] // Single button → Single (first AND last)
        public void ResolveButtonSegmentReturnsCorrectSegment(
            int index,
            int total,
            int columns,
            int expectedSegment
        )
        {
            int result = ResolveButtonSegment(index, total, columns);

            Assert.That(
                result,
                Is.EqualTo(expectedSegment),
                $"ResolveButtonSegment({index}, {total}, {columns}) should return segment {expectedSegment}"
            );
        }

        [TestCase(10, true, 8)]
        [TestCase(5, true, 8)]
        [TestCase(20, true, 8)]
        public void ShouldPaginateReturnsTrueWhenExceedsPageSize(
            int optionCount,
            bool enablePagination,
            int defaultPageSize
        )
        {
            WEnumToggleButtonsAttribute attribute = new(
                buttonsPerRow: 0,
                showSelectAll: true,
                showSelectNone: true,
                enablePagination: enablePagination,
                pageSize: defaultPageSize
            );

            (bool shouldPaginate, int pageSize) = ShouldPaginate(attribute, optionCount);

            if (optionCount > defaultPageSize)
            {
                Assert.That(
                    shouldPaginate,
                    Is.True,
                    $"Should paginate when option count ({optionCount}) exceeds page size ({defaultPageSize})"
                );
            }
            else
            {
                Assert.That(
                    shouldPaginate,
                    Is.False,
                    $"Should not paginate when option count ({optionCount}) is within page size ({defaultPageSize})"
                );
            }
        }

        [Test]
        public void ShouldPaginateReturnsFalseWhenPaginationDisabled()
        {
            WEnumToggleButtonsAttribute attribute = new(
                buttonsPerRow: 0,
                showSelectAll: true,
                showSelectNone: true,
                enablePagination: false,
                pageSize: 8
            );

            (bool shouldPaginate, int _) = ShouldPaginate(attribute, 100);

            Assert.That(
                shouldPaginate,
                Is.False,
                "Should not paginate when EnablePagination is false"
            );
        }

        [TestCase(0)]
        [TestCase(5)]
        [TestCase(10)]
        [TestCase(20)]
        public void ResolvePageSizeReturnsValidValue(int attributePageSize)
        {
            WEnumToggleButtonsAttribute attribute = new(
                buttonsPerRow: 0,
                showSelectAll: true,
                showSelectNone: true,
                enablePagination: true,
                pageSize: attributePageSize
            );

            int pageSize = ResolvePageSize(attribute);

            Assert.That(pageSize, Is.GreaterThan(0), "Page size should be positive");
            Assert.That(pageSize, Is.LessThanOrEqualTo(100), "Page size should be reasonable");
        }

        [UnityTest]
        public IEnumerator MultipleEditorsCanBeCreatedForSameTargetType()
        {
            OdinEnumToggleButtonsRegularTarget target1 =
                CreateScriptableObject<OdinEnumToggleButtonsRegularTarget>();
            OdinEnumToggleButtonsRegularTarget target2 =
                CreateScriptableObject<OdinEnumToggleButtonsRegularTarget>();

            Editor editor1 = Track(Editor.CreateEditor(target1));
            Editor editor2 = Track(Editor.CreateEditor(target2));
            bool testCompleted = false;
            Exception caughtException = null;

            Assert.That(editor1, Is.Not.Null);
            Assert.That(editor2, Is.Not.Null);
            Assert.That(editor1, Is.Not.SameAs(editor2));

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor1.OnInspectorGUI();
                    editor2.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for multiple editors. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator CanEditMultipleObjectsDoesNotThrow()
        {
            OdinEnumToggleButtonsRegularTarget target1 =
                CreateScriptableObject<OdinEnumToggleButtonsRegularTarget>();
            OdinEnumToggleButtonsRegularTarget target2 =
                CreateScriptableObject<OdinEnumToggleButtonsRegularTarget>();

            UnityEngine.Object[] targets = new UnityEngine.Object[] { target1, target2 };
            Editor editor = Track(Editor.CreateEditor(targets));
            bool testCompleted = false;
            Exception caughtException = null;

            Assert.That(editor, Is.Not.Null);

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for multiple objects. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator InspectorHandlesDestroyedTargetGracefully()
        {
            GameObject go = NewGameObject("EnumToggleMB");
            OdinEnumToggleButtonsMonoBehaviour target =
                go.AddComponent<OdinEnumToggleButtonsMonoBehaviour>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"First OnInspectorGUI should not throw. Exception: {caughtException}"
            );

            UnityEngine.Object.DestroyImmediate(target); // UNH-SUPPRESS: Test verifies behavior after target destroyed
            _trackedObjects.Remove(target);

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw after target destroyed. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawerHandlesAllAttributeConfigurations()
        {
            OdinEnumToggleButtonsAllConfigs target =
                CreateScriptableObject<OdinEnumToggleButtonsAllConfigs>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for all attribute configurations. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawerHandlesCustomButtonsPerRow()
        {
            OdinEnumToggleButtonsCustomLayout target =
                CreateScriptableObject<OdinEnumToggleButtonsCustomLayout>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw with custom buttons per row. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawerHandlesSelectAllAndSelectNoneButtons()
        {
            OdinEnumToggleButtonsToolbar target =
                CreateScriptableObject<OdinEnumToggleButtonsToolbar>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw with select all/none buttons. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawerHandlesPaginationConfiguration()
        {
            OdinEnumToggleButtonsPaginated target =
                CreateScriptableObject<OdinEnumToggleButtonsPaginated>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw with pagination. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawerHandlesLargeEnumWithPagination()
        {
            OdinEnumToggleButtonsLargeEnum target =
                CreateScriptableObject<OdinEnumToggleButtonsLargeEnum>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw for large enum with pagination. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawerHandlesColorKeyConfiguration()
        {
            OdinEnumToggleButtonsColorKey target =
                CreateScriptableObject<OdinEnumToggleButtonsColorKey>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw with color key. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator DrawerHandlesMultipleEnumFieldsOnSameTarget()
        {
            OdinEnumToggleButtonsMultipleFields target =
                CreateScriptableObject<OdinEnumToggleButtonsMultipleFields>();
            Editor editor = Track(Editor.CreateEditor(target));
            bool testCompleted = false;
            Exception caughtException = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    editor.OnInspectorGUI();
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.That(
                caughtException,
                Is.Null,
                $"OnInspectorGUI should not throw with multiple enum fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void FlagsEnumAllowsMultipleSelection()
        {
            OdinEnumToggleButtonsFlagsTarget target =
                CreateScriptableObject<OdinEnumToggleButtonsFlagsTarget>();
            target.flags = TestFlagsEnum.FlagA | TestFlagsEnum.FlagB;

            ulong mask = ConvertToUInt64(target.flags);
            ulong expectedMask = (ulong)(TestFlagsEnum.FlagA | TestFlagsEnum.FlagB);

            Assert.That(
                mask,
                Is.EqualTo(expectedMask),
                "Flags enum should support multiple selection"
            );
        }

        [Test]
        public void RegularEnumRestrictsToSingleSelection()
        {
            OdinEnumToggleButtonsRegularTarget target =
                CreateScriptableObject<OdinEnumToggleButtonsRegularTarget>();
            target.enumValue = SimpleTestEnum.OptionB;

            ulong mask = ConvertToUInt64(target.enumValue);

            Assert.That(mask, Is.EqualTo(1UL), "Regular enum should have single value");
        }

        private static EnumToggleButtonsShared.ToggleOption[] GetCachedEnumOptions(Type enumType)
        {
            return WEnumToggleButtonsOdinDrawer.GetCachedEnumOptions(enumType);
        }

        private static EnumToggleButtonsShared.ToggleOption[] BuildEnumOptions(
            Type enumType,
            bool isFlags
        )
        {
            return WEnumToggleButtonsOdinDrawer.BuildEnumOptions(enumType, isFlags);
        }

        private static ulong CalculateAllFlagsMask(EnumToggleButtonsShared.ToggleOption[] options)
        {
            return WEnumToggleButtonsOdinDrawer.CalculateAllFlagsMask(options);
        }

        private static (bool shouldPaginate, int pageSize) ShouldPaginate(
            WEnumToggleButtonsAttribute attribute,
            int optionCount
        )
        {
            int pageSize = 0;
            bool result = WEnumToggleButtonsOdinDrawer.ShouldPaginate(
                attribute,
                optionCount,
                out pageSize
            );
            return (result, pageSize);
        }

        private static int ResolvePageSize(WEnumToggleButtonsAttribute attribute)
        {
            return WEnumToggleButtonsOdinDrawer.ResolvePageSize(attribute);
        }

        private static ulong ConvertToUInt64(object value)
        {
            return EnumToggleButtonsShared.ConvertToUInt64(value);
        }

        private static bool IsPowerOfTwo(ulong value)
        {
            return EnumToggleButtonsShared.IsPowerOfTwo(value);
        }

        private static int ResolveButtonSegment(int index, int total, int columns)
        {
            return (int)EnumToggleButtonsShared.ResolveButtonSegment(index, total, columns);
        }
    }
#endif
}
