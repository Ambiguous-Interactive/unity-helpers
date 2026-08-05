// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.AssetProcessors
{
    using System;

    /// <summary>
    /// Captures the asset-change watcher's enablement, applies an override for the life of the
    /// scope, and restores exactly what it captured on dispose.
    /// </summary>
    /// <remarks>
    /// Prefer this over assigning <see cref="AssetChangeDetectionUtility.Enabled"/> and restoring
    /// by hand: the restore cannot be forgotten on an early return or an exception, and it puts
    /// back the previous state rather than a guess at the default. Disposing twice is a no-op, so
    /// a scope held in a field and disposed from teardown is safe.
    /// </remarks>
    /// <example>
    /// <code><![CDATA[
    /// // Bulk import with the watcher off, restored however it was found.
    /// using (new AssetChangeDetectionEnabledScope(false))
    /// {
    ///     ImportEverything();
    /// }
    /// ]]></code>
    /// </example>
    public sealed class AssetChangeDetectionEnabledScope : IDisposable
    {
        private readonly bool? _captured;
        private bool _disposed;

        /// <summary>
        /// Captures the current enablement and forces the watcher on or off.
        /// </summary>
        /// <param name="enabled">Whether the watcher may initialize inside the scope.</param>
        public AssetChangeDetectionEnabledScope(bool enabled)
        {
            _captured = DetectAssetChangeProcessor.EnabledOverride;
            DetectAssetChangeProcessor.EnabledOverride = enabled;
        }

        /// <summary>
        /// Restores the enablement captured when the scope was created.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DetectAssetChangeProcessor.EnabledOverride = _captured;
        }
    }
}
