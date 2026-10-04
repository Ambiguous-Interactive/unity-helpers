// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.Validation
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using UnityEditor.PackageManager;

    internal static class TestPackageRoot
    {
        internal const string PackageName = "com.wallstop-studios.unity-helpers";

        internal static string Resolve(Assembly assembly, [CallerFilePath] string sourcePath = "")
        {
            PackageInfo package = assembly == null ? null : PackageInfo.FindForAssembly(assembly);
            if (package != null && IsPackageRoot(package.resolvedPath))
            {
                return Path.GetFullPath(package.resolvedPath);
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return null;
            }

            string current = Path.GetDirectoryName(sourcePath);
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (IsPackageRoot(current))
                {
                    return Path.GetFullPath(current);
                }

                current = Path.GetDirectoryName(current);
            }

            return null;
        }

        internal static bool IsPackageRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalized = path.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (string.Equals(segment, "node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!Directory.Exists(Path.Combine(path, "Tests")))
            {
                return false;
            }

            try
            {
                using JsonDocument manifest = JsonDocument.Parse(
                    File.ReadAllText(Path.Combine(path, "package.json"))
                );
                return manifest.RootElement.ValueKind == JsonValueKind.Object
                    && manifest.RootElement.TryGetProperty("name", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && string.Equals(name.GetString(), PackageName, StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
