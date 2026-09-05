// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Validation.Continuous
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;

    /// <summary>
    /// Turns an import batch into a re-check queue for <see cref="ValidationAutoRun"/>.
    /// </summary>
    /// <remarks>
    /// Top-level rather than nested inside <see cref="ValidationAutoRun"/> so Unity's own scan for
    /// <c>AssetPostprocessor</c> subclasses finds it the same way it finds every other one in this
    /// package. It does nothing but translate paths to GUIDs -- <c>AssetPathToGUID</c> reads import
    /// metadata and does not deserialize the asset, which is the one thing a callback running
    /// inside the import phase must not do.
    /// </remarks>
    internal sealed class ValidationAutoRunProcessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            if (!ValidationAutoRun.Enabled)
            {
                return;
            }

            List<string> guids = new List<string>();
            Collect(importedAssets, guids);
            Collect(movedAssets, guids);
            ValidationAutoRun.Queue(guids, deletedAssets != null && 0 < deletedAssets.Length, 0);
        }

        private static void Collect(string[] paths, List<string> guids)
        {
            if (paths == null)
            {
                return;
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (!string.IsNullOrEmpty(guid))
                {
                    guids.Add(guid);
                }
            }
        }
    }
#endif
}
