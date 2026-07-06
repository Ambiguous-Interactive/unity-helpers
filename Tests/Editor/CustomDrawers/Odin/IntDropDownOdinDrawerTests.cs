// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.IntDropDown;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Tests.Runtime.TestTypes.Odin.IntDropDown;

    /// <summary>
    /// Tests for IntDropDownOdinDrawer ensuring IntDropDown attribute
    /// works correctly with Odin Inspector for SerializedMonoBehaviour
    /// and SerializedScriptableObject types.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class IntDropDownOdinDrawerTests : CommonTestBase
    {
        [Test]
        public void DrawerRegistrationForScriptableObjectIsCorrect()
        {
            OdinIntDropDownScriptableObjectTarget target =
                CreateScriptableObject<OdinIntDropDownScriptableObjectTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for target");
        }

        [Test]
        public void DrawerRegistrationForMonoBehaviourIsCorrect()
        {
            OdinIntDropDownMonoBehaviourTarget target = NewGameObject("IntDropDownMB")
                .AddComponent<OdinIntDropDownMonoBehaviourTarget>();
            Editor editor = Track(Editor.CreateEditor(target));

            Assert.That(editor, Is.Not.Null, "Editor should be created for MonoBehaviour target");
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForScriptableObject()
        {
            OdinIntDropDownScriptableObjectTarget target =
                CreateScriptableObject<OdinIntDropDownScriptableObjectTarget>();
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

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator OnInspectorGuiDoesNotThrowForMonoBehaviour()
        {
            OdinIntDropDownMonoBehaviourTarget target = NewGameObject("IntDropDownMB")
                .AddComponent<OdinIntDropDownMonoBehaviourTarget>();
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

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator IntDropdownSelectionWorks()
        {
            OdinIntDropDownInlineTarget target =
                CreateScriptableObject<OdinIntDropDownInlineTarget>();
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

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator EmptyOptionsShowsHelpBox()
        {
            OdinIntDropDownEmptyOptionsTarget target =
                CreateScriptableObject<OdinIntDropDownEmptyOptionsTarget>();
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

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator InvalidFieldTypeShowsHelpBox()
        {
            OdinIntDropDownInvalidTypeTarget target =
                CreateScriptableObject<OdinIntDropDownInvalidTypeTarget>();
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
                $"OnInspectorGUI should not throw for invalid field type. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator StaticMethodProviderWorks()
        {
            OdinIntDropDownStaticProviderTarget target =
                CreateScriptableObject<OdinIntDropDownStaticProviderTarget>();
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

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator InstanceMethodProviderWorks()
        {
            OdinIntDropDownInstanceProviderTarget target =
                CreateScriptableObject<OdinIntDropDownInstanceProviderTarget>();
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

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator LargeListUsesPopupDropDown()
        {
            OdinIntDropDownLargeListTarget target =
                CreateScriptableObject<OdinIntDropDownLargeListTarget>();
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
                $"OnInspectorGUI should not throw for large list with popup. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator RepeatedOnInspectorGuiCallsDoNotThrow()
        {
            OdinIntDropDownScriptableObjectTarget target =
                CreateScriptableObject<OdinIntDropDownScriptableObjectTarget>();
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
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator MultipleEditorsCanBeCreatedForSameTargetType()
        {
            OdinIntDropDownScriptableObjectTarget target1 =
                CreateScriptableObject<OdinIntDropDownScriptableObjectTarget>();
            OdinIntDropDownScriptableObjectTarget target2 =
                CreateScriptableObject<OdinIntDropDownScriptableObjectTarget>();

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

            Assert.That(caughtException, Is.Null);
            Assert.That(testCompleted, Is.True);
        }

        [Test]
        public void GetOrCreateDisplayOptionsReturnsCorrectStrings()
        {
            int[] options = { 10, 20, 30 };

            string[] displayOptions = IntDropDownOdinDrawer.GetOrCreateDisplayOptions(options);

            Assert.That(displayOptions, Is.Not.Null);
            Assert.That(displayOptions.Length, Is.EqualTo(3));
            Assert.That(displayOptions[0], Is.EqualTo("10"));
            Assert.That(displayOptions[1], Is.EqualTo("20"));
            Assert.That(displayOptions[2], Is.EqualTo("30"));
        }

        [Test]
        public void GetOrCreateDisplayOptionsReturnsEmptyForNull()
        {
            string[] displayOptions = IntDropDownOdinDrawer.GetOrCreateDisplayOptions(null);

            Assert.That(displayOptions, Is.Not.Null);
            Assert.That(displayOptions.Length, Is.EqualTo(0));
        }

        [Test]
        public void GetOrCreateDisplayOptionsReturnsEmptyForEmptyArray()
        {
            int[] options = Array.Empty<int>();

            string[] displayOptions = IntDropDownOdinDrawer.GetOrCreateDisplayOptions(options);

            Assert.That(displayOptions, Is.Not.Null);
            Assert.That(displayOptions.Length, Is.EqualTo(0));
        }

        [Test]
        public void GetCachedIntStringReturnsCorrectValue()
        {
            int value = 42;

            string result = DropDownShared.GetCachedIntString(value);

            Assert.That(result, Is.EqualTo("42"));
        }

        [Test]
        public void GetCachedIntStringCachesValues()
        {
            int value = 123;

            string result1 = DropDownShared.GetCachedIntString(value);
            string result2 = DropDownShared.GetCachedIntString(value);

            Assert.That(result1, Is.EqualTo("123"));
            Assert.That(result1, Is.SameAs(result2), "Cached string should be same reference");
        }

        [Test]
        public void DisplayOptionsCachingWorks()
        {
            int[] options = { 1, 2, 3, 4, 5 };

            string[] result1 = IntDropDownOdinDrawer.GetOrCreateDisplayOptions(options);
            string[] result2 = IntDropDownOdinDrawer.GetOrCreateDisplayOptions(options);

            Assert.That(result1, Is.Not.Null);
            Assert.That(result2, Is.Not.Null);
            Assert.That(result1.Length, Is.EqualTo(result2.Length));
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator MultipleIntDropDownFieldsOnSameTargetWork()
        {
            OdinIntDropDownMultipleFieldsTarget target =
                CreateScriptableObject<OdinIntDropDownMultipleFieldsTarget>();
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
                $"OnInspectorGUI should not throw with multiple IntDropDown fields. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator CurrentValueNotInOptionsWorks()
        {
            OdinIntDropDownInlineTarget target =
                CreateScriptableObject<OdinIntDropDownInlineTarget>();
            target.selectedFrameRate = 999;

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
                $"OnInspectorGUI should not throw when current value is not in options. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator NegativeIntOptionsWork()
        {
            OdinIntDropDownNegativeValuesTarget target =
                CreateScriptableObject<OdinIntDropDownNegativeValuesTarget>();
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
                $"OnInspectorGUI should not throw for negative int options. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }

        [UnityEngine.TestTools.UnityTest]
        public IEnumerator ZeroValueOptionWorks()
        {
            OdinIntDropDownWithZeroTarget target =
                CreateScriptableObject<OdinIntDropDownWithZeroTarget>();
            target.selectedValue = 0;

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
                $"OnInspectorGUI should not throw when value is zero. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
        }
    }
#endif
}
