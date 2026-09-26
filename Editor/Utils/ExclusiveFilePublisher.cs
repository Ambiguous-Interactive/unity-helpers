// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.IO;
    using WallstopStudios.UnityHelpers.Core.Helper;

    /// <summary>
    /// Publishes a staged editor file without replacing a concurrently created destination.
    /// </summary>
    internal static class ExclusiveFilePublisher
    {
        internal static Action<string> DeleteStagedFile = File.Delete;

        /// <summary>
        /// Returns false when another entry occupies the destination. Publish failures throw;
        /// cleanup failures return a warning after the output is already published.
        /// </summary>
        internal static bool TryPublishNewFile(
            string stagedPath,
            string destinationPath,
            out Exception cleanupWarning
        )
        {
            Exception warning = null;
            if (
                !DurableFile.TryPublishStagedFileWithoutOverwrite(
                    stagedPath,
                    destinationPath,
                    out bool removeStagedFile
                )
            )
            {
                cleanupWarning = null;
                return false;
            }

            if (removeStagedFile)
            {
                try
                {
                    DeleteStagedFile(stagedPath);
                }
                catch (Exception cleanupError)
                {
                    warning = new IOException(
                        $"Published '{destinationPath}' but could not remove staged file '{stagedPath}'.",
                        cleanupError
                    );
                }
            }

            cleanupWarning = warning;
            return true;
        }
    }
#endif
}
