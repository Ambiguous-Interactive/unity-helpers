// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
#if UNITY_EDITOR
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class SerializableCollectionLayoutCalculationTests : CommonTestBase
    {
        [TestCase(-10f, 32f)]
        [TestCase(0f, 32f)]
        [TestCase(100f, 32f)]
        [TestCase(300f, 90f)]
        [TestCase(1000f, 96f)]
        public void DictionaryChildLabelWidthStaysWithinReadableBounds(
            float columnWidth,
            float expected
        )
        {
            Assert.AreEqual(
                expected,
                SerializableDictionaryPropertyDrawer.CalculateRowChildLabelWidth(columnWidth, null),
                0.001f
            );
        }

        [TestCase(10f, 480f, 480f)]
        [TestCase(-10f, 480f, 480f)]
        [TestCase(280f, 0f, 284f)]
        [TestCase(280f, -50f, 284f)]
        [TestCase(280f, 100f, 284f)]
        [TestCase(284f, 900f, 284f)]
        [TestCase(480f, 900f, 480f)]
        public void DictionaryRowWidthPreservesDragFallbackAndWideRows(
            float rowWidth,
            float listContentWidth,
            float expected
        )
        {
            Assert.AreEqual(
                expected,
                SerializableDictionaryPropertyDrawer.ResolveRowContentWidth(
                    rowWidth,
                    listContentWidth
                ),
                0.001f
            );
        }

        [Test]
        public void OrdinaryInspectorUsesItsOwnPendingFoldoutOffsets()
        {
            Assert.AreEqual(
                17.5f,
                SerializableDictionaryPropertyDrawer.ResolvePendingFoldoutToggleOffset(null),
                0.001f
            );
            Assert.AreEqual(
                -5.5f,
                SerializableDictionaryPropertyDrawer.ResolvePendingFoldoutLabelContentOffset(null),
                0.001f
            );
        }

        [Test]
        public void SettingsInspectorUsesItsOwnPendingFoldoutOffsets()
        {
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject serializedObject = TrackDisposable(new SerializedObject(settings));
            SerializedProperty property = serializedObject.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(property != null);
            Assert.AreEqual(
                7.5f,
                SerializableDictionaryPropertyDrawer.ResolvePendingFoldoutToggleOffset(property),
                0.001f
            );
            Assert.AreEqual(
                -3f,
                SerializableDictionaryPropertyDrawer.ResolvePendingFoldoutLabelContentOffset(
                    property
                ),
                0.001f
            );
        }

        [UnityTest]
        public IEnumerator SerializedChildLabelsUseMeasuredDisplayNames()
        {
            FoldoutInteractionSetHost host = CreateScriptableObject<FoldoutInteractionSetHost>();
            UnityHelpersSettings settings = UnityHelpersSettings.instance;
            SerializedObject shortObject = TrackDisposable(new SerializedObject(host));
            SerializedObject longObject = TrackDisposable(new SerializedObject(settings));
            SerializedProperty shortLabel = shortObject.FindProperty(
                nameof(FoldoutInteractionSetHost.set)
            );
            SerializedProperty longLabel = longObject.FindProperty(
                UnityHelpersSettings.SerializedPropertyNames.WButtonCustomColors
            );
            Assert.IsTrue(shortLabel != null);
            Assert.IsTrue(longLabel != null);
            yield return TestIMGUIExecutor.Run(() =>
            {
                float shortWidth = SerializableDictionaryPropertyDrawer.CalculateRowChildLabelWidth(
                    300f,
                    shortLabel
                );
                float longWidth = SerializableDictionaryPropertyDrawer.CalculateRowChildLabelWidth(
                    300f,
                    longLabel
                );
                Assert.Greater(longWidth, shortWidth);
                Assert.GreaterOrEqual(shortWidth, 32f);
                Assert.LessOrEqual(longWidth, 96f);
            });
        }
    }
#endif
}
