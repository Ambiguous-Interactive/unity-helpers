// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System.IO;
    using UnityEditor.Build;
    using UnityEditor.Build.Reporting;

    internal sealed class ValidationBuildGate : IPreprocessBuildWithReport
    {
        internal const string SuppressionsFile = "ValidationSuppressions.txt";

        /// <inheritdoc />
        public int callbackOrder => -1000;

        internal static string[] CreateCommandLine(
            ValidationSeverity severity,
            bool hasSuppressions
        )
        {
            return hasSuppressions
                ? new[]
                {
                    ValidationBatch.FailOnArgument,
                    severity.ToString(),
                    ValidationBatch.SuppressionsArgument,
                    SuppressionsFile,
                }
                : new[] { ValidationBatch.FailOnArgument, severity.ToString() };
        }

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            if (!ValidationPreferences.Enabled)
                return;
            ValidationWorkspaceSettings.Profile profile = ValidationWorkspaceSettings
                .instance
                .ActiveProfile;
            if (!profile.gateBuild)
                return;
            ValidationBatch.Result result = ValidationBatch.Run(
                CreateCommandLine(profile.failOn, File.Exists(SuppressionsFile))
            );
            if (result.Failed)
                throw new BuildFailedException(
                    "Sentinel validation (" + profile.name + "): " + result.Summary
                );
        }
    }
#endif
}
