// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Helper
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
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
    /// It <b>does</b> stage and flush new contents before replacement. Where <c>File.Replace</c>
    /// is supported, a reader observes either the complete previous contents or the complete new
    /// ones. It <b>does</b> serialize concurrent operations on the same path within this process.
    /// It is <b>not</b> full crash safety — .NET cannot flush a <i>directory</i>, so a filesystem
    /// may still reorder the rename behind the data write. On platforms without
    /// <c>File.Replace</c>, the delete-then-move fallback briefly exposes an absent destination
    /// and can lose it if the move fails. A second process using this type cannot take ownership of
    /// the same staging path while a write is active; its operation reports failure instead. This is
    /// ownership isolation, not a cross-process transaction or a multi-file lock. Do not describe
    /// consumers of this type as crash-safe.
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

        internal const string PreservedStagingPathDataKey = nameof(PreservedStagingPathDataKey);

        private const int UnixNameAlreadyExists = 17;
        private const int DefaultBufferSize = 4096;

        // FileMode.Append does not provide atomic append; bounded per-path gates prevent writer overlap.
        private const int GateCount = 32;
        private const string OwnershipSuffix = ".lock";

        private static readonly SemaphoreSlim[] Gates = CreateGates();

        private static readonly UTF8Encoding Utf8NoByteOrderMark = new(
            encoderShouldEmitUTF8Identifier: false
        );

#if UNITY_EDITOR
        internal static Action<string> BeforeStagedSwapForTests { get; set; }
        internal static Action<string> BeforeStagedCleanupForTests { get; set; }
        internal static Action<string> BeforeCompareReadForTests { get; set; }
