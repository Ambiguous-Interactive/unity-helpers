// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System;
    using UnityEditor;

    /// <summary>Filters an owned asset path during save refusal controls.</summary>
    public sealed class HelpersAssetSaveFilter : AssetModificationProcessor
    {
        internal static string BlockedPath { get; set; }

        internal static int FilteredSaves { get; private set; }

        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (paths == null)
            {
                return Array.Empty<string>();
            }
            if (string.IsNullOrWhiteSpace(BlockedPath))
            {
                return paths;
            }
            int matches = 0;
            foreach (string path in paths)
            {
                if (string.Equals(path, BlockedPath, StringComparison.Ordinal))
                {
                    ++matches;
                }
            }
            if (matches == 0)
            {
                return paths;
            }
            ++FilteredSaves;
            string[] permitted = new string[paths.Length - matches];
            int index = 0;
            foreach (string path in paths)
            {
                if (!string.Equals(path, BlockedPath, StringComparison.Ordinal))
                {
                    permitted[index++] = path;
                }
            }
            return permitted;
        }
    }
}
