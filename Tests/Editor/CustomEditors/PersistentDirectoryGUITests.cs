// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System;
    using System.Collections;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.CustomEditors;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;
    using WallstopStudios.UnityHelpers.Tests.EditorFramework;

    [TestFixture]
    [Category("Slow")]
    [Category("Integration")]
    public sealed class PersistentDirectoryGUITests : CommonTestBase
    {
        private const string TestToolName = "PersistentDirectoryGUITests";
        private const string TestContextKey = "TestContext";

        [Test]
        public void GetPathSelectorHeightReturnsPositiveValue()
        {
            float height = PersistentDirectoryGUI.GetPathSelectorHeight(
                TestToolName,
                TestContextKey
            );

            Assert.IsTrue(0f <= height, "Height should be non-negative");
        }

        [Test]
        public void GetPathSelectorHeightWithoutHistoryReturnsMinimumHeight()
        {
            float height = PersistentDirectoryGUI.GetPathSelectorHeight(
                TestToolName,
                TestContextKey,
                displayFrequentPaths: false
            );

            Assert.AreEqual(
                EditorGUIUtility.singleLineHeight,
                height,
                0.01f,
                "Without history display, should return single line height"
            );
        }

        [Test]
        public void GetDrawFrequentPathsHeightReturnsNonNegative()
        {
            float height = PersistentDirectoryGUI.GetDrawFrequentPathsHeight(
                TestToolName,
                TestContextKey
            );

            Assert.IsTrue(0f <= height, "Height should be non-negative");
        }

        [Test]
        public void GetDrawFrequentPathsHeightEditorGUIReturnsNonNegative()
        {
            float height = PersistentDirectoryGUI.GetDrawFrequentPathsHeightEditorGUI(
                TestToolName,
                TestContextKey
            );

            Assert.IsTrue(0f <= height, "Height should be non-negative");
        }

        [Test]
        [TestCase(1, TestName = "TopN.One")]
        [TestCase(3, TestName = "TopN.Three")]
        [TestCase(5, TestName = "TopN.Five")]
        [TestCase(10, TestName = "TopN.Ten")]
        public void GetDrawFrequentPathsHeightRespectsTopN(int topN)
        {
            float height = PersistentDirectoryGUI.GetDrawFrequentPathsHeight(
                TestToolName,
                TestContextKey,
                allowExpansion: false,
                topN: topN
            );

            Assert.IsTrue(0f <= height, $"Height should be non-negative for topN={topN}");
        }

        [Test]
        public void GetDrawFrequentPathsHeightWithExpansionDisabledReturnsNonNegative()
        {
            float height = PersistentDirectoryGUI.GetDrawFrequentPathsHeight(
                TestToolName,
                TestContextKey,
                allowExpansion: false
            );

            Assert.IsTrue(0f <= height, "Height should be non-negative with expansion disabled");
        }

        [Test]
        public void GetPathSelectorStringHeightWithNullPropertyReturnsSingleLineHeight()
        {
            float height = PersistentDirectoryGUI.GetPathSelectorStringHeight(null, TestToolName);

            Assert.AreEqual(
                EditorGUIUtility.singleLineHeight,
                height,
                0.01f,
                "Null property should return single line height"
            );
        }

        [Test]
        public void GetPathSelectorStringHeightWithNonStringPropertyReturnsSingleLineHeight()
        {
            PersistentDirectoryGUITestAsset testAsset =
                CreateScriptableObject<PersistentDirectoryGUITestAsset>();
            using SerializedObject serializedObject = new(testAsset);
            SerializedProperty intProperty = serializedObject.FindProperty(
                nameof(PersistentDirectoryGUITestAsset.intValue)
            );

            float height = PersistentDirectoryGUI.GetPathSelectorStringHeight(
                intProperty,
                TestToolName
            );

            Assert.AreEqual(
                EditorGUIUtility.singleLineHeight,
                height,
                0.01f,
                "Non-string property should return single line height"
            );
        }

        [Test]
        public void GetPathSelectorStringHeightWithStringPropertyReturnsPositiveHeight()
        {
            PersistentDirectoryGUITestAsset testAsset =
                CreateScriptableObject<PersistentDirectoryGUITestAsset>();
            using SerializedObject serializedObject = new(testAsset);
            SerializedProperty stringProperty = serializedObject.FindProperty(
                nameof(PersistentDirectoryGUITestAsset.stringPath)
            );

            float height = PersistentDirectoryGUI.GetPathSelectorStringHeight(
                stringProperty,
                TestToolName
            );

            Assert.IsTrue(0f < height, "String property should return positive height");
        }

        [UnityTest]
        public IEnumerator PathSelectorStringHandlesNullPropertyGracefully()
        {
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    PersistentDirectoryGUI.PathSelectorString(
                        null,
                        TestToolName,
                        "Test Label",
                        new GUIContent("Test")
                    );
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"PathSelectorString should handle null property gracefully. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator PathSelectorObjectHandlesNullPropertyGracefully()
        {
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    PersistentDirectoryGUI.PathSelectorObject(
                        null,
                        TestToolName,
                        "Test Label",
                        new GUIContent("Test")
                    );
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"PathSelectorObject should handle null property gracefully. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator PathSelectorStringArrayHandlesNullPropertyGracefully()
        {
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    PersistentDirectoryGUI.PathSelectorStringArray(null, TestToolName);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"PathSelectorStringArray should handle null property gracefully. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator PathSelectorObjectArrayHandlesNullPropertyGracefully()
        {
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    PersistentDirectoryGUI.PathSelectorObjectArray(null, TestToolName);
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"PathSelectorObjectArray should handle null property gracefully. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator PathSelectorStringWithNonStringPropertyDoesNotThrow()
        {
            PersistentDirectoryGUITestAsset testAsset =
                CreateScriptableObject<PersistentDirectoryGUITestAsset>();
            using SerializedObject serializedObject = new(testAsset);
            SerializedProperty intProperty = serializedObject.FindProperty(
                nameof(PersistentDirectoryGUITestAsset.intValue)
            );
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    PersistentDirectoryGUI.PathSelectorString(
                        intProperty,
                        TestToolName,
                        "Test Label",
                        new GUIContent("Test")
                    );
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"PathSelectorString should handle non-string property gracefully. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator PathSelectorObjectWithNonObjectPropertyDoesNotThrow()
        {
            PersistentDirectoryGUITestAsset testAsset =
                CreateScriptableObject<PersistentDirectoryGUITestAsset>();
            using SerializedObject serializedObject = new(testAsset);
            SerializedProperty stringProperty = serializedObject.FindProperty(
                nameof(PersistentDirectoryGUITestAsset.stringPath)
            );
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    PersistentDirectoryGUI.PathSelectorObject(
                        stringProperty,
                        TestToolName,
                        "Test Label",
                        new GUIContent("Test")
                    );
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"PathSelectorObject should handle non-object property gracefully. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator DrawFrequentPathsHandlesNullCallbackGracefully()
        {
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                LogAssert.Expect(
                    LogType.Error,
                    "PersistentDirectoryGUI.DrawFrequentPaths: onPathClickedFromHistory callback cannot be null."
                );

                try
                {
                    PersistentDirectoryGUI.DrawFrequentPaths(
                        TestToolName,
                        TestContextKey,
                        onPathClickedFromHistory: null
                    );
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                "DrawFrequentPaths should handle null callback gracefully (logs error instead of throwing). "
                    + $"Expected: no exception, error logged. Actual: Exception={caughtException}"
            );
            Assert.IsTrue(
                testCompleted,
                "Test should complete successfully. Expected: testCompleted=true. Actual: testCompleted=false"
            );
        }

        [UnityTest]
        public IEnumerator DrawFrequentPathsWithEditorGUIHandlesNullCallbackGracefully()
        {
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                LogAssert.Expect(
                    LogType.Error,
                    "PersistentDirectoryGUI.DrawFrequentPathsWithEditorGUI: onPathClickedFromHistory callback cannot be null."
                );

                try
                {
                    float currentY = 0f;
                    Rect parentRect = new(0, 0, 400, 300);
                    PersistentDirectoryGUI.DrawFrequentPathsWithEditorGUI(
                        parentRect,
                        ref currentY,
                        TestToolName,
                        TestContextKey,
                        onPathClickedFromHistory: null
                    );
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                "DrawFrequentPathsWithEditorGUI should handle null callback gracefully (logs error instead of throwing). "
                    + $"Expected: no exception, error logged. Actual: Exception={caughtException}"
            );
            Assert.IsTrue(
                testCompleted,
                "Test should complete successfully. Expected: testCompleted=true. Actual: testCompleted=false"
            );
        }

        [UnityTest]
        public IEnumerator PathSelectorWithValidCallbackDoesNotThrow()
        {
            Exception caughtException = null;
            bool testCompleted = false;
            string selectedPath = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                try
                {
                    PersistentDirectoryGUI.PathSelector(
                        new GUIContent("Test Path"),
                        "Assets/TestPath",
                        TestToolName,
                        TestContextKey,
                        path => selectedPath = path
                    );
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"PathSelector should not throw with valid callback. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator PathSelectorWithNullCallbackDoesNotThrow()
        {
            Exception caughtException = null;
            bool testCompleted = false;

            yield return TestIMGUIExecutor.Run(() =>
            {
                LogAssert.Expect(
                    LogType.Error,
                    "PersistentDirectoryGUI.PathSelector: onPathChosen callback cannot be null."
                );

                try
                {
                    PersistentDirectoryGUI.PathSelector(
                        new GUIContent("Test Path"),
                        "Assets/TestPath",
                        TestToolName,
                        TestContextKey,
                        onPathChosen: null
                    );
                    testCompleted = true;
                }
                catch (Exception ex)
                {
                    caughtException = ex;
                }
            });

            Assert.IsTrue(
                caughtException == null,
                $"PathSelector should handle null callback gracefully. Exception: {caughtException}"
            );
            Assert.IsTrue(testCompleted, "Test should complete successfully");
        }

        [UnityTest]
        public IEnumerator PathSelectorReturnsCurrentPathWhenNullCallback()
        {
            string currentPath = "Assets/TestPath";
            string returnedPath = null;

            yield return TestIMGUIExecutor.Run(() =>
            {
                LogAssert.Expect(
                    LogType.Error,
                    "PersistentDirectoryGUI.PathSelector: onPathChosen callback cannot be null."
                );

                returnedPath = PersistentDirectoryGUI.PathSelector(
                    new GUIContent("Test Path"),
                    currentPath,
                    TestToolName,
                    TestContextKey,
                    onPathChosen: null
                );
            });

            Assert.AreEqual(
                currentPath,
                returnedPath,
                "PathSelector should return current path when callback is null"
            );
        }

        [Test]
        [TestCase("", TestName = "EmptyToolName")]
        [TestCase("   ", TestName = "WhitespaceToolName")]
        public void GetDrawFrequentPathsHeightHandlesEdgeCaseToolNames(string toolName)
        {
            float height = PersistentDirectoryGUI.GetDrawFrequentPathsHeight(
                toolName,
                TestContextKey
            );

            Assert.IsTrue(0f <= height, "Height should be non-negative for edge case tool names");
        }

        [Test]
        [TestCase("", TestName = "EmptyContextKey")]
        [TestCase("   ", TestName = "WhitespaceContextKey")]
        public void GetDrawFrequentPathsHeightHandlesEdgeCaseContextKeys(string contextKey)
        {
            float height = PersistentDirectoryGUI.GetDrawFrequentPathsHeight(
                TestToolName,
                contextKey
            );

            Assert.IsTrue(0f <= height, "Height should be non-negative for edge case context keys");
        }

        [Test]
        public void GetPathSelectorHeightConsistentAcrossMultipleCalls()
        {
            float height1 = PersistentDirectoryGUI.GetPathSelectorHeight(
                TestToolName,
                TestContextKey
            );
            float height2 = PersistentDirectoryGUI.GetPathSelectorHeight(
                TestToolName,
                TestContextKey
            );
            float height3 = PersistentDirectoryGUI.GetPathSelectorHeight(
                TestToolName,
                TestContextKey
            );

            Assert.AreEqual(height1, height2, 0.001f, "Height should be consistent across calls");
            Assert.AreEqual(height2, height3, 0.001f, "Height should be consistent across calls");
        }
    }
#endif
}
