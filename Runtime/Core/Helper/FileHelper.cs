// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;
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
        /// Creates a file at the specified path if it does not exist, optionally writing initial contents.
        /// </summary>
        /// <param name="path">Absolute or relative file path.</param>
        /// <param name="contents">Optional initial contents (defaults to empty).</param>
        /// <returns>True if the file was created; false if it already existed or creation failed.</returns>
        public static bool InitializePath(string path, byte[] contents = null)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

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
            catch (Exception exception)
                when (exception
                        is IOException
                            or UnauthorizedAccessException
                            or ArgumentException
                            or NotSupportedException
                            or System.Security.SecurityException
                )
            {
                return false;
            }
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
