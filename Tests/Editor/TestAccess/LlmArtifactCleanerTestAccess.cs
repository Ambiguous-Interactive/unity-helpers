// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using static WallstopStudios.UnityHelpers.Editor.AssetProcessors.LlmArtifactCleaner;

    /// <summary>Provides test-side setup and inspection for LlmArtifactCleaner.</summary>
    internal static class LlmArtifactCleanerTestAccess
    {
        internal static int PendingDeletionCount
        {
            get { return PendingDeletions.Count; }
        }

        internal static void Reset()
        {
            PendingDeletions.Clear();
            _isDeleting = false;
        }

        internal static void DeleteBlockedAssets(string[] assetPaths)
        {
            EnqueueBlockedAssets(assetPaths);
            DrainPendingDeletions();
        }
    }
#endif
}
