// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;
    using System.Runtime.CompilerServices;
    using UnityEngine;
#if UNITY_EDITOR
    using UnityEditor;
#endif

    /// <summary>
    /// Helpers for creating and resolving directories in Unity projects.
    /// </summary>
    /// <remarks>
    /// Editor paths are expected to be under the <c>Assets/</c> folder. Provides conversions between absolute and Unity-relative paths.
    /// </remarks>
    public static class DirectoryHelper
    {
        /// <summary>
        /// Ensures a directory exists in the project. In the editor, the directory must be inside <c>Assets/</c> and is created via <c>AssetDatabase</c>.
        /// </summary>
        /// <param name="relativeDirectoryPath">Unity relative path (e.g., <c>Assets/MyFolder/Sub</c>).</param>
        /// <remarks>
        /// Editor paths are canonicalized before writing; parent segments remaining within Assets are supported.
        /// Containment is lexical and does not resolve filesystem symbolic links.
        /// </remarks>
        /// <exception cref="ArgumentException">Thrown for invalid paths or paths resolving outside <c>Assets/</c> in the editor.</exception>
        public static void EnsureDirectoryExists(string relativeDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(relativeDirectoryPath))
            {
                return;
            }

            relativeDirectoryPath = relativeDirectoryPath.SanitizePath();

#if UNITY_EDITOR

            if (
                !TryResolveAssetsPath(
                    relativeDirectoryPath,
                    out string canonicalAssetPath,
                    out string absoluteDirectory
                )
            )
            {
                throw new ArgumentException(
                    $"Cannot create directory '{relativeDirectoryPath}' outside the Assets folder: "
                        + "AssetDatabase only manages paths under 'Assets/'.",
                    nameof(relativeDirectoryPath)
                );
            }

            relativeDirectoryPath = canonicalAssetPath;
            if (string.Equals(relativeDirectoryPath, "Assets", StringComparison.Ordinal))
            {
                return;
            }

            // Create the disk folder first to prevent Unity from showing a modal move-failure dialog.
            try
            {
                if (!Directory.Exists(absoluteDirectory))
                {
                    Directory.CreateDirectory(absoluteDirectory);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"DirectoryHelper: Failed to create directory on disk '{absoluteDirectory}': {e}"
                );
            }

            // Disk folders may be absent from AssetDatabase; creating again would produce a suffixed duplicate.
            if (AssetDatabase.IsValidFolder(relativeDirectoryPath))
            {
                return;
            }

            // Import an existing disk folder instead of creating a duplicate asset folder.
            bool directoryExistsOnDisk =
                !string.IsNullOrWhiteSpace(absoluteDirectory)
                && Directory.Exists(absoluteDirectory);
            if (directoryExistsOnDisk)
            {
                string parentForRefresh = Path.GetDirectoryName(relativeDirectoryPath)
                    .SanitizePath();
                if (!string.IsNullOrWhiteSpace(parentForRefresh))
                {
                    AssetDatabase.ImportAsset(
                        parentForRefresh,
                        ImportAssetOptions.ForceSynchronousImport
                            | ImportAssetOptions.ImportRecursive
                    );
                }

                if (AssetDatabase.IsValidFolder(relativeDirectoryPath))
                {
                    return;
                }
            }

            string parentPath = Path.GetDirectoryName(relativeDirectoryPath).SanitizePath();
            if (
                string.IsNullOrWhiteSpace(parentPath)
                || parentPath.Equals("Assets", StringComparison.OrdinalIgnoreCase)
            )
            {
                string folderNameToCreate = Path.GetFileName(relativeDirectoryPath);
                if (
                    !string.IsNullOrWhiteSpace(folderNameToCreate)
                    && !AssetDatabase.IsValidFolder(relativeDirectoryPath)
                    && !directoryExistsOnDisk
                )
                {
                    AssetDatabase.CreateFolder("Assets", folderNameToCreate);
                }
                return;
            }

            EnsureDirectoryExists(parentPath);
            string currentFolderName = Path.GetFileName(relativeDirectoryPath);
            if (
                !string.IsNullOrWhiteSpace(currentFolderName)
                && !AssetDatabase.IsValidFolder(relativeDirectoryPath)
                && !directoryExistsOnDisk
            )
            {
                AssetDatabase.CreateFolder(parentPath, currentFolderName);
                Debug.Log($"Created folder: {relativeDirectoryPath}");
            }
#else
            Directory.CreateDirectory(relativeDirectoryPath);
#endif
        }

        /// <summary>
        /// Gets the directory of the calling source file (useful for locating package-relative content).
        /// </summary>
        public static string GetCallerScriptDirectory([CallerFilePath] string sourceFilePath = "")
        {
            return string.IsNullOrWhiteSpace(sourceFilePath)
                ? string.Empty
                : Path.GetDirectoryName(sourceFilePath);
        }

        /// <summary>
        /// Walks up the directory tree until a folder containing <c>package.json</c> is found.
        /// </summary>
        public static string FindPackageRootPath(string startDirectory)
        {
            return FindRootPath(
                startDirectory,
                path => File.Exists(Path.Combine(path, "package.json"))
            );
        }

        /// <summary>
        /// Walks up the directory tree until <paramref name="terminalCondition"/> returns true.
        /// </summary>
        public static string FindRootPath(
            string startDirectory,
            Func<string, bool> terminalCondition
        )
        {
            string currentPath = startDirectory;
            while (!string.IsNullOrWhiteSpace(currentPath))
            {
                try
                {
                    if (terminalCondition(currentPath))
                    {
                        DirectoryInfo directoryInfo = new(currentPath);
                        if (!directoryInfo.Exists)
                        {
                            return currentPath;
                        }

                        return directoryInfo.FullName;
                    }
                }
                catch
                {
                    return currentPath;
                }

                try
                {
                    string parentPath = Path.GetDirectoryName(currentPath);
                    if (string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    currentPath = parentPath;
                }
                catch
                {
                    return currentPath;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Resolves an absolute path to a directory relative to the package root and returns a Unity-relative path.
        /// </summary>
        public static string FindAbsolutePathToDirectory(string directory)
        {
            return ResolvePackageAssetPath(
                directory != null && directory.Length == 0 ? "/" : directory
            );
        }

        /// <summary>
        /// Converts an absolute OS path to a Unity-relative path (e.g., <c>Assets/...</c>), or empty string if outside the project.
        /// </summary>
        public static string AbsoluteToUnityRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            absolutePath = absolutePath.SanitizePath();
            string projectRoot = Application.dataPath.SanitizePath();

            projectRoot = Path.GetDirectoryName(projectRoot)?.SanitizePath();
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            string projectPrefix = projectRoot.TrimEnd('/') + "/";
            if (absolutePath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                int startIndex = projectPrefix.Length;
                return startIndex < absolutePath.Length ? absolutePath[startIndex..] : string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Converts an absolute OS path to a Unity-loadable path that works for Assets, Packages, and Library/PackageCache.
        /// This method handles:
        /// <list type="bullet">
        /// <item><description>Assets/ - Returns the relative path as-is</description></item>
        /// <item><description>Packages/ - Returns the path prefixed with "Packages/"</description></item>
        /// <item><description>Library/PackageCache/ - Converts to "Packages/{packageId}/" format</description></item>
        /// </list>
        /// </summary>
        /// <param name="absolutePath">The absolute path to convert.</param>
        /// <param name="packageId">The package identifier used for Library/PackageCache resolution; blank identifiers cannot resolve cached paths.</param>
        /// <returns>A Unity-loadable path, or empty string if the path cannot be resolved.</returns>
        public static string AbsoluteToUnityLoadablePath(string absolutePath, string packageId)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            absolutePath = absolutePath.SanitizePath();

            string relativePath = AbsoluteToUnityRelativePath(absolutePath);
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                return relativePath;
            }

            const string packageCacheMarker = "Library/PackageCache/";
            int packageCacheIndex = absolutePath.IndexOf(
                packageCacheMarker,
                StringComparison.OrdinalIgnoreCase
            );
            if (0 <= packageCacheIndex)
            {
                string afterCache = absolutePath[(packageCacheIndex + packageCacheMarker.Length)..];

                int firstSlash = afterCache.IndexOf('/');
                if (0 < firstSlash)
                {
                    string pathInsidePackage = afterCache[(firstSlash + 1)..];
                    if (!string.IsNullOrWhiteSpace(packageId))
                    {
                        return $"Packages/{packageId}/{pathInsidePackage}";
                    }
                }

                return string.Empty;
            }

            const string packagesMarker = "/Packages/";
            int packagesIndex = absolutePath.IndexOf(
                packagesMarker,
                StringComparison.OrdinalIgnoreCase
            );
            if (0 <= packagesIndex)
            {
                return "Packages/" + absolutePath[(packagesIndex + packagesMarker.Length)..];
            }

            return string.Empty;
        }

        /// <summary>
        /// Resolves a path relative to the package root (identified by package.json) to a Unity-loadable path.
        /// Supports Assets, embedded and cached packages, and external package checkouts.
        /// </summary>
        /// <param name="relativePath">The path relative to the package root (e.g., "Editor/Styles/MyStyle.uss").</param>
        /// <param name="sourceFilePath">Leave as default to use the calling script's path. This parameter is automatically filled by the compiler.</param>
        /// <returns>A Unity-loadable path, or empty when the path cannot be resolved or an external package has a blank ID.</returns>
        public static string ResolvePackageAssetPath(
            string relativePath,
            [CallerFilePath] string sourceFilePath = ""
        )
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return string.Empty;
            }

            string scriptDirectory = string.IsNullOrWhiteSpace(sourceFilePath)
                ? string.Empty
                : Path.GetDirectoryName(sourceFilePath);

            if (string.IsNullOrWhiteSpace(scriptDirectory))
            {
                return string.Empty;
            }

            string packageRootAbsolute = FindPackageRootPath(scriptDirectory);
            if (string.IsNullOrWhiteSpace(packageRootAbsolute))
            {
                return string.Empty;
            }

            string packageId = ReadPackageIdFromRoot(packageRootAbsolute);

            string normalizedPath = relativePath.SanitizePath().Trim('/');
            string targetPathAbsolute = Path.Combine(
                    packageRootAbsolute,
                    normalizedPath.Replace('/', Path.DirectorySeparatorChar)
                )
                .SanitizePath();

            string projectPath = AbsoluteToUnityRelativePath(targetPathAbsolute);
            if (
                projectPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || projectPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
            )
            {
                return projectPath;
            }

            if (string.IsNullOrWhiteSpace(packageId))
            {
                return string.Empty;
            }

            return normalizedPath.Length == 0
                ? $"Packages/{packageId}"
                : $"Packages/{packageId}/{normalizedPath}";
        }

        /// <summary>
        /// Reads the package ID ("name" field) from a package.json file in the specified directory.
        /// </summary>
        /// <param name="packageRootPath">The absolute path to the package root containing package.json.</param>
        /// <returns>The package ID, or empty string if not found or could not be read.</returns>
        public static string ReadPackageIdFromRoot(string packageRootPath)
        {
            if (string.IsNullOrWhiteSpace(packageRootPath))
            {
                return string.Empty;
            }

            string packageJsonPath = Path.Combine(packageRootPath, "package.json");
            try
            {
                string json = File.ReadAllText(packageJsonPath);
                // Read the package name without adding a runtime JSON-library dependency.
                const string nameKey = "\"name\"";
                int nameIndex = json.IndexOf(nameKey, StringComparison.Ordinal);
                if (nameIndex < 0)
                {
                    return string.Empty;
                }

                int colonIndex = json.IndexOf(':', nameIndex + nameKey.Length);
                if (colonIndex < 0)
                {
                    return string.Empty;
                }

                int firstQuote = json.IndexOf('"', colonIndex + 1);
                if (firstQuote < 0)
                {
                    return string.Empty;
                }

                int secondQuote = json.IndexOf('"', firstQuote + 1);
                if (secondQuote < 0)
                {
                    return string.Empty;
                }

                return json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
            }
            catch
            {
                return string.Empty;
            }
        }

#if UNITY_EDITOR
        internal static bool TryResolveAssetsPath(
            string assetPath,
            out string canonicalAssetPath,
            out string absolutePath
        )
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                canonicalAssetPath = string.Empty;
                absolutePath = string.Empty;
                return false;
            }

            string normalized = assetPath.SanitizePath();
            if (
                !string.Equals(normalized, "Assets", StringComparison.OrdinalIgnoreCase)
                && !normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
            )
            {
                canonicalAssetPath = string.Empty;
                absolutePath = string.Empty;
                return false;
            }

            try
            {
                string assetsRoot = Path.GetFullPath(Application.dataPath)
                    .SanitizePath()
                    .TrimEnd('/');
                string suffix =
                    normalized.Length == "Assets".Length
                        ? string.Empty
                        : normalized.Substring("Assets/".Length).TrimStart('/');
                string candidate = Path.GetFullPath(Path.Combine(assetsRoot, suffix))
                    .SanitizePath()
                    .TrimEnd('/');
                StringComparison comparison =
                    Path.DirectorySeparatorChar == '\\'
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal;
                string resolvedAssetPath;
                if (string.Equals(candidate, assetsRoot, comparison))
                {
                    resolvedAssetPath = "Assets";
                }
                else if (candidate.StartsWith(assetsRoot + "/", comparison))
                {
                    resolvedAssetPath = "Assets/" + candidate.Substring(assetsRoot.Length + 1);
                }
                else
                {
                    canonicalAssetPath = string.Empty;
                    absolutePath = string.Empty;
                    return false;
                }

                canonicalAssetPath = resolvedAssetPath;
                absolutePath = candidate;
                return true;
            }
            catch (Exception)
            {
                canonicalAssetPath = string.Empty;
                absolutePath = string.Empty;
                return false;
            }
        }
#endif
    }
}
