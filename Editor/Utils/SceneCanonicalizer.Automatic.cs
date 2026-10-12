// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    public static partial class SceneCanonicalizer
    {
        private const string AutomaticFoldersKey =
            nameof(SceneCanonicalizer) + "." + nameof(TrySetAutomaticFolders);

        /// <summary>Gets the opt-in automatic folders for this editor session.</summary>
        public static string[] GetAutomaticFolders()
        {
            string stored = SessionState.GetString(AutomaticFoldersKey, string.Empty);
            return string.IsNullOrEmpty(stored) ? Array.Empty<string>() : stored.Split('\n');
        }

        /// <summary>Sets automatic scene and prefab folders for this editor session, surviving script reloads.</summary>
        /// <remarks>Pass null or an empty array to disable. Accepts only project-relative Assets paths. Open scenes are skipped; canonicalize and reopen them explicitly.</remarks>
        public static bool TrySetAutomaticFolders(string[] folders, out Exception error)
        {
            List<string> normalized = new();
            if (folders != null)
            {
                foreach (string folder in folders)
                {
                    if (string.IsNullOrWhiteSpace(folder))
                    {
                        error = new ArgumentException(
                            "Automatic folders must be project-relative Assets paths.",
                            nameof(folders)
                        );
                        return false;
                    }
                    string path = folder.Trim().Replace('\\', '/').TrimEnd('/');
                    if (
                        (
                            !string.Equals(path, "Assets", StringComparison.Ordinal)
                            && !path.StartsWith("Assets/", StringComparison.Ordinal)
                        )
                        || path.Contains("\n")
                        || path.Contains("\r")
                    )
                    {
                        error = new ArgumentException(
                            "Automatic folders must be project-relative Assets paths.",
                            nameof(folders)
                        );
                        return false;
                    }
                    foreach (string segment in path.Split('/'))
                    {
                        if (
                            string.IsNullOrWhiteSpace(segment)
                            || string.Equals(segment, ".", StringComparison.Ordinal)
                            || string.Equals(segment, "..", StringComparison.Ordinal)
                        )
                        {
                            error = new ArgumentException(
                                "Automatic folders cannot contain empty or relative segments.",
                                nameof(folders)
                            );
                            return false;
                        }
                    }
                    if (!normalized.Contains(path))
                    {
                        normalized.Add(path);
                    }
                }
            }
            normalized.Sort(StringComparer.Ordinal);
            SessionState.SetString(AutomaticFoldersKey, string.Join("\n", normalized));
            error = null;
            return true;
        }

        internal static bool IsAutomaticPath(string path)
        {
            if (
                string.IsNullOrWhiteSpace(path)
                || (
                    !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return false;
            }
            foreach (string folder in GetAutomaticFolders())
            {
                if (path.StartsWith(folder + "/", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