#endif

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

            string temporaryPath = path + TemporarySuffix;
            using (EnterGate(path))
            {
                try
                {
                    byte[] bytes = Utf8NoByteOrderMark.GetBytes(contents ?? string.Empty);
                    return TryWriteStagedBytes(path, temporaryPath, bytes, out error);
                }
                catch (Exception e)
                {
                    error = e;
                    return false;
                }
            }
        }

        /// <summary>
        /// Replaces a file's text using the requested encoding, including its byte order mark.
        /// </summary>
        /// <remarks>
        /// Carries the same durability limits as <see cref="TryWriteAllText(string, string, out Exception)"/>.
        /// Use the overload without an encoding for UTF-8 without a byte order mark.
        /// </remarks>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Text to write. Null is treated as empty.</param>
        /// <param name="encoding">Encoding whose preamble and text bytes are written.</param>
        /// <param name="error">The failure when this returns false; null otherwise.</param>
        /// <returns>True when the destination holds the encoded contents.</returns>
        public static bool TryWriteAllText(
            string path,
            string contents,
            Encoding encoding,
            out Exception error
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = new ArgumentException("A destination path is required.", nameof(path));
                return false;
            }

            if (encoding == null)
            {
                error = new ArgumentNullException(nameof(encoding));
                return false;
            }

            try
            {
                string text = contents ?? string.Empty;
                byte[] preamble = encoding.GetPreamble();
                int textByteCount = encoding.GetByteCount(text);
                byte[] bytes = new byte[checked(preamble.Length + textByteCount)];
                Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
                int written = encoding.GetBytes(text, 0, text.Length, bytes, preamble.Length);
                if (written != textByteCount)
                {
                    error = new InvalidOperationException("The encoding changed its byte count.");
                    return false;
                }

                return TryWriteAllBytes(path, bytes, out error);
            }
            catch (Exception e)
            {
                error = e;
                return false;
            }
        }

        /// <summary>
        /// Replaces a file's bytes, staging and flushing before the swap.
        /// </summary>
        /// <remarks>
        /// Carries the same durability guarantees as <see cref="TryWriteAllText(string, string, out Exception)"/>.
        /// The array is written directly; do not modify it until this call returns.
        /// </remarks>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Bytes to write. Null is treated as empty.</param>
        /// <param name="error">The failure when this returns false; null otherwise.</param>
        /// <returns>True when the destination holds the new contents.</returns>
        /// <example>
        /// <code>
        /// bool saved = DurableFile.TryWriteAllBytes(savePath, serializedBytes, out Exception error);
        /// </code>
        /// </example>
        public static bool TryWriteAllBytes(string path, byte[] contents, out Exception error)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = new ArgumentException("A destination path is required.", nameof(path));
                return false;
            }

            string temporaryPath = path + TemporarySuffix;
            using (EnterGate(path))
            {
                return TryWriteStagedBytes(
                    path,
                    temporaryPath,
                    contents ?? Array.Empty<byte>(),
                    out error
                );
            }
        }

        /// <summary>
        /// Asynchronously replaces a file's entire contents, staging and flushing before the swap.
        /// </summary>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Text to write. Null is treated as empty.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Null on success, otherwise the failure.</returns>
        public static ValueTask<Exception> WriteAllTextAsync(
            string path,
            string contents,
            CancellationToken cancellationToken = default
        )
        {
            return WriteStagedAsync(path, contents, null, cancellationToken);
        }

        /// <summary>
        /// Asynchronously replaces a file's bytes, staging and flushing before the swap.
        /// </summary>
        /// <remarks>
        /// Carries the same durability guarantees as <see cref="TryWriteAllBytes"/>.
        /// Keep the array unchanged until this call completes.
        /// </remarks>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Bytes to write. Null is treated as empty.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>Null on success, otherwise the failure.</returns>
        /// <example>
        /// <code>
        /// Exception error = await DurableFile.WriteAllBytesAsync(savePath, serializedBytes, cancellationToken);
        /// </code>
        /// </example>
        public static ValueTask<Exception> WriteAllBytesAsync(
            string path,
            byte[] contents,
            CancellationToken cancellationToken = default
        )
        {
            return WriteStagedAsync(path, null, contents, cancellationToken);
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

            string temporaryPath = destinationPath + TemporarySuffix;
            using (EnterGate(destinationPath))
            {
                FileStream source;
                FileStream ownership = null;
                FileStream staging;
                try
                {
                    // Open the source before creating destination directories so failed reads leave no new folders.
                    source = OpenCopySourceStream(sourcePath, destinationPath, useAsync: false);
                }
                catch (Exception e)
                {
                    error = e;
                    return false;
                }

                try
                {
                    ownership = OpenStagingOwnership(temporaryPath);
                    staging = OpenStagingStream(temporaryPath, useAsync: false);
                }
                catch (Exception e)
                {
                    ReleaseFileStream(source);
                    ReleaseStagingOwnership(ownership);
                    error = e;
                    return false;
                }

                try
                {
                    using (source)
                    {
                        using (staging)
                        {
                            source.CopyTo(staging, DefaultBufferSize);
                            staging.Flush(flushToDisk: true);
                        }

#if UNITY_EDITOR
                        BeforeStagedSwapForTests?.Invoke(temporaryPath);
#endif
                        Swap(temporaryPath, destinationPath);
                    }

                    error = null;
                    return true;
                }
                catch (Exception e)
                {
#if UNITY_EDITOR
                    BeforeStagedCleanupForTests?.Invoke(temporaryPath);
#endif
                    DiscardStagedFile(temporaryPath);
                    error = e;
                    return false;
                }
                finally
                {
                    ReleaseStagingOwnership(ownership);
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
        public static ValueTask<Exception> CopyAsync(
            string sourcePath,
            string destinationPath,
            CancellationToken cancellationToken = default
        )
        {
            return CopyAsync(sourcePath, destinationPath, DefaultBufferSize, cancellationToken);
        }

        /// <summary>
        /// Deletes a file, reporting failure rather than throwing. An absent file is successful;
        /// another process can create the path after deletion completes.
        /// </summary>
        /// <param name="path">File to delete.</param>
        /// <returns>True if <see cref="File.Delete(string)"/> completed without throwing.</returns>
        public static bool TryDelete(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Creates a file from flushed bytes only while its destination is absent.</summary>
        /// <remarks>
        /// Unix publication requires a filesystem that supports hard links. On Unix the staged
        /// name is removed after publication and verification; if that cleanup fails, the
        /// destination remains in place and this method reports a failure for inspection.
        /// A noncooperating writer can replace the staged name before cleanup, so that cleanup
        /// does not provide exclusive ownership against external writers.
        /// </remarks>
        /// <param name="path">Destination file path. Missing directories are created.</param>
        /// <param name="contents">Bytes to write.</param>
        /// <param name="error">
        /// The failure when this returns false; null otherwise. If staged bytes could not be
        /// published, <see cref="PreservedStagingPathDataKey"/> identifies their path in Data.
        /// </param>
        /// <param name="beforeMove">Optional action invoked just before publication.</param>
        /// <returns>True when the new file was published without replacing another file.</returns>
        internal static bool TryCreateAllBytes(
            string path,
            byte[] contents,
            out Exception error,
            Action<string> beforeMove = null
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = new ArgumentException("A destination path is required.", nameof(path));
                return false;
            }

            if (contents == null)
            {
                error = new ArgumentNullException(nameof(contents));
                return false;
            }

            using (EnterGate(path))
            {
                string temporaryPath =
                    path + ".create." + Guid.NewGuid().ToString("N") + TemporarySuffix;
                bool ownsTemporary = false;
                bool moved = false;
                try
                {
                    EnsureDirectory(path);
                    using (
                        FileStream staging = new(
                            temporaryPath,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            DefaultBufferSize,
                            useAsync: false
                        )
                    )
                    {
                        ownsTemporary = true;
                        staging.Write(contents, 0, contents.Length);
                        staging.Flush(flushToDisk: true);
                    }

                    beforeMove?.Invoke(path);
                    if (
                        !TryPublishStagedFileWithoutOverwrite(
                            temporaryPath,
                            path,
                            out bool leavesStaged
                        )
                    )
                    {
                        throw new IOException($"A file already exists at {path}.");
                    }
                    ownsTemporary = leavesStaged;
                    moved = true;
                    if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(contents))
                    {
                        throw new IOException(
                            $"Created file at {path}, but its contents differ; inspect it before retrying."
                        );
                    }
                    if (leavesStaged)
                    {
                        File.Delete(temporaryPath);
                        ownsTemporary = false;
                    }
                    error = null;
                    return true;
                }
                catch (Exception exception)
                {
                    Exception failure = moved
                        ? new IOException(
                            $"Created file at {path}, but could not verify its contents; inspect it before retrying.",
                            exception
                        )
                        : exception;
                    if (ownsTemporary)
                    {
                        failure.Data[PreservedStagingPathDataKey] = temporaryPath;
                    }
                    error = failure;
                    return false;
                }
            }
        }

        /// <summary>
        /// Publishes a staged file only if the destination has no directory entry.
        /// </summary>
        /// <remarks>
        /// A successful Unix hard link leaves the staged name behind for the caller to remove.
        /// Windows moves the staged name. On collision the staged name remains on every platform.
        /// </remarks>
        /// <returns>True if published, false if the destination was already occupied.</returns>
        internal static bool TryPublishStagedFileWithoutOverwrite(
            string stagedPath,
            string destinationPath,
            out bool leavesStaged
        )
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    File.Move(stagedPath, destinationPath);
                    leavesStaged = false;
                    return true;
                }
                catch (IOException) when (File.Exists(destinationPath))
                {
                    leavesStaged = true;
                    return false;
                }
            }

            int result;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                result = CreateHardLinkMac(stagedPath, destinationPath);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                result = CreateHardLinkLinux(stagedPath, destinationPath);
            }
            else
            {
                throw new PlatformNotSupportedException(
                    "Exclusive file publication needs hard links."
                );
            }

            if (result == 0)
            {
                leavesStaged = true;
                return true;
            }

            int nativeError = Marshal.GetLastWin32Error();
            if (nativeError == UnixNameAlreadyExists)
            {
                leavesStaged = true;
                return false;
            }

            throw new IOException(
                $"Could not publish '{destinationPath}' with a hard link (OS error {nativeError}). Check that the output filesystem supports hard links and allows writing."
            );
        }

        /// <summary>Compares current bytes, then stages and replaces the file.</summary>
        /// <remarks>
        /// The staging-file ownership spans the read, staging write, and replacement, so cooperating
        /// staged-replacement DurableFile writers cannot change the destination during this operation.
        /// A writer that does not acquire that ownership can change it after the read and before replacement.
        /// </remarks>
        internal static bool TryCompareThenReplaceBytes(
            string path,
            byte[] expected,
            byte[] replacement,
            out Exception error
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = new ArgumentException("A destination path is required.", nameof(path));
                return false;
            }

            if (expected == null || replacement == null)
            {
                error = new ArgumentNullException(
                    expected == null ? nameof(expected) : nameof(replacement)
                );
                return false;
            }

            using (EnterGate(path))
            {
                string temporaryPath = path + TemporarySuffix;
                FileStream ownership = null;
                FileStream staging = null;
                bool ownsStaging = false;
                try
                {
                    ownership = OpenStagingOwnership(temporaryPath);
#if UNITY_EDITOR
                    BeforeCompareReadForTests?.Invoke(temporaryPath);
#endif
                    byte[] current = File.ReadAllBytes(path);
                    if (!BytesEqual(current, expected))
                    {
                        error = new InvalidOperationException(
                            "The file changed since it was read."
                        );
                        return false;
                    }

                    staging = OpenStagingStream(temporaryPath, useAsync: false);
                    ownsStaging = true;
                    staging.Write(replacement, 0, replacement.Length);
                    staging.Flush(flushToDisk: true);
                    staging.Dispose();
                    staging = null;
#if UNITY_EDITOR
                    BeforeStagedSwapForTests?.Invoke(temporaryPath);
#endif
                    Swap(temporaryPath, path);
                    ownsStaging = false;
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
                    ReleaseFileStream(staging);
                    if (ownsStaging)
                    {
                        DiscardStagedFile(temporaryPath);
                    }
                    ReleaseStagingOwnership(ownership);
                }
            }
        }

        /// <summary>Compares current text, then stages and replaces the file.</summary>
        /// <remarks>
        /// The staging-file ownership spans the read, staging write, and replacement, so cooperating
        /// staged-replacement DurableFile writers cannot change the destination during this operation.
        /// A writer that does not acquire that ownership can change it after the read and before replacement.
        /// </remarks>
        internal static bool TryCompareThenReplaceAllText(
            string path,
            bool expectedExists,
            string expectedContents,
            string replacementContents,
            out bool exchanged,
            out Exception error
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = new ArgumentException("A destination path is required.", nameof(path));
                exchanged = false;
                return false;
            }

            string temporaryPath = path + TemporarySuffix;
            using (EnterGate(path))
            {
                FileStream ownership = null;
                FileStream staging = null;
                bool ownsStaging = false;
                try
                {
                    EnsureDirectory(path);
                    ownership = OpenStagingOwnership(temporaryPath);
                    bool exists;
                    string current;
                    try
                    {
                        current = File.ReadAllText(path);
                        exists = true;
                    }
                    catch (FileNotFoundException)
                    {
                        current = string.Empty;
                        exists = false;
                    }

                    if (
                        exists != expectedExists
                        || !string.Equals(current, expectedContents, StringComparison.Ordinal)
                    )
                    {
                        error = null;
                        exchanged = false;
                        return true;
                    }

                    staging = OpenStagingStream(temporaryPath, useAsync: false);
                    ownsStaging = true;
                    byte[] bytes = Utf8NoByteOrderMark.GetBytes(
                        replacementContents ?? string.Empty
                    );
                    staging.Write(bytes, 0, bytes.Length);
                    staging.Flush(flushToDisk: true);
                    staging.Dispose();
                    staging = null;
#if UNITY_EDITOR
                    BeforeStagedSwapForTests?.Invoke(temporaryPath);
#endif
                    Swap(temporaryPath, path);
                    ownsStaging = false;
                    error = null;
                    exchanged = true;
                    return true;
                }
                catch (Exception failure)
                {
                    error = failure;
                    exchanged = false;
                    return false;
                }
                finally
                {
                    ReleaseFileStream(staging);
                    if (ownsStaging)
                        DiscardStagedFile(temporaryPath);
                    ReleaseStagingOwnership(ownership);
                }
            }
        }

        internal static async ValueTask<Exception> CopyAsync(
            string sourcePath,
            string destinationPath,
            int bufferSize,
            CancellationToken cancellationToken
        )
        {
            if (bufferSize <= 0)
            {
                return new ArgumentOutOfRangeException(nameof(bufferSize));
            }

            Exception invalid = ValidateCopyPaths(sourcePath, destinationPath);
            if (invalid != null)
            {
                return invalid;
            }

            string temporaryPath = destinationPath + TemporarySuffix;
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
                FileStream source;
                FileStream ownership = null;
                FileStream staging;
                try
                {
                    // Source first, for the reason recorded in TryCopy.
                    source = OpenCopySourceStream(sourcePath, destinationPath, useAsync: true);
                }
                catch (Exception e)
                {
                    return e;
                }

                try
                {
                    ownership = OpenStagingOwnership(temporaryPath);
                    staging = OpenStagingStream(temporaryPath, useAsync: true);
                }
                catch (Exception e)
                {
                    ReleaseFileStream(source);
                    ReleaseStagingOwnership(ownership);
                    return e;
                }

                try
                {
                    using (source)
                    {
                        using (staging)
                        {
                            await source
                                .CopyToAsync(staging, bufferSize, cancellationToken)
                                .ConfigureAwait(false);

                            staging.Flush(flushToDisk: true);
                        }

#if UNITY_EDITOR
                        BeforeStagedSwapForTests?.Invoke(temporaryPath);
#endif
                        Swap(temporaryPath, destinationPath);
                    }

                    return null;
                }
                catch (Exception e)
                {
#if UNITY_EDITOR
                    BeforeStagedCleanupForTests?.Invoke(temporaryPath);
#endif
                    DiscardStagedFile(temporaryPath);
                    return e;
                }
                finally
                {
                    ReleaseStagingOwnership(ownership);
                }
            }
        }

        [DllImport("libc", EntryPoint = "link", ExactSpelling = true, SetLastError = true)]
        private static extern int CreateHardLinkLinux(string stagedPath, string destinationPath);

        [DllImport(
            "libSystem.B.dylib",
            EntryPoint = "link",
            ExactSpelling = true,
            SetLastError = true
        )]
        private static extern int CreateHardLinkMac(string stagedPath, string destinationPath);

        private static async ValueTask<Exception> WriteStagedAsync(
            string path,
            string textContents,
            byte[] byteContents,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new ArgumentException("A destination path is required.", nameof(path));
            }

            string temporaryPath = path + TemporarySuffix;
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
                FileStream ownership = null;
                FileStream staging;
                byte[] bytes;
                try
                {
                    bytes =
                        byteContents
                        ?? (
                            textContents == null
                                ? Array.Empty<byte>()
                                : Utf8NoByteOrderMark.GetBytes(textContents)
                        );
                    EnsureDirectory(path);
                    ownership = OpenStagingOwnership(temporaryPath);
                    staging = OpenStagingStream(temporaryPath, useAsync: true);
                }
                catch (Exception e)
                {
                    ReleaseStagingOwnership(ownership);
                    return e;
                }

                try
                {
                    using (staging)
                    {
                        await staging
                            .WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                            .ConfigureAwait(false);
                        staging.Flush(flushToDisk: true);
                    }

#if UNITY_EDITOR
                    BeforeStagedSwapForTests?.Invoke(temporaryPath);
#endif
                    Swap(temporaryPath, path);

                    return null;
                }
                catch (Exception e)
                {
#if UNITY_EDITOR
                    BeforeStagedCleanupForTests?.Invoke(temporaryPath);
#endif
                    DiscardStagedFile(temporaryPath);
                    return e;
                }
                finally
                {
                    ReleaseStagingOwnership(ownership);
                }
            }
        }

        private static bool TryWriteStagedBytes(
            string path,
            string temporaryPath,
            byte[] contents,
            out Exception error
        )
        {
            FileStream ownership = null;
            FileStream staging;
            try
            {
                EnsureDirectory(path);
                ownership = OpenStagingOwnership(temporaryPath);
                staging = OpenStagingStream(temporaryPath, useAsync: false);
            }
            catch (Exception e)
            {
                ReleaseStagingOwnership(ownership);
                error = e;
                return false;
            }

            try
            {
                using (staging)
                {
                    staging.Write(contents, 0, contents.Length);
                    staging.Flush(flushToDisk: true);
                }

#if UNITY_EDITOR
                BeforeStagedSwapForTests?.Invoke(temporaryPath);
#endif
                Swap(temporaryPath, path);

                error = null;
                return true;
            }
            catch (Exception e)
            {
#if UNITY_EDITOR
                BeforeStagedCleanupForTests?.Invoke(temporaryPath);
#endif
                DiscardStagedFile(temporaryPath);
                error = e;
                return false;
            }
            finally
            {
                ReleaseStagingOwnership(ownership);
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

        private static bool BytesEqual(byte[] first, byte[] second)
        {
            return first.AsSpan().SequenceEqual(second);
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

        private static FileStream OpenCopySourceStream(
            string sourcePath,
            string destinationPath,
            bool useAsync
        )
        {
            FileStream source = OpenSourceStream(sourcePath, useAsync);
            try
            {
                EnsureDirectory(destinationPath);
                return source;
            }
            catch
            {
                source.Dispose();
                throw;
            }
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

        private static FileStream OpenStagingOwnership(string temporaryPath)
        {
            return new FileStream(
                temporaryPath + OwnershipSuffix,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                bufferSize: 1,
                FileOptions.DeleteOnClose
            );
        }

        private static void ReleaseStagingOwnership(FileStream ownership)
        {
            ReleaseFileStream(ownership);
        }

        private static void ReleaseFileStream(FileStream stream)
        {
            try
            {
                stream?.Dispose();
            }
            catch (Exception)
            {
                // A close failure must not escape a Try API or mask its original file error.
            }
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
