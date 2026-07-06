// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

// Odin drawer test template - testing OdinAttributeDrawer implementations

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomDrawers
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using Sirenix.OdinInspector;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.MyFeature;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.SharedEnums;

    /// <summary>
    /// Tests for MyOdinDrawer ensuring MyAttribute works correctly
    /// with Odin Inspector for SerializedMonoBehaviour/SerializedScriptableObject.
    /// </summary>
    [TestFixture]
    public sealed class MyOdinDrawerTests : CommonTestBase
    {
        [Test]
        public void DrawerRegistrationCreatesEditorForScriptableObject()
        {
            OdinMyFeatureTarget target = CreateScriptableObject<OdinMyFeatureTarget>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.IsTrue(editor != null, "Editor should be created");
        }

        [Test]
        public void DrawerRegistrationCreatesEditorForMonoBehaviour()
        {
            OdinMyFeatureMonoBehaviour target = NewGameObject("TestMB")
                .AddComponent<OdinMyFeatureMonoBehaviour>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.IsTrue(editor != null, "Editor should be created for MB");
        }

        [Test]
        public void OnInspectorGuiDoesNotThrowForScriptableObject()
        {
            OdinMyFeatureTarget target = CreateScriptableObject<OdinMyFeatureTarget>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.DoesNotThrow(() => editor.OnInspectorGUI());
        }

        [Test]
        public void OnInspectorGuiDoesNotThrowForMonoBehaviour()
        {
            OdinMyFeatureMonoBehaviour target = NewGameObject("TestMB")
                .AddComponent<OdinMyFeatureMonoBehaviour>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.DoesNotThrow(() => editor.OnInspectorGUI());
        }

        [Test]
        public void OnInspectorGuiHandlesMultipleCalls()
        {
            OdinMyFeatureTarget target = CreateScriptableObject<OdinMyFeatureTarget>();
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.DoesNotThrow(() =>
            {
                editor.OnInspectorGUI();
                editor.OnInspectorGUI();
                editor.OnInspectorGUI();
            });
        }

        [Test]
        [TestCaseSource(nameof(FieldValueTestCases))]
        public void OnInspectorGuiHandlesVariousFieldValues(SimpleTestEnum value)
        {
            OdinMyFeatureTarget target = CreateScriptableObject<OdinMyFeatureTarget>();
            target.myField = value;
            Editor editor = Editor.CreateEditor(target);
            Track(editor);

            Assert.DoesNotThrow(() => editor.OnInspectorGUI());
        }

        private static IEnumerable<TestCaseData> FieldValueTestCases()
        {
            yield return new TestCaseData(SimpleTestEnum.One).SetName("Value.FirstEnumMember");
            yield return new TestCaseData(SimpleTestEnum.Two).SetName("Value.SecondEnumMember");
            yield return new TestCaseData(SimpleTestEnum.Three).SetName("Value.ThirdEnumMember");
            yield return new TestCaseData((SimpleTestEnum)999).SetName("Value.InvalidEnumValue");
        }
    }
#endif
}

// Required Test Categories for Odin Drawers:
// | Category                 | Test Scenarios                                          |
// | Editor creation          | Editor.CreateEditor(target) returns non-null            |
// | ScriptableObject targets | SerializedScriptableObject base class works correctly   |
// | MonoBehaviour targets    | SerializedMonoBehaviour base class works correctly      |
// | No-throw on GUI          | OnInspectorGUI() doesn't throw for valid targets        |
// | Multiple GUI calls       | Repeated OnInspectorGUI() calls don't cause issues      |
// | Various field values     | Different enum values, null references, edge cases      |
// | Multiple fields          | Multiple attributes on same target work together        |
// | Attribute configurations | Different attribute constructor parameters              |
// | Caching behavior         | Multiple instances share caches correctly               |
// | Editor cleanup           | DestroyImmediate(editor) in finally blocks              |
