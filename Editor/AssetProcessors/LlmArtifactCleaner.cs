// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;

    internal sealed class LlmArtifactCleaner : AssetPostprocessor
    {
        private const string PackagePathPrefix = "Packages/com.wallstop-studios.unity-helpers/";
        private const string LlmPrefix = "_llm_";

        internal static readonly HashSet<string> PendingDeletions = new(
            StringComparer.OrdinalIgnoreCase
        );

        internal static bool _isDeleting;

        private static readonly string[] BlockedSegments = { LlmPrefix };

        private static readonly Action DrainAction = DrainPendingDeletions;

        internal static bool ShouldDelete(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return false;
            }

            if (!assetPath.StartsWith(PackagePathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (string segment in BlockedSegments)
            {
                int index = assetPath.IndexOf(segment, StringComparison.Ordinal);
                while (0 <= index)
                {
                    // Reject doubled underscores so __llm__ does not match _llm_.
                    bool validPrefix = index == 0 || assetPath[index - 1] != '_';

                    int afterIndex = index + segment.Length;
                    bool validSuffix =
                        assetPath.Length <= afterIndex || assetPath[afterIndex] != '_';
                    if (validPrefix && validSuffix)
                    {
                        return true;
                    }

                    index = assetPath.IndexOf(segment, index + 1, StringComparison.Ordinal);
                }
            }

            return false;
        }

        internal static void EnqueueBlockedAssets(string[] assetPaths)
        {
            if (assetPaths == null || assetPaths.Length == 0)
            {
                return;
            }

            foreach (string assetPath in assetPaths)
            {
                if (ShouldDelete(assetPath))
                {
                    PendingDeletions.Add(assetPath);
                }
            }
        }

        internal static void DrainPendingDeletions()
        {
            if (PendingDeletions.Count == 0 || _isDeleting)
            {
                return;
            }

            _isDeleting = true;
            try
            {
                while (0 < PendingDeletions.Count)
                {
                    string[] batch = new string[PendingDeletions.Count];
                    PendingDeletions.CopyTo(batch);
                    PendingDeletions.Clear();

                    foreach (string batchElement in batch)
                    {
                        DeleteAsset(batchElement);
                    }
                }
            }
            finally
            {
                _isDeleting = false;
            }
        }

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            EnqueueBlockedAssets(importedAssets);
            EnqueueBlockedAssets(movedAssets);
            if (PendingDeletions.Count == 0)
            {
                return;
            }

            AssetPostprocessorDeferral.Schedule(DrainAction);
        }

        private static void DeleteAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            AssetDatabase.DeleteAsset(assetPath);
        }
    }
}
