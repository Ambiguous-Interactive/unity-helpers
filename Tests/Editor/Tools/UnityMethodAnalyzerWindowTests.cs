// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Editor.Tools
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using UnityEditor.Compilation;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor.Tools.UnityMethodAnalyzer;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Tests for the UnityMethodAnalyzerWindow focusing on state management,
    /// cancellation behavior, and UI state consistency.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    // UNH-SUPPRESS: Complex test class with manual EditorWindow lifecycle management in TearDown
    public sealed class UnityMethodAnalyzerWindowTests
    {
        private UnityMethodAnalyzerWindow _window;
        private string _tempDir;
        private readonly List<CompilerMessage> _compilerMessages = new();

        [SetUp]
        public void SetUp()
        {
            _compilerMessages.Clear();
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "UnityMethodAnalyzerWindowTests_" + Path.GetRandomFileName()
            );
            Directory.CreateDirectory(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                // EditorWindow.Close() throws when the window was created but never shown (no host view).
                Object.DestroyImmediate(_window); // UNH-SUPPRESS: EditorWindow cleanup in TearDown - Close() throws NullReferenceException
                _window = null;
            }

            if (Directory.Exists(_tempDir))
            {
                try
                {
                    Directory.Delete(_tempDir, true);
                }
                catch { }
            }
        }

        private UnityMethodAnalyzerWindow CreateWindow()
        {
            _window = ScriptableObject.CreateInstance<UnityMethodAnalyzerWindow>(); // UNH-SUPPRESS: EditorWindow cleaned up in TearDown

            _window.Initialize();
            _window._analyzer = new MethodAnalyzer(
                _compilerMessages,
                "Captured test compiler snapshot."
            );
            return _window;
        }

        [Test]
        public void WindowInitializesWithCorrectDefaultState()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            bool isAnalyzing = window._isAnalyzing;
            float analysisProgress = window._analysisProgress;
            string statusMessage = window._statusMessage;
            CancellationTokenSource cts = window._cancellationTokenSource;

            Assert.IsFalse(isAnalyzing, "Window should not be analyzing on init");
            Assert.AreEqual(0f, analysisProgress, "Progress should be 0 on init");
            Assert.AreEqual(
                "Refresh compiler diagnostics, or recompile scripts to capture them.",
                statusMessage,
                "The initial status should explain how compiler diagnostics are captured."
            );
            Assert.IsTrue(cts == null, "CancellationTokenSource should be null on init");
        }

        [Test]
        public void ResetAnalysisStateClearsAllAnalysisFields()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._isAnalyzing = true;
            window._analysisProgress = 0.75f;

            window.ResetAnalysisState();

            bool isAnalyzing = window._isAnalyzing;
            float analysisProgress = window._analysisProgress;

            Assert.IsFalse(isAnalyzing, "isAnalyzing should be false after reset");
            Assert.AreEqual(0f, analysisProgress, "analysisProgress should be 0 after reset");
        }

        [Test]
        public void CancelAnalysisDoesNothingWhenNoCancellationTokenSource()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._cancellationTokenSource = null;

            window.CancelAnalysis();

            bool isAnalyzing = window._isAnalyzing;
            Assert.IsFalse(isAnalyzing, "State should remain unchanged when CTS is null");
        }

        [Test]
        public void CancelAnalysisCancelsActiveCancellationTokenSource()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource cts = new();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;

            Assert.IsFalse(cts.IsCancellationRequested, "CTS should not be cancelled initially");

            window.CancelAnalysis();

            Assert.IsTrue(
                cts.IsCancellationRequested,
                "CTS should be cancelled after CancelAnalysis"
            );

            bool isAnalyzing = window._isAnalyzing;
            string statusMessage = window._statusMessage;

            Assert.IsFalse(isAnalyzing, "isAnalyzing should be false after CancelAnalysis");
            Assert.AreEqual(
                "Analysis cancelled",
                statusMessage,
                "Status should indicate cancellation"
            );
        }

        [Test]
        public void CancelAnalysisImmediatelyResetsAnalyzingState()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource cts = new();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;
            window._analysisProgress = 0.5f;
            window._statusMessage = "Analyzing...";

            window.CancelAnalysis();

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;

            Assert.IsFalse(isAnalyzing, "isAnalyzing should be immediately false");
            Assert.AreEqual(0f, progress, "Progress should be immediately reset to 0");
            Assert.AreEqual("Analysis cancelled", status, "Status should indicate cancellation");
        }

        [Test]
        public void CancelAnalysisDoesNotCancelAlreadyCancelledTokenSource()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource cts = new();
            cts.Cancel();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;
            window._analysisProgress = 0.5f;
            window._statusMessage = "Analyzing...";

            window.CancelAnalysis();

            Assert.IsTrue(cts.IsCancellationRequested, "CTS should remain cancelled");

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;

            Assert.IsFalse(
                isAnalyzing,
                "isAnalyzing should be false even when CTS was already cancelled"
            );
            Assert.AreEqual(
                0f,
                progress,
                "Progress should be reset even when CTS was already cancelled"
            );
            Assert.AreEqual("Analysis cancelled", status, "Status should indicate cancellation");
        }

        [Test]
        public void StartAnalysisDoesNothingWhenAlreadyAnalyzing()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._isAnalyzing = true;
            window._statusMessage = "Previous status";

            window.StartAnalysis();

            string statusMessage = window._statusMessage;
            Assert.AreEqual(
                "Previous status",
                statusMessage,
                "Status should not change when already analyzing"
            );
        }

        [Test]
        public void StartAnalysisDisposesOldCancellationTokenSource()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource oldCts = new();
            window._cancellationTokenSource = oldCts;
            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };
            AddCompilerDiagnostic("Test.cs");

            window.StartAnalysis();

            Assert.Throws<ObjectDisposedException>(() =>
            {
                CancellationToken token = oldCts.Token;
            });
        }

        [Test]
        public void StartAnalysisSetsAnalyzingStateImmediately()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };
            AddCompilerDiagnostic("Test.cs");

            window.StartAnalysis();

            bool isAnalyzing = window._isAnalyzing;
            string statusMessage = window._statusMessage;

            Assert.Pass("StartAnalysis executed without throwing");
        }

        [Test]
        public void ResetAnalysisStateIsIdempotent()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window.ResetAnalysisState();
            window.ResetAnalysisState();
            window.ResetAnalysisState();

            bool isAnalyzing = window._isAnalyzing;
            float analysisProgress = window._analysisProgress;

            Assert.IsFalse(isAnalyzing, "isAnalyzing should remain false");
            Assert.AreEqual(0f, analysisProgress, "analysisProgress should remain 0");
        }

        [Test]
        public void CancelAnalysisIsIdempotent()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource cts = new();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;
            window._analysisProgress = 0.5f;
            window._statusMessage = "Analyzing...";

            window.CancelAnalysis();
            window.CancelAnalysis();
            window.CancelAnalysis();

            Assert.IsTrue(cts.IsCancellationRequested, "CTS should be cancelled");

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;

            Assert.IsFalse(isAnalyzing, "isAnalyzing should be false after multiple cancels");
            Assert.AreEqual(0f, progress, "Progress should be reset after multiple cancels");
            Assert.AreEqual(
                "Analysis cancelled",
                status,
                "Status should indicate cancellation after multiple cancels"
            );
        }

        [Test]
        public void StartAnalysisSetsCorrectInitialStatusMessage()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };
            window._statusMessage = "Previous message";
            AddCompilerDiagnostic("Test.cs");

            window.StartAnalysis();

            string statusMessage = window._statusMessage;
            Assert.AreNotEqual(
                "Previous message",
                statusMessage,
                "Status message should have changed"
            );
        }

        [Test]
        public void StartAnalysisWithNoValidDirectoriesSetsAppropriateMessage()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { "/nonexistent/path" };

            window.StartAnalysis();

            string statusMessage = window._statusMessage;
            bool isAnalyzing = window._isAnalyzing;

            Assert.AreEqual(
                "No valid directories selected",
                statusMessage,
                "Should indicate no valid directories"
            );
            Assert.IsFalse(isAnalyzing, "Should not be analyzing when no valid directories");
        }

        [Test]
        public void StartAnalysisWithNoValidDirectoriesSignalsCompletionSource()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;
            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { "/nonexistent/path" };

            window.StartAnalysis();

            Assert.IsTrue(
                tcs.Task.IsCompleted,
                "TCS should be completed when no valid directories"
            );
            Assert.IsTrue(tcs.Task.Result, "TCS result should be true");
            Assert.IsFalse(window._isAnalyzing, "Should not be analyzing");
        }

        [Test]
        public void StartAnalysisWithEmptySourcePathsSetsAppropriateMessage()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._isAnalyzing = false;
            window._sourcePaths = new List<string>();

            window.StartAnalysis();

            string statusMessage = window._statusMessage;
            bool isAnalyzing = window._isAnalyzing;

            Assert.AreEqual(
                "No valid directories selected",
                statusMessage,
                "Should indicate no valid directories"
            );
            Assert.IsFalse(isAnalyzing, "Should not be analyzing with empty source paths");
        }

        [Test]
        public void StartAnalysisWithEmptySourcePathsSignalsCompletionSource()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;
            window._isAnalyzing = false;
            window._sourcePaths = new List<string>();

            window.StartAnalysis();

            Assert.IsTrue(tcs.Task.IsCompleted, "TCS should be completed when empty source paths");
            Assert.IsTrue(tcs.Task.Result, "TCS result should be true");
            Assert.IsFalse(window._isAnalyzing, "Should not be analyzing");
        }

        [Test]
        public void StartAnalysisWithNullEntriesInSourcePathsFiltersThemOut()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { null, "", "   ", _tempDir };
            AddCompilerDiagnostic("Test.cs");

            window.StartAnalysis();

            string statusMessage = window._statusMessage;
            Assert.AreNotEqual(
                "No valid directories selected",
                statusMessage,
                "Should have found the valid directory"
            );
        }

        [UnityTest]
        public IEnumerator CancellationDuringAnalysisResetsUIState()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 50; i++)
            {
                AddCompilerDiagnostic($"TestClass{i}.cs");
            }

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();

            yield return null;

            window.CancelAnalysis();

            float waitTime = 0f;
            float maxWaitTime = 5f;
            while (waitTime < maxWaitTime)
            {
                bool isAnalyzing = window._isAnalyzing;
                if (!isAnalyzing)
                {
                    break;
                }

                yield return null;
                waitTime += Time.deltaTime;
            }

            bool finalIsAnalyzing = window._isAnalyzing;
            float finalProgress = window._analysisProgress;
            string finalStatus = window._statusMessage;

            Assert.IsFalse(finalIsAnalyzing, "isAnalyzing should be false after cancellation");
            Assert.AreEqual(0f, finalProgress, "Progress should be 0 after cancellation");
            Assert.AreEqual(
                "Analysis cancelled",
                finalStatus,
                "Status should indicate cancellation"
            );
        }

        [UnityTest]
        public IEnumerator SuccessfulAnalysisResetsUIState()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            AddCompilerDiagnostic("SimpleTest.cs");

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;

            window.StartAnalysis();

            AnalysisWaitResult result = default;
            yield return WaitForAnalysisCompletion(window, 10f, r => result = r);

            Assert.IsFalse(
                result.IsAnalyzing,
                $"isAnalyzing should be false after completion. {result}"
            );
            Assert.AreEqual(
                0f,
                result.Progress,
                $"Progress should be 0 after completion. {result}"
            );
            Assert.IsTrue(
                result.StatusMessage.Contains("reported issues in selected directories")
                    || result.StatusMessage.Contains("Analysis failed"),
                $"Status should indicate completion or failure. {result}"
            );
        }

        [UnityTest]
        public IEnumerator SuccessfulAnalysisResetsUIStateWithSingleFile()
        {
            yield return SuccessfulAnalysisResetsUIStateWithFileCount(1);
        }

        [UnityTest]
        public IEnumerator SuccessfulAnalysisResetsUIStateWithFiveFiles()
        {
            yield return SuccessfulAnalysisResetsUIStateWithFileCount(5);
        }

        [UnityTest]
        public IEnumerator SuccessfulAnalysisResetsUIStateWithTenFiles()
        {
            yield return SuccessfulAnalysisResetsUIStateWithFileCount(10);
        }

        [UnityTest]
        public IEnumerator SuccessfulAnalysisResetsUIStateWithTwentyFiles()
        {
            yield return SuccessfulAnalysisResetsUIStateWithFileCount(20);
        }

        [UnityTest]
        public IEnumerator SuccessfulAnalysisResetsUIStateWithThirtyFiles()
        {
            yield return SuccessfulAnalysisResetsUIStateWithFileCount(30);
        }

        /// <summary>
        /// Test that specifically exercises the race condition between the analysis task
        /// completing and the ContinueWith callback actually running and enqueuing work.
        /// This validates that WaitForAnalysisCompletion properly waits for the completion
        /// callback to be processed, not just for the raw task to complete.
        /// </summary>
        [UnityTest]
        public IEnumerator CompletionCallbackRaceConditionIsHandled()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            AddCompilerDiagnostic("RaceCallbackTest.cs");

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };

            // FinalizeAnalysis signals this, after HandleAnalysisCompletion runs.
            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;

            window.StartAnalysis();

            Task rawTask = window._analysisTask;
            Assert.IsTrue(rawTask != null, "Analysis task should be set after StartAnalysis");

            float startTime = Time.realtimeSinceStartup;
            while (!rawTask.IsCompleted && (Time.realtimeSinceStartup - startTime) < 10f)
            {
                yield return null;
            }

            Assert.IsTrue(rawTask.IsCompleted, "Raw task should complete");

            // The defect: flushing immediately found nothing, because the continuation had not enqueued yet.

            bool immediatelyReset = !window._isAnalyzing;

            AnalysisWaitResult result = default;
            yield return WaitForAnalysisCompletion(window, 10f, r => result = r);

            Assert.IsFalse(
                result.IsAnalyzing,
                $"isAnalyzing should be false after proper wait. ImmediatelyReset: {immediatelyReset}. {result}"
            );
            Assert.IsTrue(
                result.CompletionSourceSignaled,
                $"Completion source should be signaled after proper wait. {result}"
            );
        }

        private IEnumerator SuccessfulAnalysisResetsUIStateWithFileCount(int fileCount)
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < fileCount; i++)
            {
                AddCompilerDiagnostic($"FileCountTest{i}.cs");
            }

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;

            window.StartAnalysis();

            AnalysisWaitResult result = default;
            yield return WaitForAnalysisCompletion(window, 15f, r => result = r);

            Assert.IsFalse(
                result.IsAnalyzing,
                $"isAnalyzing should be false after completion with {fileCount} files. {result}"
            );
            Assert.AreEqual(
                0f,
                result.Progress,
                $"Progress should be 0 after completion with {fileCount} files. {result}"
            );
            Assert.IsTrue(
                result.StatusMessage.Contains("reported issues in selected directories")
                    || result.StatusMessage.Contains("Analysis failed"),
                $"Status should indicate completion or failure with {fileCount} files. {result}"
            );
        }

        /// <summary>
        /// Test that verifies the race condition between Progress callback and ResetAnalysisState is handled.
        /// Progress&lt;T&gt; uses SynchronizationContext.Post() which can deliver callbacks after the task
        /// completion callback has already run ResetAnalysisState(). The fix guards the progress callback
        /// so it only updates when _isAnalyzing is true.
        /// </summary>
        [UnityTest]
        public IEnumerator ProgressCallbackRaceConditionIsHandled()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            AddCompilerDiagnostic("RaceConditionTest.cs");

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;

            window.StartAnalysis();

            AnalysisWaitResult result = default;
            yield return WaitForAnalysisCompletion(window, 10f, r => result = r);

            for (int i = 0; i < 5; i++)
            {
                yield return null;
                UnityMethodAnalyzerWindow.FlushMainThreadQueue();
            }

            float finalProgress = window._analysisProgress;
            bool finalIsAnalyzing = window._isAnalyzing;
            string finalStatus = window._statusMessage;

            Assert.IsFalse(
                finalIsAnalyzing,
                $"isAnalyzing should remain false after delayed callbacks. Status: '{finalStatus}', Progress: {finalProgress}"
            );
            Assert.AreEqual(
                0f,
                finalProgress,
                $"Progress should remain 0 after delayed callbacks. Status: '{finalStatus}', Result: {result}"
            );
        }

        /// <summary>
        /// Test that WaitForAnalysisCompletion works correctly even when _analysisCompletionSource
        /// is not set. In this case, it should fall back to checking _isAnalyzing.
        /// </summary>
        [UnityTest]
        public IEnumerator WaitForAnalysisCompletionWorksWithoutCompletionSource()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            AddCompilerDiagnostic("NoTCSTest.cs");

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };

            window._analysisCompletionSource = null;

            window.StartAnalysis();

            AnalysisWaitResult result = default;
            yield return WaitForAnalysisCompletion(window, 10f, r => result = r);

            Assert.IsFalse(
                result.IsAnalyzing,
                $"isAnalyzing should be false after completion without TCS. {result}"
            );
            Assert.IsFalse(
                result.CompletionSourceSignaled,
                $"Completion source should NOT be signaled when null. {result}"
            );
        }

        /// <summary>
        /// Test that verifies progress is correctly updated during analysis.
        /// </summary>
        [UnityTest]
        public IEnumerator ProgressUpdatesCorrectlyDuringAnalysis()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 10; i++)
            {
                AddCompilerDiagnostic($"ProgressTest{i}.cs");
            }

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;

            window.StartAnalysis();

            Assert.IsTrue(window._isAnalyzing, "Should be analyzing after StartAnalysis");

            AnalysisWaitResult result = default;
            yield return WaitForAnalysisCompletion(window, 15f, r => result = r);

            Assert.IsFalse(
                result.IsAnalyzing,
                $"isAnalyzing should be false after completion. {result}"
            );
            Assert.AreEqual(
                0f,
                result.Progress,
                $"Progress should be 0 after completion. {result}"
            );
        }

        /// <summary>
        /// Test that verifies rapid successive analyses don't leave stale progress values.
        /// </summary>
        [UnityTest]
        public IEnumerator RapidSuccessiveAnalysesResetProgressCorrectly()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            AddCompilerDiagnostic("RapidTest.cs");

            window._sourcePaths = new List<string> { _tempDir };

            for (int iteration = 0; iteration < 3; iteration++)
            {
                window._isAnalyzing = false;
                TaskCompletionSource<bool> tcs = new();
                window._analysisCompletionSource = tcs;

                window.StartAnalysis();

                AnalysisWaitResult result = default;
                yield return WaitForAnalysisCompletion(window, 10f, r => result = r);

                Assert.IsFalse(
                    result.IsAnalyzing,
                    $"Iteration {iteration}: isAnalyzing should be false. {result}"
                );
                Assert.AreEqual(
                    0f,
                    result.Progress,
                    $"Iteration {iteration}: Progress should be 0. {result}"
                );
            }
        }

        /// <summary>
        /// Test with large file count to stress test progress callback timing.
        /// </summary>
        [UnityTest]
        public IEnumerator LargeFileCountAnalysisResetsProgressCorrectly()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 50; i++)
            {
                AddCompilerDiagnostic($"LargeTest{i}.cs");
            }

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;

            window.StartAnalysis();

            AnalysisWaitResult result = default;
            yield return WaitForAnalysisCompletion(window, 30f, r => result = r);

            for (int i = 0; i < 5; i++)
            {
                yield return null;
                UnityMethodAnalyzerWindow.FlushMainThreadQueue();
            }

            float finalProgress = window._analysisProgress;

            Assert.IsFalse(
                result.IsAnalyzing,
                $"isAnalyzing should be false after large analysis. {result}"
            );
            Assert.AreEqual(
                0f,
                finalProgress,
                $"Progress should be 0 after large analysis. {result}"
            );
        }

        [UnityTest]
        public IEnumerator CancelButtonCanBeClickedAgainAfterCancellation()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 20; i++)
            {
                AddCompilerDiagnostic($"Test{i}.cs");
            }

            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();
            yield return null;
            window.CancelAnalysis();

            float waitTime = 0f;
            int frameCount = 0;
            while (waitTime < 5f && window._isAnalyzing)
            {
                yield return null;
                waitTime += Time.deltaTime;
                frameCount++;
            }

            string statusAfterCancel = window._statusMessage;
            bool isAnalyzingAfterCancel = window._isAnalyzing;

            Assert.IsFalse(
                isAnalyzingAfterCancel,
                $"Should not be analyzing after first cancellation. Status: '{statusAfterCancel}', WaitTime: {waitTime:F2}s, Frames: {frameCount}"
            );

            window.StartAnalysis();
            yield return null;

            Assert.Pass("Was able to start analysis again after cancellation");
        }

        [UnityTest]
        public IEnumerator AnalyzeButtonIsEnabledAfterCancellation()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 30; i++)
            {
                AddCompilerDiagnostic($"Test{i}.cs");
            }

            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();
            yield return null;

            window.CancelAnalysis();

            float waitTime = 0f;
            int frameCount = 0;
            while (waitTime < 5f && window._isAnalyzing)
            {
                yield return null;
                waitTime += Time.deltaTime;
                frameCount++;
            }

            bool isAnalyzing = window._isAnalyzing;
            string status = window._statusMessage;
            List<string> sourcePaths = window._sourcePaths;
            bool hasValidPaths =
                sourcePaths != null
                && sourcePaths.Exists(p => !string.IsNullOrEmpty(p) && Directory.Exists(p));
            bool analyzeEnabled = !isAnalyzing && hasValidPaths;

            Assert.IsTrue(
                analyzeEnabled,
                $"Analyze button should be enabled after cancellation. isAnalyzing: {isAnalyzing}, hasValidPaths: {hasValidPaths}, Status: '{status}', WaitTime: {waitTime:F2}s, Frames: {frameCount}"
            );
        }

        [UnityTest]
        public IEnumerator CancelButtonIsNotVisibleAfterCancellation()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 30; i++)
            {
                AddCompilerDiagnostic($"Test{i}.cs");
            }

            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();
            yield return null;

            window.CancelAnalysis();

            float waitTime = 0f;
            int frameCount = 0;
            while (waitTime < 5f && window._isAnalyzing)
            {
                yield return null;
                waitTime += Time.deltaTime;
                frameCount++;
            }

            bool isAnalyzing = window._isAnalyzing;
            string status = window._statusMessage;
            Assert.IsFalse(
                isAnalyzing,
                $"Cancel button should not be visible (isAnalyzing should be false). Status: '{status}', WaitTime: {waitTime:F2}s, Frames: {frameCount}"
            );
        }

        [UnityTest]
        public IEnumerator ProgressBarIsHiddenAfterCancellation()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 30; i++)
            {
                AddCompilerDiagnostic($"Test{i}.cs");
            }

            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();
            yield return null;

            window.CancelAnalysis();

            float waitTime = 0f;
            int frameCount = 0;
            while (waitTime < 5f && window._isAnalyzing)
            {
                yield return null;
                waitTime += Time.deltaTime;
                frameCount++;
            }

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;

            Assert.IsFalse(
                isAnalyzing,
                $"Progress bar should be hidden (isAnalyzing should be false). Status: '{status}', WaitTime: {waitTime:F2}s, Frames: {frameCount}"
            );
            Assert.AreEqual(0f, progress, $"Progress should be reset to 0. Status: '{status}'");
        }

        [Test]
        public void OnDisableCancelsPendingAnalysis()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource cts = new();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;

            window.OnDisable();

            Assert.IsTrue(cts.IsCancellationRequested, "CTS should be cancelled on disable");
        }

        [Test]
        public void OnDisableHandlesNullCancellationTokenSource()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._cancellationTokenSource = null;

            Assert.DoesNotThrow(
                () => window.OnDisable(),
                "OnDisable should handle null CTS gracefully"
            );
        }

        [UnityTest]
        public IEnumerator MultipleCancellationsDoNotCorruptState()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 50; i++)
            {
                AddCompilerDiagnostic($"Test{i}.cs");
            }

            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();
            yield return null;

            for (int i = 0; i < 10; i++)
            {
                window.CancelAnalysis();
            }

            float waitTime = 0f;
            int frameCount = 0;
            while (waitTime < 5f && window._isAnalyzing)
            {
                yield return null;
                waitTime += Time.deltaTime;
                frameCount++;
            }

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;

            Assert.IsFalse(
                isAnalyzing,
                $"State should be consistent after multiple cancellations. Status: '{status}', WaitTime: {waitTime:F2}s, Frames: {frameCount}"
            );
            Assert.AreEqual(
                0f,
                progress,
                $"Progress should be 0 after multiple cancellations. Status: '{status}'"
            );
        }

        [UnityTest]
        public IEnumerator RapidStartCancelCyclesDoNotCorruptState()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 20; i++)
            {
                AddCompilerDiagnostic($"Test{i}.cs");
            }

            window._sourcePaths = new List<string> { _tempDir };

            for (int cycle = 0; cycle < 5; cycle++)
            {
                float waitTime = 0f;
                while (waitTime < 2f && window._isAnalyzing)
                {
                    yield return null;
                    waitTime += Time.deltaTime;
                }

                window.StartAnalysis();
                yield return null;
                window.CancelAnalysis();
            }

            float finalWait = 0f;
            int frameCount = 0;
            while (finalWait < 5f && window._isAnalyzing)
            {
                yield return null;
                finalWait += Time.deltaTime;
                frameCount++;
            }

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;

            Assert.IsFalse(
                isAnalyzing,
                $"State should be consistent after rapid cycles. Status: '{status}', WaitTime: {finalWait:F2}s, Frames: {frameCount}"
            );
            Assert.AreEqual(
                0f,
                progress,
                $"Progress should be 0 after rapid cycles. Status: '{status}'"
            );
        }

        [Test]
        public void CancellationTokenSourceIsDisposedAfterAnalysis()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource initialCts = new();
            window._cancellationTokenSource = initialCts;
            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };
            AddCompilerDiagnostic("Test.cs");

            window.StartAnalysis();

            Assert.Throws<ObjectDisposedException>(
                () =>
                {
                    CancellationToken token = initialCts.Token;
                },
                "Old CTS should be disposed when starting new analysis"
            );
        }

        [Test]
        public void UpdateIssueCountsHandlesEmptyIssuesList()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            MethodAnalyzer analyzer = new();
            window._analyzer = analyzer;

            window.UpdateIssueCounts();

            int totalCount = window._totalCount;
            int criticalCount = window._criticalCount;
            int highCount = window._highCount;
            int mediumCount = window._mediumCount;
            int lowCount = window._lowCount;
            int infoCount = window._infoCount;

            Assert.AreEqual(0, totalCount, "Total count should be 0 for empty issues");
            Assert.AreEqual(0, criticalCount, "Critical count should be 0");
            Assert.AreEqual(0, highCount, "High count should be 0");
            Assert.AreEqual(0, mediumCount, "Medium count should be 0");
            Assert.AreEqual(0, lowCount, "Low count should be 0");
            Assert.AreEqual(0, infoCount, "Info count should be 0");
        }

        private void AddCompilerDiagnostic(string filename)
        {
            _compilerMessages.Add(
                new CompilerMessage
                {
                    file = Path.Combine(_tempDir, filename),
                    line = 1,
                    message =
                        "warning WUH015: 'Fixture.Subject.Update(int)' has an invalid callback signature.",
                    type = CompilerMessageType.Warning,
                }
            );
        }

        /// <summary>
        /// Waits for the analysis to complete and captures diagnostic information.
        /// First waits for the underlying analysis task, then repeatedly flushes
        /// the main thread queue until the completion callback has been processed.
        /// </summary>
        /// <remarks>
        /// The analysis task completion triggers a ContinueWith callback that runs on a
        /// thread pool thread, which enqueues HandleAnalysisCompletion onto the main thread
        /// queue. There's a race between the task completing and the ContinueWith callback
        /// actually running, so we must loop until the completion source is signaled
        /// (which happens at the end of HandleAnalysisCompletion via FinalizeAnalysis).
        /// </remarks>
        private IEnumerator WaitForAnalysisCompletion(
            UnityMethodAnalyzerWindow window,
            float maxWaitTime,
            Action<AnalysisWaitResult> onComplete
        )
        {
            Task analysisTask = window._analysisTask;
            TaskCompletionSource<bool> tcs = window._analysisCompletionSource;
            float startRealTime = Time.realtimeSinceStartup;
            int frameCount = 0;

            if (analysisTask == null)
            {
                UnityEngine.Debug.LogWarning(
                    $"[WaitForAnalysisCompletion] Analysis task is null! "
                        + $"IsAnalyzing: {window._isAnalyzing}, Status: '{window._statusMessage}'"
                );
            }

            while (
                (Time.realtimeSinceStartup - startRealTime) < maxWaitTime
                && analysisTask != null
                && !analysisTask.IsCompleted
            )
            {
                yield return null;
                frameCount++;

                if (frameCount % 50 == 0)
                {
                    Task currentTask = window._analysisTask;
                    if (!ReferenceEquals(currentTask, analysisTask))
                    {
                        UnityEngine.Debug.LogWarning(
                            $"[WaitForAnalysisCompletion] Task reference changed at frame {frameCount}!"
                        );
                    }
                }
            }

            /*
                The ContinueWith runs on a thread pool thread and enqueues work on the main thread,
                so flush and yield until the completion source is signaled -- which is what says
                HandleAnalysisCompletion ran and called FinalizeAnalysis.
            */
            while ((Time.realtimeSinceStartup - startRealTime) < maxWaitTime)
            {
                UnityMethodAnalyzerWindow.FlushMainThreadQueue();

                bool completionSignaled = tcs != null && tcs.Task.IsCompleted;
                bool noLongerAnalyzing = !window._isAnalyzing;

                if (completionSignaled || noLongerAnalyzing)
                {
                    yield return null;
                    frameCount++;
                    UnityMethodAnalyzerWindow.FlushMainThreadQueue();
                    break;
                }

                yield return null;
                frameCount++;
            }

            float realWaitTime = Time.realtimeSinceStartup - startRealTime;

            if (analysisTask != null && analysisTask.IsFaulted && analysisTask.Exception != null)
            {
                string taskExceptionMessage = analysisTask.Exception.GetBaseException().Message;
                UnityEngine.Debug.LogError(
                    $"[WaitForAnalysisCompletion] Task faulted with: {taskExceptionMessage}"
                );
            }

            AnalysisWaitResult result = new()
            {
                WaitTime = realWaitTime,
                FrameCount = frameCount,
                AnalysisTaskCompleted = analysisTask != null && analysisTask.IsCompleted,
                AnalysisTaskStatus = analysisTask?.Status ?? TaskStatus.Created,
                CompletionSourceSignaled = tcs?.Task.IsCompleted ?? false,
                CompletionSourceStatus = tcs?.Task.Status ?? TaskStatus.Created,
                IsAnalyzing = window._isAnalyzing,
                Progress = window._analysisProgress,
                StatusMessage = window._statusMessage,
            };

            onComplete?.Invoke(result);
        }

        [Test]
        public void GroupByFileIsDefaultGroupingOnInit()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsTrue(groupByFile, "Group by file should be true by default");
            Assert.IsFalse(groupBySeverity, "Group by severity should be false by default");
            Assert.IsFalse(groupByCategory, "Group by category should be false by default");
        }

        [Test]
        public void GroupBySelectionExactlyOneIsAlwaysSelected()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = true;
            window._groupBySeverity = false;
            window._groupByCategory = false;
            AssertExactlyOneGroupBySelected(window, "Initial state");

            window._groupByFile = false;
            window._groupBySeverity = true;
            window._groupByCategory = false;
            AssertExactlyOneGroupBySelected(window, "Severity selected");

            window._groupByFile = false;
            window._groupBySeverity = false;
            window._groupByCategory = true;
            AssertExactlyOneGroupBySelected(window, "Category selected");
        }

        [Test]
        public void GroupBySwitchFromFileToSeverityWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = true;
            window._groupBySeverity = false;
            window._groupByCategory = false;

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: true,
                groupByCategory: false
            );

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsFalse(groupByFile, "File should be deselected after clicking Severity");
            Assert.IsTrue(groupBySeverity, "Severity should be selected");
            Assert.IsFalse(groupByCategory, "Category should remain deselected");
        }

        [Test]
        public void GroupBySwitchFromSeverityToFileWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = false;
            window._groupBySeverity = true;
            window._groupByCategory = false;

            SimulateGroupByClick(
                window,
                groupByFile: true,
                groupBySeverity: false,
                groupByCategory: false
            );

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsTrue(groupByFile, "File should be selected");
            Assert.IsFalse(groupBySeverity, "Severity should be deselected after clicking File");
            Assert.IsFalse(groupByCategory, "Category should remain deselected");
        }

        [Test]
        public void GroupBySwitchFromFileToCategoryWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = true;
            window._groupBySeverity = false;
            window._groupByCategory = false;

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: false,
                groupByCategory: true
            );

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsFalse(groupByFile, "File should be deselected after clicking Category");
            Assert.IsFalse(groupBySeverity, "Severity should remain deselected");
            Assert.IsTrue(groupByCategory, "Category should be selected");
        }

        [Test]
        public void GroupBySwitchFromCategoryToFileWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = false;
            window._groupBySeverity = false;
            window._groupByCategory = true;

            SimulateGroupByClick(
                window,
                groupByFile: true,
                groupBySeverity: false,
                groupByCategory: false
            );

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsTrue(groupByFile, "File should be selected");
            Assert.IsFalse(groupBySeverity, "Severity should remain deselected");
            Assert.IsFalse(groupByCategory, "Category should be deselected after clicking File");
        }

        [Test]
        public void GroupBySwitchFromSeverityToCategoryWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = false;
            window._groupBySeverity = true;
            window._groupByCategory = false;

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: false,
                groupByCategory: true
            );

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsFalse(groupByFile, "File should remain deselected");
            Assert.IsFalse(
                groupBySeverity,
                "Severity should be deselected after clicking Category"
            );
            Assert.IsTrue(groupByCategory, "Category should be selected");
        }

        [Test]
        public void GroupBySwitchFromCategoryToSeverityWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = false;
            window._groupBySeverity = false;
            window._groupByCategory = true;

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: true,
                groupByCategory: false
            );

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsFalse(groupByFile, "File should remain deselected");
            Assert.IsTrue(groupBySeverity, "Severity should be selected");
            Assert.IsFalse(
                groupByCategory,
                "Category should be deselected after clicking Severity"
            );
        }

        [Test]
        public void GroupByClickingAlreadySelectedFileDoesNotDeselect()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = true;
            window._groupBySeverity = false;
            window._groupByCategory = false;

            SimulateGroupByClickRaw(
                window,
                newGroupByFile: false,
                newGroupBySeverity: false,
                newGroupByCategory: false
            );

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsTrue(
                groupByFile,
                "File should remain selected when clicking already-selected File"
            );
            Assert.IsFalse(groupBySeverity, "Severity should remain deselected");
            Assert.IsFalse(groupByCategory, "Category should remain deselected");
        }

        [Test]
        public void GroupByClickingAlreadySelectedSeverityDoesNotDeselect()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = false;
            window._groupBySeverity = true;
            window._groupByCategory = false;

            SimulateGroupByClickRaw(
                window,
                newGroupByFile: false,
                newGroupBySeverity: false,
                newGroupByCategory: false
            );

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsFalse(groupByFile, "File should remain deselected");
            Assert.IsTrue(
                groupBySeverity,
                "Severity should remain selected when clicking already-selected Severity"
            );
            Assert.IsFalse(groupByCategory, "Category should remain deselected");
        }

        [Test]
        public void GroupByClickingAlreadySelectedCategoryDoesNotDeselect()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = false;
            window._groupBySeverity = false;
            window._groupByCategory = true;

            SimulateGroupByClickRaw(
                window,
                newGroupByFile: false,
                newGroupBySeverity: false,
                newGroupByCategory: false
            );

            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            Assert.IsFalse(groupByFile, "File should remain deselected");
            Assert.IsFalse(groupBySeverity, "Severity should remain deselected");
            Assert.IsTrue(
                groupByCategory,
                "Category should remain selected when clicking already-selected Category"
            );
        }

        [Test]
        public void GroupByRoundTripFileToSeverityBackToFileWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = true;
            window._groupBySeverity = false;
            window._groupByCategory = false;
            AssertExactlyOneGroupBySelected(window, "Initial: File");

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: true,
                groupByCategory: false
            );
            AssertExactlyOneGroupBySelected(window, "After switch to Severity");
            Assert.IsTrue(window._groupBySeverity, "Should be Severity");

            SimulateGroupByClick(
                window,
                groupByFile: true,
                groupBySeverity: false,
                groupByCategory: false
            );
            AssertExactlyOneGroupBySelected(window, "After switch back to File");
            Assert.IsTrue(window._groupByFile, "Should be File again");
        }

        [Test]
        public void GroupByRoundTripSeverityToCategoryBackToSeverityWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = false;
            window._groupBySeverity = true;
            window._groupByCategory = false;
            AssertExactlyOneGroupBySelected(window, "Initial: Severity");

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: false,
                groupByCategory: true
            );
            AssertExactlyOneGroupBySelected(window, "After switch to Category");
            Assert.IsTrue(window._groupByCategory, "Should be Category");

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: true,
                groupByCategory: false
            );
            AssertExactlyOneGroupBySelected(window, "After switch back to Severity");
            Assert.IsTrue(window._groupBySeverity, "Should be Severity again");
        }

        [Test]
        public void GroupByFullCycleFileSeverityCategoryFileWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = true;
            window._groupBySeverity = false;
            window._groupByCategory = false;

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: true,
                groupByCategory: false
            );
            Assert.IsTrue(window._groupBySeverity, "Step 1: Should be Severity");
            AssertExactlyOneGroupBySelected(window, "Step 1");

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: false,
                groupByCategory: true
            );
            Assert.IsTrue(window._groupByCategory, "Step 2: Should be Category");
            AssertExactlyOneGroupBySelected(window, "Step 2");

            SimulateGroupByClick(
                window,
                groupByFile: true,
                groupBySeverity: false,
                groupByCategory: false
            );
            Assert.IsTrue(window._groupByFile, "Step 3: Should be File");
            AssertExactlyOneGroupBySelected(window, "Step 3");
        }

        [Test]
        public void GroupByFullCycleReverseFileCategorySeverityFileWorks()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = true;
            window._groupBySeverity = false;
            window._groupByCategory = false;

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: false,
                groupByCategory: true
            );
            Assert.IsTrue(window._groupByCategory, "Step 1: Should be Category");
            AssertExactlyOneGroupBySelected(window, "Step 1");

            SimulateGroupByClick(
                window,
                groupByFile: false,
                groupBySeverity: true,
                groupByCategory: false
            );
            Assert.IsTrue(window._groupBySeverity, "Step 2: Should be Severity");
            AssertExactlyOneGroupBySelected(window, "Step 2");

            SimulateGroupByClick(
                window,
                groupByFile: true,
                groupBySeverity: false,
                groupByCategory: false
            );
            Assert.IsTrue(window._groupByFile, "Step 3: Should be File");
            AssertExactlyOneGroupBySelected(window, "Step 3");
        }

        [Test]
        public void GroupByRapidClickingMaintainsConsistentState()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = true;
            window._groupBySeverity = false;
            window._groupByCategory = false;

            for (int i = 0; i < 50; i++)
            {
                int selection = i % 3;
                switch (selection)
                {
                    case 0:
                        SimulateGroupByClick(
                            window,
                            groupByFile: true,
                            groupBySeverity: false,
                            groupByCategory: false
                        );
                        break;
                    case 1:
                        SimulateGroupByClick(
                            window,
                            groupByFile: false,
                            groupBySeverity: true,
                            groupByCategory: false
                        );
                        break;
                    case 2:
                        SimulateGroupByClick(
                            window,
                            groupByFile: false,
                            groupBySeverity: false,
                            groupByCategory: true
                        );
                        break;
                }

                AssertExactlyOneGroupBySelected(window, $"Iteration {i}");
            }
        }

        [Test]
        public void GroupByMultipleClicksOnSameButtonMaintainsSelection()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = true;
            window._groupBySeverity = false;
            window._groupByCategory = false;

            for (int i = 0; i < 10; i++)
            {
                SimulateGroupByClickRaw(
                    window,
                    newGroupByFile: false,
                    newGroupBySeverity: false,
                    newGroupByCategory: false
                );

                bool groupByFile = window._groupByFile;
                Assert.IsTrue(groupByFile, $"File should remain selected after click {i + 1}");
                AssertExactlyOneGroupBySelected(window, $"Click {i + 1}");
            }
        }

        [Test]
        public void GroupByMultipleClicksOnAlreadySelectedSeverityMaintainsSelection()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = false;
            window._groupBySeverity = true;
            window._groupByCategory = false;

            for (int i = 0; i < 10; i++)
            {
                SimulateGroupByClickRaw(
                    window,
                    newGroupByFile: false,
                    newGroupBySeverity: false,
                    newGroupByCategory: false
                );

                bool groupBySeverity = window._groupBySeverity;
                Assert.IsTrue(
                    groupBySeverity,
                    $"Severity should remain selected after click {i + 1}"
                );
                AssertExactlyOneGroupBySelected(window, $"Click {i + 1}");
            }
        }

        [Test]
        public void GroupByMultipleClicksOnAlreadySelectedCategoryMaintainsSelection()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._groupByFile = false;
            window._groupBySeverity = false;
            window._groupByCategory = true;

            for (int i = 0; i < 10; i++)
            {
                SimulateGroupByClickRaw(
                    window,
                    newGroupByFile: false,
                    newGroupBySeverity: false,
                    newGroupByCategory: false
                );

                bool groupByCategory = window._groupByCategory;
                Assert.IsTrue(
                    groupByCategory,
                    $"Category should remain selected after click {i + 1}"
                );
                AssertExactlyOneGroupBySelected(window, $"Click {i + 1}");
            }
        }

        [Test]
        public void GroupByAllPossibleTransitionsWork()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            string[] states = { "File", "Severity", "Category" };
            for (int from = 0; from < 3; from++)
            {
                for (int to = 0; to < 3; to++)
                {
                    if (from == to)
                    {
                        continue;
                    }

                    window._groupByFile = from == 0;
                    window._groupBySeverity = from == 1;
                    window._groupByCategory = from == 2;

                    SimulateGroupByClick(
                        window,
                        groupByFile: to == 0,
                        groupBySeverity: to == 1,
                        groupByCategory: to == 2
                    );

                    bool groupByFile = window._groupByFile;
                    bool groupBySeverity = window._groupBySeverity;
                    bool groupByCategory = window._groupByCategory;

                    Assert.AreEqual(
                        to == 0,
                        groupByFile,
                        $"Transition {states[from]} -> {states[to]}: File"
                    );
                    Assert.AreEqual(
                        to == 1,
                        groupBySeverity,
                        $"Transition {states[from]} -> {states[to]}: Severity"
                    );
                    Assert.AreEqual(
                        to == 2,
                        groupByCategory,
                        $"Transition {states[from]} -> {states[to]}: Category"
                    );
                    AssertExactlyOneGroupBySelected(
                        window,
                        $"Transition {states[from]} -> {states[to]}"
                    );
                }
            }
        }

        [Test]
        public void GroupByStatePreservedWhenNoButtonClicked()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            bool[][] states =
            {
                new[] { true, false, false },
                new[] { false, true, false },
                new[] { false, false, true },
            };

            foreach (bool[] state in states)
            {
                window._groupByFile = state[0];
                window._groupBySeverity = state[1];
                window._groupByCategory = state[2];

                SimulateGroupByClickRaw(
                    window,
                    newGroupByFile: state[0],
                    newGroupBySeverity: state[1],
                    newGroupByCategory: state[2]
                );

                bool groupByFile = window._groupByFile;
                bool groupBySeverity = window._groupBySeverity;
                bool groupByCategory = window._groupByCategory;

                Assert.AreEqual(state[0], groupByFile, "File state should be preserved");
                Assert.AreEqual(state[1], groupBySeverity, "Severity state should be preserved");
                Assert.AreEqual(state[2], groupByCategory, "Category state should be preserved");
            }
        }

        private void AssertExactlyOneGroupBySelected(
            UnityMethodAnalyzerWindow window,
            string context
        )
        {
            bool groupByFile = window._groupByFile;
            bool groupBySeverity = window._groupBySeverity;
            bool groupByCategory = window._groupByCategory;

            int selectedCount =
                (groupByFile ? 1 : 0) + (groupBySeverity ? 1 : 0) + (groupByCategory ? 1 : 0);
            Assert.AreEqual(
                1,
                selectedCount,
                $"[{context}] Exactly one grouping should be selected, but {selectedCount} were selected. "
                    + $"File={groupByFile}, Severity={groupBySeverity}, Category={groupByCategory}"
            );
        }

        /// <summary>
        /// Simulates clicking a group-by button by setting up state as if the button was clicked.
        /// The target grouping should be true, others false.
        /// </summary>
        private void SimulateGroupByClick(
            UnityMethodAnalyzerWindow window,
            bool groupByFile,
            bool groupBySeverity,
            bool groupByCategory
        )
        {
            SimulateGroupByClickRaw(window, groupByFile, groupBySeverity, groupByCategory);
        }

        /// <summary>
        /// Simulates the raw toggle return values and invokes the internal logic.
        /// This allows testing edge cases like clicking an already-selected button.
        /// </summary>
        private void SimulateGroupByClickRaw(
            UnityMethodAnalyzerWindow window,
            bool newGroupByFile,
            bool newGroupBySeverity,
            bool newGroupByCategory
        )
        {
            bool currentGroupByFile = window._groupByFile;
            bool currentGroupBySeverity = window._groupBySeverity;
            bool currentGroupByCategory = window._groupByCategory;

            bool fileClicked = newGroupByFile && !currentGroupByFile;
            bool severityClicked = newGroupBySeverity && !currentGroupBySeverity;
            bool categoryClicked = newGroupByCategory && !currentGroupByCategory;

            if (fileClicked)
            {
                window._groupByFile = true;
                window._groupBySeverity = false;
                window._groupByCategory = false;
            }
            else if (severityClicked)
            {
                window._groupByFile = false;
                window._groupBySeverity = true;
                window._groupByCategory = false;
            }
            else if (categoryClicked)
            {
                window._groupByFile = false;
                window._groupBySeverity = false;
                window._groupByCategory = true;
            }
        }

        [Test]
        public void InitializeCreatesAnalyzer()
        {
            // CreateInstance<EditorWindow> triggers OnEnable, which calls Initialize, so _analyzer already exists.
            _window = ScriptableObject.CreateInstance<UnityMethodAnalyzerWindow>(); // UNH-SUPPRESS: EditorWindow cleaned up in TearDown

            Assert.IsTrue(
                _window._analyzer != null,
                "Analyzer should be non-null after CreateInstance (OnEnable calls Initialize)"
            );

            MethodAnalyzer originalAnalyzer = _window._analyzer;

            _window.Initialize();

            Assert.IsTrue(
                _window._analyzer != null,
                "Analyzer should be non-null after explicit Initialize()"
            );

            Assert.AreNotSame(
                originalAnalyzer,
                _window._analyzer,
                "Initialize() should create a new analyzer instance"
            );
        }

        [Test]
        public void InitializeIsIdempotent()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            MethodAnalyzer firstAnalyzer = window._analyzer;

            window.Initialize();

            Assert.IsTrue(
                window._analyzer != null,
                "Analyzer should be non-null after second Initialize()"
            );
        }

        [Test]
        public void StartAnalysisWithUninitializedWindowSetsErrorMessage()
        {
            _window = ScriptableObject.CreateInstance<UnityMethodAnalyzerWindow>(); // UNH-SUPPRESS: EditorWindow cleaned up in TearDown

            // CreateInstance triggers OnEnable, which calls Initialize, so _analyzer must be nulled explicitly.
            _window._analyzer = null;
            _window._sourcePaths = new List<string> { _tempDir };

            _window.StartAnalysis();

            string statusMessage = _window._statusMessage;
            bool isAnalyzing = _window._isAnalyzing;

            Assert.AreEqual(
                "Analyzer not initialized",
                statusMessage,
                "Should indicate analyzer not initialized"
            );
            Assert.IsFalse(isAnalyzing, "Should not be analyzing when analyzer is null");
        }

        [UnityTest]
        public IEnumerator SuccessfulAnalysisResetsUIStateWithDiagnostics()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            AddCompilerDiagnostic("DiagnosticTest.cs");

            window._isAnalyzing = false;
            window._sourcePaths = new List<string> { _tempDir };

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;

            bool initialIsAnalyzing = window._isAnalyzing;
            string initialStatus = window._statusMessage;

            window.StartAnalysis();

            AnalysisWaitResult result = default;
            yield return WaitForAnalysisCompletion(window, 10f, r => result = r);

            Assert.IsFalse(
                result.IsAnalyzing,
                $"isAnalyzing should be false after completion. {result}"
            );
            Assert.AreEqual(
                0f,
                result.Progress,
                $"Progress should be 0 after completion. {result}"
            );
            Assert.IsTrue(
                result.StatusMessage.Contains("reported issues in selected directories")
                    || result.StatusMessage.Contains("Analysis failed"),
                $"Status should indicate completion or failure. {result}"
            );
        }

        [UnityTest]
        public IEnumerator CancellationCompletesWithinReasonableTime()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < 30; i++)
            {
                AddCompilerDiagnostic($"CancelTimeTest{i}.cs");
            }

            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();
            yield return null;

            window.CancelAnalysis();

            float startTime = Time.realtimeSinceStartup;
            float maxWaitTime = 5f;
            int frameCount = 0;

            while (window._isAnalyzing && Time.realtimeSinceStartup - startTime < maxWaitTime)
            {
                yield return null;
                frameCount++;
            }

            float elapsedTime = Time.realtimeSinceStartup - startTime;
            bool isAnalyzing = window._isAnalyzing;
            string status = window._statusMessage;

            Assert.IsFalse(
                isAnalyzing,
                $"Cancellation should complete within {maxWaitTime}s. Elapsed: {elapsedTime:F2}s, Frames: {frameCount}, Status: '{status}'"
            );
        }

        [UnityTest]
        public IEnumerator CancellationWorksWithSmallFileCount()
        {
            yield return CancellationWorksWithFileCount(5);
        }

        [UnityTest]
        public IEnumerator CancellationWorksWithMediumFileCount()
        {
            yield return CancellationWorksWithFileCount(20);
        }

        [UnityTest]
        public IEnumerator CancellationWorksWithLargeFileCount()
        {
            yield return CancellationWorksWithFileCount(50);
        }

        private IEnumerator CancellationWorksWithFileCount(int fileCount)
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < fileCount; i++)
            {
                AddCompilerDiagnostic($"VaryingTest{i}.cs");
            }

            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();
            yield return null;

            window.CancelAnalysis();

            float waitTime = 0f;
            float maxWaitTime = 10f;
            while (waitTime < maxWaitTime && window._isAnalyzing)
            {
                yield return null;
                waitTime += Time.deltaTime;
            }

            bool isAnalyzing = window._isAnalyzing;
            string status = window._statusMessage;

            Assert.IsFalse(
                isAnalyzing,
                $"Cancellation should work with {fileCount} files. Status: '{status}', WaitTime: {waitTime:F2}s"
            );
        }

        [Test]
        public void CancelAnalysisImmediatelyResetsStateWithActiveAnalysis()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource cts = new();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;
            window._analysisProgress = 0.75f;
            window._statusMessage = "Analyzing...";

            window.CancelAnalysis();

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;
            bool cancelled = cts.IsCancellationRequested;

            Assert.IsTrue(cancelled, "CancellationToken should be cancelled");
            Assert.IsFalse(isAnalyzing, "isAnalyzing should be immediately false");
            Assert.AreEqual(0f, progress, "Progress should be immediately reset to 0");
            Assert.AreEqual("Analysis cancelled", status, "Status should indicate cancellation");
        }

        [Test]
        public void CancelAnalysisIsIdempotentWithImmediateReset()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource cts = new();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;
            window._analysisProgress = 0.5f;

            window.CancelAnalysis();

            window._isAnalyzing = true;
            window._analysisProgress = 0.3f;
            window._statusMessage = "Analyzing again...";

            window.CancelAnalysis();

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;

            Assert.IsFalse(isAnalyzing, "isAnalyzing should be false after second cancel");
            Assert.AreEqual(0f, progress, "Progress should be reset after second cancel");
            Assert.AreEqual("Analysis cancelled", status, "Status should indicate cancellation");
        }

        [Test]
        public void CancelAnalysisWithDisposedCTSDoesNotThrow()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource cts = new();
            cts.Dispose();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;
            window._analysisProgress = 0.5f;
            window._statusMessage = "Analyzing...";

            window.CancelAnalysis();

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;

            window._cancellationTokenSource = null;

            Assert.IsFalse(isAnalyzing, "isAnalyzing should be false even with disposed CTS");
            Assert.AreEqual(0f, progress, "Progress should be reset even with disposed CTS");
            Assert.AreEqual(
                "Analysis cancelled",
                status,
                "Status should indicate cancellation even with disposed CTS"
            );
        }

        [TestCase(0, TestName = "CancellationWithZeroFiles")]
        [TestCase(1, TestName = "CancellationWithOneFile")]
        [TestCase(100, TestName = "CancellationWithManyFiles")]
        public void CancelAnalysisImmediateForVariousFileCounts(int fileCount)
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            for (int i = 0; i < fileCount; i++)
            {
                AddCompilerDiagnostic($"ImmediateTest{i}.cs");
            }

            CancellationTokenSource cts = new();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;
            window._analysisProgress = 0.5f;
            window._statusMessage = "Analyzing...";
            window._sourcePaths = new List<string> { _tempDir };

            window.CancelAnalysis();

            bool isAnalyzing = window._isAnalyzing;
            float progress = window._analysisProgress;
            string status = window._statusMessage;

            Assert.IsFalse(
                isAnalyzing,
                $"isAnalyzing should be immediately false with {fileCount} files"
            );
            Assert.AreEqual(
                0f,
                progress,
                $"Progress should be immediately reset with {fileCount} files"
            );
            Assert.AreEqual(
                "Analysis cancelled",
                status,
                $"Status should indicate cancellation with {fileCount} files"
            );
        }

        [Test]
        public void OnDisableWithDisposedCTSDoesNotThrow()
        {
            _window = ScriptableObject.CreateInstance<UnityMethodAnalyzerWindow>(); // UNH-SUPPRESS: EditorWindow cleaned up in TearDown

            CancellationTokenSource cts = new();
            cts.Dispose();
            _window._cancellationTokenSource = cts;

            _window.OnDisable();

            Assert.IsTrue(
                _window._cancellationTokenSource == null,
                "CancellationTokenSource should be null after OnDisable"
            );

            _window._cancellationTokenSource = null;
        }

        [Test]
        public void OnDisableWithCancelledCTSDoesNotThrow()
        {
            _window = ScriptableObject.CreateInstance<UnityMethodAnalyzerWindow>(); // UNH-SUPPRESS: EditorWindow cleaned up in TearDown

            CancellationTokenSource cts = new();
            cts.Cancel();
            _window._cancellationTokenSource = cts;

            _window.OnDisable();

            Assert.IsTrue(
                _window._cancellationTokenSource == null,
                "CancellationTokenSource should be null after OnDisable"
            );

            _window._cancellationTokenSource = null;
        }

        [Test]
        public void OnDisableWithNullCTSDoesNotThrow()
        {
            _window = ScriptableObject.CreateInstance<UnityMethodAnalyzerWindow>(); // UNH-SUPPRESS: EditorWindow cleaned up in TearDown

            _window._cancellationTokenSource = null;

            _window.OnDisable();

            Assert.IsTrue(
                _window._cancellationTokenSource == null,
                "CancellationTokenSource should remain null after OnDisable"
            );
        }

        [TestCase(
            "",
            "Analyzer not initialized",
            Description = "Empty analyzer message when analyzer is null"
        )]
        public void StartAnalysisWithNullAnalyzerSetsCorrectMessage(
            string unused,
            string expectedMessage
        )
        {
            _window = ScriptableObject.CreateInstance<UnityMethodAnalyzerWindow>(); // UNH-SUPPRESS: EditorWindow cleaned up in TearDown

            _window._analyzer = null;
            _window._sourcePaths = new List<string> { _tempDir };

            _window.StartAnalysis();

            Assert.AreEqual(
                expectedMessage,
                _window._statusMessage,
                "Status message should indicate analyzer not initialized"
            );
            Assert.IsFalse(_window._isAnalyzing, "Should not be analyzing");
        }

        [Test]
        public void StartAnalysisWhileAlreadyAnalyzingDoesNothing()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._isAnalyzing = true;
            window._statusMessage = "Already analyzing...";
            window._analysisProgress = 0.5f;
            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();

            Assert.IsTrue(window._isAnalyzing, "Should still be analyzing");
            Assert.AreEqual(
                "Already analyzing...",
                window._statusMessage,
                "Status should not change"
            );
            Assert.AreEqual(0.5f, window._analysisProgress, "Progress should not change");
        }

        [Test]
        public void AnalysisCompletionSourceIsSignaledOnCompletion()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;

            Assert.IsFalse(tcs.Task.IsCompleted, "TCS should not be completed initially");

            window.ResetAnalysisState();
            tcs.TrySetResult(true);

            Assert.IsTrue(tcs.Task.IsCompleted, "TCS should be completed after TrySetResult");
            Assert.IsTrue(tcs.Task.Result, "TCS result should be true");
        }

        [Test]
        public void FinalizeAnalysisResetsStateAndSignalsCompletion()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;
            window._isAnalyzing = true;
            window._analysisProgress = 0.5f;

            Assert.IsTrue(window._isAnalyzing, "Should be analyzing initially");
            Assert.IsFalse(tcs.Task.IsCompleted, "TCS should not be completed initially");

            window._cancellationTokenSource = new CancellationTokenSource();
            window.CancelAnalysis();

            Assert.IsFalse(window._isAnalyzing, "Should not be analyzing after finalize");
            Assert.AreEqual(0f, window._analysisProgress, "Progress should be reset");
            Assert.IsTrue(
                tcs.Task.IsCompleted,
                "TCS should be completed after FinalizeAnalysis via CancelAnalysis"
            );
        }

        [Test]
        public void FinalizeAnalysisIsIdempotentForTaskCompletionSource()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;
            window._cancellationTokenSource = new CancellationTokenSource();
            window._isAnalyzing = true;

            window.CancelAnalysis();
            window.CancelAnalysis();
            window.CancelAnalysis();

            Assert.IsTrue(tcs.Task.IsCompleted, "TCS should be completed");
            Assert.IsTrue(tcs.Task.Result, "TCS result should be true");
        }

        [Test]
        public void InitializeCanBeCalledMultipleTimesSafely()
        {
            _window = ScriptableObject.CreateInstance<UnityMethodAnalyzerWindow>(); // UNH-SUPPRESS: EditorWindow cleaned up in TearDown

            MethodAnalyzer firstAnalyzer = _window._analyzer;

            for (int i = 0; i < 5; i++)
            {
                _window.Initialize();
                Assert.IsTrue(
                    _window._analyzer != null,
                    $"Analyzer should be non-null after Initialize() call {i + 1}"
                );
            }
        }

        /// <summary>
        /// Data-driven test verifying that the TaskCompletionSource is always signaled
        /// when StartAnalysis is called with various source path configurations.
        /// </summary>
        [TestCase(
            null,
            true,
            "TCS should be completed when source paths is null (no-op early return)"
        )]
        [TestCase(new string[0], true, "TCS should be completed when source paths is empty")]
        [TestCase(
            new[] { "/nonexistent/path" },
            true,
            "TCS should be completed when directories dont exist"
        )]
        public void TaskCompletionSourceIsSignaledWithVariousSourcePathConfigs(
            string[] sourcePaths,
            bool shouldComplete,
            string description
        )
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            TaskCompletionSource<bool> tcs = new();
            window._analysisCompletionSource = tcs;
            window._sourcePaths = sourcePaths != null ? new List<string>(sourcePaths) : null;

            window.StartAnalysis();

            Assert.AreEqual(
                shouldComplete,
                tcs.Task.IsCompleted,
                $"{description}. Task status: {tcs.Task.Status}"
            );

            if (tcs.Task.IsCompleted)
            {
                Assert.IsTrue(tcs.Task.Result, "TCS result should be true when completed");
            }
        }

        [TestCase(null, false, Description = "Null source paths should not analyze")]
        public void StartAnalysisWithInvalidSourcePathsHandlesGracefully(
            List<string> sourcePaths,
            bool shouldStartAnalyzing
        )
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            window._sourcePaths = sourcePaths;

            window.StartAnalysis();
        }

        [Test]
        public void CancelAnalysisWithCancelledButNotDisposedCTSResetsState()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            CancellationTokenSource cts = new();
            cts.Cancel();
            window._cancellationTokenSource = cts;
            window._isAnalyzing = true;
            window._analysisProgress = 0.75f;
            window._statusMessage = "Analyzing...";

            window.CancelAnalysis();

            Assert.IsFalse(window._isAnalyzing, "Should not be analyzing after cancel");
            Assert.AreEqual(0f, window._analysisProgress, "Progress should be reset");
            Assert.AreEqual(
                "Analysis cancelled",
                window._statusMessage,
                "Status should indicate cancelled"
            );

            window._cancellationTokenSource = null;
            cts.Dispose();
        }

        [Test]
        public void FlushMainThreadQueueDoesNothingWhenEmpty()
        {
            UnityMethodAnalyzerWindow.FlushMainThreadQueue();
            Assert.Pass("FlushMainThreadQueue executed without throwing when queue was empty");
        }

        [Test]
        public void FlushMainThreadQueueIsIdempotent()
        {
            UnityMethodAnalyzerWindow.FlushMainThreadQueue();
            UnityMethodAnalyzerWindow.FlushMainThreadQueue();
            UnityMethodAnalyzerWindow.FlushMainThreadQueue();
            Assert.Pass("FlushMainThreadQueue is idempotent");
        }

        [Test]
        public void AnalysisTaskIsNullBeforeStartAnalysis()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            Task analysisTask = window._analysisTask;

            Assert.IsTrue(
                analysisTask == null,
                "Analysis task should be null before StartAnalysis is called"
            );
        }

        [Test]
        public void AnalysisTaskIsSetAfterStartAnalysisWithValidDirectory()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            AddCompilerDiagnostic("TaskTest.cs");
            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();

            Task analysisTask = window._analysisTask;

            Assert.IsTrue(
                analysisTask != null,
                "Analysis task should be set after StartAnalysis with valid directory"
            );
        }

        [Test]
        public void AnalysisTaskIsNullAfterStartAnalysisWithNoValidDirectory()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            window._sourcePaths = new List<string> { "/nonexistent/path" };

            window.StartAnalysis();

            Task analysisTask = window._analysisTask;

            Assert.IsTrue(
                analysisTask == null,
                "Analysis task should remain null when no valid directories"
            );
        }

        [UnityTest]
        public IEnumerator AnalysisTaskCompletesWhenAnalysisFinishes()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            AddCompilerDiagnostic("CompletionTest.cs");
            window._sourcePaths = new List<string> { _tempDir };

            Assert.IsTrue(Directory.Exists(_tempDir), $"Temp directory should exist: {_tempDir}");

            string testFilePath = Path.Combine(_tempDir, "CompletionTest.cs");
            Assert.IsFalse(
                File.Exists(testFilePath),
                "The captured compiler report must remain readable without its source file."
            );

            window.StartAnalysis();

            Task analysisTask = window._analysisTask;
            Assert.IsTrue(
                analysisTask != null,
                $"Analysis task should be set. IsAnalyzing: {window._isAnalyzing}, Status: '{window._statusMessage}'"
            );

            float startRealTime = Time.realtimeSinceStartup;
            float maxWaitTime = 10f;
            int frameCount = 0;

            while (
                (Time.realtimeSinceStartup - startRealTime) < maxWaitTime
                && !analysisTask.IsCompleted
            )
            {
                yield return null;
                frameCount++;

                if (frameCount % 100 == 0)
                {
                    float elapsed = Time.realtimeSinceStartup - startRealTime;
                    UnityEngine.Debug.Log(
                        $"[AnalysisTaskTest] Frame {frameCount}, Elapsed: {elapsed:F2}s, "
                            + $"TaskStatus: {analysisTask.Status}, IsCompleted: {analysisTask.IsCompleted}, "
                            + $"IsFaulted: {analysisTask.IsFaulted}, IsCanceled: {analysisTask.IsCanceled}"
                    );
                }
            }

            float totalWaitTime = Time.realtimeSinceStartup - startRealTime;

            string exceptionInfo = "";
            if (analysisTask.IsFaulted && analysisTask.Exception != null)
            {
                exceptionInfo = $", Exception: {analysisTask.Exception.GetBaseException().Message}";
            }

            Assert.IsTrue(
                analysisTask.IsCompleted,
                $"Analysis task should complete. Status: {analysisTask.Status}, "
                    + $"RealWaitTime: {totalWaitTime:F2}s, Frames: {frameCount}{exceptionInfo}"
            );
            Assert.IsTrue(
                analysisTask.Status == TaskStatus.RanToCompletion,
                $"Analysis task should run to completion. Status: {analysisTask.Status}{exceptionInfo}"
            );
            Assert.AreEqual(1, window._analyzer.Issues.Count);
            Assert.AreEqual(testFilePath, window._analyzer.Issues[0].FilePath);
            Assert.AreEqual("WUH015", window._analyzer.Issues[0].IssueType);
        }

        [UnityTest]
        public IEnumerator AnalysisTaskFaultsWhenExceptionOccurs()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();
            AddCompilerDiagnostic("FaultTest.cs");
            window._sourcePaths = new List<string> { _tempDir };

            window.StartAnalysis();

            Task analysisTask = window._analysisTask;
            Assert.IsTrue(analysisTask != null, "Analysis task should be set");

            AnalysisWaitResult result = default;
            yield return WaitForAnalysisCompletion(window, 10f, r => result = r);

            Assert.IsTrue(result.AnalysisTaskCompleted, $"Analysis task should complete. {result}");
        }

        /// <summary>
        /// Diagnostic test that uses Task.Wait to determine if the issue is with
        /// Unity coroutines or with the task itself not completing.
        /// </summary>
        [Test]
        public void AnalysisTaskCompletesWithBlockingWait()
        {
            UnityMethodAnalyzerWindow window = CreateWindow();

            string testFilePath = Path.Combine(_tempDir, "BlockingWaitTest.cs");
            File.WriteAllText(testFilePath, "public class BlockingWaitTest { }");

            window._sourcePaths = new List<string> { _tempDir };

            Assert.IsTrue(Directory.Exists(_tempDir), $"Temp dir should exist: {_tempDir}");
            Assert.IsTrue(File.Exists(testFilePath), $"Test file should exist: {testFilePath}");
            Assert.IsTrue(window._analyzer != null, "Analyzer should be initialized");

            window.StartAnalysis();

            Task analysisTask = window._analysisTask;
            Assert.IsTrue(
                analysisTask != null,
                $"Analysis task should be set. IsAnalyzing: {window._isAnalyzing}, Status: '{window._statusMessage}'"
            );

            bool completed = analysisTask.Wait(TimeSpan.FromSeconds(30));

            string exceptionInfo = "";
            if (analysisTask.IsFaulted && analysisTask.Exception != null)
            {
                exceptionInfo = $", Exception: {analysisTask.Exception.GetBaseException()}";
            }

            Assert.IsTrue(
                completed,
                $"Task should complete within timeout. Status: {analysisTask.Status}, "
                    + $"IsFaulted: {analysisTask.IsFaulted}, IsCanceled: {analysisTask.IsCanceled}{exceptionInfo}"
            );

            Assert.IsTrue(
                analysisTask.Status == TaskStatus.RanToCompletion,
                $"Task should run to completion. Status: {analysisTask.Status}{exceptionInfo}"
            );

            UnityMethodAnalyzerWindow.FlushMainThreadQueue();
        }

        /// <summary>
        /// Tests the MethodAnalyzer directly without going through the window,
        /// to isolate whether the issue is in the analyzer or the window integration.
        /// </summary>
        [Test]
        public void MethodAnalyzerDirectlyCompletesWithBlockingWait()
        {
            string testFilePath = Path.Combine(_tempDir, "DirectAnalyzerTest.cs");
            File.WriteAllText(testFilePath, "public class DirectAnalyzerTest { }");

            MethodAnalyzer analyzer = new();

            Task analysisTask = analyzer.AnalyzeAsync(
                _tempDir,
                new List<string> { _tempDir },
                progress: null,
                cancellationToken: CancellationToken.None
            );

            bool completed = analysisTask.Wait(TimeSpan.FromSeconds(30));

            string exceptionInfo = "";
            if (analysisTask.IsFaulted && analysisTask.Exception != null)
            {
                exceptionInfo = $", Exception: {analysisTask.Exception.GetBaseException()}";
            }

            Assert.IsTrue(
                completed,
                $"Direct analyzer task should complete within timeout. Status: {analysisTask.Status}, "
                    + $"IsFaulted: {analysisTask.IsFaulted}, IsCanceled: {analysisTask.IsCanceled}{exceptionInfo}"
            );

            Assert.IsTrue(
                analysisTask.Status == TaskStatus.RanToCompletion,
                $"Direct analyzer task should run to completion. Status: {analysisTask.Status}{exceptionInfo}"
            );
        }

        /// <summary>
        /// Tests the MethodAnalyzer with a Progress callback to see if that's causing the hang.
        /// </summary>
        [Test]
        public void MethodAnalyzerWithProgressCompletesWithBlockingWait()
        {
            string testFilePath = Path.Combine(_tempDir, "ProgressAnalyzerTest.cs");
            File.WriteAllText(testFilePath, "public class ProgressAnalyzerTest { }");

            MethodAnalyzer analyzer = new();

            List<float> progressValues = new();
            Progress<float> progress = new(p => progressValues.Add(p));

            Task analysisTask = analyzer.AnalyzeAsync(
                _tempDir,
                new List<string> { _tempDir },
                progress: progress,
                cancellationToken: CancellationToken.None
            );

            bool completed = analysisTask.Wait(TimeSpan.FromSeconds(30));

            string exceptionInfo = "";
            if (analysisTask.IsFaulted && analysisTask.Exception != null)
            {
                exceptionInfo = $", Exception: {analysisTask.Exception.GetBaseException()}";
            }

            Assert.IsTrue(
                completed,
                $"Analyzer with progress should complete within timeout. Status: {analysisTask.Status}, "
                    + $"IsFaulted: {analysisTask.IsFaulted}, IsCanceled: {analysisTask.IsCanceled}, "
                    + $"ProgressReports: {progressValues.Count}{exceptionInfo}"
            );

            Assert.IsTrue(
                analysisTask.Status == TaskStatus.RanToCompletion,
                $"Analyzer with progress should run to completion. Status: {analysisTask.Status}{exceptionInfo}"
            );
        }

        /// <summary>
        /// Tests that the compatibility API reads the compiler snapshot synchronously.
        /// </summary>
        [Test]
        public void MethodAnalyzerSynchronousAnalyzeCompletes()
        {
            string testFilePath = Path.Combine(_tempDir, "SyncAnalyzerTest.cs");
            File.WriteAllText(testFilePath, "public class SyncAnalyzerTest { }");

            MethodAnalyzer analyzer = new();

            analyzer.Analyze(_tempDir, new List<string> { _tempDir });

            Assert.Pass("Synchronous analysis completed without hanging");
        }

        /// <summary>
        /// Tests a minimal async operation to verify Task.Wait works in Unity tests.
        /// </summary>
        [Test]
        public void MinimalAsyncTaskCompletesWithBlockingWait()
        {
            Task simpleTask = Task.Run(() =>
            {
                int sum = 0;
                for (int i = 0; i < 100; i++)
                {
                    sum += i;
                }
                return sum;
            });

            bool completed = simpleTask.Wait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(completed, $"Simple task should complete. Status: {simpleTask.Status}");
        }

        /// <summary>
        /// Tests Task.Run with async lambda to verify that's not the issue.
        /// </summary>
        [Test]
        public void TaskRunWithAsyncLambdaCompletesWithBlockingWait()
        {
            Task asyncTask = Task.Run(async () =>
            {
                await Task.Delay(10);
                return 42;
            });

            bool completed = asyncTask.Wait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(
                completed,
                $"Task.Run with async lambda should complete. Status: {asyncTask.Status}"
            );
        }

        /// <summary>
        /// Tests File.ReadAllTextAsync to verify async file IO works.
        /// </summary>
        [Test]
        public void FileReadAllTextAsyncCompletesWithBlockingWait()
        {
            string testFilePath = Path.Combine(_tempDir, "AsyncFileTest.cs");
            File.WriteAllText(testFilePath, "public class AsyncFileTest { }");

            Task<string> readTask = File.ReadAllTextAsync(testFilePath);

            bool completed = readTask.Wait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(
                completed,
                $"File.ReadAllTextAsync should complete. Status: {readTask.Status}"
            );
            Assert.IsTrue(
                readTask.Result.Contains("AsyncFileTest"),
                "File content should be correct"
            );
        }

        /// <summary>
        /// Tests SemaphoreSlim.WaitAsync to verify that's not causing issues.
        /// </summary>
        [Test]
        public void SemaphoreSlimWaitAsyncCompletesWithBlockingWait()
        {
            using SemaphoreSlim semaphore = new(4);

            Task semaphoreTask = Task.Run(async () =>
            {
                await semaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    await Task.Delay(10).ConfigureAwait(false);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            bool completed = semaphoreTask.Wait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(
                completed,
                $"SemaphoreSlim.WaitAsync should complete. Status: {semaphoreTask.Status}"
            );
        }

        /// <summary>
        /// Captures diagnostic information about an analysis wait operation.
        /// </summary>
        private struct AnalysisWaitResult
        {
            public float WaitTime;
            public int FrameCount;
            public bool AnalysisTaskCompleted;
            public TaskStatus AnalysisTaskStatus;
            public bool CompletionSourceSignaled;
            public TaskStatus CompletionSourceStatus;
            public bool IsAnalyzing;
            public float Progress;
            public string StatusMessage;

            public override readonly string ToString()
            {
                return $"WaitTime: {WaitTime:F2}s, Frames: {FrameCount}, "
                    + $"AnalysisTaskCompleted: {AnalysisTaskCompleted}, AnalysisTaskStatus: {AnalysisTaskStatus}, "
                    + $"TCSCompleted: {CompletionSourceSignaled}, TCSStatus: {CompletionSourceStatus}, "
                    + $"IsAnalyzing: {IsAnalyzing}, Progress: {Progress}, Status: '{StatusMessage}'";
            }
        }
    }
}
#endif
