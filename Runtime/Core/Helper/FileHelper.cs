// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>
    /// Lightweight file I/O helpers with safe default behaviors.
    /// </summary>
    /// <remarks>
    /// Focuses on project asset file management. Methods are safe to call in editor and player.
    /// </remarks>
    public static class FileHelper
    {
        /// <summary>
        /// Creates a file at the specified path if it does not exist, optionally writing initial contents.
        /// </summary>
        /// <param name="path">Absolute or relative file path.</param>
        /// <param name="contents">Optional initial contents (defaults to empty).</param>
        /// <returns>True if the file was created; false if it already existed or creation failed.</returns>
        public static bool InitializePath(string path, byte[] contents = null)
        {
            if (File.Exists(path))
            {
                return false;
            }

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            try
            {
                using FileStream fileStream = new(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None
                );
                contents ??= Array.Empty<byte>();
                fileStream.Write(contents, 0, contents.Length);
                return true;
            }
            catch (IOException e)
            {
                Debug.LogError($"File {path} already exists, not creating.\n{e}");
                return false;
            }
        }

        /// <summary>
        /// Asynchronously copies a file to a new path using a buffered stream.
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
            try
            {
                // Synchronous `using` (not `await using`) is intentional: the copy itself is
                // asynchronous, but disposing a FileStream synchronously avoids a dependency on
                // System.IAsyncDisposable, which is unavailable under the .NET Standard 2.0
                // profile used by older Unity LTS streams (e.g. 2021.3). Dispose() still flushes.
                using FileStream sourceStream = new(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize,
                    useAsync: true
                );
                using FileStream destinationStream = new(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize,
                    useAsync: true
                );
                await sourceStream.CopyToAsync(destinationStream, bufferSize, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
