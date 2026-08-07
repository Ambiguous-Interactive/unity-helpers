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

            using (EnterGate(path))
            {
                string temporaryPath = path + TemporarySuffix;
                FileStream staging;
                byte[] bytes;
                try
                {
                    // Encoded before the handle is opened, so nothing it can throw can strand an
                    // open FileStream that the catch below is not in a position to dispose.
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
                    error = e;
                    DiscardStagedFile(temporaryPath);
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

            GateScope gate;
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
                    // Synchronous `using` (not `await using`) keeps this off System.IAsyncDisposable,
                    // which is unavailable under the .NET Standard 2.0 profile of older Unity LTS.
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

            GateScope gate;
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
            error = ValidateCopyPaths(sourcePath, destinationPath);
            if (error != null)
            {
                return false;
            }

            using (EnterGate(destinationPath))
            {
                string temporaryPath = destinationPath + TemporarySuffix;
                FileStream source;
                FileStream staging;
                try
                {
                    EnsureDirectory(destinationPath);
                    source = OpenSourceStream(sourcePath, useAsync: false);
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
                    error = e;
                    DiscardStagedFile(temporaryPath);
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

            GateScope gate;
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
                    EnsureDirectory(destinationPath);
                    source = OpenSourceStream(sourcePath, useAsync: true);
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
                        // The flush stays synchronous: it is a metadata-scale operation and
                        // splitting it across an await buys nothing.
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
                // File.Delete is already a no-op on a missing file, so the probe is a fast path
                // rather than a correctness guard, and losing the race with another deleter is
                // harmless: the postcondition this reports is "no file remains", not "we removed it".
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

        private static GateScope EnterGate(string path)
        {
            SemaphoreSlim gate = GateFor(path);
            gate.Wait();
            return new GateScope(gate);
        }

        private static async ValueTask<GateScope> EnterGateAsync(
            string path,
            CancellationToken cancellationToken
        )
        {
            SemaphoreSlim gate = GateFor(path);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new GateScope(gate);
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

        // FileMode.Create + FileShare.None is what makes ownership decidable: if this open returns,
        // the staged file is exclusively this call's and every later failure must discard it; if it
        // throws, nothing here created anything and a staged file that exists belongs to somebody
        // else. Copy stages through this same open rather than File.Copy, because File.Copy folds
        // "could not take the staging path" and "failed halfway through writing it" into one
        // exception and the cleanup decision differs between them.
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

        // A leftover staged file reads as a half-finished write to the next attempt, and would keep
        // failing identically if the cause was a full disk.
        private static void DiscardStagedFile(string temporaryPath)
        {
            TryDelete(temporaryPath);
        }

        // Whether the destination exists decides which API can swap, and another process can change
        // that answer between the probe and the call — so neither branch trusts it. Each falls
        // through to the other on the exception that means "the file was there / was not there
        // after all".
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
                // The destination was removed after the probe. Moving into the gap is the same
                // outcome File.Replace would have produced.
                File.Move(temporaryPath, path);
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

        /// <summary>
        /// Holds a path's gate until disposed. A struct so <c>using</c> costs no allocation.
        /// </summary>
        private readonly struct GateScope : IDisposable
        {
            private readonly SemaphoreSlim _gate;

            internal GateScope(SemaphoreSlim gate)
            {
                _gate = gate;
            }

            public void Dispose()
            {
                _gate.Release();
            }
        }
    }
}
