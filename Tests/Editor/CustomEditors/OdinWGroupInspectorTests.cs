// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomEditors
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Editor.Utils.WGroup;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.WGroup;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;

    /// <summary>
    /// Tests for WGroup support in Odin-backed custom inspectors.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class OdinWGroupInspectorTests : BatchedEditorTestBase
    {
        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            WGroupLayoutBuilder.ClearCache();
            WGroupIndentDiagnostics.ResetCounters();
        }

        [TearDown]
        public override void TearDown()
        {
            WGroupIndentDiagnostics.Enabled = false;
            WGroupIndentDiagnostics.GroupNameFilter = null;
            WGroupIndentDiagnostics.ResetCounters();
            WGroupLayoutBuilder.ClearCache();
            base.TearDown();
        }

        [Test]
        public void OdinScriptableObjectWGroupLayoutIncludesGroupedFields()
        {
            OdinWGroupScriptableObjectTarget target =
                CreateScriptableObject<OdinWGroupScriptableObjectTarget>();
            SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.IsTrue(layout.TryGetGroup("stats", out WGroupDefinition group));
            Assert.That(group.DirectPropertyPaths, Does.Contain(nameof(target.health)));
            Assert.That(group.DirectPropertyPaths, Does.Contain(nameof(target.mana)));
            Assert.That(group.DirectPropertyPaths, Does.Contain(nameof(target.stamina)));
            Assert.That(layout.GroupedPaths, Does.Contain(nameof(target.health)));
            Assert.That(layout.GroupedPaths, Does.Contain(nameof(target.mana)));
            Assert.That(layout.GroupedPaths, Does.Contain(nameof(target.stamina)));
            Assert.That(layout.GroupedPaths, Does.Not.Contain(nameof(target.outsideGroup)));
        }

        [Test]
        public void OdinMonoBehaviourWGroupLayoutIncludesGroupedFields()
        {
            OdinWGroupMonoBehaviourTarget target = NewGameObject("OdinWGroup")
                .AddComponent<OdinWGroupMonoBehaviourTarget>();
            SerializedObject serializedObject = new(target);

            WGroupLayout layout = WGroupLayoutBuilder.Build(serializedObject, "m_Script");

            Assert.IsTrue(layout.TryGetGroup("stats", out WGroupDefinition group));
            Assert.That(group.DirectPropertyPaths, Does.Contain(nameof(target.health)));
            Assert.That(group.DirectPropertyPaths, Does.Contain(nameof(target.mana)));
            Assert.That(group.DirectPropertyPaths, Does.Contain(nameof(target.stamina)));
            Assert.That(layout.GroupedPaths, Does.Contain(nameof(target.health)));
            Assert.That(layout.GroupedPaths, Does.Contain(nameof(target.mana)));
            Assert.That(layout.GroupedPaths, Does.Contain(nameof(target.stamina)));
            Assert.That(layout.GroupedPaths, Does.Not.Contain(nameof(target.outsideGroup)));
        }

        [UnityTest]
        public IEnumerator OdinScriptableObjectWGroupInspectorDoesNotThrow()
        {
            OdinWGroupScriptableObjectTarget target =
                CreateScriptableObject<OdinWGroupScriptableObjectTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            Assert.That(editor, Is.TypeOf<WButtonOdinScriptableObjectInspector>());
            bool testCompleted = false;
            Exception caughtException = null;
            WGroupIndentDiagnostics.Enabled = true;
            WGroupIndentDiagnostics.GroupNameFilter = "stats";

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
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
            Assert.That(
                WGroupIndentDiagnostics.DrawPropertyLogCount,
                Is.GreaterThanOrEqualTo(3),
                "Odin scriptable object inspector should draw grouped fields through WGroupGUI."
            );
        }

        [UnityTest]
        public IEnumerator OdinMonoBehaviourWGroupInspectorDoesNotThrow()
        {
            OdinWGroupMonoBehaviourTarget target = NewGameObject("OdinWGroup")
                .AddComponent<OdinWGroupMonoBehaviourTarget>();
            UnityEditor.Editor editor = Track(UnityEditor.Editor.CreateEditor(target));
            Assert.That(editor, Is.TypeOf<WButtonOdinMonoBehaviourInspector>());
            bool testCompleted = false;
            Exception caughtException = null;
            WGroupIndentDiagnostics.Enabled = true;
            WGroupIndentDiagnostics.GroupNameFilter = "stats";

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
                $"OnInspectorGUI should not throw. Exception: {caughtException}"
            );
            Assert.That(testCompleted, Is.True);
            Assert.That(
                WGroupIndentDiagnostics.DrawPropertyLogCount,
                Is.GreaterThanOrEqualTo(3),
                "Odin mono behaviour inspector should draw grouped fields through WGroupGUI."
            );
        }
    }
#endif
}
