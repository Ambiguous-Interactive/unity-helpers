// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;
    using PlatformPropertyNames = UnityHelpers.Editor.Sprites.TextureSettingsApplierWindow.PlatformOverrideEntry.SerializedPropertyNames;

    [TestFixture]
    [Category("Slow")]
    [Category("Integration")]
    public sealed class TexturePlatformOverrideEntryDrawerTests : CommonTestBase
    {
        private TexturePlatformOverrideEntryTestHost _testHost;
        private SerializedObject _serializedObject;
        private TexturePlatformOverrideEntryDrawer _drawer;

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            _testHost = CreateScriptableObject<TexturePlatformOverrideEntryTestHost>();
            _serializedObject = new SerializedObject(_testHost);
            _drawer = new TexturePlatformOverrideEntryDrawer();
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            _serializedObject?.Dispose();
        }

        [Test]
        public void GetPropertyHeightReturnsPositiveValue()
        {
            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            Assert.IsTrue(0f < height, "Height should be positive");
        }

        [Test]
        public void GetPropertyHeightWithNoApplyFlagsReturnsMinimalHeight()
        {
            _testHost.entry.applyResizeAlgorithm = false;
            _testHost.entry.applyMaxTextureSize = false;
            _testHost.entry.applyFormat = false;
            _testHost.entry.applyCompression = false;
            _testHost.entry.applyCrunchCompression = false;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            Assert.IsTrue(0f < height, "Height should be positive even with no apply flags");
        }

        [Test]
        public void GetPropertyHeightWithApplyFlagsIncreasesHeight()
        {
            _testHost.entry.applyResizeAlgorithm = false;
            _testHost.entry.applyMaxTextureSize = false;
            _testHost.entry.applyFormat = false;
            _testHost.entry.applyCompression = false;
            _testHost.entry.applyCrunchCompression = false;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float heightWithNoFlags = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            _testHost.entry.applyResizeAlgorithm = true;
            _serializedObject.Update();

            float heightWithOneFlag = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            Assert.IsTrue(
                heightWithNoFlags < heightWithOneFlag,
                "Height should increase when apply flag is enabled"
            );
        }

        [Test]
        public void GetPropertyHeightWithAllApplyFlagsReturnsMaximalHeight()
        {
            _testHost.entry.applyResizeAlgorithm = true;
            _testHost.entry.applyMaxTextureSize = true;
            _testHost.entry.applyFormat = true;
            _testHost.entry.applyCompression = true;
            _testHost.entry.applyCrunchCompression = true;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float heightWithAllFlags = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            _testHost.entry.applyResizeAlgorithm = false;
            _testHost.entry.applyMaxTextureSize = false;
            _testHost.entry.applyFormat = false;
            _testHost.entry.applyCompression = false;
            _testHost.entry.applyCrunchCompression = false;
            _serializedObject.Update();

            float heightWithNoFlags = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            Assert.IsTrue(
                heightWithNoFlags < heightWithAllFlags,
                "Height with all flags should be greater than with no flags"
            );
        }

        [Test]
        public void GetPropertyHeightWithEmptyPlatformNameReturnsValidHeight()
        {
            _testHost.entry.platformName = string.Empty;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            Assert.IsTrue(0f < height, "Height should be positive even with empty platform name");
        }

        [Test]
        public void GetPropertyHeightConsistentAcrossMultipleCalls()
        {
            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height1 = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            float height2 = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            float height3 = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            Assert.AreEqual(height1, height2, 0.001f, "Height should be consistent across calls");
            Assert.AreEqual(height2, height3, 0.001f, "Height should be consistent across calls");
        }

        [UnityTest]
        public IEnumerator OnGUIDoesNotThrow()
        {
            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 400, height);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    _drawer.OnGUI(position, entryProperty, GUIContent.none);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnGUIWithAllFlagsEnabledDoesNotThrow()
        {
            _testHost.entry.applyResizeAlgorithm = true;
            _testHost.entry.applyMaxTextureSize = true;
            _testHost.entry.applyFormat = true;
            _testHost.entry.applyCompression = true;
            _testHost.entry.applyCrunchCompression = true;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 400, height);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    _drawer.OnGUI(position, entryProperty, GUIContent.none);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI with all flags should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnGUIWithCustomPlatformDoesNotThrow()
        {
            _testHost.entry.platformName = "MyCustomPlatform";
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 400, height);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    _drawer.OnGUI(position, entryProperty, GUIContent.none);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI with custom platform should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnGUIWithZeroWidthDoesNotThrow()
        {
            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            Rect position = new(0, 0, 0, 100);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    _drawer.OnGUI(position, entryProperty, GUIContent.none);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI with zero width should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator OnGUIWithNarrowWidthDoesNotThrow()
        {
            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 50, height);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    _drawer.OnGUI(position, entryProperty, GUIContent.none);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI with narrow width should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator RepeatedOnGUICallsDoNotThrow()
        {
            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 400, height);
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        _drawer.OnGUI(position, entryProperty, GUIContent.none);
                    }
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"Repeated OnGUI calls should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [Test]
        public void GetPropertyHeightIncrementsCorrectlyPerApplyFlag()
        {
            _testHost.entry.applyResizeAlgorithm = false;
            _testHost.entry.applyMaxTextureSize = false;
            _testHost.entry.applyFormat = false;
            _testHost.entry.applyCompression = false;
            _testHost.entry.applyCrunchCompression = false;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float baseHeight = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            List<float> heights = new();
            heights.Add(baseHeight);

            _testHost.entry.applyResizeAlgorithm = true;
            _serializedObject.Update();
            heights.Add(_drawer.GetPropertyHeight(entryProperty, GUIContent.none));

            _testHost.entry.applyMaxTextureSize = true;
            _serializedObject.Update();
            heights.Add(_drawer.GetPropertyHeight(entryProperty, GUIContent.none));

            _testHost.entry.applyFormat = true;
            _serializedObject.Update();
            heights.Add(_drawer.GetPropertyHeight(entryProperty, GUIContent.none));

            _testHost.entry.applyCompression = true;
            _serializedObject.Update();
            heights.Add(_drawer.GetPropertyHeight(entryProperty, GUIContent.none));

            _testHost.entry.applyCrunchCompression = true;
            _serializedObject.Update();
            heights.Add(_drawer.GetPropertyHeight(entryProperty, GUIContent.none));

            for (int i = 1; i < heights.Count; i++)
            {
                Assert.IsTrue(
                    heights[i - 1] < heights[i],
                    $"Height at index {i} should be greater than height at index {i - 1}"
                );
            }
        }

        [UnityTest]
        public IEnumerator OnGUIForListOfEntriesDoesNotThrow()
        {
            SerializedProperty entriesProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entries)
            );

            int count = entriesProperty.arraySize;
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    float y = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        SerializedProperty element = entriesProperty.GetArrayElementAtIndex(i);
                        float height = _drawer.GetPropertyHeight(element, GUIContent.none);
                        Rect position = new(0, y, 400, height);
                        _drawer.OnGUI(position, element, GUIContent.none);
                        y += height + EditorGUIUtility.standardVerticalSpacing;
                    }
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"OnGUI for list of entries should not throw. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        private static IEnumerable<TestCaseData> KnownPlatformRenderSafetyTestData()
        {
            yield return new TestCaseData("Standalone")
                .Returns(null)
                .SetName("RenderPhaseSafety.KnownPlatformStandalone");
            yield return new TestCaseData("Android")
                .Returns(null)
                .SetName("RenderPhaseSafety.KnownPlatformAndroid");
            yield return new TestCaseData("iPhone")
                .Returns(null)
                .SetName("RenderPhaseSafety.KnownPlatformiPhone");
            yield return new TestCaseData("WebGL")
                .Returns(null)
                .SetName("RenderPhaseSafety.KnownPlatformWebGL");
        }

        [UnityTest]
        [TestCaseSource(nameof(KnownPlatformRenderSafetyTestData))]
        public IEnumerator OnGUIDoesNotModifyKnownPlatformName(string platformName)
        {
            _testHost.entry.platformName = platformName;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 400, height);

            yield return TestIMGUIExecutor.Run(() =>
            {
                _drawer.OnGUI(position, entryProperty, GUIContent.none);
            });

            _serializedObject.ApplyModifiedProperties();
            Assert.That(
                _testHost.entry.platformName,
                Is.EqualTo(platformName),
                $"OnGUI should not modify known platform name '{platformName}' during render"
            );
        }

        [UnityTest]
        public IEnumerator OnGUIDoesNotModifyCustomPlatformName()
        {
            string customName = "MyCustomPlatform";
            _testHost.entry.platformName = customName;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 400, height);

            yield return TestIMGUIExecutor.Run(() =>
            {
                _drawer.OnGUI(position, entryProperty, GUIContent.none);
            });

            _serializedObject.ApplyModifiedProperties();
            Assert.That(
                _testHost.entry.platformName,
                Is.EqualTo(customName),
                "OnGUI should not modify custom platform name during render"
            );
        }

        [UnityTest]
        public IEnumerator OnGUIDoesNotModifyEmptyPlatformName()
        {
            _testHost.entry.platformName = string.Empty;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 400, height);

            yield return TestIMGUIExecutor.Run(() =>
            {
                _drawer.OnGUI(position, entryProperty, GUIContent.none);
            });

            _serializedObject.ApplyModifiedProperties();
            Assert.That(
                _testHost.entry.platformName,
                Is.EqualTo(string.Empty),
                "OnGUI should not modify empty platform name during render"
            );
        }

        [UnityTest]
        public IEnumerator OnGUIDoesNotModifyApplyFlagsDuringRender()
        {
            _testHost.entry.applyResizeAlgorithm = true;
            _testHost.entry.applyMaxTextureSize = false;
            _testHost.entry.applyFormat = true;
            _testHost.entry.applyCompression = false;
            _testHost.entry.applyCrunchCompression = true;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 400, height);

            yield return TestIMGUIExecutor.Run(() =>
            {
                for (int i = 0; i < 5; i++)
                {
                    _drawer.OnGUI(position, entryProperty, GUIContent.none);
                }
            });

            _serializedObject.ApplyModifiedProperties();
            Assert.That(
                _testHost.entry.applyResizeAlgorithm,
                Is.True,
                "OnGUI should not modify applyResizeAlgorithm during render"
            );
            Assert.That(
                _testHost.entry.applyMaxTextureSize,
                Is.False,
                "OnGUI should not modify applyMaxTextureSize during render"
            );
            Assert.That(
                _testHost.entry.applyFormat,
                Is.True,
                "OnGUI should not modify applyFormat during render"
            );
            Assert.That(
                _testHost.entry.applyCompression,
                Is.False,
                "OnGUI should not modify applyCompression during render"
            );
            Assert.That(
                _testHost.entry.applyCrunchCompression,
                Is.True,
                "OnGUI should not modify applyCrunchCompression during render"
            );
        }

        [Test]
        public void GetPropertyHeightWithNullPlatformNameTreatsAsCustomModeDueToSerialization()
        {
            _testHost.entry.platformName = string.Empty;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float emptyStringHeight = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            _testHost.entry.platformName = null;
            _serializedObject.Update();

            SerializedProperty nameProp = entryProperty.FindPropertyRelative(
                PlatformPropertyNames.PlatformName
            );
            TestContext.WriteLine(
                $"Null platformName serialized stringValue: \"{nameProp.stringValue}\""
            );

            float nullPlatformHeight = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            Assert.That(
                nullPlatformHeight,
                Is.EqualTo(emptyStringHeight).Within(0.001f),
                "Null platform name should be treated as empty string by Unity serialization, "
                    + "triggering custom mode (same as explicit empty string)"
            );
        }

        private static IEnumerable<TestCaseData> PlatformNameHeightModeTestData()
        {
            yield return new TestCaseData("Standalone", false).SetName(
                "HeightMode.KnownPlatform.Standalone"
            );
            yield return new TestCaseData("iPhone", false).SetName(
                "HeightMode.KnownPlatform.iPhone"
            );
            yield return new TestCaseData("Android", false).SetName(
                "HeightMode.KnownPlatform.Android"
            );
            yield return new TestCaseData("WebGL", false).SetName("HeightMode.KnownPlatform.WebGL");

            yield return new TestCaseData(string.Empty, true).SetName(
                "HeightMode.EmptyString.TriggersCustomMode"
            );

            yield return new TestCaseData(null, true).SetName(
                "HeightMode.Null.TriggersCustomModeDueToSerialization"
            );

            yield return new TestCaseData("MyCustomPlatform", true).SetName(
                "HeightMode.UnknownPlatform.TriggersCustomMode"
            );
            yield return new TestCaseData("FuturePlatform2099", true).SetName(
                "HeightMode.UnknownPlatform.FuturePlatformTriggersCustomMode"
            );
        }

        [Test]
        [TestCaseSource(nameof(PlatformNameHeightModeTestData))]
        public void GetPropertyHeightCustomModeMatchesPlatformNameType(
            string platformName,
            bool expectCustomMode
        )
        {
            _testHost.entry.platformName = "Standalone";
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float knownPlatformHeight = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            _testHost.entry.platformName = platformName;
            _serializedObject.Update();

            SerializedProperty nameProp = entryProperty.FindPropertyRelative(
                PlatformPropertyNames.PlatformName
            );
            TestContext.WriteLine(
                $"Input platformName: {(platformName == null ? "(null)" : $"\"{platformName}\"")}, "
                    + $"Serialized stringValue: \"{nameProp.stringValue}\""
            );

            float testHeight = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);

            if (expectCustomMode)
            {
                Assert.That(
                    testHeight,
                    Is.GreaterThan(knownPlatformHeight),
                    $"Platform name {(platformName == null ? "(null)" : $"\"{platformName}\"")} "
                        + "should trigger custom mode (extra line for custom name text field)"
                );
            }
            else
            {
                Assert.That(
                    testHeight,
                    Is.EqualTo(knownPlatformHeight).Within(0.001f),
                    $"Known platform \"{platformName}\" should NOT trigger custom mode"
                );
            }
        }

        [UnityTest]
        public IEnumerator RepeatedOnGUICallsDoNotDirtySerializedObject()
        {
            _testHost.entry.platformName = "Standalone";
            _testHost.entry.applyResizeAlgorithm = true;
            _testHost.entry.applyMaxTextureSize = false;
            _serializedObject.Update();

            SerializedProperty entryProperty = _serializedObject.FindProperty(
                nameof(TexturePlatformOverrideEntryTestHost.entry)
            );

            float height = _drawer.GetPropertyHeight(entryProperty, GUIContent.none);
            Rect position = new(0, 0, 400, height);

            yield return TestIMGUIExecutor.Run(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    _drawer.OnGUI(position, entryProperty, GUIContent.none);
                }
            });

            Assert.That(
                _serializedObject.hasModifiedProperties,
                Is.False,
                "Repeated OnGUI calls without user interaction should not dirty the SerializedObject"
            );
        }
    }
#endif
}
