// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

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

        // .NET's FileMode.Append is a seek-to-end at open time, NOT the O_APPEND / FILE_APPEND_DATA
        // the name suggests, so two threads appending to one path silently overwrite each other's
        // records (measured: 155 of 200 survived). Every operation therefore takes a gate keyed on
        // the destination. Striping keeps the table bounded; two unrelated paths that collide only
        // pay a little extra serialization.
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

            SemaphoreSlim gate = GateFor(path);
            gate.Wait();
            try
            {
                string temporaryPath = path + TemporarySuffix;
                FileStream stream;
                byte[] bytes;
                try
                {
                    // Encoded before the handle is opened, so nothing it can throw can strand an
                    // open FileStream that the catch below is not in a position to dispose.
                    bytes = Utf8NoByteOrderMark.GetBytes(contents ?? string.Empty);
                    EnsureDirectory(path);
                    stream = OpenStagingStream(temporaryPath, useAsync: false);
                }
                catch (Exception e)
                {
                    // The staged file was never opened here, so it is not this call's to delete.
                    error = e;
                    return false;
                }

                try
                {
                    using (stream)
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(flushToDisk: true);
                    }

                    Swap(temporaryPath, path);
                    error = null;
                    return true;
                }
                catch (Exception e)
                {
                    error = e;
                    DiscardStagedFile(temporaryPath);
                    return false;
                }
            }
            finally
            {
                gate.Release();
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

            SemaphoreSlim gate = GateFor(path);
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return e;
            }

            try
            {
                string temporaryPath = path + TemporarySuffix;
                FileStream stream;
                byte[] bytes;
                try
                {
                    // Encoded before the handle is opened, so nothing it can throw can strand an
                    // open FileStream that the catch below is not in a position to dispose.
                    bytes = Utf8NoByteOrderMark.GetBytes(contents ?? string.Empty);
                    EnsureDirectory(path);
                    stream = OpenStagingStream(temporaryPath, useAsync: true);
                }
                catch (Exception e)
                {
                    // The staged file was never opened here, so it is not this call's to delete.
                    return e;
                }

                try
                {
                    // Synchronous `using` (not `await using`) keeps this off System.IAsyncDisposable,
                    // which is unavailable under the .NET Standard 2.0 profile of older Unity LTS.
                    using (stream)
                    {
                        await stream
                            .WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                            .ConfigureAwait(false);
                        stream.Flush(flushToDisk: true);
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
            finally
            {
                gate.Release();
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

            SemaphoreSlim gate = GateFor(path);
            gate.Wait();
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
            finally
            {
                gate.Release();
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

            SemaphoreSlim gate = GateFor(path);
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return e;
            }

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
            finally
            {
                gate.Release();
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
            error = ValidateCopyPaths(sourcePath, destinationPath);
            if (error != null)
            {
                return false;
            }

            SemaphoreSlim gate = GateFor(destinationPath);
            gate.Wait();
            try
            {
                string temporaryPath = destinationPath + TemporarySuffix;
                bool staged = false;
                try
                {
                    EnsureDirectory(destinationPath);
                    File.Copy(sourcePath, temporaryPath, overwrite: true);
                    staged = true;
                    FlushToDisk(temporaryPath);
                    Swap(temporaryPath, destinationPath);
                    error = null;
                    return true;
                }
                catch (Exception e)
                {
                    error = e;
                    if (staged)
                    {
                        DiscardStagedFile(temporaryPath);
                    }

                    return false;
                }
            }
            finally
            {
                gate.Release();
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

            SemaphoreSlim gate = GateFor(destinationPath);
            try
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                return e;
            }

            string temporaryPath = destinationPath + TemporarySuffix;
            // FileHelper.CopyFileAsync opens the destination with FileMode.Create before it copies a
            // byte, so a false return still means a staged file exists here to clean up. Only a
            // failure before that call leaves nothing of ours behind.
            bool staged = false;
            try
            {
                EnsureDirectory(destinationPath);
                staged = true;
                bool copied = await FileHelper
                    .CopyFileAsync(sourcePath, temporaryPath, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!copied)
                {
                    DiscardStagedFile(temporaryPath);
                    return new IOException(
                        $"Failed to copy '{sourcePath}' to '{destinationPath}'."
                    );
                }

                // The flush and the swap stay synchronous: splitting them across an await buys
                // nothing, and both are metadata-scale operations.
                FlushToDisk(temporaryPath);
                Swap(temporaryPath, destinationPath);
                return null;
            }
            catch (Exception e)
            {
                if (staged)
                {
                    DiscardStagedFile(temporaryPath);
                }

                return e;
            }
            finally
            {
                gate.Release();
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

            // Probed up front so a missing source never reaches the staging step, where its failure
            // would be indistinguishable from a staged file another writer owns.
            if (!File.Exists(sourcePath))
            {
                return new FileNotFoundException(
                    $"Source file not found: '{sourcePath}'.",
                    sourcePath
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

        private static SemaphoreSlim GateFor(string path)
        {
            string key = path;
            try
            {
                key = Path.GetFullPath(path);
            }
            catch (Exception)
            {
                // A path that cannot be normalized still gets a gate, just not one shared with its
                // aliases.
            }

            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(key);
            return Gates[(hash & int.MaxValue) % GateCount];
        }

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
            // FileShare.Read admits readers but denies a second writer, so a cross-process append
            // fails loudly instead of overwriting records this process already committed.
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

        // File.Copy does not flush, so the staged copy is reopened purely to force it out of the
        // page cache before the swap makes it the live file.
        private static void FlushToDisk(string path)
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Write, FileShare.None);
            stream.Flush(flushToDisk: true);
        }

        // A leftover staged file reads as a half-finished write to the next attempt, and would keep
        // failing identically if the cause was a full disk.
        private static void DiscardStagedFile(string temporaryPath)
        {
            TryDelete(temporaryPath);
        }

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
                // Another process created the destination between the probe and the move.
                Replace(temporaryPath, path);
            }
        }

        private static void Replace(string temporaryPath, string path)
        {
            try
            {
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            }
            catch (NotSupportedException)
            {
                // File.Replace is the atomic swap but is not implemented on every platform. Where
                // it is missing the swap degrades to delete-then-move: the staged data is still
                // complete and flushed, but the destination is briefly absent.
                File.Delete(path);
                File.Move(temporaryPath, path);
            }
        }
    }
}
