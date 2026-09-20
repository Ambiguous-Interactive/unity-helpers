// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.IO;

    /// <summary>
    /// Publishes a staged editor file without replacing a concurrently created destination.
    /// </summary>
    internal static class ExclusiveFilePublisher
    {
        /// <summary>
        /// Returns false only when another file already occupies the destination. Copy failures
        /// throw so callers report an error rather than silently skipping a partial output.
        /// </summary>
        internal static bool TryPublishNewFile(string stagedPath, string destinationPath)
        {
            using (
                FileStream source = new(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read)
            )
            {
                FileStream destination;
                try
                {
                    destination = new FileStream(
                        destinationPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None
                    );
                }
                catch (IOException) when (File.Exists(destinationPath))
                {
                    return false;
                }

                using (destination)
                {
                    source.CopyTo(destination);
                    destination.Flush(flushToDisk: true);
                }
            }

            try
            {
                File.Delete(stagedPath);
            }
            catch (IOException)
            {
                // The output is complete even if the temporary file remains.
            }
            catch (UnauthorizedAccessException)
            {
                // The output is complete even if the temporary file remains.
            }

            return true;
        }
    }
#endif
}
