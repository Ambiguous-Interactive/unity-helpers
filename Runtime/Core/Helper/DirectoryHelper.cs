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
        /// <exception cref="ArgumentException">Thrown if attempting to create a directory outside of <c>Assets/</c> in the editor.</exception>
        public static void EnsureDirectoryExists(string relativeDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(relativeDirectoryPath))
            {
                return;
            }

            // Normalize path separators to forward slashes for cross-platform consistency
            relativeDirectoryPath = relativeDirectoryPath.SanitizePath();

#if UNITY_EDITOR
            // Case-insensitive check for Assets/ prefix to handle Windows paths
            if (!relativeDirectoryPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                if (relativeDirectoryPath.Equals("Assets", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Debug.LogError(
                    $"Attempted to create directory outside of Assets: '{relativeDirectoryPath}'"
                );
                throw new ArgumentException(
                    "Cannot create directories outside the Assets folder using AssetDatabase.",
                    nameof(relativeDirectoryPath)
                );
            }

            // First, ensure the folder exists on disk. This prevents Unity's internal
            // "Moving file failed" modal dialog when CreateAsset tries to move a temp file
            // to a destination folder that doesn't exist.
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absoluteDirectory = null;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                absoluteDirectory = Path.Combine(projectRoot, relativeDirectoryPath);
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
                        $"DirectoryHelper: Failed to create directory on disk '{absoluteDirectory}': {e.Message}"
                    );
                }
            }

            // Check both AssetDatabase and disk to avoid creating duplicate folders.
            // AssetDatabase.IsValidFolder may not immediately reflect folders created
            // via Directory.CreateDirectory until a refresh, but AssetDatabase.CreateFolder
            // will create a duplicate folder (e.g., "tree 1") if the folder exists on disk.
            if (AssetDatabase.IsValidFolder(relativeDirectoryPath))
            {
                return;
            }

            // If the directory already exists on disk, we need to make the AssetDatabase aware
            // of it to prevent duplicate folder creation (e.g., "tree 1", "tree 2").
            bool directoryExistsOnDisk =
                !string.IsNullOrEmpty(absoluteDirectory) && Directory.Exists(absoluteDirectory);
            if (directoryExistsOnDisk)
            {
                // Refresh the parent folder to make Unity aware of the new folder on disk.
                // This is more targeted than a full AssetDatabase.Refresh().
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

                // Re-check after import
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
            string scriptDirectory = GetCallerScriptDirectory();
            if (string.IsNullOrEmpty(scriptDirectory))
            {
                return string.Empty;
            }

            string packageRootAbsolute = FindPackageRootPath(scriptDirectory);
            if (string.IsNullOrEmpty(packageRootAbsolute))
            {
                return string.Empty;
            }

            string targetPathAbsolute = Path.Combine(
                packageRootAbsolute,
                directory.Replace('/', Path.DirectorySeparatorChar)
            );

            // When the package is embedded under Assets/, the Assets-relative form is the
            // Unity-loadable path.
            string assetsRelative = AbsoluteToUnityRelativePath(targetPathAbsolute);
            if (!string.IsNullOrEmpty(assetsRelative))
            {
                return assetsRelative;
            }

            // Otherwise the package is CONSUMED (a UPM "file:" local package, a registry
            // package, or Library/PackageCache), where its source lives OUTSIDE the project
            // root -- including the CI layout where the ephemeral test project is created
            // INSIDE the checkout (project at <root>/.artifacts/..., package at file:<root>),
            // so the package root is an ANCESTOR of the project, never under it.
            // AbsoluteToUnityRelativePath returns empty for every such layout, which made this
            // method (and DirectoryHelper-dependent tests like ManualRecompileTests,
            // SceneHelperTests, and DirectoryHelperTests) fail for any normally-installed
            // package. Unity exposes a consumed package under "Packages/<id>/...", so compose
            // that loadable form from the package id -- valid for file:/registry/PackageCache
            // consumption alike. AbsoluteToUnityLoadablePath cannot be reused here: it derives the
            // path from the package's PHYSICAL location, which for a file:/local package contains
            // no "Packages/" or "Library/PackageCache/" segment (the source IS the checkout), so it
            // too returns empty for the CI layout.
            string packageId = ReadPackageIdFromRoot(packageRootAbsolute);
            if (string.IsNullOrEmpty(packageId))
            {
                return string.Empty;
            }

            string normalizedDirectory = directory.SanitizePath().Trim('/');
            return normalizedDirectory.Length == 0
                ? $"Packages/{packageId}"
                : $"Packages/{packageId}/{normalizedDirectory}";
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

            if (absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                // +1 to remove the leading slash only if projectRoot doesn't end with one
                int startIndex = projectRoot.EndsWith("/", StringComparison.OrdinalIgnoreCase)
                    ? projectRoot.Length
                    : projectRoot.Length + 1;
                return absolutePath.Length > startIndex ? absolutePath[startIndex..] : string.Empty;
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
        /// <param name="packageId">The package identifier (e.g., "com.wallstop-studios.unity-helpers") used for Library/PackageCache resolution.</param>
        /// <returns>A Unity-loadable path, or empty string if the path cannot be resolved.</returns>
        public static string AbsoluteToUnityLoadablePath(string absolutePath, string packageId)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            absolutePath = absolutePath.SanitizePath();

            // First try the standard Unity relative path (works for Assets/)
            string relativePath = AbsoluteToUnityRelativePath(absolutePath);
            if (!string.IsNullOrEmpty(relativePath))
            {
                return relativePath;
            }

            // Check if path is in Library/PackageCache
            const string packageCacheMarker = "Library/PackageCache/";
            int packageCacheIndex = absolutePath.IndexOf(
                packageCacheMarker,
                StringComparison.OrdinalIgnoreCase
            );
            if (packageCacheIndex >= 0)
            {
                // Extract the portion after "Library/PackageCache/{packageFolder}/"
                string afterCache = absolutePath[(packageCacheIndex + packageCacheMarker.Length)..];

                // The package folder may have version suffix like "com.package@1.0.0"
                // Find the first separator after the package folder name
                int firstSlash = afterCache.IndexOf('/');
                if (firstSlash > 0)
                {
                    string pathInsidePackage = afterCache[(firstSlash + 1)..];
                    if (!string.IsNullOrEmpty(packageId))
                    {
                        return $"Packages/{packageId}/{pathInsidePackage}";
                    }
                }

                return string.Empty;
            }

            // Check if path already contains "Packages/" segment (embedded packages, local packages)
            const string packagesMarker = "/Packages/";
            int packagesIndex = absolutePath.IndexOf(
                packagesMarker,
                StringComparison.OrdinalIgnoreCase
            );
            if (packagesIndex >= 0)
            {
                return "Packages/" + absolutePath[(packagesIndex + packagesMarker.Length)..];
            }

            return string.Empty;
        }

        /// <summary>
        /// Resolves a path relative to the package root (identified by package.json) to a Unity-loadable path.
        /// This method works regardless of where the package is installed (Assets, Packages, or Library/PackageCache).
        /// </summary>
        /// <param name="relativePath">The path relative to the package root (e.g., "Editor/Styles/MyStyle.uss").</param>
        /// <param name="sourceFilePath">Leave as default to use the calling script's path. This parameter is automatically filled by the compiler.</param>
        /// <returns>A Unity-loadable path that can be used with <c>AssetDatabase.LoadAssetAtPath</c>.</returns>
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

            if (string.IsNullOrEmpty(scriptDirectory))
            {
                return string.Empty;
            }

            string packageRootAbsolute = FindPackageRootPath(scriptDirectory);
            if (string.IsNullOrEmpty(packageRootAbsolute))
            {
                return string.Empty;
            }

            // Read package.json to get the package ID
            string packageId = ReadPackageIdFromRoot(packageRootAbsolute);

            string targetPathAbsolute = Path.Combine(
                    packageRootAbsolute,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)
                )
                .SanitizePath();

            return AbsoluteToUnityLoadablePath(targetPathAbsolute, packageId);
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
            if (!File.Exists(packageJsonPath))
            {
                return string.Empty;
            }

            try
            {
                string json = File.ReadAllText(packageJsonPath);
                // Simple parsing - look for "name": "value"
                // This avoids dependency on JSON libraries for runtime code
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
    }
}
