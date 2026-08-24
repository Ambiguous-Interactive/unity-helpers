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

            for (int i = 0; i < BlockedSegments.Length; i++)
            {
                string segment = BlockedSegments[i];
                int index = assetPath.IndexOf(segment, StringComparison.Ordinal);
                while (0 <= index)
                {
                    // Check that _llm_ is not preceded by an underscore (to avoid matching __llm__)
                    bool validPrefix = index == 0 || assetPath[index - 1] != '_';
                    // Check that _llm_ is not followed by an additional underscore immediately after
                    int afterIndex = index + segment.Length;
                    bool validSuffix =
                        assetPath.Length <= afterIndex || assetPath[afterIndex] != '_';

                    if (validPrefix && validSuffix)
                    {
                        return true;
                    }

                    // Continue searching for next occurrence
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

            for (int i = 0; i < assetPaths.Length; i++)
            {
                string assetPath = assetPaths[i];
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

                    for (int i = 0; i < batch.Length; i++)
                    {
                        DeleteAsset(batch[i]);
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
