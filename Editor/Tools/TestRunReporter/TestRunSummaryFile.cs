// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using UnityEditor.TestTools.TestRunner.Api;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    ///     Owns the lifecycle of the summary file a test run writes: where it lives, whether a run
    ///     already holds it, and the two writes that open and close it.
    /// </summary>
    internal static class TestRunSummaryFile
    {
        /// <summary>
        ///     The project-root folder the summary files are written into.
        /// </summary>
        internal const string SummaryDirectoryName = "Temp";

        /// <summary>
        ///     The file name EditMode runs write to.
        /// </summary>
        internal const string EditModeFileName = "unity-helpers-test-run-editmode.txt";

        /// <summary>
        ///     The file name PlayMode runs write to.
        /// </summary>
        internal const string PlayModeFileName = "unity-helpers-test-run-playmode.txt";

        private const string ClaimSuffix = ".claim";

        internal static Action AfterBeginClaimForTests { get; set; }

        /// <summary>
        ///     Resolves the summary path for a single test mode.
        /// </summary>
        /// <param name="mode">Exactly one of <see cref="TestMode.EditMode"/> or <see cref="TestMode.PlayMode"/>.</param>
        /// <param name="summaryPath">The absolute path, empty when the mode is not a single known one.</param>
        /// <returns><c>true</c> when a path was resolved.</returns>
        internal static bool TryGetSummaryPath(TestMode mode, out string summaryPath)
        {
            string fileName;
            switch (mode)
            {
                case TestMode.EditMode:
                {
                    fileName = EditModeFileName;
                    break;
                }
                case TestMode.PlayMode:
                {
                    fileName = PlayModeFileName;
                    break;
                }
                default:
                {
                    summaryPath = string.Empty;
                    return false;
                }
            }

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                summaryPath = Path.Combine(projectRoot, SummaryDirectoryName, fileName);
                return true;
            }
            catch (Exception)
            {
                summaryPath = string.Empty;
                return false;
            }
        }

        /// <summary>
        ///     Reports whether the file at the given path is currently claimed by a run in flight.
        /// </summary>
        /// <param name="summaryPath">The summary path to inspect.</param>
        /// <returns><c>true</c> when the file exists and its first line carries the running marker.</returns>
        internal static bool IsMarkedRunning(string summaryPath)
        {
            if (string.IsNullOrEmpty(summaryPath))
            {
                return false;
            }

            try
            {
                foreach (string line in File.ReadLines(summaryPath))
                {
                    return TestRunSummaryFormatter.IsRunningLine(line);
                }

                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Claims the summary file for a new run by writing the running marker, refusing when a
        ///     run already holds it.
        /// </summary>
        /// <param name="summaryPath">The summary path to claim.</param>
        /// <param name="mode">The test mode the run covers.</param>
        /// <param name="startedUtc">When the run is starting.</param>
        /// <param name="owner">The unique token required to finish or discard this run.</param>
        /// <returns><c>true</c> when the marker was written and ownership was returned.</returns>
        internal static bool TryBeginRun(
            string summaryPath,
            TestMode mode,
            DateTime startedUtc,
            out string owner,
            string competingSummaryPath = null
        )
        {
            if (string.IsNullOrEmpty(summaryPath))
            {
                owner = string.Empty;
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(summaryPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using FileStream claim = OpenBeginClaim(summaryPath, competingSummaryPath);
                AfterBeginClaimForTests?.Invoke();
                if (
                    IsMarkedRunning(summaryPath)
                    || (
                        !string.IsNullOrEmpty(competingSummaryPath)
                        && IsMarkedRunning(competingSummaryPath)
                    )
                )
                {
                    owner = string.Empty;
                    return false;
                }

                string candidateOwner = Guid.NewGuid().ToString("N");
                string marker = TestRunSummaryFormatter.FormatRunningMarker(
                    mode,
                    startedUtc,
                    candidateOwner
                );
                if (!DurableFile.TryWriteAllText(summaryPath, marker, out _))
                {
                    owner = string.Empty;
                    return false;
                }

                owner = candidateOwner;
                return true;
            }
            catch (Exception)
            {
                owner = string.Empty;
                return false;
            }
        }

        /// <summary>
        ///     Replaces the running marker with the completed summary for a result tree.
        /// </summary>
        /// <param name="summaryPath">The summary path to finish.</param>
        /// <param name="owner">The ownership token returned when this run began.</param>
        /// <param name="mode">The test mode the run covered.</param>
        /// <param name="finishedUtc">When the run finished.</param>
        /// <param name="root">The root of the result tree, whose children are the assemblies.</param>
        /// <returns><c>true</c> when the summary was written.</returns>
        internal static bool TryFinishRun(
            string summaryPath,
            string owner,
            TestMode mode,
            DateTime finishedUtc,
            TestRunResultNode root
        )
        {
            if (string.IsNullOrEmpty(summaryPath) || string.IsNullOrEmpty(owner))
            {
                return false;
            }

            try
            {
                string directory = Path.GetDirectoryName(summaryPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using FileStream claim = OpenClaim(summaryPath);
                if (
                    !TryReadOwner(summaryPath, out string currentOwner)
                    || !string.Equals(currentOwner, owner, StringComparison.Ordinal)
                )
                {
                    return false;
                }

                if (!TryReadStartedUtc(summaryPath, out DateTime startedUtc))
                {
                    startedUtc = finishedUtc;
                }

                string summary = TestRunSummaryFormatter.FormatSummary(
                    mode,
                    startedUtc,
                    finishedUtc,
                    root
                );
                return DurableFile.TryWriteAllText(summaryPath, summary, out _);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        ///     Reads back when the run holding the summary file started.
        /// </summary>
        /// <param name="summaryPath">The summary path to inspect.</param>
        /// <param name="startedUtc">The parsed timestamp, in UTC.</param>
        /// <returns><c>true</c> when the file's first line carried a parseable timestamp.</returns>
        internal static bool TryReadStartedUtc(string summaryPath, out DateTime startedUtc)
        {
            if (string.IsNullOrEmpty(summaryPath))
            {
                startedUtc = default;
                return false;
            }

            try
            {
                foreach (string line in File.ReadLines(summaryPath))
                {
                    return TestRunSummaryFormatter.TryParseStartedUtc(line, out startedUtc);
                }

                startedUtc = default;
                return false;
            }
            catch (Exception)
            {
                startedUtc = default;
                return false;
            }
        }

        /// <summary>
        ///     Reads the owner of an in-flight summary marker.
        /// </summary>
        /// <param name="summaryPath">The summary path to inspect.</param>
        /// <param name="owner">The ownership token, empty when no running marker owns the file.</param>
        /// <returns><c>true</c> when the first line is a running marker with an owner.</returns>
        internal static bool TryReadOwner(string summaryPath, out string owner)
        {
            if (string.IsNullOrEmpty(summaryPath))
            {
                owner = string.Empty;
                return false;
            }

            try
            {
                foreach (string line in File.ReadLines(summaryPath))
                {
                    string parsedOwner = string.Empty;
                    bool success =
                        TestRunSummaryFormatter.IsRunningLine(line)
                        && TestRunSummaryFormatter.TryParseOwner(line, out parsedOwner);
                    owner = success ? parsedOwner : string.Empty;
                    return success;
                }
            }
            catch (Exception) { }
            owner = string.Empty;
            return false;
        }

        /// <summary>
        ///     Releases the summary file without writing a summary, for a run that never started.
        /// </summary>
        /// <param name="summaryPath">The summary path to release.</param>
        /// <param name="owner">The ownership token returned when this run began.</param>
        /// <returns><c>true</c> when the file is gone afterwards.</returns>
        internal static bool TryDiscardRun(string summaryPath, string owner)
        {
            if (string.IsNullOrEmpty(summaryPath) || string.IsNullOrEmpty(owner))
            {
                return false;
            }

            try
            {
                using FileStream claim = OpenClaim(summaryPath);
                if (
                    !TryReadOwner(summaryPath, out string currentOwner)
                    || !string.Equals(currentOwner, owner, StringComparison.Ordinal)
                )
                {
                    return false;
                }

                return DurableFile.TryDelete(summaryPath);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static FileStream OpenClaim(string summaryPath)
        {
            return new FileStream(
                summaryPath + ClaimSuffix,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.DeleteOnClose
            );
        }

        private static FileStream OpenBeginClaim(string summaryPath, string competingSummaryPath)
        {
            if (string.IsNullOrEmpty(competingSummaryPath))
            {
                return OpenClaim(summaryPath);
            }

            string directory = Path.GetDirectoryName(summaryPath) ?? string.Empty;
            string claimPath = Path.Combine(directory, "unity-helpers-test-run.claim");
            return new FileStream(
                claimPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.DeleteOnClose
            );
        }
    }
#endif
}
