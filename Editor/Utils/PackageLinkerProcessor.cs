// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
    using System;
    using System.IO;
    using UnityEditor.Build;
    using UnityEditor.Build.Reporting;
    using UnityEditor.PackageManager;
    using UnityEditor.UnityLinker;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Supplies the package's serialization preservation rules to the Unity linker.
    /// </summary>
    public sealed class PackageLinkerProcessor : IUnityLinkerProcessor
    {
        internal const string LinkXmlFileName = "link.xml";

        /// <inheritdoc />
        public int callbackOrder => 0;

        internal static string RequireLinkXmlPath(string packageRoot)
        {
            if (!string.IsNullOrWhiteSpace(packageRoot))
            {
                try
                {
                    if (Path.IsPathFullyQualified(packageRoot))
                    {
                        string path = PathHelper.Sanitize(
                            Path.Combine(packageRoot, LinkXmlFileName)
                        );
                        if (File.Exists(path))
                        {
                            return path;
                        }
                    }
                }
                catch (Exception exception)
                    when (exception is ArgumentException
                        || exception is NotSupportedException
                        || exception is PathTooLongException
                    ) { }
            }

            throw new BuildFailedException(
                $"[{nameof(PackageLinkerProcessor)}] Cannot locate the package linker descriptor in '{packageRoot}'. Reinstall Unity Helpers before building a player."
            );
        }

        /// <inheritdoc />
        /// <exception cref="BuildFailedException">
        /// The installed UPM package has no existing linker descriptor at its resolved location.
        /// </exception>
        public string GenerateAdditionalLinkXmlFile(
            BuildReport report,
            UnityLinkerBuildPipelineData data
        )
        {
            PackageInfo package = PackageInfo.FindForAssembly(
                typeof(PackageLinkerProcessor).Assembly
            );
            return package == null ? null : RequireLinkXmlPath(package.resolvedPath);
        }
    }
}
