// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>Writes validation reports to explicit paths without an editor window.</summary>
    public static class ValidationReportExportAPI
    {
        /// <summary>Writes a JSON report for a validation run.</summary>
        public static bool TryExportJson(
            string path,
            ValidationRun run,
            ValidationSuppressions suppressions,
            out string error
        )
        {
            return TryExport(path, run, suppressions, ValidationSeverity.Error, false, out error);
        }

        /// <summary>Writes a JUnit report for a validation run.</summary>
        public static bool TryExportJUnit(
            string path,
            ValidationRun run,
            ValidationSuppressions suppressions,
            ValidationSeverity threshold,
            out string error
        )
        {
            return TryExport(path, run, suppressions, threshold, true, out error);
        }

        private static bool TryExport(
            string path,
            ValidationRun run,
            ValidationSuppressions suppressions,
            ValidationSeverity threshold,
            bool junit,
            out string error
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "An output path is required.";
                return false;
            }
            if (run == null)
            {
                error = "A validation run is required.";
                return false;
            }

            try
            {
                string report = junit
                    ? ValidationWorkspaceReport.ToJUnit(run, suppressions, threshold)
                    : ValidationReport.ToJson(run, suppressions);
                if (!DurableFile.TryWriteAllText(path, report, out Exception writeError))
                {
                    error = writeError?.Message ?? "Could not write the validation report.";
                    return false;
                }
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }
    }
#endif
}
