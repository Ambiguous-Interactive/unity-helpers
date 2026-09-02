// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using UnityEditor;
    using UnityEditor.TestTools.TestRunner.Api;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Settings;

    /// <summary>
    ///     Hooks into the Unity Test Runner API to capture failed tests and export
    ///     their details to a timestamped text file in the project root.
    /// </summary>
    /// <remarks>
    ///     Registration is gated by <see cref="UnityHelpersSettings"/> via the
    ///     <see cref="IsEnabled"/> check. When disabled, no callbacks are registered
    ///     and no resources are allocated. Call <see cref="Reinitialize"/> after
    ///     changing settings to apply the new state without a domain reload.
    /// </remarks>
    [Serializable]
    [InitializeOnLoad]
    internal sealed class FailedTestsExporter : ScriptableObject, ICallbacks
    {
        private static FailedTestsExporter _instance;
        private static TestRunnerApi _api;

        [SerializeField]
        private List<FailedTestInfo> _failures = new();

        static FailedTestsExporter()
        {
            Initialize();
        }

        /// <summary>
        ///     Registers the Test Runner callbacks now, and retries only while the settings that
        ///     gate them have not loaded.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///     Registration used to happen ONLY on <see cref="EditorApplication.delayCall"/>. An
        ///     editor nothing is interacting with -- a background window, a CI editor driven over a
        ///     socket -- does not necessarily pump that tick at all: measured on 6000.4.6f1, a
        ///     queued call was still pending minutes after the reload that queued it. An exporter of
        ///     FAILED TEST RESULTS is used in precisely such an editor, and a PlayMode run reloads
        ///     the domain, so this re-registration is on the critical path for exactly the runs
        ///     whose failures most want exporting
        ///     (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/684">#684</see>).
        ///     </para>
        ///     <para>
        ///     It is not simply synchronous either, because the deferral's stated reason is real:
        ///     <see cref="UnityHelpersSettings"/> may not be loadable this early, and a read that
        ///     fails is indistinguishable from the feature being switched off. So the attempt
        ///     happens immediately and only an UNAVAILABLE settings object re-arms the retries --
        ///     <c>AssemblyReloadEvents.afterAssemblyReload</c> first, because it is a callback Unity
        ///     invokes rather than a tick it might not reach.
        ///     </para>
        /// </remarks>
        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            CleanupPreviousInstance();
            RegisterCallbacks();
        }

        /// <summary>
        ///     Re-initializes the exporter, allowing settings changes to take effect
        ///     without requiring a full domain reload.
        /// </summary>
        internal static void Reinitialize()
        {
            Initialize();
        }

        /// <summary>
        ///     Checks whether the failed tests exporter is enabled in the project settings.
        /// </summary>
        /// <returns>
        ///     <c>true</c> if the exporter is enabled; <c>false</c> if disabled or if
        ///     settings are unavailable.
        /// </returns>
        public static bool IsEnabled()
        {
            return TryIsEnabled(out bool enabled) && enabled;
        }

        /// <summary>
        ///     Reads the exporter's enabled setting, distinguishing "switched off" from
        ///     "settings could not be read yet".
        /// </summary>
        /// <param name="enabled">Receives the setting; <c>false</c> when it could not be read.</param>
        /// <returns><c>false</c> when the settings object was not available.</returns>
        /// <remarks>
        ///     <see cref="IsEnabled"/> collapses both into <c>false</c>, which is the right answer
        ///     for a caller asking whether to act and the wrong one for a caller deciding whether to
        ///     ask again. Registration is the second kind: retrying a disabled feature forever is as
        ///     wrong as giving up on one whose settings had simply not loaded.
        /// </remarks>
        internal static bool TryIsEnabled(out bool enabled)
        {
            try
            {
                enabled = UnityHelpersSettings.GetFailedTestsExporterEnabled();
                return true;
            }
            catch (Exception)
            {
                enabled = false;
                return false;
            }
        }

        private static void ArmRetries()
        {
            AssemblyReloadEvents.afterAssemblyReload -= RegisterCallbacks;
            AssemblyReloadEvents.afterAssemblyReload += RegisterCallbacks;
            EditorApplication.delayCall -= RegisterCallbacks;
            EditorApplication.delayCall += RegisterCallbacks;
        }

        private static void DisarmRetries()
        {
            AssemblyReloadEvents.afterAssemblyReload -= RegisterCallbacks;
            EditorApplication.delayCall -= RegisterCallbacks;
        }

        private static void CleanupPreviousInstance()
        {
            DisarmRetries();

            if (_api != null)
            {
                DestroyImmediate(_api);
                _api = null;
            }

            if (_instance != null)
            {
                DestroyImmediate(_instance);
                _instance = null;
            }
        }

        private static void RegisterCallbacks()
        {
            if (!TryIsEnabled(out bool enabled))
            {
                ArmRetries();
                return;
            }

            DisarmRetries();
            if (!enabled)
            {
                return;
            }

            if (_instance != null)
            {
                return;
            }

            try
            {
                _instance = CreateInstance<FailedTestsExporter>();
                _instance.hideFlags = HideFlags.HideAndDontSave;

                _api = CreateInstance<TestRunnerApi>();
                _api.hideFlags = HideFlags.HideAndDontSave;
                _api.RegisterCallbacks(_instance);
            }
            catch (Exception exception)
            {
                /*
                    Registration now runs during InitializeOnLoad, where an exception escaping would
                    take the rest of the editor's load handlers with it. Undo the half-built state
                    and ask again on the retries instead.
                */
                CleanupPreviousInstance();
                ArmRetries();
                Debug.LogWarning(
                    "[Unity Helpers] The failed tests exporter could not register with the Test "
                        + $"Runner yet: {exception.Message}. It will try again after the next "
                        + "assembly reload."
                );
            }
        }

        /// <summary>
        ///     Called by the Test Runner when a test run begins. Clears any previously
        ///     recorded failures.
        /// </summary>
        /// <param name="testsToRun">The test tree that will be executed.</param>
        void ICallbacks.RunStarted(ITestAdaptor testsToRun)
        {
            _failures.Clear();
        }

        /// <summary>
        ///     Called by the Test Runner when a test run finishes. Writes any recorded
        ///     failures to a file.
        /// </summary>
        /// <param name="result">The aggregate result of the test run.</param>
        void ICallbacks.RunFinished(ITestResultAdaptor result)
        {
            if (_failures.Count == 0)
            {
                this.Log($"Test run completed with no failures.");
                return;
            }

            string outputPath = WriteFailuresToFile();
            if (outputPath == null)
            {
                return;
            }

            this.Log($"Wrote {_failures.Count} failure(s) to: {outputPath}");
        }

        /// <summary>
        ///     Called by the Test Runner when an individual test begins. No action is taken.
        /// </summary>
        /// <param name="test">The test that is starting.</param>
        void ICallbacks.TestStarted(ITestAdaptor test) { }

        /// <summary>
        ///     Called by the Test Runner when an individual test finishes. Records the
        ///     test details if it failed.
        /// </summary>
        /// <param name="result">The result of the completed test.</param>
        void ICallbacks.TestFinished(ITestResultAdaptor result)
        {
            if (result.TestStatus != TestStatus.Failed)
            {
                return;
            }

            if (result.HasChildren)
            {
                return;
            }

            _failures.Add(
                new FailedTestInfo(
                    result.FullName,
                    result.Message ?? string.Empty,
                    result.StackTrace ?? string.Empty
                )
            );
        }

        /// <summary>
        ///     Menu item that exports the currently recorded failed tests to a file.
        /// </summary>
        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Export Failed Tests", priority = 100)]
        private static void ExportFailedTestsMenuItem()
        {
            if (!HasValidFailures())
            {
                Debug.Log("[FailedTestsExporter] No failed tests to export.");
                return;
            }

            string outputPath = _instance.WriteFailuresToFile();
            if (outputPath == null)
            {
                return;
            }

            Debug.Log(
                $"[FailedTestsExporter] Exported {_instance._failures.Count} failure(s) to: {outputPath}"
            );
        }

        /// <summary>
        ///     Validation function for the Export Failed Tests menu item.
        /// </summary>
        /// <returns><c>true</c> if there are failed tests available to export.</returns>
        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Export Failed Tests", validate = true)]
        private static bool ExportFailedTestsMenuItemValidate()
        {
            return HasValidFailures();
        }

        /// <summary>
        ///     Menu item that clears all currently recorded failed tests.
        /// </summary>
        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Clear Failed Tests", priority = 101)]
        private static void ClearFailedTestsMenuItem()
        {
            if (_instance != null)
            {
                _instance._failures.Clear();
            }

            Debug.Log("[FailedTestsExporter] Cleared failed tests.");
        }

        /// <summary>
        ///     Validation function for the Clear Failed Tests menu item.
        /// </summary>
        /// <returns><c>true</c> if there are failed tests available to clear.</returns>
        [MenuItem("Tools/Wallstop Studios/Unity Helpers/Clear Failed Tests", validate = true)]
        private static bool ClearFailedTestsMenuItemValidate()
        {
            return HasValidFailures();
        }

        private static bool HasValidFailures()
        {
            return _instance != null && 0 < _instance._failures.Count;
        }

        private string WriteFailuresToFile()
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string outputDirectory = UnityHelpersSettings.GetFailedTestsOutputDirectory();
                string targetDirectory = string.IsNullOrEmpty(outputDirectory)
                    ? projectRoot
                    : Path.GetFullPath(Path.Combine(projectRoot, outputDirectory));

                // Defense-in-depth: directory may have been removed since validation
                if (!Directory.Exists(targetDirectory))
                {
                    targetDirectory = projectRoot;
                }

                string timestamp = DateTime.Now.ToString(
                    "yyyy-MM-dd-HHmmss",
                    CultureInfo.InvariantCulture
                );
                string fileName = $"failed-tests-{timestamp}.txt";
                string outputPath = Path.Combine(targetDirectory, fileName);

                StringBuilder builder = new(_failures.Count * 512);

                for (int i = 0; i < _failures.Count; i++)
                {
                    FailedTestInfo failure = _failures[i];

                    builder.Append("TEST_FAILURE_");
                    builder.AppendLine((i + 1).ToString());

                    builder.Append("Name: ");
                    builder.AppendLine(failure.name);

                    builder.Append("Message: ");
                    builder.AppendLine(
                        string.IsNullOrEmpty(failure.message) ? "(no message)" : failure.message
                    );

                    builder.AppendLine("Stack Trace:");
                    builder.AppendLine(
                        string.IsNullOrEmpty(failure.stackTrace)
                            ? "(no stack trace)"
                            : failure.stackTrace
                    );

                    if (i < _failures.Count - 1)
                    {
                        builder.AppendLine();
                        builder.AppendLine("---");
                        builder.AppendLine();
                    }
                }

                File.WriteAllText(outputPath, builder.ToString());
                return outputPath;
            }
            catch (Exception e)
            {
                this.LogError($"Failed to write file.", e);
                return null;
            }
        }

        /// <summary>
        ///     Gets the list of recorded test failures from the most recent test run.
        /// </summary>
        public IReadOnlyList<FailedTestInfo> Failures => _failures;

        /// <summary>
        ///     Gets the current singleton instance of the exporter, or <c>null</c> if
        ///     the exporter is not initialized or is disabled.
        /// </summary>
        public static FailedTestsExporter Instance => _instance;

        /// <summary>
        ///     Contains the details of a single failed test captured by the
        ///     <see cref="FailedTestsExporter"/>.
        /// </summary>
        [Serializable]
        internal readonly struct FailedTestInfo
        {
            /// <summary>
            ///     The fully qualified name of the failed test.
            /// </summary>
            public readonly string name;

            /// <summary>
            ///     The failure message reported by the test runner.
            /// </summary>
            public readonly string message;

            /// <summary>
            ///     The stack trace at the point of failure.
            /// </summary>
            public readonly string stackTrace;

            /// <summary>
            ///     Creates a new <see cref="FailedTestInfo"/> with the specified values.
            /// </summary>
            /// <param name="name">The fully qualified name of the failed test.</param>
            /// <param name="message">The failure message reported by the test runner.</param>
            /// <param name="stackTrace">The stack trace at the point of failure.</param>
            internal FailedTestInfo(string name, string message, string stackTrace)
            {
                this.name = name;
                this.message = message;
                this.stackTrace = stackTrace;
            }
        }
    }
#endif
}
