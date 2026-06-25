// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Core.TestUtils
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.TestTools.TestRunner.Api;
    using UnityEngine;

    /// <summary>
    /// CI diagnostic: streams per-test results to the Unity log AS EACH TEST FINISHES, so a
    /// run that aborts or domain-reloads mid-run (and serializes a misleading
    /// <c>total=0</c> <c>results.xml</c>) STILL names the last test that actually ran -- and
    /// any failures -- in the captured <c>unity.log</c>. This is the data-backed key for the
    /// PlayMode <c>total=0</c> / exit-2 failures: the project results XML loses the cases,
    /// but the streamed log lines survive.
    /// </summary>
    /// <remarks>
    /// Gated on the <c>UH_STREAM_TEST_RESULTS</c> environment variable (set by the CI run
    /// step) so it is a complete no-op in normal interactive editor use. Registered via
    /// <see cref="TestRunnerApi.RegisterCallbacks"/> from <c>[InitializeOnLoad]</c>: those
    /// callbacks fire for ANY run in the editor session, including the command-line
    /// <c>-runTests</c> EditMode and PlayMode passes (the editor orchestrates PlayMode runs,
    /// so an editor-registered callback observes them). Re-registration on each domain reload
    /// is harmless -- the logger is stateless.
    /// </remarks>
    // UNH-SUPPRESS UNH003: This is a CI diagnostic utility ([InitializeOnLoad]), NOT a test class.
    // It has no [Test] methods; it creates a TestRunnerApi (a ScriptableObject) only to register a
    // global per-test stream logger, so it neither inherits CommonTestBase nor tracks that instance.
    [InitializeOnLoad]
    internal static class CiTestResultStreamLogger
    {
        private const string EnableEnvVar = "UH_STREAM_TEST_RESULTS";
        private const string Prefix = "[uh-test-stream]";

        static CiTestResultStreamLogger()
        {
            string flag = Environment.GetEnvironmentVariable(EnableEnvVar);
            if (string.IsNullOrEmpty(flag) || flag == "0" || flag == "false")
            {
                return;
            }

            // Drop stack traces for plain Log-level messages while streaming. Each
            // streamed line is a Debug.Log, and Unity prepends a ~38-frame stack trace
            // to every Log in batchmode -- with one TEST-STARTED + one status line per
            // leaf across ~thousands of tests that is the dominant cost of the captured
            // unity.log (size + the time spent extracting traces). The streamed text
            // already carries the test name, so the trace is pure noise here. Errors and
            // warnings KEEP their traces (only LogType.Log is changed), and this is gated
            // on the CI env var below, so interactive editor sessions are untouched.
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);

            TestRunnerApi api = ScriptableObject.CreateInstance<TestRunnerApi>(); // UNH-SUPPRESS UNH002: long-lived global callback registrar, not a per-test object
            api.RegisterCallbacks(new StreamingCallbacks());
            Debug.Log($"{Prefix} registered (env {EnableEnvVar}={flag}).");
        }

        private sealed class StreamingCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                Debug.Log(
                    $"{Prefix} RUN-STARTED root='{(testsToRun != null ? testsToRun.FullName : "<null>")}'"
                );
            }

            public void TestStarted(ITestAdaptor test)
            {
                // Stream the leaf as it STARTS (not just when it finishes). A test that
                // begins but never finishes -- a hang/deadlock or a mid-run domain reload --
                // leaves a TEST-STARTED line with NO matching status line below it. That
                // dangling TEST-STARTED is the authoritative name of the culprit when the leg
                // is tree-killed before results.xml is written (the historical PlayMode
                // total=0 failure mode). Suites are skipped so the signal stays per-leaf.
                if (test == null || test.IsSuite)
                {
                    return;
                }

                Debug.Log($"{Prefix} TEST-STARTED {test.FullName}");
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result?.Test == null || result.Test.IsSuite)
                {
                    return;
                }

                // One immediately-flushed line per leaf case. The LAST such line before an
                // abort/reload names the culprit even when results.xml ends up total=0.
                Debug.Log(
                    $"{Prefix} {result.TestStatus} {result.Test.FullName} ({result.Duration:F3}s)"
                );

                if (result.TestStatus == TestStatus.Failed)
                {
                    string message = (result.Message ?? string.Empty)
                        .Replace("\r", " ")
                        .Replace("\n", " ");
                    Debug.Log($"{Prefix} FAIL {result.Test.FullName} :: {message}");
                }
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                int passed = 0;
                int failed = 0;
                int other = 0;
                CountLeaves(result, ref passed, ref failed, ref other);
                int total = passed + failed + other;
                Debug.Log(
                    $"{Prefix} RUN-FINISHED status={(result != null ? result.TestStatus.ToString() : "<null>")} "
                        + $"leaves={total} passed={passed} failed={failed} other={other}"
                );

                // The smoking gun: the framework walked tests (status often Failed) yet
                // serialized zero leaves -> the project results.xml will be the misleading
                // total=0. Shout it so the log makes the cause unambiguous.
                if (total == 0)
                {
                    Debug.LogWarning(
                        $"{Prefix} ZERO-COUNT run: 0 leaf cases reached the result tree "
                            + "(mid-run domain reload / abort). The streamed per-test lines "
                            + "above are the authoritative record; results.xml is not."
                    );
                }
            }

            private static void CountLeaves(
                ITestResultAdaptor result,
                ref int passed,
                ref int failed,
                ref int other
            )
            {
                if (result?.Test == null)
                {
                    return;
                }

                if (!result.Test.IsSuite)
                {
                    switch (result.TestStatus)
                    {
                        case TestStatus.Passed:
                            passed++;
                            break;
                        case TestStatus.Failed:
                            failed++;
                            break;
                        default:
                            other++;
                            break;
                    }

                    return;
                }

                IEnumerable<ITestResultAdaptor> children = result.Children;
                if (children == null)
                {
                    return;
                }

                foreach (ITestResultAdaptor child in children)
                {
                    CountLeaves(child, ref passed, ref failed, ref other);
                }
            }
        }
    }
}
#endif
