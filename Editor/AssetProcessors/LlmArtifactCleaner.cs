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

        private static readonly string[] BlockedSegments = { LlmPrefix };
        private static readonly HashSet<string> PendingDeletions = new(
            StringComparer.OrdinalIgnoreCase
        );
        private static readonly Action DrainAction = DrainPendingDeletions;

        private static bool _isDeleting;
        private static Action<string> DeleteAssetOverrideForTesting;

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

        internal static void DeleteBlockedAssets(string[] assetPaths)
        {
            EnqueueBlockedAssets(assetPaths);
            DrainPendingDeletions();
        }

        internal static int PendingDeletionCountForTesting
        {
            get { return PendingDeletions.Count; }
        }

        internal static void ResetForTesting()
        {
            PendingDeletions.Clear();
            DeleteAssetOverrideForTesting = null;
            _isDeleting = false;
        }

        internal static void SetDeleteAssetOverrideForTesting(Action<string> deleteAction)
        {
            DeleteAssetOverrideForTesting = deleteAction;
        }

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

        private static void EnqueueBlockedAssets(string[] assetPaths)
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

        private static void DrainPendingDeletions()
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

        private static void DeleteAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            if (DeleteAssetOverrideForTesting != null)
            {
                DeleteAssetOverrideForTesting(assetPath);
                return;
            }

            AssetDatabase.DeleteAsset(assetPath);
        }
    }
}
