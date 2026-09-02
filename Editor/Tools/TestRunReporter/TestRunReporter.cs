// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
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
    /// </summary>
    internal sealed class TestRunReporter : ICallbacks
    {
        private const string EditModeMenuPath =
            "Tools/Wallstop Studios/Unity Helpers/Run EditMode Tests With Summary";
        private const string PlayModeMenuPath =
            "Tools/Wallstop Studios/Unity Helpers/Run PlayMode Tests With Summary";
        private const string LogPrefix = "[TestRunReporter] ";
        private const string CompiledAssemblyExtension = ".dll";

        private static TestRunReporter _instance;
        private static TestRunnerApi _api;

        /*
            A PlayMode run reloads the domain, which destroys the registered callbacks along with
            everything else managed. Nothing is carried across it in memory: the summary file itself
            is the state, and this re-registers on every load while a run still holds one.

            Registration happens HERE, synchronously, and delayCall is only a retry. Two reasons,
            and the second is the one that decides it. The Test Runner can broadcast RunFinished
            while the domain is still loading, so a callback registered a tick later misses the only
            event it exists for. And WProtoSubtypeTagAutoAssign measured that an editor nobody is
            interacting with -- "a background window, a CI editor driven over a socket" -- may not
            pump delayCall at all, with a queued call still pending minutes later on 6000.4.6f1. A
            CI editor driven over a socket is exactly what this type serves, so deferring its
            registration to a tick puts the whole feature behind an event that may never arrive.
        */
        [InitializeOnLoadMethod]
        private static void RegisterAfterDomainReload()
        {
            TryRegisterForRunInFlight();
            EditorApplication.delayCall -= RegisterWhenRunInFlight;
            EditorApplication.delayCall += RegisterWhenRunInFlight;
        }

        /// <summary>
        ///     Reports whether either mode's summary file is currently held by a run in flight.
        /// </summary>
        /// <returns><c>true</c> when a run holds a summary file.</returns>
        internal static bool IsAnyRunInFlight()
        {
            return TryFindRunInFlight(out TestMode _, out string _);
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

            if (TryFindRunInFlight(out TestMode runningMode, out string runningPath))
            {
                Debug.LogWarning(
                    $"{LogPrefix}Refusing to start {mode}: a {runningMode} run still holds {runningPath}. Delete that file to recover a run that was cancelled or lost."
                );
                return false;
            }

            if (!TestRunSummaryFile.TryBeginRun(summaryPath, mode, DateTime.UtcNow))
            {
                Debug.LogError($"{LogPrefix}Could not write the running marker to {summaryPath}.");
                return false;
            }

            if (!TryEnsureRegistered())
            {
                TestRunSummaryFile.TryDiscardRun(summaryPath);
                return false;
            }

            try
            {
                _api.Execute(new ExecutionSettings(new Filter { testMode = mode }));
            }
            catch (Exception exception)
            {
                TestRunSummaryFile.TryDiscardRun(summaryPath);
                Debug.LogError($"{LogPrefix}The Test Runner refused a {mode} run: {exception}");
                return false;
            }

            Debug.Log($"{LogPrefix}Started {mode} tests. Summary: {summaryPath}");
            return true;
        }

        /// <summary>
        ///     Called by the Test Runner when a run begins; the summary file was already claimed by
        ///     the menu item that started it.
        /// </summary>
        /// <param name="testsToRun">The test tree that will be executed.</param>
        void ICallbacks.RunStarted(ITestAdaptor testsToRun) { }

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
            if (!TryFindRunInFlight(out TestMode mode, out string summaryPath))
            {
                return;
            }

            TestRunResultNode root = BuildNode(result, 0);
            PopulateAssemblyBuildTimes(root);

            if (!TestRunSummaryFile.TryFinishRun(summaryPath, mode, finishedUtc, root))
            {
                Debug.LogError($"{LogPrefix}Could not write the {mode} summary to {summaryPath}.");
                return;
            }

            Debug.Log($"{LogPrefix}Wrote the {mode} summary to {summaryPath}.");
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

        /// <summary>
        ///     Registers Test Runner callbacks when, and only when, a run still holds a summary file.
        /// </summary>
        /// <returns><c>true</c> when a run is in flight and callbacks are registered for it.</returns>
        internal static bool TryRegisterForRunInFlight()
        {
            if (!IsAnyRunInFlight())
            {
                return false;
            }

            return TryEnsureRegistered();
        }

        private static bool TryFindRunInFlight(out TestMode mode, out string summaryPath)
        {
            if (TryFindRunInFlight(TestMode.EditMode, out summaryPath))
            {
                mode = TestMode.EditMode;
                return true;
            }

            if (TryFindRunInFlight(TestMode.PlayMode, out summaryPath))
            {
                mode = TestMode.PlayMode;
                return true;
            }

            mode = TestMode.EditMode;
            return false;
        }

        private static bool TryFindRunInFlight(TestMode mode, out string summaryPath)
        {
            return TestRunSummaryFile.TryGetSummaryPath(mode, out summaryPath)
                && TestRunSummaryFile.IsMarkedRunning(summaryPath);
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

            foreach (ITestResultAdaptor child in children)
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

        /*
            The DLL's write time is reported as a fact beside each assembly, never turned into a
            fresh/stale verdict. A source mtime moves whenever a formatter rewrites a file to
            byte-identical content, which Unity's content-addressed import correctly skips, so an
            mtime-based discriminator fires on every commit that touches C# and buries the real
            case. See docs/features/editor-tools/test-run-reporter.md.
        */
        private static void PopulateAssemblyBuildTimes(TestRunResultNode root)
        {
            if (root == null || root.children.Count == 0)
            {
                return;
            }

            Dictionary<string, string> outputPathsByAssemblyName = new(
                StringComparer.OrdinalIgnoreCase
            );
            try
            {
                UnityEditor.Compilation.Assembly[] assemblies = CompilationPipeline.GetAssemblies(
                    AssembliesType.Editor
                );
                for (int i = 0; i < assemblies.Length; i++)
                {
                    UnityEditor.Compilation.Assembly assembly = assemblies[i];
                    if (assembly == null || string.IsNullOrEmpty(assembly.name))
                    {
                        continue;
                    }

                    outputPathsByAssemblyName[assembly.name] = assembly.outputPath;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"{LogPrefix}Could not enumerate compiled assemblies: {exception}"
                );
                return;
            }

            for (int i = 0; i < root.children.Count; i++)
            {
                TestRunResultNode child = root.children[i];
                if (child == null)
                {
                    continue;
                }

                string assemblyName = TrimCompiledAssemblyExtension(child.fullName);
                if (
                    !outputPathsByAssemblyName.TryGetValue(assemblyName, out string outputPath)
                    || string.IsNullOrEmpty(outputPath)
                )
                {
                    continue;
                }

                try
                {
                    if (File.Exists(outputPath))
                    {
                        child.assemblyBuiltUtc = File.GetLastWriteTimeUtc(outputPath);
                    }
                }
                catch (Exception)
                {
                    child.assemblyBuiltUtc = null;
                }
            }
        }

        private static string TrimCompiledAssemblyExtension(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            if (!name.EndsWith(CompiledAssemblyExtension, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            return name.Substring(0, name.Length - CompiledAssemblyExtension.Length);
        }
    }
#endif
}
