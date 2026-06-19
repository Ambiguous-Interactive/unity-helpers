// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Tools;
    using WallstopStudios.UnityHelpers.Editor.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class ManualRecompileTests
    {
        // Temp recompile fixtures live under Assets/ (always writable and AssetDatabase-visible),
        // NOT next to this test's own source. When the package is CONSUMED -- CI's "file:" local
        // package (project nested at <root>/.artifacts/..., package at file:<root>), a registry
        // package, or Library/PackageCache -- the package source folder is outside the project
        // root or read-only, so creating + recompiling temp scripts there is impossible. An
        // Assets/ path is layout-independent and removes the prior DirectoryHelper resolution that
        // returned empty in CI and threw from this fixture's static initializer (failing all 17
        // tests in OneTimeSetUp).
        private const string TempFolderRelativePath = "Assets/__ManualRecompileTests__";

        private readonly List<string> createdAssetPaths = new();

        [TearDown]
        public void TearDown()
        {
            try
            {
                foreach (string assetPath in createdAssetPaths)
                {
                    try
                    {
                        AssetDatabase.DeleteAsset(assetPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to delete asset '{assetPath}': {ex.Message}");
                    }
                }

                createdAssetPaths.Clear();
            }
            finally
            {
                try
                {
                    // Reset test state hooks - always do this even if asset cleanup fails
                    ManualRecompile.SkipCompilationRequestForTests = false;
                    ManualRecompile.IsCompilationPendingEvaluator = null;
                    ManualRecompile.AssetsRefreshedForTests = null;
                    ManualRecompile.CompilationRequestedForTests = null;
                }
                finally
                {
                    // Clean up temp folder - always do this even if state reset fails
                    string tempFolderAbsolutePath = GetAbsolutePath(TempFolderRelativePath);

                    try
                    {
                        if (Directory.Exists(tempFolderAbsolutePath))
                        {
                            FileUtil.DeleteFileOrDirectory(tempFolderAbsolutePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"Failed to delete temp folder '{tempFolderAbsolutePath}': {ex.Message}"
                        );
                    }

                    try
                    {
                        string tempFolderMetaPath = tempFolderAbsolutePath + ".meta";

                        if (File.Exists(tempFolderMetaPath))
                        {
                            FileUtil.DeleteFileOrDirectory(tempFolderMetaPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to delete temp folder meta: {ex.Message}");
                    }

                    try
                    {
                        AssetDatabaseBatchHelper.RefreshIfNotBatching();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"Failed to refresh asset database in teardown: {ex.Message}"
                        );
                    }
                }
            }
        }

        [Test]
        public void RequestRefreshesAssetDatabaseBeforeCompile()
        {
            string className = $"ManualRecompileTemp_{Guid.NewGuid():N}";
            string assetRelativePath = $"{TempFolderRelativePath}/{className}.cs";
            string absolutePath = GetAbsolutePath(assetRelativePath);

            EnsureParentDirectoryExists(absolutePath);

            AssetDatabase.DisallowAutoRefresh();

            try
            {
                File.WriteAllText(
                    absolutePath,
                    "using UnityEngine;\r\n"
                        + $"public sealed class {className} : MonoBehaviour {{ }}"
                );
                createdAssetPaths.Add(assetRelativePath);

                MonoScript scriptBeforeRefresh = AssetDatabase.LoadAssetAtPath<MonoScript>(
                    assetRelativePath
                );
                Assert.IsTrue(
                    scriptBeforeRefresh == null,
                    $"Expected new script to remain hidden until we refresh. Asset path: '{assetRelativePath}', Absolute path: '{absolutePath}', File exists: {File.Exists(absolutePath)}"
                );

                ManualRecompile.SkipCompilationRequestForTests = true;
                ManualRecompile.IsCompilationPendingEvaluator = () => false;

                // Track that refresh was called (we can't reliably assert inside the callback
                // because AssetDatabase.Refresh may not complete immediately in all Unity versions)
                bool refreshCallbackInvoked = false;
                ManualRecompile.AssetsRefreshedForTests = () =>
                {
                    refreshCallbackInvoked = true;
                };

                LogAssert.Expect(
                    LogType.Log,
                    new Regex(
                        "Asset database refreshed; compilation request skipped",
                        RegexOptions.IgnoreCase
                    )
                );

                ManualRecompile.RequestFromMenu();

                // Verify the callback was invoked
                Assert.IsTrue(
                    refreshCallbackInvoked,
                    "AssetsRefreshedForTests callback should have been invoked"
                );

                // Check if the script is visible after the request completes
                // Note: In some Unity versions with DisallowAutoRefresh, the asset may not be
                // immediately visible even after Refresh with ForceSynchronousImport
                MonoScript scriptAfterRefresh = AssetDatabase.LoadAssetAtPath<MonoScript>(
                    assetRelativePath
                );

                // If not visible, try allowing auto refresh and checking again
                if (scriptAfterRefresh == null)
                {
                    AssetDatabase.AllowAutoRefresh();
                    AssetDatabaseBatchHelper.RefreshIfNotBatching(
                        ImportAssetOptions.ForceSynchronousImport
                    );
                    scriptAfterRefresh = AssetDatabase.LoadAssetAtPath<MonoScript>(
                        assetRelativePath
                    );
                }

                Assert.IsTrue(
                    scriptAfterRefresh != null,
                    $"Manual recompile should make the new script visible. Asset path: '{assetRelativePath}', "
                        + $"File exists: {File.Exists(absolutePath)}, "
                        + $"EditorApplication.isCompiling: {EditorApplication.isCompiling}"
                );
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
            }
        }

        [Test]
        public void RequestSkipsWhenCompilationIsInProgress()
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => true;
            ManualRecompile.SkipCompilationRequestForTests = true;

            bool compileRequested = false;
            ManualRecompile.CompilationRequestedForTests = () => compileRequested = true;

            LogAssert.Expect(
                LogType.Log,
                new Regex("Script compilation already in progress", RegexOptions.IgnoreCase)
            );

            ManualRecompile.RequestFromShortcut();

            Assert.IsFalse(
                compileRequested,
                "Manual compile requests should be skipped when Unity is already compiling."
            );
        }

        [Test]
        public void RequestFromMenuSkipsWhenCompilationIsInProgress()
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => true;
            ManualRecompile.SkipCompilationRequestForTests = true;

            bool assetsRefreshed = false;
            bool compileRequested = false;
            ManualRecompile.AssetsRefreshedForTests = () => assetsRefreshed = true;
            ManualRecompile.CompilationRequestedForTests = () => compileRequested = true;

            LogAssert.Expect(
                LogType.Log,
                new Regex("Script compilation already in progress", RegexOptions.IgnoreCase)
            );

            ManualRecompile.RequestFromMenu();

            Assert.IsFalse(
                assetsRefreshed,
                "Asset refresh should be skipped when compilation is in progress."
            );
            Assert.IsFalse(
                compileRequested,
                "Compilation request should be skipped when compilation is in progress."
            );
        }

        [Test]
        public void RequestInvokesAssetsRefreshedCallbackWhenNotCompiling()
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => false;
            ManualRecompile.SkipCompilationRequestForTests = true;

            bool assetsRefreshed = false;
            ManualRecompile.AssetsRefreshedForTests = () => assetsRefreshed = true;

            LogAssert.Expect(
                LogType.Log,
                new Regex("Asset database refreshed", RegexOptions.IgnoreCase)
            );

            ManualRecompile.RequestFromMenu();

            Assert.IsTrue(
                assetsRefreshed,
                "AssetsRefreshedForTests callback should be invoked when not compiling."
            );
        }

        [Test]
        public void RequestInvokesCompilationCallbackWhenSkipFlagIsFalse()
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => false;
            ManualRecompile.SkipCompilationRequestForTests = false;

            bool compileRequested = false;
            ManualRecompile.CompilationRequestedForTests = () => compileRequested = true;

            LogAssert.Expect(
                LogType.Log,
                new Regex(
                    "Refreshed assets and requested script compilation",
                    RegexOptions.IgnoreCase
                )
            );

            ManualRecompile.RequestFromMenu();

            Assert.IsTrue(
                compileRequested,
                "CompilationRequestedForTests callback should be invoked when SkipCompilationRequestForTests is false."
            );
        }

        [TestCase(true, false, TestName = "CompilationPendingPreventsRequest")]
        [TestCase(false, true, TestName = "NoCompilationPendingAllowsRequest")]
        public void RequestBehaviorDependsOnCompilationState(bool isCompiling, bool expectRefresh)
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => isCompiling;
            ManualRecompile.SkipCompilationRequestForTests = true;

            bool assetsRefreshed = false;
            ManualRecompile.AssetsRefreshedForTests = () => assetsRefreshed = true;

            if (isCompiling)
            {
                LogAssert.Expect(
                    LogType.Log,
                    new Regex("Script compilation already in progress", RegexOptions.IgnoreCase)
                );
            }
            else
            {
                LogAssert.Expect(
                    LogType.Log,
                    new Regex("Asset database refreshed", RegexOptions.IgnoreCase)
                );
            }

            ManualRecompile.RequestFromMenu();

            Assert.AreEqual(
                expectRefresh,
                assetsRefreshed,
                $"Assets refresh state mismatch. IsCompiling: {isCompiling}, Expected refresh: {expectRefresh}, Actual refresh: {assetsRefreshed}"
            );
        }

        [Test]
        public void IsCompilationPendingEvaluatorSetToNullRestoresDefault()
        {
            Func<bool> customEvaluator = () => true;
            ManualRecompile.IsCompilationPendingEvaluator = customEvaluator;

            ManualRecompile.IsCompilationPendingEvaluator = null;

            ManualRecompile.SkipCompilationRequestForTests = true;

            bool assetsRefreshed = false;
            ManualRecompile.AssetsRefreshedForTests = () => assetsRefreshed = true;

            ManualRecompile.RequestFromMenu();

            bool compilingAtTimeOfTest = EditorApplication.isCompiling;
            if (compilingAtTimeOfTest)
            {
                Assert.IsFalse(
                    assetsRefreshed,
                    "When evaluator reset to default and Unity is compiling, refresh should be skipped."
                );
            }
        }

        [Test]
        public void IsCompilationPendingHandlesNullEvaluatorGracefully()
        {
            // Force the evaluator to null directly to test defensive check
            ManualRecompile.isCompilationPendingEvaluator = null;

            ManualRecompile.SkipCompilationRequestForTests = true;
            bool assetsRefreshed = false;
            ManualRecompile.AssetsRefreshedForTests = () => assetsRefreshed = true;

            LogAssert.Expect(
                LogType.Warning,
                new Regex("Compilation pending evaluator is null", RegexOptions.IgnoreCase)
            );

            // After resetting to default, the evaluator will check EditorApplication.isCompiling.
            // We need to conditionally expect different log messages based on Unity's actual state.
            bool wasCompilingAtTimeOfCall = EditorApplication.isCompiling;
            if (wasCompilingAtTimeOfCall)
            {
                LogAssert.Expect(
                    LogType.Log,
                    new Regex("compilation already in progress", RegexOptions.IgnoreCase)
                );
            }
            else
            {
                LogAssert.Expect(
                    LogType.Log,
                    new Regex("Asset database refreshed", RegexOptions.IgnoreCase)
                );
            }

            ManualRecompile.RequestFromMenu();

            if (wasCompilingAtTimeOfCall)
            {
                Assert.IsFalse(
                    assetsRefreshed,
                    "When evaluator reset to default and Unity is compiling, refresh should be skipped."
                );
            }
            else
            {
                Assert.IsTrue(
                    assetsRefreshed,
                    "Request should proceed after restoring null evaluator to default when Unity is not compiling"
                );
            }
        }

        [Test]
        public void SkipCompilationFlagIsResetEvenIfCallbackThrows()
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => false;
            ManualRecompile.SkipCompilationRequestForTests = true;

            bool assetsRefreshed = false;
            ManualRecompile.AssetsRefreshedForTests = () =>
            {
                assetsRefreshed = true;
                throw new InvalidOperationException("Test exception from callback");
            };

            bool caughtException = false;
            try
            {
                ManualRecompile.RequestFromMenu();
            }
            catch (InvalidOperationException ex)
            {
                caughtException = true;
                Assert.AreEqual(
                    "Test exception from callback",
                    ex.Message,
                    "Exception message should match the thrown exception"
                );
            }

            Assert.IsTrue(caughtException, "Expected InvalidOperationException to be thrown");
            Assert.IsTrue(assetsRefreshed, "Callback should have been invoked");
            Assert.IsFalse(
                ManualRecompile.SkipCompilationRequestForTests,
                $"Skip flag should be reset even when callback throws. "
                    + $"AssetsRefreshed: {assetsRefreshed}, CaughtException: {caughtException}"
            );
        }

        [Test]
        public void CompilationCallbackExceptionDoesNotCorruptState()
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => false;
            ManualRecompile.SkipCompilationRequestForTests = false;

            bool compileRequested = false;
            ManualRecompile.CompilationRequestedForTests = () =>
            {
                compileRequested = true;
                throw new InvalidOperationException("Test exception from compilation callback");
            };

            try
            {
                ManualRecompile.RequestFromMenu();
            }
            catch (InvalidOperationException)
            {
                // Expected exception from callback
            }

            Assert.IsTrue(compileRequested, "Compilation callback should have been invoked");

            // Verify state is still clean for next request
            ManualRecompile.SkipCompilationRequestForTests = true;
            bool secondRequestRefreshed = false;
            ManualRecompile.AssetsRefreshedForTests = () => secondRequestRefreshed = true;
            ManualRecompile.CompilationRequestedForTests = null;

            LogAssert.Expect(
                LogType.Log,
                new Regex("Asset database refreshed", RegexOptions.IgnoreCase)
            );

            ManualRecompile.RequestFromMenu();

            Assert.IsTrue(
                secondRequestRefreshed,
                "Second request should work normally after exception in first request"
            );
        }

        private static IEnumerable<TestCaseData> ExceptionScenarioTestCases()
        {
            yield return new TestCaseData(
                new Action<Action, Action>(
                    (setAssetsRefreshed, setCompilationRequested) =>
                    {
                        ManualRecompile.AssetsRefreshedForTests = () =>
                        {
                            setAssetsRefreshed();
                            throw new InvalidOperationException("AssetsRefreshed exception");
                        };
                    }
                ),
                true,
                "AssetsRefreshedCallback"
            ).SetName("Exception.InAssetsRefreshedCallback.FlagIsReset");

            yield return new TestCaseData(
                new Action<Action, Action>(
                    (setAssetsRefreshed, setCompilationRequested) =>
                    {
                        ManualRecompile.SkipCompilationRequestForTests = false;
                        ManualRecompile.AssetsRefreshedForTests = setAssetsRefreshed;
                        ManualRecompile.CompilationRequestedForTests = () =>
                        {
                            setCompilationRequested();
                            throw new InvalidOperationException("CompilationRequested exception");
                        };
                    }
                ),
                false,
                "CompilationRequestedCallback"
            ).SetName("Exception.InCompilationRequestedCallback.FlagIsReset");

            yield return new TestCaseData(
                new Action<Action, Action>(
                    (setAssetsRefreshed, setCompilationRequested) =>
                    {
                        ManualRecompile.AssetsRefreshedForTests = () =>
                        {
                            setAssetsRefreshed();
                            throw new ArgumentException("ArgumentException from callback");
                        };
                    }
                ),
                true,
                "AssetsRefreshedCallbackWithArgumentException"
            ).SetName("Exception.ArgumentException.FlagIsReset");

            yield return new TestCaseData(
                new Action<Action, Action>(
                    (setAssetsRefreshed, setCompilationRequested) =>
                    {
                        ManualRecompile.AssetsRefreshedForTests = () =>
                        {
                            setAssetsRefreshed();
                            throw new NullReferenceException(
                                "NullReferenceException from callback"
                            );
                        };
                    }
                ),
                true,
                "AssetsRefreshedCallbackWithNullReferenceException"
            ).SetName("Exception.NullReferenceException.FlagIsReset");
        }

        [Test]
        [TestCaseSource(nameof(ExceptionScenarioTestCases))]
        public void SkipFlagIsResetOnExceptionInCallbacks(
            Action<Action, Action> setupCallbacks,
            bool initialSkipFlag,
            string scenarioDescription
        )
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => false;
            ManualRecompile.SkipCompilationRequestForTests = initialSkipFlag;

            bool assetsRefreshed = false;
            bool compilationRequested = false;

            setupCallbacks(() => assetsRefreshed = true, () => compilationRequested = true);

            bool caughtException = false;
            Exception thrownException = null;

            try
            {
                ManualRecompile.RequestFromMenu();
            }
            catch (Exception ex)
            {
                caughtException = true;
                thrownException = ex;
            }

            Assert.IsTrue(
                caughtException,
                $"[{scenarioDescription}] Expected exception to be thrown"
            );
            Assert.IsFalse(
                ManualRecompile.SkipCompilationRequestForTests,
                $"[{scenarioDescription}] Skip flag should be reset after exception. "
                    + $"AssetsRefreshed: {assetsRefreshed}, "
                    + $"CompilationRequested: {compilationRequested}, "
                    + $"InitialSkipFlag: {initialSkipFlag}, "
                    + $"ExceptionType: {thrownException?.GetType().Name}, "
                    + $"ExceptionMessage: {thrownException?.Message}"
            );
        }

        [Test]
        public void MultipleSequentialRequestsResetFlagCorrectly()
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => false;

            int requestCount = 0;

            for (int i = 0; i < 3; i++)
            {
                ManualRecompile.SkipCompilationRequestForTests = true;
                ManualRecompile.AssetsRefreshedForTests = () => requestCount++;

                LogAssert.Expect(
                    LogType.Log,
                    new Regex("Asset database refreshed", RegexOptions.IgnoreCase)
                );

                ManualRecompile.RequestFromMenu();

                Assert.IsFalse(
                    ManualRecompile.SkipCompilationRequestForTests,
                    $"Skip flag should be reset after request {i + 1}. RequestCount: {requestCount}"
                );
            }

            Assert.AreEqual(
                3,
                requestCount,
                "All three requests should have completed. RequestCount mismatch."
            );
        }

        [Test]
        public void RequestDoesNotResetFlagWhenSkipFlagIsFalse()
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => false;
            ManualRecompile.SkipCompilationRequestForTests = false;

            bool compileRequested = false;
            ManualRecompile.CompilationRequestedForTests = () => compileRequested = true;

            LogAssert.Expect(
                LogType.Log,
                new Regex(
                    "Refreshed assets and requested script compilation",
                    RegexOptions.IgnoreCase
                )
            );

            ManualRecompile.RequestFromMenu();

            Assert.IsTrue(compileRequested, "Compilation should have been requested");
            Assert.IsFalse(
                ManualRecompile.SkipCompilationRequestForTests,
                "Skip flag should remain false when it was initially false"
            );
        }

        [Test]
        public void ExceptionInAssetsRefreshedDoesNotPreventFlagReset()
        {
            ManualRecompile.IsCompilationPendingEvaluator = () => false;
            ManualRecompile.SkipCompilationRequestForTests = true;

            int callOrder = 0;
            int assetsRefreshedOrder = -1;

            ManualRecompile.AssetsRefreshedForTests = () =>
            {
                assetsRefreshedOrder = callOrder++;
                throw new InvalidOperationException("Intentional test exception");
            };

            bool caughtException = false;
            try
            {
                ManualRecompile.RequestFromMenu();
            }
            catch (InvalidOperationException)
            {
                caughtException = true;
            }

            Assert.IsTrue(caughtException, "Exception should have been caught");
            Assert.AreEqual(
                0,
                assetsRefreshedOrder,
                "AssetsRefreshed callback should have been called first"
            );
            Assert.IsFalse(
                ManualRecompile.SkipCompilationRequestForTests,
                $"Skip flag should be reset even when AssetsRefreshed throws. "
                    + $"CallOrder: {callOrder}, AssetsRefreshedOrder: {assetsRefreshedOrder}"
            );
        }

        private static void EnsureParentDirectoryExists(string absolutePath)
        {
            string directoryPath = Path.GetDirectoryName(absolutePath) ?? string.Empty;

            if (directoryPath.Length > 0)
            {
                Directory.CreateDirectory(directoryPath);
            }
        }

        private static string GetAbsolutePath(string relativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(projectRoot, normalizedRelativePath);
        }
    }
}
