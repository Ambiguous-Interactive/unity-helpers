// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using WallstopStudios.UnityHelpers.Core.Threading;

    /// <summary>
    /// File writes that never leave a torn document behind, for player-owned data such as saves,
    /// settings and ledgers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="File.WriteAllText(string, string)"/> truncates the destination before writing a
    /// single byte, so an interrupted write replaces a valid document with a partial one. Every
    /// write here stages the new contents in a sibling file, forces them out of the page cache, and
    /// only then swaps the staged file over the destination.
    /// </para>
    /// <para>
    /// <b>Scope — what this does and does not promise.</b>
    /// It <b>does</b> eliminate the torn-file window: a reader observes either the complete previous
    /// contents or the complete new ones. It <b>does</b> force the data out of the page cache before
    /// the swap. It <b>does</b> serialize concurrent operations on the same path within this
    /// process. It is <b>not</b> full crash safety — .NET cannot flush a <i>directory</i>, so a
    /// filesystem may still reorder the rename behind the data write. It does <b>not</b> coordinate
    /// with other processes: a second process writing the same file concurrently is reported as a
    /// failure rather than allowed to corrupt the document. Do not describe consumers of this type
    /// as crash-safe.
    /// </para>
    /// <para>
    /// Where the format allows a log of records, <see cref="TryAppendAllText"/> is strictly stronger
    /// than a whole-document rewrite: an append never rewrites bytes that are already on disk.
    /// </para>
    /// <para>
    /// Contains no <c>UnityEngine</c> dependency and is safe to call from any thread.
    /// </para>
    /// </remarks>
    public static class DurableFile
    {
        /// <summary>
        /// Suffix of the sibling file a write is staged into before the swap.
        /// </summary>
        /// <remarks>
        /// Public so consumers can recognize and ignore a leftover staged file, which is what an
        /// interrupted write leaves behind.
        /// </remarks>
        public const string TemporarySuffix = ".tmp";

        private const int DefaultBufferSize = 4096;

        // FileMode.Append does not provide atomic append; bounded per-path gates prevent writer overlap.
        private const int GateCount = 32;

        private static readonly SemaphoreSlim[] Gates = CreateGates();

        private static readonly UTF8Encoding Utf8NoByteOrderMark = new(
            encoderShouldEmitUTF8Identifier: false
        );

        /// <summary>
        /// Replaces a file's entire contents, staging and flushing before the swap.
        /// </summary>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Text to write. Null is treated as empty.</param>
        /// <param name="error">The failure when this returns false; null otherwise.</param>
        /// <returns>True when the destination holds the new contents.</returns>
        public static bool TryWriteAllText(string path, string contents, out Exception error)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = new ArgumentException("A destination path is required.", nameof(path));
                return false;
            }

            using (EnterGate(path))
            {
                string temporaryPath = path + TemporarySuffix;
                FileStream staging;
                byte[] bytes;
                try
                {
                    // Encode before opening so encoding failure cannot leave an undisposed stream.
                    bytes = Utf8NoByteOrderMark.GetBytes(contents ?? string.Empty);
                    EnsureDirectory(path);
                    staging = OpenStagingStream(temporaryPath, useAsync: false);
                }
                catch (Exception e)
                {
                    error = e;
                    return false;
                }

                try
                {
                    using (staging)
                    {
                        staging.Write(bytes, 0, bytes.Length);
                        staging.Flush(flushToDisk: true);
                    }

                    Swap(temporaryPath, path);
                    error = null;
                    return true;
                }
                catch (Exception e)
                {
                    DiscardStagedFile(temporaryPath);
                    error = e;
                    return false;
                }
            }
        }

        /// <summary>
        /// Asynchronously replaces a file's entire contents, staging and flushing before the swap.
        /// </summary>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Text to write. Null is treated as empty.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Null on success, otherwise the failure.</returns>
        public static async ValueTask<Exception> WriteAllTextAsync(
            string path,
            string contents,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ArgumentException("A destination path is required.", nameof(path));
            }

            SemaphoreLease gate;
            try
            {
                gate = await EnterGateAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return e;
            }

            using (gate)
            {
                string temporaryPath = path + TemporarySuffix;
                FileStream staging;
                byte[] bytes;
                try
                {
                    bytes = Utf8NoByteOrderMark.GetBytes(contents ?? string.Empty);
                    EnsureDirectory(path);
                    staging = OpenStagingStream(temporaryPath, useAsync: true);
                }
                catch (Exception e)
                {
                    return e;
                }

                try
                {
                    // Synchronous disposal preserves compatibility with Unity profiles lacking IAsyncDisposable.
                    using (staging)
                    {
                        await staging
                            .WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                            .ConfigureAwait(false);
                        staging.Flush(flushToDisk: true);
                    }

                    Swap(temporaryPath, path);
                    return null;
                }
                catch (Exception e)
                {
                    DiscardStagedFile(temporaryPath);
                    return e;
                }
            }
        }

        /// <summary>
        /// Appends text to a file, flushing before returning.
        /// </summary>
        /// <remarks>
        /// An append never rewrites bytes that are already on disk, so it cannot damage an earlier
        /// record. Concurrent appends from this process interleave whole records; an append from
        /// another process while one is in flight fails rather than corrupting the file. Empty or
        /// null <paramref name="contents"/> is a no-op success and does not create the file.
        /// </remarks>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Text to append.</param>
        /// <param name="error">The failure when this returns false; null otherwise.</param>
        /// <returns>True when the text reached the file, or when there was nothing to append.</returns>
        public static bool TryAppendAllText(string path, string contents, out Exception error)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = new ArgumentException("A destination path is required.", nameof(path));
                return false;
            }

            if (string.IsNullOrEmpty(contents))
            {
                error = null;
                return true;
            }

            using (EnterGate(path))
            {
                try
                {
                    EnsureDirectory(path);
                    byte[] bytes = Utf8NoByteOrderMark.GetBytes(contents);
                    using FileStream stream = OpenAppendStream(path, useAsync: false);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                    error = null;
                    return true;
                }
                catch (Exception e)
                {
                    error = e;
                    return false;
                }
            }
        }

        /// <summary>
        /// Asynchronously appends text to a file, flushing before returning.
        /// </summary>
        /// <remarks>
        /// Carries the same guarantees as <see cref="TryAppendAllText"/>.
        /// </remarks>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Text to append.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Null on success, otherwise the failure.</returns>
        public static async ValueTask<Exception> AppendAllTextAsync(
            string path,
            string contents,
            CancellationToken cancellationToken = default
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ArgumentException("A destination path is required.", nameof(path));
            }

            if (string.IsNullOrEmpty(contents))
            {
                return null;
            }

            SemaphoreLease gate;
            try
            {
                gate = await EnterGateAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return e;
            }

            using (gate)
            {
                try
                {
                    EnsureDirectory(path);
                    byte[] bytes = Utf8NoByteOrderMark.GetBytes(contents);
                    using FileStream stream = OpenAppendStream(path, useAsync: true);
                    await stream
                        .WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                        .ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                    return null;
                }
                catch (Exception e)
                {
                    return e;
                }
            }
        }

        /// <summary>
        /// Replaces a file with another file's contents, staging and flushing before the swap.
        /// </summary>
        /// <param name="sourcePath">File to copy from.</param>
        /// <param name="destinationPath">File to replace. Missing directories are created.</param>
        /// <param name="error">The failure when this returns false; null otherwise.</param>
        /// <returns>True when the destination holds the source's contents.</returns>
        public static bool TryCopy(string sourcePath, string destinationPath, out Exception error)
        {
            Exception validation = ValidateCopyPaths(sourcePath, destinationPath);
            if (validation != null)
            {
                error = validation;
                return false;
            }

            using (EnterGate(destinationPath))
            {
                string temporaryPath = destinationPath + TemporarySuffix;
                FileStream source;
                FileStream staging;
                try
                {
                    // Open the source before creating destination directories so failed reads leave no new folders.
                    source = OpenSourceStream(sourcePath, useAsync: false);
                    EnsureDirectory(destinationPath);
                }
                catch (Exception e)
                {
                    error = e;
                    return false;
                }

                try
                {
                    staging = OpenStagingStream(temporaryPath, useAsync: false);
                }
                catch (Exception e)
                {
                    source.Dispose();
                    error = e;
                    return false;
                }

                try
                {
                    using (source)
                    using (staging)
                    {
                        source.CopyTo(staging, DefaultBufferSize);
                        staging.Flush(flushToDisk: true);
                    }

                    Swap(temporaryPath, destinationPath);
                    error = null;
                    return true;
                }
                catch (Exception e)
                {
                    DiscardStagedFile(temporaryPath);
                    error = e;
                    return false;
                }
            }
        }

        /// <summary>
        /// Asynchronously replaces a file with another file's contents, staging and flushing before
        /// the swap.
        /// </summary>
        /// <param name="sourcePath">File to copy from.</param>
        /// <param name="destinationPath">File to replace. Missing directories are created.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Null on success, otherwise the failure.</returns>
        public static async ValueTask<Exception> CopyAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken = default
        )
        {
            Exception invalid = ValidateCopyPaths(sourcePath, destinationPath);
            if (invalid != null)
            {
                return invalid;
            }

            SemaphoreLease gate;
            try
            {
                gate = await EnterGateAsync(destinationPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return e;
            }

            using (gate)
            {
                string temporaryPath = destinationPath + TemporarySuffix;
                FileStream source;
                FileStream staging;
                try
                {
                    // Source first, for the reason recorded in TryCopy.
                    source = OpenSourceStream(sourcePath, useAsync: true);
                    EnsureDirectory(destinationPath);
                }
                catch (Exception e)
                {
                    return e;
                }

                try
                {
                    staging = OpenStagingStream(temporaryPath, useAsync: true);
                }
                catch (Exception e)
                {
                    source.Dispose();
                    return e;
                }

                try
                {
                    using (source)
                    using (staging)
                    {
                        await source
                            .CopyToAsync(staging, DefaultBufferSize, cancellationToken)
                            .ConfigureAwait(false);

                        staging.Flush(flushToDisk: true);
                    }

                    Swap(temporaryPath, destinationPath);
                    return null;
                }
                catch (Exception e)
                {
                    DiscardStagedFile(temporaryPath);
                    return e;
                }
            }
        }

        /// <summary>
        /// Deletes a file if it exists, reporting failure rather than throwing.
        /// </summary>
        /// <param name="path">File to delete.</param>
        /// <returns>True when no file remains at <paramref name="path"/>.</returns>
        public static bool TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                // Another deleter can win the race; the contract only requires the file to be absent.
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Exception ValidateCopyPaths(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return new ArgumentException("A source path is required.", nameof(sourcePath));
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return new ArgumentException(
                    "A destination path is required.",
                    nameof(destinationPath)
                );
            }

            return null;
        }

        private static SemaphoreSlim[] CreateGates()
        {
            SemaphoreSlim[] gates = new SemaphoreSlim[GateCount];
            for (int i = 0; i < gates.Length; ++i)
            {
                gates[i] = new SemaphoreSlim(1, 1);
            }

            return gates;
        }

        private static SemaphoreLease EnterGate(string path)
        {
            return GateFor(path).Acquire();
        }

        private static ValueTask<SemaphoreLease> EnterGateAsync(
            string path,
            CancellationToken cancellationToken
        )
        {
            return GateFor(path).AcquireAsync(cancellationToken);
        }

        private static SemaphoreSlim GateFor(string path)
        {
            string key = path;
            try
            {
                key = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                // Unnormalizable paths still serialize operations, but aliases cannot share that gate.
            }

            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(key);
            return Gates[hash.PositiveMod(GateCount)];
        }

        private static FileStream OpenSourceStream(string sourcePath, bool useAsync)
        {
            return new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                DefaultBufferSize,
                useAsync
            );
        }

        // Only a successful exclusive staging open establishes cleanup ownership; failed opens may name another writer.
        private static FileStream OpenStagingStream(string temporaryPath, bool useAsync)
        {
            return new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                DefaultBufferSize,
                useAsync
            );
        }

        private static FileStream OpenAppendStream(string path, bool useAsync)
        {
            // FileShare.Read rejects other writers so cross-process appends fail rather than overwrite records.
            return new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                DefaultBufferSize,
                useAsync
            );
        }

        private static void EnsureDirectory(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        private static void DiscardStagedFile(string temporaryPath)
        {
            TryDelete(temporaryPath);
        }

        // The destination can change after the existence probe; retry the matching alternative on that race.
        private static void Swap(string temporaryPath, string path)
        {
            if (File.Exists(path))
            {
                Replace(temporaryPath, path);
                return;
            }

            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                Replace(temporaryPath, path);
            }
        }

        private static void Replace(string temporaryPath, string path)
        {
            try
            {
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            }
            catch (FileNotFoundException)
            {
                File.Move(temporaryPath, path);
            }
            catch (NotSupportedException)
            {
                // Platforms without File.Replace use delete-then-move, which briefly exposes an absent destination.
                File.Delete(path);
                File.Move(temporaryPath, path);
            }
        }
    }
}
