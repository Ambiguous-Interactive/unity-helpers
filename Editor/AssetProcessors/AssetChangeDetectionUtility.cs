// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
    /// <summary>
    /// Provides editor utilities for the <c>DetectAssetChanged</c> asset-change watcher.
    /// </summary>
    public static class AssetChangeDetectionUtility
    {
        /// <summary>
        /// Clears the asset-change watcher's loop-protection state and pending change queue.
        /// </summary>
        /// <remarks>
        /// This preserves discovered watchers and subscriptions. Use it after fixing a callback that
        /// caused recursive asset-change processing so the editor can resume dispatching changes
        /// without a domain reload.
        /// </remarks>
        public static void ResetLoopProtection()
        {
            DetectAssetChangeProcessor.ResetLoopProtection();
        }
    }
}
