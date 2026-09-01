// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEngine;

    /// <summary>Turns a Unity asset path into a path the filesystem can be asked about.</summary>
    /// <remarks>
    /// An asset path is project-relative, so reading one through <c>System.IO</c> works only while
    /// the process working directory is the project root. Unity sets it there and essentially
    /// everything relies on it, but a validator that reports clean when the read fails is exactly
    /// the vacuous pass these checks exist to prevent, so the assumption is removed rather than
    /// documented.
    /// </remarks>
    internal static class AuthoredAssetPaths
    {
        /// <summary>The project-relative folder Unity keeps authored assets in.</summary>
        internal const string AssetsFolder = "Assets";

        /// <summary>Resolves <paramref name="assetPath"/> against the project root.</summary>
        /// <param name="assetPath">A project-relative asset path, or an absolute path.</param>
        /// <returns>An absolute path, or the input when it is already absolute or unresolvable.</returns>
        internal static string ToFileSystemPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || Path.IsPathRooted(assetPath))
            {
                return assetPath;
            }

            string projectRoot = ProjectRoot();
            return string.IsNullOrEmpty(projectRoot)
                ? assetPath
                : Path.Combine(projectRoot, assetPath);
        }

        /// <summary>Turns <paramref name="filePath"/> back into a project-relative asset path.</summary>
        /// <param name="filePath">An absolute path inside the project.</param>
        /// <returns>The asset path, or the input when it is not under the project root.</returns>
        /// <remarks>
        /// A finding names the path a reader can click in the Project window, so the filesystem
        /// path is used for the read and the asset path for the report.
        /// </remarks>
        internal static string ToAssetPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return filePath;
            }

            string projectRoot = ProjectRoot();
            if (string.IsNullOrEmpty(projectRoot))
            {
                return filePath;
            }

            string normalizedRoot = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            string normalized = filePath.Replace('\\', '/');
            return normalized.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(normalizedRoot.Length)
                : normalized;
        }

        /// <summary>Every authored asset under the project's <c>Assets</c> folder.</summary>
        /// <returns>The asset paths, project-relative so a reader can click them.</returns>
        internal static IReadOnlyList<string> AuthoredAssetsUnderProjectRoot()
        {
            IReadOnlyList<string> found = AuthoredAssetYaml.EnumerateAuthoredAssets(
                ToFileSystemPath(AssetsFolder)
            );

            List<string> assetPaths = new(found.Count);
            foreach (string filePath in found)
            {
                assetPaths.Add(ToAssetPath(filePath));
            }

            return assetPaths;
        }

        /// <summary>The directory holding the project's <c>Assets</c> folder.</summary>
        /// <returns>The project root, or <c>null</c> when it cannot be determined.</returns>
        internal static string ProjectRoot()
        {
            string dataPath = Application.dataPath;
            if (string.IsNullOrEmpty(dataPath))
            {
                return null;
            }

            DirectoryInfo parent = Directory.GetParent(dataPath);
            return parent == null ? null : parent.FullName;
        }
    }
#endif
}
