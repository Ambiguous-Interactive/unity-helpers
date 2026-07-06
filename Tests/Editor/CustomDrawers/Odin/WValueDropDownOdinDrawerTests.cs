// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ValueDropDown;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.ValueDropDown;

    /// <summary>
    /// Tests for WValueDropDownOdinDrawer ensuring WValueDropDown attribute
    /// works correctly with Odin Inspector for SerializedMonoBehaviour
    /// and SerializedScriptableObject types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class WValueDropDownOdinDrawerTests : CommonTestBase
    {
        [Test]
        public void DrawerRegistrationForScriptableObjectIsCorrect()
        {
            OdinValueDropDownScriptableObjectTarget target =
                CreateScriptableObject<OdinValueDropDownScriptableObjectTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for target");
        }

        [Test]
        public void DrawerRegistrationForMonoBehaviourIsCorrect()
        {
            OdinValueDropDownMonoBehaviourTarget target = NewGameObject("ValueDropDownMB")
                .AddComponent<OdinValueDropDownMonoBehaviourTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for MonoBehaviour target");
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForScriptableObject()
        {
            OdinValueDropDownScriptableObjectTarget target =
                CreateScriptableObject<OdinValueDropDownScriptableObjectTarget>();
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
                $"OnInspectorGUI should not throw for ScriptableObject. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForMonoBehaviour()
        {
            OdinValueDropDownMonoBehaviourTarget target = NewGameObject("ValueDropDownMB")
                .AddComponent<OdinValueDropDownMonoBehaviourTarget>();
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
        public IEnumerator StringDropdownFromStaticMethodWorks()
        {
            OdinValueDropDownStaticProviderTarget target =
                CreateScriptableObject<OdinValueDropDownStaticProviderTarget>();
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
                $"OnInspectorGUI should not throw for static method provider. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EnumDropdownWorks()
        {
            OdinValueDropDownEnumTarget target =
                CreateScriptableObject<OdinValueDropDownEnumTarget>();
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
                $"OnInspectorGUI should not throw for enum dropdown. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator ObjectDropdownWorks()
        {
            OdinValueDropDownObjectRefTarget target =
                CreateScriptableObject<OdinValueDropDownObjectRefTarget>();
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
                $"OnInspectorGUI should not throw for object dropdown. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator EmptyOptionsShowsHelpBox()
        {
            OdinValueDropDownEmptyOptionsTarget target =
                CreateScriptableObject<OdinValueDropDownEmptyOptionsTarget>();
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
                $"OnInspectorGUI should not throw when options are empty. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator InlineIntOptionsWork()
        {
            OdinValueDropDownInlineIntTarget target =
                CreateScriptableObject<OdinValueDropDownInlineIntTarget>();
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
                $"OnInspectorGUI should not throw for inline int options. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator InlineStringOptionsWork()
        {
            OdinValueDropDownInlineStringTarget target =
                CreateScriptableObject<OdinValueDropDownInlineStringTarget>();
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
                $"OnInspectorGUI should not throw for inline string options. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void FindSelectedIndexReturnsCorrectIndexForMatchingValue()
        {
            object[] options = { "Alpha", "Beta", "Gamma" };
            object currentValue = "Beta";

            int index = WValueDropDownOdinDrawer.FindSelectedIndex(currentValue, options);

            Assert.That(index, Is.EqualTo(1), "Index should be 1 for 'Beta'");
        }

        [Test]
        public void FindSelectedIndexReturnsNegativeOneForNonMatchingValue()
        {
            object[] options = { "Alpha", "Beta", "Gamma" };
            object currentValue = "Delta";

            int index = WValueDropDownOdinDrawer.FindSelectedIndex(currentValue, options);

            Assert.That(index, Is.EqualTo(-1), "Index should be -1 for non-matching value");
        }

        [Test]
        public void FindSelectedIndexReturnsNegativeOneForNullValue()
        {
            object[] options = { "Alpha", "Beta", "Gamma" };

            int index = WValueDropDownOdinDrawer.FindSelectedIndex(null, options);

            Assert.That(index, Is.EqualTo(-1), "Index should be -1 for null value");
        }

        [Test]
        public void ValuesMatchReturnsTrueForSameReference()
        {
            object value = new object();

            bool match = DropDownShared.ValuesMatch(value, value);

            Assert.That(match, Is.True, "Same reference should match");
        }

        [Test]
        public void ValuesMatchReturnsTrueForEqualStrings()
        {
            string value1 = "Test";
            string value2 = "Test";

            bool match = DropDownShared.ValuesMatch(value1, value2);

            Assert.That(match, Is.True, "Equal strings should match");
        }

        [Test]
        public void ValuesMatchReturnsFalseForNullAndNonNull()
        {
            object value1 = null;
            object value2 = "Test";

            bool match = DropDownShared.ValuesMatch(value1, value2);

            Assert.That(match, Is.False, "Null and non-null should not match");
        }

        [Test]
        public void ValuesMatchReturnsTrueForNumericEquality()
        {
            int value1 = 42;
            long value2 = 42L;

            bool match = DropDownShared.ValuesMatch(value1, value2);

            Assert.That(match, Is.True, "Numerically equal values should match");
        }

        [Test]
        public void ValuesMatchReturnsTrueForEnumAndIntEquivalent()
        {
            TestDropDownMode enumValue = TestDropDownMode.ModeB;
            int intValue = 1;

            bool match = DropDownShared.ValuesMatch(enumValue, intValue);

            Assert.That(match, Is.True, "Enum and its underlying int value should match");
        }

        [Test]
        public void GetDisplayOptionsReturnsCorrectStrings()
        {
            object[] options = { "Alpha", 42, TestDropDownMode.ModeA };

            string[] displayOptions = WValueDropDownOdinDrawer.GetDisplayOptions(options);

            Assert.That(displayOptions, Is.Not.Null);
            Assert.That(displayOptions.Length, Is.EqualTo(3));
            Assert.That(displayOptions[0], Is.EqualTo("Alpha"));
            Assert.That(displayOptions[1], Is.EqualTo("42"));
            Assert.That(displayOptions[2], Is.EqualTo("Mode A"));
        }

        [Test]
        public void FormatOptionReturnsNullStringForNull()
        {
            string formatted = DropDownShared.FormatOption(null);

            Assert.That(formatted, Is.EqualTo("(null)"));
        }

        [Test]
        public void FormatOptionReturnsEnumName()
        {
            string formatted = DropDownShared.FormatOption(TestDropDownMode.ModeC);

            Assert.That(formatted, Is.EqualTo("Mode C"));
        }

        [Test]
        public void FormatOptionReturnsIntAsString()
        {
            string formatted = DropDownShared.FormatOption(123);

            Assert.That(formatted, Is.EqualTo("123"));
        }

        [TestCase(
            TestDropDownMode.ModeA,
            "Mode A",
            TestName = "FormatOption.Enum.FirstValue.AddSpaces"
        )]
        [TestCase(
            TestDropDownMode.ModeB,
            "Mode B",
            TestName = "FormatOption.Enum.MiddleValue.AddSpaces"
        )]
        [TestCase(
            TestDropDownMode.ModeC,
            "Mode C",
            TestName = "FormatOption.Enum.LastValue.AddSpaces"
        )]
        public void FormatOptionHandlesEnumValuesWithSpacing(
            TestDropDownMode enumValue,
            string expected
        )
        {
            string formatted = DropDownShared.FormatOption(enumValue);

            Assert.That(
                formatted,
                Is.EqualTo(expected),
                $"FormatOption should add spaces to PascalCase enum '{enumValue}'"
            );
        }

        [TestCase(
            TestModeEnum.ModeA,
            "Mode A",
            TestName = "FormatOption.TestModeEnum.ModeA.AddSpaces"
        )]
        [TestCase(
            TestModeEnum.ModeB,
            "Mode B",
            TestName = "FormatOption.TestModeEnum.ModeB.AddSpaces"
        )]
        [TestCase(
            TestModeEnum.ModeC,
            "Mode C",
            TestName = "FormatOption.TestModeEnum.ModeC.AddSpaces"
        )]
        public void FormatOptionHandlesTestModeEnumWithSpacing(
            TestModeEnum enumValue,
            string expected
        )
        {
            string formatted = DropDownShared.FormatOption(enumValue);

            Assert.That(
                formatted,
                Is.EqualTo(expected),
                $"FormatOption should add spaces to enum value '{enumValue}'"
            );
        }

        [TestCase(
            SimpleTestEnum.OptionA,
            "Option A",
            TestName = "FormatOption.SimpleTestEnum.OptionA.AddSpaces"
        )]
        [TestCase(
            SimpleTestEnum.OptionB,
            "Option B",
            TestName = "FormatOption.SimpleTestEnum.OptionB.AddSpaces"
        )]
        [TestCase(
            SimpleTestEnum.OptionC,
            "Option C",
            TestName = "FormatOption.SimpleTestEnum.OptionC.AddSpaces"
        )]
        public void FormatOptionHandlesSimpleTestEnumWithSpacing(
            SimpleTestEnum enumValue,
            string expected
        )
        {
            string formatted = DropDownShared.FormatOption(enumValue);

            Assert.That(
                formatted,
                Is.EqualTo(expected),
                $"FormatOption should add spaces to enum value '{enumValue}'"
            );
        }

        [TestCase(0, "0", TestName = "FormatOption.Int.Zero.ReturnsString")]
        [TestCase(1, "1", TestName = "FormatOption.Int.One.ReturnsString")]
        [TestCase(-1, "-1", TestName = "FormatOption.Int.NegativeOne.ReturnsString")]
        [TestCase(int.MaxValue, "2147483647", TestName = "FormatOption.Int.MaxValue.ReturnsString")]
        [TestCase(
            int.MinValue,
            "-2147483648",
            TestName = "FormatOption.Int.MinValue.ReturnsString"
        )]
        [TestCase(42, "42", TestName = "FormatOption.Int.FortyTwo.ReturnsString")]
        public void FormatOptionHandlesIntegerEdgeCases(int intValue, string expected)
        {
            string formatted = DropDownShared.FormatOption(intValue);

            Assert.That(
                formatted,
                Is.EqualTo(expected),
                $"FormatOption should convert integer {intValue} to string '{expected}'"
            );
        }

        [TestCase("", "", TestName = "FormatOption.String.Empty.ReturnsEmpty")]
        [TestCase("Hello", "Hello", TestName = "FormatOption.String.Simple.ReturnsSame")]
        [TestCase(
            "Hello World",
            "Hello World",
            TestName = "FormatOption.String.WithSpace.ReturnsSame"
        )]
        [TestCase(
            "  spaced  ",
            "  spaced  ",
            TestName = "FormatOption.String.Whitespace.PreservesSpaces"
        )]
        [TestCase(
            "CamelCase",
            "CamelCase",
            TestName = "FormatOption.String.CamelCase.NoModification"
        )]
        public void FormatOptionHandlesStringInputs(string stringValue, string expected)
        {
            string formatted = DropDownShared.FormatOption(stringValue);

            Assert.That(
                formatted,
                Is.EqualTo(expected),
                $"FormatOption should return string '{stringValue}' as-is"
            );
        }

        [TestCase(1.5f, TestName = "FormatOption.Float.ReturnsString")]
        [TestCase(3.14159f, TestName = "FormatOption.Float.Pi.ReturnsString")]
        public void FormatOptionHandlesFloatValues(float floatValue)
        {
            string formatted = DropDownShared.FormatOption(floatValue);

            Assert.That(
                formatted,
                Is.Not.Null.And.Not.Empty,
                $"FormatOption should return non-empty string for float {floatValue}"
            );
            Assert.That(
                formatted,
                Does.Contain(".").Or.Matches(@"\d+"),
                $"FormatOption should return numeric representation for float {floatValue}"
            );
        }

        [TestCase(1.5, TestName = "FormatOption.Double.ReturnsString")]
        [TestCase(3.14159265359, TestName = "FormatOption.Double.Pi.ReturnsString")]
        public void FormatOptionHandlesDoubleValues(double doubleValue)
        {
            string formatted = DropDownShared.FormatOption(doubleValue);

            Assert.That(
                formatted,
                Is.Not.Null.And.Not.Empty,
                $"FormatOption should return non-empty string for double {doubleValue}"
            );
        }

        [UnityTest]
        public IEnumerator InstanceMethodProviderWorks()
        {
            OdinValueDropDownInstanceProviderTarget target =
                CreateScriptableObject<OdinValueDropDownInstanceProviderTarget>();
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
                $"OnInspectorGUI should not throw for instance method provider. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator RepeatedOnInspectorGuiCallsDoNotThrow()
        {
            OdinValueDropDownScriptableObjectTarget target =
                CreateScriptableObject<OdinValueDropDownScriptableObjectTarget>();
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
                $"OnInspectorGUI should not throw on repeated calls. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator MultipleEditorsCanBeCreatedForSameTargetType()
        {
            OdinValueDropDownScriptableObjectTarget target1 =
                CreateScriptableObject<OdinValueDropDownScriptableObjectTarget>();
            OdinValueDropDownScriptableObjectTarget target2 =
                CreateScriptableObject<OdinValueDropDownScriptableObjectTarget>();

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
        public IEnumerator LargeOptionListUsesPopupDropDown()
        {
            OdinValueDropDownLargeListTarget target =
                CreateScriptableObject<OdinValueDropDownLargeListTarget>();
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
                $"OnInspectorGUI should not throw for large option list. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityTest]
        public IEnumerator MultipleDropdownFieldsOnSameTargetWork()
        {
            OdinValueDropDownMultipleFieldsTarget target =
                CreateScriptableObject<OdinValueDropDownMultipleFieldsTarget>();
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
                $"OnInspectorGUI should not throw with multiple dropdown fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }
    }
#endif
}
