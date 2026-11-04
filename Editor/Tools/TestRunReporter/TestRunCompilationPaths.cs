// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_TEST_FRAMEWORK
    using System;
    using System.Collections.Generic;
    using System.IO;
    using WallstopStudios.UnityHelpers.Editor.Validation;
    using PackageInfo = UnityEditor.PackageManager.PackageInfo;

    internal static class TestRunCompilationPaths
    {
        internal static string Resolve(string path, Dictionary<string, string> packages)
        {
            const string PackagePrefix = "Packages/";
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            {
                return path;
            }
            if (!path.StartsWith(PackagePrefix, StringComparison.Ordinal))
            {
                return AuthoredAssetPaths.ToFileSystemPath(path);
            }
            int end = path.IndexOf('/', PackagePrefix.Length);
            if (end < 0)
            {
                return AuthoredAssetPaths.ToFileSystemPath(path);
            }
            string packagePath = path.Substring(0, end);
            if (!packages.TryGetValue(packagePath, out string resolved))
            {
                PackageInfo package = PackageInfo.FindForAssetPath(packagePath);
                resolved = package == null ? null : package.resolvedPath;
                packages[packagePath] = resolved;
            }
            return string.IsNullOrWhiteSpace(resolved)
                ? AuthoredAssetPaths.ToFileSystemPath(path)
                : Path.Combine(resolved, path.Substring(end + 1));
        }
    }
#endif
}
