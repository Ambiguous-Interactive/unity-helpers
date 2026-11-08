// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_TEST_FRAMEWORK
    using WallstopStudios.UnityHelpers.Core.Extension;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEditor.TestTools.TestRunner.Api;
    using UnityEngine;

    /// <summary>
    ///     Starts a test run from a menu item and writes a pollable summary file, for a process
    ///     driving an editor it does not own.
    ///     Requires the Unity Test Framework package.
    /// </summary>
    internal sealed class TestRunReporter : ICallbacks
    {
        private const string EditModeMenuPath =
            "Tools/Wallstop Studios/Unity Helpers/Run EditMode Tests With Summary";
        private const string PlayModeMenuPath =
            "Tools/Wallstop Studios/Unity Helpers/Run PlayMode Tests With Summary";
        private const string LogPrefix = "[TestRunReporter] ";
        private const string OwnerSessionKey = nameof(TestRunReporter) + "." + nameof(_owner);
        private const string RunStartedSessionKey =
            nameof(TestRunReporter) + "." + nameof(_runStarted);

        private static TestRunReporter _instance;
        private static TestRunnerApi _api;
        private static string _owner;
        private static bool _runStarted;

        /// <summary>
        ///     Reports whether either mode's summary file is currently held by a run in flight.
        /// </summary>
        /// <returns><c>true</c> when a run holds a summary file.</returns>
        internal static bool IsAnyRunInFlight()
        {
            return TryFindRunInFlight(out TestMode _, out string _, out string _);
        }

        /// <summary>
        ///     Claims a mode's summary file and asks the Test Runner to execute it, returning
        ///     without waiting for the run to finish.
        /// </summary>
        /// <param name="mode">The test mode to run.</param>
        /// <returns><c>true</c> when the run was started.</returns>
        internal static bool StartRun(TestMode mode)
        {
            if (!TestRunSummaryFile.TryGetSummaryPath(mode, out string summaryPath))
            {
                Debug.LogWarning($"{LogPrefix}No summary file is defined for test mode {mode}.");
                return false;
            }

            if (TryFindRunInFlight(out TestMode runningMode, out string runningPath, out string _))
            {
                Debug.LogWarning(
                    $"{LogPrefix}Refusing to start {mode}: a {runningMode} run still holds {runningPath}. Delete that file to recover a run that was cancelled or lost."
                );
                return false;
            }

            TestMode competingMode =
                mode == TestMode.EditMode ? TestMode.PlayMode : TestMode.EditMode;
            if (
                !TestRunSummaryFile.TryGetSummaryPath(
                    competingMode,
                    out string competingSummaryPath
                )
            )
            {
                Debug.LogError(
                    $"{LogPrefix}No competing summary file is defined for test mode {competingMode}."
                );
                return false;
            }

            if (
                !TestRunSummaryFile.TryBeginRun(
                    summaryPath,
                    mode,
                    DateTime.UtcNow,
                    out string owner,
                    competingSummaryPath
                )
            )
            {
                Debug.LogError($"{LogPrefix}Could not write the running marker to {summaryPath}.");
                return false;
            }

            SetActiveOwner(owner);

            if (!TryEnsureRegistered())
            {
                TestRunSummaryFile.TryDiscardRun(summaryPath, owner);
                ClearActiveOwner();
                return false;
            }

            try
            {
                _api.Execute(new ExecutionSettings(new Filter { testMode = mode }));
            }
            catch (Exception exception)
            {
                TestRunSummaryFile.TryDiscardRun(summaryPath, owner);
                ClearActiveOwner();
                Debug.LogError($"{LogPrefix}The Test Runner refused a {mode} run: {exception}");
                return false;
            }

            Debug.Log($"{LogPrefix}Started {mode} tests. Summary: {summaryPath}");
            return true;
        }

        /// <summary>
        ///     Registers Test Runner callbacks when, and only when, a run still holds a summary file.
        /// </summary>
        /// <returns><c>true</c> when a run is in flight and callbacks are registered for it.</returns>
        internal static bool TryRegisterForRunInFlight()
        {
            if (!TryFindRunInFlight(out TestMode _, out string _, out string owner))
            {
                return false;
            }

            string sessionOwner = SessionState.GetString(OwnerSessionKey, string.Empty);
            if (!string.Equals(owner, sessionOwner, StringComparison.Ordinal))
            {
                return false;
            }

            _owner = owner;
            _runStarted = SessionState.GetBool(RunStartedSessionKey, defaultValue: false);
            return TryEnsureRegistered();
        }

        internal static void ClearActiveOwner()
        {
            _owner = string.Empty;
            _runStarted = false;
            SessionState.EraseString(OwnerSessionKey);
            SessionState.EraseBool(RunStartedSessionKey);
        }

        /// <summary>Collects assembly build times and source freshness for a result tree.</summary>
        internal static void PopulateAssemblyBuildTimes(TestRunResultNode root)
        {
            if (root == null)
            {
                return;
            }
            try
            {
                UnityEditor.Compilation.Assembly[] assemblies = CompilationPipeline.GetAssemblies(
                    AssembliesType.Editor
                );
                List<TestRunAssemblyInput> inputs = new(assemblies.Length);
                Dictionary<string, string> packages = new(StringComparer.Ordinal);
                foreach (UnityEditor.Compilation.Assembly assembly in assemblies)
                {
                    if (assembly == null || string.IsNullOrWhiteSpace(assembly.name))
                    {
                        continue;
                    }
                    List<string> references = new();
                    foreach (
                        UnityEditor.Compilation.Assembly reference in assembly.assemblyReferences
                    )
                    {
                        if (reference != null)
                        {
                            references.Add(reference.name);
                        }
                    }
                    int sourceCount = assembly.sourceFiles.Length;
                    string[] sources = new string[sourceCount];
                    for (int index = 0; index < sourceCount; ++index)
                    {
                        sources[index] = TestRunCompilationPaths.Resolve(
                            assembly.sourceFiles[index],
                            packages
                        );
                    }
                    inputs.Add(
                        new TestRunAssemblyInput(
                            assembly.name,
                            TestRunCompilationPaths.Resolve(assembly.outputPath, packages),
                            sources,
                            references.ToArray()
                        )
                    );
                }
                TestRunAssemblyFreshness.Populate(root, inputs);
            }
            catch (Exception exception)
            {
                foreach (TestRunResultNode node in root.children)
                {
                    if (node != null)
                    {
                        node.freshnessErrors.Add(
                            $"Cannot enumerate compilation inputs: {exception.Message}"
                        );
                    }
                }
                Debug.LogWarning(
                    $"{LogPrefix}Could not enumerate compiled assemblies: {exception}"
                );
            }
        }

        // Re-register synchronously after reload so early RunFinished events are captured even when delayCall never runs.
        [InitializeOnLoadMethod]
        private static void RegisterAfterDomainReload()
        {
            TryRegisterForRunInFlight();
            EditorApplication.delayCall -= RegisterWhenRunInFlight;
            EditorApplication.delayCall += RegisterWhenRunInFlight;
        }

        [MenuItem(EditModeMenuPath, priority = 110)]
        private static void RunEditModeTestsMenuItem()
        {
            StartRun(TestMode.EditMode);
        }

        [MenuItem(PlayModeMenuPath, priority = 111)]
        private static void RunPlayModeTestsMenuItem()
        {
            StartRun(TestMode.PlayMode);
        }

        private static void RegisterWhenRunInFlight()
        {
            EditorApplication.delayCall -= RegisterWhenRunInFlight;
            TryRegisterForRunInFlight();
        }

        private static bool TryFindRunInFlight(
            out TestMode mode,
            out string summaryPath,
            out string owner
        )
        {
            if (TryFindRunInFlight(TestMode.EditMode, out summaryPath, out owner))
            {
                mode = TestMode.EditMode;
                return true;
            }

            if (TryFindRunInFlight(TestMode.PlayMode, out summaryPath, out owner))
            {
                mode = TestMode.PlayMode;
                return true;
            }

            mode = TestMode.EditMode;
            owner = string.Empty;
            return false;
        }

        private static bool TryFindRunInFlight(
            TestMode mode,
            out string summaryPath,
            out string owner
        )
        {
            owner = string.Empty;
            return TestRunSummaryFile.TryGetSummaryPath(mode, out summaryPath)
                && TestRunSummaryFile.TryReadOwner(summaryPath, out owner);
        }

        private static bool TryEnsureRegistered()
        {
            try
            {
                if (_api == null)
                {
                    _api = ScriptableObject.CreateInstance<TestRunnerApi>();
                    _api.hideFlags = HideFlags.HideAndDontSave;
                    _instance = null;
                }

                if (_instance == null)
                {
                    _instance = new TestRunReporter();
                    _api.RegisterCallbacks(_instance);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix}Could not register Test Runner callbacks: {exception}");
                return false;
            }
        }

        private static void SetActiveOwner(string owner)
        {
            _owner = owner;
            _runStarted = false;
            SessionState.SetString(OwnerSessionKey, owner);
            SessionState.SetBool(RunStartedSessionKey, value: false);
        }

        private static TestRunResultNode BuildNode(ITestResultAdaptor result, int depth)
        {
            if (result == null || TestRunSummaryFormatter.MaximumTreeDepth <= depth)
            {
                return null;
            }

            TestRunResultNode node = new()
            {
                fullName = result.FullName ?? string.Empty,
                status = result.TestStatus,
                message = result.Message ?? string.Empty,
                stackTrace = result.StackTrace ?? string.Empty,
                durationSeconds = result.Duration,
            };

            IEnumerable<ITestResultAdaptor> children = result.Children;
            if (children == null)
            {
                return node;
            }

            foreach (ITestResultAdaptor child in children.AsValueEnumerableRequired())
            {
                TestRunResultNode childNode = BuildNode(child, depth + 1);
                if (childNode == null)
                {
                    continue;
                }

                node.children.Add(childNode);
            }

            return node;
        }

        /// <summary>
        ///     Called by the Test Runner when a run begins; the summary file was already claimed by
        ///     the menu item that started it.
        /// </summary>
        /// <param name="testsToRun">The test tree that will be executed.</param>
        void ICallbacks.RunStarted(ITestAdaptor testsToRun)
        {
            if (string.IsNullOrWhiteSpace(_owner))
            {
                return;
            }

            if (_runStarted)
            {
                ClearActiveOwner();
                return;
            }

            _runStarted = true;
            SessionState.SetBool(RunStartedSessionKey, value: true);
        }

        /// <summary>
        ///     Called by the Test Runner when an individual test begins. No action is taken.
        /// </summary>
        /// <param name="test">The test that is starting.</param>
        void ICallbacks.TestStarted(ITestAdaptor test) { }

        /// <summary>
        ///     Called by the Test Runner when an individual test finishes. No action is taken; the
        ///     whole tree is walked once at the end instead.
        /// </summary>
        /// <param name="result">The result of the completed test.</param>
        void ICallbacks.TestFinished(ITestResultAdaptor result) { }

        /// <summary>
        ///     Called by the Test Runner when a run finishes. Replaces the running marker of
        ///     whichever summary file this reporter claimed with the completed summary.
        /// </summary>
        /// <param name="result">The aggregate result of the test run.</param>
        void ICallbacks.RunFinished(ITestResultAdaptor result)
        {
            DateTime finishedUtc = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(_owner) || !_runStarted)
            {
                return;
            }

            if (!TryFindRunInFlight(out TestMode mode, out string summaryPath, out string owner))
            {
                return;
            }

            if (!string.Equals(owner, _owner, StringComparison.Ordinal))
            {
                return;
            }

            TestRunResultNode root = BuildNode(result, 0);
            PopulateAssemblyBuildTimes(root);

            if (!TestRunSummaryFile.TryFinishRun(summaryPath, _owner, mode, finishedUtc, root))
            {
                Debug.LogError($"{LogPrefix}Could not write the {mode} summary to {summaryPath}.");
                return;
            }

            ClearActiveOwner();
            Debug.Log($"{LogPrefix}Wrote the {mode} summary to {summaryPath}.");
        }
    }
#endif
}
