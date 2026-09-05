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
        /// <inheritdoc />
        public int callbackOrder => -1000;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            ValidationWorkspaceSettings.Profile profile = ValidationWorkspaceSettings
                .instance
                .ActiveProfile;
            if (!profile.gateBuild)
                return;
            ValidationBatch.Result result = ValidationBatch.Run(
                new[]
                {
                    ValidationBatch.FailOnArgument,
                    profile.failOn.ToString(),
                    ValidationBatch.SuppressionsArgument,
                    File.Exists("ValidationSuppressions.txt")
                        ? "ValidationSuppressions.txt"
                        : string.Empty,
                }
            );
            if (result.Failed)
                throw new BuildFailedException(
                    "Sentinel validation (" + profile.name + "): " + result.Summary
                );
        }
    }
#endif
}
