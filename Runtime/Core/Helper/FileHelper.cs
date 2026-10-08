// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Lightweight file I/O helpers with safe default behaviors.
    /// </summary>
    /// <remarks>
    /// Focuses on project asset file management. Methods are safe to call in editor and player.
    /// </remarks>
    public static class FileHelper
    {
        /// <summary>
        /// Creates an absent file only after its initial contents have been staged and flushed.
        /// </summary>
        /// <remarks>
        /// Failed staging leaves the destination absent so initialization can be retried. Platforms or
        /// filesystems without atomic publication that preserves competing creators return false.
        /// Owned temporary files are removed on a best-effort basis; process interruption may leave one.
        /// </remarks>
        /// <param name="path">Absolute or relative file path; blank paths are refused.</param>
        /// <param name="contents">Optional initial contents (defaults to empty).</param>
        /// <returns>True if the file was created; false if it already existed or creation failed.</returns>
        public static bool InitializePath(string path, byte[] contents = null)
        {
            return DurableFile.TryInitializeAllBytes(path, contents);
        }

        /// <summary>
        /// Asynchronously copies a file and replaces the destination after the copy completes.
        /// </summary>
        /// <param name="sourcePath">Source file path.</param>
        /// <param name="destinationPath">Destination file path (overwrites).</param>
        /// <param name="bufferSize">Buffer size in bytes (default 81920).</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True on success; false if the copy fails or is cancelled.</returns>
        public static async ValueTask<bool> CopyFileAsync(
            string sourcePath,
            string destinationPath,
            int bufferSize = 81920,
            CancellationToken cancellationToken = default
        )
        {
            if (bufferSize <= 0)
            {
                return false;
            }

            Exception error = await DurableFile
                .CopyAsync(sourcePath, destinationPath, bufferSize, cancellationToken)
                .ConfigureAwait(false);
            return error == null;
        }
    }
}
