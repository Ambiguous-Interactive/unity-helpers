// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Utils;
    using static WallstopStudios.UnityHelpers.Editor.AssetProcessors.SpriteLabelProcessor;
    using Object = UnityEngine.Object;

    /// <summary>Provides test-side setup and inspection for SpriteLabelProcessor.</summary>
    internal static class SpriteLabelProcessorTestAccess
    {
        internal static int PendingImportedPathCount
        {
            get { return PendingImportedPaths.Count; }
        }

        internal static string[] SnapshotPendingImportedPaths()
        {
            string[] snapshot = new string[PendingImportedPaths.Count];
            PendingImportedPaths.CopyTo(snapshot);
            return snapshot;
        }

        internal static void EnqueueImportedPaths(string[] importedAssets)
        {
            if (importedAssets == null || importedAssets.Length == 0)
            {
                return;
            }

            foreach (string path in importedAssets)
            {
                if (IsCandidatePath(path))
                {
                    PendingImportedPaths.Add(path);
                }
            }
        }

        internal static void Reset()
        {
            PendingImportedPaths.Clear();
        }
    }
#endif
}
