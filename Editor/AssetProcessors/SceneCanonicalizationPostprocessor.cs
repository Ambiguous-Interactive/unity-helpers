// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Utils;

    internal sealed class SceneCanonicalizationPostprocessor : AssetPostprocessor
    {
        private static readonly HashSet<string> PendingPaths = new(StringComparer.Ordinal);
        private static readonly Action DrainAction = DrainPendingPaths;

        internal static void QueuePaths(string[] paths)
        {
            if (paths == null)
            {
                return;
            }
            foreach (string path in paths)
            {
                if (SceneCanonicalizer.IsAutomaticPath(path))
                {
                    PendingPaths.Add(path);
                }
            }
            if (0 < PendingPaths.Count)
            {
                AssetPostprocessorDeferral.Schedule(DrainAction);
            }
        }

        internal static void DrainPendingPaths()
        {
            using PooledResource<List<string>> lease = Buffers<string>.List.Get(
                out List<string> paths
            );
            paths.AddRange(PendingPaths);
            PendingPaths.Clear();
            paths.Sort(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (!SceneCanonicalizer.IsAutomaticPath(path))
                {
                    continue;
                }
                if (
                    path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                    && EditorSceneManager.GetSceneByPath(path).IsValid()
                )
                {
                    continue;
                }
                if (
                    !SceneCanonicalizer.TryCanonicalizeFile(
                        path,
                        out bool changed,
                        out Exception error
                    )
                )
                {
                    Debug.LogWarning($"Cannot canonicalize '{path}': {error.Message}");
                }
                else if (changed)
                {
                    AssetDatabase.ImportAsset(path);
                }
            }
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            QueuePaths(importedAssets);
            QueuePaths(movedAssets);
        }
    }
}
