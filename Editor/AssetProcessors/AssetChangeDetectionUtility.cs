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
        /// Whether the asset-change watcher may initialize. Defaults to <see langword="false"/> in
        /// batch mode and <see langword="true"/> otherwise.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Discovering <c>[DetectAssetChanged]</c> handlers is an all-types / all-methods
        /// reflection scan. Running it inside Unity's import phase destabilizes the asset pipeline
        /// -- a native crash on some Unity versions, multi-minute importer stalls on others -- and
        /// a headless run has no author to act on a callback, so the default keeps it off there.
        /// </para>
        /// <para>
        /// Set this from an <c>[InitializeOnLoad]</c> static constructor so it applies before the
        /// watcher's own deferred initialization. Assign <see langword="false"/> to keep the
        /// watcher off in an interactive editor, or <see langword="true"/> to opt a headless asset
        /// pipeline back in. Once the watcher has initialized, turning it off stops further
        /// initialization but leaves already-discovered subscriptions in place; use
        /// <see cref="ResetEnabledToDefault"/> to drop a previous override.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code><![CDATA[
        /// [InitializeOnLoad]
        /// internal static class DisableAssetWatcher
        /// {
        ///     static DisableAssetWatcher()
        ///     {
        ///         AssetChangeDetectionUtility.Enabled = false;
        ///     }
        /// }
        /// ]]></code>
        /// </example>
        public static bool Enabled
        {
            get => DetectAssetChangeProcessor.IsEnabled;
            set => DetectAssetChangeProcessor.EnabledOverride = value;
        }

        /// <summary>
        /// Drops any <see cref="Enabled"/> override and restores the default policy.
        /// </summary>
        public static void ResetEnabledToDefault()
        {
            DetectAssetChangeProcessor.EnabledOverride = null;
        }

        /// <summary>
        /// Forces <see cref="Enabled"/> for the life of the returned scope, then restores whatever
        /// it was before.
        /// </summary>
        /// <param name="enabled">Whether the watcher may initialize inside the scope.</param>
        /// <returns>A scope that restores the captured state when disposed.</returns>
        /// <example>
        /// <code><![CDATA[
        /// using (AssetChangeDetectionUtility.EnabledScope(false))
        /// {
        ///     ImportEverything();
        /// }
        /// ]]></code>
        /// </example>
        public static AssetChangeDetectionEnabledScope EnabledScope(bool enabled)
        {
            return new AssetChangeDetectionEnabledScope(enabled);
        }

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
